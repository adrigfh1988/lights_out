using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F82: the shared speech engine behind StalkerVoice (Stan, the Weeping Angel) and HunterVoice (the hunter). A
/// subclass says WHEN to talk (its own sight sampling and event hooks, all ending in TrySay) and supplies
/// the lines, priorities, cooldowns and look; this base owns everything else: the pacing rules (global
/// gap after a bubble finishes, per-category cooldowns, priority interrupts - a refused line is dropped,
/// never queued), the per-category shuffle bags (no line repeats until its bag is empty, and never back to
/// back), the SpeechBubble itself (built lazily on the first line so it sits above the HUD in the canvas
/// order, destroyed in OnDestroy), and the per-letter blip / head-nod / eye-flare pulses.
///
/// Categories are plain ints here (each subclass casts its own enum). No statics - everything is
/// per-instance and dies with the scene.
/// </summary>
public abstract class CreatureVoice : MonoBehaviour
{
    [Header("Pacing")]
    [Tooltip("Gap between a bubble finishing and the next (unless a more urgent line interrupts).")]
    [SerializeField] protected float minGapSeconds = 5f;

    [Header("Voice")]
    [Tooltip("Per-letter voice blips. Off since 7 Oct 2026 - the bubbles speak silently; the nod and eye flare still play.")]
    [SerializeField] protected bool playBlips = false;
    [SerializeField] protected float blipVolume = 0.35f;
    [SerializeField] protected float nodDegrees = 4f;
    [SerializeField] protected float nodSeconds = 0.1f;
    [SerializeField] protected float flareSeconds = 0.12f;

    private AudioSource _blipSource;
    private AudioClip[] _blipClips;
    private SpeechBubble _bubble;
    private bool _bubbleFailed;
    private bool _wasBusy;
    private bool _voiceReady;

    private float _gapUntil;
    private int _currentPriority;
    private int _lastPriority;
    private bool _currentShout;
    private bool _currentOutlivesEnding;

    private float[] _nextAllowed;
    private List<int>[] _bags;
    private int[] _lastDrawn;

    private float _nod;
    private float _flare;

    /// <summary>Time.time of the last line actually shown.</summary>
    protected float LastSpokeAt { get; private set; } = -999f;

    /// <summary>Head dip in degrees for the owner to add to its head bone (world space, additive on the animated pose).</summary>
    public float NodDegrees => _nod * nodDegrees;

    /// <summary>0..1 - how hard the eyes should flare right now (a letter just blipped).</summary>
    public float EyeFlare => _flare;

    // ---------------------------------------------------------------- subclass contract

    protected abstract int CategoryCount { get; }
    protected abstract string[] LinesFor(int category);
    protected abstract int PriorityOf(int category);
    protected abstract float CooldownOf(int category);
    protected abstract bool IsShout(int category);
    /// <summary>Glyph glitches while typing; off for lines that must stay readable.</summary>
    protected virtual bool GlitchFor(int category) => true;
    /// <summary>A line that may start (and keep playing) during an ending sequence, until GameOutcome.IsOver.</summary>
    protected virtual bool OutlivesEnding(int category) => false;
    /// <summary>Extra per-subclass veto (e.g. Stan is vanished, the hunter is lying in ambush).</summary>
    protected virtual bool CanSpeak(int category) => true;
    protected abstract Vector3 BubbleAnchor();
    /// <summary>Distance at which the bubble is drawn smallest.</summary>
    protected abstract float BubbleRange { get; }
    protected virtual SpeechBubble.Palette BubblePalette => SpeechBubble.Palette.Paper;
    protected virtual float[] BlipFrequencies => new[] { 700f, 950f, 1200f };
    protected virtual Vector2 BlipPitchRange => new Vector2(0.8f, 1.25f);
    /// <summary>Called every unpaused frame while the run is live - sight sampling and triggers go here.</summary>
    protected abstract void Tick(float deltaTime);

    // ---------------------------------------------------------------- setup

    /// <summary>Call once from the subclass's Configure. `spatialTemplate` (may be null) lends its
    /// spatialBlend/maxDistance to the voice's own source - blips change the pitch per shot, which would
    /// bend any other one-shots sharing a source.</summary>
    protected void InitVoice(AudioSource spatialTemplate)
    {
        _blipSource = gameObject.AddComponent<AudioSource>();
        _blipSource.playOnAwake = false;
        _blipSource.spatialBlend = spatialTemplate != null ? spatialTemplate.spatialBlend : 1f;
        _blipSource.maxDistance = spatialTemplate != null ? spatialTemplate.maxDistance : 22f;

        // Short blips, never looped (a short click looped back to back is a buzz - the F75 bug).
        float[] frequencies = BlipFrequencies;
        _blipClips = new AudioClip[frequencies.Length];
        for (int i = 0; i < frequencies.Length; i++) _blipClips[i] = DoorAudio.BuildClunkClip(0.045f, frequencies[i], 0.15f);

        int count = CategoryCount;
        _nextAllowed = new float[count];
        _bags = new List<int>[count];
        _lastDrawn = new int[count];
        for (int i = 0; i < count; i++) _lastDrawn[i] = -1;
        _voiceReady = true;
    }

    protected virtual void OnDestroy()
    {
        if (_bubble != null) Destroy(_bubble.gameObject);
    }

    // ---------------------------------------------------------------- update

    protected static bool RunIsLive(GameOutcome outcome) =>
        GameFlow.IsRunActive && !GameOutcome.IsOver && !(outcome != null && outcome.IsEnding);

    /// <summary>The GameOutcome the live gate reads; subclasses return the one they were configured with.</summary>
    protected abstract GameOutcome Outcome { get; }

    private void Update()
    {
        if (!_voiceReady) return;

        bool busy = _bubble != null && _bubble.IsBusy;
        if (!RunIsLive(Outcome))
        {
            // An ending clears the bubble at once - except a line allowed to outlive it (the capture line),
            // which plays on under the blackout until the end screen.
            if (busy && (!_currentOutlivesEnding || GameOutcome.IsOver)) Silence();
            return;
        }

        float dt = Time.deltaTime;
        _nod = Mathf.MoveTowards(_nod, 0f, dt / Mathf.Max(0.01f, nodSeconds));
        _flare = Mathf.MoveTowards(_flare, 0f, dt / Mathf.Max(0.01f, flareSeconds));

        if (_wasBusy && !busy) _gapUntil = Time.time + minGapSeconds;
        _wasBusy = busy;

        if (dt <= 0f) return; // paused: nothing advances
        Tick(dt);
    }

    // ---------------------------------------------------------------- speaking

    /// <summary>Applies the live gates, the category cooldown, the global gap and the priority rules; a
    /// refused line is dropped, not queued. True if a line was shown.</summary>
    protected bool TrySay(int category)
    {
        if (!_voiceReady || category < 0 || category >= CategoryCount) return false;
        if (Time.timeScale <= 0f) return false;
        bool live = RunIsLive(Outcome);
        if (!live && !(OutlivesEnding(category) && !GameOutcome.IsOver)) return false;
        if (!CanSpeak(category)) return false;

        string[] lines = LinesFor(category);
        if (lines == null || lines.Length == 0) return false;

        float now = Time.time;
        if (now < _nextAllowed[category]) return false;

        int priority = PriorityOf(category);
        bool busy = _bubble != null && _bubble.IsBusy;
        if (busy)
        {
            if (priority <= _currentPriority) return false; // never interrupts an equal/higher line
        }
        else if (now < _gapUntil && priority <= _lastPriority)
        {
            return false; // inside the quiet gap, only something more urgent than the last line gets through
        }

        if (!EnsureBubble()) return false;

        string line = Draw(category, lines);
        if (string.IsNullOrEmpty(line)) return false;
        line = line.Replace("{floor}", GameFlow.CurrentFloor.ToString());

        bool shout = IsShout(category);
        _currentShout = shout;
        _currentOutlivesEnding = OutlivesEnding(category);
        _bubble.Show(line, shout ? SpeechBubble.Style.Shout : SpeechBubble.Style.Normal, BubbleAnchor, GlitchFor(category));

        _nextAllowed[category] = now + CooldownOf(category);
        _currentPriority = priority;
        _lastPriority = priority;
        LastSpokeAt = now;
        _wasBusy = true;
        return true;
    }

    /// <summary>Clears any open bubble at once.</summary>
    public void Silence()
    {
        if (_bubble != null) _bubble.Clear();
        _wasBusy = false;
        _currentOutlivesEnding = false;
        _nod = 0f;
        _flare = 0f;
    }

    private bool EnsureBubble()
    {
        if (_bubble != null) return true;
        if (_bubbleFailed) return false;

        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null)
        {
            _bubbleFailed = true;
            return false;
        }

        _bubble = SpeechBubble.Create(canvas, BubbleRange, BubblePalette);
        _bubble.name = $"{name}SpeechBubble";
        _bubble.OnLetter += OnLetter;

        // Below TouchControls (so its buttons stay on top); otherwise it is simply the newest sibling.
        Transform touch = canvas.rootCanvas.transform.Find("TouchControls");
        if (touch != null) _bubble.transform.SetSiblingIndex(touch.GetSiblingIndex());
        return true;
    }

    private void OnLetter(char c)
    {
        _nod = 1f;
        _flare = 1f;
        if (!playBlips || _blipSource == null || _blipClips == null || _blipClips.Length == 0) return;
        Vector2 range = BlipPitchRange;
        _blipSource.pitch = Random.Range(range.x, range.y) * (_currentShout ? 0.7f : 1f);
        _blipSource.PlayOneShot(_blipClips[Random.Range(0, _blipClips.Length)], blipVolume);
    }

    /// <summary>Shuffle bag per category: every line once before any repeats, and the first draw of a
    /// fresh bag never repeats the last line of the previous one.</summary>
    private string Draw(int category, string[] lines)
    {
        List<int> bag = _bags[category];
        if (bag == null)
        {
            bag = new List<int>();
            _bags[category] = bag;
        }

        // An edited (shorter) pool invalidates the bag.
        if (bag.Count > 0 && bag.Exists(i => i >= lines.Length)) bag.Clear();

        if (bag.Count == 0)
        {
            for (int i = 0; i < lines.Length; i++) bag.Add(i);
            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }
            // Draws pop from the end: make sure that is not the line said last.
            if (bag.Count > 1 && bag[bag.Count - 1] == _lastDrawn[category])
            {
                int swap = Random.Range(0, bag.Count - 1);
                (bag[bag.Count - 1], bag[swap]) = (bag[swap], bag[bag.Count - 1]);
            }
        }

        int pick = bag[bag.Count - 1];
        bag.RemoveAt(bag.Count - 1);
        _lastDrawn[category] = pick;
        return lines[pick];
    }
}

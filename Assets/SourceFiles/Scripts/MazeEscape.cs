using System.Collections;
using StarterAssets;
using TMPro;
using UnityEngine;

/// <summary>
/// The endgame. Collecting the last star does not win: a hatch opens somewhere in the maze and the
/// player has one minute to find it. Reaching it drops them out of the maze onto a platform below and
/// wins. Running out of time loses.
/// </summary>
public class MazeEscape : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Seconds to find the hatch once it opens")]
    [SerializeField] private float escapeSeconds = 60f;

    [Header("Hatch")]
    [SerializeField] private Color hatchColor = new Color(0.25f, 1f, 0.7f);
    [Tooltip("How close the player has to get to drop through")]
    [SerializeField] private float hatchTriggerRadius = 1.1f;
    [SerializeField] private float hatchLightRange = 12f;
    [SerializeField] private float hatchLightIntensity = 5f;
    [Tooltip("Cells this far or closer to the player when the hatch opens are skipped, so it is never a free win")]
    [SerializeField] private float minimumDistanceFromPlayer = 12f;

    [Header("Lift variant (F73)")]
    [Tooltip("Radius of the ring the player has to stand inside")]
    [SerializeField] private float liftRingRadius = 2f;
    [Tooltip("Total seconds standing inside the ring to complete it")]
    [SerializeField] private float liftFillSeconds = 8f;
    [Tooltip("Fraction of the fill rate the progress decays at per second while outside the ring")]
    [SerializeField] private float liftDecayFraction = 0.5f;
    [Tooltip("How far calling the lift (first time standing inside it) can be heard (m)")]
    [SerializeField] private float liftCallNoiseRadius = 60f;

    [Header("Drop")]
    [Tooltip("How far below the maze floor the landing platform sits")]
    [SerializeField] private float dropDepth = 4f;
    [Tooltip("How far below the maze floor the player starts the drop. Must clear the 0.2 m floor slab, or the capsule spawns inside it and gets shoved.")]
    [SerializeField] private float dropStartDepth = 2.5f;
    [SerializeField] private float platformSize = 8f;

    [Header("UI")]
    [SerializeField] private Color timerColor = Color.red;
    [SerializeField] private int timerFontSize = 64;

    [Header("Wayfinding")]
    [SerializeField] private float pingInterval = 2f;
    [SerializeField] private float pingVolume = 0.7f;
    [Tooltip("Must exceed the maze diagonal (~47 m for 11x11 at 3 m) so the ping is audible from anywhere")]
    [SerializeField] private float pingMaxDistance = 60f;
    [SerializeField] private float chevronRadius = 70f;

    private MazeGenerator _maze;
    private Transform _player;
    private GameOutcome _outcome;
    private Material _emissiveSource;
    private PlayerStealthState _stealth;
    private AIFollower _hunter;

    // F77 Key Hunt: the padlock. A KeyHunt floor's hatch/lift refuses to complete while _keys is
    // incomplete - see BindKeyRing/HandleKeysChanged/ApplyLockLook.
    private KeyRing _keys;
    private PlayerHud _hud;
    private bool _locked;
    private float _lockedNudgeTimer;
    private Light _exitLight;
    private Material _exitGlow;
    // F80: the trapdoor's two leaves (null on the primitive fallback and on a Lift). Shut while padlocked,
    // ajar once the hatch can be used, thrown open on the drop.
    private Transform _lidL;
    private Transform _lidR;
    private float _lidAngle;
    private Coroutine _lidRoutine;
    private const float LidAjarAngle = 16f;
    private const float LidOpenAngle = 100f;
    // F80: the walk-in. Stepping on the open trapdoor takes the controls, throws the leaves back, eases
    // the player over the opening looking down, then lowers them through as the blackout comes up.
    private bool _entering;
    private const float HatchApproachSeconds = 0.75f;
    private const float HatchDescendSeconds = 0.45f;
    private AudioClip _padlockClip;
    private Transform _pingAnchor;
    private static readonly Color LockedColor = new Color(0.9f, 0.15f, 0.1f);

    private bool _running;
    private bool _finished;
    private float _timeLeft;
    private Vector3 _hatchPosition;

    // F73: 50% seeded chance (floors 3-5) that the escape point is a Lift - same cell choice and
    // countdown as a Hatch, but completed by standing inside a ring for liftFillSeconds instead of
    // walking onto a trigger.
    private bool _isLift;
    private float _liftProgress;
    private bool _liftCalled;
    private TextMeshProUGUI _liftText;

    private TextMeshProUGUI _timerText;
    private TextMeshProUGUI _chevronText;
    private AudioSource _ping;
    private AudioClip _pingClip;
    private float _pingTimer;
    private float _chevronPulse;

    public Vector3 HatchPosition => _hatchPosition;
    public bool HatchOpen { get; private set; }

    /// <summary>Seconds left on the countdown. Valid even after Stop() - Stop() hides the display, it does not zero the field.</summary>
    public float TimeLeft => Mathf.Max(0f, _timeLeft);

    /// <summary>F36 Second Wind: holds the countdown without the permanent Stop(), so it can resume.</summary>
    public bool Frozen { get; set; }

    /// <summary>Called by MazeGenerator once the maze and the actors exist.</summary>
    /// <summary>Per-floor countdown. Read when the hatch opens, so it can be set any time before that.</summary>
    public void SetEscapeSeconds(float seconds) { escapeSeconds = Mathf.Max(10f, seconds); }

    public void Configure(MazeGenerator maze, Transform player, Material emissiveSource, GameOutcome outcome, AIFollower hunter)
    {
        _maze = maze;
        _player = player;
        _emissiveSource = emissiveSource;
        _outcome = outcome;
        _hunter = hunter;
        _stealth = player != null ? player.GetComponent<PlayerStealthState>() : null;
    }

    /// <summary>Freeze the countdown and hide it. Idempotent; called by GameOutcome when any ending begins.</summary>
    public void Stop()
    {
        _running = false;
        _finished = true;
        if (_timerText != null) _timerText.gameObject.SetActive(false);
        if (_ping != null) _ping.Stop();
    }

    /// <summary>F77: the player's KeyRing (an instance component, built alongside it) and the HUD, for the padlock's subtitles. Unsubscribes any previous ring first - CLAUDE.md's every-subscriber-unsubscribes rule.</summary>
    public void BindKeyRing(KeyRing ring, PlayerHud hud)
    {
        if (_keys != null) _keys.Changed -= HandleKeysChanged;
        _keys = ring;
        _hud = hud;
        if (_keys != null) _keys.Changed += HandleKeysChanged;
    }

    private void OnEnable()
    {
        GameManager.AllStarsCollected += BeginEscape;
    }

    private void OnDisable()
    {
        GameManager.AllStarsCollected -= BeginEscape;
        if (_keys != null) _keys.Changed -= HandleKeysChanged;
    }

    /// <summary>KeyRing.Changed: once every key is held, the padlock clears and the hatch/lift resumes.</summary>
    private void HandleKeysChanged()
    {
        if (!_locked || _keys == null || !_keys.IsComplete) return;
        _locked = false;
        ApplyLockLook();
        _hud?.ShowSubtitle(_isLift ? "THE LIFT IS UNLOCKED" : "THE HATCH IS UNLOCKED", 2.5f);
    }

    private void BeginEscape()
    {
        if (_running || _finished || _maze == null || _player == null || GameOutcome.IsOver) return;

        _hatchPosition = ChooseHatchCell();
        HatchOpen = true;

        // F73 (+17): floors 1-2 always a Hatch; floors 3-5 a 50% seeded Lift, tied to UsedSeed alone
        // (not the floor) so a same-seed retry always gets the same variant.
        _isLift = GameFlow.CurrentFloor >= 3 && new System.Random(_maze.UsedSeed + 17).NextDouble() < 0.5;

        if (_isLift) BuildLift(_hatchPosition);
        else BuildHatch(_hatchPosition);

        BuildLandingPlatform(_hatchPosition);
        BuildTimerText();

        // F77: locked for the whole escape if this is a KeyHunt floor and keys are still missing.
        _locked = _keys != null && !_keys.IsComplete;
        ApplyLockLook();

        _timeLeft = escapeSeconds;
        _running = true;

        Debug.Log($"MazeEscape: {(_isLift ? "lift" : "hatch")} opened at {_hatchPosition}. {escapeSeconds:0} seconds.", this);
    }

    private void Update()
    {
        if (!_running || _finished || Frozen || _entering) return;

        float dt = Time.deltaTime;
        _timeLeft -= dt;

        if (_timerText != null)
        {
            float shown = Mathf.Max(0f, _timeLeft);
            _timerText.text = FormatTime(shown);

            // Pulse once a second under ten, so the last stretch reads without having to look at it
            if (shown <= 10f)
            {
                float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(shown * Mathf.PI));
                _timerText.color = new Color(timerColor.r, timerColor.g, timerColor.b, pulse);
            }
        }

        UpdatePing(dt);
        UpdateChevron(dt);

        if (_timeLeft <= 0f)
        {
            _outcome.Lose(LoseReason.OutOfTime);
            Stop();
            return;
        }

        // Impossible in practice (the hatch spawns >= 12 m from the player and a locker cannot move
        // them there), but guarded anyway: a hatch/lift drop must never fire while inside a locker.
        if (_stealth != null && _stealth.Hidden) return;

        if (_isLift)
        {
            UpdateLift(dt);
            return;
        }

        Vector3 toHatch = _player.position - _hatchPosition;
        toHatch.y = 0f;
        if (toHatch.sqrMagnitude <= hatchTriggerRadius * hatchTriggerRadius)
        {
            if (_locked) { NudgeLocked(dt); return; }
            // The primitive fallback has no leaves to open, so it keeps the instant drop.
            if (_lidL != null || _lidR != null) StartCoroutine(EnterHatch());
            else CompleteEscape();
        }
    }

    /// <summary>F77: walking onto a locked hatch does not drop the player - a padlock clunk and a reminder subtitle, at most every 2 s.</summary>
    private void NudgeLocked(float dt)
    {
        _lockedNudgeTimer -= dt;
        if (_lockedNudgeTimer > 0f) return;
        _lockedNudgeTimer = 2f;

        _hud?.ShowSubtitle($"LOCKED · {_keys.Held} / {_keys.Total} KEYS", 1.8f);
        if (_padlockClip == null) _padlockClip = DoorAudio.BuildClunkClip(0.18f, 160f, 0.35f);
        AudioSource.PlayClipAtPoint(_padlockClip, _hatchPosition, 0.7f);
    }

    /// <summary>Standing inside the ring fills liftProgress at 1 s/s; standing outside it drains at liftDecayFraction s/s. Completes through the exact same path as the hatch trigger above.</summary>
    private void UpdateLift(float dt)
    {
        Vector3 flat = _player.position - _hatchPosition;
        flat.y = 0f;
        bool inside = flat.magnitude <= liftRingRadius;

        // F77: locked - no fill, no decay, no call noise. Just a reminder while standing in the ring.
        if (_locked)
        {
            if (_liftText != null)
            {
                _liftText.gameObject.SetActive(inside);
                if (inside) _liftText.text = $"LIFT LOCKED · {_keys.Held} / {_keys.Total} KEYS";
            }
            return;
        }

        if (inside)
        {
            if (!_liftCalled)
            {
                _liftCalled = true;
                _hunter?.HearNoise(_hatchPosition, liftCallNoiseRadius);
            }
            _liftProgress = Mathf.Min(liftFillSeconds, _liftProgress + dt);
        }
        else
        {
            _liftProgress = Mathf.Max(0f, _liftProgress - dt * liftDecayFraction);
        }

        if (_liftText != null)
        {
            bool show = inside || _liftProgress > 0f;
            _liftText.gameObject.SetActive(show);
            if (show)
            {
                int percent = liftFillSeconds > 0f ? Mathf.RoundToInt(_liftProgress / liftFillSeconds * 100f) : 0;
                _liftText.text = $"LIFT ARRIVING {percent}%";
            }
        }

        if (_liftProgress >= liftFillSeconds)
        {
            CompleteEscape();
        }
    }

    private void UpdatePing(float dt)
    {
        if (_player == null) return;

        _pingTimer -= dt;
        if (_pingTimer > 0f) return;

        Vector3 target = WayfindingTarget();
        float distance = Vector3.Distance(_player.position, target);
        _pingTimer = pingInterval * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(distance / 20f));
        _chevronPulse = 1f;

        if (_ping != null && _pingClip != null)
        {
            // F77: the ping comes from wherever the chevron points - the hatch while unlocked, the
            // nearest missing key while locked.
            if (_pingAnchor != null) _pingAnchor.position = target;
            _ping.PlayOneShot(_pingClip, pingVolume);
        }
    }

    private void UpdateChevron(float dt)
    {
        if (_chevronText == null) return;

        _chevronPulse = Mathf.Max(0f, _chevronPulse - 3f * dt);

        Camera cam = Camera.main;
        if (cam != null && _player != null)
        {
            Vector3 forward = cam.transform.forward;
            forward.y = 0f;

            Vector3 toHatchFlat = WayfindingTarget() - _player.position;
            toHatchFlat.y = 0f;

            if (forward.sqrMagnitude > 0.0001f && toHatchFlat.sqrMagnitude > 0.0001f)
            {
                float bearing = Vector3.SignedAngle(forward, toHatchFlat, Vector3.up);
                float rad = bearing * Mathf.Deg2Rad;

                RectTransform rect = _chevronText.rectTransform;
                rect.anchoredPosition = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * chevronRadius;
                rect.localRotation = Quaternion.Euler(0f, 0f, -bearing);
            }
        }

        Color c = _locked ? LockedColor : hatchColor;
        c.a = 0.35f + 0.65f * _chevronPulse;
        _chevronText.color = c;
    }

    /// <summary>F77: while locked, the ping and chevron point at the nearest missing key instead of the hatch/lift - fairness while the player cannot finish yet.</summary>
    private Vector3 WayfindingTarget()
    {
        if (_locked && _keys != null && _keys.TryNearestMissing(_player.position, out Vector3 key)) return key;
        return _hatchPosition;
    }

    /// <summary>F77: red while locked (same language as a fuse box's red indicator), back to hatchColor once every key is held.</summary>
    private void ApplyLockLook()
    {
        Color c = _locked ? LockedColor : hatchColor;
        if (_exitLight != null) _exitLight.color = c;
        if (_exitGlow != null)
        {
            _exitGlow.SetColor("_EmissionColor", c * 3f);
            _exitGlow.SetColor("_BaseColor", c);
        }
        SetLidAngle(_locked ? 0f : LidAjarAngle, 0.7f);
    }

    private void SetLidAngle(float degrees, float seconds)
    {
        if (_lidL == null && _lidR == null) return;
        if (_lidRoutine != null) StopCoroutine(_lidRoutine);
        _lidRoutine = StartCoroutine(SwingLid(degrees, seconds));
    }

    private IEnumerator SwingLid(float target, float seconds)
    {
        float start = _lidAngle;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            _lidAngle = Mathf.SmoothStep(start, target, Mathf.Clamp01(t / seconds));
            ApplyLidAngle();
            yield return null;
        }
        _lidAngle = target;
        ApplyLidAngle();
        _lidRoutine = null;
    }

    /// <summary>Each leaf lies flat at 0 and lifts its free (centre) edge about its hinge's local Z.</summary>
    private void ApplyLidAngle()
    {
        if (_lidL != null) _lidL.localRotation = Quaternion.Euler(0f, 0f, _lidAngle);
        if (_lidR != null) _lidR.localRotation = Quaternion.Euler(0f, 0f, -_lidAngle);
    }

    internal static string FormatTime(float seconds)
    {
        return $"{Mathf.FloorToInt(seconds / 60f)}:{Mathf.FloorToInt(seconds % 60f):00}";
    }

    // ---------------------------------------------------------------- placement

    private Vector3 ChooseHatchCell()
    {
        var cells = _maze.CellCenters;
        var candidates = new System.Collections.Generic.List<Vector3>();

        foreach (Vector3 cell in cells)
        {
            if (Vector3.Distance(cell, _player.position) >= minimumDistanceFromPlayer)
            {
                candidates.Add(cell);
            }
        }

        // A very small maze may have nowhere far enough; anywhere but underfoot will do then
        if (candidates.Count == 0)
        {
            return cells[Random.Range(0, cells.Count)];
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

    private void BuildHatch(Vector3 position)
    {
        GameObject root = new GameObject("EscapeHatch");
        root.transform.position = position;

        // A glowing panel set into the floor. Emissive geometry rather than light alone, so it is
        // visible from down a corridor rather than only lighting the wall next to it.
        // F80: the authored trapdoor (InteractableKit.escapeHatch) when there is one - its 'Glow' slab is
        // that same emissive panel, showing round and between the two wooden leaves.
        InteractableKit kit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        GameObject template = kit != null ? kit.EscapeHatch : null;
        GameObject panel = null;
        if (template != null)
        {
            GameObject model = Instantiate(template, root.transform);
            model.name = "Trapdoor";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.SetActive(true);

            Transform glow = model.transform.Find("Glow");
            if (glow != null && glow.GetComponent<MeshRenderer>() != null) panel = glow.gameObject;
            _lidL = model.transform.Find("LidL");
            _lidR = model.transform.Find("LidR");
        }

        if (panel == null)
        {
            panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "HatchPanel";
            Destroy(panel.GetComponent<Collider>());
            panel.transform.SetParent(root.transform, false);
            panel.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            panel.transform.localScale = new Vector3(1.8f, 0.06f, 1.8f);
        }

        if (_emissiveSource != null)
        {
            Material material = new Material(_emissiveSource) { name = "HatchGlow" };
            material.EnableKeyword("_EMISSION");
            material.SetColor("_BaseColor", hatchColor);
            material.SetColor("_EmissionColor", hatchColor * 3f);
            panel.GetComponent<MeshRenderer>().sharedMaterial = material;
            _exitGlow = material;
        }

        // Shadows off: the flashlight is the only shadow caster, so the atlas stays undivided.
        GameObject lightHolder = new GameObject("HatchLight");
        lightHolder.transform.SetParent(root.transform, false);
        lightHolder.transform.localPosition = new Vector3(0f, 1.5f, 0f);

        Light light = lightHolder.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = hatchColor;
        light.range = hatchLightRange;
        light.intensity = hatchLightIntensity;
        light.shadows = LightShadows.None;
        _exitLight = light;

        BuildPingSource(position);
    }

    /// <summary>
    /// A 2 m ring of emissive floor plus a caged platform, built from primitives - acceptable here (per
    /// the spec) since MazeEscape already builds its hatch at runtime the same way. Standing inside the
    /// ring is what UpdateLift tracks; everything else (the ping, the light, the timer) is identical to
    /// BuildHatch.
    /// </summary>
    private void BuildLift(Vector3 position)
    {
        GameObject root = new GameObject("EscapeLift");
        root.transform.position = position;

        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "LiftRing";
        Destroy(ring.GetComponent<Collider>());
        ring.transform.SetParent(root.transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        ring.transform.localScale = new Vector3(liftRingRadius * 2f, 0.03f, liftRingRadius * 2f);

        Material glow = null;
        if (_emissiveSource != null)
        {
            glow = new Material(_emissiveSource) { name = "LiftGlow" };
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_BaseColor", hatchColor);
            glow.SetColor("_EmissionColor", hatchColor * 3f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = glow;
            _exitGlow = glow;
        }

        // The "caged platform": four thin vertical bars around the ring's perimeter.
        for (int i = 0; i < 4; i++)
        {
            float angle = i * 90f * Mathf.Deg2Rad;
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "LiftBar";
            Destroy(bar.GetComponent<Collider>());
            bar.transform.SetParent(root.transform, false);
            bar.transform.localPosition = new Vector3(Mathf.Sin(angle) * liftRingRadius, 1.5f, Mathf.Cos(angle) * liftRingRadius);
            bar.transform.localScale = new Vector3(0.08f, 3f, 0.08f);
            if (glow != null) bar.GetComponent<MeshRenderer>().sharedMaterial = glow;
        }

        GameObject lightHolder = new GameObject("LiftLight");
        lightHolder.transform.SetParent(root.transform, false);
        lightHolder.transform.localPosition = new Vector3(0f, 1.5f, 0f);

        Light light = lightHolder.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = hatchColor;
        light.range = hatchLightRange;
        light.intensity = hatchLightIntensity;
        light.shadows = LightShadows.None;
        _exitLight = light;

        BuildPingSource(position);
    }

    /// <summary>F77: the ping's AudioSource lives on its own unparented GameObject (not the hatch/lift light holder), so UpdatePing can move it onto the current wayfinding target (the hatch, or the nearest missing key while locked) right before each PlayOneShot.</summary>
    private void BuildPingSource(Vector3 position)
    {
        GameObject pingGo = new GameObject("EscapePing");
        pingGo.transform.position = position;
        _pingAnchor = pingGo.transform;

        _pingClip = BuildPingClip();
        _ping = pingGo.AddComponent<AudioSource>();
        _ping.playOnAwake = false;
        _ping.loop = false;
        _ping.spatialBlend = 1f;
        _ping.rolloffMode = AudioRolloffMode.Linear;
        _ping.minDistance = 3f;
        _ping.maxDistance = pingMaxDistance;
        _ping.dopplerLevel = 0f;
        _pingTimer = pingInterval;
    }

    /// <summary>
    /// A short descending tone, so the player can tell "getting warmer" from panning and pitch alone.
    /// Built from scratch, the same way HorrorAudioDirector builds its heartbeat.
    /// </summary>
    private static AudioClip BuildPingClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.35f;
        const float decay = 0.12f;
        const float freqStart = 880f;
        const float freqEnd = 660f;

        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float sweepPerSecond = (freqEnd - freqStart) / duration;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            // Integrated frequency (a proper linear chirp), or the phase jumps between samples
            float phase = 2f * Mathf.PI * (freqStart * t + 0.5f * sweepPerSecond * t * t);

            float envelope = Mathf.Exp(-t / decay);
            envelope *= Mathf.Clamp01(t / 0.003f); // 3 ms fade-in kills the attack click

            samples[i] = Mathf.Sin(phase) * envelope;
        }

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f)
        {
            float gain = 0.8f / peak;
            for (int i = 0; i < sampleCount; i++) samples[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("HatchPing", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void BuildLandingPlatform(Vector3 hatchPosition)
    {
        GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name = "EscapePlatform";
        platform.transform.position = new Vector3(
            hatchPosition.x,
            hatchPosition.y - dropDepth - 0.1f,
            hatchPosition.z);
        platform.transform.localScale = new Vector3(platformSize, 0.2f, platformSize);

        // Unparented: the platform is scaled 8x, and a child would inherit that
        GameObject lightHolder = new GameObject("PlatformLight");
        lightHolder.transform.position = platform.transform.position + Vector3.up * 3f;

        Light light = lightHolder.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = hatchColor;
        light.range = 14f;
        light.intensity = 4f;
        light.shadows = LightShadows.None;
    }

    // ---------------------------------------------------------------- outcomes

    /// <summary>Shared completion for a Hatch reached or a Lift filled - drop, then GameOutcome, always.</summary>
    /// <summary>
    /// The trapdoor walk-in (F80). Ends through the same FloorCleared/Stop pair as CompleteEscape; the only
    /// difference is that the player is lowered through the opening instead of being placed under it.
    /// Bails out untouched if an ending (a capture) starts while the player is still stepping across.
    /// </summary>
    private IEnumerator EnterHatch()
    {
        _entering = true;
        PlayerLock.Freeze(_player, true);
        SetLidAngle(LidOpenAngle, 0.4f);
        AudioSource.PlayClipAtPoint(DoorAudio.BuildClunkClip(0.3f, 70f, 0.6f), _hatchPosition, 0.8f);

        ThirdPersonController controller = _player.GetComponent<ThirdPersonController>();
        CharacterController characterController = _player.GetComponent<CharacterController>();

        Vector3 start = _player.position;
        Vector3 over = new Vector3(_hatchPosition.x, start.y, _hatchPosition.z);
        Vector3 lookPoint = _hatchPosition + Vector3.down * 2f;

        float t = 0f;
        while (t < HatchApproachSeconds)
        {
            if (GameOutcome.IsOver || (_outcome != null && _outcome.IsEnding))
            {
                _entering = false;
                yield break;
            }

            t += Time.deltaTime;
            MovePlayer(characterController, Vector3.Lerp(start, over, Mathf.SmoothStep(0f, 1f, t / HatchApproachSeconds)));
            if (controller != null) controller.LookAt(lookPoint, 1f - Mathf.Exp(-7f * Time.deltaTime));
            yield return null;
        }

        // The blackout starts here and covers the camera passing through the floor.
        if (controller != null) controller.MovementLocked = true;
        _outcome.FloorCleared();

        t = 0f;
        while (t < HatchDescendSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / HatchDescendSeconds);
            MovePlayer(characterController, over + Vector3.down * (dropStartDepth * k * k));
            yield return null;
        }

        Stop();
    }

    /// <summary>A CharacterController overrides transform writes unless it is switched off around them.</summary>
    private void MovePlayer(CharacterController characterController, Vector3 position)
    {
        if (characterController != null) characterController.enabled = false;
        _player.position = position;
        if (characterController != null) characterController.enabled = true;
    }

    private void CompleteEscape()
    {
        DropThroughHatch();
        _outcome.FloorCleared();
        Stop();
    }

    /// <summary>
    /// Hatch geometry, so it lives here rather than in GameOutcome: the player is placed below the
    /// floor over the landing platform.
    /// </summary>
    private void DropThroughHatch()
    {
        SetLidAngle(LidOpenAngle, 0.25f);

        ThirdPersonController controller = _player.GetComponent<ThirdPersonController>();
        CharacterController characterController = _player.GetComponent<CharacterController>();

        // Locked, not disabled: gravity keeps running, so the player drops the last stretch onto the
        // platform under their own weight instead of being teleported onto it.
        if (controller != null) controller.MovementLocked = true;

        if (characterController != null) characterController.enabled = false;
        _player.position = new Vector3(_hatchPosition.x, _hatchPosition.y - dropStartDepth, _hatchPosition.z);
        if (characterController != null) characterController.enabled = true;
    }

    // ---------------------------------------------------------------- ui

    private static TMP_FontAsset ResolveFont() => RuntimeUi.ResolveFont();

    private Canvas ResolveCanvas() => RuntimeUi.ResolveCanvas();

    private void BuildTimerText()
    {
        Canvas canvas = ResolveCanvas();
        if (canvas == null)
        {
            Debug.LogWarning("MazeEscape: no Canvas in the scene, the countdown will not be shown.", this);
            return;
        }

        GameObject holder = new GameObject("EscapeTimer");
        holder.transform.SetParent(canvas.transform, false);
        holder.layer = canvas.gameObject.layer;

        _timerText = holder.AddComponent<TextMeshProUGUI>();
        _timerText.font = ResolveFont();
        _timerText.fontSize = timerFontSize;
        _timerText.color = timerColor;
        _timerText.alignment = TextAlignmentOptions.Center;
        _timerText.text = FormatTime(escapeSeconds);

        RectTransform rect = _timerText.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -40f);
        rect.sizeDelta = new Vector2(400f, 90f);

        // Parented under the timer so it hides with it in Stop().
        _chevronText = RuntimeUi.CreateText(_timerText.transform, "HatchBearing", "▲", 34f, hatchColor);
        RectTransform chevronRect = _chevronText.rectTransform;
        chevronRect.anchorMin = new Vector2(0.5f, 0.5f);
        chevronRect.anchorMax = new Vector2(0.5f, 0.5f);
        chevronRect.pivot = new Vector2(0.5f, 0.5f);
        chevronRect.sizeDelta = new Vector2(50f, 50f);

        // F73: hidden until UpdateLift has something to show - a no-op, always-inactive child on a
        // Hatch floor. Parented under the timer, so it hides with it in Stop() too.
        _liftText = RuntimeUi.CreateText(_timerText.transform, "LiftProgress", "", 28f, hatchColor);
        RectTransform liftRect = _liftText.rectTransform;
        liftRect.anchorMin = new Vector2(0.5f, 0f);
        liftRect.anchorMax = new Vector2(0.5f, 0f);
        liftRect.pivot = new Vector2(0.5f, 1f);
        liftRect.anchoredPosition = new Vector2(0f, -20f);
        liftRect.sizeDelta = new Vector2(400f, 50f);
        _liftText.gameObject.SetActive(false);
    }
}


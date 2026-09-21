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

    private bool _running;
    private bool _finished;
    private float _timeLeft;
    private Vector3 _hatchPosition;

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

    public void Configure(MazeGenerator maze, Transform player, Material emissiveSource, GameOutcome outcome)
    {
        _maze = maze;
        _player = player;
        _emissiveSource = emissiveSource;
        _outcome = outcome;
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

    private void OnEnable()
    {
        GameManager.AllStarsCollected += BeginEscape;
    }

    private void OnDisable()
    {
        GameManager.AllStarsCollected -= BeginEscape;
    }

    private void BeginEscape()
    {
        if (_running || _finished || _maze == null || _player == null || GameOutcome.IsOver) return;

        _hatchPosition = ChooseHatchCell();
        HatchOpen = true;

        BuildHatch(_hatchPosition);
        BuildLandingPlatform(_hatchPosition);
        BuildTimerText();

        _timeLeft = escapeSeconds;
        _running = true;

        Debug.Log($"MazeEscape: hatch opened at {_hatchPosition}. {escapeSeconds:0} seconds.", this);
    }

    private void Update()
    {
        if (!_running || _finished || Frozen) return;

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
        // them there), but guarded anyway: a hatch drop must never fire while inside a locker.
        if (_stealth != null && _stealth.Hidden) return;

        Vector3 toHatch = _player.position - _hatchPosition;
        toHatch.y = 0f;
        if (toHatch.sqrMagnitude <= hatchTriggerRadius * hatchTriggerRadius)
        {
            DropThroughHatch();
            _outcome.FloorCleared();
            Stop();
        }
    }

    private void UpdatePing(float dt)
    {
        if (_player == null) return;

        _pingTimer -= dt;
        if (_pingTimer > 0f) return;

        float distance = Vector3.Distance(_player.position, _hatchPosition);
        _pingTimer = pingInterval * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(distance / 20f));
        _chevronPulse = 1f;

        if (_ping != null && _pingClip != null)
        {
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

            Vector3 toHatchFlat = _hatchPosition - _player.position;
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

        Color c = hatchColor;
        c.a = 0.35f + 0.65f * _chevronPulse;
        _chevronText.color = c;
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
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "HatchPanel";
        Destroy(panel.GetComponent<Collider>());
        panel.transform.SetParent(root.transform, false);
        panel.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        panel.transform.localScale = new Vector3(1.8f, 0.06f, 1.8f);

        if (_emissiveSource != null)
        {
            Material material = new Material(_emissiveSource) { name = "HatchGlow" };
            material.EnableKeyword("_EMISSION");
            material.SetColor("_BaseColor", hatchColor);
            material.SetColor("_EmissionColor", hatchColor * 3f);
            panel.GetComponent<MeshRenderer>().sharedMaterial = material;
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

        _pingClip = BuildPingClip();
        _ping = lightHolder.AddComponent<AudioSource>();
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
    /// Built from scratch, the same way HorrorAudioDirector builds its heartbeat and tension bed.
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

    /// <summary>
    /// Hatch geometry, so it lives here rather than in GameOutcome: the player is placed below the
    /// floor over the landing platform.
    /// </summary>
    private void DropThroughHatch()
    {
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
    }
}


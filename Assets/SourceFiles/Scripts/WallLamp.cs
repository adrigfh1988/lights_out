using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A decorative-but-relevant wall sconce: gentle waver on every lamp, and a chance of an occasional
/// blackout blink on a fraction of them. Built by MazeGenerator.BuildWallLamps.
///
/// Blink/Kill are flags consumed inside Update rather than one-off writes, because Update overwrites
/// the light intensity and the emission property block every frame regardless.
/// </summary>
public class WallLamp : MonoBehaviour
{
    private struct BlinkRequest
    {
        public float Delay;
        public float Duration;
    }

    [Header("Themed prefab wiring")]
    [Tooltip("Prefab-wired point light. Leave unassigned and the generator falls back to the first child Light (used by both the gallery preview and the primitive fallback path).")]
    [SerializeField] private Light lightSource;
    [Tooltip("Prefab-wired glass/fixture renderer whose _EmissionColor the flicker dims every frame. The material needs _EMISSION enabled with a non-black colour or the dimming is invisible. Falls back to the first child Renderer if unassigned.")]
    [SerializeField] private Renderer glassRenderer;

    private Light _light;
    private Renderer _renderer;
    private MaterialPropertyBlock _block;
    private Color _emissionBaseColor;
    private float _base;
    private bool _faulty;
    private float _phase;
    private bool _configured;

    private float _dropoutTimer;
    private float _dropoutRemaining;
    private System.Random _rng;

    private readonly List<BlinkRequest> _blinks = new List<BlinkRequest>();

    private bool _dying;
    private float _killFadeSeconds;
    private float _killTimer;

    // F73: Blackout floors start every lamp unpowered. A delayed SetPowered (the fuse box's ripple)
    // is a simple countdown rather than a queue - only one power transition is ever pending at a time.
    private bool _powered = true;
    private float _powerDelayRemaining = -1f;
    private bool _powerDelayTargetOn;

    /// <summary>The light's range, used by LightPool.</summary>
    public float Range => _light != null ? _light.range : 0f;

    /// <summary>0..1 fraction of base intensity after flicker/blink, updated every frame. 0 while unpowered.</summary>
    public float CurrentIntensity01 { get; private set; } = 1f;

    /// <summary>False while this lamp has no power (F73 Blackout floors, before its sector's fuse is restored).</summary>
    public bool IsPowered => _powered;

    /// <summary>True once Kill's fade has finished. The lamp stays dark for the rest of the run.</summary>
    public bool IsDead { get; private set; }

    /// <summary>True for the lamp mounted above a locker. Exempt from Kill - a locker you cannot find is not a hiding spot.</summary>
    public bool IsLockerLamp { get; private set; }

    /// <summary>
    /// The resolved light: lightSource if the prefab wired one, otherwise the first child Light. Lets a
    /// caller (MazeGenerator.BuildWallLamp) set colour/range/intensity on a themed clone before calling
    /// Configure below, without duplicating the fallback-resolution logic.
    /// </summary>
    public Light LightOrChild => lightSource != null ? lightSource : (lightSource = GetComponentInChildren<Light>());

    /// <summary>Called once by MazeGenerator.BuildWallLamps right after AddComponent (primitive path).</summary>
    public void Configure(Light light, Renderer fixtureRenderer, float baseIntensity, bool faulty, int phase, bool isLockerLamp)
    {
        _light = light;
        _renderer = fixtureRenderer;
        _base = baseIntensity;
        _faulty = faulty;
        _phase = phase;
        _rng = new System.Random(phase);
        _block = new MaterialPropertyBlock();
        IsLockerLamp = isLockerLamp;
        _configured = true;
        _powered = true;

        if (_renderer != null && _renderer.sharedMaterial != null && _renderer.sharedMaterial.HasProperty("_EmissionColor"))
        {
            _emissionBaseColor = _renderer.sharedMaterial.GetColor("_EmissionColor");
        }

        ScheduleNextDropout();

        // F73: every lamp registers so LightPool.ExposureAt (PlayerStealthState's stealth exposure) sees
        // it without MazeGenerator handing out its lamp list directly. Range is already final here - it
        // is set on the Light before Configure runs in every one of BuildWallLamp's three branches.
        LightPool.Register(transform, _light != null ? _light.range : 0f, () => CurrentIntensity01);
    }

    private void OnDestroy()
    {
        LightPool.Unregister(transform);
    }

    /// <summary>
    /// Called once by MazeGenerator.BuildWallLamp for a themed prefab clone. Resolves the prefab-wired
    /// light/glass (falling back to GetComponentInChildren) and defers to the Configure above. Gallery
    /// instances are never Configured, so _configured/_light both stay unset and Update leaves them alone.
    /// </summary>
    public void Configure(float baseIntensity, bool faulty, int phase, bool isLockerLamp)
    {
        Light resolvedLight = lightSource != null ? lightSource : GetComponentInChildren<Light>();
        Renderer resolvedGlass = glassRenderer != null ? glassRenderer : GetComponentInChildren<Renderer>();
        Configure(resolvedLight, resolvedGlass, baseIntensity, faulty, phase, isLockerLamp);
    }

    private void ScheduleNextDropout()
    {
        _dropoutTimer = _faulty ? (float)(3.0 + _rng.NextDouble() * 6.0) : float.MaxValue;
    }

    /// <summary>Queues a blackout of `seconds`, optionally starting `delay` seconds from now. Stacks with waver/dropout/kill.</summary>
    public void Blink(float seconds, float delay = 0f)
    {
        if (IsDead || seconds <= 0f) return;
        _blinks.Add(new BlinkRequest { Delay = Mathf.Max(0f, delay), Duration = seconds });
    }

    /// <summary>Starts a fade to permanently off. No-op on a locker lamp or one already dead/dying. Allowed while unpowered - it stays dead once powered back on (Update's unpowered branch returns before the dying fade runs).</summary>
    public void Kill(float fadeSeconds)
    {
        if (IsLockerLamp || IsDead || _dying) return;
        _dying = true;
        _killFadeSeconds = Mathf.Max(0.1f, fadeSeconds);
        _killTimer = 0f;
    }

    /// <summary>
    /// F73: powers this lamp on or off. `delay` (the fuse box's BFS ripple, seconds) queues the change
    /// instead of applying it immediately - only one transition is ever pending, so a plain countdown is
    /// enough. Never touches IsDead/_dying either way: an unpowered lamp is simply dark, not killed, and
    /// a killed lamp that gets powered back on resumes (and finishes) its fade instead of lighting up.
    /// </summary>
    public void SetPowered(bool on, float delay = 0f)
    {
        if (delay > 0f)
        {
            _powerDelayTargetOn = on;
            _powerDelayRemaining = delay;
            return;
        }

        ApplyPower(on);
    }

    private void ApplyPower(bool on)
    {
        if (on)
        {
            if (_powered) return;
            _powered = true;
            if (_light != null) _light.enabled = true;

            // The power-on "strike": two quick blinks before settling to steady light. Queued through
            // the same _blinks list Blink() uses, rather than a new coroutine.
            _blinks.Add(new BlinkRequest { Delay = 0f, Duration = 0.08f });
            _blinks.Add(new BlinkRequest { Delay = 0.16f, Duration = 0.08f });
        }
        else
        {
            _powered = false;
        }
    }

    private void Update()
    {
        // Gallery lamps are never Configured (only MazeGenerator's clones are), so both guards below
        // keep them static previews instead of ticking the flicker/dropout logic in the Editor.
        if (!_configured || _light == null) return;
        // Once dead, stay dead: without this the frame after the fade finishes falls through to the
        // waver below and the fixture glows again, which is the opposite of what Kill is for.
        if (IsDead) return;

        if (_powerDelayRemaining >= 0f)
        {
            _powerDelayRemaining -= Time.deltaTime;
            if (_powerDelayRemaining <= 0f)
            {
                _powerDelayRemaining = -1f;
                ApplyPower(_powerDelayTargetOn);
            }
        }

        if (!_powered)
        {
            // Dark, no flicker/dropout tick - a Blackout floor's lamp before its fuse is restored.
            CurrentIntensity01 = 0f;
            _light.intensity = 0f;
            _light.enabled = false;

            if (_renderer != null)
            {
                _renderer.GetPropertyBlock(_block);
                _block.SetColor("_EmissionColor", Color.black);
                _renderer.SetPropertyBlock(_block);
            }
            return;
        }

        // Gentle waver on every lamp, 0.85..1.0
        float n = Mathf.PerlinNoise(Time.time * 6f + _phase, 0.3f);
        float k = 0.85f + 0.15f * n;

        if (_faulty)
        {
            if (_dropoutRemaining > 0f)
            {
                _dropoutRemaining -= Time.deltaTime;
                k = 0f;
                if (_dropoutRemaining <= 0f) ScheduleNextDropout();
            }
            else
            {
                _dropoutTimer -= Time.deltaTime;
                if (_dropoutTimer <= 0f)
                {
                    _dropoutRemaining = 0.08f + (float)_rng.NextDouble() * 0.27f;
                }
            }
        }

        // Queued blinks force a total blackout while active, regardless of waver/dropout above.
        for (int i = _blinks.Count - 1; i >= 0; i--)
        {
            BlinkRequest request = _blinks[i];
            if (request.Delay > 0f)
            {
                request.Delay -= Time.deltaTime;
                _blinks[i] = request;
                continue;
            }

            request.Duration -= Time.deltaTime;
            if (request.Duration <= 0f)
            {
                _blinks.RemoveAt(i);
                continue;
            }

            _blinks[i] = request;
            k = 0f;
        }

        if (_dying)
        {
            _killTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_killTimer / _killFadeSeconds);

            // A wavering ramp down to zero rather than a clean lerp, so it reads as three stutters
            // rather than a smooth dim.
            float stutter = 0.4f + 0.6f * Mathf.PingPong(t * 6f, 1f);
            k *= Mathf.Lerp(1f, 0f, t) * stutter;

            if (t >= 1f)
            {
                IsDead = true;
                _dying = false;
                k = 0f;
                _light.enabled = false;
            }
        }

        CurrentIntensity01 = k;
        _light.intensity = _base * k;

        if (_renderer != null)
        {
            // A property block rather than an instance material: every lamp shares one Material, so
            // writing to it directly would flicker every other lamp in lockstep.
            _renderer.GetPropertyBlock(_block);
            _block.SetColor("_EmissionColor", _emissionBaseColor * k);
            _renderer.SetPropertyBlock(_block);
        }
    }
}

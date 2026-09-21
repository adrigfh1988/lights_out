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

    /// <summary>The light's range, used by ExposureAt.</summary>
    public float Range => _light != null ? _light.range : 0f;

    /// <summary>0..1 fraction of base intensity after flicker/blink, updated every frame.</summary>
    public float CurrentIntensity01 { get; private set; } = 1f;

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

        if (_renderer != null && _renderer.sharedMaterial != null && _renderer.sharedMaterial.HasProperty("_EmissionColor"))
        {
            _emissionBaseColor = _renderer.sharedMaterial.GetColor("_EmissionColor");
        }

        ScheduleNextDropout();
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

    /// <summary>Starts a fade to permanently off. No-op on a locker lamp or one already dead/dying.</summary>
    public void Kill(float fadeSeconds)
    {
        if (IsLockerLamp || IsDead || _dying) return;
        _dying = true;
        _killFadeSeconds = Mathf.Max(0.1f, fadeSeconds);
        _killTimer = 0f;
    }

    private void Update()
    {
        // Gallery lamps are never Configured (only MazeGenerator's clones are), so both guards below
        // keep them static previews instead of ticking the flicker/dropout logic in the Editor.
        if (!_configured || _light == null) return;
        // Once dead, stay dead: without this the frame after the fade finishes falls through to the
        // waver below and the fixture glows again, which is the opposite of what Kill is for.
        if (IsDead) return;

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

    /// <summary>How strongly point is lit by the nearest lamp that reaches it, 0..1.</summary>
    public static float ExposureAt(IReadOnlyList<WallLamp> lamps, Vector3 point)
    {
        if (lamps == null) return 0f;

        float best = 0f;
        for (int i = 0; i < lamps.Count; i++)
        {
            WallLamp lamp = lamps[i];
            if (lamp == null) continue;

            float range = lamp.Range;
            if (range <= 0f) continue;

            float d = Vector3.Distance(lamp.transform.position, point);
            if (d < range)
            {
                best = Mathf.Max(best, (1f - d / range) * lamp.CurrentIntensity01);
            }
        }

        return best;
    }
}

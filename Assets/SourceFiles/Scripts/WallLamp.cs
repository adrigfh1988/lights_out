using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A decorative-but-relevant wall sconce: gentle waver on every lamp, and a chance of an occasional
/// blackout blink on a fraction of them. Built by MazeGenerator.BuildWallLamps.
/// </summary>
public class WallLamp : MonoBehaviour
{
    private Light _light;
    private Renderer _renderer;
    private MaterialPropertyBlock _block;
    private Color _emissionBaseColor;
    private float _base;
    private bool _faulty;
    private float _phase;

    private float _dropoutTimer;
    private float _dropoutRemaining;
    private System.Random _rng;

    /// <summary>The light's range, used by ExposureAt.</summary>
    public float Range => _light != null ? _light.range : 0f;

    /// <summary>0..1 fraction of base intensity after flicker/blink, updated every frame.</summary>
    public float CurrentIntensity01 { get; private set; } = 1f;

    /// <summary>Called once by MazeGenerator.BuildWallLamps right after AddComponent.</summary>
    public void Configure(Light light, Renderer fixtureRenderer, float baseIntensity, bool faulty, int phase)
    {
        _light = light;
        _renderer = fixtureRenderer;
        _base = baseIntensity;
        _faulty = faulty;
        _phase = phase;
        _rng = new System.Random(phase);
        _block = new MaterialPropertyBlock();

        if (_renderer != null && _renderer.sharedMaterial != null && _renderer.sharedMaterial.HasProperty("_EmissionColor"))
        {
            _emissionBaseColor = _renderer.sharedMaterial.GetColor("_EmissionColor");
        }

        ScheduleNextDropout();
    }

    private void ScheduleNextDropout()
    {
        _dropoutTimer = _faulty ? (float)(3.0 + _rng.NextDouble() * 6.0) : float.MaxValue;
    }

    private void Update()
    {
        if (_light == null) return;

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

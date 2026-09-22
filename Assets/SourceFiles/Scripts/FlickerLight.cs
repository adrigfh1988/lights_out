using UnityEngine;

/// <summary>
/// A small, cheap flicker for set-piece dressing lights (F70 decision 10: at most one Light per set
/// piece, range &lt;= 4, intensity &lt;= 0.8, LightShadows.None; these are never added to
/// MazeGenerator._lamps, so DreadDirector/PhantomDirector's lamp logic ignores them). Configured once by
/// MazeGenerator.BuildSetPieces with a per-instance seed so two copies of the same set piece do not
/// flicker in lockstep.
///
/// Drives an optional Light's intensity and/or an optional emissive Renderer's _EmissionColor (same
/// MaterialPropertyBlock technique as WallLamp, so sharing one material across many props never flickers
/// them together). Either reference may be left unassigned - a prop can glow without lighting anything
/// (an emissive-only screen) or light without an emissive surface of its own.
///
/// Uses Time.time, the run clock: it stands still behind the title screen and while paused, same as
/// WallLamp.
/// </summary>
public class FlickerLight : MonoBehaviour
{
    public enum FlickerMode
    {
        /// <summary>Gentle Perlin wobble, +/-25% of base intensity. A shrine candle, a lantern.</summary>
        Candle,
        /// <summary>Steady, with occasional short (0.05-0.3 s) total dropouts every 2-7 s. A dying torch, a failing screen.</summary>
        Faulty,
        /// <summary>Mostly off, with short bright jittery bursts. A shorted wire, an electrical spark.</summary>
        Spark
    }

    [SerializeField] private FlickerMode mode = FlickerMode.Candle;

    [Tooltip("Optional. Dimmed by writing Light.intensity every frame.")]
    [SerializeField] private Light targetLight;

    [Tooltip("Optional. Dimmed through a MaterialPropertyBlock's _EmissionColor - the shared material itself is never touched, so other props sharing it are unaffected. Needs _EMISSION enabled with a non-black emission colour or the dimming is invisible.")]
    [SerializeField] private Renderer emissiveRenderer;

    private System.Random _rng;
    private float _phase;
    private float _baseIntensity;
    private Color _emissionBaseColor;
    private MaterialPropertyBlock _block;
    private bool _configured;

    private float _dropoutTimer;
    private float _dropoutRemaining;
    private float _burstTimer;
    private float _burstRemaining;

    /// <summary>Called once by MazeGenerator.BuildSetPieces after the prefab is cloned.</summary>
    public void Configure(int seed)
    {
        _rng = new System.Random(seed);
        _phase = seed % 1000;
        _block = new MaterialPropertyBlock();

        if (targetLight != null) _baseIntensity = targetLight.intensity;

        if (emissiveRenderer != null && emissiveRenderer.sharedMaterial != null && emissiveRenderer.sharedMaterial.HasProperty("_EmissionColor"))
        {
            _emissionBaseColor = emissiveRenderer.sharedMaterial.GetColor("_EmissionColor");
        }

        if (mode == FlickerMode.Faulty) ScheduleNextDropout();
        if (mode == FlickerMode.Spark) ScheduleNextBurst();

        _configured = true;
    }

    private void ScheduleNextDropout()
    {
        _dropoutTimer = (float)(2.0 + _rng.NextDouble() * 5.0);
    }

    private void ScheduleNextBurst()
    {
        _burstTimer = (float)(1.5 + _rng.NextDouble() * 3.5);
    }

    private void Update()
    {
        // Never Configured (a gallery showcase instance) -> stay a static preview, same guard WallLamp uses.
        if (!_configured) return;
        if (targetLight == null && emissiveRenderer == null) return;

        float k = 1f;

        switch (mode)
        {
            case FlickerMode.Candle:
                float noise = Mathf.PerlinNoise(Time.time * 4f + _phase, 0.5f);
                k = 0.75f + 0.25f * noise;
                break;

            case FlickerMode.Faulty:
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
                        _dropoutRemaining = 0.05f + (float)_rng.NextDouble() * 0.25f;
                    }
                }
                break;

            case FlickerMode.Spark:
                if (_burstRemaining > 0f)
                {
                    _burstRemaining -= Time.deltaTime;
                    k = 0.5f + 0.5f * Mathf.PerlinNoise(Time.time * 30f, 0.5f);
                    if (_burstRemaining <= 0f) ScheduleNextBurst();
                }
                else
                {
                    _burstTimer -= Time.deltaTime;
                    k = 0f;
                    if (_burstTimer <= 0f)
                    {
                        _burstRemaining = 0.08f + (float)_rng.NextDouble() * 0.2f;
                    }
                }
                break;
        }

        if (targetLight != null) targetLight.intensity = _baseIntensity * k;

        if (emissiveRenderer != null)
        {
            emissiveRenderer.GetPropertyBlock(_block);
            _block.SetColor("_EmissionColor", _emissionBaseColor * k);
            emissiveRenderer.SetPropertyBlock(_block);
        }
    }
}

using UnityEngine;

/// <summary>
/// F71: makes a star's point light breathe gently instead of sitting at a flat intensity - a small
/// "this is alive, this is the thing worth finding" cue now that the rest of the maze has gone much
/// darker around it. Attached by MazeGenerator.AttachStarLight right after the Light itself.
///
/// Deliberately not FlickerLight: that component's Candle mode is Perlin noise for prop dressing, not
/// the clean, predictable sine wave a wayfinding cue wants, and it is gated behind Configure(seed) for a
/// different call pattern (BuildSetPieces, not AttachStarLight).
/// </summary>
public class StarPulse : MonoBehaviour
{
    private const float FrequencyHz = 0.5f;
    private const float Amplitude = 0.3f;

    private Light _light;
    private float _baseIntensity;

    /// <summary>F90: an authored template (the lore note's glint) carries a Light and no Configure call - pick it up here. Configure still overrides.</summary>
    private void Awake()
    {
        if (_light != null) return;
        _light = GetComponent<Light>();
        if (_light != null) _baseIntensity = _light.intensity;
    }

    /// <summary>Called once by MazeGenerator.AttachStarLight right after the Light is created.</summary>
    public void Configure(Light light)
    {
        _light = light;
        _baseIntensity = light.intensity;
    }

    private void Update()
    {
        if (_light == null) return;

        // Time.time, not unscaled: the pulse stands still behind the title screen and under pause,
        // same as every other run-clock-driven effect in the maze.
        float wave = Mathf.Sin(2f * Mathf.PI * FrequencyHz * Time.time);
        _light.intensity = _baseIntensity * (1f + Amplitude * wave);
    }
}

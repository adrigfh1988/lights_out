using UnityEngine;

/// <summary>
/// F75 Fuse Map shop item: a small, slow-pulsing point light added at runtime to a fuse box, wall
/// button or vault door when the Fuse Map modifier is active for the floor - see
/// MazeGenerator.SetUpAtmosphere. One shared component so FuseBox/WallButton/MazeDoor don't each
/// duplicate the pulse math; same clean sine-wave technique as StarPulse, just slower and dimmer.
/// </summary>
public class Beacon : MonoBehaviour
{
    private const float FrequencyHz = 0.35f;
    private const float BaseIntensity = 0.55f;
    private const float Amplitude = 0.5f;

    private Light _light;

    /// <summary>Adds a beacon child at `localOffset` under `parent` and returns it, already ticking.</summary>
    public static Beacon Attach(Transform parent, Vector3 localOffset)
    {
        GameObject go = new GameObject("Beacon");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localOffset;

        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.5f, 0.85f, 1f);
        light.range = 3f;
        light.intensity = BaseIntensity;
        light.shadows = LightShadows.None;

        Beacon beacon = go.AddComponent<Beacon>();
        beacon._light = light;
        return beacon;
    }

    private void Update()
    {
        if (_light == null) return;

        // Time.time, not unscaled: stands still behind the title screen and under pause, same as
        // every other run-clock-driven effect in the maze.
        float wave = Mathf.Sin(2f * Mathf.PI * FrequencyHz * Time.time);
        _light.intensity = BaseIntensity * (1f + Amplitude * wave);
    }
}

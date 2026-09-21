using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Takes the lights out. Everything here is a play-mode RenderSettings change, so it reverts when you
/// stop - the scene's authored lighting is never touched.
/// </summary>
[DefaultExecutionOrder(-50)]
public class HorrorAtmosphere : MonoBehaviour
{
    [Header("Ambient")]
    [SerializeField] private Color ambientLight = new Color(0.02f, 0.02f, 0.03f);

    [Header("Fog")]
    [SerializeField] private bool enableFog = true;
    [SerializeField] private Color fogColor = new Color(0.01f, 0.01f, 0.015f);
    [Tooltip("Exponential-squared fog reaches near-total opacity around 3/density metres. 0.07 ~ 43 m, 0.12 ~ 25 m. Lower = you see further.")]
    [SerializeField] private float fogDensity = 0.07f;

    [Header("Lights")]
    [Tooltip("Intensity for the scene's directional light. Raise it at runtime to see the maze while tuning.")]
    [SerializeField] private float directionalLightIntensity = 0f;

    [Header("Post")]
    [Tooltip("The scene profile ships +0.2 exposure, which fights all of the above. This neutralises it on a runtime copy of the profile - the asset on disk is not touched.")]
    [SerializeField] private float postExposure = -0.2f;

    // Base values Darken lerps away from. Captured in Awake from the serialized defaults, then
    // overwritten by ApplyProfile if a floor profile is in use - either way Darken has a real base.
    private Color _baseAmbient;
    private float _baseFogDensity;
    private Coroutine _darkenRoutine;

    /// <summary>Per-floor light level, keeping whatever fog colour is already serialized. Awake has already applied the defaults, so this re-applies. Used by the primitive fallback path, which has no theme colour to hand in.</summary>
    public void ApplyProfile(Color ambient, float fogDensity)
    {
        ApplyProfile(ambient, fogDensity, fogColor);
    }

    /// <summary>
    /// Per-floor light level and fog colour. Awake has already applied the defaults, so this re-applies.
    /// Also updates the camera's background colour when the camera already exists - SetUpAtmosphere runs
    /// after FirstPersonRig, so in practice it always does - so SealTheSkybox's job stays consistent with
    /// a themed fog colour instead of whatever the serialized default was.
    /// </summary>
    public void ApplyProfile(Color ambient, float fogDensity, Color fogColor)
    {
        ambientLight = ambient;
        this.fogDensity = fogDensity;
        this.fogColor = fogColor;
        _baseAmbient = ambient;
        _baseFogDensity = fogDensity;
        RenderSettings.ambientLight = ambient;
        if (enableFog)
        {
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogColor = fogColor;
        }

        Camera camera = Camera.main;
        if (camera != null) camera.backgroundColor = fogColor;
    }

    /// <summary>
    /// F27: lerps ambient/fog toward a darker target over `seconds`. Absolute, not cumulative - a
    /// fraction of 0.35 then 0.6 is monotonic, since each lerps from the untouched base rather than
    /// from wherever the previous call left off.
    /// </summary>
    public void Darken(float fraction, float seconds)
    {
        fraction = Mathf.Clamp01(fraction);
        if (_darkenRoutine != null) StopCoroutine(_darkenRoutine);
        _darkenRoutine = StartCoroutine(DarkenRoutine(fraction, Mathf.Max(0.01f, seconds)));
    }

    private System.Collections.IEnumerator DarkenRoutine(float fraction, float seconds)
    {
        Color startAmbient = RenderSettings.ambientLight;
        float startFog = RenderSettings.fogDensity;

        Color targetAmbient = _baseAmbient * (1f - 0.5f * fraction);
        float targetFog = _baseFogDensity * (1f + 0.5f * fraction);

        float t = 0f;
        while (t < seconds)
        {
            if (GameOutcome.IsOver) yield break;

            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / seconds);
            RenderSettings.ambientLight = Color.Lerp(startAmbient, targetAmbient, k);
            if (enableFog) RenderSettings.fogDensity = Mathf.Lerp(startFog, targetFog, k);
            yield return null;
        }

        RenderSettings.ambientLight = targetAmbient;
        if (enableFog) RenderSettings.fogDensity = targetFog;
        _darkenRoutine = null;
    }

    private void Awake()
    {
        _baseAmbient = ambientLight;
        _baseFogDensity = fogDensity;

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambientLight;

        // Without this the maze stays visibly grey no matter what else is off: the skybox reflection
        // cubemap keeps lighting the walls, which are glossy enough to show it.
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = null;
        RenderSettings.reflectionIntensity = 0f;

        if (enableFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
        }

        foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
        {
            if (light.type == LightType.Directional)
            {
                light.intensity = directionalLightIntensity;
            }
        }
    }

    private void Start()
    {
        // Start, not Awake: the camera has been reparented by FirstPersonRig and the Volume has run
        // its own OnEnable by now.
        SealTheSkybox();
        NeutraliseExposure();
    }

    private void SealTheSkybox()
    {
        // Unity's fog never touches the skybox, so without this the world fades toward the fog colour
        // while the background stays a different one. The maze has a ceiling, but gaps and the moment
        // of spawning still show it.
        Camera camera = Camera.main;
        if (camera == null) return;

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = fogColor;
    }

    private void NeutraliseExposure()
    {
        foreach (Volume volume in FindObjectsByType<Volume>(FindObjectsInactive.Exclude))
        {
            // volume.profile (not sharedProfile) lazily clones the asset, so this edit lives only for
            // this play session and leaves the .asset on disk clean.
            if (volume.profile != null && volume.profile.TryGet(out ColorAdjustments colorAdjustments))
            {
                colorAdjustments.postExposure.overrideState = true;
                colorAdjustments.postExposure.value = postExposure;
            }
        }
    }
}

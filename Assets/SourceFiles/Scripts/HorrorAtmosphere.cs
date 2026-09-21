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

    /// <summary>Per-floor light level. Awake has already applied the defaults, so this re-applies.</summary>
    public void ApplyProfile(Color ambient, float fogDensity)
    {
        ambientLight = ambient;
        this.fogDensity = fogDensity;
        RenderSettings.ambientLight = ambient;
        if (enableFog) RenderSettings.fogDensity = fogDensity;
    }

    private void Awake()
    {
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

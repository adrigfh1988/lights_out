using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// F76 T9: trims fill-rate cost on mobile web - 0.75x URP render scale and a lower flashlight shadow
/// tier. Player builds only: never runs in the Editor, since flipping renderScale on the live
/// UniversalRenderPipelineAsset would dirty the very asset every other build (and the Editor) shares.
/// Added once by MazeGenerator.SetUpAtmosphere.
/// </summary>
public class MobilePerformance : MonoBehaviour
{
    private const float MobileRenderScale = 0.75f;
    private const float DetectionWindowSeconds = 2f;

    private Flashlight _flashlight;
    private bool _applied;

    public void Configure(Flashlight flashlight)
    {
        _flashlight = flashlight;
    }

#if !UNITY_EDITOR
    private void Start()
    {
        if (Application.isMobilePlatform)
        {
            Apply("Application.isMobilePlatform");
            return;
        }
        StartCoroutine(WatchForEarlyTouch());
    }

    // iPadOS Safari reports itself as desktop Mac, so isMobilePlatform alone would miss it - a touch
    // in the first couple of seconds after launch counts too (matches TouchInput's own auto-detect).
    private IEnumerator WatchForEarlyTouch()
    {
        float elapsed = 0f;
        while (elapsed < DetectionWindowSeconds)
        {
            if (_applied) yield break;

#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                Apply("first touch within 2s of launch");
                yield break;
            }
#endif
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void Apply(string reason)
    {
        if (_applied) return;
        _applied = true;

        UniversalRenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (asset != null) asset.renderScale = MobileRenderScale;

        if (_flashlight != null)
        {
            _flashlight.SetShadowTier(UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium);
        }

        Debug.Log($"MobilePerformance: applied mobile settings ({reason}) - render scale {MobileRenderScale}, flashlight shadows Medium.");
    }
#endif
}

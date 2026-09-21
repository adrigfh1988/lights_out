using UnityEngine;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The player's head-mounted torch, and the risk that comes with it: while it is on the AI can see
/// the player from much further away. Created at runtime by <see cref="FirstPersonRig"/> on the camera.
/// </summary>
public class Flashlight : MonoBehaviour
{
    [Header("Beam")]
    [Tooltip("How far the beam reaches. Keep it near the fog distance, or the cone ends in fog you cannot see through anyway.")]
    [SerializeField] private float range = 32f;
    [Tooltip("Width of the lit circle. A wide floodlight rather than a torch beam.")]
    [SerializeField] private float outerAngle = 95f;
    [Tooltip("Width of the bright core. The gap to outerAngle is the soft edge.")]
    [SerializeField] private float innerAngle = 50f;
    [Tooltip("A wider cone spreads the same energy over more wall, so this rises with outerAngle")]
    [SerializeField] private float intensity = 6.5f;
    [SerializeField] private Color beamColor = new Color(1f, 0.95f, 0.86f);

    [Header("Flicker")]
    [Tooltip("How much the beam wavers, 0 = steady")]
    [SerializeField] private float flickerAmount = 0.06f;
    [SerializeField] private float flickerSpeed = 7f;

    [Header("Battery")]
    [Tooltip("Total seconds the torch can be on in one run. It never recharges.")]
    [SerializeField] private float batterySeconds = 180f;
    [Tooltip("Below this fraction the beam starts to stutter")]
    [SerializeField] private float lowBatteryFraction = 0.2f;
    [Tooltip("Flicker amount at empty; blends from flickerAmount as the charge falls below lowBatteryFraction")]
    [SerializeField] private float dyingFlickerAmount = 0.55f;
    [Tooltip("Beam intensity multiplier at the very end of the battery")]
    [SerializeField] private float dyingIntensityFraction = 0.55f;

    private Light _light;
    private PlayerStealthState _stealth;
    private float _stutterRemaining;

    public bool IsOn { get; private set; } = true;

    /// <summary>The beam's reach, for anything that wants to know if it just caught something.</summary>
    public float Range => range;

    /// <summary>Full width of the lit cone.</summary>
    public float OuterAngle => outerAngle;

    /// <summary>Ignore the toggle key while false: under the pause menu, and through an ending.</summary>
    public bool InputEnabled = true;

    /// <summary>Total seconds of battery remaining. Starts at batterySeconds and only ever falls.</summary>
    public float SecondsLeft { get; private set; }

    /// <summary>0..1 fraction of the battery remaining.</summary>
    public float Charge => batterySeconds > 0f ? SecondsLeft / batterySeconds : 0f;

    /// <summary>True once the battery has run out for the rest of the run.</summary>
    public bool IsDead => SecondsLeft <= 0f;

    /// <summary>True while the battery is low enough to stutter.</summary>
    public bool IsLow => Charge <= lowBatteryFraction;

    public void SetOn(bool on)
    {
        // A dead torch cannot be switched on - this is also what makes GameOutcome.CaptureSequence's
        // SetOn(true) flicker beats a no-op on a dead battery, so the sequence just plays dark.
        IsOn = on && !IsDead;
        ApplyState();
    }

    /// <summary>Same as SetOn(false), named so the intent at a call site (e.g. Locker.Enter) is clear.</summary>
    public void ForceOff()
    {
        SetOn(false);
    }

    /// <summary>Shop cosmetic: recolours the beam for the rest of the campaign.</summary>
    public void SetBeamColor(Color color)
    {
        beamColor = color;
        if (_light != null) _light.color = color;
    }

    /// <summary>Spare Battery: a full cell. Switches the torch on - you just loaded it, it is on.</summary>
    public void Refill()
    {
        SecondsLeft = batterySeconds;
        SetOn(true);
    }

    /// <summary>
    /// A DreadDirector telegraph beat: makes the beam waver hard for `seconds`, ignored while off or
    /// dead. A flag read in Update rather than a one-off write, since Update rewrites intensity every
    /// frame anyway.
    /// </summary>
    public void Stutter(float seconds)
    {
        _stutterRemaining = Mathf.Max(_stutterRemaining, seconds);
    }

    /// <summary>
    /// Called by FirstPersonRig right after AddComponent — which means Awake has already run with no
    /// stealth state to push the initial value into, so re-apply it here.
    /// </summary>
    public void Bind(PlayerStealthState stealth)
    {
        _stealth = stealth;
        ApplyState();
    }

    private void Awake()
    {
        SecondsLeft = batterySeconds;

        _light = gameObject.AddComponent<Light>();
        _light.type = LightType.Spot;
        _light.color = beamColor;
        _light.intensity = intensity;
        _light.range = range;
        _light.spotAngle = outerAngle;
        _light.innerSpotAngle = innerAngle;
        _light.shadows = LightShadows.Soft;
        _light.shadowStrength = 0.9f;
        _light.shadowNearPlane = 0.3f;

        // A new Light defaults to the Medium shadow tier (512 px in PC_RPAsset), which looks like mush
        // across a 20 m cone. usePipelineSettings has to go off as well or the bias values below are
        // ignored in favour of the pipeline asset's.
        UniversalAdditionalLightData data = _light.GetUniversalAdditionalLightData();
        if (data != null)
        {
            data.additionalLightsShadowResolutionTier =
                UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh;
            data.usePipelineSettings = false;
        }

        // The walls are half-metre boxes lit at a grazing angle from close range, so the pipeline
        // defaults (0.1 / 0.5) leave a visible gap at the wall/floor seam.
        _light.shadowBias = 0.05f;
        _light.shadowNormalBias = 0.35f;

        ApplyState();
    }

    private void Update()
    {
        // Decremented every frame, on or off, so a stutter queued while the torch happens to be off
        // cannot linger and fire later once it is switched on.
        _stutterRemaining = Mathf.Max(0f, _stutterRemaining - Time.deltaTime);

        // Scaled deltaTime: the drain freezes for free behind the title screen and under pause.
        if (IsOn && !IsDead)
        {
            SecondsLeft = Mathf.Max(0f, SecondsLeft - Time.deltaTime);
            if (IsDead) SetOn(false);
        }

#if ENABLE_INPUT_SYSTEM
        // Read the key directly: the Player action map has no spare action, and rewiring PlayerInput
        // for one key is more risk than it is worth. Keyboard.current is null with no keyboard present.
        // Hidden also blocks it: a locker forces InputEnabled off, but PauseMenu.Resume sets it back
        // on, and this is the cheap second guard against a torch leaking light inside a closed locker.
        bool hiddenGate = _stealth == null || !_stealth.Hidden;
        if (InputEnabled && !IsDead && hiddenGate && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
        {
            SetOn(!IsOn);
        }
#endif

        if (IsOn)
        {
            bool stuttering = _stutterRemaining > 0f;
            float amount = stuttering ? 0.75f
                : IsLow ? Mathf.Lerp(dyingFlickerAmount, flickerAmount, Charge / lowBatteryFraction)
                : flickerAmount;
            float flicker = amount > 0f ? 1f - amount + amount * Mathf.PerlinNoise(Time.time * flickerSpeed, 0f) : 1f;
            float dyingScale = Mathf.Lerp(dyingIntensityFraction, 1f, Mathf.Clamp01(Charge / lowBatteryFraction));

            float finalIntensity = intensity * flicker * dyingScale;
            if ((Charge < 0.05f || stuttering) && Mathf.PerlinNoise(Time.time * 3f, 7f) > 0.8f) finalIntensity = 0f;

            _light.intensity = finalIntensity;
        }
    }

    private void ApplyState()
    {
        _light.enabled = IsOn;
        _light.intensity = intensity;

        if (_stealth != null)
        {
            _stealth.FlashlightOn = IsOn;
        }
    }
}

using UnityEngine;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The player's head-mounted torch, and the risk that comes with it: while it is on the AI can see
/// the player from much further away. Created at runtime by <see cref="FirstPersonRig"/> on the camera.
///
/// F71: the single beam became two - a wide default flood and a narrow, brighter focused beam held on
/// RMB / gamepad LT - blended over <see cref="focusBlendSeconds"/> so the transition reads as a zoom,
/// not a cut. <see cref="Range"/> and <see cref="OuterAngle"/> always return the current blended value,
/// so anything testing "is X in the beam" (AIFollower's beam awareness, F73) stays correct through the
/// blend. The battery also recharges slowly while off (decision D3) instead of being a one-shot resource.
/// </summary>
public class Flashlight : MonoBehaviour
{
    [Header("Beam - wide (default)")]
    [Tooltip("Width of the lit circle when not focused. A wide floodlight rather than a torch beam.")]
    [SerializeField] private float wideOuterAngle = 60f;
    [Tooltip("Width of the bright core when not focused. The gap to wideOuterAngle is the soft edge.")]
    [SerializeField] private float wideInnerAngle = 28f;
    [SerializeField] private float wideIntensity = 9f;
    [Tooltip("How far the wide beam reaches. Keep it near the fog distance, or the cone ends in fog you cannot see through anyway.")]
    [SerializeField] private float wideRange = 26f;

    [Header("Beam - focused (RMB / gamepad LT held)")]
    [Tooltip("A narrow, punchier beam for peering down a corridor - less light spilled sideways, more thrown forward.")]
    [SerializeField] private float focusedOuterAngle = 24f;
    [SerializeField] private float focusedInnerAngle = 12f;
    [SerializeField] private float focusedIntensity = 16f;
    [SerializeField] private float focusedRange = 38f;
    [Tooltip("Seconds to blend fully in or out of the focused beam")]
    [SerializeField] private float focusBlendSeconds = 0.15f;
    [Tooltip("Battery drains this much faster while focused")]
    [SerializeField] private float focusedDrainMultiplier = 1.5f;

    [SerializeField] private Color beamColor = new Color(1f, 0.95f, 0.86f);

    [Header("Flicker")]
    [Tooltip("How much the beam wavers, 0 = steady")]
    [SerializeField] private float flickerAmount = 0.06f;
    [SerializeField] private float flickerSpeed = 7f;

    [Header("Battery")]
    [Tooltip("Total seconds of continuous ON time the torch can run before it goes dark. Not a hard campaign limit - see the recharge fields below.")]
    [SerializeField] private float batterySeconds = 180f;
    [Tooltip("Seconds the torch must stay off (or dead) before it starts recharging")]
    [SerializeField] private float rechargeDelaySeconds = 2f;
    [Tooltip("Seconds of OFF time to recover one second of charge (decision D3: 1 s gained per 3 s off)")]
    [SerializeField] private float rechargeSecondsPerCharge = 3f;
    [Tooltip("Recharge never raises the charge above this fraction of batterySeconds - only a Spare Battery tops it up the rest of the way")]
    [SerializeField] private float rechargeCapFraction = 0.6f;
    [Tooltip("Below this fraction the beam starts to stutter")]
    [SerializeField] private float lowBatteryFraction = 0.2f;
    [Tooltip("Flicker amount at empty; blends from flickerAmount as the charge falls below lowBatteryFraction")]
    [SerializeField] private float dyingFlickerAmount = 0.55f;
    [Tooltip("Beam intensity multiplier at the very end of the battery")]
    [SerializeField] private float dyingIntensityFraction = 0.55f;

    private Light _light;
    private PlayerStealthState _stealth;
    private float _stutterRemaining;
    private float _offSeconds;
    private float _rangeMultiplier = 1f;
    private float _rechargeMultiplier = 1f;
    /// <summary>F75 Focus Lens shop item: overrides focusedDrainMultiplier when &gt;= 0; -1 means "use the serialized default".</summary>
    private float _focusedDrainMultiplierOverride = -1f;

    public bool IsOn { get; private set; } = true;

    /// <summary>True while RMB / gamepad LT is held and the input gates (InputEnabled, not hidden) allow it.</summary>
    public bool IsFocused { get; private set; }

    /// <summary>0 = fully wide, 1 = fully focused. Eases over focusBlendSeconds rather than snapping.</summary>
    public float FocusBlend { get; private set; }

    /// <summary>The beam's current reach (blended wide/focused, times SetRangeMultiplier), for anything that wants to know if it just caught something.</summary>
    public float Range => CurrentRange;

    /// <summary>Full width of the current (blended) lit cone.</summary>
    public float OuterAngle => CurrentOuterAngle;

    private float CurrentOuterAngle => Mathf.Lerp(wideOuterAngle, focusedOuterAngle, FocusBlend);
    private float CurrentInnerAngle => Mathf.Lerp(wideInnerAngle, focusedInnerAngle, FocusBlend);
    private float CurrentIntensity => Mathf.Lerp(wideIntensity, focusedIntensity, FocusBlend);

    // The multiplier only scales the focused end of the blend, so the Focus Lens shop item (F75) makes
    // focusing reach further without changing the wide, default beam at all.
    private float CurrentRange => Mathf.Lerp(wideRange, focusedRange * _rangeMultiplier, FocusBlend);

    /// <summary>Ignore the toggle key while false: under the pause menu, and through an ending.</summary>
    public bool InputEnabled = true;

    /// <summary>
    /// Total seconds of battery remaining. Starts at batterySeconds; falls while the torch is on (faster
    /// while focused) and creeps back up while it is off, capped at rechargeCapFraction * batterySeconds
    /// (decision D3) - so it can rise as well as fall now.
    /// </summary>
    public float SecondsLeft { get; private set; }

    /// <summary>0..1 fraction of the battery remaining.</summary>
    public float Charge => batterySeconds > 0f ? SecondsLeft / batterySeconds : 0f;

    /// <summary>True while the battery is empty. No longer permanent for the rest of the run - it clears itself once recharge (while off) brings SecondsLeft back above zero.</summary>
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

    /// <summary>Removes battery directly (F75: the Stalker's touch drains 25%). Floors at zero rather than negative.</summary>
    public void Drain(float fraction)
    {
        SecondsLeft = Mathf.Max(0f, SecondsLeft - fraction * batterySeconds);
        if (IsDead) SetOn(false);
    }

    /// <summary>Adds battery directly (F72: a vault Battery Cell). Positive counterpart to Drain, capped at a full charge.</summary>
    public void AddCharge(float fraction)
    {
        SecondsLeft = Mathf.Min(batterySeconds, SecondsLeft + fraction * batterySeconds);
    }

    /// <summary>Focus Lens shop hook (F75): scales the focused beam's range. Wide range is untouched. Default 1.</summary>
    public void SetRangeMultiplier(float multiplier)
    {
        _rangeMultiplier = Mathf.Max(0.01f, multiplier);
    }

    /// <summary>Focus Lens shop hook (F75): overrides how much faster the battery drains while focused - the Lens sets this to 1 (no penalty at all) instead of the default 1.5x. Pass a negative value to go back to the serialized default.</summary>
    public void SetFocusedDrainMultiplier(float multiplier)
    {
        _focusedDrainMultiplierOverride = multiplier;
    }

    /// <summary>F74 WeakBattery run rule: sets the starting charge as a fraction of a full battery. Called once, right after Awake, before the player has had a chance to drain anything.</summary>
    public void SetStartFraction(float fraction)
    {
        SecondsLeft = Mathf.Clamp01(fraction) * batterySeconds;
    }

    /// <summary>F74 WeakBattery run rule: scales the off-time recharge rate (decision D3's 1 s per 3 s off). Default 1.</summary>
    public void SetRechargeMultiplier(float multiplier)
    {
        _rechargeMultiplier = Mathf.Max(0.01f, multiplier);
    }

    /// <summary>F76 MobilePerformance (T9): drops the shadow resolution tier on mobile web. Never called
    /// in the Editor - see MobilePerformance's own player-build-only guard.</summary>
    public void SetShadowTier(int tier)
    {
        if (_light == null) return;
        UniversalAdditionalLightData data = _light.GetUniversalAdditionalLightData();
        if (data != null) data.additionalLightsShadowResolutionTier = tier;
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

        bool hiddenGate = _stealth == null || !_stealth.Hidden;

#if ENABLE_INPUT_SYSTEM
        // Read the keys directly: the Player action map has no spare action, and rewiring PlayerInput
        // for these is more risk than it is worth. Keyboard/Mouse/Gamepad.current are null with no
        // matching device present. Hidden also blocks both: a locker forces InputEnabled off, but
        // PauseMenu.Resume sets it back on, and this is the cheap second guard against light leaking
        // out of a closed locker.
        if (InputEnabled && !IsDead && hiddenGate &&
            ((Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) || TouchInput.Pressed(TouchInput.TouchAction.Torch)))
        {
            SetOn(!IsOn);
        }

        IsFocused = InputEnabled && hiddenGate &&
            ((Mouse.current != null && Mouse.current.rightButton.isPressed) ||
             (Gamepad.current != null && Gamepad.current.leftTrigger.isPressed) ||
             TouchInput.Held(TouchInput.TouchAction.Focus));
#else
        IsFocused = false;
#endif

        // Scaled deltaTime, not unscaled: the blend freezes for free behind the title screen and under
        // pause, the same as the drain below.
        float focusTarget = IsFocused ? 1f : 0f;
        FocusBlend = focusBlendSeconds > 0f
            ? Mathf.MoveTowards(FocusBlend, focusTarget, Time.deltaTime / focusBlendSeconds)
            : focusTarget;

        // Scaled deltaTime: the drain (and the recharge below) freeze for free behind the title screen
        // and under pause.
        if (IsOn)
        {
            float drainRate = IsFocused ? (_focusedDrainMultiplierOverride >= 0f ? _focusedDrainMultiplierOverride : focusedDrainMultiplier) : 1f;
            SecondsLeft = Mathf.Max(0f, SecondsLeft - Time.deltaTime * drainRate);
            _offSeconds = 0f;
            if (IsDead) SetOn(false);
        }
        else
        {
            _offSeconds += Time.deltaTime;

            // Decision D3: 1 s of charge per 3 s off, after a 2 s grace so a quick toggle-off-on cannot
            // nudge the bar, capped well below full so a Spare Battery still matters. Blocked once the
            // run is over - the endings drive the torch themselves (CaptureSequence's flicker beats, the
            // drop-through blackout) and should not fight a trickle charge underneath them.
            if (!GameOutcome.IsOver && _offSeconds > rechargeDelaySeconds)
            {
                float cap = batterySeconds * rechargeCapFraction;
                if (SecondsLeft < cap)
                {
                    SecondsLeft = Mathf.Min(cap, SecondsLeft + Time.deltaTime * _rechargeMultiplier / rechargeSecondsPerCharge);
                }
            }
        }

        if (IsOn)
        {
            bool stuttering = _stutterRemaining > 0f;
            float amount = stuttering ? 0.75f
                : IsLow ? Mathf.Lerp(dyingFlickerAmount, flickerAmount, Charge / lowBatteryFraction)
                : flickerAmount;
            float flicker = amount > 0f ? 1f - amount + amount * Mathf.PerlinNoise(Time.time * flickerSpeed, 0f) : 1f;
            float dyingScale = Mathf.Lerp(dyingIntensityFraction, 1f, Mathf.Clamp01(Charge / lowBatteryFraction));

            float finalIntensity = CurrentIntensity * flicker * dyingScale;
            if ((Charge < 0.05f || stuttering) && Mathf.PerlinNoise(Time.time * 3f, 7f) > 0.8f) finalIntensity = 0f;

            _light.intensity = finalIntensity;
        }

        // The beam shape follows the wide/focused blend even while off, so switching on mid-blend never
        // pops to the wrong shape.
        _light.spotAngle = CurrentOuterAngle;
        _light.innerSpotAngle = CurrentInnerAngle;
        _light.range = CurrentRange;

        // Written every frame regardless of IsOn - PlayerStealthState.VisibilityMultiplier only reads
        // this while FlashlightOn is true, so there is no harm in it being current whenever that flips.
        if (_stealth != null) _stealth.TorchVisibility = Mathf.Lerp(0.85f, 1.25f, FocusBlend);
    }

    private void ApplyState()
    {
        _light.enabled = IsOn;
        _light.intensity = CurrentIntensity;
        _light.spotAngle = CurrentOuterAngle;
        _light.innerSpotAngle = CurrentInnerAngle;
        _light.range = CurrentRange;

        if (_stealth != null)
        {
            _stealth.FlashlightOn = IsOn;
        }
    }
}

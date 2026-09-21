using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How loud and how visible the player currently is.
///
/// This is the only thing the flashlight and the AI both touch. Without it the AI falls back to its
/// old behaviour (always fully visible, never heard), so nothing here is load-bearing for the
/// non-maze scene.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerStealthState : MonoBehaviour
{
    [Header("Noise")]
    [Tooltip("Speed above which the player counts as sprinting (m/s)")]
    [SerializeField] private float sprintSpeedThreshold = 4.5f;
    [Tooltip("Speed above which the player counts as moving at all (m/s)")]
    [SerializeField] private float walkSpeedThreshold = 0.5f;
    [Tooltip("How far a sprinting player can be heard (m)")]
    [SerializeField] private float sprintNoiseRadius = 15f;
    [Tooltip("How far a walking player can be heard (m)")]
    [SerializeField] private float walkNoiseRadius = 5f;
    [Tooltip("How fast the noise radius follows the player's speed")]
    [SerializeField] private float noiseSmoothing = 8f;

    [Header("Visibility")]
    [Tooltip("How visible the player is with the torch off, relative to on")]
    [SerializeField] private float darkVisibility = 0.35f;
    [Tooltip("Extra visibility when standing in a wall lamp's light with the torch off, at the lamp itself")]
    [SerializeField] private float lampVisibilityBonus = 0.45f;

    private CharacterController _controller;
    private IReadOnlyList<WallLamp> _lamps = System.Array.Empty<WallLamp>();

    /// <summary>How far away the player can currently be heard, in metres. 0 when standing still.</summary>
    public float NoiseRadius { get; private set; }

    /// <summary>Set by the flashlight so the audio director can react without knowing about it.</summary>
    public bool FlashlightOn { get; set; } = true;

    /// <summary>True while hidden inside a locker.</summary>
    public bool Hidden { get; private set; }

    /// <summary>The locker while hidden, otherwise null.</summary>
    public IHidingSpot CurrentHidingSpot { get; private set; }

    /// <summary>0..1, how strongly a nearby wall lamp is lighting the player up.</summary>
    public float LampExposure { get; private set; }

    /// <summary>
    /// Set once by Locker.Expose and never cleared during the run - GameOutcome reads it after
    /// SetHidden(null) has already cleared Hidden/CurrentHidingSpot.
    /// </summary>
    public bool LastExposed { get; set; }

    /// <summary>Scales how far the AI can see the player. 1 = fully lit, lower = harder to spot.</summary>
    public float VisibilityMultiplier =>
        Hidden ? 0f : FlashlightOn ? 1f : Mathf.Min(1f, darkVisibility + lampVisibilityBonus * LampExposure);

    /// <summary>0 when standing still, 1 when sprinting. Drives footstep volume.</summary>
    public float NoiseFraction => sprintNoiseRadius > 0f ? Mathf.Clamp01(NoiseRadius / sprintNoiseRadius) : 0f;

    /// <summary>Called by Locker on Enter/Leave. spot == null clears the hidden state.</summary>
    public void SetHidden(IHidingSpot spot)
    {
        Hidden = spot != null;
        CurrentHidingSpot = spot;
    }

    /// <summary>Handed by MazeGenerator once every lamp exists. May be empty.</summary>
    public void SetLamps(IReadOnlyList<WallLamp> lamps)
    {
        _lamps = lamps ?? System.Array.Empty<WallLamp>();
    }

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    private void Update()
    {
        // Velocity rather than the input flags: it is the truth, and it correctly drops to zero when
        // the player is holding sprint against a wall (or standing still inside a locker).
        Vector3 velocity = _controller.velocity;
        velocity.y = 0f;
        float speed = velocity.magnitude;

        float target;
        if (speed > sprintSpeedThreshold) target = sprintNoiseRadius;
        else if (speed > walkSpeedThreshold) target = walkNoiseRadius;
        else target = 0f;

        NoiseRadius = Mathf.MoveTowards(NoiseRadius, target, sprintNoiseRadius * noiseSmoothing * Time.deltaTime);

        LampExposure = WallLamp.ExposureAt(_lamps, transform.position + Vector3.up);
    }
}

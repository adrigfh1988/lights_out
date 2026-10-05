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

    /// <summary>How far away the player can currently be heard, in metres. 0 when standing still.</summary>
    public float NoiseRadius { get; private set; }

    /// <summary>Multiplies the noise radius. 1 = normal. Set once per floor from FloorProfile.NoiseScale (Quiet Shoes).</summary>
    public float NoiseScale { get; set; } = 1f;

    /// <summary>Set by the flashlight so the audio director can react without knowing about it.</summary>
    public bool FlashlightOn { get; set; } = true;

    /// <summary>
    /// How visible the torch itself makes the player, written every frame by Flashlight from its
    /// wide/focused blend (F71): a wide flood is a little less telling than the old flat 1, a focused
    /// beam considerably more, since it is a bright, narrow, unmistakable shaft. Only read while
    /// FlashlightOn is true - see VisibilityMultiplier below.
    /// </summary>
    public float TorchVisibility { get; set; } = 1f;

    /// <summary>
    /// F74: a floor set on the player from outside (currently only Lantern, while carried - 0.8, per
    /// the slice spec) rather than computed here. Folded into VisibilityMultiplier as a lower bound so
    /// it can only make the player more visible, never override Hidden.
    /// </summary>
    public float MinVisibility { get; set; }

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
        Hidden ? 0f : Mathf.Max(MinVisibility, FlashlightOn ? TorchVisibility : Mathf.Min(1f, darkVisibility + lampVisibilityBonus * LampExposure));

    /// <summary>0 when standing still, 1 when sprinting. Drives footstep volume.</summary>
    public float NoiseFraction => sprintNoiseRadius > 0f ? Mathf.Clamp01(NoiseRadius / sprintNoiseRadius) : 0f;

    // F74: every NoisySurface (glass/puddle) the player currently overlaps, ref-counted so two
    // overlapping patches never stack multiplicatively - the loudest active one simply wins. Walk and
    // sprint multipliers are tracked separately since glass/puddle scale them by different amounts.
    private readonly List<(float walk, float sprint)> _activeSurfaces = new List<(float, float)>();

    /// <summary>The multiplier actually in effect for the player's current motion state (walk or sprint), recomputed every Update. 1 while off any noisy surface.</summary>
    public float SurfaceNoiseMultiplier { get; private set; } = 1f;

    /// <summary>Called by NoisySurface.OnTriggerEnter.</summary>
    public void EnterSurface(float walkMultiplier, float sprintMultiplier)
    {
        _activeSurfaces.Add((walkMultiplier, sprintMultiplier));
    }

    /// <summary>Called by NoisySurface.OnTriggerExit/OnDestroy. A no-op if the pair was never entered (defensive).</summary>
    public void ExitSurface(float walkMultiplier, float sprintMultiplier)
    {
        for (int i = 0; i < _activeSurfaces.Count; i++)
        {
            if (!Mathf.Approximately(_activeSurfaces[i].walk, walkMultiplier) || !Mathf.Approximately(_activeSurfaces[i].sprint, sprintMultiplier)) continue;
            _activeSurfaces.RemoveAt(i);
            break;
        }
    }

    /// <summary>Called by Locker on Enter/Leave. spot == null clears the hidden state.</summary>
    public void SetHidden(IHidingSpot spot)
    {
        Hidden = spot != null;
        CurrentHidingSpot = spot;
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

        float walkSurface = 1f, sprintSurface = 1f;
        for (int i = 0; i < _activeSurfaces.Count; i++)
        {
            walkSurface = Mathf.Max(walkSurface, _activeSurfaces[i].walk);
            sprintSurface = Mathf.Max(sprintSurface, _activeSurfaces[i].sprint);
        }

        float target;
        if (speed > sprintSpeedThreshold) { target = sprintNoiseRadius * sprintSurface; SurfaceNoiseMultiplier = sprintSurface; }
        else if (speed > walkSpeedThreshold) { target = walkNoiseRadius * walkSurface; SurfaceNoiseMultiplier = walkSurface; }
        else { target = 0f; SurfaceNoiseMultiplier = 1f; }
        target *= NoiseScale;

        NoiseRadius = Mathf.MoveTowards(NoiseRadius, target, sprintNoiseRadius * noiseSmoothing * Time.deltaTime);

        // F73: LightPool covers every wall lamp (and, from F74, candles/lanterns) without MazeGenerator
        // handing this component its lamp list directly.
        LampExposure = LightPool.ExposureAt(transform.position + Vector3.up);
    }
}

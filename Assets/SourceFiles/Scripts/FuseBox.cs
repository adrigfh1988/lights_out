using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Blackout floor's objective (F73): hold E for holdSeconds to restore power to this box's sector,
/// bringing its lamps back on in a ripple timed by each one's BFS step distance from here. Cloned from
/// InteractableKit.fuseBox by MazeGenerator.BuildFuses. Carries an ObjectiveToken so GameManager counts
/// it alongside the floor's stars - see CLAUDE.md's Pickup-counting gotcha (this is deliberately not a
/// Pickup either, for the same reason).
/// </summary>
public class FuseBox : MonoBehaviour, IInteractable
{
    [Header("Template wiring (InteractableKit.fuseBox)")]
    [Tooltip("Recolours red -> green on completion. Either may be left unassigned.")]
    [SerializeField] private Renderer indicatorRenderer;
    [SerializeField] private Light indicatorLight;

    [SerializeField] private float holdSeconds = 2f;
    [Tooltip("How far restoring power hums, so the hunter can hear it (m)")]
    [SerializeField] private float noiseRadius = 30f;
    [Tooltip("Seconds of ripple delay per BFS step from this box to a lamp in its sector")]
    [SerializeField] private float rippleSecondsPerStep = 0.12f;

    private static readonly Color LockedColor = new Color(0.9f, 0.12f, 0.08f);
    private static readonly Color DoneColor = new Color(0.15f, 0.9f, 0.35f);

    private readonly List<WallLamp> _sectorLamps = new List<WallLamp>();
    private readonly List<int> _sectorSteps = new List<int>();
    private AIFollower _hunter;
    private ObjectiveToken _token;
    private AudioSource _audio;
    private AudioClip _clunkClip;
    private AudioClip _humClip;
    private bool _done;
    private Beacon _beacon;

    /// <summary>F75 Fuse Map: whether this box has already been restored - the Star Compass skips a finished one.</summary>
    public bool IsDone => _done;

    /// <summary>F75 Fuse Map shop item: a slow-pulsing 3 m beacon so this box is findable across the room.</summary>
    public void EnableBeacon()
    {
        if (_beacon == null) _beacon = Beacon.Attach(transform, Vector3.up * 0.4f);
    }

    /// <summary>
    /// Called right after Instantiate by MazeGenerator.BuildFuses. `sectorSteps[i]` is lamp i's BFS step
    /// distance from this box - the ripple delay is steps * rippleSecondsPerStep, applied through
    /// WallLamp.SetPowered's own delay rather than a coroutine here.
    /// </summary>
    public void Configure(List<WallLamp> sectorLamps, List<int> sectorSteps, AIFollower hunter)
    {
        _sectorLamps.Clear();
        if (sectorLamps != null) _sectorLamps.AddRange(sectorLamps);
        _sectorSteps.Clear();
        if (sectorSteps != null) _sectorSteps.AddRange(sectorSteps);
        _hunter = hunter;

        _token = gameObject.AddComponent<ObjectiveToken>();

        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 1f;
        _audio.maxDistance = 20f;
        _clunkClip = DoorAudio.BuildClunkClip(0.3f, 70f, 0.4f);
        _humClip = DoorAudio.BuildClunkClip(0.6f, 220f, 0.1f);
    }

    string IInteractable.Prompt => "RESTORE POWER";
    float IInteractable.HoldSeconds => holdSeconds;
    bool IInteractable.CanInteract => !_done;

    void IInteractable.Interact(PlayerInteractor who)
    {
        if (_done) return;
        _done = true;

        for (int i = 0; i < _sectorLamps.Count; i++)
        {
            WallLamp lamp = _sectorLamps[i];
            if (lamp == null) continue;
            int steps = i < _sectorSteps.Count ? _sectorSteps[i] : 0;
            lamp.SetPowered(true, steps * rippleSecondsPerStep);
        }

        if (_audio != null)
        {
            if (_clunkClip != null) _audio.PlayOneShot(_clunkClip, 0.8f);
            if (_humClip != null) _audio.PlayOneShot(_humClip, 0.5f);
        }

        _hunter?.HearNoise(transform.position, noiseRadius);
        _token?.Complete();
    }

    void IInteractable.OnFocus(bool focused) { }

    private void Update()
    {
        if (indicatorRenderer == null && indicatorLight == null) return;

        Color c = _done ? DoneColor : LockedColor;
        if (indicatorRenderer != null && indicatorRenderer.material.HasProperty("_EmissionColor"))
        {
            indicatorRenderer.material.SetColor("_EmissionColor", c * 2f);
        }
        if (indicatorLight != null) indicatorLight.color = c;
    }
}

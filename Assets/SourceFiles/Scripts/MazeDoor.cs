using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A rolling shutter across a maze passage (F72). Cloned from InteractableKit.shutterDoor by
/// MazeGenerator.BuildDoors, post-bake, with a plain collider and deliberately no NavMeshObstacle
/// (decision D5) - the navmesh was already baked by the time doors exist, and a shutter can be any
/// width the corridor needs. A NavMeshAgent has no physics response to a plain collider, so the hunter
/// would otherwise walk straight through a closed one; instead this component watches for it and slams
/// the door open itself (see CheckHunterBash), which is the only thing AIFollower had to grow a hook
/// for (Stall).
///
/// Three kinds (decision D6): Manual is a free E toggle on either side. Security is locked until its
/// WallButton calls Unlock(), but can always be opened with E from the inside so a maze can never trap
/// the player (decision: "never trap the player"). Timed behaves like Security except Unlock() only
/// holds it open for a while before it tries to re-lock itself.
/// </summary>
public class MazeDoor : MonoBehaviour, IInteractable
{
    public enum Kind { Manual, Security, Timed }

    [Header("Template wiring (InteractableKit.shutterDoor)")]
    [Tooltip("Slides up to open. Authored as a 1x1 unit panel at local position/scale (0,0,0)/(1,1,depth) - Configure scales X to the opening width and Y to the door height, then keeps it floor-anchored.")]
    [SerializeField] private Transform panel;
    [Tooltip("Recolours to say locked/timed/open/manual. Either may be left unassigned.")]
    [SerializeField] private Renderer statusLightRenderer;
    [SerializeField] private Light statusLight;

    [Header("Timing")]
    [SerializeField] private float openSeconds = 0.6f;
    [SerializeField] private float closeSeconds = 0.8f;
    [Tooltip("How fast a hunter bash throws the door open")]
    [SerializeField] private float bashOpenSeconds = 0.15f;
    [Tooltip("How long a bashed-open door stays open before trying to revert")]
    [SerializeField] private float bashHoldSeconds = 4f;
    [SerializeField] private float bashStallSeconds = 1.2f;
    [Tooltip("Flat distance at which an oncoming hunter counts as bashing the door")]
    [SerializeField] private float bashRange = 1.6f;
    [Tooltip("Seconds a Timed door stays unlocked once its button is pressed")]
    [SerializeField] private float timedOpenSeconds = 25f;
    [Tooltip("Flat distance at which the player/hunter standing in the doorway blocks a close")]
    [SerializeField] private float doorwayClearRadius = 1.3f;

    private static readonly Color LockedColor = new Color(0.9f, 0.12f, 0.08f);
    private static readonly Color TimedColor = new Color(0.95f, 0.65f, 0.1f);
    private static readonly Color OpenColor = new Color(0.15f, 0.9f, 0.35f);
    private static readonly Color ManualColor = Color.white;

    private Kind _kind;
    private float _doorHeight;
    private float _baseY;
    private AIFollower _hunter;
    private Transform _player;

    private Vector3 _doorCenter;
    // World direction from the door toward the "inside" (far, non-start) side. Only meaningful for
    // Security/Timed - Manual doors are interactable from either side, so this is never read for them.
    private Vector3 _insideDirection = Vector3.forward;

    private float _panelOffset;    // 0 (closed) .. _doorHeight (fully open), on top of _baseY
    private bool _open;
    // Set once and never cleared: AllStarsCollected (any Security/Timed) or the player opening a Manual
    // door (which then simply stays open until they choose to close it again).
    private bool _permanentlyOpen;
    private bool _unlocked;
    private float _timedCloseAt = -1f;
    private float _bashHoldUntil = -1f;
    private Coroutine _slideRoutine;

    private BoxCollider _blocker;      // solid, disabled while open - physically stops the player
    private BoxCollider _interactZone; // trigger, always on - lets PlayerInteractor find the door even open

    private AudioSource _audio;
    private AudioClip _clunkClip;
    private AudioClip _slamClip;

    /// <summary>True while the button that unlocks a Timed door may be pressed again (closed and not permanently opened).</summary>
    public bool CanBePressedAgain => _kind == Kind.Timed && !_permanentlyOpen && !_open;

    /// <summary>F75 Fuse Map shop item: true for a vault's security door(s), set by MazeGenerator.PlaceVaults.</summary>
    public bool IsVaultDoor { get; set; }

    private Beacon _beacon;

    /// <summary>F75 Fuse Map shop item: a slow-pulsing 3 m beacon above the door.</summary>
    public void EnableBeacon()
    {
        if (_beacon == null) _beacon = Beacon.Attach(transform, Vector3.up * (_doorHeight + 0.3f));
    }

    /// <summary>Called right after Instantiate by MazeGenerator.BuildDoors.</summary>
    public void Configure(Kind kind, float width, float height, Vector3 doorCenter, Vector3 insideDirection,
        AIFollower hunter, Transform player)
    {
        _kind = kind;
        _doorHeight = height;
        _baseY = height * 0.5f;
        _doorCenter = doorCenter;
        _insideDirection = insideDirection.sqrMagnitude > 0.0001f ? insideDirection.normalized : Vector3.forward;
        _hunter = hunter;
        _player = player;

        if (panel != null) panel.localScale = new Vector3(width, height, panel.localScale.z);

        _blocker = gameObject.AddComponent<BoxCollider>();
        _blocker.size = new Vector3(width, height, 0.3f);
        _blocker.center = new Vector3(0f, _baseY, 0f);

        _interactZone = gameObject.AddComponent<BoxCollider>();
        _interactZone.isTrigger = true;
        _interactZone.size = new Vector3(width, height, 0.6f);
        _interactZone.center = new Vector3(0f, _baseY, 0f);

        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 1f;
        _audio.maxDistance = 18f;
        _clunkClip = DoorAudio.BuildClunkClip(0.28f, 90f, 0.5f);
        _slamClip = DoorAudio.BuildClunkClip(0.4f, 55f, 0.9f);

        SetClosedImmediate();
    }

    /// <summary>BuildDoors converts the farthest security door on a Lockdown floor (HasTimedDoor) into a Timed one.</summary>
    public void ConvertToTimed()
    {
        _kind = Kind.Timed;
    }

    private void OnEnable() => GameManager.AllStarsCollected += HandleAllStarsCollected;
    private void OnDisable() => GameManager.AllStarsCollected -= HandleAllStarsCollected;

    /// <summary>Decision D6: every Security/Timed door opens for good once the floor's stars are all in, so the hatch cell can never be sealed off.</summary>
    private void HandleAllStarsCollected()
    {
        if (_kind == Kind.Manual) return;
        _permanentlyOpen = true;
        _unlocked = true;
        if (!_open) StartSlide(true, openSeconds);
    }

    // ---------------------------------------------------------------- IInteractable

    string IInteractable.Prompt => _kind == Kind.Manual ? (_open ? "CLOSE" : "OPEN") : "OPEN";
    float IInteractable.HoldSeconds => 0f;

    bool IInteractable.CanInteract
    {
        get
        {
            if (_kind == Kind.Manual) return true;
            // Never trap the player: Security/Timed can always be opened with E from the inside.
            if (_permanentlyOpen || _open) return false;
            return IsPlayerInside();
        }
    }

    void IInteractable.Interact(PlayerInteractor who)
    {
        if (_kind == Kind.Manual)
        {
            if (_open)
            {
                if (!DoorwayOccupied())
                {
                    // Cleared on close, or Update's CheckBash gate (!_open && !_permanentlyOpen)
                    // stays shut forever and the hunter walks through a door the player closed.
                    _permanentlyOpen = false;
                    StartSlide(false, closeSeconds);
                }
            }
            else
            {
                _permanentlyOpen = true; // stays open until the player closes it again
                StartSlide(true, openSeconds);
            }
            return;
        }

        // Opened by hand from the inside - same "stays open" rule as a manual door from here on.
        _unlocked = true;
        _permanentlyOpen = true;
        StartSlide(true, openSeconds);
    }

    void IInteractable.OnFocus(bool focused) { }

    private bool IsPlayerInside()
    {
        if (_player == null) return false;
        Vector3 flat = _player.position - _doorCenter;
        flat.y = 0f;
        return Vector3.Dot(flat, _insideDirection) > 0f;
    }

    private bool DoorwayOccupied()
    {
        if (_player == null) return false;
        Vector3 flat = _player.position - _doorCenter;
        flat.y = 0f;
        return flat.magnitude < doorwayClearRadius;
    }

    /// <summary>
    /// True while any door-breaker (the hunter, the Stalker) stands in the doorway. Reads the DoorBreakers
    /// registry rather than just the hunter, or a shutter would roll down on a Stalker frozen in the gap.
    /// </summary>
    private bool BreakerInDoorway()
    {
        IReadOnlyList<DoorBreakers.Entry> breakers = DoorBreakers.All;
        for (int i = 0; i < breakers.Count; i++)
        {
            if (breakers[i].Actor == null) continue;
            Vector3 flat = breakers[i].Actor.position - _doorCenter;
            flat.y = 0f;
            if (flat.magnitude < doorwayClearRadius) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- WallButton hook

    /// <summary>A second door Unlock() also opens - the two-opening vault fallback in MazeGenerator.PlaceVaults. Null for every other door.</summary>
    public MazeDoor LinkedDoor { get; set; }

    /// <summary>Called by WallButton.Interact. Security opens for good; Timed opens for timedOpenSeconds and can be re-armed once it closes.</summary>
    public void Unlock()
    {
        // One-way on purpose (never set both doors as each other's link): a braided-maze vault has two
        // openings and one button, and this chain is how the second shutter hears about it.
        if (LinkedDoor != null && LinkedDoor != this) LinkedDoor.Unlock();

        if (_permanentlyOpen) return;
        _unlocked = true;

        if (_kind == Kind.Timed)
        {
            _timedCloseAt = Time.time + timedOpenSeconds;
            if (!_open) StartSlide(true, openSeconds);
        }
        else
        {
            _permanentlyOpen = true;
            if (!_open) StartSlide(true, openSeconds);
        }
    }

    // ---------------------------------------------------------------- per-frame

    private void Update()
    {
        UpdateStatusLight();

        if (_kind == Kind.Timed && _unlocked && !_permanentlyOpen && _open && Time.time >= _timedCloseAt)
        {
            // "Retry each 0.5s" (decision D6) - simplest correct version is just trying every frame and
            // doing nothing while the doorway is occupied; Time.deltaTime being near-zero costs nothing.
            if (!DoorwayOccupied() && !BreakerInDoorway())
            {
                _unlocked = false;
                StartSlide(false, closeSeconds);
            }
        }

        if (_bashHoldUntil > 0f && Time.time >= _bashHoldUntil)
        {
            if (_kind == Kind.Manual || _permanentlyOpen)
            {
                _bashHoldUntil = -1f;
            }
            else if (DoorwayOccupied() || BreakerInDoorway())
            {
                // Someone is still in the gap: try again shortly instead of giving up, or a bashed
                // Security door would stay open (but still "locked") for the rest of the floor.
                _bashHoldUntil = Time.time + 0.5f;
            }
            else
            {
                _bashHoldUntil = -1f;
                if (_kind == Kind.Security) _unlocked = false;
                if (_open) StartSlide(false, closeSeconds);
            }
        }

        if (!_open && !_permanentlyOpen) CheckBash();
    }

    /// <summary>
    /// Decision D5, generalised by F75's DoorBreakers registry: any registered breaker within range,
    /// heading through the door, bashes it open. A plain NavMeshAgent has no physics response to a
    /// closed collider, so without this it would silently clip through instead of being stopped by the
    /// door the way the player is. The hunter (loud, Stall) and the Stalker (silent, no stall) share
    /// this same check - MazeDoor itself does not know or care which is which beyond Entry.Silent.
    /// </summary>
    private void CheckBash()
    {
        IReadOnlyList<DoorBreakers.Entry> breakers = DoorBreakers.All;
        for (int i = 0; i < breakers.Count; i++)
        {
            DoorBreakers.Entry entry = breakers[i];
            if (entry.Actor == null) continue;

            Vector3 flat = entry.Actor.position - _doorCenter;
            flat.y = 0f;
            if (flat.magnitude >= bashRange) continue;

            // A hidden Stalker unregisters itself for the duration (see Stalker.HideAndRespawn), but
            // this guard is cheap insurance against any future breaker with a disabled/off-mesh agent -
            // NavMeshAgent.velocity/hasPath/steeringTarget all log on one of those.
            NavMeshAgent agent = entry.Agent;
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) continue;

            Vector3 heading = agent.velocity.sqrMagnitude > 0.01f
                ? agent.velocity
                : agent.hasPath ? agent.steeringTarget - entry.Actor.position : Vector3.zero;
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.0001f) continue;

            // Either direction through the door counts - the actor can be heading through from the
            // inside just as well as the outside.
            if (Mathf.Abs(Vector3.Dot(heading.normalized, _insideDirection)) < 0.3f) continue;

            Bash(entry);
            return; // one bash per frame is enough
        }
    }

    private void Bash(DoorBreakers.Entry entry)
    {
        _bashHoldUntil = Time.time + bashHoldSeconds;
        StartSlide(true, bashOpenSeconds, entry.Silent);
        if (!entry.Silent)
        {
            PlayClip(_slamClip, 1f);
            entry.Stall?.Invoke(bashStallSeconds);
        }
    }

    // ---------------------------------------------------------------- sliding + collider + status light

    private void StartSlide(bool opening, float duration, bool silent = false)
    {
        if (_slideRoutine != null) StopCoroutine(_slideRoutine);
        // _open is the door's intent and flips the moment a slide starts, not when it finishes. Every
        // decision reads it, so a stale value mid-slide was two bugs: CheckBash re-bashed a door that
        // was already rolling up every frame (restarting this slide and re-stalling the hunter forever,
        // stuck in front of a shutter that looked open), and Unlock/AllStarsCollected arriving while a
        // bashed door rolled back down saw "open", did nothing, and left it shut but marked open for good.
        _open = opening;
        _slideRoutine = StartCoroutine(Slide(opening, duration));
        // F75: the Stalker slips under a door silently - no clunk, no slam (see Bash).
        if (!silent) PlayClip(_clunkClip, 0.7f);
    }

    private IEnumerator Slide(bool opening, float duration)
    {
        float startOffset = _panelOffset;
        float endOffset = opening ? _doorHeight : 0f;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            _panelOffset = duration > 0f ? Mathf.Lerp(startOffset, endOffset, Mathf.Clamp01(t / duration)) : endOffset;
            ApplyPanelOffset();
            yield return null;
        }

        _panelOffset = endOffset;
        ApplyPanelOffset();
        _slideRoutine = null;
    }

    private void ApplyPanelOffset()
    {
        if (panel != null) panel.localPosition = new Vector3(0f, _baseY + _panelOffset, 0f);
        // "collider disabled once the panel clears 2 m" (decision D7) - the player can walk through
        // well before the panel finishes sliding all the way into its cavity.
        if (_blocker != null) _blocker.enabled = _panelOffset < 2f;
    }

    private void SetClosedImmediate()
    {
        _panelOffset = 0f;
        ApplyPanelOffset();
        _open = false;
    }

    private void UpdateStatusLight()
    {
        Color c = _kind == Kind.Manual ? ManualColor
            : _open || _permanentlyOpen ? OpenColor
            : _kind == Kind.Timed && _unlocked ? TimedColor
            : LockedColor;

        if (statusLightRenderer != null && statusLightRenderer.material.HasProperty("_EmissionColor"))
        {
            statusLightRenderer.material.SetColor("_EmissionColor", c * 2f);
        }
        if (statusLight != null) statusLight.color = c;
    }

    private void PlayClip(AudioClip clip, float volume)
    {
        if (_audio != null && clip != null) _audio.PlayOneShot(clip, volume);
    }
}

using System;
using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.AI;
// System.Random would otherwise collide with UnityEngine.Random throughout this file
using Random = UnityEngine.Random;

[RequireComponent(typeof(NavMeshAgent))]
public class AIFollower : MonoBehaviour
{
    [SerializeField] private Transform target;

    [Header("Movement")]
    [Tooltip("Speed when close to the target (m/s)")]
    [SerializeField] private float moveSpeed = 3.5f;
    [Tooltip("Speed when far from the target (m/s). Clamped in Start to the player's sprint speed x maxSpeedFraction - it must never be able to out-sprint the player.")]
    [SerializeField] private float runSpeed = 4.5f;
    [Tooltip("Distance at which the follower switches from moveSpeed to runSpeed")]
    [SerializeField] private float runDistance = 4f;
    [Tooltip("The agent decelerates to a stop this far from the target. Kept inside captureRadius in TickChase, or it would park out of reach.")]
    [SerializeField] private float stoppingDistance = 0.9f;
    [SerializeField] private float acceleration = 14f;

    [Header("Following")]
    [Tooltip("How often the destination is refreshed (seconds)")]
    [SerializeField] private float repathInterval = 0.1f;
    [Tooltip("How far from the target to search for the closest walkable point (e.g. when the player is jumping)")]
    [SerializeField] private float navMeshSearchRadius = 6f;
    [Tooltip("If the follower falls farther than this from the target it is teleported next to it. Only used without wander points.")]
    [SerializeField] private float teleportDistance = 40f;

    [Header("Capture")]
    [Tooltip("Flat distance to the player's centre at which it has you. Must be larger than the chase stopping distance or it never fires.")]
    [SerializeField] private float captureRadius = 1.4f;

    [Header("Perception")]
    [Tooltip("How far the follower can see a fully lit player (m). Scaled down when their flashlight is off.")]
    [SerializeField] private float viewDistance = 18f;
    [Tooltip("Total width of the field of view (degrees)")]
    [SerializeField] private float viewAngle = 110f;
    [Tooltip("Height of the eyes above the follower's feet")]
    [SerializeField] private float eyeHeight = 1.5f;
    [Tooltip("Height of the point on the target that has to be visible")]
    [SerializeField] private float targetHeight = 1f;
    [Tooltip("How long the follower keeps chasing after losing sight (seconds)")]
    [SerializeField] private float loseSightTime = 3f;
    [Tooltip("Seconds between two line-of-sight tests")]
    [SerializeField] private float sightCheckInterval = 0.1f;

    [Header("Escalation")]
    [Tooltip("Speed before a single star has been taken. Low enough to read as barely moving at all.")]
    [SerializeField] private float dormantSpeed = 0.7f;

    [Header("Hearing")]
    [Tooltip("Seconds between hearing checks")]
    [SerializeField] private float hearingInterval = 0.3f;

    [Header("Wander")]
    [Tooltip("How close the follower has to get to a wander point before picking the next one")]
    [SerializeField] private float wanderStoppingDistance = 0.3f;
    [Tooltip("Chance of heading for a cell that still holds a star instead of anywhere. A hard preference would make it camp them.")]
    [Range(0f, 1f)]
    [SerializeField] private float patrolBias = 0.6f;

    [Header("Search")]
    [Tooltip("How far around the last known position to sweep (m)")]
    [SerializeField] private float searchRadius = 7.5f;
    [Tooltip("How many spots to check before giving up")]
    [SerializeField] private int searchPoints = 4;
    [Tooltip("Seconds spent looking around at each spot")]
    [SerializeField] private float searchDwellTime = 1f;
    [Tooltip("Hard limit on a single search, so repeated noises cannot trap it in a loop")]
    [SerializeField] private float searchBudget = 12f;

    [Header("Speed cap")]
    [Tooltip("Every speed is clamped to the player's sprint speed times this. It must never be able to out-sprint the player.")]
    [Range(0.5f, 0.99f)]
    [SerializeField] private float maxSpeedFraction = 0.86f;

    [Header("Hiding spots")]
    [Tooltip("How close a hiding spot has to be to the last known position to be checked during a search")]
    [SerializeField] private float hidingSpotSearchRadius = 6f;

    [Header("Rotation")]
    [SerializeField] private float turnSpeed = 540f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float animationBlendRate = 10f;

    private static readonly int AnimSpeed = Animator.StringToHash("Speed");
    private static readonly int AnimGrounded = Animator.StringToHash("Grounded");
    private static readonly int AnimMotionSpeed = Animator.StringToHash("MotionSpeed");

    private enum State
    {
        Chase,
        Wander,
        Search,
        Captured
    }

    private NavMeshAgent _agent;
    private float _repathTimer;
    private float _animationBlend;
    private float _modelYawOffset;

    // Populated by MazeGenerator. While it is empty the follower behaves exactly as it did before
    // the maze existed: it chases the player unconditionally.
    private readonly List<Vector3> _wanderPoints = new List<Vector3>();

    // Populated by MazeGenerator.PlaceAI. Front-of-locker positions checked during a search sweep.
    private readonly List<Vector3> _hidingSpots = new List<Vector3>();
    private bool _bustingHidingSpot;   // saw the player go in; capture on arrival, no sight needed
    private bool _wasHidden;

    private State _state = State.Chase;
    private bool _hasSight;
    private float _sightTimer;
    private float _lostSightTimer;
    private Vector3 _lastKnownPosition;
    private int _wanderIndex = -1;
    private float _wanderRepickCooldown;

    private readonly RaycastHit[] _sightHits = new RaycastHit[16];

    // Hearing
    private float _hearingTimer;
    private NavMeshPath _hearingPath;
    private PlayerStealthState _stealth;

    // Search sweep
    private readonly List<Vector3> _searchQueue = new List<Vector3>();
    private float _searchDwellTimer;
    private float _searchBudgetTimer;

    // Cells that still hold an uncollected star. Destroyed stars compare == null through Unity's
    // fake-null, so filtering the list is self-cleaning and needs no event.
    private IReadOnlyList<Transform> _patrolTargets;

    // Escalation. Left at 1 so the follower behaves at full strength if nothing drives it.
    private float _threat = 1f;
    // Multiplies how far the player's noise carries. Set per floor; 1 = full hearing.
    private float _hearingScale = 1f;
    private bool _hunting;

    /// <summary>Raised when the follower starts or stops actively chasing the player.</summary>
    public event Action<bool> ChaseStateChanged;

    /// <summary>Raised once, when the follower reaches a player it can see.</summary>
    public event Action PlayerCaught;

    /// <summary>Height of the eyes above the feet, for anything that wants to look at them.</summary>
    public float EyeHeight => eyeHeight;

    /// <summary>Corridor positions the follower patrols when it cannot see the player.</summary>
    public void SetWanderPoints(IEnumerable<Vector3> points)
    {
        // Called from MazeGenerator.Awake, which runs before this component's Awake, so nothing here
        // may touch _agent.
        _wanderPoints.Clear();
        if (points != null)
        {
            _wanderPoints.AddRange(points);
        }

        _state = _wanderPoints.Count > 0 ? State.Wander : State.Chase;
        _wanderIndex = -1;
    }

    /// <summary>Objects worth checking on while wandering. Entries may be destroyed at any time.</summary>
    public void SetPatrolTargets(IReadOnlyList<Transform> targets)
    {
        _patrolTargets = targets;
    }

    /// <summary>Front-of-locker positions. Called from MazeGenerator.PlaceAI, before this component's Awake.</summary>
    public void SetHidingSpots(IEnumerable<Vector3> fronts)
    {
        _hidingSpots.Clear();
        if (fronts != null)
        {
            _hidingSpots.AddRange(fronts);
        }
    }

    /// <summary>
    /// How dangerous the follower currently is, from 0 (nothing collected, barely stirring) to 1.
    /// <paramref name="hunting"/> is the endgame: once the hatch is open it runs everywhere, whether
    /// it can see the player or not.
    /// </summary>
    public void SetThreatLevel(float normalized, bool hunting)
    {
        _threat = Mathf.Clamp01(normalized);
        _hunting = hunting;
    }

    /// <summary>
    /// Sets how dangerous this floor's hunter is. Called by MazeGenerator before Awake, so it only
    /// writes fields. The speed cap against the player's sprint still applies afterwards in Start.
    /// </summary>
    public void ApplyDifficulty(FloorProfile profile)
    {
        if (profile == null) return;

        dormantSpeed = profile.DormantSpeed;
        moveSpeed = profile.WalkSpeed;
        runSpeed = profile.RunSpeed;
        viewDistance = profile.ViewDistance;
        viewAngle = profile.ViewAngle;
        _hearingScale = profile.HearingScale;
        loseSightTime = profile.LoseSightTime;
        patrolBias = profile.PatrolBias;
        searchBudget = profile.SearchBudget;
    }

    /// <summary>Speed while patrolling or searching.</summary>
    private float WanderSpeed()
    {
        return _hunting ? runSpeed : Mathf.Lerp(dormantSpeed, moveSpeed, _threat);
    }

    /// <summary>
    /// Speed while it can see the player. Capped at walking pace until the hatch opens - before then
    /// it can always be outrun by sprinting, and the moment it starts running is meant to land as a
    /// shock rather than as the end of a gradual ramp.
    /// </summary>
    private float ChaseSpeed()
    {
        return _hunting ? runSpeed : Mathf.Lerp(dormantSpeed, moveSpeed, _threat);
    }

    void Awake()
    {
        _hearingPath = new NavMeshPath();
        _agent = GetComponent<NavMeshAgent>();
        _agent.speed = moveSpeed;
        _agent.acceleration = acceleration;
        _agent.stoppingDistance = stoppingDistance;
        // Rotation is handled in Update so the follower can look at the target while running
        _agent.updateRotation = false;

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        // The robot model is rotated inside this object (the prefab's "Robot" child is at 90 degrees),
        // so turn this object by the opposite amount to make the visible model face the target
        if (animator != null)
        {
            _modelYawOffset = Vector3.SignedAngle(
                FlatDirection(transform.forward), FlatDirection(animator.transform.forward), Vector3.up);
        }
    }

    void Start()
    {
        if (target == null)
        {
            FindTarget();
        }

        // Resolve regardless of how target was set: assigning it in the Inspector would otherwise
        // silently switch off hearing and the flashlight coupling.
        if (_stealth == null && target != null)
        {
            _stealth = target.GetComponent<PlayerStealthState>();
        }

        // It must never be able to out-sprint the player, whatever the Inspector or a saved scene says.
        ThirdPersonController player = target != null ? target.GetComponent<ThirdPersonController>() : null;
        if (player != null)
        {
            float cap = player.SprintSpeed * maxSpeedFraction;
            if (runSpeed > cap || moveSpeed > cap)
            {
                Debug.LogWarning($"AIFollower: speeds clamped to {cap:0.00} m/s (player sprint {player.SprintSpeed}).", this);
            }

            runSpeed = Mathf.Min(runSpeed, cap);
            moveSpeed = Mathf.Min(moveSpeed, cap);
            dormantSpeed = Mathf.Min(dormantSpeed, cap);
        }

        if (!_agent.isOnNavMesh)
        {
            PlaceOnNavMesh(transform.position, 20f);
        }

        if (animator != null)
        {
            // The starter animator controller defaults these to false / 0, which freezes the legs in the air pose
            animator.SetBool(AnimGrounded, true);
            animator.SetFloat(AnimMotionSpeed, 1f);
        }
    }

    void Update()
    {
        if (target == null)
        {
            FindTarget();
            if (target == null)
            {
                return;
            }
        }

        if (!_agent.isOnNavMesh)
        {
            PlaceOnNavMesh(transform.position, 20f);
            UpdateRotation(FlatDirection(target.position - transform.position));
            UpdateAnimator();
            return;
        }

        if (_wanderPoints.Count == 0)
        {
            UpdateChaseOnly();
        }
        else
        {
            UpdatePerception();
            UpdateHearing();
            UpdateStateMachine();
        }

        UpdateAnimator();
    }

    // ---------------------------------------------------------------- legacy behaviour

    private void UpdateChaseOnly()
    {
        _repathTimer -= Time.deltaTime;
        if (_repathTimer <= 0f)
        {
            _repathTimer = repathInterval;
            SetDestinationNear(target.position);

            if (Vector3.Distance(transform.position, target.position) > teleportDistance)
            {
                PlaceOnNavMesh(target.position, navMeshSearchRadius);
            }
        }

        float distance = FlatDirection(target.position - transform.position).magnitude;
        MoveSpeedTowards(distance > runDistance ? runSpeed : moveSpeed);
        UpdateRotation(FlatDirection(target.position - transform.position));
    }

    // ---------------------------------------------------------------- perception

    private void UpdatePerception()
    {
        _sightTimer -= Time.deltaTime;
        if (_sightTimer > 0f)
        {
            return;
        }
        _sightTimer = sightCheckInterval;

        _hasSight = CanSeeTarget();
    }

    /// <summary>
    /// Listens for the player. Deliberately routes to Search and never to Chase: a heard sprint
    /// promoting the follower to Chase would give it run speed and a live-tracked destination through
    /// a wall, which reads as cheating and makes sprinting a death sentence rather than a risk.
    /// </summary>
    private void UpdateHearing()
    {
        if (_stealth == null) return;

        _hearingTimer -= Time.deltaTime;
        if (_hearingTimer > 0f) return;
        _hearingTimer = hearingInterval;

        // Early floors hear less: the same footsteps carry a shorter way
        float noiseRadius = _stealth.NoiseRadius * _hearingScale;
        if (noiseRadius <= 0.01f) return;

        // Straight-line distance is always <= path distance, so this is a correct cheap reject.
        if (Vector3.Distance(transform.position, target.position) > noiseRadius) return;

        // Path distance from here on: in a maze, sound should travel down corridors, or creeping
        // around a corner buys the player nothing. Both ends are snapped to the mesh first, or
        // CalculatePath just returns false whenever the player is a fraction off it.
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit from, 2f, NavMesh.AllAreas)) return;
        if (!NavMesh.SamplePosition(target.position, out NavMeshHit to, 2f, NavMesh.AllAreas)) return;
        if (!NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _hearingPath)) return;
        if (_hearingPath.status != NavMeshPathStatus.PathComplete) return;
        if (PathLength(_hearingPath) > noiseRadius) return;

        _lastKnownPosition = target.position;
        if (_state != State.Chase && _state != State.Captured)
        {
            EnterSearch();
        }
    }

    private static float PathLength(NavMeshPath path)
    {
        Vector3[] corners = path.corners;
        float length = 0f;
        for (int i = 1; i < corners.Length; i++)
        {
            length += Vector3.Distance(corners[i - 1], corners[i]);
        }

        return length;
    }

    private bool CanSeeTarget()
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 chest = target.position + Vector3.up * targetHeight;

        // A player with their flashlight off is much harder to pick out. Light affects how far, not
        // where - the cone and the line of sight below are unchanged.
        float view = viewDistance * (_stealth != null ? _stealth.VisibilityMultiplier : 1f);

        Vector3 flat = FlatDirection(chest - eye);
        if (flat.sqrMagnitude > view * view)
        {
            return false;
        }
        if (flat.sqrMagnitude < 0.0001f)
        {
            return true;
        }

        // The follower's own transform is yawed away from the visible model, so the cone has to be
        // built around the model's forward or the robot would "see" sideways.
        Vector3 facing = ModelForward();
        if (Vector3.Angle(facing, flat) > viewAngle * 0.5f)
        {
            return false;
        }

        // The maze walls sit on the same layer as the player, so the blocker is identified by its
        // hierarchy rather than by a layer mask.
        Vector3 ray = chest - eye;
        float distance = ray.magnitude;
        int count = Physics.RaycastNonAlloc(eye, ray / distance, _sightHits, distance, ~0, QueryTriggerInteraction.Ignore);

        float closest = float.MaxValue;
        Transform blocker = null;
        for (int i = 0; i < count; i++)
        {
            Transform hit = _sightHits[i].transform;
            // Ignore the follower's own colliders: the eye point sits inside them.
            if (IsPartOf(hit, transform))
            {
                continue;
            }

            if (_sightHits[i].distance < closest)
            {
                closest = _sightHits[i].distance;
                blocker = hit;
            }
        }

        return blocker == null || IsPartOf(blocker, target);
    }

    private static bool IsPartOf(Transform candidate, Transform root)
    {
        while (candidate != null)
        {
            if (candidate == root)
            {
                return true;
            }
            candidate = candidate.parent;
        }

        return false;
    }

    // ---------------------------------------------------------------- states

    private void UpdateStateMachine()
    {
        // Before the sight promotion below, or a stale sight reading would flip it back to Chase
        if (_state == State.Captured)
        {
            TickCaptured();
            return;
        }

        // "It saw you go in" - the last _hasSight reading before Hidden flips true is still valid,
        // because UpdatePerception ran earlier this same frame.
        bool hiddenNow = _stealth != null && _stealth.Hidden;
        if (hiddenNow && !_wasHidden && _state == State.Chase && _hasSight) _bustingHidingSpot = true;
        if (!hiddenNow) _bustingHidingSpot = false;
        _wasHidden = hiddenNow;

        State previous = _state;

        if (_hasSight)
        {
            _state = State.Chase;
            _lostSightTimer = loseSightTime;
            _lastKnownPosition = target.position;
        }
        else if (_state == State.Chase && !_bustingHidingSpot)
        {
            _lostSightTimer -= Time.deltaTime;
            if (_lostSightTimer <= 0f)
            {
                EnterSearch();
            }
        }

        if (_state != previous && (_state == State.Chase || previous == State.Chase))
        {
            ChaseStateChanged?.Invoke(_state == State.Chase);
        }

        switch (_state)
        {
            case State.Chase:
                TickChase();
                break;
            case State.Search:
                TickSearch();
                break;
            default:
                TickWander();
                break;
        }
    }

    private void TickChase()
    {
        // Enforced here rather than trusted to two serialized fields: a saved scene bakes every value
        // into its YAML, and a stopping distance outside the capture radius means it never catches.
        _agent.stoppingDistance = Mathf.Min(stoppingDistance, captureRadius - 0.3f);
        _agent.isStopped = false;
        MoveSpeedTowards(ChaseSpeed());

        _repathTimer -= Time.deltaTime;
        if (_repathTimer <= 0f)
        {
            _repathTimer = repathInterval;
            // Without sight, head for the last place the player was actually seen instead of tracking
            // them through the wall they just stepped behind. Busting a hiding spot always means the
            // locker's front - the player cannot be seen again until Expose() opens the door.
            SetDestinationNear(_bustingHidingSpot ? _lastKnownPosition : (_hasSight ? target.position : _lastKnownPosition));
        }

        if (_hasSight)
        {
            UpdateRotation(FlatDirection(target.position - transform.position));
        }
        else
        {
            UpdateRotation(CurrentHeading());
        }

        // Both gates matter. Sight: a bare distance test captures through a wall, since two actors on
        // opposite faces of a 0.5 m wall are barely 1.5 m apart. Run active: Update still ticks at
        // timeScale 0, behind the title screen. Busting a hiding spot substitutes for sight, and gets
        // a wider radius: the player is a further 0.6 m inside the locker than the door it stops at.
        float radius = _bustingHidingSpot ? captureRadius + 0.8f : captureRadius;
        if (GameFlow.IsRunActive && (_hasSight || _bustingHidingSpot)
            && FlatDirection(target.position - transform.position).sqrMagnitude <= radius * radius)
        {
            if (_bustingHidingSpot && _stealth != null)
            {
                _stealth.CurrentHidingSpot?.Expose();
            }

            EnterCaptured();
        }
    }

    private void EnterCaptured()
    {
        _state = State.Captured;
        _bustingHidingSpot = false;
        _agent.isStopped = true;
        _agent.velocity = Vector3.zero;
        PlayerCaught?.Invoke();
    }

    private void TickCaptured()
    {
        UpdateRotation(FlatDirection(target.position - transform.position));
    }

    /// <summary>
    /// Queue up a handful of spots around the last known position. Stopping dead at the exact point
    /// reads as giving up instantly; sweeping the area around it reads as looking for you.
    /// </summary>
    private void EnterSearch()
    {
        _state = State.Search;
        _searchBudgetTimer = searchBudget;
        _searchDwellTimer = 0f;
        // A fresh noise can land mid-dwell, and the dwell is what set isStopped. Clearing it here is
        // what stops the follower standing frozen until the search budget runs out.
        _agent.isStopped = false;
        _searchQueue.Clear();

        foreach (Vector3 point in _wanderPoints)
        {
            if (Vector3.Distance(point, _lastKnownPosition) <= searchRadius)
            {
                _searchQueue.Add(point);
            }
        }

        // Shuffle so repeated searches in the same area do not walk the same route
        for (int i = _searchQueue.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (_searchQueue[i], _searchQueue[j]) = (_searchQueue[j], _searchQueue[i]);
        }

        if (_searchQueue.Count > searchPoints)
        {
            _searchQueue.RemoveRange(searchPoints, _searchQueue.Count - searchPoints);
        }

        // Always check the exact spot first
        _searchQueue.Insert(0, _lastKnownPosition);

        // Any hiding spot near the noise/sighting is checked right after, nearest first - "it stops
        // in front of your locker and stares" before resuming the wider sweep.
        List<Vector3> nearbyHidingSpots = new List<Vector3>();
        foreach (Vector3 spot in _hidingSpots)
        {
            if (Vector3.Distance(spot, _lastKnownPosition) <= hidingSpotSearchRadius)
            {
                nearbyHidingSpots.Add(spot);
            }
        }
        nearbyHidingSpots.Sort((a, b) =>
            Vector3.Distance(a, _lastKnownPosition).CompareTo(Vector3.Distance(b, _lastKnownPosition)));
        for (int i = 0; i < nearbyHidingSpots.Count; i++)
        {
            _searchQueue.Insert(1 + i, nearbyHidingSpots[i]);
        }

        SetDestinationNear(_searchQueue[0]);
    }

    private void TickSearch()
    {
        _agent.stoppingDistance = wanderStoppingDistance;
        MoveSpeedTowards(WanderSpeed());

        // Without a hard budget, repeated noises can keep refilling the queue forever
        _searchBudgetTimer -= Time.deltaTime;
        if (_searchBudgetTimer <= 0f || _searchQueue.Count == 0)
        {
            _state = State.Wander;
            _wanderRepickCooldown = 0f;
            return;
        }

        if (_searchDwellTimer > 0f)
        {
            // Stand and turn on the spot, so the sweep is legible from a distance - except at a hiding
            // spot, where it stands and stares at the locker instead of spinning.
            _searchDwellTimer -= Time.deltaTime;
            _agent.isStopped = true;

            if (_searchQueue.Count > 0 && _hidingSpots.Contains(_searchQueue[0]))
            {
                UpdateRotation(FlatDirection(_searchQueue[0] - transform.position));
            }
            else
            {
                UpdateRotation(Quaternion.AngleAxis(120f * Time.deltaTime, Vector3.up) * ModelForward());
            }

            if (_searchDwellTimer <= 0f)
            {
                _agent.isStopped = false;
                _searchQueue.RemoveAt(0);
                if (_searchQueue.Count > 0) SetDestinationNear(_searchQueue[0]);
            }
            return;
        }

        if (ReachedDestination())
        {
            _searchDwellTimer = searchDwellTime;
            return;
        }

        UpdateRotation(CurrentHeading());
    }

    private void TickWander()
    {
        _agent.stoppingDistance = wanderStoppingDistance;
        _agent.isStopped = false;
        MoveSpeedTowards(WanderSpeed());

        _wanderRepickCooldown -= Time.deltaTime;
        if (_wanderIndex < 0 || (_wanderRepickCooldown <= 0f && ReachedDestination()))
        {
            PickWanderPoint();
        }

        UpdateRotation(CurrentHeading());
    }

    private void PickWanderPoint()
    {
        if (_wanderPoints.Count == 0)
        {
            return;
        }

        // Mostly head for something still worth guarding, but not always: a hard preference would make
        // it camp the stars, which is unfair rather than tense.
        int next = -1;
        if (_patrolTargets != null && Random.value < patrolBias)
        {
            next = NearestCellToLivePatrolTarget();
        }

        if (next < 0)
        {
            next = Random.Range(0, _wanderPoints.Count);
        }

        if (_wanderPoints.Count > 1)
        {
            // Never pick the point we just arrived at, or the robot would stand still
            while (next == _wanderIndex)
            {
                next = Random.Range(0, _wanderPoints.Count);
            }
        }

        _wanderIndex = next;
        // Give the path a moment to be computed before the arrival test runs again
        _wanderRepickCooldown = 0.25f;
        SetDestinationNear(_wanderPoints[next]);
    }

    private bool ReachedDestination()
    {
        if (_agent.pathPending)
        {
            return false;
        }

        // remainingDistance is Infinity on a partial or invalid path, so the status is tested first
        if (_agent.pathStatus != NavMeshPathStatus.PathComplete)
        {
            return true;
        }

        if (!_agent.hasPath)
        {
            return true;
        }

        return _agent.remainingDistance <= _agent.stoppingDistance + 0.05f;
    }

    // ---------------------------------------------------------------- movement helpers

    private void SetDestinationNear(Vector3 position)
    {
        // The target can be mid-jump or on unreachable ground, so head for the closest walkable point instead
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, navMeshSearchRadius, NavMesh.AllAreas))
        {
            _agent.SetDestination(hit.position);
        }
        else
        {
            // Never keep an old destination: SetDestination projects to the nearest NavMesh point itself
            _agent.SetDestination(position);
        }
    }

    private void MoveSpeedTowards(float desiredSpeed)
    {
        _agent.speed = Mathf.MoveTowards(_agent.speed, desiredSpeed, acceleration * Time.deltaTime);
    }

    /// <summary>Index of the wander point closest to the nearest star still standing, or -1.</summary>
    private int NearestCellToLivePatrolTarget()
    {
        Transform best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < _patrolTargets.Count; i++)
        {
            // Destroyed stars compare == null through Unity's fake-null, so collected ones simply
            // stop being candidates with no bookkeeping.
            Transform candidate = _patrolTargets[i];
            if (candidate == null) continue;

            float distance = Vector3.Distance(transform.position, candidate.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        if (best == null) return -1;

        int nearestCell = -1;
        float nearestCellDistance = float.MaxValue;
        for (int i = 0; i < _wanderPoints.Count; i++)
        {
            float distance = Vector3.Distance(_wanderPoints[i], best.position);
            if (distance < nearestCellDistance)
            {
                nearestCellDistance = distance;
                nearestCell = i;
            }
        }

        return nearestCell;
    }

    private Vector3 ModelForward()
    {
        return FlatDirection(Quaternion.AngleAxis(_modelYawOffset, Vector3.up) * transform.forward);
    }

    private Vector3 CurrentHeading()
    {
        Vector3 velocity = FlatDirection(_agent.velocity);
        if (velocity.sqrMagnitude > 0.0001f)
        {
            return velocity;
        }

        return _agent.hasPath ? FlatDirection(_agent.steeringTarget - transform.position) : Vector3.zero;
    }

    private void UpdateRotation(Vector3 lookDirection)
    {
        if (lookDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion desired = Quaternion.LookRotation(lookDirection.normalized, Vector3.up) * Quaternion.Euler(0f, -_modelYawOffset, 0f);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, turnSpeed * Time.deltaTime);
    }

    private void UpdateAnimator()
    {
        if (animator == null)
        {
            return;
        }

        float speed = FlatDirection(_agent.velocity).magnitude;
        _animationBlend = Mathf.Lerp(_animationBlend, speed, Time.deltaTime * animationBlendRate);
        if (_animationBlend < 0.01f)
        {
            _animationBlend = 0f;
        }

        animator.SetFloat(AnimSpeed, _animationBlend);
        animator.SetFloat(AnimMotionSpeed, 1f);
        animator.SetBool(AnimGrounded, true);
    }

    private void FindTarget()
    {
        // The player prefab has two objects tagged "Player": a static root and the child that actually
        // moves (it owns the CharacterController). Following the root would track the spawn point forever.
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject player in players)
        {
            if (player.GetComponent<CharacterController>() != null)
            {
                target = player.transform;
                _stealth = player.GetComponent<PlayerStealthState>();
                return;
            }
        }

        if (players.Length > 0)
        {
            target = players[0].transform;
            _stealth = target.GetComponent<PlayerStealthState>();
        }
    }

    private void PlaceOnNavMesh(Vector3 around, float radius)
    {
        if (NavMesh.SamplePosition(around, out NavMeshHit hit, radius, NavMesh.AllAreas))
        {
            _agent.Warp(hit.position);
        }
    }

    private static Vector3 FlatDirection(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 facing = FlatDirection(Quaternion.AngleAxis(_modelYawOffset, Vector3.up) * transform.forward);
        if (facing.sqrMagnitude < 0.0001f)
        {
            facing = FlatDirection(transform.forward);
        }
        facing.Normalize();

        Gizmos.color = _hasSight ? Color.red : new Color(1f, 1f, 0f, 0.6f);
        Gizmos.DrawRay(eye, Quaternion.Euler(0f, -viewAngle * 0.5f, 0f) * facing * viewDistance);
        Gizmos.DrawRay(eye, Quaternion.Euler(0f, viewAngle * 0.5f, 0f) * facing * viewDistance);
        Gizmos.DrawRay(eye, facing * viewDistance);

        Gizmos.color = _state == State.Captured ? Color.red : new Color(1f, 0.5f, 0f, 0.8f);
        Vector3 feet = transform.position + Vector3.up * 0.05f;
        Vector3 previousPoint = feet + Vector3.right * captureRadius;
        for (int i = 1; i <= 32; i++)
        {
            float angle = i * Mathf.PI * 2f / 32f;
            Vector3 point = feet + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * captureRadius;
            Gizmos.DrawLine(previousPoint, point);
            previousPoint = point;
        }
    }
}

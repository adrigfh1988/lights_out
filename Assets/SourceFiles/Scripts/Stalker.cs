using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The second creature (F75, decision D7): the robot body (Assets/Prefabs/PlayerRobot.prefab),
/// blackened, with pin-prick eyes, scaled 1.15 - deliberately not Adam, so the two killers still read
/// differently once LIGHTS OUT &gt; Build Hunter Body has been run for the hunter. Appears on floors 4
/// and 5 (FloorProfile.HasStalker). It moves only while unlit - not in the torch cone, not in a powered
/// lamp/candle/lantern pool (LightPool) - and freezes mid-stride the instant a light finds it.
///
/// It is not lethal: touching the player makes it shriek (the hunter hears the exact position from
/// 40 m and its last-known position is forced to match), drains a quarter of the torch battery, and it
/// vanishes and reappears far away 12 s later. It never captures and never touches GameOutcome - "one
/// killer stays one killer".
///
/// Cloned from InteractableKit.stalker by MazeGenerator.BuildStalker, post-PlaceAI (+16), which also
/// adds and warps its NavMeshAgent. Configure is called later, from SetUpAtmosphere, once the
/// player/camera/flashlight/HUD/hunter/outcome all exist - the same late-binding pattern as
/// Lantern/LoreNote/ThemeInteractable.
/// </summary>
public class Stalker : MonoBehaviour
{
    private const float ChestHeight = 1.0f;
    private const float HeadHeight = 1.55f;
    private const float TouchRadius = 1.0f;
    private const float ExposureThreshold = 0.25f;
    private const float FarPathDistance = 30f;
    private const float ChaseSpeedValue = 3.4f;
    private const float WanderSpeedValue = 1.5f;
    private const float RepathInterval = 0.25f;
    private const float HideSeconds = 12f;
    private const float SingleTickRange = 8f;
    private const float TorchAngleSlack = 3f;

    private static readonly int AnimSpeed = Animator.StringToHash("Speed");
    private static readonly int AnimMotionSpeed = Animator.StringToHash("MotionSpeed");
    private static readonly int AnimGrounded = Animator.StringToHash("Grounded");

    private MazeGenerator _maze;
    private Transform _player;
    private Flashlight _torch;
    private Transform _torchCamera;
    private PlayerStealthState _stealth;
    private AIFollower _hunter;
    private PlayerHud _hud;
    private GameOutcome _outcome;

    private NavMeshAgent _agent;
    private Animator _animator;
    private bool _hasSpeedParam;
    private bool _hasMotionSpeedParam;
    private bool _hasGroundedParam;
    private float _modelYawOffset;
    private Renderer[] _renderers;

    // Only two sounds: a single tick the moment a light freezes it nearby, and the shriek on touch. The
    // F75 "servo click loop while moving" was removed 4 Oct 2026 - a 60 ms click looped back to back
    // played as a continuous 1800 Hz buzz that read as a music bed following the stalker around.
    private AudioSource _audio;
    private AudioClip _tickClip;
    private AudioClip _shriekClip;

    // Created on first use: NavMeshPath's constructor is not allowed in a field initializer (Unity throws
    // when the builder AddComponents this onto the template).
    private NavMeshPath _path;
    private readonly RaycastHit[] _hits = new RaycastHit[16];

    private bool _configured;
    private bool _wasLit;
    private float _repathTimer;
    private bool _hasWanderTarget;

    private bool _hiddenAfterTouch;
    private float _reappearAt = -1f;

    /// <summary>Cached at the RepathInterval cadence, not recomputed every frame - TryPathLength runs a full NavMesh.CalculatePath.</summary>
    private bool _far;

    /// <summary>Called once by MazeGenerator.SetUpAtmosphere, after every system it reads exists. The
    /// NavMeshAgent itself is added and warped earlier, by MazeGenerator.BuildStalker.</summary>
    public void Configure(MazeGenerator maze, Transform player, Flashlight torch, Transform torchCamera,
        PlayerStealthState stealth, AIFollower hunter, PlayerHud hud, GameOutcome outcome)
    {
        _maze = maze;
        _player = player;
        _torch = torch;
        _torchCamera = torchCamera;
        _stealth = stealth;
        _hunter = hunter;
        _hud = hud;
        _outcome = outcome;

        _agent = GetComponent<NavMeshAgent>();
        if (_agent == null)
        {
            Debug.LogWarning("Stalker: no NavMeshAgent - MazeGenerator.BuildStalker should have added one.", this);
            return;
        }

        _animator = GetComponentInChildren<Animator>();
        if (_animator != null)
        {
            foreach (AnimatorControllerParameter param in _animator.parameters)
            {
                if (param.type == AnimatorControllerParameterType.Float && param.name == "Speed") _hasSpeedParam = true;
                else if (param.type == AnimatorControllerParameterType.Float && param.name == "MotionSpeed") _hasMotionSpeedParam = true;
                else if (param.type == AnimatorControllerParameterType.Bool && param.name == "Grounded") _hasGroundedParam = true;
            }
            // Starter Assets' controller defaults Grounded to false, which freezes the legs in the air
            // pose (see AIFollower.Start's identical fix for the hunter's own robot body).
            if (_hasGroundedParam) _animator.SetBool(AnimGrounded, true);
            if (_hasMotionSpeedParam) _animator.SetFloat(AnimMotionSpeed, 1f);
            // The stripped-down template has no ThirdPersonController to receive the Starter Assets
            // clips' OnFootstep/OnLand animation events - the stalker moves silently on purpose.
            _animator.fireEvents = false;
        }

        // The robot's Animator lives on a child node yawed 90 degrees from the root (AIFollower's own
        // modelFacingYaw comment) - the NavMeshAgent must not drive rotation itself, or the mesh faces
        // sideways to its travel direction. Chase() below turns the root to compensate every frame.
        _agent.updateRotation = false;
        _modelYawOffset = _animator != null
            ? Vector3.SignedAngle(FlatDirection(transform.forward), FlatDirection(_animator.transform.forward), Vector3.up)
            : 0f;

        _renderers = GetComponentsInChildren<Renderer>(true);

        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 1f;
        _audio.maxDistance = 22f;
        _tickClip = DoorAudio.BuildClunkClip(0.12f, 500f, 0.3f);
        _shriekClip = DoorAudio.BuildClunkClip(0.5f, 900f, 0.75f);

        // F75: registers as a silent door-breaker - it slips under a closed shutter with no slam, no
        // stall (contrast AIFollower's own registration).
        DoorBreakers.Register(transform, _agent, true, null);
        _configured = true;
    }

    private void OnDestroy()
    {
        DoorBreakers.Unregister(transform);
    }

    private void Update()
    {
        if (!_configured || _agent == null) return;

        if (_hiddenAfterTouch)
        {
            if (Time.time >= _reappearAt) Reappear();
            return;
        }

        if (!GameFlow.IsRunActive || GameOutcome.IsOver || (_outcome != null && _outcome.IsEnding))
        {
            Freeze();
            return;
        }

        if (!_agent.isOnNavMesh) return;

        bool lit = IsLit();
        ApplyLitState(lit);

        if (!lit)
        {
            Chase();
            CheckTouch();
        }

        UpdateAnimatorParams();
    }

    private void Freeze()
    {
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
        }
        if (_animator != null) _animator.speed = 0f;
    }

    // ---------------------------------------------------------------- lit test

    /// <summary>Lit if a registered LightPool source reaches the chest strongly enough, or the torch is
    /// on, in range, inside the (blended) beam cone plus a few degrees of slack, and unobstructed.</summary>
    private bool IsLit()
    {
        Vector3 chest = transform.position + Vector3.up * ChestHeight;
        if (LightPool.ExposureAt(chest) > ExposureThreshold) return true;

        if (_torch == null || _torchCamera == null || !_torch.IsOn) return false;

        Vector3 head = transform.position + Vector3.up * HeadHeight;
        float halfAngle = _torch.OuterAngle * 0.5f + TorchAngleSlack;
        float range = _torch.Range;

        return TestBeamPoint(chest, halfAngle, range) || TestBeamPoint(head, halfAngle, range);
    }

    private bool TestBeamPoint(Vector3 point, float halfAngle, float range)
    {
        Vector3 toPoint = point - _torchCamera.position;
        float distance = toPoint.magnitude;
        if (distance < 0.0001f || distance > range) return false;
        if (Vector3.Angle(_torchCamera.forward, toPoint) >= halfAngle) return false;
        return HasClearLine(_torchCamera.position, point);
    }

    /// <summary>Camera -&gt; point, ignoring the stalker's own colliders/triggers (and triggers globally
    /// via QueryTriggerInteraction.Ignore) - and the player's own hierarchy, since the camera sits
    /// inside/near the player's body (same reasoning as AIFollower.HasLineOfSightIgnoring). Blocked only
    /// by something that is not part of either.</summary>
    private bool HasClearLine(Vector3 from, Vector3 to)
    {
        Vector3 ray = to - from;
        float distance = ray.magnitude;
        if (distance < 0.0001f) return true;

        int count = Physics.RaycastNonAlloc(from, ray / distance, _hits, distance, ~0, QueryTriggerInteraction.Ignore);
        float closest = float.MaxValue;
        Transform blocker = null;
        for (int i = 0; i < count; i++)
        {
            Transform hit = _hits[i].transform;
            if (IsPartOf(hit, transform) || (_player != null && IsPartOf(hit, _player))) continue;
            if (_hits[i].distance < closest)
            {
                closest = _hits[i].distance;
                blocker = hit;
            }
        }
        return blocker == null;
    }

    private static bool IsPartOf(Transform candidate, Transform root)
    {
        while (candidate != null)
        {
            if (candidate == root) return true;
            candidate = candidate.parent;
        }
        return false;
    }

    private void ApplyLitState(bool lit)
    {
        if (lit && !_wasLit)
        {
            // "a single tick when it freezes within 8 m" - the moment it goes from moving to lit-frozen.
            float d = _player != null ? Vector3.Distance(transform.position, _player.position) : float.MaxValue;
            if (d <= SingleTickRange && _audio != null && _tickClip != null) _audio.PlayOneShot(_tickClip, 0.6f);
        }

        if (lit)
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
            if (_animator != null) _animator.speed = 0f;
        }
        else
        {
            _agent.isStopped = false;
            if (_animator != null) _animator.speed = 1f;
        }
        _wasLit = lit;
    }

    // ---------------------------------------------------------------- movement

    /// <summary>Unlit: chase the player's live position, repathing every 0.25 s. Far away (&gt;30 m by
    /// path) it wanders random cells at a slower pace instead of beelining across the whole maze.</summary>
    private void Chase()
    {
        _repathTimer -= Time.deltaTime;

        if (_repathTimer <= 0f)
        {
            _repathTimer = RepathInterval;
            _far = _player == null || !TryPathLength(transform.position, _player.position, out float length) || length > FarPathDistance;

            if (_far)
            {
                if (!_hasWanderTarget || ReachedDestination()) PickWanderTarget();
            }
            else
            {
                _hasWanderTarget = false;
                _agent.SetDestination(_player.position);
            }
        }

        _agent.speed = _far ? WanderSpeedValue : ChaseSpeedValue;

        // agent.updateRotation is off (see Configure) - turn the root to face travel direction,
        // compensating for the model's own yaw offset, exactly the way AIFollower.UpdateRotation does
        // for the hunter's identical robot body. Only while actually moving; while lit, Chase() is not
        // called at all (see Update), so rotation simply freezes along with everything else.
        Vector3 heading = _agent.velocity;
        heading.y = 0f;
        if (heading.sqrMagnitude > 0.01f)
        {
            Quaternion desired = Quaternion.LookRotation(heading.normalized, Vector3.up) * Quaternion.Euler(0f, -_modelYawOffset, 0f);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, 720f * Time.deltaTime);
        }
    }

    private static Vector3 FlatDirection(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    private void PickWanderTarget()
    {
        if (_maze == null) return;
        IReadOnlyList<Vector3> centers = _maze.CellCenters;
        if (centers == null || centers.Count == 0) return;

        Vector3 target = centers[Random.Range(0, centers.Count)];
        _agent.SetDestination(target);
        _hasWanderTarget = true;
    }

    private bool ReachedDestination()
    {
        if (_agent.pathPending) return false;
        if (_agent.pathStatus != NavMeshPathStatus.PathComplete) return true;
        if (!_agent.hasPath) return true;
        return _agent.remainingDistance <= _agent.stoppingDistance + 0.3f;
    }

    private bool TryPathLength(Vector3 from, Vector3 to, out float length)
    {
        length = 0f;
        if (!NavMesh.SamplePosition(from, out NavMeshHit fromHit, 3f, NavMesh.AllAreas)) return false;
        if (!NavMesh.SamplePosition(to, out NavMeshHit toHit, 3f, NavMesh.AllAreas)) return false;
        if (_path == null) _path = new NavMeshPath();
        if (!NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, _path)) return false;
        if (_path.status != NavMeshPathStatus.PathComplete) return false;

        Vector3[] corners = _path.corners;
        for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
        return true;
    }

    // ---------------------------------------------------------------- audio + animator

    private void UpdateAnimatorParams()
    {
        if (_animator == null) return;

        Vector3 flatVelocity = _agent.velocity;
        flatVelocity.y = 0f;
        float speed = flatVelocity.magnitude;

        if (_hasSpeedParam) _animator.SetFloat(AnimSpeed, speed);
        if (_hasMotionSpeedParam) _animator.SetFloat(AnimMotionSpeed, 1f);
        if (_hasGroundedParam) _animator.SetBool(AnimGrounded, true);
    }

    // ---------------------------------------------------------------- touch

    private void CheckTouch()
    {
        if (_player == null || _hunter == null) return;
        if (_stealth != null && _stealth.Hidden) return;
        if (!GameFlow.IsRunActive || GameOutcome.IsOver) return;
        if (_outcome != null && _outcome.IsEnding) return;

        Vector3 flat = _player.position - transform.position;
        flat.y = 0f;
        if (flat.magnitude > TouchRadius) return;

        Touch();
    }

    /// <summary>"It shrieked" - the hunter hears the exact position and its search always lands here,
    /// the torch loses a quarter of its charge, and a subtitle sells the beat. Never captures, never
    /// touches GameOutcome.</summary>
    private void Touch()
    {
        if (_audio != null && _shriekClip != null) _audio.PlayOneShot(_shriekClip, 1f);

        _hunter.HearNoise(_player.position, 40f);
        _hunter.ForceLastKnownPosition(_player.position);
        _torch?.Drain(0.25f);
        _hud?.ShowSubtitle("IT SCREAMED.", 2.5f);

        HideAndRespawn();
    }

    private void HideAndRespawn()
    {
        SetRenderersVisible(false);
        if (_agent != null)
        {
            _agent.isStopped = true;
            _agent.enabled = false;
        }
        // Unregistered for the duration - a disabled/off-mesh agent must never be read by
        // MazeDoor.CheckBash. Re-registered in Reappear once the agent is back on the mesh.
        DoorBreakers.Unregister(transform);
        _hiddenAfterTouch = true;
        _reappearAt = Time.time + HideSeconds;
    }

    private void Reappear()
    {
        _hiddenAfterTouch = false;

        Vector3 target = _maze != null && _player != null ? _maze.FarthestCellCenterFrom(_player.position) : transform.position;
        if (_agent != null)
        {
            _agent.enabled = true;
            if (NavMesh.SamplePosition(target, out NavMeshHit hit, 10f, NavMesh.AllAreas))
            {
                _agent.Warp(hit.position);
            }
            else
            {
                transform.position = target;
            }
            _agent.isStopped = false;
            DoorBreakers.Register(transform, _agent, true, null);
        }

        SetRenderersVisible(true);
        _wasLit = false;
        _repathTimer = 0f;
    }

    private void SetRenderersVisible(bool visible)
    {
        if (_renderers == null) return;
        foreach (Renderer renderer in _renderers)
        {
            if (renderer != null) renderer.enabled = visible;
        }
    }
}

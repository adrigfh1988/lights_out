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
/// F81 "lively Stan": a puppet-like stop-motion gait (the Animator is never disabled - its
/// animator.speed is gated to 0 between "steps" so it still rewrites the whole pose every frame, which
/// is what keeps the additive LateUpdate bone offsets from compounding; the Animator is forced to
/// AlwaysAnimate for the same reason), a hunched, head-tilted posture, a head that tracks the player's
/// camera (and holds its last aim while lit - the light reveals it was already looking at you), a lunge
/// when a light leaves it, a watch-then-rush beat when the player stares at it in the dark, a roam that
/// drifts toward the player, and an optional head twitch while lit (litTwitch; the body still never
/// moves). Every tunable is a [SerializeField] on the InteractableKit.stalker template.
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
    private const float RepathInterval = 0.25f;
    private const float HideSeconds = 12f;
    private const float SingleTickRange = 8f;
    private const float TorchAngleSlack = 3f;

    private static readonly int AnimSpeed = Animator.StringToHash("Speed");
    private static readonly int AnimMotionSpeed = Animator.StringToHash("MotionSpeed");
    private static readonly int AnimGrounded = Animator.StringToHash("Grounded");

    /// <summary>Behaviour layered under the lit freeze (lit/hidden stay booleans, not modes).</summary>
    private enum Mode { Roam, Stalk, Watch, Lunge }

    [Header("Movement")]
    [Tooltip("Agent speed while stalking (path to the player <= 30 m).")]
    [SerializeField] private float stalkSpeed = 3.6f;
    [Tooltip("Agent speed while roaming (player far away by path).")]
    [SerializeField] private float roamSpeed = 2.4f;
    [SerializeField] private float roamMinDistanceToPlayer = 10f;
    [SerializeField] private int roamCandidates = 8;
    [SerializeField] private float roamRepickSeconds = 8f;

    [Header("Stop-motion")]
    [Tooltip("Gate the Animator so the gait advances in discrete steps (puppet look).")]
    [SerializeField] private bool stopMotion = true;
    [Tooltip("Step rate is random in [min, max] steps per second.")]
    [SerializeField] private float stepFpsMin = 9f;
    [SerializeField] private float stepFpsMax = 14f;
    [SerializeField] private float lungeStepFps = 18f;
    [Tooltip("Also hop the body's position on step frames. Turn off if a shutter ever closes on Stan.")]
    [SerializeField] private bool stepBodyTranslation = true;

    [Header("Gait")]
    [Tooltip("MotionSpeed parameter - multiplies the locomotion state's playback rate.")]
    [SerializeField] private float stalkMotionSpeed = 1.45f;
    [SerializeField] private float roamMotionSpeed = 1.15f;
    [SerializeField] private float lungeMotionSpeed = 2.0f;
    [Tooltip("Speed parameter = agent speed * this (leans the blend toward the run clip).")]
    [SerializeField] private float animSpeedScale = 1.35f;

    [Header("Posture")]
    [Tooltip("Degrees pitched forward, applied in world space about the model's right axis.")]
    [SerializeField] private float spineHunch = 10f;
    [SerializeField] private float chestHunch = 12f;
    [SerializeField] private float neckForward = 8f;
    [Tooltip("Constant head roll; the side is picked at random per spawn.")]
    [SerializeField] private float headTilt = 16f;
    [SerializeField] private float lungeExtraHunch = 18f;
    [Tooltip("Upper arms swing forward by this much during a lunge. If they go backwards on screen, flip the sign in ApplyPose.")]
    [SerializeField] private float lungeArmReach = 55f;

    [Header("Head tracking")]
    [SerializeField] private float headTrackRange = 16f;
    [SerializeField] private float headYawLimit = 80f;
    [SerializeField] private float headPitchLimit = 35f;
    [SerializeField] private float headTwitchIntervalMin = 1.5f;
    [SerializeField] private float headTwitchIntervalMax = 4f;
    [SerializeField] private float headTwitchAngle = 14f;

    [Header("Lunge")]
    [SerializeField] private float lungeSpeed = 6.5f;
    [SerializeField] private float lungeAcceleration = 120f;
    [SerializeField] private float lungeSeconds = 0.6f;
    [SerializeField] private float lungeCooldown = 5f;
    [Tooltip("Flat metres; the light must leave it within this range of the player to trigger a lunge.")]
    [SerializeField] private float lungeTriggerRange = 12f;
    [Tooltip("Ignore beam-edge flicker: it must have been lit at least this long.")]
    [SerializeField] private float lungeMinLitSeconds = 0.4f;

    [Header("Watch")]
    [Tooltip("Rolled once per eligible encounter.")]
    [SerializeField] private float watchChance = 0.5f;
    [SerializeField] private float watchMinDistance = 6f;
    [SerializeField] private float watchMaxDistance = 16f;
    [Tooltip("Stan must be within this angle of the camera's forward.")]
    [SerializeField] private float watchViewAngle = 30f;
    [SerializeField] private float watchSecondsMin = 1.2f;
    [SerializeField] private float watchSecondsMax = 2.5f;
    [SerializeField] private float watchCooldown = 10f;

    [Header("Lit twitch (weeping-angel micro-movement)")]
    [Tooltip("While lit and close, the head (only) snaps a few degrees now and then. Off = strict F75 freeze.")]
    [SerializeField] private bool litTwitch = true;
    [SerializeField] private float litTwitchRange = 7f;
    [SerializeField] private float litTwitchIntervalMin = 3f;
    [SerializeField] private float litTwitchIntervalMax = 6f;
    [SerializeField] private float litTwitchAngle = 12f;

    [Header("Voice (F82)")]
    [Tooltip("Stan's lines (Assets/SourceFiles/Data/StanPhrases.asset). Assigned by LIGHTS OUT > Build Phrase Books; empty = built-in defaults.")]
    [SerializeField] private StalkerPhraseBook phraseBook;

    private MazeGenerator _maze;
    private Transform _player;
    private Flashlight _torch;
    private Transform _torchCamera;
    private PlayerStealthState _stealth;
    private AIFollower _hunter;
    private PlayerHud _hud;
    private GameOutcome _outcome;

    // F82: the speech-bubble voice (added in Configure) and the eyes it flares while a letter blips.
    private StalkerVoice _voice;
    private Renderer[] _eyeRenderers;
    private Color[] _eyeBaseEmission;
    private MaterialPropertyBlock _eyeBlock;
    private float _appliedFlare;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private NavMeshAgent _agent;
    private Animator _animator;
    private bool _hasSpeedParam;
    private bool _hasMotionSpeedParam;
    private bool _hasGroundedParam;
    private float _modelYawOffset;
    private Renderer[] _renderers;

    // Only these sounds: a single tick the moment a light freezes it nearby (and, quieter, on a lit head
    // twitch), a one-shot skitter at lunge start, and the shriek on touch. The F75 "servo click loop
    // while moving" was removed 4 Oct 2026 - a 60 ms click looped back to back played as a continuous
    // 1800 Hz buzz that read as a music bed following the stalker around. Never loop a short click.
    private AudioSource _audio;
    private AudioClip _tickClip;
    private AudioClip _shriekClip;
    private AudioClip _skitterClip;

    // Created on first use: NavMeshPath's constructor is not allowed in a field initializer (Unity throws
    // when the builder AddComponents this onto the template).
    private NavMeshPath _path;
    private readonly RaycastHit[] _hits = new RaycastHit[16];

    private bool _configured;
    private bool _wasLit;
    private float _repathTimer;
    private bool _hasWanderTarget;
    private float _roamPickedAt;

    private bool _hiddenAfterTouch;
    private float _reappearAt = -1f;

    /// <summary>Cached at the RepathInterval cadence, not recomputed every frame - TryPathLength runs a full NavMesh.CalculatePath.</summary>
    private bool _far;

    // F81 behaviour state.
    private Mode _mode = Mode.Roam;
    private float _baseAcceleration = 40f;
    private float _litSince;
    private float _lungeEndsAt;
    private float _nextLungeAllowedAt;
    private float _watchEndsAt;
    private float _nextWatchAllowedAt;
    private bool _watchRolled;

    // F81 stop-motion state.
    private float _stepAccum;
    private float _nextStepIn = 0.1f;
    private float _lastStepTime;
    private float _lastStepDelta;
    private bool _steppedThisFrame;
    private Quaternion _desiredRootRotation = Quaternion.identity;
    private bool _hasHeading;

    // F81 pose state (all applied additively in LateUpdate, world space).
    private Transform _spine;
    private Transform _chest;
    private Transform _neck;
    private Transform _head;
    private Transform _leftUpperArm;
    private Transform _rightUpperArm;
    private float _tiltSign = 1f;
    private float _headYaw;
    private float _headPitch;
    private float _twitchRoll;
    private float _twitchYaw;
    private int _twitchStepsLeft;
    private float _nextHeadTwitchAt;
    private float _lungeBlend;
    private float _nextLitTwitchAt;
    private float _litRoll;
    private float _litRollUntil;

    /// <summary>Stop-motion needs an Animator to gate; without one everything runs smooth.</summary>
    private bool Stepping => stopMotion && _animator != null;

    /// <summary>True while it is vanished after a touch (F82: StalkerVoice stays quiet then).</summary>
    public bool IsHiddenAfterTouch => _hiddenAfterTouch;

    /// <summary>World position of its head height (F82: where the speech bubble is anchored and sight is tested from).</summary>
    public Vector3 HeadWorldPosition => transform.position + Vector3.up * HeadHeight;

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

            // F81: the prefab culls off-screen Animators (CullUpdateTransforms), which stops them
            // rewriting bone transforms - our additive LateUpdate bone offsets would then compound every
            // frame and spin the head. One character, so always animate.
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (_animator.isHuman)
            {
                _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
                _chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
                if (_chest == null) _chest = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
                _neck = _animator.GetBoneTransform(HumanBodyBones.Neck);
                _head = _animator.GetBoneTransform(HumanBodyBones.Head);
                _leftUpperArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                _rightUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            }
        }

        // The robot's Animator lives on a child node yawed 90 degrees from the root (AIFollower's own
        // modelFacingYaw comment) - the NavMeshAgent must not drive rotation itself, or the mesh faces
        // sideways to its travel direction. ComputeHeading below turns the root to compensate.
        _agent.updateRotation = false;
        _modelYawOffset = _animator != null
            ? Vector3.SignedAngle(FlatDirection(transform.forward), FlatDirection(_animator.transform.forward), Vector3.up)
            : 0f;

        // F81: with stepped translation the agent simulates normally but the transform only follows it on
        // step frames (ApplyStepTransform), so the body visibly holds still, then hops.
        _baseAcceleration = _agent.acceleration;
        if (Stepping && stepBodyTranslation) _agent.updatePosition = false;

        _tiltSign = Random.value < 0.5f ? -1f : 1f;

        _renderers = GetComponentsInChildren<Renderer>(true);

        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 1f;
        _audio.maxDistance = 22f;
        _tickClip = DoorAudio.BuildClunkClip(0.12f, 500f, 0.3f);
        _shriekClip = DoorAudio.BuildClunkClip(0.5f, 900f, 0.75f);
        _skitterClip = DoorAudio.BuildClunkClip(0.18f, 1300f, 0.85f);

        _mode = Mode.Roam;
        _nextStepIn = NextStepInterval();
        _lastStepTime = Time.time;
        _nextHeadTwitchAt = Time.time + Random.Range(headTwitchIntervalMin, headTwitchIntervalMax);
        _nextLitTwitchAt = 0f;

        // F75: registers as a silent door-breaker - it slips under a closed shutter with no slam, no
        // stall (contrast AIFollower's own registration).
        DoorBreakers.Register(transform, _agent, true, null);

        // F82: Stan talks. The voice owns the speech bubble; its eyes flare per blipped letter through a
        // MaterialPropertyBlock (the shared Stalker_Eyes material is never touched).
        FindEyeRenderers();
        _voice = GetComponent<StalkerVoice>();
        if (_voice == null) _voice = gameObject.AddComponent<StalkerVoice>();
        _voice.Configure(this, phraseBook, player, torchCamera, stealth, hunter, outcome, _audio);

        _configured = true;
    }

    /// <summary>The two emissive eye spheres (names contain "Eye_Glow"); remembers the shared material's emission once.</summary>
    private void FindEyeRenderers()
    {
        List<Renderer> eyes = new List<Renderer>();
        foreach (Renderer renderer in _renderers)
        {
            if (renderer != null && renderer.name.Contains("Eye_Glow")) eyes.Add(renderer);
        }
        _eyeRenderers = eyes.ToArray();
        _eyeBaseEmission = new Color[_eyeRenderers.Length];
        for (int i = 0; i < _eyeRenderers.Length; i++)
        {
            Material shared = _eyeRenderers[i].sharedMaterial;
            _eyeBaseEmission[i] = shared != null && shared.HasProperty(EmissionColorId) ? shared.GetColor(EmissionColorId) : Color.white;
        }
        _eyeBlock = new MaterialPropertyBlock();
    }

    private void OnDestroy()
    {
        DoorBreakers.Unregister(transform);
    }

    private void Update()
    {
        _steppedThisFrame = false;
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
            UpdateMode();
            ComputeHeading();
            CheckTouch();
        }
        else
        {
            UpdateLitTwitch();
        }

        // CheckTouch may have hidden us (and disabled the agent) this frame.
        if (_hiddenAfterTouch) return;
        StepAnimation(lit);
    }

    private void Freeze()
    {
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
        }
        if (_animator != null) _animator.speed = 0f;
        CancelBehaviours();
        // F82: an ending clears any open bubble at once (not on the title-screen freeze - nothing to clear there).
        if (_voice != null && (GameOutcome.IsOver || (_outcome != null && _outcome.IsEnding))) _voice.Silence();
    }

    /// <summary>Ends any lunge/watch and returns to Roam; restores the agent's base acceleration.</summary>
    private void CancelBehaviours()
    {
        if (_agent != null) _agent.acceleration = _baseAcceleration;
        _mode = Mode.Roam;
        _watchRolled = false;
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
    internal bool HasClearLine(Vector3 from, Vector3 to)
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

    /// <summary>Edge bookkeeping and the agent freeze. The Animator's speed is no longer written here -
    /// StepAnimation owns it, so a lit Stan holds its mid-stride pose.</summary>
    private void ApplyLitState(bool lit)
    {
        if (lit && !_wasLit)
        {
            // "a single tick when it freezes within 8 m" - the moment it goes from moving to lit-frozen.
            float d = _player != null ? Vector3.Distance(transform.position, _player.position) : float.MaxValue;
            if (d <= SingleTickRange && _audio != null && _tickClip != null) _audio.PlayOneShot(_tickClip, 0.6f);

            // F82: only remarks on it if he can see you - "too bright" at your back would be nonsense.
            if (_voice != null && _voice.CanSeePlayer) _voice.Say(StalkerPhraseBook.Category.Lit);

            _litSince = Time.time;
            _nextLitTwitchAt = Time.time + Random.Range(litTwitchIntervalMin, litTwitchIntervalMax);
        }

        if (lit)
        {
            // Order matters: stop, kill velocity, then pull the agent's simulated position back to the
            // visible body (with stepped translation it can be a frame or more ahead of the transform).
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
            _agent.nextPosition = transform.position;
        }
        else
        {
            _agent.isStopped = false;

            // lit -> unlit: the light left it. If it was held long enough, close enough and the player is
            // not hiding, it lunges.
            if (_wasLit
                && Time.time - _litSince >= lungeMinLitSeconds
                && Time.time >= _nextLungeAllowedAt
                && FlatDistanceToPlayer() <= lungeTriggerRange
                && (_stealth == null || !_stealth.Hidden))
            {
                StartLunge();
            }
        }
        _wasLit = lit;
    }

    // ---------------------------------------------------------------- movement

    /// <summary>Unlit: repath every 0.25 s and pick the behaviour. Far away (&gt;30 m by path) it Roams,
    /// drifting toward the player's area at a slower pace; near it Stalks (the player's live position);
    /// a Watch (stop and stare) can interrupt a Stalk and ends in a Lunge, which a light leaving it can
    /// also trigger (StartLunge, from ApplyLitState).</summary>
    private void UpdateMode()
    {
        _repathTimer -= Time.deltaTime;
        bool repath = _repathTimer <= 0f;
        if (repath)
        {
            _repathTimer = RepathInterval;
            _far = _player == null || !TryPathLength(transform.position, _player.position, out float length) || length > FarPathDistance;
        }

        if (_mode == Mode.Lunge && Time.time >= _lungeEndsAt) EndLunge();

        if (_mode == Mode.Watch)
        {
            // ApplyLitState(false) sets isStopped = false every unlit frame, so stop *after* it, here.
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
            if (Time.time >= _watchEndsAt || FlatDistanceToPlayer() < TouchRadius + 0.5f)
            {
                StartLunge(); // the watch is the wind-up, so the lunge cooldown is ignored
                _agent.isStopped = false;
            }
            else
            {
                _agent.speed = stalkSpeed;
                return;
            }
        }

        if (_mode == Mode.Lunge)
        {
            _agent.speed = lungeSpeed;
            _agent.acceleration = lungeAcceleration;
            if ((repath || _repathTimer <= 0f) && _player != null)
            {
                _repathTimer = RepathInterval;
                _agent.SetDestination(_player.position);
            }
            return;
        }

        // Roam / Stalk follow _far; a Watch or Lunge in progress is never interrupted by it.
        if (repath)
        {
            _mode = _far ? Mode.Roam : Mode.Stalk;
            if (_far)
            {
                if (!_hasWanderTarget || ReachedDestination() || Time.time - _roamPickedAt > roamRepickSeconds) PickRoamTarget();
            }
            else
            {
                _hasWanderTarget = false;
                _agent.SetDestination(_player.position);
            }
        }

        _agent.speed = _mode == Mode.Roam ? roamSpeed : stalkSpeed;

        if (_mode == Mode.Stalk) TryStartWatch();
    }

    /// <summary>Stalk -> Watch: the player is looking at it in the dark from a middling distance, so
    /// sometimes it stops dead and stares before rushing. Rolled once per eligible window.</summary>
    private void TryStartWatch()
    {
        if (_torchCamera == null || _player == null) return;

        float flat = FlatDistanceToPlayer();
        Vector3 head = transform.position + Vector3.up * HeadHeight;
        bool eligible = flat >= watchMinDistance && flat <= watchMaxDistance
            && Vector3.Angle(_torchCamera.forward, head - _torchCamera.position) <= watchViewAngle
            && HasClearLine(_torchCamera.position, head);

        if (!eligible)
        {
            _watchRolled = false;
            return;
        }

        if (_watchRolled || Time.time < _nextWatchAllowedAt) return;

        _watchRolled = true;
        // Success or fail, the cooldown starts so a failed roll is not re-rolled every frame.
        _nextWatchAllowedAt = Time.time + watchCooldown;
        if (Random.value < watchChance)
        {
            _mode = Mode.Watch;
            _watchEndsAt = Time.time + Random.Range(watchSecondsMin, watchSecondsMax);
            if (_voice != null) _voice.Say(StalkerPhraseBook.Category.Staring);
        }
    }

    private void StartLunge()
    {
        _mode = Mode.Lunge;
        _lungeEndsAt = Time.time + lungeSeconds;
        _repathTimer = 0f;
        // Force a step on this very frame so the first jump is immediate (and at the lunge's step rate).
        _nextStepIn = 1f / Mathf.Max(1f, lungeStepFps);
        _stepAccum = _nextStepIn;
        if (_audio != null && _skitterClip != null) _audio.PlayOneShot(_skitterClip, 0.7f);
        if (_voice != null) _voice.Say(StalkerPhraseBook.Category.Lunge);
    }

    private void EndLunge()
    {
        _agent.acceleration = _baseAcceleration;
        _mode = Mode.Stalk;
        _nextLungeAllowedAt = Time.time + lungeCooldown;
    }

    /// <summary>Computes the root's desired facing from the agent's velocity (the same maths as before,
    /// compensating for the model's own yaw offset like AIFollower.UpdateRotation). Applied immediately
    /// when not stepping, otherwise on step frames by ApplyStepTransform. Only runs unlit, so rotation
    /// freezes along with everything else while lit.</summary>
    private void ComputeHeading()
    {
        Vector3 heading = _agent.velocity;
        heading.y = 0f;
        _hasHeading = heading.sqrMagnitude > 0.01f;
        if (!_hasHeading) return;

        _desiredRootRotation = Quaternion.LookRotation(heading.normalized, Vector3.up) * Quaternion.Euler(0f, -_modelYawOffset, 0f);
        if (!Stepping) transform.rotation = Quaternion.RotateTowards(transform.rotation, _desiredRootRotation, 720f * Time.deltaTime);
    }

    private static Vector3 FlatDirection(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    private float FlatDistanceToPlayer()
    {
        if (_player == null) return float.MaxValue;
        return FlatDirection(_player.position - transform.position).magnitude;
    }

    /// <summary>Roam target: of a handful of random cells at least roamMinDistanceToPlayer from the
    /// player, the nearest one - so it drifts toward you instead of wandering to random corners. If every
    /// candidate is too close, falls back to a uniformly random cell.</summary>
    private void PickRoamTarget()
    {
        if (_maze == null) return;
        IReadOnlyList<Vector3> centers = _maze.CellCenters;
        if (centers == null || centers.Count == 0) return;

        Vector3 target = centers[Random.Range(0, centers.Count)];
        if (_player != null)
        {
            bool found = false;
            float best = float.MaxValue;
            for (int i = 0; i < roamCandidates; i++)
            {
                Vector3 candidate = centers[Random.Range(0, centers.Count)];
                float d = FlatDirection(candidate - _player.position).magnitude;
                if (d < roamMinDistanceToPlayer || d >= best) continue;
                best = d;
                target = candidate;
                found = true;
            }
            if (!found) target = centers[Random.Range(0, centers.Count)];
        }

        _agent.SetDestination(target);
        _hasWanderTarget = true;
        _roamPickedAt = Time.time;
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

    // ---------------------------------------------------------------- stop-motion + animator

    private float NextStepInterval()
    {
        if (_mode == Mode.Lunge) return 1f / Mathf.Max(1f, lungeStepFps);
        return 1f / Mathf.Max(1f, Random.Range(stepFpsMin, stepFpsMax));
    }

    /// <summary>The stop-motion gate. The Animator is never disabled: while unlit an accumulator collects
    /// delta time and on a "step" frame animator.speed is set so the whole accumulated time plays in one
    /// go; on every other frame it is 0 - the Animator still evaluates and rewrites the full pose, which
    /// is what keeps the LateUpdate bone offsets additive instead of compounding. (This is the
    /// plan's "call UpdateAnimatorParams from StepAnimation on step frames" option, so Speed/MotionSpeed
    /// only change on steps and blend weights do not glide between them.) While lit it holds speed 0 and
    /// leaves the parameters alone, so the frozen pose stays mid-stride.</summary>
    private void StepAnimation(bool lit)
    {
        _lastStepDelta = 0f;
        if (_animator == null) return;

        if (lit)
        {
            _animator.speed = 0f;
            _stepAccum = 0f;
            _lastStepTime = Time.time;
            return;
        }

        if (!Stepping)
        {
            UpdateAnimatorParams();
            _animator.speed = 1f;
            _steppedThisFrame = true;
            _lastStepDelta = Time.deltaTime;
            _lastStepTime = Time.time;
            return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f)
        {
            _animator.speed = 0f;
            return;
        }

        _stepAccum += dt;
        if (_stepAccum >= _nextStepIn)
        {
            UpdateAnimatorParams();
            // Advance the whole accumulated time this frame; the cap stops a hitch from fast-forwarding the clip.
            _animator.speed = Mathf.Min(_stepAccum / dt, 30f);
            _stepAccum = 0f;
            _nextStepIn = NextStepInterval();
            _steppedThisFrame = true;
            _lastStepDelta = Mathf.Min(Time.time - _lastStepTime, 0.25f);
            _lastStepTime = Time.time;
            ApplyStepTransform();
        }
        else
        {
            _animator.speed = 0f;
        }
    }

    /// <summary>On a step frame: snap the visible body to the agent's simulated position (when translation
    /// is stepped) and catch the root's rotation up by the time since the last step.</summary>
    private void ApplyStepTransform()
    {
        if (!_agent.updatePosition) transform.position = _agent.nextPosition;
        if (Stepping && _hasHeading)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, _desiredRootRotation, 720f * _lastStepDelta);
        }
    }

    /// <summary>Only called on step frames (or every unlit frame with stop-motion off). Speed is scaled so
    /// the blend leans toward the run clip; MotionSpeed (the locomotion playback rate) follows the mode.</summary>
    private void UpdateAnimatorParams()
    {
        Vector3 flatVelocity = _agent.velocity;
        flatVelocity.y = 0f;
        float speed = flatVelocity.magnitude * animSpeedScale;

        float motion = _mode == Mode.Lunge ? lungeMotionSpeed : _mode == Mode.Roam ? roamMotionSpeed : stalkMotionSpeed;

        if (_hasSpeedParam) _animator.SetFloat(AnimSpeed, speed);
        if (_hasMotionSpeedParam) _animator.SetFloat(AnimMotionSpeed, motion);
        if (_hasGroundedParam) _animator.SetBool(AnimGrounded, true);
    }

    // ---------------------------------------------------------------- pose

    private void LateUpdate()
    {
        if (!_configured || _animator == null || _hiddenAfterTouch) return;
        ApplyPose();
        ApplyEyeFlare();
    }

    /// <summary>F82: eyes brighten x(1 + flare) while a speech letter blips - per-renderer property block
    /// over the base emission read once from the shared material. Written only when the value changes.</summary>
    private void ApplyEyeFlare()
    {
        if (_voice == null || _eyeRenderers == null) return;
        float flare = _voice.EyeFlare;
        if (Mathf.Approximately(flare, _appliedFlare)) return;
        _appliedFlare = flare;

        for (int i = 0; i < _eyeRenderers.Length; i++)
        {
            Renderer eye = _eyeRenderers[i];
            if (eye == null) continue;
            if (flare <= 0f)
            {
                eye.SetPropertyBlock(null);
                continue;
            }
            eye.GetPropertyBlock(_eyeBlock);
            _eyeBlock.SetColor(EmissionColorId, _eyeBaseEmission[i] * (1f + flare));
            eye.SetPropertyBlock(_eyeBlock);
        }
    }

    /// <summary>Weeping-angel micro-movement while lit: the head (only) snaps a few degrees and ticks
    /// quietly. No translation, no leg motion, no root rotation - the body freeze is untouched.</summary>
    private void UpdateLitTwitch()
    {
        if (!litTwitch || _player == null) return;
        if (Time.time < _nextLitTwitchAt) return;
        if (FlatDistanceToPlayer() > litTwitchRange) return;

        _headYaw = Mathf.Clamp(_headYaw + Random.Range(-1f, 1f) * litTwitchAngle, -headYawLimit, headYawLimit);
        _litRoll = (Random.value < 0.5f ? -1f : 1f) * litTwitchAngle;
        _litRollUntil = Time.time + 0.12f;
        if (_audio != null && _tickClip != null) _audio.PlayOneShot(_tickClip, 0.35f);
        _nextLitTwitchAt = Time.time + Random.Range(litTwitchIntervalMin, litTwitchIntervalMax);
    }

    /// <summary>Hunch, head tilt, head tracking, twitches and lunge arm reach. Everything is a world-space
    /// rotation about an axis taken from the model's visual frame (never a local euler - the rig's local
    /// axes are unknown), applied on top of the pose the Animator just wrote. Runs while lit too: the
    /// Animator keeps rewriting the frozen pose at speed 0, so offsets never compound. The state that
    /// feeds it (head aim, twitches, lunge blend) only changes on step frames while unlit, so it is
    /// stop-motion as well and simply holds while lit.</summary>
    private void ApplyPose()
    {
        // _animator.transform.forward (flattened) is the model's visual facing - see _modelYawOffset.
        Vector3 fwd = FlatDirection(_animator.transform.forward);
        if (fwd.sqrMagnitude < 0.0001f) return;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        if (_steppedThisFrame) AdvancePoseState(fwd);
        if (_litRoll != 0f && Time.time >= _litRollUntil) _litRoll = 0f;

        // Positive angle about 'right' pitches forward/down (Unity is left-handed: Euler(+x) looks down).
        Rot(_spine, right, spineHunch);
        Rot(_chest, right, chestHunch + lungeExtraHunch * _lungeBlend);
        Rot(_neck, right, neckForward);

        // Negative pitch swings a hanging arm forward. If it goes backwards on screen, flip this sign.
        float reach = -lungeArmReach * _lungeBlend;
        Rot(_leftUpperArm, right, reach);
        Rot(_rightUpperArm, right, reach);

        if (_head != null)
        {
            float yaw = _headYaw + _twitchYaw;
            Rot(_head, Vector3.up, yaw);
            Vector3 yawedRight = Quaternion.AngleAxis(yaw, Vector3.up) * right;
            Rot(_head, yawedRight, _headPitch);
            Vector3 headForward = Quaternion.AngleAxis(yaw, Vector3.up) * fwd;
            Rot(_head, headForward, _tiltSign * headTilt + _twitchRoll + _litRoll);
            // F82: the head dips on each blipped letter of a speech line (decays in StalkerVoice).
            if (_voice != null) Rot(_head, yawedRight, _voice.NodDegrees);
        }
    }

    /// <summary>Step-frame-only pose state: lunge blend, head twitch, and the head's aim at the camera
    /// (snapped, not eased - stop-motion).</summary>
    private void AdvancePoseState(Vector3 fwd)
    {
        _lungeBlend = Mathf.MoveTowards(_lungeBlend, _mode == Mode.Lunge ? 1f : 0f, 6f * _lastStepDelta);

        if (_twitchStepsLeft > 0)
        {
            _twitchStepsLeft--;
            if (_twitchStepsLeft == 0)
            {
                _twitchRoll = 0f;
                _twitchYaw = 0f;
            }
        }
        else if (Time.time >= _nextHeadTwitchAt)
        {
            _twitchRoll = Random.Range(-1f, 1f) * headTwitchAngle;
            _twitchYaw = Random.Range(-1f, 1f) * headTwitchAngle * 0.5f;
            _twitchStepsLeft = Random.Range(1, 3); // one or two steps
            _nextHeadTwitchAt = Time.time + Random.Range(headTwitchIntervalMin, headTwitchIntervalMax);
        }

        float targetYaw = 0f;
        float targetPitch = 0f;
        if (_torchCamera != null)
        {
            Vector3 head = transform.position + Vector3.up * HeadHeight;
            Vector3 toCamera = _torchCamera.position - head;
            Vector3 flat = FlatDirection(toCamera);
            if (flat.magnitude <= headTrackRange && flat.sqrMagnitude > 0.0001f)
            {
                targetYaw = Mathf.Clamp(Vector3.SignedAngle(fwd, flat, Vector3.up), -headYawLimit, headYawLimit);
                // Positive = look down (camera lower than the head).
                targetPitch = Mathf.Clamp(Mathf.Atan2(-toCamera.y, flat.magnitude) * Mathf.Rad2Deg, -headPitchLimit, headPitchLimit);
            }
        }
        _headYaw = targetYaw;
        _headPitch = targetPitch;
    }

    private static void Rot(Transform bone, Vector3 axis, float degrees)
    {
        if (bone != null && degrees != 0f) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
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
        // F82: the touch line replaces the subtitle (the shriek stays); the bubble finishes after he vanishes.
        if (_voice != null) _voice.Say(StalkerPhraseBook.Category.Touch);
        else _hud?.ShowSubtitle("IT SCREAMED.", 2.5f);

        HideAndRespawn();
    }

    private void HideAndRespawn()
    {
        SetRenderersVisible(false);
        // Ends any lunge/watch (and restores acceleration) before the agent is disabled.
        CancelBehaviours();
        _lungeBlend = 0f;
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
                // Warp moves the agent; with updatePosition off make sure the transform matches.
                transform.position = hit.position;
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
        _hasWanderTarget = false;
        _mode = Mode.Roam;
        _stepAccum = 0f;
        _lastStepTime = Time.time;
        _headYaw = 0f;
        _headPitch = 0f;
        _twitchRoll = 0f;
        _twitchYaw = 0f;
        _twitchStepsLeft = 0;
        _litRoll = 0f;
        _lungeBlend = 0f;
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

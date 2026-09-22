using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Makes the hunter perceptible before it is visible: heavy footsteps and a mechanical hum that carry
/// through walls, and two points of red where its eyes are.
///
/// The audio is deliberately not occluded. Hearing it through the wall it is behind is the mechanic,
/// not a bug - it is how the player localises it with the flashlight off.
/// </summary>
public class AIPresence : MonoBehaviour
{
    [Header("Footsteps")]
    [SerializeField] private AudioClip[] footstepClips;
    [Tooltip("Pitching the robot's own footsteps down this far turns them into something much heavier")]
    [SerializeField] private float minPitch = 0.55f;
    [SerializeField] private float maxPitch = 0.70f;
    [SerializeField] private float footstepVolume = 0.9f;
    [Tooltip("Metres of travel between footsteps")]
    [SerializeField] private float strideLength = 1.4f;
    [SerializeField] private float footstepMaxDistance = 25f;

    [Header("Hum")]
    [SerializeField] private AudioClip humClip;
    [SerializeField] private float humVolume = 0.25f;
    [SerializeField] private float humPitch = 0.4f;
    [SerializeField] private float humMaxDistance = 14f;

    [Header("Eyes")]
    [SerializeField] private bool spawnEyeGlow = true;
    [SerializeField] private Color eyeColor = new Color(1f, 0.05f, 0.03f);
    [Tooltip("Emission multiplier. Needs to be well above 1 to punch through fog and catch bloom.")]
    [SerializeField] private float eyeEmission = 4f;
    [SerializeField] private float eyeSize = 0.075f;
    [Tooltip("Bone name used to anchor the eyes/face light when the model is not Humanoid, or has no Head-mapped bone")]
    [SerializeField] private string headBoneName = "Head";
    [Tooltip("Eye bone names. Used when the rig actually has eye bones (the Timmy robot does); a rig without them (the zombie) falls back to an offset from the head instead.")]
    [SerializeField] private string leftEyeBoneName = "Left_Eye";
    [SerializeField] private string rightEyeBoneName = "Right_Eye";
    [Tooltip("Eye offset from the head bone when the rig has no eye bones, in the Animator's own frame (m)")]
    [SerializeField] private float eyeUp = 0.06f;
    [SerializeField] private float eyeForward = 0.11f;
    [SerializeField] private float eyeSpacing = 0.055f;

    [Header("Face")]
    [SerializeField] private float lurkingEyeEmissionScale = 0.3f;

    /// <summary>The mode the built eyes currently read as.</summary>
    public enum FaceMode
    {
        Normal,
        Lurking
    }

    private NavMeshAgent _agent;
    private AudioSource _footstepSource;
    private AudioSource _humSource;
    private float _distanceSinceStep;

    private Material _eyeMaterial;
    private readonly List<Transform> _eyes = new List<Transform>();
    private FaceMode _faceMode = FaceMode.Normal;
    private float _holdRemaining;
    private bool _humMuted;

    /// <summary>Called by MazeGenerator before Awake, so the clips can come from the player prefab.</summary>
    public void Configure(AudioClip[] footsteps, AudioClip hum, Material eyeMaterialSource)
    {
        if (footsteps != null && footsteps.Length > 0) footstepClips = footsteps;
        if (hum != null) humClip = hum;
        if (spawnEyeGlow && eyeMaterialSource != null) BuildEyes(eyeMaterialSource);
    }

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();

        _footstepSource = CreateSource("AI_Footsteps", footstepMaxDistance, false);
        _humSource = CreateSource("AI_Hum", humMaxDistance, true);
    }

    private void Start()
    {
        if (humClip != null)
        {
            _humSource.clip = humClip;
            _humSource.pitch = humPitch;
            _humSource.volume = humVolume;
            _humSource.Play();
        }
    }

    private void Update()
    {
        // Ticked before every early return below, so a Hold() during a footstep-free moment (the AI
        // standing still, or with no footstep clips) still releases on schedule.
        TickHold();

        if (_agent == null || footstepClips == null || footstepClips.Length == 0) return;

        // Driven by distance travelled rather than animation events: the AI's ThirdPersonController is
        // disabled so its OnFootstep never fires, and this makes the cadence track actual speed, which
        // is what tells the player it has started running.
        float speed = _agent.velocity.magnitude;
        if (speed < 0.1f)
        {
            return;
        }

        _distanceSinceStep += speed * Time.deltaTime;
        if (_distanceSinceStep < strideLength) return;

        _distanceSinceStep = 0f;
        _footstepSource.pitch = Random.Range(minPitch, maxPitch);
        _footstepSource.PlayOneShot(footstepClips[Random.Range(0, footstepClips.Length)], footstepVolume);
    }

    /// <summary>
    /// Mutes the hum for `seconds` (extends rather than restarts, so an overlapping telegraph and
    /// breath beat both simply keep it silent until the later of the two ends), then plays one
    /// footstep on release as the "it's back" beat.
    /// </summary>
    public void Hold(float seconds)
    {
        _holdRemaining = Mathf.Max(_holdRemaining, seconds);
        if (_humSource != null && !_humMuted)
        {
            _humMuted = true;
            _humSource.mute = true;
        }
    }

    private void TickHold()
    {
        if (_holdRemaining <= 0f) return;

        _holdRemaining -= Time.deltaTime;
        if (_holdRemaining > 0f) return;

        if (_humSource != null)
        {
            _humSource.mute = false;
        }
        _humMuted = false;

        if (_footstepSource != null && footstepClips != null && footstepClips.Length > 0)
        {
            _footstepSource.pitch = Random.Range(minPitch, maxPitch);
            _footstepSource.PlayOneShot(footstepClips[Random.Range(0, footstepClips.Length)], footstepVolume);
        }
    }

    /// <summary>
    /// Ends a Hold() early: the "it's back" footstep plays next frame instead of waiting out the full
    /// hold. Only acts on a genuine hold - forcing one on nothing held would fire a spurious footstep.
    /// </summary>
    public void ReleaseHold()
    {
        if (_holdRemaining > 0f) _holdRemaining = 0.001f;
    }

    /// <summary>Brightens/dims the eye glow. Used by F27's second lamp-death wave. Re-applies the current face mode, which multiplies this base.</summary>
    public void SetEyeEmission(float emission)
    {
        eyeEmission = emission;
        ApplyFaceMode();
    }

    /// <summary>Switches the eyes between idle and lurking (the ambush).</summary>
    public void SetFaceMode(FaceMode mode)
    {
        _faceMode = mode;
        ApplyFaceMode();
    }

    private void ApplyFaceMode()
    {
        float eyeEmissionScale = _faceMode == FaceMode.Lurking ? lurkingEyeEmissionScale : 1f;

        if (_eyeMaterial != null)
        {
            _eyeMaterial.SetColor("_EmissionColor", eyeColor * eyeEmission * eyeEmissionScale);
        }
    }

    /// <summary>Stop the hum and footsteps for good. Used once the run is over.</summary>
    public void Silence()
    {
        if (_humSource != null) _humSource.Stop();
        if (_footstepSource != null) _footstepSource.Stop();
        SetFaceMode(FaceMode.Normal);
        enabled = false;
    }

    private void OnDestroy()
    {
        if (_eyeMaterial != null) Destroy(_eyeMaterial);
    }

    private AudioSource CreateSource(string sourceName, float maxDistance, bool loop)
    {
        GameObject holder = new GameObject(sourceName);
        holder.transform.SetParent(transform, false);

        AudioSource source = holder.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2f;
        source.maxDistance = maxDistance;
        return source;
    }

    /// <summary>
    /// Builds the two glowing eyes and the face light. Works with either rig this project has used: the
    /// Timmy robot, which has actual Left_Eye/Right_Eye bones, and the zombie body, which only has a
    /// Head bone - eyes are then placed by a fixed offset from it, in the Animator's own frame, so the
    /// construction does not care which way the rig's rest pose happens to face.
    /// </summary>
    private void BuildEyes(Material emissiveSource)
    {
        // Spheres, not point lights: a small point light lights the wall beside it but is not itself
        // visible down a corridor, and seeing the eyes before the body is the entire point.
        Material eyeMaterial = new Material(emissiveSource);
        eyeMaterial.name = "AI_EyeGlow";
        eyeMaterial.EnableKeyword("_EMISSION");
        eyeMaterial.SetColor("_BaseColor", eyeColor);
        eyeMaterial.SetColor("_EmissionColor", eyeColor * eyeEmission);
        _eyeMaterial = eyeMaterial;

        Animator modelAnimator = GetComponentInChildren<Animator>();

        Transform head = modelAnimator != null && modelAnimator.isHuman
            ? modelAnimator.GetBoneTransform(HumanBodyBones.Head)
            : null;
        if (head == null) head = FindDeep(transform, headBoneName);

        // Pose the skeleton along the Animator's rest frame before reading any bone position - this
        // runs from PlaceAI inside Awake, before any animation frame has been evaluated, so without
        // this call the bones would still be in the FBX's raw import pose.
        modelAnimator?.Update(0f);

        // A Humanoid rig with mapped eye bones (Adam maps LeftEye/RightEye to his eye sclera meshes)
        // gets the red dots exactly in its sockets; anything else falls back to the name lookup.
        Transform leftEyeBone = null, rightEyeBone = null;
        if (modelAnimator != null && modelAnimator.isHuman)
        {
            leftEyeBone = modelAnimator.GetBoneTransform(HumanBodyBones.LeftEye);
            rightEyeBone = modelAnimator.GetBoneTransform(HumanBodyBones.RightEye);
        }
        if (leftEyeBone == null || rightEyeBone == null)
        {
            leftEyeBone = FindDeep(transform, leftEyeBoneName);
            rightEyeBone = FindDeep(transform, rightEyeBoneName);
        }

        Vector3 leftEyeWorld;
        Vector3 rightEyeWorld;
        Transform leftParent;
        Transform rightParent;

        if (leftEyeBone != null && rightEyeBone != null)
        {
            leftEyeWorld = leftEyeBone.position;
            rightEyeWorld = rightEyeBone.position;
            leftParent = leftEyeBone;
            rightParent = rightEyeBone;
        }
        else if (head != null)
        {
            // The face's own frame, not the Animator node's: a Generic rig can face sideways inside
            // its node (AIFollower.modelFacingYaw), and the eyes must sit on the face.
            AIFollower follower = GetComponent<AIFollower>();
            Vector3 forward = follower != null ? follower.VisualForward
                : (modelAnimator != null ? modelAnimator.transform.forward : transform.forward);
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            Vector3 forwardOffset = forward * eyeForward;
            Vector3 upOffset = Vector3.up * eyeUp;
            Vector3 rightOffset = right * eyeSpacing;

            leftEyeWorld = head.position + forwardOffset + upOffset - rightOffset;
            rightEyeWorld = head.position + forwardOffset + upOffset + rightOffset;
            leftParent = head;
            rightParent = head;
        }
        else
        {
            Debug.LogWarning("AIPresence: no eye bones and no Head bone found on the model; eyes are skipped, SetFaceMode will have nothing to drive.", this);
            return;
        }

        Transform leftEye = BuildEye("LeftEye_Glow", leftEyeWorld, leftParent, eyeMaterial);
        Transform rightEye = BuildEye("RightEye_Glow", rightEyeWorld, rightParent, eyeMaterial);
        _eyes.Add(leftEye);
        _eyes.Add(rightEye);
    }

    private Transform BuildEye(string eyeName, Vector3 worldPosition, Transform parent, Material material)
    {
        GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eye.name = eyeName;
        Destroy(eye.GetComponent<Collider>());
        eye.transform.SetPositionAndRotation(worldPosition, parent.rotation);
        eye.transform.SetParent(parent, true);
        // eyeSize is a world size. A rig exported with a scaled armature root (ZombieSmooth.fbx carries
        // 107x on its root node) would otherwise turn a 7.5 cm sphere into an 8 m one.
        eye.transform.localScale = WorldToLocalScale(parent, eyeSize);

        MeshRenderer renderer = eye.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return eye.transform;
    }

    /// <summary>Local scale that gives a uniform world size of `worldSize` under `parent`, whatever the parent chain's scale is.</summary>
    private static Vector3 WorldToLocalScale(Transform parent, float worldSize)
    {
        Vector3 lossy = parent.lossyScale;
        return new Vector3(
            worldSize / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
            worldSize / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            worldSize / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
    }

    private static Transform FindDeep(Transform root, string childName)
    {
        if (root.name == childName) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), childName);
            if (found != null) return found;
        }

        return null;
    }
}

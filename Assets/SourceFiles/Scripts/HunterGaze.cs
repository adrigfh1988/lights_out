using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F50: aims the hunter's head and neck at whatever it is currently paying attention to, on top of
/// whatever the Locomotion/LookAround/Lurk clip is already doing underneath.
///
/// Target priority (see ResolveTarget): the player's camera while the follower has sight or has just
/// caught them; the last known position while searching, or chasing blind after losing sight; a slow
/// sweep point in front of the follower while looking around an empty search spot; the ambushed star
/// while lying in wait; otherwise nothing, and the head blends back to the clip's own pose.
///
/// Runs in LateUpdate, after Animator.Update has already written this frame's bone pose - applying the
/// aim any earlier would just be overwritten. Added to the body instance, and wired to its AIFollower,
/// by LIGHTS OUT > Build Hunter Body; presentation only, same as the rest of F50 - it never reads or
/// writes anything perception/capture cares about beyond the read-only getters AIFollower exposes for it.
/// </summary>
public class HunterGaze : MonoBehaviour
{
    [Tooltip("The hunter this gaze belongs to. Wired by HunterBodyBuilder.")]
    [SerializeField] private AIFollower follower;

    [Header("Clamp")]
    [Tooltip("Max yaw off the chest's forward (degrees)")]
    [SerializeField] private float yawClamp = 70f;
    [Tooltip("Max pitch off the chest's forward (degrees)")]
    [SerializeField] private float pitchClamp = 25f;

    [Header("Trim")]
    [Tooltip("Constant yaw added to every aim (degrees, positive = right). For nulling out a last few degrees by eye in Play; the value survives Play mode only if copied back.")]
    [SerializeField] private float yawTrim = 0f;
    [Tooltip("Constant pitch added to every aim (degrees, positive = down)")]
    [SerializeField] private float pitchTrim = 0f;

    [Header("Blend")]
    [Tooltip("Seconds to blend onto a newly acquired target")]
    [SerializeField] private float blendInSeconds = 0.4f;
    [Tooltip("Seconds to blend back to the clip's own pose once there is nothing to look at")]
    [SerializeField] private float blendOutSeconds = 0.6f;
    [Tooltip("How much of the aim the head bone takes, of the two")]
    [Range(0f, 1f)] [SerializeField] private float headWeight = 0.7f;
    [Tooltip("How much of the aim the neck bone takes, of the two")]
    [Range(0f, 1f)] [SerializeField] private float neckWeight = 0.3f;

    [Header("Search sweep")]
    [Tooltip("Distance in front of the chest the LookAround sweep point sits at")]
    [SerializeField] private float sweepDistance = 3f;
    [Tooltip("Height the last-known-position and ambush-star targets aim at, above the ground")]
    [SerializeField] private float targetHeight = 1.6f;

    private Animator _animator;
    private Transform _head;
    private Transform _neck;
    private Camera _camera;

    // Measured once in Start from the avatar's T-pose (see TPoseWorldRotation), so the aim is added on
    // top of the clip instead of replacing it outright, and no clip's own head turn leaks into it.
    private Quaternion _headRestOffset = Quaternion.identity;
    private Quaternion _neckRestOffset = Quaternion.identity;

    private float _weight;
    private Quaternion _lastHeadRotation = Quaternion.identity;
    private Quaternion _lastNeckRotation = Quaternion.identity;
    private bool _haveAim;

    private void Start()
    {
        _animator = GetComponentInChildren<Animator>();
        if (_animator == null || !_animator.isHuman)
        {
            Debug.LogWarning("HunterGaze: no Humanoid Animator found on this body; disabling.", this);
            enabled = false;
            return;
        }

        _head = _animator.GetBoneTransform(HumanBodyBones.Head);
        _neck = _animator.GetBoneTransform(HumanBodyBones.Neck);

        if (_head == null)
        {
            Debug.LogWarning("HunterGaze: rig is missing a Head bone; disabling.", this);
            enabled = false;
            return;
        }

        // The rest offset is "how the bone sits relative to a body facing straight ahead". Two things
        // matter about how it is measured:
        //  1. Against the body's actual facing, never against a bone's own forward axis. Adam is a 3ds
        //     Max Biped rig, whose bones point their local axes along the bone rather than the way the
        //     character faces, so chest.forward is roughly 90 degrees off.
        //  2. From the avatar's T-pose, not from whatever the idle clip happens to be doing at Start. A
        //     clip that holds the head a few degrees to one side would otherwise bake that turn into the
        //     offset, and the head would look that many degrees past every target for the whole run.
        // In the Humanoid T-pose the character faces the Animator's +Z, so that is the frame to use.
        Quaternion bodyLook = Quaternion.LookRotation(FlatForward(_animator.transform.forward), Vector3.up);
        _headRestOffset = Quaternion.Inverse(bodyLook) * TPoseWorldRotation(_head);
        _neckRestOffset = _neck != null ? Quaternion.Inverse(bodyLook) * TPoseWorldRotation(_neck) : Quaternion.identity;
    }

    /// <summary>
    /// The bone's world rotation in the avatar's T-pose: the product of the avatar's stored local
    /// rotations from the Animator's own transform down to the bone, under the Animator's current world
    /// rotation. Falls back to the bone's current rotation (the old, pose-dependent measurement) if the
    /// avatar carries no skeleton description.
    /// </summary>
    private Quaternion TPoseWorldRotation(Transform bone)
    {
        Avatar avatar = _animator.avatar;
        SkeletonBone[] skeleton = avatar != null ? avatar.humanDescription.skeleton : null;
        if (skeleton == null || skeleton.Length == 0) return bone.rotation;

        Dictionary<string, Quaternion> tPoseLocal = new Dictionary<string, Quaternion>(skeleton.Length);
        foreach (SkeletonBone entry in skeleton)
        {
            tPoseLocal[entry.name] = entry.rotation;
        }

        Quaternion chain = Quaternion.identity;
        for (Transform t = bone; t != null && t != _animator.transform; t = t.parent)
        {
            // A node the avatar does not describe keeps whatever it has now (it is not animated anyway).
            Quaternion local = tPoseLocal.TryGetValue(t.name, out Quaternion stored) ? stored : t.localRotation;
            chain = local * chain;
        }

        return _animator.transform.rotation * chain;
    }

    private void LateUpdate()
    {
        if (follower == null || _head == null) return;

        if (_camera == null)
        {
            ResolveCamera();
        }

        bool stunned = follower.CurrentPose == AIFollower.Pose.Stunned;

        if (!stunned)
        {
            Vector3? target = ResolveTarget();

            float blendSeconds = target.HasValue ? blendInSeconds : blendOutSeconds;
            float desired = target.HasValue ? 1f : 0f;
            _weight = blendSeconds > 0.0001f
                ? Mathf.MoveTowards(_weight, desired, Time.deltaTime / blendSeconds)
                : desired;

            if (target.HasValue)
            {
                Vector3 toTarget = target.Value - _head.position;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    Quaternion chestLook = Quaternion.LookRotation(FlatForward(follower.VisualForward), Vector3.up);
                    Quaternion aim = ClampToChest(Quaternion.LookRotation(toTarget, Vector3.up), chestLook)
                                     * Quaternion.Euler(pitchTrim, yawTrim, 0f);

                    _lastHeadRotation = aim * _headRestOffset;
                    _lastNeckRotation = _neck != null ? aim * _neckRestOffset : Quaternion.identity;
                    _haveAim = true;
                }
            }
        }
        // Stunned: weight and the cached aim are left exactly as they were. The Animator still rewrites
        // the bones to the clip's raw pose every frame, so re-applying the same cached rotation below is
        // what actually holds the head "frozen" rather than letting it snap back to the clip.

        if (!_haveAim || _weight <= 0.0001f) return;

        // Neck first, then head: the neck is the head's parent, so writing the head's world rotation and
        // then rotating the neck would drag the head past its aim by the neck's share.
        if (_neck != null)
        {
            _neck.rotation = Quaternion.Slerp(_neck.rotation, _lastNeckRotation, _weight * neckWeight);
        }
        _head.rotation = Quaternion.Slerp(_head.rotation, _lastHeadRotation, _weight * headWeight);
    }

    /// <summary>Decision 6's target priority. Null means "nothing to look at" - the weight blends back out and the clip's own head pose takes over.</summary>
    private Vector3? ResolveTarget()
    {
        switch (follower.CurrentPose)
        {
            case AIFollower.Pose.Lurk:
                return follower.AmbushStarPosition + Vector3.up * targetHeight;
            case AIFollower.Pose.LookAround:
                return SweepPoint();
        }

        if (follower.HasSight || follower.IsCaptured)
        {
            return _camera != null ? _camera.transform.position : (Vector3?)null;
        }

        // The lose-sight grace: while still in Chase without sight, or while Searching, it keeps staring
        // at the corner it last saw the player round rather than snapping to face wherever it is walking.
        if (follower.IsSearching || (follower.IsChasing && !follower.HasSight))
        {
            return follower.LastKnownPosition + Vector3.up * targetHeight;
        }

        return null;
    }

    /// <summary>
    /// A point ahead of the follower that sweeps side to side with CanSeeTarget's own _scanYawOffset, so
    /// the head visibly does the same sweep the sight cone is doing during an empty search dwell.
    /// </summary>
    private Vector3 SweepPoint()
    {
        Vector3 direction = Quaternion.AngleAxis(follower.ScanYawOffset, Vector3.up) * FlatForward(follower.VisualForward);
        return follower.transform.position + Vector3.up * targetHeight + direction.normalized * sweepDistance;
    }

    private void ResolveCamera()
    {
        Transform playerTarget = follower.Target;
        if (playerTarget != null)
        {
            FirstPersonRig rig = playerTarget.GetComponent<FirstPersonRig>();
            if (rig != null && rig.PlayerCamera != null)
            {
                _camera = rig.PlayerCamera;
                return;
            }
        }

        // Retried every LateUpdate until it resolves: HunterGaze.Start and AIFollower.Start have no
        // defined order relative to each other, so `target` (and its FirstPersonRig) may not be ready yet
        // the first few frames.
        _camera = Camera.main;
    }

    /// <summary>Clamps `aim` to within yawClamp/pitchClamp of `chestLook`, keeping aim's own roll-free frame.</summary>
    private Quaternion ClampToChest(Quaternion aim, Quaternion chestLook)
    {
        Quaternion relative = Quaternion.Inverse(chestLook) * aim;
        Vector3 euler = relative.eulerAngles;
        float yaw = Mathf.Clamp(NormalizeAngle(euler.y), -yawClamp, yawClamp);
        float pitch = Mathf.Clamp(NormalizeAngle(euler.x), -pitchClamp, pitchClamp);
        return chestLook * Quaternion.Euler(pitch, yaw, 0f);
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle;
    }

    private static Vector3 FlatForward(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }
}

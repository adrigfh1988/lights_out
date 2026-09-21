using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

/// <summary>
/// F26: scheduled, weighted, gameplay-inert fakes - a silhouette that vanishes in the torch beam,
/// footsteps that stop a beat after you do, a lamp dying beside you, a whisper in one ear. None of them
/// carry a collider, raise a noise event, or touch the hunter's state; a fake must never mask a real cue
/// that is about to matter, which is why they refuse to fire near the hunter or during a relocation
/// telegraph (DreadDirector.IsTelegraphing).
///
/// No execution-order attribute - everything it needs is handed in through Configure, after MazeGenerator
/// has built the world. Added at runtime, so OnEnable runs during AddComponent, before Configure.
/// </summary>
public class PhantomDirector : MonoBehaviour
{
    private enum PhantomType
    {
        Silhouette,
        Footsteps,
        LampBlink,
        Whisper
    }

    [Header("Scheduling")]
    [Tooltip("Mean seconds between phantoms at full progress. Overridden per floor by FloorProfile.PhantomInterval when a profile is bound.")]
    [SerializeField] private float phantomInterval = 18f;
    [Tooltip("Minimum seconds between two phantoms, even if the roll comes up short")]
    [SerializeField] private float minSpacing = 8f;
    [Tooltip("No phantom fires while the hunter is this close or closer (m) - a fake must not mask something real")]
    [SerializeField] private float hunterExclusionDistance = 12f;
    [Tooltip("A skipped check is retried after interval x this fraction, rather than waiting a full interval")]
    [SerializeField] private float failRetryFraction = 0.5f;

    [Header("Weights (relative, need not sum to anything in particular)")]
    [SerializeField] private float silhouetteWeight = 3f;
    [SerializeField] private float footstepsWeight = 3f;
    [SerializeField] private float lampBlinkWeight = 2f;
    [SerializeField] private float whisperWeight = 2f;
    [Tooltip("Multiplier on the silhouette's weight while the torch is off - then only the eyes are visible")]
    [SerializeField] private float silhouetteTorchOffMultiplier = 2f;

    [Header("Silhouette")]
    [SerializeField] private float silhouetteMinDistance = 10f;
    [SerializeField] private float silhouetteMaxDistance = 22f;
    [Tooltip("How close to the hunter a silhouette cell may not be (m)")]
    [SerializeField] private float silhouetteHunterExclusion = 4f;
    [SerializeField] private float silhouetteLifetimeMin = 0.8f;
    [SerializeField] private float silhouetteLifetimeMax = 1.6f;
    [Tooltip("Torch beam catches it inside this fraction of the beam's outer angle")]
    [SerializeField] private float silhouetteBeamAngleFraction = 0.45f;
    [Tooltip("...and inside this fraction of the beam's range")]
    [SerializeField] private float silhouetteBeamRangeFraction = 0.9f;

    [Header("Footsteps behind you")]
    [SerializeField] private float footstepDistanceBehind = 2.5f;
    [SerializeField] private float footstepStrideLength = 1.4f;
    [SerializeField] private float footstepPitch = 0.85f;
    [SerializeField] private float footstepVolume = 0.35f;
    [SerializeField] private float footstepMinDistance = 1f;
    [SerializeField] private float footstepMaxDistance = 12f;
    [SerializeField] private float footstepDurationMin = 4f;
    [SerializeField] private float footstepDurationMax = 7f;
    [Tooltip("How late the final step lands after the player stops (s) - that lateness is the beat")]
    [SerializeField] private float footstepLateStepDelay = 0.35f;

    [Header("Lamp beside you dies")]
    [SerializeField] private float lampBlinkRadius = 6f;
    [SerializeField] private float lampBlinkSeconds = 0.45f;

    private MazeGenerator _maze;
    private Transform _player;
    private Camera _camera;
    private AIFollower _follower;
    private HorrorAudioDirector _audio;
    private Flashlight _flashlight;
    private PlayerStealthState _stealth;
    private GameOutcome _outcome;
    private DreadDirector _dread;
    private AudioClip[] _footstepClips;

    private GameObject _phantomsRoot;
    private Material _silhouetteMaterial;
    private Material _eyeMaterial;

    private readonly RaycastHit[] _rayHits = new RaycastHit[16];

    private float _timer;
    private float _lastPhantomTime = -9999f;
    private int _collected;
    private int _total;

    private float Progress => _total > 0 ? _collected / (float)_total : 0f;

    private void Awake()
    {
        _phantomsRoot = new GameObject("Phantoms");
        _phantomsRoot.transform.SetParent(transform, false);
    }

    public void Configure(MazeGenerator maze, Transform player, Camera camera, AIFollower follower, HorrorAudioDirector audio,
        Flashlight flashlight, PlayerStealthState stealth, GameOutcome outcome, DreadDirector dread,
        Material wallMat, Material starMat, AudioClip[] footsteps, FloorProfile profile)
    {
        _maze = maze;
        _player = player;
        _camera = camera;
        _follower = follower;
        _audio = audio;
        _flashlight = flashlight;
        _stealth = stealth;
        _outcome = outcome;
        _dread = dread;
        _footstepClips = footsteps;

        // Its own dark copies rather than MazeGenerator's runtime material list, so it owns their
        // lifetime and destroys them itself in OnDestroy.
        if (_silhouetteMaterial == null) _silhouetteMaterial = BuildSilhouetteMaterial(wallMat);
        if (_eyeMaterial == null) _eyeMaterial = BuildEyeMaterial(starMat);

        if (profile != null) phantomInterval = profile.PhantomInterval;

        _timer = ComputeInterval(Progress) * Random.Range(0.6f, 1.4f);
    }

    private void OnEnable()
    {
        GameManager.ProgressChanged += HandleProgress;
    }

    private void OnDisable()
    {
        GameManager.ProgressChanged -= HandleProgress;

        StopAllCoroutines();
        if (_phantomsRoot != null)
        {
            for (int i = _phantomsRoot.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(_phantomsRoot.transform.GetChild(i).gameObject);
            }
        }
    }

    private void OnDestroy()
    {
        if (_silhouetteMaterial != null) Destroy(_silhouetteMaterial);
        if (_eyeMaterial != null) Destroy(_eyeMaterial);
    }

    private void HandleProgress(int collected, int total)
    {
        _collected = collected;
        _total = total;
    }

    private void Update()
    {
        if (_maze == null || _player == null) return;

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;

        bool fired = TryFirePhantom();
        _timer = fired
            ? ComputeInterval(Progress) * Random.Range(0.6f, 1.4f)
            : ComputeInterval(Progress) * failRetryFraction;
    }

    private float ComputeInterval(float progress)
    {
        return Mathf.Lerp(phantomInterval * 1.5f, phantomInterval, progress);
    }

    private bool BailedOut()
    {
        return GameOutcome.IsOver || (_outcome != null && _outcome.IsEnding) || !GameFlow.IsRunActive;
    }

    private bool PassesGate()
    {
        if (_maze == null || _player == null || _follower == null) return false;
        if (BailedOut()) return false;
        if (_follower.IsChasing) return false;
        // F44: a stare is already the real thing - a phantom firing during one would undercut it.
        if (_follower.IsStaring) return false;
        if (_follower.FlatDistanceToTarget <= hunterExclusionDistance) return false;
        if (_stealth != null && _stealth.Hidden) return false;
        if (Time.time - _lastPhantomTime < minSpacing) return false;
        if (_dread != null && _dread.IsTelegraphing) return false;

        return true;
    }

    private bool TryFirePhantom()
    {
        if (!PassesGate()) return false;

        bool fired;
        switch (PickType())
        {
            case PhantomType.Silhouette:
                fired = TrySpawnSilhouette();
                break;
            case PhantomType.Footsteps:
                fired = TryStartFootsteps();
                break;
            case PhantomType.LampBlink:
                fired = TryBlinkLamp();
                break;
            default:
                fired = TryWhisper();
                break;
        }

        if (fired) _lastPhantomTime = Time.time;
        return fired;
    }

    private PhantomType PickType()
    {
        float silhouette = silhouetteWeight * (_flashlight != null && !_flashlight.IsOn ? silhouetteTorchOffMultiplier : 1f);
        float total = silhouette + footstepsWeight + lampBlinkWeight + whisperWeight;

        float r = Random.value * total;
        if (r < silhouette) return PhantomType.Silhouette;
        r -= silhouette;
        if (r < footstepsWeight) return PhantomType.Footsteps;
        r -= footstepsWeight;
        if (r < lampBlinkWeight) return PhantomType.LampBlink;
        return PhantomType.Whisper;
    }

    // ---------------------------------------------------------------- silhouette

    private bool TrySpawnSilhouette()
    {
        if (_camera == null || _maze == null) return false;

        IReadOnlyList<Vector3> cells = _maze.CellCenters;
        List<Vector3> candidates = new List<Vector3>();

        for (int i = 0; i < cells.Count; i++)
        {
            Vector3 cell = cells[i];
            float distance = Vector3.Distance(_camera.transform.position, cell);
            if (distance < silhouetteMinDistance || distance > silhouetteMaxDistance) continue;

            Vector3 viewport = _camera.WorldToViewportPoint(cell);
            if (viewport.z <= 0f) continue;
            if (viewport.x < 0.15f || viewport.x > 0.85f || viewport.y < 0.15f || viewport.y > 0.85f) continue;

            if (_follower != null && Vector3.Distance(_follower.transform.position, cell) < silhouetteHunterExclusion) continue;

            if (!IsRayClear(_camera.transform.position, cell + Vector3.up * 1.2f)) continue;

            candidates.Add(cell);
        }

        if (candidates.Count == 0) return false;

        SpawnSilhouette(candidates[Random.Range(0, candidates.Count)]);
        return true;
    }

    private void SpawnSilhouette(Vector3 position)
    {
        GameObject root = new GameObject("Phantom_Silhouette");
        root.transform.SetParent(_phantomsRoot.transform, false);
        root.transform.position = position;

        GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.name = "Body";
        Collider capsuleCollider = capsule.GetComponent<Collider>();
        if (capsuleCollider != null)
        {
            capsuleCollider.enabled = false;
            Destroy(capsuleCollider);
        }
        capsule.transform.SetParent(root.transform, false);
        capsule.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        capsule.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);

        Renderer bodyRenderer = capsule.GetComponent<Renderer>();
        if (bodyRenderer != null)
        {
            if (_silhouetteMaterial != null) bodyRenderer.sharedMaterial = _silhouetteMaterial;
            bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
            bodyRenderer.receiveShadows = false;
        }

        if (_eyeMaterial != null)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                eye.name = "Eye";
                Collider eyeCollider = eye.GetComponent<Collider>();
                if (eyeCollider != null)
                {
                    eyeCollider.enabled = false;
                    Destroy(eyeCollider);
                }
                eye.transform.SetParent(root.transform, false);
                eye.transform.localPosition = new Vector3(side * 0.1f, 1.55f, 0.2f);
                eye.transform.localScale = Vector3.one * 0.06f;

                Renderer eyeRenderer = eye.GetComponent<Renderer>();
                eyeRenderer.sharedMaterial = _eyeMaterial;
                eyeRenderer.shadowCastingMode = ShadowCastingMode.Off;
                eyeRenderer.receiveShadows = false;
            }
        }

        StartCoroutine(SilhouetteRoutine(root, position));
    }

    private IEnumerator SilhouetteRoutine(GameObject root, Vector3 position)
    {
        float lifetime = Random.Range(silhouetteLifetimeMin, silhouetteLifetimeMax);
        float t = 0f;

        while (t < lifetime)
        {
            if (BailedOut()) break;

            // Vanish the instant the torch beam actually reaches it, whatever the random lifetime says.
            if (_flashlight != null && _flashlight.IsOn && _camera != null)
            {
                Vector3 dir = position - _camera.transform.position;
                float distance = dir.magnitude;
                if (distance < _flashlight.Range * silhouetteBeamRangeFraction
                    && Vector3.Angle(_camera.transform.forward, dir) < _flashlight.OuterAngle * silhouetteBeamAngleFraction)
                {
                    break;
                }
            }

            t += Time.deltaTime;
            yield return null;
        }

        if (root != null) Destroy(root);
    }

    // ---------------------------------------------------------------- footsteps behind you

    private bool TryStartFootsteps()
    {
        if (_footstepClips == null || _footstepClips.Length == 0 || _camera == null || _player == null) return false;

        GameObject holder = new GameObject("Phantom_Footsteps");
        holder.transform.SetParent(_phantomsRoot.transform, false);

        AudioSource source = holder.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = footstepMinDistance;
        source.maxDistance = footstepMaxDistance;
        source.pitch = footstepPitch;

        StartCoroutine(FootstepsRoutine(holder, source));
        return true;
    }

    private IEnumerator FootstepsRoutine(GameObject holder, AudioSource source)
    {
        float duration = Random.Range(footstepDurationMin, footstepDurationMax);
        // Offset half a stride so the first step does not land right on spawn.
        float distanceSinceStep = footstepStrideLength * 0.5f;
        Vector3 lastPlayerPos = _player.position;
        float t = 0f;

        while (t < duration)
        {
            if (BailedOut())
            {
                if (holder != null) Destroy(holder);
                yield break;
            }

            // Cut instantly, no late step: a chase starting is the real thing, and a phantom must
            // never be mistaken for it.
            if (_follower != null && _follower.IsChasing)
            {
                if (holder != null) Destroy(holder);
                yield break;
            }

            if (_camera != null)
            {
                Vector3 back = -_camera.transform.forward;
                back.y = 0f;
                if (back.sqrMagnitude < 0.0001f) back = -_camera.transform.right;
                back.Normalize();

                Vector3 pos = _camera.transform.position + back * footstepDistanceBehind;
                pos.y = _player.position.y;
                holder.transform.position = pos;
            }

            Vector3 playerPos = _player.position;
            distanceSinceStep += Vector3.Distance(playerPos, lastPlayerPos);
            lastPlayerPos = playerPos;

            if (distanceSinceStep >= footstepStrideLength)
            {
                distanceSinceStep = 0f;
                PlayFootstep(source);
            }

            if (_stealth != null && _stealth.NoiseRadius <= 0f) break;

            t += Time.deltaTime;
            yield return null;
        }

        // Whether it ended because the player stopped or because the duration simply ran out, the
        // ending is the same: the beat that sells it as something that was following you.
        float lateT = 0f;
        while (lateT < footstepLateStepDelay)
        {
            if (BailedOut() || (_follower != null && _follower.IsChasing))
            {
                if (holder != null) Destroy(holder);
                yield break;
            }
            lateT += Time.deltaTime;
            yield return null;
        }

        PlayFootstep(source);

        yield return new WaitForSeconds(1f);
        if (holder != null) Destroy(holder);
    }

    private void PlayFootstep(AudioSource source)
    {
        if (_footstepClips == null || _footstepClips.Length == 0) return;
        source.PlayOneShot(_footstepClips[Random.Range(0, _footstepClips.Length)], footstepVolume);
    }

    // ---------------------------------------------------------------- lamp beside you dies

    private bool TryBlinkLamp()
    {
        if (_maze == null || _player == null) return false;

        IReadOnlyList<WallLamp> lamps = _maze.Lamps;
        WallLamp nearest = null;
        float nearestDistance = lampBlinkRadius;

        for (int i = 0; i < lamps.Count; i++)
        {
            WallLamp lamp = lamps[i];
            if (lamp == null || lamp.IsDead) continue;

            float distance = Vector3.Distance(lamp.transform.position, _player.position);
            if (distance <= nearestDistance)
            {
                nearestDistance = distance;
                nearest = lamp;
            }
        }

        if (nearest == null) return false;

        nearest.Blink(lampBlinkSeconds);
        return true;
    }

    // ---------------------------------------------------------------- whisper

    private bool TryWhisper()
    {
        if (_audio == null) return false;

        float pan = Random.value < 0.5f ? -0.9f : 0.9f;
        _audio.PlayWhisper(pan);
        return true;
    }

    // ---------------------------------------------------------------- shared helpers

    private bool IsRayClear(Vector3 from, Vector3 to)
    {
        Vector3 diff = to - from;
        float distance = diff.magnitude;
        if (distance < 0.01f) return true;

        int count = Physics.RaycastNonAlloc(from, diff / distance, _rayHits, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform hit = _rayHits[i].transform;
            if (_player != null && IsPartOf(hit, _player)) continue;
            if (_follower != null && IsPartOf(hit, _follower.transform)) continue;
            return false;
        }

        return true;
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

    private static Material BuildSilhouetteMaterial(Material source)
    {
        if (source == null) return null;

        Material copy = new Material(source) { name = "Phantom_Silhouette" };
        copy.DisableKeyword("_EMISSION");
        copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        Color tint = new Color(0.02f, 0.02f, 0.025f);
        if (copy.HasProperty("_EmissionColor")) copy.SetColor("_EmissionColor", Color.black);
        if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", tint);
        if (copy.HasProperty("_Color")) copy.SetColor("_Color", tint);

        return copy;
    }

    private static Material BuildEyeMaterial(Material source)
    {
        if (source == null) return null;

        Material copy = new Material(source) { name = "Phantom_Eyes" };
        copy.EnableKeyword("_EMISSION");
        Color eyeColor = new Color(1f, 0.05f, 0.03f);
        if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", eyeColor);
        if (copy.HasProperty("_Color")) copy.SetColor("_Color", eyeColor);
        // Dimmer than the real eyes (AIPresence.eyeEmission 4) - a phantom should read as almost right.
        copy.SetColor("_EmissionColor", eyeColor * 2.5f);

        return copy;
    }
}

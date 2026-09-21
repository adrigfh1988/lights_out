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

    private NavMeshAgent _agent;
    private AudioSource _footstepSource;
    private AudioSource _humSource;
    private float _distanceSinceStep;

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

    /// <summary>Stop the hum and footsteps for good. Used once the run is over.</summary>
    public void Silence()
    {
        if (_humSource != null) _humSource.Stop();
        if (_footstepSource != null) _footstepSource.Stop();
        enabled = false;
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

    private void BuildEyes(Material emissiveSource)
    {
        // Spheres, not point lights: a small point light lights the wall beside it but is not itself
        // visible down a corridor, and seeing the eyes before the body is the entire point.
        Material eyeMaterial = new Material(emissiveSource);
        eyeMaterial.name = "AI_EyeGlow";
        eyeMaterial.EnableKeyword("_EMISSION");
        eyeMaterial.SetColor("_BaseColor", eyeColor);
        eyeMaterial.SetColor("_EmissionColor", eyeColor * eyeEmission);

        foreach (string boneName in new[] { "Left_Eye", "Right_Eye" })
        {
            Transform bone = FindDeep(transform, boneName);
            if (bone == null) continue;

            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = boneName + "_Glow";
            Destroy(eye.GetComponent<Collider>());
            eye.transform.SetParent(bone, false);
            eye.transform.localPosition = Vector3.zero;
            eye.transform.localScale = Vector3.one * eyeSize;

            MeshRenderer renderer = eye.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = eyeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
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

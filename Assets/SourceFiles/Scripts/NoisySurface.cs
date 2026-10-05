using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A patch of floor that makes footsteps louder (F74): broken glass or a puddle. Cloned from
/// InteractableKit.glassPatch/puddle by MazeGenerator.BuildNoisySurfaces, and also spawned dynamically
/// at a shattered bottle's impact point (see ThrowableItem.Impact). A trigger collider tracks whoever
/// is standing on it and drives PlayerStealthState.EnterSurface/ExitSurface (ref-counted: two overlapping
/// patches never stack multiplicatively, the strongest one simply wins) plus an occasional footstep
/// one-shot timed to the walker's own speed.
/// </summary>
public class NoisySurface : MonoBehaviour
{
    public enum Kind { Glass, Puddle }

    [SerializeField] private Kind kind = Kind.Glass;
    [Tooltip("Multiplies PlayerStealthState's walking noise radius while standing here")]
    [SerializeField] private float walkMultiplier = 3f;
    [Tooltip("Multiplies the sprinting noise radius while standing here")]
    [SerializeField] private float sprintMultiplier = 1.6f;
    [Tooltip("Flat side length of the trigger square (m)")]
    [SerializeField] private float triggerSize = 1.6f;

    private BoxCollider _trigger;
    private AudioSource _audio;
    private AudioClip _stepClip;

    private readonly List<PlayerStealthState> _occupants = new List<PlayerStealthState>();
    private readonly Dictionary<PlayerStealthState, CharacterController> _controllers = new Dictionary<PlayerStealthState, CharacterController>();
    private float _strideTimer;

    /// <summary>Called right after Instantiate by MazeGenerator.BuildNoisySurfaces, or by SpawnGlassShatter for a thrown bottle. `diameter` overrides the template's default trigger size (a shatter patch is smaller than an authored one - F74 slice spec).</summary>
    public void Configure(Kind newKind, float? diameter = null)
    {
        kind = newKind;
        if (diameter.HasValue) triggerSize = diameter.Value;

        walkMultiplier = kind == Kind.Glass ? 3f : 2f;
        sprintMultiplier = kind == Kind.Glass ? 1.6f : 1.3f;

        _trigger = GetComponent<BoxCollider>();
        if (_trigger == null) _trigger = gameObject.AddComponent<BoxCollider>();
        _trigger.isTrigger = true;
        _trigger.size = new Vector3(triggerSize, 1.6f, triggerSize);
        _trigger.center = new Vector3(0f, 0.8f, 0f);

        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 1f;
        _audio.maxDistance = 10f;
        _stepClip = kind == Kind.Glass ? DoorAudio.BuildClunkClip(0.1f, 900f, 0.7f) : DoorAudio.BuildClunkClip(0.14f, 260f, 0.3f);
    }

    /// <summary>Clones `template`, sizes it to the shatter's small radius, and drops it flat on the nearest floor below `point`.</summary>
    public static NoisySurface SpawnGlassShatter(NoisySurface template, Vector3 point, Transform parent)
    {
        if (template == null) return null;

        Vector3 floorPoint = point;
        if (Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 2f, ~0, QueryTriggerInteraction.Ignore))
        {
            floorPoint = hit.point;
        }

        NoisySurface clone = Instantiate(template, floorPoint + Vector3.up * 0.01f, Quaternion.identity, parent);
        clone.gameObject.SetActive(true);
        clone.name = "GlassShatter";
        // F74 slice spec: a shatter patch is 0.8 m radius, smaller than an authored floor patch's 1.6 m side.
        clone.Configure(Kind.Glass, 1.6f * 0.5f);
        return clone;
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerStealthState stealth = other.GetComponentInParent<PlayerStealthState>();
        if (stealth == null || _occupants.Contains(stealth)) return;

        _occupants.Add(stealth);
        _controllers[stealth] = other.GetComponentInParent<CharacterController>();
        stealth.EnterSurface(walkMultiplier, sprintMultiplier);
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerStealthState stealth = other.GetComponentInParent<PlayerStealthState>();
        if (stealth == null || !_occupants.Contains(stealth)) return;

        _occupants.Remove(stealth);
        _controllers.Remove(stealth);
        stealth.ExitSurface(walkMultiplier, sprintMultiplier);
    }

    private void Update()
    {
        if (_occupants.Count == 0) return;

        float bestSpeed = 0f;
        foreach (KeyValuePair<PlayerStealthState, CharacterController> entry in _controllers)
        {
            if (entry.Value == null) continue;
            Vector3 v = entry.Value.velocity;
            v.y = 0f;
            bestSpeed = Mathf.Max(bestSpeed, v.magnitude);
        }

        if (bestSpeed < 0.4f)
        {
            _strideTimer = 0.15f;
            return;
        }

        _strideTimer -= Time.deltaTime;
        if (_strideTimer <= 0f)
        {
            if (_audio != null && _stepClip != null) _audio.PlayOneShot(_stepClip, 0.6f);
            _strideTimer = Mathf.Lerp(0.55f, 0.3f, Mathf.Clamp01(bestSpeed / 6f));
        }
    }

    private void OnDestroy()
    {
        foreach (PlayerStealthState stealth in _occupants)
        {
            if (stealth != null) stealth.ExitSurface(walkMultiplier, sprintMultiplier);
        }
        _occupants.Clear();
        _controllers.Clear();
    }
}

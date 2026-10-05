using UnityEngine;

/// <summary>
/// A bottle or can the player can pick up and throw (F74). Cloned from InteractableKit.throwableBottle/
/// throwableCan by MazeGenerator.BuildThrowables while lying on the floor (a plain IInteractable
/// pickup - trigger collider, no Rigidbody), then re-instantiated by ThrowController as a flying
/// projectile when thrown (solid collider + Rigidbody, tumbling). A bottle shatters on its first real
/// impact and leaves a small glass NoisySurface behind; a can clatters, settles, and reverts to a plain
/// pickup so it can be thrown again.
/// </summary>
public class ThrowableItem : MonoBehaviour, IInteractable
{
    public enum Kind { Bottle, Can }

    [SerializeField] private Kind kind = Kind.Bottle;

    private Collider _collider;
    private Rigidbody _rb;
    private AIFollower _hunter;
    private NoisySurface _glassPatchTemplate;
    private Transform _mazeRoot;

    private bool _isProjectile;
    private bool _hasImpacted;
    private float _flightTimer;

    private static AudioClip s_bottleShatterClip;
    private static AudioClip s_canClatterClip;

    public Kind ItemKind => kind;

    private void Awake()
    {
        _collider = GetComponent<Collider>();
        if (_collider != null) _collider.isTrigger = true; // at rest - PlayerInteractor's SphereCast finds triggers too, and the CharacterController never kicks it
    }

    /// <summary>Called by ThrowController right after Instantiate: turns this clone into a flying projectile.</summary>
    public void Launch(Vector3 origin, Vector3 velocity, AIFollower hunter, NoisySurface glassPatchTemplate, Transform mazeRoot)
    {
        transform.position = origin;
        _hunter = hunter;
        _glassPatchTemplate = glassPatchTemplate;
        _mazeRoot = mazeRoot;
        _isProjectile = true;
        _hasImpacted = false;
        _flightTimer = 0f;

        if (_collider != null) _collider.isTrigger = false;

        _rb = gameObject.GetComponent<Rigidbody>();
        if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
        _rb.isKinematic = false;
        _rb.linearVelocity = velocity;
        _rb.angularVelocity = new Vector3(Random.Range(-8f, 8f), Random.Range(-8f, 8f), Random.Range(-8f, 8f));
    }

    string IInteractable.Prompt => kind == Kind.Bottle ? "PICK UP BOTTLE" : "PICK UP CAN";
    float IInteractable.HoldSeconds => 0f;
    bool IInteractable.CanInteract => !_isProjectile;

    void IInteractable.Interact(PlayerInteractor who)
    {
        ThrowController controller = who.GetComponent<ThrowController>();
        if (controller == null || !controller.TryPickUp(kind)) return;

        Destroy(gameObject);
    }

    void IInteractable.OnFocus(bool focused) { }

    private void Update()
    {
        if (!_isProjectile) return;
        _flightTimer += Time.deltaTime;

        // A can that has settled (its impact is done and it has stopped rolling) becomes a plain pickup
        // again - decision: "a can... stays re-pickable" (F74 slice spec).
        if (_hasImpacted && kind == Kind.Can && _rb != null && _flightTimer > 0.3f && _rb.linearVelocity.sqrMagnitude < 0.02f)
        {
            SettleCan();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!_isProjectile || _hasImpacted) return;
        if (_flightTimer < 0.08f) return; // ignore the instant of launch

        _hasImpacted = true;
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        Impact(point);
    }

    private void Impact(Vector3 point)
    {
        _hunter?.HearNoise(point, 18f);

        if (kind == Kind.Bottle)
        {
            PlayOneShotAt(point, GetBottleShatterClip(), 0.8f);
            NoisySurface.SpawnGlassShatter(_glassPatchTemplate, point, _mazeRoot);
            Destroy(gameObject);
        }
        else
        {
            PlayOneShotAt(point, GetCanClatterClip(), 0.6f);
            // Left rolling to a stop - SettleCan (above) flips it back to a pickup once it is still.
        }
    }

    private void SettleCan()
    {
        _isProjectile = false;
        if (_rb != null)
        {
            Destroy(_rb);
            _rb = null;
        }
        if (_collider != null) _collider.isTrigger = true;
    }

    private static void PlayOneShotAt(Vector3 point, AudioClip clip, float volume)
    {
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, point, volume);
    }

    private static AudioClip GetBottleShatterClip()
    {
        if (s_bottleShatterClip == null) s_bottleShatterClip = DoorAudio.BuildClunkClip(0.35f, 1400f, 0.85f);
        return s_bottleShatterClip;
    }

    private static AudioClip GetCanClatterClip()
    {
        if (s_canClatterClip == null) s_canClatterClip = DoorAudio.BuildClunkClip(0.25f, 320f, 0.6f);
        return s_canClatterClip;
    }
}

using UnityEngine;

/// <summary>
/// A shard of currency lying in the maze. Deliberately not a Pickup: GameManager counts Pickup objects
/// as stars, so a shard built on it would corrupt the count and could fire AllStarsCollected early.
/// Silent to the hunter on purpose: the risk of a shard is the detour and the footsteps it costs, not
/// the pickup itself.
/// </summary>
public class ShardPickup : MonoBehaviour
{
    [Tooltip("How fast the shard spins in place")]
    [SerializeField] private float spinDegreesPerSecond = 60f;
    [Tooltip("How far the shard bobs up and down")]
    [SerializeField] private float bobAmplitude = 0.06f;
    [SerializeField] private float bobSpeed = 1.6f;

    private AudioClip _chime;
    private float _phase;
    private Vector3 _start;
    private bool _taken;

    /// <summary>Called right after Instantiate. phase offsets the bob so a cluster of shards does not pulse in lockstep.</summary>
    public void Configure(AudioClip chime, float phase)
    {
        _chime = chime;
        _phase = phase;
    }

    private void Start()
    {
        _start = transform.position;
    }

    private void Update()
    {
        transform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.World);

        float y = _start.y + Mathf.Sin((Time.time + _phase) * bobSpeed) * bobAmplitude;
        transform.position = new Vector3(transform.position.x, y, transform.position.z);
    }

    private void OnTriggerEnter(Collider other)
    {
        // Same rule as Locker.IsPlayer: two objects carry the Player tag and only one moves.
        if (_taken || !other.CompareTag("Player") || other.GetComponent<CharacterController>() == null) return;
        if (!GameFlow.IsRunActive || GameOutcome.IsOver) return;

        _taken = true;
        PlayerWallet.CollectMazeShard();
        if (_chime != null) AudioSource.PlayClipAtPoint(_chime, transform.position, 0.6f);
        Destroy(gameObject);
    }
}

using UnityEngine;

/// <summary>
/// A wall-mounted button that unlocks one linked MazeDoor (F72). Cloned from InteractableKit.wallButton
/// by MazeGenerator.BuildDoors, placed on the locked side of its door. Pressing it makes noise (the
/// hunter can hear a button being pressed) and, if the door itself is out of line of sight, tells the
/// player something happened elsewhere instead of leaving them guessing.
/// </summary>
public class WallButton : MonoBehaviour, IInteractable
{
    [Header("Template wiring (InteractableKit.wallButton)")]
    [SerializeField] private Renderer statusLightRenderer;
    [SerializeField] private Light statusLight;

    [Tooltip("How far a press can be heard (m)")]
    [SerializeField] private float noiseRadius = 10f;

    private static readonly Color ReadyColor = new Color(0.85f, 0.15f, 0.1f);
    private static readonly Color TimedReadyColor = new Color(0.95f, 0.65f, 0.1f);
    private static readonly Color UsedColor = new Color(0.2f, 0.85f, 0.3f);

    private MazeDoor _door;
    private AIFollower _hunter;
    private PlayerHud _hud;
    private Vector3 _doorLookPoint;
    private AudioSource _audio;
    private AudioClip _clickClip;
    private bool _everPressed;
    private Beacon _beacon;

    /// <summary>F75 Fuse Map: whether this button still needs pressing - the Star Compass skips one already used (unless it is a re-pressable Timed button whose door has closed again).</summary>
    public bool NeedsAttention => !_everPressed || (_door != null && _door.CanBePressedAgain);

    /// <summary>F75 Fuse Map shop item: a slow-pulsing 3 m beacon so this button is findable across the room.</summary>
    public void EnableBeacon()
    {
        if (_beacon == null) _beacon = Beacon.Attach(transform, Vector3.up * 0.3f);
    }

    /// <summary>Called right after Instantiate by MazeGenerator.BuildDoors.</summary>
    public void Configure(MazeDoor door, AIFollower hunter)
    {
        _door = door;
        _hunter = hunter;
        _doorLookPoint = door != null ? door.transform.position + Vector3.up : transform.position;

        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 1f;
        _audio.maxDistance = 12f;
        _clickClip = DoorAudio.BuildClickClip();
    }

    /// <summary>Called later by MazeGenerator.SetUpAtmosphere, once the HUD exists (same late-wiring pattern as Locker.Bind).</summary>
    public void BindHud(PlayerHud hud) => _hud = hud;

    string IInteractable.Prompt => "PRESS";
    float IInteractable.HoldSeconds => 0f;
    bool IInteractable.CanInteract => _door != null && (!_everPressed || _door.CanBePressedAgain);

    void IInteractable.Interact(PlayerInteractor who)
    {
        if (_door == null) return;

        _door.Unlock();
        _everPressed = true;
        if (_audio != null && _clickClip != null) _audio.PlayOneShot(_clickClip, 0.7f);
        _hunter?.HearNoise(transform.position, noiseRadius);

        if (_hud != null)
        {
            Vector3 origin = transform.position + Vector3.up * 1.2f;
            bool blocked = Physics.Linecast(origin, _doorLookPoint, out RaycastHit hitInfo, ~0, QueryTriggerInteraction.Ignore)
                        && hitInfo.collider.GetComponentInParent<MazeDoor>() != _door;
            if (blocked) _hud.ShowSubtitle("Somewhere, a shutter rolls up.", 3f);
        }
    }

    void IInteractable.OnFocus(bool focused) { }

    private void Update()
    {
        if (statusLightRenderer == null && statusLight == null) return;

        Color c = !_everPressed ? ReadyColor
            : (_door != null && _door.CanBePressedAgain) ? TimedReadyColor
            : UsedColor;

        if (statusLightRenderer != null && statusLightRenderer.material.HasProperty("_EmissionColor"))
        {
            statusLightRenderer.material.SetColor("_EmissionColor", c * 2f);
        }
        if (statusLight != null) statusLight.color = c;
    }
}

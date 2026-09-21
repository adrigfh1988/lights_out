using System.Collections;
using StarterAssets;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Anything the player can hide inside. Implemented by Locker; AIFollower talks only to this.</summary>
public interface IHidingSpot
{
    Vector3 FrontPosition { get; }

    /// <summary>The hunter busts the player out: swing the door open and drop the hiding state.</summary>
    void Expose();
}

/// <summary>
/// A hiding spot built by MazeGenerator.BuildLockers. Handles its own E-to-enter/leave input like
/// Flashlight handles F, teleports the player inside, locks movement and clamps the look to the slit.
/// </summary>
public class Locker : MonoBehaviour, IHidingSpot
{
    [SerializeField] private float yawRange = 35f;
    [SerializeField] private float pitchRange = 25f;
    [SerializeField] private float doorOpenDegrees = 110f;
    [SerializeField] private float doorOpenSeconds = 0.25f;

    private Vector3 _insidePosition;
    private float _facingYaw;
    private Transform _door;

    private Transform _player;
    private PlayerStealthState _stealth;
    private Flashlight _flashlight;
    private PlayerHud _hud;
    private ThirdPersonController _controller;

    private bool _playerInTrigger;
    private bool _exposed;
    private float _savedBottom;
    private float _savedTop;

    public Vector3 FrontPosition { get; private set; }
    public bool Occupied { get; private set; }

    /// <summary>Called by MazeGenerator.BuildLockers right after the geometry is built.</summary>
    public void Configure(Vector3 insidePosition, Vector3 frontPosition, float facingYaw, Transform door)
    {
        _insidePosition = insidePosition;
        FrontPosition = frontPosition;
        _facingYaw = facingYaw;
        _door = door;
    }

    /// <summary>Called by MazeGenerator.SetUpAtmosphere once the player and HUD exist.</summary>
    public void Bind(Transform player, PlayerStealthState stealth, Flashlight flashlight, PlayerHud hud)
    {
        _player = player;
        _stealth = stealth;
        _flashlight = flashlight;
        _hud = hud;
        _controller = player != null ? player.GetComponent<ThirdPersonController>() : null;
    }

    private void Update()
    {
        if (_player == null || _stealth == null || _controller == null) return;

#if ENABLE_INPUT_SYSTEM
        bool pressed = (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                    || (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);
#else
        bool pressed = false;
#endif

        if (!pressed || !GameFlow.IsRunActive || GameOutcome.IsOver) return;

        if (Occupied) Leave();
        else if (_playerInTrigger) Enter();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInTrigger = true;
        if (!Occupied && _hud != null) _hud.SetPrompt("E  HIDE");
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInTrigger = false;
        if (!Occupied && _hud != null) _hud.SetPrompt(null);
    }

    // Two objects carry the Player tag - a static root and the child that actually moves - so the
    // CharacterController is what tells them apart, the same rule AIFollower.FindTarget uses.
    private static bool IsPlayer(Collider other)
    {
        return other.CompareTag("Player") && other.GetComponent<CharacterController>() != null;
    }

    private void Enter()
    {
        _stealth.SetHidden(this);
        Occupied = true;
        _exposed = false;

        if (_flashlight != null)
        {
            _flashlight.ForceOff();
            _flashlight.InputEnabled = false;
        }

        CharacterController cc = _player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        _player.position = _insidePosition;
        _player.rotation = Quaternion.Euler(0f, _facingYaw, 0f);
        if (cc != null) cc.enabled = true;

        _controller.MovementLocked = true;
        // Snap the view to look out through the slit before the clamp locks it there.
        _controller.LookAt(FrontPosition + Vector3.up * 1.6f, 1f);
        _controller.SetYawClamp(_facingYaw, yawRange);
        _savedBottom = _controller.BottomClamp;
        _savedTop = _controller.TopClamp;
        _controller.BottomClamp = -pitchRange;
        _controller.TopClamp = pitchRange;

        if (_hud != null) _hud.SetPrompt("E  LEAVE");
    }

    private void Leave()
    {
        CharacterController cc = _player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        _player.position = FrontPosition;
        _player.rotation = Quaternion.Euler(0f, _facingYaw, 0f);
        if (cc != null) cc.enabled = true;

        _controller.MovementLocked = false;
        _controller.ClearYawClamp();
        _controller.BottomClamp = _savedBottom;
        _controller.TopClamp = _savedTop;

        // Torch stays off - the player chooses whether to switch it back on.
        if (_flashlight != null) _flashlight.InputEnabled = true;
        _stealth.SetHidden(null);
        Occupied = false;

        if (_hud != null) _hud.SetPrompt(_playerInTrigger ? "E  HIDE" : null);
    }

    /// <summary>
    /// The hunter watched the player climb in and has now arrived: swing the door open and hand
    /// control back so the capture sequence's camera ease can turn onto its eyes. Harmless if called
    /// twice - EnterCaptured only fires PlayerCaught once, but this guards regardless.
    /// </summary>
    public void Expose()
    {
        if (_exposed) return;
        _exposed = true;

        if (_door != null) StartCoroutine(OpenDoor());

        if (_controller != null)
        {
            _controller.ClearYawClamp();
            _controller.BottomClamp = _savedBottom;
            _controller.TopClamp = _savedTop;
        }

        if (_stealth != null)
        {
            _stealth.SetHidden(null);
            _stealth.LastExposed = true;
        }
    }

    private IEnumerator OpenDoor()
    {
        Quaternion start = _door.localRotation;
        Quaternion end = start * Quaternion.Euler(0f, doorOpenDegrees, 0f);
        float t = 0f;

        while (t < doorOpenSeconds)
        {
            t += Time.deltaTime;
            _door.localRotation = Quaternion.Slerp(start, end, Mathf.Clamp01(t / doorOpenSeconds));
            yield return null;
        }

        _door.localRotation = end;
    }
}

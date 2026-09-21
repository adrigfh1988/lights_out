using System.Collections;
using StarterAssets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The lit room between floors: a counter, a salesman whose head follows the player, and a door to the
/// next floor. Unlike everything else in this project the room is AUTHORED in the scene, so it can be
/// selected, moved and restyled in the Editor. Generate it once with the menu item
/// LIGHTS OUT > Build Shop Room (Assets/Editor/ShopRoomBuilder.cs); this component only drives it.
///
/// It is not a run and not an ending: see GameFlow.IsInShop. MazeGenerator finds it, hands it the
/// player/HUD/menu through Configure, and GameOutcome.FloorCleared teleports the player onto the
/// Arrival point and calls Open once the payout ledger is dismissed.
/// </summary>
public class ShopRoom : MonoBehaviour
{
    [Header("Parts (wired by the builder; drag your own in if you rebuild the room by hand)")]
    [Tooltip("Where the player stands when they arrive. Its Y rotation is the way they face.")]
    [SerializeField] private Transform arrivalPoint;
    [Tooltip("Trigger volume in front of the counter. Only its XZ footprint is used.")]
    [SerializeField] private Collider counterZone;
    [Tooltip("Trigger volume in front of the door. Only its XZ footprint is used.")]
    [SerializeField] private Collider doorZone;
    [Tooltip("The salesman's head. It turns to follow the player, within Head Yaw Limit of how it is facing here.")]
    [SerializeField] private Transform headPivot;
    [Tooltip("The sign over the counter. Its alpha wavers.")]
    [SerializeField] private TextMeshPro counterSign;
    [Tooltip("The sign over the door. Its text is set to the next floor's number on arrival.")]
    [SerializeField] private TextMeshPro doorSign;

    [Header("Salesman")]
    [Tooltip("Degrees per second the head turns to track the player")]
    [SerializeField] private float headTurnSpeed = 40f;
    [Tooltip("How far either side of its authored facing the head is allowed to turn")]
    [SerializeField] private float headYawLimit = 70f;

    [Header("Ambience")]
    [SerializeField] private float humVolume = 0.22f;

    private Transform _player;
    private PlayerHud _hud;
    private ShopMenu _menu;

    private bool _open;
    private bool _inCounter;
    private bool _inDoor;
    private float _headBaseYaw;

    private AudioSource _hum;
    private AudioClip _humClip;

    /// <summary>True once the room has the parts it needs to work. False = the builder has not been run.</summary>
    public bool IsComplete => arrivalPoint != null && counterZone != null && doorZone != null;

    /// <summary>Called by MazeGenerator.SetUpAtmosphere. Everything physical already exists in the scene.</summary>
    public void Configure(Transform player, PlayerHud hud, ShopMenu menu)
    {
        _player = player;
        _hud = hud;
        _menu = menu;
    }

    private void Awake()
    {
        if (headPivot != null) _headBaseYaw = headPivot.eulerAngles.y;

        _humClip = BuildShopHumClip();
        _hum = gameObject.GetComponent<AudioSource>();
        if (_hum == null) _hum = gameObject.AddComponent<AudioSource>();
        _hum.playOnAwake = false;
        _hum.loop = true;
        _hum.spatialBlend = 0f;
        _hum.volume = humVolume;
        _hum.clip = _humClip;
    }

    // ---------------------------------------------------------------- lifecycle

    /// <summary>Teleports the player into the room, facing the way the Arrival point faces. Called at black, so the pop is never seen.</summary>
    public void ArrivePlayer(Transform player)
    {
        if (player == null || arrivalPoint == null) return;

        float yaw = arrivalPoint.eulerAngles.y;

        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.position = arrivalPoint.position;
        player.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (cc != null) cc.enabled = true;

        player.GetComponent<ThirdPersonController>()?.ResetCameraRotation(yaw);

        if (doorSign != null) doorSign.text = $"FLOOR {GameFlow.CurrentFloor + 1}";
    }

    /// <summary>Opens the room for free movement. Called once the ledger's CONTINUE is pressed.</summary>
    public void Open()
    {
        _open = true;
        _inCounter = false;
        _inDoor = false;
        if (_hum != null) _hum.Play();
        if (_hud != null) _hud.ShowSubtitle(GreetingFor(GameFlow.CurrentFloor), 3f);
    }

    private void Update()
    {
        UpdateSignWaver();
        if (headPivot != null) UpdateHeadTracking();

        if (!_open || !GameFlow.IsInShop || (_menu != null && _menu.IsOpen) || Time.timeScale <= 0f) return;

        UpdateZones();
        HandleInteract();
    }

    private void UpdateZones()
    {
        if (_player == null) return;

        bool inCounter = ContainsXZ(counterZone, _player.position);
        bool inDoor = ContainsXZ(doorZone, _player.position);
        if (inCounter == _inCounter && inDoor == _inDoor) return;

        _inCounter = inCounter;
        _inDoor = inDoor;
        if (_hud == null) return;

        if (_inCounter) _hud.SetPrompt("E  TALK");
        else if (_inDoor) _hud.SetPrompt("E  DESCEND");
        else _hud.SetPrompt(null);
    }

    private void HandleInteract()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) pressed = true;
        if (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame) pressed = true;
#endif
        if (!pressed) return;

        if (_inCounter) { if (_menu != null) _menu.Open(); }
        else if (_inDoor) Descend();
    }

    private void UpdateHeadTracking()
    {
        if (_player == null) return;

        Vector3 toPlayer = _player.position - headPivot.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f) return;

        // World yaws throughout, so the room can be rotated or the salesman turned in the Editor.
        float wantedYaw = Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg;
        float delta = Mathf.Clamp(Mathf.DeltaAngle(_headBaseYaw, wantedYaw), -headYawLimit, headYawLimit);
        float targetYaw = _headBaseYaw + delta;

        float newYaw = Mathf.MoveTowardsAngle(headPivot.eulerAngles.y, targetYaw, headTurnSpeed * Time.deltaTime);
        headPivot.rotation = Quaternion.Euler(0f, newYaw, 0f);
    }

    private void UpdateSignWaver()
    {
        if (counterSign == null) return;
        float alpha = Mathf.Lerp(0.8f, 1f, Mathf.PerlinNoise(Time.time * 4f, 0.7f));
        Color c = counterSign.color;
        c.a = alpha;
        counterSign.color = c;
    }

    /// <summary>E at the door: blackout, then GameFlow.NextFloor().</summary>
    private void Descend()
    {
        StartCoroutine(DescendRoutine());
    }

    private IEnumerator DescendRoutine()
    {
        _open = false;
        GameFlow.IsInShop = false;
        PlayerLock.Freeze(_player, true);
        if (_hud != null) _hud.SetPrompt(null);
        if (_hum != null) _hum.Stop();

        Canvas canvas = RuntimeUi.ResolveCanvas();
        Image blackout = canvas != null ? RuntimeUi.CreatePanel(canvas.transform, "Blackout", new Color(0f, 0f, 0f, 0f)).GetComponent<Image>() : null;
        if (blackout != null) blackout.transform.SetAsLastSibling();

        float t = 0f;
        while (t < 0.5f)
        {
            // GameFlow.NextFloor reloads the scene regardless, but bail cleanly if an ending beat us to it.
            if (GameOutcome.IsOver) yield break;

            t += Time.deltaTime;
            if (blackout != null) blackout.color = new Color(0f, 0f, 0f, Mathf.Clamp01(t / 0.5f));
            yield return null;
        }

        GameFlow.NextFloor();
    }

    private static string GreetingFor(int floor)
    {
        switch (floor)
        {
            case 1: return "Shards for the dark. What'll it be?";
            case 2: return "Back again.";
            case 3: return "It's getting louder up there.";
            default: return "Last stop before the bottom.";
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>XZ only: a Y-ranged test is one CharacterController skin-width away from never matching the player's feet.</summary>
    private static bool ContainsXZ(Collider zone, Vector3 point)
    {
        if (zone == null) return false;
        Bounds bounds = zone.bounds;
        return point.x >= bounds.min.x && point.x <= bounds.max.x
            && point.z >= bounds.min.z && point.z <= bounds.max.z;
    }

    /// <summary>4 s of two low sines with a slow amplitude wobble - the pattern every synthesised clip in this project follows.</summary>
    private static AudioClip BuildShopHumClip()
    {
        const int sampleRate = 44100;
        const float duration = 4f;

        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float wobble = 0.9f + 0.1f * Mathf.Sin(2f * Mathf.PI * 0.3f * t); // 0.8..1
            float value = 0.5f * Mathf.Sin(2f * Mathf.PI * 55f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 110f * t);
            samples[i] = value * wobble;
        }

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f)
        {
            float gain = 0.5f / peak;
            for (int i = 0; i < sampleCount; i++) samples[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("ShopHum", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void OnDestroy()
    {
        if (_humClip != null) Destroy(_humClip);
    }
}

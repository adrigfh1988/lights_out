using UnityEngine;

/// <summary>
/// F77 Key Hunt: a brass key on a wall hook, cloned from InteractableKit.keyPickup by
/// MazeGenerator.BuildKeys. Deliberately neither a Pickup nor an ObjectiveToken (CLAUDE.md's star-count
/// gotcha): keys never gate the hatch *opening*, only its use - see MazeEscape's padlock. Press E to
/// take it; the key then lives on the player's KeyRing.
/// </summary>
public class KeyPickup : MonoBehaviour, IInteractable
{
    [Header("Template wiring (InteractableKit.keyPickup)")]
    [Tooltip("Faint warm pulse so the key reads on a Blackout floor.")]
    [SerializeField] private Light glintLight;

    [Tooltip("How far taking this key can be heard - a quiet jingle, stars are the loud thing.")]
    [SerializeField] private float noiseRadius = 12f;

    private KeyRing _ring;
    private PlayerHud _hud;
    private AIFollower _hunter;
    private AudioClip _clink;
    private Beacon _beacon;
    private bool _taken;

    public bool IsTaken => _taken;

    /// <summary>F75 Fuse Map shop item: a small pulsing light, same as a fuse box/button/vault door.</summary>
    public void EnableBeacon()
    {
        if (_beacon == null) _beacon = Beacon.Attach(transform, Vector3.up * 0.3f);
    }

    /// <summary>Late-bound by MazeGenerator.SetUpAtmosphere, once the player's KeyRing/HUD/hunter exist (the same late-wiring pattern as BatteryCellPickup.BindPlayerSystems - this is built earlier in Awake).</summary>
    public void Bind(KeyRing ring, PlayerHud hud, AIFollower hunter)
    {
        _ring = ring;
        _hud = hud;
        _hunter = hunter;
        _clink = DoorAudio.BuildClunkClip(0.12f, 900f, 0.2f);
    }

    string IInteractable.Prompt => "TAKE KEY";
    float IInteractable.HoldSeconds => 0f;
    bool IInteractable.CanInteract => !_taken && _ring != null;

    void IInteractable.Interact(PlayerInteractor who)
    {
        if (_taken || _ring == null) return;
        _taken = true;

        _ring.Collect(this);
        _hud?.ShowSubtitle($"KEY {_ring.Held} / {_ring.Total}", 2f);
        _hunter?.HearNoise(transform.position, noiseRadius);
        if (_clink != null) AudioSource.PlayClipAtPoint(_clink, transform.position, 0.6f);

        Destroy(gameObject);
    }

    void IInteractable.OnFocus(bool focused) { }

    private void Update()
    {
        if (glintLight == null) return;

        // Time.time, not unscaled: stands still behind the title screen and under pause, same as Beacon/StarPulse.
        // Base 0.22 matches the template's authored intensity (InteractableKitBuilder.BuildKeyPickup).
        glintLight.intensity = 0.22f * (1f + 0.4f * Mathf.Sin(2f * Mathf.PI * 0.8f * Time.time));
    }
}

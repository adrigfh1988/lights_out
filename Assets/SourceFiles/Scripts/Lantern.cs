using UnityEngine;

/// <summary>
/// A carry/place lantern (F74): a portable light pool that costs the player their sprint while held.
/// Cloned from InteractableKit.lantern by MazeGenerator.BuildLantern. Picking it up parents it to the
/// camera (lower right, like a held prop) and registers it in LightPool from there; setting it down
/// (E with no focus while carried - the same Fallback path LoreReadingUi uses to close) drops it on the
/// floor ahead of the player. Left alone and far from the player for a while, a placed lantern draws
/// the hunter once.
/// </summary>
public class Lantern : MonoBehaviour, IInteractable
{
    [Tooltip("The point light that makes this a light source. Falls back to the first child Light if unassigned.")]
    [SerializeField] private Light lanternLight;

    private const float LightRadius = 6f;
    private const float LightIntensity = 1.1f;
    private const float CarriedVisibility = 0.8f;
    private const float UnattendedDistance = 12f;
    private const float LureSubtitleDistance = 20f;
    private const float LureNoiseRadius = 60f;

    private Collider _collider;
    private Transform _player;
    private PlayerStamina _stamina;
    private PlayerStealthState _stealth;
    private AIFollower _hunter;
    private PlayerHud _hud;
    private System.Random _rng;

    private bool _carried;
    private bool _lured;
    private float _unattendedTimer;
    private float _lureAt;

    /// <summary>Called right after Instantiate by MazeGenerator.BuildLantern.</summary>
    public void Configure(System.Random rng)
    {
        _rng = rng;
        _collider = GetComponent<Collider>();

        if (lanternLight == null) lanternLight = GetComponentInChildren<Light>();
        if (lanternLight != null)
        {
            lanternLight.range = LightRadius;
            lanternLight.intensity = LightIntensity;
        }

        LightPool.Register(transform, lanternLight != null ? lanternLight.range : LightRadius, () => 1f);
        ScheduleLure();
    }

    /// <summary>Called later by MazeGenerator.SetUpAtmosphere, once these all exist - same late-wiring pattern as WallButton.BindHud.</summary>
    public void BindPlayerSystems(Transform player, PlayerStamina stamina, PlayerStealthState stealth, AIFollower hunter, PlayerHud hud)
    {
        _player = player;
        _stamina = stamina;
        _stealth = stealth;
        _hunter = hunter;
        _hud = hud;
    }

    string IInteractable.Prompt => _carried ? "PUT DOWN LANTERN" : "PICK UP LANTERN";
    float IInteractable.HoldSeconds => 0f;
    bool IInteractable.CanInteract => true;

    void IInteractable.Interact(PlayerInteractor who)
    {
        if (_carried) PutDown(who);
        else PickUp(who);
    }

    void IInteractable.OnFocus(bool focused) { }

    private void PickUp(PlayerInteractor who)
    {
        Camera cam = who.Camera;
        if (cam == null) return;

        _carried = true;
        transform.SetParent(cam.transform, false);
        transform.localPosition = new Vector3(0.32f, -0.28f, 0.5f);
        transform.localRotation = Quaternion.identity;

        if (_collider != null) _collider.enabled = false;
        if (_stamina != null) _stamina.SprintBlocked = true;
        if (_stealth != null) _stealth.MinVisibility = CarriedVisibility;

        who.Fallback = this; // E with no focus while carried puts it down - see PlayerInteractor.Fallback
        _lured = false;
        _unattendedTimer = 0f;
    }

    private void PutDown(PlayerInteractor who)
    {
        _carried = false;
        transform.SetParent(null);

        Camera cam = who.Camera;
        Transform anchor = cam != null ? cam.transform : who.transform;
        Vector3 forward = anchor.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = who.transform.forward;
        forward.Normalize();

        // Cast forward from the player's feet, not the camera, and pull the drop point back short of
        // anything solid so it never lands inside a wall.
        Vector3 origin = who.transform.position + Vector3.up * 0.6f;
        float distance = 1.0f;
        if (Physics.Raycast(origin, forward, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            distance = Mathf.Max(0.3f, hit.distance - 0.3f);
        }

        transform.position = who.transform.position + forward * distance;
        transform.rotation = Quaternion.identity;

        if (_collider != null) _collider.enabled = true;
        if (_stamina != null) _stamina.SprintBlocked = false;
        if (_stealth != null) _stealth.MinVisibility = 0f;

        if (who.Fallback == (IInteractable)this) who.Fallback = null;
        ScheduleLure();
    }

    private void ScheduleLure()
    {
        double roll = _rng != null ? _rng.NextDouble() : 0.5;
        _lureAt = 25f + (float)roll * 20f; // 25..45 s, decision D14/F74 slice spec
        _unattendedTimer = 0f;
        _lured = false;
    }

    private void Update()
    {
        if (_carried || _lured || _player == null || _hunter == null) return;
        if (!GameFlow.IsRunActive || GameOutcome.IsOver) return;

        float distance = Vector3.Distance(transform.position, _player.position);
        if (distance <= UnattendedDistance)
        {
            _unattendedTimer = 0f;
            return;
        }

        _unattendedTimer += Time.deltaTime;
        if (_unattendedTimer < _lureAt) return;

        _lured = true;
        _hunter.HearNoise(transform.position, LureNoiseRadius);
        if (distance > LureSubtitleDistance) _hud?.ShowSubtitle("Something is drawn to the light.", 3f);
    }

    private void OnDestroy()
    {
        LightPool.Unregister(transform);
    }
}

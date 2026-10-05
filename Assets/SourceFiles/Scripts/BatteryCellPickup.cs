using UnityEngine;

/// <summary>
/// A vault-only battery top-up (F72 decision D3/D11), cloned from InteractableKit.batteryCell.
/// Deliberately not a Pickup - GameManager.Start counts Pickup objects as stars, so one built on that
/// base would corrupt the star count and could fire AllStarsCollected early (see CLAUDE.md).
///
/// A 20% seeded chance per vault also rides along on this same pickup: a Second Wind token, if the
/// player is not already holding one (MazeGenerator.BuildVaultLoot rolls this once at placement time).
/// </summary>
public class BatteryCellPickup : MonoBehaviour, IInteractable
{
    [Tooltip("Fraction of a full battery this restores, capped at a full charge")]
    [SerializeField] private float rechargeFraction = 0.5f;

    private Flashlight _flashlight;
    private PlayerHud _hud;
    private bool _grantsSecondWind;
    private bool _taken;

    /// <summary>Set once by MazeGenerator.BuildVaultLoot right after Instantiate.</summary>
    public void SetGrantsSecondWind(bool grants) => _grantsSecondWind = grants;

    /// <summary>Called later by MazeGenerator.SetUpAtmosphere, once the player's flashlight and HUD exist (the same late-wiring pattern as Locker.Bind - this is built before either does).</summary>
    public void BindPlayerSystems(Flashlight flashlight, PlayerHud hud)
    {
        _flashlight = flashlight;
        _hud = hud;
    }

    string IInteractable.Prompt => "PICK UP BATTERY CELL";
    float IInteractable.HoldSeconds => 0f;
    bool IInteractable.CanInteract => !_taken;

    void IInteractable.Interact(PlayerInteractor who)
    {
        if (_taken) return;
        _taken = true;

        _flashlight?.AddCharge(rechargeFraction);

        if (_grantsSecondWind && PlayerInventory.Count(ShopItem.SecondWind) == 0)
        {
            PlayerInventory.Grant(ShopItem.SecondWind);
            _hud?.ShowSubtitle("+ SECOND WIND", 3f);
        }
        else
        {
            _hud?.ShowSubtitle("BATTERY CELL", 2f);
        }

        Destroy(gameObject);
    }

    void IInteractable.OnFocus(bool focused) { }
}

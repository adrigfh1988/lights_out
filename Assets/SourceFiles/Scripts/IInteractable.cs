/// <summary>
/// One interaction system for everything new the player can press E on (F72 decision D12) - doors,
/// buttons, vault loot, and later fuse boxes/lore notes/theme interactables. PlayerInteractor is the
/// only thing that ever calls Interact; a MazeDoor/WallButton/BatteryCellPickup otherwise behaves like
/// any other MonoBehaviour. Locker deliberately does not implement this - it keeps its own E handling
/// and only asks PlayerInteractor.HasFocus to yield priority (see Locker.Update).
/// </summary>
public interface IInteractable
{
    /// <summary>Shown after "E  " while this is focused and CanInteract, e.g. "OPEN" or "PICK UP BATTERY CELL".</summary>
    string Prompt { get; }

    /// <summary>False hides the prompt and blocks Interact even while focused (e.g. a locked door from the wrong side).</summary>
    bool CanInteract { get; }

    /// <summary>0 = a single press. Above 0, PlayerInteractor fills progress while E is held and focus is kept, and calls Interact once it reaches this many seconds.</summary>
    float HoldSeconds { get; }

    /// <summary>Called by PlayerInteractor: a press (HoldSeconds == 0) or a completed hold.</summary>
    void Interact(PlayerInteractor who);

    /// <summary>Raised by PlayerInteractor whenever this becomes (or stops being) the focused interactable. Most implementers can leave this empty.</summary>
    void OnFocus(bool focused);
}

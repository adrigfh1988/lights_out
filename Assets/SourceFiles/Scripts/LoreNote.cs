using UnityEngine;

/// <summary>
/// A paper note pinned to a wall (F74). Cloned from InteractableKit.loreNote by
/// MazeGenerator.BuildLoreNotes. Reading it opens LoreReadingUi (never pauses, never touches the
/// cursor - see that class's doc comment); the first read of a given note id, campaign-wide, pays a
/// one-off +3 shard bonus through LoreArchive/PlayerWallet.AwardBonus.
/// </summary>
public class LoreNote : MonoBehaviour, IInteractable
{
    /// <summary>F87: raised every time a note is opened, with its id (the Intake's note is -1). ContractTracker's Read it all contract listens.</summary>
    public static event System.Action<int> Read;

    private int _noteId;
    private string _text;
    private LoreReadingUi _reader;
    private PlayerHud _hud;

    /// <summary>Called right after Instantiate by MazeGenerator.BuildLoreNotes.</summary>
    public void Configure(int noteId, string text)
    {
        _noteId = noteId;
        _text = text;
    }

    /// <summary>Called later by MazeGenerator.SetUpAtmosphere, once the reader/HUD exist - same late-wiring pattern as WallButton.BindHud.</summary>
    public void Bind(LoreReadingUi reader, PlayerHud hud)
    {
        _reader = reader;
        _hud = hud;
    }

    string IInteractable.Prompt => _reader != null && _reader.IsOpen ? "CLOSE" : "READ";
    float IInteractable.HoldSeconds => 0f;
    bool IInteractable.CanInteract => _reader != null;

    void IInteractable.Interact(PlayerInteractor who)
    {
        if (_reader == null) return;

        if (_reader.IsOpen)
        {
            _reader.Close();
            return;
        }

        _reader.Show(_text, who);
        Read?.Invoke(_noteId);
        if (LoreArchive.MarkRead(_noteId))
        {
            PlayerWallet.AwardBonus(3);
            _hud?.ShowSubtitle("+3 SHARDS", 2.5f);
        }
    }

    void IInteractable.OnFocus(bool focused) { }
}

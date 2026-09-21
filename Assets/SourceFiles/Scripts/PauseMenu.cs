using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Esc pauses. Added at runtime by MazeGenerator; builds nothing until the first pause, so its panel
/// is created above every other piece of HUD.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    private static readonly Color ButtonIdle = new Color(0.10f, 0.10f, 0.12f, 0.92f);
    private static readonly Color ButtonHover = new Color(0.48f, 0.08f, 0.06f, 1f);

    private Transform _player;
    private Flashlight _flashlight;
    private MainMenu _menu;
    private GameOutcome _outcome;
    private ShopMenu _shopMenu;
    private int _seed;

    private GameObject _panel;
    private Button _restartButton;
    private bool _paused;

    /// <summary>True while the pause panel is up.</summary>
    public bool IsPaused => _paused;

    /// <summary>Flashlight, menu and shopMenu may be null: no first-person rig, no title screen, or no shop.</summary>
    public void Configure(Transform player, Flashlight flashlight, MainMenu menu, GameOutcome outcome, int seed, ShopMenu shopMenu)
    {
        _player = player;
        _flashlight = flashlight;
        _menu = menu;
        _outcome = outcome;
        _seed = seed;
        _shopMenu = shopMenu;
    }

    private void Update()
    {
        if (_paused)
        {
            // Held every frame: focus changes re-lock the cursor
            PlayerLock.SetCursorFree(true);
        }

        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        // Read directly, like Flashlight: the Player action map has no Pause action
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) pressed = true;
        if (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame) pressed = true;
#endif
        if (!pressed) return;

        if (_paused) Resume();
        else if (CanPause()) Pause();
    }

    private bool CanPause()
    {
        // The shop is a third phase (decision 5): not a run, but pausing still works there.
        if (!GameFlow.IsRunActive && !GameFlow.IsInShop) return false;
        if (_menu != null && _menu.IsOpen) return false;
        if (GameOutcome.IsOver) return false;
        if (_outcome != null && _outcome.IsEnding) return false;
        // The Esc that closes the shop panel must not also open the pause menu in the same frame -
        // Update order between the two components is not defined.
        if (_shopMenu != null && (_shopMenu.IsOpen || _shopMenu.ClosedThisFrame)) return false;
        return true;
    }

    private void Pause()
    {
        _paused = true;
        Time.timeScale = 0f;
        // Covers every source: the heartbeat one-shots, the drone and the hunter's hum
        AudioListener.pause = true;

        PlayerLock.Freeze(_player, true);
        if (_flashlight != null) _flashlight.InputEnabled = false;

        if (_panel == null) BuildPanel();
        // Replaying a cleared floor from the shop would re-earn its payout (decision 4c).
        if (_restartButton != null) _restartButton.gameObject.SetActive(!GameFlow.IsInShop);
        _panel.SetActive(true);
        _panel.transform.SetAsLastSibling();

        PlayerLock.SetCursorFree(true);
    }

    private void Resume()
    {
        _paused = false;
        if (_panel != null) _panel.SetActive(false);

        Time.timeScale = 1f;
        AudioListener.pause = false;

        PlayerLock.Freeze(_player, false);
        // The shop is lit; the torch stays off and disabled there for the whole phase.
        if (_flashlight != null) _flashlight.InputEnabled = !GameFlow.IsInShop;
        PlayerLock.SetCursorFree(false);
    }

    private void OnDestroy()
    {
        if (!_paused) return;

        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    private void BuildPanel()
    {
        RuntimeUi.EnsureEventSystem();
        Canvas canvas = RuntimeUi.ResolveCanvas();

        _panel = RuntimeUi.CreatePanel(canvas.transform, "PauseScreen", new Color(0f, 0f, 0f, 0.75f));

        TextMeshProUGUI title = RuntimeUi.CreateText(_panel.transform, "Title", "PAUSED", 84f, Color.white);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 10f;
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -220f), new Vector2(1200f, 120f));

        Vector2 size = new Vector2(420f, 86f);

        Button resume = RuntimeUi.CreateButton(_panel.transform, "RESUME",
            new Vector2(0f, 400f), size, 40f, ButtonIdle, ButtonHover, Color.white);
        resume.onClick.AddListener(Resume);

        _restartButton = RuntimeUi.CreateButton(_panel.transform, "RESTART MAZE",
            new Vector2(0f, 290f), size, 40f, ButtonIdle, ButtonHover, Color.white);
        _restartButton.onClick.AddListener(() => GameFlow.Restart(true, _seed));

        Button menu = RuntimeUi.CreateButton(_panel.transform, "MAIN MENU",
            new Vector2(0f, 180f), size, 40f, ButtonIdle, ButtonHover, Color.white);
        menu.onClick.AddListener(GameFlow.ReturnToMenu);

        Button quit = RuntimeUi.CreateButton(_panel.transform, "QUIT",
            new Vector2(0f, 70f), size, 40f, ButtonIdle, ButtonHover, Color.white);
        quit.onClick.AddListener(GameFlow.Quit);
    }
}

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
    private Button _skipButton;
    private bool _paused;
    /// <summary>Last frame the cursor was locked and a pause was allowed. See the lock-loss check in Update.</summary>
    private bool _wasLockedAndPausable;

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

        // In a browser, Esc belongs to the page: it releases the pointer lock and usually never reaches
        // the game. So a lock lost while the game was pausable counts as a pause request. Only a
        // Locked -> unlocked edge counts, and only if pausing was allowed on the frame before too, so a
        // menu that frees the cursor itself (shop, endings) never trips it.
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        bool canPause = !_paused && CanPause();
        // F76: touch mode never holds a pointer lock in the first place (see PlayerLock.SetCursorFree),
        // so the very first tap on a touch-capable laptop would otherwise read as a lost lock and pause.
        bool lockLost = _wasLockedAndPausable && !locked && canPause && !TouchInput.Active;
        _wasLockedAndPausable = locked && canPause;
        if (lockLost)
        {
            Pause();
            return;
        }

        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        // Read directly, like Flashlight: the Player action map has no Pause action. P is the
        // browser-safe key; Esc still works everywhere else.
        if (Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.pKey.wasPressedThisFrame)) pressed = true;
        if (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame) pressed = true;
#endif
        if (TouchInput.Pressed(TouchInput.TouchAction.Pause)) pressed = true;
        if (!pressed) return;

        if (_paused) Resume();
        else if (CanPause()) Pause();
    }

    /// <summary>F76 T7: lets TouchControls pause for the "ROTATE YOUR DEVICE" overlay through the same
    /// single-sourced Pause() rather than duplicating its rules. Returns false (a no-op) if something
    /// else already has it paused, or CanPause() says no - TouchControls only calls ExternalResume if
    /// this returned true, so it never resumes a pause it did not itself cause.</summary>
    public bool ExternalPause()
    {
        if (_paused || !CanPause()) return false;
        Pause();
        return true;
    }

    /// <summary>Counterpart to ExternalPause. A no-op if not currently paused.</summary>
    public void ExternalResume()
    {
        if (_paused) Resume();
    }

    private bool CanPause()
    {
        // The shop is a third phase (decision 5): not a run, but pausing still works there.
        if (!GameFlow.IsRunActive && !GameFlow.IsInShop && !GameFlow.IsInIntake) return false;
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
        if (_restartButton != null) _restartButton.gameObject.SetActive(!GameFlow.IsInShop && !GameFlow.IsInIntake);
        // F86: only the Intake offers a way out of itself.
        if (_skipButton != null) _skipButton.gameObject.SetActive(GameFlow.IsInIntake);
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
        // F86: in the Intake the torch is only usable once the tutorial has handed it over (IntakeRoom.TorchAllowed).
        if (_flashlight != null)
        {
            _flashlight.InputEnabled = !GameFlow.IsInShop && (!GameFlow.IsInIntake || (IntakeRoom.Active != null && IntakeRoom.Active.TorchAllowed));
        }
        PlayerLock.SetCursorFree(false);
    }

    /// <summary>F86: leave the Intake straight into floor 1, the same way finishing it does.</summary>
    private void SkipTutorial()
    {
        IntakeRoom room = IntakeRoom.Active;
        Resume();
        if (room != null) room.Skip();
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

        // F86: SKIP TUTORIAL shares the RESTART slot - the two are never shown together (see Pause).
        _skipButton = RuntimeUi.CreateButton(_panel.transform, "SKIP TUTORIAL",
            new Vector2(0f, 290f), size, 40f, ButtonIdle, ButtonHover, Color.white);
        _skipButton.onClick.AddListener(SkipTutorial);
        _skipButton.gameObject.SetActive(false);

        Button menu = RuntimeUi.CreateButton(_panel.transform, "MAIN MENU",
            new Vector2(0f, 180f), size, 40f, ButtonIdle, ButtonHover, Color.white);
        menu.onClick.AddListener(GameFlow.ReturnToMenu);

        Button quit = RuntimeUi.CreateButton(_panel.transform, "QUIT",
            new Vector2(0f, 70f), size, 40f, ButtonIdle, ButtonHover, Color.white);
        quit.onClick.AddListener(GameFlow.Quit);
        quit.gameObject.SetActive(GameFlow.CanQuit);

        BuildTouchOptions(size);
    }

    // ---------------------------------------------------------------- F76: touch controls options

    private TextMeshProUGUI _touchModeLabel;

    private void BuildTouchOptions(Vector2 size)
    {
        Button touchMode = RuntimeUi.CreateButton(_panel.transform, TouchModeLabel(TouchInput.CurrentMode),
            new Vector2(0f, 520f), size, 30f, ButtonIdle, ButtonHover, Color.white);
        _touchModeLabel = touchMode.GetComponentInChildren<TextMeshProUGUI>();
        touchMode.onClick.AddListener(CycleTouchMode);

        BuildSensitivitySlider();
    }

    private static string TouchModeLabel(TouchInput.Mode mode) => $"TOUCH CONTROLS: {mode.ToString().ToUpperInvariant()}";

    private void CycleTouchMode()
    {
        TouchInput.Mode next;
        switch (TouchInput.CurrentMode)
        {
            case TouchInput.Mode.Auto: next = TouchInput.Mode.On; break;
            case TouchInput.Mode.On: next = TouchInput.Mode.Off; break;
            default: next = TouchInput.Mode.Auto; break;
        }
        TouchInput.SetMode(next);
        if (_touchModeLabel != null) _touchModeLabel.text = TouchModeLabel(next);
    }

    /// <summary>Built by hand: RuntimeUi has no slider helper (a plain UGUI Slider - Background,
    /// Fill Area/Fill, Handle Slide Area/Handle - the same structure Unity's own Slider prefab uses).</summary>
    private void BuildSensitivitySlider()
    {
        GameObject holder = new GameObject("SensitivitySlider");
        holder.transform.SetParent(_panel.transform, false);
        holder.layer = _panel.layer;
        RectTransform holderRect = holder.AddComponent<RectTransform>();
        RuntimeUi.Place(holderRect, new Vector2(0.5f, 0f), new Vector2(0f, 640f), new Vector2(420f, 70f));

        TextMeshProUGUI label = RuntimeUi.CreateText(holder.transform, "Label", "LOOK SENSITIVITY", 24f, Color.white);
        RuntimeUi.Place(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(420f, 26f));

        GameObject trackObj = new GameObject("Track");
        trackObj.transform.SetParent(holder.transform, false);
        trackObj.layer = holder.layer;
        Image track = trackObj.AddComponent<Image>();
        track.color = new Color(0f, 0f, 0f, 0.55f);
        RuntimeUi.Place(track.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(420f, 14f));

        GameObject fillAreaObj = new GameObject("FillArea");
        fillAreaObj.transform.SetParent(trackObj.transform, false);
        fillAreaObj.layer = holder.layer;
        RectTransform fillAreaRect = fillAreaObj.AddComponent<RectTransform>();
        RuntimeUi.Stretch(fillAreaRect);

        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(fillAreaObj.transform, false);
        fillObj.layer = holder.layer;
        Image fill = fillObj.AddComponent<Image>();
        fill.color = ButtonHover;
        RuntimeUi.Stretch(fill.rectTransform);

        GameObject handleAreaObj = new GameObject("HandleArea");
        handleAreaObj.transform.SetParent(trackObj.transform, false);
        handleAreaObj.layer = holder.layer;
        RectTransform handleAreaRect = handleAreaObj.AddComponent<RectTransform>();
        RuntimeUi.Stretch(handleAreaRect);

        GameObject handleObj = new GameObject("Handle");
        handleObj.transform.SetParent(handleAreaObj.transform, false);
        handleObj.layer = holder.layer;
        Image handle = handleObj.AddComponent<Image>();
        handle.color = Color.white;
        RuntimeUi.Place(handle.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 26f));

        Slider slider = holder.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = TouchInput.MinLookSensitivity;
        slider.maxValue = TouchInput.MaxLookSensitivity;
        slider.value = TouchInput.LookSensitivity;
        slider.onValueChanged.AddListener(TouchInput.SetLookSensitivity);
    }
}

using StarterAssets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Title screen, rules screen, and the gate that holds the game frozen until the player presses Start.
///
/// The maze, the player and the hunter are all built by MazeGenerator before this runs - the world is
/// already standing behind the cover art. This just stops the clock until someone asks for it.
/// </summary>
[DefaultExecutionOrder(-80)]
public class MainMenu : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private string gameTitle = "LIGHTS OUT";
    [SerializeField] private string tagline = "Collect. Escape. Don't be seen.";

    [Header("Cover")]
    [Tooltip("Leave empty to use the cover generated in code. Drop a painted one in here when you have it.")]
    [SerializeField] private Sprite coverImage;
    [Tooltip("Kept modest on purpose: the cover is built pixel by pixel on the CPU, and it is a soft gradient that stretches without showing its resolution")]
    [SerializeField] private int generatedCoverWidth = 960;
    [SerializeField] private int generatedCoverHeight = 540;

    [Header("Look")]
    [SerializeField] private Color accent = new Color(0.86f, 0.12f, 0.09f);
    [SerializeField] private Color buttonIdle = new Color(0.10f, 0.10f, 0.12f, 0.92f);
    [SerializeField] private Color buttonHover = new Color(0.48f, 0.08f, 0.06f, 1f);

    private GameObject _titleScreen;
    private GameObject _rulesScreen;
    private TextMeshProUGUI _controlText;
    private Texture2D _generatedCover;
    private Sprite _generatedSprite;
    private Transform _player;
    private bool _menuOpen = true;
    // F86: the rules screen starts the run only as START GAME's fallback (no Intake built); from CONTROLS it is just a back button.
    private bool _rulesStartsGame = true;
    private TextMeshProUGUI _rulesButtonLabel;

    [Header("Music")]
    [Tooltip("Volume of the title music (Discovering Rooms)")]
    [SerializeField] private float musicVolume = 0.3f;
    [Tooltip("Seconds the title music takes to fade out once the run starts (real time - the clock is frozen behind the menu)")]
    [SerializeField] private float musicFadeSeconds = 1.2f;

    private AudioClip _musicClip;
    private AudioSource _music;
    private bool _musicFading;

    /// <summary>True while the title or rules screen is up. The pause menu stays out of the way.</summary>
    public bool IsOpen => _menuOpen;

    public void Configure(Transform player, AudioClip music = null)
    {
        _musicClip = music;
        if (player == null) return;
        _player = player;

        // Freeze immediately as well as in Start. Mouse look is not scaled by deltaTime, so without
        // this the camera can be nudged on frame 0 before Start has run.
        PlayerLock.Freeze(_player, true);
    }

    private void Awake()
    {
        // Freeze everything. The maze is fully built by now, so this catches the world on frame one.
        Time.timeScale = 0f;
    }

    private void Start()
    {
        // Start rather than Awake: StarterAssetsInputs.Awake forces the cursor hidden, and the
        // scene's own components need to have finished initialising before the UI goes over them.
        RuntimeUi.EnsureEventSystem();

        Canvas canvas = RuntimeUi.ResolveCanvas();
        RuntimeUi.MakeCanvasResolutionIndependent(canvas);

        BuildTitleScreen(canvas.transform);
        BuildRulesScreen(canvas.transform);
        _rulesScreen.SetActive(false);

        PlayerLock.Freeze(_player, true);

        // A retry goes straight into the game. Through StartGame rather than by hiding the screens:
        // it is what unfreezes the player, relocks the cursor and undoes the zeroed clock from Awake.
        if (GameFlow.SkipMenuOnLoad)
        {
            GameFlow.SkipMenuOnLoad = false;

            // F83: CONTINUE on a save taken in the shop. Same housekeeping as StartGame, but it is not a run:
            // IsRunActive stays false and the player lands in the shop.
            if (GameFlow.ResumeInShop)
            {
                GameFlow.ResumeInShop = false;
                CloseMenus();
                GameOutcome outcome = FindAnyObjectByType<GameOutcome>();
                if (outcome != null) outcome.EnterShopDirectly();
                else GameFlow.NextFloor();
                return;
            }

            StartGame();
            return;
        }

        PlayMenuMusic();
    }

    /// <summary>The title music, only when the title screen is actually shown (a retry or CONTINUE skips it).</summary>
    private void PlayMenuMusic()
    {
        if (_musicClip == null) return;

        _music = gameObject.AddComponent<AudioSource>();
        _music.playOnAwake = false;
        _music.loop = true;
        _music.spatialBlend = 0f;
        _music.clip = _musicClip;
        _music.volume = musicVolume;
        _music.Play();
    }

    private void OnDestroy()
    {
        // Leaving play mode with a zeroed clock would carry into the next session
        Time.timeScale = 1f;

        if (_generatedSprite != null) Destroy(_generatedSprite);
        if (_generatedCover != null) Destroy(_generatedCover);
    }

    private void Update()
    {
        if (_musicFading && _music != null)
        {
            // Unscaled: CloseMenus restores the clock, but the fade must not depend on it.
            // Clamped step: one long hitch (a loading frame) must not swallow the whole fade.
            float step = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _music.volume = Mathf.MoveTowards(_music.volume, 0f, musicVolume / Mathf.Max(0.01f, musicFadeSeconds) * step);
            if (_music.volume <= 0f)
            {
                _music.Stop();
                _musicFading = false;
            }
        }

        if (!_menuOpen) return;

        // Held every frame rather than set once: StarterAssetsInputs re-locks the cursor on every
        // focus change, so a single assignment loses the moment the window is alt-tabbed.
        PlayerLock.SetCursorFree(true);
    }

    // ---------------------------------------------------------------- flow

    private void ShowRules(bool startsGame)
    {
        _rulesStartsGame = startsGame;
        if (_rulesButtonLabel != null) _rulesButtonLabel.text = startsGame ? "START" : "BACK";

        // F76: TouchControls' own -83 Update has not necessarily run yet this frame (this is an
        // onClick callback), and the very first tap is exactly the case that needs Active to be
        // current right now, not next frame - Poll() is idempotent per frame either way.
        TouchInput.Poll();
        if (_controlText != null) _controlText.text = ControlsText();

        _titleScreen.SetActive(false);
        _rulesScreen.SetActive(true);
    }

    private void StartGame()
    {
        CloseMenus();

        GameFlow.BeginRun();
    }

    /// <summary>
    /// F86: START GAME. A new campaign opens in the Intake (the playable tutorial) instead of the rules screen; with no
    /// IntakeRoom built it falls back to the old rules screen, which then starts the run.
    /// </summary>
    private void BeginNewCampaign()
    {
        IntakeRoom room = FindAnyObjectByType<IntakeRoom>();
        if (room == null || !room.IsComplete)
        {
            ShowRules(true);
            return;
        }

        // CloseMenus unfreezes the player and restores the clock; Begin (same frame) re-freezes for the wake-up beat.
        CloseMenus();
        room.Begin();
    }

    /// <summary>The CONTROLS button: the old rules screen as a reference sheet. Its button goes back to the title.</summary>
    private void ShowControls() => ShowRules(false);

    /// <summary>The part of starting that both a run and a shop resume need: hide the screens, hand the player back, undo Awake's zeroed clock.</summary>
    private void CloseMenus()
    {
        TouchInput.Poll();
        _musicFading = _music != null && _music.isPlaying;

        // F76 T8: a fullscreen request needs a user gesture on the web, and this tap is one. iPhone
        // Safari does not support it at all - ignore failure either way, nothing here depends on it.
        if (TouchInput.Active)
        {
            try { Screen.fullScreen = true; } catch { /* best effort only */ }
        }

        _menuOpen = false;
        _titleScreen.SetActive(false);
        _rulesScreen.SetActive(false);

        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);
        Time.timeScale = 1f;
    }

    /// <summary>F76 T6: shown instead of the keyboard/mouse control line while touch is active or the
    /// platform is mobile (iPadOS Safari under-reports as desktop, so isMobilePlatform is checked too).</summary>
    private static string ControlsText()
    {
        if (!TouchInput.Active && !Application.isMobilePlatform)
        {
            return "<b>WASD</b>  move   <b>SHIFT</b>  sprint   <b>MOUSE</b>  look   <b>F</b>  flashlight   <b>E</b>  hide / talk\n" +
                   "<b>1</b>  spare battery   <b>2</b>  star compass   <b>ESC</b>  pause\n" +
                   "<size=30>Your torch has three minutes of light in it. Your legs have less. Lockers hide you — unless it saw you climb in.</size>";
        }

        return "<b>LEFT THUMB</b>  move   <b>RIGHT THUMB</b>  look   buttons: <b>TORCH</b>, <b>FOCUS</b>, <b>INTERACT</b>, <b>THROW</b>, <b>SPRINT</b>\n" +
               "<size=30>Your torch has three minutes of light in it. Your legs have less. Lockers hide you — unless it saw you climb in.</size>";
    }

    // ---------------------------------------------------------------- screens

    private void BuildTitleScreen(Transform canvas)
    {
        _titleScreen = RuntimeUi.CreatePanel(canvas, "TitleScreen", Color.white);

        Image cover = _titleScreen.GetComponent<Image>();
        cover.sprite = coverImage != null ? coverImage : GenerateCover();
        cover.type = Image.Type.Simple;
        cover.color = Color.white;

        TextMeshProUGUI title = RuntimeUi.CreateText(_titleScreen.transform, "Title", gameTitle, 120f, Color.white);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 14f;
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(1600f, 160f));

        TextMeshProUGUI sub = RuntimeUi.CreateText(_titleScreen.transform, "Tagline", tagline, 38f, new Color(0.75f, 0.75f, 0.78f));
        RuntimeUi.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -290f), new Vector2(1200f, 60f));

        // F83: with a saved run a plain CONTINUE sits on top of the usual stack; without one the title screen is unchanged.
        if (SaveSystem.TryLoad(out SaveData save))
        {
            Button continueButton = RuntimeUi.CreateButton(_titleScreen.transform, "CONTINUE",
                new Vector2(0f, 520f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
            continueButton.onClick.AddListener(ContinueSavedRun);
            _savedFloor = save.floor;
        }

        Button start = RuntimeUi.CreateButton(_titleScreen.transform, "START GAME",
            new Vector2(0f, 410f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
        start.onClick.AddListener(() => ConfirmThenStart(BeginNewCampaign));

        // F48: builds one floor out of the Maze Modular Puzzle Kit instead of the FloorThemes gallery.
        // A test button - it skips the Intake and drops straight into the run.
        Button startNewMaze = RuntimeUi.CreateButton(_titleScreen.transform, "START NEW MAZE",
            new Vector2(0f, 300f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
        startNewMaze.onClick.AddListener(() => ConfirmThenStart(StartKitMaze));

        // F86: the old rules screen, kept as a reference sheet now that START GAME opens the Intake.
        Button controls = RuntimeUi.CreateButton(_titleScreen.transform, "CONTROLS",
            new Vector2(0f, 190f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
        controls.onClick.AddListener(ShowControls);

        Button exit = RuntimeUi.CreateButton(_titleScreen.transform, "EXIT",
            new Vector2(0f, 80f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
        exit.onClick.AddListener(GameFlow.Quit);
        exit.gameObject.SetActive(GameFlow.CanQuit);
    }

    private void StartKitMaze()
    {
        GameFlow.StartKitMaze();
    }

    // ---------------------------------------------------------------- F83: saved run

    private GameObject _confirmPanel;
    private int _savedFloor;

    /// <summary>CONTINUE: restore the save and reload through the normal SkipMenuOnLoad path (StartGame, or the shop for a shop save).</summary>
    private void ContinueSavedRun()
    {
        // The reload throws the fullscreen gesture away, so ask for it now, on the tap itself (same as StartGame).
        TouchInput.Poll();
        if (TouchInput.Active)
        {
            try { Screen.fullScreen = true; } catch { /* best effort only */ }
        }

        if (!SaveSystem.ResumeCampaign()) RefreshTitleScreen();
    }

    /// <summary>START GAME / START NEW MAZE: with a saved run in the way, ask before throwing it away.</summary>
    private void ConfirmThenStart(System.Action begin)
    {
        if (!SaveSystem.HasSave)
        {
            begin();
            return;
        }

        if (_confirmPanel == null) BuildConfirmPanel();
        _confirmText.text = $"Your saved run (floor {_savedFloor}) will be lost.";
        _confirmAction = begin;
        _confirmPanel.SetActive(true);
        _confirmPanel.transform.SetAsLastSibling();
    }

    private TextMeshProUGUI _confirmText;
    private System.Action _confirmAction;

    private void BuildConfirmPanel()
    {
        _confirmPanel = RuntimeUi.CreatePanel(_titleScreen.transform, "ConfirmStartOver", new Color(0.02f, 0.02f, 0.03f, 0.97f));

        TextMeshProUGUI heading = RuntimeUi.CreateText(_confirmPanel.transform, "Heading", "START OVER?", 76f, accent);
        heading.fontStyle = FontStyles.Bold;
        heading.characterSpacing = 8f;
        RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(1400f, 100f));

        _confirmText = RuntimeUi.CreateText(_confirmPanel.transform, "Body", "", 40f, new Color(0.88f, 0.88f, 0.9f));
        RuntimeUi.Place(_confirmText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -420f), new Vector2(1400f, 80f));

        Button startOver = RuntimeUi.CreateButton(_confirmPanel.transform, "START OVER",
            new Vector2(0f, 400f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
        startOver.onClick.AddListener(() =>
        {
            SaveSystem.Delete();
            _confirmPanel.SetActive(false);
            System.Action action = _confirmAction;
            _confirmAction = null;
            action?.Invoke();
        });

        Button back = RuntimeUi.CreateButton(_confirmPanel.transform, "BACK",
            new Vector2(0f, 290f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
        back.onClick.AddListener(() => _confirmPanel.SetActive(false));

        _confirmPanel.SetActive(false);
    }

    /// <summary>The save turned out unusable when CONTINUE was pressed (TryLoad already deleted it): rebuild the title screen without CONTINUE.</summary>
    private void RefreshTitleScreen()
    {
        bool wasActive = _titleScreen != null && _titleScreen.activeSelf;
        if (_titleScreen != null) Destroy(_titleScreen);
        _confirmPanel = null;

        Canvas canvas = RuntimeUi.ResolveCanvas();
        BuildTitleScreen(canvas.transform);
        _titleScreen.SetActive(wasActive);
        // Sits under the rules screen in sibling order, as it did when first built.
        if (_rulesScreen != null) _titleScreen.transform.SetSiblingIndex(_rulesScreen.transform.GetSiblingIndex());
    }

    private void BuildRulesScreen(Transform canvas)
    {
        _rulesScreen = RuntimeUi.CreatePanel(canvas, "RulesScreen", new Color(0.02f, 0.02f, 0.03f, 0.97f));

        TextMeshProUGUI heading = RuntimeUi.CreateText(_rulesScreen.transform, "Heading", "HOW TO PLAY", 76f, accent);
        heading.fontStyle = FontStyles.Bold;
        heading.characterSpacing = 10f;
        RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1400f, 100f));

        string rules =
            "<b>1.</b>   Collect every star hidden in the maze.\n\n" +
            "<b>2.</b>   A hatch will open somewhere. Find it before the timer runs out.\n\n" +
            "<b>3.</b>   You are not alone down here. If it sees you, <color=#DB1F17><b>run</b></color>.\n\n" +
            "<b>4.</b>   Shards glint in the dark. Between floors, someone will sell you things for them.";

        TextMeshProUGUI body = RuntimeUi.CreateText(_rulesScreen.transform, "Rules", rules, 36f, new Color(0.88f, 0.88f, 0.9f));
        body.alignment = TextAlignmentOptions.Left;
        RuntimeUi.Place(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1100f, 380f));

        // Text is filled in by ShowRules() (touch-aware - see ControlsText), not here: TouchInput.Active
        // is not settled yet this early (Start runs before the first tap that could turn it on).
        _controlText = RuntimeUi.CreateText(_rulesScreen.transform, "Controls", "", 34f, new Color(0.62f, 0.62f, 0.66f));
        RuntimeUi.Place(_controlText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -210f), new Vector2(1400f, 170f));

        Button start = RuntimeUi.CreateButton(_rulesScreen.transform, "START",
            new Vector2(0f, 150f), new Vector2(420f, 90f), 44f, buttonIdle, buttonHover, Color.white);
        _rulesButtonLabel = start.GetComponentInChildren<TextMeshProUGUI>();
        start.onClick.AddListener(() =>
        {
            if (_rulesStartsGame) StartGame();
            else
            {
                _rulesScreen.SetActive(false);
                _titleScreen.SetActive(true);
            }
        });
    }

    // ---------------------------------------------------------------- generated cover

    /// <summary>
    /// A cover built in code: the flashlight cone cutting up through fog, and two red eyes waiting
    /// above it. Stands in until a painted one is dropped into the coverImage slot.
    /// </summary>
    private Sprite GenerateCover()
    {
        int w = Mathf.Max(64, generatedCoverWidth);
        int h = Mathf.Max(64, generatedCoverHeight);
        float aspect = h / (float)w;

        _generatedCover = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "GeneratedCover" };
        Color[] pixels = new Color[w * h];

        Color fog = new Color(0.014f, 0.016f, 0.024f);
        Color beam = new Color(0.62f, 0.56f, 0.46f);

        for (int y = 0; y < h; y++)
        {
            float v = y / (float)(h - 1);

            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1);
                Color c = fog;

                // Flashlight cone, apex just off the bottom edge, widening as it rises
                float alongCone = v + 0.05f;
                if (alongCone > 0f)
                {
                    float halfWidth = alongCone * 0.66f;
                    float across = 1f - Mathf.Clamp01(Mathf.Abs(u - 0.5f) / Mathf.Max(halfWidth, 0.0001f));
                    across *= across;
                    c += beam * (across * Mathf.Exp(-alongCone * 2.8f) * 1.15f);
                }

                c += EyeGlow(u, v, 0.466f, 0.585f, aspect);
                c += EyeGlow(u, v, 0.534f, 0.585f, aspect);

                // Vignette, so the title and buttons always have something dark to sit on
                float dx = (u - 0.5f) * 2f;
                float dy = (v - 0.5f) * 2f;
                float falloff = 1f - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) * 0.72f);
                c *= Mathf.Lerp(0.3f, 1f, falloff);

                pixels[y * w + x] = new Color(c.r, c.g, c.b, 1f);
            }
        }

        _generatedCover.SetPixels(pixels);
        _generatedCover.Apply();

        _generatedSprite = Sprite.Create(_generatedCover, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f));
        return _generatedSprite;
    }

    private static Color EyeGlow(float u, float v, float centreX, float centreY, float aspect)
    {
        float dx = u - centreX;
        float dy = (v - centreY) * aspect;
        float distance = Mathf.Sqrt(dx * dx + dy * dy);

        float halo = Mathf.Exp(-(distance / 0.055f) * (distance / 0.055f) * 2.2f) * 0.55f;
        float core = distance < 0.0075f ? 1f : 0f;

        float intensity = halo + core;
        return new Color(1f * intensity, 0.07f * intensity, 0.04f * intensity);
    }
}

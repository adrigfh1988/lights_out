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
    private Texture2D _generatedCover;
    private Sprite _generatedSprite;
    private Transform _player;
    private bool _menuOpen = true;

    /// <summary>True while the title or rules screen is up. The pause menu stays out of the way.</summary>
    public bool IsOpen => _menuOpen;

    public void Configure(Transform player)
    {
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
            StartGame();
        }
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
        if (!_menuOpen) return;

        // Held every frame rather than set once: StarterAssetsInputs re-locks the cursor on every
        // focus change, so a single assignment loses the moment the window is alt-tabbed.
        PlayerLock.SetCursorFree(true);
    }

    // ---------------------------------------------------------------- flow

    private void ShowRules()
    {
        _titleScreen.SetActive(false);
        _rulesScreen.SetActive(true);
    }

    private void StartGame()
    {
        _menuOpen = false;
        _titleScreen.SetActive(false);
        _rulesScreen.SetActive(false);

        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);
        Time.timeScale = 1f;

        GameFlow.RunStartTime = Time.time;
        GameFlow.IsRunActive = true;
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

        Button start = RuntimeUi.CreateButton(_titleScreen.transform, "START GAME",
            new Vector2(0f, 250f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
        start.onClick.AddListener(ShowRules);

        Button exit = RuntimeUi.CreateButton(_titleScreen.transform, "EXIT",
            new Vector2(0f, 140f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
        exit.onClick.AddListener(GameFlow.Quit);
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
            "<b>3.</b>   You are not alone down here. If it sees you, <color=#DB1F17><b>run</b></color>.";

        TextMeshProUGUI body = RuntimeUi.CreateText(_rulesScreen.transform, "Rules", rules, 40f, new Color(0.88f, 0.88f, 0.9f));
        body.alignment = TextAlignmentOptions.Left;
        RuntimeUi.Place(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1100f, 380f));

        string controls =
            "<b>WASD</b>  move   <b>SHIFT</b>  sprint   <b>MOUSE</b>  look   <b>F</b>  flashlight   <b>E</b>  hide\n" +
            "<size=30>Your torch has three minutes of light in it. Your legs have less. Lockers hide you — unless it saw you climb in.</size>";

        TextMeshProUGUI controlText = RuntimeUi.CreateText(_rulesScreen.transform, "Controls", controls, 34f, new Color(0.62f, 0.62f, 0.66f));
        RuntimeUi.Place(controlText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -200f), new Vector2(1400f, 130f));

        Button start = RuntimeUi.CreateButton(_rulesScreen.transform, "START",
            new Vector2(0f, 150f), new Vector2(420f, 90f), 44f, buttonIdle, buttonHover, Color.white);
        start.onClick.AddListener(StartGame);
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

using TMPro;
using UnityEngine;

/// <summary>
/// The escalation. Everything that should get worse as the maze empties out is driven from one place:
/// the counter in the corner, how loud and how wrong the soundtrack is, and how fast the thing in the
/// dark is willing to move.
///
/// Execution order is deliberately ahead of MainMenu so the counter is created first and therefore
/// draws underneath the title screen rather than on top of it.
/// </summary>
[DefaultExecutionOrder(-85)]
public class TensionDirector : MonoBehaviour
{
    [Header("Counter")]
    [SerializeField] private string label = "STARS";
    [SerializeField] private float fontSize = 38f;
    [SerializeField] private Color calmColor = new Color(0.86f, 0.86f, 0.9f);
    [Tooltip("The counter bleeds toward this as the last stars come in")]
    [SerializeField] private Color alarmedColor = new Color(0.9f, 0.15f, 0.1f);

    [Header("Star noise")]
    [Tooltip("How far the sound of a star being taken carries at zero progress (m, path distance, before the floor's hearing scale)")]
    [SerializeField] private float starNoiseRadius = 14f;
    [Tooltip("...at full progress. 34 m is most of an 11x11 maze: by the last star the hunter always comes.")]
    [SerializeField] private float starNoiseRadiusAtPeak = 34f;

    private AIFollower _follower;
    private HorrorAudioDirector _audio;
    private PlayerHud _hud;
    private KeyRing _keys;

    private TextMeshProUGUI _counter;
    private int _collected;
    private int _total;
    private bool _hunting;

    public void Configure(AIFollower follower, HorrorAudioDirector audio)
    {
        _follower = follower;
        _audio = audio;
        Apply();
    }

    /// <summary>Wired separately: SetUpAtmosphere builds the HUD after this director's Configure call.</summary>
    public void BindHud(PlayerHud hud)
    {
        _hud = hud;
    }

    /// <summary>F77: the player's KeyRing, for the KEYS counter segment. Unsubscribes any previous ring first - CLAUDE.md's every-subscriber-unsubscribes rule.</summary>
    public void BindKeyRing(KeyRing ring)
    {
        if (_keys != null) _keys.Changed -= Apply;
        _keys = ring;
        if (_keys != null) _keys.Changed += Apply;
        Apply();
    }

    /// <summary>Shop cosmetic (F35): recolours the calm end of the counter. The alarmed end is unchanged.</summary>
    public void SetCalmColor(Color color)
    {
        calmColor = color;
        Apply();
    }

    private void OnEnable()
    {
        GameManager.ProgressChanged += HandleProgress;
        GameManager.ObjectivesChanged += Apply;
        GameManager.AllStarsCollected += HandleHatchOpen;
        Pickup.OnCollectedAt += HandleStarTaken;
    }

    private void OnDisable()
    {
        GameManager.ProgressChanged -= HandleProgress;
        GameManager.ObjectivesChanged -= Apply;
        GameManager.AllStarsCollected -= HandleHatchOpen;
        Pickup.OnCollectedAt -= HandleStarTaken;
        if (_keys != null) _keys.Changed -= Apply;
    }

    private void Start()
    {
        BuildCounter();
        Apply();
    }

    private void Update()
    {
        // Hidden in the shop (decision 5): the shop panel shows the wallet instead.
        if (_counter != null) _counter.gameObject.SetActive(!GameFlow.IsInSafeRoom);
    }

    private float Progress => _total > 0 ? _collected / (float)_total : 0f;

    private void HandleProgress(int collected, int total)
    {
        _collected = collected;
        _total = total;
        Apply();
    }

    private void HandleHatchOpen()
    {
        _hunting = true;
        Apply();

        if (_audio != null) _audio.BeginPanic();
    }

    /// <summary>
    /// Fired by Pickup before OnCoinCollected/ProgressChanged, so Progress here is "before this star" -
    /// the star you just took is heard at the current (lower) radius, the next one is louder.
    /// </summary>
    private void HandleStarTaken(Vector3 at)
    {
        if (_follower == null) return;

        float radius = Mathf.Lerp(starNoiseRadius, starNoiseRadiusAtPeak, Progress);
        bool heard = _follower.HearNoise(at, radius);
        if (heard && _hud != null && Progress >= 0.5f)
        {
            _hud.ShowSubtitle("It heard that.", 2.2f);
        }
    }

    private void Apply()
    {
        float progress = Progress;

        if (_counter != null)
        {
            string text = $"{label}  {_collected} / {_total}";
            // F73: Blackout floors gate the hatch on fuses too - GameManager.TokensTotal is 0 on every
            // other floor, so this leaves the text exactly as it was there.
            if (GameManager.TokensTotal > 0)
            {
                text += $"   FUSES  {GameManager.TokensDone} / {GameManager.TokensTotal}";
            }
            // F77: KeyHunt floors append a third segment - 0 on every other floor (Total stays 0 until
            // BuildKeys/KeyRing.Configure run), leaving this exactly as it was there.
            if (_keys != null && _keys.Total > 0)
            {
                text += $"   KEYS  {_keys.Held} / {_keys.Total}";
            }
            _counter.text = text;
            _counter.color = Color.Lerp(calmColor, alarmedColor, progress);
        }

        if (_audio != null) _audio.SetIntensity(progress);
        if (_follower != null) _follower.SetThreatLevel(progress, _hunting);
    }

    private void BuildCounter()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        _counter = RuntimeUi.CreateText(canvas.transform, "StarCounter", $"{label}  0 / 0", fontSize, calmColor);
        _counter.alignment = TextAlignmentOptions.TopLeft;

        RectTransform rect = _counter.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(36f, -30f);
        // Wide enough for F77's three-segment "STARS a / b   FUSES c / d   KEYS e / f", with wrapping off
        // so a long line can never spill down onto the FLOOR label underneath.
        rect.sizeDelta = new Vector2(1100f, 60f);
        _counter.textWrappingMode = TextWrappingModes.NoWrap;
    }
}

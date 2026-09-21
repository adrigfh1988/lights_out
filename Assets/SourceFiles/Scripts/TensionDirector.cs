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

    private AIFollower _follower;
    private HorrorAudioDirector _audio;

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

    private void OnEnable()
    {
        GameManager.ProgressChanged += HandleProgress;
        GameManager.AllStarsCollected += HandleHatchOpen;
    }

    private void OnDisable()
    {
        GameManager.ProgressChanged -= HandleProgress;
        GameManager.AllStarsCollected -= HandleHatchOpen;
    }

    private void Start()
    {
        BuildCounter();
        Apply();
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

    private void Apply()
    {
        float progress = Progress;

        if (_counter != null)
        {
            _counter.text = $"{label}  {_collected} / {_total}";
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
        rect.sizeDelta = new Vector2(420f, 60f);
    }
}

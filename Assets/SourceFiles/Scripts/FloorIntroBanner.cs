using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// The "FLOOR 3 - THE CRYPT" card shown once a floor's run actually starts (F74). Built and Show()n by
/// MazeGenerator.SetUpAtmosphere every scene load; the coroutine below waits out the title screen
/// itself (GameFlow.IsRunActive), so this can be called before the player has pressed Start without
/// flashing early. Runs on unscaled time so it is unaffected by the timeScale freeze it is waiting out.
/// </summary>
public class FloorIntroBanner : MonoBehaviour
{
    private const float FadeIn = 0.5f;
    private const float FadeOut = 1f;
    private const float TotalSeconds = 5.5f;

    private GameObject _holder;
    private CanvasGroup _group;
    private TextMeshProUGUI _title;
    private TextMeshProUGUI _objective;
    private TextMeshProUGUI _rule;
    private TextMeshProUGUI _temperament;
    private Coroutine _routine;

    private void EnsureUi()
    {
        if (_holder != null) return;

        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        _holder = new GameObject("FloorIntroBanner");
        _holder.transform.SetParent(canvas.transform, false);
        _holder.layer = canvas.gameObject.layer;
        _group = _holder.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        RectTransform rect = _holder.AddComponent<RectTransform>();
        RuntimeUi.Place(rect, new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(900f, 160f));

        _title = RuntimeUi.CreateText(_holder.transform, "Title", "", 38f, Color.white);
        _title.characterSpacing = 4f;
        RuntimeUi.Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(900f, 44f));

        _objective = RuntimeUi.CreateText(_holder.transform, "Objective", "", 24f, new Color(0.85f, 0.85f, 0.82f));
        // F77: widened from 900 to 1100 - a rolled floor's line is now up to three segments ("FIND n
        // STARS" plus 2 of RESTORE n FUSES / FIND n KEYS FOR THE HATCH / SECURITY LOCKDOWN).
        RuntimeUi.Place(_objective.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(1100f, 32f));

        _rule = RuntimeUi.CreateText(_holder.transform, "Rule", "", 22f, new Color(0.95f, 0.7f, 0.3f));
        _rule.fontStyle = FontStyles.Italic;
        RuntimeUi.Place(_rule.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(900f, 30f));

        _temperament = RuntimeUi.CreateText(_holder.transform, "Temperament", "", 20f, new Color(0.7f, 0.72f, 0.78f));
        _temperament.fontStyle = FontStyles.Italic;
        RuntimeUi.Place(_temperament.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(900f, 28f));
    }

    public void Show(string title, string objective, string rule, string temperament)
    {
        EnsureUi();
        if (_holder == null) return;

        _title.text = title;
        _objective.text = objective;
        _rule.gameObject.SetActive(!string.IsNullOrEmpty(rule));
        _rule.text = rule;
        _temperament.gameObject.SetActive(!string.IsNullOrEmpty(temperament));
        _temperament.text = temperament;

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(Routine());
    }

    private IEnumerator Routine()
    {
        _group.alpha = 0f;

        while (!GameFlow.IsRunActive) yield return null;

        float hold = Mathf.Max(0f, TotalSeconds - FadeIn - FadeOut);

        float t = 0f;
        while (t < FadeIn)
        {
            t += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Clamp01(t / FadeIn);
            yield return null;
        }
        _group.alpha = 1f;

        t = 0f;
        while (t < hold)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        t = 0f;
        while (t < FadeOut)
        {
            t += Time.unscaledDeltaTime;
            _group.alpha = 1f - Mathf.Clamp01(t / FadeOut);
            yield return null;
        }
        _group.alpha = 0f;
        _routine = null;
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The resource HUD: torch battery, sprint stamina, and the interaction prompt lockers write into.
/// Built in Start (order -84) so it sits under the title screen, exactly like TensionDirector's counter.
/// Added to the MazeGenerator object by SetUpAtmosphere.
/// </summary>
[DefaultExecutionOrder(-84)]
public class PlayerHud : MonoBehaviour
{
    private static readonly Color CalmColor = new Color(0.86f, 0.86f, 0.9f);
    private static readonly Color AlarmedColor = new Color(0.9f, 0.15f, 0.1f);
    private static readonly Color StaminaColor = new Color(0.9f, 0.9f, 0.86f);
    private static readonly Color StaminaExhaustedColor = new Color(0.9f, 0.2f, 0.15f);
    private static readonly Vector2 BarSize = new Vector2(320f, 16f);

    private Flashlight _flashlight;
    private PlayerStamina _stamina;

    private GameObject _batteryHolder;
    private Image _batteryFill;
    private Image _batteryGhost;
    private float _batteryShown = 1f;
    private float _batteryVel;
    private TextMeshProUGUI _batteryLabel;

    private GameObject _staminaHolder;
    private CanvasGroup _staminaGroup;
    private Image _staminaFill;
    private Image _staminaGhost;
    private float _staminaShown = 1f;
    private float _staminaVel;
    private float _staminaAlpha;

    private TextMeshProUGUI _promptText;

    /// <summary>Either may be null (no rig, or a scene with no player).</summary>
    public void Configure(Flashlight flashlight, PlayerStamina stamina)
    {
        _flashlight = flashlight;
        _stamina = stamina;
    }

    /// <summary>Called by Locker on enter/exit/trigger. Null or empty hides the prompt.</summary>
    public void SetPrompt(string text)
    {
        if (_promptText == null) return;
        bool show = !string.IsNullOrEmpty(text);
        _promptText.gameObject.SetActive(show);
        if (show) _promptText.text = text;
    }

    private void Start()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        _batteryFill = CreateBar(canvas.transform, "BatteryBar", new Vector2(20f, 20f), BarSize,
            new Color(0f, 0f, 0f, 0.55f), CalmColor, out _batteryHolder, out _batteryGhost);

        _batteryLabel = RuntimeUi.CreateText(canvas.transform, "BatteryLabel", "TORCH", 20f, CalmColor);
        _batteryLabel.alignment = TextAlignmentOptions.Left;
        RectTransform labelRect = _batteryLabel.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(0f, 0f);
        labelRect.pivot = new Vector2(0f, 0f);
        labelRect.anchoredPosition = new Vector2(20f, 34f);
        labelRect.sizeDelta = new Vector2(220f, 24f);

        _staminaFill = CreateBar(canvas.transform, "StaminaBar", new Vector2(20f, 62f), BarSize,
            new Color(0f, 0f, 0f, 0.55f), StaminaColor, out _staminaHolder, out _staminaGhost);
        _staminaGroup = _staminaHolder.AddComponent<CanvasGroup>();
        _staminaGroup.alpha = 0f;

        // Sits under TensionDirector's star counter (top-left, 36 px in, 30 px down, 60 px tall)
        TextMeshProUGUI floorText = RuntimeUi.CreateText(canvas.transform, "FloorLabel",
            $"FLOOR {GameFlow.CurrentFloor}", 26f, new Color(0.62f, 0.62f, 0.66f));
        floorText.alignment = TextAlignmentOptions.TopLeft;
        floorText.characterSpacing = 6f;
        RectTransform floorRect = floorText.rectTransform;
        floorRect.anchorMin = new Vector2(0f, 1f);
        floorRect.anchorMax = new Vector2(0f, 1f);
        floorRect.pivot = new Vector2(0f, 1f);
        floorRect.anchoredPosition = new Vector2(38f, -84f);
        floorRect.sizeDelta = new Vector2(320f, 34f);

        _promptText = RuntimeUi.CreateText(canvas.transform, "InteractPrompt", "", 34f, Color.white);
        RectTransform promptRect = _promptText.rectTransform;
        promptRect.anchorMin = new Vector2(0.5f, 0f);
        promptRect.anchorMax = new Vector2(0.5f, 0f);
        promptRect.pivot = new Vector2(0.5f, 0f);
        promptRect.anchoredPosition = new Vector2(0f, 120f);
        promptRect.sizeDelta = new Vector2(600f, 50f);
        _promptText.gameObject.SetActive(false);
    }

    private void Update()
    {
        UpdateBattery();
        UpdateStamina();
    }

    private void UpdateBattery()
    {
        if (_flashlight == null || _batteryFill == null)
        {
            if (_batteryHolder != null) _batteryHolder.SetActive(false);
            if (_batteryLabel != null) _batteryLabel.gameObject.SetActive(false);
            return;
        }

        float charge = _flashlight.Charge;
        // Eased toward the real value so the bar visibly slides down instead of stepping. unscaled
        // time keeps the easing alive if the clock is frozen, and the real value does not move then.
        _batteryShown = Mathf.SmoothDamp(_batteryShown, charge, ref _batteryVel, 0.25f, float.MaxValue, Time.unscaledDeltaTime);
        SetFill(_batteryFill, _batteryShown);
        // The ghost lags behind on the way down, so a drain leaves a pale trail that shrinks after it
        SetFill(_batteryGhost, Mathf.Max(_batteryShown, Mathf.MoveTowards(GhostValue(_batteryGhost), charge, 0.05f * Time.unscaledDeltaTime)));

        // Calm above 30%, ramping to alarmed as it empties from there.
        float t = Mathf.Clamp01(charge / 0.3f);
        Color color = Color.Lerp(AlarmedColor, CalmColor, t);
        _batteryFill.color = color;

        if (_flashlight.IsDead)
        {
            _batteryLabel.text = "TORCH DEAD";
            _batteryLabel.color = AlarmedColor;
        }
        else
        {
            _batteryLabel.text = "TORCH";
            bool blinking = charge <= 0.1f;
            if (blinking)
            {
                bool on = ((int)(Time.time * 4f)) % 2 == 0;
                _batteryLabel.color = on ? AlarmedColor : CalmColor;
            }
            else
            {
                _batteryLabel.color = color;
            }
        }
    }

    private void UpdateStamina()
    {
        if (_stamina == null || _staminaFill == null)
        {
            if (_staminaHolder != null) _staminaHolder.SetActive(false);
            return;
        }

        float fraction = _stamina.Fraction;
        // Faster easing than the battery: sprinting and recovering are seconds-long, so the bar has
        // to visibly drain and refill in step with them.
        _staminaShown = Mathf.SmoothDamp(_staminaShown, fraction, ref _staminaVel, 0.12f, float.MaxValue, Time.unscaledDeltaTime);
        SetFill(_staminaFill, _staminaShown);
        SetFill(_staminaGhost, Mathf.Max(_staminaShown, Mathf.MoveTowards(GhostValue(_staminaGhost), fraction, 0.4f * Time.unscaledDeltaTime)));

        // Exhausted pulses red so the lockout is legible; a recovering bar tints toward white as it fills
        Color color = StaminaColor;
        if (_stamina.Exhausted)
        {
            float pulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f));
            color = StaminaExhaustedColor * pulse;
            color.a = 1f;
        }
        _staminaFill.color = color;

        // Stays visible until it has genuinely finished refilling, then fades out
        bool full = fraction >= 0.999f && _staminaShown >= 0.99f;
        float targetAlpha = full ? 0f : 1f;
        _staminaAlpha = Mathf.MoveTowards(_staminaAlpha, targetAlpha, Time.unscaledDeltaTime * (full ? 1f : 6f));
        if (_staminaGroup != null) _staminaGroup.alpha = _staminaAlpha;
    }

    /// <summary>
    /// Sets how much of the track a fill covers by moving its right anchor. Image.Type.Filled cannot
    /// be used here: with no sprite assigned Unity ignores fillAmount and always draws a full rect,
    /// which is why the bars used to look like they never moved.
    /// </summary>
    private static void SetFill(Image fill, float amount)
    {
        if (fill == null) return;
        RectTransform rect = fill.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(Mathf.Clamp01(amount), 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static float GhostValue(Image ghost)
    {
        return ghost != null ? ghost.rectTransform.anchorMax.x : 0f;
    }

    /// <summary>Track + a fill and a pale trailing "ghost" fill, both sized by anchors.</summary>
    private static Image CreateBar(Transform parent, string name, Vector2 anchoredPosition, Vector2 size,
        Color trackColor, Color fillColor, out GameObject holder, out Image ghost)
    {
        holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        holder.layer = parent.gameObject.layer;

        Image track = holder.AddComponent<Image>();
        track.color = trackColor;
        RectTransform trackRect = track.rectTransform;
        trackRect.anchorMin = new Vector2(0f, 0f);
        trackRect.anchorMax = new Vector2(0f, 0f);
        trackRect.pivot = new Vector2(0f, 0f);
        trackRect.anchoredPosition = anchoredPosition;
        trackRect.sizeDelta = size;

        // Ghost first so the real fill draws on top of it
        GameObject ghostObj = new GameObject(name + "_Ghost");
        ghostObj.transform.SetParent(holder.transform, false);
        ghostObj.layer = holder.layer;
        ghost = ghostObj.AddComponent<Image>();
        ghost.color = new Color(1f, 1f, 1f, 0.28f);
        ghost.raycastTarget = false;
        SetFill(ghost, 1f);

        GameObject fillObj = new GameObject(name + "_Fill");
        fillObj.transform.SetParent(holder.transform, false);
        fillObj.layer = holder.layer;

        Image fill = fillObj.AddComponent<Image>();
        fill.color = fillColor;
        fill.raycastTarget = false;
        SetFill(fill, 1f);

        return fill;
    }
}

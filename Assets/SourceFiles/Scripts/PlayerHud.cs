using System.Collections;
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

    private static readonly Color SubtitleColor = new Color(0.78f, 0.72f, 0.72f);
    private TextMeshProUGUI _subtitleText;
    private Coroutine _subtitleRoutine;

    private static readonly Color ShardColour = new Color(0.7f, 0.9f, 1f);
    private TextMeshProUGUI _shardText;
    private int _shardShown;
    private float _shardPunch;

    // Shop cosmetic (F35): everything that used to read CalmColor directly now reads this instead.
    private Color _tint = CalmColor;

    /// <summary>Shop cosmetic: recolours CalmColor's uses (battery label, item slots) for the rest of the campaign.</summary>
    public void SetTint(Color color)
    {
        _tint = color;
    }

    private static readonly Color SecondWindColor = new Color(0.25f, 1f, 0.7f);
    private TextMeshProUGUI _slot1Text;
    private TextMeshProUGUI _slot2Text;
    private TextMeshProUGUI _secondWindText;
    private float _slot1Punch;
    private float _slot2Punch;

    /// <summary>Called by ConsumableController right after a consumable is used: flashes the slot white and punches it.</summary>
    public void PulseSlot(int index)
    {
        if (index == 0) _slot1Punch = 1f;
        else if (index == 1) _slot2Punch = 1f;
    }

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

    /// <summary>
    /// A dread cue, not narration: one line at a time, low on screen, fading in then out over
    /// unscaled time so it cannot freeze on screen through a pause. A new call replaces the current one.
    /// </summary>
    public void ShowSubtitle(string text, float seconds)
    {
        if (_subtitleText == null || string.IsNullOrEmpty(text)) return;
        if (_subtitleRoutine != null) StopCoroutine(_subtitleRoutine);
        _subtitleRoutine = StartCoroutine(SubtitleRoutine(text, Mathf.Max(0.2f, seconds)));
    }

    private IEnumerator SubtitleRoutine(string text, float seconds)
    {
        const float fadeIn = 0.15f;
        const float fadeOut = 0.6f;
        float hold = Mathf.Max(0f, seconds - fadeIn - fadeOut);

        _subtitleText.text = $"<i>{text}</i>";
        _subtitleText.gameObject.SetActive(true);

        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.unscaledDeltaTime;
            SetSubtitleAlpha(Mathf.Clamp01(t / fadeIn));
            yield return null;
        }
        SetSubtitleAlpha(1f);

        t = 0f;
        while (t < hold)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        t = 0f;
        while (t < fadeOut)
        {
            t += Time.unscaledDeltaTime;
            SetSubtitleAlpha(1f - Mathf.Clamp01(t / fadeOut));
            yield return null;
        }

        SetSubtitleAlpha(0f);
        _subtitleText.gameObject.SetActive(false);
        _subtitleRoutine = null;
    }

    private void SetSubtitleAlpha(float alpha)
    {
        Color c = SubtitleColor;
        c.a = alpha;
        _subtitleText.color = c;
    }

    private void Start()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        _batteryFill = CreateBar(canvas.transform, "BatteryBar", new Vector2(20f, 20f), BarSize,
            new Color(0f, 0f, 0f, 0.55f), _tint, out _batteryHolder, out _batteryGhost);

        _batteryLabel = RuntimeUi.CreateText(canvas.transform, "BatteryLabel", "TORCH", 20f, _tint);
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

        // Under the floor label, top-left. Hidden in the shop - the shop panel shows the wallet instead.
        _shardText = RuntimeUi.CreateText(canvas.transform, "ShardCounter", "SHARDS  0", 26f, ShardColour);
        _shardText.alignment = TextAlignmentOptions.TopLeft;
        _shardText.characterSpacing = 6f;
        RectTransform shardRect = _shardText.rectTransform;
        shardRect.anchorMin = new Vector2(0f, 1f);
        shardRect.anchorMax = new Vector2(0f, 1f);
        shardRect.pivot = new Vector2(0f, 1f);
        shardRect.anchoredPosition = new Vector2(38f, -118f);
        shardRect.sizeDelta = new Vector2(320f, 34f);
        _shardShown = PlayerWallet.Shards;
        _shardText.text = $"SHARDS  {_shardShown}";

        _promptText = RuntimeUi.CreateText(canvas.transform, "InteractPrompt", "", 34f, Color.white);
        RectTransform promptRect = _promptText.rectTransform;
        promptRect.anchorMin = new Vector2(0.5f, 0f);
        promptRect.anchorMax = new Vector2(0.5f, 0f);
        promptRect.pivot = new Vector2(0.5f, 0f);
        promptRect.anchoredPosition = new Vector2(0f, 120f);
        promptRect.sizeDelta = new Vector2(600f, 50f);
        _promptText.gameObject.SetActive(false);

        // Above the prompt (120) and clear of the escape timer, which is top-centre.
        _subtitleText = RuntimeUi.CreateText(canvas.transform, "DreadSubtitle", "", 30f, SubtitleColor);
        _subtitleText.fontStyle = FontStyles.Italic;
        _subtitleText.characterSpacing = 2f;
        RectTransform subtitleRect = _subtitleText.rectTransform;
        subtitleRect.anchorMin = new Vector2(0.5f, 0f);
        subtitleRect.anchorMax = new Vector2(0.5f, 0f);
        subtitleRect.pivot = new Vector2(0.5f, 0f);
        subtitleRect.anchoredPosition = new Vector2(0f, 196f);
        subtitleRect.sizeDelta = new Vector2(900f, 44f);
        _subtitleText.gameObject.SetActive(false);

        // Item slots, bottom-right, pivoted from the bottom-right corner.
        _secondWindText = RuntimeUi.CreateText(canvas.transform, "SecondWindLabel", "SECOND WIND", 22f, SecondWindColor);
        _secondWindText.alignment = TextAlignmentOptions.Right;
        PlaceBottomRight(_secondWindText.rectTransform, new Vector2(-20f, 104f), new Vector2(360f, 30f));
        _secondWindText.gameObject.SetActive(false);

        _slot1Text = RuntimeUi.CreateText(canvas.transform, "Slot1", "1   BATTERY  x0", 24f, _tint);
        _slot1Text.alignment = TextAlignmentOptions.Right;
        PlaceBottomRight(_slot1Text.rectTransform, new Vector2(-20f, 62f), new Vector2(360f, 34f));

        _slot2Text = RuntimeUi.CreateText(canvas.transform, "Slot2", "2   COMPASS  x0", 24f, _tint);
        _slot2Text.alignment = TextAlignmentOptions.Right;
        PlaceBottomRight(_slot2Text.rectTransform, new Vector2(-20f, 20f), new Vector2(360f, 34f));
    }

    private static void PlaceBottomRight(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private void Update()
    {
        UpdateBattery();
        UpdateStamina();
        UpdateShardCounter();
        UpdateItemSlots();
    }

    private void UpdateItemSlots()
    {
        if (_slot1Text == null) return;

        bool inShop = GameFlow.IsInShop;
        bool secondWindHeld = PlayerInventory.Count(ShopItem.SecondWind) > 0;

        _slot1Text.gameObject.SetActive(!inShop);
        _slot2Text.gameObject.SetActive(!inShop);
        if (_secondWindText != null) _secondWindText.gameObject.SetActive(!inShop && secondWindHeld);
        if (inShop) return;

        _slot1Punch = Mathf.MoveTowards(_slot1Punch, 0f, Time.unscaledDeltaTime / 0.25f);
        _slot2Punch = Mathf.MoveTowards(_slot2Punch, 0f, Time.unscaledDeltaTime / 0.25f);

        int batteryCount = PlayerInventory.Count(ShopItem.SpareBattery);
        _slot1Text.text = $"1   BATTERY  x{batteryCount}";
        ApplySlot(_slot1Text, batteryCount > 0, _slot1Punch);

        int compassCount = PlayerInventory.Count(ShopItem.StarCompass);
        _slot2Text.text = $"2   COMPASS  x{compassCount}";
        ApplySlot(_slot2Text, compassCount > 0, _slot2Punch);
    }

    private void ApplySlot(TextMeshProUGUI text, bool has, float punch)
    {
        float scale = 1f + 0.25f * punch;
        text.rectTransform.localScale = new Vector3(scale, scale, 1f);

        Color baseColor = _tint;
        baseColor.a = has ? 1f : 0.35f;
        text.color = Color.Lerp(baseColor, Color.white, punch);
    }

    private void UpdateShardCounter()
    {
        if (_shardText == null) return;

        if (GameFlow.IsInShop)
        {
            _shardText.gameObject.SetActive(false);
            return;
        }
        _shardText.gameObject.SetActive(true);

        int shards = PlayerWallet.Shards;
        if (shards != _shardShown)
        {
            _shardShown = shards;
            _shardText.text = $"SHARDS  {_shardShown}";
            _shardPunch = 1f;
        }

        _shardPunch = Mathf.MoveTowards(_shardPunch, 0f, Time.unscaledDeltaTime / 0.25f);
        float scale = 1f + 0.25f * _shardPunch;
        _shardText.rectTransform.localScale = new Vector3(scale, scale, 1f);
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
        Color color = Color.Lerp(AlarmedColor, _tint, t);
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
                _batteryLabel.color = on ? AlarmedColor : _tint;
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

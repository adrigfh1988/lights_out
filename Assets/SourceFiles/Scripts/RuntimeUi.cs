using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Small helpers for building UI in code. Everything in this project is assembled at runtime rather
/// than authored in the scene, so this keeps the font lookup and the widget boilerplate in one place.
/// </summary>
public static class RuntimeUi
{
    /// <summary>The project has exactly one font. Find it without needing a serialized reference.</summary>
    public static TMP_FontAsset ResolveFont()
    {
        if (TMP_Settings.defaultFontAsset != null) return TMP_Settings.defaultFontAsset;

        // Fall back to whatever the scene's own text uses. Include inactive: the win panel is hidden.
        foreach (TextMeshProUGUI text in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include))
        {
            if (text.font != null) return text.font;
        }

        return null;
    }

    /// <summary>
    /// Nothing in this scene is clickable without one. The project is Input System only, so this has
    /// to be InputSystemUIInputModule - the legacy StandaloneInputModule would silently do nothing.
    /// </summary>
    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;

        GameObject holder = new GameObject("EventSystem");
        holder.AddComponent<EventSystem>();

        InputSystemUIInputModule module = holder.AddComponent<InputSystemUIInputModule>();
        // Without actions the module reports no input at all; the defaults cover point and click.
        module.AssignDefaultActions();
    }

    /// <summary>The root canvas, creating one only if the scene somehow has none.</summary>
    public static Canvas ResolveCanvas()
    {
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
        {
            if (canvas.transform.parent == null) return canvas;
        }

        Canvas found = Object.FindAnyObjectByType<Canvas>();
        if (found != null) return found;

        GameObject holder = new GameObject("Canvas");
        Canvas created = holder.AddComponent<Canvas>();
        created.renderMode = RenderMode.ScreenSpaceOverlay;
        holder.AddComponent<CanvasScaler>();
        holder.AddComponent<GraphicRaycaster>();
        return created;
    }

    /// <summary>
    /// The scene's scaler is Constant Pixel Size, which pixel-locks every element to whatever
    /// resolution it happens to run at. Scale with screen size instead, against 1080p.
    /// </summary>
    public static void MakeCanvasResolutionIndependent(Canvas canvas)
    {
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    public static GameObject CreatePanel(Transform parent, string name, Color color)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        panel.layer = parent.gameObject.layer;

        Image image = panel.AddComponent<Image>();
        image.color = color;
        Stretch(image.rectTransform);
        return panel;
    }

    public static TextMeshProUGUI CreateText(Transform parent, string name, string content, float fontSize, Color color)
    {
        GameObject holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        holder.layer = parent.gameObject.layer;

        TextMeshProUGUI text = holder.AddComponent<TextMeshProUGUI>();
        text.font = ResolveFont();
        text.fontSize = fontSize;
        text.color = color;
        text.text = content;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    public static void Place(RectTransform rect, Vector2 anchor, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// The Image is left white and the whole look comes from the Button's colour tints, because
    /// Selectable multiplies its tint into the graphic - tinting a dark image just stays dark.
    /// </summary>
    public static Button CreateButton(Transform parent, string label, Vector2 anchoredPosition, Vector2 size,
        float fontSize, Color idle, Color hover, Color textColor)
    {
        GameObject holder = new GameObject(label + " Button");
        holder.transform.SetParent(parent, false);
        holder.layer = parent.gameObject.layer;

        Image image = holder.AddComponent<Image>();
        image.color = Color.white;
        Place(image.rectTransform, new Vector2(0.5f, 0f), anchoredPosition, size);

        Button button = holder.AddComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;
        colors.normalColor = idle;
        colors.highlightedColor = hover;
        colors.pressedColor = new Color(hover.r * 1.3f, hover.g * 1.3f, hover.b * 1.3f, 1f);
        colors.selectedColor = idle;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        TextMeshProUGUI text = CreateText(holder.transform, "Label", label, fontSize, textColor);
        Stretch(text.rectTransform);

        return button;
    }
}

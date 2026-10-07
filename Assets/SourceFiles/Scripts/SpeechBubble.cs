using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F82: one comic-style speech bubble on the project's single Canvas, owned by StalkerVoice. Screen-space:
/// every LateUpdate the world anchor is projected with the main camera. On screen it sits above the anchor
/// with its tail pointing down at it; off screen or behind the camera it sticks to the screen edge with
/// the tail rotated toward the anchor. The line types out letter by letter (OnLetter fires for the letters
/// that should blip), pops in with an overshoot, jitters, optionally glitches single glyphs, holds, then
/// fades while drifting up. Everything runs on scaled time, so a pause freezes it mid-sentence (and it is
/// hidden at timeScale 0). Sprites are generated at runtime - no texture assets - and every graphic has
/// raycastTarget off so touch controls underneath stay clickable.
/// </summary>
public class SpeechBubble : MonoBehaviour
{
    public enum Style { Normal, Shout }

    private enum Phase { Idle, Typing, Holding, Fading }

    [Header("Typing")]
    [SerializeField] private float charsPerSecond = 22f;
    [SerializeField] private float punctuationPause = 0.25f;
    [Tooltip("OnLetter fires every Nth letter (letters only, not spaces or punctuation).")]
    [SerializeField] private int blipEveryNLetters = 2;
    [SerializeField] private float glitchChance = 0.06f;
    [SerializeField] private float glitchSeconds = 0.08f;

    [Header("Look")]
    [SerializeField] private float fontSize = 30f;
    [SerializeField] private float shoutFontScale = 1.3f;
    [SerializeField] private float maxTextWidth = 420f;
    [SerializeField] private float popSeconds = 0.18f;
    [SerializeField] private float tiltDegrees = 4f;
    [SerializeField] private float jitterNormal = 2f;
    [SerializeField] private float jitterShout = 6f;
    [SerializeField] private float holdBaseSeconds = 0.9f;
    [SerializeField] private float holdPerCharSeconds = 0.04f;
    [SerializeField] private float fadeSeconds = 0.35f;
    [SerializeField] private float driftUp = 12f;

    [Header("Placement")]
    [Tooltip("Canvas units kept clear between a clamped bubble and the screen edge.")]
    [SerializeField] private float edgeInset = 90f;
    [SerializeField] private float smoothSeconds = 0.06f;
    [SerializeField] private float tailLength = 24f;

    private const string GlitchGlyphs = "#%&@?!";
    private const string PausePunctuation = ".,?!…";

    /// <summary>F82: the bubble's colours - fill and outline are baked into the generated sprites, text/shout tint the TMP text.</summary>
    public struct Palette
    {
        public Color Fill;
        public Color Outline;
        public Color Text;
        public Color Shout;

        /// <summary>Stan: off-white paper, near-black ink, red shouts.</summary>
        public static Palette Paper => new Palette
        {
            Fill = new Color(0xE8 / 255f, 0xE4 / 255f, 0xDA / 255f, 0.92f),
            Outline = new Color(0x11 / 255f, 0x11 / 255f, 0x11 / 255f, 1f),
            Text = new Color(0x11 / 255f, 0x11 / 255f, 0x11 / 255f, 1f),
            Shout = new Color(0xB0 / 255f, 0x15 / 255f, 0x15 / 255f, 1f),
        };

        /// <summary>The hunter: near-black bubble, blood-red outline, pale text, bright red shouts.</summary>
        public static Palette Night => new Palette
        {
            Fill = new Color(0.04f, 0.035f, 0.035f, 0.9f),
            Outline = new Color(0.55f, 0.04f, 0.04f, 1f),
            Text = new Color(0.88f, 0.84f, 0.82f, 1f),
            Shout = new Color(1f, 0.18f, 0.14f, 1f),
        };
    }

    private Palette _palette = Palette.Paper;

    /// <summary>Fires for each letter that should blip (every blipEveryNLetters-th letter as it is revealed).</summary>
    public event Action<char> OnLetter;

    /// <summary>True from Show until the fade ends or Clear.</summary>
    public bool IsBusy => _phase != Phase.Idle;

    private Canvas _canvas;
    private RectTransform _root;
    private RectTransform _bodyRect;
    private RectTransform _tailRect;
    private RectTransform _textRect;
    private TextMeshProUGUI _text;
    private CanvasGroup _group;
    private Texture2D _bodyTexture;
    private Texture2D _tailTexture;
    private Sprite _bodySprite;
    private Sprite _tailSprite;
    private Camera _camera;
    private float _sightRange = 18f;

    private Phase _phase = Phase.Idle;
    private string _full = "";
    private Style _style;
    private bool _glitchEnabled;
    private Func<Vector3> _anchor;
    private Vector2 _bodySize;

    private float _age;
    private float _phaseTime;
    private float _budget;
    private float _pause;
    private int _shown;
    private int _letters;
    private int _glitchIndex = -1;
    private char _glitchChar;
    private float _glitchUntil;
    private int _appliedGlitch = -1;
    private float _tilt;

    private bool _hasPosition;
    private Vector2 _position;
    private Vector2 _positionVelocity;
    private Vector2 _jitter;
    private float _nextJitterAt;

    /// <summary>Creates the bubble as a child of the canvas (hidden until Show).</summary>
    public static SpeechBubble Create(Canvas canvas, float sightRange, Palette palette)
    {
        GameObject holder = new GameObject("StanSpeechBubble", typeof(RectTransform));
        holder.transform.SetParent(canvas.transform, false);
        holder.layer = canvas.gameObject.layer;
        SpeechBubble bubble = holder.AddComponent<SpeechBubble>();
        bubble._palette = palette;
        bubble.Build(canvas, sightRange);
        return bubble;
    }

    private void Build(Canvas canvas, float sightRange)
    {
        _canvas = canvas.rootCanvas;
        _sightRange = Mathf.Max(1f, sightRange);

        _root = (RectTransform)transform;
        _root.anchorMin = Vector2.zero;
        _root.anchorMax = Vector2.zero;
        _root.pivot = new Vector2(0.5f, 0.5f);

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        BuildSprites();

        // Child order: Tail (behind) -> Body -> Text.
        GameObject tail = new GameObject("Tail", typeof(RectTransform));
        tail.transform.SetParent(transform, false);
        tail.layer = gameObject.layer;
        Image tailImage = tail.AddComponent<Image>();
        tailImage.sprite = _tailSprite;
        tailImage.raycastTarget = false;
        _tailRect = tailImage.rectTransform;
        _tailRect.anchorMin = _tailRect.anchorMax = new Vector2(0.5f, 0.5f);
        _tailRect.pivot = new Vector2(0.5f, 1f);
        _tailRect.sizeDelta = new Vector2(tailLength * 1.15f, tailLength);

        GameObject body = new GameObject("Body", typeof(RectTransform));
        body.transform.SetParent(transform, false);
        body.layer = gameObject.layer;
        Image bodyImage = body.AddComponent<Image>();
        bodyImage.sprite = _bodySprite;
        bodyImage.type = Image.Type.Sliced;
        bodyImage.raycastTarget = false;
        _bodyRect = bodyImage.rectTransform;
        _bodyRect.anchorMin = _bodyRect.anchorMax = new Vector2(0.5f, 0.5f);
        _bodyRect.pivot = new Vector2(0.5f, 0.5f);
        _bodyRect.anchoredPosition = Vector2.zero;

        _text = RuntimeUi.CreateText(transform, "Text", "", fontSize, _palette.Text);
        _text.raycastTarget = false;
        _text.richText = false;
        _text.textWrappingMode = TextWrappingModes.Normal;
        _text.overflowMode = TextOverflowModes.Overflow;
        _text.alignment = TextAlignmentOptions.Center;
        _textRect = _text.rectTransform;
        _textRect.anchorMin = _textRect.anchorMax = new Vector2(0.5f, 0.5f);
        _textRect.pivot = new Vector2(0.5f, 0.5f);
        _textRect.anchoredPosition = Vector2.zero;
    }

    private void OnDestroy()
    {
        if (_bodySprite != null) Destroy(_bodySprite);
        if (_tailSprite != null) Destroy(_tailSprite);
        if (_bodyTexture != null) Destroy(_bodyTexture);
        if (_tailTexture != null) Destroy(_tailTexture);
    }

    // ---------------------------------------------------------------- sprites

    /// <summary>A 64x64 rounded rect (paper fill, 3 px ink outline) 9-sliced with border 20, and a 32x32 triangle tail.</summary>
    private void BuildSprites()
    {
        const int size = 64;
        const float radius = 18f;
        const float outline = 3f;
        _bodyTexture = NewTexture(size, size);
        Color[] pixels = new Color[size * size];
        float half = size * 0.5f;
        float inner = half - 1f - radius;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - half) - inner;
                float qy = Mathf.Abs(y + 0.5f - half) - inner;
                float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                pixels[y * size + x] = ShadePixel(d, outline, _palette);
            }
        }
        _bodyTexture.SetPixels(pixels);
        _bodyTexture.Apply(false, false);
        _bodySprite = Sprite.Create(_bodyTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(20f, 20f, 20f, 20f));

        const int tailSize = 32;
        _tailTexture = NewTexture(tailSize, tailSize);
        Color[] tail = new Color[tailSize * tailSize];
        Vector2 a = new Vector2(3f, 31f), b = new Vector2(29f, 31f), c = new Vector2(16f, 2f), centroid = new Vector2(16f, 20f);
        for (int y = 0; y < tailSize; y++)
        {
            for (int x = 0; x < tailSize; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                // Only the two slanted sides count: the base edge is hidden behind the body anyway.
                float d = Mathf.Max(EdgeDistance(p, a, c, centroid), EdgeDistance(p, b, c, centroid));
                tail[y * tailSize + x] = ShadePixel(d, outline, _palette);
            }
        }
        _tailTexture.SetPixels(tail);
        _tailTexture.Apply(false, false);
        _tailSprite = Sprite.Create(_tailTexture, new Rect(0, 0, tailSize, tailSize), new Vector2(0.5f, 1f), 100f);
    }

    private static Texture2D NewTexture(int w, int h)
    {
        Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.hideFlags = HideFlags.HideAndDontSave;
        return texture;
    }

    /// <summary>Signed distance to a shape (negative inside) -> anti-aliased fill with an ink outline band.</summary>
    private static Color ShadePixel(float d, float outline, Palette palette)
    {
        float cover = Mathf.Clamp01(0.5f - d);
        if (cover <= 0f) return Color.clear;
        float edge = Mathf.Clamp01(d + outline + 0.5f);
        Color color = Color.Lerp(palette.Fill, palette.Outline, edge);
        color.a = Mathf.Lerp(palette.Fill.a, 1f, edge) * cover;
        return color;
    }

    /// <summary>Distance from p to the line a-b, positive on the side away from 'inside'.</summary>
    private static float EdgeDistance(Vector2 p, Vector2 a, Vector2 b, Vector2 inside)
    {
        Vector2 edge = b - a;
        Vector2 normal = new Vector2(-edge.y, edge.x).normalized;
        float d = Vector2.Dot(p - a, normal);
        if (Vector2.Dot(inside - a, normal) > 0f) d = -d;
        return d;
    }

    // ---------------------------------------------------------------- public api

    public void Show(string text, Style style, Func<Vector3> anchor, bool glitch = true)
    {
        if (_root == null || string.IsNullOrEmpty(text) || anchor == null) return;

        _full = text;
        _style = style;
        _anchor = anchor;
        _glitchEnabled = glitch;

        bool shout = style == Style.Shout;
        _text.fontSize = shout ? fontSize * shoutFontScale : fontSize;
        _text.fontStyle = shout ? FontStyles.Bold : FontStyles.Normal;
        _text.color = shout ? _palette.Shout : _palette.Text;
        _text.text = _full;
        _text.maxVisibleCharacters = 0;

        // Size to fit: wrap at maxTextWidth, pad 18 px sideways and 10 px vertically.
        Vector2 preferred = _text.GetPreferredValues(_full, maxTextWidth, 0f);
        float textWidth = Mathf.Min(preferred.x, maxTextWidth) + 4f;
        _textRect.sizeDelta = new Vector2(textWidth, preferred.y);
        _bodySize = new Vector2(textWidth + 36f, preferred.y + 20f);
        _bodyRect.sizeDelta = _bodySize;
        _root.sizeDelta = _bodySize;

        _age = 0f;
        _phaseTime = 0f;
        _budget = 0f;
        _pause = 0f;
        _shown = 0;
        _letters = 0;
        _glitchIndex = -1;
        _appliedGlitch = -1;
        _tilt = UnityEngine.Random.Range(-tiltDegrees, tiltDegrees);
        _hasPosition = false;
        _jitter = Vector2.zero;
        _nextJitterAt = 0f;
        _phase = Phase.Typing;
        _group.alpha = 1f;
        ApplyScale(0f);
    }

    /// <summary>Hides the bubble at once, mid-sentence if need be.</summary>
    public void Clear()
    {
        _phase = Phase.Idle;
        if (_group != null) _group.alpha = 0f;
    }

    // ---------------------------------------------------------------- update

    private void Update()
    {
        if (_phase == Phase.Idle) return;
        if (Time.timeScale <= 0f)
        {
            _group.alpha = 0f; // paused: the run's other menus draw over, the bubble just hides
            return;
        }

        float dt = Time.deltaTime;
        _age += dt;
        _group.alpha = 1f;

        switch (_phase)
        {
            case Phase.Typing:
                UpdateTyping(dt);
                break;
            case Phase.Holding:
                _phaseTime += dt;
                if (_phaseTime >= holdBaseSeconds + holdPerCharSeconds * _full.Length)
                {
                    _phase = Phase.Fading;
                    _phaseTime = 0f;
                }
                break;
            case Phase.Fading:
                _phaseTime += dt;
                if (_phaseTime >= fadeSeconds)
                {
                    Clear();
                    return;
                }
                _group.alpha = 1f - _phaseTime / fadeSeconds;
                break;
        }
    }

    private void UpdateTyping(float dt)
    {
        if (_pause > 0f) _pause -= dt;
        else _budget += dt * charsPerSecond;

        while (_budget >= 1f && _pause <= 0f && _shown < _full.Length)
        {
            _budget -= 1f;
            char c = _full[_shown];
            _shown++;
            if (char.IsLetter(c))
            {
                _letters++;
                if (_glitchEnabled && UnityEngine.Random.value < glitchChance)
                {
                    _glitchIndex = _shown - 1;
                    _glitchChar = GlitchGlyphs[UnityEngine.Random.Range(0, GlitchGlyphs.Length)];
                    _glitchUntil = Time.time + glitchSeconds;
                }
                if (_letters % Mathf.Max(1, blipEveryNLetters) == 0 && OnLetter != null) OnLetter(c);
            }
            else if (PausePunctuation.IndexOf(c) >= 0)
            {
                _pause = punctuationPause;
            }
        }

        // Glitch: a revealed glyph briefly shows a random character, then settles. Done in the string we
        // push to TMP, only on frames where that changes.
        int want = _glitchIndex >= 0 && _glitchIndex < _shown && Time.time < _glitchUntil ? _glitchIndex : -1;
        if (want != _appliedGlitch)
        {
            _appliedGlitch = want;
            if (want < 0)
            {
                _text.text = _full;
            }
            else
            {
                char[] chars = _full.ToCharArray();
                chars[want] = _glitchChar;
                _text.text = new string(chars);
            }
        }
        _text.maxVisibleCharacters = _shown;

        if (_shown >= _full.Length && _pause <= 0f)
        {
            if (_appliedGlitch >= 0)
            {
                _appliedGlitch = -1;
                _text.text = _full;
                _text.maxVisibleCharacters = _shown;
            }
            _phase = Phase.Holding;
            _phaseTime = 0f;
        }
    }

    private void ApplyScale(float distanceScale)
    {
        // Pop-in: 0 -> 1.15 over 60 % of popSeconds (ease out), then settle to 1.
        float t = popSeconds > 0f ? Mathf.Clamp01(_age / popSeconds) : 1f;
        float pop;
        if (t < 0.6f)
        {
            float u = t / 0.6f;
            pop = Mathf.Lerp(0f, 1.15f, 1f - (1f - u) * (1f - u));
        }
        else
        {
            pop = Mathf.Lerp(1.15f, 1f, (t - 0.6f) / 0.4f);
        }
        _root.localScale = Vector3.one * (pop * distanceScale);
    }

    private void LateUpdate()
    {
        if (_phase == Phase.Idle || Time.timeScale <= 0f || _anchor == null) return;
        if (_camera == null) _camera = Camera.main;
        if (_camera == null)
        {
            _group.alpha = 0f;
            return;
        }

        float scale = Mathf.Max(0.01f, _canvas.scaleFactor);
        Vector2 screen = new Vector2(Screen.width, Screen.height) / scale;
        Vector2 center = screen * 0.5f;

        Vector3 world = _anchor();
        Vector3 sp = _camera.WorldToScreenPoint(world);
        Vector2 target = new Vector2(sp.x, sp.y) / scale;
        bool behind = sp.z < 0f;
        Vector2 toTarget = target - center;
        if (behind)
        {
            // Mirror through the screen centre (the point is on the far side) and push it well off screen.
            toTarget = -toTarget;
            if (toTarget.sqrMagnitude < 1f) toTarget = Vector2.down;
            target = center + toTarget.normalized * screen.magnitude;
        }

        float dist = Vector3.Distance(_camera.transform.position, world);
        float distanceScale = Mathf.Lerp(1.15f, 0.7f, Mathf.Clamp01(dist / _sightRange));
        ApplyScale(distanceScale);
        _root.localRotation = Quaternion.Euler(0f, 0f, _tilt);

        Vector2 half = _bodySize * 0.5f * Mathf.Max(0.0001f, distanceScale);
        bool onScreen = !behind
            && target.x >= edgeInset && target.x <= screen.x - edgeInset
            && target.y >= edgeInset && target.y <= screen.y - edgeInset;

        Vector2 desired;
        if (onScreen)
        {
            desired = target + new Vector2(0f, half.y + tailLength * distanceScale);
            desired.x = Mathf.Clamp(desired.x, edgeInset + half.x, Mathf.Max(edgeInset + half.x, screen.x - edgeInset - half.x));
            desired.y = Mathf.Min(desired.y, screen.y - edgeInset - half.y);
        }
        else
        {
            // Ray from the centre toward the target, cut at the rect shrunk by inset + half-extents.
            Vector2 limit = new Vector2(
                Mathf.Max(1f, center.x - edgeInset - half.x),
                Mathf.Max(1f, center.y - edgeInset - half.y));
            float kx = Mathf.Abs(toTarget.x) > 0.001f ? limit.x / Mathf.Abs(toTarget.x) : float.MaxValue;
            float ky = Mathf.Abs(toTarget.y) > 0.001f ? limit.y / Mathf.Abs(toTarget.y) : float.MaxValue;
            float k = Mathf.Min(1f, Mathf.Min(kx, ky));
            desired = center + toTarget * k;
        }

        if (!_hasPosition)
        {
            _position = desired;
            _positionVelocity = Vector2.zero;
            _hasPosition = true;
        }
        else
        {
            _position = Vector2.SmoothDamp(_position, desired, ref _positionVelocity, smoothSeconds);
        }

        // Jitter while typing (30 Hz); shouts shake harder.
        if (_phase == Phase.Typing)
        {
            if (Time.time >= _nextJitterAt)
            {
                _nextJitterAt = Time.time + 1f / 30f;
                _jitter = UnityEngine.Random.insideUnitCircle * (_style == Style.Shout ? jitterShout : jitterNormal);
            }
        }
        else
        {
            _jitter = Vector2.zero;
        }

        float drift = _phase == Phase.Fading ? driftUp * Mathf.Clamp01(_phaseTime / fadeSeconds) : 0f;
        _root.anchoredPosition = _position + _jitter + new Vector2(0f, drift);

        // Tail: points from the bubble toward the target, set on the body edge nearest it. Direction is taken
        // in the bubble's own (tilted) frame so it stays glued to the anchor.
        Vector2 d = target - _position;
        if (d.sqrMagnitude < 1f) d = Vector2.down;
        d = Quaternion.Euler(0f, 0f, -_tilt) * d.normalized;
        Vector2 localHalf = _bodySize * 0.5f;
        float tx = Mathf.Abs(d.x) > 0.001f ? localHalf.x / Mathf.Abs(d.x) : float.MaxValue;
        float ty = Mathf.Abs(d.y) > 0.001f ? localHalf.y / Mathf.Abs(d.y) : float.MaxValue;
        float edge = Mathf.Min(tx, ty);
        _tailRect.anchoredPosition = d * (edge - 2f);
        _tailRect.localRotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.down, d));
    }
}

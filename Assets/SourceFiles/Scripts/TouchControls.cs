using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StarterAssets;

/// <summary>
/// F76: the on-screen controls for the Web build on a tablet - a floating move stick, a look pad, and
/// the buttons every direct key-reader also listens to through <see cref="TouchInput"/>. Built once in
/// code (like every other piece of HUD - see RuntimeUi) by MazeGenerator.SetUpAtmosphere; its own root
/// stays hidden until TouchInput.Active says the on-screen controls should show at all.
///
/// -83: after PlayerHud (-84) builds the HUD it sits above, before MainMenu (-80) builds the title/rules
/// screens (there is only one shared Canvas in this project - see RuntimeUi.ResolveCanvas - so "above
/// the HUD, below the menus" is sibling order, not a separate sortingOrder; the lazily-built pause/shop/
/// end-screen panels call SetAsLastSibling on every show, so they always end up on top regardless).
/// Also drives TouchInput.Poll() every frame and the two StarterAssetsInputs flags (cursorInputForLook,
/// LookIsPixelDelta) that only make sense once a TouchControls exists to own them.
/// </summary>
[DefaultExecutionOrder(-83)]
public class TouchControls : MonoBehaviour
{
    private static readonly Color PanelTintDim = new Color(0.85f, 0.85f, 0.9f, 0.14f);
    private static readonly Color PanelTintLit = new Color(0.95f, 0.85f, 0.55f, 0.55f);

    private const float StickRadius = 150f;
    // Degrees per canvas pixel before ThirdPersonController.LookSensitivity (5 on the prefab): a
    // half-screen swipe (~900 reference px) turns about 160 degrees. The mouse binding uses 0.05.
    private const float LookBaseSensitivity = 0.035f;
    private const int NoPointer = -1;

    // ---------------------------------------------------------------- wiring
    private StarterAssetsInputs _inputs;
    private PlayerStamina _stamina;
    private PlayerInteractor _interactor;
    private ThrowController _throwController;
    private PlayerHud _hud;
    private MainMenu _menu;
    private PauseMenu _pause;
    private ShopMenu _shopMenu;
    private GameOutcome _outcome;
    private LoreReadingUi _lore;

    // ---------------------------------------------------------------- built UI
    private Canvas _canvas;
    private GameObject _root;
    private RectTransform _moveZone;
    private RectTransform _stickBase;
    private RectTransform _stickKnob;

    private Image _interactBg;
    private Image _interactFill;
    private TextMeshProUGUI _interactLabel;
    private GameObject _throwButtonObj;
    private TextMeshProUGUI _throwLabel;
    private GameObject _item1Obj;
    private TextMeshProUGUI _item1Label;
    private GameObject _item2Obj;
    private TextMeshProUGUI _item2Label;
    private Image _sprintBg;

    private GameObject _portraitOverlay;

    // ---------------------------------------------------------------- state
    private bool _prevActive;
    private bool _pausedByRotation;
    private bool _sprintToggled;

    private int _movePointerId = NoPointer;
    private Vector2 _stickBaseLocal;
    private Vector2 _moveVector;

    private Vector2 _lookLastScreenPos;
    private Vector2 _lookAccum;

    /// <summary>Any argument may be null (no rig, no title screen, no shop). Called once by
    /// MazeGenerator.SetUpAtmosphere; debugForce mirrors MazeGenerator's debugForceTouchControls toggle.</summary>
    public void Configure(StarterAssetsInputs inputs, PlayerStamina stamina, PlayerInteractor interactor,
        ThrowController throwController, PlayerHud hud, MainMenu menu, PauseMenu pause, ShopMenu shopMenu,
        GameOutcome outcome, LoreReadingUi lore, bool debugForce)
    {
        _inputs = inputs;
        _stamina = stamina;
        _interactor = interactor;
        _throwController = throwController;
        _hud = hud;
        _menu = menu;
        _pause = pause;
        _shopMenu = shopMenu;
        _outcome = outcome;
        _lore = lore;
        TouchInput.DebugForce = debugForce;
    }

    private void Start()
    {
        // Same reasoning as PlayerHud/MainMenu: build after everything's own initialisation has run.
        RuntimeUi.EnsureEventSystem();
        _canvas = RuntimeUi.ResolveCanvas();
        if (_canvas == null) return;

        BuildRoot();
        BuildPortraitOverlay();
        _root.SetActive(false);
    }

    private void Update()
    {
        if (_root == null) return;

        TouchInput.Poll();
        bool active = TouchInput.Active;
        HandleActiveTransition(active);

        if (_inputs != null)
        {
            // T5: never let the KeyboardMouse look action feed the camera while touch drives it
            // directly through LateUpdate below - otherwise a hybrid touch/mouse device could double up.
            _inputs.cursorInputForLook = !active;
            _inputs.LookIsPixelDelta = active;
        }

        UpdatePortrait(active);

        bool show = active && !_portraitOverlay.activeSelf && ShouldShowControls();
        if (_root.activeSelf != show)
        {
            _root.SetActive(show);
            if (!show)
            {
                // A finger can still be "down" on a button/stick the instant the root is hidden (a
                // lore panel opens, the run ends) - without this the held action would be stuck true.
                TouchInput.ClearAll();
                ReleaseStick();
                _sprintToggled = false;
                if (_inputs != null) _inputs.SprintInput(false);
            }
        }

        if (show)
        {
            UpdateInteractVisual();
            UpdateThrowVisual();
            UpdateItemVisuals();
            UpdateSprint();
        }
    }

    private void LateUpdate()
    {
        // Runs before ThirdPersonController.LateUpdate (order 0) reads StarterAssetsInputs.look, and
        // after every UGUI pointer callback for this frame has already fired (they run during the
        // Update phase) - so this always sees this frame's drag, never last frame's.
        if (_inputs == null || !TouchInput.Active) return;

        if (_lookAccum != Vector2.zero)
        {
            float sensitivity = LookBaseSensitivity * TouchInput.LookSensitivity;
            _inputs.LookInput(_lookAccum * sensitivity);
            _lookAccum = Vector2.zero;
        }
        else
        {
            _inputs.LookInput(Vector2.zero);
        }

        if (_movePointerId != NoPointer)
        {
            _inputs.MoveInput(_moveVector);
        }
    }

    private void OnDisable()
    {
        TouchInput.ClearAll();
    }

    // ------------------------------------------------------------ visibility

    private void HandleActiveTransition(bool active)
    {
        if (active == _prevActive) return;
        _prevActive = active;

        if (active)
        {
            PlayerLock.SetCursorFree(true);
        }
        else
        {
            TouchInput.ClearAll();
            ReleaseStick();
            _sprintToggled = false;
            if (_inputs != null) _inputs.SprintInput(false);

            // Switching back to keyboard/mouse mid-run: re-lock the cursor exactly as StartGame/Resume
            // would, but only if nothing else already owns the cursor.
            if (GameFlow.IsRunActive && ShouldShowControls())
            {
                PlayerLock.SetCursorFree(false);
            }
        }
    }

    private bool ShouldShowControls()
    {
        if (_menu != null && _menu.IsOpen) return false;
        if (_pause != null && _pause.IsPaused) return false;
        if (_shopMenu != null && _shopMenu.IsOpen) return false;
        if (_lore != null && _lore.IsOpen) return false;
        if (GameOutcome.IsOver) return false;
        if (_outcome != null && _outcome.IsEnding) return false;
        if (!GameFlow.IsRunActive && !GameFlow.IsInShop) return false;
        return true;
    }

    private void UpdatePortrait(bool active)
    {
        bool portrait = active && Screen.height > Screen.width;
        if (portrait == _portraitOverlay.activeSelf) return;

        _portraitOverlay.SetActive(portrait);
        if (portrait)
        {
            _portraitOverlay.transform.SetAsLastSibling();
            // Goes through PauseMenu so the pause/resume rules stay single-sourced (T7) - and only
            // resumes on the way out if this overlay is the one that paused (ExternalPause returns
            // false if something else already had it paused, or CanPause() says no).
            _pausedByRotation = _pause != null && _pause.ExternalPause();
        }
        else if (_pausedByRotation)
        {
            _pausedByRotation = false;
            if (_pause != null) _pause.ExternalResume();
        }
    }

    // ------------------------------------------------------------ per-frame visuals

    private void UpdateInteractVisual()
    {
        string prompt = _hud != null ? _hud.CurrentPrompt : null;
        bool hasFocus = _interactor != null && _interactor.HasFocus;

        if (_interactLabel != null) _interactLabel.text = string.IsNullOrEmpty(prompt) ? "INTERACT" : StripPromptForButton(prompt);
        if (_interactBg != null) _interactBg.color = hasFocus ? PanelTintLit : PanelTintDim;

        float progress = _interactor != null ? _interactor.HoldProgress : 0f;
        if (_interactFill != null)
        {
            _interactFill.fillAmount = progress;
            _interactFill.gameObject.SetActive(progress > 0.001f);
        }
    }

    private void UpdateThrowVisual()
    {
        int carried = _throwController != null ? _throwController.Carried : 0;
        if (_throwButtonObj != null) _throwButtonObj.SetActive(carried > 0);
        if (carried > 0 && _throwLabel != null) _throwLabel.text = $"THROW x{carried}";
    }

    private void UpdateItemVisuals()
    {
        int battery = PlayerInventory.Count(ShopItem.SpareBattery);
        if (_item1Obj != null) _item1Obj.SetActive(battery > 0);
        if (battery > 0 && _item1Label != null) _item1Label.text = $"BATTERY x{battery}";

        int compass = PlayerInventory.Count(ShopItem.StarCompass);
        if (_item2Obj != null) _item2Obj.SetActive(compass > 0);
        if (compass > 0 && _item2Label != null) _item2Label.text = $"COMPASS x{compass}";
    }

    private void ToggleSprint()
    {
        _sprintToggled = !_sprintToggled;
    }

    private void UpdateSprint()
    {
        if (_movePointerId == NoPointer) _sprintToggled = false; // auto-off when the stick is released
        if (_stamina != null && _stamina.Exhausted) _sprintToggled = false;

        if (_inputs != null) _inputs.SprintInput(_sprintToggled);
        if (_sprintBg != null) _sprintBg.color = _sprintToggled ? PanelTintLit : PanelTintDim;
    }

    /// <summary>"E  OPEN" / "E  RESTORE POWER  53%" -> "OPEN" / "RESTORE POWER" - the radial fill
    /// already shows hold progress, so the trailing percentage would just be noise on a small button.</summary>
    private static string StripPromptForButton(string prompt)
    {
        string s = prompt.TrimStart();
        if (s.Length > 0 && s[0] == 'E') s = s.Substring(1).TrimStart();

        int percentIndex = s.IndexOf('%');
        if (percentIndex >= 0)
        {
            int cut = percentIndex + 1;
            while (cut > 0 && !char.IsWhiteSpace(s[cut - 1])) cut--;
            while (cut > 0 && char.IsWhiteSpace(s[cut - 1])) cut--;
            s = s.Substring(0, cut);
        }
        return s.Trim();
    }

    // ------------------------------------------------------------ stick / look pad callbacks

    private void BeginStick(PointerEventData eventData)
    {
        _movePointerId = eventData.pointerId;
        TouchInput.NotifyTouchActivity();

        RectTransformUtility.ScreenPointToLocalPointInRectangle(_moveZone, eventData.position, eventData.pressEventCamera, out Vector2 local);
        _stickBaseLocal = local;
        _moveVector = Vector2.zero;

        _stickBase.gameObject.SetActive(true);
        _stickBase.anchoredPosition = local;
        _stickKnob.anchoredPosition = local;
    }

    private void DragStick(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_moveZone, eventData.position, eventData.pressEventCamera, out Vector2 local);
        Vector2 offset = local - _stickBaseLocal;
        if (offset.magnitude > StickRadius) offset = offset.normalized * StickRadius;

        _stickKnob.anchoredPosition = _stickBaseLocal + offset;
        _moveVector = offset / StickRadius;
    }

    private void EndStick()
    {
        _movePointerId = NoPointer;
        _moveVector = Vector2.zero;
        if (_inputs != null) _inputs.MoveInput(Vector2.zero);
        if (_stickBase != null) _stickBase.gameObject.SetActive(false);
    }

    private void ReleaseStick()
    {
        if (_movePointerId != NoPointer) EndStick();
    }

    // ------------------------------------------------------------ build

    private void BuildRoot()
    {
        _root = new GameObject("TouchControls");
        _root.transform.SetParent(_canvas.transform, false);
        _root.layer = _canvas.gameObject.layer;
        RectTransform rootRect = _root.AddComponent<RectTransform>();
        RuntimeUi.Stretch(rootRect);

        BuildMoveZone(_root.transform);
        BuildLookZone(_root.transform);
        BuildStick();

        // Buttons are built after the zones so they win the raycast over the full-area look pad
        // underneath them (later sibling draws - and hit-tests - on top).
        BuildInteractButton(_root.transform);
        BuildTorchButton(_root.transform);
        BuildFocusButton(_root.transform);
        BuildThrowButton(_root.transform);
        BuildItemButtons(_root.transform);
        BuildSprintButton(_root.transform);
        BuildPauseButton(_root.transform);
    }

    private void BuildMoveZone(Transform parent)
    {
        GameObject zone = new GameObject("MoveZone");
        zone.transform.SetParent(parent, false);
        zone.layer = parent.gameObject.layer;

        Image image = zone.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        _moveZone = image.rectTransform;
        _moveZone.anchorMin = new Vector2(0f, 0f);
        _moveZone.anchorMax = new Vector2(0.45f, 0.62f);
        _moveZone.offsetMin = Vector2.zero;
        _moveZone.offsetMax = Vector2.zero;

        StickHandler handler = zone.AddComponent<StickHandler>();
        handler.Owner = this;
    }

    private void BuildLookZone(Transform parent)
    {
        GameObject zone = new GameObject("LookZone");
        zone.transform.SetParent(parent, false);
        zone.layer = parent.gameObject.layer;

        Image image = zone.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2(0.42f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        LookPadHandler handler = zone.AddComponent<LookPadHandler>();
        handler.Owner = this;
    }

    private void BuildStick()
    {
        GameObject baseObj = new GameObject("StickBase");
        baseObj.transform.SetParent(_moveZone, false);
        baseObj.layer = _moveZone.gameObject.layer;
        Image baseImage = baseObj.AddComponent<Image>();
        baseImage.color = new Color(1f, 1f, 1f, 0.18f);
        baseImage.raycastTarget = false;
        _stickBase = baseImage.rectTransform;
        RuntimeUi.Place(_stickBase, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(StickRadius * 2f, StickRadius * 2f));

        GameObject knobObj = new GameObject("StickKnob");
        knobObj.transform.SetParent(_moveZone, false);
        knobObj.layer = _moveZone.gameObject.layer;
        Image knobImage = knobObj.AddComponent<Image>();
        knobImage.color = new Color(1f, 1f, 1f, 0.42f);
        knobImage.raycastTarget = false;
        _stickKnob = knobImage.rectTransform;
        RuntimeUi.Place(_stickKnob, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80f, 80f));

        _stickBase.gameObject.SetActive(false);
    }

    private (GameObject holder, Image bg, TextMeshProUGUI label) CreateHoldButton(
        Transform parent, TouchInput.TouchAction action, string label,
        Vector2 anchor, Vector2 anchoredPosition, Vector2 size, float fontSize)
    {
        GameObject holder = new GameObject(label + " TouchButton");
        holder.transform.SetParent(parent, false);
        holder.layer = parent.gameObject.layer;

        Image bg = holder.AddComponent<Image>();
        bg.color = PanelTintDim;
        RuntimeUi.Place(bg.rectTransform, anchor, anchoredPosition, size);

        TextMeshProUGUI text = RuntimeUi.CreateText(holder.transform, "Label", label, fontSize, Color.white);
        RuntimeUi.Stretch(text.rectTransform);

        HoldButtonHandler handler = holder.AddComponent<HoldButtonHandler>();
        handler.Action = action;

        return (holder, bg, text);
    }

    private void BuildInteractButton(Transform parent)
    {
        (GameObject holder, Image bg, TextMeshProUGUI label) = CreateHoldButton(parent, TouchInput.TouchAction.Interact, "INTERACT",
            new Vector2(1f, 0f), new Vector2(-220f, 230f), new Vector2(190f, 190f), 26f);
        _interactBg = bg;
        _interactLabel = label;

        GameObject fillObj = new GameObject("HoldFill");
        fillObj.transform.SetParent(holder.transform, false);
        fillObj.layer = holder.layer;
        Image fill = fillObj.AddComponent<Image>();
        fill.sprite = WhiteSprite();
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Radial360;
        fill.color = new Color(1f, 0.9f, 0.5f, 0.5f);
        fill.raycastTarget = false;
        RuntimeUi.Stretch(fill.rectTransform);
        fill.fillAmount = 0f;
        fillObj.transform.SetAsFirstSibling(); // under the label, above the background
        _interactFill = fill;
    }

    private void BuildTorchButton(Transform parent)
    {
        CreateHoldButton(parent, TouchInput.TouchAction.Torch, "TORCH",
            new Vector2(1f, 0f), new Vector2(-440f, 340f), new Vector2(130f, 130f), 22f);
    }

    private void BuildFocusButton(Transform parent)
    {
        CreateHoldButton(parent, TouchInput.TouchAction.Focus, "FOCUS",
            new Vector2(1f, 0f), new Vector2(-440f, 190f), new Vector2(130f, 130f), 22f);
    }

    private void BuildThrowButton(Transform parent)
    {
        (GameObject holder, Image _, TextMeshProUGUI label) = CreateHoldButton(parent, TouchInput.TouchAction.Throw, "THROW",
            new Vector2(1f, 0f), new Vector2(-220f, 450f), new Vector2(130f, 130f), 20f);
        _throwButtonObj = holder;
        _throwLabel = label;
        _throwButtonObj.SetActive(false);
    }

    private void BuildItemButtons(Transform parent)
    {
        (GameObject holder1, Image _, TextMeshProUGUI label1) = CreateHoldButton(parent, TouchInput.TouchAction.Item1, "BATTERY",
            new Vector2(1f, 0f), new Vector2(-650f, 300f), new Vector2(120f, 100f), 18f);
        _item1Obj = holder1;
        _item1Label = label1;
        _item1Obj.SetActive(false);

        (GameObject holder2, Image _, TextMeshProUGUI label2) = CreateHoldButton(parent, TouchInput.TouchAction.Item2, "COMPASS",
            new Vector2(1f, 0f), new Vector2(-650f, 190f), new Vector2(120f, 100f), 18f);
        _item2Obj = holder2;
        _item2Label = label2;
        _item2Obj.SetActive(false);
    }

    private void BuildSprintButton(Transform parent)
    {
        GameObject holder = new GameObject("SPRINT TouchButton");
        holder.transform.SetParent(parent, false);
        holder.layer = parent.gameObject.layer;

        Image bg = holder.AddComponent<Image>();
        bg.color = PanelTintDim;
        RuntimeUi.Place(bg.rectTransform, new Vector2(0f, 0f), new Vector2(190f, 380f), new Vector2(150f, 100f));
        _sprintBg = bg;

        TextMeshProUGUI label = RuntimeUi.CreateText(holder.transform, "Label", "SPRINT", 22f, Color.white);
        RuntimeUi.Stretch(label.rectTransform);

        TapToggleHandler handler = holder.AddComponent<TapToggleHandler>();
        handler.OnTap = ToggleSprint;
    }

    private void BuildPauseButton(Transform parent)
    {
        CreateHoldButton(parent, TouchInput.TouchAction.Pause, "II",
            new Vector2(1f, 1f), new Vector2(-70f, -70f), new Vector2(110f, 90f), 30f);
    }

    private void BuildPortraitOverlay()
    {
        _portraitOverlay = RuntimeUi.CreatePanel(_canvas.transform, "RotateDeviceOverlay", new Color(0.01f, 0.01f, 0.02f, 0.96f));

        TextMeshProUGUI text = RuntimeUi.CreateText(_portraitOverlay.transform, "Message", "ROTATE YOUR DEVICE", 52f, Color.white);
        text.characterSpacing = 6f;
        RuntimeUi.Place(text.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 220f));

        _portraitOverlay.SetActive(false);
    }

    // A single shared 1x1 white sprite so the hold-progress radial fill (Image.Type.Filled) has
    // something to mask - with no sprite assigned Image.fillAmount is silently ignored.
    private static Sprite _whiteSprite;

    private static Sprite WhiteSprite()
    {
        if (_whiteSprite == null)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "TouchControlsWhite" };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            _whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
        }
        return _whiteSprite;
    }

    // ------------------------------------------------------------ pointer handlers

    /// <summary>Held while a finger is down, tracked by pointerId so a second finger elsewhere cannot
    /// steal or drop it. Used for every button except SPRINT, which is tap-to-toggle.</summary>
    private sealed class HoldButtonHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public TouchInput.TouchAction Action;
        private int _pointerId = NoPointer;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_pointerId != NoPointer) return;
            _pointerId = eventData.pointerId;
            TouchInput.SetHeld(Action, true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;
            _pointerId = NoPointer;
            TouchInput.SetHeld(Action, false);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // A finger sliding off the button releases it, like a physical button would.
            if (eventData.pointerId != _pointerId) return;
            _pointerId = NoPointer;
            TouchInput.SetHeld(Action, false);
        }

        private void OnDisable()
        {
            if (_pointerId == NoPointer) return;
            _pointerId = NoPointer;
            TouchInput.SetHeld(Action, false);
        }
    }

    private sealed class TapToggleHandler : MonoBehaviour, IPointerClickHandler
    {
        public System.Action OnTap;

        public void OnPointerClick(PointerEventData eventData)
        {
            TouchInput.NotifyTouchActivity();
            OnTap?.Invoke();
        }
    }

    private sealed class StickHandler : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public TouchControls Owner;
        private int _pointerId = NoPointer;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_pointerId != NoPointer) return;
            _pointerId = eventData.pointerId;
            Owner.BeginStick(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;
            Owner.DragStick(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;
            _pointerId = NoPointer;
            Owner.EndStick();
        }

        private void OnDisable()
        {
            if (_pointerId == NoPointer) return;
            _pointerId = NoPointer;
            Owner.EndStick();
        }
    }

    /// <summary>Full-area drag pad behind the buttons. Tracks its own pointerId separately from the
    /// stick's, so moving and looking work from two fingers at once.</summary>
    private sealed class LookPadHandler : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public TouchControls Owner;
        private int _pointerId = NoPointer;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_pointerId != NoPointer) return;
            _pointerId = eventData.pointerId;
            Owner._lookLastScreenPos = eventData.position;
            TouchInput.NotifyTouchActivity();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;

            Vector2 delta = eventData.position - Owner._lookLastScreenPos;
            Owner._lookLastScreenPos = eventData.position;

            float scale = Owner._canvas != null && Owner._canvas.scaleFactor > 0.0001f ? Owner._canvas.scaleFactor : 1f;
            // Y is flipped to match the mouse binding's InvertVector2(invertX=false) processor, which a
            // touch drag bypasses: dragging up looks up, the same as moving the mouse up.
            delta.y = -delta.y;
            Owner._lookAccum += delta / scale;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;
            _pointerId = NoPointer;
        }

        private void OnDisable()
        {
            _pointerId = NoPointer;
        }
    }
}

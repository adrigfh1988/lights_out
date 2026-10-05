using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// F76: the one facade every direct key-reader (Flashlight, PlayerInteractor, Locker, ThrowController,
/// ConsumableController, PauseMenu, ShopMenu, ShopRoom) ORs in next to its keyboard/gamepad check, plus
/// the auto-detect for whether the on-screen controls should show at all (<see cref="Active"/>).
///
/// A plain static class per CLAUDE.md's "statics must survive a scene reload and must not survive Play
/// mode" rule - <see cref="ResetStatics"/> below. Edge detection uses <see cref="Time.frameCount"/>
/// (unaffected by Time.timeScale, unlike Time.time) rather than a per-frame Update pump: buttons write
/// through <see cref="SetHeld"/> the instant UGUI's pointer events fire, and every reader - regardless
/// of its own script execution order relative to the EventSystem - sees Pressed/Released exactly once,
/// one frame after the edge. Reading it on the same frame as the edge would be a race against whichever
/// of TouchControls, EventSystem and the 8 direct readers happens to run first that frame.
/// </summary>
public static class TouchInput
{
    public enum TouchAction { Interact, Torch, Focus, Throw, Item1, Item2, Pause, Sprint }

    /// <summary>The pause menu's "TOUCH CONTROLS" setting. Auto is the default: on-screen controls
    /// follow real touch/keyboard activity. On/Off pin them regardless.</summary>
    public enum Mode { Auto, On, Off }

    private const int ActionCount = 8;
    private const string ModePrefKey = "LightsOut.TouchControls.Mode";
    private const string SensitivityPrefKey = "LightsOut.TouchControls.LookSensitivity";
    public const float DefaultLookSensitivity = 1f;
    public const float MinLookSensitivity = 0.25f;
    public const float MaxLookSensitivity = 3f;

    // Squared screen-space mouse delta (px) that counts as "the mouse moved" for the auto-off check.
    private const float MouseMoveThresholdSq = 4f;
    // Compatibility mouse/pointer events a WebGL touch can synthesise are ignored for this long after
    // any real touch activity, so a tablet does not flicker between touch and mouse mode.
    private const float TouchActivityGrace = 0.5f;

    private static readonly bool[] _held = new bool[ActionCount];
    private static readonly int[] _pressedFrame = new int[ActionCount];
    private static readonly int[] _releasedFrame = new int[ActionCount];

    private static int _lastPollFrame = -1;
    private static float _lastTouchActivityTime = -100f;

    /// <summary>True while the on-screen controls should be visible and driving input. See T2 in
    /// plannings/tablet-touch-plan.md for the auto-detect rules.</summary>
    public static bool Active { get; private set; }

    /// <summary>MazeGenerator's Inspector debug toggle. Forces Active on regardless of Mode, for
    /// testing on-screen controls in the Editor without a touchscreen.</summary>
    public static bool DebugForce;

    public static Mode CurrentMode { get; private set; } = Mode.Auto;

    public static float LookSensitivity { get; private set; } = DefaultLookSensitivity;

    static TouchInput()
    {
        LoadPrefs();
    }

    // With Enter Play Mode > Reload Domain disabled, statics survive between presses of Play - Active
    // and the frame bookkeeping are per-session state and must not leak into the next one. The saved
    // Mode/LookSensitivity are genuine settings, so they are reloaded fresh rather than zeroed.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Active = false;
        DebugForce = false;
        _lastPollFrame = -1;
        _lastTouchActivityTime = -100f;
        ClearAll();
        LoadPrefs();
    }

    private static void LoadPrefs()
    {
        try
        {
            int modeValue = PlayerPrefs.GetInt(ModePrefKey, (int)Mode.Auto);
            CurrentMode = modeValue >= 0 && modeValue <= 2 ? (Mode)modeValue : Mode.Auto;
            LookSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityPrefKey, DefaultLookSensitivity), MinLookSensitivity, MaxLookSensitivity);
        }
        catch
        {
            CurrentMode = Mode.Auto;
            LookSensitivity = DefaultLookSensitivity;
        }
    }

    /// <summary>Saved to PlayerPrefs. Called by the pause menu's TOUCH CONTROLS button.</summary>
    public static void SetMode(Mode mode)
    {
        CurrentMode = mode;
        try
        {
            PlayerPrefs.SetInt(ModePrefKey, (int)mode);
            PlayerPrefs.Save();
        }
        catch
        {
            // Nothing to do - the in-memory value above still applies for this session.
        }
    }

    /// <summary>Saved to PlayerPrefs. Called by the pause menu's LOOK SENSITIVITY slider.</summary>
    public static void SetLookSensitivity(float value)
    {
        LookSensitivity = Mathf.Clamp(value, MinLookSensitivity, MaxLookSensitivity);
        try
        {
            PlayerPrefs.SetFloat(SensitivityPrefKey, LookSensitivity);
            PlayerPrefs.Save();
        }
        catch
        {
            // In-memory value still applies for this session.
        }
    }

    /// <summary>A touch happened somewhere (a button, the stick or the look pad) - used to debounce the
    /// synthetic mouse events some WebGL browsers raise after a real touch.</summary>
    public static void NotifyTouchActivity()
    {
        _lastTouchActivityTime = Time.unscaledTime;
    }

    /// <summary>Written by TouchControls' button/stick/pad handlers on pointer down/up. Only stamps the
    /// frame on a genuine edge, so a handler calling this every frame while held does not matter.</summary>
    public static void SetHeld(TouchAction action, bool held)
    {
        int i = (int)action;
        if (_held[i] == held) return;
        _held[i] = held;
        if (held)
        {
            _pressedFrame[i] = Time.frameCount;
            NotifyTouchActivity();
        }
        else
        {
            _releasedFrame[i] = Time.frameCount;
        }
    }

    public static bool Held(TouchAction action) => _held[(int)action];

    /// <summary>True on the frame after SetHeld(action, true) was called - one frame later than the
    /// physical tap, so every reader sees it exactly once regardless of script execution order.</summary>
    public static bool Pressed(TouchAction action) => _pressedFrame[(int)action] == Time.frameCount - 1;

    public static bool Released(TouchAction action) => _releasedFrame[(int)action] == Time.frameCount - 1;

    /// <summary>Drops every button back to not-held with no edge event. Called by TouchControls whenever
    /// it hides its root mid-press (a menu opens, the run ends) so a finger left "down" on a button that
    /// just vanished cannot leave PlayerInteractor/Flashlight/etc. reading a stuck Held forever.</summary>
    public static void ClearAll()
    {
        for (int i = 0; i < ActionCount; i++)
        {
            _held[i] = false;
            _pressedFrame[i] = int.MinValue;
            _releasedFrame[i] = int.MinValue;
        }
    }

    /// <summary>Formats a key hint so it reads sensibly on-screen even while touch is active, e.g.
    /// TouchInput.Key("E", "TAP") -> "E" on keyboard, "TAP" while the on-screen controls are up.</summary>
    public static string Key(string keyboardLabel, string touchLabel) => Active ? touchLabel : keyboardLabel;

    /// <summary>
    /// Updates <see cref="Active"/> for this frame. Idempotent per frame (TouchControls' own Update
    /// calls it, but so does MainMenu on the very first tap, before TouchControls' -83 Update would
    /// otherwise see it) - a second call the same frame is a no-op.
    /// </summary>
    public static void Poll()
    {
        int frame = Time.frameCount;
        if (frame == _lastPollFrame) return;
        _lastPollFrame = frame;

        if (DebugForce)
        {
            Active = true;
            return;
        }

        switch (CurrentMode)
        {
            case Mode.On:
                Active = true;
                return;
            case Mode.Off:
                Active = false;
                return;
        }

#if ENABLE_INPUT_SYSTEM
        if (!Active)
        {
            bool mobile = Application.isMobilePlatform;
            bool touchBegan = Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
            if (touchBegan) NotifyTouchActivity();
            if (mobile || touchBegan) Active = true;
        }
        else
        {
            bool keyPressed = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
            bool mousePressed = Mouse.current != null &&
                (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame ||
                 Mouse.current.delta.ReadValue().sqrMagnitude > MouseMoveThresholdSq);

            // iPadOS Safari reports as desktop Mac (Application.isMobilePlatform is false there) and a
            // touch can raise a compatibility mouse event - ignore both for a short grace window after
            // any genuine touch activity so a tablet does not flicker between modes.
            bool withinTouchGrace = Time.unscaledTime - _lastTouchActivityTime < TouchActivityGrace;
            if ((keyPressed || mousePressed) && !withinTouchGrace) Active = false;
        }
#endif
    }
}

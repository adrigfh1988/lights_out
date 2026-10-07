using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// F72 decision D12: the one interaction system every new pickup/door/button goes through. A camera
/// spherecast each frame finds the nearest IInteractable in range; E presses it (or, for a hold
/// interactable, fills progress while held). The HUD's focus prompt overrides Locker's trigger prompt
/// while something is focused - see PlayerHud.SetFocusPrompt.
///
/// Added to the player by MazeGenerator.SetUpAtmosphere, once the camera and HUD exist.
/// </summary>
[DefaultExecutionOrder(-10)]
public class PlayerInteractor : MonoBehaviour
{
    [Tooltip("Sphere radius of the interact probe (m)")]
    [SerializeField] private float probeRadius = 0.25f;
    [Tooltip("How far the probe reaches (m)")]
    [SerializeField] private float probeDistance = 2.4f;

    private Camera _camera;
    private PlayerHud _hud;
    private PlayerStealthState _stealth;

    private IInteractable _focused;
    private float _holdElapsed;

    /// <summary>True while something interactable is in range and lit up - Locker yields E to it (see Locker.Update).</summary>
    public bool HasFocus => _focused != null;

    /// <summary>F76: 0..1 progress toward completing the focused hold interaction, for TouchControls'
    /// INTERACT radial fill. 0 when nothing is focused or the focused interactable is not a hold one.</summary>
    public float HoldProgress => _focused != null && _focused.HoldSeconds > 0f ? Mathf.Clamp01(_holdElapsed / _focused.HoldSeconds) : 0f;

    /// <summary>The camera the probe casts from - read by F74's Lantern/theme interactables that need it for their own purposes (parenting a held prop, a screen-space marker) rather than duplicating the reference.</summary>
    public Camera Camera => _camera;

    /// <summary>
    /// F74 decision: "coordinate with PlayerInteractor rather than adding a second E reader". Something
    /// that just took over the player's E key without being Probed (Lantern while carried, LoreReadingUi
    /// while its panel is open) sets this so a press with no focused interactable still goes somewhere.
    /// Cleared by whoever set it once its own state ends.
    /// </summary>
    public IInteractable Fallback { get; set; }

    /// <summary>Called once by MazeGenerator.SetUpAtmosphere. Any argument may be null (no rig, no HUD).</summary>
    public void Configure(Camera playerCamera, PlayerHud hud, PlayerStealthState stealth)
    {
        _camera = playerCamera;
        _hud = hud;
        _stealth = stealth;
    }

    private void Update()
    {
        if (_camera == null) return;

        if (!IsActive())
        {
            ClearFocus();
            return;
        }

        IInteractable hit = Probe();
        if (hit != _focused)
        {
            _focused?.OnFocus(false);
            _focused = hit;
            _holdElapsed = 0f;
            _focused?.OnFocus(true);
        }

        ReadInput(out bool pressed, out bool held);

        if (_focused == null)
        {
            // F74: nothing under the crosshair, but something (Lantern while carried, LoreReadingUi
            // while open) still owns E - see the Fallback doc comment.
            if (Fallback != null && Fallback.CanInteract)
            {
                if (_hud != null) _hud.SetFocusPrompt($"E  {Fallback.Prompt}");
                if (pressed) Fallback.Interact(this);
            }
            else if (_hud != null)
            {
                _hud.SetFocusPrompt(null);
            }
            return;
        }

        bool canInteract = _focused.CanInteract;

        if (_focused.HoldSeconds <= 0f)
        {
            if (pressed && canInteract) _focused.Interact(this);
            if (_hud != null) _hud.SetFocusPrompt(canInteract ? $"E  {_focused.Prompt}" : null);
        }
        else
        {
            if (held && canInteract)
            {
                _holdElapsed += Time.deltaTime;
                if (_holdElapsed >= _focused.HoldSeconds)
                {
                    IInteractable completed = _focused;
                    _holdElapsed = 0f;
                    completed.Interact(this);
                }
                else if (_hud != null)
                {
                    int percent = Mathf.RoundToInt(Mathf.Clamp01(_holdElapsed / _focused.HoldSeconds) * 100f);
                    _hud.SetFocusPrompt($"E  {_focused.Prompt}  {percent}%");
                }
            }
            else
            {
                _holdElapsed = 0f;
                if (_hud != null) _hud.SetFocusPrompt(canInteract ? $"E  {_focused.Prompt}" : null);
            }
        }
    }

    /// <summary>
    /// Off while hidden in a locker, before the run starts, once it is over, in the shop, and under the
    /// pause menu / title screen - all of which stand the clock still, so Time.timeScale is the cheap
    /// single test for the last one instead of a PauseMenu reference (matches CLAUDE.md's Time.time note).
    /// </summary>
    private bool IsActive()
    {
        if (Time.timeScale <= 0f) return false;
        if (!(GameFlow.IsRunActive || GameFlow.IsInIntake) || GameOutcome.IsOver || GameFlow.IsInShop) return false;
        if (_stealth != null && _stealth.Hidden) return false;
        return true;
    }

    private void ClearFocus()
    {
        if (_focused != null)
        {
            _focused.OnFocus(false);
            _focused = null;
        }
        _holdElapsed = 0f;
        if (_hud != null) _hud.SetFocusPrompt(null);
    }

    private static readonly RaycastHit[] Hits = new RaycastHit[16];

    private IInteractable Probe()
    {
        Ray ray = new Ray(_camera.transform.position, _camera.transform.forward);
        int count = Physics.SphereCastNonAlloc(ray, probeRadius, Hits, probeDistance, ~0, QueryTriggerInteraction.Collide);

        IInteractable best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            IInteractable candidate = Hits[i].collider.GetComponentInParent<IInteractable>();
            if (candidate == null || !candidate.CanInteract) continue;
            if (Hits[i].distance >= bestDistance) continue;

            bestDistance = Hits[i].distance;
            best = candidate;
        }
        return best;
    }

    private void ReadInput(out bool pressed, out bool held)
    {
#if ENABLE_INPUT_SYSTEM
        pressed = (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
               || (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame)
               || TouchInput.Pressed(TouchInput.TouchAction.Interact);
        held = (Keyboard.current != null && Keyboard.current.eKey.isPressed)
            || (Gamepad.current != null && Gamepad.current.buttonWest.isPressed)
            || TouchInput.Held(TouchInput.TouchAction.Interact);
#else
        pressed = false;
        held = false;
#endif
    }
}

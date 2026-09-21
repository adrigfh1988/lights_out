using System.Collections;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Hotkeys for the two active consumables (F33): 1 refills the torch from a Spare Battery, 2 points a
/// Star Compass chevron at the nearest live star for a few seconds. Both are held items bought in the
/// shop and spent here.
/// </summary>
public class ConsumableController : MonoBehaviour
{
    private static readonly Color CompassColor = new Color(1f, 0.64f, 0.19f);
    private const float ChevronRadius = 70f;
    private const float CompassSeconds = 5f;
    private const float CompassFadeOut = 0.6f;

    private Transform _player;
    private Flashlight _flashlight;
    private PlayerStealthState _stealth;
    private MazeGenerator _maze;
    private PlayerHud _hud;
    private MazeEscape _escape;
    private GameOutcome _outcome;
    private MainMenu _menu;
    private PauseMenu _pause;

    private TextMeshProUGUI _chevron;
    private Coroutine _compassRoutine;
    private bool _retired;

    public void Configure(Transform player, Flashlight flashlight, PlayerStealthState stealth, MazeGenerator maze,
        PlayerHud hud, MazeEscape escape, GameOutcome outcome, MainMenu menu, PauseMenu pause)
    {
        _player = player;
        _flashlight = flashlight;
        _stealth = stealth;
        _maze = maze;
        _hud = hud;
        _escape = escape;
        _outcome = outcome;
        _menu = menu;
        _pause = pause;
    }

    private void OnEnable()
    {
        GameManager.AllStarsCollected += Retire;
    }

    private void OnDisable()
    {
        GameManager.AllStarsCollected -= Retire;
    }

    /// <summary>The compass has nothing left to point at once every star is gone - the hatch chevron takes over.</summary>
    private void Retire()
    {
        _retired = true;
        StopCompass();
    }

    private void Update()
    {
        if (!GameFlow.IsRunActive || GameOutcome.IsOver) return;
        if (_outcome != null && _outcome.IsEnding) return;
        if (_menu != null && _menu.IsOpen) return;
        if (_pause != null && _pause.IsPaused) return;

        bool slot1 = false;
        bool slot2 = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame || Keyboard.current.numpad1Key.wasPressedThisFrame) slot1 = true;
            if (Keyboard.current.digit2Key.wasPressedThisFrame || Keyboard.current.numpad2Key.wasPressedThisFrame) slot2 = true;
        }
        if (Gamepad.current != null)
        {
            if (Gamepad.current.dpad.left.wasPressedThisFrame) slot1 = true;
            if (Gamepad.current.dpad.right.wasPressedThisFrame) slot2 = true;
        }
#endif
        if (slot1) UseBattery();
        if (slot2) UseCompass();
    }

    private void UseBattery()
    {
        if (PlayerInventory.Count(ShopItem.SpareBattery) == 0)
        {
            if (_hud != null) _hud.ShowSubtitle("No spare battery.", 1.8f);
            return;
        }
        if (_stealth != null && _stealth.Hidden)
        {
            if (_hud != null) _hud.ShowSubtitle("Not in here.", 1.8f);
            return;
        }
        if (_flashlight != null && _flashlight.Charge >= 0.99f)
        {
            if (_hud != null) _hud.ShowSubtitle("The torch is full.", 1.8f);
            return;
        }

        PlayerInventory.TryConsume(ShopItem.SpareBattery);
        if (_flashlight != null) _flashlight.Refill();
        if (_hud != null)
        {
            _hud.PulseSlot(0);
            _hud.ShowSubtitle("Fresh cell.", 1.8f);
        }
    }

    private void UseCompass()
    {
        if (PlayerInventory.Count(ShopItem.StarCompass) == 0)
        {
            if (_hud != null) _hud.ShowSubtitle("No star compass.", 1.8f);
            return;
        }
        if (_retired || (_escape != null && _escape.HatchOpen))
        {
            if (_hud != null) _hud.ShowSubtitle("Nothing left to point at.", 1.8f);
            return;
        }
        if (_compassRoutine != null) return; // already active

        PlayerInventory.TryConsume(ShopItem.StarCompass);
        if (_hud != null) _hud.PulseSlot(1);
        _compassRoutine = StartCoroutine(CompassRoutine());
    }

    private IEnumerator CompassRoutine()
    {
        EnsureChevron();
        if (_chevron == null) yield break;

        _chevron.gameObject.SetActive(true);
        float elapsed = 0f;

        while (elapsed < CompassSeconds)
        {
            if (!GameFlow.IsRunActive || GameOutcome.IsOver) break;

            Transform nearest = NearestStar();
            Camera cam = Camera.main;
            if (nearest == null) break; // no stars left

            if (cam != null && _player != null)
            {
                Vector3 forward = cam.transform.forward;
                forward.y = 0f;
                Vector3 toStar = nearest.position - _player.position;
                toStar.y = 0f;

                if (forward.sqrMagnitude > 0.0001f && toStar.sqrMagnitude > 0.0001f)
                {
                    float bearing = Vector3.SignedAngle(forward, toStar, Vector3.up);
                    float rad = bearing * Mathf.Deg2Rad;
                    _chevron.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * ChevronRadius;
                    _chevron.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -bearing);
                }
            }

            float pulse = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(elapsed * Mathf.PI));
            float fade = elapsed >= CompassSeconds - CompassFadeOut ? Mathf.Clamp01((CompassSeconds - elapsed) / CompassFadeOut) : 1f;
            Color c = CompassColor;
            c.a = pulse * fade;
            _chevron.color = c;

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (_chevron != null) _chevron.gameObject.SetActive(false);
        _compassRoutine = null;
    }

    private Transform NearestStar()
    {
        if (_maze == null || _player == null) return null;

        Transform best = null;
        float bestDistance = float.MaxValue;
        foreach (Transform star in _maze.Stars)
        {
            if (star == null) continue;

            Vector3 flat = star.position - _player.position;
            flat.y = 0f;
            float distance = flat.sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = star;
            }
        }
        return best;
    }

    private void EnsureChevron()
    {
        if (_chevron != null) return;

        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        // Same centre-anchored 50x50 rect as MazeEscape.BuildTimerText's chevron.
        _chevron = RuntimeUi.CreateText(canvas.transform, "StarCompass", "▲", 40f, CompassColor);
        RectTransform rect = _chevron.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(50f, 50f);
        _chevron.gameObject.SetActive(false);
    }

    private void StopCompass()
    {
        if (_compassRoutine != null)
        {
            StopCoroutine(_compassRoutine);
            _compassRoutine = null;
        }
        if (_chevron != null) _chevron.gameObject.SetActive(false);
    }
}

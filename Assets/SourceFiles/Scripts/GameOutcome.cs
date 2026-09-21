using System.Collections;
using StarterAssets;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

public enum LoseReason
{
    Caught,
    OutOfTime
}

/// <summary>
/// Owns how a run ends: the capture sequence, silencing the world, and the end screen with its retry
/// buttons. Added at runtime by MazeGenerator. Single-shot - the first ending wins, and a hatch drop
/// or a timeout that lands mid-capture is ignored.
/// </summary>
public class GameOutcome : MonoBehaviour
{
    private static readonly Color Green = new Color(0.25f, 1f, 0.7f);
    private static readonly Color Red = new Color(0.9f, 0.15f, 0.1f);
    private static readonly Color Grey = new Color(0.75f, 0.75f, 0.78f);
    private static readonly Color ButtonIdle = new Color(0.10f, 0.10f, 0.12f, 0.92f);
    private static readonly Color ButtonHover = new Color(0.48f, 0.08f, 0.06f, 1f);

    // Seconds into the capture sequence at which the torch flips. Ends off.
    private static readonly float[] FlickerTimes = { 0.5f, 0.56f, 0.62f, 0.68f, 0.74f, 0.80f, 0.9f };

    /// <summary>True once the run has ended. Static, so reset when the scene is torn down.</summary>
    public static bool IsOver { get; private set; }

    /// <summary>True while the capture sequence is playing.</summary>
    public bool IsEnding { get; private set; }

    private Transform _player;
    private AIFollower _follower;
    private HorrorAudioDirector _audio;
    private MazeEscape _escape;
    private Flashlight _flashlight;
    private int _seed;

    private int _collected;
    private int _total;
    private float _runTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsOver = false;
    }

    /// <summary>Follower, escape and flashlight may each be null; every use is guarded.</summary>
    public void Configure(Transform player, AIFollower follower, HorrorAudioDirector audio,
        MazeEscape escape, Flashlight flashlight, int seed)
    {
        _player = player;
        _audio = audio;
        _escape = escape;
        _flashlight = flashlight;
        _seed = seed;

        if (_follower != null) _follower.PlayerCaught -= HandleCaught;
        _follower = follower;
        if (_follower != null) _follower.PlayerCaught += HandleCaught;
    }

    private void Awake()
    {
        IsOver = false;
    }

    private void OnEnable()
    {
        GameManager.ProgressChanged += HandleProgress;
        if (_follower != null) _follower.PlayerCaught += HandleCaught;
    }

    private void OnDisable()
    {
        GameManager.ProgressChanged -= HandleProgress;
        if (_follower != null) _follower.PlayerCaught -= HandleCaught;
    }

    private void OnDestroy()
    {
        IsOver = false;
    }

    private void Update()
    {
        // Held every frame, for the same reason as the title screen: focus changes re-lock the cursor
        if (IsOver) PlayerLock.SetCursorFree(true);
    }

    private void HandleProgress(int collected, int total)
    {
        _collected = collected;
        _total = total;
    }

    private void HandleCaught()
    {
        Lose(LoseReason.Caught);
    }

    // ---------------------------------------------------------------- endings

    public void Win()
    {
        if (IsOver || IsEnding) return;

        IsOver = true;
        GameFlow.IsRunActive = false;
        _runTime = Time.time - GameFlow.RunStartTime;

        EndTheHunt();
        if (_escape != null) _escape.Stop();
        if (_flashlight != null) _flashlight.InputEnabled = false;
        PlayerLock.Freeze(_player, true);

        ShowEndScreen(true, LoseReason.Caught, false);
    }

    public void Lose(LoseReason reason)
    {
        if (IsOver || IsEnding) return;

        if (reason == LoseReason.Caught)
        {
            StartCoroutine(CaptureSequence());
            return;
        }

        IsOver = true;
        GameFlow.IsRunActive = false;
        _runTime = Time.time - GameFlow.RunStartTime;

        EndTheHunt();
        if (_escape != null) _escape.Stop();
        if (_flashlight != null) _flashlight.InputEnabled = false;
        PlayerLock.Freeze(_player, true);

        ShowEndScreen(false, reason, _flashlight != null && _flashlight.IsOn);
    }

    private IEnumerator CaptureSequence()
    {
        IsEnding = true;
        GameFlow.IsRunActive = false;
        _runTime = Time.time - GameFlow.RunStartTime;
        bool torchWasOn = _flashlight != null && _flashlight.IsOn;

        // The countdown freezes where it is, so it cannot produce a second ending mid-sequence
        if (_escape != null) _escape.Stop();
        PlayerLock.Freeze(_player, true);
        if (_flashlight != null) _flashlight.InputEnabled = false;
        if (_audio != null) _audio.PlayCaptureSting();

        ThirdPersonController controller = _player != null ? _player.GetComponent<ThirdPersonController>() : null;
        Image blackout = null;
        int flicker = 0;
        float elapsed = 0f;

        while (elapsed < 1.4f)
        {
            elapsed += Time.deltaTime;

            // Ease onto the hunter's eyes; lands in about half a second
            if (controller != null && _follower != null)
            {
                Vector3 eyes = _follower.transform.position + Vector3.up * _follower.EyeHeight;
                controller.LookAt(eyes, 1f - Mathf.Pow(0.001f, Time.deltaTime));
            }

            while (flicker < FlickerTimes.Length && elapsed >= FlickerTimes[flicker])
            {
                // Even entries switch it off, odd ones back on; the last (index 6) leaves it off
                if (_flashlight != null) _flashlight.SetOn(flicker % 2 == 1);
                flicker++;
            }

            if (elapsed >= 0.9f)
            {
                if (blackout == null)
                {
                    // Created now rather than earlier so it is the topmost sibling
                    Canvas canvas = RuntimeUi.ResolveCanvas();
                    if (canvas != null)
                    {
                        blackout = RuntimeUi.CreatePanel(canvas.transform, "Blackout", new Color(0f, 0f, 0f, 0f)).GetComponent<Image>();
                    }
                }

                if (blackout != null)
                {
                    blackout.color = new Color(0f, 0f, 0f, Mathf.Clamp01((elapsed - 0.9f) / 0.5f));
                }
            }

            yield return null;
        }

        if (blackout != null) blackout.color = Color.black;

        EndTheHunt();
        IsOver = true;
        IsEnding = false;

        yield return new WaitForSeconds(0.2f);

        ShowEndScreen(false, LoseReason.Caught, torchWasOn);
    }

    /// <summary>
    /// Once the run is over the hunter stops mattering, so silence it. Without this the heartbeat keeps
    /// pounding over the end screen and the hum keeps looping.
    /// </summary>
    private void EndTheHunt()
    {
        foreach (HorrorAudioDirector director in FindObjectsByType<HorrorAudioDirector>(FindObjectsInactive.Exclude))
        {
            // Silence first: disabling the component stops its Update but leaves its AudioSources running
            director.Silence();
            // Disabling then runs its OnDisable, which unsubscribes it from the chase event
            director.enabled = false;
        }

        foreach (AIPresence presence in FindObjectsByType<AIPresence>(FindObjectsInactive.Exclude))
        {
            presence.Silence();
        }

        foreach (AIFollower follower in FindObjectsByType<AIFollower>(FindObjectsInactive.Exclude))
        {
            follower.enabled = false;

            NavMeshAgent agent = follower.GetComponent<NavMeshAgent>();
            if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
        }
    }

    // ---------------------------------------------------------------- end screen

    private void ShowEndScreen(bool won, LoseReason reason, bool torchWasOn)
    {
        RuntimeUi.EnsureEventSystem();

        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        Transform root = RuntimeUi.CreatePanel(canvas.transform, "EndScreen", new Color(0f, 0f, 0f, 0.85f)).transform;

        string title;
        string subtitle;
        Color titleColor = won ? Green : Red;

        if (won)
        {
            bool finalFloor = GameFlow.CurrentFloor >= FloorProfile.FinalFloor;
            title = finalFloor ? "YOU GOT OUT" : $"FLOOR {GameFlow.CurrentFloor} CLEARED";
            subtitle = finalFloor ? "The hatch closed behind you. There is nothing left below." : "The hatch closed behind you. It is darker down here.";
        }
        else if (reason == LoseReason.Caught)
        {
            title = "IT FOUND YOU";

            PlayerStealthState stealth = _player != null ? _player.GetComponent<PlayerStealthState>() : null;
            if (stealth != null && stealth.LastExposed)
            {
                subtitle = "It watched you hide.";
            }
            else
            {
                subtitle = torchWasOn ? "It was faster in the dark." : "You never heard it coming.";
            }
        }
        else
        {
            title = "OUT OF TIME";
            int metres = 0;
            if (_escape != null && _player != null)
            {
                Vector3 toHatch = _escape.HatchPosition - _player.position;
                toHatch.y = 0f;
                metres = Mathf.RoundToInt(toHatch.magnitude);
            }

            subtitle = $"The hatch sealed. You were {_collected} stars in and {metres} metres away.";
        }

        TextMeshProUGUI titleText = RuntimeUi.CreateText(root, "Title", title, 96f, titleColor);
        titleText.fontStyle = FontStyles.Bold;
        RuntimeUi.Place(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(1600f, 140f));

        TextMeshProUGUI subText = RuntimeUi.CreateText(root, "Subtitle", subtitle, 36f, Grey);
        RuntimeUi.Place(subText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(1600f, 60f));

        string stats = $"FLOOR {GameFlow.CurrentFloor}     STARS {_collected} / {_total}     TIME {MazeEscape.FormatTime(_runTime)}     SEED {_seed}";
        TextMeshProUGUI statsText = RuntimeUi.CreateText(root, "Stats", stats, 32f, Grey);
        statsText.characterSpacing = 4f;
        RuntimeUi.Place(statsText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -380f), new Vector2(1600f, 50f));

        // Wider than the title screen's buttons: "TRY THIS MAZE AGAIN" does not fit in 420 at 40 pt
        Vector2 size = new Vector2(560f, 86f);

        // Winning below the final floor leads somewhere: the next, harder floor
        if (won && GameFlow.CurrentFloor < FloorProfile.FinalFloor)
        {
            Button next = RuntimeUi.CreateButton(root, "NEXT FLOOR",
                new Vector2(0f, 440f), size, 42f, new Color(0.08f, 0.32f, 0.24f, 0.95f), new Color(0.15f, 0.55f, 0.42f, 1f), Color.white);
            next.onClick.AddListener(GameFlow.NextFloor);
        }

        Button again = RuntimeUi.CreateButton(root, won ? "REPLAY THIS FLOOR" : "TRY THIS MAZE AGAIN",
            new Vector2(0f, 330f), size, 38f, ButtonIdle, ButtonHover, Color.white);
        again.onClick.AddListener(() => GameFlow.Restart(true, _seed));

        Button fresh = RuntimeUi.CreateButton(root, "NEW MAZE",
            new Vector2(0f, 220f), size, 38f, ButtonIdle, ButtonHover, Color.white);
        fresh.onClick.AddListener(() => GameFlow.Restart(false, _seed));

        Button menu = RuntimeUi.CreateButton(root, "MAIN MENU",
            new Vector2(0f, 110f), size, 38f, ButtonIdle, ButtonHover, Color.white);
        menu.onClick.AddListener(GameFlow.ReturnToMenu);

        PlayerLock.SetCursorFree(true);
    }
}

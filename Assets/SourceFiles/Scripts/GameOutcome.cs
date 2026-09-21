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

    [Tooltip("How long the hunter stands stunned after Second Wind lets go (F36).")]
    [SerializeField] private float secondWindStunSeconds = 6f;

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

    // F32/F37: the room between floors. May all stay null (escapeSequence off, or BindShop never called).
    private ShopRoom _shop;
    private MazeGenerator _maze;
    private PlayerHud _hud;
    private PlayerStealthState _stealth;
    private bool _showingLedger;
    private bool _continuePressed;

    // Kept only so ShowEndScreen can report the number after the fact.
    private Payout _floorPayout;
    private Payout _consolationPayout;

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

    /// <summary>Wired after escape and outcome.Configure, once the shop and stealth references exist.</summary>
    public void BindShop(ShopRoom shop, MazeGenerator maze, PlayerHud hud, PlayerStealthState stealth)
    {
        _shop = shop;
        _maze = maze;
        _hud = hud;
        _stealth = stealth;
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
        if (IsOver || _showingLedger) PlayerLock.SetCursorFree(true);
    }

    private void HandleProgress(int collected, int total)
    {
        _collected = collected;
        _total = total;
    }

    private void HandleCaught()
    {
        if (IsOver || IsEnding) return;

        // F36 Second Wind: a release, not an invulnerability. Consumed automatically, once.
        if (PlayerInventory.Count(ShopItem.SecondWind) > 0 && _follower != null && _maze != null)
        {
            PlayerInventory.TryConsume(ShopItem.SecondWind);
            StartCoroutine(SecondWindSequence());
            return;
        }

        Lose(LoseReason.Caught);
    }

    private IEnumerator SecondWindSequence()
    {
        IsEnding = true; // pause, dread, phantoms, and Lose() all wait
        if (_escape != null) _escape.Frozen = true; // not Stop(): Stop is permanent
        bool torchWasOn = _flashlight != null && _flashlight.IsOn;

        PlayerLock.Freeze(_player, true);
        if (_flashlight != null) _flashlight.InputEnabled = false;
        if (_audio != null) _audio.PlayCaptureSting();

        Canvas canvas = RuntimeUi.ResolveCanvas();
        Image blackout = canvas != null
            ? RuntimeUi.CreatePanel(canvas.transform, "Blackout", new Color(0f, 0f, 0f, 0f)).GetComponent<Image>()
            : null;
        if (blackout != null) blackout.transform.SetAsLastSibling();

        // A hard cut, not the slow capture fade.
        if (blackout != null) yield return Fade(blackout, 0f, 1f, 0.25f);
        else yield return new WaitForSeconds(0.25f);

        yield return new WaitForSeconds(0.7f);

        // Let go: warp the hunter to the far side of the maze and stun it.
        Vector3 far = _maze.FarthestCellCenterFrom(_player.position);
        _follower.Release(far, secondWindStunSeconds);

        // If it busted a locker, the player is still standing inside it with movement locked.
        // Locker.Leave() clears MovementLocked directly, so re-assert the freeze straight after -
        // the sequence is not done with the player yet.
        if (_maze != null)
        {
            foreach (Locker locker in _maze.Lockers)
            {
                if (locker != null && locker.Occupied) locker.ForceLeave();
            }
            PlayerLock.Freeze(_player, true);
        }

        if (_stealth != null) _stealth.LastExposed = false;
        if (_audio != null) _audio.ResumeBed();
        if (_flashlight != null)
        {
            _flashlight.SetOn(torchWasOn);
            _flashlight.Stutter(1.5f);
        }

        if (blackout != null)
        {
            yield return Fade(blackout, 1f, 0f, 0.6f);
            Destroy(blackout.gameObject);
        }
        else
        {
            yield return new WaitForSeconds(0.6f);
        }

        if (_escape != null) _escape.Frozen = false;
        PlayerLock.Freeze(_player, false);
        if (_flashlight != null) _flashlight.InputEnabled = true;
        IsEnding = false;

        if (_hud != null) _hud.ShowSubtitle("It let go. It will not do that twice.", 3.2f);
    }

    // ---------------------------------------------------------------- endings

    /// <summary>The hatch was reached. Below the final floor this is not an ending: black, teleport, ledger, shop.</summary>
    public void FloorCleared()
    {
        if (IsOver || IsEnding) return;

        if (GameFlow.CurrentFloor >= FloorProfile.FinalFloor || _shop == null)
        {
            Win();
            return;
        }

        StartCoroutine(FloorClearSequence());
    }

    private IEnumerator FloorClearSequence()
    {
        IsEnding = true; // blocks pause, dread, phantoms, and a second ending
        GameFlow.IsRunActive = false;
        _runTime = Time.time - GameFlow.RunStartTime;
        float secondsLeft = _escape != null ? _escape.TimeLeft : 0f;

        EndTheHunt();
        if (_flashlight != null) { _flashlight.SetOn(false); _flashlight.InputEnabled = false; }
        PlayerLock.Freeze(_player, true);

        Canvas canvas = RuntimeUi.ResolveCanvas();
        Image blackout = null;
        if (canvas != null)
        {
            blackout = RuntimeUi.CreatePanel(canvas.transform, "Blackout", new Color(0f, 0f, 0f, 0f)).GetComponent<Image>();
            blackout.transform.SetAsLastSibling();
        }

        if (blackout != null) yield return Fade(blackout, 0f, 1f, 0.6f);
        else yield return new WaitForSeconds(0.6f); // the player is falling onto the platform meanwhile

        _shop.ArrivePlayer(_player);

        Payout payout = PlayerWallet.ComputeFloorClear(GameFlow.CurrentFloor, _collected, secondsLeft);
        PlayerWallet.Deposit(payout);

        Transform ledger = blackout != null ? BuildLedger(blackout.transform, payout) : null;
        _showingLedger = true; // Update frees the cursor while this is true
        PlayerLock.SetCursorFree(true);

        _continuePressed = false;
        yield return new WaitUntil(() => _continuePressed);
        _showingLedger = false;

        GameFlow.IsInShop = true;
        IsEnding = false;
        _shop.Open();
        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);

        if (blackout != null)
        {
            yield return Fade(blackout, 1f, 0f, 0.5f, ledger);
            Destroy(blackout.gameObject);
        }
    }

    /// <summary>The floor-clear ledger, built onto the blackout panel. CONTINUE sets _continuePressed.</summary>
    private Transform BuildLedger(Transform parent, Payout payout)
    {
        // No prior pause/menu may have built one yet (mainMenu = false skips the title screen's call).
        RuntimeUi.EnsureEventSystem();

        GameObject ledger = RuntimeUi.CreatePanel(parent, "Ledger", Color.clear);
        Transform root = ledger.transform;

        TextMeshProUGUI title = RuntimeUi.CreateText(root, "Title", payout.Title, 84f, Green);
        title.fontStyle = FontStyles.Bold;
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(1600f, 120f));

        TextMeshProUGUI subtitle = RuntimeUi.CreateText(root, "Subtitle",
            "The hatch closed behind you. Something down here is open.", 34f, Grey);
        RuntimeUi.Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -280f), new Vector2(1400f, 50f));

        float y = -370f;
        foreach (PayoutLine line in payout.Lines)
        {
            bool informational = line.Amount == 0 && line.Label.StartsWith("SHARDS FOUND");
            Color lineColor = informational ? new Color(Grey.r, Grey.g, Grey.b, 0.6f) : Grey;

            TextMeshProUGUI label = RuntimeUi.CreateText(root, "Label", line.Label, 36f, lineColor);
            label.alignment = TextAlignmentOptions.Left;
            RuntimeUi.Place(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(-100f, y), new Vector2(900f, 50f));

            string amountText = informational ? "already yours" : $"+{line.Amount}";
            TextMeshProUGUI amount = RuntimeUi.CreateText(root, "Amount", amountText, 36f, lineColor);
            amount.alignment = TextAlignmentOptions.Right;
            RuntimeUi.Place(amount.rectTransform, new Vector2(0.5f, 1f), new Vector2(420f, y), new Vector2(300f, 50f));

            y -= 58f;
        }

        GameObject rule = RuntimeUi.CreatePanel(root, "Rule", new Color(Grey.r, Grey.g, Grey.b, 0.4f));
        RuntimeUi.Place(rule.GetComponent<Image>().rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y - 6f), new Vector2(900f, 2f));
        y -= 50f;

        TextMeshProUGUI payoutText = RuntimeUi.CreateText(root, "Payout", $"PAYOUT  +{payout.Total}", 44f, Green);
        RuntimeUi.Place(payoutText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(900f, 60f));
        y -= 60f;

        Color shardColor = new Color(0.7f, 0.9f, 1f);
        TextMeshProUGUI wallet = RuntimeUi.CreateText(root, "Wallet", $"{PlayerWallet.CurrencyName}  {PlayerWallet.Shards}", 44f, shardColor);
        RuntimeUi.Place(wallet.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(900f, 60f));

        Button continueButton = RuntimeUi.CreateButton(root, "CONTINUE", new Vector2(0f, 160f), new Vector2(560f, 86f), 42f,
            new Color(0.08f, 0.32f, 0.24f, 0.95f), new Color(0.15f, 0.55f, 0.42f, 1f), Color.white);
        continueButton.onClick.AddListener(() => _continuePressed = true);

        return root;
    }

    /// <summary>Fades a full-screen Image between two alpha values. If alsoFade is given, its CanvasGroup fades out alongside it.</summary>
    private static IEnumerator Fade(Image image, float from, float to, float seconds, Transform alsoFade = null)
    {
        CanvasGroup group = null;
        if (alsoFade != null)
        {
            group = alsoFade.GetComponent<CanvasGroup>();
            if (group == null) group = alsoFade.gameObject.AddComponent<CanvasGroup>();
        }

        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / seconds);
            if (image != null) image.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, k));
            if (group != null) group.alpha = 1f - k;
            yield return null;
        }

        if (image != null) image.color = new Color(0f, 0f, 0f, to);
        if (group != null) group.alpha = 0f;
    }

    public void Win()
    {
        if (IsOver || IsEnding) return;

        IsOver = true;
        GameFlow.IsRunActive = false;
        _runTime = Time.time - GameFlow.RunStartTime;

        float secondsLeft = _escape != null ? _escape.TimeLeft : 0f;
        _floorPayout = PlayerWallet.ComputeFloorClear(GameFlow.CurrentFloor, _collected, secondsLeft);
        PlayerWallet.Deposit(_floorPayout);

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

        _consolationPayout = PlayerWallet.ComputeConsolation(GameFlow.CurrentFloor, _collected);
        PlayerWallet.Deposit(_consolationPayout);

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

        _consolationPayout = PlayerWallet.ComputeConsolation(GameFlow.CurrentFloor, _collected);
        PlayerWallet.Deposit(_consolationPayout);

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

        // Second line: what this attempt paid into the campaign wallet.
        bool isFinalWin = won && GameFlow.CurrentFloor >= FloorProfile.FinalFloor;
        string stats2 = null;
        Color shardColor = new Color(0.7f, 0.9f, 1f);
        if (!won)
        {
            int amount = _consolationPayout != null ? _consolationPayout.Total : 0;
            stats2 = amount > 0
                ? $"CONSOLATION  +{amount}     {PlayerWallet.CurrencyName}  {PlayerWallet.Shards}"
                : $"NO CONSOLATION     {PlayerWallet.CurrencyName}  {PlayerWallet.Shards}";
        }
        else if (isFinalWin)
        {
            int amount = _floorPayout != null ? _floorPayout.Total : 0;
            stats2 = $"PAYOUT  +{amount}     CAMPAIGN TOTAL  {PlayerWallet.TotalEarned}";
        }

        if (stats2 != null)
        {
            TextMeshProUGUI stats2Text = RuntimeUi.CreateText(root, "Stats2", stats2, 32f, shardColor);
            stats2Text.characterSpacing = 4f;
            RuntimeUi.Place(stats2Text.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -430f), new Vector2(1600f, 50f));
        }

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

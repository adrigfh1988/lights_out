using System.Collections;
using System.Collections.Generic;
using StarterAssets;
using TMPro;
using UnityEngine;

/// <summary>
/// F87 Contracts: checks the contract the player took in the shop for this floor. An instance component on the player,
/// added by MazeGenerator.SetUpAtmosphere like KeyRing - a scene reload recreates it, which is exactly "per floor".
/// The taken contract itself is campaign state (ContractState); this only watches the floor.
///
/// It listens to existing systems (Locker via PlayerStealthState.Hidden, AIFollower.PlayerCaught, Stalker.PlayerTouched,
/// the torch and the controller, PlayerWallet's shard count, LoreNote.Read, ThrowableItem.HunterInvestigated, the run
/// clock) and only counts while the run is active, so the Intake locker or the shop never register. A failed contract
/// is final; one that is certain completes at once; the rest are decided when the floor clears (<see cref="AppendTo"/>).
/// Never gates the hatch and never touches GameManager.
/// </summary>
public class ContractTracker : MonoBehaviour
{
    public enum State { None, InProgress, Complete, Failed, Void }

    private static readonly Color Grey = new Color(0.75f, 0.75f, 0.78f);
    private static readonly Color Green = new Color(0.25f, 1f, 0.7f);
    private static readonly Color Red = new Color(0.9f, 0.15f, 0.1f);
    private static readonly Color Amber = new Color(1f, 0.72f, 0.42f);

    private int _id = -1;
    private State _state = State.None;
    private PlayerHud _hud;
    private PlayerStealthState _stealth;
    private Flashlight _flashlight;
    private ThirdPersonController _controller;
    private CharacterController _characterController;
    private AIFollower _hunter;

    private float _darkSeconds;
    private int _shardsPlaced;
    private int _shardTarget;
    private int _notesPlaced;
    private readonly HashSet<int> _notesRead = new HashSet<int>();
    private int _distractions;
    private bool _subscribed;

    private TextMeshProUGUI _line;
    private MainMenu _menu;

    /// <summary>The contract being tracked this floor, or -1.</summary>
    public int ContractId => _id;
    public State Status => _state;
    public bool Active => _id >= 0;

    /// <summary>Called once by MazeGenerator.SetUpAtmosphere, after the maze (its shards and notes) is built.</summary>
    public void Configure(Transform player, PlayerStealthState stealth, Flashlight flashlight, AIFollower hunter, PlayerHud hud, MainMenu menu)
    {
        _stealth = stealth;
        _flashlight = flashlight;
        _hud = hud;
        _menu = menu;
        _controller = player != null ? player.GetComponent<ThirdPersonController>() : null;
        _characterController = player != null ? player.GetComponent<CharacterController>() : null;

        _id = -1;
        _state = State.None;
        if (!ContractState.HasActive || ContractState.ActiveFloor != GameFlow.CurrentFloor) return;

        _id = ContractState.ActiveId;
        _state = State.InProgress;

        // Count what this maze actually has. A contract that cannot be done here is voided, not failed.
        string voidLine = null;
        if (_id == ContractState.Scavenger)
        {
            _shardsPlaced = FindObjectsByType<ShardPickup>(FindObjectsInactive.Exclude).Length;
            _shardTarget = Mathf.Max(1, Mathf.CeilToInt(_shardsPlaced * ContractState.ScavengerFraction));
            if (_shardsPlaced == 0) voidLine = "Nothing glitters down here - the contract's off.";
        }
        else if (_id == ContractState.Reader)
        {
            foreach (LoreNote note in FindObjectsByType<LoreNote>(FindObjectsInactive.Exclude))
            {
                if (note.GetComponentInParent<IntakeRoom>() == null) _notesPlaced++;
            }
            if (_notesPlaced == 0) voidLine = "No notes down here - the contract's off.";
        }

        if (voidLine != null)
        {
            _state = State.Void;
            StartCoroutine(SubtitleOnRunStart(voidLine));
        }

        if (hunter != null)
        {
            _hunter = hunter;
            _hunter.PlayerCaught += HandleCaught;
        }
        LoreNote.Read += HandleNoteRead;
        Stalker.PlayerTouched += HandleStalkerTouch;
        ThrowableItem.HunterInvestigated += HandleDistraction;
        _subscribed = true;

        BuildLine();
        RefreshLine();
    }

    private void OnDestroy()
    {
        if (_hunter != null) _hunter.PlayerCaught -= HandleCaught;
        if (!_subscribed) return;
        LoreNote.Read -= HandleNoteRead;
        Stalker.PlayerTouched -= HandleStalkerTouch;
        ThrowableItem.HunterInvestigated -= HandleDistraction;
    }

    // ---------------------------------------------------------------- events

    private bool Counting => _state == State.InProgress && GameFlow.IsRunActive && !GameOutcome.IsOver;

    private void HandleCaught()
    {
        // Second Wind also raises PlayerCaught: being caught at all breaks "not a scratch".
        if (Counting && _id == ContractState.Untouched) Fail();
    }

    private void HandleStalkerTouch()
    {
        if (Counting && _id == ContractState.Untouched) Fail();
    }

    private void HandleNoteRead(int noteId)
    {
        if (!Counting || _id != ContractState.Reader || noteId < 0) return;
        _notesRead.Add(noteId);
        if (_notesRead.Count >= _notesPlaced) Complete();
        else RefreshLine();
    }

    private void HandleDistraction()
    {
        if (!Counting || _id != ContractState.Distract) return;
        _distractions++;
        if (_distractions >= ContractState.DistractCount) Complete();
        else RefreshLine();
    }

    // ---------------------------------------------------------------- per-frame checks

    private void Update()
    {
        if (_line != null) _line.gameObject.SetActive(_id >= 0 && !GameFlow.IsInSafeRoom && (_menu == null || !_menu.IsOpen));

        if (!Counting || Time.timeScale <= 0f) return;

        switch (_id)
        {
            case ContractState.NoHide:
                if (_stealth != null && _stealth.Hidden) Fail();
                break;

            case ContractState.Silent:
                if (_controller != null && _controller.IsSprinting) Fail();
                break;

            case ContractState.DarkWalk:
            {
                bool torchOff = _flashlight == null || !_flashlight.IsOn;
                bool moving = _characterController != null && _characterController.velocity.sqrMagnitude > 0.25f;
                if (torchOff && moving && (_stealth == null || !_stealth.Hidden))
                {
                    _darkSeconds += Time.deltaTime;
                    if (_darkSeconds >= ContractState.DarkWalkSeconds) Complete();
                    else RefreshLineThrottled();
                }
                break;
            }

            case ContractState.Scavenger:
                if (PlayerWallet.ShardsFoundThisAttempt >= _shardTarget) Complete();
                else RefreshLineThrottled();
                break;

            case ContractState.Quick:
                if (Elapsed > ContractState.QuickSeconds(GameFlow.CurrentFloor)) Fail();
                else RefreshLineThrottled();
                break;
        }
    }

    private static float Elapsed => Time.time - GameFlow.RunStartTime;

    private float _nextRefresh;

    private void RefreshLineThrottled()
    {
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 0.5f;
        RefreshLine();
    }

    // ---------------------------------------------------------------- results

    private void Fail()
    {
        if (_state != State.InProgress) return;
        _state = State.Failed;
        _hud?.ShowSubtitle($"CONTRACT FAILED  -  {ContractState.Def(_id).Name}", 3f);
        RefreshLine();
    }

    private void Complete()
    {
        if (_state != State.InProgress) return;
        _state = State.Complete;
        _hud?.ShowSubtitle($"CONTRACT DONE  -  {ContractState.Def(_id).Name}", 3f);
        RefreshLine();
    }

    /// <summary>
    /// The floor has been cleared: decides whatever is still open, adds the CONTRACT line to the payout (the reward
    /// rides in the payout's Total, so Deposit pays it with everything else) and consumes the contract. A no-op with
    /// no contract. Called by GameOutcome right after PlayerWallet.ComputeFloorClear, for the ledger and for floor 5's win screen.
    /// </summary>
    public void AppendTo(Payout payout)
    {
        if (payout == null || _id < 0 || !ContractState.HasActive) return;

        if (_state == State.InProgress)
        {
            switch (_id)
            {
                case ContractState.NoHide:
                case ContractState.Untouched:
                case ContractState.Silent:
                    _state = State.Complete; // nothing went wrong all floor
                    break;
                case ContractState.Quick:
                    _state = Elapsed <= ContractState.QuickSeconds(GameFlow.CurrentFloor) ? State.Complete : State.Failed;
                    break;
                default:
                    _state = State.Failed; // DarkWalk / Scavenger / Reader / Distract would have completed on the spot
                    break;
            }
        }

        string name = ContractState.Def(_id).Name.TrimEnd('.');
        int reward = _state == State.Complete ? ContractState.Reward(_id, ContractState.ActiveFloor) : 0;
        string status = _state == State.Complete ? "DONE" : _state == State.Void ? "VOID" : "FAILED";

        payout.Lines.Add(new PayoutLine { Label = $"CONTRACT  {name}  {status}", Amount = reward });
        payout.Total += reward;

        ContractState.SalesmanLine = _state == State.Complete ? "A deal's a deal." : _state == State.Failed ? "Can't win them all." : null;
        ContractState.ClearActive();
        RefreshLine();
    }

    // ---------------------------------------------------------------- hud

    private void BuildLine()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        _line = RuntimeUi.CreateText(canvas.transform, "ContractLine", "", 26f, Amber);
        _line.alignment = TextAlignmentOptions.TopLeft;
        _line.textWrappingMode = TextWrappingModes.NoWrap;
        RectTransform rect = _line.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(36f, -150f);
        rect.sizeDelta = new Vector2(1100f, 40f);
    }

    private void RefreshLine()
    {
        if (_line == null || _id < 0) return;

        string name = ContractState.Def(_id).Name.TrimEnd('.');
        string text;
        Color color;
        switch (_state)
        {
            case State.Complete: text = $"CONTRACT  -  {name}  -  DONE"; color = Green; break;
            case State.Failed: text = $"CONTRACT  -  {name}  -  FAILED"; color = Red; break;
            case State.Void: text = $"CONTRACT  -  {name}  -  VOID"; color = Grey; break;
            default: text = $"CONTRACT  -  {name}{Progress()}"; color = Amber; break;
        }
        _line.text = text;
        _line.color = color;
    }

    private string Progress()
    {
        switch (_id)
        {
            case ContractState.DarkWalk: return $"  -  {Mathf.FloorToInt(_darkSeconds)} / {Mathf.RoundToInt(ContractState.DarkWalkSeconds)} s";
            case ContractState.Scavenger: return $"  -  {Mathf.Min(PlayerWallet.ShardsFoundThisAttempt, _shardTarget)} / {_shardTarget}";
            case ContractState.Reader: return $"  -  {_notesRead.Count} / {_notesPlaced}";
            case ContractState.Distract: return $"  -  {_distractions} / {ContractState.DistractCount}";
            case ContractState.Quick:
            {
                float left = Mathf.Max(0f, ContractState.QuickSeconds(GameFlow.CurrentFloor) - Elapsed);
                return $"  -  {MazeEscape.FormatTime(left)} left";
            }
            default: return "";
        }
    }

    private IEnumerator SubtitleOnRunStart(string line)
    {
        while (!GameFlow.IsRunActive) yield return null;
        yield return new WaitForSeconds(6f); // after the floor card
        if (_hud != null) _hud.ShowSubtitle(line, 4f);
    }
}

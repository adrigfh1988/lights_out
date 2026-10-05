using System;
using UnityEngine;

/// <summary>
/// Counts stars and reports progress. Everything that reacts to the count (the HUD, the hunter, the
/// hatch, the dread beats) subscribes to the two static events below.
///
/// F73: a Blackout floor's fuse boxes gate the hatch too, so the "everything collected" condition is
/// stars AND ObjectiveTokens (fuses), not stars alone. ProgressChanged still reports stars only - that
/// signal drives the escalation curve (TensionDirector, DreadDirector) exactly as before - while the new
/// StarsCollected/StarsTotal/TokensDone/TokensTotal statics let anything (the counter text) read both
/// counts without this component handing out a second event.
/// </summary>
public class GameManager : MonoBehaviour
{
    /// <summary>
    /// Raised when the last star AND the last ObjectiveToken (if any) are in. MazeEscape listens and
    /// opens the hatch; if nothing is listening (escapeSequence off), collecting them all clears the
    /// floor directly.
    /// </summary>
    public static event Action AllStarsCollected;

    /// <summary>Raised with (starsCollected, starsTotal) at startup and after every star pickup - fuses are not part of this signal, see the class doc comment.</summary>
    public static event Action<int, int> ProgressChanged;

    /// <summary>F73: raised when a non-star objective (a fuse) completes, after TokensDone has been updated. The HUD counter redraws on it.</summary>
    public static event Action ObjectivesChanged;

    /// <summary>Stars collected so far this floor.</summary>
    public static int StarsCollected { get; private set; }

    /// <summary>Total stars this floor started with.</summary>
    public static int StarsTotal { get; private set; }

    /// <summary>ObjectiveTokens (fuses) completed so far this floor. 0 on a floor with none.</summary>
    public static int TokensDone { get; private set; }

    /// <summary>Total ObjectiveTokens this floor started with. 0 on a floor with none.</summary>
    public static int TokensTotal { get; private set; }

    private int _remainingCoins;
    private int _totalCoins;
    private int _remainingTokens;
    private int _totalTokens;

    void Start()
    {
        // Count all coins currently present in the scene
        _remainingCoins = FindObjectsByType<Pickup>(FindObjectsInactive.Exclude).Length;
        _totalCoins = _remainingCoins;
        StarsTotal = _totalCoins;
        StarsCollected = 0;

        // F73: fuse boxes (and any future non-star objective) built during MazeGenerator.Awake, which
        // runs well before this Start - see CLAUDE.md's execution-order table.
        _remainingTokens = FindObjectsByType<ObjectiveToken>(FindObjectsInactive.Exclude).Length;
        _totalTokens = _remainingTokens;
        TokensTotal = _totalTokens;
        TokensDone = 0;

        // Subscribe to coin/token collection notifications
        Pickup.OnCoinCollected += HandleCoinCollected;
        ObjectiveToken.Completed += HandleTokenCompleted;

        ProgressChanged?.Invoke(0, _totalCoins);
    }

    void OnDestroy()
    {
        // Unsubscribe to avoid static-event leaks between scene reloads
        Pickup.OnCoinCollected -= HandleCoinCollected;
        ObjectiveToken.Completed -= HandleTokenCompleted;
    }

    private void HandleCoinCollected()
    {
        _remainingCoins--;
        StarsCollected = _totalCoins - _remainingCoins;

        ProgressChanged?.Invoke(StarsCollected, _totalCoins);
        CheckEverythingDone();
    }

    private void HandleTokenCompleted(Vector3 at)
    {
        _remainingTokens--;
        TokensDone = _totalTokens - _remainingTokens;
        // Raised after TokensDone is updated, so the counter reads the new value. Not ProgressChanged:
        // its listeners (DreadDirector, PhantomDirector, GameOutcome) treat each raise as a star.
        ObjectivesChanged?.Invoke();
        CheckEverythingDone();
    }

    private void CheckEverythingDone()
    {
        if (_remainingCoins > 0 || _remainingTokens > 0) return;

        if (AllStarsCollected != null)
        {
            AllStarsCollected.Invoke();
        }
        else
        {
            ClearFloorDirectly();
        }
    }

    /// <summary>No hatch to find: hand straight to the ending code.</summary>
    private static void ClearFloorDirectly()
    {
        GameOutcome outcome = FindAnyObjectByType<GameOutcome>();
        if (outcome != null) outcome.FloorCleared();
    }
}

using System;
using UnityEngine;

/// <summary>
/// Counts stars and reports progress. Everything that reacts to the count (the HUD, the hunter, the
/// hatch, the dread beats) subscribes to the two static events below.
/// </summary>
public class GameManager : MonoBehaviour
{
    /// <summary>
    /// Raised when the last star is collected. MazeEscape listens and opens the hatch; if nothing is
    /// listening (escapeSequence off), collecting them all clears the floor directly.
    /// </summary>
    public static event Action AllStarsCollected;

    /// <summary>Raised with (collected, total) at startup and after every pickup.</summary>
    public static event Action<int, int> ProgressChanged;

    private int _remainingCoins;
    private int _totalCoins;

    void Start()
    {
        // Count all coins currently present in the scene
        _remainingCoins = FindObjectsByType<Pickup>(FindObjectsInactive.Exclude).Length;
        _totalCoins = _remainingCoins;

        // Subscribe to coin collection notifications
        Pickup.OnCoinCollected += HandleCoinCollected;

        ProgressChanged?.Invoke(0, _totalCoins);
    }

    void OnDestroy()
    {
        // Unsubscribe to avoid static-event leaks between scene reloads
        Pickup.OnCoinCollected -= HandleCoinCollected;
    }

    private void HandleCoinCollected()
    {
        _remainingCoins--;

        ProgressChanged?.Invoke(_totalCoins - _remainingCoins, _totalCoins);

        if (_remainingCoins <= 0)
        {
            if (AllStarsCollected != null)
            {
                AllStarsCollected.Invoke();
            }
            else
            {
                ClearFloorDirectly();
            }
        }
    }

    /// <summary>No hatch to find: hand straight to the ending code.</summary>
    private static void ClearFloorDirectly()
    {
        GameOutcome outcome = FindAnyObjectByType<GameOutcome>();
        if (outcome != null) outcome.FloorCleared();
    }
}

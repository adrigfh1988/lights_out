using System;
using StarterAssets;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [SerializeField] private GameObject winPanel;
    [SerializeField] private StarterAssetsInputs playerInputs;

    /// <summary>
    /// Raised when the last star is collected. If anything is listening, collecting them all is no
    /// longer the end of the game - the listener takes over. With no listener this falls back to the
    /// original behaviour of winning immediately.
    /// </summary>
    public static event Action AllStarsCollected;

    /// <summary>Raised with (collected, total) at startup and after every pickup.</summary>
    public static event Action<int, int> ProgressChanged;

    private int _remainingCoins;
    private int _totalCoins;

    void Start()
    {
        // Count all coins currently present in the scene
        _remainingCoins = FindObjectsByType<Pickup>(FindObjectsSortMode.None).Length;
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
                WinGame();
            }
        }
    }

    public void WinGame()
    {
        Debug.Log("You Win!");

        if (winPanel != null)
        {
            winPanel.SetActive(true);
        }

        if (playerInputs != null)
        {
            playerInputs.enabled = false;
        }
    }
}

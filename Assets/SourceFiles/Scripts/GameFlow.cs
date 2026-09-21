using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// State that has to survive a scene reload, and the reload itself. There is nothing else in the
/// project that outlives the scene, so this is deliberately a plain static class.
/// </summary>
public static class GameFlow
{
    /// <summary>Set by Restart; consumed by MainMenu.Start so a retry skips the title screen.</summary>
    public static bool SkipMenuOnLoad;

    /// <summary>Seed the next maze is built from. 0 = none pending; consumed by MazeGenerator.Awake.</summary>
    public static int PendingSeed;

    /// <summary>Time.time when the run started. Time.time stands still at timeScale 0, so this is net of menu and pause.</summary>
    public static float RunStartTime;

    /// <summary>True from the moment the game is started until an ending takes over.</summary>
    public static bool IsRunActive;

    /// <summary>Which floor the next maze is built for, 1 to FloorProfile.FinalFloor. Survives reloads so a retry stays on the same floor; only the main menu resets it.</summary>
    public static int CurrentFloor = 1;

    /// <summary>
    /// True while the player is in the room between floors. Not a run (lockers, capture, dread are off)
    /// and not an ending (cursor stays locked, pause works).
    /// </summary>
    public static bool IsInShop;

    public static void Restart(bool sameMaze, int currentSeed)
    {
        SkipMenuOnLoad = true;
        PendingSeed = sameMaze ? currentSeed : 0;
        Reload();
    }

    /// <summary>Go up one floor with a fresh maze. Callers only offer this below the final floor.</summary>
    public static void NextFloor()
    {
        CurrentFloor = Mathf.Min(CurrentFloor + 1, FloorProfile.FinalFloor);
        PlayerInventory.AdvanceFloor();
        Restart(false, 0);
    }

    public static void ReturnToMenu()
    {
        SkipMenuOnLoad = false;
        CurrentFloor = 1;
        PendingSeed = 0;
        PlayerWallet.ResetCampaign();
        PlayerInventory.ResetCampaign();
        DreadDirector.ResetCampaign();
        Reload();
    }

    public static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void Reload()
    {
        IsRunActive = false;
        IsInShop = false;
        // Restart can be called from the pause menu with both engaged. MainMenu.Awake zeroes the
        // clock again in the new scene anyway.
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // With Enter Play Mode > Reload Domain disabled, statics survive between presses of Play. A stale
    // SkipMenuOnLoad would bypass the title screen on the next run.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        SkipMenuOnLoad = false;
        PendingSeed = 0;
        RunStartTime = 0f;
        IsRunActive = false;
        CurrentFloor = 1;
        IsInShop = false;
    }
}

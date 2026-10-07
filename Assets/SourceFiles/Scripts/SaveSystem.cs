using System;
using UnityEngine;

/// <summary>
/// F83: one campaign checkpoint, flattened to what JsonUtility can serialise (no Dictionary/HashSet - those
/// become parallel int arrays). Each static owner (PlayerWallet, PlayerInventory, LoreArchive, DreadDirector)
/// fills and reads its own slice through a CaptureTo/RestoreFrom pair, so no field has to be made public.
/// ShopItem values are stored as ints: the enum may be appended to but never reordered or trimmed.
/// </summary>
[Serializable]
public class SaveData
{
    /// <summary>SaveSystem.Version when written; a mismatch discards the save.</summary>
    public int version;
    /// <summary>The floor to resume (1..FinalFloor). With inShop, the floor that was just cleared.</summary>
    public int floor;
    /// <summary>Resume inside the shop after clearing <see cref="floor"/> rather than at the start of it.</summary>
    public bool inShop;
    public bool useKitMaze;
    /// <summary>GameFlow.RolledObjectives, the whole array.</summary>
    public int[] rolledObjectives;

    // PlayerWallet
    public int shards, totalEarned;
    public int[] mazeShardsEarnedOnFloor, consolationPaidOnFloor;

    // PlayerInventory (heldItems/heldCounts are parallel)
    public int[] heldItems, heldCounts;
    public int[] pendingModifiers, activeModifiers;
    public int torchColourIndex, hudTintIndex, ownedTorchColours, ownedHudTints;

    // LoreArchive
    public int[] readNoteIds;

    // DreadDirector
    public bool ambushSubtitleShown;
}

/// <summary>
/// F83: save/resume for the campaign, whole floors only - never mid-maze. There are two save points:
/// arriving in the shop after a floor clear (and every purchase there), and walking through the shop door
/// into the next floor (GameFlow.NextFloor). Death never saves; the final Win deletes it (GameOutcome.Win);
/// GameFlow.ReturnToMenu leaves it alone, which is what puts CONTINUE on the title screen. CONTINUE always
/// builds a fresh maze (seed 0), so reloading can't be used to scout a layout.
///
/// Storage sits behind <see cref="ISaveStore"/> so the backend is a one-file swap. Holds no static run state.
/// </summary>
public static class SaveSystem
{
    /// <summary>Bump when SaveData's meaning changes. A save with another version is discarded.</summary>
    public const int Version = 1;

    /// <summary>Namespaced: itch.io serves many games from shared origins.</summary>
    public const string Key = "lightsout.save.v1";

    // ---------------------------------------------------------------- storage

    private interface ISaveStore
    {
        string Read();
        void Write(string json);
        void Delete();
    }

    /// <summary>PlayerPrefs. On the web Unity keeps these in IndexedDB and Save() is what flushes them.</summary>
    private sealed class PlayerPrefsStore : ISaveStore
    {
        public string Read() => PlayerPrefs.HasKey(Key) ? PlayerPrefs.GetString(Key, "") : "";

        public void Write(string json)
        {
            PlayerPrefs.SetString(Key, json);
            PlayerPrefs.Save();
        }

        public void Delete()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }

    private static readonly ISaveStore Store = new PlayerPrefsStore();

    // ---------------------------------------------------------------- queries

    /// <summary>True if a readable, current-version save exists. Deletes an unreadable one (see TryLoad).</summary>
    public static bool HasSave => TryLoad(out _);

    /// <summary>False if there is no save, or it failed to parse / is the wrong version / is out of range (it is then deleted, with one warning).</summary>
    public static bool TryLoad(out SaveData data)
    {
        data = null;

        string json;
        try { json = Store.Read(); }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveSystem: could not read the save ({e.Message}).");
            return false;
        }

        if (string.IsNullOrEmpty(json)) return false;

        SaveData parsed = Parse(json);
        if (parsed == null)
        {
            Debug.LogWarning("SaveSystem: the stored save is unreadable or from another version - discarding it.");
            Delete();
            return false;
        }

        data = parsed;
        return true;
    }

    /// <summary>JsonUtility parse plus the validity rules applied by TryLoad. Null = invalid.</summary>
    private static SaveData Parse(string json)
    {
        SaveData d;
        try { d = JsonUtility.FromJson<SaveData>(json); }
        catch (Exception) { return null; }

        if (d == null || d.version != Version) return null;
        if (d.floor < 1 || d.floor > FloorProfile.FinalFloor) return null;
        // The shop only follows floors 1..FinalFloor-1; the last floor ends in Win.
        if (d.inShop && d.floor >= FloorProfile.FinalFloor) return null;
        return d;
    }

    // ---------------------------------------------------------------- writing

    /// <summary>
    /// Snapshots the statics. <paramref name="floor"/> is the floor to resume, or with inShop the floor just cleared.
    /// A failed write only logs - saving must never break the game.
    /// </summary>
    public static void WriteCheckpoint(int floor, bool inShop)
    {
        try
        {
            SaveData d = new SaveData
            {
                version = Version,
                floor = Mathf.Clamp(floor, 1, FloorProfile.FinalFloor),
                inShop = inShop,
                useKitMaze = GameFlow.UseKitMaze,
                rolledObjectives = (int[])GameFlow.RolledObjectives.Clone()
            };

            PlayerWallet.CaptureTo(d);
            PlayerInventory.CaptureTo(d);
            LoreArchive.CaptureTo(d);
            DreadDirector.CaptureTo(d);

            Store.Write(JsonUtility.ToJson(d));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveSystem: could not write the save ({e.Message}).");
        }
    }

    public static void Delete()
    {
        try { Store.Delete(); }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveSystem: could not delete the save ({e.Message}).");
        }
    }

    // ---------------------------------------------------------------- resuming

    /// <summary>
    /// CONTINUE: restores every static from the save and reloads into a fresh maze (PendingSeed 0). A save
    /// taken in the shop sets GameFlow.ResumeInShop so MainMenu.Start enters the shop instead of the run.
    /// Returns false (changing nothing) if there is no usable save.
    /// </summary>
    public static bool ResumeCampaign()
    {
        if (!TryLoad(out SaveData d)) return false;

        PlayerWallet.RestoreFrom(d);
        PlayerInventory.RestoreFrom(d);
        LoreArchive.RestoreFrom(d);
        DreadDirector.RestoreFrom(d);

        GameFlow.CurrentFloor = d.floor;
        GameFlow.UseKitMaze = d.useKitMaze;
        CopyRolledObjectives(d);
        GameFlow.ResumeInShop = d.inShop;

        GameFlow.Restart(false, 0);
        return true;
    }

    /// <summary>
    /// Shop resume only: the rebuilt background maze rolls its mechanics pair again from its fresh seed and
    /// overwrites RolledObjectives[floor]; this puts the pair the player actually played back, so the next
    /// floor still excludes it.
    /// </summary>
    public static void ReapplyRolledObjectives()
    {
        if (TryLoad(out SaveData d)) CopyRolledObjectives(d);
    }

    private static void CopyRolledObjectives(SaveData d)
    {
        int[] target = GameFlow.RolledObjectives;
        Array.Clear(target, 0, target.Length);
        if (d.rolledObjectives != null) Array.Copy(d.rolledObjectives, target, Mathf.Min(d.rolledObjectives.Length, target.Length));
    }
}

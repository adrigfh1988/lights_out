using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F74: which lore note ids have ever been read this campaign - a note only pays its +3 shard bonus
/// the first time, campaign-wide, so revisiting floor 2 on a retry (or reading the same note twice)
/// never double-pays. Campaign-scoped per CLAUDE.md's static-reset rule: reset by the
/// SubsystemRegistration hook and cleared by GameFlow.ReturnToMenu, same lifetime as PlayerWallet.
/// </summary>
public static class LoreArchive
{
    private static readonly HashSet<int> ReadIds = new HashSet<int>();

    /// <summary>True the first time this id is marked read this campaign; false on every later call for the same id.</summary>
    public static bool MarkRead(int noteId)
    {
        // F86: negative ids (the Intake's note) are never recorded - no shard bonus, nothing in a save.
        if (noteId < 0) return false;
        return ReadIds.Add(noteId);
    }

    public static bool HasRead(int noteId) => ReadIds.Contains(noteId);

    public static void ResetCampaign()
    {
        ReadIds.Clear();
    }

    /// <summary>F83: writes the read note ids into a save.</summary>
    public static void CaptureTo(SaveData data)
    {
        data.readNoteIds = new int[ReadIds.Count];
        ReadIds.CopyTo(data.readNoteIds);
    }

    /// <summary>F83: replaces the read set from a save (null tolerated).</summary>
    public static void RestoreFrom(SaveData data)
    {
        ReadIds.Clear();
        if (data.readNoteIds == null) return;
        foreach (int id in data.readNoteIds) ReadIds.Add(id);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetCampaign();
}

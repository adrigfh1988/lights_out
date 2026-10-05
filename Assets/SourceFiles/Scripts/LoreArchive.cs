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
        return ReadIds.Add(noteId);
    }

    public static bool HasRead(int noteId) => ReadIds.Contains(noteId);

    public static void ResetCampaign()
    {
        ReadIds.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetCampaign();
}

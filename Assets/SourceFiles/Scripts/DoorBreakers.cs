using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F75: static registry of every actor that can bash a closed <see cref="MazeDoor"/> open by walking
/// into it. Before this slice MazeDoor.CheckHunterBash hard-coded the one AIFollower reference it was
/// configured with; the Stalker needed the same "a NavMeshAgent would otherwise clip through a plain
/// collider" fix without MazeDoor knowing anything about a second creature. The hunter keeps its loud
/// slam and <see cref="AIFollower.Stall"/>; the Stalker (decision D7/F75) opens a door silently and keeps
/// moving - "it slips under".
///
/// Scene-scoped, not campaign-scoped (CLAUDE.md's static-reset rule): every registrant unregisters
/// itself (AIFollower.OnDisable, Stalker.OnDestroy), and the SubsystemRegistration reset below is the
/// belt-and-braces backstop for a stale entry surviving a domain reload.
/// </summary>
public static class DoorBreakers
{
    public struct Entry
    {
        public Transform Actor;
        public NavMeshAgent Agent;
        /// <summary>True for a breaker that opens a door with no slam and no stall (the Stalker).</summary>
        public bool Silent;
        /// <summary>Called with the bash-stall duration for a non-silent breaker (the hunter's AIFollower.Stall). Null for a silent one.</summary>
        public System.Action<float> Stall;
    }

    private static readonly List<Entry> _entries = new List<Entry>();

    public static IReadOnlyList<Entry> All => _entries;

    public static void Register(Transform actor, NavMeshAgent agent, bool silent, System.Action<float> stall)
    {
        if (actor == null) return;
        Unregister(actor); // idempotent: a re-Configure never leaves a duplicate entry
        _entries.Add(new Entry { Actor = actor, Agent = agent, Silent = silent, Stall = stall });
    }

    public static void Unregister(Transform actor)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Actor == actor) _entries.RemoveAt(i);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _entries.Clear();
    }
}

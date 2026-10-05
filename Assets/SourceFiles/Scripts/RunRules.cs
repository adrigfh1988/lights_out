using UnityEngine;

/// <summary>
/// F74 decision D9: one seeded rule per floor from floor 2 on, so a Retry of the same maze keeps its
/// rule (it is a pure function of UsedSeed and the floor number, never re-rolled). Most rules just
/// mutate the FloorProfile fields that already exist - ApplyToProfile is called once, from
/// MazeGenerator.ApplyFloorProfile right after PlayerInventory.ApplyActiveModifiers, so AIFollower.
/// ApplyDifficulty (which reads the profile later, in PlaceAI) and SpawnShards (which reads
/// FloorProfile.ShardCount live) both see the mutated values automatically. The handful of effects with
/// nowhere to live on FloorProfile (DyingGrid's periodic kill, WeakBattery's start charge/recharge rate,
/// GlassFloors' extra noisy-surface count, KeenEars' shard payout) are read directly off this class by
/// whichever system needs them.
///
/// Current/Name/Description are scene-scoped in spirit (recomputed at the top of every floor's Awake,
/// before anything reads them) but kept as statics per CLAUDE.md's rule for new run state: reset by the
/// SubsystemRegistration hook and cleared by GameFlow.ReturnToMenu, so a stale rule can never survive
/// between presses of Play with Reload Domain disabled.
/// </summary>
public static class RunRules
{
    public enum Rule { None, DyingGrid, KeenEars, HeavyFog, WeakBattery, Restless, GlassFloors, RichVeins }

    private static readonly Rule[] Pool =
    {
        Rule.DyingGrid, Rule.KeenEars, Rule.HeavyFog, Rule.WeakBattery, Rule.Restless, Rule.GlassFloors, Rule.RichVeins
    };

    public static Rule Current { get; private set; } = Rule.None;
    public static string Name { get; private set; } = "";
    public static string Description { get; private set; } = "";

    /// <summary>GlassFloors: multiplies BuildNoisySurfaces' spawn count. 1 on every other rule/floor.</summary>
    public static float NoisyPatchMultiplier { get; private set; } = 1f;

    /// <summary>KeenEars: multiplies a maze shard's payout (not the anti-farm counter it feeds - see PlayerWallet.CollectMazeShard). 1 on every other rule/floor.</summary>
    public static float ShardPayoutMultiplier { get; private set; } = 1f;

    /// <summary>WeakBattery: fraction of a full charge the flashlight starts at. 1 (full, the old default) on every other rule/floor.</summary>
    public static float BatteryStartFraction { get; private set; } = 1f;

    /// <summary>WeakBattery: multiplies the flashlight's off-time recharge rate. 1 on every other rule/floor.</summary>
    public static float BatteryRechargeMultiplier { get; private set; } = 1f;

    /// <summary>Resolved once per floor by MazeGenerator.ApplyFloorProfile, before its useFloorProfile-off early return, so Current is never stale even on a debug/Inspector-only maze.</summary>
    public static void Resolve(int usedSeed, int floor)
    {
        NoisyPatchMultiplier = 1f;
        ShardPayoutMultiplier = 1f;
        BatteryStartFraction = 1f;
        BatteryRechargeMultiplier = 1f;

        if (floor < 2)
        {
            Current = Rule.None;
            Name = "";
            Description = "";
            return;
        }

        int hash = HashSeedFloor(usedSeed, floor);
        int index = ((hash % Pool.Length) + Pool.Length) % Pool.Length;
        Current = Pool[index];

        switch (Current)
        {
            case Rule.DyingGrid:
                Name = "DYING GRID";
                Description = "A lamp loses power every minute.";
                break;
            case Rule.KeenEars:
                Name = "KEEN EARS";
                Description = "It hears farther here. Shards are worth more.";
                ShardPayoutMultiplier = 1.5f;
                break;
            case Rule.HeavyFog:
                Name = "HEAVY FOG";
                Description = "You see less; so does it.";
                break;
            case Rule.WeakBattery:
                Name = "WEAK BATTERY";
                Description = "You start half-charged, but it stores faster off.";
                BatteryStartFraction = 0.55f;
                BatteryRechargeMultiplier = 1.5f;
                break;
            case Rule.Restless:
                Name = "RESTLESS";
                Description = "It moves and appears more often.";
                break;
            case Rule.GlassFloors:
                Name = "GLASS FLOORS";
                Description = "More broken glass and puddles underfoot.";
                NoisyPatchMultiplier = 2f;
                break;
            case Rule.RichVeins:
                Name = "RICH VEINS";
                Description = "More shards, but it walks a little faster.";
                break;
        }
    }

    /// <summary>Mutates fields FloorProfile already exposes. Everything else above is read directly by whichever system needs it.</summary>
    public static void ApplyToProfile(FloorProfile p)
    {
        if (p == null) return;

        switch (Current)
        {
            case Rule.KeenEars:
                p.HearingScale *= 1.35f;
                break;
            case Rule.HeavyFog:
                p.FogDensity *= 1.6f;
                p.ViewDistance *= 0.75f;
                break;
            case Rule.Restless:
                p.PhantomInterval *= 0.6f; // relocation interval itself is scaled on DreadDirector directly - see SetHardMinimumCooldownMultiplier
                break;
            case Rule.RichVeins:
                p.ShardCount = Mathf.RoundToInt(p.ShardCount * 2f);
                p.WalkSpeed *= 1.1f;
                break;
        }
    }

    /// <summary>F77: shared with FloorProfile.RollObjectives, which salts the seed (0x4B455953, "KEYS") so the
    /// objectives roll never correlates with this rule roll.</summary>
    internal static int HashSeedFloor(int seed, int floor)
    {
        unchecked
        {
            int h = seed * 397 + floor * 74317;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return h;
        }
    }

    /// <summary>Cleared alongside the rest of the campaign's run state - see GameFlow.ReturnToMenu.</summary>
    public static void ResetCampaign()
    {
        Current = Rule.None;
        Name = "";
        Description = "";
        NoisyPatchMultiplier = 1f;
        ShardPayoutMultiplier = 1f;
        BatteryStartFraction = 1f;
        BatteryRechargeMultiplier = 1f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetCampaign();
}

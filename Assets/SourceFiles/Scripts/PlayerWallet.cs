using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One line of a Payout ledger, e.g. "STARS  2 x 10" / "+20".</summary>
public struct PayoutLine
{
    public string Label;
    public int Amount;
}

/// <summary>A receipt shown on the ledger (floor clear) or the end screen (consolation/final win).</summary>
public sealed class Payout
{
    public string Title;
    public readonly List<PayoutLine> Lines = new List<PayoutLine>();
    public int Total;

    /// <summary>Set only by ComputeConsolation. 0 = not a consolation payout. Deposit() uses it to update the per-floor cap.</summary>
    public int ConsolationFloor;
}

/// <summary>
/// The campaign's currency. Statics, reset at the main menu and at SubsystemRegistration - nothing is
/// saved to disk, and a run never outlives Play mode. Anti-farm bookkeeping (decision 4) lives here:
/// a same-seed retry re-spawns the same maze, so both maze shards and consolation are capped per floor.
/// </summary>
public static class PlayerWallet
{
    public const string CurrencyName = "SHARDS";
    public const int StarValue = 10;
    public const int ShardValue = 5;
    public const float TimeBonusPerSecond = 0.5f;
    public const int TimeBonusCap = 40;
    public const float ConsolationFraction = 0.35f;

    public static int Shards { get; private set; }

    /// <summary>Everything earned this campaign, spent or not. Shown on the final screen.</summary>
    public static int TotalEarned { get; private set; }

    /// <summary>Maze shards picked up since BeginAttempt. Informational line on the ledger.</summary>
    public static int ShardsFoundThisAttempt { get; private set; }

    // Anti-farm (decision 4). Index = floor, 1..FinalFloor.
    private static readonly int[] MazeShardsEarnedOnFloor = new int[FloorProfile.FinalFloor + 1];
    private static readonly int[] ConsolationPaidOnFloor = new int[FloorProfile.FinalFloor + 1];

    public static int FloorBonus(int floor) => 30 + 20 * Mathf.Max(0, floor - 1);

    public static int MazeShardsAlreadyTakenOnFloor(int floor)
    {
        int f = Mathf.Clamp(floor, 1, FloorProfile.FinalFloor);
        return MazeShardsEarnedOnFloor[f] / ShardValue;
    }

    /// <summary>Called by MazeGenerator.Awake: a new attempt at the current floor.</summary>
    public static void BeginAttempt()
    {
        ShardsFoundThisAttempt = 0;
    }

    /// <summary>ShardPickup: live deposit, counted against the floor's cap.</summary>
    public static void CollectMazeShard()
    {
        ShardsFoundThisAttempt++;
        int floor = Mathf.Clamp(GameFlow.CurrentFloor, 1, FloorProfile.FinalFloor);
        MazeShardsEarnedOnFloor[floor] += ShardValue;
        Add(ShardValue);
    }

    public static Payout ComputeFloorClear(int floor, int stars, float secondsLeft)
    {
        Payout payout = new Payout { Title = $"FLOOR {floor} CLEARED" };

        int starTotal = stars * StarValue;
        payout.Lines.Add(new PayoutLine { Label = $"STARS  {stars} x {StarValue}", Amount = starTotal });

        int timeBonus = Mathf.Min(TimeBonusCap, Mathf.RoundToInt(Mathf.Max(0f, secondsLeft) * TimeBonusPerSecond));
        payout.Lines.Add(new PayoutLine { Label = $"TIME LEFT  {MazeEscape.FormatTime(secondsLeft)}", Amount = timeBonus });

        int clearBonus = FloorBonus(floor);
        payout.Lines.Add(new PayoutLine { Label = $"FLOOR {floor} CLEARED", Amount = clearBonus });

        // Informational only - these shards were already deposited live as they were picked up.
        int shardsFound = ShardsFoundThisAttempt;
        payout.Lines.Add(new PayoutLine { Label = $"SHARDS FOUND  {shardsFound} x {ShardValue}  (already yours)", Amount = 0 });

        payout.Total = starTotal + timeBonus + clearBonus;
        return payout;
    }

    public static Payout ComputeConsolation(int floor, int stars)
    {
        int f = Mathf.Clamp(floor, 1, FloorProfile.FinalFloor);
        int raw = Mathf.RoundToInt(stars * StarValue * ConsolationFraction);
        int cap = FloorProfile.For(f).StarCount * StarValue;
        int allowed = Mathf.Clamp(cap - ConsolationPaidOnFloor[f], 0, raw);

        Payout payout = new Payout
        {
            Title = "CONSOLATION",
            Total = allowed,
            ConsolationFloor = f
        };
        payout.Lines.Add(new PayoutLine { Label = $"CONSOLATION  35% of {stars} stars", Amount = allowed });
        return payout;
    }

    public static void Deposit(Payout payout)
    {
        if (payout == null) return;
        Add(payout.Total);
        if (payout.ConsolationFloor > 0)
        {
            ConsolationPaidOnFloor[payout.ConsolationFloor] += payout.Total;
        }
    }

    public static bool TrySpend(int amount)
    {
        if (amount <= 0) return true;
        if (amount > Shards) return false;
        Shards -= amount;
        return true;
    }

    /// <summary>Inspector debug affordance: deposited into the wallet when the maze is built.</summary>
    public static void DebugDeposit(int amount)
    {
        if (amount != 0) Add(amount);
    }

    private static void Add(int amount)
    {
        Shards += amount;
        TotalEarned += amount;
    }

    public static void ResetCampaign()
    {
        Shards = 0;
        TotalEarned = 0;
        ShardsFoundThisAttempt = 0;
        Array.Clear(MazeShardsEarnedOnFloor, 0, MazeShardsEarnedOnFloor.Length);
        Array.Clear(ConsolationPaidOnFloor, 0, ConsolationPaidOnFloor.Length);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetCampaign();
}

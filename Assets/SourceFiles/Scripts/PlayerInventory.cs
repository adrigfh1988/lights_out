using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the player is holding, campaign-wide. Statics, reset at the main menu and at
/// SubsystemRegistration - the economy lives for one campaign and never touches disk.
/// </summary>
public static class PlayerInventory
{
    public const int ConsumableCap = 3;

    private static readonly Dictionary<ShopItem, int> Held = new Dictionary<ShopItem, int>();  // SpareBattery, StarCompass, SecondWind

    /// <summary>Bought in the shop for the NEXT floor.</summary>
    public static readonly HashSet<ShopItem> PendingModifiers = new HashSet<ShopItem>();

    /// <summary>Applied to the floor being played right now.</summary>
    public static readonly HashSet<ShopItem> ActiveModifiers = new HashSet<ShopItem>();

    public static int TorchColourIndex { get; private set; }
    public static int HudTintIndex { get; private set; }

    // Bitmasks, bit 0 = default (always owned).
    private static int _ownedTorchColours = 1;
    private static int _ownedHudTints = 1;

    public static int Count(ShopItem item) => Held.TryGetValue(item, out int n) ? n : 0;

    public static int CapFor(ShopItem item) => item == ShopItem.SecondWind ? 1 : ConsumableCap;

    public static bool CanHoldMore(ShopItem item) => Count(item) < CapFor(item);

    public static void Grant(ShopItem item)
    {
        Held[item] = Count(item) + 1;
    }

    public static bool TryConsume(ShopItem item)
    {
        if (Count(item) <= 0) return false;
        Held[item] = Count(item) - 1;
        return true;
    }

    public static bool HasPending(ShopItem m) => PendingModifiers.Contains(m);
    public static bool HasActive(ShopItem m) => ActiveModifiers.Contains(m);

    public static bool OwnsTorchColour(int i) => (_ownedTorchColours & (1 << i)) != 0;
    public static bool OwnsHudTint(int i) => (_ownedHudTints & (1 << i)) != 0;

    public static void GrantTorchColour(int i)
    {
        _ownedTorchColours |= 1 << i;
        TorchColourIndex = i;
    }

    public static void SelectTorchColour(int i)
    {
        if (OwnsTorchColour(i)) TorchColourIndex = i;
    }

    public static void GrantHudTint(int i)
    {
        _ownedHudTints |= 1 << i;
        HudTintIndex = i;
    }

    public static void SelectHudTint(int i)
    {
        if (OwnsHudTint(i)) HudTintIndex = i;
    }

    /// <summary>GameFlow.NextFloor: the perks bought in the shop become this floor's; the old ones are spent.</summary>
    public static void AdvanceFloor()
    {
        ActiveModifiers.Clear();
        ActiveModifiers.UnionWith(PendingModifiers);
        PendingModifiers.Clear();
    }

    /// <summary>MazeGenerator.ApplyFloorProfile, before it copies the values out. Mutates the profile.</summary>
    public static void ApplyActiveModifiers(FloorProfile p)
    {
        if (p == null) return;
        if (HasActive(ShopItem.LongFuse)) p.EscapeSeconds += 10f;
        if (HasActive(ShopItem.StaminaTonic)) p.SprintSeconds += 4f;
        // 5->3, 4->3, 3->2, 2->2
        if (HasActive(ShopItem.ExtraLamps)) p.CellsPerLamp = Mathf.Max(2, Mathf.CeilToInt(p.CellsPerLamp * 0.6f));
        if (HasActive(ShopItem.QuietShoes)) p.NoiseScale = 0.7f;
    }

    public static void ResetCampaign()
    {
        Held.Clear();
        PendingModifiers.Clear();
        ActiveModifiers.Clear();
        TorchColourIndex = 0;
        HudTintIndex = 0;
        _ownedTorchColours = 1;
        _ownedHudTints = 1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetCampaign();
}

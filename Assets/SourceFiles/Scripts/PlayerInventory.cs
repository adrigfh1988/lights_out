using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the player is holding, campaign-wide. Statics, reset at the main menu and at
/// SubsystemRegistration - the economy lives for one campaign. F83: SaveSystem persists it at the floor
/// checkpoints through CaptureTo/RestoreFrom.
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
        // F71: CellsPerLamp got sparser (5..8, was 2..3), so the old floor of 2 no longer reads as
        // "extra" - 3 is the new minimum spacing this perk guarantees.
        if (HasActive(ShopItem.ExtraLamps)) p.CellsPerLamp = Mathf.Max(3, Mathf.CeilToInt(p.CellsPerLamp * 0.6f));
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

    /// <summary>F83: writes everything held, pending, active and cosmetic into a save. ShopItem is stored as its int value.</summary>
    public static void CaptureTo(SaveData data)
    {
        data.heldItems = new int[Held.Count];
        data.heldCounts = new int[Held.Count];
        int i = 0;
        foreach (KeyValuePair<ShopItem, int> pair in Held)
        {
            data.heldItems[i] = (int)pair.Key;
            data.heldCounts[i] = pair.Value;
            i++;
        }

        data.pendingModifiers = ToIntArray(PendingModifiers);
        data.activeModifiers = ToIntArray(ActiveModifiers);
        data.torchColourIndex = TorchColourIndex;
        data.hudTintIndex = HudTintIndex;
        data.ownedTorchColours = _ownedTorchColours;
        data.ownedHudTints = _ownedHudTints;
    }

    /// <summary>F83: replaces the inventory from a save. Unknown ShopItem values are skipped; null / mismatched arrays are tolerated.</summary>
    public static void RestoreFrom(SaveData data)
    {
        ResetCampaign();

        if (data.heldItems != null && data.heldCounts != null)
        {
            int n = Mathf.Min(data.heldItems.Length, data.heldCounts.Length);
            for (int i = 0; i < n; i++)
            {
                if (!System.Enum.IsDefined(typeof(ShopItem), data.heldItems[i])) continue;
                ShopItem item = (ShopItem)data.heldItems[i];
                int count = Mathf.Clamp(data.heldCounts[i], 0, CapFor(item));
                if (count > 0) Held[item] = count;
            }
        }

        FillSet(PendingModifiers, data.pendingModifiers);
        FillSet(ActiveModifiers, data.activeModifiers);

        // Bit 0 (the default look) is always owned.
        _ownedTorchColours = data.ownedTorchColours | 1;
        _ownedHudTints = data.ownedHudTints | 1;
        TorchColourIndex = ValidSelection(data.torchColourIndex, ShopCatalogue.TorchColours.Length, _ownedTorchColours);
        HudTintIndex = ValidSelection(data.hudTintIndex, ShopCatalogue.HudTints.Length, _ownedHudTints);
    }

    private static int[] ToIntArray(HashSet<ShopItem> set)
    {
        int[] result = new int[set.Count];
        int i = 0;
        foreach (ShopItem item in set) result[i++] = (int)item;
        return result;
    }

    private static void FillSet(HashSet<ShopItem> set, int[] values)
    {
        if (values == null) return;
        foreach (int v in values)
        {
            if (System.Enum.IsDefined(typeof(ShopItem), v)) set.Add((ShopItem)v);
        }
    }

    private static int ValidSelection(int index, int paletteLength, int ownedMask)
    {
        if (index < 0 || index >= paletteLength || index >= 31) return 0;
        return (ownedMask & (1 << index)) != 0 ? index : 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetCampaign();
}

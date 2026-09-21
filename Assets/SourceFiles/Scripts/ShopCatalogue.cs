using UnityEngine;

/// <summary>Every purchasable thing in the shop. Kept as an enum rather than a string, so a typo fails at compile time.</summary>
public enum ShopItem
{
    SpareBattery,
    StarCompass,
    SecondWind,
    LongFuse,
    QuietShoes,
    ExtraLamps,
    StaminaTonic,
    TorchColour,
    HudTint
}

/// <summary>What kind of thing a ShopItem is - drives how ShopMenu draws its card and how PlayerInventory holds it.</summary>
public enum ItemKind
{
    Consumable,
    SecondWind,
    FloorModifier,
    Cosmetic
}

/// <summary>Plain data for one shop entry. No behaviour: PlayerWallet/PlayerInventory decide what buying one does.</summary>
public sealed class ShopItemDef
{
    public ShopItem Id;
    public ItemKind Kind;
    public string Name;
    public string Blurb;
    public int BasePrice;
    public int Cap;
}

/// <summary>
/// Item definitions, prices and cosmetic palettes. Plain static data, exactly like FloorProfile - no
/// ScriptableObjects, no prefabs. PlayerWallet and PlayerInventory are the only things that mutate state.
/// </summary>
public static class ShopCatalogue
{
    /// <summary>Price grows 20% per shop floor past the first, so the late game is roomier but never buys everything.</summary>
    public const float PriceGrowthPerFloor = 0.2f;

    public static readonly ShopItemDef[] Items =
    {
        new ShopItemDef { Id = ShopItem.SpareBattery, Kind = ItemKind.Consumable, Name = "Spare Battery",
            Blurb = "A fresh cell. Refills the torch and switches it on. Press 1.", BasePrice = 30, Cap = 3 },
        new ShopItemDef { Id = ShopItem.StarCompass, Kind = ItemKind.Consumable, Name = "Star Compass",
            Blurb = "Points at the nearest star for five seconds. Press 2.", BasePrice = 25, Cap = 3 },
        new ShopItemDef { Id = ShopItem.SecondWind, Kind = ItemKind.SecondWind, Name = "Second Wind",
            Blurb = "When it catches you, it lets go. Once.", BasePrice = 120, Cap = 1 },
        new ShopItemDef { Id = ShopItem.LongFuse, Kind = ItemKind.FloorModifier, Name = "Long Fuse",
            Blurb = "Ten more seconds on the hatch timer, next floor.", BasePrice = 35, Cap = 1 },
        new ShopItemDef { Id = ShopItem.QuietShoes, Kind = ItemKind.FloorModifier, Name = "Quiet Shoes",
            Blurb = "Your footsteps carry a third less, next floor.", BasePrice = 40, Cap = 1 },
        new ShopItemDef { Id = ShopItem.ExtraLamps, Kind = ItemKind.FloorModifier, Name = "Extra Lamps",
            Blurb = "More lamps lit on the next floor.", BasePrice = 35, Cap = 1 },
        new ShopItemDef { Id = ShopItem.StaminaTonic, Kind = ItemKind.FloorModifier, Name = "Stamina Tonic",
            Blurb = "Four more seconds of sprint, next floor.", BasePrice = 30, Cap = 1 },
        new ShopItemDef { Id = ShopItem.TorchColour, Kind = ItemKind.Cosmetic, Name = "Torch Colour",
            Blurb = "Cold. Ember. Violet.", BasePrice = 50, Cap = 0 },
        new ShopItemDef { Id = ShopItem.HudTint, Kind = ItemKind.Cosmetic, Name = "HUD Tint",
            Blurb = "Phosphor. Amber. Ice.", BasePrice = 25, Cap = 0 },
    };

    public static ShopItemDef Get(ShopItem id)
    {
        foreach (ShopItemDef def in Items)
        {
            if (def.Id == id) return def;
        }
        return null;
    }

    /// <summary>shopFloor is the floor just cleared - the room is visited before GameFlow.NextFloor.</summary>
    public static int PriceFor(ShopItemDef def, int shopFloor)
    {
        if (def == null) return 0;
        return Mathf.RoundToInt(def.BasePrice * (1f + PriceGrowthPerFloor * Mathf.Max(0, shopFloor - 1)));
    }

    // Index 0 is the default look and is never for sale - GrantTorchColour/GrantHudTint start owning it.
    public static readonly (string Name, Color Colour)[] TorchColours =
    {
        ("Warm",   new Color(1f, 0.95f, 0.86f)),   // Flashlight's beamColor default
        ("Cold",   new Color(0.78f, 0.88f, 1f)),
        ("Ember",  new Color(1f, 0.72f, 0.45f)),
        ("Violet", new Color(0.82f, 0.68f, 1f)),
    };

    public static readonly (string Name, Color Colour)[] HudTints =
    {
        ("Bone",     new Color(0.86f, 0.86f, 0.9f)),  // PlayerHud.CalmColor / TensionDirector.calmColor default
        ("Phosphor", new Color(0.62f, 1f, 0.7f)),
        ("Amber",    new Color(1f, 0.78f, 0.45f)),
        ("Ice",      new Color(0.7f, 0.9f, 1f)),
    };
}

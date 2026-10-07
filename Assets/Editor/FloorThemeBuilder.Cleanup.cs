using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F89: the pack-built stand-ins for the primitive dressing the maze used to carry (plannings/primitive-cleanup-plan.md).
/// Two halves:
///   - CleanupDefs: new Alchemist PropPieces (AlchDefs, so they ride the F79 build/load path and the AlchemistSet
///     flags in ThemeSets) that replace a primitive prop of the same kind - crates, candles, an urn, a specimen shelf.
///   - Dress*: re-skin the primitive parts of four set pieces (shrine, sparking fuse box, summoning circle, the fallen
///     runner's torch) with pack models. The theme builders call them on a fresh build and PrimitiveDressingCleanup
///     calls the same methods on the existing prefabs, so both routes end in the same state.
/// Everything is placed by renderer bounds (pack pivots are unreliable), colliders come from the builder, never the pack.
/// </summary>
public static partial class FloorThemeBuilder
{
    // ---------------------------------------------------------------- Alchemist stand-ins

    private static List<AlchDef> CleanupDefs()
    {
        const string M = "Items/Misc/", Li = "Items/Lights/", O = "Items/Organic/", D = "Items/Dishes/", F = "Furniture/";
        const PropPiece.MountKind Floor = PropPiece.MountKind.Floor, Wall = PropPiece.MountKind.Wall;

        return new List<AlchDef>
        {
            // Boiler CrateStack (two rust-coloured cubes) -> the pack's trunks.
            new AlchDef
            {
                Name = "Alch_CrateStack", Set = AlchemistSet.Crates, Mount = Wall, Depth = 0.6f, Weight = 1f, RootBox = true,
                Compose = () => Compose("Alch_CrateStack", 0.6f, 0f,
                    new[] { P(M + "Trunk01", 0f, 0.3f), P(M + "Trunk01", 0.04f, 0.3f, 25f, scale: 0.8f, on: 0) })
            },
            // Crypt CandleCluster (cylinders and flame spheres) -> the pack's candles.
            new AlchDef
            {
                Name = "Alch_Candles", Set = AlchemistSet.Candles, Mount = Floor, Depth = 0.35f, Weight = 1.5f,
                Compose = () => Compose("Alch_Candles", 0.35f, 0.35f,
                    new[] { P(Li + "Candle01", -0.14f, 0.15f), P(Li + "Candle01b", 0.02f, 0.22f), P(Li + "Candle01c", 0.16f, 0.14f), P(Li + "Candle01d", -0.04f, 0.08f, 40f), P(Li + "Candle01", 0.1f, 0.28f, 90f) })
            },
            // Crypt Urn (a stone cylinder and a sphere) -> one of the pack's big jugs, scaled up to burial-urn size.
            new AlchDef
            {
                Name = "Alch_Urn", Set = AlchemistSet.Urns, Mount = Wall, Depth = 0.5f, Weight = 1f, RootBox = true,
                Compose = () => Compose("Alch_Urn", 0.5f, 0f, new[] { P(D + "ClutterPichet02", 0f, 0.25f, scale: 2.4f) })
            },
            // Lab SpecimenShelf (a frame of cylinder jars) -> the pack's wall shelf with specimen jars.
            new AlchDef
            {
                Name = "Alch_SpecimenShelf", Set = AlchemistSet.SpecimenShelf, Mount = Wall, Depth = 0.36f, Weight = 0.4f, // 1 made it ~1 in 4 Lab wall props (8 Oct 2026)
                Compose = () =>
                {
                    GameObject root = new GameObject("Alch_SpecimenShelf");
                    GameObject shelf = HangFlat(root, F + "WoodShelf01", 0f, 1.35f, 0f);
                    if (shelf != null)
                    {
                        float top = AlchemistPack.RenderBounds(shelf).max.y;
                        AlchemistPack.Place(root.transform, O + "JarBaby01", new Vector3(-0.5f, top, 0.17f), Quaternion.identity, 0.75f);
                        AlchemistPack.Place(root.transform, O + "JarBrain", new Vector3(0.0f, top, 0.17f), Quaternion.identity, 0.75f);
                        AlchemistPack.Place(root.transform, O + "JarBaby03", new Vector3(0.5f, top, 0.17f), Quaternion.identity, 0.75f);
                    }
                    return root;
                }
            },
        };
    }

    // ---------------------------------------------------------------- set-piece dressing

    internal const string CleanupAltar = "PackAltar", CleanupFuseBox = "PackFuseBox", CleanupLamp = "PackTorch";

    /// <summary>Crypt_Shrine: a pack table for the altar, seven pack candles along it and three skulls. Collider on a holder, as the primitive altar had one.</summary>
    internal static void DressShrine(GameObject root)
    {
        GameObject altar = PlaceFitted(root.transform, "Furniture/WoodTableSquare01", new Vector3(0f, 0f, 0.45f), Quaternion.identity, 1.6f, 0.8f, 0.9f);
        if (altar == null) return;
        altar.name = CleanupAltar;
        Bounds table = AlchemistPack.RenderBounds(altar);
        ColliderHolder(root.transform, "AltarCollider", table);

        string[] candles = { "Items/Lights/Candle01", "Items/Lights/Candle01b", "Items/Lights/Candle01c", "Items/Lights/Candle01d" };
        System.Random rng = new System.Random(304);
        for (int i = 0; i < 7; i++)
        {
            float cx = table.center.x + (i - 3) * Mathf.Min(0.2f, table.size.x / 8f);
            GameObject c = AlchemistPack.Place(root.transform, candles[i % candles.Length], new Vector3(cx, table.max.y, table.center.z - 0.1f), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            if (c != null) c.name = $"PackCandle{i}";
        }
        string[] skulls = { "Items/Organic/Skull01", "Items/Organic/Skull01b", "Items/Organic/Skull02" };
        for (int i = 0; i < 3; i++)
        {
            GameObject s = AlchemistPack.Place(root.transform, skulls[i], new Vector3(table.center.x + (i - 1) * 0.4f, table.max.y, table.center.z + 0.12f), Quaternion.Euler(0f, 160f + i * 15f, 0f));
            if (s != null) s.name = $"PackSkull{i}";
        }
    }

    /// <summary>Boiler_Sparks: the Socket Pack's fuse box hung on the wall, door removed so it reads as torn open.</summary>
    internal static void DressSparks(GameObject root)
    {
        GameObject box = PackSpawn(root.transform, SocketRoot + "Fuse_Box_01.prefab");
        if (box == null) return;
        box.name = CleanupFuseBox;
        box.transform.localScale *= 2f;
        Transform door = FindDeep(box.transform, "Fuse_Box_Door");
        if (door != null) door.gameObject.SetActive(false);

        Bounds b = AlchemistPack.RenderBounds(box);
        if (b.size.x < b.size.z) box.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * box.transform.rotation;
        b = AlchemistPack.RenderBounds(box);
        box.transform.position += new Vector3(-b.center.x, 1.6f - b.center.y, 0.01f - b.min.z);
        ColliderHolder(root.transform, "FuseBoxCollider", AlchemistPack.RenderBounds(box));
    }

    /// <summary>Hollow_Circle: eight pack candles in a ring and a pack baby doll in the middle.</summary>
    internal static void DressCircle(GameObject root)
    {
        string[] candles = { "Items/Lights/Candle01", "Items/Lights/Candle01b", "Items/Lights/Candle01c", "Items/Lights/Candle01d" };
        System.Random rng = new System.Random(314);
        for (int i = 0; i < 8; i++)
        {
            float angle = i / 8f * Mathf.PI * 2f;
            Vector3 pos = new Vector3(Mathf.Cos(angle) * 0.7f, 0f, 0.9f + Mathf.Sin(angle) * 0.7f);
            GameObject c = AlchemistPack.Place(root.transform, candles[i % candles.Length], pos, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            if (c != null) c.name = $"PackCandle{i}";
        }
        GameObject doll = AlchemistPack.Place(root.transform, "Items/Organic/Baby01", new Vector3(0f, 0f, 0.9f), Quaternion.Euler(0f, 25f, 0f) * Quaternion.Euler(90f, 0f, 0f));
        if (doll != null) doll.name = "PackDoll";
    }

    /// <summary>FallenRunner: the dropped torch is the pack's oil lamp lying on its side.</summary>
    internal static void DressTorch(GameObject root)
    {
        GameObject lamp = AlchemistPack.Place(root.transform, "Items/Lights/Oillamp01", new Vector3(0.5f, 0f, 1.08f), Quaternion.Euler(0f, 30f, 80f));
        if (lamp != null) lamp.name = CleanupLamp;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Spawns a pack piece, shrinks it (never grows it) to fit maxX/maxZ/maxY, and sets its bottom-centre at localBottomCentre. parent must sit at the world origin.</summary>
    private static GameObject PlaceFitted(Transform parent, string path, Vector3 localBottomCentre, Quaternion rotation, float maxX, float maxZ, float maxY)
    {
        GameObject go = AlchemistPack.Spawn(parent, path);
        if (go == null) return null;
        go.transform.localRotation = rotation * go.transform.localRotation;

        Vector3 s = AlchemistPack.RenderBounds(go).size;
        if (s.x < s.z && maxX > maxZ) go.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * go.transform.rotation; // long side along the wall
        s = AlchemistPack.RenderBounds(go).size;
        float f = Mathf.Min(1f, Mathf.Min(maxX / Mathf.Max(0.0001f, s.x), Mathf.Min(maxZ / Mathf.Max(0.0001f, s.z), maxY / Mathf.Max(0.0001f, s.y))));
        if (f < 0.999f) go.transform.localScale *= f;

        Bounds b = AlchemistPack.RenderBounds(go);
        go.transform.position += parent.TransformPoint(localBottomCentre) - new Vector3(b.center.x, b.min.y, b.center.z);
        return go;
    }

    private static void ColliderHolder(Transform root, string name, Bounds worldBounds)
    {
        GameObject holder = new GameObject(name);
        holder.transform.SetParent(root, false);
        FitBox(holder, worldBounds); // root sits at the world origin, so world bounds are root-local
    }
}

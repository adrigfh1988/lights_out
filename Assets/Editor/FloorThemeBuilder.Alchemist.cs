using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// F79: the Alchemist House pack (Assets/BK_AlchemistHouse) as maze dressing. About 70 loose props become
/// 31 composed PropPiece prefabs under Themes/Common/Prefabs/Alchemist - floor clutter clusters, standing
/// and hung wall props, and rugs as floor decals - that plug into the existing F70 placement passes
/// (MazeGenerator.BuildWallProps / BuildFloorProps / BuildDecals) with no new pass and no new RNG offset.
/// Kept in its own partial file like FloorThemeBuilder.Dressing.cs. Spec: plannings/alchemist-maze-dressing-plan.md.
///
/// The full-rebuild builders reach these prefabs: each theme builder and BuildCommonDressing call
/// BuildAlchemistProps with their subset. (The one-off Add Alchemist Dressing migration that appended them
/// to pre-F79 galleries has been applied and removed - it is in git history.)
/// </summary>
public static partial class FloorThemeBuilder
{
    [Flags]
    internal enum AlchemistSet
    {
        None = 0, Books = 1, Bottles = 2, Dishes = 4, Lab = 8, Organic = 16, Specimens = 32,
        Storage = 64, Furniture = 128, Paintings = 256, Weapons = 512, Rugs = 1024, All = ~0
    }

    internal const string AlchemistFolder = CommonRoot + "/Prefabs/Alchemist";

    /// <summary>Per gallery row (Ward, Boiler Deck, Crypt, Lab, Hollow) - the one place a theme's subset is defined.</summary>
    internal static readonly AlchemistSet[] ThemeSets =
    {
        AlchemistSet.Books | AlchemistSet.Bottles | AlchemistSet.Dishes | AlchemistSet.Storage | AlchemistSet.Furniture | AlchemistSet.Paintings | AlchemistSet.Rugs,
        AlchemistSet.Bottles | AlchemistSet.Dishes | AlchemistSet.Storage | AlchemistSet.Furniture,
        AlchemistSet.All,
        AlchemistSet.Books | AlchemistSet.Bottles | AlchemistSet.Lab | AlchemistSet.Organic | AlchemistSet.Specimens | AlchemistSet.Furniture,
        AlchemistSet.All
    };

    /// <summary>The kit maze's subset. Kit mode forces wall/ceiling prop chances to 0, so only the Floor and FloorDecal kinds of these are ever listed (see KitPropFilter).</summary>
    internal const AlchemistSet KitSet = AlchemistSet.Books | AlchemistSet.Bottles | AlchemistSet.Dishes | AlchemistSet.Lab | AlchemistSet.Organic | AlchemistSet.Storage | AlchemistSet.Rugs;

    private static readonly string[] AlchemistRowNames = { "Ward", "Boiler Deck", "Crypt", "Lab", "Hollow" };

    private static Dictionary<string, PropPiece> _alchemistProps;

    // ---------------------------------------------------------------- definitions

    private sealed class AlchDef
    {
        public string Name;
        public AlchemistSet Set;
        public PropPiece.MountKind Mount;
        public float Depth;
        public float Weight;
        public bool RootBox;               // standing wall props: one BoxCollider on the root, fitted to the bounds
        public Func<GameObject> Compose;   // builds the cluster at the world origin; the root is named Name
    }

    private struct AlchPart
    {
        public string Path;
        public float X, Z, Yaw, Pitch, Roll, Scale, Lift;
        public int On;
    }

    /// <summary>One piece of a cluster. x/z are the bounds' bottom-centre; yaw is applied after pitch/roll; `on` stacks it on the top of an earlier part.</summary>
    private static AlchPart P(string path, float x, float z, float yaw = 0f, float pitch = 0f, float roll = 0f, float scale = 1f, int on = -1, float lift = 0f)
    {
        return new AlchPart { Path = path, X = x, Z = z, Yaw = yaw, Pitch = pitch, Roll = roll, Scale = scale, On = on, Lift = lift };
    }

    private static List<AlchDef> AlchemistDefs()
    {
        const string B = "Items/Books/", Bt = "Items/Bottles/", D = "Items/Dishes/", I = "Items/Ingredients/", L = "Items/Lab/",
            Li = "Items/Lights/", M = "Items/Misc/", O = "Items/Organic/", W = "Items/Weapons/", F = "Furniture/";
        const PropPiece.MountKind Floor = PropPiece.MountKind.Floor, Wall = PropPiece.MountKind.Wall;

        AlchDef Cluster(string name, AlchemistSet set, float depth, float weight, float maxHeight, params AlchPart[] parts)
        {
            return new AlchDef { Name = name, Set = set, Mount = Floor, Depth = depth, Weight = weight, Compose = () => Compose(name, depth, maxHeight, parts) };
        }

        AlchDef Standing(string name, AlchemistSet set, float depth, float weight, params AlchPart[] parts)
        {
            return new AlchDef { Name = name, Set = set, Mount = Wall, Depth = depth, Weight = weight, RootBox = true, Compose = () => Compose(name, depth, 0f, parts) };
        }

        AlchDef Hung(string name, AlchemistSet set, float depth, float weight, Action<GameObject> build)
        {
            return new AlchDef { Name = name, Set = set, Mount = Wall, Depth = depth, Weight = weight, Compose = () => { GameObject root = new GameObject(name); build(root); return root; } };
        }

        AlchDef Rug(string name, string carpet)
        {
            return new AlchDef { Name = name, Set = AlchemistSet.Rugs, Mount = PropPiece.MountKind.FloorDecal, Depth = 0f, Weight = 0.35f, Compose = () => ComposeRug(name, carpet) };
        }

        return new List<AlchDef>
        {
            // ---- floor clusters ----
            Cluster("Alch_BookPileA", AlchemistSet.Books, 0.40f, 1.0f, 0.35f,
                P(B + "Book01", 0f, 0.20f, pitch: 90f), P(B + "Book02", 0.01f, 0.21f, 14f, 90f, on: 0), P(B + "Book03", -0.01f, 0.20f, -10f, 90f, on: 1),
                P(B + "Book04", 0f, 0.22f, 22f, 90f, on: 2), P(B + "Book05", 0.32f, 0.16f, 70f, 90f), P(B + "Book05b", -0.30f, 0.24f, -30f, 90f)),
            Cluster("Alch_BookPileB", AlchemistSet.Books, 0.40f, 1.0f, 0.35f,
                P(B + "Book05c", -0.28f, 0.14f, 15f, 90f), P(B + "Book06", -0.02f, 0.18f, -25f, 90f), P(B + "Book06b", 0.04f, 0.19f, 35f, 90f, on: 1),
                P(B + "Book06c", 0.30f, 0.12f, 80f, 90f), P(B + "Book07", 0.22f, 0.30f, -8f, 90f), P(B + "Book08", -0.22f, 0.32f, 52f, 90f)),
            Cluster("Alch_BookPileC", AlchemistSet.Books, 0.40f, 0.8f, 0.35f,
                P(B + "Book09", -0.30f, 0.20f, roll: -65f), P(B + "Book09b", -0.15f, 0.20f, 4f, roll: -65f), P(B + "Book09c", 0f, 0.21f, -3f, roll: -65f),
                P(B + "Book09d", 0.15f, 0.20f, 5f, roll: -65f), P(B + "Book09e", 0.30f, 0.19f, 0f, roll: -65f)),
            Cluster("Alch_BookPileD", AlchemistSet.Books, 0.40f, 0.8f, 0.35f,
                P(B + "Book10", -0.22f, 0.20f, 5f, 90f), P(B + "Book10b", -0.21f, 0.21f, -18f, 90f, on: 0), P(B + "Book10c", -0.22f, 0.20f, 25f, 90f, on: 1), P(B + "Book11", -0.20f, 0.2f, -6f, 90f, on: 2),
                P(B + "Book12", 0.24f, 0.20f, -12f, 90f), P(B + "Book13", 0.25f, 0.21f, 20f, 90f, on: 4), P(B + "Book14", 0.24f, 0.20f, -30f, 90f, on: 5)),
            Cluster("Alch_BookRowA", AlchemistSet.Books, 0.42f, 0.6f, 0.35f,
                P(B + "Books1", -0.48f, 0.20f, pitch: 90f, scale: 0.8f), P(B + "Books4", 0.50f, 0.26f, 5f, 90f, scale: 0.8f)),
            Cluster("Alch_BookRowB", AlchemistSet.Books, 0.42f, 0.6f, 0.35f,
                P(B + "Books5", -0.45f, 0.20f, pitch: 90f, scale: 0.6f), P(B + "Books3", 0.40f, 0.26f, 6f, 90f, scale: 0.6f), P(B + "Books2", 0.05f, 0.18f, 90f, 90f, scale: 0.6f)),
            Cluster("Alch_BottlesA", AlchemistSet.Bottles, 0.35f, 1.0f, 0.35f,
                P(Bt + "Bottle01", -0.25f, 0.15f), P(Bt + "Bottle02", -0.10f, 0.22f), P(Bt + "Bottle03", 0.05f, 0.14f), P(Bt + "Bottle04", 0.20f, 0.22f),
                P(Bt + "Bottle01b", 0.02f, 0.30f, 70f, 90f)),
            Cluster("Alch_BottlesB", AlchemistSet.Bottles, 0.35f, 1.0f, 0.35f,
                P(Bt + "Bottle02b", -0.28f, 0.16f), P(Bt + "Bottle03b", -0.12f, 0.22f), P(Bt + "Bottle04b", 0.04f, 0.14f), P(Bt + "Flask01", 0.20f, 0.20f),
                P(Bt + "Flask03", 0.24f, 0.30f, 40f, 90f)),
            Cluster("Alch_Flasks", AlchemistSet.Lab, 0.35f, 1.0f, 0.35f,
                P(Bt + "Flask02", -0.28f, 0.14f), P(Bt + "Flask04", -0.12f, 0.22f), P(Bt + "Flask05", 0.04f, 0.14f), P(Bt + "Flask06", 0.20f, 0.22f, 30f, 90f), P(Bt + "Flask07", 0.30f, 0.12f)),
            Cluster("Alch_Dishes", AlchemistSet.Dishes, 0.40f, 1.0f, 0.35f,
                P(D + "ClutterPlate01", -0.28f, 0.20f), P(D + "ClutterPlate01", -0.27f, 0.21f, 30f, on: 0), P(D + "ClutterBowl01", -0.02f, 0.18f), P(D + "ClutterBowl02", 0.22f, 0.22f),
                P(D + "ClutterGlass01", 0.10f, 0.32f), P(D + "ClutterGlass02", -0.12f, 0.33f, 25f, 90f), P(D + "ClutterTanker01", 0.34f, 0.10f)),
            Cluster("Alch_Jugs", AlchemistSet.Dishes, 0.40f, 0.8f, 0.35f,
                P(D + "ClutterPichet01", -0.25f, 0.16f), P(D + "ClutterPichet02", 0.04f, 0.30f, 60f, 90f), P(D + "ClutterPichet03", 0.20f, 0.14f), P(D + "ClutterBowl03", -0.12f, 0.34f)),
            Cluster("Alch_Ingredients", AlchemistSet.Lab, 0.42f, 0.8f, 0.35f,
                P(I + "ClutterBowl03Ing01", -0.40f, 0.13f), P(I + "ClutterBowl03Ing02", -0.20f, 0.13f, 20f), P(I + "ClutterBowl03Ing03", 0f, 0.13f, 40f), P(I + "ClutterBowl03Ing04", 0.20f, 0.13f, 70f),
                P(I + "ClutterBowl03Ing05", 0.40f, 0.13f, 100f), P(I + "ClutterBowl03Ing06", -0.30f, 0.33f, 130f), P(I + "ClutterBowl03Ing07", -0.10f, 0.33f, 10f),
                P(I + "ClutterBowl03Ing03b", 0.10f, 0.33f, 50f), P(I + "ClutterBowl03Ing04b", 0.30f, 0.33f, 90f)),
            Cluster("Alch_LabGlass", AlchemistSet.Lab, 0.40f, 0.8f, 0.45f,
                P(L + "LabSetup01", 0.05f, 0.12f, 90f, 90f), P(L + "Caldron01", -0.25f, 0.28f), P(L + "CaldronTripod01", 0.25f, 0.30f, 75f, 90f)),
            Cluster("Alch_Skulls", AlchemistSet.Organic, 0.40f, 1.0f, 0.35f,
                P(O + "Skull01", -0.25f, 0.18f, 20f), P(O + "Skull01b", 0f, 0.16f, -30f), P(O + "Skull02", 0.25f, 0.20f, 200f),
                P(Li + "Candle01", -0.05f, 0.32f), P(Li + "Candle01c", 0.20f, 0.34f, 40f, 90f)),
            Cluster("Alch_Remains", AlchemistSet.Organic, 0.45f, 0.5f, 0.35f,
                P(O + "DragonSkull", -0.05f, 0.22f, 10f, scale: 0.65f), P(O + "Brain", 0.35f, 0.14f), P(O + "Bulb01", -0.40f, 0.14f), P(O + "Bulb01", -0.30f, 0.32f, 60f),
                P(O + "Apple", 0.30f, 0.34f), P(O + "Apple", 0.18f, 0.36f, 90f)),
            Cluster("Alch_SpecimenJars", AlchemistSet.Specimens, 0.40f, 0.5f, 0.46f,
                P(O + "JarBaby01", -0.45f, 0.18f, scale: 0.75f), P(O + "JarBaby02", -0.22f, 0.18f, scale: 0.75f), P(O + "JarBaby03", 0f, 0.18f, scale: 0.75f),
                P(O + "JarBrain", 0.22f, 0.18f, scale: 0.75f), P(O + "Jar01", 0.66f, 0.2f, 20f, 90f, scale: 0.75f)),
            Cluster("Alch_Baskets", AlchemistSet.Storage, 0.45f, 1.0f, 0.35f,
                P(M + "Basket01", -0.28f, 0.22f, 15f), P(M + "Basket02", 0.12f, 0.22f), P(M + "Basket03", 0.12f, 0.12f, 40f, 90f),
                P(M + "Log01", -0.40f, 0.34f, 80f, 90f), P(M + "Log02", -0.25f, 0.38f, 20f, 90f), P(M + "Log03", 0.38f, 0.32f, 100f, 90f), P(Li + "Candle01d", 0.30f, 0.12f)),

            // ---- standing wall props (one box collider on the root) ----
            Standing("Alch_TrunkStack", AlchemistSet.Storage, 0.50f, 1.0f,
                P(M + "Trunk01", 0f, 0.25f), P(M + "Basket01", -0.12f, 0.25f, 20f, on: 0), P(Li + "Candle01b_on", 0.2f, 0.25f, on: 0)),
            Standing("Alch_ChairAndStool", AlchemistSet.Furniture, 0.65f, 1.0f,
                P(F + "Chair01", -0.30f, 0.35f, 180f), P(F + "WoodStool", 0.25f, 0.30f), P(B + "Book14", 0.25f, 0.30f, 20f, 90f, on: 1)),
            Standing("Alch_GlobeStand", AlchemistSet.Furniture, 0.45f, 0.7f,
                P(F + "WoodStool", 0f, 0.25f), P(M + "Globe", 0f, 0.25f, on: 0), P(Li + "Oillamp01", 0.35f, 0.20f)),
            Standing("Alch_CauldronSpill", AlchemistSet.Lab, 0.55f, 0.7f,
                P(L + "CaldronTripod02", -0.55f, 0.28f), P(L + "CaldronTripod03", -0.15f, 0.30f, 70f, 90f), P(L + "LabSetup02", 0.30f, 0.20f), P(L + "LabSetup03", 0.80f, 0.24f)),

            // ---- hung wall props (no collider) ----
            Hung("Alch_Painting1", AlchemistSet.Paintings, 0.10f, 0.6f, r => HangFlat(r, M + "Painting01", 0f, 1.60f, 3f, scale: 1.6f)),
            Hung("Alch_Painting2", AlchemistSet.Paintings, 0.10f, 0.6f, r => HangFlat(r, M + "painting02", 0f, 1.55f, -4f, 180f, 1.8f)), // this frame's picture faces -Z at its default yaw
            Hung("Alch_Painting3", AlchemistSet.Paintings, 0.10f, 0.6f, r => HangFlat(r, M + "painting03", 0f, 1.45f, 2.5f, scale: 1.3f)),
            Hung("Alch_Painting4", AlchemistSet.Paintings, 0.10f, 0.6f, r => HangFlat(r, M + "painting04", 0f, 1.45f, -3f, scale: 1.3f)),
            Hung("Alch_Board", AlchemistSet.Paintings, 0.30f, 0.5f, r =>
            {
                GameObject board = HangFlat(r, F + "Board", 0f, 1.55f, 0f);
                HangDagger(r, board);
            }),
            Hung("Alch_WeaponRackA", AlchemistSet.Weapons, 0.10f, 0.6f, r =>
            {
                HangLong(r, W + "Sword01", 0f, 1.55f, 40f, 0.01f);
                HangLong(r, W + "Axe01", 0f, 1.55f, -40f, 0.05f);
            }),
            Hung("Alch_WeaponRackB", AlchemistSet.Weapons, 0.20f, 0.6f, r =>
            {
                HangLong(r, W + "Bow01", -0.15f, 1.6f, 0f, 0.01f);
                HangStaff(r, W + "Staff01", 0.4f);
                HangLong(r, W + "Arrow01", -0.15f, 0.95f, -18f, 0.05f);
                HangLong(r, W + "Arrow01", -0.05f, 0.95f, 0f, 0.05f);
                HangLong(r, W + "Arrow01", 0.05f, 0.95f, 18f, 0.05f);
            }),

            // ---- rugs (floor decals) ----
            Rug("Alch_Rug1", "Furniture/Carpet01"),
            Rug("Alch_Rug2", "Furniture/Carpet02"),
            Rug("Alch_Rug3", "Furniture/Carpet03"),
        };
    }

    // ---------------------------------------------------------------- composition

    /// <summary>
    /// Builds a cluster at the world origin. Parts are placed by bounds; the whole is then uniformly
    /// shrunk (about its own base) if it exceeds the depth / height budget, centred on x = 0, and pushed
    /// back so the wall side sits 3 cm off the wall plane (z = 0). +Z is into the corridor.
    /// </summary>
    private static GameObject Compose(string name, float depth, float maxHeight, AlchPart[] parts)
    {
        GameObject root = new GameObject(name);
        List<GameObject> placed = new List<GameObject>();
        foreach (AlchPart p in parts)
        {
            GameObject go = AlchemistPack.Spawn(root.transform, p.Path, p.Scale);
            placed.Add(go);
            if (go == null) continue;

            go.transform.localRotation = Quaternion.Euler(0f, p.Yaw, 0f) * Quaternion.Euler(p.Pitch, 0f, p.Roll) * go.transform.localRotation;
            Bounds b = AlchemistPack.RenderBounds(go);
            float baseY = p.On >= 0 && placed[p.On] != null ? AlchemistPack.RenderBounds(placed[p.On]).max.y : 0f;
            go.transform.position += new Vector3(p.X, baseY + p.Lift, p.Z) - new Vector3(b.center.x, b.min.y, b.center.z);
        }

        Settle(root, depth, maxHeight);
        return root;
    }

    private static void Settle(GameObject root, float depth, float maxHeight)
    {
        Bounds b = AlchemistPack.RenderBounds(root);
        float f = 1f;
        if (depth > 0f && b.size.z > 0.0001f) f = Mathf.Min(f, (depth - 0.02f) / b.size.z);
        if (maxHeight > 0f && b.size.y > 0.0001f) f = Mathf.Min(f, maxHeight / b.size.y);
        if (f < 0.999f)
        {
            // Scaled about the bounds' bottom-centre, not the origin: a pack pivot can be metres from its mesh.
            Vector3 anchor = new Vector3(b.center.x, 0f, b.center.z);
            foreach (Transform child in root.transform)
            {
                child.position = anchor + (child.position - anchor) * f;
                child.localScale *= f;
            }
            b = AlchemistPack.RenderBounds(root);
        }

        Vector3 shift = new Vector3(-b.center.x, 0f, 0.03f - b.min.z);
        foreach (Transform child in root.transform) child.position += shift;
    }

    /// <summary>The carpet as a child scaled so its long side is exactly 1 m and centred on a scale-1 root lying at y = 0 (MazeGenerator scales the root from DecalSizeRange).</summary>
    private static GameObject ComposeRug(string name, string carpet)
    {
        GameObject root = new GameObject(name);
        GameObject go = AlchemistPack.Spawn(root.transform, carpet);
        if (go == null) return root;

        Bounds b = AlchemistPack.RenderBounds(go);
        float longSide = Mathf.Max(b.size.x, b.size.z);
        if (longSide > 0.0001f) go.transform.localScale *= 1f / longSide;

        b = AlchemistPack.RenderBounds(go);
        go.transform.position += -new Vector3(b.center.x, b.min.y, b.center.z);
        return root;
    }

    // ---- hung helpers: the root sits on the floor at the wall face, the piece is placed by bounds ----

    /// <summary>A flat piece hung on the wall: its thin horizontal axis turned to Z, picture side towards +Z, back flush at z = 0.01, centred on (x, y), tilted about Z.</summary>
    private static GameObject HangFlat(GameObject root, string path, float x, float y, float tilt, float flipYaw = 0f, float scale = 1f)
    {
        GameObject go = AlchemistPack.Spawn(root.transform, path, scale);
        if (go == null) return null;

        Bounds b = AlchemistPack.RenderBounds(go);
        if (b.size.x < b.size.z) go.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * go.transform.rotation;
        if (Mathf.Abs(flipYaw) > 0.01f) go.transform.rotation = Quaternion.Euler(0f, flipYaw, 0f) * go.transform.rotation;
        if (Mathf.Abs(tilt) > 0.01f) go.transform.rotation = Quaternion.Euler(0f, 0f, tilt) * go.transform.rotation;

        b = AlchemistPack.RenderBounds(go);
        go.transform.position += new Vector3(x - b.center.x, y - b.center.y, 0.01f - b.min.z);
        return go;
    }

    /// <summary>Turns a long, thin piece (a weapon) so its long axis is vertical and it lies flat against the wall plane, then rolls it about Z.</summary>
    private static GameObject HangLong(GameObject root, string path, float x, float y, float roll, float backZ)
    {
        GameObject go = AlchemistPack.Spawn(root.transform, path);
        if (go == null) return null;
        StandUpAndFlatten(go);

        if (Mathf.Abs(roll) > 0.01f) AlchemistPack.RotateAboutCentre(go, Quaternion.Euler(0f, 0f, roll));

        Bounds b = AlchemistPack.RenderBounds(go);
        go.transform.position += new Vector3(x - b.center.x, y - b.center.y, backZ - b.min.z);
        return go;
    }

    /// <summary>A staff leaning in a corner: foot on the floor, head against the wall.</summary>
    private static GameObject HangStaff(GameObject root, string path, float x)
    {
        GameObject go = AlchemistPack.Spawn(root.transform, path);
        if (go == null) return null;
        StandUpAndFlatten(go);
        AlchemistPack.RotateAboutCentre(go, Quaternion.Euler(-5f, 0f, 0f));

        Bounds b = AlchemistPack.RenderBounds(go);
        go.transform.position += new Vector3(x - b.center.x, -b.min.y, 0.01f - b.min.z);
        return go;
    }

    private static void StandUpAndFlatten(GameObject go)
    {
        Vector3 s = AlchemistPack.RenderBounds(go).size;
        if (s.z >= s.x && s.z >= s.y) AlchemistPack.RotateAboutCentre(go, Quaternion.Euler(-90f, 0f, 0f)); // long axis Z -> Y
        else if (s.x >= s.y) AlchemistPack.RotateAboutCentre(go, Quaternion.Euler(0f, 0f, 90f));            // long axis X -> Y

        // Spin about the (now vertical) long axis to whichever quarter turn is thinnest along Z.
        float best = float.MaxValue;
        int bestK = 0;
        for (int k = 0; k < 4; k++)
        {
            float z = AlchemistPack.RenderBounds(go).size.z;
            if (z < best - 0.0001f) { best = z; bestK = k; }
            AlchemistPack.RotateAboutCentre(go, Quaternion.Euler(0f, 90f, 0f));
        }
        for (int k = 0; k < bestK; k++) AlchemistPack.RotateAboutCentre(go, Quaternion.Euler(0f, 90f, 0f));
    }

    /// <summary>A dagger stuck point-first into the board, handle out.</summary>
    private static void HangDagger(GameObject root, GameObject board)
    {
        if (board == null) return;
        GameObject dagger = AlchemistPack.Spawn(root.transform, "Items/Weapons/Dagger01");
        if (dagger == null) return;

        Bounds bb = AlchemistPack.RenderBounds(board);
        Bounds b = AlchemistPack.RenderBounds(dagger);
        // Long axis is Z by default; the blade tip is assumed to point -Z. Sunk 6 cm into the board's face.
        Vector3 target = new Vector3(bb.center.x + 0.35f, bb.center.y - 0.1f, bb.max.z - 0.06f);
        dagger.transform.position += new Vector3(target.x - b.center.x, target.y - b.center.y, target.z - b.min.z);
    }

    // ---------------------------------------------------------------- public build / load

    /// <summary>
    /// Builds (or loads, when the prefab already exists - hand edits survive, same rule as the rest of the
    /// builder) every Alchemist PropPiece in `sets`. The folder is created on demand.
    /// </summary>
    internal static PropPiece[] BuildAlchemistProps(AlchemistSet sets)
    {
        if (_alchemistProps == null) _alchemistProps = new Dictionary<string, PropPiece>();

        List<PropPiece> result = new List<PropPiece>();
        foreach (AlchDef def in AlchemistDefs())
        {
            if ((def.Set & sets) == 0) continue;
            if (_alchemistProps.TryGetValue(def.Name, out PropPiece cached) && cached != null) { result.Add(cached); continue; }

            PropPiece prop = LoadOrBuildAlchemist(def);
            if (prop == null) continue;
            _alchemistProps[def.Name] = prop;
            result.Add(prop);
        }
        return result.ToArray();
    }

    private static PropPiece LoadOrBuildAlchemist(AlchDef def)
    {
        string path = $"{AlchemistFolder}/{def.Name}.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null && !_overwriteExisting) return existing.GetComponent<PropPiece>();

        GameObject root = def.Compose();
        if (root == null) return null;
        root.name = def.Name;

        if (def.RootBox)
        {
            Bounds b = AlchemistPack.RenderBounds(root);
            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = b.size;
        }

        PropPiece prop = root.AddComponent<PropPiece>();
        SerializedObject so = new SerializedObject(prop);
        so.FindProperty("mount").enumValueIndex = (int)def.Mount;
        so.FindProperty("depth").floatValue = def.Depth;
        so.FindProperty("weight").floatValue = def.Weight;
        so.FindProperty("enabledInMaze").boolValue = true;
        if (def.Mount == PropPiece.MountKind.FloorDecal)
        {
            so.FindProperty("decalSizeRange").vector2Value = new Vector2(2.8f, 3.4f);
            so.FindProperty("randomRoll").boolValue = true;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        EnsureFolder(AlchemistFolder);
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return saved.GetComponent<PropPiece>();
    }

    /// <summary>
    /// No-dialog entry point for unity-mcp / scripts: builds (or loads) every Alchemist prefab and returns
    /// how many there are. With overwrite, every prefab is recomposed from the current layouts and saved
    /// over its existing file (the one deliberate exception to "hand edits survive"). It overwrites in
    /// place rather than deleting first, so the prefabs keep their GUIDs and the gallery / KitMazeTheme
    /// references to them stay valid.
    /// </summary>
    public static int BuildAllAlchemistProps(bool overwrite = false)
    {
        _alchemistProps = null;
        _overwriteExisting = overwrite;
        try
        {
            return BuildAlchemistProps(AlchemistSet.All).Length;
        }
        finally
        {
            _overwriteExisting = false;
        }
    }

    private static bool _overwriteExisting;

    /// <summary>Floor and FloorDecal kinds only - the kit maze never places wall or ceiling props (kit mode forces those chances to 0).</summary>
    internal static PropPiece[] KitPropFilter(PropPiece[] props)
    {
        List<PropPiece> kept = new List<PropPiece>();
        foreach (PropPiece p in props)
        {
            if (p != null && (p.Mount == PropPiece.MountKind.Floor || p.Mount == PropPiece.MountKind.FloorDecal)) kept.Add(p);
        }
        return kept.ToArray();
    }
}

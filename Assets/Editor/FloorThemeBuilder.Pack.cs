using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F85 pack dressing: the unused corners of the art packs as maze dressing (plannings/pack-dressing-plan.md).
/// Kept in its own partial file like FloorThemeBuilder.Alchemist.cs. Two halves:
///   - FurnitureDefs: the Alchemist House's big furniture as standing/hung wall props and a floor cluster. They are
///     AlchDefs, so they ride the F79 build/load/cache path and the AlchemistSet flags in ThemeSets.
///   - PackDef (DeadBody LITE + Socket Pack): composed set pieces (beds, tubs), a standing shroud wall prop, hanging
///     bodies as ceiling props and the Socket Pack's vents / sockets / fuse boxes as hung wall props. Prefabs land in
///     Themes/Common/Prefabs/PackDressing; look-alike models are children behind a ModelVariants (picked by a hash of
///     the cell at runtime, never an rng draw).
/// The full-rebuild builders include these natively (BuildPackProps / BuildPackSetPieces / KitSetPieces);
/// PackDressing.Apply appends them to an existing scene's gallery rows and KitMazeTheme without rebuilding them.
/// </summary>
public static partial class FloorThemeBuilder
{
    internal const string PackFolder = CommonRoot + "/Prefabs/PackDressing";
    private const string DeadBodyRoot = "Assets/DeadBody LITE/Prefabs/";
    private const string SocketRoot = "Assets/Socket Pack/Prefabs/";

    // Theme bits for PackDef.Themes: one per gallery row (Ward, Boiler Deck, Crypt, Lab, Hollow) plus the kit maze.
    internal const int PackWard = 1, PackBoiler = 2, PackCrypt = 4, PackLab = 8, PackHollow = 16, PackKit = 32;

    internal static int PackBitForRow(int rowIndex) => 1 << rowIndex;

    /// <summary>The ceiling height the hanging bodies are built for (floor 3, the lowest); HangingBodyFit re-cuts the rope at spawn for floor 5 (floors 3 and 5 share the Crypt row, 3.2 m and 4.0 m).</summary>
    private const float CryptCeiling = 3.2f;
    /// <summary>Hanging feet this far off the floor: head height, so the player walks into a body, not under it.</summary>
    private const float HangFeetClearance = 0.4f;

    private enum PackKind { Set, Wall, Ceiling }

    private struct PackBuild
    {
        public GameObject Root;
        public float Width, Depth;
    }

    private sealed class PackDef
    {
        public string Name;
        public int Themes;
        public PackKind Kind;
        public float Weight = 1f;
        /// <summary>PropPiece/SetPiece.enabledInMaze. False = built and shown in the gallery row, never spawned (Pack_StandingShroud, 7 Oct 2026: reads as the hunter in the dark; kept for a future idea).</summary>
        public bool Enabled = true;
        public bool AllowLowHang;
        public string RequiredFolder;
        public Func<PackBuild> Compose;
    }

    private static Dictionary<string, GameObject> _packPrefabs;
    private static bool _overwritePack;

    // ---------------------------------------------------------------- Alchemist furniture

    private static List<AlchDef> FurnitureDefs()
    {
        const string F = "Furniture/", Bt = "Items/Bottles/", O = "Items/Organic/";
        const PropPiece.MountKind Wall = PropPiece.MountKind.Wall;

        AlchDef Stand(string name, AlchemistSet set, string path, float weight, bool flip = false)
        {
            return new AlchDef { Name = name, Set = set, Mount = Wall, Depth = 0.7f, Weight = weight, RootBox = true, Compose = () => ComposeFurniture(name, 0.68f, path, flip) };
        }

        return new List<AlchDef>
        {
            Stand("Alch_Cupboard1", AlchemistSet.Cabinets, F + "WoodCupboard01", 0.7f),
            Stand("Alch_Cupboard2", AlchemistSet.Cabinets, F + "WoodCupboard02", 0.7f),
            Stand("Alch_Cupboard3", AlchemistSet.Cabinets, F + "WoodCupboard03", 0.7f, true),
            Stand("Alch_Drawer1", AlchemistSet.Cabinets, F + "Drawer01", 0.6f),
            Stand("Alch_Drawer2", AlchemistSet.Cabinets, F + "Drawer02", 0.6f),
            Stand("Alch_Drawer3", AlchemistSet.Cabinets, F + "Drawer03", 0.6f),
            Stand("Alch_WoodDrawer", AlchemistSet.Cabinets, F + "WoodDrawer01", 0.6f, true),
            Stand("Alch_LongTable", AlchemistSet.Tables, F + "Table01", 0.6f),
            Stand("Alch_Workbench", AlchemistSet.Workshop, F + "Workbench", 0.7f),
            Stand("Alch_Sink", AlchemistSet.Sink, F + "TableSink", 0.6f),
            Stand("Alch_Stove", AlchemistSet.Stove, F + "Stove", 0.6f),
            // Alembic02 (0.81 m deep, 18 % shrink to fit) is left out: past the 15 % rule.
            new AlchDef
            {
                Name = "Alch_WoodShelf", Set = AlchemistSet.Shelves, Mount = Wall, Depth = 0.36f, Weight = 0.7f,
                Compose = () =>
                {
                    GameObject root = new GameObject("Alch_WoodShelf");
                    GameObject shelf = HangFlat(root, F + "WoodShelf01", 0f, 1.35f, 0f);
                    if (shelf != null)
                    {
                        float top = AlchemistPack.RenderBounds(shelf).max.y;
                        AlchemistPack.Place(root.transform, Bt + "Bottle02", new Vector3(-0.55f, top, 0.17f), Quaternion.identity);
                        AlchemistPack.Place(root.transform, Bt + "Bottle04", new Vector3(0.05f, top, 0.15f), Quaternion.Euler(0f, 40f, 0f));
                        AlchemistPack.Place(root.transform, Bt + "Flask01", new Vector3(0.5f, top, 0.17f), Quaternion.identity);
                    }
                    return root;
                }
            },
            new AlchDef
            {
                Name = "Alch_Babies", Set = AlchemistSet.Nursery, Mount = PropPiece.MountKind.Floor, Depth = 0.4f, Weight = 0.6f,
                Compose = () => Compose("Alch_Babies", 0.4f, 0.35f,
                    new[]
                    {
                        P(O + "Baby01", -0.42f, 0.2f, 25f, 90f), P(O + "Baby02", 0f, 0.26f, -40f, 90f), P(O + "Baby03", 0.42f, 0.2f, 70f, 90f)
                    })
            },
        };
    }

    /// <summary>
    /// One piece of big furniture, turned so its shallower floor side runs into the corridor, bottom-centre on the
    /// origin. Unlike Compose it may shrink the piece by at most 15 % to fit the depth budget; anything deeper is
    /// left out (null, logged) rather than squashed.
    /// </summary>
    private static GameObject ComposeFurniture(string name, float depth, string path, bool flipFront)
    {
        GameObject root = new GameObject(name);
        GameObject go = AlchemistPack.Spawn(root.transform, path);
        if (go == null) { UnityEngine.Object.DestroyImmediate(root); return null; }

        Vector3 s = AlchemistPack.RenderBounds(go).size;
        float turn = s.x < s.z ? 90f : 0f;
        if (flipFront) turn += 180f;
        go.transform.rotation = Quaternion.Euler(0f, turn, 0f) * go.transform.rotation;

        Bounds b = AlchemistPack.RenderBounds(go);
        go.transform.position += -new Vector3(b.center.x, b.min.y, b.center.z);

        float fit = (depth - 0.02f) / Mathf.Max(0.0001f, b.size.z);
        if (fit < 0.85f)
        {
            Debug.Log($"FloorThemeBuilder: {name} left out - it is {b.size.z:0.00} m deep and would need shrinking to {fit:P0} to fit a {depth:0.00} m wall prop.");
            UnityEngine.Object.DestroyImmediate(root);
            return null;
        }

        Settle(root, depth, 0f);
        return root;
    }

    // ---------------------------------------------------------------- DeadBody LITE + Socket Pack

    private static List<PackDef> PackDefs()
    {
        string[] bedBodies = { "Bed_&_Body/Bed_Body (1)", "Bed_&_Body/Bed_Body (2)", "Bed_&_Body/Bed_Body (3)", "Bed_&_Body/Bed_Body (4)", "Bed_&_Body/Bed_Body (5)", "Bed_&_Body/Bed_Body (6)" };
        string[] beds = { "Bed_Only/Bed_Rusty (1)", "Bed_Only/Bed_Rusty (2)", "Bed_Only/Bed_Rusty (3)", "Bed_Only/Bed_Rusty (4)", "Bed_Only/Bed_Rusty (5)" };
        string[] tubs = { "Bath_Tub/Bathtub_Blooded", "Bath_Tub/Bathtub With Bodies (2)", "Bath_Tub/Bathtub With Bodies (3)" };
        string[] stands = { "Hanging_Body/Standing/Deadbody_Stand (1)", "Hanging_Body/Standing/Deadbody_Stand (2)", "Hanging_Body/Standing/Deadbody_Stand (3)" };
        string[] hangs =
        {
            "Hanging_Body/Hanging 1/Deadbody_Hanging", "Hanging_Body/Hanging 1/Deadbody_Hanging (1)", "Hanging_Body/Hanging 1/Deadbody_Hanging (2)", "Hanging_Body/Hanging 1/Deadbody_Hanging (3)",
            "Hanging_Body/Hanging 2/Deadbody_HangingS2", "Hanging_Body/Hanging 2/Deadbody_HangingS2 (1)", "Hanging_Body/Hanging 2/Deadbody_HangingS2 (2)", "Hanging_Body/Hanging 2/Deadbody_HangingS2 (3)"
        };
        string[] vents = { "Air_Vent_01", "Air_Vent_02", "Air_Vent_03", "Air_Vent_04", "Air_Vent_05" };
        string[] sockets = { "Socket_01", "Socket_02", "Socket_03", "Socket_04", "Socket_05", "Socket_06" };
        string[] fuses = { "Fuse_Box_02", "Fuse_Box_03" };

        const string dead = "Assets/DeadBody LITE", socket = "Assets/Socket Pack";

        return new List<PackDef>
        {
            new PackDef { Name = "Pack_MorgueBed", Themes = PackWard | PackLab | PackKit, Kind = PackKind.Set, Weight = 1f, RequiredFolder = dead, Compose = () => ComposeLying("Pack_MorgueBed", DeadBodyRoot, bedBodies, 90f) },
            new PackDef { Name = "Pack_EmptyBed", Themes = PackWard | PackCrypt | PackHollow, Kind = PackKind.Set, Weight = 0.8f, RequiredFolder = dead, Compose = () => ComposeLying("Pack_EmptyBed", DeadBodyRoot, beds, 90f) },
            new PackDef { Name = "Pack_BloodyBath", Themes = PackWard | PackCrypt | PackHollow | PackKit, Kind = PackKind.Set, Weight = 0.8f, RequiredFolder = dead, Compose = () => ComposeLying("Pack_BloodyBath", DeadBodyRoot, tubs, 0f) },
            new PackDef { Name = "Pack_StandingShroud", Themes = PackCrypt | PackHollow, Kind = PackKind.Wall, Weight = 0.5f, Enabled = false, RequiredFolder = dead, Compose = () => ComposeStanding("Pack_StandingShroud", stands) },
            new PackDef { Name = "Pack_HangingBody", Themes = PackCrypt | PackHollow, Kind = PackKind.Ceiling, Weight = 1f, AllowLowHang = true, RequiredFolder = dead, Compose = () => ComposeHanging("Pack_HangingBody", hangs, CryptCeiling) },
            new PackDef { Name = "Pack_AirVent", Themes = PackWard | PackBoiler | PackLab, Kind = PackKind.Wall, Weight = 0.7f, RequiredFolder = socket, Compose = () => ComposeHungModels("Pack_AirVent", SocketRoot, vents, 2.6f, 2f, false) },
            new PackDef { Name = "Pack_Socket", Themes = PackWard | PackBoiler | PackLab, Kind = PackKind.Wall, Weight = 0.6f, RequiredFolder = socket, Compose = () => ComposeHungModels("Pack_Socket", SocketRoot, sockets, 0.45f, 2.5f, false) },
            new PackDef { Name = "Pack_FuseBox", Themes = PackBoiler | PackLab, Kind = PackKind.Wall, Weight = 0.5f, RequiredFolder = socket, Compose = () => ComposeHungModels("Pack_FuseBox", SocketRoot, fuses, 1.4f, 2f, true) },
        };
    }

    private static bool PackAvailable(PackDef def) => AssetDatabase.IsValidFolder(def.RequiredFolder);

    // ---- public build / load ----

    /// <summary>Wall and ceiling PropPieces of the DeadBody LITE / Socket Pack set for one theme bit (PackWard ... PackKit). Built once, loaded afterwards.</summary>
    internal static PropPiece[] BuildPackProps(int themeBit)
    {
        List<PropPiece> result = new List<PropPiece>();
        foreach (GameObject prefab in BuildPackPrefabs(themeBit, set: false))
        {
            PropPiece prop = prefab.GetComponent<PropPiece>();
            if (prop != null) result.Add(prop);
        }
        return result.ToArray();
    }

    /// <summary>SetPieces of the DeadBody LITE set for one theme bit.</summary>
    internal static SetPiece[] BuildPackSetPieces(int themeBit)
    {
        List<SetPiece> result = new List<SetPiece>();
        foreach (GameObject prefab in BuildPackPrefabs(themeBit, set: true))
        {
            SetPiece piece = prefab.GetComponent<SetPiece>();
            if (piece != null) result.Add(piece);
        }
        return result.ToArray();
    }

    /// <summary>The common set pieces the kit maze lists: its FallenRunner plus the pack's morgue bed and bath.</summary>
    internal static SetPiece[] KitSetPieces(SetPiece fallenRunner)
    {
        List<SetPiece> list = new List<SetPiece> { fallenRunner };
        list.AddRange(BuildPackSetPieces(PackKit));
        return list.ToArray();
    }

    /// <summary>Names of every F85 prefab a theme (or the kit) should list, for PackDressing.NeedsUpgrade. Only pieces whose pack is in the project.</summary>
    internal static List<string> ExpectedPackNames(int themeBit, bool sets)
    {
        List<string> names = new List<string>();
        foreach (PackDef def in PackDefs())
        {
            if ((def.Themes & themeBit) == 0 || !PackAvailable(def) || (def.Kind == PackKind.Set) != sets) continue;
            names.Add(def.Name);
        }
        if (!sets && themeBit != PackKit)
        {
            int row = 0;
            while ((1 << row) != themeBit) row++;
            AlchemistSet wanted = ThemeSets[row] & AlchemistSet.FurnitureAll;
            foreach (AlchDef def in AlchemistDefs())
            {
                if ((def.Set & wanted) != 0 && AssetDatabase.IsValidFolder("Assets/BK_AlchemistHouse")) names.Add(def.Name);
            }
        }
        return names;
    }

    private static List<GameObject> BuildPackPrefabs(int themeBit, bool set)
    {
        if (_packPrefabs == null) _packPrefabs = new Dictionary<string, GameObject>();

        List<GameObject> result = new List<GameObject>();
        foreach (PackDef def in PackDefs())
        {
            if ((def.Themes & themeBit) == 0 || (def.Kind == PackKind.Set) != set || !PackAvailable(def)) continue;
            if (_packPrefabs.TryGetValue(def.Name, out GameObject cached) && cached != null) { result.Add(cached); continue; }

            GameObject prefab = LoadOrBuildPack(def);
            if (prefab == null) continue;
            _packPrefabs[def.Name] = prefab;
            result.Add(prefab);
        }
        return result;
    }

    private static GameObject LoadOrBuildPack(PackDef def)
    {
        string path = $"{PackFolder}/{def.Name}.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null && !_overwritePack) return existing;

        PackBuild build = def.Compose();
        if (build.Root == null) return null;
        GameObject root = build.Root;
        root.name = def.Name;

        if (def.Kind == PackKind.Set)
        {
            SetPiece piece = root.AddComponent<SetPiece>();
            SerializedObject so = new SerializedObject(piece);
            so.FindProperty("width").floatValue = build.Width;
            so.FindProperty("depth").floatValue = build.Depth;
            so.FindProperty("weight").floatValue = def.Weight;
            so.FindProperty("enabledInMaze").boolValue = def.Enabled;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            PropPiece prop = root.AddComponent<PropPiece>();
            SerializedObject so = new SerializedObject(prop);
            so.FindProperty("mount").enumValueIndex = (int)(def.Kind == PackKind.Ceiling ? PropPiece.MountKind.Ceiling : PropPiece.MountKind.Wall);
            so.FindProperty("depth").floatValue = build.Depth;
            so.FindProperty("weight").floatValue = def.Weight;
            so.FindProperty("enabledInMaze").boolValue = def.Enabled;
            so.FindProperty("allowLowHang").boolValue = def.AllowLowHang;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        EnsureFolder(PackFolder);
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path); // in place when it exists, so GUIDs (and scene references) survive
        UnityEngine.Object.DestroyImmediate(root);
        return saved;
    }

    /// <summary>
    /// No-dialog entry point for unity-mcp / scripts: builds (or loads) every pack prefab. With overwrite each is
    /// recomposed and saved over its file (GUIDs kept), the one exception to "hand edits survive".
    /// </summary>
    public static int BuildAllPackDressing(bool overwrite = false)
    {
        _packPrefabs = null;
        _alchemistProps = null;
        _overwritePack = overwrite;
        _overwriteExisting = overwrite;
        try
        {
            int count = BuildAlchemistProps(AlchemistSet.FurnitureAll).Length;
            for (int bit = PackWard; bit <= PackKit; bit <<= 1)
            {
                count += BuildPackProps(bit).Length + BuildPackSetPieces(bit).Length;
            }
            return count;
        }
        finally
        {
            _overwritePack = false;
            _overwriteExisting = false;
        }
    }

    // ---------------------------------------------------------------- spawning + pack quirks

    /// <summary>
    /// A pack prefab instance, unpacked and stripped for maze dressing: the pack's ExecuteInEditMode scripts
    /// (BathTub, DeadBodyHanging) are applied once and removed, then rigidbodies / colliders / lights go and any
    /// Built-in material is swapped for a URP copy. Null (logged) if the asset is missing.
    /// </summary>
    private static GameObject PackSpawn(Transform parent, string assetPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null)
        {
            Debug.LogError($"FloorThemeBuilder: pack prefab not found at {assetPath}.");
            return null;
        }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        foreach (MonoBehaviour script in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (script == null) continue;
            string type = script.GetType().Name;
            if (type == "BathTub") ApplyBathTubState(script);
            if (type == "BathTub" || type == "DeadBodyHanging") UnityEngine.Object.DestroyImmediate(script);
        }

        AlchemistPack.Strip(go, true);
        PackMaterials.ConvertHierarchy(go);
        return go;
    }

    /// <summary>The pack's BathTub script switches the Blood and Body_n children from its serialized flags every frame; do it once, here.</summary>
    private static void ApplyBathTubState(MonoBehaviour tub)
    {
        SerializedObject so = new SerializedObject(tub);
        void Toggle(string flag, string target)
        {
            SerializedProperty f = so.FindProperty(flag);
            SerializedProperty t = so.FindProperty(target);
            GameObject go = t != null ? t.objectReferenceValue as GameObject : null;
            if (f != null && go != null) go.SetActive(f.boolValue);
        }
        Toggle("Blood", "BloodObj");
        Toggle("DeadBody_1", "Body_1");
        Toggle("DeadBody_2", "Body_2");
        Toggle("DeadBody_3", "Body_3");
    }

    private static void AttachVariants(GameObject root, List<GameObject> options)
    {
        ModelVariants variants = root.AddComponent<ModelVariants>();
        SerializedObject so = new SerializedObject(variants);
        SerializedProperty prop = so.FindProperty("options");
        prop.arraySize = options.Count;
        for (int i = 0; i < options.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = options[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        for (int i = 0; i < options.Count; i++) options[i].SetActive(i == 0);
    }

    private static void FitBox(GameObject holder, Bounds worldBounds, float shrink = 0f)
    {
        BoxCollider box = holder.AddComponent<BoxCollider>();
        box.center = worldBounds.center; // holders sit at the world origin
        box.size = worldBounds.size - new Vector3(shrink, 0f, shrink);
    }

    // ---- lying pieces: beds and tubs, the SetPiece shape (pivot on the floor at the wall face, +Z into the corridor) ----

    /// <summary>
    /// One variant per model under a holder, each turned by yaw (a bed lies along Z, so 90 puts its long side along the
    /// wall), bottom-centred on x = 0 and pushed back so its wall side sits 3 cm off the wall plane, with one box
    /// collider fitted to its renderer bounds. Width/Depth are the largest variant's footprint.
    /// </summary>
    private static PackBuild ComposeLying(string name, string folder, string[] models, float yaw)
    {
        GameObject root = new GameObject(name);
        List<GameObject> options = new List<GameObject>();
        float width = 0f, depth = 0f;

        for (int i = 0; i < models.Length; i++)
        {
            GameObject holder = new GameObject($"Variant{i + 1}");
            holder.transform.SetParent(root.transform, false);
            GameObject go = PackSpawn(holder.transform, $"{folder}{models[i]}.prefab");
            if (go == null) { UnityEngine.Object.DestroyImmediate(holder); continue; }

            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
            Bounds b = AlchemistPack.RenderBounds(go);
            go.transform.position += new Vector3(-b.center.x, -b.min.y, 0.03f - b.min.z);

            b = AlchemistPack.RenderBounds(go);
            FitBox(holder, b);
            width = Mathf.Max(width, b.size.x);
            depth = Mathf.Max(depth, b.max.z);
            options.Add(holder);
        }

        if (options.Count == 0) { UnityEngine.Object.DestroyImmediate(root); return default; }
        AttachVariants(root, options);
        return new PackBuild { Root = root, Width = width + 0.1f, Depth = depth };
    }

    // ---- standing shroud: a wall prop (pivot on the floor at the wall face) ----

    private static PackBuild ComposeStanding(string name, string[] models)
    {
        GameObject root = new GameObject(name);
        List<GameObject> options = new List<GameObject>();
        float depth = 0f;

        for (int i = 0; i < models.Length; i++)
        {
            GameObject holder = new GameObject($"Variant{i + 1}");
            holder.transform.SetParent(root.transform, false);
            GameObject go = PackSpawn(holder.transform, $"{DeadBodyRoot}{models[i]}.prefab");
            if (go == null) { UnityEngine.Object.DestroyImmediate(holder); continue; }

            Bounds b = AlchemistPack.RenderBounds(go);
            go.transform.position += new Vector3(-b.center.x, -b.min.y, 0.03f - b.min.z);

            b = AlchemistPack.RenderBounds(go);
            FitBox(holder, b);
            depth = Mathf.Max(depth, b.max.z);
            options.Add(holder);
        }

        if (options.Count == 0) { UnityEngine.Object.DestroyImmediate(root); return default; }
        AttachVariants(root, options);
        return new PackBuild { Root = root, Depth = depth };
    }

    // ---- hanging bodies: a ceiling prop (pivot at the ceiling underside, hanging along -Y) ----

    /// <summary>
    /// The pack's DeadBodyHanging script just sets its rope bone's local Y; here that is solved once so the rope's top
    /// reaches the ceiling underside (y = 0, tucked 5 cm in) while the feet hang HangFeetClearance off the floor of a
    /// room `ceiling` m tall. The rope top is read from a baked skinned mesh at two rope lengths (it is linear in the
    /// length). Skinned renderers update off-screen, since their stored bounds know nothing of the stretched rope.
    /// </summary>
    private static PackBuild ComposeHanging(string name, string[] models, float ceiling)
    {
        GameObject root = new GameObject(name);
        List<GameObject> options = new List<GameObject>();
        float depth = ceiling - HangFeetClearance; // ceiling underside to the soles

        for (int i = 0; i < models.Length; i++)
        {
            GameObject holder = new GameObject($"Variant{i + 1}");
            holder.transform.SetParent(root.transform, false);
            GameObject go = PackSpawn(holder.transform, $"{DeadBodyRoot}{models[i]}.prefab");
            Transform rope = go != null ? FindDeep(go.transform, "Rope_Length") : null;
            if (go == null || rope == null)
            {
                Debug.LogError($"FloorThemeBuilder: {models[i]} has no Rope_Length bone - skipped.");
                UnityEngine.Object.DestroyImmediate(holder);
                continue;
            }

            float l1 = 1.5f, l2 = 2.5f;
            rope.localPosition = new Vector3(0f, l1, 0f);
            Bounds b1 = BakedBounds(go);
            rope.localPosition = new Vector3(0f, l2, 0f);
            Bounds b2 = BakedBounds(go);
            float slope = (b2.max.y - b1.max.y) / (l2 - l1);
            float feet = b1.min.y;
            float length = l1 + (feet + depth - b1.max.y) / Mathf.Max(0.01f, slope);
            rope.localPosition = new Vector3(0f, length, 0f);

            Bounds b = BakedBounds(go);
            go.transform.position += new Vector3(0f, 0.05f - b.max.y, 0f);
            foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;

            // Only the body is solid (the cone and head, not the rope above or the thread-thin tail below).
            b = BakedBounds(go);
            BoxCollider box = holder.AddComponent<BoxCollider>();
            box.center = new Vector3(b.center.x, b.min.y + 1.1f, b.center.z);
            box.size = new Vector3(0.55f, 1.3f, Mathf.Max(0.5f, b.size.z * 0.8f));

            HangingBodyFit fit = holder.AddComponent<HangingBodyFit>();
            SerializedObject fitSo = new SerializedObject(fit);
            fitSo.FindProperty("rope").objectReferenceValue = rope;
            fitSo.FindProperty("body").objectReferenceValue = go.transform;
            fitSo.FindProperty("box").objectReferenceValue = box;
            fitSo.FindProperty("ropeLength0").floatValue = length;
            fitSo.FindProperty("bodyY0").floatValue = go.transform.localPosition.y;
            fitSo.FindProperty("boxY0").floatValue = box.center.y;
            fitSo.FindProperty("slope").floatValue = slope;
            fitSo.FindProperty("depth0").floatValue = depth;
            fitSo.FindProperty("feetClearance").floatValue = HangFeetClearance;
            fitSo.ApplyModifiedPropertiesWithoutUndo();
            options.Add(holder);
        }

        if (options.Count == 0) { UnityEngine.Object.DestroyImmediate(root); return default; }
        AttachVariants(root, options);
        return new PackBuild { Root = root, Depth = depth };
    }

    private static Bounds BakedBounds(GameObject go)
    {
        bool any = false;
        Bounds result = new Bounds(go.transform.position, Vector3.zero);
        foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            UnityEngine.Mesh baked = new UnityEngine.Mesh();
            smr.BakeMesh(baked);
            foreach (Vector3 v in baked.vertices)
            {
                Vector3 w = smr.transform.TransformPoint(v);
                if (!any) { result = new Bounds(w, Vector3.zero); any = true; }
                else result.Encapsulate(w);
            }
            UnityEngine.Object.DestroyImmediate(baked);
        }
        return result;
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform child in t)
        {
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    // ---- hung Socket Pack models: a wall prop with no collider ----

    /// <summary>
    /// Vents, sockets and fuse boxes: each model scaled up (the pack is life-size and tiny from down a corridor),
    /// thin axis turned to Z with its back 1 cm off the wall, centred at `height` on x = 0. A fuse box's glass door
    /// is hidden (its shader is not URP), same as the interactable fuse box.
    /// </summary>
    private static PackBuild ComposeHungModels(string name, string folder, string[] models, float height, float scale, bool hideDoor)
    {
        GameObject root = new GameObject(name);
        List<GameObject> options = new List<GameObject>();
        float depth = 0f;

        for (int i = 0; i < models.Length; i++)
        {
            GameObject holder = new GameObject($"Variant{i + 1}");
            holder.transform.SetParent(root.transform, false);
            GameObject go = PackSpawn(holder.transform, $"{folder}{models[i]}.prefab");
            if (go == null) { UnityEngine.Object.DestroyImmediate(holder); continue; }

            go.transform.localScale *= scale;
            if (hideDoor)
            {
                Transform door = FindDeep(go.transform, "Fuse_Box_Door");
                if (door != null) door.gameObject.SetActive(false);
            }

            Bounds b = AlchemistPack.RenderBounds(go);
            if (b.size.x < b.size.z) go.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * go.transform.rotation;
            b = AlchemistPack.RenderBounds(go);
            go.transform.position += new Vector3(-b.center.x, height - b.center.y, 0.01f - b.min.z);

            depth = Mathf.Max(depth, AlchemistPack.RenderBounds(go).max.z);
            options.Add(holder);
        }

        if (options.Count == 0) { UnityEngine.Object.DestroyImmediate(root); return default; }
        AttachVariants(root, options);
        return new PackBuild { Root = root, Depth = depth };
    }
}

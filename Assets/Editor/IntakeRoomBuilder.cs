using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LIGHTS OUT > Build > Intake Room. Generates the F86 tutorial room as ordinary scene objects, once, so it can be
/// inspected and edited in Unity like the shop. Nothing here runs in a build; at Play time the IntakeRoom component
/// drives what this made.
///
/// Layout (room-local, +Z north, floor top y = 0): A the admissions bay (7 x 7: the bed, the dying lamp, the sign, the
/// bell, the shutter), B the holding room beyond the shutter (the locker, a side doorway onto a dark corridor E where
/// the silhouette walks), C the star room (the star and the trapdoor). Shell and zones are primitives; the dressing is
/// the Ward's own pieces (the DeadBody LITE beds, Alchemist furniture) and the live InteractableKit templates.
///
/// Sits at (-100, 5, -100): clear of the maze (x -70..-4, z -70..-26), the shop (-92, 5, -45), the FloorThemes
/// gallery (-70, 5, -6) and the KitMazeTheme (-70, 5, 10), and outside the maze's NavMeshSurface volume.
/// Rebuilding replaces the existing room. Spec: plannings/intake-and-contracts-plan.md.
/// </summary>
public static partial class IntakeRoomBuilder
{
    private const string RoomName = "IntakeRoom";
    private const string MaterialFolder = "Assets/SourceFiles/Materials/Intake";
    private const string AdamModel = "Assets/UnityTechnologies/Adam Character Pack/Adam/Adam.FBX";
    private const string AdamController = "Assets/UnityTechnologies/Adam Character Pack/Adam/Adam_AnimationController.controller";
    private const string PropFolder = "Assets/SourceFiles/Themes/Common/Prefabs/";

    private static readonly Vector3 RoomPosition = new Vector3(-100f, 5f, -100f);
    private const float Height = 3f;
    private const float Wall = 0.2f;

    private static readonly Color Amber = new Color(1f, 0.72f, 0.42f);
    private static readonly Color Warm = new Color(1f, 0.84f, 0.66f);
    private static readonly Color Red = new Color(0.9f, 0.15f, 0.1f);

    private struct Mats
    {
        public Material Wall, Floor, Ceiling, Metal, Brass, Marker, Lamp, Black, Eyes, Arrow;
    }

    private static Transform _root;

    /// <summary>Null when the scene has the IntakeRoom prefab with every part the component needs. Used by ProjectSetup.</summary>
    public static string Problem()
    {
        IntakeRoom room = Object.FindAnyObjectByType<IntakeRoom>(FindObjectsInactive.Include);
        if (room == null)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null
                ? "missing from the scene (Build Missing Pieces places the saved prefab)"
                : "missing (START GAME falls back to the old rules screen)";
        }
        if (!room.IsComplete) return "incomplete";
        if (!PrefabUtility.IsPartOfPrefabInstance(room.gameObject)) return $"not a prefab yet (Build Missing Pieces saves it as {PrefabPath})";
        return null;
    }

    /// <summary>The layout checks (light budget, clear walking paths, nothing through a wall) on the room as it is now - hand edits included. Report only.</summary>
    public static string LayoutProblem()
    {
        IntakeRoom room = Object.FindAnyObjectByType<IntakeRoom>(FindObjectsInactive.Include);
        return room == null ? null : AuditProblems(room.transform);
    }

    /// <summary>ProjectSetup's fix: place the saved prefab when the room is missing, or save a plain scene room as the prefab.</summary>
    public static void Repair()
    {
        IntakeRoom room = Object.FindAnyObjectByType<IntakeRoom>(FindObjectsInactive.Include);
        if (room == null) BuildSilently();
        else if (!PrefabUtility.IsPartOfPrefabInstance(room.gameObject)) SaveAsPrefab(room.gameObject);
    }

    /// <summary>
    /// Generates the room from scratch and saves it over the prefab - a reset, not the way to change the room. To change
    /// it, open Assets/Prefabs/IntakeRoom.prefab and edit it by hand.
    /// </summary>
    [MenuItem("LIGHTS OUT/Build/Intake Room (regenerate)", priority = 105)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Build Intake Room", "Open the game scene first.", "OK");
            return;
        }

        bool hasPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        if ((hasPrefab || Object.FindAnyObjectByType<IntakeRoom>(FindObjectsInactive.Include) != null) &&
            !EditorUtility.DisplayDialog("Regenerate Intake Room",
                $"This throws away the current room and regenerates it from code, overwriting {PrefabPath}.\n\n" +
                "Every change you made to it by hand will be lost. To change the room, edit the prefab instead.",
                "Regenerate", "Cancel"))
        {
            return;
        }

        Generate();
    }

    /// <summary>
    /// No dialogs, never overwrites hand work: when the prefab exists and the room is missing, it places the prefab;
    /// it only generates from code when there is no prefab yet. Used by ProjectSetup and for scripted rebuilds.
    /// </summary>
    public static void BuildSilently()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("Build Intake Room: open the game scene first.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            if (Object.FindAnyObjectByType<IntakeRoom>(FindObjectsInactive.Include) == null)
            {
                InstantiatePrefab();
                EditorSceneManager.MarkSceneDirty(scene);
            }
            return;
        }

        Generate();
    }

    /// <summary>Generates the room from code (replacing any existing one) and saves it as the prefab.</summary>
    private static void Generate()
    {
        Scene scene = SceneManager.GetActiveScene();
        IntakeRoom existing = Object.FindAnyObjectByType<IntakeRoom>(FindObjectsInactive.Include);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
        GameObject stale;
        while ((stale = GameObject.Find(RoomName)) != null) Undo.DestroyObjectImmediate(stale);

        if (AlchemistMaterialUpgrade.NeedsConversion()) AlchemistMaterialUpgrade.Convert();

        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        FloorThemeSet themes = Object.FindAnyObjectByType<FloorThemeSet>(FindObjectsInactive.Include);
        if (kit == null) Debug.LogError("Build Intake Room: no InteractableKit in the scene - the battery, bottle, note and trapdoor will be missing. Run LIGHTS OUT > Build > Interactables first.");
        if (themes == null || themes.ForFloor(1) == null || themes.ForFloor(1).Locker == null)
        {
            Debug.LogError("Build Intake Room: no Floor Themes row with a locker - the hiding step will be missing. Run LIGHTS OUT > Build > Floor Themes first.");
        }

        BuildCore(scene, kit, themes);
    }

    // ---------------------------------------------------------------- build

    private static void BuildCore(Scene scene, InteractableKit kit, FloorThemeSet themes)
    {
        EnsureFolder();
        Mats m = LoadMaterials();

        GameObject rootGo = new GameObject(RoomName);
        Undo.RegisterCreatedObjectUndo(rootGo, "Build Intake Room");
        rootGo.transform.position = RoomPosition;
        _root = rootGo.transform;
        _flickerSigns.Clear();
        _soundPoints.Clear();
        _table1 = _table1Max = _table2 = _table2Max = Vector3.zero;
        _props = Group(_root, "Props");
        _decals = Group(_root, "Decals");

        BuildShell(m);
        BuildFurniture();

        // ---- zones and points
        Transform points = Group(_root, "Points");
        Transform arrival = Point(points, "Arrival", new Vector3(-2.7f, 0f, -2.2f), 0f);
        Transform stand = Point(points, "Stand", new Vector3(-1.45f, 0f, -2.2f), 0f);
        Collider moveZone = Zone(points, "MoveZone", new Vector3(1.0f, 1.25f, -0.6f), new Vector3(1.5f, 2.5f, 1.5f));
        Collider roomBZone = Zone(points, "RoomBZone", new Vector3(0f, 1.25f, 7.9f), new Vector3(4f, 2.5f, 8.2f));
        Collider hatchZone = Zone(points, "HatchZone", new Vector3(0f, 1.25f, 14.8f), new Vector3(1.3f, 2.5f, 1.3f));

        // The floor marker the move step points at.
        GameObject marker = Prim(_root, PrimitiveType.Cylinder, "MoveMarker", new Vector3(1.0f, 0.01f, -0.6f), new Vector3(1.1f, 0.01f, 1.1f), m.Marker);
        Object.DestroyImmediate(marker.GetComponent<Collider>());

        // ---- the player's bed (bed + collider that is off while they lie in it)
        GameObject bed = PlaceProp("PackDressing/Pack_EmptyBed", new Vector3(-2.7f, 0f, -2.2f), 90f);
        Collider bedBlocker = bed != null ? AddBlocker(bed, "BedBlocker") : null;
        if (bedBlocker != null) bedBlocker.enabled = false;
        GameObject bed2 = PlaceProp("PackDressing/Pack_MorgueBed", new Vector3(-2.7f, 0f, 0.6f), 90f);
        if (bed2 != null) AddBlocker(bed2, "BedBlocker2");

        // ---- lights
        Transform lights = Group(_root, "Lights");
        Light mainLamp = PointLight(lights, "MainLamp", new Vector3(0f, 2.55f, 0f), ColdWhite, 9f, 2.2f);
        Light lampB = PointLight(lights, "LampB", new Vector3(0f, 2.55f, 7.8f), Warm, 7f, 1.6f);
        Light lampC = PointLight(lights, "LampC", new Vector3(0f, 2.3f, 16.4f), Amber, 8f, 1.8f);
        Light corridorLamp = PointLight(lights, "CorridorLamp", new Vector3(3.3f, 2.4f, 9f), new Color(0.9f, 0.35f, 0.25f), 6f, 0.9f);
        Renderer mainGlass = BuildCeilingFixture("MainLampFixture", new Vector3(0f, Height, 0f), 0f, GlassMat("Intake_GlassCold", ColdWhite, m.Lamp), m.Lamp);
        BuildCeilingFixture("LampBFixture", new Vector3(0f, Height, 7.8f), 0f, GlassMat("Intake_GlassWarm", Warm, m.Lamp), m.Lamp);
        Renderer corridorGlass = BuildCeilingFixture("CorridorFixture", new Vector3(3.3f, Height, 9f), 90f, GlassMat("Intake_RedGlass", new Color(1f, 0.12f, 0.08f), m.Lamp), m.Lamp);
        Transform hanging;
        Renderer hangingGlow;
        BuildHangingLamp(lampC, new Vector3(0f, Height, 16.4f), m, out hanging, out hangingGlow);

        // ---- sign, bell, shutter
        TextMeshPro sign = BuildSign(_root, "FocusSign", new Vector3(-2.55f, 1.9f, 3.46f), "KEEP QUIET.\nIT HEARS EVERYTHING.", new Color(0.82f, 0.8f, 0.7f), 1.7f);
        IntakeTarget bell = BuildBellKit(kit, m) ?? BuildBell(m);
        Transform shutter = BuildShutterKit(kit, m) ?? BuildShutter(m);

        // ---- the live kit templates
        BatteryCellPickup battery = null;
        ThrowableItem bottle = null;
        LoreNote note = null;
        Transform trapdoor = null;
        Renderer glow = null;
        Transform lidL = null, lidR = null;
        Light hatchLight = PointLight(lights, "HatchLight", new Vector3(0f, 0.9f, 14.8f), new Color(0.25f, 1f, 0.7f), 5f, 1.0f);
        if (kit != null)
        {
            if (kit.BatteryCell != null)
            {
                battery = CloneKit(kit.BatteryCell, "IntakeBattery");
                battery.transform.position = _root.TransformPoint(new Vector3(2.6f, 0.95f, 1.2f));
                if (_table2Max != Vector3.zero) SeatAt(battery.gameObject, _table2Max.y + 0.002f); // stood on the table, not hovering
            }
            if (kit.ThrowableBottle != null)
            {
                bottle = CloneKit(kit.ThrowableBottle, "IntakeBottle");
                bottle.transform.position = _root.TransformPoint(new Vector3(2.5f, 0.95f, -1.9f));
                if (_table1Max != Vector3.zero) SeatAt(bottle.gameObject, _table1Max.y + 0.002f);
            }
            if (kit.LoreNote != null)
            {
                note = CloneKit(kit.LoreNote, "IntakeNote");
                note.transform.position = _root.TransformPoint(new Vector3(3.47f, 1.45f, -0.35f));
                note.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up); // +Z points into the room, as the maze's notes do
            }
            if (kit.EscapeHatch != null)
            {
                GameObject t = Object.Instantiate(kit.EscapeHatch, _root);
                t.name = "Trapdoor";
                t.SetActive(true);
                t.transform.localPosition = new Vector3(0f, 0f, 14.8f);
                t.transform.localRotation = Quaternion.identity;
                trapdoor = t.transform;
                lidL = t.transform.Find("LidL");
                lidR = t.transform.Find("LidR");
                Transform g = t.transform.Find("Glow");
                if (g != null) glow = g.GetComponent<Renderer>();
                MakeMovable(t.transform);
            }
        }
        if (trapdoor == null) Debug.LogWarning("Build Intake Room: no escape hatch template, so the Intake hatch is missing.");

        // ---- the star (a look-alike: never a Pickup, GameManager counts those at Start)
        Transform star = BuildStar(m);

        // ---- the locker and the silhouette
        Locker locker = null;
        FloorTheme ward = themes != null ? themes.ForFloor(1) : null;
        if (ward != null && ward.Locker != null)
        {
            locker = Object.Instantiate(ward.Locker, _root);
            locker.gameObject.SetActive(true);
            locker.name = "IntakeLocker";
            locker.transform.localPosition = new Vector3(-2.0f, 0f, 9.0f);
            locker.transform.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
        }

        Transform routeGroup = Group(_root, "Route");
        Transform r0 = Point(routeGroup, "Start", new Vector3(3.3f, 0f, 5.4f), 0f);
        Transform r1 = Point(routeGroup, "Doorway", new Vector3(3.3f, 0f, 9.0f), 0f);
        Transform r2 = Point(routeGroup, "End", new Vector3(3.3f, 0f, 11.2f), 0f);
        Transform silhouette = BuildSilhouette(m, r0.localPosition);

        // ---- goal arrow and glow (IntakeRoom moves them; hidden until a goal has a target)
        Transform goalArrow = BuildGoalArrow(m);
        Light goalGlow = PointLight(lights, "GoalGlow", new Vector3(0f, 1f, 0f), new Color(1f, 0.78f, 0.35f), 1.6f, 1.5f);
        goalGlow.enabled = false;

        // ---- wire the component
        IntakeRoom room = rootGo.AddComponent<IntakeRoom>();
        SerializedObject so = new SerializedObject(room);
        Wire(so, "arrivalPoint", arrival);
        Wire(so, "standPoint", stand);
        Wire(so, "bedBlocker", bedBlocker);
        Wire(so, "moveZone", moveZone);
        Wire(so, "roomBZone", roomBZone);
        Wire(so, "hatchZone", hatchZone);
        Wire(so, "mainLamp", mainLamp);
        Wire(so, "mainLampFixture", mainGlass);
        Wire(so, "lampB", lampB);
        Wire(so, "lampC", lampC);
        Wire(so, "focusSign", sign);
        Wire(so, "battery", battery);
        Wire(so, "bottle", bottle);
        Wire(so, "note", note);
        Wire(so, "bell", bell);
        Wire(so, "shutter", shutter);
        Wire(so, "locker", locker);
        Wire(so, "silhouette", silhouette);
        SerializedProperty routeProp = so.FindProperty("route");
        routeProp.arraySize = 3;
        routeProp.GetArrayElementAtIndex(0).objectReferenceValue = r0;
        routeProp.GetArrayElementAtIndex(1).objectReferenceValue = r1;
        routeProp.GetArrayElementAtIndex(2).objectReferenceValue = r2;
        Wire(so, "star", star);
        Wire(so, "hatchLidL", lidL);
        Wire(so, "hatchLidR", lidR);
        Wire(so, "hatchGlow", glow);
        Wire(so, "hatchLight", hatchLight);
        Wire(so, "goalArrow", goalArrow);
        Wire(so, "goalGlow", goalGlow);
        so.ApplyModifiedPropertiesWithoutUndo();

        // ---- F91: the dressing, the ambience and the particles (no new lights; nothing here moves a tutorial point)
        BuildDressing(rootGo);
        WireAmbience(rootGo, corridorLamp, corridorGlass, lampC, hangingGlow, hanging);

        Selection.activeGameObject = rootGo;
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Intake room built. Save the scene (Ctrl+S) to keep it.\n{Audit(rootGo.transform)}", rootGo);
        SaveAsPrefab(rootGo);
    }

    // ---------------------------------------------------------------- shell

    private static void BuildShell(Mats m)
    {
        Transform shell = Group(_root, "Shell");

        // Floors and ceilings: one slab per room, top of floor at y = 0, underside of ceiling at y = 3.
        float[][] slabs =
        {
            new[] { -3.7f, -3.7f, 3.7f, 3.7f },   // A
            new[] { -2.2f, 3.5f, 2.2f, 12.2f },   // B
            new[] { 2.0f, 4.8f, 4.6f, 11.7f },    // E corridor
            new[] { -3.2f, 12.0f, 3.2f, 18.4f },  // C
        };
        foreach (float[] s in slabs)
        {
            Slab(shell, "Floor", s[0], -0.2f, s[1], s[2], 0f, s[3], m.Floor);
            Slab(shell, "Ceiling", s[0], Height, s[1], s[2], Height + 0.2f, s[3], m.Ceiling);
        }

        // Room A.
        WallBox(shell, "A_South", -3.7f, -3.7f, 3.7f, -3.5f, m.Wall);
        WallBox(shell, "A_West", -3.7f, -3.5f, -3.5f, 3.5f, m.Wall);
        WallBox(shell, "A_East", 3.5f, -3.5f, 3.7f, 3.5f, m.Wall);
        WallBox(shell, "A_North_L", -3.7f, 3.5f, -0.8f, 3.7f, m.Wall);
        WallBox(shell, "A_North_R", 0.8f, 3.5f, 3.7f, 3.7f, m.Wall);
        Lintel(shell, "A_North_Lintel", -0.8f, 3.5f, 0.8f, 3.7f, m.Wall);

        // Room B.
        WallBox(shell, "B_West", -2.2f, 3.7f, -2.0f, 12.2f, m.Wall);
        WallBox(shell, "B_East_S", 2.0f, 3.7f, 2.2f, 7.8f, m.Wall);
        WallBox(shell, "B_East_N", 2.0f, 10.2f, 2.2f, 12.0f, m.Wall);
        Lintel(shell, "B_East_Lintel", 2.0f, 7.8f, 2.2f, 10.2f, m.Wall);
        WallBox(shell, "B_North_L", -3.2f, 12.0f, -1.0f, 12.2f, m.Wall);
        WallBox(shell, "B_North_R", 1.0f, 12.0f, 3.2f, 12.2f, m.Wall);
        Lintel(shell, "B_North_Lintel", -1.0f, 12.0f, 1.0f, 12.2f, m.Wall);

        // Corridor E.
        WallBox(shell, "E_East", 4.4f, 4.8f, 4.6f, 11.7f, m.Wall);
        WallBox(shell, "E_South", 2.2f, 4.8f, 4.6f, 5.0f, m.Wall);
        WallBox(shell, "E_North", 2.2f, 11.5f, 4.6f, 11.7f, m.Wall);

        // Room C.
        WallBox(shell, "C_West", -3.2f, 12.2f, -3.0f, 18.4f, m.Wall);
        WallBox(shell, "C_East", 3.0f, 12.2f, 3.2f, 18.4f, m.Wall);
        WallBox(shell, "C_North", -3.2f, 18.2f, 3.2f, 18.4f, m.Wall);
    }

    private static void Slab(Transform parent, string name, float x0, float y0, float z0, float x1, float y1, float z1, Material mat)
    {
        MetricBox(Box(parent, name, x0, y0, z0, x1, y1, z1, mat, true));
    }

    private static void WallBox(Transform parent, string name, float x0, float z0, float x1, float z1, Material mat)
    {
        MetricBox(Box(parent, name, x0, 0f, z0, x1, Height, z1, mat, true));
    }

    private static void Lintel(Transform parent, string name, float x0, float z0, float x1, float z1, Material mat)
    {
        MetricBox(Box(parent, name, x0, 2.4f, z0, x1, Height, z1, mat, true));
    }

    private static GameObject Box(Transform parent, string name, float x0, float y0, float z0, float x1, float y1, float z1, Material mat, bool collider)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        go.transform.localScale = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0));
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    // ---------------------------------------------------------------- pieces

    private static TextMeshPro BuildSign(Transform root, string name, Vector3 localPosition, string text, Color color, float fontSize, Quaternion? rotation = null)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = localPosition;
        if (rotation.HasValue) go.transform.localRotation = rotation.Value;

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.characterSpacing = 3f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.rectTransform.sizeDelta = new Vector2(2.6f, 1.2f);
        return tmp;
    }

    private static IntakeTarget BuildBell(Mats m)
    {
        Transform bellRoot = Group(_root, "Bell");
        bellRoot.localPosition = new Vector3(2.2f, 1.75f, 3.2f);

        GameObject bracket = Prim(bellRoot, PrimitiveType.Cube, "Bracket", new Vector3(0f, 0.34f, 0.16f), new Vector3(0.08f, 0.08f, 0.34f), m.Metal);
        Object.DestroyImmediate(bracket.GetComponent<Collider>());
        GameObject bell = Prim(bellRoot, PrimitiveType.Sphere, "Bell", Vector3.zero, new Vector3(0.36f, 0.4f, 0.36f), m.Brass, true);
        Object.DestroyImmediate(bell.GetComponent<SphereCollider>());
        SphereCollider hit = bell.AddComponent<SphereCollider>();
        hit.isTrigger = true; // a solid one would block the player
        hit.radius = 2.1f;    // local: about 0.8 m once the bell's 0.38 scale is applied - generous, the player is throwing across a dark room
        return bell.AddComponent<IntakeTarget>();
    }

    private static Transform BuildShutter(Mats m)
    {
        Transform shutter = Group(_root, "Shutter");
        shutter.localPosition = Vector3.zero;
        // Slatted look: three plates in one rising unit, the middle one proud.
        Box(shutter, "Panel", -0.8f, 0.02f, 3.5f, 0.8f, 2.4f, 3.7f, m.Metal, true);
        Box(shutter, "Rail", -0.8f, 1.15f, 3.42f, 0.8f, 1.25f, 3.5f, m.Black, false);
        Box(shutter, "RailB", -0.8f, 0.55f, 3.42f, 0.8f, 0.65f, 3.5f, m.Black, false);
        Box(shutter, "RailC", -0.8f, 1.75f, 3.42f, 0.8f, 1.85f, 3.5f, m.Black, false);
        return shutter;
    }

    private static Transform BuildStar(Mats m)
    {
        // A table to hold it, then the star prefab minus its Pickup.
        GameObject table = PlaceProp("Alchemist/Alch_LongTable", new Vector3(0f, 0f, 17.55f), 0f);
        Vector3 top = new Vector3(0f, 1.0f, 17.5f);
        if (table != null)
        {
            AddBlocker(table, "TableBlocker");
            Bounds tb = AlchemistPack.RenderBounds(table);
            top = _root.InverseTransformPoint(new Vector3(tb.center.x, tb.max.y + 0.3f, tb.center.z));
        }

        GameObject starPrefab = null;
        MazeGenerator maze = Object.FindAnyObjectByType<MazeGenerator>(FindObjectsInactive.Include);
        if (maze != null)
        {
            SerializedProperty p = new SerializedObject(maze).FindProperty("starPrefab");
            starPrefab = p != null ? p.objectReferenceValue as GameObject : null;
        }

        GameObject star;
        if (starPrefab != null)
        {
            star = (GameObject)PrefabUtility.InstantiatePrefab(starPrefab, _root);
            PrefabUtility.UnpackPrefabInstance(star, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (Pickup pickup in star.GetComponentsInChildren<Pickup>(true)) Object.DestroyImmediate(pickup);
            foreach (Collider c in star.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            star.name = "IntakeStar";
        }
        else
        {
            Debug.LogWarning("Build Intake Room: MazeGenerator has no starPrefab, so the Intake star is a plain emissive sphere.");
            star = Prim(_root, PrimitiveType.Sphere, "IntakeStar", Vector3.zero, Vector3.one * 0.35f, m.Lamp);
            Object.DestroyImmediate(star.GetComponent<Collider>());
        }
        star.transform.localPosition = top;
        // The star is ~1 m tall, so centring it 0.3 m over the top left its points inside the table: float it 0.1 m clear.
        if (table != null) SeatAt(star, top.y - 0.3f + 0.1f);
        PointLight(star.transform, "StarLight", Vector3.zero, new Color(1f, 0.9f, 0.4f), 5f, 1.2f);
        return star.transform;
    }

    /// <summary>
    /// A down-pointing chevron: two unlit amber bars meeting at the bottom, the same bright amber as the objective card.
    /// Its origin is the tip; IntakeRoom floats it over the goal's target, bobs it and turns it to face the camera.
    /// </summary>
    private static Transform BuildGoalArrow(Mats m)
    {
        Transform arrow = Group(_root, "GoalArrow");
        foreach (float side in new[] { -1f, 1f })
        {
            GameObject arm = Prim(arrow, PrimitiveType.Cube, side < 0f ? "ArmL" : "ArmR", new Vector3(side * 0.19f, 0.16f, 0f), new Vector3(0.5f, 0.1f, 0.1f), m.Arrow);
            arm.transform.localRotation = Quaternion.Euler(0f, 0f, side * 40f);
            arm.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        arrow.gameObject.SetActive(false);
        return arrow;
    }

    private static Transform BuildSilhouette(Mats m, Vector3 localPosition)
    {
        GameObject adam = AssetDatabase.LoadAssetAtPath<GameObject>(AdamModel);
        Transform holder = Group(_root, "Silhouette");
        holder.localPosition = localPosition;
        if (adam == null)
        {
            Debug.LogError($"Build Intake Room: {AdamModel} is missing, so the hide step has no silhouette. Import the Adam Character Pack.");
            // A bare black pillar stands in so the room still works.
            GameObject pillar = Prim(holder, PrimitiveType.Capsule, "Body", new Vector3(0f, 0.9f, 0f), new Vector3(0.5f, 0.9f, 0.5f), m.Black);
            Object.DestroyImmediate(pillar.GetComponent<Collider>());
            return holder;
        }

        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(adam, holder);
        body.name = "Adam";
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;

        Animator animator = body.GetComponent<Animator>();
        if (animator == null) animator = body.AddComponent<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AdamController);
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = m.Black;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        foreach (Collider c in body.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

        Transform head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head != null)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                GameObject eye = Prim(head, PrimitiveType.Sphere, side < 0f ? "EyeL" : "EyeR", Vector3.zero, Vector3.one, m.Eyes);
                Object.DestroyImmediate(eye.GetComponent<Collider>());
                eye.transform.position = head.position + body.transform.forward * 0.1f + body.transform.up * 0.07f + body.transform.right * (0.035f * side);
                Vector3 parent = head.lossyScale;
                eye.transform.localScale = new Vector3(0.02f / parent.x, 0.02f / parent.y, 0.02f / parent.z);
            }
        }
        else
        {
            Debug.LogWarning("Build Intake Room: Adam has no Humanoid Head bone, so the silhouette has no eyes.", body);
        }
        return holder;
    }

    // ---------------------------------------------------------------- furniture

    private static void BuildFurniture()
    {
        Transform dressing = Group(_root, "Dressing");

        // Room A: tables for the bottle and the battery, a cupboard, a chair.
        GameObject t1 = PlaceAgainst("Furniture/WoodTableSquare01", dressing, new Vector3(2.7f, 0f, -1.9f), 90f);
        if (t1 != null) { AddBlocker(t1, "TableBlocker"); _table1 = _root.InverseTransformPoint(AlchemistPack.RenderBounds(t1).min); _table1Max = _root.InverseTransformPoint(AlchemistPack.RenderBounds(t1).max); }
        GameObject t2 = PlaceAgainst("Furniture/WoodTableSquare01", dressing, new Vector3(2.7f, 0f, 1.2f), 90f);
        if (t2 != null) { AddBlocker(t2, "TableBlocker"); _table2 = _root.InverseTransformPoint(AlchemistPack.RenderBounds(t2).min); _table2Max = _root.InverseTransformPoint(AlchemistPack.RenderBounds(t2).max); }
        // Yaw 0 puts the cupboard's doors toward the room (180 faced the wall).
        GameObject cupboard = PlaceAgainst("Furniture/WoodCupboard01", dressing, new Vector3(1.2f, 0f, -3.2f), 0f);
        if (cupboard != null) AddBlocker(cupboard, "CupboardBlocker");
        PlaceAgainst("Furniture/Chair01", dressing, new Vector3(-1.2f, 0f, 1.6f), 70f);

        // Room B: drawers along the west wall.
        // Yaw 180 faces the drawers into the room; it is 0.78 m deep that way, so its centre sits 0.4 m off the wall face.
        GameObject drawers = PlaceAgainst("Furniture/Drawer01", dressing, new Vector3(-1.6f, 0f, 5.6f), 180f);
        if (drawers != null) AddBlocker(drawers, "DrawerBlocker");
        GameObject cupboardB = PlaceAgainst("Furniture/WoodCupboard02", dressing, new Vector3(-1.7f, 0f, 11.0f), 90f);
        if (cupboardB != null) AddBlocker(cupboardB, "CupboardBlocker");
    }

    /// <summary>Alchemist furniture by Prefabs/-relative path (or a PackDressing / Alchemist PropPiece prefab under the theme folders), bottom-centre on the floor.</summary>
    private static GameObject PlaceAgainst(string path, Transform parent, Vector3 bottomCentre, float yaw)
    {
        GameObject go = AlchemistPack.Place(parent, path, bottomCentre, Quaternion.Euler(0f, yaw, 0f));
        return go;
    }

    /// <summary>A composed PropPiece/SetPiece prefab from SourceFiles/Themes/Common/Prefabs, stripped to bare visuals.</summary>
    private static GameObject PlaceProp(string relativePath, Vector3 bottomCentre, float yaw)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PropFolder + relativePath + ".prefab");
        if (prefab == null)
        {
            Debug.LogWarning($"Build Intake Room: prefab '{relativePath}' not found under {PropFolder}.");
            return null;
        }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _root);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        TrimVariants(go);
        foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
        AlchemistPack.Strip(go, true);
        AlchemistPack.FixDefaultMaterials(go);

        Bounds b = AlchemistPack.RenderBounds(go);
        go.transform.position += _root.TransformPoint(bottomCentre) - new Vector3(b.center.x, b.min.y, b.center.z);
        return go;
    }

    /// <summary>One invisible BoxCollider child matching the item's render bounds (the pack's own colliders are stripped).</summary>
    private static Collider AddBlocker(GameObject go, string name)
    {
        Bounds b = AlchemistPack.RenderBounds(go);
        GameObject blocker = new GameObject(name);
        blocker.transform.SetParent(go.transform.parent, false);
        blocker.transform.position = b.center;
        BoxCollider box = blocker.AddComponent<BoxCollider>();
        box.size = b.size;
        return box;
    }

    // ---------------------------------------------------------------- helpers

    private static T CloneKit<T>(T template, string name) where T : Component
    {
        T clone = Object.Instantiate(template, _root);
        clone.gameObject.name = name;
        clone.gameObject.SetActive(true);
        return clone;
    }

    private static void MakeMovable(Transform root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
    }

    private static void Wire(SerializedObject so, string field, Object value)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p == null) { Debug.LogError($"Build Intake Room: IntakeRoom has no serialized field '{field}'."); return; }
        p.objectReferenceValue = value;
    }

    private static Transform Group(Transform parent, string name)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    private static Transform Point(Transform parent, string name, Vector3 localPosition, float yaw)
    {
        Transform t = Group(parent, name);
        t.localPosition = localPosition;
        t.localRotation = Quaternion.Euler(0f, yaw, 0f);
        return t;
    }

    private static Collider Zone(Transform parent, string name, Vector3 localCenter, Vector3 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localCenter;
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size;
        return box;
    }

    private static GameObject Prim(Transform parent, PrimitiveType type, string name, Vector3 localPosition, Vector3 scale, Material material, bool keepCollider = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = scale;
        if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;
        if (!keepCollider)
        {
            Collider c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
        }
        return go;
    }

    private static Light PointLight(Transform parent, string name, Vector3 localPosition, Color color, float range, float intensity)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        // The flashlight is the only shadow caster in this game.
        light.shadows = LightShadows.None;
        return light;
    }

    // ---------------------------------------------------------------- audit

    /// <summary>The plan's assertions: light budget, no overlap with the other authored pieces, every part wired.</summary>
    public static string Audit(Transform root)
    {
        StringBuilder sb = new StringBuilder("Intake audit:\n");
        int lights = root.GetComponentsInChildren<Light>(true).Length;
        sb.AppendLine($"  lights: {lights} (budget 7+star) {(lights <= 8 ? "OK" : "TOO MANY")}");

        Bounds b = new Bounds(root.position, Vector3.zero);
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) { if (!(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds); }
        sb.AppendLine($"  bounds: {b.min} .. {b.max}");

        List<string> clashes = new List<string>();
        foreach (string other in new[] { "ShopRoom", "FloorThemes", "KitMazeTheme", "MazeGenerator" })
        {
            GameObject go = GameObject.Find(other);
            if (go == null) continue;
            Bounds ob = new Bounds(go.transform.position, Vector3.zero);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) ob.Encapsulate(r.bounds);
            if (ob.Intersects(b)) clashes.Add(other);
        }
        sb.AppendLine(clashes.Count == 0 ? "  overlap: none OK" : "  overlap with: " + string.Join(", ", clashes));

        IntakeRoom room = root.GetComponent<IntakeRoom>();
        sb.AppendLine($"  complete: {(room != null && room.IsComplete ? "OK" : "NO")}");
        sb.Append(AuditPaths(root));
        return sb.ToString();
    }

    // ---------------------------------------------------------------- materials

    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(MaterialFolder)) return;
        AssetDatabase.CreateFolder("Assets/SourceFiles/Materials", "Intake");
    }

    private static Mats LoadMaterials()
    {
        Mats m = new Mats
        {
            Wall = LoadOrCreate("Intake_Wall", new Color(0.30f, 0.34f, 0.32f), null, 0.1f),
            Floor = LoadOrCreate("Intake_Floor", new Color(0.13f, 0.14f, 0.14f), null, 0.35f),
            Ceiling = LoadOrCreate("Intake_Ceiling", new Color(0.2f, 0.21f, 0.2f), null, 0.05f),
            Metal = LoadOrCreate("Intake_Metal", new Color(0.26f, 0.27f, 0.29f), null, 0.4f),
            Brass = LoadOrCreate("Intake_Brass", new Color(0.7f, 0.5f, 0.2f), null, 0.5f),
            Marker = LoadOrCreate("Intake_Marker", new Color(1f, 0.72f, 0.42f), new Color(1f, 0.72f, 0.42f) * 1.6f, 0.1f),
            Lamp = LoadOrCreate("Intake_Lamp", new Color(1f, 0.9f, 0.7f), new Color(1f, 0.85f, 0.6f) * 2.2f, 0.1f),
            Black = LoadOrCreate("Intake_Silhouette", new Color(0.01f, 0.01f, 0.012f), null, 0.0f),
            Eyes = LoadOrCreate("Intake_Eyes", new Color(1f, 0.1f, 0.05f), new Color(1f, 0.1f, 0.05f) * 5f, 0.1f),
            Arrow = LoadOrCreateUnlit("Intake_Arrow", new Color(1f, 0.72f, 0.1f)),
        };
        // F91 D1: the shell wears the Ward's own surfaces (the flat Intake_Wall/Floor/Ceiling stay as fallbacks).
        m.Wall = WardMaterial("Ward_Wall", m.Wall);
        m.Floor = WardMaterial("Ward_Floor", m.Floor);
        m.Ceiling = WardMaterial("Ward_Ceiling", m.Ceiling);
        AssetDatabase.SaveAssets();
        return m;
    }

    /// <summary>An unlit material: the goal arrow must read in the dark, and nothing may shade it.</summary>
    private static Material LoadOrCreateUnlit(string name, Color color)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) return LoadOrCreate(name, color, color * 3f, 0f);

        Material material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Material LoadOrCreate(string name, Color color, Color? emission, float smoothness)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);

        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}

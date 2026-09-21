using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LIGHTS OUT > Build Shop Room. Generates the room between floors as ordinary scene objects, once, so
/// it can be inspected and edited in Unity like anything else. Nothing here runs in a build; at Play
/// time the ShopRoom component just drives what this made.
///
/// Rebuilding replaces the existing room (after asking). Materials live in
/// Assets/SourceFiles/Materials/Shop and are reused, not overwritten, so tweaks to them survive.
/// </summary>
public static class ShopRoomBuilder
{
    private const string RoomName = "ShopRoom";
    private const string MaterialFolder = "Assets/SourceFiles/Materials/Shop";

    // Where the room sits, west of the 11x11 maze (which spans x -70..-20 and z -70..-20). Floor top of
    // the maze is y = 5.0. It must stay above y = -5 or RespawnPlayer bounces the player out of it.
    private static readonly Vector3 RoomPosition = new Vector3(-92f, 5f, -45f);

    private const float Inner = 12f;      // floor size inside the walls
    private const float WallHeight = 4f;
    private const float WallThickness = 0.5f;

    private static readonly Color HatchGreen = new Color(0.25f, 1f, 0.7f);
    private static readonly Color Amber = new Color(1f, 0.72f, 0.42f);

    [MenuItem("LIGHTS OUT/Build Shop Room")]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Build Shop Room", "Open the game scene first.", "OK");
            return;
        }

        ShopRoom existing = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Build Shop Room",
                    "A ShopRoom already exists in this scene. Replace it? Any changes you made to it will be lost.",
                    "Replace", "Cancel"))
            {
                return;
            }
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        EnsureFolder();
        Materials mats = LoadMaterials();

        GameObject root = new GameObject(RoomName);
        Undo.RegisterCreatedObjectUndo(root, "Build Shop Room");
        root.transform.position = RoomPosition;

        BuildShell(root.transform, mats);
        BuildLights(root.transform);
        BuildCounter(root.transform, mats);
        Transform head = BuildSalesman(root.transform, mats);
        TextMeshPro counterSign = BuildSign(root.transform, "ShopSign", new Vector3(0f, 3.2f, 5.6f), Quaternion.identity, "SHARDS ACCEPTED", Amber, 8f);
        TextMeshPro doorSign = BuildSign(root.transform, "DoorSign", new Vector3(0f, 3.3f, -5.7f), Quaternion.Euler(0f, 180f, 0f), "FLOOR 2", HatchGreen, 6f);
        BuildDoor(root.transform, mats);

        Transform points = new GameObject("Points").transform;
        points.SetParent(root.transform, false);

        Transform arrival = new GameObject("Arrival").transform;
        arrival.SetParent(points, false);
        arrival.localPosition = new Vector3(0f, 0f, -1.5f);
        arrival.localRotation = Quaternion.identity; // facing +Z, toward the counter

        Collider counterZone = BuildZone(points, "CounterZone", new Vector3(0f, 1.25f, 2.4f), new Vector3(4.5f, 2.5f, 2.2f));
        Collider doorZone = BuildZone(points, "DoorZone", new Vector3(0f, 1.25f, -4.9f), new Vector3(2.6f, 2.5f, 2.0f));

        ShopRoom room = root.AddComponent<ShopRoom>();
        SerializedObject so = new SerializedObject(room);
        so.FindProperty("arrivalPoint").objectReferenceValue = arrival;
        so.FindProperty("counterZone").objectReferenceValue = counterZone;
        so.FindProperty("doorZone").objectReferenceValue = doorZone;
        so.FindProperty("headPivot").objectReferenceValue = head;
        so.FindProperty("counterSign").objectReferenceValue = counterSign;
        so.FindProperty("doorSign").objectReferenceValue = doorSign;
        so.ApplyModifiedPropertiesWithoutUndo();

        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log("Shop room built. Save the scene (Ctrl+S) to keep it.", root);
    }

    // ---------------------------------------------------------------- pieces

    private static void BuildShell(Transform root, Materials m)
    {
        Transform shell = Group(root, "Shell");
        float outer = Inner + 2f * WallThickness;
        float half = Inner * 0.5f;
        float offset = half + WallThickness * 0.5f;
        float wallY = WallHeight * 0.5f;

        Box(shell, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(outer, 0.2f, outer), m.Floor);
        Box(shell, "Ceiling", new Vector3(0f, WallHeight + 0.1f, 0f), new Vector3(outer, 0.2f, outer), m.Ceiling);
        Box(shell, "Wall_N", new Vector3(0f, wallY, offset), new Vector3(outer, WallHeight, WallThickness), m.Wall);
        Box(shell, "Wall_S", new Vector3(0f, wallY, -offset), new Vector3(outer, WallHeight, WallThickness), m.Wall);
        Box(shell, "Wall_E", new Vector3(offset, wallY, 0f), new Vector3(WallThickness, WallHeight, outer), m.Wall);
        Box(shell, "Wall_W", new Vector3(-offset, wallY, 0f), new Vector3(WallThickness, WallHeight, outer), m.Wall);
    }

    private static void BuildLights(Transform root)
    {
        Transform lights = Group(root, "Lights");
        Color warm = new Color(1f, 0.82f, 0.62f);
        const float q = 3.5f;

        PointLight(lights, "Light_NE", new Vector3(q, 3.6f, q), warm, 9f, 1.6f);
        PointLight(lights, "Light_NW", new Vector3(-q, 3.6f, q), warm, 9f, 1.6f);
        PointLight(lights, "Light_SE", new Vector3(q, 3.6f, -q), warm, 9f, 1.6f);
        PointLight(lights, "Light_SW", new Vector3(-q, 3.6f, -q), warm, 9f, 1.6f);
        PointLight(lights, "CounterLight", new Vector3(0f, 3.2f, 4f), Amber, 6f, 2.2f);
        PointLight(lights, "DoorLight", new Vector3(0f, 1.5f, -5.5f), HatchGreen, 5f, 1.2f);
    }

    private static void BuildCounter(Transform root, Materials m)
    {
        Transform counter = Group(root, "Counter");
        Box(counter, "CounterTop", new Vector3(0f, 0.525f, 4f), new Vector3(3.2f, 1.05f, 0.9f), m.Counter);
        GameObject strip = Box(counter, "CounterStrip", new Vector3(0f, 1.05f, 3.55f), new Vector3(3.2f, 0.04f, 0.04f), m.CounterStrip);
        Object.DestroyImmediate(strip.GetComponent<Collider>());
    }

    /// <summary>The salesman stands behind the counter facing -Z (the room). Returns the head pivot the component turns.</summary>
    private static Transform BuildSalesman(Transform root, Materials m)
    {
        Transform salesman = Group(root, "Salesman");
        salesman.localPosition = new Vector3(0f, 0f, 5.1f);

        // The body keeps its collider: it is what stops the player walking through the salesman.
        Primitive(salesman, PrimitiveType.Capsule, "Body", new Vector3(0f, 0.95f, 0f), new Vector3(0.55f, 0.95f, 0.55f), m.Coat, keepCollider: true);

        // Pivot faces -Z (yaw 180), so "in front of the head" is local +Z for everything parented to it.
        Transform head = new GameObject("Head").transform;
        head.SetParent(salesman, false);
        head.localPosition = Vector3.zero;
        head.localRotation = Quaternion.Euler(0f, 180f, 0f);

        Primitive(head, PrimitiveType.Sphere, "HeadMesh", new Vector3(0f, 2.05f, 0f), Vector3.one * 0.42f, m.Coat);
        Primitive(head, PrimitiveType.Cylinder, "HatBrim", new Vector3(0f, 2.28f, 0f), new Vector3(0.8f, 0.03f, 0.8f), m.Coat);
        Primitive(head, PrimitiveType.Cylinder, "HatCrown", new Vector3(0f, 2.42f, 0f), new Vector3(0.42f, 0.14f, 0.42f), m.Coat);
        Primitive(head, PrimitiveType.Sphere, "EyeL", new Vector3(-0.09f, 2.08f, 0.19f), Vector3.one * 0.06f, m.Eyes);
        Primitive(head, PrimitiveType.Sphere, "EyeR", new Vector3(0.09f, 2.08f, 0.19f), Vector3.one * 0.06f, m.Eyes);
        return head;
    }

    private static TextMeshPro BuildSign(Transform root, string name, Vector3 localPosition, Quaternion localRotation, string text, Color color, float spacing)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = localRotation;

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = 6f;
        tmp.color = color;
        tmp.characterSpacing = spacing;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.rectTransform.sizeDelta = new Vector2(10f, 2f);
        return tmp;
    }

    private static void BuildDoor(Transform root, Materials m)
    {
        Transform door = Group(root, "Door");
        const float z = -6f; // the inner face of the south wall

        GameObject postL = Box(door, "PostL", new Vector3(-0.9f, 1.5f, z), new Vector3(0.2f, 3f, 0.2f), m.DoorFrame);
        GameObject postR = Box(door, "PostR", new Vector3(0.9f, 1.5f, z), new Vector3(0.2f, 3f, 0.2f), m.DoorFrame);
        GameObject lintel = Box(door, "Lintel", new Vector3(0f, 3.1f, z), new Vector3(2f, 0.2f, 0.2f), m.DoorFrame);
        GameObject panel = Box(door, "Panel", new Vector3(0f, 1.5f, z + 0.02f), new Vector3(1.6f, 2.8f, 0.1f), m.DoorPanel);

        // Set dressing against a wall: nothing should collide with it.
        foreach (GameObject part in new[] { postL, postR, lintel, panel }) Object.DestroyImmediate(part.GetComponent<Collider>());
    }

    private static Collider BuildZone(Transform parent, string name, Vector3 localCenter, Vector3 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localCenter;

        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size;
        return box;
    }

    // ---------------------------------------------------------------- helpers

    private static Transform Group(Transform parent, string name)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    private static GameObject Box(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material)
    {
        return Primitive(parent, PrimitiveType.Cube, name, localPosition, size, material, keepCollider: true);
    }

    private static GameObject Primitive(Transform parent, PrimitiveType type, string name, Vector3 localPosition, Vector3 scale, Material material, bool keepCollider = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = scale;
        if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;

        if (!keepCollider)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
        }
        return go;
    }

    private static void PointLight(Transform parent, string name, Vector3 localPosition, Color color, float range, float intensity)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        // The flashlight is the only shadow caster in this game; a shadowed point light costs six atlas faces.
        light.shadows = LightShadows.None;
    }

    // ---------------------------------------------------------------- materials

    private struct Materials
    {
        public Material Floor, Ceiling, Wall, Counter, CounterStrip, Coat, Eyes, DoorFrame, DoorPanel;
    }

    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(MaterialFolder)) return;
        AssetDatabase.CreateFolder("Assets/SourceFiles/Materials", "Shop");
    }

    private static Materials LoadMaterials()
    {
        Materials m = new Materials
        {
            Floor = LoadOrCreate("Shop_Floor", new Color(0.10f, 0.10f, 0.12f), null),
            Ceiling = LoadOrCreate("Shop_Ceiling", new Color(0.06f, 0.06f, 0.07f), null),
            Wall = LoadOrCreate("Shop_Wall", new Color(0.22f, 0.22f, 0.25f), null),
            Counter = LoadOrCreate("Shop_Counter", new Color(0.12f, 0.10f, 0.09f), null),
            Coat = LoadOrCreate("Shop_Coat", new Color(0.04f, 0.04f, 0.05f), null),
            DoorPanel = LoadOrCreate("Shop_DoorPanel", new Color(0.03f, 0.03f, 0.03f), null),
            CounterStrip = LoadOrCreate("Shop_CounterStrip", Amber, Amber * 2f),
            Eyes = LoadOrCreate("Shop_Eyes", new Color(1f, 0.7f, 0.3f), new Color(1f, 0.7f, 0.3f) * 4f),
            DoorFrame = LoadOrCreate("Shop_DoorFrame", HatchGreen, HatchGreen * 2f),
        };
        AssetDatabase.SaveAssets();
        return m;
    }

    private static Material LoadOrCreate(string name, Color color, Color? emission)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        // The stock 0.5 throws a specular flare straight back down the flashlight beam
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.15f);

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

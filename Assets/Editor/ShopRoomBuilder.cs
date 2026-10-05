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
/// The room is an alchemist's shop dressed from the Assets/BK_AlchemistHouse pack (its materials are
/// converted to URP by AlchemistMaterialUpgrade). The shell, lights, door, counter and salesman live in
/// this file; the placement helpers and the furniture passes are in ShopRoomBuilder.Dressing.cs.
///
/// Rebuilding replaces the existing room (after asking). Spec: plannings/shop-room-alchemist-plan.md.
/// </summary>
public static partial class ShopRoomBuilder
{
    private const string RoomName = "ShopRoom";
    private const string MaterialFolder = "Assets/SourceFiles/Materials/Shop";

    // Where the room sits, west of the 11x11 maze (which spans x -70..-20 and z -70..-20). Floor top of
    // the maze is y = 5.0. It must stay above y = -5 or RespawnPlayer bounces the player out of it.
    private static readonly Vector3 RoomPosition = new Vector3(-92f, 5f, -45f);
    private const string GuardFolder = "Assets/UnityTechnologies/Adam Character Pack/Guard";

    private const float HalfX = 4.5f;        // inner half-width  (3 modules x 3 m)
    private const float HalfZ = 6f;          // inner half-depth  (4 modules x 3 m)
    private const float RoomHeight = 3f;
    private const float Module = 3f;         // 2.5 m pack module x ArchScale
    private const float ArchScale = 1.2f;

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
        }

        if (AlchemistMaterialUpgrade.NeedsConversion())
        {
            if (!EditorUtility.DisplayDialog("Build Shop Room",
                    "The Alchemist House materials are still on Built-in shaders and would render magenta. Convert them to URP now? " +
                    "This edits the materials under Assets/BK_AlchemistHouse in place.",
                    "Convert", "Cancel"))
            {
                return;
            }
        }

        BuildSilently();
    }

    /// <summary>No dialogs: replaces any existing room and converts the pack materials if needed. Used for scripted rebuilds.</summary>
    public static void BuildSilently()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("Build Shop Room: open the game scene first.");
            return;
        }

        ShopRoom existing = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        // A previous build that died half-way leaves the root behind without its component.
        GameObject stale;
        while ((stale = GameObject.Find(RoomName)) != null) Undo.DestroyObjectImmediate(stale);

        if (AlchemistMaterialUpgrade.NeedsConversion()) AlchemistMaterialUpgrade.Convert();

        BuildCore(scene);
    }

    private static void BuildCore(Scene scene)
    {
        EnsureFolder();
        Materials mats = LoadMaterials();
        UsedPrefabs.Clear();
        KeptLights.Clear();

        GameObject root = new GameObject(RoomName);
        Undo.RegisterCreatedObjectUndo(root, "Build Shop Room");
        root.transform.position = RoomPosition;
        _room = root.transform;

        BuildShell(root.transform);
        BuildBackdrop(root.transform, mats.Wall);
        BuildColliders(root.transform);
        BuildDoor(root.transform);
        GameObject[] counterTables = BuildCounter(root.transform);
        Transform head = BuildSalesman(root.transform, mats);
        DressCounter(Group(root.transform, "CounterTop"), counterTables);
        BuildBackWall(root.transform);
        BuildLabSide(root.transform);
        BuildLibrarySide(root.transform);
        BuildCorners(root.transform);
        BuildCarpets(root.transform);
        BuildLights(root.transform);

        TextMeshPro counterSign = BuildSign(root.transform, "ShopSign", new Vector3(0f, 2.45f, 3.6f), Quaternion.identity, "SHARDS ACCEPTED", Amber, 8f, 3.5f);
        TextMeshPro doorSign = BuildSign(root.transform, "DoorSign", new Vector3(0f, 2.68f, -5.92f), Quaternion.Euler(0f, 180f, 0f), "FLOOR 2", HatchGreen, 6f, 3f);

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
        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log($"Shop room built ({UsedPrefabs.Count} pack prefabs). Save the scene (Ctrl+S) to keep it.\n{Audit(root.transform)}", root);
    }

    // ---------------------------------------------------------------- pieces

    /// <summary>The salesman stands behind the counter facing -Z (the room). Returns the head pivot the component turns.</summary>
    /// <remarks>
    /// The body is the Guard from the Adam Character Pack (the hunter is Adam, so the two never read as the
    /// same thing), idling on the pack's own controller. The head pivot is its Humanoid Head bone: ShopRoom
    /// turns it in LateUpdate, on top of whatever the idle clip did that frame.
    /// </remarks>
    private static Transform BuildSalesman(Transform root, Materials m)
    {
        Transform salesman = Group(root, "Salesman");
        salesman.localPosition = new Vector3(0f, 0f, 5.1f);
        salesman.localRotation = Quaternion.Euler(0f, 180f, 0f);

        // This collider is what stops the player walking through the salesman; the Guard itself has none.
        CapsuleCollider blocker = salesman.gameObject.AddComponent<CapsuleCollider>();
        blocker.center = new Vector3(0f, 0.9f, 0f);
        blocker.radius = 0.3f;
        blocker.height = 1.8f;

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(GuardFolder + "/Guard.FBX");
        if (model == null)
        {
            Debug.LogError($"ShopRoomBuilder: {GuardFolder}/Guard.FBX is missing, so the shop has no salesman body.");
            return null;
        }

        GameObject guard = (GameObject)PrefabUtility.InstantiatePrefab(model);
        guard.name = "Guard";
        guard.transform.SetParent(salesman, false);

        Animator animator = guard.GetComponent<Animator>();
        if (animator == null) animator = guard.AddComponent<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(GuardFolder + "/Guard_AnimationController.controller");
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // Pose it now, so the room shows him idling in the Editor too instead of standing in his bind pose.
        AnimationClip idle = LoadGuardIdle();
        if (idle != null)
        {
            idle.SampleAnimation(guard, 0f);
            guard.transform.localPosition = Vector3.zero;
            guard.transform.localRotation = Quaternion.identity;
        }

        Transform head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null)
        {
            Debug.LogWarning("ShopRoomBuilder: the Guard has no Humanoid Head bone, so the salesman's head will not follow the player.", guard);
            return null;
        }

        BuildSalesmanEyes(guard, head, salesman, m);
        return head;
    }

    /// <summary>Two small emissive beads over the Guard's own eyes, riding the head bone, so he keeps the amber stare.</summary>
    private static void BuildSalesmanEyes(GameObject guard, Transform head, Transform salesman, Materials m)
    {
        Renderer eyes = null;
        foreach (Renderer r in guard.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name.Contains("reflection_eyes")) eyes = r;
        }
        if (eyes == null) return;

        Bounds b = eyes.bounds;
        Vector3 centre = b.center + salesman.forward * (Vector3.Scale(b.extents, Abs(salesman.forward)).magnitude + 0.004f);
        float half = Vector3.Scale(b.extents, Abs(salesman.right)).magnitude * 0.55f;

        foreach (float side in new[] { -1f, 1f })
        {
            GameObject eye = Primitive(head, PrimitiveType.Sphere, side < 0f ? "EyeL" : "EyeR", Vector3.zero, Vector3.one, m.Eyes);
            eye.transform.position = centre + salesman.right * (half * side);
            eye.transform.rotation = Quaternion.identity;
            SetWorldScale(eye.transform, 0.02f);
        }
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    private static void SetWorldScale(Transform t, float size)
    {
        Vector3 parent = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(size / parent.x, size / parent.y, size / parent.z);
    }

    private static AnimationClip LoadGuardIdle()
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(GuardFolder + "/Guard_Idle.FBX"))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__")) return clip;
        }
        return null;
    }

    private static TextMeshPro BuildSign(Transform root, string name, Vector3 localPosition, Quaternion localRotation, string text, Color color, float spacing, float fontSize)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = localRotation;

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.characterSpacing = spacing;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.rectTransform.sizeDelta = new Vector2(10f, 2f);
        return tmp;
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

    // The salesman's eyes and the wall backdrop are the only primitives left. The old Shop_Floor/Ceiling/Counter/Door materials stay on disk, unused.
    private struct Materials
    {
        public Material Eyes, Wall;
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
            Wall = LoadOrCreate("Shop_Wall", new Color(0.22f, 0.22f, 0.25f), null),
            Eyes = LoadOrCreate("Shop_Eyes", new Color(1f, 0.7f, 0.3f), new Color(1f, 0.7f, 0.3f) * 4f),
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

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LIGHTS OUT &gt; Build Floor Themes. Generates five floors' worth of themed set-dressing - materials
/// and prefabs under Assets/SourceFiles/Themes, and a scene gallery (FloorThemes) with one live prefab
/// instance of each piece per floor - so an artist can inspect and retouch every piece MazeGenerator
/// clones at Play time. Nothing here runs in a build; at Play time MazeGenerator.ResolveTheme just finds
/// what this made and clones the scene instances (see decision 1 in plannings/floor-themes-plan.md).
///
/// Rebuilding never silently loses work: materials and prefabs are loaded if they already exist
/// (LoadOrCreate, same pattern as ShopRoomBuilder), and the whole Themes folder is only ever deleted
/// when the artist explicitly picks "Replace everything".
/// </summary>
public static class FloorThemeBuilder
{
    private const string ThemesRoot = "Assets/SourceFiles/Themes";
    private const string GalleryName = "FloorThemes";
    private static readonly Vector3 GalleryOrigin = new Vector3(-70f, 5f, -6f);
    private const float RowSpacing = 12f;
    private const float PieceSpacing = 6f;

    // Canonical authored sizes. WallPiece.ScaleFor divides by these; FloorTile/CeilingTile are scaled by
    // MazeGenerator using cellSize / TileSize. The gallery and SampleCell show every piece at scale 1.
    private const float WallHeight = 4f;
    private const float TileSize = 4.5f;

    // cellSize/2 - wallThickness/2 at the canonical 4.5 m cell / 0.5 m wall the pieces are authored for.
    private const float BackFaceDistance = 2.0f;

    private static Material _plinthMaterial;

    [MenuItem("LIGHTS OUT/Build Floor Themes")]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Build Floor Themes", "Open the game scene first.", "OK");
            return;
        }

        FloorThemeSet existingSet = UnityEngine.Object.FindAnyObjectByType<FloorThemeSet>(FindObjectsInactive.Include);
        if (existingSet != null)
        {
            if (!EditorUtility.DisplayDialog("Build Floor Themes",
                    "A FloorThemes gallery already exists in this scene. Replace it? Any changes you made to the gallery hierarchy itself (not the material/prefab assets) will be lost.",
                    "Replace", "Cancel"))
            {
                return;
            }
        }

        int choice = EditorUtility.DisplayDialogComplex("Build Floor Themes",
            "Keep the existing materials and prefabs under Assets/SourceFiles/Themes (any hand edits survive), or delete and rebuild everything?",
            "Keep existing assets (recommended)", "Cancel", "Replace everything");
        if (choice == 1) return; // Cancel
        bool replaceAssets = choice == 2;

        if (existingSet != null) Undo.DestroyObjectImmediate(existingSet.gameObject);
        if (replaceAssets && AssetDatabase.IsValidFolder(ThemesRoot)) AssetDatabase.DeleteAsset(ThemesRoot);
        EnsureFolder(ThemesRoot);
        // The cached plinth material may point at an asset the line above just deleted.
        _plinthMaterial = null;

        GameObject galleryRoot = new GameObject(GalleryName);
        Undo.RegisterCreatedObjectUndo(galleryRoot, "Build Floor Themes");
        galleryRoot.transform.position = GalleryOrigin;
        FloorThemeSet themeSet = galleryRoot.AddComponent<FloorThemeSet>();

        var builders = new Func<ThemeAssets>[] { BuildWard, BuildBoilerDeck, BuildCrypt, BuildLab, BuildHollow };
        var rowNames = new[] { "Floor1_Ward", "Floor2_BoilerDeck", "Floor3_Crypt", "Floor4_Lab", "Floor5_Hollow" };

        List<FloorTheme> builtThemes = new List<FloorTheme>();
        for (int i = 0; i < builders.Length; i++)
        {
            ThemeAssets assets = builders[i]();
            builtThemes.Add(BuildRow(galleryRoot.transform, rowNames[i], i, assets));
        }

        SerializedObject setSo = new SerializedObject(themeSet);
        SerializedProperty floorsProp = setSo.FindProperty("floors");
        floorsProp.arraySize = builtThemes.Count;
        for (int i = 0; i < builtThemes.Count; i++)
        {
            floorsProp.GetArrayElementAtIndex(i).objectReferenceValue = builtThemes[i];
        }
        setSo.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = galleryRoot;
        SceneView.lastActiveSceneView?.FrameSelected();
        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log("Floor themes built. Save the scene (Ctrl+S) to keep them.", galleryRoot);
    }

    // ---------------------------------------------------------------- per-theme data

    private class ThemeAssets
    {
        public string DisplayName;
        public Color Ambient;
        public Color FogColor;
        public float FogDensityScale = 1f;
        public Color LampColor;
        public float FaultyLampChance;
        public float WallPropChance;
        public float CeilingPropChance;
        public Material WallMaterial;
        public WallPiece Wall;
        public GameObject Pillar;
        public GameObject FloorTile;
        public GameObject CeilingTile;
        public WallLamp Lamp;
        public Locker Locker;
        public PropPiece[] Props;
    }

    // ---------------------------------------------------------------- shared low-level helpers

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static Transform Group(Transform parent, string name)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    private static GameObject Primitive(Transform parent, PrimitiveType type, string pieceName, Vector3 localPosition, Vector3 localScale, Material material, bool keepCollider, Quaternion? localRotation = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = pieceName;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = localRotation ?? Quaternion.identity;
        go.transform.localScale = localScale;
        if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;

        if (!keepCollider)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        }
        return go;
    }

    private static GameObject Box(Transform parent, string pieceName, Vector3 localPosition, Vector3 size, Material material, bool keepCollider = false, Quaternion? localRotation = null)
    {
        return Primitive(parent, PrimitiveType.Cube, pieceName, localPosition, size, material, keepCollider, localRotation);
    }

    /// <summary>The built-in cylinder is 2 units tall with a 1-unit diameter at scale 1, so this converts a radius/height pair into that scale.</summary>
    private static GameObject CylinderRH(Transform parent, string pieceName, Vector3 localPosition, float radius, float height, Material material, bool keepCollider = false, Quaternion? localRotation = null)
    {
        return Primitive(parent, PrimitiveType.Cylinder, pieceName, localPosition, new Vector3(radius * 2f, height * 0.5f, radius * 2f), material, keepCollider, localRotation);
    }

    /// <summary>A cylinder spanning two local points - handy for torch handles, chains, wires, pipes at an angle.</summary>
    private static GameObject CylinderBetween(Transform parent, string pieceName, Vector3 a, Vector3 b, float radius, Material material, bool keepCollider = false)
    {
        Vector3 mid = (a + b) * 0.5f;
        float length = Mathf.Max(0.01f, Vector3.Distance(a, b));
        GameObject go = Primitive(parent, PrimitiveType.Cylinder, pieceName, mid, new Vector3(radius * 2f, length * 0.5f, radius * 2f), material, keepCollider);
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
        return go;
    }

    private static GameObject Sphere(Transform parent, string pieceName, Vector3 localPosition, Vector3 scale, Material material, bool keepCollider = false)
    {
        return Primitive(parent, PrimitiveType.Sphere, pieceName, localPosition, scale, material, keepCollider);
    }

    private static GameObject Capsule(Transform parent, string pieceName, Vector3 localPosition, Vector3 scale, Material material, bool keepCollider = false, Quaternion? localRotation = null)
    {
        return Primitive(parent, PrimitiveType.Capsule, pieceName, localPosition, scale, material, keepCollider, localRotation);
    }

    private static Texture2D Texture(string path)
    {
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex == null) Debug.LogWarning($"FloorThemeBuilder: texture not found at {path}, leaving the material untextured there.");
        return tex;
    }

    /// <summary>Loads a normal-map texture and fixes its import type if it was not already imported as one - URP renders an un-typed normal map wrong.</summary>
    private static Texture2D NormalTexture(string path)
    {
        Texture2D tex = Texture(path);
        if (tex == null) return null;

        if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        return tex;
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
        light.shadows = LightShadows.None;
    }

    /// <summary>Loaded if it already exists at folder/name.mat (so hand edits survive a rebuild), otherwise created as a URP Lit material with the given tint, optional albedo/normal textures and tiling, and optional emission.</summary>
    private static Material LoadOrCreateMat(string folder, string name, Color color, string texturePath, string normalPath, Vector2 tiling, Color? emission, float smoothness = 0.15f)
    {
        string path = $"{folder}/Materials/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material mat = new Material(shader) { name = name };

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

        if (!string.IsNullOrEmpty(texturePath))
        {
            Texture2D tex = Texture(texturePath);
            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }
            if (mat.HasProperty("_BaseMap")) mat.SetTextureScale("_BaseMap", tiling);
            if (mat.HasProperty("_MainTex")) mat.SetTextureScale("_MainTex", tiling);
        }

        if (!string.IsNullOrEmpty(normalPath))
        {
            Texture2D normalTex = NormalTexture(normalPath);
            if (normalTex != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normalTex);
                mat.EnableKeyword("_NORMALMAP");
            }
        }

        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        EnsureFolder($"{folder}/Materials");
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    /// <summary>Loaded if folder/Prefabs/prefabName.prefab already exists (the freshly built temp hierarchy is discarded), otherwise saved fresh.</summary>
    private static GameObject SaveAsPrefab(GameObject root, string folder, string prefabName)
    {
        string path = $"{folder}/Prefabs/{prefabName}.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            UnityEngine.Object.DestroyImmediate(root);
            return existing;
        }

        EnsureFolder($"{folder}/Prefabs");
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return saved;
    }

    private static T SaveAsPrefab<T>(GameObject root, string folder, string prefabName) where T : Component
    {
        return SaveAsPrefab(root, folder, prefabName).GetComponent<T>();
    }

    // ---------------------------------------------------------------- door slats (mirrors MazeGenerator.BuildDoorSlats)

    /// <summary>Three horizontal slats with two 0.06 m gaps around eye height, same proportions as MazeGenerator's primitive locker door. Returns [bottom, middle, top] so a caller can hang extra dressing off one of them.</summary>
    private static GameObject[] BuildDoorSlats(Transform doorPivot, float doorWidth, float doorHeight, float depth, Material material)
    {
        const float gap = 0.06f;
        float bottomHeight = Mathf.Clamp(1.5f, 0.1f, doorHeight);
        float midHeight = 0.08f;
        float midStart = bottomHeight + gap;
        float midEnd = midStart + midHeight;
        float topStart = midEnd + gap;
        float topHeight = Mathf.Max(0.1f, doorHeight - topStart);

        GameObject bottom = DoorSlat(doorPivot, "Slat_Bottom", doorWidth, bottomHeight, depth, bottomHeight * 0.5f, material);
        GameObject middle = DoorSlat(doorPivot, "Slat_Middle", doorWidth, midHeight, depth, midStart + midHeight * 0.5f, material);
        GameObject top = DoorSlat(doorPivot, "Slat_Top", doorWidth, topHeight, depth, topStart + topHeight * 0.5f, material);
        return new[] { bottom, middle, top };
    }

    private static GameObject DoorSlat(Transform parent, string slatName, float width, float height, float depth, float centerY, Material material)
    {
        // Slats keep their collider: it is what stops the hunter's LOS raycast seeing through a closed door.
        return Box(parent, slatName, new Vector3(width * 0.5f, centerY, 0f), new Vector3(width, height, depth), material, keepCollider: true);
    }

    // ---------------------------------------------------------------- piece finishers

    private static WallPiece FinishWall(GameObject root, string folder)
    {
        // Default authoredLength/Height/Thickness (5, 4, 0.5) already match the canonical size every
        // wall below is built at, so there is nothing to override via SerializedObject here.
        root.AddComponent<WallPiece>();
        return SaveAsPrefab<WallPiece>(root, folder, root.name);
    }

    private static WallLamp FinishLamp(GameObject root, string folder, Light light, Renderer glass)
    {
        WallLamp lamp = root.AddComponent<WallLamp>();
        SerializedObject so = new SerializedObject(lamp);
        so.FindProperty("lightSource").objectReferenceValue = light;
        so.FindProperty("glassRenderer").objectReferenceValue = glass;
        so.ApplyModifiedPropertiesWithoutUndo();
        return SaveAsPrefab<WallLamp>(root, folder, root.name);
    }

    private static PropPiece FinishProp(GameObject root, string folder, PropPiece.MountKind mount, float depth, float weight, bool enabledInMaze = true)
    {
        PropPiece prop = root.AddComponent<PropPiece>();
        SerializedObject so = new SerializedObject(prop);
        so.FindProperty("mount").enumValueIndex = (int)mount;
        so.FindProperty("depth").floatValue = depth;
        so.FindProperty("weight").floatValue = weight;
        so.FindProperty("enabledInMaze").boolValue = enabledInMaze;
        so.ApplyModifiedPropertiesWithoutUndo();
        return SaveAsPrefab<PropPiece>(root, folder, root.name);
    }

    /// <summary>
    /// The standard locker shell every theme's locker is built from: body, plinth, trigger and anchors
    /// at the numbers the Ward table gives (every other theme's row says "Anchors as Ward" - same hinge
    /// position too, so Locker.Expose always swings the same way). buildDoorContents fills the door
    /// pivot (three slats for most themes, something else for Crypt/Hollow); extraBodyDressing adds
    /// anything else against the body (bevel strips, an LED strip, ...).
    /// </summary>
    private static Locker BuildLockerShell(string folder, string prefabName, Material bodyMat, Action<Transform> buildDoorContents, Action<GameObject> extraBodyDressing = null)
    {
        const float width = 1.1f;
        const float height = 2.1f;
        const float depth = 0.6f;

        GameObject root = new GameObject(prefabName);

        Box(root.transform, "Body", new Vector3(0f, height * 0.5f, 0.3f), new Vector3(width, height, depth), bodyMat, keepCollider: true);
        extraBodyDressing?.Invoke(root);

        GameObject doorPivot = new GameObject("Door");
        doorPivot.transform.SetParent(root.transform, false);
        doorPivot.transform.localPosition = new Vector3(-width * 0.5f, 0f, 0.62f);
        buildDoorContents(doorPivot.transform);

        Box(root.transform, "Plinth", new Vector3(0f, 0.025f, 0.3f), new Vector3(width + 0.1f, 0.05f, depth + 0.1f), bodyMat);

        BoxCollider trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(1.4f, 2.0f, 1.2f);
        trigger.center = new Vector3(0f, 1.0f, 1.5f);

        GameObject insideAnchor = new GameObject("InsideAnchor");
        insideAnchor.transform.SetParent(root.transform, false);
        insideAnchor.transform.localPosition = new Vector3(0f, 0f, 0.3f);

        GameObject frontAnchor = new GameObject("FrontAnchor");
        frontAnchor.transform.SetParent(root.transform, false);
        frontAnchor.transform.localPosition = new Vector3(0f, 0f, 1.6f);

        Locker locker = root.AddComponent<Locker>();
        SerializedObject so = new SerializedObject(locker);
        so.FindProperty("door").objectReferenceValue = doorPivot.transform;
        so.FindProperty("insideAnchor").objectReferenceValue = insideAnchor.transform;
        so.FindProperty("frontAnchor").objectReferenceValue = frontAnchor.transform;
        so.ApplyModifiedPropertiesWithoutUndo();

        return SaveAsPrefab<Locker>(root, folder, prefabName);
    }

    // ---------------------------------------------------------------- theme 1: the Ward

    private static ThemeAssets BuildWard()
    {
        const string folder = ThemesRoot + "/1_Ward";
        const string tiles = "Assets/SourceFiles/Textures/Repeating Tiles";

        Material wallMat = LoadOrCreateMat(folder, "Ward_Wall", new Color(0.55f, 0.62f, 0.58f), "Assets/SourceFiles/Textures/Grid/Grid_02_BaseMap.png", null, new Vector2(2f, 1f), null);
        Material dadoMat = LoadOrCreateMat(folder, "Ward_Dado", new Color(0.16f, 0.26f, 0.21f), null, null, Vector2.one, null);
        Material floorMat = LoadOrCreateMat(folder, "Ward_Floor", new Color(0.40f, 0.41f, 0.39f), $"{tiles}/Tile_CheckerBoard_Albedo.png", $"{tiles}/Tile_CheckerBoard_normal.png", new Vector2(4f, 4f), null);
        Material ceilingMat = LoadOrCreateMat(folder, "Ward_Ceiling", new Color(0.50f, 0.50f, 0.48f), null, null, Vector2.one, null);
        Material metalMat = LoadOrCreateMat(folder, "Ward_Metal", new Color(0.70f, 0.72f, 0.70f), null, null, Vector2.one, null);
        Color glassColor = new Color(0.85f, 1.0f, 0.95f);
        Material glassMat = LoadOrCreateMat(folder, "Ward_Glass", glassColor, null, null, Vector2.one, glassColor * 2.5f);
        Material linenMat = LoadOrCreateMat(folder, "Ward_Linen", new Color(0.62f, 0.60f, 0.55f), null, null, Vector2.one, null);
        Material crossMat = LoadOrCreateMat(folder, "Ward_Cross", new Color(0.6f, 0.1f, 0.1f), null, null, Vector2.one, null);

        // Wall: body + a dado band, skirting and cornice.
        GameObject wallRoot = new GameObject("Ward_Wall");
        Box(wallRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(5f, 4f, 0.5f), wallMat, keepCollider: true);
        Box(wallRoot.transform, "Dado", new Vector3(0f, 1.1f, 0f), new Vector3(5f, 0.12f, 0.54f), dadoMat);
        Box(wallRoot.transform, "Skirting", new Vector3(0f, 0.075f, 0f), new Vector3(5f, 0.15f, 0.54f), dadoMat);
        Box(wallRoot.transform, "Cornice", new Vector3(0f, 3.95f, 0f), new Vector3(5f, 0.1f, 0.54f), dadoMat);
        WallPiece wall = FinishWall(wallRoot, folder);

        // Pillar: box body with a capital cap.
        GameObject pillarRoot = new GameObject("Ward_Pillar");
        Box(pillarRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(0.7f, 4f, 0.7f), dadoMat, keepCollider: true);
        Box(pillarRoot.transform, "Cap", new Vector3(0f, 3.95f, 0f), new Vector3(0.8f, 0.1f, 0.8f), metalMat);
        GameObject pillar = SaveAsPrefab(pillarRoot, folder, "Ward_Pillar");

        // FloorTile: slab with a drain.
        GameObject floorRoot = new GameObject("Ward_FloorTile");
        Box(floorRoot.transform, "Tile", new Vector3(0f, -0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), floorMat, keepCollider: true);
        CylinderRH(floorRoot.transform, "Drain", new Vector3(1.2f, 0.005f, -1.1f), 0.175f, 0.01f, metalMat);
        GameObject floorTile = SaveAsPrefab(floorRoot, folder, "Ward_FloorTile");

        // CeilingTile: slab with one panel seam.
        GameObject ceilingRoot = new GameObject("Ward_CeilingTile");
        Box(ceilingRoot.transform, "Tile", new Vector3(0f, 0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), ceilingMat, keepCollider: true);
        Box(ceilingRoot.transform, "Seam", new Vector3(0f, -0.005f, 1.5f), new Vector3(TileSize, 0.02f, 0.03f), dadoMat);
        GameObject ceilingTile = SaveAsPrefab(ceilingRoot, folder, "Ward_CeilingTile");

        // Lamp: fluorescent tube.
        GameObject lampRoot = new GameObject("Ward_Lamp");
        Box(lampRoot.transform, "Housing", new Vector3(0f, 0f, 0.06f), new Vector3(1.2f, 0.08f, 0.12f), metalMat);
        GameObject glassGo = Box(lampRoot.transform, "Glass", new Vector3(0f, -0.04f, 0.09f), new Vector3(1.1f, 0.05f, 0.08f), glassMat);
        GameObject lightHolder = new GameObject("Light");
        lightHolder.transform.SetParent(lampRoot.transform, false);
        lightHolder.transform.localPosition = new Vector3(0f, 0f, 0.15f);
        Light lampLight = lightHolder.AddComponent<Light>();
        lampLight.type = LightType.Point;
        lampLight.shadows = LightShadows.None;
        WallLamp lamp = FinishLamp(lampRoot, folder, lampLight, glassGo.GetComponent<Renderer>());

        // Locker: hospital cabinet with a red cross on the bottom slat.
        Locker locker = BuildLockerShell(folder, "Ward_Locker", metalMat,
            doorPivot =>
            {
                GameObject[] slats = BuildDoorSlats(doorPivot, 1.06f, 2.06f, 0.04f, metalMat);
                float crossX = 0.53f;
                Box(doorPivot, "CrossH", new Vector3(crossX, 0.9f, 0.045f), new Vector3(0.3f, 0.06f, 0.01f), crossMat);
                Box(doorPivot, "CrossV", new Vector3(crossX, 0.9f, 0.045f), new Vector3(0.06f, 0.3f, 0.01f), crossMat);
                _ = slats;
            });

        // Props.
        List<PropPiece> props = new List<PropPiece>();

        GameObject gurneyRoot = new GameObject("Gurney");
        Box(gurneyRoot.transform, "Frame", new Vector3(0f, 0.75f, 0.65f), new Vector3(1.9f, 0.06f, 0.65f), metalMat);
        Box(gurneyRoot.transform, "Mattress", new Vector3(0f, 0.84f, 0.65f), new Vector3(1.8f, 0.12f, 0.6f), linenMat, keepCollider: true);
        foreach (float lx in new[] { -0.85f, 0.85f })
        foreach (float lz in new[] { 0.35f, 0.95f })
            Box(gurneyRoot.transform, "Leg", new Vector3(lx, 0.375f, lz), new Vector3(0.04f, 0.75f, 0.04f), metalMat);
        props.Add(FinishProp(gurneyRoot, folder, PropPiece.MountKind.Wall, 0.7f, 2f));

        GameObject ivRoot = new GameObject("IVStand");
        CylinderRH(ivRoot.transform, "Pole", new Vector3(0f, 0.9f, 0.2f), 0.03f, 1.8f, metalMat, keepCollider: true);
        CylinderRH(ivRoot.transform, "Base", new Vector3(0f, 0.01f, 0.2f), 0.2f, 0.02f, metalMat);
        Capsule(ivRoot.transform, "Bag", new Vector3(0f, 1.7f, 0.2f), new Vector3(0.12f, 0.25f, 0.12f), linenMat);
        props.Add(FinishProp(ivRoot, folder, PropPiece.MountKind.Wall, 0.4f, 1f));

        GameObject pipeRoot = new GameObject("WallPipe");
        GameObject pipeBody = CylinderBetween(pipeRoot.transform, "Pipe", new Vector3(-2.25f, 2.2f, 0.1f), new Vector3(2.25f, 2.2f, 0.1f), 0.06f, metalMat, keepCollider: true);
        _ = pipeBody;
        Box(pipeRoot.transform, "BracketL", new Vector3(-1.5f, 2.05f, 0.05f), new Vector3(0.05f, 0.15f, 0.05f), metalMat);
        Box(pipeRoot.transform, "BracketR", new Vector3(1.5f, 2.05f, 0.05f), new Vector3(0.05f, 0.15f, 0.05f), metalMat);
        props.Add(FinishProp(pipeRoot, folder, PropPiece.MountKind.Wall, 0.2f, 1f));

        GameObject ventRoot = new GameObject("Vent");
        Box(ventRoot.transform, "Grille", new Vector3(0f, -0.05f, 0f), new Vector3(0.6f, 0.08f, 0.6f), metalMat, keepCollider: true);
        props.Add(FinishProp(ventRoot, folder, PropPiece.MountKind.Ceiling, 0.1f, 1f));

        return new ThemeAssets
        {
            DisplayName = "The Ward",
            Ambient = new Color(0.10f, 0.12f, 0.11f),
            FogColor = new Color(0.02f, 0.03f, 0.025f),
            FogDensityScale = 1.0f,
            LampColor = glassColor,
            FaultyLampChance = 0.25f,
            WallPropChance = 0.35f,
            CeilingPropChance = 0.15f,
            WallMaterial = wallMat,
            Wall = wall,
            Pillar = pillar,
            FloorTile = floorTile,
            CeilingTile = ceilingTile,
            Lamp = lamp,
            Locker = locker,
            Props = props.ToArray()
        };
    }

    // ---------------------------------------------------------------- theme 3: the Crypt
    // (declared here, ahead of theme 2 below, purely by editing order - Build()'s builder array is what
    // actually fixes the floor order: Ward, BoilerDeck, Crypt, Lab, Hollow.)

    private static ThemeAssets BuildCrypt()
    {
        const string folder = ThemesRoot + "/3_Crypt";
        const string tiles = "Assets/SourceFiles/Textures/Repeating Tiles";

        Material stoneMat = LoadOrCreateMat(folder, "Crypt_Stone", new Color(0.38f, 0.34f, 0.30f), $"{tiles}/Moon_Albedo.png", $"{tiles}/Moon_Normal.png", new Vector2(2f, 2f), null);
        Material floorMat = LoadOrCreateMat(folder, "Crypt_Floor", new Color(0.22f, 0.18f, 0.15f), $"{tiles}/SandTroddenRed_Albedo.tif", $"{tiles}/SandTrodden_Normal.tif", new Vector2(3f, 3f), null);
        Material ceilingMat = LoadOrCreateMat(folder, "Crypt_Ceiling", new Color(0.20f, 0.18f, 0.16f), $"{tiles}/Moon_Albedo.png", $"{tiles}/Moon_Normal.png", new Vector2(2f, 2f), null);
        Material woodMat = LoadOrCreateMat(folder, "Crypt_Wood", new Color(0.20f, 0.15f, 0.11f), null, null, Vector2.one, null);
        Material ironMat = LoadOrCreateMat(folder, "Crypt_Iron", new Color(0.10f, 0.10f, 0.10f), null, null, Vector2.one, null);
        Color flameColor = new Color(1.0f, 0.55f, 0.20f);
        Material flameMat = LoadOrCreateMat(folder, "Crypt_Flame", flameColor, null, null, Vector2.one, flameColor * 3f);
        Material boneMat = LoadOrCreateMat(folder, "Crypt_Bone", new Color(0.60f, 0.56f, 0.48f), null, null, Vector2.one, null);

        GameObject wallRoot = new GameObject("Crypt_Wall");
        Box(wallRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(5f, 4f, 0.5f), stoneMat, keepCollider: true);
        Box(wallRoot.transform, "Ledge", new Vector3(0f, 2.4f, 0f), new Vector3(5f, 0.12f, 0.6f), stoneMat);
        WallPiece wall = FinishWall(wallRoot, folder);

        GameObject pillarRoot = new GameObject("Crypt_Pillar");
        CylinderRH(pillarRoot.transform, "Shaft", new Vector3(0f, 2f, 0f), 0.4f, 4f, stoneMat, keepCollider: true);
        CylinderRH(pillarRoot.transform, "Base", new Vector3(0f, 0.075f, 0f), 0.5f, 0.15f, stoneMat);
        CylinderRH(pillarRoot.transform, "Capital", new Vector3(0f, 3.925f, 0f), 0.5f, 0.15f, stoneMat);
        GameObject pillar = SaveAsPrefab(pillarRoot, folder, "Crypt_Pillar");

        GameObject floorRoot = new GameObject("Crypt_FloorTile");
        Box(floorRoot.transform, "Tile", new Vector3(0f, -0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), floorMat, keepCollider: true);
        GameObject floorTile = SaveAsPrefab(floorRoot, folder, "Crypt_FloorTile");

        GameObject ceilingRoot = new GameObject("Crypt_CeilingTile");
        Box(ceilingRoot.transform, "Tile", new Vector3(0f, 0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), ceilingMat, keepCollider: true);
        GameObject ceilingTile = SaveAsPrefab(ceilingRoot, folder, "Crypt_CeilingTile");

        // Lamp: iron torch, tilted toward the corridor.
        GameObject lampRoot = new GameObject("Crypt_Lamp");
        Box(lampRoot.transform, "Bracket", new Vector3(0f, 0f, 0.03f), new Vector3(0.06f, 0.3f, 0.06f), ironMat);
        CylinderBetween(lampRoot.transform, "Handle", new Vector3(0f, 0f, 0.05f), new Vector3(0f, 0.2f, 0.2f), 0.03f, ironMat);
        GameObject flameGo = Capsule(lampRoot.transform, "Flame", new Vector3(0f, 0.2f, 0.2f), new Vector3(0.12f, 0.2f, 0.12f), flameMat);
        GameObject cryptLightHolder = new GameObject("Light");
        cryptLightHolder.transform.SetParent(lampRoot.transform, false);
        cryptLightHolder.transform.localPosition = new Vector3(0f, 0.25f, 0.22f);
        Light cryptLight = cryptLightHolder.AddComponent<Light>();
        cryptLight.type = LightType.Point;
        cryptLight.shadows = LightShadows.None;
        WallLamp lamp = FinishLamp(lampRoot, folder, cryptLight, flameGo.GetComponent<Renderer>());

        // Locker: standing sarcophagus. Door is two slabs with a look-out gap instead of three slats,
        // but the hinge pivot and its swing are identical to every other theme.
        Locker locker = BuildLockerShell(folder, "Crypt_Locker", stoneMat,
            doorPivot =>
            {
                const float doorWidth = 1.06f;
                const float lowerHeight = 1.5f;
                const float gap = 0.06f;
                const float upperHeight = 2.06f - lowerHeight - gap;
                DoorSlat(doorPivot, "Slab_Lower", doorWidth, lowerHeight, 0.05f, lowerHeight * 0.5f, woodMat);
                DoorSlat(doorPivot, "Slab_Upper", doorWidth, upperHeight, 0.05f, lowerHeight + gap + upperHeight * 0.5f, woodMat);
            },
            root =>
            {
                Box(root.transform, "BevelL", new Vector3(-0.53f, 1.05f, 0.62f), new Vector3(0.04f, 2.1f, 0.02f), woodMat);
                Box(root.transform, "BevelR", new Vector3(0.53f, 1.05f, 0.62f), new Vector3(0.04f, 2.1f, 0.02f), woodMat);
            });

        List<PropPiece> props = new List<PropPiece>();

        GameObject rubbleRoot = new GameObject("Rubble");
        Sphere(rubbleRoot.transform, "Chunk1", new Vector3(-0.2f, 0.18f, 0.3f), new Vector3(0.9f, 0.35f, 0.7f), stoneMat, keepCollider: true);
        Sphere(rubbleRoot.transform, "Chunk2", new Vector3(0.3f, 0.12f, 0.2f), new Vector3(0.5f, 0.22f, 0.4f), stoneMat);
        Sphere(rubbleRoot.transform, "Chunk3", new Vector3(0f, 0.08f, 0.45f), new Vector3(0.35f, 0.16f, 0.3f), stoneMat);
        props.Add(FinishProp(rubbleRoot, folder, PropPiece.MountKind.Wall, 0.6f, 2f));

        GameObject urnRoot = new GameObject("Urn");
        CylinderRH(urnRoot.transform, "Body", new Vector3(0f, 0.3f, 0.2f), 0.2f, 0.6f, stoneMat, keepCollider: true);
        Sphere(urnRoot.transform, "Lip", new Vector3(0f, 0.58f, 0.2f), Vector3.one * 0.45f, stoneMat);
        props.Add(FinishProp(urnRoot, folder, PropPiece.MountKind.Wall, 0.5f, 1f));

        GameObject brokenPillarRoot = new GameObject("BrokenPillar");
        GameObject brokenShaft = CylinderRH(brokenPillarRoot.transform, "Shaft", new Vector3(0f, 0.6f, 0.35f), 0.3f, 1.2f, stoneMat, keepCollider: true);
        brokenShaft.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
        props.Add(FinishProp(brokenPillarRoot, folder, PropPiece.MountKind.Wall, 0.7f, 1f));

        GameObject bonePileRoot = new GameObject("BonePile");
        System.Random boneRng = new System.Random(1);
        for (int i = 0; i < 6; i++)
        {
            Vector3 pos = new Vector3((float)(boneRng.NextDouble() - 0.5) * 0.5f, 0.06f, (float)boneRng.NextDouble() * 0.3f);
            Capsule(bonePileRoot.transform, $"Bone{i}", pos, new Vector3(0.06f, 0.25f, 0.06f), boneMat, keepCollider: i == 0);
        }
        props.Add(FinishProp(bonePileRoot, folder, PropPiece.MountKind.Wall, 0.5f, 1f));

        GameObject chainsRoot = new GameObject("Chains");
        System.Random chainRng = new System.Random(2);
        for (int i = 0; i < 3; i++)
        {
            float cx = (float)(chainRng.NextDouble() - 0.5) * 3.5f;
            float cz = (float)(chainRng.NextDouble() - 0.5) * 3.5f;
            CylinderBetween(chainsRoot.transform, $"Chain{i}", new Vector3(cx, 0f, cz), new Vector3(cx, -1.2f, cz), 0.02f, ironMat, keepCollider: i == 0);
        }
        props.Add(FinishProp(chainsRoot, folder, PropPiece.MountKind.Ceiling, 1.5f, 1f));

        GameObject rootsRoot = new GameObject("Roots");
        System.Random rootRng = new System.Random(3);
        for (int i = 0; i < 4; i++)
        {
            float rx = (float)(rootRng.NextDouble() - 0.5) * 3f;
            float rz = (float)(rootRng.NextDouble() - 0.5) * 3f;
            GameObject root = CylinderBetween(rootsRoot.transform, $"Root{i}", new Vector3(rx, 0f, rz), new Vector3(rx + 0.2f, -0.8f, rz + 0.2f), 0.03f, woodMat, keepCollider: i == 0);
            _ = root;
        }
        props.Add(FinishProp(rootsRoot, folder, PropPiece.MountKind.Ceiling, 0.8f, 1f));

        return new ThemeAssets
        {
            DisplayName = "The Crypt",
            Ambient = new Color(0.07f, 0.06f, 0.05f),
            FogColor = new Color(0.02f, 0.015f, 0.01f),
            FogDensityScale = 1.1f,
            LampColor = flameColor,
            FaultyLampChance = 0.50f,
            WallPropChance = 0.4f,
            CeilingPropChance = 0.25f,
            WallMaterial = stoneMat,
            Wall = wall,
            Pillar = pillar,
            FloorTile = floorTile,
            CeilingTile = ceilingTile,
            Lamp = lamp,
            Locker = locker,
            Props = props.ToArray()
        };
    }

    // ---------------------------------------------------------------- theme 2: Boiler Deck

    private static ThemeAssets BuildBoilerDeck()
    {
        const string folder = ThemesRoot + "/2_BoilerDeck";
        const string tiles = "Assets/SourceFiles/Textures/Repeating Tiles";

        Material wallMat = LoadOrCreateMat(folder, "Boiler_Wall", new Color(0.42f, 0.30f, 0.24f), $"{tiles}/Tile_Hexagon_Grey_Albedo.png", $"{tiles}/Tile_Hexagon_normal.png", new Vector2(2f, 2f), null);
        Material floorMat = LoadOrCreateMat(folder, "Boiler_Floor", new Color(0.22f, 0.20f, 0.18f), $"{tiles}/Tile_Hexagon_Albedo.png", $"{tiles}/Tile_Hexagon_normal.png", new Vector2(3f, 3f), null);
        Material ceilingMat = LoadOrCreateMat(folder, "Boiler_Ceiling", new Color(0.12f, 0.10f, 0.09f), null, null, Vector2.one, null);
        Material ironMat = LoadOrCreateMat(folder, "Boiler_Iron", new Color(0.20f, 0.19f, 0.18f), null, null, Vector2.one, null, smoothness: 0.3f);
        Material rustMat = LoadOrCreateMat(folder, "Boiler_Rust", new Color(0.45f, 0.24f, 0.14f), null, null, Vector2.one, null);
        Color glassColor = new Color(1.0f, 0.62f, 0.30f);
        Material glassMat = LoadOrCreateMat(folder, "Boiler_Glass", glassColor, null, null, Vector2.one, glassColor * 2.5f);

        GameObject wallRoot = new GameObject("Boiler_Wall");
        Box(wallRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(5f, 4f, 0.5f), wallMat, keepCollider: true);
        foreach (float rx in new[] { -2f, -1f, 0f, 1f, 2f })
        {
            Sphere(wallRoot.transform, "RivetTop", new Vector3(rx, 3.9f, 0.26f), Vector3.one * 0.06f, ironMat);
            Sphere(wallRoot.transform, "RivetBottom", new Vector3(rx, 0.1f, 0.26f), Vector3.one * 0.06f, ironMat);
        }
        CylinderBetween(wallRoot.transform, "Pipe", new Vector3(-2.5f, 3.3f, 0.32f), new Vector3(2.5f, 3.3f, 0.32f), 0.08f, rustMat);
        WallPiece wall = FinishWall(wallRoot, folder);

        GameObject pillarRoot = new GameObject("Boiler_Pillar");
        Box(pillarRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(0.7f, 4f, 0.7f), ironMat, keepCollider: true);
        Box(pillarRoot.transform, "FlangeTop", new Vector3(0f, 3.96f, 0f), new Vector3(0.9f, 0.08f, 0.9f), ironMat);
        Box(pillarRoot.transform, "FlangeBottom", new Vector3(0f, 0.04f, 0f), new Vector3(0.9f, 0.08f, 0.9f), ironMat);
        GameObject pillar = SaveAsPrefab(pillarRoot, folder, "Boiler_Pillar");

        GameObject floorRoot = new GameObject("Boiler_FloorTile");
        Box(floorRoot.transform, "Tile", new Vector3(0f, -0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), floorMat, keepCollider: true);
        Box(floorRoot.transform, "Grate", new Vector3(-1.1f, 0.01f, 1.1f), new Vector3(1.2f, 0.02f, 1.2f), ironMat);
        GameObject floorTile = SaveAsPrefab(floorRoot, folder, "Boiler_FloorTile");

        GameObject ceilingRoot = new GameObject("Boiler_CeilingTile");
        Box(ceilingRoot.transform, "Tile", new Vector3(0f, 0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), ceilingMat, keepCollider: true);
        CylinderBetween(ceilingRoot.transform, "Pipe", new Vector3(0f, -0.25f, -2.25f), new Vector3(0f, -0.25f, 2.25f), 0.12f, rustMat);
        GameObject ceilingTile = SaveAsPrefab(ceilingRoot, folder, "Boiler_CeilingTile");

        // Lamp: caged bulkhead.
        GameObject lampRoot = new GameObject("Boiler_Lamp");
        CylinderRH(lampRoot.transform, "Base", new Vector3(0f, 0f, 0.03f), 0.14f, 0.06f, ironMat);
        GameObject glassGo = Sphere(lampRoot.transform, "Glass", new Vector3(0f, 0f, 0.16f), Vector3.one * 0.22f, glassMat);
        for (int i = 0; i < 3; i++)
        {
            float angle = i * 60f;
            Vector3 offset = Quaternion.Euler(0f, 0f, angle) * new Vector3(0.12f, 0f, 0f);
            GameObject bar = Box(lampRoot.transform, "CageBar", new Vector3(offset.x, offset.y, 0.16f), new Vector3(0.02f, 0.3f, 0.02f), ironMat);
            bar.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
        GameObject boilerLightHolder = new GameObject("Light");
        boilerLightHolder.transform.SetParent(lampRoot.transform, false);
        boilerLightHolder.transform.localPosition = new Vector3(0f, 0f, 0.16f);
        Light boilerLight = boilerLightHolder.AddComponent<Light>();
        boilerLight.type = LightType.Point;
        boilerLight.shadows = LightShadows.None;
        WallLamp lamp = FinishLamp(lampRoot, folder, boilerLight, glassGo.GetComponent<Renderer>());

        // Locker: steel locker with vents and a handle.
        Locker locker = BuildLockerShell(folder, "Boiler_Locker", ironMat,
            doorPivot =>
            {
                BuildDoorSlats(doorPivot, 1.06f, 2.06f, 0.04f, ironMat);
                Box(doorPivot, "Vent1", new Vector3(0.53f, 0.4f, 0.045f), new Vector3(0.5f, 0.05f, 0.02f), ironMat);
                Box(doorPivot, "Vent2", new Vector3(0.53f, 0.5f, 0.045f), new Vector3(0.5f, 0.05f, 0.02f), ironMat);
                Box(doorPivot, "Handle", new Vector3(0.98f, 1.0f, 0.05f), new Vector3(0.03f, 0.12f, 0.02f), rustMat);
            });

        List<PropPiece> props = new List<PropPiece>();

        GameObject BuildBarrelBody(Transform parent, float x)
        {
            CylinderRH(parent, "Drum", new Vector3(x, 0.45f, 0.3f), 0.3f, 0.9f, rustMat, keepCollider: true);
            CylinderRH(parent, "RingTop", new Vector3(x, 0.8f, 0.3f), 0.32f, 0.03f, ironMat);
            CylinderRH(parent, "RingBottom", new Vector3(x, 0.1f, 0.3f), 0.32f, 0.03f, ironMat);
            return parent.gameObject;
        }

        GameObject barrelRoot = new GameObject("Barrel");
        BuildBarrelBody(barrelRoot.transform, 0f);
        props.Add(FinishProp(barrelRoot, folder, PropPiece.MountKind.Wall, 0.7f, 2f));

        GameObject barrelPairRoot = new GameObject("BarrelPair");
        BuildBarrelBody(barrelPairRoot.transform, -0.35f);
        BuildBarrelBody(barrelPairRoot.transform, 0.35f);
        props.Add(FinishProp(barrelPairRoot, folder, PropPiece.MountKind.Wall, 0.7f, 1f));

        GameObject valveRoot = new GameObject("ValveWheel");
        GameObject wheel = CylinderRH(valveRoot.transform, "Wheel", new Vector3(0f, 1.4f, 0.25f), 0.25f, 0.04f, rustMat);
        wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        CylinderRH(valveRoot.transform, "Stem", new Vector3(0f, 1.4f, 0.1f), 0.04f, 0.2f, rustMat).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        props.Add(FinishProp(valveRoot, folder, PropPiece.MountKind.Wall, 0.25f, 1f));

        GameObject crateRoot = new GameObject("CrateStack");
        Box(crateRoot.transform, "CrateBottom", new Vector3(0f, 0.3f, 0.3f), Vector3.one * 0.6f, rustMat, keepCollider: true);
        Box(crateRoot.transform, "CrateTop", new Vector3(0.05f, 0.85f, 0.25f), Vector3.one * 0.5f, rustMat);
        props.Add(FinishProp(crateRoot, folder, PropPiece.MountKind.Wall, 0.7f, 1f));

        GameObject ductRoot = new GameObject("Duct");
        Box(ductRoot.transform, "Duct", new Vector3(0f, -0.2f, 0f), new Vector3(0.5f, 0.4f, TileSize), ironMat, keepCollider: true);
        props.Add(FinishProp(ductRoot, folder, PropPiece.MountKind.Ceiling, 0.5f, 1f));

        GameObject steamRoot = new GameObject("SteamValve");
        CylinderRH(steamRoot.transform, "Pipe", new Vector3(0f, -0.3f, 0f), 0.1f, 0.6f, rustMat, keepCollider: true);
        CylinderRH(steamRoot.transform, "Wheel", new Vector3(0f, -0.6f, 0f), 0.15f, 0.04f, rustMat).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        props.Add(FinishProp(steamRoot, folder, PropPiece.MountKind.Ceiling, 0.6f, 1f));

        return new ThemeAssets
        {
            DisplayName = "Boiler Deck",
            Ambient = new Color(0.09f, 0.07f, 0.06f),
            FogColor = new Color(0.03f, 0.02f, 0.015f),
            FogDensityScale = 1.0f,
            LampColor = glassColor,
            FaultyLampChance = 0.30f,
            WallPropChance = 0.45f,
            CeilingPropChance = 0.3f,
            WallMaterial = wallMat,
            Wall = wall,
            Pillar = pillar,
            FloorTile = floorTile,
            CeilingTile = ceilingTile,
            Lamp = lamp,
            Locker = locker,
            Props = props.ToArray()
        };
    }

    // ---------------------------------------------------------------- theme 4: the Lab

    private static ThemeAssets BuildLab()
    {
        const string folder = ThemesRoot + "/4_Lab";
        const string tiles = "Assets/SourceFiles/Textures/Repeating Tiles";
        const string grid = "Assets/SourceFiles/Textures/Grid";

        Material wallMat = LoadOrCreateMat(folder, "Lab_Wall", new Color(0.15f, 0.16f, 0.18f), $"{tiles}/Circuit_Albedo.png", $"{tiles}/Circuit_Normal.png", new Vector2(2f, 2f), null);
        Material floorMat = LoadOrCreateMat(folder, "Lab_Floor", new Color(0.12f, 0.12f, 0.14f), $"{grid}/Grid_01_BaseMap.png", $"{grid}/Grid_01_Normal.png", new Vector2(2f, 2f), null);
        Material ceilingMat = LoadOrCreateMat(folder, "Lab_Ceiling", new Color(0.10f, 0.10f, 0.11f), null, null, Vector2.one, null);
        Material metalMat = LoadOrCreateMat(folder, "Lab_Metal", new Color(0.22f, 0.23f, 0.26f), null, null, Vector2.one, null, smoothness: 0.35f);
        Color glassColor = new Color(1.0f, 0.15f, 0.10f);
        Material glassMat = LoadOrCreateMat(folder, "Lab_Glass", glassColor, null, null, Vector2.one, glassColor * 2.5f);
        Color ledColor = new Color(0.2f, 1.0f, 0.9f);
        Material ledMat = LoadOrCreateMat(folder, "Lab_Led", ledColor, null, null, Vector2.one, ledColor * 2f);
        Material screenMat = LoadOrCreateMat(folder, "Lab_Screen", new Color(0.02f, 0.02f, 0.03f), null, null, Vector2.one, null, smoothness: 0.6f);

        GameObject wallRoot = new GameObject("Lab_Wall");
        Box(wallRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(5f, 4f, 0.5f), wallMat, keepCollider: true);
        Box(wallRoot.transform, "CableTray", new Vector3(0f, 3.6f, 0.35f), new Vector3(5f, 0.08f, 0.3f), metalMat);
        foreach (float cy in new[] { 3.58f, 3.62f, 3.66f })
            CylinderBetween(wallRoot.transform, "Cable", new Vector3(-2.4f, cy, 0.35f), new Vector3(2.4f, cy, 0.35f), 0.02f, metalMat);
        Box(wallRoot.transform, "SeamL", new Vector3(-1.25f, 2f, 0.26f), new Vector3(0.02f, 3.6f, 0.53f), metalMat);
        Box(wallRoot.transform, "SeamR", new Vector3(1.25f, 2f, 0.26f), new Vector3(0.02f, 3.6f, 0.53f), metalMat);
        WallPiece wall = FinishWall(wallRoot, folder);

        GameObject pillarRoot = new GameObject("Lab_Pillar");
        Box(pillarRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(0.7f, 4f, 0.7f), metalMat, keepCollider: true);
        Box(pillarRoot.transform, "LedStrip", new Vector3(0f, 2f, 0.36f), new Vector3(0.02f, 3.6f, 0.02f), ledMat);
        GameObject pillar = SaveAsPrefab(pillarRoot, folder, "Lab_Pillar");

        GameObject floorRoot = new GameObject("Lab_FloorTile");
        Box(floorRoot.transform, "Tile", new Vector3(0f, -0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), floorMat, keepCollider: true);
        GameObject floorTile = SaveAsPrefab(floorRoot, folder, "Lab_FloorTile");

        GameObject ceilingRoot = new GameObject("Lab_CeilingTile");
        Box(ceilingRoot.transform, "Tile", new Vector3(0f, 0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), ceilingMat, keepCollider: true);
        Box(ceilingRoot.transform, "CableTray", new Vector3(0f, -0.2f, 0f), new Vector3(TileSize, 0.06f, 0.2f), metalMat);
        GameObject ceilingTile = SaveAsPrefab(ceilingRoot, folder, "Lab_CeilingTile");

        // Lamp: red emergency box.
        GameObject lampRoot = new GameObject("Lab_Lamp");
        Box(lampRoot.transform, "Housing", new Vector3(0f, 0f, 0.07f), new Vector3(0.32f, 0.16f, 0.14f), metalMat);
        GameObject glassGo = Box(lampRoot.transform, "Glass", new Vector3(0f, 0f, 0.15f), new Vector3(0.26f, 0.1f, 0.02f), glassMat);
        GameObject labLightHolder = new GameObject("Light");
        labLightHolder.transform.SetParent(lampRoot.transform, false);
        labLightHolder.transform.localPosition = new Vector3(0f, 0f, 0.15f);
        Light labLight = labLightHolder.AddComponent<Light>();
        labLight.type = LightType.Point;
        labLight.shadows = LightShadows.None;
        WallLamp lamp = FinishLamp(lampRoot, folder, labLight, glassGo.GetComponent<Renderer>());

        // Locker: containment pod with an LED strip and a small screen.
        Locker locker = BuildLockerShell(folder, "Lab_Locker", metalMat,
            doorPivot =>
            {
                BuildDoorSlats(doorPivot, 1.06f, 2.06f, 0.04f, metalMat);
                Box(doorPivot, "LedStrip", new Vector3(1.02f, 1.0f, 0.045f), new Vector3(0.02f, 1.4f, 0.01f), ledMat);
                Box(doorPivot, "Screen", new Vector3(0.53f, 1.3f, 0.045f), new Vector3(0.2f, 0.12f, 0.01f), screenMat);
            });

        List<PropPiece> props = new List<PropPiece>();

        GameObject rackRoot = new GameObject("ServerRack");
        Box(rackRoot.transform, "Cabinet", new Vector3(0f, 1.0f, 0.3f), new Vector3(0.6f, 2.0f, 0.6f), metalMat, keepCollider: true);
        for (int i = 0; i < 6; i++)
        {
            float ly = 0.3f + i * 0.28f;
            Box(rackRoot.transform, $"Led{i}", new Vector3(0f, ly, 0.61f), new Vector3(0.4f, 0.02f, 0.01f), ledMat);
        }
        props.Add(FinishProp(rackRoot, folder, PropPiece.MountKind.Wall, 0.7f, 2f));

        GameObject tankRoot = new GameObject("Tank");
        CylinderRH(tankRoot.transform, "Base", new Vector3(0f, 0.1f, 0.35f), 0.28f, 0.2f, screenMat, keepCollider: true);
        Capsule(tankRoot.transform, "Body", new Vector3(0f, 0.9f, 0.35f), new Vector3(0.5f, 1.6f, 0.5f), screenMat, keepCollider: true);
        props.Add(FinishProp(tankRoot, folder, PropPiece.MountKind.Wall, 0.7f, 1f));

        GameObject monitorRoot = new GameObject("Monitor");
        Box(monitorRoot.transform, "Screen", new Vector3(0f, 1.6f, 0.03f), new Vector3(0.6f, 0.4f, 0.06f), screenMat, keepCollider: true);
        props.Add(FinishProp(monitorRoot, folder, PropPiece.MountKind.Wall, 0.15f, 1f));

        GameObject cableBundleRoot = new GameObject("CableBundle");
        for (int i = 0; i < 5; i++)
        {
            float cy = 0.3f + i * 0.05f;
            GameObject cable = CylinderBetween(cableBundleRoot.transform, $"Cable{i}", new Vector3(-2.25f, cy, 0.05f), new Vector3(2.25f, cy, 0.05f), 0.02f, metalMat, keepCollider: i == 0);
            _ = cable;
        }
        props.Add(FinishProp(cableBundleRoot, folder, PropPiece.MountKind.Wall, 0.2f, 1f));

        GameObject cableDropRoot = new GameObject("CableDrop");
        System.Random cableRng = new System.Random(4);
        for (int i = 0; i < 3; i++)
        {
            float cx = (float)(cableRng.NextDouble() - 0.5) * 2f;
            float cz = (float)(cableRng.NextDouble() - 0.5) * 2f;
            CylinderBetween(cableDropRoot.transform, $"Cable{i}", new Vector3(cx, 0f, cz), new Vector3(cx + 0.1f, -1.2f, cz), 0.015f, metalMat, keepCollider: i == 0);
        }
        props.Add(FinishProp(cableDropRoot, folder, PropPiece.MountKind.Ceiling, 1.2f, 1f));

        GameObject fanRoot = new GameObject("Fan");
        CylinderRH(fanRoot.transform, "Housing", new Vector3(0f, -0.05f, 0f), 0.35f, 0.1f, metalMat, keepCollider: true);
        for (int i = 0; i < 4; i++)
        {
            GameObject blade = Box(fanRoot.transform, $"Blade{i}", new Vector3(0f, -0.05f, 0f), new Vector3(0.6f, 0.02f, 0.1f), metalMat);
            blade.transform.localRotation = Quaternion.Euler(0f, i * 45f, 0f);
        }
        props.Add(FinishProp(fanRoot, folder, PropPiece.MountKind.Ceiling, 0.3f, 1f));

        return new ThemeAssets
        {
            DisplayName = "The Lab",
            Ambient = new Color(0.05f, 0.03f, 0.03f),
            FogColor = new Color(0.02f, 0.008f, 0.008f),
            FogDensityScale = 1.15f,
            LampColor = glassColor,
            FaultyLampChance = 0.40f,
            WallPropChance = 0.4f,
            CeilingPropChance = 0.3f,
            WallMaterial = wallMat,
            Wall = wall,
            Pillar = pillar,
            FloorTile = floorTile,
            CeilingTile = ceilingTile,
            Lamp = lamp,
            Locker = locker,
            Props = props.ToArray()
        };
    }

    // ---------------------------------------------------------------- theme 5: the Hollow

    private static ThemeAssets BuildHollow()
    {
        const string folder = ThemesRoot + "/5_Hollow";
        const string tiles = "Assets/SourceFiles/Textures/Repeating Tiles";

        Material wallMat = LoadOrCreateMat(folder, "Hollow_Wall", new Color(0.10f, 0.09f, 0.11f), $"{tiles}/Runes_Albedo.png", $"{tiles}/Runes_Normal.png", new Vector2(2f, 2f), new Color(0.06f, 0.02f, 0.10f));
        Material floorMat = LoadOrCreateMat(folder, "Hollow_Floor", new Color(0.04f, 0.04f, 0.05f), null, null, Vector2.one, null, smoothness: 0.4f);
        Material ceilingMat = LoadOrCreateMat(folder, "Hollow_Ceiling", new Color(0.02f, 0.02f, 0.025f), null, null, Vector2.one, null);
        Material woodMat = LoadOrCreateMat(folder, "Hollow_Wood", new Color(0.06f, 0.05f, 0.05f), null, null, Vector2.one, null);
        Material clothMat = LoadOrCreateMat(folder, "Hollow_Cloth", new Color(0.12f, 0.10f, 0.10f), null, null, Vector2.one, null);
        Color glassColor = new Color(1.0f, 0.90f, 0.75f);
        Material glassMat = LoadOrCreateMat(folder, "Hollow_Glass", glassColor, null, null, Vector2.one, glassColor * 2.5f);
        Material mirrorMat = LoadOrCreateMat(folder, "Hollow_Mirror", new Color(0.05f, 0.05f, 0.06f), null, null, Vector2.one, null, smoothness: 0.9f);
        if (mirrorMat.HasProperty("_Metallic")) mirrorMat.SetFloat("_Metallic", 0.8f);

        // Wall: body only, no dressing - the runes carry it.
        GameObject wallRoot = new GameObject("Hollow_Wall");
        Box(wallRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(5f, 4f, 0.5f), wallMat, keepCollider: true);
        WallPiece wall = FinishWall(wallRoot, folder);

        GameObject pillarRoot = new GameObject("Hollow_Pillar");
        Box(pillarRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(0.6f, 4f, 0.6f), wallMat, keepCollider: true);
        GameObject pillar = SaveAsPrefab(pillarRoot, folder, "Hollow_Pillar");

        GameObject floorRoot = new GameObject("Hollow_FloorTile");
        Box(floorRoot.transform, "Tile", new Vector3(0f, -0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), floorMat, keepCollider: true);
        GameObject floorTile = SaveAsPrefab(floorRoot, folder, "Hollow_FloorTile");

        GameObject ceilingRoot = new GameObject("Hollow_CeilingTile");
        Box(ceilingRoot.transform, "Tile", new Vector3(0f, 0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), ceilingMat, keepCollider: true);
        GameObject ceilingTile = SaveAsPrefab(ceilingRoot, folder, "Hollow_CeilingTile");

        // Lamp: bare bulb hanging off an arm. Root stays at the wall mount; the 0.5 m offset to the bulb
        // is within ExposureAt's distance tolerance (decision/plan note under Theme 5's lamp row).
        GameObject lampRoot = new GameObject("Hollow_Lamp");
        CylinderBetween(lampRoot.transform, "Arm", new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0.5f), 0.02f, woodMat);
        CylinderBetween(lampRoot.transform, "Wire", new Vector3(0f, 0f, 0.5f), new Vector3(0f, -0.3f, 0.5f), 0.008f, woodMat);
        GameObject glassGo = Sphere(lampRoot.transform, "Bulb", new Vector3(0f, -0.3f, 0.5f), Vector3.one * 0.14f, glassMat);
        GameObject hollowLightHolder = new GameObject("Light");
        hollowLightHolder.transform.SetParent(lampRoot.transform, false);
        hollowLightHolder.transform.localPosition = new Vector3(0f, -0.3f, 0.5f);
        Light hollowLight = hollowLightHolder.AddComponent<Light>();
        hollowLight.type = LightType.Point;
        hollowLight.shadows = LightShadows.None;
        WallLamp lamp = FinishLamp(lampRoot, folder, hollowLight, glassGo.GetComponent<Renderer>());

        // Locker: black wardrobe. Left panel is the hinged, slatted door (narrower than the standard
        // width); right is a fixed mirror strip on the body itself, not on the swinging door.
        Locker locker = BuildLockerShell(folder, "Hollow_Locker", woodMat,
            doorPivot =>
            {
                BuildDoorSlats(doorPivot, 0.74f, 2.06f, 0.04f, woodMat);
            },
            root =>
            {
                Box(root.transform, "MirrorPanel", new Vector3(0.38f, 1.05f, 0.62f), new Vector3(0.3f, 2.0f, 0.03f), mirrorMat);
            });

        List<PropPiece> props = new List<PropPiece>();

        GameObject mirrorRoot = new GameObject("Mirror");
        Box(mirrorRoot.transform, "Frame", new Vector3(0f, 1.5f, 0.03f), new Vector3(0.9f, 1.4f, 0.06f), woodMat);
        Box(mirrorRoot.transform, "Glass", new Vector3(0f, 1.5f, 0.06f), new Vector3(0.8f, 1.3f, 0.01f), mirrorMat);
        props.Add(FinishProp(mirrorRoot, folder, PropPiece.MountKind.Wall, 0.1f, 1f));

        GameObject shroudRoot = new GameObject("Shroud");
        GameObject shroudBox = Box(shroudRoot.transform, "Cloth", new Vector3(0f, 1.1f, 0.15f), new Vector3(0.8f, 2.2f, 0.05f), clothMat);
        shroudBox.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
        props.Add(FinishProp(shroudRoot, folder, PropPiece.MountKind.Wall, 0.3f, 2f));

        GameObject effigyRoot = new GameObject("Effigy");
        Capsule(effigyRoot.transform, "Body", new Vector3(0f, 0.85f, 0.25f), new Vector3(0.5f, 1.7f, 0.5f), clothMat, keepCollider: true);
        Sphere(effigyRoot.transform, "Head", new Vector3(0f, 1.75f, 0.25f), Vector3.one * 0.3f, clothMat);
        // Off by default: PhantomDirector already does silhouettes, and a static standing figure would
        // confuse that read. The artist can flip enabledInMaze back on for a given theme if wanted.
        props.Add(FinishProp(effigyRoot, folder, PropPiece.MountKind.Wall, 0.5f, 1f, enabledInMaze: false));

        GameObject hangingClothRoot = new GameObject("HangingCloth");
        Box(hangingClothRoot.transform, "Cloth", new Vector3(0f, -0.8f, 0f), new Vector3(0.6f, 1.6f, 0.04f), clothMat, keepCollider: true);
        props.Add(FinishProp(hangingClothRoot, folder, PropPiece.MountKind.Ceiling, 1.6f, 1f));

        GameObject bellRoot = new GameObject("Bell");
        CylinderRH(bellRoot.transform, "Body", new Vector3(0f, -0.15f, 0f), 0.2f, 0.3f, woodMat, keepCollider: true);
        props.Add(FinishProp(bellRoot, folder, PropPiece.MountKind.Ceiling, 0.6f, 1f));

        return new ThemeAssets
        {
            DisplayName = "The Hollow",
            Ambient = new Color(0.035f, 0.03f, 0.045f),
            FogColor = new Color(0.01f, 0.01f, 0.015f),
            FogDensityScale = 1.0f,
            LampColor = glassColor,
            FaultyLampChance = 0.60f,
            WallPropChance = 0.3f,
            CeilingPropChance = 0.2f,
            WallMaterial = wallMat,
            Wall = wall,
            Pillar = pillar,
            FloorTile = floorTile,
            CeilingTile = ceilingTile,
            Lamp = lamp,
            Locker = locker,
            Props = props.ToArray()
        };
    }

    // ---------------------------------------------------------------- row assembly

    /// <summary>
    /// Builds one floor's gallery row: a showcase instance of every piece (each a real prefab instance,
    /// via PrefabUtility.InstantiatePrefab, so the artist can select and override them like any other
    /// scene object), a SampleCell mock-up, and the FloorTheme component whose fields point at the
    /// showcase instances - not the SampleCell copies, per decision 1 in the plan.
    /// </summary>
    private static FloorTheme BuildRow(Transform gallery, string rowName, int rowIndex, ThemeAssets assets)
    {
        GameObject rowGo = new GameObject(rowName);
        rowGo.transform.SetParent(gallery, false);
        rowGo.transform.localPosition = new Vector3(0f, 0f, rowIndex * RowSpacing);

        float x = 8f;
        WallPiece wallInstance = InstantiateShowcase(rowGo.transform, assets.Wall, ref x);
        GameObject pillarInstance = InstantiateShowcase(rowGo.transform, assets.Pillar, ref x);
        GameObject floorInstance = InstantiateShowcase(rowGo.transform, assets.FloorTile, ref x);
        GameObject ceilingInstance = InstantiateShowcase(rowGo.transform, assets.CeilingTile, ref x);
        WallLamp lampInstance = InstantiateShowcase(rowGo.transform, assets.Lamp, ref x);
        Locker lockerInstance = InstantiateShowcase(rowGo.transform, assets.Locker, ref x);

        List<PropPiece> propInstances = new List<PropPiece>();
        if (assets.Props != null)
        {
            foreach (PropPiece propPrefab in assets.Props)
            {
                propInstances.Add(InstantiateShowcase(rowGo.transform, propPrefab, ref x));
            }
        }

        BuildSampleCell(rowGo.transform, assets);

        FloorTheme theme = rowGo.AddComponent<FloorTheme>();
        SerializedObject so = new SerializedObject(theme);
        so.FindProperty("displayName").stringValue = assets.DisplayName;
        so.FindProperty("ambient").colorValue = assets.Ambient;
        so.FindProperty("fogColor").colorValue = assets.FogColor;
        so.FindProperty("fogDensityScale").floatValue = assets.FogDensityScale;
        so.FindProperty("lampColor").colorValue = assets.LampColor;
        so.FindProperty("faultyLampChance").floatValue = assets.FaultyLampChance;
        so.FindProperty("wall").objectReferenceValue = wallInstance;
        so.FindProperty("pillar").objectReferenceValue = pillarInstance;
        so.FindProperty("floorTile").objectReferenceValue = floorInstance;
        so.FindProperty("ceilingTile").objectReferenceValue = ceilingInstance;
        so.FindProperty("lamp").objectReferenceValue = lampInstance;
        so.FindProperty("locker").objectReferenceValue = lockerInstance;

        SerializedProperty propsProp = so.FindProperty("props");
        propsProp.arraySize = propInstances.Count;
        for (int i = 0; i < propInstances.Count; i++)
        {
            propsProp.GetArrayElementAtIndex(i).objectReferenceValue = propInstances[i];
        }

        so.FindProperty("wallPropChance").floatValue = assets.WallPropChance;
        so.FindProperty("ceilingPropChance").floatValue = assets.CeilingPropChance;
        so.FindProperty("wallMaterial").objectReferenceValue = assets.WallMaterial;
        so.ApplyModifiedPropertiesWithoutUndo();

        return theme;
    }

    /// <summary>Instantiates a prefab as a showcase piece in the row, on a small dark plinth so it reads clearly in the Scene view, and advances x by PieceSpacing.</summary>
    private static T InstantiateShowcase<T>(Transform row, T prefab, ref float x) where T : Component
    {
        if (prefab == null) return null;
        GameObject instance = InstantiateShowcaseGo(row, prefab.gameObject, ref x);
        return instance.GetComponent<T>();
    }

    /// <summary>GameObject-only overload for pieces with no distinguishing component of their own (Pillar, FloorTile, CeilingTile).</summary>
    private static GameObject InstantiateShowcase(Transform row, GameObject prefab, ref float x)
    {
        return prefab == null ? null : InstantiateShowcaseGo(row, prefab, ref x);
    }

    private static GameObject InstantiateShowcaseGo(Transform row, GameObject prefab, ref float x)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, row);
        instance.transform.localPosition = new Vector3(x, 0f, 0f);
        instance.name = prefab.name;

        GameObject plinth = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plinth.name = "Plinth";
        plinth.transform.SetParent(row, false);
        plinth.transform.localPosition = new Vector3(x, -0.05f, 0f);
        plinth.transform.localScale = new Vector3(2.2f, 0.1f, 2.2f);
        UnityEngine.Object.DestroyImmediate(plinth.GetComponent<Collider>());
        plinth.GetComponent<MeshRenderer>().sharedMaterial = PlinthMaterial();

        x += PieceSpacing;
        return instance;
    }

    /// <summary>
    /// A real asset (Assets/SourceFiles/Themes/Materials/GalleryPlinth.mat), not an in-memory material:
    /// the plinths are scene objects, and a scene that references a material that exists only in memory
    /// loses it on reload and shows pink.
    /// </summary>
    private static Material PlinthMaterial()
    {
        if (_plinthMaterial != null) return _plinthMaterial;

        _plinthMaterial = LoadOrCreateMat(ThemesRoot, "GalleryPlinth", new Color(0.03f, 0.03f, 0.03f), null, null, Vector2.one, null);
        return _plinthMaterial;
    }

    /// <summary>A one-cell mock-up assembled from a second set of prefab instances (kept separate from the row's showcase instances, which is what FloorTheme actually points at) so the artist can see how the pieces read together as a corridor corner.</summary>
    private static void BuildSampleCell(Transform row, ThemeAssets assets)
    {
        Transform cell = Group(row, "SampleCell");
        cell.localPosition = Vector3.zero;

        const float half = TileSize * 0.5f;
        const float lampHeight = 2.6f;

        if (assets.FloorTile != null)
        {
            GameObject floor = (GameObject)PrefabUtility.InstantiatePrefab(assets.FloorTile, cell);
            floor.transform.localPosition = Vector3.zero;
            floor.name = "FloorTile";
        }

        if (assets.CeilingTile != null)
        {
            GameObject ceiling = (GameObject)PrefabUtility.InstantiatePrefab(assets.CeilingTile, cell);
            ceiling.transform.localPosition = new Vector3(0f, WallHeight, 0f);
            ceiling.name = "CeilingTile";
        }

        if (assets.Wall != null)
        {
            GameObject southWall = (GameObject)PrefabUtility.InstantiatePrefab(assets.Wall.gameObject, cell);
            southWall.transform.localPosition = new Vector3(0f, 0f, -half);
            southWall.transform.localRotation = Quaternion.identity;
            southWall.name = "SouthWall";

            GameObject westWall = (GameObject)PrefabUtility.InstantiatePrefab(assets.Wall.gameObject, cell);
            westWall.transform.localPosition = new Vector3(-half, 0f, 0f);
            westWall.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            westWall.name = "WestWall";
        }

        if (assets.Pillar != null)
        {
            GameObject pillar = (GameObject)PrefabUtility.InstantiatePrefab(assets.Pillar, cell);
            pillar.transform.localPosition = new Vector3(-half, 0f, -half);
            pillar.transform.localScale = new Vector3(1f, WallHeight / 4f, 1f);
            pillar.name = "Pillar";
        }

        if (assets.Lamp != null)
        {
            GameObject lamp = (GameObject)PrefabUtility.InstantiatePrefab(assets.Lamp.gameObject, cell);
            Vector3 wallDir = Vector3.left; // on the west wall
            lamp.transform.localPosition = wallDir * (BackFaceDistance - 0.08f) + Vector3.up * lampHeight;
            lamp.transform.localRotation = Quaternion.LookRotation(-wallDir, Vector3.up);
            lamp.name = "Lamp";
        }

        if (assets.Locker != null)
        {
            GameObject locker = (GameObject)PrefabUtility.InstantiatePrefab(assets.Locker.gameObject, cell);
            Vector3 openDir = Vector3.forward; // on the south wall, door faces into the cell
            locker.transform.localPosition = -openDir * BackFaceDistance;
            locker.transform.localRotation = Quaternion.LookRotation(openDir, Vector3.up);
            locker.name = "Locker";
        }

        if (assets.Props != null)
        {
            foreach (PropPiece prop in assets.Props)
            {
                if (prop == null || prop.Mount != PropPiece.MountKind.Wall) continue;

                GameObject propInstance = (GameObject)PrefabUtility.InstantiatePrefab(prop.gameObject, cell);
                Vector3 dir = Vector3.left; // west wall, offset along +Z so it clears the lamp
                propInstance.transform.localPosition = dir * BackFaceDistance + Vector3.forward * 1.3f;
                propInstance.transform.localRotation = Quaternion.LookRotation(-dir);
                propInstance.name = "Prop";
                break; // one wall prop is enough for the mock-up
            }
        }
    }
}

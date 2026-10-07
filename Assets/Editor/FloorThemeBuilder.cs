using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LIGHTS OUT &gt; Build &gt; Floor Themes. Generates five floors' worth of themed set-dressing - materials
/// and prefabs under Assets/SourceFiles/Themes, and a scene gallery (FloorThemes) with one live prefab
/// instance of each piece per floor - so an artist can inspect and retouch every piece MazeGenerator
/// clones at Play time. Nothing here runs in a build; at Play time MazeGenerator.ResolveTheme just finds
/// what this made and clones the scene instances (see decision 1 in plannings/floor-themes-plan.md).
///
/// Rebuilding never silently loses work: materials and prefabs are loaded if they already exist
/// (LoadOrCreate, same pattern as ShopRoomBuilder), and the whole Themes folder is only ever deleted
/// when the artist explicitly picks "Replace everything".
/// </summary>
public static partial class FloorThemeBuilder
{
    private const string ThemesRoot = "Assets/SourceFiles/Themes";
    private const string GalleryName = "FloorThemes";
    private static readonly Vector3 GalleryOrigin = new Vector3(-70f, 5f, -6f);
    private const float RowSpacing = 12f;
    internal const float PieceSpacing = 6f;

    // Canonical authored sizes. WallPiece.ScaleFor divides by these; FloorTile/CeilingTile are scaled by
    // MazeGenerator using cellSize / TileSize. The gallery and SampleCell show every piece at scale 1.
    private const float WallHeight = 4f;
    private const float TileSize = 4.5f;

    // cellSize/2 - wallThickness/2 at the canonical 4.5 m cell / 0.5 m wall the pieces are authored for.
    private const float BackFaceDistance = 2.0f;

    /// <summary>
    /// How far a set piece's wall decal stands off the wall. MazeGenerator.BuildSetPieces puts a set piece's
    /// pivot ON the wall face with +Z into the cell, so a wall decal belongs just in front of z = 0. It used to
    /// be 1.98 (as if the wall were 2 m ahead), which hung every set-piece wall decal in mid-air - fixed 7 Oct 2026;
    /// SetPieceDecalFix moves the ones in prefabs built before that.
    /// </summary>
    internal const float WallDecalInset = 0.02f;

    private static Material _plinthMaterial;

    [MenuItem("LIGHTS OUT/Build/Floor Themes", priority = 100)]
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

        BuildSilently(replaceAssets: choice == 2);
    }

    /// <summary>No dialogs: replaces any existing gallery, keeping the theme assets unless replaceAssets. Used by LIGHTS OUT &gt; Build Missing Pieces.</summary>
    public static void BuildSilently(bool replaceAssets = false)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("Build Floor Themes: open the game scene first.");
            return;
        }

        FloorThemeSet existingSet = UnityEngine.Object.FindAnyObjectByType<FloorThemeSet>(FindObjectsInactive.Include);
        if (existingSet != null) Undo.DestroyObjectImmediate(existingSet.gameObject);
        if (replaceAssets && AssetDatabase.IsValidFolder(ThemesRoot)) AssetDatabase.DeleteAsset(ThemesRoot);
        EnsureFolder(ThemesRoot);
        // The cached plinth material, and every F70 dressing cache under Themes/Common, may point at an
        // asset the line above just deleted.
        _plinthMaterial = null;
        ResetDressingCaches();

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

        // The builders' ambient colours predate the F71 darkness pass; bring the fresh rows down to it.
        DarknessRelight.Relight();
        // The drawn blood decals are only placeholders when the Blood decal pack is in the project.
        BloodDecalUpgrade.Apply();
        SetPieceDecalFix.Apply();
        CorpseUpgrade.Apply();
        PackDressing.Apply(); // F85: any pack piece a hand-edited row or an older scene lacks
        PrimitiveDressingCleanup.Apply(); // F89: no untextured primitive dressing in an older or hand-edited row
        RemainingPrimitivesCleanup.Apply(); // F90: no untextured wall trims or plain pillars; kit patches / note / shard on their new looks
        ThemeSurfaceUpgrade.Apply(); // F88: real PBR surface textures on the structural theme materials

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

        // F70.
        public float FloorPropChance;
        public float WallDecalChance;
        public float FloorDecalChance;
        public int MaxDecalsPerCell;
        public SetPiece[] SetPieces;
        public int SetPieceCount;
        public DustMotes Dust;
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
        Material pillarMat = LoadOrCreateMat(folder, "Ward_Pillar", new Color(0.56f, 0.64f, 0.60f), null, null, Vector2.one, null); // F90: the wall's tile set at a column's tiling (ThemeSurfaceUpgrade)
        Material floorMat = LoadOrCreateMat(folder, "Ward_Floor", new Color(0.40f, 0.41f, 0.39f), $"{tiles}/Tile_CheckerBoard_Albedo.png", $"{tiles}/Tile_CheckerBoard_normal.png", new Vector2(4f, 4f), null);
        Material ceilingMat = LoadOrCreateMat(folder, "Ward_Ceiling", new Color(0.50f, 0.50f, 0.48f), null, null, Vector2.one, null);
        Material metalMat = LoadOrCreateMat(folder, "Ward_Metal", new Color(0.70f, 0.72f, 0.70f), null, null, Vector2.one, null);
        Color glassColor = new Color(0.85f, 1.0f, 0.95f);
        Material glassMat = LoadOrCreateMat(folder, "Ward_Glass", glassColor, null, null, Vector2.one, glassColor * 2.5f);
        Material crossMat = LoadOrCreateMat(folder, "Ward_Cross", new Color(0.6f, 0.1f, 0.1f), null, null, Vector2.one, null);

        // Wall: just the textured body (the dado band, skirting and cornice were untextured trims - F90).
        GameObject wallRoot = new GameObject("Ward_Wall");
        Box(wallRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(5f, 4f, 0.5f), wallMat, keepCollider: true);
        WallPiece wall = FinishWall(wallRoot, folder);

        // Pillar: a plain textured column (the metal cap was an untextured trim - F90).
        GameObject pillarRoot = new GameObject("Ward_Pillar");
        Box(pillarRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(0.7f, 4f, 0.7f), pillarMat, keepCollider: true);
        GameObject pillar = SaveAsPrefab(pillarRoot, folder, "Ward_Pillar");

        // FloorTile: slab (the old primitive drain disc is gone - F89).
        GameObject floorRoot = new GameObject("Ward_FloorTile");
        Box(floorRoot.transform, "Tile", new Vector3(0f, -0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), floorMat, keepCollider: true);
        GameObject floorTile = SaveAsPrefab(floorRoot, folder, "Ward_FloorTile");

        // CeilingTile: slab.
        GameObject ceilingRoot = new GameObject("Ward_CeilingTile");
        Box(ceilingRoot.transform, "Tile", new Vector3(0f, 0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), ceilingMat, keepCollider: true);
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

        // ---- F70: additional props (plan section 3.3, Ward table) ----

        props.AddRange(BuildCommonFloorProps());
        props.AddRange(BuildAlchemistProps(ThemeSets[0])); // F79
        props.AddRange(BuildPackProps(PackWard)); // F85

        CommonDecalMaterials wardCommonDecals = GetCommonDecalMaterials();
        Material wardGrimeMat = LoadOrCreateDecalMat(folder, "Ward_Grime", EnsureDecalTexture("Decal_Grime"), new Color(0.1f, 0.14f, 0.1f), 0.1f);
        Material wardDripMat = LoadOrCreateDecalMat(folder, "Ward_Drip", EnsureDecalTexture("Decal_Drip"), new Color(0.14f, 0.16f, 0.12f), 0.15f);
        Material wardFloorGrimeMat = LoadOrCreateDecalMat(folder, "Ward_FloorGrime", EnsureDecalTexture("Decal_Grime"), new Color(0.1f, 0.11f, 0.1f), 0.1f);
        props.AddRange(BuildThemeDecals(folder, "Ward", wardCommonDecals, wardGrimeMat, wardDripMat, wardFloorGrimeMat));

        // ---- F70: set pieces (plan section 3.4) ----

        List<SetPiece> setPieces = new List<SetPiece>
        {
            BuildFallenRunnerSetPiece(wardCommonDecals)
        };
        setPieces.AddRange(BuildPackSetPieces(PackWard)); // F85

        DustMotes dust = BuildDustPrefab(folder, "Ward_Dust", new Color(0.8f, 0.8f, 0.75f), 25f, 0.015f, 0.03f);

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
            Props = props.ToArray(),
            FloorPropChance = 0.3f,
            WallDecalChance = 0.45f,
            FloorDecalChance = 0.3f,
            MaxDecalsPerCell = 2,
            SetPieces = setPieces.ToArray(),
            SetPieceCount = 3,
            Dust = dust
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

        // ---- F70: additional props (plan section 3.3, Crypt table) ----

        Material cobwebMat = LoadOrCreateDecalMat(folder, "Crypt_Cobweb", EnsureDecalTexture("Decal_Grime"), new Color(0.5f, 0.5f, 0.48f), 0.05f);
        GameObject cobwebRoot = new GameObject("Cobweb");
        for (int wi = 0; wi < 4; wi++)
        {
            CreateDecalQuad(cobwebRoot.transform, $"Strand{wi}", cobwebMat, true, new Vector3(0f, -0.05f, 0f), Quaternion.Euler(0f, wi * 45f, 0f), 0.5f);
        }
        props.Add(FinishProp(cobwebRoot, folder, PropPiece.MountKind.Ceiling, 0.5f, 1f));

        props.AddRange(BuildCommonFloorProps());
        props.AddRange(BuildAlchemistProps(ThemeSets[2])); // F79
        props.AddRange(BuildPackProps(PackCrypt)); // F85

        CommonDecalMaterials cryptCommonDecals = GetCommonDecalMaterials();
        Material cryptGrimeMat = LoadOrCreateDecalMat(folder, "Crypt_Grime", EnsureDecalTexture("Decal_Grime"), new Color(0.08f, 0.1f, 0.06f), 0.05f);
        Material cryptDripMat = LoadOrCreateDecalMat(folder, "Crypt_Drip", EnsureDecalTexture("Decal_Drip"), new Color(0.1f, 0.12f, 0.07f), 0.1f);
        Material cryptFloorGrimeMat = LoadOrCreateDecalMat(folder, "Crypt_FloorGrime", EnsureDecalTexture("Decal_Grime"), new Color(0.07f, 0.09f, 0.05f), 0.05f);
        props.AddRange(BuildThemeDecals(folder, "Crypt", cryptCommonDecals, cryptGrimeMat, cryptDripMat, cryptFloorGrimeMat));

        // ---- F70: set pieces (plan section 3.4) ----

        SetPiece BuildCryptShrine()
        {
            GameObject root = new GameObject("Crypt_Shrine");
            DressShrine(root); // pack altar, candles and skulls (F89)
            CreateDecalQuad(root.transform, "DripDecal", cryptCommonDecals.BloodPool, false, new Vector3(0f, 2.1f, WallDecalInset), Quaternion.identity, 0.9f);

            GameObject lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.1f, 0.4f);
            Light shrineLight = lightGo.AddComponent<Light>();
            Color candleFlameColor = new Color(1.0f, 0.55f, 0.2f);
            shrineLight.type = LightType.Point;
            shrineLight.range = 3.5f;
            shrineLight.intensity = 0.7f;
            shrineLight.color = candleFlameColor;
            shrineLight.shadows = LightShadows.None;
            ConfigureFlicker(root, FlickerLight.FlickerMode.Candle, shrineLight, null);

            return FinishSetPiece(root, folder, width: 1.6f, depth: 0.9f, weight: 1f);
        }

        List<SetPiece> setPieces = new List<SetPiece>
        {
            BuildCryptShrine(),
            BuildFallenRunnerSetPiece(cryptCommonDecals)
        };
        setPieces.AddRange(BuildPackSetPieces(PackCrypt)); // F85

        DustMotes dust = BuildDustPrefab(folder, "Crypt_Dust", new Color(0.7f, 0.68f, 0.6f), 50f, 0.015f, 0.03f);

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
            Props = props.ToArray(),
            FloorPropChance = 0.35f,
            WallDecalChance = 0.5f,
            FloorDecalChance = 0.35f,
            MaxDecalsPerCell = 2,
            SetPieces = setPieces.ToArray(),
            SetPieceCount = 3,
            Dust = dust
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
        Material pillarMat = LoadOrCreateMat(folder, "Boiler_Pillar", new Color(0.42f, 0.30f, 0.24f), null, null, Vector2.one, null); // F90: wall plates at a column's tiling
        Color glassColor = new Color(1.0f, 0.62f, 0.30f);
        Material glassMat = LoadOrCreateMat(folder, "Boiler_Glass", glassColor, null, null, Vector2.one, glassColor * 2.5f);

        GameObject wallRoot = new GameObject("Boiler_Wall");
        Box(wallRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(5f, 4f, 0.5f), wallMat, keepCollider: true);
        WallPiece wall = FinishWall(wallRoot, folder);

        GameObject pillarRoot = new GameObject("Boiler_Pillar");
        Box(pillarRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(0.7f, 4f, 0.7f), pillarMat, keepCollider: true);
        GameObject pillar = SaveAsPrefab(pillarRoot, folder, "Boiler_Pillar");

        GameObject floorRoot = new GameObject("Boiler_FloorTile");
        Box(floorRoot.transform, "Tile", new Vector3(0f, -0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), floorMat, keepCollider: true);
        GameObject floorTile = SaveAsPrefab(floorRoot, folder, "Boiler_FloorTile");

        GameObject ceilingRoot = new GameObject("Boiler_CeilingTile");
        Box(ceilingRoot.transform, "Tile", new Vector3(0f, 0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), ceilingMat, keepCollider: true);
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

        // ---- F70: additional props (plan section 3.3, Boiler Deck table) ----

        props.AddRange(BuildCommonFloorProps());
        props.AddRange(BuildAlchemistProps(ThemeSets[1])); // F79
        props.AddRange(BuildPackProps(PackBoiler)); // F85

        CommonDecalMaterials boilerCommonDecals = GetCommonDecalMaterials();
        Material boilerGrimeMat = LoadOrCreateDecalMat(folder, "Boiler_Grime", EnsureDecalTexture("Decal_Grime"), new Color(0.05f, 0.04f, 0.03f), 0.2f);
        Material boilerDripMat = LoadOrCreateDecalMat(folder, "Boiler_Drip", EnsureDecalTexture("Decal_Drip"), new Color(0.08f, 0.05f, 0.02f), 0.3f);
        Material boilerFloorGrimeMat = LoadOrCreateDecalMat(folder, "Boiler_FloorGrime", EnsureDecalTexture("Decal_Grime"), new Color(0.04f, 0.03f, 0.02f), 0.9f);
        props.AddRange(BuildThemeDecals(folder, "Boiler", boilerCommonDecals, boilerGrimeMat, boilerDripMat, boilerFloorGrimeMat));

        // ---- F70: set pieces (plan section 3.4) ----

        SetPiece BuildBoilerSparks()
        {
            GameObject root = new GameObject("Boiler_Sparks");
            DressSparks(root); // Socket Pack fuse box (F89)
            Color sparkColor = new Color(1.0f, 0.75f, 0.2f);
            AddLocalParticles(root.transform, folder, "Boiler_Sparks_FX", sparkColor, 3f, 0.15f, 0.35f, 0.02f, 0.04f, 1.5f, new Vector3(0.05f, 0.05f, 0.05f));

            GameObject lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0.1f, 1.3f, 0.2f);
            Light sparkLight = lightGo.AddComponent<Light>();
            sparkLight.type = LightType.Point;
            sparkLight.range = 3f;
            sparkLight.intensity = 0.7f;
            sparkLight.color = sparkColor;
            sparkLight.shadows = LightShadows.None;
            ConfigureFlicker(root, FlickerLight.FlickerMode.Spark, sparkLight, null);

            Material scorchMat = LoadOrCreateDecalMat(folder, "Boiler_Scorch", EnsureDecalTexture("Decal_Grime"), new Color(0.02f, 0.02f, 0.02f), 0.05f);
            CreateDecalQuad(root.transform, "ScorchDecal", scorchMat, false, new Vector3(0f, 1.2f, 0.21f), Quaternion.identity, 0.9f);
            return FinishSetPiece(root, folder, width: 1.2f, depth: 0.4f, weight: 1f);
        }

        List<SetPiece> setPieces = new List<SetPiece>
        {
            BuildBoilerSparks(),
            BuildFallenRunnerSetPiece(boilerCommonDecals)
        };
        setPieces.AddRange(BuildPackSetPieces(PackBoiler)); // F85

        DustMotes dust = BuildDustPrefab(folder, "Boiler_Dust", new Color(0.6f, 0.5f, 0.4f), 35f, 0.015f, 0.03f,
            extraChildren: dustRoot => AddLocalParticles(dustRoot.transform, folder, "Boiler_Embers", new Color(1.0f, 0.55f, 0.2f), 1.5f, 4f, 7f, 0.01f, 0.01f, 0.2f, new Vector3(7f, 3f, 7f)));

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
            Props = props.ToArray(),
            FloorPropChance = 0.3f,
            WallDecalChance = 0.45f,
            FloorDecalChance = 0.3f,
            MaxDecalsPerCell = 2,
            SetPieces = setPieces.ToArray(),
            SetPieceCount = 3,
            Dust = dust
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
        Material pillarMat = LoadOrCreateMat(folder, "Lab_Pillar", new Color(0.15f, 0.16f, 0.18f), null, null, Vector2.one, null); // F90: wall panels at a column's tiling
        Color glassColor = new Color(1.0f, 0.15f, 0.10f);
        Material glassMat = LoadOrCreateMat(folder, "Lab_Glass", glassColor, null, null, Vector2.one, glassColor * 2.5f);
        Color ledColor = new Color(0.2f, 1.0f, 0.9f);
        Material ledMat = LoadOrCreateMat(folder, "Lab_Led", ledColor, null, null, Vector2.one, ledColor * 2f);
        Material screenMat = LoadOrCreateMat(folder, "Lab_Screen", new Color(0.02f, 0.02f, 0.03f), null, null, Vector2.one, null, smoothness: 0.6f);

        GameObject wallRoot = new GameObject("Lab_Wall");
        Box(wallRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(5f, 4f, 0.5f), wallMat, keepCollider: true);
        WallPiece wall = FinishWall(wallRoot, folder);

        GameObject pillarRoot = new GameObject("Lab_Pillar");
        Box(pillarRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(0.7f, 4f, 0.7f), pillarMat, keepCollider: true);
        Box(pillarRoot.transform, "LedStrip", new Vector3(0f, 2f, 0.36f), new Vector3(0.02f, 3.6f, 0.02f), ledMat);
        GameObject pillar = SaveAsPrefab(pillarRoot, folder, "Lab_Pillar");

        GameObject floorRoot = new GameObject("Lab_FloorTile");
        Box(floorRoot.transform, "Tile", new Vector3(0f, -0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), floorMat, keepCollider: true);
        GameObject floorTile = SaveAsPrefab(floorRoot, folder, "Lab_FloorTile");

        GameObject ceilingRoot = new GameObject("Lab_CeilingTile");
        Box(ceilingRoot.transform, "Tile", new Vector3(0f, 0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), ceilingMat, keepCollider: true);
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

        // ---- F70: additional props (plan section 3.3, Lab table) ----

        props.AddRange(BuildCommonFloorProps());
        props.AddRange(BuildAlchemistProps(ThemeSets[3])); // F79
        props.AddRange(BuildPackProps(PackLab)); // F85

        CommonDecalMaterials labCommonDecals = GetCommonDecalMaterials();
        Material labGrimeMat = LoadOrCreateDecalMat(folder, "Lab_Grime", EnsureDecalTexture("Decal_Grime"), new Color(0.1f, 0.15f, 0.13f), 0.2f);
        Material labDripMat = LoadOrCreateDecalMat(folder, "Lab_Drip", EnsureDecalTexture("Decal_Drip"), new Color(0.1f, 0.3f, 0.15f), 0.4f);
        Material labFloorGrimeMat = LoadOrCreateDecalMat(folder, "Lab_FloorGrime", EnsureDecalTexture("Decal_Grime"), new Color(0.08f, 0.09f, 0.09f), 0.2f);
        // Lab halves blood weights: clean-room with rare violence (decision, plan section 3.2).
        props.AddRange(BuildThemeDecals(folder, "Lab", labCommonDecals, labGrimeMat, labDripMat, labFloorGrimeMat, bloodWeightScale: 0.5f));

        // ---- F70: set pieces (plan section 3.4) ----

        List<SetPiece> setPieces = new List<SetPiece>
        {
            BuildFallenRunnerSetPiece(labCommonDecals)
        };
        setPieces.AddRange(BuildPackSetPieces(PackLab)); // F85

        DustMotes dust = BuildDustPrefab(folder, "Lab_Dust", new Color(0.85f, 0.9f, 0.9f), 12f, 0.015f, 0.03f);

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
            Props = props.ToArray(),
            FloorPropChance = 0.22f,
            WallDecalChance = 0.35f,
            FloorDecalChance = 0.2f,
            MaxDecalsPerCell = 2,
            SetPieces = setPieces.ToArray(),
            SetPieceCount = 3,
            Dust = dust
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

        // ---- F70: additional props (plan section 3.3, Hollow table) ----

        Color lanternGlow = new Color(0.9f, 0.7f, 0.35f);
        props.AddRange(BuildCommonFloorProps());
        props.AddRange(BuildAlchemistProps(ThemeSets[4])); // F79
        props.AddRange(BuildPackProps(PackHollow)); // F85

        // Hollow doubles Handprint/ClawMarks weight (decision, plan section 3.2).
        CommonDecalMaterials hollowCommonDecals = GetCommonDecalMaterials();
        Material hollowGrimeMat = LoadOrCreateDecalMat(folder, "Hollow_Grime", EnsureDecalTexture("Decal_Grime"), new Color(0.03f, 0.03f, 0.035f), 0.05f);
        Material hollowDripMat = LoadOrCreateDecalMat(folder, "Hollow_Drip", EnsureDecalTexture("Decal_Drip"), new Color(0.05f, 0.02f, 0.02f), 0.1f);
        Material hollowFloorGrimeMat = LoadOrCreateDecalMat(folder, "Hollow_FloorGrime", EnsureDecalTexture("Decal_Grime"), new Color(0.02f, 0.02f, 0.025f), 0.05f);
        props.AddRange(BuildThemeDecals(folder, "Hollow", hollowCommonDecals, hollowGrimeMat, hollowDripMat, hollowFloorGrimeMat, handClawWeightScale: 2f));

        // ---- F70: set pieces (plan section 3.4) ----

        SetPiece BuildHollowCircle()
        {
            GameObject root = new GameObject("Hollow_Circle");
            DressCircle(root); // pack candles and a baby doll (F89)

            for (int hI = 0; hI < 3; hI++)
            {
                float hx = -0.6f + hI * 0.6f;
                CreateDecalQuad(root.transform, $"Handprint{hI}", hollowCommonDecals.Handprint, false, new Vector3(hx, 1.4f, WallDecalInset), Quaternion.Euler(0f, 0f, hI * 15f), 0.28f);
            }

            GameObject lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.5f, 0.9f);
            Light circleLight = lightGo.AddComponent<Light>();
            circleLight.type = LightType.Point;
            circleLight.range = 3f;
            circleLight.intensity = 0.6f;
            circleLight.color = lanternGlow;
            circleLight.shadows = LightShadows.None;
            ConfigureFlicker(root, FlickerLight.FlickerMode.Candle, circleLight, null);

            return FinishSetPiece(root, folder, width: 1.6f, depth: 1.7f, weight: 1f);
        }

        List<SetPiece> setPieces = new List<SetPiece>
        {
            BuildHollowCircle(),
            BuildFallenRunnerSetPiece(hollowCommonDecals)
        };
        setPieces.AddRange(BuildPackSetPieces(PackHollow)); // F85

        // Hollow dust is slower (ash) - lower startSpeed than the shared default.
        DustMotes dust = BuildDustPrefab(folder, "Hollow_Dust", new Color(0.55f, 0.55f, 0.55f), 40f, 0.015f, 0.03f, speedMin: 0.01f, speedMax: 0.03f);

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
            Props = props.ToArray(),
            FloorPropChance = 0.35f,
            WallDecalChance = 0.5f,
            FloorDecalChance = 0.35f,
            MaxDecalsPerCell = 2,
            SetPieces = setPieces.ToArray(),
            SetPieceCount = 3,
            Dust = dust
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

        // F70: set pieces need more room on their plinth than a single prop.
        List<SetPiece> setPieceInstances = new List<SetPiece>();
        if (assets.SetPieces != null)
        {
            foreach (SetPiece setPiecePrefab in assets.SetPieces)
            {
                // InstantiateShowcase already advances x by PieceSpacing; the extra half-step here
                // brings a set piece's total advance to PieceSpacing * 1.5, since it needs more room
                // than a single prop (plan section 3.5).
                setPieceInstances.Add(InstantiateShowcase(rowGo.transform, setPiecePrefab, ref x));
                x += PieceSpacing * 0.5f;
            }
        }

        DustMotes dustInstance = InstantiateShowcase(rowGo.transform, assets.Dust, ref x);

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

        // F70.
        so.FindProperty("floorPropChance").floatValue = assets.FloorPropChance;
        so.FindProperty("wallDecalChance").floatValue = assets.WallDecalChance;
        so.FindProperty("floorDecalChance").floatValue = assets.FloorDecalChance;
        so.FindProperty("maxDecalsPerCell").intValue = assets.MaxDecalsPerCell;

        SerializedProperty setPiecesProp = so.FindProperty("setPieces");
        setPiecesProp.arraySize = setPieceInstances.Count;
        for (int i = 0; i < setPieceInstances.Count; i++)
        {
            setPiecesProp.GetArrayElementAtIndex(i).objectReferenceValue = setPieceInstances[i];
        }
        so.FindProperty("setPieceCount").intValue = assets.SetPieceCount;
        so.FindProperty("dust").objectReferenceValue = dustInstance;

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

    internal static GameObject InstantiateShowcaseGo(Transform row, GameObject prefab, ref float x)
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

            // F70: one floor prop (south wall) and one wall decal (west wall, above the wall prop) so the
            // mock-up also shows the new mount kinds reading together in a corner.
            foreach (PropPiece prop in assets.Props)
            {
                if (prop == null || prop.Mount != PropPiece.MountKind.Floor) continue;

                GameObject floorPropInstance = (GameObject)PrefabUtility.InstantiatePrefab(prop.gameObject, cell);
                Vector3 dir = Vector3.back; // south wall
                floorPropInstance.transform.localPosition = dir * BackFaceDistance + Vector3.right * 1.0f;
                floorPropInstance.transform.localRotation = Quaternion.LookRotation(-dir);
                floorPropInstance.name = "FloorProp";
                break;
            }

            foreach (PropPiece prop in assets.Props)
            {
                if (prop == null || prop.Mount != PropPiece.MountKind.WallDecal) continue;

                GameObject decalInstance = (GameObject)PrefabUtility.InstantiatePrefab(prop.gameObject, cell);
                Vector3 dir = Vector3.left; // west wall
                float y = Mathf.Clamp(1.6f, prop.DecalSizeRange.y * 0.5f + 0.3f, 2.2f - prop.DecalSizeRange.y * 0.5f);
                decalInstance.transform.localPosition = dir * (BackFaceDistance - 0.04f) + Vector3.forward * -1.0f + Vector3.up * y;
                decalInstance.transform.localRotation = Quaternion.LookRotation(-dir);
                decalInstance.transform.localScale = Vector3.one * prop.DecalSizeRange.y;
                decalInstance.name = "WallDecal";
                break;
            }
        }
    }
}

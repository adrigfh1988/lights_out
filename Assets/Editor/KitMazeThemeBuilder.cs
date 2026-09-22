using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LIGHTS OUT &gt; Build Kit Maze Theme. Creates the KitMazeTheme scene object F48's "START NEW MAZE"
/// path reads: seven prefab *asset* references loaded by path from Assets/Maze/Prefabs (the Maze
/// Modular Puzzle Kit), plus a showcase row so the pieces can be inspected in the Scene view. Nothing
/// here runs in a build; at Play time MazeGenerator.ResolveKitTheme just finds what this made and
/// clones the prefab assets directly (see decision 2 in plannings/kit-maze-plan.md).
/// </summary>
public static class KitMazeThemeBuilder
{
    private const string ThemeName = "KitMazeTheme";
    private const string PrefabFolder = "Assets/Maze/Prefabs";
    private static readonly Vector3 ThemeOrigin = new Vector3(-70f, 5f, 10f);
    private const float ShowcaseSpacing = 4f;

    [MenuItem("LIGHTS OUT/Build Kit Maze Theme")]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Build Kit Maze Theme", "Open the game scene first.", "OK");
            return;
        }

        KitMazeTheme existing = Object.FindAnyObjectByType<KitMazeTheme>(FindObjectsInactive.Include);
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Build Kit Maze Theme",
                    "A KitMazeTheme already exists in this scene. Replace it? Any changes you made to it will be lost.",
                    "Replace", "Cancel"))
            {
                return;
            }
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        GameObject wall1M = LoadPiece("Wall_1M");
        GameObject wall2M = LoadPiece("Wall_2M");
        GameObject wall3M = LoadPiece("Wall_3M");
        GameObject pillar = LoadPiece("Pilar"); // sic - the kit's own filename
        GameObject floor1M = LoadPiece("Floor_1M");
        GameObject floor2M = LoadPiece("Floor_2M");
        GameObject floor3M = LoadPiece("Floor_3M");

        if (wall1M == null || wall2M == null || wall3M == null || pillar == null ||
            floor1M == null || floor2M == null || floor3M == null)
        {
            Debug.LogError("KitMazeThemeBuilder: one or more kit prefabs are missing under " + PrefabFolder + ". Aborted.");
            return;
        }

        // F70 dressing pieces. Missing ones are logged but not fatal - IsComplete only checks the seven
        // structural pieces above, so a maze with, say, no Wall_Door still builds, just without gates.
        GameObject wallLamp = LoadPiece("Wall_Lamp");
        GameObject wallLight = LoadPiece("Wall_Light");
        GameObject wallDoor = LoadPiece("Wall_Door");
        GameObject switchLever = LoadPiece("Switch");
        GameObject[] trees =
        {
            LoadPiece("Tree_1_V1"), LoadPiece("Tree_1_V2"),
            LoadPiece("Tree_2_V1"), LoadPiece("Tree_2_V2"),
            LoadPiece("Tree_3_V1"), LoadPiece("Tree_3_V2"),
            LoadPiece("Tree_4_V1"), LoadPiece("Tree_4_V2"),
        };
        List<GameObject> foundTrees = new List<GameObject>();
        foreach (GameObject tree in trees)
        {
            if (tree != null) foundTrees.Add(tree);
        }

        GameObject root = new GameObject(ThemeName);
        Undo.RegisterCreatedObjectUndo(root, "Build Kit Maze Theme");
        root.transform.position = ThemeOrigin;

        KitMazeTheme theme = root.AddComponent<KitMazeTheme>();
        SerializedObject so = new SerializedObject(theme);
        so.FindProperty("wall1M").objectReferenceValue = wall1M;
        so.FindProperty("wall2M").objectReferenceValue = wall2M;
        so.FindProperty("wall3M").objectReferenceValue = wall3M;
        so.FindProperty("pillar").objectReferenceValue = pillar;
        so.FindProperty("floor1M").objectReferenceValue = floor1M;
        so.FindProperty("floor2M").objectReferenceValue = floor2M;
        so.FindProperty("floor3M").objectReferenceValue = floor3M;

        so.FindProperty("wallLamp").objectReferenceValue = wallLamp;
        so.FindProperty("wallLight").objectReferenceValue = wallLight;
        so.FindProperty("wallDoor").objectReferenceValue = wallDoor;
        so.FindProperty("switchLever").objectReferenceValue = switchLever;

        SerializedProperty treesProp = so.FindProperty("trees");
        treesProp.arraySize = foundTrees.Count;
        for (int i = 0; i < foundTrees.Count; i++)
        {
            treesProp.GetArrayElementAtIndex(i).objectReferenceValue = foundTrees[i];
        }

        // F70: the shared Themes/Common dressing (floor clutter, decals, the Switch wrapped as a
        // collider-less Floor prop, the FallenRunner set piece, the kit's own dust prefab) - built by
        // FloorThemeBuilder so both builders draw from one source (plan section 2, Editor/*.cs rows).
        FloorThemeBuilder.CommonDressing common = FloorThemeBuilder.BuildCommonDressing(switchLever);

        SerializedProperty propsProp = so.FindProperty("props");
        propsProp.arraySize = common.Props?.Length ?? 0;
        for (int i = 0; i < propsProp.arraySize; i++)
        {
            propsProp.GetArrayElementAtIndex(i).objectReferenceValue = common.Props[i];
        }

        SerializedProperty setPiecesProp = so.FindProperty("setPieces");
        setPiecesProp.arraySize = common.SetPieces?.Length ?? 0;
        for (int i = 0; i < setPiecesProp.arraySize; i++)
        {
            setPiecesProp.GetArrayElementAtIndex(i).objectReferenceValue = common.SetPieces[i];
        }

        so.FindProperty("dust").objectReferenceValue = common.Dust;

        // F70 dressing density (plan decision 8 sibling for kit mode).
        so.FindProperty("floorPropChance").floatValue = 0.3f;
        so.FindProperty("wallDecalChance").floatValue = 0.45f;
        so.FindProperty("floorDecalChance").floatValue = 0.3f;
        so.FindProperty("maxDecalsPerCell").intValue = 2;
        so.FindProperty("setPieceCount").intValue = 2;
        so.FindProperty("wallLightChance").floatValue = 0.12f;
        so.FindProperty("gateCount").intValue = 2;
        so.FindProperty("treeCount").intValue = 3;

        so.ApplyModifiedPropertiesWithoutUndo();

        Transform showcase = new GameObject("Showcase").transform;
        showcase.SetParent(root.transform, false);
        float x = 0f;
        foreach (GameObject piece in new[] { wall1M, wall2M, wall3M, pillar, floor1M, floor2M, floor3M, wallLamp, wallLight, wallDoor, switchLever })
        {
            if (piece == null) continue;
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(piece, showcase);
            instance.transform.localPosition = new Vector3(x, 0f, 0f);
            x += ShowcaseSpacing;
        }
        foreach (GameObject tree in foundTrees)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(tree, showcase);
            instance.transform.localPosition = new Vector3(x, 0f, 0f);
            instance.transform.localScale = Vector3.one * 0.55f;
            x += ShowcaseSpacing;
        }
        if (common.Props != null)
        {
            foreach (PropPiece prop in common.Props)
            {
                if (prop == null) continue;
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prop.gameObject, showcase);
                instance.transform.localPosition = new Vector3(x, 0f, 0f);
                x += ShowcaseSpacing;
            }
        }
        if (common.SetPieces != null)
        {
            foreach (SetPiece setPiece in common.SetPieces)
            {
                if (setPiece == null) continue;
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(setPiece.gameObject, showcase);
                instance.transform.localPosition = new Vector3(x, 0f, 0f);
                x += ShowcaseSpacing * 1.5f;
            }
        }
        if (common.Dust != null)
        {
            GameObject dustInstance = (GameObject)PrefabUtility.InstantiatePrefab(common.Dust.gameObject, showcase);
            dustInstance.transform.localPosition = new Vector3(x, 0f, 0f);
            x += ShowcaseSpacing;
        }

        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log("Kit Maze Theme built. Save the scene (Ctrl+S) to keep it.", root);
    }

    private static GameObject LoadPiece(string prefabName)
    {
        string path = $"{PrefabFolder}/{prefabName}.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) Debug.LogError($"KitMazeThemeBuilder: prefab not found at {path}.");
        return prefab;
    }
}

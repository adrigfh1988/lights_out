using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The Intake room is a hand-editable prefab (Assets/Prefabs/IntakeRoom.prefab). The builder only generates a first draft
/// (or a deliberate reset); after that, open the prefab and move, add or delete things by hand - nothing overwrites it.
/// Check Project still runs <see cref="Audit"/> on whatever the room is now, so a hand edit that blocks a walking path,
/// unwires a part or blows the light budget is flagged instead of silently breaking the tutorial.
/// </summary>
public static partial class IntakeRoomBuilder
{
    public const string PrefabPath = "Assets/Prefabs/IntakeRoom.prefab";
    private const string MeshFolder = "Assets/SourceFiles/Meshes/Intake";

    /// <summary>
    /// Turns a scene room into the prefab and connects the scene object to it. The shell's generated box meshes live
    /// only in the scene until now, and a prefab cannot reference scene objects, so they are first saved as mesh assets.
    /// Overwrites an existing prefab.
    /// </summary>
    public static void SaveAsPrefab(GameObject room)
    {
        EnsureFolderPath(MeshFolder);
        EnsureFolderPath("Assets/Prefabs");

        // Old mesh assets from a previous generation: the regenerated room no longer uses them.
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { MeshFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!IsMeshUsedBy(room, path)) AssetDatabase.DeleteAsset(path);
        }

        int i = 0;
        foreach (MeshFilter mf in room.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh == null || EditorUtility.IsPersistent(mesh) || !mesh.name.StartsWith("IntakeBox_")) continue;
            string path = AssetDatabase.GenerateUniqueAssetPath($"{MeshFolder}/{mesh.name}_{i++:00}.asset");
            AssetDatabase.CreateAsset(mesh, path); // the scene's MeshFilter now points at the asset
        }
        AssetDatabase.SaveAssets();

        PrefabUtility.SaveAsPrefabAssetAndConnect(room, PrefabPath, InteractionMode.AutomatedAction, out bool ok);
        if (!ok) Debug.LogError($"Build Intake Room: could not save {PrefabPath}.", room);
        else Debug.Log($"Intake room saved as {PrefabPath}. Edit it by hand (double-click the prefab), then save the scene (Ctrl+S).", room);
    }

    /// <summary>Puts the saved prefab back in the scene (used when the room is missing but the prefab exists).</summary>
    private static GameObject InstantiatePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) return null;
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(go, "Place Intake Room");
        Debug.Log($"Intake room placed from {PrefabPath}. Save the scene (Ctrl+S) to keep it.", go);
        return go;
    }

    /// <summary>The audit lines that failed (every check line ends in OK when it passes), or null.</summary>
    private static string AuditProblems(Transform root)
    {
        List<string> bad = new List<string>();
        foreach (string raw in Audit(root).Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("Intake audit") || line.StartsWith("bounds:") || line.EndsWith("OK")) continue;
            bad.Add(line);
        }
        return bad.Count == 0 ? null : string.Join("; ", bad);
    }

    private static bool IsMeshUsedBy(GameObject room, string assetPath)
    {
        foreach (MeshFilter mf in room.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh != null && AssetDatabase.GetAssetPath(mf.sharedMesh) == assetPath) return true;
        }
        return false;
    }

    private static void EnsureFolderPath(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolderPath(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }
}

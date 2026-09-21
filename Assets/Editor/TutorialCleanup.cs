using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LIGHTS OUT > Remove Tutorial Leftovers. One-shot cleanup of what the Unity Essentials tutorial left
/// in the scene and prefabs. Run it once, then delete this file.
///
/// It looks things up by NAME (scene objects) and by type NAME (RespawnPlayer), never by C# type, so it
/// keeps compiling after the scripts it removes are gone. It refuses to remove any object that contains
/// something the game needs, and it only deletes an asset once nothing else references it.
/// </summary>
public static class TutorialCleanup
{
    // Tutorial props in GetStarted_Scene. Anything not found is simply skipped.
    private static readonly string[] SceneObjectNames =
    {
        "Tutorial", "Collectibles", "WinPanel", "Ground", "Colliders", "Cube_Hollow", "Secret_Area",
        "Hidden_Area_1", "Hidden_Area_2", "Hidden_Area_3",
        "Platform_Teal_Floating", "Platform_Green_Floating",
        "01_Box_Yellow", "02_Box_Orange", "03_Box_Orange", "04_Box_Red",
        "SpawnPoint_1", "SpawnPoint_2",
    };

    // Component type names (short) that mean "the game needs this" - a candidate containing one is kept.
    private static readonly HashSet<string> ProtectedTypes = new HashSet<string>
    {
        "MazeGenerator", "GameManager", "AIFollower", "NavMeshSurface", "CharacterController",
        "Volume", "Canvas", "Camera", "ShopRoom", "ThirdPersonController",
    };

    private static readonly string[] PlayerPrefabs =
    {
        "Assets/PlayerRobot.prefab",
        "Assets/Prefabs/PlayerRobot.prefab",
    };

    // Assets deleted once nothing else uses them (checked after the scene is cleaned and saved).
    private static readonly string[] AssetsToDelete =
    {
        "Assets/Prefabs/Moving_Platform.prefab",
        "Assets/Prefabs/Stairs.prefab",
        "Assets/Prefabs/Wall_Light_Left.prefab",
        "Assets/Prefabs/Wall_Light_Right.prefab",
        "Assets/SourceFiles/Scripts/MotionAudioController.cs",
        "Assets/SourceFiles/Scripts/RespawnPlayer.cs",
    };

    private const string RespawnTypeName = "RespawnPlayer";

    [MenuItem("LIGHTS OUT/Remove Tutorial Leftovers")]
    public static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Remove Tutorial Leftovers", "Open the game scene first.", "OK");
            return;
        }

        List<GameObject> toRemove = new List<GameObject>();
        List<string> kept = new List<string>();
        foreach (GameObject root in scene.GetRootGameObjects()) Collect(root.transform, toRemove, kept);

        int objectCount = toRemove.Sum(g => g.GetComponentsInChildren<Transform>(true).Length);
        string message =
            $"Scene objects to remove ({toRemove.Count}, {objectCount} with children):\n  " +
            (toRemove.Count > 0 ? string.Join("\n  ", toRemove.Select(g => g.name)) : "(none found)") +
            (kept.Count > 0 ? "\n\nKept because they contain something the game needs:\n  " + string.Join("\n  ", kept) : "") +
            "\n\nAlso: the RespawnPlayer component is removed from the PlayerRobot prefabs, and the tutorial " +
            "prefabs/scripts (Moving_Platform, Stairs, Wall_Light_*, MotionAudioController, RespawnPlayer) are " +
            "deleted once nothing references them.\n\nThe scene will be SAVED. Everything is in git, so " +
            "this can be reverted there.";

        if (!EditorUtility.DisplayDialog("Remove Tutorial Leftovers", message, "Remove", "Cancel")) return;

        foreach (GameObject go in toRemove)
        {
            if (go != null) Undo.DestroyObjectImmediate(go);
        }

        int stripped = 0;
        foreach (string path in PlayerPrefabs) stripped += StripRespawnPlayer(path);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        List<string> deleted = new List<string>();
        List<string> stillUsed = new List<string>();
        DeleteUnreferenced(deleted, stillUsed);

        AssetDatabase.Refresh();

        string summary =
            $"Removed {toRemove.Count} scene object(s), {stripped} RespawnPlayer component(s), " +
            $"{deleted.Count} asset(s). Scene saved.";
        if (stillUsed.Count > 0) summary += "\nStill referenced, so kept: " + string.Join(", ", stillUsed);
        Debug.Log("TutorialCleanup: " + summary + "\nDeleted: " + string.Join(", ", deleted));
        EditorUtility.DisplayDialog("Remove Tutorial Leftovers", summary + "\n\nYou can now delete Assets/Editor/TutorialCleanup.cs.", "OK");
    }

    // ---------------------------------------------------------------- scene

    private static void Collect(Transform t, List<GameObject> toRemove, List<string> kept)
    {
        GameObject go = t.gameObject;

        if (SceneObjectNames.Contains(go.name))
        {
            // A child of a prefab instance cannot be destroyed on its own; only the instance root can.
            bool removable = !PrefabUtility.IsPartOfPrefabInstance(go)
                             || PrefabUtility.GetOutermostPrefabInstanceRoot(go) == go;

            if (!removable)
            {
                kept.Add($"{go.name} (part of a prefab instance)");
            }
            else
            {
                string reason = FindProtectedContent(go);
                if (reason != null) kept.Add($"{go.name} (contains {reason})");
                else toRemove.Add(go);
            }

            return; // do not look inside something already decided
        }

        foreach (Transform child in t) Collect(child, toRemove, kept);
    }

    /// <summary>Returns what the game needs inside this object, or null if it is safe to remove.</summary>
    private static string FindProtectedContent(GameObject go)
    {
        foreach (Component c in go.GetComponentsInChildren<Component>(true))
        {
            if (c == null) continue; // a missing script; harmless

            string typeName = c.GetType().Name;
            if (ProtectedTypes.Contains(typeName)) return typeName;
            if (c is Light light && light.type == LightType.Directional) return "the directional light";
            // The scene's looping ambience is taken over as the hunter's hum (MazeGenerator.TakeOverSceneAmbience)
            if (c is AudioSource source && source.loop) return "a looping AudioSource (the hunter's hum)";
        }

        return null;
    }

    // ---------------------------------------------------------------- prefabs and assets

    private static int StripRespawnPlayer(string prefabPath)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null) return 0;

        GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
        int removed = 0;
        try
        {
            foreach (MonoBehaviour behaviour in contents.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null && behaviour.GetType().Name == RespawnTypeName)
                {
                    Object.DestroyImmediate(behaviour);
                    removed++;
                }
            }

            if (removed > 0) PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        return removed;
    }

    private static void DeleteUnreferenced(List<string> deleted, List<string> stillUsed)
    {
        HashSet<string> candidates = new HashSet<string>(AssetsToDelete.Where(p => AssetDatabase.LoadMainAssetAtPath(p) != null));

        // Everything that could hold a reference: scenes and prefabs. Materials, meshes and clips do not
        // reference prefabs or scripts.
        string[] holders = AssetDatabase.FindAssets("t:Scene t:Prefab")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.StartsWith("Assets/") && !candidates.Contains(p))
            .ToArray();

        HashSet<string> referenced = new HashSet<string>();
        foreach (string holder in holders)
        {
            foreach (string dependency in AssetDatabase.GetDependencies(holder, false)) referenced.Add(dependency);
        }

        foreach (string path in candidates)
        {
            if (referenced.Contains(path))
            {
                stillUsed.Add(path);
                continue;
            }

            if (AssetDatabase.DeleteAsset(path)) deleted.Add(path);
        }
    }
}

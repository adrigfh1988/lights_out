using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// F89: brings an existing scene (FloorThemes gallery, KitMazeTheme) and the Themes prefabs to the state a fresh
/// Build &gt; Floor Themes / Kit Maze Theme now produces, without rebuilding anything: the untextured primitive dressing
/// (white sheets, discs and squares on the floor, cylinder barrels, cube crates, sphere rubble...) is removed and, where
/// a pack model fits, replaced by it. What goes and what replaces it is the table below; the reasoning is in
/// plannings/primitive-cleanup-plan.md.
///
/// No menu item: Build &gt; Floor Themes and Build &gt; Kit Maze Theme call Apply at the end, Check Project flags
/// NeedsUpgrade, Build Missing Pieces runs Apply. Idempotent. Marks the scene dirty, never saves.
/// </summary>
public static class PrimitiveDressingCleanup
{
    private const string ThemesRoot = "Assets/SourceFiles/Themes";

    /// <summary>Props deleted outright (no pack model reads as the same thing) or replaced by a new Alch_* prefab that PackDressing appends.</summary>
    private static readonly string[] RemovedProps =
    {
        // common (every theme + the kit maze)
        "Papers", "Debris", "Bucket", "Bottles",
        // Ward
        "Gurney", "IVStand", "WallPipe", "Vent", "Wheelchair", "MedTrolley", "PrivacyCurtain", "WallChart", "ExitSign", "BrokenGlass", "Syringes",
        // Boiler Deck (CrateStack -> Alch_CrateStack)
        "Barrel", "BarrelPair", "ValveWheel", "CrateStack", "Duct", "SteamValve", "PressureGauge", "FuseBox", "PipeCluster", "HangingHooks", "CoalPile", "OilCan",
        // Crypt (Urn -> Alch_Urn, CandleCluster -> Alch_Candles)
        "Rubble", "Urn", "BrokenPillar", "BonePile", "Chains", "Roots", "Sarcophagus", "SkullNiche", "CandleCluster", "ScatteredBones",
        // Lab (SpecimenShelf -> Alch_SpecimenShelf, Whiteboard -> Alch_Board)
        "ServerRack", "Tank", "Monitor", "CableBundle", "CableDrop", "Fan", "SpecimenShelf", "Whiteboard", "BiohazardDrum", "BrokenCeilingLamp", "GlassShards", "ClipboardPile",
        // Hollow
        "Mirror", "Shroud", "Effigy", "HangingCloth", "Bell", "RockingChair", "StickTotem", "PhotoFrames", "Lantern", "Doll", "ClothNest",
    };

    /// <summary>Set pieces deleted whole: once the primitive parts go, what is left (a decal, a particle system) reads as nothing.</summary>
    private static readonly string[] RemovedSets = { "Ward_Triage", "Ward_Isolation", "Crypt_Collapse", "Boiler_Burst", "Lab_Containment", "Lab_Desk", "Hollow_Nest" };

    /// <summary>Primitive decoration sitting on a structural tile: the drain disc and grate on the floor, the seam / pipe / tray on the ceiling.</summary>
    private static readonly (string prefab, string child)[] TileDecor =
    {
        ("Ward_FloorTile", "Drain"), ("Boiler_FloorTile", "Grate"), ("Ward_CeilingTile", "Seam"), ("Boiler_CeilingTile", "Pipe"), ("Lab_CeilingTile", "CableTray"),
    };

    private sealed class SetPieceFix
    {
        public string Prefab;
        public string[] Primitives;          // child names (or "Prefix*") that go
        public System.Action<GameObject> Dress;
        public string Marker;                // child the dressed prefab has
    }

    private static readonly SetPieceFix[] SetFixes =
    {
        new SetPieceFix { Prefab = "Crypt_Shrine", Primitives = new[] { "Altar", "Candle*", "Flame*", "SkullA", "SkullB", "SkullC" }, Dress = FloorThemeBuilder.DressShrine, Marker = FloorThemeBuilder.CleanupAltar },
        new SetPieceFix { Prefab = "Boiler_Sparks", Primitives = new[] { "FuseBoxOpen", "DoorAjar", "DanglingCable" }, Dress = FloorThemeBuilder.DressSparks, Marker = FloorThemeBuilder.CleanupFuseBox },
        new SetPieceFix { Prefab = "Hollow_Circle", Primitives = new[] { "Candle*", "Flame*", "DollHead", "DollBody" }, Dress = FloorThemeBuilder.DressCircle, Marker = "PackDoll" },
        new SetPieceFix { Prefab = "FallenRunner", Primitives = new[] { "Torch" }, Dress = FloorThemeBuilder.DressTorch, Marker = FloorThemeBuilder.CleanupLamp },
    };

    /// <summary>Materials only the removed pieces used; deleted when nothing in the project references them any more.</summary>
    private static readonly string[] OrphanMaterialCandidates =
    {
        "Ward_Linen", "Ward_GlassShard", "Ward_ExitSign", "Common_Paper", "Common_Debris", "Common_Bucket", "Common_Bottle", "Common_Torch",
        "Crypt_Bone", "Crypt_CandleFlame", "Lab_Jar", "Lab_Whiteboard", "Lab_Biohazard", "Lab_GlassShard", "Lab_ContainmentPuddle", "Lab_CRTGlow",
        "Hollow_Cloth", "Hollow_Lantern", "Boiler_BurstPuddle",
    };

    // ---------------------------------------------------------------- query

    public static bool NeedsUpgrade()
    {
        foreach (SerializedObject so in ThemeObjects())
        {
            if (ListsRemoved(so.FindProperty("props"), RemovedProps) || ListsRemoved(so.FindProperty("setPieces"), RemovedSets)) return true;
        }
        foreach ((string prefab, string child) in TileDecor)
        {
            GameObject asset = FindPrefab(prefab);
            if (asset != null && asset.transform.Find(child) != null) return true;
        }
        foreach (SetPieceFix fix in SetFixes)
        {
            GameObject asset = FindPrefab(fix.Prefab);
            if (asset != null && asset.transform.Find(fix.Marker) == null && HasPrimitive(asset, fix) && PackFolderPresent()) return true;
        }
        foreach (string name in RemovedProps.Concat(RemovedSets))
        {
            if (FindPrefab(name) != null) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- apply

    public static string Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        StringBuilder sb = new StringBuilder("Primitive cleanup - ");
        int removedEntries = 0;

        // 1. Rows and the kit theme: drop the list entries, destroy their showcase instances (and plinths), re-seat the sample cell.
        foreach (FloorTheme theme in Object.FindObjectsByType<FloorTheme>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            removedEntries += CleanTheme(new SerializedObject(theme), theme.transform);
        }
        foreach (KitMazeTheme kit in Object.FindObjectsByType<KitMazeTheme>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            removedEntries += CleanTheme(new SerializedObject(kit), kit.transform);
        }
        sb.Append($"{removedEntries} list entries removed; ");

        // 2. Structural tiles lose their primitive decor.
        int tiles = 0;
        foreach ((string prefab, string child) in TileDecor)
        {
            GameObject asset = FindPrefab(prefab);
            if (asset == null || asset.transform.Find(child) == null) continue;
            string path = AssetDatabase.GetAssetPath(asset);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            Transform t = contents.transform.Find(child);
            if (t != null) Object.DestroyImmediate(t.gameObject);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            tiles++;
        }
        sb.Append($"{tiles} tile decor; ");

        // 3. Set pieces keep their lights, flicker and decals, but their primitive parts become pack models.
        int sets = 0;
        if (PackFolderPresent())
        {
            foreach (SetPieceFix fix in SetFixes)
            {
                GameObject asset = FindPrefab(fix.Prefab);
                if (asset == null || asset.transform.Find(fix.Marker) != null || !HasPrimitive(asset, fix)) continue;
                RebuildSetPiece(asset, fix);
                sets++;
            }
        }
        sb.Append($"{sets} set pieces re-skinned; ");

        // 4. The prefab assets of everything removed.
        int deleted = 0;
        foreach (string name in RemovedProps.Concat(RemovedSets))
        {
            GameObject asset = FindPrefab(name);
            if (asset == null) continue;
            if (AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(asset))) deleted++;
        }
        sb.Append($"{deleted} prefabs deleted; ");

        // 5. Stand-ins (Alch_CrateStack, Alch_Candles, Alch_Urn, Alch_SpecimenShelf, Alch_Board on the Lab row).
        PackDressing.Apply();

        // 6. Materials nothing uses any more.
        sb.Append($"{DeleteOrphanMaterials()} materials deleted");

        AssetDatabase.SaveAssets();
        if ((removedEntries + tiles + sets + deleted > 0) && scene.IsValid() && scene.isLoaded) EditorSceneManager.MarkSceneDirty(scene);

        string summary = sb + (removedEntries + tiles + sets + deleted > 0 ? ". Save the scene (Ctrl+S) to keep it." : ".");
        Debug.Log(summary);
        return summary;
    }

    // ---------------------------------------------------------------- helpers

    private static bool PackFolderPresent() => AssetDatabase.IsValidFolder("Assets/BK_AlchemistHouse");

    private static IEnumerable<SerializedObject> ThemeObjects()
    {
        foreach (FloorTheme theme in Object.FindObjectsByType<FloorTheme>(FindObjectsInactive.Include, FindObjectsSortMode.None)) yield return new SerializedObject(theme);
        foreach (KitMazeTheme kit in Object.FindObjectsByType<KitMazeTheme>(FindObjectsInactive.Include, FindObjectsSortMode.None)) yield return new SerializedObject(kit);
    }

    private static string SourceName(Object o)
    {
        Component c = o as Component;
        if (c == null) return null;
        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject);
        return (source != null ? source : c.gameObject).name;
    }

    private static bool ListsRemoved(SerializedProperty list, string[] names)
    {
        if (list == null) return false;
        for (int i = 0; i < list.arraySize; i++)
        {
            string n = SourceName(list.GetArrayElementAtIndex(i).objectReferenceValue);
            if (n != null && names.Contains(n)) return true;
        }
        return false;
    }

    private static GameObject FindPrefab(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:Prefab", new[] { ThemesRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }

    private static bool Matches(string childName, string pattern) => pattern.EndsWith("*") ? childName.StartsWith(pattern.TrimEnd('*')) : childName == pattern;

    private static bool HasPrimitive(GameObject root, SetPieceFix fix)
    {
        foreach (Transform child in root.transform)
        {
            if (fix.Primitives.Any(p => Matches(child.name, p))) return true;
        }
        return false;
    }

    /// <summary>Instantiates the set piece prefab at the world origin, drops its primitive children, dresses it with pack models and saves it over the same file (GUID kept).</summary>
    private static void RebuildSetPiece(GameObject asset, SetPieceFix fix)
    {
        string path = AssetDatabase.GetAssetPath(asset);
        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.transform.localScale = Vector3.one;

        foreach (Transform child in root.transform.Cast<Transform>().ToArray())
        {
            if (fix.Primitives.Any(p => Matches(child.name, p))) Object.DestroyImmediate(child.gameObject);
        }
        fix.Dress(root);

        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }

    private static int CleanTheme(SerializedObject so, Transform themeRoot)
    {
        int removed = 0;
        removed += RemoveEntries(so.FindProperty("props"), RemovedProps);
        removed += RemoveEntries(so.FindProperty("setPieces"), RemovedSets);
        so.ApplyModifiedPropertiesWithoutUndo();

        // Showcase instances (gallery rows: children of the row; kit: children of "Showcase"), plus the SampleCell copies.
        List<Transform> doomed = new List<Transform>();
        foreach (Transform t in themeRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t == themeRoot || !PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            if (source == null) continue;
            if (RemovedProps.Contains(source.name) || RemovedSets.Contains(source.name)) doomed.Add(t);
        }
        foreach (Transform t in doomed)
        {
            if (t == null) continue;
            Transform parent = t.parent;
            if (parent != null && parent.name == "SampleCell") ReseatSampleProp(so, t);
            else DestroyShowcase(t);
        }
        return removed;
    }

    private static int RemoveEntries(SerializedProperty list, string[] names)
    {
        if (list == null) return 0;
        int removed = 0;
        for (int i = list.arraySize - 1; i >= 0; i--)
        {
            Object o = list.GetArrayElementAtIndex(i).objectReferenceValue;
            string n = SourceName(o);
            bool isNull = o == null;
            if (!isNull && !names.Contains(n)) continue;
            if (!isNull) list.GetArrayElementAtIndex(i).objectReferenceValue = null; // first delete nulls the slot, second removes it
            list.DeleteArrayElementAtIndex(i);
            removed++;
        }
        return removed;
    }

    /// <summary>Destroys a showcase instance and the gallery plinth sitting under it, if any.</summary>
    private static void DestroyShowcase(Transform instance)
    {
        Transform parent = instance.parent;
        if (parent != null)
        {
            foreach (Transform sibling in parent.Cast<Transform>().ToArray())
            {
                if (sibling.name == "Plinth" && Mathf.Abs(sibling.localPosition.x - instance.localPosition.x) < 0.01f) Object.DestroyImmediate(sibling.gameObject);
            }
        }
        Object.DestroyImmediate(instance.gameObject);
    }

    /// <summary>The SampleCell mock-up showed the first wall and floor prop; a removed one is swapped for the next prop of the same kind the theme still lists.</summary>
    private static void ReseatSampleProp(SerializedObject themeSo, Transform instance)
    {
        PropPiece.MountKind kind = instance.name == "FloorProp" ? PropPiece.MountKind.Floor : PropPiece.MountKind.Wall;
        Vector3 pos = instance.localPosition;
        Quaternion rot = instance.localRotation;
        string name = instance.name;
        Transform parent = instance.parent;

        PropPiece replacement = null;
        SerializedProperty props = themeSo.FindProperty("props");
        for (int i = 0; i < props.arraySize && replacement == null; i++)
        {
            PropPiece p = props.GetArrayElementAtIndex(i).objectReferenceValue as PropPiece;
            if (p != null && p.Mount == kind) replacement = p;
        }
        Object.DestroyImmediate(instance.gameObject);
        if (replacement == null) return;

        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(replacement.gameObject);
        GameObject prefab = source != null ? source : replacement.gameObject;
        GameObject clone = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        clone.transform.localPosition = pos;
        clone.transform.localRotation = rot;
        clone.name = name;
    }

    private static int DeleteOrphanMaterials()
    {
        HashSet<string> used = new HashSet<string>();
        List<string> roots = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/SourceFiles", "Assets/Prefabs" })) roots.Add(AssetDatabase.GUIDToAssetPath(guid));
        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" })) roots.Add(AssetDatabase.GUIDToAssetPath(guid));
        foreach (string dep in AssetDatabase.GetDependencies(roots.ToArray(), true)) used.Add(dep);
        // Materials reference textures, never each other, but the builders' LoadOrCreateMat looks a material up by path: leave no half-dead set.
        int deleted = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { ThemesRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!OrphanMaterialCandidates.Contains(name) || used.Contains(path)) continue;
            if (AssetDatabase.DeleteAsset(path)) deleted++;
        }
        return deleted;
    }
}

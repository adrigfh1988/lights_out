using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// F85 pack dressing, applied to a scene that already has its FloorThemes gallery and KitMazeTheme: appends the
/// pieces each row (or the kit maze) should have - the Alchemist furniture, the Socket Pack vents / sockets / fuse
/// boxes, the DeadBody LITE beds, baths, shroud and hanging bodies - without rebuilding anything, so hand edits and
/// the F71 relit ambient values survive. What each theme gets is defined once, in FloorThemeBuilder.Pack.cs and
/// FloorThemeBuilder.Alchemist.cs (ThemeSets); the full-rebuild builders include the same pieces natively.
///
/// No menu item of its own: Build &gt; Floor Themes and Build &gt; Kit Maze Theme call Apply at the end, Check Project
/// flags NeedsUpgrade and Build Missing Pieces runs Apply. Idempotent; marks the scene dirty, never saves.
/// Spec: plannings/pack-dressing-plan.md.
/// </summary>
public static class PackDressing
{
    private static readonly string[] RowNames = { "Ward", "Boiler Deck", "Crypt", "Lab", "Hollow" };

    /// <summary>True if some gallery row or the kit maze lacks a piece it should list (and whose pack is in the project).</summary>
    public static bool NeedsUpgrade()
    {
        FloorThemeSet themeSet = Object.FindAnyObjectByType<FloorThemeSet>(FindObjectsInactive.Include);
        if (themeSet != null)
        {
            SerializedProperty floors = new SerializedObject(themeSet).FindProperty("floors");
            for (int i = 0; i < floors.arraySize && i < RowNames.Length; i++)
            {
                FloorTheme theme = floors.GetArrayElementAtIndex(i).objectReferenceValue as FloorTheme;
                if (theme == null) continue;
                int bit = FloorThemeBuilder.PackBitForRow(i);
                SerializedObject so = new SerializedObject(theme);
                if (Missing(so.FindProperty("props"), FloorThemeBuilder.ExpectedPackNames(bit, sets: false))) return true;
                if (Missing(so.FindProperty("setPieces"), FloorThemeBuilder.ExpectedPackNames(bit, sets: true))) return true;
            }
        }

        KitMazeTheme kit = Object.FindAnyObjectByType<KitMazeTheme>(FindObjectsInactive.Include);
        if (kit != null)
        {
            SerializedObject so = new SerializedObject(kit);
            if (Missing(so.FindProperty("setPieces"), FloorThemeBuilder.ExpectedPackNames(FloorThemeBuilder.PackKit, sets: true))) return true;
        }
        return false;
    }

    /// <summary>
    /// No dialogs. Builds (or loads) the pack prefabs and appends whatever each row / the kit maze lacks, as showcase
    /// instances past the row's right-most child. Returns a one-line summary.
    /// </summary>
    public static string Apply()
    {
        if (AlchemistMaterialUpgrade.NeedsConversion()) AlchemistMaterialUpgrade.Convert();

        Scene scene = SceneManager.GetActiveScene();
        StringBuilder sb = new StringBuilder("Pack dressing - ");
        int total = 0;

        FloorThemeSet themeSet = Object.FindAnyObjectByType<FloorThemeSet>(FindObjectsInactive.Include);
        if (themeSet == null)
        {
            sb.Append("rows: skipped (no gallery); ");
        }
        else
        {
            SerializedProperty floors = new SerializedObject(themeSet).FindProperty("floors");
            for (int i = 0; i < floors.arraySize && i < RowNames.Length; i++)
            {
                FloorTheme theme = floors.GetArrayElementAtIndex(i).objectReferenceValue as FloorTheme;
                if (theme == null) continue;

                int bit = FloorThemeBuilder.PackBitForRow(i);
                List<PropPiece> props = new List<PropPiece>(FloorThemeBuilder.BuildAlchemistProps(FloorThemeBuilder.ThemeSets[i] & FloorThemeBuilder.AlchemistSet.FurnitureAll));
                props.AddRange(FloorThemeBuilder.BuildPackProps(bit));
                SetPiece[] sets = FloorThemeBuilder.BuildPackSetPieces(bit);

                int added = AppendToRow(theme, props.ToArray(), sets);
                total += added;
                sb.Append($"{RowNames[i]} +{added}, ");
            }
        }

        KitMazeTheme kit = Object.FindAnyObjectByType<KitMazeTheme>(FindObjectsInactive.Include);
        if (kit == null)
        {
            sb.Append("kit: skipped (no KitMazeTheme)");
        }
        else
        {
            int added = AppendToKit(kit, new PropPiece[0], FloorThemeBuilder.BuildPackSetPieces(FloorThemeBuilder.PackKit));
            total += added;
            sb.Append($"Kit +{added}");
        }

        AssetDatabase.SaveAssets();
        if (total > 0 && scene.IsValid() && scene.isLoaded) EditorSceneManager.MarkSceneDirty(scene);

        string summary = sb + $" ({total} total)" + (total > 0 ? ". Save the scene (Ctrl+S) to keep them." : ".");
        Debug.Log(summary);
        return summary;
    }

    // ---------------------------------------------------------------- append helpers (the F79 pair, now also for set pieces)

    private static bool Missing(SerializedProperty list, List<string> expected)
    {
        HashSet<string> listed = ListedNames(list);
        foreach (string name in expected)
        {
            if (!listed.Contains(name)) return true;
        }
        return false;
    }

    private static HashSet<string> ListedNames(SerializedProperty list)
    {
        HashSet<string> names = new HashSet<string>();
        for (int i = 0; i < list.arraySize; i++)
        {
            Component existing = list.GetArrayElementAtIndex(i).objectReferenceValue as Component;
            if (existing == null) continue;
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject);
            names.Add((source != null ? source : existing.gameObject).name); // a prefab asset reference is its own source
        }
        return names;
    }

    private static int AppendToRow(FloorTheme theme, PropPiece[] props, SetPiece[] sets)
    {
        SerializedObject so = new SerializedObject(theme);
        SerializedProperty propsProp = so.FindProperty("props");
        SerializedProperty setsProp = so.FindProperty("setPieces");
        HashSet<string> listedProps = ListedNames(propsProp);
        HashSet<string> listedSets = ListedNames(setsProp);

        Transform row = theme.transform;
        float x = float.MinValue;
        for (int i = 0; i < row.childCount; i++) x = Mathf.Max(x, row.GetChild(i).localPosition.x);
        x = x == float.MinValue ? 8f : x + FloorThemeBuilder.PieceSpacing;

        int added = 0;
        foreach (PropPiece prefab in props)
        {
            if (prefab == null || listedProps.Contains(prefab.gameObject.name)) continue;
            GameObject instance = FloorThemeBuilder.InstantiateShowcaseGo(row, prefab.gameObject, ref x);

            int slot = propsProp.arraySize;
            propsProp.arraySize = slot + 1;
            propsProp.GetArrayElementAtIndex(slot).objectReferenceValue = instance.GetComponent<PropPiece>();
            listedProps.Add(prefab.gameObject.name);
            added++;
        }
        foreach (SetPiece prefab in sets)
        {
            if (prefab == null || listedSets.Contains(prefab.gameObject.name)) continue;
            GameObject instance = FloorThemeBuilder.InstantiateShowcaseGo(row, prefab.gameObject, ref x);
            x += FloorThemeBuilder.PieceSpacing * 0.5f; // set pieces need more room

            int slot = setsProp.arraySize;
            setsProp.arraySize = slot + 1;
            setsProp.GetArrayElementAtIndex(slot).objectReferenceValue = instance.GetComponent<SetPiece>();
            listedSets.Add(prefab.gameObject.name);
            added++;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return added;
    }

    private static int AppendToKit(KitMazeTheme kit, PropPiece[] props, SetPiece[] sets)
    {
        SerializedObject so = new SerializedObject(kit);
        SerializedProperty propsProp = so.FindProperty("props");
        SerializedProperty setsProp = so.FindProperty("setPieces");
        HashSet<string> listedProps = ListedNames(propsProp);
        HashSet<string> listedSets = ListedNames(setsProp);

        // KitMazeThemeBuilder keeps its showcase copies under a "Showcase" child, 4 m apart.
        Transform showcase = kit.transform.Find("Showcase");
        float x = 0f;
        if (showcase != null)
        {
            x = float.MinValue;
            for (int i = 0; i < showcase.childCount; i++) x = Mathf.Max(x, showcase.GetChild(i).localPosition.x);
            x = x == float.MinValue ? 0f : x + 4f;
        }

        int added = 0;
        foreach (PropPiece prefab in props)
        {
            if (prefab == null || listedProps.Contains(prefab.gameObject.name)) continue;
            int slot = propsProp.arraySize;
            propsProp.arraySize = slot + 1;
            propsProp.GetArrayElementAtIndex(slot).objectReferenceValue = prefab; // the kit theme stores prefab assets, not instances
            listedProps.Add(prefab.gameObject.name);
            if (showcase != null) { Showcase(showcase, prefab.gameObject, ref x, 4f); }
            added++;
        }
        foreach (SetPiece prefab in sets)
        {
            if (prefab == null || listedSets.Contains(prefab.gameObject.name)) continue;
            int slot = setsProp.arraySize;
            setsProp.arraySize = slot + 1;
            setsProp.GetArrayElementAtIndex(slot).objectReferenceValue = prefab;
            listedSets.Add(prefab.gameObject.name);
            if (showcase != null) { Showcase(showcase, prefab.gameObject, ref x, 6f); }
            added++;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return added;
    }

    private static void Showcase(Transform parent, GameObject prefab, ref float x, float step)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.transform.localPosition = new Vector3(x, 0f, 0f);
        x += step;
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// F90: brings an existing scene and the Themes prefabs to what Build &gt; Floor Themes / Build &gt; Interactables now produce, without a
/// rebuild: the last untextured pieces of the maze go.
///   - wall trims (Ward dado / skirting / cornice and pillar cap, Boiler rivets / pipe and pillar flanges, Lab cable tray / cables /
///     seams) are deleted from the wall and pillar prefabs - they were flat-coloured and read as "forms on the wall";
///   - the pillar Body of Ward / Boiler / Lab gets its own textured material (the wall's set at a column's tiling);
///   - the kit's glass patch and puddle become decal looks, the lore note an Alchemist book on a small shelf, and the kit gains a
///     pack-model shard (InteractableKitBuilder.RebuildF90Templates); the Intake's note is re-cloned from the new template;
///   - the fixture metals / wood (lockers, lamp housings, shutter, bell, valve) get texture sets (ThemeSurfaceUpgrade rows).
/// Spec: plannings/remaining-primitives-plan.md.
///
/// No menu item: Build &gt; Floor Themes calls Apply, Check Project flags NeedsUpgrade, Build Missing Pieces runs Apply. Idempotent.
/// Marks the scene dirty, never saves.
/// </summary>
public static class RemainingPrimitivesCleanup
{
    private const string ThemesRoot = "Assets/SourceFiles/Themes";

    private static readonly (string prefab, string[] children)[] Trims =
    {
        ("Ward_Wall", new[] { "Dado", "Skirting", "Cornice" }),
        ("Ward_Pillar", new[] { "Cap" }),
        ("Boiler_Wall", new[] { "RivetTop", "RivetBottom", "Pipe" }),
        ("Boiler_Pillar", new[] { "FlangeTop", "FlangeBottom" }),
        ("Lab_Wall", new[] { "CableTray", "Cable", "SeamL", "SeamR" }),
    };

    private static readonly (string prefab, string material)[] PillarMaterials =
    {
        ("Ward_Pillar", "1_Ward/Materials/Ward_Pillar"),
        ("Boiler_Pillar", "2_BoilerDeck/Materials/Boiler_Pillar"),
        ("Lab_Pillar", "4_Lab/Materials/Lab_Pillar"),
    };

    private static readonly string[] OrphanMaterialCandidates = { "Ward_Dado" };

    private static bool AlchemistPresent() => AssetDatabase.IsValidFolder("Assets/BK_AlchemistHouse");

    // ---------------------------------------------------------------- query

    public static bool NeedsUpgrade() => Problems().Count > 0;

    public static List<string> Problems()
    {
        List<string> bad = new List<string>();

        foreach ((string prefab, string[] children) in Trims)
        {
            GameObject asset = FindPrefab(prefab);
            if (asset == null) continue;
            if (asset.GetComponentsInChildren<Transform>(true).Any(t => t != asset.transform && children.Contains(t.name))) bad.Add($"{prefab} still has untextured trim");
        }
        foreach ((string prefab, string material) in PillarMaterials)
        {
            GameObject asset = FindPrefab(prefab);
            if (asset == null) continue;
            Transform body = asset.transform.Find("Body");
            Renderer r = body != null ? body.GetComponent<Renderer>() : null;
            string want = System.IO.Path.GetFileNameWithoutExtension(material);
            if (r != null && (r.sharedMaterial == null || r.sharedMaterial.name != want)) bad.Add($"{prefab} body is not on {want}");
        }

        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (kit != null)
        {
            if (kit.GlassPatch != null && !PatchIsCurrent(kit.GlassPatch, NoisePatchLook.GlassTexturePath)) bad.Add("the glass patch is still a flat quad");
            if (kit.Puddle != null && !PatchIsCurrent(kit.Puddle, NoisePatchLook.PuddleTexturePath)) bad.Add("the puddle is still a flat quad");
            if (kit.LoreNote != null && AlchemistPresent() && !IsShelvedBook(kit.LoreNote)) bad.Add("the lore note is not the shelved book");
            if (kit.ShardModel == null && AlchemistPresent()) bad.Add("the kit has no shard model");

            IntakeRoom intake = Object.FindAnyObjectByType<IntakeRoom>(FindObjectsInactive.Include);
            LoreNote intakeNote = IntakeNote(intake);
            if (intakeNote != null && AlchemistPresent() && !IsShelvedBook(intakeNote)) bad.Add("the Intake note is the old flat panel");
        }
        return bad;
    }

    // ---------------------------------------------------------------- apply

    public static string Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        StringBuilder sb = new StringBuilder("F90 cleanup - ");

        // Materials first: the pillar materials must exist before a prefab body is pointed at them, and the fixtures get their textures.
        ThemeSurfaceUpgrade.Apply();

        int trims = 0;
        foreach ((string prefab, string[] children) in Trims)
        {
            GameObject asset = FindPrefab(prefab);
            if (asset == null) continue;
            if (!asset.GetComponentsInChildren<Transform>(true).Any(t => t != asset.transform && children.Contains(t.name))) continue;
            string path = AssetDatabase.GetAssetPath(asset);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            foreach (Transform t in contents.GetComponentsInChildren<Transform>(true).Where(t => t != contents.transform && children.Contains(t.name)).ToArray())
            {
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            trims++;
        }
        sb.Append($"{trims} trimmed prefabs; ");

        int pillars = 0;
        foreach ((string prefab, string material) in PillarMaterials)
        {
            GameObject asset = FindPrefab(prefab);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>($"{ThemesRoot}/{material}.mat");
            if (asset == null || mat == null) continue;
            Transform body = asset.transform.Find("Body");
            Renderer r = body != null ? body.GetComponent<Renderer>() : null;
            if (r == null || r.sharedMaterial == mat) continue;
            string path = AssetDatabase.GetAssetPath(asset);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            contents.transform.Find("Body").GetComponent<Renderer>().sharedMaterial = mat;
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            pillars++;
        }
        sb.Append($"{pillars} pillars; ");

        // Kit slots (glass patch, puddle, note, shard model).
        int kitFixed = 0;
        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (kit != null)
        {
            bool stale = (kit.GlassPatch != null && !PatchIsCurrent(kit.GlassPatch, NoisePatchLook.GlassTexturePath))
                         || (kit.Puddle != null && !PatchIsCurrent(kit.Puddle, NoisePatchLook.PuddleTexturePath))
                         || (kit.LoreNote != null && AlchemistPresent() && !IsShelvedBook(kit.LoreNote));
            bool missingShard = kit.ShardModel == null && AlchemistPresent();
            if (stale) { InteractableKitBuilder.RebuildF90Templates(); kitFixed++; }
            else if (missingShard) { InteractableKitBuilder.Build(); kitFixed++; }
        }
        sb.Append($"{kitFixed} kit rebuild; ");

        // The Intake's note is a plain clone of the template, so it does not follow a rebuilt slot.
        int intakeFixed = 0;
        kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        IntakeRoom intake = Object.FindAnyObjectByType<IntakeRoom>(FindObjectsInactive.Include);
        LoreNote oldNote = IntakeNote(intake);
        if (kit != null && kit.LoreNote != null && oldNote != null && AlchemistPresent()
            && !IsShelvedBook(oldNote) && IsShelvedBook(kit.LoreNote))
        {
            Transform parent = oldNote.transform.parent;
            Vector3 pos = oldNote.transform.position;
            Quaternion rot = oldNote.transform.rotation;
            string noteName = oldNote.gameObject.name;
            LoreNote clone = Object.Instantiate(kit.LoreNote, parent);
            clone.gameObject.name = noteName;
            clone.gameObject.SetActive(true);
            clone.transform.SetPositionAndRotation(pos, rot);
            SerializedObject so = new SerializedObject(intake);
            so.FindProperty("note").objectReferenceValue = clone;
            so.ApplyModifiedPropertiesWithoutUndo();
            Object.DestroyImmediate(oldNote.gameObject);
            intakeFixed++;
        }
        sb.Append($"{intakeFixed} intake note; ");

        sb.Append($"{DeleteOrphanMaterials()} materials deleted");
        AssetDatabase.SaveAssets();

        int changed = trims + pillars + kitFixed + intakeFixed;
        if (changed > 0 && scene.IsValid() && scene.isLoaded) EditorSceneManager.MarkSceneDirty(scene);
        string summary = sb + (changed > 0 ? ". Save the scene (Ctrl+S) to keep it." : ".");
        Debug.Log(summary);
        return summary;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The patch has a "Look" decal quad on the current texture.</summary>
    private static bool PatchIsCurrent(NoisySurface patch, string texturePath)
    {
        Transform look = patch.transform.Find("Look");
        Renderer r = look != null ? look.GetComponent<Renderer>() : null;
        if (r == null || r.sharedMaterial == null) return false;
        Texture tex = r.sharedMaterial.GetTexture("_BaseMap");
        return tex != null && AssetDatabase.GetAssetPath(tex) == texturePath;
    }

    /// <summary>The F90 note: a shelf, the Book12 model and a Glint light. (A change of book or shelf bumps this name check, so the migration redoes the template.)</summary>
    private static bool IsShelvedBook(LoreNote n) => n.transform.Find("Glint") != null && n.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("Book12"));

    private static LoreNote IntakeNote(IntakeRoom intake)
    {
        if (intake == null) return null;
        SerializedProperty p = new SerializedObject(intake).FindProperty("note");
        return p != null ? p.objectReferenceValue as LoreNote : null;
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

    private static int DeleteOrphanMaterials()
    {
        HashSet<string> used = new HashSet<string>();
        List<string> roots = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/SourceFiles", "Assets/Prefabs" })) roots.Add(AssetDatabase.GUIDToAssetPath(guid));
        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" })) roots.Add(AssetDatabase.GUIDToAssetPath(guid));
        foreach (string dep in AssetDatabase.GetDependencies(roots.ToArray(), true)) used.Add(dep);
        int deleted = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { ThemesRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!OrphanMaterialCandidates.Contains(System.IO.Path.GetFileNameWithoutExtension(path)) || used.Contains(path)) continue;
            if (AssetDatabase.DeleteAsset(path)) deleted++;
        }
        return deleted;
    }
}

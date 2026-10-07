using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Swaps the F70 code-drawn blood decals (a disc with droplets, a few ellipses, a thick line - flat red blobs)
/// for the textures in Assets/Blood decal pack ("Blood splatter decal package", Saturn Entertainment).
/// The pack's own materials are Built-in pipeline shaders (magenta in URP), so only its textures are used.
///
/// Three things, all idempotent:
///  1. the pack textures get decal import settings (alpha is transparency, clamped, 512 max for the web build);
///  2. one transparent URP Lit material per texture under Themes/Common/Materials/BloodPack, and the three shared
///     materials BloodSplat/BloodSmear/BloodPool switched in place to their first variant - so every theme's
///     prefab and the set pieces that bake a BloodPool quad change at once;
///  3. every *_BloodSplat / *_BloodSmear / *_BloodPool PropPiece prefab under Themes gets a DecalVariants
///     component listing its look-alikes and their aspect ratios (MazeGenerator picks one per decal).
///
/// Run automatically at the end of LIGHTS OUT &gt; Build &gt; Floor Themes and Build &gt; Kit Maze Theme (their
/// LoadOrCreate materials would otherwise keep the old drawn textures); Check Project flags anything not yet
/// upgraded and Build Missing Pieces runs it. Without the pack, it does nothing and the drawn decals stay.
/// </summary>
public static class BloodDecalUpgrade
{
    private const string PackFolder = "Assets/Blood decal pack/blood";
    private const string ThemesRoot = "Assets/SourceFiles/Themes";
    private const string CommonMaterials = ThemesRoot + "/Common/Materials";
    private const string VariantFolder = CommonMaterials + "/BloodPack";

    // blood.tif is left out: its alpha never quite reaches 0, so it draws a faint square on a lit floor.
    private static readonly string[] SplatVariants = { "blood1", "blood2", "blood3" };
    private static readonly string[] SmearVariants = { "blood4", "blood5", "blood6", "blood7" };
    private static readonly string[] PoolVariants = { "blood3", "blood2", "blood1" };

    /// <summary>Wet blood catches the torch a little; the drawn decals used 0.55.</summary>
    private const float Smoothness = 0.55f;

    public static bool PackPresent => AssetDatabase.IsValidFolder(PackFolder);

    /// <summary>True if the pack is in the project but some shared material or blood prefab is still on the drawn decals.</summary>
    public static bool NeedsUpgrade()
    {
        if (!PackPresent) return false;

        foreach (string name in new[] { "BloodSplat", "BloodSmear", "BloodPool" })
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>($"{CommonMaterials}/{name}.mat");
            if (mat != null && !UsesPackTexture(mat)) return true;
        }

        foreach (string path in BloodPrefabPaths())
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            DecalVariants variants = prefab.GetComponent<DecalVariants>();
            if (variants == null || variants.Count == 0) return true;
        }

        GameObject runner = AssetDatabase.LoadAssetAtPath<GameObject>(CorpseUpgrade.FallenRunnerPath);
        Transform drag = runner != null ? runner.transform.Find("DragMarksDecal") : null;
        MeshRenderer dragQuad = drag != null ? drag.GetComponent<MeshRenderer>() : null;
        return dragQuad != null && dragQuad.sharedMaterial != null && !UsesPackTexture(dragQuad.sharedMaterial);
    }

    /// <summary>
    /// The FallenRunner's baked DragMarksDecal quad (the drawn row of red ovals) gets the blood-pack smear blood5,
    /// shaped to its aspect with the longest side kept. Crypt_Collapse's DustDecal shares the drawn DragMarks material
    /// but is dust, not blood - it is left alone, which is why the shared material itself is not switched.
    /// </summary>
    private static bool UpgradeFallenRunnerDrag(Dictionary<string, Material> variantMats, Dictionary<string, float> aspects)
    {
        if (!variantMats.TryGetValue("blood5", out Material smear)) return false;

        GameObject loaded = AssetDatabase.LoadAssetAtPath<GameObject>(CorpseUpgrade.FallenRunnerPath);
        Transform probe = loaded != null ? loaded.transform.Find("DragMarksDecal") : null;
        MeshRenderer probeQuad = probe != null ? probe.GetComponent<MeshRenderer>() : null;
        if (probeQuad == null || probeQuad.sharedMaterial == smear) return false;

        GameObject root = PrefabUtility.LoadPrefabContents(CorpseUpgrade.FallenRunnerPath);
        try
        {
            Transform quad = root.transform.Find("DragMarksDecal");
            quad.GetComponent<MeshRenderer>().sharedMaterial = smear;
            float aspect = aspects.TryGetValue("blood5", out float a) ? a : 1f;
            Vector3 s = quad.localScale;
            float side = Mathf.Max(s.x, s.y);
            quad.localScale = aspect >= 1f ? new Vector3(side, side / aspect, s.z) : new Vector3(side * aspect, side, s.z);
            PrefabUtility.SaveAsPrefabAsset(root, CorpseUpgrade.FallenRunnerPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Does all three steps. Returns how many prefabs it (re)wrote; 0 with no pack.</summary>
    public static int Apply()
    {
        if (!PackPresent) return 0;

        Dictionary<string, Material> variantMats = new Dictionary<string, Material>();
        Dictionary<string, float> aspects = new Dictionary<string, float>();
        EnsureFolder(VariantFolder);

        foreach (string name in new[] { "blood1", "blood2", "blood3", "blood4", "blood5", "blood6", "blood7" })
        {
            string texPath = $"{PackFolder}/{name}.tif";
            Texture2D tex = PrepareTexture(texPath, out float aspect);
            if (tex == null) continue;
            aspects[name] = aspect;
            variantMats[name] = LoadOrCreateVariantMaterial(name, tex);
        }

        UpgradeShared("BloodSplat", SplatVariants, variantMats);
        UpgradeShared("BloodSmear", SmearVariants, variantMats);
        UpgradeShared("BloodPool", PoolVariants, variantMats);

        int written = 0;
        foreach (string path in BloodPrefabPaths())
        {
            string[] set = path.EndsWith("_BloodSmear.prefab") || path.EndsWith("_DragMarks.prefab") ? SmearVariants
                : path.EndsWith("_BloodPool.prefab") || path.EndsWith("_Footprints.prefab") ? PoolVariants
                : SplatVariants;
            bool freeRoll = path.EndsWith("_Footprints.prefab") || path.EndsWith("_DragMarks.prefab");
            if (WriteVariants(path, set, variantMats, aspects, freeRoll)) written++;
        }
        if (UpgradeFallenRunnerDrag(variantMats, aspects)) written++;

        AssetDatabase.SaveAssets();
        Debug.Log($"BloodDecalUpgrade: blood decals now use the Blood decal pack ({variantMats.Count} variant materials, {written} prefabs).");
        return written;
    }

    // ---------------------------------------------------------------- steps

    private static Texture2D PrepareTexture(string path, out float aspect)
    {
        aspect = 1f;
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return null;

        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        if (width > 0 && height > 0) aspect = (float)width / height;

        bool dirty = false;
        if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; dirty = true; }
        if (importer.wrapMode != TextureWrapMode.Clamp) { importer.wrapMode = TextureWrapMode.Clamp; dirty = true; }
        if (importer.maxTextureSize != 512) { importer.maxTextureSize = 512; dirty = true; }
        if (dirty) importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material LoadOrCreateVariantMaterial(string name, Texture2D texture)
    {
        string path = $"{VariantFolder}/Blood_{name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = $"Blood_{name}" };
            AssetDatabase.CreateAsset(mat, path);
        }

        MakeTransparentDecal(mat, texture);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>A shared drawn-decal material (still used by set pieces that bake a quad) switched to the set's first variant.</summary>
    private static void UpgradeShared(string sharedName, string[] set, Dictionary<string, Material> variantMats)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>($"{CommonMaterials}/{sharedName}.mat");
        if (mat == null || !variantMats.TryGetValue(set[0], out Material first)) return;

        MakeTransparentDecal(mat, first.GetTexture("_BaseMap") as Texture2D);
        EditorUtility.SetDirty(mat);
    }

    /// <param name="freeRoll">Footprints were laid at a fixed heading (they had a walking direction); as blood they spin freely.</param>
    private static bool WriteVariants(string prefabPath, string[] set, Dictionary<string, Material> variantMats, Dictionary<string, float> aspects, bool freeRoll = false)
    {
        List<Material> mats = new List<Material>();
        List<float> ratios = new List<float>();
        foreach (string name in set)
        {
            if (!variantMats.TryGetValue(name, out Material mat)) continue;
            mats.Add(mat);
            ratios.Add(aspects.TryGetValue(name, out float a) ? a : 1f);
        }
        if (mats.Count == 0) return false;

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            DecalVariants variants = root.GetComponent<DecalVariants>();
            if (variants == null) variants = root.AddComponent<DecalVariants>();

            SerializedObject so = new SerializedObject(variants);
            SerializedProperty matsProp = so.FindProperty("materials");
            SerializedProperty aspectProp = so.FindProperty("aspects");
            matsProp.arraySize = mats.Count;
            aspectProp.arraySize = ratios.Count;
            for (int i = 0; i < mats.Count; i++)
            {
                matsProp.GetArrayElementAtIndex(i).objectReferenceValue = mats[i];
                aspectProp.GetArrayElementAtIndex(i).floatValue = ratios[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            if (freeRoll && root.TryGetComponent(out PropPiece piece))
            {
                SerializedObject pieceSo = new SerializedObject(piece);
                pieceSo.FindProperty("randomRoll").boolValue = true;
                pieceSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // The prefab itself shows the first variant, shaped to it, so the gallery row previews it correctly.
            variants.Apply(0);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>URP Lit, alpha-blended (soft edges - the drawn decals were alpha-clipped), white so the texture's own colour shows, no depth write, no shadows.</summary>
    private static void MakeTransparentDecal(Material mat, Texture2D texture)
    {
        if (texture != null)
        {
            mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
        }
        mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
        mat.SetFloat("_Smoothness", Smoothness);

        mat.SetFloat("_Surface", 1f);   // Transparent
        mat.SetFloat("_Blend", 0f);     // Alpha
        // Off, or URP re-validates the material to premultiplied alpha and adds the torch's specular over the
        // WHOLE quad regardless of alpha - a pale rectangle around every decal. Found in the 7 Oct smoke test.
        if (mat.HasProperty("_BlendModePreserveSpecular")) mat.SetFloat("_BlendModePreserveSpecular", 0f);
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.SetFloat("_AlphaClip", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_ReceiveShadows")) mat.SetFloat("_ReceiveShadows", 1f);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetShaderPassEnabled("DepthOnly", false);
        mat.SetShaderPassEnabled("ShadowCaster", false);
        mat.renderQueue = (int)RenderQueue.Transparent;
    }

    private static bool UsesPackTexture(Material mat)
    {
        Texture tex = mat.GetTexture("_BaseMap");
        return tex != null && AssetDatabase.GetAssetPath(tex).StartsWith(PackFolder);
    }

    /// <summary>Every theme's blood decal prefab (Ward_, Boiler_, Crypt_, Lab_, Hollow_, Kit_ ...).</summary>
    private static IEnumerable<string> BloodPrefabPaths()
    {
        if (!AssetDatabase.IsValidFolder(ThemesRoot)) yield break;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ThemesRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith("_BloodSplat.prefab") || path.EndsWith("_BloodSmear.prefab") || path.EndsWith("_BloodPool.prefab")
                || path.EndsWith("_Footprints.prefab")  // footprints become blood (7 Oct 2026)
                || path.EndsWith("_DragMarks.prefab")) yield return path; // drag marks become blood smears (F85)
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F88: real PBR surface textures (ambientCG, CC0 - see Assets/SourceFiles/Textures/PBR/CREDITS.md) on the structural
/// materials of the themed floors that play: Ward (1), BoilerDeck (2), Crypt (3 and 5, which borrows the Crypt row) and Lab (4).
/// Walls, floors and ceilings only; the Hollow row, the kit maze, the shop and the Intake are out of scope. Pillars share
/// materials with trim pieces (Ward_Dado, Boiler_Iron, Lab_Metal), so they are left plain, except the Crypt pillar, which is
/// Crypt_Stone and so is textured with the wall.
///
/// Run automatically at the end of LIGHTS OUT &gt; Build &gt; Floor Themes (a "Replace everything" rebuild recreates the
/// materials untextured and this puts the textures back) and flagged by LIGHTS OUT &gt; Check Project. Idempotent: a material that
/// already has its set assigned is left alone (so a hand-tweaked tint survives). It edits the existing .mat assets in place;
/// it never touches the scene.
///
/// URP Lit wants smoothness in the alpha of the metallic map, so for every set this bakes
/// &lt;Id&gt;_MetalSmooth.png (R = metalness * MetalScale or 0, A = 1 - roughness) from the downloaded Roughness/Metalness jpgs.
/// The pack step reads the jpg files directly, so it does not depend on how those are imported. Tiling is the real-world
/// repeat over the primitive's UV 0..1 face: a 5 x 4 m wall face, a 4.5 m floor/ceiling tile.
/// </summary>
public static class ThemeSurfaceUpgrade
{
    private const string Themes = "Assets/SourceFiles/Themes";
    private const string PbrRoot = "Assets/SourceFiles/Textures/PBR";

    private struct Row
    {
        public string Material;      // path under Themes, no extension
        public string SetId;         // ambientCG asset id, or a GreenMossGames library texture name when Library is set
        public bool Library;         // GreenMossGames Ultimate Material Library set: albedo + normal only (no roughness/AO), constant smoothness
        public Vector2 Tiling;       // texture repeats across the primitive's 0..1 UV face
        public Color Tint;           // multiplies the albedo; chosen to keep the old palette and the F71 darkness balance
        public float Smoothness;     // scale on the roughness-derived smoothness (alpha of the packed map)
        public float MetalScale;     // 0 = ignore the set's metalness map (stone, tile, plaster); <1 keeps metal from going black without reflections
        public float NormalScale;    // _BumpScale
        public float Occlusion;      // _OcclusionStrength (only used when the set has an AO map)
    }

    private static readonly Row[] Rows =
    {
        // Ward: clinical wall tile, chequered floor, acoustic ceiling tile.
        new Row { Material = "1_Ward/Materials/Ward_Wall",         SetId = "Tiles107",         Tiling = new Vector2(3f, 2f), Tint = new Color(0.56f, 0.64f, 0.60f), Smoothness = 0.55f, MetalScale = 0f,    NormalScale = 1f,   Occlusion = 0.8f },
        new Row { Material = "1_Ward/Materials/Ward_Floor",        SetId = "Tiles074",         Tiling = new Vector2(2f, 2f), Tint = new Color(0.60f, 0.60f, 0.58f), Smoothness = 0.5f,  MetalScale = 0f,    NormalScale = 1f,   Occlusion = 1f },
        new Row { Material = "1_Ward/Materials/Ward_Ceiling",      SetId = "OfficeCeiling001", Tiling = new Vector2(2f, 2f), Tint = new Color(0.52f, 0.52f, 0.50f), Smoothness = 0.3f,  MetalScale = 0f,    NormalScale = 1f,   Occlusion = 0.8f },
        // BoilerDeck: rusted plate walls, perforated walkway grating floor, dark rusted ceiling.
        new Row { Material = "2_BoilerDeck/Materials/Boiler_Wall", SetId = "MetalPlates011",   Tiling = new Vector2(3f, 2f), Tint = new Color(1.00f, 1.00f, 1.00f), Smoothness = 0.6f,  MetalScale = 0.35f, NormalScale = 1f,   Occlusion = 0.8f },
        new Row { Material = "2_BoilerDeck/Materials/Boiler_Floor",SetId = "MetalWalkway014",  Tiling = new Vector2(3f, 3f), Tint = new Color(0.35f, 0.35f, 0.35f), Smoothness = 0.6f,  MetalScale = 0.35f, NormalScale = 1f,   Occlusion = 1f },
        new Row { Material = "2_BoilerDeck/Materials/Boiler_Ceiling", SetId = "MetalPlates013",Tiling = new Vector2(2f, 2f), Tint = new Color(0.35f, 0.30f, 0.27f), Smoothness = 0.5f,  MetalScale = 0.35f, NormalScale = 1f,   Occlusion = 0.8f },
        // Crypt (floors 3 and 5): rough stone blocks (also the pillar and ledge), flagstones, mossy stone ceiling.
        new Row { Material = "3_Crypt/Materials/Crypt_Stone",      SetId = "castle_wall_varriation", Library = true, Tiling = new Vector2(3f, 2f), Tint = new Color(0.40f, 0.36f, 0.33f), Smoothness = 0.3f,  MetalScale = 0f,    NormalScale = 1.2f, Occlusion = 1f },
        new Row { Material = "3_Crypt/Materials/Crypt_Floor",      SetId = "monastery_stone_floor", Library = true, Tiling = new Vector2(2f, 2f), Tint = new Color(0.40f, 0.30f, 0.18f), Smoothness = 0.35f,  MetalScale = 0f,    NormalScale = 1f,   Occlusion = 1f },
        new Row { Material = "3_Crypt/Materials/Crypt_Ceiling",    SetId = "stone_brick_wall_001", Library = true, Tiling = new Vector2(2f, 2f), Tint = new Color(0.32f, 0.33f, 0.40f), Smoothness = 0.3f,  MetalScale = 0f,    NormalScale = 1f,   Occlusion = 1f },
        // Lab: sealed metal wall panels, sealed concrete floor, clean ceiling tile.
        new Row { Material = "4_Lab/Materials/Lab_Wall",           SetId = "MetalPlates004",   Tiling = new Vector2(3f, 2f), Tint = new Color(0.20f, 0.25f, 0.30f), Smoothness = 0.6f,  MetalScale = 0.35f, NormalScale = 1f,   Occlusion = 1f },
        new Row { Material = "4_Lab/Materials/Lab_Floor",          SetId = "Concrete033",      Tiling = new Vector2(2f, 2f), Tint = new Color(0.25f, 0.26f, 0.32f), Smoothness = 0.6f,  MetalScale = 0f,    NormalScale = 1f,   Occlusion = 1f },
        new Row { Material = "4_Lab/Materials/Lab_Ceiling",        SetId = "OfficeCeiling001", Tiling = new Vector2(2f, 2f), Tint = new Color(0.10f, 0.10f, 0.11f), Smoothness = 0.3f,  MetalScale = 0f,    NormalScale = 1f,   Occlusion = 0.8f },
    };

    private static string MatPath(Row r) => $"{Themes}/{r.Material}.mat";
    private const string LibRoot = "Assets/GreenMossGames/UltimateMaterialLibrary/Textures";

    private static string Map(Row r, string suffix, string ext = "jpg")
    {
        if (r.Library)
        {
            if (suffix == "Color") return $"{LibRoot}/{r.SetId}_diff_1k.png";
            if (suffix == "NormalGL") return $"{LibRoot}/{r.SetId}_nor_gl_1k.png";
            return null; // the library ships no roughness, AO or metalness
        }
        return $"{PbrRoot}/{r.SetId}/{r.SetId}_{suffix}.{ext}";
    }

    private static bool Has(Row r, string suffix) { string p = Map(r, suffix); return p != null && File.Exists(p); }

    /// <summary>One line per material that still lacks its texture set; empty when everything is done. Read by ProjectSetup.</summary>
    public static string Problems()
    {
        List<string> bad = new List<string>();
        foreach (Row r in Rows)
        {
            string why = MaterialProblem(r);
            if (why != null) bad.Add($"{Path.GetFileNameWithoutExtension(r.Material)} ({why})");
        }
        return bad.Count == 0 ? null : string.Join(", ", bad);
    }

    public static bool NeedsUpgrade() => Problems() != null;

    private static string MaterialProblem(Row r)
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(MatPath(r));
        if (m == null) return "material missing";
        Texture2D color = AssetDatabase.LoadAssetAtPath<Texture2D>(Map(r, "Color"));
        if (color == null) return $"{r.SetId} not in the project";
        if (m.GetTexture("_BaseMap") != color) return "no albedo";
        if (m.GetTexture("_BumpMap") == null || !AssetDatabase.GetAssetPath(m.GetTexture("_BumpMap")).Contains(r.SetId)) return "no normal map";
        if (!r.Library && m.GetTexture("_MetallicGlossMap") == null) return "no smoothness map";
        if (r.Library && (m.GetTexture("_MetallicGlossMap") != null || Mathf.Abs(m.GetFloat("_Smoothness") - r.Smoothness) > 0.001f)) return "smoothness";
        if (Vector2.Distance(m.GetTextureScale("_BaseMap"), r.Tiling) > 0.001f) return "tiling";
        return null;
    }

    /// <summary>No dialogs. Imports the sets, bakes the packed maps and wires every row's material; rows already done are skipped.</summary>
    public static void Apply()
    {
        bool any = false;
        HashSet<string> imported = new HashSet<string>();
        foreach (Row r in Rows)
        {
            if (MaterialProblem(r) == null) continue;
            Material m = AssetDatabase.LoadAssetAtPath<Material>(MatPath(r));
            if (m == null) { Debug.LogWarning($"ThemeSurfaceUpgrade: {MatPath(r)} is missing, skipped."); continue; }
            if (!File.Exists(Map(r, "Color")) || !File.Exists(Map(r, "NormalGL")))
            {
                Debug.LogWarning($"ThemeSurfaceUpgrade: texture set {r.SetId} is not in {PbrRoot} (see CREDITS.md), skipped {m.name}.");
                continue;
            }

            if (imported.Add(r.SetId)) ImportSet(r);
            // MetalScale is part of the bake, so the packed map is per row, not per set (two rows of one set could differ).
            string packed = Pack(r);
            Wire(m, r, packed);
            any = true;
        }
        if (any) AssetDatabase.SaveAssets();
    }

    // -------------------------------------------------------------- import

    private static void ImportSet(Row r)
    {
        Configure(Map(r, "Color"), TextureImporterType.Default, srgb: true);
        Configure(Map(r, "NormalGL"), TextureImporterType.NormalMap, srgb: false);
        if (Has(r, "AmbientOcclusion")) Configure(Map(r, "AmbientOcclusion"), TextureImporterType.Default, srgb: false);
        // Roughness and Metalness are only bake inputs (read from disk), but keep them linear and out of the way.
        if (Has(r, "Roughness")) Configure(Map(r, "Roughness"), TextureImporterType.Default, srgb: false);
        if (Has(r, "Metalness")) Configure(Map(r, "Metalness"), TextureImporterType.Default, srgb: false);
    }

    private static void Configure(string path, TextureImporterType type, bool srgb)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
        bool dirty = ti.textureType != type || ti.sRGBTexture != srgb || ti.maxTextureSize != 1024
                     || !ti.mipmapEnabled || ti.anisoLevel != 4 || ti.textureCompression != TextureImporterCompression.Compressed;
        if (!dirty) return;
        ti.textureType = type;
        if (type == TextureImporterType.Default) ti.sRGBTexture = srgb;
        ti.maxTextureSize = 1024;
        ti.mipmapEnabled = true;
        ti.anisoLevel = 4;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.crunchedCompression = false;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.alphaSource = TextureImporterAlphaSource.None;
        ti.SaveAndReimport();
    }

    // -------------------------------------------------------------- baked metallic/smoothness map

    /// <summary>Bakes R = metal * MetalScale, A = 1 - roughness. Name carries the MetalScale so rows with different values never share a file.</summary>
    private static string Pack(Row r)
    {
        if (r.Library) return null; // constant smoothness on the material instead
        string metalTag = r.MetalScale > 0f ? "_m" + Mathf.RoundToInt(r.MetalScale * 100f) : "";
        string path = $"{PbrRoot}/{r.SetId}/{r.SetId}_MetalSmooth{metalTag}.png";
        if (File.Exists(path)) { Configure(path, TextureImporterType.Default, srgb: false); FixPacked(path); return path; }

        Texture2D rough = LoadRaw(Map(r, "Roughness"));
        Texture2D metal = r.MetalScale > 0f && Has(r, "Metalness") ? LoadRaw(Map(r, "Metalness")) : null;
        if (rough == null) { Debug.LogWarning($"ThemeSurfaceUpgrade: {Map(r, "Roughness")} missing, {r.SetId} gets no smoothness map."); return null; }

        int w = rough.width, h = rough.height;
        Color32[] rp = rough.GetPixels32();
        Color32[] mp = metal != null && metal.width == w && metal.height == h ? metal.GetPixels32() : null;
        Color32[] outPx = new Color32[rp.Length];
        for (int i = 0; i < rp.Length; i++)
        {
            byte mR = mp != null ? (byte)Mathf.RoundToInt(mp[i].r * r.MetalScale) : (byte)0;
            outPx[i] = new Color32(mR, 0, 0, (byte)(255 - rp[i].r));
        }
        Texture2D packed = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        packed.SetPixels32(outPx);
        packed.Apply();
        File.WriteAllBytes(path, packed.EncodeToPNG());
        Object.DestroyImmediate(packed);
        Object.DestroyImmediate(rough);
        if (metal != null) Object.DestroyImmediate(metal);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        Configure(path, TextureImporterType.Default, srgb: false);
        FixPacked(path);
        return path;
    }

    /// <summary>The packed map's alpha is smoothness data, not transparency: keep it as-is and out of any alpha-dilation.</summary>
    private static void FixPacked(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
        if (ti.alphaSource == TextureImporterAlphaSource.FromInput && !ti.alphaIsTransparency) return;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = false;
        ti.SaveAndReimport();
    }

    private static Texture2D LoadRaw(string path)
    {
        if (!File.Exists(path)) return null;
        Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        return ImageConversion.LoadImage(t, File.ReadAllBytes(path), false) ? t : null;
    }

    // -------------------------------------------------------------- material

    private static void Wire(Material m, Row r, string packedPath)
    {
        Texture2D color = AssetDatabase.LoadAssetAtPath<Texture2D>(Map(r, "Color"));
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(Map(r, "NormalGL"));
        Texture2D ao = Has(r, "AmbientOcclusion") ? AssetDatabase.LoadAssetAtPath<Texture2D>(Map(r, "AmbientOcclusion")) : null;
        Texture2D packed = packedPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath) : null;

        Undo.RecordObject(m, "Theme surface textures");
        m.SetTexture("_BaseMap", color);
        m.SetTextureScale("_BaseMap", r.Tiling);
        m.SetTextureOffset("_BaseMap", Vector2.zero);
        if (m.HasProperty("_MainTex")) { m.SetTexture("_MainTex", color); m.SetTextureScale("_MainTex", r.Tiling); }
        m.SetColor("_BaseColor", r.Tint);
        if (m.HasProperty("_Color")) m.SetColor("_Color", r.Tint);

        m.SetTexture("_BumpMap", normal);
        m.SetTextureScale("_BumpMap", r.Tiling);
        m.SetFloat("_BumpScale", r.NormalScale);
        m.EnableKeyword("_NORMALMAP");

        if (ao != null)
        {
            m.SetTexture("_OcclusionMap", ao);
            m.SetTextureScale("_OcclusionMap", r.Tiling);
            m.SetFloat("_OcclusionStrength", r.Occlusion);
            m.EnableKeyword("_OCCLUSIONMAP");
        }
        else
        {
            m.SetTexture("_OcclusionMap", null);
            m.DisableKeyword("_OCCLUSIONMAP");
        }

        if (r.Library)
        {
            m.SetTexture("_MetallicGlossMap", null);
            m.DisableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", r.Smoothness);
        }
        else if (packed != null)
        {
            m.SetTexture("_MetallicGlossMap", packed);
            m.SetTextureScale("_MetallicGlossMap", r.Tiling);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", r.Smoothness);
            m.SetFloat("_SmoothnessTextureChannel", 0f); // 0 = metallic map alpha
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        EditorUtility.SetDirty(m);
    }
}

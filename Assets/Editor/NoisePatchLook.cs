using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// F90: what the glass and puddle floor patches (NoisySurface, InteractableKit.glassPatch/puddle) look like. They were two flat
/// coloured squares; now each is a transparent decal quad with an irregular outline, so the patch still reads on the floor (the
/// player has to see it to avoid it) without a hard-edged grey or white rectangle.
///
/// Glass: a scatter of broken-glass shards, drawn once into Assets/SourceFiles/Textures/Decals/GlassShards.png (no CC0 glass decal
/// exists on ambientCG and the Alchemist pack has no broken glass). Puddle: an irregular rounded pool silhouette (the Blood decal pack only has splatters), tinted near-black
/// and glossy so the torch's specular wets it. Used by InteractableKitBuilder.BuildFloorPatch; both materials are edited in place, so
/// a hand tweak to a tint survives only until the next rebuild of the slot (the look is set every time).
/// </summary>
internal static class NoisePatchLook
{
    internal const string GlassTexturePath = "Assets/SourceFiles/Textures/Decals/GlassShards.png";
    internal const string PuddleTexturePath = "Assets/SourceFiles/Textures/Decals/PuddleMask.png";

    /// <summary>The 1.6 m footprint the trigger uses; the decal quad covers exactly it.</summary>
    internal const float Size = 1.6f;

    internal static void ApplyGlass(Material mat)
    {
        Texture2D tex = EnsureGlassTexture();
        MakeTransparentDecal(mat, tex, new Color(0.80f, 0.93f, 1f), 0.95f);
        // A faint glint of its own so a patch still shows a little where the torch is not pointing (the old quad glowed too).
        mat.EnableKeyword("_EMISSION");
        mat.SetTexture("_EmissionMap", tex);
        mat.SetColor("_EmissionColor", new Color(0.18f, 0.30f, 0.34f));
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(mat);
    }

    internal static void ApplyPuddle(Material mat)
    {
        Texture2D tex = EnsurePuddleTexture();
        MakeTransparentDecal(mat, tex, new Color(0.05f, 0.08f, 0.10f), 0.98f);
        mat.DisableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", Color.black);
        EditorUtility.SetDirty(mat);
    }

    /// <summary>True once a patch material is on the transparent decal look (used by the migration's check).</summary>
    internal static bool IsDecal(Material mat) => mat != null && mat.GetFloat("_Surface") > 0.5f && mat.GetTexture("_BaseMap") != null;

    /// <summary>URP Lit, alpha-blended, no depth write, no shadows. _BlendModePreserveSpecular stays 0 (see CLAUDE.md: URP otherwise re-validates to premultiplied and the torch's specular lights the whole quad).</summary>
    private static void MakeTransparentDecal(Material mat, Texture2D texture, Color tint, float smoothness)
    {
        if (texture != null)
        {
            mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
        }
        mat.SetColor("_BaseColor", tint);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", 0f);

        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_BlendModePreserveSpecular")) mat.SetFloat("_BlendModePreserveSpecular", 0f);
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.SetFloat("_AlphaClip", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetShaderPassEnabled("DepthOnly", false);
        mat.SetShaderPassEnabled("ShadowCaster", false);
        mat.renderQueue = (int)RenderQueue.Transparent;
    }

    // ---------------------------------------------------------------- the shard texture

    /// <summary>Draws the shard scatter once (deterministic, fixed seed) and imports it as a clamped, alpha-transparent texture.</summary>
    internal static Texture2D EnsureGlassTexture()
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(GlassTexturePath);
        if (existing != null) return existing;

        string folder = System.IO.Path.GetDirectoryName(GlassTexturePath).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(folder))
        {
            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
        }

        const int n = 512;
        Color[] px = new Color[n * n];
        // Transparent pixels carry the glass colour too, so bilinear filtering never fringes the edges dark.
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.85f, 0.95f, 1f, 0f);

        System.Random rng = new System.Random(9071);
        float centre = n * 0.5f, radius = n * 0.46f;
        for (int s = 0; s < 120; s++)
        {
            // Denser near the middle of the patch, a few strays out to the rim.
            double r = Mathf.Sqrt((float)rng.NextDouble()) * radius * (rng.NextDouble() < 0.8 ? 0.75f : 1f);
            double a = rng.NextDouble() * Mathf.PI * 2.0;
            Vector2 c = new Vector2(centre + (float)(System.Math.Cos(a) * r), centre + (float)(System.Math.Sin(a) * r));
            float size = Mathf.Lerp(9f, 34f, (float)rng.NextDouble() * (float)rng.NextDouble() * 1.2f);
            float rot = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            // A thin irregular triangle or sliver.
            Vector2[] v = new Vector2[3];
            for (int k = 0; k < 3; k++)
            {
                float ang = rot + k * 2.094f + (float)(rng.NextDouble() - 0.5) * 0.9f;
                float len = size * (k == 0 ? 1f : Mathf.Lerp(0.35f, 0.8f, (float)rng.NextDouble()));
                v[k] = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * len;
            }
            float tint = Mathf.Lerp(0.78f, 1f, (float)rng.NextDouble());
            DrawShard(px, n, v, tint, (float)rng.NextDouble());
        }

        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false, false);
        tex.SetPixels(px);
        tex.Apply();
        System.IO.File.WriteAllBytes(GlassTexturePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(GlassTexturePath, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(GlassTexturePath) is TextureImporter ti)
        {
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 512;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(GlassTexturePath);
    }

    /// <summary>A rounded, wobbly pool with a few satellite drops; alpha soft at the edge, a thin brighter rim (the meniscus).</summary>
    internal static Texture2D EnsurePuddleTexture()
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(PuddleTexturePath);
        if (existing != null) return existing;
        EnsureDecalFolder();

        const int n = 512;
        Color[] px = new Color[n * n];
        System.Random rng = new System.Random(4412);
        float[] amp = new float[5], phase = new float[5];
        for (int k = 0; k < 5; k++) { amp[k] = (0.045f / (k + 1)) * (0.6f + (float)rng.NextDouble()); phase[k] = (float)(rng.NextDouble() * Mathf.PI * 2.0); }
        // Satellite drops: (angle, distance, radius) in 0..1 texture units.
        Vector3[] drops = new Vector3[7];
        for (int k = 0; k < drops.Length; k++) drops[k] = new Vector3((float)(rng.NextDouble() * Mathf.PI * 2.0), Mathf.Lerp(0.40f, 0.47f, (float)rng.NextDouble()), Mathf.Lerp(0.008f, 0.02f, (float)rng.NextDouble()));

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / n - 0.5f, (y + 0.5f) / n - 0.5f);
                float dist = p.magnitude, ang = Mathf.Atan2(p.y, p.x);
                float edge = 0.33f;
                for (int k = 0; k < 5; k++) edge += amp[k] * Mathf.Sin((k + 2) * ang + phase[k]);
                float inside = (edge - dist) / 0.012f; // > 0 inside
                float a = Mathf.Clamp01(inside);
                foreach (Vector3 d in drops)
                {
                    Vector2 dc = new Vector2(Mathf.Cos(d.x), Mathf.Sin(d.x)) * d.y;
                    a = Mathf.Max(a, Mathf.Clamp01((d.z - Vector2.Distance(p, dc)) / 0.004f));
                }
                float rim = Mathf.Clamp01(1f - Mathf.Abs(inside - 0.5f) / 3f);
                float shade = Mathf.Lerp(0.55f, 1f, rim);
                px[y * n + x] = new Color(shade, shade, shade, a * 0.92f);
            }
        }
        return SaveDecal(px, n, PuddleTexturePath);
    }

    private static void EnsureDecalFolder()
    {
        string folder = System.IO.Path.GetDirectoryName(GlassTexturePath).Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }

    private static Texture2D SaveDecal(Color[] px, int n, string path)
    {
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false, false);
        tex.SetPixels(px);
        tex.Apply();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(path) is TextureImporter ti)
        {
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 512;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void DrawShard(Color[] px, int n, Vector2[] v, float tint, float edgeBias)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(v[0].x, Mathf.Min(v[1].x, v[2].x))) - 1);
        int x1 = Mathf.Min(n - 1, Mathf.CeilToInt(Mathf.Max(v[0].x, Mathf.Max(v[1].x, v[2].x))) + 1);
        int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(v[0].y, Mathf.Min(v[1].y, v[2].y))) - 1);
        int y1 = Mathf.Min(n - 1, Mathf.CeilToInt(Mathf.Max(v[0].y, Mathf.Max(v[1].y, v[2].y))) + 1);

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float d0 = EdgeDistance(p, v[0], v[1]);
                float d1 = EdgeDistance(p, v[1], v[2]);
                float d2 = EdgeDistance(p, v[2], v[0]);
                bool inside = Sign(p, v[0], v[1]) == Sign(p, v[1], v[2]) && Sign(p, v[1], v[2]) == Sign(p, v[2], v[0]);
                float edge = Mathf.Min(d0, Mathf.Min(d1, d2));
                if (!inside) continue;

                // Body: translucent; rim: brighter and nearly opaque, one long edge catches the light more (edgeBias).
                float rim = Mathf.Clamp01(1f - edge / 1.6f);
                float bright = Mathf.Lerp(tint * 0.85f, 1f, rim) + (d0 < 1.6f ? edgeBias * 0.15f : 0f);
                float alpha = Mathf.Lerp(0.42f, 0.95f, rim);
                Color existing = px[y * n + x];
                Color c = new Color(0.82f * bright, 0.94f * bright, 1f * bright, Mathf.Max(existing.a, alpha));
                px[y * n + x] = c;
            }
        }
    }

    private static bool Sign(Vector2 p, Vector2 a, Vector2 b) => ((p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y)) < 0f;

    private static float EdgeDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
        return Vector2.Distance(p, a + ab * t);
    }
}

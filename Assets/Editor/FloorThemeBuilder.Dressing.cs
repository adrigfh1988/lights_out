using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// F70 maze dressing: shared helpers used by every theme's Build* method in FloorThemeBuilder.cs, plus
/// the common (non-themed) prop/set-piece/dust set both the theme builders and KitMazeThemeBuilder pull
/// from. Kept in its own file per plannings/maze-dressing-plan.md decision 13; FloorThemeBuilder itself
/// is now `partial` so these helpers see its private consts (ThemesRoot, PieceSpacing, ...) and existing
/// low-level helpers (Box, CylinderRH, LoadOrCreateMat, SaveAsPrefab, FinishProp, ...) as if written in
/// the same file.
/// </summary>
public static partial class FloorThemeBuilder
{
    internal const string CommonRoot = ThemesRoot + "/Common";
    private const string CommonTexturesFolder = CommonRoot + "/Textures";

    // ---------------------------------------------------------------- decal textures (F70 section 3.1)

    /// <summary>
    /// Loads Themes/Common/Textures/{name}.png if it already exists (a hand-painted replacement
    /// survives a rebuild), otherwise procedurally draws it with a fixed seed and imports it with the
    /// exact sequence the plan calls out: WriteAllBytes -> ImportAsset -> TextureImporter settings ->
    /// SaveAndReimport -> LoadAssetAtPath. White RGB, shape carried entirely in alpha - tint comes from
    /// the material colour.
    /// </summary>
    private static Texture2D EnsureDecalTexture(string name)
    {
        string path = $"{CommonTexturesFolder}/{name}.png";
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        EnsureFolder(CommonTexturesFolder);

        Texture2D tex = new Texture2D(256, 256, TextureFormat.RGBA32, true);
        Color32[] blank = new Color32[256 * 256];
        for (int i = 0; i < blank.Length; i++) blank[i] = new Color32(255, 255, 255, 0);
        tex.SetPixels32(blank);

        switch (name)
        {
            case "Decal_BloodSplat": DrawBloodSplat(tex, new System.Random(101)); break;
            case "Decal_BloodSmear": DrawBloodSmear(tex, new System.Random(102)); break;
            case "Decal_BloodPool": DrawBloodPool(tex, new System.Random(103)); break;
            case "Decal_DragMarks": DrawDragMarks(tex, new System.Random(104)); break;
            case "Decal_Handprint": DrawHandprint(tex, new System.Random(105)); break;
            case "Decal_Claw": DrawClaw(tex, new System.Random(106)); break;
            case "Decal_Grime": DrawGrime(tex, new System.Random(107)); break;
            case "Decal_Drip": DrawDrip(tex, new System.Random(108)); break;
            case "Decal_Footprints": DrawFootprints(tex, new System.Random(109)); break;
            default:
                Debug.LogWarning($"FloorThemeBuilder: no generator for decal texture '{name}'.");
                break;
        }

        tex.Apply();
        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);

        File.WriteAllBytes(path, png);
        AssetDatabase.ImportAsset(path);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // -- low-level pixel drawing --------------------------------------------------------------------

    private static void BlendPixel(Texture2D tex, int x, int y, float alpha, float rgb)
    {
        if (x < 0 || y < 0 || x >= tex.width || y >= tex.height || alpha <= 0f) return;
        Color existing = tex.GetPixel(x, y);
        float a = Mathf.Max(existing.a, Mathf.Clamp01(alpha));
        tex.SetPixel(x, y, new Color(rgb, rgb, rgb, a));
    }

    private static void Disc(Texture2D tex, float cx, float cy, float radius, float softness, float rgb, float maxAlpha = 1f)
    {
        int r = Mathf.CeilToInt(radius + softness);
        int icx = Mathf.RoundToInt(cx), icy = Mathf.RoundToInt(cy);
        for (int y = -r; y <= r; y++)
        for (int x = -r; x <= r; x++)
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float a = (1f - Mathf.Clamp01((d - radius) / Mathf.Max(0.01f, softness))) * maxAlpha;
            if (a <= 0f) continue;
            BlendPixel(tex, icx + x, icy + y, a, rgb);
        }
    }

    private static void Ellipse(Texture2D tex, float cx, float cy, float rx, float ry, float angleDeg, float softness, float rgb, float maxAlpha = 1f)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(-rad), sin = Mathf.Sin(-rad);
        int r = Mathf.CeilToInt(Mathf.Max(rx, ry) + softness);
        int icx = Mathf.RoundToInt(cx), icy = Mathf.RoundToInt(cy);
        for (int y = -r; y <= r; y++)
        for (int x = -r; x <= r; x++)
        {
            float lx = x * cos - y * sin;
            float ly = x * sin + y * cos;
            float d = Mathf.Sqrt((lx * lx) / (rx * rx) + (ly * ly) / (ry * ry));
            float a = (1f - Mathf.Clamp01((d - 1f) * Mathf.Max(rx, ry) / Mathf.Max(0.01f, softness))) * maxAlpha;
            if (a <= 0f) continue;
            BlendPixel(tex, icx + x, icy + y, a, rgb);
        }
    }

    /// <summary>A jittered, alpha-faded thick line - blood smears, claw gouges, drip lines.</summary>
    private static void Stroke(Texture2D tex, System.Random rng, float x0, float y0, float x1, float y1, float width, float alphaStart, float alphaEnd, float jitter)
    {
        float length = Mathf.Max(1f, Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)));
        int steps = Mathf.CeilToInt(length) * 2;
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float px = Mathf.Lerp(x0, x1, t) + (float)(rng.NextDouble() - 0.5) * jitter;
            float py = Mathf.Lerp(y0, y1, t) + (float)(rng.NextDouble() - 0.5) * jitter;
            float alpha = Mathf.Lerp(alphaStart, alphaEnd, t);
            Disc(tex, px, py, width * 0.5f, 1f, 1f, alpha);
        }
    }

    /// <summary>A central disc plus a scatter of shrinking satellite droplets - the shared shape behind BloodSplat.</summary>
    private static void Splat(Texture2D tex, System.Random rng, float cx, float cy, float r, int blobs)
    {
        Disc(tex, cx, cy, r, 3f, 1f);
        for (int i = 0; i < blobs; i++)
        {
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            float dist = r * (0.6f + (float)rng.NextDouble() * 1.6f);
            float bx = cx + Mathf.Cos(angle) * dist;
            float by = cy + Mathf.Sin(angle) * dist;
            float br = r * (0.06f + (float)rng.NextDouble() * 0.14f);
            Disc(tex, bx, by, br, 1.5f, 1f);
        }
    }

    private static void DrawBloodSplat(Texture2D tex, System.Random rng)
    {
        Splat(tex, rng, 128, 128, 44, 8);
    }

    private static void DrawBloodSmear(Texture2D tex, System.Random rng)
    {
        Stroke(tex, rng, 34, 128, 210, 118, 60, 0.9f, 0.55f, 6f);
        for (int i = 0; i < 5; i++)
        {
            float t = (float)i / 4f;
            float x0 = Mathf.Lerp(120f, 220f, t);
            Stroke(tex, rng, x0, 120f, x0 + 8f, 180f + i * 6f, 6f, 0.5f, 0.05f, 3f);
        }
    }

    private static void DrawBloodPool(Texture2D tex, System.Random rng)
    {
        for (int i = 0; i < 6; i++)
        {
            float ox = (float)(rng.NextDouble() - 0.5) * 30f;
            float oy = (float)(rng.NextDouble() - 0.5) * 30f;
            float rx = 60f + (float)rng.NextDouble() * 30f;
            float ry = 60f + (float)rng.NextDouble() * 30f;
            float angle = (float)rng.NextDouble() * 180f;
            Ellipse(tex, 128 + ox, 128 + oy, rx, ry, angle, 3f, 1f);
        }
    }

    private static void DrawDragMarks(Texture2D tex, System.Random rng)
    {
        foreach (float lane in new[] { -34f, 34f })
        {
            float x = 24f;
            while (x < 232f)
            {
                float dashLen = 26f + (float)rng.NextDouble() * 16f;
                Stroke(tex, rng, x, 128f + lane, Mathf.Min(232f, x + dashLen), 128f + lane, 16f, 0.65f, 0.3f, 2f);
                x += dashLen + 14f + (float)rng.NextDouble() * 10f;
            }
        }
    }

    private static void DrawHandprint(Texture2D tex, System.Random rng)
    {
        Ellipse(tex, 128, 150, 34, 46, 0f, 2f, 1f);
        for (int i = 0; i < 4; i++)
        {
            float fx = 100 + i * 20;
            float fAngle = -18 + i * 12;
            Ellipse(tex, fx, 90 - i * 2, 9, 30, fAngle, 1.5f, 1f);
        }
        Ellipse(tex, 96, 150, 12, 26, 55f, 1.5f, 1f); // thumb
        // smear trailing down from the palm
        Stroke(tex, rng, 128, 190, 132, 226, 26, 0.55f, 0.05f, 4f);
    }

    private static void DrawClaw(Texture2D tex, System.Random rng)
    {
        for (int i = 0; i < 4; i++)
        {
            float x0 = 60 + i * 30;
            Stroke(tex, rng, x0, 40, x0 + 30, 216, 5f, 0.9f, 0.6f, 5f);
        }
    }

    private static void DrawGrime(Texture2D tex, System.Random rng)
    {
        for (int i = 0; i < 40; i++)
        {
            float bx = (float)rng.NextDouble() * 256f;
            float by = (float)rng.NextDouble() * 256f;
            float br = 20f + (float)rng.NextDouble() * 60f;
            float ba = 0.15f + (float)rng.NextDouble() * 0.25f;
            Disc(tex, bx, by, br, br * 0.6f, 1f, ba);
        }
    }

    private static void DrawDrip(Texture2D tex, System.Random rng)
    {
        Ellipse(tex, 128, 34, 70, 30, 0f, 4f, 1f);
        int drips = 3 + rng.Next(4);
        for (int i = 0; i < drips; i++)
        {
            float x = 70 + (float)rng.NextDouble() * 116f;
            float len = 90f + (float)rng.NextDouble() * 110f;
            Stroke(tex, rng, x, 40f, x, Mathf.Min(250f, 40f + len), 6f, 0.7f, 0.1f, 1.5f);
        }
    }

    private static void DrawFootprints(Texture2D tex, System.Random rng)
    {
        for (int i = 0; i < 4; i++)
        {
            float y = 40 + i * 50;
            float x = 128 + (i % 2 == 0 ? -22f : 22f);
            Ellipse(tex, x, y, 16, 26, (float)(rng.NextDouble() - 0.5) * 10f, 2f, 1f);
            Ellipse(tex, x, y - 30, 10, 12, 0f, 1.5f, 1f); // toe pad
        }
    }

    // ---------------------------------------------------------------- decal materials + prefabs (3.2)

    /// <summary>URP Lit, alpha-clipped, per decision 2: no Decal Projector, works on both render assets.</summary>
    private static Material LoadOrCreateDecalMat(string folder, string name, Texture2D texture, Color color, float smoothness)
    {
        string path = $"{folder}/Materials/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        Material mat = new Material(shader) { name = name };

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (texture != null)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
        }

        if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.4f);
        if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.renderQueue = 2450;

        EnsureFolder($"{folder}/Materials");
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    /// <summary>
    /// A prop-piece root with one Quad child using the given decal material, per the pivot convention in
    /// decision 3.2: wall decal child rotation Euler(0,180,0) (built-in Quad faces -Z, this turns it to
    /// face +Z, matching every wall prop's "faces into the corridor" convention); floor decal child
    /// rotation Euler(90,0,0) (normal -> +Y). No collider, shadows off, root scale left at 1 (MazeGenerator
    /// sets it from DecalSizeRange at Play time).
    /// </summary>
    private static PropPiece CreateDecalProp(string folder, string name, Material material, PropPiece.MountKind mount, Vector2 sizeRange, float weight, bool randomRoll = true)
    {
        GameObject root = new GameObject(name);
        CreateDecalQuad(root.transform, "Quad", material, mount == PropPiece.MountKind.FloorDecal, Vector3.zero, Quaternion.identity, 1f);

        PropPiece prop = root.AddComponent<PropPiece>();
        SerializedObject so = new SerializedObject(prop);
        so.FindProperty("mount").enumValueIndex = (int)mount;
        so.FindProperty("depth").floatValue = 0f;
        so.FindProperty("weight").floatValue = weight;
        so.FindProperty("enabledInMaze").boolValue = true;
        so.FindProperty("decalSizeRange").vector2Value = sizeRange;
        so.FindProperty("randomRoll").boolValue = randomRoll;
        so.ApplyModifiedPropertiesWithoutUndo();

        return SaveAsPrefab<PropPiece>(root, folder, name);
    }

    /// <summary>A single decal quad, used both by CreateDecalProp (standalone prop) and by set pieces that bake a decal directly in as a child (no PropPiece, no separate placement pass).</summary>
    private static GameObject CreateDecalQuad(Transform parent, string name, Material material, bool isFloor, Vector3 localPosition, Quaternion extraSpin, float scale)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.transform.SetParent(parent, false);

        Collider collider = quad.GetComponent<Collider>();
        if (collider != null) Object.DestroyImmediate(collider);

        MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;

        Quaternion baseRotation = isFloor ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.Euler(0f, 180f, 0f);
        quad.transform.localPosition = localPosition;
        quad.transform.localRotation = extraSpin * baseRotation;
        quad.transform.localScale = Vector3.one * scale;
        return quad;
    }

    /// <summary>The seven materials the common blood/handprint/claw/footprint decals share across every theme (decision: prefabs are per-theme, materials are common - see plan section 3.2). Built once and cached.</summary>
    internal struct CommonDecalMaterials
    {
        public Material BloodSplat, BloodSmear, Handprint, Claw, BloodPool, DragMarks, Footprints;
    }

    /// <summary>
    /// Clears every F70 static cache that points at an asset under Themes/Common (decal materials,
    /// common floor props, the shared FallenRunner set piece). Called by FloorThemeBuilder.Build()
    /// alongside its own _plinthMaterial reset, right after a "Replace everything" deletes ThemesRoot -
    /// without this, a stale cache would hand back references to assets that no longer exist.
    /// </summary>
    private static void ResetDressingCaches()
    {
        _commonDecalMats = null;
        _commonFloorProps = null;
        _fallenRunner = null;
    }

    private static CommonDecalMaterials? _commonDecalMats;

    private static CommonDecalMaterials GetCommonDecalMaterials()
    {
        if (_commonDecalMats.HasValue) return _commonDecalMats.Value;

        Color blood = new Color(0.28f, 0.02f, 0.02f);
        Color pool = new Color(0.22f, 0.01f, 0.01f);
        Color drag = new Color(0.2f, 0.02f, 0.02f);
        Color foot = new Color(0.15f, 0.02f, 0.02f);
        Color claw = new Color(0.05f, 0.05f, 0.05f);

        CommonDecalMaterials mats = new CommonDecalMaterials
        {
            BloodSplat = LoadOrCreateDecalMat(CommonRoot, "BloodSplat", EnsureDecalTexture("Decal_BloodSplat"), blood, 0.55f),
            BloodSmear = LoadOrCreateDecalMat(CommonRoot, "BloodSmear", EnsureDecalTexture("Decal_BloodSmear"), blood, 0.55f),
            Handprint = LoadOrCreateDecalMat(CommonRoot, "Handprint", EnsureDecalTexture("Decal_Handprint"), blood, 0.55f),
            Claw = LoadOrCreateDecalMat(CommonRoot, "ClawMarks", EnsureDecalTexture("Decal_Claw"), claw, 0.15f),
            BloodPool = LoadOrCreateDecalMat(CommonRoot, "BloodPool", EnsureDecalTexture("Decal_BloodPool"), pool, 0.8f),
            DragMarks = LoadOrCreateDecalMat(CommonRoot, "DragMarks", EnsureDecalTexture("Decal_DragMarks"), drag, 0.15f),
            Footprints = LoadOrCreateDecalMat(CommonRoot, "Footprints", EnsureDecalTexture("Decal_Footprints"), foot, 0.15f),
        };
        _commonDecalMats = mats;
        return mats;
    }

    /// <summary>
    /// The common decal set (3.2), built into `folder` under prefab names prefixed with `prefix` (a
    /// theme's own prefab folder for a themed row, or CommonRoot for the kit's copies) - decal prefabs
    /// are per-theme because weight is serialized on the prefab, but the blood/handprint/claw/footprint
    /// materials passed in via `common` are the shared common assets; grime/drip/floorGrime get the
    /// per-theme-tinted material the caller built. bloodWeightScale/handClawWeightScale apply the F70
    /// per-theme weighting (Lab halves blood, Hollow doubles handprint/claw).
    /// </summary>
    private static PropPiece[] BuildThemeDecals(string folder, string prefix, CommonDecalMaterials common, Material grimeMat, Material dripMat, Material floorGrimeMat, float bloodWeightScale = 1f, float handClawWeightScale = 1f)
    {
        return new[]
        {
            CreateDecalProp(folder, $"{prefix}_BloodSplat", common.BloodSplat, PropPiece.MountKind.WallDecal, new Vector2(0.5f, 1.1f), 1f * bloodWeightScale),
            CreateDecalProp(folder, $"{prefix}_BloodSmear", common.BloodSmear, PropPiece.MountKind.WallDecal, new Vector2(0.8f, 1.6f), 1f * bloodWeightScale),
            CreateDecalProp(folder, $"{prefix}_Handprint", common.Handprint, PropPiece.MountKind.WallDecal, new Vector2(0.22f, 0.3f), 1.5f * handClawWeightScale),
            CreateDecalProp(folder, $"{prefix}_ClawMarks", common.Claw, PropPiece.MountKind.WallDecal, new Vector2(0.5f, 0.9f), 1f * handClawWeightScale),
            CreateDecalProp(folder, $"{prefix}_Grime", grimeMat, PropPiece.MountKind.WallDecal, new Vector2(1.0f, 1.8f), 2f),
            CreateDecalProp(folder, $"{prefix}_Drip", dripMat, PropPiece.MountKind.WallDecal, new Vector2(0.8f, 1.4f), 1f),
            CreateDecalProp(folder, $"{prefix}_BloodPool", common.BloodPool, PropPiece.MountKind.FloorDecal, new Vector2(0.8f, 1.6f), 1f * bloodWeightScale),
            CreateDecalProp(folder, $"{prefix}_DragMarks", common.DragMarks, PropPiece.MountKind.FloorDecal, new Vector2(1.5f, 2.5f), 0.7f, randomRoll: false),
            CreateDecalProp(folder, $"{prefix}_Footprints", common.Footprints, PropPiece.MountKind.FloorDecal, new Vector2(1.2f, 1.8f), 0.7f, randomRoll: false),
            CreateDecalProp(folder, $"{prefix}_FloorGrime", floorGrimeMat, PropPiece.MountKind.FloorDecal, new Vector2(1.2f, 2.2f), 2f),
        };
    }

    // ---------------------------------------------------------------- common floor props (3.3)

    private static PropPiece[] _commonFloorProps;

    /// <summary>Papers, Debris, Bucket, Bottles - single shared prefabs in Themes/Common/Prefabs, weights fixed, added to every theme's props and to the kit set. Built once and cached.</summary>
    private static PropPiece[] BuildCommonFloorProps()
    {
        if (_commonFloorProps != null) return _commonFloorProps;

        Material paperMat = LoadOrCreateMat(CommonRoot, "Common_Paper", new Color(0.55f, 0.55f, 0.5f), null, null, Vector2.one, null);
        Material debrisMat = LoadOrCreateMat(CommonRoot, "Common_Debris", new Color(0.3f, 0.3f, 0.3f), null, null, Vector2.one, null);
        Material bucketMat = LoadOrCreateMat(CommonRoot, "Common_Bucket", new Color(0.25f, 0.25f, 0.28f), null, null, Vector2.one, null, smoothness: 0.3f);
        Material bottleMat = LoadOrCreateMat(CommonRoot, "Common_Bottle", new Color(0.05f, 0.08f, 0.05f), null, null, Vector2.one, null, smoothness: 0.6f);

        GameObject papersRoot = new GameObject("Papers");
        System.Random papersRng = new System.Random(201);
        int paperCount = 5 + papersRng.Next(4);
        for (int i = 0; i < paperCount; i++)
        {
            float px = (float)(papersRng.NextDouble() - 0.5) * 0.35f;
            float pz = (float)papersRng.NextDouble() * 0.3f;
            float yaw = (float)papersRng.NextDouble() * 360f;
            GameObject sheet = Box(papersRoot.transform, $"Sheet{i}", new Vector3(px, 0.0015f * (i + 1), pz), new Vector3(0.21f, 0.003f, 0.3f), paperMat);
            sheet.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }
        PropPiece papers = FinishProp(papersRoot, CommonRoot, PropPiece.MountKind.Floor, 0.3f, 1f);

        GameObject debrisRoot = new GameObject("Debris");
        System.Random debrisRng = new System.Random(202);
        for (int i = 0; i < 6; i++)
        {
            float dx = (float)(debrisRng.NextDouble() - 0.5) * 0.4f;
            float dz = (float)debrisRng.NextDouble() * 0.4f;
            float s = 0.05f + (float)debrisRng.NextDouble() * 0.1f;
            if (debrisRng.NextDouble() < 0.5) Box(debrisRoot.transform, $"Chunk{i}", new Vector3(dx, s * 0.5f, dz), Vector3.one * s, debrisMat);
            else Sphere(debrisRoot.transform, $"Chunk{i}", new Vector3(dx, s * 0.5f, dz), Vector3.one * s, debrisMat);
        }
        PropPiece debris = FinishProp(debrisRoot, CommonRoot, PropPiece.MountKind.Floor, 0.4f, 1f);

        GameObject bucketRoot = new GameObject("Bucket");
        GameObject bucketBody = CylinderRH(bucketRoot.transform, "Body", new Vector3(0f, 0.15f, 0.2f), 0.15f, 0.3f, bucketMat);
        bucketBody.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        PropPiece bucket = FinishProp(bucketRoot, CommonRoot, PropPiece.MountKind.Floor, 0.35f, 0.7f);

        GameObject bottlesRoot = new GameObject("Bottles");
        CylinderRH(bottlesRoot.transform, "Bottle0", new Vector3(-0.1f, 0.08f, 0.15f), 0.03f, 0.18f, bottleMat).transform.localRotation = Quaternion.Euler(0f, 0f, 80f);
        CylinderRH(bottlesRoot.transform, "Bottle1", new Vector3(0.05f, 0.09f, 0.2f), 0.03f, 0.18f, bottleMat);
        CylinderRH(bottlesRoot.transform, "Bottle2", new Vector3(0.15f, 0.09f, 0.1f), 0.03f, 0.18f, bottleMat);
        PropPiece bottles = FinishProp(bottlesRoot, CommonRoot, PropPiece.MountKind.Floor, 0.3f, 0.7f);

        _commonFloorProps = new[] { papers, debris, bucket, bottles };
        return _commonFloorProps;
    }

    // ---------------------------------------------------------------- set pieces (3.4)

    private static SetPiece FinishSetPiece(GameObject root, string folder, float width, float depth, float weight, bool enabledInMaze = true)
    {
        SetPiece piece = root.AddComponent<SetPiece>();
        SerializedObject so = new SerializedObject(piece);
        so.FindProperty("width").floatValue = width;
        so.FindProperty("depth").floatValue = depth;
        so.FindProperty("weight").floatValue = weight;
        so.FindProperty("enabledInMaze").boolValue = enabledInMaze;
        so.ApplyModifiedPropertiesWithoutUndo();
        return SaveAsPrefab<SetPiece>(root, folder, root.name);
    }

    /// <summary>Configures one FlickerLight child by SerializedObject (mode/targetLight/emissiveRenderer), the same field-name contract the runtime component exposes.</summary>
    private static void ConfigureFlicker(GameObject go, FlickerLight.FlickerMode mode, Light targetLight, Renderer emissiveRenderer)
    {
        FlickerLight flicker = go.AddComponent<FlickerLight>();
        SerializedObject so = new SerializedObject(flicker);
        so.FindProperty("mode").enumValueIndex = (int)mode;
        so.FindProperty("targetLight").objectReferenceValue = targetLight;
        so.FindProperty("emissiveRenderer").objectReferenceValue = emissiveRenderer;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static SetPiece _fallenRunner;

    /// <summary>The one set piece shared by every theme and the kit maze (decision: theme setPieces = its 2 + FallenRunner; kit setPieces = FallenRunner). Built once and cached.</summary>
    private static SetPiece BuildFallenRunnerSetPiece(CommonDecalMaterials common)
    {
        if (_fallenRunner != null) return _fallenRunner;

        Material clothMat = LoadOrCreateMat(CommonRoot, "Common_TarpCloth", new Color(0.07f, 0.07f, 0.08f), null, null, Vector2.one, null);
        Material torchMat = LoadOrCreateMat(CommonRoot, "Common_Torch", new Color(0.12f, 0.12f, 0.12f), null, null, Vector2.one, null, smoothness: 0.4f);

        GameObject root = new GameObject("FallenRunner");

        GameObject body = Capsule(root.transform, "Body", new Vector3(0f, 0.12f, 0.55f), new Vector3(0.42f, 0.85f, 0.42f), clothMat, keepCollider: true);
        body.transform.localRotation = Quaternion.Euler(0f, 5f, 90f);

        GameObject arm = Capsule(root.transform, "Arm", new Vector3(0.35f, 0.1f, 0.95f), new Vector3(0.1f, 0.3f, 0.1f), clothMat);
        arm.transform.localRotation = Quaternion.Euler(0f, 25f, 85f);

        GameObject torch = CylinderRH(root.transform, "Torch", new Vector3(0.5f, 0.05f, 1.08f), 0.025f, 0.22f, torchMat);
        torch.transform.localRotation = Quaternion.Euler(0f, 0f, 75f);

        GameObject lightGo = new GameObject("Light");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0.55f, 0.06f, 1.1f);
        lightGo.transform.localRotation = Quaternion.LookRotation(Vector3.forward + Vector3.down * 0.3f);
        Light spot = lightGo.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.range = 4f;
        spot.spotAngle = 35f;
        spot.intensity = 0.6f;
        spot.color = new Color(1f, 0.75f, 0.4f);
        spot.shadows = LightShadows.None;

        CreateDecalQuad(root.transform, "BloodPoolDecal", common.BloodPool, true, new Vector3(0f, 0.006f, 0.55f), Quaternion.Euler(0f, 20f, 0f), 1.2f);
        CreateDecalQuad(root.transform, "DragMarksDecal", common.DragMarks, true, new Vector3(0f, 0.006f, 1.9f), Quaternion.identity, 2.0f);
        CreateDecalQuad(root.transform, "HandprintDecal", common.Handprint, false, new Vector3(0f, 1.3f, 1.98f), Quaternion.identity, 0.28f);

        ConfigureFlicker(root, FlickerLight.FlickerMode.Faulty, spot, null);

        _fallenRunner = FinishSetPiece(root, CommonRoot, width: 1.8f, depth: 0.5f, weight: 1f);
        return _fallenRunner;
    }

    // ---------------------------------------------------------------- particle systems (Part B, section 5)

    private static Material LoadOrCreateParticleMat(string folder, string name, Color color, bool lit)
    {
        string path = $"{folder}/Materials/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find(lit ? "Universal Render Pipeline/Particles/Lit" : "Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        Material mat = new Material(shader) { name = name };

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);

        if (!lit && mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 2f);
        }
        if (!lit && mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (!lit && mat.HasProperty("_Color")) mat.SetColor("_Color", color);

        EnsureFolder($"{folder}/Materials");
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    /// <summary>
    /// One camera-attached dust ParticleSystem prefab (decision 12): Lit, opaque, World simulation
    /// (motes drift, not follow), small size so they only read in the torch beam. Root carries DustMotes.
    /// </summary>
    private static DustMotes BuildDustPrefab(string folder, string name, Color color, float rate, float sizeMin, float sizeMax, float speedMin = 0.02f, float speedMax = 0.08f, System.Action<GameObject> extraChildren = null)
    {
        string path = $"{folder}/Prefabs/{name}.prefab";
        GameObject existingAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existingAsset != null) return existingAsset.GetComponent<DustMotes>();

        GameObject root = new GameObject(name);
        ParticleSystem ps = root.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;
        main.playOnAwake = true;
        main.prewarm = true;

        var emission = ps.emission;
        emission.rateOverTime = rate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(7f, 3f, 7f);
        shape.position = new Vector3(0f, 0f, 2.5f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.05f;
        noise.frequency = 0.3f;
        noise.scrollSpeed = 0.1f;

        ParticleSystemRenderer renderer = root.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = LoadOrCreateParticleMat(folder, name + "Mat", color, lit: true);
        renderer.receiveShadows = false;
        renderer.shadowCastingMode = ShadowCastingMode.Off;

        extraChildren?.Invoke(root);
        root.AddComponent<DustMotes>();

        return SaveAsPrefab<DustMotes>(root, folder, name);
    }

    /// <summary>A small local-space child particle system for a set piece or a theme's rare unlit accent (embers, steam, sparks) - Particles/Unlit, opaque, emissive-bright.</summary>
    private static ParticleSystem AddLocalParticles(Transform parent, string folder, string name, Color color, float rateOverTime, float lifetimeMin, float lifetimeMax, float sizeMin, float sizeMax, float upSpeed, Vector3 boxScale, bool loop = true)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        ParticleSystem ps = child.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = loop;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeMin, lifetimeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(upSpeed * 0.7f, upSpeed * 1.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 60;
        main.playOnAwake = true;
        main.prewarm = loop;

        var emission = ps.emission;
        emission.rateOverTime = rateOverTime;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = boxScale;

        ParticleSystemRenderer renderer = child.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = LoadOrCreateParticleMat(folder, name + "Mat", color, lit: false);
        renderer.shadowCastingMode = ShadowCastingMode.Off;

        return ps;
    }

    // ---------------------------------------------------------------- kit common dressing (F70)

    /// <summary>What KitMazeThemeBuilder assigns to KitMazeTheme.Props/SetPieces/Dust: common floor clutter, common decal copies (weighted "Kit"), the Switch wrapped as a collider-less Floor prop, the shared FallenRunner set piece and the kit's own dust prefab. No wall/ceiling props (decision 7).</summary>
    internal struct CommonDressing
    {
        public PropPiece[] Props;
        public SetPiece[] SetPieces;
        public DustMotes Dust;
    }

    internal static CommonDressing BuildCommonDressing(GameObject switchPrefab)
    {
        List<PropPiece> props = new List<PropPiece>();
        props.AddRange(BuildCommonFloorProps());

        CommonDecalMaterials common = GetCommonDecalMaterials();
        Material kitGrime = LoadOrCreateDecalMat(CommonRoot, "Kit_Grime", EnsureDecalTexture("Decal_Grime"), new Color(0.07f, 0.07f, 0.06f), 0.1f);
        Material kitDrip = LoadOrCreateDecalMat(CommonRoot, "Kit_Drip", EnsureDecalTexture("Decal_Drip"), new Color(0.15f, 0.09f, 0.04f), 0.2f);
        Material kitFloorGrime = LoadOrCreateDecalMat(CommonRoot, "Kit_FloorGrime", EnsureDecalTexture("Decal_Grime"), new Color(0.06f, 0.06f, 0.05f), 0.1f);
        props.AddRange(BuildThemeDecals(CommonRoot, "Kit", common, kitGrime, kitDrip, kitFloorGrime));

        if (switchPrefab != null)
        {
            GameObject switchRoot = new GameObject("SwitchLever");
            GameObject switchInstance = (GameObject)PrefabUtility.InstantiatePrefab(switchPrefab, switchRoot.transform);
            switchInstance.transform.localPosition = new Vector3(0f, 0f, -0.1f);
            foreach (Collider collider in switchInstance.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }
            props.Add(FinishProp(switchRoot, CommonRoot, PropPiece.MountKind.Floor, 0.3f, 0.6f));
        }

        SetPiece fallenRunner = BuildFallenRunnerSetPiece(common);
        DustMotes dust = BuildDustPrefab(CommonRoot, "Kit_Dust", new Color(0.8f, 0.8f, 0.75f), 25f, 0.015f, 0.03f);

        return new CommonDressing
        {
            Props = props.ToArray(),
            SetPieces = new[] { fallenRunner },
            Dust = dust
        };
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Repairs set-piece prefabs built before 7 Oct 2026 (idempotent):
///  - their wall decals (Ward Triage smear and claw, Crypt drip, Hollow handprints and claw, FallenRunner handprint)
///    sat at local z = 1.98, i.e. 2 m out from the wall the set piece is mounted on - in mid-air. They move to
///    FloorThemeBuilder.WallDecalInset, just in front of the wall face (the set piece's pivot);
///  - a quad using a shared blood material (BloodSplat/BloodSmear/BloodPool, now Blood decal pack textures - see
///    BloodDecalUpgrade) is reshaped to its texture's aspect, longest side kept, so a 2.6:1 smear isn't squashed square.
/// Run automatically by Build &gt; Floor Themes and Build &gt; Kit Maze Theme; Check Project flags what's left.
/// </summary>
public static class SetPieceDecalFix
{
    private const string ThemesRoot = "Assets/SourceFiles/Themes";
    private const float OldWallDecalZ = 1.98f;
    private static readonly string[] SharedBlood = { "BloodSplat", "BloodSmear", "BloodPool" };

    public static bool NeedsFix()
    {
        foreach (string path in SetPiecePaths())
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && FixHierarchy(prefab.transform, dryRun: true) > 0) return true;
        }
        return false;
    }

    /// <summary>Returns how many prefabs it rewrote.</summary>
    public static int Apply()
    {
        int written = 0;
        foreach (string path in SetPiecePaths())
        {
            GameObject loaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (loaded == null || FixHierarchy(loaded.transform, dryRun: true) == 0) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                FixHierarchy(root.transform, dryRun: false);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                written++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        if (written > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"SetPieceDecalFix: re-seated the wall decals / blood quads in {written} set-piece prefab(s).");
        }
        return written;
    }

    /// <summary>Counts (and unless dryRun, fixes) the misplaced wall decals and mis-shaped blood quads under root.</summary>
    private static int FixHierarchy(Transform root, bool dryRun)
    {
        int count = 0;
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            Transform t = renderer.transform;
            if (t == root) continue;
            Material mat = renderer.sharedMaterial;
            if (mat == null) continue;

            // A wall decal: a quad turned to face into the cell (CreateDecalQuad's Euler(0, 180, 0) base), at the old 2 m offset.
            bool isWallQuad = Mathf.Abs(Mathf.DeltaAngle(t.localEulerAngles.x, 0f)) < 1f;
            if (isWallQuad && Mathf.Abs(t.localPosition.z - OldWallDecalZ) < 0.01f && renderer.GetComponent<MeshFilter>() != null
                && renderer.GetComponent<MeshFilter>().sharedMesh != null && renderer.GetComponent<MeshFilter>().sharedMesh.name == "Quad")
            {
                count++;
                if (!dryRun)
                {
                    Vector3 p = t.localPosition;
                    t.localPosition = new Vector3(p.x, p.y, FloorThemeBuilder.WallDecalInset);
                }
            }

            if (System.Array.IndexOf(SharedBlood, mat.name) >= 0)
            {
                float aspect = TextureAspect(mat.GetTexture("_BaseMap"));
                Vector3 s = t.localScale;
                float side = Mathf.Max(s.x, s.y);
                Vector3 want = aspect >= 1f ? new Vector3(side, side / aspect, s.z) : new Vector3(side * aspect, side, s.z);
                if ((want - s).sqrMagnitude > 0.0001f)
                {
                    count++;
                    if (!dryRun) t.localScale = want;
                }
            }
        }
        return count;
    }

    private static float TextureAspect(Texture texture)
    {
        if (texture == null) return 1f;
        if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter importer)
        {
            importer.GetSourceTextureWidthAndHeight(out int w, out int h);
            if (w > 0 && h > 0) return (float)w / h;
        }
        return texture.height > 0 ? (float)texture.width / texture.height : 1f;
    }

    private static IEnumerable<string> SetPiecePaths()
    {
        if (!AssetDatabase.IsValidFolder(ThemesRoot)) yield break;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ThemesRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && prefab.GetComponent<SetPiece>() != null) yield return path;
        }
    }
}

using UnityEditor;
using UnityEngine;

/// <summary>
/// The FallenRunner set piece's corpse: the primitive capsule body and arm are replaced by the six lying
/// bodies of Assets/DeadBody LITE (Body_Only/DeadBody (1..6), already URP Lit, real-world scale, box collider),
/// one active per placement via ModelVariants (MazeGenerator.BuildSetPieces picks by a hash of the cell).
///
/// Each body is laid along the wall (the pack models lie along their Z, the set piece's wall runs along X) and
/// seated by its renderer bounds where the capsule lay - centre (0, floor, 0.55) - since pack pivots are not
/// at the model's centre. The torch, its flickering light and the blood/drag/handprint decals are kept.
///
/// Used two ways: FloorThemeBuilder.BuildFallenRunnerSetPiece calls AddBodies for a fresh build, and Apply
/// rewrites an existing FallenRunner prefab (idempotent; run by Build &gt; Floor Themes / Kit Maze Theme and
/// flagged by Check Project). Without the pack nothing changes and the capsule body stays.
/// </summary>
public static class CorpseUpgrade
{
    public const string FallenRunnerPath = "Assets/SourceFiles/Themes/Common/Prefabs/FallenRunner.prefab";
    private const string BodyFolder = "Assets/DeadBody LITE/Prefabs/Body_Only";
    private const string BodiesName = "Bodies";

    /// <summary>Where the capsule body lay, set-piece local: on the floor, 0.55 m out from the wall.</summary>
    private static readonly Vector3 BodyCentre = new Vector3(0f, 0f, 0.55f);

    public static bool PackPresent => AssetDatabase.IsValidFolder(BodyFolder);

    public static bool NeedsUpgrade()
    {
        if (!PackPresent) return false;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FallenRunnerPath);
        if (prefab == null) return false;
        ModelVariants variants = prefab.GetComponent<ModelVariants>();
        return variants == null || variants.Count == 0 || prefab.transform.Find("Body") != null;
    }

    /// <summary>Rewrites the FallenRunner prefab with the pack bodies. True if it changed anything.</summary>
    public static bool Apply()
    {
        if (!NeedsUpgrade()) return false;

        GameObject root = PrefabUtility.LoadPrefabContents(FallenRunnerPath);
        try
        {
            if (!AddBodies(root.transform)) return false;
            PrefabUtility.SaveAsPrefabAsset(root, FallenRunnerPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("CorpseUpgrade: FallenRunner now uses the DeadBody LITE corpses.");
        return true;
    }

    /// <summary>
    /// Removes the primitive Body/Arm capsules under root and adds the pack bodies plus a ModelVariants listing them.
    /// False (root untouched) if the pack is missing.
    /// </summary>
    public static bool AddBodies(Transform root)
    {
        GameObject[] prefabs = LoadBodies();
        if (prefabs.Length == 0) return false;

        foreach (string old in new[] { "Body", "Arm", BodiesName })
        {
            Transform t = root.Find(old);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        GameObject holder = new GameObject(BodiesName);
        holder.transform.SetParent(root, false);

        GameObject[] options = new GameObject[prefabs.Length];
        for (int i = 0; i < prefabs.Length; i++)
        {
            GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], holder.transform);
            body.transform.localPosition = Vector3.zero;
            // Lengthwise along the wall, a few degrees off square so it reads as fallen, not laid out.
            body.transform.localRotation = Quaternion.Euler(0f, 90f + (i % 2 == 0 ? 6f : -8f), 0f);

            foreach (Rigidbody rb in body.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);

            Bounds b = LocalBounds(root, body);
            body.transform.localPosition += new Vector3(BodyCentre.x - b.center.x, BodyCentre.y - b.min.y, BodyCentre.z - b.center.z);
            body.SetActive(i == 0);
            options[i] = body;
        }

        ModelVariants variants = root.GetComponent<ModelVariants>();
        if (variants == null) variants = root.gameObject.AddComponent<ModelVariants>();
        SerializedObject so = new SerializedObject(variants);
        SerializedProperty prop = so.FindProperty("options");
        prop.arraySize = options.Length;
        for (int i = 0; i < options.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = options[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    private static GameObject[] LoadBodies()
    {
        if (!PackPresent) return new GameObject[0];
        System.Collections.Generic.List<GameObject> found = new System.Collections.Generic.List<GameObject>();
        for (int i = 1; i <= 6; i++)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{BodyFolder}/DeadBody ({i}).prefab");
            if (prefab != null) found.Add(prefab);
        }
        return found.ToArray();
    }

    /// <summary>The body's renderer bounds in root's space (it may be inactive, so renderers are read directly).</summary>
    private static Bounds LocalBounds(Transform root, GameObject body)
    {
        bool first = true;
        Bounds result = new Bounds();
        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            Bounds w = r.bounds;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = new Vector3((c & 1) == 0 ? w.min.x : w.max.x, (c & 2) == 0 ? w.min.y : w.max.y, (c & 4) == 0 ? w.min.z : w.max.z);
                Vector3 local = root.InverseTransformPoint(corner);
                if (first) { result = new Bounds(local, Vector3.zero); first = false; }
                else result.Encapsulate(local);
            }
        }
        return result;
    }
}

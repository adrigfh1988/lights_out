using UnityEditor;
using UnityEngine;

/// <summary>
/// Pivot-agnostic helpers for the Alchemist House pack (Assets/BK_AlchemistHouse), shared by the F79
/// maze-dressing builders (FloorThemeBuilder.Alchemist.cs, InteractableKitBuilder.cs). The pack's pivots
/// and root scales are all over the place (see plannings/shop-room-alchemist-plan.md), so nothing is ever
/// positioned by transform.position alone: every Place* call measures the renderer bounds after rotating
/// and shifts to land them where asked. This is ShopRoomBuilder.Dressing's logic, copied rather than shared
/// (ShopRoomBuilder is left alone on purpose), with two differences: the root a piece is spawned under is
/// expected to sit at the world origin with identity rotation (so world bounds == root-local bounds), and
/// Strip can also remove lights and particles, because dressing prefabs carry neither.
/// </summary>
internal static class AlchemistPack
{
    internal const string Prefabs = "Assets/BK_AlchemistHouse/Prefabs/";

    // ---------------------------------------------------------------- spawning

    /// <summary>
    /// Instantiates Prefabs + path + ".prefab" under parent, unpacks it completely and strips it for
    /// dressing use. Returns null (and logs one error) if the prefab is missing. Never sets localScale;
    /// scaleMul multiplies the prefab's own.
    /// </summary>
    internal static GameObject Spawn(Transform parent, string path, float scaleMul = 1f)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + path + ".prefab");
        if (prefab == null)
        {
            Debug.LogError($"AlchemistPack: pack prefab '{path}' not found under {Prefabs}.");
            return null;
        }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = go.transform.localScale * scaleMul;
        Strip(go, true);
        FixDefaultMaterials(go);
        return go;
    }

    private static Material _stickerFallback;

    /// <summary>
    /// A few pack bottles (Bottle01/02/04) leave their label submesh on Unity's Default-Material, which is
    /// Standard-shaded and renders magenta under URP. The pack assets are left alone; this instance gets the
    /// pack's own URP BottleSticker02 label instead.
    /// </summary>
    internal static void FixDefaultMaterials(GameObject go)
    {
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && mats[i].name != "Default-Material") continue;
                if (_stickerFallback == null)
                {
                    foreach (string guid in AssetDatabase.FindAssets("BottleSticker02 t:Material", new[] { "Assets/BK_AlchemistHouse" }))
                    {
                        _stickerFallback = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                        if (_stickerFallback != null) break;
                    }
                }
                if (_stickerFallback == null) continue;
                mats[i] = _stickerFallback;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    /// <summary>
    /// Joints, then Rigidbodies, then every Collider; components named "VolumetricLight" (a Built-in-only
    /// pack script); missing-script slots; and, when dressing is true, every Light, ParticleSystemRenderer
    /// and ParticleSystem. Anything with a Rigidbody would fall, and anything with a non-convex
    /// MeshCollider is a runtime error, so a prefab that is going into a maze keeps none of them.
    /// </summary>
    internal static void Strip(GameObject go, bool dressing)
    {
        foreach (Component c in go.GetComponentsInChildren<Component>(true))
        {
            if (c != null && c.GetType().Name == "VolumetricLight") Object.DestroyImmediate(c);
        }
        foreach (Joint joint in go.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(joint);
        foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
        foreach (Collider col in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

        if (!dressing) return;

        foreach (ParticleSystemRenderer r in go.GetComponentsInChildren<ParticleSystemRenderer>(true)) Object.DestroyImmediate(r);
        foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true)) Object.DestroyImmediate(ps);
        foreach (Light l in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
    }

    // ---------------------------------------------------------------- bounds

    /// <summary>MeshRenderer + SkinnedMeshRenderer bounds only (particle renderers report junk in edit mode).</summary>
    internal static Bounds RenderBounds(GameObject go)
    {
        bool any = false;
        Bounds b = new Bounds(go.transform.position, Vector3.zero);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    // ---------------------------------------------------------------- placement

    /// <summary>
    /// Spawn, rotate, then translate so the bounds' bottom-centre sits at localBottomCentre in parent's
    /// space. parent must be at the world origin with identity rotation and scale.
    /// </summary>
    internal static GameObject Place(Transform parent, string path, Vector3 localBottomCentre, Quaternion rotation, float scaleMul = 1f)
    {
        GameObject go = Spawn(parent, path, scaleMul);
        if (go == null) return null;
        go.transform.localRotation = rotation * go.transform.localRotation;

        Bounds b = RenderBounds(go);
        go.transform.position += parent.TransformPoint(localBottomCentre) - new Vector3(b.center.x, b.min.y, b.center.z);
        return go;
    }

    /// <summary>Same, but aligning the bounds' centre.</summary>
    internal static GameObject PlaceCentred(Transform parent, string path, Vector3 localCentre, Quaternion rotation, float scaleMul = 1f)
    {
        GameObject go = Spawn(parent, path, scaleMul);
        if (go == null) return null;
        go.transform.localRotation = rotation * go.transform.localRotation;

        Bounds b = RenderBounds(go);
        go.transform.position += parent.TransformPoint(localCentre) - b.center;
        return go;
    }

    /// <summary>Rotates an already-placed piece about its own bounds centre (world axes), keeping the centre fixed.</summary>
    internal static void RotateAboutCentre(GameObject go, Quaternion rotation)
    {
        Vector3 centre = RenderBounds(go).center;
        go.transform.position = centre + rotation * (go.transform.position - centre);
        go.transform.rotation = rotation * go.transform.rotation;
    }
}

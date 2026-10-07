using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared Built-in to URP material copies for asset packs that ship Standard-shader materials (Socket Pack,
/// and Gabies' keys): the pack's own materials are never touched - each source becomes a URP Lit copy under
/// Assets/SourceFiles/Materials/Interactables/Pack_&lt;name&gt;.mat (loaded, never overwritten, once it exists).
/// Used by InteractableKitBuilder (fuse box, key) and the F85 pack dressing (air vents, sockets, fuse boxes).
/// </summary>
internal static class PackMaterials
{
    internal const string MaterialFolder = "Assets/SourceFiles/Materials/Interactables";

    /// <summary>
    /// A URP Lit material carrying `source`'s base colour/texture, normal map and metallic map. Materials
    /// already on a URP shader are returned as they are.
    /// </summary>
    internal static Material UrpCopyOf(Material source)
    {
        if (source == null) return null;
        if (source.shader != null && source.shader.name.StartsWith("Universal Render Pipeline/")) return source;

        string safeName = source.name.Replace(' ', '_');
        string path = $"{MaterialFolder}/Pack_{safeName}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) return source;

        EnsureFolder(MaterialFolder);
        Material material = new Material(shader) { name = $"Pack_{safeName}" };

        if (source.HasProperty("_MainTex") && source.GetTexture("_MainTex") != null)
        {
            material.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
            material.SetTextureScale("_BaseMap", source.GetTextureScale("_MainTex"));
            material.SetTextureOffset("_BaseMap", source.GetTextureOffset("_MainTex"));
        }
        material.SetColor("_BaseColor", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);

        if (source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null)
        {
            material.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
            material.EnableKeyword("_NORMALMAP");
        }

        if (source.HasProperty("_MetallicGlossMap") && source.GetTexture("_MetallicGlossMap") != null)
        {
            // Both shaders read metallic from R and smoothness from A of this map; _Smoothness scales A.
            material.SetTexture("_MetallicGlossMap", source.GetTexture("_MetallicGlossMap"));
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Smoothness", source.HasProperty("_GlossMapScale") ? source.GetFloat("_GlossMapScale") : 1f);
        }
        else
        {
            material.SetFloat("_Metallic", source.HasProperty("_Metallic") ? source.GetFloat("_Metallic") : 0f);
            material.SetFloat("_Smoothness", source.HasProperty("_Glossiness") ? source.GetFloat("_Glossiness") : 0.5f);
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>Swaps every non-URP material under root for its UrpCopyOf. Returns true if anything changed.</summary>
    internal static bool ConvertHierarchy(GameObject root)
    {
        bool any = false;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material converted = UrpCopyOf(materials[i]);
                if (converted == materials[i]) continue;
                materials[i] = converted;
                changed = true;
            }
            if (changed) { renderer.sharedMaterials = materials; any = true; }
        }
        return any;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}

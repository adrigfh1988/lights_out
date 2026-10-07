using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The Alchemist House pack ships on Built-in shaders (Standard, a GrabPass refraction shader, legacy
/// particles), which render magenta or dead in URP. This converts them in place, once. Only materials under
/// Assets/BK_AlchemistHouse are ever touched, and anything already on a URP shader is skipped, so running it
/// again changes nothing. No menu item of its own any more: every builder that uses the pack (Shop Room,
/// Floor Themes' alchemist dressing, the re-skinned templates) calls Convert() when NeedsConversion().
///
/// The pack is third-party and untracked: re-importing the package is the undo.
/// </summary>
public static class AlchemistMaterialUpgrade
{
    public const string PackFolder = "Assets/BK_AlchemistHouse";

    private const string LitShader = "Universal Render Pipeline/Lit";
    private const string ParticleShader = "Universal Render Pipeline/Particles/Unlit";

    // The torch is live in the shop and a glossy surface flares straight back down the beam.
    private const float MaxSmoothness = 0.35f;

    /// <summary>True if any material under the pack is still on a non-URP shader.</summary>
    public static bool NeedsConversion()
    {
        foreach (Material material in PackMaterials())
        {
            if (material.shader == null || !material.shader.name.StartsWith("Universal Render Pipeline/")) return true;
        }
        return false;
    }

    /// <summary>Idempotent. Returns how many materials it changed.</summary>
    public static int Convert()
    {
        int converted = 0;
        int skipped = 0;
        StandardUpgrader standard = new StandardUpgrader("Standard");

        foreach (Material material in PackMaterials())
        {
            string shaderName = material.shader != null ? material.shader.name : "";

            if (shaderName.StartsWith("Universal Render Pipeline/"))
            {
                skipped++;
                continue;
            }

            switch (shaderName)
            {
                case "Standard":
                    standard.Upgrade(material, MaterialUpgrader.UpgradeFlags.None);
                    if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", Mathf.Min(material.GetFloat("_Smoothness"), MaxSmoothness));
                    break;

                case "Custom/Refraction":
                    ConvertGlass(material);
                    break;

                case "Legacy Shaders/Particles/Additive (Soft)":
                    ConvertParticle(material, additive: true);
                    break;

                case "Legacy Shaders/Particles/Alpha Blended":
                    ConvertParticle(material, additive: false);
                    break;

                default:
                    Debug.LogWarning($"AlchemistMaterialUpgrade: no conversion for shader '{shaderName}' on {material.name}, skipped.", material);
                    skipped++;
                    continue;
            }

            EditorUtility.SetDirty(material);
            converted++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Alchemist House materials: converted {converted}, skipped {skipped}.");
        return converted;
    }

    // ---------------------------------------------------------------- pieces

    private static void ConvertGlass(Material material)
    {
        // Read before the shader swap; the new shader has different property names.
        Texture bump = material.HasProperty("_BumpMap") ? material.GetTexture("_BumpMap") : null;
        Color tint = material.name == "LiquidJar" ? new Color(0.35f, 0.6f, 0.3f, 0.55f) : new Color(0.75f, 0.85f, 0.82f, 0.22f);

        material.shader = Shader.Find(LitShader);
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Smoothness", MaxSmoothness);
        if (bump != null)
        {
            material.SetTexture("_BumpMap", bump);
            material.EnableKeyword("_NORMALMAP");
        }
        MakeTransparent(material, additive: false);
    }

    private static void ConvertParticle(Material material, bool additive)
    {
        Texture main = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
        Color tint = material.HasProperty("_TintColor") ? material.GetColor("_TintColor") : Color.white;

        material.shader = Shader.Find(ParticleShader);
        if (main != null) material.SetTexture("_BaseMap", main);
        material.SetColor("_BaseColor", tint);
        MakeTransparent(material, additive);
    }

    /// <summary>The same property set the URP material inspector writes for a transparent surface.</summary>
    private static void MakeTransparent(Material material, bool additive)
    {
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", additive ? 2f : 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        material.SetFloat("_ZWrite", 0f);
        material.renderQueue = (int)RenderQueue.Transparent;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
    }

    private static System.Collections.Generic.IEnumerable<Material> PackMaterials()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { PackFolder }))
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (material != null) yield return material;
        }
    }
}

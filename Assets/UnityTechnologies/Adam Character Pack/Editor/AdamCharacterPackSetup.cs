using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityTechnologies.AdamCharacterPack.EditorTools
{
    /// <summary>
    /// A .unitypackage can only contain files under Assets/, so the pack cannot ship the
    /// ProjectSettings entry that points Unity at its render pipeline asset. Without it the
    /// scenes render with whatever pipeline asset the host project already had, and the
    /// lighting comes out blown out.
    ///
    /// This offers to make that one assignment. Everything else the pack needs (the volume
    /// profile, post-processing on the cameras, baked lighting) already travels inside the scenes.
    /// </summary>
    static class AdamCharacterPackSetup
    {
        const string k_PipelineGuid = "edd00f45e8af34461adbdd4e98184bac"; // URP-Pipeline.asset
        const string k_MenuPath = "Tools/Adam Character Pack/Setup Render Pipeline";
        const string k_DeclinedKey = "AdamCharacterPack.SetupDeclined";
        const string k_AskedThisSessionKey = "AdamCharacterPack.SetupAsked";

        [MenuItem(k_MenuPath, false, 0)]
        static void SetupFromMenu()
        {
            var pipeline = FindPipeline();
            if (pipeline == null)
            {
                EditorUtility.DisplayDialog("Adam Character Pack",
                    "Could not find URP-Pipeline.asset. Re-import the package and make sure the " +
                    "Settings folder is included.", "OK");
                return;
            }

            if (IsConfigured(pipeline))
            {
                EditorUtility.DisplayDialog("Adam Character Pack",
                    "This project is already using the pack's render pipeline asset.", "OK");
                return;
            }

            Apply(pipeline);
            EditorUtility.DisplayDialog("Adam Character Pack",
                "Done. The pack's scenes should now render as intended.\n\n" +
                "The baked lighting ships with the package — there is no need to rebake it.", "OK");
        }

        /// <summary>
        /// Offers the change on import rather than making it silently: reassigning the render
        /// pipeline affects every scene in the host project, not just this pack's.
        /// </summary>
        [InitializeOnLoadMethod]
        static void OfferOnImport()
        {
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool(k_AskedThisSessionKey, false) ||
                    EditorPrefs.GetBool(k_DeclinedKey, false))
                    return;

                var pipeline = FindPipeline();
                if (pipeline == null || IsConfigured(pipeline))
                    return;

                SessionState.SetBool(k_AskedThisSessionKey, true);

                var choice = EditorUtility.DisplayDialogComplex(
                    "Adam Character Pack",
                    "This project is not using the render pipeline asset the Adam Character Pack " +
                    "was authored with, so its scenes will not look as intended.\n\n" +
                    "Assign it now? This changes the Render Pipeline Asset in Graphics and in every " +
                    "Quality level, which affects all scenes in this project.\n\n" +
                    $"You can do this later from {k_MenuPath}.",
                    "Assign it", "Not now", "Don't ask again");

                switch (choice)
                {
                    case 0:
                        Apply(pipeline);
                        break;
                    case 2:
                        EditorPrefs.SetBool(k_DeclinedKey, true);
                        break;
                }
            };
        }

        static RenderPipelineAsset FindPipeline()
        {
            var path = AssetDatabase.GUIDToAssetPath(k_PipelineGuid);
            return string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path);
        }

        static bool IsConfigured(RenderPipelineAsset pipeline)
        {
            if (GraphicsSettings.defaultRenderPipeline != pipeline)
                return false;

            // A Quality level that overrides the pipeline wins over the Graphics one, so a project
            // can look correct until the user switches quality. Treat that as not configured.
            var current = QualitySettings.GetQualityLevel();
            try
            {
                for (var i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    var level = QualitySettings.renderPipeline;
                    if (level != null && level != pipeline)
                        return false;
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(current, false);
            }

            return true;
        }

        static void Apply(RenderPipelineAsset pipeline)
        {
            GraphicsSettings.defaultRenderPipeline = pipeline;

            var current = QualitySettings.GetQualityLevel();
            var levels = QualitySettings.names.Length;
            try
            {
                for (var i = 0; i < levels; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    QualitySettings.renderPipeline = pipeline;
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(current, false);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Adam Character Pack] Render pipeline asset assigned in Graphics and in " +
                      $"{levels} quality level(s).");
        }
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The F71 darkness pass, run automatically at the end of LIGHTS OUT &gt; Build &gt; Floor Themes (it used to
/// be its own menu item, which was easy to forget after a rebuild). F71: FloorProfile's ambient numbers dropped a lot
/// (0.10/0.035 -&gt; 0.015/0.006, see plannings/darkness-and-interactivity-spec.md decision D2), but a
/// themed floor never uses FloorProfile.Ambient at all - MazeGenerator.ResolveTheme takes _theme.Ambient
/// instead (CLAUDE.md, "Verified facts") - so lowering the profile alone does nothing on floors 1-5; only
/// the kit maze and the primitive fallback would have gotten darker. FloorTheme's own ambient (authored
/// by LIGHTS OUT &gt; Build &gt; Floor Themes) is up to 0.12 max channel (the Crypt row) and needs relighting to
/// match. This scales every FloorTheme's ambient colour down so its brightest channel is
/// TargetMaxChannel, keeping the tint (a warm row stays warmer than a cool one) but bringing every row
/// into the new range.
///
/// Idempotent: a row already at or under TargetMaxChannel is left alone, so running this twice - or
/// running it after Build Floor Themes has been re-run with "Keep existing assets" - changes nothing on
/// the second pass. Edits go through SerializedObject so Undo/dirtying work the normal Editor way; no
/// runtime API on FloorTheme is needed.
/// </summary>
public static class DarknessRelight
{
    /// <summary>The brightest of ambient.r/g/b after relighting. See slice F71 step 2.</summary>
    private const float TargetMaxChannel = 0.012f;

    // A row already this close to the target is left alone - without this, floating-point rounding in
    // the scale-down could nudge an already-relit row by a fraction of a stop every time the menu item
    // runs, which would defeat "idempotent".
    private const float Tolerance = 0.0001f;

    /// <summary>True if any FloorTheme row in the open scene is brighter than the darkness pass allows. Read by ProjectSetup's check.</summary>
    public static bool NeedsRelight()
    {
        foreach (FloorTheme theme in Object.FindObjectsByType<FloorTheme>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SerializedProperty ambientProp = new SerializedObject(theme).FindProperty("ambient");
            if (ambientProp == null) continue;
            Color c = ambientProp.colorValue;
            if (Mathf.Max(c.r, c.g, c.b) > TargetMaxChannel + Tolerance) return true;
        }
        return false;
    }

    /// <summary>No dialogs. Relights every FloorTheme row in the open scene; does nothing if there are none.</summary>
    public static void Relight()
    {
        Scene scene = SceneManager.GetActiveScene();
        FloorTheme[] themes = Object.FindObjectsByType<FloorTheme>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (!scene.IsValid() || !scene.isLoaded || themes.Length == 0) return;

        bool anyChanged = false;
        foreach (FloorTheme theme in themes)
        {
            if (RelightOne(theme)) anyChanged = true;
        }

        if (anyChanged)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"DarknessRelight: relit {themes.Length} FloorTheme row(s), max ambient channel now <= {TargetMaxChannel:0.###}.");
        }
        else
        {
            Debug.Log("DarknessRelight: nothing to do - every FloorTheme row is already at or under the target.");
        }
    }

    /// <summary>Scales one row's ambient down to TargetMaxChannel if it is brighter. Returns whether it changed anything.</summary>
    private static bool RelightOne(FloorTheme theme)
    {
        SerializedObject serialized = new SerializedObject(theme);
        SerializedProperty ambientProp = serialized.FindProperty("ambient");
        if (ambientProp == null) return false;

        Color oldAmbient = ambientProp.colorValue;
        float max = Mathf.Max(oldAmbient.r, oldAmbient.g, oldAmbient.b);
        if (max <= TargetMaxChannel + Tolerance) return false;

        float scale = TargetMaxChannel / max;
        Color newAmbient = new Color(oldAmbient.r * scale, oldAmbient.g * scale, oldAmbient.b * scale, oldAmbient.a);

        Undo.RecordObject(theme, "Relight Floor Themes (Darkness Pass)");
        ambientProp.colorValue = newAmbient;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(theme);

        Debug.Log($"DarknessRelight: {theme.name} ambient {ColorToString(oldAmbient)} -> {ColorToString(newAmbient)}", theme);
        return true;
    }

    private static string ColorToString(Color c) => $"({c.r:0.###}, {c.g:0.###}, {c.b:0.###})";
}

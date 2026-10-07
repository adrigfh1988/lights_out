using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// LIGHTS OUT &gt; Build Phrase Books (F82). Creates Assets/SourceFiles/Data/StanPhrases.asset and
/// HunterPhrases.asset with the default lines if they are missing - never overwrites an existing one, so
/// the user's edits win - then assigns Stan's to the InteractableKit's Stalker template and puts a
/// HunterVoice (with its book) on the scene's hunter, both through SerializedObject so the tunables are
/// editable in the Inspector. InteractableKitBuilder calls <see cref="AssignTo"/> too, so a full kit
/// rebuild keeps Stan's link. Without the links both still talk (each voice falls back to its built-in
/// defaults and logs one warning), the lines just can't be edited.
/// </summary>
public static class StanPhraseBookBuilder
{
    public const string AssetFolder = "Assets/SourceFiles/Data";
    public const string AssetPath = AssetFolder + "/StanPhrases.asset";
    public const string HunterAssetPath = AssetFolder + "/HunterPhrases.asset";

    [MenuItem("LIGHTS OUT/Build Phrase Books (Stan + Hunter)")]
    public static void Build()
    {
        bool stanCreated = LoadOrCreate(AssetPath, out StalkerPhraseBook stanBook, b => b.FillDefaults());
        bool hunterCreated = LoadOrCreate(HunterAssetPath, out HunterPhraseBook hunterBook, b => b.FillDefaults());

        string report =
            $"Stan: {AssetPath} {(stanCreated ? "created" : "already existed (left untouched)")}.\n" +
            $"Hunter: {HunterAssetPath} {(hunterCreated ? "created" : "already existed (left untouched)")}.\n\n";

        // Stan: the InteractableKit's Stalker template.
        Stalker template = null;
        foreach (InteractableKit kit in Object.FindObjectsByType<InteractableKit>(FindObjectsInactive.Include))
        {
            if (kit != null && kit.Stalker != null)
            {
                template = kit.Stalker;
                break;
            }
        }
        if (template != null)
        {
            AssignTo(template);
            EditorSceneManager.MarkSceneDirty(template.gameObject.scene);
            report += "Stan's book is assigned to the Stalker template.\n";
        }
        else
        {
            report += "No Stalker template in the open scene - run LIGHTS OUT > Build Interactables, then this again.\n";
        }

        // Hunter: the scene's AIFollower gets a HunterVoice (added if missing) with the book assigned.
        AIFollower hunter = Object.FindFirstObjectByType<AIFollower>(FindObjectsInactive.Include);
        if (hunter != null)
        {
            HunterVoice voice = hunter.GetComponent<HunterVoice>();
            if (voice == null) voice = Undo.AddComponent<HunterVoice>(hunter.gameObject);
            SerializedObject so = new SerializedObject(voice);
            SerializedProperty property = so.FindProperty("phraseBook");
            if (property != null)
            {
                property.objectReferenceValue = hunterBook;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(hunter.gameObject.scene);
            report += $"The hunter ({hunter.name}) has a HunterVoice with its book assigned.\n";
        }
        else
        {
            report += "No hunter (AIFollower) in the open scene - its voice will use the built-in lines.\n";
        }

        EditorUtility.DisplayDialog("Build Phrase Books", report + "\nSave the scene.", "OK");
    }

    /// <summary>Assigns Stan's phrase book asset (if it exists) to a Stalker's serialized <c>phraseBook</c> field.</summary>
    public static void AssignTo(Stalker stalker)
    {
        if (stalker == null) return;
        StalkerPhraseBook book = AssetDatabase.LoadAssetAtPath<StalkerPhraseBook>(AssetPath);
        if (book == null) return;

        SerializedObject so = new SerializedObject(stalker);
        SerializedProperty property = so.FindProperty("phraseBook");
        if (property == null) return;
        property.objectReferenceValue = book;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Loads the asset at `path`, or creates it (filled by `fill`) if missing. True if created.</summary>
    private static bool LoadOrCreate<T>(string path, out T asset, System.Action<T> fill) where T : ScriptableObject
    {
        asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return false;

        if (!Directory.Exists(AssetFolder))
        {
            Directory.CreateDirectory(AssetFolder);
            AssetDatabase.Refresh();
        }
        asset = ScriptableObject.CreateInstance<T>();
        fill(asset);
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        return true;
    }
}

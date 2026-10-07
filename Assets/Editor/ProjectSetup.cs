using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The two top entries of the LIGHTS OUT menu, so nobody has to remember which builder was run when.
///
/// LIGHTS OUT &gt; Check Project reports, piece by piece, whether everything the game clones or reads out of the
/// scene is there and up to date, naming the menu item that fixes each gap. It changes nothing.
///
/// LIGHTS OUT &gt; Build Missing Pieces runs only the builders whose piece is missing, in dependency order
/// (Interactables before Phrase Books, which assigns Stan's book to the Stalker template). Pieces that already
/// exist are never rebuilt - rebuilding one on purpose is LIGHTS OUT &gt; Build &gt; that piece.
///
/// Every new builder adds a Piece here (see CLAUDE.md, "Editor menu").
/// </summary>
public static class ProjectSetup
{
    private sealed class Piece
    {
        public string Name;
        public string FixMenu;
        /// <summary>Null = present and up to date; otherwise what is wrong.</summary>
        public System.Func<string> Problem;
        /// <summary>Run by Build Missing Pieces when Problem is not null. Null = report only.</summary>
        public System.Action Build;
    }

    private static List<Piece> Pieces() => new List<Piece>
    {
        new Piece
        {
            Name = "Alchemist House materials (URP)",
            FixMenu = "converted automatically by Build > Shop Room / Floor Themes",
            Problem = () => AlchemistMaterialUpgrade.NeedsConversion() ? "still on Built-in shaders (render magenta)" : null,
            Build = () => AlchemistMaterialUpgrade.Convert()
        },
        new Piece
        {
            Name = "Floor themes gallery",
            FixMenu = "Build > Floor Themes",
            Problem = () =>
            {
                FloorThemeSet set = Object.FindAnyObjectByType<FloorThemeSet>(FindObjectsInactive.Include);
                if (set == null) return "missing";
                if (!set.IsComplete) return "a row is incomplete";
                return DarknessRelight.NeedsRelight() ? "a row is brighter than the F71 darkness pass" : null;
            },
            Build = () =>
            {
                if (Object.FindAnyObjectByType<FloorThemeSet>(FindObjectsInactive.Include) == null) FloorThemeBuilder.BuildSilently();
                else DarknessRelight.Relight(); // present but too bright: the only gap fixable without a rebuild
            }
        },
        new Piece
        {
            Name = "Kit maze theme",
            FixMenu = "Build > Kit Maze Theme",
            Problem = () =>
            {
                KitMazeTheme kit = Object.FindAnyObjectByType<KitMazeTheme>(FindObjectsInactive.Include);
                if (kit == null) return "missing";
                return kit.IsComplete ? null : "incomplete";
            },
            Build = () =>
            {
                if (Object.FindAnyObjectByType<KitMazeTheme>(FindObjectsInactive.Include) == null) KitMazeThemeBuilder.Build();
            }
        },
        new Piece
        {
            Name = "Interactables kit",
            FixMenu = "Build > Interactables (builds only the empty slots)",
            Problem = () =>
            {
                InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
                if (kit == null) return "missing";
                List<string> empty = EmptySlots(kit);
                return empty.Count == 0 ? null : "empty slot(s): " + string.Join(", ", empty);
            },
            Build = InteractableKitBuilder.Build
        },
        new Piece
        {
            Name = "Phrase books (Stan + hunter)",
            FixMenu = "Build > Phrase Books (Stan + Hunter)",
            Problem = PhraseBookProblem,
            Build = StanPhraseBookBuilder.Build
        },
        new Piece
        {
            Name = "Shop room",
            FixMenu = "Build > Shop Room",
            Problem = () =>
            {
                ShopRoom shop = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
                if (shop == null) return "missing (floors 1-4 fall back to a plain win)";
                SerializedProperty head = new SerializedObject(shop).FindProperty("headPivot");
                return head != null && head.objectReferenceValue == null ? "the salesman has no headPivot" : null;
            },
            Build = () =>
            {
                if (Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include) == null) ShopRoomBuilder.BuildSilently();
            }
        },
        new Piece
        {
            Name = "Hunter body (Adam)",
            FixMenu = "Build > Hunter Body",
            Problem = () =>
            {
                AIFollower hunter = Object.FindAnyObjectByType<AIFollower>(FindObjectsInactive.Include);
                if (hunter == null) return "no AIFollower in the scene";
                return hunter.transform.Find(HunterBodyBuilder.BodyChildName) == null ? "still the placeholder robot" : null;
            },
            Build = HunterBodyBuilder.Build
        },
        new Piece
        {
            Name = "Tutorial prefabs",
            FixMenu = "delete their scene instances, then the prefabs (report only)",
            Problem = () =>
            {
                List<string> left = new List<string>();
                foreach (string name in new[] { "Moving_Platform", "Stairs", "Wall_Light_Left", "Wall_Light_Right" })
                {
                    if (AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/{name}.prefab") != null) left.Add(name);
                }
                return left.Count == 0 ? null : "still in Assets/Prefabs: " + string.Join(", ", left);
            },
            Build = null
        },
    };

    [MenuItem("LIGHTS OUT/Check Project", priority = 0)]
    public static void Check()
    {
        if (!SceneReady("Check Project")) return;

        StringBuilder report = new StringBuilder();
        int problems = 0;
        foreach (Piece piece in Pieces())
        {
            string problem = piece.Problem();
            if (problem == null)
            {
                report.AppendLine($"OK    {piece.Name}");
            }
            else
            {
                problems++;
                report.AppendLine($"FIX   {piece.Name}: {problem}\n        -> LIGHTS OUT > {piece.FixMenu}");
            }
        }

        string header = problems == 0 ? "Everything is built and up to date.\n\n" : $"{problems} thing(s) need attention.\n\n";
        Debug.Log("LIGHTS OUT > Check Project\n" + header + report);
        EditorUtility.DisplayDialog("Check Project", header + report, "OK");
    }

    [MenuItem("LIGHTS OUT/Build Missing Pieces", priority = 1)]
    public static void BuildMissing()
    {
        if (!SceneReady("Build Missing Pieces")) return;

        List<string> ran = new List<string>();
        List<string> manual = new List<string>();
        foreach (Piece piece in Pieces())
        {
            if (piece.Problem() == null) continue;
            if (piece.Build == null)
            {
                manual.Add(piece.Name);
                continue;
            }

            piece.Build();
            ran.Add(piece.Name);
        }

        if (ran.Count > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        string message = ran.Count == 0 ? "Nothing was missing." : "Built / fixed:\n  " + string.Join("\n  ", ran) + "\n\nSave the scene (Ctrl+S).";
        if (manual.Count > 0) message += "\n\nNeeds a hand (see Check Project):\n  " + string.Join("\n  ", manual);
        message += "\n\nRun Check Project to confirm - an incomplete piece that already exists is never rebuilt automatically.";
        EditorUtility.DisplayDialog("Build Missing Pieces", message, "OK");
    }

    // ---------------------------------------------------------------- helpers

    private static bool SceneReady(string title)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid() && scene.isLoaded) return true;
        EditorUtility.DisplayDialog(title, "Open the game scene first.", "OK");
        return false;
    }

    /// <summary>Every object-reference slot on the kit that is empty. escapeHatch is optional (an empty slot is the old glowing slab).</summary>
    private static List<string> EmptySlots(InteractableKit kit)
    {
        List<string> empty = new List<string>();
        SerializedProperty it = new SerializedObject(kit).GetIterator();
        bool enterChildren = true;
        while (it.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (it.propertyType != SerializedPropertyType.ObjectReference || it.name == "m_Script" || it.name == "escapeHatch") continue;
            if (it.objectReferenceValue == null) empty.Add(it.name);
        }
        return empty;
    }

    private static string PhraseBookProblem()
    {
        if (AssetDatabase.LoadAssetAtPath<StalkerPhraseBook>(StanPhraseBookBuilder.AssetPath) == null ||
            AssetDatabase.LoadAssetAtPath<HunterPhraseBook>(StanPhraseBookBuilder.HunterAssetPath) == null)
        {
            return "asset missing (in-memory defaults are used)";
        }

        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (kit != null && kit.Stalker != null && !HasBook(kit.Stalker)) return "Stan's template has no book assigned";

        AIFollower hunter = Object.FindAnyObjectByType<AIFollower>(FindObjectsInactive.Include);
        if (hunter != null)
        {
            HunterVoice voice = hunter.GetComponent<HunterVoice>();
            if (voice == null || !HasBook(voice)) return "the hunter has no HunterVoice / book";
        }
        return null;
    }

    private static bool HasBook(Object component)
    {
        SerializedProperty book = new SerializedObject(component).FindProperty("phraseBook");
        return book == null || book.objectReferenceValue != null;
    }
}

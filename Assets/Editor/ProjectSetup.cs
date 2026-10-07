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
            Name = "Blood decals (Blood decal pack)",
            FixMenu = "Build Missing Pieces (or any Build > Floor Themes / Kit Maze Theme run)",
            Problem = () => BloodDecalUpgrade.NeedsUpgrade() ? "some blood decals still use the code-drawn placeholder textures" : null,
            Build = () => BloodDecalUpgrade.Apply()
        },
        new Piece
        {
            Name = "Set-piece decals",
            FixMenu = "Build Missing Pieces (or any Build > Floor Themes / Kit Maze Theme run)",
            Problem = () => SetPieceDecalFix.NeedsFix() ? "set-piece wall decals float 2 m off their wall, or blood quads are mis-shaped" : null,
            Build = () => SetPieceDecalFix.Apply()
        },
        new Piece
        {
            Name = "Corpses (DeadBody LITE)",
            FixMenu = "Build Missing Pieces (or any Build > Floor Themes / Kit Maze Theme run)",
            Problem = () => CorpseUpgrade.NeedsUpgrade() ? "the FallenRunner set piece still has the primitive capsule body" : null,
            Build = () => CorpseUpgrade.Apply()
        },
        new Piece
        {
            Name = "Theme surface textures (F88)",
            FixMenu = "Build Missing Pieces (or any Build > Floor Themes run)",
            Problem = () => ThemeSurfaceUpgrade.Problems(),
            Build = () => ThemeSurfaceUpgrade.Apply()
        },
        new Piece
        {
            Name = "Pack dressing (beds, baths, bodies, furniture, vents)",
            FixMenu = "Build Missing Pieces (or any Build > Floor Themes / Kit Maze Theme run)",
            Problem = () => PackDressing.NeedsUpgrade() ? "a gallery row or the kit maze lacks pieces from the DeadBody LITE / Socket Pack / Alchemist furniture set" : null,
            Build = () => PackDressing.Apply()
        },
        new Piece
        {
            Name = "Primitive dressing cleanup (F89)",
            FixMenu = "Build Missing Pieces (or any Build > Floor Themes / Kit Maze Theme run)",
            Problem = () => PrimitiveDressingCleanup.NeedsUpgrade() ? "untextured primitive props / set pieces (or their prefabs) are still in a gallery row or the kit maze" : null,
            Build = () => PrimitiveDressingCleanup.Apply()
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
            Name = "Last untextured pieces (F90)",
            FixMenu = "Build Missing Pieces (or Build > Floor Themes)",
            Problem = () =>
            {
                List<string> bad = RemainingPrimitivesCleanup.Problems();
                return bad.Count == 0 ? null : string.Join("; ", bad);
            },
            Build = () => RemainingPrimitivesCleanup.Apply()
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
            Name = "Shop door hinge",
            FixMenu = "Build Missing Pieces (adds the hinge, clears the leaf's Static flag)",
            Problem = () =>
            {
                ShopRoom shop = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
                if (shop == null) return null; // reported by "Shop room"
                SerializedProperty hinge = new SerializedObject(shop).FindProperty("doorHinge");
                Transform pivot = hinge != null ? hinge.objectReferenceValue as Transform : null;
                if (pivot == null) return FindShopDoorLeaf(shop) != null ? "no doorHinge wired" : null;
                if (IsStaticAnywhere(pivot)) return "the door leaf is Static - static batching freezes it shut";
                return FindFusedDoorModel(shop) != null ? "the pack's fused Door model (frame + closed panel) hides the swinging leaf" : null;
            },
            Build = FixShopDoorHinge
        },
        new Piece
        {
            Name = "Shop contract board (F87)",
            FixMenu = "Build Missing Pieces (adds the board to the existing shop; a full Build > Shop Room builds it too)",
            Problem = ShopRoomBuilder.ContractBoardProblem,
            Build = () => ShopRoomBuilder.AddContractBoard()
        },
        new Piece
        {
            Name = "Intake room (F86 tutorial)",
            FixMenu = "Build > Intake Room",
            Problem = IntakeRoomBuilder.Problem,
            Build = () =>
            {
                if (Object.FindAnyObjectByType<IntakeRoom>(FindObjectsInactive.Include) == null) IntakeRoomBuilder.BuildSilently();
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

    /// <summary>The shop's closed door leaf (ShopRoom/Door/WoodDoor01...), whether or not it already sits under a hinge.</summary>
    private static Transform FindShopDoorLeaf(ShopRoom shop)
    {
        Transform door = shop.transform.Find("Door");
        if (door == null) return null;
        foreach (Transform t in door.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("WoodDoor01")) return t;
        }
        return null;
    }

    private static bool IsStaticAnywhere(Transform root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0) return true;
        }
        return false;
    }

    /// <summary>Gives an already-built shop room what Build &gt; Shop Room now makes: a hinge pivot on the leaf, wired into ShopRoom, nothing under it Static.</summary>
    private static void FixShopDoorHinge()
    {
        ShopRoom shop = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
        if (shop == null) return;

        SerializedObject so = new SerializedObject(shop);
        SerializedProperty hingeProp = so.FindProperty("doorHinge");
        Transform hinge = hingeProp.objectReferenceValue as Transform;

        if (hinge == null)
        {
            Transform leaf = FindShopDoorLeaf(shop);
            if (leaf == null) return;

            hinge = leaf.parent != null && leaf.parent.name == "DoorHinge" ? leaf.parent : null;
            if (hinge == null)
            {
                Undo.RegisterFullObjectHierarchyUndo(leaf.parent.gameObject, "Hinge Shop Door");
                hinge = ShopRoom.CreateDoorHinge(shop.transform, leaf);
                Undo.RegisterCreatedObjectUndo(hinge.gameObject, "Hinge Shop Door");
            }

            hingeProp.objectReferenceValue = hinge;
            so.ApplyModifiedProperties();
        }

        foreach (Transform t in hinge.GetComponentsInChildren<Transform>(true)) Undo.RecordObject(t.gameObject, "Hinge Shop Door");
        ShopRoomBuilder.MakeMovable(hinge);

        GameObject fused = FindFusedDoorModel(shop);
        if (fused != null) Undo.DestroyObjectImmediate(fused);

        EditorSceneManager.MarkSceneDirty(shop.gameObject.scene);
    }

    /// <summary>
    /// The pack's Architecture/Door model under ShopRoom/Door: one mesh with the frame and a closed panel fused
    /// together. Older Build &gt; Shop Room runs placed it over the leaf, so the leaf swung open unseen behind it.
    /// </summary>
    private static GameObject FindFusedDoorModel(ShopRoom shop)
    {
        Transform group = shop.transform.Find("Door");
        if (group == null) return null;
        foreach (Transform child in group)
        {
            if (child.name == "Door" && child.GetComponent<Renderer>() != null) return child.gameObject;
        }
        return null;
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

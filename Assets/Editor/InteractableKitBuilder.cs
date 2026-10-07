using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LIGHTS OUT &gt; Build &gt; Interactables. Generates the primitive-built templates every door/button/vault
/// pickup clones at runtime (F72 decision D11) - materials under Assets/SourceFiles/Materials/Interactables
/// (never overwritten if present) and a scene gallery (InteractableKit) holding one live instance of
/// each template, so an artist can inspect and retouch them exactly like FloorThemes. Nothing here runs
/// in a build; at Play time MazeGenerator.BuildDoors just finds what this made and clones it.
///
/// Build-missing-only by default (decision D11): an InteractableKit slot that is already assigned is
/// left completely alone, so a later slice (F73's fuse box, etc.) can add its own template without
/// touching this one. Re-running this menu item is always safe.
/// </summary>
public static class InteractableKitBuilder
{
    private const string MaterialFolder = "Assets/SourceFiles/Materials/Interactables";
    private const string GalleryName = "InteractableKit";
    private const string RobotPrefabPath = "Assets/Prefabs/PlayerRobot.prefab";

    // 30 m+ clear of every existing authored/generated spot (CLAUDE.md "The one thing to understand
    // first" + F70's dressing note): the largest maze footprint (floor 4, 12x8 cells at 5.5 m, so
    // origin (-70,4.98,-70) out to roughly x:[-70,-4] z:[-70,-26]), FloorThemes (-70,5,-6), ShopRoom
    // (-92,5,-45) and KitMazeTheme (-70,5,10). +60 in Z clears all four with margin to spare.
    private static readonly Vector3 GalleryOrigin = new Vector3(-70f, 5f, 60f);
    private const float PieceSpacing = 5f;

    // Asset Store packs imported 4 Oct 2026 for the key and fuse box models ("Simple Low Poly Keys" by
    // 3Digitalis, "Sockets and Fuse Box Pack" by TrueBlue Studios). Both ship Built-in Standard materials,
    // so only their meshes/textures are used - UrpCopyOf rebuilds each material as URP Lit under
    // MaterialFolder. If a pack is missing the builder falls back to the original primitive model.
    private const string KeyModelPrefabPath = "Assets/Gabies_Assets/Keys/Prefabs/Simple_02.prefab";
    private const string FuseBoxModelPrefabPath = "Assets/Socket Pack/Prefabs/Fuse_Box_01.prefab";
    /// <summary>The pack key is life-size (13 cm); scaled up so it reads from down a dark corridor.</summary>
    private const float KeyModelScale = 1.6f;
    /// <summary>The pack fuse box is a 24 x 16 cm consumer unit; doubled to a 48 x 32 cm wall cabinet.</summary>
    private const float FuseBoxModelScale = 2f;
    /// <summary>The Maze kit's lever panel (Switch.prefab, already URP via the kit's PixPal shader) is the shutter button's model; 0.7 turns its 52 x 70 cm into a 36 x 49 cm wall panel.</summary>
    private const string KitSwitchPrefabPath = "Assets/Maze/Prefabs/Switch.prefab";
    private const string KitSwitchModelPath = "Assets/Maze/Models/Switch.fbx";
    private const float KitSwitchScale = 0.7f;

    [MenuItem("LIGHTS OUT/Build/Interactables", priority = 102)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Build Interactables", "Open the game scene first.", "OK");
            return;
        }

        EnsureFolder();

        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        GameObject root;
        if (kit != null)
        {
            root = kit.gameObject;
        }
        else
        {
            root = new GameObject(GalleryName);
            Undo.RegisterCreatedObjectUndo(root, "Build Interactables");
            root.transform.position = GalleryOrigin;
            kit = root.AddComponent<InteractableKit>();
        }

        SerializedObject so = new SerializedObject(kit);
        SerializedProperty shutterProp = so.FindProperty("shutterDoor");
        SerializedProperty buttonProp = so.FindProperty("wallButton");
        SerializedProperty cellProp = so.FindProperty("batteryCell");
        SerializedProperty fuseProp = so.FindProperty("fuseBox");
        SerializedProperty bottleProp = so.FindProperty("throwableBottle");
        SerializedProperty canProp = so.FindProperty("throwableCan");
        SerializedProperty variantsProp = so.FindProperty("bottleVariants");
        SerializedProperty glassProp = so.FindProperty("glassPatch");
        SerializedProperty puddleProp = so.FindProperty("puddle");
        SerializedProperty lanternProp = so.FindProperty("lantern");
        SerializedProperty noteProp = so.FindProperty("loreNote");
        SerializedProperty bellProp = so.FindProperty("callBell");
        SerializedProperty valveProp = so.FindProperty("steamValve");
        SerializedProperty candlesProp = so.FindProperty("candles");
        SerializedProperty terminalProp = so.FindProperty("terminal");
        SerializedProperty stalkerProp = so.FindProperty("stalker");
        SerializedProperty keyProp = so.FindProperty("keyPickup");

        // Continue the row after whatever is already there: build-missing-only used to restart at x = 0,
        // which stacked every later slice's template on top of the shutter door.
        float x = NextFreeX(root.transform);
        int built = 0;

        if (shutterProp.objectReferenceValue == null)
        {
            MazeDoor door = BuildShutterDoor(root.transform, new Vector3(x, 0f, 0f));
            shutterProp.objectReferenceValue = door;
            x += PieceSpacing;
            built++;
        }
        if (buttonProp.objectReferenceValue == null)
        {
            WallButton button = BuildWallButton(root.transform, new Vector3(x, 1f, 0f));
            buttonProp.objectReferenceValue = button;
            x += PieceSpacing;
            built++;
        }
        if (cellProp.objectReferenceValue == null)
        {
            BatteryCellPickup cell = BuildBatteryCell(root.transform, new Vector3(x, 0.4f, 0f));
            cellProp.objectReferenceValue = cell;
            x += PieceSpacing;
            built++;
        }
        // F73: build-missing-only (decision D11) - adds just this slot to an existing kit without
        // touching the F72 slots above.
        if (fuseProp.objectReferenceValue == null)
        {
            FuseBox box = BuildFuseBox(root.transform, new Vector3(x, 0.5f, 0f));
            fuseProp.objectReferenceValue = box;
            x += PieceSpacing;
            built++;
        }

        // F74: same build-missing-only rule - every slot below only touches an empty slot of its own.
        if (bottleProp.objectReferenceValue == null)
        {
            ThrowableItem bottle = BuildBottle(root.transform, new Vector3(x, 0.15f, 0f));
            bottleProp.objectReferenceValue = bottle;
            x += PieceSpacing;
            built++;
        }
        if (canProp.objectReferenceValue == null)
        {
            ThrowableItem can = BuildCan(root.transform, new Vector3(x, 0.08f, 0f));
            canProp.objectReferenceValue = can;
            x += PieceSpacing;
            built++;
        }
        if (glassProp.objectReferenceValue == null)
        {
            NoisySurface glass = BuildFloorPatch(root.transform, new Vector3(x, 0.01f, 0f), "GlassPatch",
                new Color(0.55f, 0.6f, 0.5f), new Color(0.55f, 0.6f, 0.5f) * 0.8f);
            glassProp.objectReferenceValue = glass;
            x += PieceSpacing;
            built++;
        }
        if (puddleProp.objectReferenceValue == null)
        {
            NoisySurface puddle = BuildFloorPatch(root.transform, new Vector3(x, 0.01f, 0f), "Puddle",
                new Color(0.05f, 0.08f, 0.1f), null);
            puddleProp.objectReferenceValue = puddle;
            x += PieceSpacing;
            built++;
        }
        if (lanternProp.objectReferenceValue == null)
        {
            Lantern lantern = BuildLantern(root.transform, new Vector3(x, 0.2f, 0f));
            lanternProp.objectReferenceValue = lantern;
            x += PieceSpacing;
            built++;
        }
        if (noteProp.objectReferenceValue == null)
        {
            LoreNote note = BuildLoreNote(root.transform, new Vector3(x, 1.2f, 0f));
            noteProp.objectReferenceValue = note;
            x += PieceSpacing;
            built++;
        }
        if (bellProp.objectReferenceValue == null)
        {
            ThemeInteractable bell = BuildCallBell(root.transform, new Vector3(x, 1.6f, 0f));
            bellProp.objectReferenceValue = bell;
            x += PieceSpacing;
            built++;
        }
        if (valveProp.objectReferenceValue == null)
        {
            ThemeInteractable valve = BuildSteamValve(root.transform, new Vector3(x, 1f, 0f));
            valveProp.objectReferenceValue = valve;
            x += PieceSpacing;
            built++;
        }
        if (candlesProp.objectReferenceValue == null)
        {
            ThemeInteractable candles = BuildCandles(root.transform, new Vector3(x, 0.5f, 0f));
            candlesProp.objectReferenceValue = candles;
            x += PieceSpacing;
            built++;
        }
        if (terminalProp.objectReferenceValue == null)
        {
            ThemeInteractable terminal = BuildTerminal(root.transform, new Vector3(x, 0.6f, 0f));
            terminalProp.objectReferenceValue = terminal;
            x += PieceSpacing;
            built++;
        }

        // F75: same build-missing-only rule.
        if (stalkerProp.objectReferenceValue == null)
        {
            Stalker stalker = BuildStalkerTemplate(root.transform, new Vector3(x, 0f, 0f));
            if (stalker != null)
            {
                stalkerProp.objectReferenceValue = stalker;
                x += PieceSpacing;
                built++;
            }
        }

        // F77: same build-missing-only rule, after the stalker block.
        if (keyProp.objectReferenceValue == null)
        {
            KeyPickup key = BuildKeyPickup(root.transform, new Vector3(x, 1.3f, 0f));
            keyProp.objectReferenceValue = key;
            x += PieceSpacing;
            built++;
        }

        // F79: bottle look-alikes (optional - an empty array is valid), same build-missing-only rule.
        if (variantsProp.arraySize == 0)
        {
            List<ThrowableItem> variants = new List<ThrowableItem>();
            foreach (BottleVariant spec in BottleVariants)
            {
                ThrowableItem variant = BuildBottleVariant(root.transform, new Vector3(x, 0.15f, 0f), spec);
                if (variant == null) continue;
                variants.Add(variant);
                x += PieceSpacing;
                built++;
            }
            variantsProp.arraySize = variants.Count;
            for (int i = 0; i < variants.Count; i++) variantsProp.GetArrayElementAtIndex(i).objectReferenceValue = variants[i];
        }

        // F80: the escape hatch's trapdoor, same build-missing-only rule. Optional - without the pack
        // the slot stays empty and MazeEscape keeps its glowing slab.
        SerializedProperty hatchProp = so.FindProperty("escapeHatch");
        if (hatchProp.objectReferenceValue == null)
        {
            GameObject hatch = BuildEscapeHatch(root.transform, new Vector3(x, 0f, 0f));
            if (hatch != null)
            {
                hatchProp.objectReferenceValue = hatch;
                x += PieceSpacing;
                built++;
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();

        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
        EditorSceneManager.MarkSceneDirty(scene);

        if (built == 0)
        {
            Debug.Log("Interactables kit already complete - nothing built.", root);
        }
        else
        {
            Debug.Log($"Interactables kit: built {built} missing template(s) at {GalleryOrigin}. Save the scene (Ctrl+S) to keep them.", root);
        }
    }

    /// <summary>
    /// LIGHTS OUT &gt; Build &gt; Re-skin Templates &gt; Key, Fuse Box, Button. Build() is build-missing-only, so
    /// swapping a template's model (the Asset Store meshes and the kit lever, 4 Oct 2026) needs its slot
    /// emptied first: this destroys the current KeyPickup, FuseBox and WallButton gallery objects, clears
    /// the three slots and runs Build(), which re-creates just those at the end of the row. Save the
    /// scene afterwards.
    /// </summary>
    [MenuItem("LIGHTS OUT/Build/Re-skin Templates/Key, Fuse Box, Button", priority = 150)]
    public static void RebuildModelTemplates()
    {
        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (kit == null)
        {
            Build();
            return;
        }

        SerializedObject so = new SerializedObject(kit);
        foreach (string slot in new[] { "keyPickup", "fuseBox", "wallButton" })
        {
            SerializedProperty prop = so.FindProperty(slot);
            Component old = prop.objectReferenceValue as Component;
            prop.objectReferenceValue = null;
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        Build();
    }

    /// <summary>
    /// LIGHTS OUT &gt; Build &gt; Re-skin Templates &gt; Bottle, Can, Lantern, Candles, Note. F79: re-skins five
    /// templates with Alchemist House pack models (and adds six bottle look-alikes). Same pattern as
    /// RebuildModelTemplates - the gallery objects in those slots (and every bottleVariants entry) are
    /// destroyed, the slots cleared and Build() re-creates just those at the end of the row. No dialogs.
    /// Save the scene afterwards.
    /// </summary>
    [MenuItem("LIGHTS OUT/Build/Re-skin Templates/Bottle, Can, Lantern, Candles, Note", priority = 151)]
    public static void RebuildAlchemistTemplates()
    {
        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (kit == null)
        {
            Build();
            return;
        }

        if (AlchemistMaterialUpgrade.NeedsConversion()) AlchemistMaterialUpgrade.Convert();

        SerializedObject so = new SerializedObject(kit);
        foreach (string slot in new[] { "throwableBottle", "throwableCan", "lantern", "candles", "loreNote" })
        {
            SerializedProperty prop = so.FindProperty(slot);
            Component old = prop.objectReferenceValue as Component;
            prop.objectReferenceValue = null;
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }

        SerializedProperty variants = so.FindProperty("bottleVariants");
        for (int i = 0; i < variants.arraySize; i++)
        {
            Component old = variants.GetArrayElementAtIndex(i).objectReferenceValue as Component;
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }
        variants.arraySize = 0;
        so.ApplyModifiedPropertiesWithoutUndo();

        Build();
    }

    /// <summary>
    /// F80: a double-leaf cellar trapdoor for MazeEscape.BuildHatch - two WoodDoor01 leaves lying flat,
    /// hinged on their outer edges (children 'LidL'/'LidR', opened about local Z), over a 'Glow' slab that
    /// MazeEscape gives its own emissive material at runtime. No colliders: the hatch is a position test.
    /// Null if the pack is not in the project.
    /// </summary>
    private static GameObject BuildEscapeHatch(Transform parent, Vector3 localPosition)
    {
        const string leaf = "Architecture/WoodDoor01";
        if (!HasPackPrefab(leaf)) return null;

        const float leafScale = 0.88f;   // 1.07 x 1.93 m door -> a 0.94 x 1.70 m leaf
        const float leafWidth = 0.94f;
        const float lift = 0.03f;         // leaves sit just proud of the glow slab

        Material glowMat = LoadOrCreateMat("Interactable_HatchGlow", new Color(0.25f, 1f, 0.7f), new Color(0.25f, 1f, 0.7f) * 3f, 0.1f);

        GameObject root = new GameObject("EscapeHatch");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        Primitive(root.transform, PrimitiveType.Cube, "Glow", new Vector3(0f, 0.01f, 0f), new Vector3(leafWidth * 2f + 0.24f, 0.02f, 1.94f), glowMat, keepCollider: false);

        // The shaft: a black inset over the glow, so the open hatch reads as a hole with a lit rim rather
        // than a glowing floor panel.
        Material pitMat = LoadOrCreateMat("Interactable_HatchPit", Color.black, null, 0f);
        Primitive(root.transform, PrimitiveType.Cube, "Pit", new Vector3(0f, 0.013f, 0f), new Vector3(leafWidth * 2f - 0.1f, 0.02f, 1.6f), pitMat, keepCollider: false);

        Transform lidL = new GameObject("LidL").transform;
        lidL.SetParent(root.transform, false);
        lidL.localPosition = new Vector3(-leafWidth, lift, 0f);
        AlchemistPack.Place(lidL, leaf, new Vector3(leafWidth * 0.5f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f), leafScale);

        Transform lidR = new GameObject("LidR").transform;
        lidR.SetParent(root.transform, false);
        lidR.localPosition = new Vector3(leafWidth, lift, 0f);
        AlchemistPack.Place(lidR, leaf, new Vector3(-leafWidth * 0.5f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(90f, 0f, 0f), leafScale);

        return root;
    }

    /// <summary>One slot past the right-most existing template, so a rebuilt or later-slice template never lands on an older one.</summary>
    private static float NextFreeX(Transform root)
    {
        float max = -PieceSpacing;
        for (int i = 0; i < root.childCount; i++)
        {
            max = Mathf.Max(max, root.GetChild(i).localPosition.x);
        }
        return max + PieceSpacing;
    }

    // ---------------------------------------------------------------- templates

    private static MazeDoor BuildShutterDoor(Transform parent, Vector3 localPosition)
    {
        Material metal = LoadOrCreateMat("Interactable_Shutter", new Color(0.24f, 0.25f, 0.27f), null, 0.4f);
        Material lightMat = LoadOrCreateMat("Interactable_StatusLight", new Color(0.9f, 0.12f, 0.08f), new Color(0.9f, 0.12f, 0.08f) * 2f, 0.1f);

        GameObject root = new GameObject("ShutterDoor");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        MazeDoor door = root.AddComponent<MazeDoor>();

        // Authored as a 1x1 unit panel at local (0,0,0) - MazeDoor.Configure scales X/Y to the real
        // opening and repositions it every frame while sliding (see MazeDoor.ApplyPanelOffset).
        GameObject panel = Primitive(root.transform, PrimitiveType.Cube, "Panel", Vector3.zero, new Vector3(1f, 1f, 0.1f), metal, keepCollider: false);

        GameObject statusLightGo = new GameObject("StatusLight");
        statusLightGo.transform.SetParent(root.transform, false);
        statusLightGo.transform.localPosition = new Vector3(0f, 2.6f, 0.08f);
        GameObject bulb = Primitive(statusLightGo.transform, PrimitiveType.Sphere, "Bulb", Vector3.zero, Vector3.one * 0.1f, lightMat, keepCollider: false);
        Light light = statusLightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = lightMat.HasProperty("_EmissionColor") ? lightMat.GetColor("_EmissionColor") : Color.red;
        light.range = 2.5f;
        light.intensity = 0.6f;
        light.shadows = LightShadows.None;

        SerializedObject so = new SerializedObject(door);
        so.FindProperty("panel").objectReferenceValue = panel.transform;
        so.FindProperty("statusLightRenderer").objectReferenceValue = bulb.GetComponent<Renderer>();
        so.FindProperty("statusLight").objectReferenceValue = light;
        so.ApplyModifiedPropertiesWithoutUndo();

        return door;
    }

    private static WallButton BuildWallButton(Transform parent, Vector3 localPosition)
    {
        Material plateMat = LoadOrCreateMat("Interactable_Plate", new Color(0.16f, 0.17f, 0.19f), null, 0.35f);
        Material capMat = LoadOrCreateMat("Interactable_ButtonCap", new Color(0.75f, 0.08f, 0.06f), null, 0.55f);
        Material lightMat = LoadOrCreateMat("Interactable_StatusLight", new Color(0.9f, 0.12f, 0.08f), new Color(0.9f, 0.12f, 0.08f) * 2f, 0.1f);

        GameObject root;
        Vector3 statusPos;
        GameObject model = MountKitSwitch();
        if (model != null)
        {
            // Maze kit lever panel (4 Oct 2026, user's pick over a store pack): 52 x 70 x 20 cm at
            // scale 1 with its pivot at the bottom-back and the slot facing +Z, so at 0.7 it is placed
            // with its back on z = 0 and its height centred on the root (the generator mounts the root
            // at hand height, LookRotation(-dirVec)).
            root = new GameObject("WallButton");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = Vector3.one * KitSwitchScale;
            model.transform.localPosition = new Vector3(0f, -0.35f * KitSwitchScale, -0.1f * KitSwitchScale);

            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0f, 0.1f * KitSwitchScale);
            collider.size = new Vector3(0.52f, 0.7f, 0.2f) * KitSwitchScale;

            statusPos = new Vector3(0f, 0.35f * KitSwitchScale + 0.03f, 0.1f * KitSwitchScale);
        }
        else
        {
            Debug.LogWarning($"InteractableKitBuilder: {KitSwitchPrefabPath} not found - building the primitive button instead.");
            // The root itself is the plate - a real, trigger-free collider for PlayerInteractor's spherecast.
            root = Primitive(parent, PrimitiveType.Cube, "WallButton", localPosition, new Vector3(0.3f, 0.4f, 0.08f), plateMat, keepCollider: true);
            Primitive(root.transform, PrimitiveType.Cylinder, "Button", new Vector3(0f, 0f, 0.09f),
                new Vector3(0.12f, 0.03f, 0.12f), capMat, keepCollider: false, localRotation: Quaternion.Euler(90f, 0f, 0f));
            statusPos = new Vector3(0f, 0.14f, 0.06f);
        }
        WallButton button = root.AddComponent<WallButton>();

        GameObject statusLightGo = new GameObject("StatusLight");
        statusLightGo.transform.SetParent(root.transform, false);
        statusLightGo.transform.localPosition = statusPos;
        GameObject bulb = Primitive(statusLightGo.transform, PrimitiveType.Sphere, "Bulb", Vector3.zero, Vector3.one * 0.04f, lightMat, keepCollider: false);
        Light light = statusLightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = Color.red;
        light.range = 1f;
        light.intensity = 0.4f;
        light.shadows = LightShadows.None;

        SerializedObject so = new SerializedObject(button);
        so.FindProperty("statusLightRenderer").objectReferenceValue = bulb.GetComponent<Renderer>();
        so.FindProperty("statusLight").objectReferenceValue = light;
        so.ApplyModifiedPropertiesWithoutUndo();

        return button;
    }

    private static BatteryCellPickup BuildBatteryCell(Transform parent, Vector3 localPosition)
    {
        Material bodyMat = LoadOrCreateMat("Interactable_BatteryBody", new Color(0.2f, 0.2f, 0.22f), null, 0.5f);
        Material bandMat = LoadOrCreateMat("Interactable_BatteryBand", new Color(0.25f, 0.95f, 0.4f), new Color(0.25f, 0.95f, 0.4f) * 2f, 0.2f);

        // The root is the cell body - trigger-free solid collider for the interactor ray (decision D11).
        GameObject root = CylinderRH(parent, "BatteryCell", localPosition, 0.07f, 0.16f, bodyMat, keepCollider: true);
        BatteryCellPickup pickup = root.AddComponent<BatteryCellPickup>();

        CylinderRH(root.transform, "Band", Vector3.zero, 0.075f, 0.04f, bandMat, keepCollider: false);

        return pickup;
    }

    private static FuseBox BuildFuseBox(Transform parent, Vector3 localPosition)
    {
        Material cabinetMat = LoadOrCreateMat("Interactable_FuseCabinet", new Color(0.22f, 0.23f, 0.25f), null, 0.35f);
        Material leverMat = LoadOrCreateMat("Interactable_FuseLever", new Color(0.12f, 0.12f, 0.13f), null, 0.5f);
        Material lightMat = LoadOrCreateMat("Interactable_StatusLight", new Color(0.9f, 0.12f, 0.08f), new Color(0.9f, 0.12f, 0.08f) * 2f, 0.1f);

        GameObject root;
        Vector3 leverPos;
        Vector3 statusPos;
        GameObject model = MountPackModel(FuseBoxModelPrefabPath, "FuseBoxModel");
        if (model != null)
        {
            // Pack cabinet (4 Oct 2026): its back sits on z = 0 and the door/fuses face +Z, which is the
            // template's "into the room" side, so no rotation. The glass door is hidden - its shader is
            // not URP and an open cabinet shows the fuse row, which reads as "something to operate".
            root = new GameObject("FuseBox");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = Vector3.one * FuseBoxModelScale;
            Transform door = FindDeep(model.transform, "Fuse_Box_Door");
            if (door != null) door.gameObject.SetActive(false);

            // Solid collider for the interactor spherecast (decision D11), sized to the scaled cabinet.
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0f, 0.1f);
            collider.size = new Vector3(0.48f, 0.32f, 0.22f);

            // Lever against the cabinet's right side; the indicator bulb sits on the lid, back from the edge.
            leverPos = new Vector3(0.25f, -0.03f, 0.1f);
            statusPos = new Vector3(-0.17f, 0.185f, 0.1f);
        }
        else
        {
            Debug.LogWarning($"InteractableKitBuilder: {FuseBoxModelPrefabPath} not found (Sockets and Fuse Box Pack not imported?) - building the primitive cabinet instead.");
            // The root is the cabinet body - trigger-free solid collider for the interactor ray (decision D11).
            root = Primitive(parent, PrimitiveType.Cube, "FuseBox", localPosition, new Vector3(0.5f, 0.6f, 0.16f), cabinetMat, keepCollider: true);
            leverPos = new Vector3(0.12f, -0.05f, 0.1f);
            statusPos = new Vector3(-0.15f, 0.2f, 0.1f);
        }
        FuseBox box = root.AddComponent<FuseBox>();

        Primitive(root.transform, PrimitiveType.Cylinder, "Lever", leverPos,
            new Vector3(0.03f, 0.1f, 0.03f), leverMat, keepCollider: false, localRotation: Quaternion.Euler(0f, 0f, 20f));

        GameObject statusLightGo = new GameObject("StatusLight");
        statusLightGo.transform.SetParent(root.transform, false);
        statusLightGo.transform.localPosition = statusPos;
        GameObject bulb = Primitive(statusLightGo.transform, PrimitiveType.Sphere, "Bulb", Vector3.zero, Vector3.one * 0.06f, lightMat, keepCollider: false);
        Light light = statusLightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = Color.red;
        light.range = 1.5f;
        light.intensity = 0.5f;
        light.shadows = LightShadows.None;

        SerializedObject so = new SerializedObject(box);
        so.FindProperty("indicatorRenderer").objectReferenceValue = bulb.GetComponent<Renderer>();
        so.FindProperty("indicatorLight").objectReferenceValue = light;
        so.ApplyModifiedPropertiesWithoutUndo();

        return box;
    }

    // ---------------------------------------------------------------- F74 templates

    // ---------------------------------------------------------------- F79: Alchemist House re-skins

    /// <summary>A pack model for one bottle look-alike: prefab path under the pack's Prefabs/ folder and a scale multiplier (the flasks are small).</summary>
    private struct BottleVariant
    {
        public string Path;
        public float Scale;
        public BottleVariant(string path, float scale) { Path = path; Scale = scale; }
    }

    private const string PackBottle = "Items/Bottles/Bottle02";
    private const string PackCan = "Items/Dishes/ClutterTanker01";
    private const string PackLantern = "Items/Lights/Oillamp01_on";
    private const string PackNote = "Items/Books/Book09";
    private const float PackNoteScale = 1.5f;

    private static readonly BottleVariant[] BottleVariants =
    {
        new BottleVariant("Items/Bottles/Bottle01", 1f),
        new BottleVariant("Items/Bottles/Bottle03", 1f),
        new BottleVariant("Items/Bottles/Bottle04", 1f),
        new BottleVariant("Items/Bottles/Bottle02b", 1f),
        new BottleVariant("Items/Bottles/Flask04", 1.5f),
        new BottleVariant("Items/Bottles/Flask07", 1.5f),
    };

    private static bool HasPackPrefab(string path)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>(AlchemistPack.Prefabs + path + ".prefab") != null;
    }

    /// <summary>
    /// A throwable whose root origin is the pack model's centre (MazeGenerator.BuildThrowables lifts the
    /// root by a fixed height above the floor) and whose root collider is a primitive capsule fitted to the
    /// model - ThrowableItem.Awake reads GetComponent&lt;Collider&gt;() on the root and Launch adds a
    /// Rigidbody to it, so a non-convex mesh collider would not do. Null if the pack prefab is missing.
    /// </summary>
    private static ThrowableItem BuildPackThrowable(Transform parent, Vector3 localPosition, string rootName, string modelPath, float modelScale,
        ThrowableItem.Kind kind, float minRadius, float minHeight)
    {
        if (!HasPackPrefab(modelPath)) return null;

        GameObject root = new GameObject(rootName);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        GameObject model = AlchemistPack.Spawn(root.transform, modelPath, modelScale);
        Bounds b = AlchemistPack.RenderBounds(model);
        model.transform.position += root.transform.position - b.center;
        b = AlchemistPack.RenderBounds(model);

        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.height = Mathf.Max(b.size.y, minHeight);
        capsule.radius = Mathf.Min(capsule.height * 0.5f, Mathf.Max(minRadius, Mathf.Min(b.size.x, b.size.z) * 0.5f));
        capsule.center = Vector3.zero;

        ThrowableItem item = root.AddComponent<ThrowableItem>();
        SerializedObject so = new SerializedObject(item);
        so.FindProperty("kind").enumValueIndex = (int)kind;
        so.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    private static ThrowableItem BuildBottleVariant(Transform parent, Vector3 localPosition, BottleVariant spec)
    {
        string modelName = spec.Path.Substring(spec.Path.LastIndexOf('/') + 1);
        ThrowableItem item = BuildPackThrowable(parent, localPosition, $"Bottle_{modelName}", spec.Path, spec.Scale, ThrowableItem.Kind.Bottle, 0.03f, 0.1f);
        if (item == null) Debug.LogWarning($"InteractableKitBuilder: {AlchemistPack.Prefabs}{spec.Path}.prefab not found - skipping that bottle variant.");
        return item;
    }

    private static ThrowableItem BuildBottle(Transform parent, Vector3 localPosition)
    {
        ThrowableItem packed = BuildPackThrowable(parent, localPosition, "Bottle", PackBottle, 1f, ThrowableItem.Kind.Bottle, 0.03f, 0.1f);
        if (packed != null) return packed;

        Debug.LogWarning($"InteractableKitBuilder: {AlchemistPack.Prefabs}{PackBottle}.prefab not found - building the primitive bottle instead.");
        return BuildBottlePrimitive(parent, localPosition);
    }

    private static ThrowableItem BuildCan(Transform parent, Vector3 localPosition)
    {
        ThrowableItem packed = BuildPackThrowable(parent, localPosition, "Can", PackCan, 1f, ThrowableItem.Kind.Can, 0.035f, 0.09f);
        if (packed != null) return packed;

        Debug.LogWarning($"InteractableKitBuilder: {AlchemistPack.Prefabs}{PackCan}.prefab not found - building the primitive can instead.");
        return BuildCanPrimitive(parent, localPosition);
    }

    private static ThrowableItem BuildBottlePrimitive(Transform parent, Vector3 localPosition)
    {
        Material glassMat = LoadOrCreateMat("Interactable_BottleGlass", new Color(0.1f, 0.28f, 0.16f), new Color(0.05f, 0.1f, 0.06f), 0.7f);

        GameObject root = CylinderRH(parent, "Bottle", localPosition, 0.045f, 0.26f, glassMat, keepCollider: true);
        CylinderRH(root.transform, "Neck", new Vector3(0f, 0.16f, 0f), 0.018f, 0.1f, glassMat, keepCollider: false);
        ThrowableItem item = root.AddComponent<ThrowableItem>();

        SerializedObject so = new SerializedObject(item);
        so.FindProperty("kind").enumValueIndex = (int)ThrowableItem.Kind.Bottle;
        so.ApplyModifiedPropertiesWithoutUndo();

        return item;
    }

    private static ThrowableItem BuildCanPrimitive(Transform parent, Vector3 localPosition)
    {
        Material metalMat = LoadOrCreateMat("Interactable_CanMetal", new Color(0.72f, 0.74f, 0.76f), null, 0.75f);

        GameObject root = CylinderRH(parent, "Can", localPosition, 0.033f, 0.12f, metalMat, keepCollider: true);
        ThrowableItem item = root.AddComponent<ThrowableItem>();

        SerializedObject so = new SerializedObject(item);
        so.FindProperty("kind").enumValueIndex = (int)ThrowableItem.Kind.Can;
        so.ApplyModifiedPropertiesWithoutUndo();

        return item;
    }

    /// <summary>Shared by glassPatch/puddle: a flat quad lying on the floor. `emission` null = a matte (non-glowing) puddle; a colour = glass's faint glassy sheen.</summary>
    private static NoisySurface BuildFloorPatch(Transform parent, Vector3 localPosition, string name, Color color, Color? emission)
    {
        Material mat = LoadOrCreateMat($"Interactable_{name}", color, emission, 0.75f);

        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        GameObject quad = Primitive(root.transform, PrimitiveType.Quad, "Quad", Vector3.zero, Vector3.one * 1.6f, mat, keepCollider: false,
            localRotation: Quaternion.Euler(90f, 0f, 0f));
        quad.transform.localPosition = Vector3.up * 0.005f;

        return root.AddComponent<NoisySurface>();
    }

    private static Lantern BuildLantern(Transform parent, Vector3 localPosition)
    {
        if (!HasPackPrefab(PackLantern))
        {
            Debug.LogWarning($"InteractableKitBuilder: {AlchemistPack.Prefabs}{PackLantern}.prefab not found - building the primitive lantern instead.");
            return BuildLanternPrimitive(parent, localPosition);
        }

        GameObject root = new GameObject("Lantern");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        // The pack lamp (its own light is stripped by AlchemistPack.Spawn - ours below keeps the old numbers),
        // centred over the root with its underside at -0.025 where the old primitive base's underside was.
        GameObject model = AlchemistPack.Spawn(root.transform, PackLantern);
        Bounds b = AlchemistPack.RenderBounds(model);
        model.transform.position += new Vector3(root.transform.position.x - b.center.x, root.transform.position.y - 0.025f - b.min.y, root.transform.position.z - b.center.z);
        b = AlchemistPack.RenderBounds(model);

        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.height = b.size.y;
        capsule.radius = Mathf.Min(b.size.y * 0.5f, Mathf.Max(b.size.x, b.size.z) * 0.5f);
        capsule.center = root.transform.InverseTransformPoint(b.center);

        GameObject lightGo = new GameObject("Light");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.position = new Vector3(b.center.x, b.min.y + b.size.y * 0.6f, b.center.z); // at the glass
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.78f, 0.45f);
        light.range = 6f;
        light.intensity = 1.1f;
        light.shadows = LightShadows.None;

        Lantern lantern = root.AddComponent<Lantern>();
        SerializedObject so = new SerializedObject(lantern);
        so.FindProperty("lanternLight").objectReferenceValue = light;
        so.ApplyModifiedPropertiesWithoutUndo();

        return lantern;
    }

    private static Lantern BuildLanternPrimitive(Transform parent, Vector3 localPosition)
    {
        Material bodyMat = LoadOrCreateMat("Interactable_LanternBody", new Color(0.2f, 0.18f, 0.14f), null, 0.4f);
        Material glassMat = LoadOrCreateMat("Interactable_LanternGlass", new Color(0.95f, 0.75f, 0.4f), new Color(0.95f, 0.75f, 0.4f) * 2f, 0.3f);

        GameObject root = CylinderRH(parent, "Lantern", localPosition, 0.1f, 0.05f, bodyMat, keepCollider: true); // base
        CylinderRH(root.transform, "Glass", new Vector3(0f, 0.14f, 0f), 0.08f, 0.22f, glassMat, keepCollider: false);
        CylinderRH(root.transform, "Cap", new Vector3(0f, 0.27f, 0f), 0.1f, 0.04f, bodyMat, keepCollider: false);

        GameObject lightGo = new GameObject("Light");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.14f, 0f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.78f, 0.45f);
        light.range = 6f;
        light.intensity = 1.1f;
        light.shadows = LightShadows.None;

        Lantern lantern = root.AddComponent<Lantern>();
        SerializedObject so = new SerializedObject(lantern);
        so.FindProperty("lanternLight").objectReferenceValue = light;
        so.ApplyModifiedPropertiesWithoutUndo();

        return lantern;
    }

    /// <summary>
    /// A closed pack book (Book09 x1.5) lying flat against the Edge quad, cover towards +Z - the side
    /// MazeGenerator.BuildLoreNotes faces into the corridor (LookRotation(-dirVec)). The Edge quad and its
    /// BoxCollider are exactly what the primitive note has: they carry the click target.
    /// Note for the reviewer: the built-in Quad is visible from its -Z side only, so the old Paper and Edge
    /// quads face the wall once placed; the book is built to face the corridor instead.
    /// </summary>
    private static LoreNote BuildLoreNote(Transform parent, Vector3 localPosition)
    {
        if (!HasPackPrefab(PackNote))
        {
            Debug.LogWarning($"InteractableKitBuilder: {AlchemistPack.Prefabs}{PackNote}.prefab not found - building the primitive note instead.");
            return BuildLoreNotePrimitive(parent, localPosition);
        }

        Material edgeMat = LoadOrCreateMat("Interactable_NoteEdge", new Color(0.9f, 0.85f, 0.5f), new Color(0.9f, 0.85f, 0.5f) * 1.2f, 0.1f);

        GameObject root = new GameObject("LoreNote");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        // A Quad only renders from its -Z side and MazeGenerator.BuildLoreNotes points the root's +Z into
        // the corridor, so the glow border is turned to face +Z here (the primitive note never was, and
        // cannot be seen from the corridor). It sits between the wall and the book's back cover.
        GameObject edge = Primitive(root.transform, PrimitiveType.Quad, "Edge", new Vector3(0f, 0f, 0.005f), new Vector3(0.42f, 0.58f, 1f), edgeMat, keepCollider: false,
            localRotation: Quaternion.Euler(0f, 180f, 0f));
        BoxCollider box = edge.AddComponent<BoxCollider>();
        box.size = new Vector3(1f, 1f, 0.1f);

        GameObject book = AlchemistPack.Spawn(root.transform, PackNote, PackNoteScale);
        Bounds b = AlchemistPack.RenderBounds(book);
        // Upright, thin axis along Z (a book standing against the wall); yaw picks which cover looks out.
        if (b.size.x < b.size.z) book.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * book.transform.rotation;
        book.transform.rotation = Quaternion.Euler(0f, NoteBookYaw, 0f) * book.transform.rotation;
        b = AlchemistPack.RenderBounds(book);
        book.transform.position += new Vector3(root.transform.position.x - b.center.x, root.transform.position.y - b.center.y, root.transform.position.z + 0.012f - b.min.z);

        return root.AddComponent<LoreNote>();
    }

    /// <summary>Which way the book is turned about Y after its thin axis is put on Z: 0 or 180 (set from a visual check, so the cover - not the back - faces +Z).</summary>
    private const float NoteBookYaw = 0f;

    private static LoreNote BuildLoreNotePrimitive(Transform parent, Vector3 localPosition)
    {
        Material paperMat = LoadOrCreateMat("Interactable_NotePaper", new Color(0.78f, 0.74f, 0.62f), null, 0.1f);
        Material edgeMat = LoadOrCreateMat("Interactable_NoteEdge", new Color(0.9f, 0.85f, 0.5f), new Color(0.9f, 0.85f, 0.5f) * 1.2f, 0.1f);

        GameObject root = new GameObject("LoreNote");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        // The edge sits a hair behind the paper, so it reads as a faint emissive border - the interactor
        // ray hits a plain BoxCollider on this larger backing quad, easier to click than the paper itself.
        GameObject edge = Primitive(root.transform, PrimitiveType.Quad, "Edge", new Vector3(0f, 0f, 0.01f), new Vector3(0.42f, 0.58f, 1f), edgeMat, keepCollider: false);
        Primitive(root.transform, PrimitiveType.Quad, "Paper", Vector3.zero, new Vector3(0.38f, 0.54f, 1f), paperMat, keepCollider: false);
        BoxCollider box = edge.AddComponent<BoxCollider>();
        box.size = new Vector3(1f, 1f, 0.1f);

        return root.AddComponent<LoreNote>();
    }

    private static ThemeInteractable BuildCallBell(Transform parent, Vector3 localPosition)
    {
        Material metalMat = LoadOrCreateMat("Interactable_BellMetal", new Color(0.55f, 0.46f, 0.2f), null, 0.7f);

        GameObject root = new GameObject("CallBell");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        CylinderRH(root.transform, "Bracket", new Vector3(0f, 0.1f, 0f), 0.02f, 0.1f, metalMat, keepCollider: false);
        GameObject bell = Primitive(root.transform, PrimitiveType.Sphere, "Bell", Vector3.zero, new Vector3(0.14f, 0.12f, 0.14f), metalMat, keepCollider: false);
        SphereCollider sphere = bell.AddComponent<SphereCollider>();
        sphere.radius = 0.55f;

        return root.AddComponent<ThemeInteractable>();
    }

    private static ThemeInteractable BuildSteamValve(Transform parent, Vector3 localPosition)
    {
        Material metalMat = LoadOrCreateMat("Interactable_ValveMetal", new Color(0.35f, 0.32f, 0.3f), null, 0.55f);

        GameObject root = CylinderRH(parent, "SteamValve", localPosition, 0.03f, 0.08f, metalMat, keepCollider: true); // stem
        CylinderRH(root.transform, "Wheel", new Vector3(0f, 0.05f, 0f), 0.16f, 0.03f, metalMat, keepCollider: false);
        Primitive(root.transform, PrimitiveType.Cube, "SpokeA", new Vector3(0f, 0.05f, 0f), new Vector3(0.3f, 0.02f, 0.02f), metalMat, keepCollider: false);
        Primitive(root.transform, PrimitiveType.Cube, "SpokeB", new Vector3(0f, 0.05f, 0f), new Vector3(0.02f, 0.02f, 0.3f), metalMat, keepCollider: false);

        return root.AddComponent<ThemeInteractable>();
    }

    private static readonly string[] PackCandles = { "Items/Lights/Candle01", "Items/Lights/Candle01c", "Items/Lights/Candle01d", "Items/Lights/Candle01b" };

    /// <summary>
    /// Three pack candles side by side (0.12 m apart) and a fourth in front, their bottoms on the root's y = 0
    /// (the generator drops the root on the floor at the wall face, so the group sits 0.1 m out from the wall).
    /// One BoxCollider fits the group; the (disabled-until-lit) Light sits on the tallest wick with a tiny
    /// emissive flame bead, as the primitive candles had.
    /// </summary>
    private static ThemeInteractable BuildCandles(Transform parent, Vector3 localPosition)
    {
        foreach (string path in PackCandles)
        {
            if (HasPackPrefab(path)) continue;
            Debug.LogWarning($"InteractableKitBuilder: {AlchemistPack.Prefabs}{path}.prefab not found - building the primitive candles instead.");
            return BuildCandlesPrimitive(parent, localPosition);
        }

        Material flameMat = LoadOrCreateMat("Interactable_CandleFlame", new Color(1f, 0.6f, 0.15f), new Color(1f, 0.6f, 0.15f) * 3f, 0.1f);

        GameObject root = new GameObject("Candles");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        Vector3[] spots = { new Vector3(-0.12f, 0f, 0.1f), new Vector3(0f, 0f, 0.1f), new Vector3(0.12f, 0f, 0.1f), new Vector3(0.02f, 0f, 0.24f) };
        float[] yaws = { 0f, 40f, -25f, 80f };
        Bounds group = default;
        Bounds tallest = default;
        bool any = false;
        for (int i = 0; i < PackCandles.Length; i++)
        {
            GameObject candle = AlchemistPack.Spawn(root.transform, PackCandles[i]);
            candle.transform.rotation = Quaternion.Euler(0f, yaws[i], 0f) * candle.transform.rotation;
            Bounds b = AlchemistPack.RenderBounds(candle);
            candle.transform.position += root.transform.TransformPoint(spots[i]) - new Vector3(b.center.x, b.min.y, b.center.z);
            b = AlchemistPack.RenderBounds(candle);

            if (!any) { group = b; tallest = b; any = true; }
            else { group.Encapsulate(b); if (b.max.y > tallest.max.y) tallest = b; }
        }

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = root.transform.InverseTransformPoint(group.center);
        box.size = group.size;

        GameObject wick = Primitive(root.transform, PrimitiveType.Sphere, "Flame", Vector3.zero, Vector3.one * 0.02f, flameMat, keepCollider: false);
        wick.transform.position = new Vector3(tallest.center.x, tallest.max.y + 0.006f, tallest.center.z);

        Light light = wick.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.6f, 0.15f);
        light.range = 4f;
        light.intensity = 1f;
        light.shadows = LightShadows.None;

        ThemeInteractable candles = root.AddComponent<ThemeInteractable>();
        SerializedObject so = new SerializedObject(candles);
        so.FindProperty("indicatorLight").objectReferenceValue = light;
        so.ApplyModifiedPropertiesWithoutUndo();

        return candles;
    }

    private static ThemeInteractable BuildCandlesPrimitive(Transform parent, Vector3 localPosition)
    {
        Material waxMat = LoadOrCreateMat("Interactable_CandleWax", new Color(0.72f, 0.66f, 0.5f), null, 0.2f);
        Material flameMat = LoadOrCreateMat("Interactable_CandleFlame", new Color(1f, 0.6f, 0.15f), new Color(1f, 0.6f, 0.15f) * 3f, 0.1f);

        GameObject root = new GameObject("Candles");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        Light flameLight = null;
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 0.08f;
            float height = 0.1f + i * 0.02f;
            GameObject stick = CylinderRH(root.transform, $"Candle{i}", new Vector3(x, height * 0.5f, 0f), 0.015f, height, waxMat, keepCollider: i == 1);
            GameObject flame = Primitive(stick.transform, PrimitiveType.Sphere, "Flame", new Vector3(0f, height * 0.5f + 0.02f, 0f), Vector3.one * 0.03f, flameMat, keepCollider: false);
            if (i == 1)
            {
                Light light = flame.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = flameMat.HasProperty("_EmissionColor") ? flameMat.GetColor("_EmissionColor") : new Color(1f, 0.6f, 0.15f);
                light.range = 4f;
                light.intensity = 1f;
                light.shadows = LightShadows.None;
                flameLight = light;
            }
        }

        ThemeInteractable candles = root.AddComponent<ThemeInteractable>();
        SerializedObject so = new SerializedObject(candles);
        so.FindProperty("indicatorLight").objectReferenceValue = flameLight;
        so.ApplyModifiedPropertiesWithoutUndo();

        return candles;
    }

    private static ThemeInteractable BuildTerminal(Transform parent, Vector3 localPosition)
    {
        Material caseMat = LoadOrCreateMat("Interactable_TerminalCase", new Color(0.18f, 0.19f, 0.2f), null, 0.4f);
        Material screenMat = LoadOrCreateMat("Interactable_TerminalScreen", new Color(0.1f, 0.5f, 0.15f), new Color(0.1f, 0.5f, 0.15f) * 1.6f, 0.2f);

        GameObject root = Primitive(parent, PrimitiveType.Cube, "Terminal", localPosition, new Vector3(0.5f, 0.7f, 0.4f), caseMat, keepCollider: true);
        Primitive(root.transform, PrimitiveType.Quad, "Screen", new Vector3(0f, 0.1f, 0.21f), new Vector3(0.32f, 0.22f, 1f), screenMat, keepCollider: false,
            localRotation: Quaternion.Euler(0f, 180f, 0f));

        return root.AddComponent<ThemeInteractable>();
    }

    // ---------------------------------------------------------------- F75 template (the Stalker)

    /// <summary>
    /// Decision D7/F75: the Stalker's body is the player's own robot, blackened, with pin-prick eyes -
    /// deliberately not Adam. Instantiates PlayerRobot.prefab, unpacks it (the prefab asset itself is
    /// never touched), then strips everything that is not visual: movement/input, the player's own
    /// stealth/stamina components, and every camera/listener/CinemachineBrain the prefab carries (its
    /// MainCamera lives inside it - leaving it would give the scene two MainCameras). Keeps the Animator,
    /// its controller and the skinned meshes. Returns null (and skips the slot) if the prefab is missing.
    /// </summary>
    private static Stalker BuildStalkerTemplate(Transform parent, Vector3 localPosition)
    {
        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);
        if (prefabAsset == null)
        {
            Debug.LogError($"InteractableKitBuilder: {RobotPrefabPath} not found - the stalker template was skipped.");
            return null;
        }

        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, parent);
        root.name = "Stalker";
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * 1.15f;

        // Breaks the prefab connection so the strip below can freely remove components/children -
        // PlayerRobot.prefab itself is never modified.
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // Order matters: ThirdPersonController's [RequireComponent] on CharacterController/PlayerInput
        // would block removing those two first.
        DestroyAllOfType<StarterAssets.ThirdPersonController>(root);
        DestroyAllOfType<StarterAssets.StarterAssetsInputs>(root);
        DestroyAllOfType<FirstPersonRig>(root);
        DestroyAllOfType<PlayerStealthState>(root);
        DestroyAllOfType<PlayerStamina>(root);
        DestroyAllOfType<UnityEngine.InputSystem.PlayerInput>(root);
        DestroyAllOfType<CharacterController>(root);

        // The whole camera child goes, not just the Camera component - its MainCamera tag is exactly
        // what would give the scene a second MainCamera once this template is active.
        foreach (Camera camera in new List<Camera>(root.GetComponentsInChildren<Camera>(true)))
        {
            if (camera != null) Object.DestroyImmediate(camera.gameObject);
        }
        DestroyAllOfType<AudioListener>(root);
        DestroyAllOfType<Unity.Cinemachine.CinemachineBrain>(root);

        // The prefab also carries the third-person PlayerFollowCamera (a Cinemachine virtual camera, no
        // Camera component, so the loop above misses it). Every stalker would drag one along.
        foreach (Unity.Cinemachine.CinemachineCamera vcam in new List<Unity.Cinemachine.CinemachineCamera>(root.GetComponentsInChildren<Unity.Cinemachine.CinemachineCamera>(true)))
        {
            if (vcam != null) Object.DestroyImmediate(vcam.gameObject);
        }

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.tag = "Untagged";
        }

        Material bodyMat = LoadOrCreateMat("Stalker_Body", new Color(0.02f, 0.02f, 0.025f), null, 0.75f);
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = bodyMat;
            renderer.sharedMaterials = mats;
        }

        BuildStalkerEyes(root);

        Stalker stalker = root.AddComponent<Stalker>();
        StanPhraseBookBuilder.AssignTo(stalker); // F82: keep the phrase-book link across a full rebuild (no-op if the asset doesn't exist yet)
        // An expensive skinned mesh + animator should not sit active (and animating/rendering) in the
        // gallery - MazeGenerator.BuildStalker's Instantiate + SetActive(true) is what brings a clone
        // to life at runtime, same pattern as CreateDoor.
        root.SetActive(false);
        return stalker;
    }

    private static void DestroyAllOfType<T>(GameObject root) where T : Component
    {
        foreach (T component in root.GetComponentsInChildren<T>(true))
        {
            if (component != null) Object.DestroyImmediate(component);
        }
    }

    /// <summary>
    /// Two tiny white emissive sphere eyes on the head bone - same bone lookup AIPresence.BuildEyes uses
    /// (a Humanoid Head mapping, else a "Head"-named node), and the same world-space placement +
    /// WorldToLocalScale technique (a bone can carry any scale in its own local frame; placing/sizing in
    /// bone-local space directly could make the eyes invisible or metres wide).
    /// </summary>
    private static void BuildStalkerEyes(GameObject root)
    {
        Animator animator = root.GetComponentInChildren<Animator>();
        Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) head = FindDeep(root.transform, "Head");
        if (head == null)
        {
            Debug.LogWarning("InteractableKitBuilder: no Head bone found on the robot rig - the stalker has no eyes.", root);
            return;
        }

        // The Animator node itself (not the eyes' own bone) sits 90 degrees off the root on this rig -
        // AIFollower.ComputeModelYawOffset's exact note - so the face direction comes from its forward.
        Vector3 forward = animator != null ? animator.transform.forward : root.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = root.transform.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        const float eyeForward = 0.09f;
        const float eyeUp = 0.02f;
        const float eyeSpacing = 0.045f;
        const float eyeWorldSize = 0.03f;

        Vector3 leftWorld = head.position + forward * eyeForward + Vector3.up * eyeUp - right * eyeSpacing;
        Vector3 rightWorld = head.position + forward * eyeForward + Vector3.up * eyeUp + right * eyeSpacing;

        Material eyeMat = LoadOrCreateMat("Stalker_Eyes", Color.white, Color.white * 3f, 0.1f);
        BuildEyeSphere(head, "LeftEye_Glow", leftWorld, eyeMat, eyeWorldSize);
        BuildEyeSphere(head, "RightEye_Glow", rightWorld, eyeMat, eyeWorldSize);
    }

    private static void BuildEyeSphere(Transform parent, string name, Vector3 worldPosition, Material material, float worldSize)
    {
        GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eye.name = name;
        Collider collider = eye.GetComponent<Collider>();
        if (collider != null) Object.DestroyImmediate(collider);
        eye.transform.SetPositionAndRotation(worldPosition, parent.rotation);
        eye.transform.SetParent(parent, true);
        eye.transform.localScale = WorldToLocalScale(parent, worldSize);
        eye.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>Local scale that gives a uniform world size of `worldSize` under `parent`, whatever the parent chain's scale is - same technique as AIPresence.WorldToLocalScale.</summary>
    private static Vector3 WorldToLocalScale(Transform parent, float worldSize)
    {
        Vector3 lossy = parent.lossyScale;
        return new Vector3(
            worldSize / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
            worldSize / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            worldSize / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    // ---------------------------------------------------------------- F77 template (Key Hunt)

    /// <summary>
    /// A brass key on a dark iron wall hook (F77). The root is the hook plate - a solid collider for the
    /// interactor spherecast, the same decision D11 precedent as BuildBatteryCell/BuildFuseBox. The
    /// generator mounts it with the lore-note offset recipe and rotates it with LookRotation(-dirVec), so
    /// it is authored facing +Z here.
    /// </summary>
    private static KeyPickup BuildKeyPickup(Transform parent, Vector3 localPosition)
    {
        Material hookMat = LoadOrCreateMat("Interactable_KeyHook", new Color(0.16f, 0.16f, 0.17f), null, 0.45f);
        Material brassMat = LoadOrCreateMat("Interactable_KeyBrass", new Color(0.85f, 0.65f, 0.25f), new Color(0.85f, 0.65f, 0.25f) * 1.5f, 0.6f);

        // Unscaled root (a scaled primitive root would squash every child by its own scale) carrying the
        // solid collider for the interactor spherecast (decision D11); the hook plate is a child.
        GameObject root = new GameObject("KeyPickup");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        BoxCollider rootCollider = root.AddComponent<BoxCollider>();
        rootCollider.center = new Vector3(0f, -0.03f, 0.03f);
        rootCollider.size = new Vector3(0.18f, 0.34f, 0.09f);
        KeyPickup key = root.AddComponent<KeyPickup>();

        Primitive(root.transform, PrimitiveType.Cube, "Plate", new Vector3(0f, -0.03f, 0f), new Vector3(0.16f, 0.3f, 0.03f), hookMat, keepCollider: false);
        Primitive(root.transform, PrimitiveType.Cylinder, "Hook", new Vector3(0f, 0.06f, 0.03f),
            new Vector3(0.02f, 0.03f, 0.02f), hookMat, keepCollider: false, localRotation: Quaternion.Euler(90f, 0f, 0f));

        GameObject model = MountPackModel(KeyModelPrefabPath, "KeyModel");
        if (model != null)
        {
            // Pack key (4 Oct 2026): 13 cm tall, bow and keyring near the top, flat in Z. Hung so the ring
            // sits on the hook, just in front of the plate. A faint brass emission on the key body itself
            // so the metal catches the eye in the dark even before the glint light does.
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = Vector3.one * KeyModelScale;
            model.transform.localPosition = new Vector3(0f, -0.035f, 0.045f);
            Renderer body = model.GetComponent<Renderer>();
            if (body != null && body.sharedMaterial != null)
            {
                // Kept very faint: at 0.35 the whole key blew out to white under the glint light.
                Material bodyMat = body.sharedMaterial;
                bodyMat.EnableKeyword("_EMISSION");
                bodyMat.SetColor("_EmissionColor", new Color(0.85f, 0.65f, 0.25f) * 0.06f);
                bodyMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                EditorUtility.SetDirty(bodyMat);
            }
        }
        else
        {
            Debug.LogWarning($"InteractableKitBuilder: {KeyModelPrefabPath} not found (Simple Low Poly Keys not imported?) - building the primitive key instead.");
            Primitive(root.transform, PrimitiveType.Cylinder, "KeyBow", new Vector3(0f, 0.03f, 0.04f),
                new Vector3(0.07f, 0.008f, 0.07f), brassMat, keepCollider: false, localRotation: Quaternion.Euler(90f, 0f, 0f));
            Primitive(root.transform, PrimitiveType.Cube, "KeyShaft", new Vector3(0f, -0.045f, 0.04f),
                new Vector3(0.018f, 0.12f, 0.014f), brassMat, keepCollider: false);
            Primitive(root.transform, PrimitiveType.Cube, "KeyBit", new Vector3(0.015f, -0.09f, 0.04f),
                new Vector3(0.04f, 0.02f, 0.014f), brassMat, keepCollider: false);
        }

        GameObject glintGo = new GameObject("Glint");
        glintGo.transform.SetParent(root.transform, false);
        // Held well off the key: a point light a few centimetres from the metal saturates it to white.
        glintGo.transform.localPosition = new Vector3(0f, 0.05f, 0.32f);
        Light glint = glintGo.AddComponent<Light>();
        glint.type = LightType.Point;
        glint.color = new Color(1f, 0.85f, 0.55f);
        glint.range = 1.5f;
        glint.intensity = 0.22f;
        glint.shadows = LightShadows.None;

        SerializedObject so = new SerializedObject(key);
        so.FindProperty("glintLight").objectReferenceValue = glint;
        so.ApplyModifiedPropertiesWithoutUndo();

        return key;
    }

    // ---------------------------------------------------------------- helpers (same conventions as FloorThemeBuilder/ShopRoomBuilder)

    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(MaterialFolder)) return;
        if (!AssetDatabase.IsValidFolder("Assets/SourceFiles/Materials")) AssetDatabase.CreateFolder("Assets/SourceFiles", "Materials");
        AssetDatabase.CreateFolder("Assets/SourceFiles/Materials", "Interactables");
    }

    private static GameObject Primitive(Transform parent, PrimitiveType type, string name, Vector3 localPosition, Vector3 localScale,
        Material material, bool keepCollider, Quaternion? localRotation = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = localRotation ?? Quaternion.identity;
        go.transform.localScale = localScale;
        if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;

        if (!keepCollider)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
        }
        return go;
    }

    /// <summary>The built-in cylinder is 2 units tall with a 1-unit diameter at scale 1, so this converts a radius/height pair into that scale.</summary>
    private static GameObject CylinderRH(Transform parent, string name, Vector3 localPosition, float radius, float height, Material material, bool keepCollider)
    {
        return Primitive(parent, PrimitiveType.Cylinder, name, localPosition, new Vector3(radius * 2f, height * 0.5f, radius * 2f), material, keepCollider);
    }

    // ---------------------------------------------------------------- Asset Store pack models (4 Oct 2026)

    /// <summary>
    /// Instantiates a pack prefab as a (linked) prefab instance named `name`, with every Built-in material
    /// on it swapped for a URP Lit copy from UrpCopyOf. Returns null if the prefab is not in the project,
    /// so each caller can fall back to its primitive model. Not parented here: the caller decides.
    /// </summary>
    private static GameObject MountPackModel(string prefabPath, string name)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return null;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = name;

        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
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
            if (changed) renderer.sharedMaterials = materials;
        }
        return instance;
    }

    /// <summary>
    /// The Maze kit's Switch panel, unpacked (so its own BoxCollider can go - the template root carries
    /// the interactor collider) and with its lever restored: the kit prefab's "Palanca" child has an
    /// empty MeshFilter, while Switch.fbx still contains the "Lever" mesh it was meant to show. Null if
    /// the kit is not in the project.
    /// </summary>
    private static GameObject MountKitSwitch()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KitSwitchPrefabPath);
        if (prefab == null) return null;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = "SwitchModel";

        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(collider);
        }

        UnityEngine.Mesh leverMesh = null;
        foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(KitSwitchModelPath))
        {
            if (sub is UnityEngine.Mesh mesh && mesh.name == "Lever") { leverMesh = mesh; break; }
        }
        foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null && leverMesh != null) filter.sharedMesh = leverMesh;
        }
        return instance;
    }

    /// <summary>
    /// A URP Lit material carrying `source`'s base colour/texture, normal map and metallic map, saved as
    /// MaterialFolder/Pack_&lt;name&gt;.mat (loaded, never overwritten, if it already exists - same rule as
    /// LoadOrCreateMat). Materials already on a URP shader are returned as they are. Only the textures
    /// are reused; the packs' own Standard materials are left untouched.
    /// </summary>
    private static Material UrpCopyOf(Material source)
    {
        if (source == null) return null;
        if (source.shader != null && source.shader.name.StartsWith("Universal Render Pipeline/")) return source;

        string safeName = source.name.Replace(' ', '_');
        string path = $"{MaterialFolder}/Pack_{safeName}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) return source;

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

    /// <summary>Loaded if it already exists (hand edits survive a rebuild), otherwise created as a URP Lit material.</summary>
    private static Material LoadOrCreateMat(string name, Color color, Color? emission, float smoothness)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);

        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}

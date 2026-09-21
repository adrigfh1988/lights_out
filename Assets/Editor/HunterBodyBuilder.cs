using System;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LIGHTS OUT &gt; Build Hunter Body (F47, second attempt). Swaps whatever is under AI_Follower - the
/// placeholder Timmy robot, or the missing-prefab zombie from the first attempt - for an instance of
/// Adam from the Adam Character Pack, used as-is: no importer edits, no material, no rescale (the pack
/// already ships a correctly rigged, textured, unit-scaled Humanoid).
///
/// Locomotion clips are looked up by keyword in Assets/SourceFiles/Animation/Hunter/ (Mixamo drops),
/// with pack clips as fallbacks so the swap works before any Mixamo download. Hunter.controller is
/// generated data and is rebuilt from scratch every run, unlike Adam's own assets. See
/// plannings/hunter-body-adam-plan.md for the full decision record; plannings/hunter-body-plan.md is the
/// superseded zombie attempt, kept for the AIPresence background.
///
/// Nothing here runs in a build; it is a one-shot scene edit, same pattern as ShopRoomBuilder.
/// </summary>
public static class HunterBodyBuilder
{
    private const string PackFolder = "Assets/UnityTechnologies/Adam Character Pack";
    private const string ModelPath = PackFolder + "/Adam/Adam.FBX";
    private const string PackWalkPath = PackFolder + "/Adam/Adam_Walk.FBX";
    private const string PackIdlePath = PackFolder + "/Guard/Guard_Idle.FBX";
    private const string PackWalkClipName = "Adam_Walk";
    private const string PackIdleClipName = "Guard_Idle";

    private const string ClipFolder = "Assets/SourceFiles/Animation/Hunter";
    private const string AnimationFolder = "Assets/SourceFiles/Animation";
    private const string ControllerPath = AnimationFolder + "/Hunter.controller";
    private const string BodyChildName = "HunterBody";
    private const string PlayerRobotFileName = "PlayerRobot.prefab";

    private const float DefaultWalkSpeed = 1.6f;
    private const float DefaultRunSpeed = 4.0f;
    private const float DefaultEyeHeight = 1.6f;
    private const float EyeHeightPadding = 0.10f;

    [MenuItem("LIGHTS OUT/Build Hunter Body")]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Build Hunter Body", "Open the game scene first.", "OK");
            return;
        }

        // Loaded, never (re)imported - Adam.FBX's importer belongs to the pack and must not be touched.
        GameObject adamPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (adamPrefab == null)
        {
            EditorUtility.DisplayDialog("Build Hunter Body",
                "Import the Adam Character Pack from Package Manager > My Assets first.", "OK");
            return;
        }

        AIFollower aiFollower = UnityEngine.Object.FindAnyObjectByType<AIFollower>();
        if (aiFollower == null)
        {
            EditorUtility.DisplayDialog("Build Hunter Body", "No AIFollower found in the scene (looked for AI_Follower).", "OK");
            return;
        }
        Transform aiRoot = aiFollower.transform;

        // ---------------------------------------------------------------- clips

        EnsureFolder(ClipFolder);

        ResolveClip("idle", PackIdlePath, PackIdleClipName, out AnimationClip idleClip, out string idleSource);
        ResolveClip("walk", PackWalkPath, PackWalkClipName, out AnimationClip walkClip, out string walkSource);

        string runFbx = FindClipFbx("run");
        AnimationClip runClipFound = null;
        if (runFbx != null)
        {
            PrepareClipFbx(runFbx);
            runClipFound = LoadClip(runFbx, null);
        }

        bool runIsSpedUpWalk = runClipFound == null;
        AnimationClip runClipForTree = runIsSpedUpWalk ? walkClip : runClipFound;

        float walkThreshold = GetClipSpeed(walkClip, DefaultWalkSpeed);
        float runThreshold = runIsSpedUpWalk ? DefaultRunSpeed : GetClipSpeed(runClipForTree, DefaultRunSpeed);
        string runSource = runIsSpedUpWalk
            ? $"{walkSource} ×{(runThreshold / Mathf.Max(0.0001f, walkThreshold)):0.#}"
            : Path.GetFileNameWithoutExtension(runFbx);

        if (idleClip == null)
        {
            Debug.LogError("HunterBodyBuilder: no idle clip could be loaded (neither a custom clip nor the Guard_Idle fallback) - the Locomotion blend tree will be missing its idle pose.");
        }

        // ---------------------------------------------------------------- controller (rebuilt every run)

        AnimatorController controller = BuildController(idleClip, walkClip, runClipForTree, runIsSpedUpWalk, walkThreshold, runThreshold);

        // ---------------------------------------------------------------- measurement (needs the controller for the idle pose)

        float headHeightY = MeasureHeadHeight(adamPrefab, controller, out bool headHeightOk);
        float eyeHeight;
        if (headHeightOk)
        {
            eyeHeight = headHeightY + EyeHeightPadding;
        }
        else
        {
            eyeHeight = DefaultEyeHeight;
            Debug.LogWarning($"HunterBodyBuilder: could not measure the Humanoid Head bone on Adam; eyeHeight falls back to {DefaultEyeHeight:F2} m.", adamPrefab);
        }

        // ---------------------------------------------------------------- scene swap, one undo step

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build Hunter Body");

        Transform existingBody = aiRoot.Find(BodyChildName);
        if (existingBody != null)
        {
            if (!EditorUtility.DisplayDialog("Build Hunter Body",
                    "AI_Follower already has a HunterBody child. Replace it?", "Replace", "Cancel"))
            {
                return;
            }
            Undo.DestroyObjectImmediate(existingBody.gameObject);
        }

        Transform robotChild = FindRobotChild(aiRoot);
        if (robotChild != null)
        {
            if (!EditorUtility.DisplayDialog("Build Hunter Body",
                    $"Remove the Timmy robot body ('{robotChild.name}') from AI_Follower? It will be replaced by Adam. " +
                    "To restore it later, drag PlayerRobot.prefab back under AI_Follower (Ctrl+Z right after this also restores it, in one step).",
                    "Remove", "Cancel"))
            {
                return;
            }
            Undo.DestroyObjectImmediate(robotChild.gameObject);
        }

        GameObject bodyInstance = (GameObject)PrefabUtility.InstantiatePrefab(adamPrefab, aiRoot);
        Undo.RegisterCreatedObjectUndo(bodyInstance, "Build Hunter Body");
        bodyInstance.name = BodyChildName;
        bodyInstance.transform.localPosition = Vector3.zero;
        bodyInstance.transform.localRotation = Quaternion.identity;
        bodyInstance.transform.localScale = Vector3.one;

        Animator bodyAnimator = bodyInstance.GetComponentInChildren<Animator>();
        if (bodyAnimator != null)
        {
            bodyAnimator.runtimeAnimatorController = controller;
            bodyAnimator.applyRootMotion = false;
            // The hunter is very often just out of view; a culled animator freezes the skeleton the
            // eyes and the face light hang from.
            bodyAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        else
        {
            Debug.LogWarning("HunterBodyBuilder: the instantiated body has no Animator.", bodyInstance);
        }

        SerializedObject so = new SerializedObject(aiFollower);
        so.FindProperty("animator").objectReferenceValue = bodyAnimator;
        so.FindProperty("eyeHeight").floatValue = eyeHeight;
        // Humanoid: the body faces the Animator's own +Z, so there is nothing to measure - unlike the
        // Generic zombie rig, whose node could face any which way inside its own transform.
        so.FindProperty("modelFacingYaw").floatValue = 0f;
        so.ApplyModifiedProperties();

        Debug.Log($"HunterBodyBuilder: idle={idleSource}, walk={walkSource}, run={runSource} " +
            $"(walkThreshold={walkThreshold:F2}, runThreshold={runThreshold:F2}), eyeHeight={eyeHeight:F2} m.", bodyInstance);

        Undo.CollapseUndoOperations(undoGroup);

        Selection.activeGameObject = bodyInstance;
        SceneView.lastActiveSceneView?.FrameSelected();
        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log("Hunter body built. Save the scene (Ctrl+S) to keep it.", bodyInstance);
    }

    // ---------------------------------------------------------------- clips

    /// <summary>
    /// A custom clip from ClipFolder if one matches `keyword`, else the named clip loaded from
    /// `packFbxPath`. The pack fallback is only ever read from, except for the defensive loop-time fix
    /// in <see cref="EnsurePackClipLoops"/> (expected to be a no-op - both pack clips already loop).
    /// </summary>
    private static void ResolveClip(string keyword, string packFbxPath, string packClipName, out AnimationClip clip, out string source)
    {
        string customFbx = FindClipFbx(keyword);
        if (customFbx != null)
        {
            PrepareClipFbx(customFbx);
            clip = LoadClip(customFbx, null);
            if (clip != null)
            {
                source = Path.GetFileNameWithoutExtension(customFbx);
                return;
            }
        }

        clip = EnsurePackClipLoops(packFbxPath, packClipName);
        source = packClipName;
    }

    /// <summary>Finds an FBX model file under ClipFolder whose file name contains `keyword` (case-insensitive). First match wins.</summary>
    private static string FindClipFbx(string keyword)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ClipFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase)) continue;
            if (!(AssetImporter.GetAtPath(path) is ModelImporter)) continue;

            string fileName = Path.GetFileNameWithoutExtension(path);
            if (fileName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return path;
        }
        return null;
    }

    /// <summary>
    /// Sets a Mixamo clip FBX's importer to Humanoid/CreateFromThisModel with looping, root-locked clips,
    /// as Decision 2 of the plan requires - but only touches the importer, and only reimports, if
    /// something actually differs, so a rebuild does not thrash a file the user already prepared.
    /// </summary>
    private static void PrepareClipFbx(string path)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) return;

        bool dirty = false;
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            dirty = true;
        }
        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            dirty = true;
        }
        if (!importer.importAnimation)
        {
            importer.importAnimation = true;
            dirty = true;
        }

        ModelImporterClipAnimation[] current = importer.clipAnimations;
        ModelImporterClipAnimation[] baseline = current != null && current.Length > 0 ? current : importer.defaultClipAnimations;
        ModelImporterClipAnimation[] configured = new ModelImporterClipAnimation[baseline.Length];
        bool clipsDiffer = current == null || current.Length != baseline.Length;

        for (int i = 0; i < baseline.Length; i++)
        {
            ModelImporterClipAnimation clip = baseline[i];
            if (!clip.loopTime || !clip.lockRootRotation || !clip.lockRootHeightY || !clip.lockRootPositionXZ
                || !clip.keepOriginalOrientation || !clip.keepOriginalPositionY || !clip.keepOriginalPositionXZ)
            {
                clipsDiffer = true;
            }
            clip.loopTime = true;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;
            configured[i] = clip;
        }

        if (clipsDiffer)
        {
            importer.clipAnimations = configured;
            dirty = true;
        }

        if (dirty)
        {
            importer.SaveAndReimport();
        }
    }

    /// <summary>
    /// Loads `packClipName` from `packFbxPath` and, only if its loopTime is somehow false, sets it true
    /// via the importer and reimports - the one write this builder is allowed to make inside the pack
    /// folder. Both pack clips already loop, so in practice this never touches the importer.
    /// </summary>
    private static AnimationClip EnsurePackClipLoops(string packFbxPath, string packClipName)
    {
        AnimationClip clip = LoadClip(packFbxPath, packClipName);
        if (clip == null) return null;
        if (AnimationUtility.GetAnimationClipSettings(clip).loopTime) return clip;

        ModelImporter importer = AssetImporter.GetAtPath(packFbxPath) as ModelImporter;
        if (importer == null) return clip;

        ModelImporterClipAnimation[] current = importer.clipAnimations;
        ModelImporterClipAnimation[] baseline = current != null && current.Length > 0 ? current : importer.defaultClipAnimations;
        ModelImporterClipAnimation[] configured = new ModelImporterClipAnimation[baseline.Length];
        for (int i = 0; i < baseline.Length; i++)
        {
            ModelImporterClipAnimation c = baseline[i];
            if (c.name == packClipName) c.loopTime = true;
            configured[i] = c;
        }
        importer.clipAnimations = configured;
        importer.SaveAndReimport();

        // The clip reference from before SaveAndReimport is stale.
        return LoadClip(packFbxPath, packClipName);
    }

    /// <summary>First non-preview AnimationClip in the model's asset representations, preferring one named `preferredName` if given.</summary>
    private static AnimationClip LoadClip(string path, string preferredName)
    {
        AnimationClip fallback = null;
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
        {
            if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;
            if (preferredName != null && clip.name == preferredName) return clip;
            if (fallback == null) fallback = clip;
        }
        return fallback;
    }

    private static float GetClipSpeed(AnimationClip clip, float fallback)
    {
        if (clip == null) return fallback;
        float magnitude = clip.averageSpeed.magnitude;
        return magnitude < 0.05f ? fallback : magnitude;
    }

    // ---------------------------------------------------------------- controller

    /// <summary>
    /// Rebuilt every run - unlike Adam's own assets, this is generated data, so a stale reference to the
    /// deleted zombie clips can never survive a rebuild. One default state, "Locomotion", holding a 1D
    /// blend tree on Speed with explicit (not automatic) thresholds; when the run child is really the
    /// walk clip sped up, its timeScale is set after AddChild so it plays back at the run threshold.
    /// </summary>
    private static AnimatorController BuildController(AnimationClip idleClip, AnimationClip walkClip, AnimationClip runClip,
        bool runIsSpedUpWalk, float walkThreshold, float runThreshold)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
        }

        EnsureFolder(AnimationFolder);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("MotionSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);

        AnimatorState locomotionState = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;

        if (idleClip != null) tree.AddChild(idleClip, 0f);
        if (walkClip != null) tree.AddChild(walkClip, walkThreshold);

        int runChildIndex = -1;
        if (runClip != null)
        {
            tree.AddChild(runClip, runThreshold);
            runChildIndex = tree.children.Length - 1;
        }

        if (runIsSpedUpWalk && runChildIndex >= 0 && walkThreshold > 0.0001f)
        {
            ChildMotion[] children = tree.children;
            children[runChildIndex].timeScale = runThreshold / walkThreshold;
            tree.children = children;
        }

        controller.layers[0].stateMachine.defaultState = locomotionState;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    // ---------------------------------------------------------------- measurement

    /// <summary>
    /// Instantiates Adam at the world origin, assigns the freshly built controller so Animator.Update(0f)
    /// poses him at the blend tree's idle (Speed defaults to 0), and reads back the Humanoid Head bone's
    /// world Y - which, at the origin with identity rotation/scale, is exactly the height AIFollower.eyeHeight
    /// needs once the body is parented under AI_Follower. Destroys the temporary instance before returning.
    /// </summary>
    private static float MeasureHeadHeight(GameObject prefabAsset, AnimatorController controller, out bool ok)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset);
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;

        ok = false;
        float headY = 0f;

        Animator animator = instance.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.runtimeAnimatorController = controller;
            animator.Rebind();
            animator.Update(0f);

            if (animator.isHuman)
            {
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null && head.position.y > 0.5f)
                {
                    headY = head.position.y;
                    ok = true;
                }
            }
        }

        UnityEngine.Object.DestroyImmediate(instance);
        return headY;
    }

    // ---------------------------------------------------------------- scene lookup

    /// <summary>
    /// The direct child of AI_Follower whose prefab source is PlayerRobot.prefab (matched by filename,
    /// not by a hardcoded GUID - the project has two copies of the prefab at different paths). Skips the
    /// NavMesh Link child. Returns null if there is none (e.g. the swap already ran).
    /// </summary>
    private static Transform FindRobotChild(Transform aiRoot)
    {
        for (int i = 0; i < aiRoot.childCount; i++)
        {
            Transform child = aiRoot.GetChild(i);
            if (child.GetComponent<NavMeshLink>() != null) continue;

            UnityEngine.Object source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
            if (source == null) continue;

            string assetPath = AssetDatabase.GetAssetPath(source);
            if (!string.IsNullOrEmpty(assetPath) && Path.GetFileName(assetPath) == PlayerRobotFileName)
            {
                return child;
            }
        }
        return null;
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}

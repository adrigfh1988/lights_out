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

    // F50: written into AIFollower alongside animator/eyeHeight/modelFacingYaw below. The scene's
    // AI_Follower is a pre-existing object with its own serialized values baked into the scene YAML
    // (acceleration 14, turnSpeed 540, animationBlendRate 10) - a new C# field default only applies to an
    // object that has never had that field serialized before, so decision 10's "mannequin" fix has to be
    // written the same way eyeHeight already is, or it never takes effect on this scene.
    private const float DefaultAcceleration = 6f;
    private const float DefaultSpeedRampRate = 14f;
    private const float DefaultAnimationBlendRate = 6f;
    private const float DefaultTurnSpeedMoving = 200f;
    private const float DefaultTurnSpeedStationary = 120f;

    // F50: suffix the mirrored copy of a turn take gets, so BuildController's 2D tree can always find
    // "<take>_Mirror" for the left-turn child next to the un-mirrored right-turn one.
    private const string MirrorSuffix = "_Mirror";

    // F50 decision 9: the clip's contact frame is meant to land right as GameOutcome.CaptureSequence's
    // blackout starts. reachContactSeconds after capture, reachContactNormalizedTime through the clip -
    // the Reach state's playback speed is scaled to land there; the clip itself is never trimmed.
    private const float ReachContactSeconds = 0.9f;
    private const float ReachContactNormalizedTime = 0.55f;
    private const float PoseCrossfadeSeconds = 0.25f;

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

        ResolveClip("idle", true, PackIdlePath, PackIdleClipName, out AnimationClip idleClip, out string idleSource);
        ResolveClip("walk", true, PackWalkPath, PackWalkClipName, out AnimationClip walkClip, out string walkSource);

        string runFbx = FindClipFbx("run");
        AnimationClip runClipFound = null;
        if (runFbx != null)
        {
            PrepareClipFbx(runFbx, true);
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

        // F50: the new optional keywords. Each degrades to "missing" - no state, Pose maps to Idle - when
        // no clip resolves, exactly like the pre-existing idle/walk/run keywords did before this feature.
        AnimationClip turnClip = ResolveTurnClip(out AnimationClip turnMirroredClip, out string turnSource);
        AnimationClip lookClip = ResolveOptionalClip("look", true, out string lookSource);
        AnimationClip lurkClip = ResolveOptionalClip("lurk", true, out string lurkSource);
        AnimationClip stunClip = ResolveOptionalClip("stun", true, out string stunSource);
        AnimationClip reachClip = ResolveOptionalClip("reach", false, out string reachSource);

        // ---------------------------------------------------------------- controller (rebuilt every run)

        AnimatorController controller = BuildController(idleClip, walkClip, runClipForTree, runIsSpedUpWalk,
            walkThreshold, runThreshold, turnClip, turnMirroredClip, lookClip, lurkClip, stunClip, reachClip);

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

        HunterGaze gaze = Undo.AddComponent<HunterGaze>(bodyInstance);
        SerializedObject gazeSo = new SerializedObject(gaze);
        gazeSo.FindProperty("follower").objectReferenceValue = aiFollower;
        gazeSo.ApplyModifiedProperties();

        SerializedObject so = new SerializedObject(aiFollower);
        so.FindProperty("animator").objectReferenceValue = bodyAnimator;
        so.FindProperty("eyeHeight").floatValue = eyeHeight;
        // Humanoid: the body faces the Animator's own +Z, so there is nothing to measure - unlike the
        // Generic zombie rig, whose node could face any which way inside its own transform.
        so.FindProperty("modelFacingYaw").floatValue = 0f;
        // F50 decision 10: the scene's AI_Follower already has its own serialized acceleration/turnSpeed/
        // animationBlendRate values baked in from before this feature, so the "mannequin" fix has to be
        // written here, the same way eyeHeight already is, or a new C# field default never takes effect.
        so.FindProperty("acceleration").floatValue = DefaultAcceleration;
        so.FindProperty("speedRampRate").floatValue = DefaultSpeedRampRate;
        so.FindProperty("animationBlendRate").floatValue = DefaultAnimationBlendRate;
        so.FindProperty("turnSpeedMoving").floatValue = DefaultTurnSpeedMoving;
        so.FindProperty("turnSpeedStationary").floatValue = DefaultTurnSpeedStationary;
        // F50 decision 8: true (the body keeps spinning during a search dwell) whenever no look clip
        // resolved - it is the only way left to show a sweep at all without one.
        so.FindProperty("bodySweepsDuringSearch").boolValue = lookClip == null;
        so.ApplyModifiedProperties();

        Debug.Log($"HunterBodyBuilder: idle={idleSource}, walk={walkSource}, run={runSource} " +
            $"(walkThreshold={walkThreshold:F2}, runThreshold={runThreshold:F2}), turn={turnSource}, " +
            $"look={lookSource}, lurk={lurkSource}, stun={stunSource}, reach={reachSource}, " +
            $"eyeHeight={eyeHeight:F2} m, bodySweepsDuringSearch={(lookClip == null)}.", bodyInstance);

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
    private static void ResolveClip(string keyword, bool loop, string packFbxPath, string packClipName, out AnimationClip clip, out string source)
    {
        string customFbx = FindClipFbx(keyword);
        if (customFbx != null)
        {
            PrepareClipFbx(customFbx, loop);
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

    /// <summary>
    /// F50: a keyword with no pack fallback - look/lurk/stun/reach. Missing (no file, or no non-preview
    /// clip in it) degrades to null, exactly like a missing pack-backed keyword would if the pack itself
    /// were incomplete: the caller maps that to Pose.Idle and the builder skips the state entirely.
    /// </summary>
    private static AnimationClip ResolveOptionalClip(string keyword, bool loop, out string source)
    {
        string fbxPath = FindClipFbx(keyword);
        if (fbxPath == null)
        {
            source = "missing";
            return null;
        }

        PrepareClipFbx(fbxPath, loop);
        AnimationClip clip = LoadClip(fbxPath, null);
        if (clip == null)
        {
            source = "missing (no clip found in file)";
            return null;
        }

        source = Path.GetFileNameWithoutExtension(fbxPath);
        return clip;
    }

    /// <summary>
    /// F50 decision 7: the turn keyword additionally needs a mirrored copy of the same take for the left
    /// turn - PrepareTurnClipFbx writes both a normal and a "&lt;take&gt;_Mirror" entry into the importer,
    /// and this reads them back explicitly by name rather than trusting LoadClip's "first non-preview
    /// clip" fallback, which has no reason to prefer the un-mirrored one.
    /// </summary>
    private static AnimationClip ResolveTurnClip(out AnimationClip mirroredClip, out string source)
    {
        mirroredClip = null;

        string fbxPath = FindClipFbx("turn");
        if (fbxPath == null)
        {
            source = "missing";
            return null;
        }

        PrepareTurnClipFbx(fbxPath);

        AnimationClip original = null;
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath))
        {
            if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;

            if (clip.name.EndsWith(MirrorSuffix, StringComparison.Ordinal))
            {
                if (mirroredClip == null) mirroredClip = clip;
            }
            else if (original == null)
            {
                original = clip;
            }
        }

        if (original == null)
        {
            source = "missing (no clip found in file)";
            mirroredClip = null;
            return null;
        }

        source = Path.GetFileNameWithoutExtension(fbxPath);
        return original;
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
    /// Sets a Mixamo clip FBX's importer to Humanoid/CreateFromThisModel with root-locked clips, as
    /// Decision 2 of the plan requires, looping according to `loop` (F50 decision 4: everything loops
    /// except the reach clip - without this it would restart under the capture blackout every cycle,
    /// until Release() moves the hunter on from Captured). Only touches the importer, and only reimports,
    /// if something actually differs, so a rebuild does not thrash a file the user already prepared.
    /// </summary>
    private static void PrepareClipFbx(string path, bool loop)
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
            if (clip.loopTime != loop || !clip.lockRootRotation || !clip.lockRootHeightY || !clip.lockRootPositionXZ
                || !clip.keepOriginalOrientation || !clip.keepOriginalPositionY || !clip.keepOriginalPositionXZ)
            {
                clipsDiffer = true;
            }
            clip.loopTime = loop;
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
    /// F50 decision 7: like PrepareClipFbx, but writes two entries per take - the original, and a
    /// "&lt;take&gt;_Mirror" copy with `mirror = true` for the left turn. Always re-derives from
    /// `importer.defaultClipAnimations` (never from the importer's current, possibly-already-mirrored
    /// clipAnimations), so a rebuild can never compound into "..._Mirror_Mirror" entries.
    /// </summary>
    private static void PrepareTurnClipFbx(string path)
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

        ModelImporterClipAnimation[] existing = importer.clipAnimations;
        ModelImporterClipAnimation[] baseline = importer.defaultClipAnimations;

        ModelImporterClipAnimation[] configured = new ModelImporterClipAnimation[baseline.Length * 2];
        for (int i = 0; i < baseline.Length; i++)
        {
            configured[i] = CloneWithLocks(baseline[i], false);
            ModelImporterClipAnimation mirrored = CloneWithLocks(baseline[i], true);
            mirrored.name = baseline[i].name + MirrorSuffix;
            configured[baseline.Length + i] = mirrored;
        }

        bool clipsDiffer = existing == null || existing.Length != configured.Length;
        if (!clipsDiffer)
        {
            for (int i = 0; i < configured.Length; i++)
            {
                if (existing[i].name != configured[i].name || existing[i].mirror != configured[i].mirror
                    || existing[i].loopTime != configured[i].loopTime)
                {
                    clipsDiffer = true;
                    break;
                }
            }
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
    /// A fresh clip-animation entry copying `source`'s take/frame range, with the standard loop + root
    /// locks applied and mirrored if requested. Always a new object rather than a mutated `source` -
    /// ModelImporterClipAnimation is a reference type, and PrepareTurnClipFbx needs the original and the
    /// mirrored copy to end up as two independent entries, not two variables aliasing one.
    /// </summary>
    private static ModelImporterClipAnimation CloneWithLocks(ModelImporterClipAnimation source, bool mirror)
    {
        ModelImporterClipAnimation clip = new ModelImporterClipAnimation
        {
            name = source.name,
            takeName = source.takeName,
            firstFrame = source.firstFrame,
            lastFrame = source.lastFrame,
            mirror = mirror
        };
        clip.loopTime = true;
        clip.lockRootRotation = true;
        clip.lockRootHeightY = true;
        clip.lockRootPositionXZ = true;
        clip.keepOriginalOrientation = true;
        clip.keepOriginalPositionY = true;
        clip.keepOriginalPositionXZ = true;
        return clip;
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
    /// Hunter.controller is generated data, not authored content: this method deletes and recreates it
    /// from scratch on every run of LIGHTS OUT &gt; Build Hunter Body, wiring whatever clips resolved this
    /// run into a fixed shape - a Locomotion blend tree (1D on Speed, or 2D on Speed/Turn once a turn clip
    /// exists), one state each for LookAround/Lurk/Stunned when their clip resolved, and a Reach one-shot
    /// from Any State when a reach clip resolved. Unlike Adam's own imported assets, nothing here is meant
    /// to survive being hand-edited in the Animator window - a state added, a transition retimed, a
    /// parameter renamed by hand is silently wiped the next time this runs, same as it already was before
    /// F50 for the single-state version. Treat the asset itself as disposable; this method is the only
    /// source of truth for its contents.
    /// </summary>
    private static AnimatorController BuildController(
        AnimationClip idleClip, AnimationClip walkClip, AnimationClip runClip, bool runIsSpedUpWalk,
        float walkThreshold, float runThreshold,
        AnimationClip turnClip, AnimationClip turnMirroredClip,
        AnimationClip lookClip, AnimationClip lurkClip, AnimationClip stunClip, AnimationClip reachClip)
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
        // F50: always added, whether or not any of the states below end up existing - AIFollower's own
        // Start() guard is what makes writing to these safe on a controller that lacks them (the Timmy
        // fallback), not their absence here.
        controller.AddParameter("Pose", AnimatorControllerParameterType.Int);
        controller.AddParameter("Turn", AnimatorControllerParameterType.Float);
        controller.AddParameter("Reach", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine root = controller.layers[0].stateMachine;

        AnimatorState locomotionState = BuildLocomotionState(controller, idleClip, walkClip, runClip,
            runIsSpedUpWalk, walkThreshold, runThreshold, turnClip, turnMirroredClip);
        root.defaultState = locomotionState;

        AddPoseState(root, locomotionState, "LookAround", lookClip, AIFollower.Pose.LookAround);
        AddPoseState(root, locomotionState, "Lurk", lurkClip, AIFollower.Pose.Lurk);
        AddPoseState(root, locomotionState, "Stunned", stunClip, AIFollower.Pose.Stunned);
        AddReachState(root, locomotionState, reachClip);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    /// <summary>
    /// The Locomotion blend tree. A 1D tree on Speed alone, as before F50, unless a turn clip (and its
    /// mirrored copy) resolved - then a 2D freeform-cartesian tree on (Speed, Turn), with the turn clip at
    /// (0, +1) and its mirror at (0, -1) for the opposite direction. Explicit thresholds throughout, not
    /// automatic - the run child's threshold is the actual clip speed (or, for a sped-up walk, the walk
    /// threshold's own timeScale multiple), not wherever AddChild happened to land it.
    /// </summary>
    private static AnimatorState BuildLocomotionState(AnimatorController controller,
        AnimationClip idleClip, AnimationClip walkClip, AnimationClip runClip, bool runIsSpedUpWalk,
        float walkThreshold, float runThreshold, AnimationClip turnClip, AnimationClip turnMirroredClip)
    {
        AnimatorState locomotionState = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);

        if (turnClip != null && turnMirroredClip != null)
        {
            tree.blendType = BlendTreeType.FreeformCartesian2D;
            tree.blendParameter = "Speed";
            tree.blendParameterY = "Turn";

            if (idleClip != null) tree.AddChild(idleClip, new Vector2(0f, 0f));
            if (walkClip != null) tree.AddChild(walkClip, new Vector2(walkThreshold, 0f));

            int runChildIndex = -1;
            if (runClip != null)
            {
                tree.AddChild(runClip, new Vector2(runThreshold, 0f));
                runChildIndex = tree.children.Length - 1;
            }

            tree.AddChild(turnClip, new Vector2(0f, 1f));
            tree.AddChild(turnMirroredClip, new Vector2(0f, -1f));

            if (runIsSpedUpWalk && runChildIndex >= 0 && walkThreshold > 0.0001f)
            {
                ChildMotion[] children = tree.children;
                children[runChildIndex].timeScale = runThreshold / walkThreshold;
                tree.children = children;
            }
        }
        else
        {
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
        }

        return locomotionState;
    }

    /// <summary>
    /// F50 decision 2/11: a state that exists only when its clip resolved, entered/exited purely on Pose
    /// equality - a 0.25s crossfade each way, no exit time, so the transition starts the instant
    /// AIFollower's UpdateAnimator writes the new Pose rather than waiting for Locomotion to loop round.
    /// </summary>
    private static void AddPoseState(AnimatorStateMachine root, AnimatorState locomotionState, string stateName,
        AnimationClip clip, AIFollower.Pose pose)
    {
        if (clip == null) return;

        AnimatorState state = root.AddState(stateName);
        state.motion = clip;

        AnimatorStateTransition toState = locomotionState.AddTransition(state);
        toState.hasExitTime = false;
        toState.duration = PoseCrossfadeSeconds;
        toState.AddCondition(AnimatorConditionMode.Equals, (float)(int)pose, "Pose");

        AnimatorStateTransition toLocomotion = state.AddTransition(locomotionState);
        toLocomotion.hasExitTime = false;
        toLocomotion.duration = PoseCrossfadeSeconds;
        toLocomotion.AddCondition(AnimatorConditionMode.NotEqual, (float)(int)pose, "Pose");
    }

    /// <summary>
    /// F50 decision 9: only added when a reach clip resolved (the Reach parameter itself always exists -
    /// see BuildController - so a missing clip just means the trigger fires and nothing plays). Entered
    /// from Any State on the Reach trigger, canTransitionToSelf false so the trigger re-firing mid-clip
    /// (it does not, AIFollower only sets it once in EnterCaptured, but Any State transitions are shared
    /// machinery) can never restart it. Exits to Locomotion on exit time 1.0, not "no exit": Release()
    /// (Second Wind) can take the hunter from Captured back to Wander mid-reach, and without this exit the
    /// animator would sit on the clip's last frame for the whole stun that follows.
    /// </summary>
    private static void AddReachState(AnimatorStateMachine root, AnimatorState locomotionState, AnimationClip reachClip)
    {
        if (reachClip == null) return;

        AnimatorState state = root.AddState("Reach");
        state.motion = reachClip;
        if (reachClip.length > 0.0001f)
        {
            state.speed = (ReachContactNormalizedTime * reachClip.length) / ReachContactSeconds;
        }

        AnimatorStateTransition fromAny = root.AddAnyStateTransition(state);
        fromAny.hasExitTime = false;
        fromAny.duration = 0f;
        fromAny.canTransitionToSelf = false;
        fromAny.AddCondition(AnimatorConditionMode.If, 0f, "Reach");

        AnimatorStateTransition toLocomotion = state.AddTransition(locomotionState);
        toLocomotion.hasExitTime = true;
        toLocomotion.exitTime = 1.0f;
        toLocomotion.duration = PoseCrossfadeSeconds;
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

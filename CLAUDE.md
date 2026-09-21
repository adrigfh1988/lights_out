# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**LIGHTS OUT** — a first-person horror maze game. Unity 6 (editor `6000.6.2f1`), URP. It started life as the "Unity Essentials" / Get Started tutorial (a third-person robot collecting stars) and the tutorial assets are still in the project, but the game is now: you are dropped into a dark procedural maze with a flashlight, you collect 5 stars while something hunts you, and then you have 60 seconds to find the escape hatch.

Not a git repository. There is no test suite and no assembly definitions under `Assets/`, so all game code compiles into `Assembly-CSharp`.

## Commands

Development happens in the Unity Editor (Rider / Visual Studio / VS Code attach via `.vscode/launch.json`). The generated `*.csproj`, `*.sln`, `Library/`, `Temp/`, `obj/` and `Logs/` are Unity-generated — do not edit them.

Batch-mode compile check / build:

```
"C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe" -batchmode -quit -projectPath "D:\Unity\essentials\My project" -logFile -
```

Compile errors from the running Editor also land in `Logs/Editor.log` — `grep "error CS" Logs/Editor.log` is the fastest way to check the last compile without launching a second Unity instance (one is usually already running and holds the project lock).

Only scene in build settings: `Assets/Scenes/GetStarted_Scene.unity`. No tests exist; if added, run with `-runTests -testPlatform EditMode|PlayMode` (and `-testFilter <name>`).

## The one thing to understand first

**Almost nothing is authored in the scene.** `MazeGenerator.Awake` (`[DefaultExecutionOrder(-100)]`) builds the entire game at runtime: walls, floor, ceiling, the NavMesh bake, the stars, the first-person rig, the flashlight, and every director component, each `AddComponent`-ed onto the `MazeGenerator` GameObject and wired with a `Configure`/`Bind` call. The only authored pieces left are `GameManager.winPanel` and the tutorial's `Remaining_Collectibles_UI.prefab`.

So: **do not look for Inspector wiring or prefabs to explain behaviour, and do not add setup to the scene.** Read `MazeGenerator.SetUpAtmosphere` (near the end of the file) — that method is the composition root. New systems get created and configured there.

Execution order matters and is set explicitly:

| Order | Component | Why |
|-------|-----------|-----|
| −100 | `MazeGenerator` | builds everything before anything else's `Awake` |
| −90 | `FirstPersonRig` | camera/flashlight exist before directors look for them |
| −85 | `TensionDirector` | its counter is created before the title screen, so it draws *underneath* it |
| −80 | `MainMenu` | freezes the clock (`Time.timeScale = 0`) until the player presses Start |
| −50 | `HorrorAtmosphere` | ambient/fog/skybox blackout after the world exists |

## Run lifecycle

1. `MainMenu` opens with `Time.timeScale = 0`; `StartGame` unfreezes, sets `GameFlow.RunStartTime` and `GameFlow.IsRunActive = true`.
2. `Pickup.OnTriggerEnter` (Player tag) → static `Pickup.OnCoinCollected` → `GameManager` decrements and raises `GameManager.ProgressChanged(collected, total)`.
3. Last star → `GameManager.AllStarsCollected`. **If anything is subscribed, that listener takes over**; only with no listener does `GameManager.WinGame` fall back to the tutorial's win panel. `MazeEscape` and `TensionDirector` both subscribe.
4. `MazeEscape` opens a glowing hatch at least 12 m away, starts a 60 s countdown, and builds a landing platform underneath it.
5. Ending, always through `GameOutcome`:
   - reach the hatch → `MazeEscape.DropThroughHatch()` then `GameOutcome.Win()`
   - timer expires → `GameOutcome.Lose(LoseReason.OutOfTime)`
   - hunter catches you → `AIFollower.PlayerCaught` → `GameOutcome.Lose(LoseReason.Caught)` → `CaptureSequence` coroutine (camera eases onto its eyes, torch flickers out, blackout) → end screen.

`GameOutcome` is single-shot: `IsOver` / `IsEnding` guard every entry point, so a hatch drop landing mid-capture is ignored.

## Systems

- **World** — `MazeGenerator` (~790 lines): seeded maze, runtime `NavMeshSurface` bake, star placement, actor placement, and the composition root described above. `UsedSeed` is the seed actually used (Inspector `seed`, a pending retry seed, or `Environment.TickCount`); it is logged and shown on the end screen.
- **Player** — `FirstPersonRig` builds the camera and head-mounted `Flashlight` (F key, flicker, `InputEnabled` so endings can take the torch away). `PlayerStealthState` derives `NoiseRadius` from actual `CharacterController` velocity and exposes `VisibilityMultiplier` (driven by the torch). Movement is still `ThirdPersonController` (Starter Assets, namespace `StarterAssets`) with `FirstPerson = true`; it owns `MovementLocked`, `LockCameraPosition` and `LookAt(worldPoint, turnFraction)`.
- **Hunter** — `AIFollower` (~830 lines): sight cone + LOS, hearing, last-known-position search sweep, patrol bias toward uncollected stars, wander points, `SetThreatLevel(progress, hunting)` escalation dial. States: `Chase`, `Wander`, `Search`, `Captured`. Events: `ChaseStateChanged(bool)`, `PlayerCaught`.
- **Dread layer** — `AIPresence` (occlusion-free footsteps/hum + emissive eyes), `HorrorAudioDirector` (distance heartbeat, sting, drone, panic track, `PlayCaptureSting`, `Silence`), `HorrorAtmosphere` (ambient/fog/skybox blackout).
- **Escalation** — `TensionDirector` is the single place where "the maze getting emptier makes things worse" is expressed: counter colour, audio intensity, AI threat level.
- **Flow / shell** — `GameFlow` (static: `SkipMenuOnLoad`, `PendingSeed`, `RunStartTime`, `IsRunActive`, `Restart(sameMaze, seed)`, `ReturnToMenu`, `Quit`), `PlayerLock` (static freeze + cursor helpers), `MainMenu`, `PauseMenu` (Esc / gamepad Start), `GameOutcome` (end screens with retry buttons), `RuntimeUi` (all UI is built in code — `CreatePanel`, `CreateText`, `CreateButton`, `Place`, `ResolveCanvas`, `EnsureEventSystem`).

## Gotchas that bite

- **Statics must survive a scene reload and must not survive Play mode.** `GameFlow` and `GameOutcome.IsOver` both have a `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset, because with *Enter Play Mode > Reload Domain* disabled a stale `SkipMenuOnLoad` would skip the title screen on the next run. Any new static run state needs the same treatment.
- **Every static-event subscriber unsubscribes** in `OnDestroy`/`OnDisable` (`GameManager`, `MazeEscape`, `TensionDirector`, `HorrorAudioDirector`, `GameOutcome`). Restart is a plain `SceneManager.LoadScene`, so a missed unsubscribe leaks across reloads. Keep the pattern.
- **`Time.time` is the run clock** and stands still at `timeScale = 0`, which is why run time is net of menu and pause. Don't swap it for `Time.unscaledTime`.
- **The cursor fight.** `StarterAssetsInputs` re-locks the cursor on every focus change, so anything showing a menu calls `PlayerLock.SetCursorFree(true)` *every frame*, not once. Freezing the player is three flags (`MovementLocked`, `LockCameraPosition`, `cursorLocked`) — always go through `PlayerLock.Freeze`.
- **Capture needs sight, not just distance.** A bare radius test captures through a wall (two actors either side of a 0.5 m wall are ~1.5 m apart), and `Update` still ticks at `timeScale = 0` behind the title screen — hence the `GameFlow.IsRunActive && _hasSight` gate in `AIFollower`.
- **Reflection is load-bearing in two places.** `RespawnPlayer` zeroes `ThirdPersonController`'s private `_verticalVelocity`, and `UpdateCollectibleCount` resolves `Type.GetType("Pickup")`. Renaming either silently breaks them at runtime with no compile error.
- **`UpdateCollectibleCount` recounts `Pickup` objects every frame** via `FindObjectsByType` instead of listening to `ProgressChanged`. It is tutorial-era; `TensionDirector`'s counter is the real HUD.
- **`GameManager` counts stars at `Start`.** Anything spawning or removing stars after that desyncs the count.
- **Input System only** (`activeInputHandler: 1`). Keep the `#if ENABLE_INPUT_SYSTEM` guards in `ThirdPersonController` / `StarterAssetsInputs`. Note `Flashlight` and `PauseMenu` read `Keyboard.current` / `Gamepad.current` directly and bypass the action map, so those bindings are not remappable yet.
- Keep `.meta` files alongside assets when moving or renaming; scenes and prefabs are text-serialized YAML, so hand edits must preserve GUID references.

## Planning docs

`plannings/` sits outside `Assets/`, so Unity never imports it:

- `full-game-feature-plan.md` — the road to a full game, Tiers 1–5 (F1–F21).
- `tier1-core-loop-plan.md` — the detailed spec for capture, restart, pause and end screens.

**As of 20 Sep 2026 Tier 1 is being implemented.** `GameFlow`, `PlayerLock`, `GameOutcome`, `PauseMenu` and the `Captured` state all exist. If something in this file disagrees with the code, the code is newer — check `MazeGenerator.SetUpAtmosphere` first.

## Non-game assets

- `Assets/Tutorials/` and `Assets/SourceFiles/Models|Animation|Materials|...` are tutorial/content assets (`com.unity.learn.iet-framework`; `Assets/Tutorials/Settings/Editor/TutorialCallbacks.cs` and `Styles/ApplyCustomThemeHelper.cs` are editor-side helpers).
- Render settings: URP with `PC_RPAsset`/`Mobile_RPAsset` in `Assets/SourceFiles/Settings/`.
- Tutorial leftovers still in the build: `GameManager.winPanel`, `Remaining_Collectibles_UI.prefab`, `MotionAudioController`, `RespawnPlayer`, and the third-person `PlayerRobot.prefab`s.

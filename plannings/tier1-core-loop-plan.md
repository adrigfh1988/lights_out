# Tier 1 — Core Loop: Capture, Restart, Pause, End Screens

Planning document only. This folder sits outside `Assets/`, so Unity does not import it and it has no effect on the game. Implementation plan for F1–F4 of `full-game-feature-plan.md`, written for someone who has not seen the codebase before. Every claim below about existing code was checked against the source; line numbers refer to the files as they are today.

## Context

LIGHTS OUT is a first-person horror maze. Everything is built at runtime by `MazeGenerator.Awake` (`Assets/SourceFiles/Scripts/MazeGenerator.cs`, `[DefaultExecutionOrder(-100)]`): the maze, the NavMesh, the stars, the player rig, and then — in `SetUpAtmosphere()` (lines 677–709) — the horror components are `AddComponent`ed onto the `MazeGenerator` GameObject and wired by hand (`HorrorAudioDirector.Bind`, `TensionDirector.Configure`, `MazeEscape.Configure`, `MainMenu.Configure`). No UI is authored in the scene except the tutorial's `WinPanel` ("You Win!", inactive, referenced by `GameManager.winPanel`). All other UI is built in code through `RuntimeUi` (`RuntimeUi.cs`).

The loop today: collect 5 stars → `GameManager.AllStarsCollected` → `MazeEscape.BeginEscape` opens a hatch and a 60 s timer → reach it: `MazeEscape.Win()`; time out: `MazeEscape.Lose()`.

### The two gaps this plan closes

1. **The hunter cannot catch the player.** `AIFollower` (`AIFollower.cs`) has three states — `enum State { Chase, Wander, Search }` (line 81) — and no contact logic. In `TickChase` (line 450) it sets `_agent.stoppingDistance = stoppingDistance` (1.5 m, line 20) and paths to the player's sampled position, so it walks up, parks ~1.5 m from the player's centre, and stands there. Nothing anywhere calls `MazeEscape.Lose()` except the timer (line 106).
2. **There is no way to play again.** No `SceneManager.LoadScene` exists in the project. `MazeEscape.Win()`/`Lose()` (lines 232–266) lock movement and show a panel; the only way out is stopping Play mode. There is also no pause.

### Facts that shape the design (verified)

- **Scene reload is safe.** No `DontDestroyOnLoad`, no mutable statics anywhere in `Assets/SourceFiles/Scripts`. Every subscriber to the three static events (`Pickup.OnCoinCollected`, `GameManager.AllStarsCollected`, `GameManager.ProgressChanged`) unsubscribes: `GameManager.OnDestroy` (line 35), `MazeEscape.OnDisable` (63), `TensionDirector.OnDisable` (43). `HorrorAudioDirector` subscribes to the instance event `AIFollower.ChaseStateChanged` and unsubscribes in `OnDisable` (176). `MazeGenerator.OnDestroy` (138) only destroys its runtime material copies. `MainMenu.OnDestroy` (71) restores `Time.timeScale = 1`.
  - One leak to fix while here: `MainMenu.GenerateCover()` creates a `Texture2D` (`_generatedCover`, line 204) every load and never destroys it.
- **`MainMenu` gates every load.** `MainMenu.Awake` sets `Time.timeScale = 0` (line 52); `Configure` and `Start` freeze the player; `Update` (77) holds the cursor free every frame while `_menuOpen`; `StartGame()` (95) is the only thing that unfreezes, relocks the cursor and restores `timeScale`. A plain reload therefore shows the title screen on every retry unless `StartGame` is reached automatically.
- **Freezing the player is three flags, not one.** `MainMenu.FreezePlayer` (108) sets `ThirdPersonController.MovementLocked` (movement input ignored, gravity still runs), `ThirdPersonController.LockCameraPosition` (mouse look is not `deltaTime`-scaled, so `timeScale = 0` does not stop it) and `StarterAssetsInputs.cursorLocked` (its `OnApplicationFocus` re-applies this on every focus change, line 81 of `StarterAssetsInputs.cs`, so setting `Cursor.lockState` alone loses on alt-tab). `MainMenu.FreezePlayer` and `MainMenu.QuitGame` are both private.
- **The camera cannot be pointed by writing its transform.** `ThirdPersonController.CameraRotation()` (216) rewrites `CinemachineCameraTarget.transform.rotation` from the private `_cinemachineTargetYaw/_cinemachineTargetPitch` every `LateUpdate`. In first person the scene camera is a child of `PlayerCameraRoot` (`FirstPersonRig.Awake`, line 76), so it inherits that rotation. The only public entry is `ResetCameraRotation(float yaw)` (459), which zeroes pitch and logs. Pitch is clamped to ±`pitchClamp` = 80° (`FirstPersonRig.ConfigureController`, 97).
- **`Update` runs at `timeScale = 0`.** `AIFollower.Update`, `MazeEscape.Update` and everything else still tick behind the title screen with `deltaTime = 0`. Any new check that is not `deltaTime`-driven (a distance test, a key press) needs an explicit "run is active" gate.
- **Input.** `StarterAssets.inputactions` has one `Player` map with Move/Look/Jump/Sprint only — no Pause/Cancel action. `Flashlight.Update` (80) already reads `Keyboard.current.fKey.wasPressedThisFrame` directly for the same reason; do the same for Escape.
- **Scene wiring.** The scene's `AI_Follower` object (`GetStarted_Scene.unity` line 1512) serialises only `target` and `moveSpeed` for `AIFollower`; every other field uses the code default, so changing a default in `AIFollower.cs` takes effect without a scene edit. `MazeGenerator` in the scene: 11×11, `seed: 0`, `starCount: 5`. Player `CharacterController` radius 0.28 (`Prefabs/PlayerRobot.prefab`), agent radius 0.5. Corridors are `cellSize − wallThickness` = 2.5 m wide; walls 0.5 m.
- **Sight.** `AIFollower.CanSeeTarget()` (342) = range × `PlayerStealthState.VisibilityMultiplier`, 110° cone around `ModelForward()`, then a raycast that treats anything not under `target` as a blocker. Result cached in `_hasSight`, refreshed every `sightCheckInterval` (0.1 s).
- **Audio to silence on game over.** `HorrorAudioDirector.Silence()` (line 115) stops its four sources. `AIPresence` (hum loop + footsteps, `AIPresence.cs` 49–86) has no such method; `MazeEscape.EndTheHunt()` (212) disables the follower and stops the agent but the hum keeps looping.
- **Stats sources.** `GameManager.ProgressChanged(collected, total)` fires from `GameManager.Start` and after every pickup. `Time.time` does not advance while `timeScale = 0`, so a start timestamp taken in `StartGame` gives run time net of menu and pause. The seed is chosen at `MazeGenerator.Awake` line 119 (`usedSeed`) and only logged.

## Decisions and assumptions

These resolve the open questions from the feature plan. Change them here before implementing if the answer is different.

- **Capture is instant death.** No struggle/escape mechanic. Sequence is ~1.6 s long and unskippable.
- **Capture only happens while the hunter is chasing with sight.** A wandering hunter that walks into the player from behind in the dark does not kill; it has to have seen them (`_hasSight`) and be in `Chase`. This keeps the torch-off gamble meaningful and avoids deaths that read as unfair. If bump-kills are wanted later, it is a one-line change in the gate (§3).
- **Retry offers both.** End screens have "TRY THIS MAZE AGAIN" (same seed) and "NEW MAZE" (fresh seed), plus "MAIN MENU". The pause menu has Resume / Restart (same maze) / Main Menu / Quit.
- **Everything stays runtime-built** through `RuntimeUi`. No prefabs, no scene edits, no new Inspector references. Every new component is added from `MazeGenerator.SetUpAtmosphere` exactly like `MazeEscape` is, so the non-maze fallback (`escapeSequence`/`mainMenu` flags off, or no `MazeGenerator` in a scene) keeps working untouched.
- **The tutorial `WinPanel` is no longer used on the maze path.** `MazeEscape.Win()` stops calling `GameManager.WinGame()`; the new end screen replaces it. `GameManager.WinGame` stays as-is for the no-listener fallback in `HandleCoinCollected`.
- **Statics survive reloads on purpose** and are reset on Play start by `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`, because with *Enter Play Mode → Reload Domain* disabled a stale "skip menu" flag would otherwise bypass the title screen on the next press of Play.

## Architecture

Three private things are about to be needed from three places, so they move to shared owners first:

| Need | Today | Becomes |
|------|-------|---------|
| Win / lose with a reason, silence everything, show a screen | `MazeEscape.Win/Lose/EndTheHunt/ShowLosePanel`, private, timer-only | **`GameOutcome`** component (new): `Win()`, `Lose(LoseReason)`, single-shot, owns the end screens |
| Freeze/unfreeze the player, quit | `MainMenu.FreezePlayer`, `MainMenu.QuitGame`, private | **`PlayerLock`** static helper (new) |
| State that must survive a scene reload | nothing | **`GameFlow`** static class (new): skip-menu flag, pending seed, run timestamp, run-active flag, `Restart/ToMenu/Quit` |
| Pause | nothing | **`PauseMenu`** component (new) |

Dependency direction: `AIFollower` → raises an event. `GameOutcome` listens, runs the capture sequence, ends the run. `MazeEscape` calls `GameOutcome` instead of ending the run itself. `MainMenu`, `PauseMenu`, `GameOutcome` all use `PlayerLock` and `GameFlow`. Nothing new references `MazeGenerator` except through the existing `Configure` pattern.

## Files

New scripts go in `Assets/SourceFiles/Scripts/`. Unity generates the `.meta` for each on import; do not hand-write GUIDs. No assembly definitions exist, so everything lands in `Assembly-CSharp`.

### New

1. `GameFlow.cs` — static class, no MonoBehaviour.
2. `PlayerLock.cs` — static helper.
3. `GameOutcome.cs` — MonoBehaviour, added at runtime by `MazeGenerator`.
4. `PauseMenu.cs` — MonoBehaviour, added at runtime by `MazeGenerator`.

### Modified

5. `AIFollower.cs` — capture detection, `Captured` state, `PlayerCaught` event.
6. `ThirdPersonController.cs` — one public look setter.
7. `Flashlight.cs` — public `SetOn(bool)`, input gate.
8. `HorrorAudioDirector.cs` — `PlayCaptureSting()`.
9. `AIPresence.cs` — `Silence()`.
10. `MazeEscape.cs` — delegate outcomes to `GameOutcome`, add `Stop()`.
11. `MazeGenerator.cs` — expose the seed, honour a pending seed, add the two new components.
12. `MainMenu.cs` — use `PlayerLock`/`GameFlow`, skip-to-game path, texture cleanup, `IsOpen`.
13. `RuntimeUi.cs` — one small addition (a full-screen fade image helper) if not inlined in `GameOutcome`.

---

## 1. `GameFlow.cs` (new, static)

```
public static class GameFlow
{
    public static bool  SkipMenuOnLoad;     // set by Restart(); consumed by MainMenu.Start
    public static int   PendingSeed;        // 0 = none; consumed by MazeGenerator.Awake
    public static float RunStartTime;       // Time.time at MainMenu.StartGame
    public static bool  IsRunActive;        // true from StartGame until GameOutcome ends the run

    public static void Restart(bool sameMaze, int currentSeed);
    public static void ReturnToMenu();
    public static void Quit();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics();
}
```

- `Restart(sameMaze, currentSeed)`: `SkipMenuOnLoad = true; PendingSeed = sameMaze ? currentSeed : 0; IsRunActive = false; Time.timeScale = 1f; AudioListener.pause = false; SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);`. Restoring `timeScale` and `AudioListener.pause` here is deliberate: `Restart` can be called from the pause menu with both engaged, and `MainMenu.Awake` in the new scene sets `timeScale = 0` itself anyway.
- `ReturnToMenu()`: same but `SkipMenuOnLoad = false; PendingSeed = 0`.
- `Quit()`: the body of today's `MainMenu.QuitGame` (line 125: `EditorApplication.isPlaying = false` under `UNITY_EDITOR`, else `Application.Quit()`). Delete it from `MainMenu`.
- `ResetStatics()`: zero all four fields. Required — see *Decisions*.

## 2. `PlayerLock.cs` (new, static)

```
public static class PlayerLock
{
    public static void Freeze(Transform player, bool frozen);
    public static void SetCursorFree(bool free);
}
```

- `Freeze` is the body of `MainMenu.FreezePlayer` (108–123): `GetComponent<ThirdPersonController>()` → `MovementLocked = frozen; LockCameraPosition = frozen;` and `GetComponent<StarterAssetsInputs>()` → `cursorLocked = !frozen`. Null-safe on both, and on a null `player`.
- `SetCursorFree(true)` = `Cursor.lockState = None; Cursor.visible = true`; `false` = `Locked; false`. Callers that show a menu must call this **every frame** while the menu is up (the reason is documented at `MainMenu.Update` line 81: `StarterAssetsInputs` re-locks on focus change).
- Replace `MainMenu.FreezePlayer` with calls to this; keep `MainMenu` holding `_controller`/`_inputs`? No — it only needs the player `Transform` now. Simplify `Configure(Transform player)` to store the transform.

## 3. `AIFollower.cs` — capture detection

Add, in order of appearance:

- Serialized, under a new `[Header("Capture")]`:
  - `float captureRadius = 1.4f` — "Flat distance to the player's centre at which it has you. Must be larger than the Chase stopping distance or it never fires."
  - Change the default of the existing `stoppingDistance` (line 20) from `1.5f` to `0.9f`. Rationale to put in its tooltip: the agent decelerates to a stop at `remainingDistance <= stoppingDistance`; at 1.5 m it would park outside a 1.4 m capture radius forever. 0.9 m is still outside the two capsules (0.28 + 0.5). This changes nothing in the scene YAML (only `target`/`moveSpeed` are overridden there today).
  - **Enforce the invariant in code, not in two serialized fields.** The moment someone saves the scene, every `AIFollower` field gets baked into the YAML and a later default change is ignored. So in `TickChase` set `_agent.stoppingDistance = Mathf.Min(stoppingDistance, captureRadius - 0.3f)` — the agent always closes to inside the capture radius whatever the Inspector says, and the default change above is tuning rather than correctness.
- `enum State` gains `Captured`.
- `public event Action PlayerCaught;` — raised exactly once.
- Capture test, at the **end of `TickChase`** (after the movement/rotation code, ~line 472), guarded so it cannot fire behind the title screen or after the run is over:
  ```
  if (GameFlow.IsRunActive && _hasSight && FlatDirection(target.position - transform.position).sqrMagnitude <= captureRadius * captureRadius)
      EnterCaptured();
  ```
  Both gates are load-bearing:
  - `_hasSight` — a bare distance check captures through a wall: two actors hugging opposite faces of a 0.5 m wall are ~1.5 m apart centre-to-centre. `_hasSight` includes the line-of-sight raycast (`CanSeeTarget`, 369–393) and is refreshed every 0.1 s, which is fine at these speeds.
  - `GameFlow.IsRunActive` — `Update` ticks at `timeScale = 0`. The AI spawns at the farthest cell so this cannot trip on frame one today, but it must not depend on that.
- `EnterCaptured()`: `_state = State.Captured; _agent.isStopped = true; _agent.velocity = Vector3.zero; PlayerCaught?.Invoke();` — do **not** raise `ChaseStateChanged(false)`; the sting logic in `HorrorAudioDirector` only reacts to `true` and a "stopped chasing" here is misleading anyway.
- `UpdateStateMachine` (412): first line becomes `if (_state == State.Captured) { TickCaptured(); return; }` — before the `_hasSight` promotion, or a stale sight reading would flip it back to Chase. `TickCaptured()`: `UpdateRotation(FlatDirection(target.position - transform.position))` and nothing else; `UpdateAnimator` (called from `Update`, 257) already blends `Speed` to 0 from `_agent.velocity`.
- `EnterCaptured()` is the only entry into the state; do not add a public wrapper — nothing outside the follower should be able to put it there.
- `SetThreatLevel`, `SetWanderPoints`, hearing: unchanged. Hearing routes to `EnterSearch` (326) only when `_state != Chase`; add `&& _state != State.Captured` to that condition (line 324) so a noise during the death sequence cannot wake it.

Gizmo (765): draw a `captureRadius` wire disc at the feet, colour by `_state == Captured`. Optional, cheap, useful for tuning.

## 4. `ThirdPersonController.cs` — look setter

Add next to `ResetCameraRotation` (459):

```
/// Points the view at a world position. Only meaningful with LockCameraPosition = true,
/// otherwise the next mouse delta takes over again. Pitch is clamped like mouse look.
public void LookAt(Vector3 worldPoint, float turnFraction)
```

- Compute the yaw/pitch of `worldPoint - CinemachineCameraTarget.transform.position`; `_cinemachineTargetYaw = Mathf.LerpAngle(current, targetYaw, turnFraction)`, same for pitch with `Mathf.Clamp(…, BottomClamp, TopClamp)`. `CameraRotation()` in `LateUpdate` then applies it as normal, and in first person `Move()` (line 296) already slaves the body yaw to the camera every frame, so the body turns with it.
- Do not add a `Debug.Log` (unlike `ResetCameraRotation`); it is called every frame during the sequence.
- Keep inside the existing `#if ENABLE_INPUT_SYSTEM` conventions of the file: the method itself has no input dependency, so it lives outside the `#if`.

## 5. `Flashlight.cs`

- Extract the toggle: `public void SetOn(bool on) { IsOn = on; ApplyState(); }`; the F-key branch in `Update` (85) calls `SetOn(!IsOn)`.
- `public bool InputEnabled = true;` — the key is ignored while false. `GameOutcome` sets it false at the start of any ending and `PauseMenu` sets it false while paused (otherwise F toggles the torch under the pause scrim; `Update` runs at `timeScale = 0`).
- `ApplyState` already pushes `VisibilityMultiplier`/`FlashlightOn` into `PlayerStealthState`; nothing else changes.

## 6. `HorrorAudioDirector.cs`

- `public void PlayCaptureSting()`: same body as `HandleChaseStateChanged(true)` (line 212–223) but `volume = 1f`, `pitch = stingPitch * 0.8f`, and `SetScheduledEndTime(dspTime + 2.5f)`. Also stop `_tension` and `_drone` here (the bed and panic loop cut out under the sting; silence is the point).
- `Silence()` is unchanged and still called afterwards by `GameOutcome`.

## 7. `AIPresence.cs`

- `public void Silence()`: `_humSource.Stop(); _footstepSource.Stop(); enabled = false;`. Called from `GameOutcome.EndTheHunt`. Today the hum loops over the win/lose screen.

## 8. `GameOutcome.cs` (new component)

Added to the `MazeGenerator` object by `SetUpAtmosphere`. Owns run endings. Public surface:

```
public enum LoseReason { Caught, OutOfTime }

public class GameOutcome : MonoBehaviour
{
    public static bool IsOver { get; private set; }        // reset in OnDestroy (scene reload)
    public bool IsEnding { get; private set; }               // capture sequence in progress

    public void Configure(Transform player, AIFollower follower, HorrorAudioDirector audio, MazeEscape escape, Flashlight flashlight, int seed);
    public void Win();                       // called by MazeEscape
    public void Lose(LoseReason reason);     // called by MazeEscape (OutOfTime) and internally (Caught)
}
```

`follower`, `escape` and `flashlight` may each be null (`aiFollower` missing from the scene; `escapeSequence` off; `firstPerson` off). Every use is null-conditional: `_escape?.Stop()`, `_flashlight?.SetOn(false)`, and `Configure` only subscribes to `PlayerCaught` when `follower != null`.

Private state: `_collected`, `_total` (from `GameManager.ProgressChanged`, subscribed in `OnEnable`/`OnDisable` exactly like `TensionDirector` 37–47; because the component is added during `MazeGenerator.Awake` at order −100 it is enabled before `GameManager.Start` fires `(0, total)`), `_player`, `_follower`, `_audio`, `_escape`, `_flashlight`, `_seed`, `_hatchOpened` (set from `GameManager.AllStarsCollected`, used only for the stats line).

**Single-shot guard.** `Win()` and `Lose()` both start with `if (IsOver || IsEnding) return;` — capture after the hatch drop, or the timer expiring mid-capture, must be ignored. Set `IsEnding = true` immediately in `Lose(Caught)`; set `IsOver = true` in `Win()`, `Lose(OutOfTime)`, and at the end of the capture coroutine. `GameFlow.IsRunActive = false` in all three as well.

**Subscription to the hunter.** In `Configure`: `follower.PlayerCaught += HandleCaught` (and `-=` in `OnDisable`; null-safe). `HandleCaught()` → `Lose(LoseReason.Caught)`.

**`EndTheHunt()`** — move from `MazeEscape` (212–230) verbatim, plus `foreach (AIPresence p in FindObjectsByType<AIPresence>(…)) p.Silence();`. For `Caught` it must **not** disable the follower before the sequence has finished (the hunter needs to keep turning to face you) — so order is: sequence, then `EndTheHunt`.

**`Win()`** — the drop-teleport stays in `MazeEscape` (it is hatch geometry — see §10); `GameOutcome.Win()` is: guard → `IsOver = true; GameFlow.IsRunActive = false` → `EndTheHunt()` → `_escape?.Stop()` → `PlayerLock.Freeze(_player, true)` → `ShowEndScreen(win)`. The end screen appears on the same frame as the teleport and the player still falls onto the landing platform behind it: `Move()` applies gravity regardless of `MovementLocked` (comment at `ThirdPersonController` line 252), and the end screen never sets `timeScale = 0`.

**`Lose(OutOfTime)`** — guard → `IsOver = true` → `EndTheHunt()` → `_escape.Stop()` → freeze → `ShowEndScreen(lose, OutOfTime)`.

**`Lose(Caught)` — the capture sequence**, as a coroutine (`Time.timeScale` is 1 during play, so scaled `WaitForSeconds` is fine; do not use `Realtime` variants or the pause menu's `timeScale = 0` could never happen — it can't anyway because `PauseMenu` refuses while `IsEnding`):

| t (s) | Action |
|-------|--------|
| 0.0 | `IsEnding = true; GameFlow.IsRunActive = false; _escape.Stop();` — countdown freezes where it is. `PlayerLock.Freeze(_player, true)` (`MovementLocked` already zeroes walk/sprint input). `_flashlight?.InputEnabled = false`. `_audio.PlayCaptureSting()`. |
| 0.0 → 0.5 | Each frame: `controller.LookAt(hunterEyes, 1 - Mathf.Pow(0.001f, Time.deltaTime))` — an exponential ease that lands in ~0.5 s. `hunterEyes = follower.transform.position + Vector3.up * 1.5f` (the AI's `eyeHeight`; expose it as a public getter or just use the constant, it is the same value the eyes are built at in `AIPresence.BuildEyes`). |
| 0.5 → 0.9 | Flashlight: three hard flickers — `SetOn(false)` / `SetOn(true)` at 0.5, 0.62, 0.74, ending **off** at 0.9. With the torch off `VisibilityMultiplier` drops, which does not matter now (the AI is in `Captured` and no longer perceives). |
| 0.9 → 1.4 | Fade-to-black: a full-screen `Image` (black, alpha 0 → 1 over 0.5 s) created with `RuntimeUi.CreatePanel(canvas, "Blackout", clear)` — created *now*, not earlier, so it is the topmost sibling. |
| 1.4 | `EndTheHunt()` (silences everything; disables the follower — it has done its job). `IsOver = true`. |
| 1.6 | `ShowEndScreen(lose, Caught)` on top of the blackout. |

The blackout stays; the end screen scrim sits over it.

**`ShowEndScreen(bool won, LoseReason reason)`** — this *is* F4. Built with `RuntimeUi`, parented to `RuntimeUi.ResolveCanvas()`, created last so it draws over the star counter (`TensionDirector.BuildCounter`) and anything else:

- Scrim: `CreatePanel(canvas, "EndScreen", new Color(0,0,0,0.85f))`.
- Title, 96 pt bold, `Place(rect, (0.5,1), (0,-200), (1600,140))`:
  - Win: `"YOU GOT OUT"`, colour `MazeEscape.hatchColor`-like green `(0.25,1,0.7)`.
  - Caught: `"IT FOUND YOU"`, red `(0.9,0.15,0.1)`.
  - OutOfTime: `"OUT OF TIME"`, same red.
- Subtitle, 36 pt, grey `(0.75,0.75,0.78)`, at `(0,-300)`:
  - Win: `"The hatch closed behind you."`
  - Caught: `"It was faster in the dark."` / `"You never heard it coming."` (pick by whether `flashlight.IsOn` was true at capture — a small nudge toward the stealth mechanic).
  - OutOfTime: `"The hatch sealed. You were {n} stars in and {m} metres away."` — distance from `_escape.HatchPosition` (expose it, §10); metres is flat distance rounded.
- Stats line, 32 pt, `(0,-380)`, monospaced feel via `characterSpacing = 4`:
  `"STARS {collected} / {total}     TIME {m:ss}     SEED {seed}"` — time = `Time.time - GameFlow.RunStartTime` formatted like `MazeEscape.FormatTime`. Seed shown so it can be typed back in later (F13) and so bug reports carry it.
- Buttons, using `RuntimeUi.CreateButton` with the same size/colours as `MainMenu` (`(420,86)`, idle `(0.10,0.10,0.12,0.92)`, hover `(0.48,0.08,0.06,1)`) at anchored y = 330 / 220 / 110 (anchor is bottom-centre, as `CreateButton` hardcodes):
  - `"TRY THIS MAZE AGAIN"` → `GameFlow.Restart(sameMaze: true, _seed)`
  - `"NEW MAZE"` → `GameFlow.Restart(false, _seed)`
  - `"MAIN MENU"` → `GameFlow.ReturnToMenu()`
- After building: hold `PlayerLock.SetCursorFree(true)` every frame from `Update` while `IsOver` (same reason as `MainMenu.Update`). `RuntimeUi.EnsureEventSystem()` was already called by `MainMenu.Start`; call it again anyway — it is idempotent and the end screen must work with `mainMenu = false`.
- Do **not** set `Time.timeScale = 0` on the end screen. The world keeps running behind the scrim with the hunter frozen; the win drop in particular needs time to pass.

`OnDestroy`: `IsOver = false` (static, so it must be reset for the next scene instance; `GameFlow.ResetStatics` covers the Play-start case, this covers reload).

## 9. `PauseMenu.cs` (new component)

Added by `SetUpAtmosphere` right after `MainMenu`. `Configure(Transform player, Flashlight flashlight, MainMenu menu, int seed)` — `flashlight` and `menu` may be null.

- `Update`:
  ```
  if (Keyboard.current == null) return;  // plus Gamepad.current?.startButton as a free extra
  bool pressed = Keyboard.current.escapeKey.wasPressedThisFrame || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
  if (!pressed) return;
  if (_paused) Resume(); else if (CanPause()) Pause();
  ```
  `CanPause()` = `GameFlow.IsRunActive && !(menu != null && menu.IsOpen) && !GameOutcome.IsOver && !(outcome.IsEnding)`. Wrap the `Keyboard`/`Gamepad` reads in `#if ENABLE_INPUT_SYSTEM` like `Flashlight` does.
  - Editor caveat for the tester: in the Editor, Esc also releases the cursor lock at OS level regardless of what we do; that is Unity, not a bug. Standalone builds do not do this.
- `Pause()`: `_paused = true; Time.timeScale = 0f; AudioListener.pause = true;` (covers every source including the heartbeat one-shots and `AIPresence`); `PlayerLock.Freeze(player, true)`; `flashlight.InputEnabled = false`; build or `SetActive(true)` the panel. While `_paused`, `Update` also calls `PlayerLock.SetCursorFree(true)` every frame.
- `Resume()`: reverse all of it: `SetActive(false)`, `timeScale = 1`, `AudioListener.pause = false`, `Freeze(false)`, `InputEnabled = true`, `SetCursorFree(false)`. Input flags held at pause time (`StarterAssetsInputs.move/sprint`) are fine: the Input System updates on unscaled time, so a key released during the pause already delivered its `OnMove(zero)`.
- Panel (built once, lazily, on first pause): scrim `(0,0,0,0.75)`, title `"PAUSED"` 84 pt bold at `(0,-220)`, four buttons at y = 400 / 290 / 180 / 70:
  - `"RESUME"` → `Resume()`
  - `"RESTART MAZE"` → `GameFlow.Restart(true, seed)` — same layout; the seed comes from `MazeGenerator.UsedSeed` (§11), passed in `Configure`.
  - `"MAIN MENU"` → `GameFlow.ReturnToMenu()`
  - `"QUIT"` → `GameFlow.Quit()`
- Draw order: built lazily at first pause, so it is above the star counter and the escape timer automatically. If the end screen is later built it is created after → above the (hidden) pause panel. Fine.
- Nothing here subscribes to statics; nothing to unsubscribe. `OnDestroy`: if `_paused`, restore `timeScale = 1` and `AudioListener.pause = false` (belt and braces alongside `GameFlow.Restart`).

## 10. `MazeEscape.cs`

- `Configure(...)` gains a `GameOutcome outcome` parameter; store it.
- Add `public void Stop()`: `_running = false; _finished = true; if (_timerText) _timerText.gameObject.SetActive(false);`. Idempotent.
- Add `public Vector3 HatchPosition => _hatchPosition;` and `public bool HatchOpen { get; private set; }`, set true in `BeginEscape`.
- `Update` (85): the two endings become
  - timer expired → `_outcome.Lose(LoseReason.OutOfTime)` (the outcome calls `Stop()` back; keep the local `return`).
  - in the hatch → do the **drop teleport** locally (lines 240–249, it is hatch-specific geometry) and then `_outcome.Win()`. Remove the `_gameManager.WinGame()` call and the `_gameManager` field/lookup.
- Delete `Win()`, `Lose()`, `EndTheHunt()`, `ShowLosePanel()`. Keep `BuildTimerText`, `BuildHatch`, `BuildLandingPlatform`, `ChooseHatchCell`, `FormatTime` (make `FormatTime` `internal static` so `GameOutcome` reuses it).
- `BeginEscape` (68): also bail if `GameOutcome.IsOver` — the last star cannot be collected after a capture (player is frozen) but cheap to guard.

## 11. `MazeGenerator.cs`

- `public int UsedSeed { get; private set; }`.
- Line 119: `int usedSeed = GameFlow.PendingSeed != 0 ? GameFlow.PendingSeed : (seed != 0 ? seed : System.Environment.TickCount); GameFlow.PendingSeed = 0; UsedSeed = usedSeed;`. The Inspector `seed` is still honoured when nothing is pending. `SpawnStars(usedSeed)` (474) already derives its RNG from the same value, so "same maze" reproduces both walls and star cells; the hatch cell (`MazeEscape.ChooseHatchCell`, `UnityEngine.Random`) and the AI's wander choices are deliberately not seeded — a retry is the same maze, not the same run.
- `SetUpAtmosphere` (677) becomes, after the `TensionDirector` block:
  ```
  Transform player = FindPlayer();
  Flashlight flashlight = player != null ? player.GetComponent<FirstPersonRig>()?.Flashlight : null;

  GameOutcome outcome = FindAnyObjectByType<GameOutcome>() ?? gameObject.AddComponent<GameOutcome>();

  MazeEscape escape = null;
  if (escapeSequence)
  {
      escape = FindAnyObjectByType<MazeEscape>() ?? gameObject.AddComponent<MazeEscape>();
      escape.Configure(this, player, FindStarMaterial(), outcome);
  }

  outcome.Configure(player, aiFollower, director, escape, flashlight, UsedSeed);

  MainMenu menu = null;
  if (mainMenu)
  {
      menu = FindAnyObjectByType<MainMenu>() ?? gameObject.AddComponent<MainMenu>();
      menu.Configure(player);
  }
  else
  {
      // No title screen means nothing ever calls MainMenu.StartGame, which is what starts the run.
      GameFlow.IsRunActive = true;
      GameFlow.RunStartTime = Time.time;
  }

  PauseMenu pause = FindAnyObjectByType<PauseMenu>() ?? gameObject.AddComponent<PauseMenu>();
  pause.Configure(player, flashlight, menu, UsedSeed);
  ```
  `FirstPersonRig.Flashlight` is populated in the rig's `Awake`, which has already run synchronously inside `PlacePlayer` (line 579), so it is valid here. When `firstPerson` is off there is no flashlight; every consumer null-checks it. The `else` branch is what makes capture and pause work with `mainMenu` unticked — without it `GameFlow.IsRunActive` would stay false for the whole run.
- `GameOutcome` must exist before `MazeEscape.Configure` receives it; `MainMenu` still comes after everything that builds HUD so its screens are the topmost UI at load; `PauseMenu` builds nothing until first pause so its position does not matter.

## 12. `MainMenu.cs`

- `public bool IsOpen => _menuOpen;`
- Replace `FreezePlayer(x)` with `PlayerLock.Freeze(_player, x)`; replace the cursor lines in `Update`/`StartGame` with `PlayerLock.SetCursorFree(...)`; delete `QuitGame` and bind Exit to `GameFlow.Quit`. `Configure` stores the `Transform`.
- `StartGame()` (95): add `GameFlow.RunStartTime = Time.time; GameFlow.IsRunActive = true;` at the end.
- `Start()` (55): keep the body exactly as it is (event system, `MakeCanvasResolutionIndependent`, build both screens, freeze) and append `if (GameFlow.SkipMenuOnLoad) { GameFlow.SkipMenuOnLoad = false; StartGame(); }`. Going through `StartGame` (not just hiding the screens) is what unfreezes the player, relocks the cursor and restores the `timeScale` that `Awake` zeroed; `StartGame` also dereferences `_titleScreen`/`_rulesScreen`, so the screens must be built first. The canvas scaler call must run on every load or retry runs would get pixel-locked UI. The cover generation is a 960×540 CPU loop — not worth special-casing.
  - With skip-on there is no frame on which the title is visible; `TensionDirector`'s counter (built in its own `Start`, order −85, before `MainMenu` −80) is already on screen. Correct.
- `OnDestroy` (71): add `if (_generatedCover != null) Destroy(_generatedCover);`. Note `Sprite.Create` also allocates a `Sprite`; keep the reference and destroy it too.

## 13. `RuntimeUi.cs`

Optional: `public static Image CreateFullscreenImage(Transform parent, string name, Color color)` — `CreatePanel` already returns the GameObject with an `Image`; `GameOutcome` can use `CreatePanel(...).GetComponent<Image>()` and animate `color.a`. Only add a helper if it reads better. No other changes.

---

## Execution order and lifetime summary

| Order | Component | Notes |
|-------|-----------|-------|
| −100 `Awake` | `MazeGenerator` | picks seed (pending → inspector → tick), builds world, `AddComponent`s: `HorrorAudioDirector`, `TensionDirector`, `MazeEscape`, **`GameOutcome`**, `MainMenu`, **`PauseMenu`** |
| −85 `Start` | `TensionDirector` | star counter |
| −80 `Awake` / `Start` | `MainMenu` | `timeScale = 0`; either builds title or, with `SkipMenuOnLoad`, calls `StartGame()` immediately |
| default `Start` | `GameManager` | `ProgressChanged(0, total)` → `TensionDirector`, `GameOutcome` |
| play | `AIFollower.TickChase` | may raise `PlayerCaught` → `GameOutcome.Lose(Caught)` |
| play | `MazeEscape.Update` | → `GameOutcome.Lose(OutOfTime)` / `Win()` |
| any | `PauseMenu.Update` | Esc, gated by `GameFlow.IsRunActive`, `MainMenu.IsOpen`, `GameOutcome.IsOver/IsEnding` |
| end | `GameFlow.Restart/ReturnToMenu` | `LoadScene(active buildIndex)`; `GameOutcome.OnDestroy` resets `IsOver`; `MainMenu.OnDestroy` resets `timeScale` |

Build settings contain one scene (`Assets/Scenes/GetStarted_Scene.unity`, index 0); reload by active build index, not by name, so a rename does not break it.

## Compile check

Editor: normal domain reload on save. Batch (from `CLAUDE.md`, editor must be closed):

```
"C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe" -batchmode -quit -projectPath "D:\Unity\essentials\My project" -logFile -
```

## Acceptance checklist

Run each in the Editor with the Console visible. "Seed" = the value in `MazeGenerator: building a 11x11 maze with seed N`.

**F1 — capture**
- [ ] Stand in a corridor, torch on, let the hunter walk at you from the front. At ~1.4 m the view snaps to its eyes, the torch stutters and dies, the screen goes black, `IT FOUND YOU` appears. The heartbeat, drone, hum and footsteps are all silent on the end screen.
- [ ] Stand against a wall with the hunter on the other side of the same wall (use the Scene view). No capture, however long you wait.
- [ ] Let it lose sight of you (round a corner) and reach your last-known position while you stand 1 m from that point behind a wall. No capture.
- [ ] Open the hatch, then get caught during the countdown: the timer text disappears immediately and does **not** later produce `OUT OF TIME`.
- [ ] Drop through the hatch with the hunter right behind you: the win screen shows, no capture afterwards.
- [ ] Press Play, do **not** press Start, and use the Scene view to drag the hunter onto the player: nothing happens (`IsRunActive` gate).
- [ ] Gizmo: with `AI_Follower` selected the capture disc is visible and turns colour in `Captured`.

**F2 — restart**
- [ ] Lose → `TRY THIS MAZE AGAIN`: the Console logs the **same** seed, the walls and star positions match, and the game starts immediately with no title screen and the cursor locked.
- [ ] Lose → `NEW MAZE`: different seed, no title screen.
- [ ] `MAIN MENU` from either end screen or from pause: title screen shown, cursor free, world frozen.
- [ ] Three restarts in a row: no `MissingReferenceException`, no doubled heartbeat/drone (would indicate a leaked subscriber or source), no growing count of `GeneratedCover` textures in the Memory Profiler / no "Texture2D … leaked" warning on exit.
- [ ] Stop Play, press Play again: the title screen shows (statics reset).
- [ ] Set *Project Settings → Editor → Enter Play Mode Options → Reload Domain* **off**, restart once, stop, Play: title still shows.

**F3 — pause**
- [ ] Esc on the title/rules screen: nothing happens.
- [ ] Esc mid-run: world freezes, all audio stops (including a heartbeat mid-thump), cursor free, `PAUSED` panel. Mouse movement does not turn the camera. F does not toggle the torch.
- [ ] Esc again or `RESUME`: everything resumes, cursor locked, torch key works.
- [ ] Esc during the escape countdown: the timer value does not change while paused.
- [ ] Esc during the capture sequence: ignored. Esc on an end screen: ignored.
- [ ] Alt-tab out and back while paused: cursor is still free and visible.
- [ ] `RESTART MAZE` from pause: same seed, immediate start, `timeScale` is 1 (the star bobbing moves).
- [ ] `QUIT` from pause in the Editor stops Play mode.

**F4 — end screens**
- [ ] Each of the three endings shows the right title, subtitle and a stats line with correct stars, a run time that excludes menu and pause time, and the seed from the Console.
- [ ] `OUT OF TIME` subtitle reports a plausible distance to the hatch.
- [ ] The scene-authored `WinPanel` ("You Win!") never appears on the maze path.
- [ ] With `MazeGenerator.mainMenu` unticked: the run starts immediately, pause works, end screens and their buttons work (event system created on demand).
- [ ] With `MazeGenerator.escapeSequence` unticked: collecting the last star still ends the game via the tutorial `WinPanel` (`GameManager.WinGame` fallback), and capture still works.

## Out of scope (deliberately)

- Any difficulty/seed UI (F10/F13) — `UsedSeed` and `GameFlow.PendingSeed` are the hooks for it.
- Audio mixer groups (F15): `AudioListener.pause` is enough for pause; end-screen music can come later.
- A struggle/QTE on capture, checkpoints, or lives.
- Refreshing `CLAUDE.md` (it still describes the tutorial scene) — worth doing, but as its own change.

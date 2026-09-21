# LIGHTS OUT — Road to a Full Game

Planning document only. This folder sits outside `Assets/`, so Unity does not import it and it has no effect on the game. Iterate here first, implement after.

## What already exists (baseline)

Far more than "a maze with a robot". Almost all of it is built at runtime from `MazeGenerator.Awake` (`[DefaultExecutionOrder(-100)]`); the only authored pieces left are `GameManager.winPanel` and the tutorial's `Remaining_Collectibles_UI.prefab`.

- **World**: seeded procedural maze (`MazeGenerator`, 756 lines) — walls/floor/ceiling, runtime NavMesh bake, star placement, actor placement.
- **Player**: first-person rig (`FirstPersonRig`), head-mounted flashlight (`Flashlight`, F key, flicker, shadow tuning), stealth model (`PlayerStealthState`: noise radius from actual velocity, visibility multiplier driven by the torch).
- **Hunter**: `AIFollower` (780 lines) with sight cone + LOS, hearing, last-known-position search sweep, patrol bias toward uncollected stars, wander points, and a `SetThreatLevel` escalation dial.
- **Dread layer**: `AIPresence` (occlusion-free footsteps/hum + emissive eyes), `HorrorAudioDirector` (distance heartbeat, spot sting, drone, panic track), `HorrorAtmosphere` (ambient/fog/skybox blackout), `TensionDirector` (escalates counter colour, audio intensity and AI threat as stars are taken).
- **Loop**: 5 stars → `GameManager.AllStarsCollected` → `MazeEscape` opens a glowing hatch and starts a 60 s timer → reach it and drop onto a landing platform (win), or time out (lose).
- **Shell**: `MainMenu` (generated cover art, title, rules screen, `Time.timeScale = 0` gate), `RuntimeUi` helpers.

**Two facts that shape everything below**, verified in the code:

1. **The hunter cannot catch you.** `AIFollower` has `stoppingDistance = 1.5f`, its `State` enum is only `{ Chase, Wander, Search }`, and a grep across all scripts in the folder finds no contact handler at all — no `OnTriggerEnter`, no damage, no call into `MazeEscape.Lose()`. It walks up to you, stops 1.5 m short, and waits. The only fail state in the game is the 60 s escape timer, i.e. the last ~10% of a run. Everything before the final star is tension with zero stakes.
2. **There is no way to play twice.** No `SceneManager.LoadScene` call exists in the project. Win and Lose both just lock movement and show a panel; the only exit is leaving Play mode. Good news: every static-event subscriber (`GameManager`, `MazeEscape`, `TensionDirector`, `HorrorAudioDirector`) unsubscribes in `OnDestroy`/`OnDisable`, and `MazeGenerator.OnDestroy` only disposes its runtime material copies, so a scene reload is clean — restart is genuinely cheap to add.

---

## Tier 1 — Core loop gaps (the game is not a game without these)

| ID | Feature | Why it's missing / what it fixes | Existing hooks | Size |
|----|---------|----------------------------------|----------------|------|
| **F1** | **Death on capture.** Hunter within grab range for N frames → capture sequence (movement lock, camera snaps to it, sting, fade) → lose. | See fact 1: the hunter is currently a jump-scare prop. This is the single highest-value item in the document. | `AIFollower` chase state + `ChaseStateChanged`, `MazeEscape.Lose()` already handles lock + panel, `HorrorAudioDirector` sting | M |
| **F2** | **Restart / Retry.** Buttons on both the win and lose panels; reload the scene with a fresh seed (or the same one, see Q2). | See fact 2. Events are already leak-free, so this is mostly UI + `LoadScene`. One gotcha: `MainMenu` gates every scene start with `timeScale = 0` and the title screen, so a plain reload shows the menu on every retry — a `static bool` surviving the reload tells `MainMenu` to jump straight to `StartGame`, and the same static can carry the seed for a same-maze retry. | `RuntimeUi.CreateButton`, `MazeGenerator.seed`, `MainMenu.StartGame` | S |
| **F3** | **Pause menu.** Esc → freeze, unlock cursor, Resume / Restart / Options / Quit. | No pause exists; Esc does nothing. `MainMenu` already solves the hard parts (timeScale gate, cursor fight with `StarterAssetsInputs`). | `MainMenu.FreezePlayer`, `RuntimeUi` | S |
| **F4** | **A real lose screen.** Cause of death ("The hunter found you" vs "Out of time"), run stats (stars, time, seed). | `ShowLosePanel` is one generic panel for a single cause; F1 adds a second. | `MazeEscape.ShowLosePanel` | S |

## Tier 2 — Give the stealth mechanics teeth

Right now the player has one decision (torch on/off) and no resources. These add pressure and choices.

| ID | Feature | Why | Existing hooks | Size |
|----|---------|-----|----------------|------|
| **F5** | **Flashlight battery.** Drains while on, recharges slowly while off; low-battery flicker. Optional pickups. | `Flashlight` costs visibility only — with no resource, the optimal play is "torch off forever" once you learn the maze. | `Flashlight.IsOn`, `flickerAmount`, `PlayerStealthState.FlashlightOn` | M |
| **F6** | **Sprint stamina.** Sprinting spikes `NoiseRadius` to 15 m but is otherwise free and infinite. Stamina makes fleeing a budget. | `PlayerStealthState` already derives noise from velocity; `ThirdPersonController` owns sprint. | `PlayerStealthState.NoiseFraction`, `ThirdPersonController` | M |
| **F7** | **Hiding.** Alcoves or lockers carved by the generator; crouch/enter to drop visibility hard while the hunter searches. | The search sweep (`searchRadius`, `searchBudget`) is built and currently has nothing to search *around* — breaking LOS in a corridor is the only counterplay. | `AIFollower` search state, `PlayerStealthState.VisibilityMultiplier`, `MazeGenerator` dead-end cells | L |
| **F8** | **Throwable distraction.** Throw an object; impact registers as noise at that point and pulls the hunter. | Hearing already exists but is one-way (the player can only be heard, never bait). | `AIFollower` hearing (`hearingInterval`, `NavMeshPath`) | M |
| **F9** | **Hatch direction cue.** A directional pulse/audio ping during the escape so the 60 s is a race, not a coin flip. | `MazeEscape` places the hatch ≥ 12 m away with no wayfinding; losing to bad luck reads as unfair. | `MazeEscape._hatchPosition`, emissive hatch, `HorrorAudioDirector` | S |

## Tier 3 — Progression and replay value

| ID | Feature | Why | Existing hooks | Size |
|----|---------|-----|----------------|------|
| **F10** | **Difficulty presets** (Calm / Standard / Nightmare) driving `width`/`height`/`starCount`/`escapeSeconds` and the hunter's threat floor. | Every knob is already serialized and `SetThreatLevel` is already the escalation dial — this is wiring, not new systems. | `MazeGenerator` fields, `AIFollower.SetThreatLevel`, `TensionDirector` | S |
| **F11** | **Run chain / levels.** Win → next maze, bigger and darker, carrying nothing but your time. 3–5 stages then a real ending. | The generator is seeded and fully runtime, so "next level" is a reload with different parameters. Turns a 5-minute demo into a session. | `MazeGenerator.seed`, F2's reload path | M |
| **F12** | **Best-time / stats save** via `PlayerPrefs`; show on the title screen. | Nothing persists between sessions. Cheap, and makes F11 mean something. | `MainMenu`, `RuntimeUi` | S |
| **F13** | **Daily / shareable seed.** Enter a seed from the menu; show the seed on the results screen. | `seed = 0` already means "random, log it" — surfacing it is nearly free and gives the game a reason to be replayed by two people. | `MazeGenerator.seed` | S |

## Tier 4 — Options and accessibility

| ID | Feature | Why | Existing hooks | Size |
|----|---------|-----|----------------|------|
| **F14** | **Options screen**: mouse sensitivity, invert Y, master/SFX/music volume, FOV. | None of these are adjustable; `FirstPersonRig.fieldOfView` and the controller's sensitivity are compile-time only. | `RuntimeUi`, `FirstPersonRig`, `ThirdPersonController` | M |
| **F15** | **Audio mixer groups**, so F14's sliders have something to move and the win screen can duck everything at once. | `HorrorAudioDirector.Silence()` currently has to stop every source by hand. | `HorrorAudioDirector`, `AIPresence` | S |
| **F16** | **Subtitles / visual cues for audio.** | The game is played by ear (footsteps through walls, heartbeat, hum); with no visual channel it is unplayable deaf or muted. Also helps silent and streamed play. | `AIPresence`, `HorrorAudioDirector` intensity | M |
| **F17** | **Gamepad support + a controls screen.** | `StarterAssets.inputactions` already carries gamepad bindings, but `Flashlight.Update` reads `Keyboard.current.fKey` directly and bypasses the action map, so the torch is keyboard-only. | `StarterAssetsInputs`, `Flashlight` | S |

## Tier 5 — Polish and ship

| ID | Feature | Why | Existing hooks | Size |
|----|---------|-----|----------------|------|
| **F18** | **Player footsteps + breathing.** | The hunter has `AIPresence`; the player has nothing tying their own noise to what they hear. Breathing that quickens with proximity sells the stealth. | `PlayerStealthState.NoiseFraction`, `MotionAudioController` | M |
| **F19** | **Intro / framing.** | The rules screen states mechanics, not fiction — one line on why you're here and what the stars are. | `MainMenu.BuildRulesScreen` | S |
| **F20** | **Visual identity pass.** | The cover is procedurally generated placeholder art with the `coverImage` slot empty; stars and hatch are untextured emissive geometry. | `MainMenu.coverImage`, `VFX_FloatUp.prefab` | M |
| **F21** | **Credits + build.** | Standalone Windows, or WebGL for itch.io — real-time shadows plus a runtime NavMesh bake need a perf check there. | Build settings (`GetStarted_Scene` only) | M |

---

## Suggested first slice

**F1 + F2 + F3 + F4** — capture, restart, pause, a lose screen that says why. That alone converts this from an atmospheric demo into something with a loop you can lose and immediately re-enter. Then **F10 + F13** (near-free, high replay value), then pick one of F5/F6/F7 for depth.

## Open questions

1. **On capture (F1)** — instant lose and restart, or a "grabbed, struggle free once" mechanic? Instant is far more tense and much less work.
2. **Restart (F2)** — same maze (learn it, beat it) or a new seed every time (always fresh, never masterable)? Or offer both buttons?
3. **Scope (F11)** — one hard maze you replay for time, or a chain of levels with an ending? This decides whether the game is a score-attack or a short campaign.
4. **Authoring** — everything is runtime-generated today. Keep that (consistent, no scene merge pain) or start authoring prefabs for the heavier UI (options, pause)? Recommend keeping runtime.
5. **Target** — is this a portfolio piece, an itch.io release, or a learning project? Changes how much of Tier 4/5 matters.

# Tier 2 — Stealth With Teeth: Battery, Stamina, Lockers, Hatch Cue, Wall Lamps

Planning document only. This folder sits outside `Assets/`, so Unity does not import it and it has no effect on the game. Implementation plan for **F5, F6, F7, F9** of `full-game-feature-plan.md` plus two additions requested on 20 Sep 2026: **wall lamps** (a little ambient light in the maze) and a **hard cap on the hunter's speed** (it is never as fast as a sprinting player, even when it runs). Written for someone who has not seen the codebase before. Every claim about existing code was checked against the source on 20 Sep 2026; line numbers refer to the files as they are today.

Read `CLAUDE.md` first, then `plannings/tier1-core-loop-plan.md` for the conventions this plan continues (runtime composition root, static-event hygiene, `RuntimeUi`, `PlayerLock`, `GameFlow`, `GameOutcome`).

---

## Context

LIGHTS OUT is a first-person horror maze. `MazeGenerator.Awake` (`Assets/SourceFiles/Scripts/MazeGenerator.cs`, `[DefaultExecutionOrder(-100)]`, line 114) builds the world in this order: `Generate` → `CacheCellCenters` → `BuildGeometry` → `BuildRuntimeNavMesh` → `BuildCeiling` → `DisableExistingPickups` → `SpawnStars` → `PlacePlayer` → `PlaceAI` → `SetUpAtmosphere` (line 684, the composition root where every director is `AddComponent`ed and wired with `Configure`/`Bind`).

Tier 1 is done: the hunter captures (`AIFollower.EnterCaptured` → `PlayerCaught` → `GameOutcome.Lose(LoseReason.Caught)`), the run restarts through `GameFlow`, Esc pauses, end screens show a reason and stats.

### What Tier 2 fixes

Right now the player has one decision (torch on or off) and no resources:

1. **The torch is free.** `Flashlight` costs visibility only, so once a player knows the maze the optimal play is "torch off forever" — and with no ambient light that is also miserable to play.
2. **Sprint is free.** `ThirdPersonController.Move` (line 260): `targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed`. Infinite sprint at 5.335 m/s; the only cost is the 15 m noise radius from `PlayerStealthState`.
3. **The hunter is faster than the player in the endgame.** `AIFollower.runSpeed = 5.5f` (line 17) vs `ThirdPersonController.SprintSpeed = 5.335f` (prefab-serialized, `Prefabs/PlayerRobot.prefab` line 1926). Once the hatch opens `ChaseSpeed()` and `WanderSpeed()` (lines 176–190) return `runSpeed`, so a straight-corridor flee is lost by arithmetic. The user's requirement: **it is never as fast as the player, even when it runs.**
4. **There is nothing to hide in.** The search sweep (`EnterSearch`/`TickSearch`, lines 484–560) is built but the only counterplay is breaking line of sight in a corridor.
5. **The 60 s escape is a coin flip.** `MazeEscape.ChooseHatchCell` (line 138) puts the hatch ≥ 12 m away with no wayfinding beyond the hatch's own 12 m point light.

### Facts that shape the design (verified)

**Player / controller**
- `ThirdPersonController` (namespace `StarterAssets`, `Assets/SourceFiles/Scripts/ThirdPersonController.cs`): `MoveSpeed = 2.0`, `SprintSpeed = 5.335` (both public, prefab values match the code defaults). `MovementLocked` (line 43) zeroes move input but lets gravity run. `LockCameraPosition` (line 89) stops all mouse look. `BottomClamp`/`TopClamp` (lines 83–86) are public pitch clamps; `FirstPersonRig.ConfigureController` sets them to ±80. **There is no yaw clamp.** `LookAt(worldPoint, turnFraction)` (line 447) writes the private yaw/pitch targets; `CameraRotation()` (line 216) re-clamps pitch and writes `CinemachineCameraTarget.transform.rotation` every `LateUpdate`.
- Teleporting the player: disable the `CharacterController`, set `position`, re-enable — `MazeGenerator.PlacePlayer` (lines 566–573) and `MazeEscape.DropThroughHatch` (lines 233–235) both do exactly this.
- Player capsule: radius 0.28, height 1.8, centre y 0.93 (`PlayerRobot.prefab` lines 1906–1912). Eye height 1.6 (`FirstPersonRig.eyeHeight`).
- Input: `PlayerInput` on the prefab uses `StarterAssets.inputactions` (guid `4419d82f…`). Its `Player` map is Move / Look / Jump (`space`, `<Gamepad>/buttonSouth`) / Sprint (`leftShift`, `<Gamepad>/leftTrigger`) only. `Flashlight` (F) and `PauseMenu` (Esc / `startButton`) read `Keyboard.current` / `Gamepad.current` directly. `eKey` and `<Gamepad>/buttonWest` are unused.

**Stealth**
- `PlayerStealthState` (59 lines): `NoiseRadius` from `CharacterController.velocity` (sprint > 4.5 m/s → 15 m, walk > 0.5 m/s → 5 m, else 0). `VisibilityMultiplier { get; set; }` is a plain settable property. **`Flashlight.ApplyState` (line 108) writes it directly** (`IsOn ? 1f : hiddenVisibility 0.35f`) on every toggle — so anything else that sets it gets overwritten on the next F press. Also sets `FlashlightOn`.
- `AIFollower.CanSeeTarget()` (line 276): `view = viewDistance(18) × VisibilityMultiplier`; early-out `if (flat.sqrMagnitude > view*view) return false`, **but** `if (flat.sqrMagnitude < 0.0001f) return true` — so a multiplier of 0 hides the player at any distance except literally coincident. Then a 110° cone and a raycast where anything not under `target` blocks.
- Capture gate (`TickChase`, line 452): `GameFlow.IsRunActive && _hasSight && flatDistance² ≤ captureRadius²(1.4)`. **No sight, no capture.** Hearing (`UpdateHearing`, line 214) only ever promotes to `Search`, never `Chase`.
- `EnterSearch` (line 484) queues `_wanderPoints` within `searchRadius` (7.5 m) of `_lastKnownPosition`, shuffled, capped at `searchPoints` (4), with the exact spot inserted first; `TickSearch` dwells `searchDwellTime` (1 s) at each, turning on the spot.
- `SetWanderPoints` / `SetPatrolTargets` (lines 130–151) are called from `MazeGenerator.PlaceAI` **before `AIFollower.Awake`** and may not touch `_agent`.

**Hunter speed**
- Speeds: `moveSpeed 3.5` (**scene-serialized** at `GetStarted_Scene.unity` line 1499 — a code-default change is silently ignored), `runSpeed 5.5` (code default only), `dormantSpeed 0.7`. `ChaseSpeed()` = `_hunting ? runSpeed : Lerp(dormantSpeed, moveSpeed, _threat)`; `WanderSpeed()` identical. `UpdateChaseOnly` (legacy no-maze path, line 166) also uses `runSpeed`. `MoveSpeedTowards` (line 655) is the single write to `_agent.speed`. `Start()` (line 340) already resolves `target` and `_stealth`.

**Generator**
- Wall flags `_wallN/E/S/W[x,z]` (line 78). A **dead end** is a cell with exactly three `true` flags. Start cell is `(0,0)`; AI spawns at `FarthestCell()`; stars prefer `_distance ≥ max(2, width/2)` and use `IsFarEnough(candidate, chosen, minSeparation)` (line 522) with `System.Random(usedSeed + 1)`. Corridor width = `cellSize − wallThickness` = 2.5 m; walls 0.5 m thick, 4 m high; `FloorTop = origin.y + 0.02`. Agent radius 0.5.
- **Bake order matters:** the navmesh is baked with `CollectObjects.Volume` over every render mesh (comment at line 133), so anything with a `MeshRenderer` inside the volume *before* `BuildRuntimeNavMesh` is carved out of the walkable area and anything built *after* (the ceiling, line 373) is not.
- `CreateBox` (line 391) = primitive cube, layer 0, parented to `_mazeRoot`, `sharedMaterial`. `DarkCopy` (line 290) makes a tinted runtime copy and registers it in `_runtimeMaterials`, which `OnDestroy` destroys. `FindStarMaterial()` (line 676) is the only emissive material to hand; `MazeEscape.BuildHatch` and `AIPresence.BuildEyes` both copy it and set `_EmissionColor`.
- Star lights (`AttachStarLight`, line 538): point, range 5, intensity 1.5, `LightShadows.None` with the rule *"the flashlight is the only shadow caster in the maze"* (repeated in `MazeEscape` and `AIPresence`). Keep it.

**Rendering**
- `PC_Renderer.asset` and `Mobile_Renderer.asset` both have `m_RenderingMode: 2` = **Forward+** (clustered). `PC_RPAsset.asset` still carries `m_AdditionalLightsPerObjectLimit: 4`, but that limit only applies in plain Forward. This matters because the floor is **one** 33 m box: under Forward it could receive four lights total and two dozen lamps would pop in and out. Under Forward+ it is fine. **Do not switch the rendering mode.**
- Existing assets you could reuse but which would need a serialized slot (= a scene edit): `Assets/Prefabs/Wall_Light_Left.prefab` (cube 0.2 scale + white point light range 5 intensity 2) and `Assets/SourceFiles/Materials/Lamp_Mat.mat` (URP Lit, warm base colour, emission black). This plan builds lamps in code instead; the prefab is an optional fallback.

**Shell / audio / UI**
- `TensionDirector` is `[DefaultExecutionOrder(-85)]` and builds its counter in `Start` so it is created **before** `MainMenu` (`-80`, builds title/rules in `Start`, lines 57–67) and therefore draws underneath it. `MainMenu` never calls `SetAsLastSibling`; `PauseMenu.Pause` does (line 77), and `GameOutcome` creates its panels last. Any new HUD must follow the same rule: order ≤ −81, build in `Start`.
- `MazeEscape` owns `_hatchPosition`, the timer text (`BuildTimerText`, line 244, anchored top-centre at y −40, 400×90) and `Stop()` (line 61), which `GameOutcome` calls on every ending. `GameOutcome.EndTheHunt` silences `HorrorAudioDirector` and `AIPresence` **only** — any new `AudioSource` on `MazeEscape` must be stopped in `Stop()`.
- The `AudioListener` is on the camera (`FirstPersonRig.MoveAudioListener`), so positional audio pans with head yaw. `AudioListener.pause = true` under pause covers every source.
- Synthesised clips: `HorrorAudioDirector.BuildHeartbeatClip` / `AddThump` (lines 242–333) are the pattern for a code-built one-shot.
- `GameOutcome.CaptureSequence` (line 147) calls `_flashlight.SetOn(bool)` seven times for the flicker and reads `IsOn` for the subtitle (`Lose`, line 144; `CaptureSequence`, line 152). `PauseMenu` and `GameOutcome` set `Flashlight.InputEnabled`.
- `MainMenu.BuildRulesScreen` (line 149) has a controls line: `WASD move  SHIFT sprint  MOUSE look  F flashlight`.
- `Time.deltaTime` is 0 behind the title screen and under pause; `Update` still ticks. Anything not `deltaTime`-driven needs a `GameFlow.IsRunActive` gate.
- Statics must be reset in a `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` if they carry run state. **This plan adds no new statics**; everything is handed through `Configure`.

---

## Decisions and assumptions

Change these here before implementing if the answer is different.

- **Battery is a 180 s cumulative budget with no recharge and no pickups.** The user's instruction ("at most 3 mins in total") overrides the feature plan's "recharges slowly while off". The torch drains only while on; when it hits zero it dies for the rest of the run. Wall lamps are the safety valve that keeps a dead-torch run playable.
- **Hunter speed cap = 86 % of the player's sprint speed** (`5.335 × 0.86 = 4.59 m/s`), enforced in code against whatever the Inspector says. Pre-hatch chase stays at `moveSpeed` (3.5). Post-hatch it runs at the cap. Stamina then decides the chase, see the table below.
- **Stamina: 8 s of sprint from full, 16 s to refill from empty, refill starts 1.5 s after you stop, and once emptied you cannot sprint again until 25 %.** Exhausted = walking speed, nothing else (no stumble, no breathing — F18).
- **Hiding spots are lockers**, one per chosen dead-end cell, against the dead end's back wall. Enter/leave with **E** (`<Gamepad>/buttonWest`). Inside: movement locked, torch forced off, limited look (±35° yaw, ±25° pitch) through the door slit, `Hidden = true` → visibility 0.
- **"It saw you go in" rule.** If the hunter is in `Chase` with sight at the moment the player becomes hidden, it goes to the locker and captures on arrival without needing sight. If it did not see you, hiding is safe: its search sweep visits any locker near the last-known position, stands in front of it for a moment, and moves on. Camping a locker pre-hatch has no direct cost (you must leave to collect stars; the escape timer punishes it post-hatch). Accepted.
- **Hatch cue = positional audio ping every 2 s from the hatch, plus a small on-screen bearing chevron under the countdown.** No minimap, no path line.
- **Wall lamps are decorative and gameplay-relevant:** warm, dim, no shadows, about one cell in five plus one above every locker; a few of them flicker/blink. Standing near a lit lamp raises the player's visibility with the torch off (so the light is something to avoid, and lockers/dark corners are the answer).
- **Everything stays runtime-built.** No prefabs, no scene edits, no new Inspector references. New components are created from `MazeGenerator.SetUpAtmosphere` (or the generator's build steps) like everything else, and every new field has a code default that produces the tuned behaviour.
- **Tuning numbers below are starting values**, exposed as `[SerializeField]` with tooltips like the rest of the project.

### Speed and stamina arithmetic

| Actor / state | Speed | Source |
|---|---|---|
| Player walk | 2.0 m/s | `MoveSpeed` |
| Player sprint | 5.335 m/s | `SprintSpeed` |
| Hunter dormant (0 stars) | 0.7 | `dormantSpeed` |
| Hunter chase, pre-hatch, all stars | 3.5 | `moveSpeed` (scene) |
| Hunter run, post-hatch | **4.5** (was 5.5; hard cap 4.59 = `SprintSpeed × 0.86`) | `runSpeed` default, clamped in code |

- Pre-hatch flee: sprint gains 1.84 m/s → **~14.7 m over a full 8 s bar**, then an exhausted player (2.0) loses 1.5 m/s to a walking hunter. That is the stake: sprint to break line of sight, not to outrun it forever.
- Post-hatch flee: sprint gains only 0.75 m/s → **~6 m per bar**. `loseSightTime` is 3 s. The endgame is designed as *break LOS around a corner, then hide or keep moving*, not a footrace. If playtests say it is too tight, raise `sprintSeconds` before touching the cap ratio.

---

## Architecture

| Need | Today | Becomes |
|---|---|---|
| Who owns how visible the player is | `Flashlight` writes `PlayerStealthState.VisibilityMultiplier` | **`PlayerStealthState` computes it** from `FlashlightOn`, `Hidden`, and lamp exposure. `Flashlight` only sets `FlashlightOn`. |
| Torch resource | none | `Flashlight` gains a battery (`Charge`, `SecondsLeft`, `IsDead`) |
| Sprint resource | none | **`PlayerStamina`** component (new) on the player, driving `ThirdPersonController.SprintLocked` |
| Hunter speed invariant | two serialized floats | `AIFollower` clamps every speed to `player SprintSpeed × maxSpeedFraction` in `Start` |
| Hiding | none | **`Locker`** component (new) per locker; **`PlayerHider`**-style logic lives in `Locker` + `PlayerStealthState.Hidden`; `AIFollower` learns hiding-spot positions and the "saw you enter" rule |
| Hatch wayfinding | hatch light only | `MazeEscape` gains a ping source + bearing chevron |
| Ambient light | none | `MazeGenerator.BuildWallLamps` + **`WallLamp`** component (new, flicker + exposure query) |
| Resource HUD | star counter only | **`PlayerHud`** component (new): battery bar, stamina bar, interaction prompt |

Dependency direction stays one-way: `MazeGenerator` creates and configures; `Locker` → `PlayerStealthState` / `ThirdPersonController` / `Flashlight`; `AIFollower` reads `PlayerStealthState` only; `PlayerHud` reads `Flashlight`, `PlayerStamina`, and a prompt string set by `Locker`. Nothing new references `MazeGenerator` except through `Configure`.

---

## Files

New scripts go in `Assets/SourceFiles/Scripts/`. Unity generates the `.meta` on import; do not hand-write GUIDs. Everything lands in `Assembly-CSharp`.

### New
1. `PlayerStamina.cs` — MonoBehaviour on the player.
2. `Locker.cs` — MonoBehaviour on each locker root.
3. `WallLamp.cs` — MonoBehaviour on each lamp (flicker, exposure query).
4. `PlayerHud.cs` — MonoBehaviour on the `MazeGenerator` object, `[DefaultExecutionOrder(-84)]`.

### Modified
5. `PlayerStealthState.cs` — owns visibility; `Hidden`, lamp exposure, `CurrentHidingSpot`.
6. `Flashlight.cs` — battery; stops writing `VisibilityMultiplier`.
7. `ThirdPersonController.cs` — `SprintLocked`, `IsSprinting`, yaw clamp.
8. `AIFollower.cs` — speed clamp, hiding spots in search, "saw you enter" capture.
9. `MazeEscape.cs` — hatch ping + bearing chevron; stop them in `Stop()`.
10. `MazeGenerator.cs` — locker cell choice, `BuildLockers` before the bake, star exclusion, `BuildWallLamps` after the bake, wiring in `SetUpAtmosphere`.
11. `MainMenu.cs` — rules/controls text.
12. `GameOutcome.cs` — subtitle for "caught in a locker"; tolerate a dead torch in the capture flicker.

---

## 1. `PlayerStealthState.cs` — own the visibility number

Replace the settable property with a computed one and add the hidden/lamp inputs.

```
[Header("Visibility")]
[Tooltip("How visible the player is with the torch off, relative to on")]
[SerializeField] private float darkVisibility = 0.35f;          // moved here from Flashlight.hiddenVisibility
[Tooltip("Extra visibility when standing in a wall lamp's light with the torch off, at the lamp itself")]
[SerializeField] private float lampVisibilityBonus = 0.45f;

public bool  FlashlightOn { get; set; } = true;                 // unchanged, set by Flashlight
public bool  Hidden { get; private set; }                       // true inside a locker
public IHidingSpot CurrentHidingSpot { get; private set; }       // the locker, while hidden
public float LampExposure { get; private set; }                 // 0..1, from WallLamp.ExposureAt

public float VisibilityMultiplier =>
    Hidden ? 0f : FlashlightOn ? 1f : Mathf.Min(1f, darkVisibility + lampVisibilityBonus * LampExposure);

public void SetHidden(IHidingSpot spot)   { Hidden = spot != null; CurrentHidingSpot = spot; }
public void SetLamps(IReadOnlyList<WallLamp> lamps)  // from MazeGenerator; may be empty
```

- `IHidingSpot` is a tiny interface declared in `Locker.cs`: `Vector3 FrontPosition { get; }  void Expose();` — `AIFollower` calls `Expose()` when it busts the player out (§4).
- In `Update`, after the noise code: `LampExposure = WallLamp.ExposureAt(_lamps, transform.position + Vector3.up)` (§7). Cheap: ~25 distance checks.
- Remove the `set` on `VisibilityMultiplier`. `Flashlight` is the only writer today (line 108); `AIFollower.CanSeeTarget` is the only reader. The compiler will flag any other writer.
- Noise while hidden: `CharacterController.velocity` is zero inside a locker, so `NoiseRadius` falls to 0 on its own — no change.

## 2. `Flashlight.cs` — battery

```
[Header("Battery")]
[Tooltip("Total seconds the torch can be on in one run. It never recharges.")]
[SerializeField] private float batterySeconds = 180f;
[Tooltip("Below this fraction the beam starts to stutter")]
[SerializeField] private float lowBatteryFraction = 0.2f;
[Tooltip("Flicker amount at empty; blends from flickerAmount as the charge falls below lowBatteryFraction")]
[SerializeField] private float dyingFlickerAmount = 0.55f;
[Tooltip("Beam intensity multiplier at the very end of the battery")]
[SerializeField] private float dyingIntensityFraction = 0.55f;

public float SecondsLeft { get; private set; }     // starts at batterySeconds
public float Charge => batterySeconds > 0f ? SecondsLeft / batterySeconds : 0f;
public bool  IsDead => SecondsLeft <= 0f;
public bool  IsLow  => Charge <= lowBatteryFraction;
```

- `Awake`: `SecondsLeft = batterySeconds`.
- `Update`, before the key read: `if (IsOn && !IsDead) { SecondsLeft = Mathf.Max(0f, SecondsLeft - Time.deltaTime); if (IsDead) SetOn(false); }`. Scaled `deltaTime` means the title screen and pause freeze the drain for free; do **not** gate this on `GameFlow.IsRunActive` (it would be redundant and would stop the drain during the capture sequence, which is harmless either way).
- Key handling: `if (InputEnabled && !IsDead && …fKey…) SetOn(!IsOn);`. Pressing F on a dead torch does nothing (optional: a quiet click one-shot — skip unless trivial).
- `SetOn(bool on)`: `IsOn = on && !IsDead;` then `ApplyState()`. This is what lets `GameOutcome.CaptureSequence` keep calling `SetOn(true/false)` unchanged: a dead torch just stays dark through the flicker, which reads correctly.
- Flicker in `Update`: `float amount = IsLow ? Mathf.Lerp(dyingFlickerAmount, flickerAmount, Charge / lowBatteryFraction) : flickerAmount;` and multiply the resulting intensity by `Mathf.Lerp(dyingIntensityFraction, 1f, Mathf.Clamp01(Charge / lowBatteryFraction))`. Below ~5 % add an occasional dropout: `if (Charge < 0.05f && Mathf.PerlinNoise(Time.time * 3f, 7f) > 0.8f) intensity = 0f`.
- `ApplyState()`: delete the `VisibilityMultiplier` write; keep `_stealth.FlashlightOn = IsOn`. Delete the `hiddenVisibility` field (moved to `PlayerStealthState.darkVisibility`).
- Public `ForceOff()` = `SetOn(false)` — used by `Locker` on entry (name it so the intent is clear at the call site; `SetOn(false)` is fine too).

## 3. `PlayerStamina.cs` (new) + `ThirdPersonController.cs`

**Controller hook** (`ThirdPersonController.Move`, line 260):

```
[Tooltip("Sprint input is ignored while true. Driven by PlayerStamina.")]
public bool SprintLocked = false;

/// True this frame if the sprint key is held, allowed, and there is movement input.
public bool IsSprinting { get; private set; }
```

```
bool wantsSprint = _input.sprint && !SprintLocked && !MovementLocked;
float targetSpeed = wantsSprint ? SprintSpeed : MoveSpeed;
if (moveInput == Vector2.zero) targetSpeed = 0.0f;
IsSprinting = wantsSprint && moveInput != Vector2.zero;
```

**Yaw clamp** (for lockers, §4), in `CameraRotation()` right after the pitch clamp:

```
[Tooltip("Half-width of the allowed yaw range in degrees. 0 = unlimited.")]
public float YawClampRange = 0f;
public float YawClampCenter = 0f;
...
if (YawClampRange > 0f)
    _cinemachineTargetYaw = YawClampCenter + Mathf.Clamp(Mathf.DeltaAngle(YawClampCenter, _cinemachineTargetYaw), -YawClampRange, YawClampRange);
```

Add a `public void SetYawClamp(float centerYaw, float range)` and `ClearYawClamp()`; `LookAt` must call `ClearYawClamp()` first, or the capture sequence's ease onto the hunter's eyes is clamped to the locker slit.

**`PlayerStamina`** (added to the player in `MazeGenerator.PlacePlayer`, next to `PlayerStealthState`):

```
[SerializeField] private float sprintSeconds = 8f;      // full → empty while sprinting
[SerializeField] private float recoverSeconds = 16f;    // empty → full while not sprinting
[SerializeField] private float recoverDelay = 1.5f;     // pause before recovery starts
[Range(0f,1f)] [SerializeField] private float reengageFraction = 0.25f; // after hitting 0, sprint unlocks here

public float Fraction { get; private set; } = 1f;
public bool  Exhausted { get; private set; }
public bool  IsSprinting => _controller != null && _controller.IsSprinting;
```

- `Update`: if `_controller.IsSprinting`: `Fraction -= dt / sprintSeconds; _delay = recoverDelay; if (Fraction <= 0) { Fraction = 0; Exhausted = true; }`. Else: `_delay -= dt; if (_delay <= 0) Fraction += dt / recoverSeconds;` clamp to 1. `if (Exhausted && Fraction >= reengageFraction) Exhausted = false;` Finally `_controller.SprintLocked = Exhausted;`.
- Hysteresis is required: without `reengageFraction` a held Shift stutters between sprint and walk at zero.
- Nothing else couples to noise: `PlayerStealthState` derives noise from actual speed (sprint threshold 4.5 m/s), so an exhausted player automatically gets quiet.
- Because `Time.deltaTime` is scaled, stamina neither drains nor refills under pause or behind the title screen.

## 4. Lockers — `Locker.cs` (new), `MazeGenerator.cs`, `AIFollower.cs`

### 4a. Choosing and building lockers (`MazeGenerator`)

New fields:

```
[Header("Lockers")]
[SerializeField] private int lockerCount = 4;
[Tooltip("Width across the corridor (m)")]
[SerializeField] private float lockerWidth = 0.9f;
[Tooltip("Depth out from the back wall (m). Keep <= 0.7 so the cell centre stays reachable for the 0.5 m agent.")]
[SerializeField] private float lockerDepth = 0.6f;
[SerializeField] private float lockerHeight = 2.1f;
[SerializeField] private Color lockerTint = new Color(0.16f, 0.17f, 0.19f);
```

New private state: `List<Vector2Int> _lockerCells`, `List<Locker> _lockers`, `List<WallLamp> _lamps`. Expose `public IReadOnlyList<Locker> Lockers`.

**`ChooseLockerCells(System.Random rng)`** — called from `Awake` right after `Generate`/`CacheCellCenters` (it only needs wall flags and `_distance`):
1. Candidates = cells with exactly three walls (`(_wallN?1:0)+(_wallE?1:0)+(_wallS?1:0)+(_wallW?1:0) == 3`), excluding `(0,0)` and `FarthestCell()` (the AI spawn).
2. Shuffle with `new System.Random(usedSeed + 3)` (stars use `+1`, lamps will use `+2`). Pick greedily with `IsFarEnough(candidate, chosen, minSeparation)` where `minSeparation = max(2, (width+height)/4)` (the star rule), second pass without the separation, exactly like `SpawnStars`. Fewer dead ends than `lockerCount` → build what fits, `Debug.LogWarning`.
3. Record for each chosen cell the **open direction** (the one `false` wall) — the locker goes against the opposite wall.

**`BuildLockers()`** — called from `Awake` **between `BuildGeometry` and `BuildRuntimeNavMesh`**, so each locker body is carved out of the navmesh and the hunter paths around it, not through it. Geometry per locker (all parented under one `Locker_x_z` root under `_mazeRoot`, root positioned at the cell centre, rotated so its local +Z points from the back wall toward the opening):
- Back-wall face is at `cellSize/2 − wallThickness/2 = 1.25 m` from the cell centre. Body centre = `back face + depth/2` toward the opening, y = `FloorTop + height/2`.
- Body: `CreateBox`-style cube `width × height × depth` with a runtime `DarkCopy(wallMaterial, lockerTint, "Maze_Locker")`. That is the collider that blocks the player and the hunter's raycast.
- Door: a thin cube `width−0.04 × height−0.04 × 0.04` proud of the front face by 0.02, **same material**, split into three horizontal slats with two 0.06 m gaps at roughly eye height (1.5–1.7 m) so the player can see out. The slats are separate cubes; remove nothing — their colliders are what stop the hunter's LOS raycast from seeing in. (The multiplier is 0 while hidden anyway; the slats are for looks and for the "door opens" beat.) Parent all slats under a `Door` transform whose pivot is on the hinge edge so `Expose()` can swing it open with a rotation.
- A short emissive strip? No — the wall lamp above the locker (§7) is what makes it findable.
- Trigger: `BoxCollider isTrigger` on the root, size `1.4 × 2.0 × 1.2`, centred 0.9 m in front of the door. Layer 0.
- `AddComponent<Locker>()` and `locker.Configure(insidePosition, frontPosition, facingYaw, doorTransform)` where `insidePosition` = body centre at floor height (the player capsule fits: interior 0.9 × 0.6, capsule radius 0.28), `frontPosition` = 1.0 m in front of the door at floor height, `facingYaw` = the world yaw looking out of the locker.
- Walkable depth check: cell walkable span is 2.5 m; locker depth 0.6 leaves 1.9 m, and the cell centre (1.25 m from the back face) is 0.65 m clear of the door — more than the 0.5 m agent radius, so the cell's wander point stays reachable. Keep depth ≤ 0.7.
- `SpawnStars`: skip any cell in `_lockerCells` (add to the `continue` list next to the AI cell).
- `PlaceAI`: after `SetPatrolTargets`, call `aiFollower.SetHidingSpots(frontPositions)` (a `List<Vector3>` of every locker's `FrontPosition`).
- `PlacePlayer`: add `PlayerStamina` if missing. After the rig exists, `stealth.SetLamps(_lamps)` happens in `SetUpAtmosphere` (lamps are built later).

### 4b. `Locker.cs`

```
public interface IHidingSpot { Vector3 FrontPosition { get; } void Expose(); }

public class Locker : MonoBehaviour, IHidingSpot
{
    [SerializeField] private float yawRange = 35f;
    [SerializeField] private float pitchRange = 25f;
    [SerializeField] private float doorOpenDegrees = 110f;
    [SerializeField] private float doorOpenSeconds = 0.25f;

    public Vector3 FrontPosition { get; private set; }
    public bool Occupied { get; private set; }

    public void Configure(Vector3 insidePosition, Vector3 frontPosition, float facingYaw, Transform door);  // from BuildLockers
    public void Bind(Transform player, PlayerStealthState stealth, Flashlight flashlight, PlayerHud hud);   // from SetUpAtmosphere
}
```

Do **not** use a static for the prompt (CLAUDE.md static gotcha). `Bind` receives the HUD and calls `hud.SetPrompt("E  HIDE")` / `hud.SetPrompt(null)` from `OnTriggerEnter/Exit` and on enter/leave. Both `OnTrigger*` filter `other.CompareTag("Player")` **and** `other.GetComponent<CharacterController>() != null` (two objects carry the Player tag — `AIFollower.FindTarget` comment).

Update (reads input directly like `Flashlight`):
```
bool pressed = Keyboard.current?.eKey.wasPressedThisFrame == true || Gamepad.current?.buttonWest.wasPressedThisFrame == true;
if (!pressed || !GameFlow.IsRunActive || GameOutcome.IsOver) return;
if (Occupied) Leave(); else if (_playerInTrigger) Enter();
```

**`Enter()`**
1. `_stealth.SetHidden(this)`; `Occupied = true`.
2. `_flashlight.ForceOff(); _flashlight.InputEnabled = false;` (a torch inside a locker would leak light; and `PauseMenu.Resume` sets `InputEnabled = true` — so on `Resume` the torch key would work again inside the locker. Guard: `Flashlight.Update` also checks `_stealth != null && _stealth.Hidden` before honouring F. Cheap and robust.)
3. Teleport: `cc.enabled = false; player.position = _insidePosition; player.rotation = Euler(0, facingYaw, 0); cc.enabled = true;`
4. `controller.MovementLocked = true; controller.SetYawClamp(facingYaw, yawRange); _savedBottom = controller.BottomClamp; … BottomClamp = -pitchRange; TopClamp = pitchRange;` and `controller.ResetCameraRotation(facingYaw)` is **not** used (it logs and zeroes pitch); instead call `controller.LookAt(frontPosition + Vector3.up * 1.6f, 1f)` once so the view starts looking out through the slit — then the clamp holds it.
5. `_hud.SetPrompt("E  LEAVE")`.

**`Leave()`** — the reverse: teleport to `FrontPosition`, `MovementLocked = false`, `ClearYawClamp()`, restore pitch clamps, `_flashlight.InputEnabled = true` (torch stays off; the player chooses), `_stealth.SetHidden(null)`, `Occupied = false`, prompt back to `E  HIDE` while still in the trigger.

**`Expose()`** (called by `AIFollower` when it busts the player, and harmless if called twice): swing `Door` open over `doorOpenSeconds` (coroutine, `localRotation` about the hinge), `controller.ClearYawClamp()` and restore pitch clamps so `GameOutcome.CaptureSequence`'s `LookAt` can turn onto the hunter's eyes; leave the player inside and hidden-flag cleared (`_stealth.SetHidden(null)`) so `IsOn`/subtitle logic behaves. Set a `WasExposed` flag that `GameOutcome` can read for the subtitle (§9).

Pause/leave interactions: `PlayerLock.Freeze(player, true)` sets `MovementLocked`; `Freeze(false)` clears it — which would **unlock movement inside a locker after Resume**. Fix in `PlayerLock.Freeze`: only clear `MovementLocked` if the stealth state is not hidden: `controller.MovementLocked = frozen || (stealth != null && stealth.Hidden)`. One line, and `PlayerLock` already fetches components off the player.

Escape-hatch drop while hidden is impossible (hatch trigger is a distance test to the player, who is inside a locker ≥ 12 m from where the hatch spawned relative to the player — but not guaranteed). Guard anyway: `MazeEscape.Update` skips the hatch test while `stealth.Hidden`.

### 4c. `AIFollower.cs`

```
[Header("Speed cap")]
[Tooltip("Every speed is clamped to the player's sprint speed times this. It must never be able to out-sprint the player.")]
[Range(0.5f, 0.99f)] [SerializeField] private float maxSpeedFraction = 0.86f;

[Header("Hiding spots")]
[Tooltip("How close a hiding spot has to be to the last known position to be checked during a search")]
[SerializeField] private float hidingSpotSearchRadius = 6f;

private readonly List<Vector3> _hidingSpots = new List<Vector3>();
private bool _bustingHidingSpot;   // saw the player go in; capture on arrival

public void SetHidingSpots(IEnumerable<Vector3> fronts)   // like SetWanderPoints: before Awake, no _agent
```

**Speed clamp** in `Start()`, after `target`/`_stealth` are resolved:
```
var player = target != null ? target.GetComponent<StarterAssets.ThirdPersonController>() : null;
if (player != null)
{
    float cap = player.SprintSpeed * maxSpeedFraction;
    if (runSpeed > cap || moveSpeed > cap) Debug.LogWarning($"AIFollower: speeds clamped to {cap:0.00} m/s (player sprint {player.SprintSpeed}).", this);
    runSpeed = Mathf.Min(runSpeed, cap);
    moveSpeed = Mathf.Min(moveSpeed, cap);
    dormantSpeed = Mathf.Min(dormantSpeed, cap);
}
```
Also lower the code default `runSpeed` to `4.5f` (it must sit under the 4.588 cap or Start logs the clamp warning) and fix its tooltip ("Matches the player's sprint speed" is now false). `moveSpeed` (3.5 in the scene) is under the cap and untouched. Because `MoveSpeedTowards` is the only write to `_agent.speed` and every caller goes through `ChaseSpeed()/WanderSpeed()/runSpeed`, clamping the three fields covers `UpdateChaseOnly` too.

**Saw-you-enter rule.** In `UpdateStateMachine`, before the `_hasSight` promotion:
```
bool hiddenNow = _stealth != null && _stealth.Hidden;
if (hiddenNow && !_wasHidden && _state == State.Chase && _hasSight) _bustingHidingSpot = true;   // it watched you climb in
if (!hiddenNow) _bustingHidingSpot = false;
_wasHidden = hiddenNow;
```
Note `_hasSight` is refreshed every 0.1 s, so read the cached value — at the moment `Hidden` flips, the last reading still says "seen". Then in `TickChase`:
- Keep `_state = Chase` while `_bustingHidingSpot` even though `_hasSight` is now false: change `else if (_state == State.Chase)` timeout branch to `else if (_state == State.Chase && !_bustingHidingSpot)`.
- Destination: `_bustingHidingSpot ? _lastKnownPosition : (_hasSight ? target.position : _lastKnownPosition)` (already the last-known path; `_lastKnownPosition` was set to the player's position on the last sighted frame, i.e. at the locker's front).
- Capture gate becomes `GameFlow.IsRunActive && (_hasSight || _bustingHidingSpot) && flatDistance² ≤ bustRadius²` where `bustRadius = captureRadius + 0.8f` while busting (the player is 0.6 m deeper inside the locker than the door the agent stops at). On capture while busting: `_stealth.CurrentHidingSpot?.Expose();` **before** `PlayerCaught?.Invoke()`.
- Reset `_bustingHidingSpot = false` in `EnterCaptured` and whenever the player leaves the locker (`!hiddenNow`).

**Search visits lockers.** In `EnterSearch`, after inserting `_lastKnownPosition` at index 0: for each hiding spot within `hidingSpotSearchRadius` of `_lastKnownPosition`, `_searchQueue.Insert(1, spot)` (nearest first). `TickSearch` already dwells and turns at each point; while dwelling at a hiding spot it should **face the locker** rather than spin: store the spot in a `HashSet<Vector3>`/parallel flag and, if the current queue head is a hiding spot, `UpdateRotation(spot − transform.position)` instead of the 120°/s turn. That is the "it stops in front of your locker and stares" beat. With `Hidden` visibility at 0 it cannot see you; it moves on.

**Hearing while hidden** needs nothing: velocity is zero inside.

## 5. `MazeEscape.cs` — hatch cue

New fields:
```
[Header("Wayfinding")]
[SerializeField] private float pingInterval = 2f;
[SerializeField] private float pingVolume = 0.7f;
[Tooltip("Must exceed the maze diagonal (~47 m for 11x11 at 3 m) so the ping is audible from anywhere")]
[SerializeField] private float pingMaxDistance = 60f;
[SerializeField] private float chevronRadius = 70f;
```

- `BuildHatch`: add an `AudioSource` on the `HatchLight` holder (`spatialBlend 1`, `rolloffMode Linear`, `minDistance 3`, `maxDistance pingMaxDistance`, `dopplerLevel 0`, `playOnAwake false`). Clip: `BuildPingClip()` — a 0.35 s tone sweeping 880 → 660 Hz with an exponential decay and the 3 ms fade-in from `AddThump`; normalise to 0.8 like the heartbeat. Make `AddThump`/the normalise helper `internal static` on `HorrorAudioDirector` or copy the 20 lines; copying is fine.
- `Update` (while `_running`): `_pingTimer -= dt; if (_pingTimer <= 0) { _pingTimer = pingInterval; _ping.PlayOneShot(clip, pingVolume); _chevronPulse = 1f; }`. The listener is on the camera, so left/right panning tracks where the player is looking — that is the direction cue. Interval shortens with proximity: `pingInterval * Mathf.Lerp(0.5f, 1f, Clamp01(distance / 20f))`.
- **Bearing chevron**: in `BuildTimerText`, also create `RuntimeUi.CreateText(canvas, "HatchBearing", "▲", 34f, hatchColor)` (TMP's default font has the glyph; if it renders as a box, use `"^"`) parented **under the timer's holder** so it hides with it. Each frame: bearing = `SignedAngle(cameraFlatForward, hatchDir, up)`; place at `anchoredPosition = (sin(b), cos(b)) * chevronRadius` relative to the timer centre, `localRotation = Euler(0,0,-b)`; alpha = `0.35 + 0.65 * _chevronPulse`, `_chevronPulse` decays at 3/s. Camera = `Camera.main` (the rig makes it a child of the head, so its yaw is the look yaw).
- `Stop()`: `if (_ping != null) _ping.Stop();` — `GameOutcome.EndTheHunt` does not know about this source. Pause is covered by `AudioListener.pause`.
- Skip the hatch-reach test while `_stealth.Hidden` (§4b). `Configure` gets the `PlayerStealthState` from `player.GetComponent`.

## 6. `PlayerHud.cs` (new)

`[DefaultExecutionOrder(-84)]`, added in `SetUpAtmosphere` right after `TensionDirector`, built in `Start` so it sits under the title screen. `Configure(Flashlight flashlight, PlayerStamina stamina)`; either may be null (no rig).

- **Battery bar**: bottom-left, label `TORCH`, a 220×10 fill `Image` inside a dark track; fill width = `Charge`; colour lerps `calm (0.86,0.86,0.9)` → `alarmed (0.9,0.15,0.1)` below 30 %; below 10 % the label blinks at 2 Hz; at 0 the label reads `TORCH DEAD` and stays red.
- **Stamina bar**: directly above it, label-less, same size, off-white; alpha fades to 0 within 1 s of being full and back to 1 the moment it drains; turns red while `Exhausted`.
- **Prompt**: bottom-centre text (`E  HIDE` / `E  LEAVE`), 34 pt, hidden when null. `public void SetPrompt(string text)`.
- All widgets use `RuntimeUi.CreateText`/`CreatePanel`/`Place`. An `Image` with `Image.Type.Filled` is the easiest fill; `RuntimeUi` has no image helper beyond `CreatePanel`, so either add `CreateBar(parent, name, size, trackColor, fillColor)` to `RuntimeUi` or inline it here.
- `Update` only reads; it does not gate on `IsRunActive` (values do not change while frozen anyway).

## 7. Wall lamps — `MazeGenerator.BuildWallLamps()` + `WallLamp.cs` (new)

Fields on `MazeGenerator`:
```
[Header("Wall lamps")]
[SerializeField] private bool wallLamps = true;
[Tooltip("Roughly one lamp per this many cells, plus one above every locker")]
[SerializeField] private int cellsPerLamp = 5;
[SerializeField] private float lampHeight = 2.6f;
[SerializeField] private Color lampColor = new Color(1f, 0.72f, 0.42f);
[SerializeField] private float lampRange = 6f;
[SerializeField] private float lampIntensity = 1.1f;
[Range(0f,1f)] [SerializeField] private float faultyLampChance = 0.25f;
```

`BuildWallLamps()` runs from `Awake` **after `BuildCeiling`** (after the bake; the fixture sits at 2.6 m, above agent height, so it would not carve anyway, but keep the "after the bake" rule for anything that is not a wall).
1. `rng = new System.Random(usedSeed + 2)` so a same-seed retry gets the same lamps.
2. Cell list = every locker cell (mandatory) + a shuffled pick of `width*height / cellsPerLamp` other cells (skip `(0,0)`? No — a lamp at spawn is a good tutorial: it shows the player what a lamp is. Keep it eligible.)
3. For each cell pick a wall that exists (for a locker cell: the back wall, centred above the locker). Mount point = wall face + 0.08 m into the corridor, `y = FloorTop + lampHeight`, at the wall's midpoint along the corridor.
4. Fixture: a `0.28 × 0.10 × 0.14` cube (no collider needed — `Destroy(GetComponent<Collider>())` like the hatch panel) with an **emissive** runtime material: `new Material(FindStarMaterial())` tinted `lampColor`, `_EmissionColor = lampColor * 2.5f`, `EnableKeyword("_EMISSION")`, registered in `_runtimeMaterials`. One shared material for all lamps.
5. `Light`: Point, `lampColor`, `range lampRange`, `intensity lampIntensity`, **`shadows = LightShadows.None`**. Optional: a small downward `Spot` instead of a point reads more like a sconce, but a point is cheaper to reason about; start with point.
6. `AddComponent<WallLamp>().Configure(light, fixtureRenderer, baseIntensity, faulty: rng.NextDouble() < faultyLampChance, phase: rng.Next(1000))`. Collect into `_lamps`.
7. Under Forward+ there is no per-object light cap, so ~28 lamps + 5 star lights + hatch + torch are fine. If the project were ever moved back to Forward, the single-box floor would cap at 4 lights and this feature would need the floor tiled per cell — note it in the field tooltip.

`WallLamp`:
```
public float Range { get; }
public float CurrentIntensity01 { get; }   // 0..1 of base, after flicker/blink

private void Update()
{
    float n = Mathf.PerlinNoise(Time.time * 6f + _phase, 0.3f);   // 0.85..1.0 gentle waver
    float k = 0.85f + 0.15f * n;
    if (_faulty) { /* every 3–9 s (rng from phase) drop to 0 for 0.08–0.35 s, sometimes twice */ }
    _light.intensity = _base * k;
    _renderer.material emission scaled the same way — use a MaterialPropertyBlock so the shared material is untouched.
}

public static float ExposureAt(IReadOnlyList<WallLamp> lamps, Vector3 point)
{
    float best = 0f;
    foreach (var lamp in lamps) { float d = Vector3.Distance(lamp.transform.position, point);
        if (d < lamp.Range) best = Mathf.Max(best, (1f - d / lamp.Range) * lamp.CurrentIntensity01); }
    return best;
}
```
`PlayerStealthState.SetLamps(_lamps)` is called from `SetUpAtmosphere`. A blinking lamp therefore momentarily hides the player standing under it — a free dramatic beat.

## 8. `MazeGenerator.SetUpAtmosphere` wiring (final shape)

```
… existing HorrorAtmosphere / HorrorAudioDirector.Bind / TensionDirector.Configure …

PlayerHud hud = FindAnyObjectByType<PlayerHud>() ?? gameObject.AddComponent<PlayerHud>();
Transform player = FindPlayer();
FirstPersonRig rig = …; Flashlight flashlight = …;
PlayerStamina stamina = player != null ? player.GetComponent<PlayerStamina>() : null;
PlayerStealthState stealth = player != null ? player.GetComponent<PlayerStealthState>() : null;
hud.Configure(flashlight, stamina);
if (stealth != null) stealth.SetLamps(_lamps);
foreach (Locker locker in _lockers) locker.Bind(player, stealth, flashlight, hud);

… GameOutcome / MazeEscape / MainMenu / PauseMenu as today …
```

Awake order becomes: `Generate → CacheCellCenters → ChooseLockerCells → BuildGeometry → BuildLockers → BuildRuntimeNavMesh → BuildCeiling → BuildWallLamps → DisableExistingPickups → SpawnStars → PlacePlayer → PlaceAI → SetUpAtmosphere`.

## 9. `GameOutcome.cs` and `MainMenu.cs`

- `GameOutcome.ShowEndScreen`, Caught branch: if the player's `CurrentHidingSpot`/`WasExposed` says they were pulled out of a locker, subtitle `"It watched you hide."`; else keep the torch-based pair. Read the flag via `_player.GetComponent<PlayerStealthState>()` at ending time (store it before `SetHidden(null)` is cleared — simplest: `Locker.Expose()` sets `stealth.LastExposed = true`, a plain property that nothing resets during the run).
- `GameOutcome.Lose(OutOfTime)` and the win path: nothing changes. The capture flicker calls `SetOn(true)` on a possibly dead torch — `Flashlight.SetOn` now refuses, so the sequence just plays dark.
- `MainMenu.BuildRulesScreen` controls line → `WASD move   SHIFT sprint   MOUSE look   F flashlight   E hide`, and a second line: `Your torch has three minutes of light in it. Your legs have less. Lockers hide you — unless it saw you climb in.` Keep the existing `<size=30>` styling.

---

## Execution order and lifetime summary

| Order | Component | Notes |
|---|---|---|
| −100 `Awake` | `MazeGenerator` | chooses locker cells, builds lockers **before** the bake, lamps after the ceiling, adds `PlayerStamina` to the player, `AddComponent`s `PlayerHud`, hands lamps/lockers/hiding spots through `Configure`/`Set*` |
| −90 `Awake` | `FirstPersonRig` | creates `Flashlight` (battery starts full) |
| −85 `Start` | `TensionDirector` | star counter |
| −84 `Start` | `PlayerHud` | battery + stamina bars + prompt, under the menus |
| −80 `Start` | `MainMenu` | title over everything |
| default | `PlayerStamina`, `PlayerStealthState`, `WallLamp`, `Locker`, `Flashlight` | all `deltaTime`-driven or gated on `GameFlow.IsRunActive` |
| default `Start` | `AIFollower` | clamps speeds to the player's sprint × 0.86 and logs if it had to |
| play | `AIFollower` | busts a seen hider → `Locker.Expose()` → `PlayerCaught` |
| end | `GameOutcome` | `MazeEscape.Stop()` now also stops the ping |
| reload | `GameFlow` | no new statics; all new state dies with the scene |

## Compile check

`CLAUDE.md`'s batch-mode command only works when the Editor is closed (it needs `Temp/UnityLockfile`). With the Editor open, either save and read `Logs/Editor.log` (`grep "error CS" Logs/Editor.log`) or drive Unity's bundled Roslyn directly — the exact recipe (response file, `Library/ScriptAssemblies` references, known pitfalls) is in `C:\Users\adria\.claude\projects\D--Unity-essentials-My-project\memory\project-compile-check-editor-open.md`. Expect `CS0649` noise on `[SerializeField]` fields; anything else with `error CS` is real.

## Acceptance checklist

Run in the Editor with the Console visible. "Seed" = the value in `MazeGenerator: building a 11x11 maze with seed N`.

**Speed cap**
- [ ] Console shows `AIFollower: speeds clamped …` only if an Inspector value exceeds the cap (with the new `runSpeed 4.5` default it should not log).
- [ ] Open the hatch, get spotted in a long straight corridor with full stamina and sprint away: the gap **grows** slowly for ~8 s. Stop sprinting: it closes.
- [ ] Set `maxSpeedFraction` to 0.99 and `runSpeed` to 9 in the Inspector at runtime: the agent's speed in the NavMeshAgent inspector never exceeds ~5.28.

**F5 — battery**
- [ ] HUD `TORCH` bar starts full; drains only while the torch is on; frozen under pause and on the title screen (compare `SecondsLeft` before/after a 10 s pause).
- [ ] Set `batterySeconds` to 20 in the Inspector: at ~4 s left the beam stutters and dims; at 0 it dies, F does nothing, the label reads `TORCH DEAD`.
- [ ] Get caught with a dead torch: the capture sequence plays fully dark, no exception, the subtitle is the "never heard it coming" one.
- [ ] With the torch dead the maze is still navigable by lamp light (see below).

**F6 — stamina**
- [ ] Hold Shift+W: the bar empties in ~8 s, speed drops to walking, footstep cadence slows, the bar turns red; Shift does nothing until ~25 %; refill starts 1.5 s after release and takes ~16 s from empty.
- [ ] Holding Shift against a wall does not drain (no movement input → `IsSprinting` false). Holding Shift while stationary does not drain.
- [ ] Bar is invisible when full and fades in on first use.

**F7 — lockers**
- [ ] With the Scene view open, count lockers = 4 (11×11), each in a dead end against the back wall, never in the start cell, the AI's cell, or a star cell; each has a lamp above it.
- [ ] Same seed → same locker cells.
- [ ] The hunter never clips through a locker body (it is carved from the navmesh: check the NavMesh gizmo).
- [ ] Walk into the trigger: `E  HIDE` prompt. Press E: view snaps inside looking out through the slit; WASD does nothing; look is limited; the torch is off and F is dead; prompt reads `E  LEAVE`. E again: you step out in front, controls back, torch still off but F works.
- [ ] Pause while hidden, resume: still locked in the locker, F still dead.
- [ ] Hide **unseen** with the hunter searching nearby: it walks to the locker, stands facing it ~1 s, walks away. No capture.
- [ ] Hide **while it is chasing you with sight**: it comes straight to the locker, the door swings open, camera turns onto its eyes, `IT FOUND YOU` with subtitle `It watched you hide.`
- [ ] Hide, then let the hunter walk past unaware with the torch previously on: no sting, no chase (visibility 0).
- [ ] Time out while hidden (post-hatch): `OUT OF TIME` shows and the door/clamps do not leave the camera stuck.

**F9 — hatch cue**
- [ ] When the hatch opens, a ping sounds every ~2 s, audibly panned toward the hatch as you turn your head; it quickens as you get close.
- [ ] A chevron orbits the countdown pointing at the hatch's bearing and pulses with each ping.
- [ ] Ping stops on win, on either lose, and is silent under pause. Not audible before the hatch opens.

**Wall lamps**
- [ ] ~28 warm lamps at 2.6 m on real walls (none floating in an opening), plus one above each locker; roughly a quarter of them flicker or blink occasionally.
- [ ] Same seed → same lamp placement.
- [ ] No lamp casts shadows (Light inspector: Shadow Type = No Shadows). The flashlight's shadows look the same as before.
- [ ] Torch off, standing under a lamp: the hunter spots you from noticeably further than in a dark corridor (compare against `viewDistance × 0.35 ≈ 6.3 m` vs up to `× 0.8 ≈ 14 m`).
- [ ] Frame rate on the PC pipeline is not visibly worse than before (Forward+; no rendering mode change was made).

**Regression**
- [ ] Three restarts in a row: no `MissingReferenceException`, no leaked `Maze_Locker`/lamp materials warning on exit, no doubled ping.
- [ ] `MazeGenerator.escapeSequence` unticked: lockers, lamps, battery, stamina all still work; no ping.
- [ ] `MazeGenerator.mainMenu` unticked: HUD visible from frame one.

## Out of scope (deliberately)

- Battery pickups / recharging (F5 optional) — ruled out by the 3-minute total.
- Throwables (F8), player breathing/footsteps (F18), remappable bindings for F/E/Esc (F17).
- Locker door animation on normal enter/leave (only the bust opens it) and any locker interior model.
- Difficulty presets (F10) — `batterySeconds`, `sprintSeconds`, `lockerCount`, `maxSpeedFraction` are the knobs they will drive.
- Updating `CLAUDE.md` for the new systems — do it as its own change after this lands.

<div align="center">

# L I G H T S &nbsp; O U T

**Collect. Escape. Don't be seen.**

A first-person horror maze game for Unity 6.<br>
Five floors. One flashlight. Something is hunting you.

![Unity](https://img.shields.io/badge/Unity-6000.6.2f1-black?logo=unity)
![Pipeline](https://img.shields.io/badge/render%20pipeline-URP%2017.6-blue)
![Input](https://img.shields.io/badge/input-Input%20System-lightgrey)
![Platform](https://img.shields.io/badge/platform-Windows-informational)

</div>

---

## The game

You are dropped into a pitch-black maze with a flashlight that will not last. Every star in the maze has to be collected. Every star you take makes the maze emptier, and the thing walking it more certain of where you are.

When the last star is yours, a hatch opens somewhere in the dark and a clock starts. Find it. Drop through. Spend what you found on the way in a strange little shop between floors. Then do it again, deeper.

There are five floors. Each one is bigger, darker and meaner than the last.

- **Procedural.** A new maze every run, built from a seed. Die, and you can retry the *same* maze or roll a fresh one.
- **Hunted.** The hunter has senses: a sight cone with line of sight, hearing driven by how fast *you* are actually moving, a memory of where it last saw you, and a habit of drifting toward the stars you haven't taken yet.
- **Dreadful.** The maze fights dirty. Lamps ripple and die. Footsteps that aren't the hunter's fall in behind you. The hunter relocates along your own trail. The last star arrives in silence.
- **Economical.** Shards glint in the nooks of the maze. Between floors, someone will sell you a torch battery, a second chance, or quieter shoes.

## How to play

1. Collect **every star**. The counter in the corner shows what's left, and it changes colour as the maze gets emptier.
2. A **hatch** opens at least 12 m from where you stand, and a countdown starts (**120 s on floor 1, 60 s on floor 5**). Reach it.
3. If the hunter **sees** you, run. If it **catches** you, it's over.
4. Clear floors 1–4 to fall through to **the shop**. Clear floor 5 to escape for good.

### Controls

| Action | Keyboard & mouse | Gamepad |
|---|---|---|
| Move | `W` `A` `S` `D` | Left stick |
| Look | Mouse | Right stick |
| Sprint | `Shift` | Left trigger |
| Flashlight | `F` | — |
| Hide in a locker / talk / leave the shop | `E` | West button (X / Square) |
| Use spare battery | `1` | D-pad left |
| Use star compass | `2` | D-pad right |
| Pause | `Esc` | Start |
| Close the shop menu | `Esc` | East button (B / Circle) |

> The flashlight, pause and interact bindings read the keyboard and gamepad directly rather than going through the Input System action map, so they can't be remapped yet.

### Survival notes

- **Your torch holds three minutes of light.** Once the battery dies, it's dead for the run unless you bought a spare.
- **Sprint is a budget, not a right.** A full bar buys 14 s of sprint on floor 1 and only 8 s on floor 5. Empty it and you're locked out until it climbs back to a quarter.
- **Lockers hide you.** Unless the hunter saw you climb inside.
- **Noise carries.** Footsteps are measured from your real velocity, so creeping is quieter than sprinting, and the torch changes how visible you are.
- **Lamps are a trade.** Lit corridors are easier to read and easier to be seen in.

## The five floors

Difficulty isn't Easy / Medium / Hard. It's *how far in you are*. Every number below is blended between a gentle floor 1 and a merciless floor 5 in a single place, `FloorProfile`.

| Floor | Look | Maze | Stars | Lockers | Hunter walks / runs | Sees to | Hatch timer |
|:-:|---|:-:|:-:|:-:|:-:|:-:|:-:|
| 1 | **The Ward** | 7 × 7 | 2 | 5 | 1.6 / 1.8 m/s | 9 m | 120 s |
| 2 | **Boiler Deck** | 8 × 8 | 3 | 6 | 2.1 / 2.5 m/s | 11 m | 105 s |
| 3 | **The Crypt** | 9 × 9 | 4 | 8 | 2.6 / 3.2 m/s | 14 m | 90 s |
| 4 | **The Lab** | 10 × 10 | 4 | 9 | 3.0 / 3.8 m/s | 16 m | 75 s |
| 5 | **The Hollow** | 11 × 11 | 5 | 10 | 3.5 / 4.5 m/s | 18 m | 60 s |

Floors 2–4 are linear blends of floor 1 and floor 5 (rounded here); `FloorProfile.For(floor)` is authoritative. For reference, you walk at 2.0 m/s.

Each floor also has its own wall, floor and ceiling pieces, pillars, lamp fixtures, lockers and set-dressing props, so they read as different places rather than a tint change.

### The hunter

The hunter is a small state machine with escalating behaviour:

| State | What it does |
|---|---|
| **Wander** | Patrols, biased toward stars you haven't collected yet. |
| **Chase** | Has sight of you. The only state in which it can capture you. |
| **Search** | Lost you. Sweeps your last known position. |
| **Stare** | *Floors below the last.* On sighting you it rushes to a vantage point and watches, red-faced, advancing only while you look away. It breaks into a chase if you get too close or it waits too long. |
| **Ambush** | Waits two cells around a corner from the star you're heading for. Ended by you taking the star, by sight, by a heard noise, or by a timeout. |
| **Captured** | You lost. The camera eases onto its eyes and the torch dies. |

The stare/ambush mix is tuned per floor: early floors lean on staring and short ambushes, while on floor 5 sight is simply a chase and ambushes last longest.

## The shop

Between floors you fall into a small room west of the maze. A payout ledger tallies what you earned, then a salesman deals in **Shards**.

**Payout:** 10 per star, 5 per shard, up to 40 for time left on the hatch clock, plus a clear bonus. Dying still pays a 35 % consolation on stars taken, and both consolation and maze shards are capped per floor so retrying the same seed can't be farmed.

| Item | Price | Effect |
|---|:-:|---|
| Spare Battery | 30 | Refills the torch and switches it on. Hotkey `1`. Hold up to 3. |
| Star Compass | 25 | Points at the nearest star for five seconds. Hotkey `2`. Hold up to 3. |
| **Second Wind** | 120 | When it catches you, it lets go. Once. |
| Long Fuse | 35 | +10 s on the next hatch timer. |
| Quiet Shoes | 40 | Your footsteps carry a third less, next floor. |
| Extra Lamps | 35 | More lamps lit on the next floor. |
| Stamina Tonic | 30 | +4 s of sprint, next floor. |
| Torch Colour | 50 | Cold, Ember or Violet beam. |
| HUD Tint | 25 | Phosphor, Amber or Ice interface. |

Base prices rise **20 % per floor** after the first, so the late game is roomier but never lets you buy everything. Walk through the shop's door (`E`) when you're done.

## Getting started

### Requirements

- **Unity `6000.6.2f1`** (via Unity Hub)
- Windows (the project and its tooling are set up for Windows; nothing in the game code is platform-specific)

### Run it

1. Clone the repository and add the folder to Unity Hub.
2. Open it with editor version `6000.6.2f1`.
3. Open `Assets/Scenes/GetStarted_Scene.unity`. It is the only scene in Build Settings.
4. Press **Play**.

There is nothing to wire up. The scene is nearly empty by design.

### Command-line compile check

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe" `
    -batchmode -quit -projectPath "<path to this folder>" -logFile -
```

If the Editor is already open it holds the project lock, so instead read the last compile from `Logs/Editor.log`:

```bash
grep "error CS" Logs/Editor.log
```

There is no automated test suite yet.

## How it's built

### Nearly everything is made at runtime

`MazeGenerator.Awake` (execution order −100) builds the whole game: walls, floor, ceiling, the NavMesh bake, the stars, the first-person rig, the flashlight, and every director component, each added to the `MazeGenerator` GameObject and wired with a `Configure` / `Bind` call. **`MazeGenerator.SetUpAtmosphere` is the composition root.** New systems get created and configured there. Don't look for Inspector wiring to explain behaviour.

Two pieces are authored in the scene so an artist can select and retouch them:

| Authored piece | Where | Built by |
|---|---|---|
| **Shop room** | `ShopRoom` object, west of the maze | `LIGHTS OUT ▸ Build Shop Room` |
| **Floor-theme gallery** | `FloorThemes` object, north of the maze, one row per floor | `LIGHTS OUT ▸ Build Floor Themes` |

At Play, each floor **clones the scene instances** of its theme row (so unapplied overrides count) and then hides the gallery. If either piece is missing, the game logs an error and falls back: the maze to the old primitive look, the shop to a plain win.

A third editor tool, `LIGHTS OUT ▸ Build Hunter Body`, swaps the placeholder robot for the Adam character as the hunter.

### Systems at a glance

| Layer | Components |
|---|---|
| **World** | `MazeGenerator`, `FloorProfile`, `FloorTheme` / `FloorThemeSet`, `Locker`, `WallLamp` |
| **Player** | `FirstPersonRig`, `Flashlight`, `PlayerStealthState`, `PlayerStamina`, `ThirdPersonController` (Starter Assets) |
| **Hunter** | `AIFollower`, `AIPresence` |
| **Dread** | `HorrorAtmosphere`, `HorrorAudioDirector`, `DreadDirector`, `PhantomDirector`, `TensionDirector` |
| **Economy** | `ShopCatalogue`, `PlayerWallet`, `PlayerInventory`, `ShardPickup`, `ShopRoom`, `ShopMenu`, `ConsumableController` |
| **Flow & UI** | `GameFlow`, `GameOutcome`, `MainMenu`, `PauseMenu`, `PlayerHud`, `PlayerLock`, `RuntimeUi` (every UI element is built in code) |

### Run lifecycle

```
MainMenu (time frozen) ─ Start ─▶ run begins
        │
        ▼
  collect stars ──▶ last star ──▶ hatch opens + countdown
                                       │
        ┌──────────────────────────────┼───────────────────────────┐
        ▼                              ▼                           ▼
  reach hatch, floor 1–4       reach hatch, floor 5       timer hits 0 / caught
   fall ▸ shop ▸ next floor          WIN                        LOSE
```

Every ending goes through `GameOutcome`, which is single-shot: a hatch drop that lands mid-capture is ignored.

### Things that will bite you

- **Statics must survive a scene reload but not Play mode.** `GameFlow`, `GameOutcome`, `PlayerWallet` and `PlayerInventory` reset in a `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`, because with *Reload Domain* disabled a stale flag would skip the title screen on the next run. Any new static run state needs the same.
- **Unsubscribe every static event** in `OnDestroy` / `OnDisable`. Restart is a plain `SceneManager.LoadScene`, and a missed unsubscribe leaks across reloads.
- **`ShardPickup` is deliberately not a `Pickup`.** `GameManager` counts `Pickup`s as stars at `Start`; a shard sharing the type would corrupt the count and could fire the win early.
- **`Time.time` is the run clock** and stands still at `timeScale = 0`, which is why run time is net of menu and pause. Don't swap it for `Time.unscaledTime`.
- **The cursor fight.** `StarterAssetsInputs` re-locks the cursor on focus change, so anything showing a menu must call `PlayerLock.SetCursorFree(true)` *every frame*. Freeze the player through `PlayerLock.Freeze`.
- **Capture needs sight, not just distance.** A bare radius test captures through a 0.5 m wall.
- **Input System only.** Keep the `#if ENABLE_INPUT_SYSTEM` guards in the Starter Assets scripts.
- **Keep `.meta` files with their assets.** Scenes and prefabs are text YAML; hand edits must preserve GUID references.

`CLAUDE.md` at the repo root carries the deeper architecture notes, including the execution-order table.

## Project layout

```
Assets/
├─ Scenes/GetStarted_Scene.unity   the one scene; nearly empty by design
├─ SourceFiles/
│  ├─ Scripts/                     all game code (compiles into Assembly-CSharp)
│  ├─ StarterAssets/, InputSystem/ third-person controller + input
│  ├─ Themes/                      per-floor materials and prefabs (1_Ward … 5_Hollow)
│  ├─ Animation/, Models/, Materials/, Textures/, SoundFX/, ...
│  └─ Settings/                    URP assets (PC and Mobile)
├─ Editor/                         the LIGHTS OUT ▸ Build … menu items
├─ Audio/, Materials/, Prefabs/, VFX/, Skyboxes/
└─ UnityTechnologies/              Adam Character Pack (third-party)
```

There are no assembly definitions, so runtime scripts compile into `Assembly-CSharp` and `Assets/Editor` into `Assembly-CSharp-Editor`.

## Credits & licence

- Built on the Unity **Essentials** starter project: the Starter Assets first/third-person controller and the Timmy robot come from it.
- The hunter's body uses the **Adam Character Pack** by Unity Technologies, a *Restricted Asset* licensed for **personal, non-commercial use only** (see `Assets/UnityTechnologies/Adam Character Pack/license.txt`). **Any commercial release needs that asset replaced.**
- This repository does not yet declare a licence for the game's own code. Until one is added, all rights are reserved by the author.

using System.Collections;
using System.Collections.Generic;
using StarterAssets;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Builds a procedural maze at Play time, bakes a runtime NavMesh over it, then moves the player,
/// the AI follower and the collectible stars inside it.
///
/// Everything happens in Awake (execution order -100) because other components capture state in Start:
/// GameManager counts the Pickup objects, RespawnPlayer records the spawn position.
/// </summary>
[DefaultExecutionOrder(-100)]
public class MazeGenerator : MonoBehaviour
{
    [Header("Maze")]
    [Tooltip("Number of cells along X")]
    [SerializeField] private int width = 11;
    [Tooltip("Number of cells along Z")]
    [SerializeField] private int height = 11;
    [Tooltip("Corridor pitch in metres. The walkable corridor is cellSize - wallThickness. Raised at Awake if it would make the halls narrower than minCorridorWidth.")]
    [SerializeField] private float cellSize = 4.5f;
    [Tooltip("Narrowest walkable hall allowed (m). cellSize is forced up to minCorridorWidth + wallThickness if needed.")]
    [SerializeField] private float minCorridorWidth = 4f;
    [Tooltip("Open a wall at every dead end so the maze has loops and nowhere to get cornered")]
    [SerializeField] private bool removeDeadEnds = true;
    [SerializeField] private float wallThickness = 0.5f;
    [Tooltip("At 1.6 m eye height, 3 m walls read as low and leak the layout")]
    [SerializeField] private float wallHeight = 4f;
    [Tooltip("0 = a different maze every run (the seed used is logged)")]
    [SerializeField] private int seed = 0;
    [Tooltip("Bottom-left corner of the maze. Y is the floor top. Keep well clear of the tutorial props: the navmesh bake volume pads 2 m beyond the walls, and anything it catches becomes walkable.")]
    [SerializeField] private Vector3 origin = new Vector3(-70f, 4.98f, -70f);
    [Tooltip("A ceiling is what stops the unfoggable skybox showing above the walls")]
    [SerializeField] private bool buildCeiling = true;

    [Header("Materials")]
    [SerializeField] private Material wallMaterial;
    [SerializeField] private Material floorMaterial;
    [Tooltip("Both source materials are bright and the wall one is emissive, so the generator tints runtime copies rather than editing the assets the rest of the scene shares")]
    [SerializeField] private Color wallTint = new Color(0.22f, 0.22f, 0.25f);
    [SerializeField] private Color floorTint = new Color(0.10f, 0.10f, 0.12f);
    [SerializeField] private Color ceilingTint = new Color(0.06f, 0.06f, 0.07f);
    [Tooltip("The stock 0.477 throws a specular flare straight back down the flashlight beam")]
    [SerializeField] private float surfaceSmoothness = 0.15f;

    [Header("Stars")]
    [SerializeField] private GameObject starPrefab;
    [SerializeField] private int starCount = 5;
    [Tooltip("Height of a star above the maze floor")]
    [SerializeField] private float starHeight = 1f;
    [Tooltip("Hide the hand-placed stars of the original scene")]
    [SerializeField] private bool disableExistingPickups = true;
    [Tooltip("An emissive material glows but lights nothing. Without a light per star you walk straight past them in the dark.")]
    [SerializeField] private bool starLights = true;
    [SerializeField] private float starLightRange = 5f;
    [SerializeField] private float starLightIntensity = 1.5f;
    [Tooltip("Used only when useFloorProfile is off; otherwise FloorProfile.ShardCount wins")]
    [SerializeField] private int shardCount = 6;

    [Header("Atmosphere")]
    [SerializeField] private bool firstPerson = true;
    [SerializeField] private bool darkness = true;
    [Tooltip("Collecting the last star opens a hatch and starts a countdown instead of winning outright")]
    [SerializeField] private bool escapeSequence = true;
    [Tooltip("Title screen and rules screen, with the game held frozen until Start is pressed")]
    [SerializeField] private bool mainMenu = true;
    [Tooltip("Looping bed, e.g. SFX_AmbienceClose. Only used (pitched down into a drone) when Base Music is empty.")]
    [SerializeField] private AudioClip droneClip;
    [Tooltip("Base music: each floor loops one of these at its own pitch, picked from the seed (a Retry keeps it). Empty = the Drone Clip above.")]
    [SerializeField] private AudioClip[] baseMusic;
    [Tooltip("One-shot fired when the hunter spots you")]
    [SerializeField] private AudioClip stingClip;
    [Tooltip("Looping track for the escape, e.g. Music_Exciting")]
    [SerializeField] private AudioClip panicClip;
    [Tooltip("Played whole when the hunter spots you and starts a chase (Violin Stabs). Empty = the synthesised sting.")]
    [SerializeField] private AudioClip spottedClip;
    [Tooltip("Looping track under the title and rules screens (Discovering Rooms); fades out on START.")]
    [SerializeField] private AudioClip menuMusic;

    [Header("Decals")]
    [Tooltip("Multiplies the theme's wall/floor decal chances (capped at 0.9 per roll) and, above 1, allows one more decal per cell. 1 = the theme's own numbers.")]
    [SerializeField, Min(0f)] private float decalDensity = 2f;
    [Tooltip("Extra pick weight for decals with look-alike variants (the Blood decal pack splats/smears/pools) over grime, drips and the rest. 1 = the prefab's own weight.")]
    [SerializeField, Min(0f)] private float bloodWeight = 3f;

    [Header("Props (F85)")]
    [Tooltip("Multiplies the theme's wall / ceiling / floor prop chances (capped at 0.85 per roll, never below the theme's own number). 1 = the theme's own numbers. Only the +4 / +6 dressing streams shift.")]
    [SerializeField, Min(0f)] private float propDensity = 1.4f;
    [Tooltip("Extra set pieces on a themed floor, on top of the theme's own count. Set pieces only land in eligible cells, so this is a ceiling, not a guarantee.")]
    [SerializeField, Min(0)] private int setPieceBonus = 1;

    [Header("Actors")]
    [SerializeField] private NavMeshSurface navMeshSurface;
    [SerializeField] private AIFollower aiFollower;
    [Tooltip("Jumping out of the maze would trivialise it")]
    [SerializeField] private bool disablePlayerJump = true;

    [Header("Floors")]
    [Tooltip("Scale the maze, hunter, light and timers to how far into the game the player is (see FloorProfile). Off = use the values in this Inspector as they are.")]
    [SerializeField] private bool useFloorProfile = true;
    [Tooltip("For testing: start a fresh run on this floor (1 to 5) without playing up to it. The game carries on from there - the shop door still leads to the next floor. 0 = start on floor 1.")]
    [SerializeField] private int debugFloor = 0;

    [Header("Lockers")]
    [SerializeField] private int lockerCount = 10;
    [Tooltip("Width across the corridor (m)")]
    [SerializeField] private float lockerWidth = 1.1f;
    [Tooltip("Depth out from the back wall (m). Keep <= 0.7 so the cell centre stays reachable for the 0.5 m agent.")]
    [SerializeField] private float lockerDepth = 0.6f;
    [SerializeField] private float lockerHeight = 2.1f;
    [SerializeField] private Color lockerTint = new Color(0.16f, 0.17f, 0.19f);

    [Header("Debug")]
    [Tooltip("For testing the shop: deposited into the wallet when the maze is built.")]
    [SerializeField] private int debugShards = 0;
    [Tooltip("For testing: skip the title screen and open the floor as if the hatch had just been reached.")]
    [SerializeField] private bool debugStartInShop = false;
    [Tooltip("F76: forces TouchInput.Active on regardless of Mode, so the on-screen controls show in the Editor without a touchscreen - click them with the mouse, or use the Device Simulator.")]
    [SerializeField] private bool debugForceTouchControls = false;

    [Header("Wall lamps")]
    [SerializeField] private bool wallLamps = true;
    [Tooltip("Roughly one lamp per this many cells, plus one above every locker")]
    [SerializeField] private int cellsPerLamp = 5;
    [SerializeField] private float lampHeight = 2.6f;
    [SerializeField] private Color lampColor = new Color(1f, 0.72f, 0.42f);
    [SerializeField] private float lampRange = 7f;
    [SerializeField] private float lampIntensity = 1.1f;
    [Range(0f, 1f)]
    [SerializeField] private float faultyLampChance = 0.25f;

    [Header("Themes")]
    [Tooltip("Scene gallery built by LIGHTS OUT > Build > Floor Themes. Found automatically; assign only to override.")]
    [SerializeField] private FloorThemeSet themeSet;
    [Tooltip("Combine the static themed geometry (walls, pillars, floor, ceiling) after the navmesh bake. Off until profiled.")]
    [SerializeField] private bool staticBatchThemedGeometry = false;

    [Header("Kit maze (F48)")]
    [Tooltip("Scene object built by LIGHTS OUT > Build > Kit Maze Theme. Found automatically; assign only to override.")]
    [SerializeField] private KitMazeTheme kitTheme;
    [Tooltip("Corridor pitch when GameFlow.UseKitMaze is set. Whole metres so 3M/2M/1M pieces fill a wall exactly.")]
    [SerializeField] private int kitCellSize = 5;

    [Header("Interactables (F72)")]
    [Tooltip("Scene gallery built by LIGHTS OUT > Build > Interactables. Found automatically; assign only to override.")]
    [SerializeField] private InteractableKit interactableKit;

    [Header("Key Hunt (F77)")]
    [Tooltip("For testing: forces this floor's mechanics instead of rolling them. None = roll normally.")]
    [SerializeField] private FloorProfile.Objective debugObjectives = FloorProfile.Objective.None;

    /// <summary>
    /// F70: one dressing source, two providers. Every prop pass (wall/ceiling/floor props, decals, set
    /// pieces, dust) reads this instead of _theme directly, so the kit maze gets the shared prop set
    /// without faking a FloorTheme. Resolved once by ResolveDressing, right after ResolveKitTheme.
    /// </summary>
    private struct MazeDressing
    {
        public PropPiece[] Props;
        public SetPiece[] SetPieces;
        public DustMotes Dust;
        public float WallPropChance, CeilingPropChance, FloorPropChance, WallDecalChance, FloorDecalChance;
        public int MaxDecalsPerCell, SetPieceCount;
    }

    /// <summary>F70: bookkeeping for one recorded 3 m kit wall piece (SpawnKitWallRun), so BuildKitGates can
    /// replace it and BuildKitWallLights can tell which of its two faces look into a real (non-void,
    /// non-set-piece) cell.</summary>
    private struct KitWallSegment
    {
        public GameObject Piece;
        public Vector2Int? CellPlus;
        public Vector2Int? CellMinus;
    }

    // Wall flags per cell. The west/south walls are the ones actually built; the east wall of cell
    // (x, z) is the west wall of (x + 1, z), so carving a passage clears both sides.
    private bool[,] _wallN;
    private bool[,] _wallE;
    private bool[,] _wallS;
    private bool[,] _wallW;

    // Step distance from the start cell through open passages, used to place the stars and the AI far away.
    private int[,] _distance;

    private Transform _mazeRoot;
    private readonly List<Vector3> _cellCenters = new List<Vector3>();
    private readonly List<Transform> _stars = new List<Transform>();
    /// <summary>Cells SpawnStars chose. Kept so SpawnShards can stay clear of them.</summary>
    private readonly List<Vector2Int> _starCells = new List<Vector2Int>();
    /// <summary>F45: cell centres two steps from star i, around a corner. Parallel to _stars/_starCells.</summary>
    private readonly List<List<Vector3>> _ambushCells = new List<List<Vector3>>();
    private readonly List<Material> _runtimeMaterials = new List<Material>();
    private readonly List<Vector2Int> _lockerCells = new List<Vector2Int>();
    // Direction from a locker cell's centre toward the wall its locker stands against
    private readonly Dictionary<Vector2Int, Vector3> _lockerWall = new Dictionary<Vector2Int, Vector3>();
    private readonly List<Locker> _lockers = new List<Locker>();
    private readonly List<WallLamp> _lamps = new List<WallLamp>();
    /// <summary>F73: the cell each entry of _lamps was built at, parallel to it - lets BuildFuses map a lamp to its sector without lamps needing to know their own cell.</summary>
    private readonly List<Vector2Int> _lampCells = new List<Vector2Int>();
    private FloorProfile _profile;
    private Material _wallMat;
    private Material _floorMat;
    private Material _ceilingMat;

    private FloorTheme _theme;
    /// <summary>F48: true when GameFlow.UseKitMaze is set and a complete KitMazeTheme was found. _theme stays null in this mode - see ResolveKitTheme.</summary>
    private bool _kitMode;
    private Transform _wallsGroup, _pillarsGroup, _floorGroup, _ceilingGroup, _lampsGroup, _lockersGroup, _propsGroup;
    /// <summary>F70: parents for set-piece and decal clones, created alongside the other groups in BuildGeometry.</summary>
    private Transform _setPiecesGroup, _decalsGroup;
    /// <summary>Wall directions already taken by a wall prop, per cell. Consulted by BuildCeilingProps (no double-dressing a cell) and SpawnShards (no shard clipping a prop).</summary>
    private readonly Dictionary<Vector2Int, List<Vector3>> _propWalls = new Dictionary<Vector2Int, List<Vector3>>();
    /// <summary>F70: resolved once by ResolveDressing. Every prop/decal/set-piece pass reads this, never _theme directly.</summary>
    private MazeDressing _dressing;
    /// <summary>F70: cells a set piece has reserved this run. Skipped by wall/ceiling/floor props and decals; also used by kit trees so a tree does not double up with a set piece.</summary>
    private readonly HashSet<Vector2Int> _setPieceCells = new HashSet<Vector2Int>();
    /// <summary>F70 kit mode: every 3 m wall piece SpawnKitWallRun laid down, keyed by its wall-segment name prefix (e.g. "Wall_W_0_3"), so BuildKitGates can replace one and BuildKitWallLights can dress the rest.</summary>
    private readonly Dictionary<string, List<KitWallSegment>> _kitWallSegments = new Dictionary<string, List<KitWallSegment>>();

    /// <summary>F72: every door/button/vault-loot instance built this run, kept so SetUpAtmosphere can late-bind the HUD/flashlight once they exist (same pattern as _lockers).</summary>
    private readonly List<MazeDoor> _doors = new List<MazeDoor>();
    private readonly List<WallButton> _buttons = new List<WallButton>();
    private readonly List<BatteryCellPickup> _batteryPickups = new List<BatteryCellPickup>();
    /// <summary>F72: cells SpawnShards must place a shard in (a vault's 2 forced shards), consumed and cleared by SpawnShards. Each entry is one shard - a vault with 2 shards appears twice.</summary>
    private readonly List<Vector2Int> _vaultForcedShardCells = new List<Vector2Int>();
    /// <summary>F72: every cell BuildDoors used for a door, button or vault this run - reused (not copied) by F73's BuildFuses so a fuse box never lands on one of them.</summary>
    private readonly HashSet<Vector2Int> _doorUsedCells = new HashSet<Vector2Int>();
    /// <summary>F73: every fuse box instance built this run.</summary>
    private readonly List<FuseBox> _fuseBoxes = new List<FuseBox>();

    /// <summary>F77: every key built this run (KeyHunt floors only), late-bound onto the player's KeyRing once it exists.</summary>
    private readonly List<KeyPickup> _keys = new List<KeyPickup>();

    /// <summary>F74: every lore note built this run, kept so SetUpAtmosphere can late-bind the reader/HUD once they exist (same pattern as _buttons).</summary>
    private readonly List<LoreNote> _loreNotes = new List<LoreNote>();
    /// <summary>F74: this floor's lantern (0 or 1), late-bound once the player/hunter/HUD exist.</summary>
    private readonly List<Lantern> _lanterns = new List<Lantern>();
    /// <summary>F74: every theme interactable built this run (call bell/steam valve/candles/terminal), late-bound for its HUD reference.</summary>
    private readonly List<ThemeInteractable> _themeInteractables = new List<ThemeInteractable>();
    /// <summary>F74: Lab terminals specifically - ConfigureTerminal needs the player camera, which does not exist yet when BuildThemeInteractables runs.</summary>
    private readonly List<ThemeInteractable> _pendingTerminals = new List<ThemeInteractable>();

    /// <summary>F75: this floor's stalker (floors 4-5 only, FloorProfile.HasStalker), late-bound once the player/camera/flashlight/hunter/HUD/outcome exist. Null on every other floor or an incomplete kit.</summary>
    private Stalker _stalker;

    /// <summary>The theme this maze was built from, or null when the primitive fallback was used.</summary>
    public FloorTheme Theme => _theme;

    /// <summary>The spawned stars. Entries become null as they are collected.</summary>
    public IReadOnlyList<Transform> Stars => _stars;

    /// <summary>Every locker built for this maze.</summary>
    public IReadOnlyList<Locker> Lockers => _lockers;

    /// <summary>Every wall lamp built for this maze.</summary>
    public IReadOnlyList<WallLamp> Lamps => _lamps;

    /// <summary>Every fuse box built for this maze (F75 Fuse Map compass targeting).</summary>
    public IReadOnlyList<FuseBox> FuseBoxes => _fuseBoxes;

    /// <summary>Every wall button built for this maze (F75 Fuse Map compass targeting).</summary>
    public IReadOnlyList<WallButton> Buttons => _buttons;

    /// <summary>Every key built for this maze this run (F77 Key Hunt; ConsumableController's Fuse Map compass targeting).</summary>
    public IReadOnlyList<KeyPickup> Keys => _keys;

    /// <summary>Floor-level world centre of every cell, row-major (x + z * width).</summary>
    public IReadOnlyList<Vector3> CellCenters => _cellCenters;

    /// <summary>The seed this maze was actually built from, whether it came from the Inspector, a retry or the clock.</summary>
    public int UsedSeed { get; private set; }

    /// <summary>World size of one cell (F71: DreadDirector uses this to keep a kill-wave away from a star).</summary>
    public float CellSize => cellSize;

    private float FloorTop => origin.y + 0.02f;

    /// <summary>Floor-level world centre of one cell.</summary>
    public Vector3 CellCenter(int x, int z)
    {
        return new Vector3(
            origin.x + (x + 0.5f) * cellSize,
            FloorTop,
            origin.z + (z + 0.5f) * cellSize);
    }

    /// <summary>F45: cell centres two steps from star `starIndex`, around a corner from it - never a straight line to it. Empty for an out-of-range index or a star that sits in a dead end.</summary>
    public IReadOnlyList<Vector3> AmbushCellsFor(int starIndex)
    {
        if (starIndex < 0 || starIndex >= _ambushCells.Count) return System.Array.Empty<Vector3>();
        return _ambushCells[starIndex];
    }

    private void Awake()
    {
        if (width < 2 || height < 2)
        {
            Debug.LogError("MazeGenerator needs at least a 2x2 grid.", this);
            return;
        }

        // A fresh start is one the title screen will front; a reload from the shop door or a retry
        // arrives with SkipMenuOnLoad already set. Read before the debug toggle below sets it too.
        bool freshStart = !GameFlow.SkipMenuOnLoad;

        // Debug affordance: skip the title screen so a shop iteration does not cost a full floor.
        if (debugStartInShop) GameFlow.SkipMenuOnLoad = true;

        // Moved ahead of ApplyFloorProfile (F74) so RunRules.Resolve, called from inside it, can hash
        // the seed that is actually used rather than the Inspector's raw seed field - nothing between
        // here and the old call site reads seed/PendingSeed, so this is a pure reorder.
        int usedSeed = GameFlow.PendingSeed != 0 ? GameFlow.PendingSeed
            : seed != 0 ? seed : System.Environment.TickCount;
        GameFlow.PendingSeed = 0;
        UsedSeed = usedSeed;

        ApplyFloorProfile(freshStart, usedSeed);
        ResolveTheme();

        ResolveKitTheme(); // sets _kitMode; when true also forces _theme = null
        ResolveDressing();
        if (_kitMode)
        {
            // Guard ahead of the general clamp below: at minCorridorWidth 4 and wallThickness 0.4 the
            // clamp's minCellSize is 4.4, which would push a 4 m kitCellSize up and break the
            // whole-metre 3/2/1 filler. Round up to the smallest whole metre that still clears it instead.
            int minKitCellSize = Mathf.CeilToInt(minCorridorWidth + 0.4f);
            if (kitCellSize < minKitCellSize)
            {
                Debug.LogWarning($"MazeGenerator: kitCellSize {kitCellSize} would give halls narrower than {minCorridorWidth} m; raised to {minKitCellSize}.", this);
                kitCellSize = minKitCellSize;
            }

            // F40 slice A decision 4: kit mode keeps overriding scale on purpose. The profile's
            // Width/Height still apply (a 10x6 kit maze is fine), but its CellSize/WallHeight are
            // ignored here in favour of the kit's fixed 3/2/1 m pieces.
            cellSize = Mathf.Max(1, kitCellSize);
            wallThickness = 0.4f;   // kit walls are 0.37
            wallHeight = 3.9f;      // kit walls stand 4 m from kitY = FloorTop - 0.1
            Debug.Log($"MazeGenerator: kit maze, cell {cellSize} m.", this);
        }

        // Saved scenes carry their own cellSize, so the width rule is enforced here rather than trusted
        float minCellSize = minCorridorWidth + wallThickness;
        if (cellSize < minCellSize)
        {
            Debug.LogWarning($"MazeGenerator: cellSize {cellSize} gives halls {cellSize - wallThickness:0.0} m wide; raised to {minCellSize} for {minCorridorWidth} m halls.", this);
            cellSize = minCellSize;
        }

        Debug.Log($"MazeGenerator: building a {width}x{height} maze with seed {usedSeed}.", this);

        // A new attempt at the current floor. Must run before SpawnShards reads the per-floor cap.
        PlayerWallet.BeginAttempt();
        if (debugShards != 0) PlayerWallet.DebugDeposit(debugShards);

        Generate(usedSeed);
        CacheCellCenters();
        ChooseLockerCells(new System.Random(usedSeed + 3));
        BuildGeometry();
        BuildPillars();
        BuildLockers();

        // F70, pre-bake: set pieces reserve their cell (and record their wall in _propWalls) before wall
        // props run, so BuildWallProps/BuildCeilingProps skip them. +8 is a new offset, never read by
        // anything that existed before this feature.
        BuildSetPieces(new System.Random(usedSeed + 8));

        // +4 is the RNG offset props own (+0 carve, +1 stars, +2 lamps, +3 lockers, +5 shards). One
        // instance is threaded through both prop passes below so the stream stays continuous across the
        // navmesh bake between them - same seed and floor always puts props on the same walls.
        System.Random propRng = new System.Random(usedSeed + 4);
        BuildWallProps(propRng);

        // F70, pre-bake: kit-only gates and trees carry colliders, so they must go in before the bake too.
        if (_kitMode) BuildKitDressingPreBake(new System.Random(usedSeed + 9));

        // Bake before anything else is placed inside the volume: the surface collects render meshes on
        // every layer, so a star, a ceiling or a robot standing in the maze would be carved out of the
        // navmesh. Lockers and wall props are built above, before this line, so their bodies are carved
        // out too and the hunter paths around them instead of through them.
        BuildRuntimeNavMesh();

        BuildCeiling();
        BuildCeilingProps(propRng);
        BuildWallLamps();
        if (_kitMode) BuildKitWallLights(new System.Random(usedSeed + 10));

        // F70, post-bake: flat, collider-less dressing - safe to add after the navmesh has already
        // been carved from the bodies built above.
        BuildFloorProps(new System.Random(usedSeed + 6));
        BuildDecals(new System.Random(usedSeed + 7));
        BuildDust();

        DisableExistingPickups();
        SpawnStars(usedSeed);

        // F72, post-bake: doors/buttons/vaults, after the stars exist (security placement tests that
        // blocking a passage actually cuts a star off) and before the shards (a vault's forced shards
        // are subtracted from the normal pool, not added on top). +11 is a new offset (+15 for vault
        // loot); it never touches the existing carve/star/lamp/locker/prop/shard streams (+0..+10).
        BuildDoors(new System.Random(usedSeed + 11));

        // F73, post-bake, after BuildDoors: Blackout-floor fuse boxes and their lamp sectors. +12 is a
        // new offset, never read by anything that existed before this feature.
        BuildFuses(new System.Random(usedSeed + 12));

        // F77, post-bake, after BuildFuses (fuse walls reserved) and before +13 (so throwables/notes see
        // key walls). +18: +17 is MazeEscape's lift roll.
        BuildKeys(new System.Random(usedSeed + 18));

        // F74, post-bake, after BuildFuses so every free-wall/free-cell pass below sees _doorUsedCells
        // fully populated. Decision D15: +13 threaded through both throwable passes (one continuous
        // stream, same convention as propRng above), +14 threaded through notes and theme interactables,
        // +15 for the lantern (shares its numeric offset with BuildDoors' vault-loot stream, but as a
        // separate System.Random instance - see the spec's decision D15).
        System.Random throwRng = new System.Random(usedSeed + 13);
        BuildThrowables(throwRng);
        BuildNoisySurfaces(throwRng);

        System.Random noteRng = new System.Random(usedSeed + 14);
        BuildLoreNotes(noteRng);
        BuildThemeInteractables(noteRng);

        BuildLantern(new System.Random(usedSeed + 15));

        SpawnShards(usedSeed);
        PlacePlayer();
        PlaceAI();

        // F75, post-PlaceAI: the second creature (+16), on floors where FloorProfile.HasStalker.
        BuildStalker(new System.Random(usedSeed + 16));

        SetUpAtmosphere();

        if (staticBatchThemedGeometry && _theme != null) CombineStaticGroups();
        // Whatever the artist sees in the gallery is exactly what was cloned, including unapplied
        // overrides - so once cloning is done the gallery itself has no further reason to be visible.
        // Also hidden when the fallback ran: an incomplete gallery must not leave five rows of point
        // lights and locker triggers live in the scene during a run.
        if (themeSet != null) themeSet.gameObject.SetActive(false);
        // F72: same reasoning - the InteractableKit gallery has nothing left to show once its templates
        // are cloned, and an incomplete kit must not leave a live MazeDoor/WallButton sitting in the scene.
        if (interactableKit != null) interactableKit.gameObject.SetActive(false);
    }

    /// <summary>
    /// Picks the FloorThemes gallery row this maze clones, or leaves _theme null to fall back to the
    /// primitive geometry the game shipped with before theming existed. The Editor cannot run the
    /// LIGHTS OUT &gt; Build &gt; Floor Themes menu item on this implementer's behalf (it does not hold the
    /// project lock), so until the user runs it and saves the scene, every maze uses this fallback.
    /// </summary>
    private void ResolveTheme()
    {
        _theme = null;
        if (_profile == null) return; // Inspector mode (useFloorProfile off): primitives, as before

        if (themeSet == null) themeSet = FindAnyObjectByType<FloorThemeSet>(FindObjectsInactive.Include);
        if (themeSet == null)
        {
            Debug.LogError("MazeGenerator: no FloorThemes gallery in the scene, building the plain maze. Run LIGHTS OUT > Build > Floor Themes in the Editor, then save the scene.", this);
            return;
        }

        FloorTheme theme = themeSet.ForFloor(_profile.ThemeIndex);
        if (theme == null || !theme.IsComplete)
        {
            Debug.LogError($"MazeGenerator: FloorThemes has no complete theme for floor {_profile.ThemeIndex}, building the plain maze.", themeSet);
            return;
        }

        _theme = theme;
        Debug.Log($"MazeGenerator: theme '{theme.DisplayName}'.", this);
    }

    /// <summary>
    /// F48: when GameFlow.UseKitMaze is set, finds the KitMazeTheme scene object and switches to the
    /// Maze Modular Puzzle Kit geometry (BuildKitGeometry / BuildKitPillars) instead of the FloorThemes
    /// gallery. _theme is forced null so the ceiling, lockers, wall lamps, props and atmosphere profile
    /// all keep taking their existing primitive branches unchanged (decision 1 in the plan).
    /// </summary>
    private void ResolveKitTheme()
    {
        _kitMode = false;
        if (!GameFlow.UseKitMaze) return;

        if (kitTheme == null) kitTheme = FindAnyObjectByType<KitMazeTheme>(FindObjectsInactive.Include);
        if (kitTheme == null || !kitTheme.IsComplete)
        {
            Debug.LogError("MazeGenerator: GameFlow.UseKitMaze is set but there is no complete KitMazeTheme in the scene. Run LIGHTS OUT > Build > Kit Maze Theme, then save the scene. Building the normal maze.", this);
            return;
        }

        _kitMode = true;
        _theme = null;
    }

    /// <summary>
    /// F70 decision 6: one dressing source, two providers. Called right after ResolveKitTheme, so
    /// _theme/_kitMode are both settled. Themed floor -> _theme's fields. Kit mode -> kitTheme's common
    /// dressing fields, with wall/ceiling prop chances forced to 0 (decision 7: the kit set never gets
    /// theme wall/ceiling props). Anything else (primitive fallback) -> every field stays at its C#
    /// default (null/0), so BuildWallProps and friends' empty/zero checks make every new pass a no-op -
    /// the graceful-degradation rule in decision 14.
    /// </summary>
    private void ResolveDressing()
    {
        if (_theme != null)
        {
            _dressing = new MazeDressing
            {
                Props = _theme.Props,
                SetPieces = _theme.SetPieces,
                Dust = _theme.Dust,
                WallPropChance = DenserProps(_theme.WallPropChance),
                CeilingPropChance = DenserProps(_theme.CeilingPropChance),
                FloorPropChance = DenserProps(_theme.FloorPropChance),
                WallDecalChance = _theme.WallDecalChance,
                FloorDecalChance = _theme.FloorDecalChance,
                MaxDecalsPerCell = _theme.MaxDecalsPerCell,
                SetPieceCount = _theme.SetPieceCount > 0 ? _theme.SetPieceCount + setPieceBonus : 0
            };
        }
        else if (_kitMode && kitTheme != null)
        {
            _dressing = new MazeDressing
            {
                Props = kitTheme.Props,
                SetPieces = kitTheme.SetPieces,
                Dust = kitTheme.Dust,
                WallPropChance = 0f,
                CeilingPropChance = 0f,
                FloorPropChance = DenserProps(kitTheme.FloorPropChance),
                WallDecalChance = kitTheme.WallDecalChance,
                FloorDecalChance = kitTheme.FloorDecalChance,
                MaxDecalsPerCell = kitTheme.MaxDecalsPerCell,
                SetPieceCount = kitTheme.SetPieceCount
            };
        }
        else
        {
            _dressing = default;
        }
    }

    /// <summary>F85: a theme's prop chance scaled by propDensity, capped at 0.85 but never lowered below its own value.</summary>
    private float DenserProps(float chance)
    {
        return Mathf.Max(chance, Mathf.Min(0.85f, chance * propDensity));
    }

    /// <summary>F85: look-alike models under a prop (hanging bodies, shrouds, sockets, vents) picked by a hash of the cell - no rng draw, same trick as the set pieces.</summary>
    private static void ApplyModelVariants(Component clone, int x, int z, int salt)
    {
        if (clone.TryGetComponent(out ModelVariants models)) models.Apply((x * 73856093) ^ (z * 19349663) ^ salt);
    }

    /// <summary>
    /// Picks this run's floor and overwrites the world-size and lamp fields with its values. The
    /// hunter, light and timers are handed their values later, where each is wired up. Runs first in
    /// Awake, before anything reads width, height, starCount, lockerCount or the lamp settings.
    /// </summary>
    private void ApplyFloorProfile(bool freshStart, int usedSeed)
    {
        _profile = null;

        // F74 decision D9: resolved before the useFloorProfile-off early return, so RunRules.Current is
        // never left stale (it is a pure function of usedSeed/floor, always safe to recompute even on
        // an Inspector-only test maze where nothing below reads it).
        RunRules.Resolve(usedSeed, GameFlow.CurrentFloor);

        if (!useFloorProfile) return;

        // A debug floor picks the floor a fresh start begins on, and is written back so the HUD, end
        // screens and Next Floor all agree with it. Only on a fresh start: a reload that came from the
        // shop door or a retry (SkipMenuOnLoad) must keep the floor the game set, or the shop's door
        // would lead straight back to the same floor forever.
        if (debugFloor > 0 && freshStart) GameFlow.CurrentFloor = Mathf.Clamp(debugFloor, 1, FloorProfile.FinalFloor);

        // F77: debugObjectives (Inspector) forces this floor's mechanics instead of rolling them from the
        // seed; None means "roll normally" (null forced, not Objective.None - that would mean "force no
        // mechanics", see FloorProfile.For's doc comment).
        FloorProfile.Objective? forcedObjectives = debugObjectives != FloorProfile.Objective.None ? debugObjectives : (FloorProfile.Objective?)null;
        _profile = FloorProfile.For(GameFlow.CurrentFloor, usedSeed, forcedObjectives);
        PlayerInventory.ApplyActiveModifiers(_profile);
        // F74: run rules (DyingGrid, KeenEars, HeavyFog, WeakBattery, Restless, GlassFloors, RichVeins)
        // layer on top of the shop's own modifiers - see RunRules.ApplyToProfile's doc comment for which
        // fields it touches and why that is enough (AIFollower.ApplyDifficulty and SpawnShards both read
        // the mutated profile later in this same Awake).
        RunRules.ApplyToProfile(_profile);

        width = _profile.Width;
        height = _profile.Height;
        cellSize = _profile.CellSize;
        wallHeight = _profile.WallHeight;
        starCount = _profile.StarCount;
        lockerCount = _profile.LockerCount;
        cellsPerLamp = _profile.CellsPerLamp;
        lampIntensity = _profile.LampIntensity;
        lampRange = _profile.LampRange;

        // F40 slice A decision 5: lamps scale with wall height so a low ceiling (floor 3) doesn't hide
        // its lamps in the torch beam and a tall one (floor 4) doesn't leave them looking stranded low.
        // The serialized lampHeight (2.6) is read here as the 4 m baseline; ApplyFloorProfile runs once
        // per scene load and only writes this field here, so there is no compounding across floors.
        lampHeight = Mathf.Clamp(lampHeight * _profile.WallHeight / 4f, 2.3f, 3.4f);

        string modifiers = PlayerInventory.ActiveModifiers.Count > 0
            ? string.Join(", ", PlayerInventory.ActiveModifiers)
            : "none";
        Debug.Log($"MazeGenerator: {_profile}, modifiers: {modifiers}", this);
    }

    private void OnDestroy()
    {
        // Runtime material copies are not collected with the scene in the editor
        foreach (Material material in _runtimeMaterials)
        {
            if (material != null) Destroy(material);
        }
        _runtimeMaterials.Clear();

        // F73: LightPool is scene-scoped, not campaign-scoped (CLAUDE.md's static-reset rule) - cleared
        // here as well as by its own SubsystemRegistration reset, so a maze rebuild never carries stale
        // entries over from the floor just left.
        LightPool.Clear();
    }

    // ---------------------------------------------------------------- generation

    private void Generate(int usedSeed)
    {
        _wallN = new bool[width, height];
        _wallE = new bool[width, height];
        _wallS = new bool[width, height];
        _wallW = new bool[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                _wallN[x, z] = true;
                _wallE[x, z] = true;
                _wallS[x, z] = true;
                _wallW[x, z] = true;
            }
        }

        // Its own RNG so the maze layout never perturbs UnityEngine.Random (used by the pickup VFX).
        System.Random rng = new System.Random(usedSeed);

        bool[,] visited = new bool[width, height];
        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        Vector2Int current = Vector2Int.zero;
        visited[0, 0] = true;
        stack.Push(current);

        // Recursive backtracker (iterative): always yields a perfect maze, so every cell is reachable.
        List<int> candidates = new List<int>(4);
        while (stack.Count > 0)
        {
            current = stack.Peek();
            candidates.Clear();

            if (current.y + 1 < height && !visited[current.x, current.y + 1]) candidates.Add(0); // north
            if (current.x + 1 < width && !visited[current.x + 1, current.y]) candidates.Add(1);  // east
            if (current.y - 1 >= 0 && !visited[current.x, current.y - 1]) candidates.Add(2);     // south
            if (current.x - 1 >= 0 && !visited[current.x - 1, current.y]) candidates.Add(3);     // west

            if (candidates.Count == 0)
            {
                stack.Pop();
                continue;
            }

            int direction = candidates[rng.Next(candidates.Count)];
            Vector2Int next = current;
            switch (direction)
            {
                case 0:
                    next.y += 1;
                    _wallN[current.x, current.y] = false;
                    _wallS[next.x, next.y] = false;
                    break;
                case 1:
                    next.x += 1;
                    _wallE[current.x, current.y] = false;
                    _wallW[next.x, next.y] = false;
                    break;
                case 2:
                    next.y -= 1;
                    _wallS[current.x, current.y] = false;
                    _wallN[next.x, next.y] = false;
                    break;
                default:
                    next.x -= 1;
                    _wallW[current.x, current.y] = false;
                    _wallE[next.x, next.y] = false;
                    break;
            }

            visited[next.x, next.y] = true;
            stack.Push(next);
        }

        if (removeDeadEnds) RemoveDeadEnds(rng);

        ComputeDistances();
    }

    private int WallCount(int x, int z)
    {
        return (_wallN[x, z] ? 1 : 0) + (_wallE[x, z] ? 1 : 0) + (_wallS[x, z] ? 1 : 0) + (_wallW[x, z] ? 1 : 0);
    }

    /// <summary>
    /// Turns the perfect maze into a braided one: every cell with three walls gets one more wall
    /// knocked through, so there are no dead ends and the maze is full of loops. Prefers a wall whose
    /// far side is also a dead end, which fixes two at once and keeps the number of new loops low.
    /// Only interior walls are ever opened, so the outer boundary stays sealed.
    /// </summary>
    private void RemoveDeadEnds(System.Random rng)
    {
        List<Vector2Int> deadEnds = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (WallCount(x, z) == 3) deadEnds.Add(new Vector2Int(x, z));
            }
        }

        Shuffle(deadEnds, rng);

        List<int> options = new List<int>(4);
        foreach (Vector2Int cell in deadEnds)
        {
            // An earlier removal may already have opened this one up
            if (WallCount(cell.x, cell.y) != 3) continue;

            options.Clear();
            int preferred = -1;
            for (int direction = 0; direction < 4; direction++)
            {
                if (!IsWallClosed(cell.x, cell.y, direction)) continue;

                Vector2Int neighbour = Neighbour(cell, direction);
                if (neighbour.x < 0 || neighbour.x >= width || neighbour.y < 0 || neighbour.y >= height) continue;

                options.Add(direction);
                if (preferred < 0 && WallCount(neighbour.x, neighbour.y) == 3) preferred = direction;
            }

            // A dead end has three closed walls and at most two of them are on the boundary
            if (options.Count == 0) continue;

            int chosen = preferred >= 0 ? preferred : options[rng.Next(options.Count)];
            OpenWall(cell, chosen);
        }
    }

    // 0 = north, 1 = east, 2 = south, 3 = west, matching the carving code above
    private bool IsWallClosed(int x, int z, int direction)
    {
        switch (direction)
        {
            case 0: return _wallN[x, z];
            case 1: return _wallE[x, z];
            case 2: return _wallS[x, z];
            default: return _wallW[x, z];
        }
    }

    private static Vector2Int Neighbour(Vector2Int cell, int direction)
    {
        switch (direction)
        {
            case 0: return new Vector2Int(cell.x, cell.y + 1);
            case 1: return new Vector2Int(cell.x + 1, cell.y);
            case 2: return new Vector2Int(cell.x, cell.y - 1);
            default: return new Vector2Int(cell.x - 1, cell.y);
        }
    }

    private void OpenWall(Vector2Int cell, int direction)
    {
        Vector2Int other = Neighbour(cell, direction);
        switch (direction)
        {
            case 0: _wallN[cell.x, cell.y] = false; _wallS[other.x, other.y] = false; break;
            case 1: _wallE[cell.x, cell.y] = false; _wallW[other.x, other.y] = false; break;
            case 2: _wallS[cell.x, cell.y] = false; _wallN[other.x, other.y] = false; break;
            default: _wallW[cell.x, cell.y] = false; _wallE[other.x, other.y] = false; break;
        }
    }

    private void ComputeDistances()
    {
        _distance = new int[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                _distance[x, z] = -1;
            }
        }

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        _distance[0, 0] = 0;
        queue.Enqueue(Vector2Int.zero);

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            int next = _distance[cell.x, cell.y] + 1;

            if (!_wallN[cell.x, cell.y]) TryVisit(cell.x, cell.y + 1, next, queue);
            if (!_wallE[cell.x, cell.y]) TryVisit(cell.x + 1, cell.y, next, queue);
            if (!_wallS[cell.x, cell.y]) TryVisit(cell.x, cell.y - 1, next, queue);
            if (!_wallW[cell.x, cell.y]) TryVisit(cell.x - 1, cell.y, next, queue);
        }
    }

    private void TryVisit(int x, int z, int distance, Queue<Vector2Int> queue)
    {
        if (x < 0 || x >= width || z < 0 || z >= height) return;
        if (_distance[x, z] >= 0) return;

        _distance[x, z] = distance;
        queue.Enqueue(new Vector2Int(x, z));
    }

    private void CacheCellCenters()
    {
        _cellCenters.Clear();
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                _cellCenters.Add(CellCenter(x, z));
            }
        }
    }

    // ---------------------------------------------------------------- lockers

    /// <summary>
    /// Picks lockers that stand against the SIDE wall of a hall rather than at the end of one. The
    /// maze has no dead ends any more, and a locker in a straight stretch is a choice the player
    /// makes on the move instead of a trap at the end of a corridor. Straight cells (walls on two
    /// opposite sides only) are used first; corners and junctions only if the maze runs out of them.
    /// Excludes the start and the AI spawn. Only needs wall flags, so it runs before any geometry.
    /// </summary>
    private void ChooseLockerCells(System.Random rng)
    {
        _lockerCells.Clear();
        _lockerWall.Clear();

        Vector2Int aiCell = FarthestCell();
        List<Vector2Int> straight = new List<Vector2Int>();
        List<Vector2Int> other = new List<Vector2Int>();

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (x == 0 && z == 0) continue;
                if (x == aiCell.x && z == aiCell.y) continue;

                bool northSouth = _wallN[x, z] && _wallS[x, z] && !_wallE[x, z] && !_wallW[x, z];
                bool eastWest = _wallE[x, z] && _wallW[x, z] && !_wallN[x, z] && !_wallS[x, z];
                if (northSouth || eastWest) straight.Add(new Vector2Int(x, z));
                else if (WallCount(x, z) >= 1) other.Add(new Vector2Int(x, z));
            }
        }

        Shuffle(straight, rng);
        Shuffle(other, rng);

        // Same greedy-then-relaxed pass as SpawnStars, so lockers do not cluster in one corner
        int minSeparation = Mathf.Max(2, (width + height) / 7);
        List<Vector2Int> chosen = new List<Vector2Int>();

        for (int pass = 0; pass < 2 && chosen.Count < lockerCount; pass++)
        {
            for (int pool = 0; pool < 2; pool++)
            {
                foreach (Vector2Int candidate in pool == 0 ? straight : other)
                {
                    if (chosen.Count >= lockerCount) break;
                    if (chosen.Contains(candidate)) continue;
                    if (pass == 0 && !IsFarEnough(candidate, chosen, minSeparation)) continue;
                    chosen.Add(candidate);
                }
            }
        }

        _lockerCells.AddRange(chosen);

        // Which wall each locker stands against: one of the cell's real walls, picked from the seed
        List<Vector3> walls = new List<Vector3>(4);
        foreach (Vector2Int cell in chosen)
        {
            walls.Clear();
            if (_wallN[cell.x, cell.y]) walls.Add(Vector3.forward);
            if (_wallE[cell.x, cell.y]) walls.Add(Vector3.right);
            if (_wallS[cell.x, cell.y]) walls.Add(Vector3.back);
            if (_wallW[cell.x, cell.y]) walls.Add(Vector3.left);
            _lockerWall[cell] = walls[rng.Next(walls.Count)];
        }

        if (chosen.Count < lockerCount)
        {
            Debug.LogWarning($"MazeGenerator: only {chosen.Count} of {lockerCount} lockers fit in this maze.", this);
        }
    }

    /// <summary>
    /// Built between BuildGeometry and BuildRuntimeNavMesh so every locker body is carved out of the
    /// navmesh and the hunter paths around it, not through it.
    /// </summary>
    private void BuildLockers()
    {
        _lockers.Clear();

        foreach (Vector2Int cell in _lockerCells)
        {
            BuildLocker(cell);
        }
    }

    private void BuildLocker(Vector2Int cell)
    {
        if (_theme != null && _theme.Locker != null)
        {
            BuildThemedLocker(cell);
        }
        else
        {
            BuildPrimitiveLocker(cell);
        }
    }

    /// <summary>
    /// Clones the theme's locker prefab and hands MazeGenerator's own computed positions to Configure -
    /// the prefab's own insideAnchor/frontAnchor win when the artist set them, otherwise the same offsets
    /// the primitive locker uses are computed from its transform. The prefab's own trigger collider
    /// replaces the runtime-added one BuildPrimitiveLocker builds by hand.
    /// </summary>
    private void BuildThemedLocker(Vector2Int cell)
    {
        int x = cell.x;
        int z = cell.y;
        Vector3 cellCenter = CellCenter(x, z);
        Vector3 openDir = -_lockerWall[cell];
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        Vector3 wallFace = cellCenter - openDir * backFaceDistance;

        Locker locker = Instantiate(_theme.Locker, wallFace, Quaternion.LookRotation(openDir, Vector3.up), _lockersGroup);
        locker.gameObject.SetActive(true);
        locker.name = $"Locker_{x}_{z}";

        Vector3 inside = locker.InsideAnchor != null
            ? locker.InsideAnchor.position
            : locker.transform.TransformPoint(new Vector3(0f, 0f, lockerDepth * 0.5f));
        Vector3 front = locker.FrontAnchor != null
            ? locker.FrontAnchor.position
            : locker.transform.TransformPoint(new Vector3(0f, 0f, lockerDepth + 1.0f));

        locker.Configure(inside, front, locker.transform.eulerAngles.y, locker.Door);
        _lockers.Add(locker);
    }

    private void BuildPrimitiveLocker(Vector2Int cell)
    {
        int x = cell.x;
        int z = cell.y;
        Vector3 cellCenter = CellCenter(x, z);

        // The locker stands against the wall chosen in ChooseLockerCells and its door faces the
        // middle of the hall, i.e. away from that wall.
        Vector3 openDir = -_lockerWall[cell];

        GameObject root = new GameObject($"Locker_{x}_{z}");
        root.transform.SetParent(_lockersGroup, false);
        root.transform.position = cellCenter;
        root.transform.rotation = Quaternion.LookRotation(openDir, Vector3.up);

        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        float frontFaceZ = -backFaceDistance + lockerDepth;

        Material lockerMat = DarkCopy(wallMaterial, lockerTint, "Maze_Locker");

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.layer = 0;
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, lockerHeight * 0.5f, -backFaceDistance + lockerDepth * 0.5f);
        body.transform.localScale = new Vector3(lockerWidth, lockerHeight, lockerDepth);
        if (lockerMat != null) body.GetComponent<MeshRenderer>().sharedMaterial = lockerMat;

        // Door: three horizontal slats on a hinge at the left edge, proud of the front face by 0.02 m.
        // Their colliders are what stops the hunter's LOS raycast seeing in - visibility is 0 while
        // hidden anyway, so the gaps between them are purely for looks.
        GameObject doorPivot = new GameObject("Door");
        doorPivot.transform.SetParent(root.transform, false);
        doorPivot.transform.localPosition = new Vector3(-lockerWidth * 0.5f, 0f, frontFaceZ + 0.02f);
        doorPivot.transform.localRotation = Quaternion.identity;

        BuildDoorSlats(doorPivot.transform, lockerWidth - 0.04f, lockerHeight - 0.04f, 0.04f, lockerMat);

        BoxCollider trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(1.4f, 2.0f, 1.2f);
        trigger.center = new Vector3(0f, 1.0f, frontFaceZ + 0.9f);

        Vector3 insidePosition = root.transform.TransformPoint(new Vector3(0f, 0f, -backFaceDistance + lockerDepth * 0.5f));
        Vector3 frontPosition = root.transform.TransformPoint(new Vector3(0f, 0f, frontFaceZ + 1.0f));
        float facingYaw = root.transform.eulerAngles.y;

        Locker locker = root.AddComponent<Locker>();
        locker.Configure(insidePosition, frontPosition, facingYaw, doorPivot.transform);
        _lockers.Add(locker);
    }

    /// <summary>Three slats with two 0.06 m gaps around eye height (1.5-1.7 m), spanning the door width.</summary>
    private void BuildDoorSlats(Transform doorPivot, float doorWidth, float doorHeight, float depth, Material material)
    {
        const float gap = 0.06f;

        float bottomHeight = Mathf.Clamp(1.5f, 0.1f, doorHeight);
        float midHeight = 0.08f;
        float midStart = bottomHeight + gap;
        float midEnd = midStart + midHeight;
        float topStart = midEnd + gap;
        float topHeight = Mathf.Max(0.1f, doorHeight - topStart);

        CreateDoorSlat(doorPivot, "Slat_Bottom", doorWidth, bottomHeight, depth, bottomHeight * 0.5f, material);
        CreateDoorSlat(doorPivot, "Slat_Middle", doorWidth, midHeight, depth, midStart + midHeight * 0.5f, material);
        CreateDoorSlat(doorPivot, "Slat_Top", doorWidth, topHeight, depth, topStart + topHeight * 0.5f, material);
    }

    private void CreateDoorSlat(Transform parent, string slatName, float width, float height, float depth, float centerY, Material material)
    {
        GameObject slat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slat.name = slatName;
        slat.layer = 0;
        slat.transform.SetParent(parent, false);
        slat.transform.localPosition = new Vector3(width * 0.5f, centerY, 0f);
        slat.transform.localScale = new Vector3(width, height, depth);
        if (material != null) slat.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    // ---------------------------------------------------------------- pillars

    /// <summary>
    /// Themed only: one pillar at every grid vertex where the walls meeting it are not just a straight
    /// wall passing through - i.e. corners, junctions, dead-end wall stubs, and the outer boundary's own
    /// corners. A vertex with exactly two closed segments that are collinear (both the east-west pair or
    /// both the north-south pair) is a straight run and is skipped to reduce clutter.
    /// </summary>
    private void BuildPillars()
    {
        if (_kitMode)
        {
            BuildKitPillars();
            return;
        }

        if (_theme == null || _theme.Pillar == null) return;

        for (int vx = 0; vx <= width; vx++)
        {
            for (int vz = 0; vz <= height; vz++)
            {
                if (!VertexNeedsPillar(vx, vz)) continue;

                Vector3 position = new Vector3(origin.x + vx * cellSize, FloorTop, origin.z + vz * cellSize);
                GameObject pillar = Instantiate(_theme.Pillar, position, Quaternion.identity, _pillarsGroup);
                pillar.SetActive(true);
                pillar.name = $"Pillar_{vx}_{vz}";
                pillar.transform.localScale = new Vector3(1f, wallHeight / 4f, 1f);
            }
        }
    }

    private bool VertexNeedsPillar(int vx, int vz)
    {
        bool west = HorizontalWallClosed(vx - 1, vz);
        bool east = HorizontalWallClosed(vx, vz);
        bool south = VerticalWallClosed(vx, vz - 1);
        bool north = VerticalWallClosed(vx, vz);

        int closedCount = (west ? 1 : 0) + (east ? 1 : 0) + (south ? 1 : 0) + (north ? 1 : 0);
        if (closedCount == 0) return false; // nothing meets here at all
        if (closedCount != 2) return true;  // a corner, a T-junction or a dead-end stub

        bool collinear = (west && east) || (south && north);
        return !collinear; // two collinear segments is a straight wall passing through - skip it
    }

    /// <summary>Is the horizontal (east-west running) wall segment at grid row zBoundary, spanning cell column x to x+1, closed? Out-of-range x means no such segment.</summary>
    private bool HorizontalWallClosed(int x, int zBoundary)
    {
        if (x < 0 || x >= width) return false;
        if (zBoundary <= 0) return _wallS[x, 0];
        if (zBoundary >= height) return _wallN[x, height - 1];
        return _wallS[x, zBoundary]; // == _wallN[x, zBoundary - 1], kept in sync by the carving code
    }

    /// <summary>Is the vertical (north-south running) wall segment at grid column xBoundary, spanning cell row z to z+1, closed? Out-of-range z means no such segment.</summary>
    private bool VerticalWallClosed(int xBoundary, int z)
    {
        if (z < 0 || z >= height) return false;
        if (xBoundary <= 0) return _wallW[0, z];
        if (xBoundary >= width) return _wallE[width - 1, z];
        return _wallW[xBoundary, z]; // == _wallE[xBoundary - 1, z], kept in sync by the carving code
    }

    // ---------------------------------------------------------------- props

    /// <summary>
    /// Built before the navmesh bake so a prop's body collider carves the navmesh like a locker does.
    /// One prop per eligible cell at most. Skips the start cell, the AI cell and every locker cell
    /// (decision 6). Themed floor or kit mode via _dressing (F70 decision 6/16) - reads _dressing instead
    /// of _theme directly, but draws from propRng in exactly the order it always has (chance, wall pick,
    /// prop pick), so an existing seed's props land on the same walls as before this feature. The new
    /// _setPieceCells skip is checked only after all of those draws (F70 decision 16).
    ///
    /// _propWalls itself is no longer cleared here - BuildSetPieces runs first and needs to leave its
    /// reservations in place for this pass to skip, so the Clear moved to the top of BuildSetPieces.
    /// </summary>
    private void BuildWallProps(System.Random rng)
    {
        if (_dressing.Props == null || _dressing.Props.Length == 0) return;

        Vector2Int aiCell = FarthestCell();
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        List<Vector3> walls = new List<Vector3>(4);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (x == 0 && z == 0) continue;
                if (x == aiCell.x && z == aiCell.y) continue;

                Vector2Int cell = new Vector2Int(x, z);
                if (lockerCellSet.Contains(cell)) continue;
                if (rng.NextDouble() >= _dressing.WallPropChance) continue;

                walls.Clear();
                if (_wallN[x, z]) walls.Add(Vector3.forward);
                if (_wallE[x, z]) walls.Add(Vector3.right);
                if (_wallS[x, z]) walls.Add(Vector3.back);
                if (_wallW[x, z]) walls.Add(Vector3.left);
                if (walls.Count == 0) continue;

                Vector3 dir = walls[rng.Next(walls.Count)];
                PropPiece prop = PickWeightedProp(_dressing.Props, PropPiece.MountKind.Wall, rng);
                if (prop == null) return; // no wall props in this theme, nothing more to try

                // F70: a set piece already claimed this cell. Checked after every RNG draw the cell
                // makes (chance, wall pick, prop pick), same rule as the low-ceiling guard below, so an
                // old scene (no set pieces) draws exactly as it always has.
                if (_setPieceCells.Contains(cell)) continue;

                Vector3 pos = CellCenter(x, z) + dir * backFaceDistance;
                PropPiece clone = Instantiate(prop, pos, Quaternion.LookRotation(-dir), _propsGroup);
                clone.gameObject.SetActive(true);
                clone.name = $"Prop_{prop.name}_{x}_{z}";
                ApplyModelVariants(clone, x, z, 0x3A11);

                if (!_propWalls.TryGetValue(cell, out List<Vector3> taken))
                {
                    taken = new List<Vector3>();
                    _propWalls[cell] = taken;
                }
                taken.Add(dir);
            }
        }
    }

    /// <summary>Built after the navmesh bake. Never shares a cell with a wall prop (decision 6, "keeps clutter readable") or a set piece.</summary>
    private void BuildCeilingProps(System.Random rng)
    {
        if (_dressing.Props == null || _dressing.Props.Length == 0) return;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (x == 0 && z == 0) continue;

                Vector2Int cell = new Vector2Int(x, z);
                if (_lockerCells.Contains(cell)) continue;
                if (_propWalls.ContainsKey(cell)) continue;
                if (rng.NextDouble() >= _dressing.CeilingPropChance) continue;

                PropPiece prop = PickWeightedProp(_dressing.Props, PropPiece.MountKind.Ceiling, rng);
                if (prop == null) return; // no ceiling props in this theme, nothing more to try

                Vector3 pos = CellCenter(x, z) + Vector3.up * wallHeight;
                Quaternion rotation = Quaternion.Euler(0f, rng.Next(4) * 90f, 0f);

                // F40 slice A decision 6: a low ceiling (floor 3) can't fit every prop without brushing
                // the player. Checked after every RNG draw this cell makes (chance, pick, rotation) so
                // the seed stream is unchanged - a taller floor still gets exactly the props, in exactly
                // the same rotations, it would have before this guard existed.
                if (!prop.AllowLowHang && prop.Depth > wallHeight - 2.2f) continue;

                // F70: same rule as above - checked after every draw, so it never perturbs the stream.
                if (_setPieceCells.Contains(cell)) continue;

                PropPiece clone = Instantiate(prop, pos, rotation, _propsGroup);
                clone.gameObject.SetActive(true);
                clone.name = $"Prop_{prop.name}_{x}_{z}";
                ApplyModelVariants(clone, x, z, 0x4B22);
                foreach (HangingBodyFit fit in clone.GetComponentsInChildren<HangingBodyFit>(true)) fit.Fit(wallHeight); // F85: rope cut to this floor's ceiling
            }
        }
    }

    /// <summary>Weighted pick among a theme's props of one mount kind, honouring EnabledInMaze. Null if the theme has none of that kind (or none currently enabled).</summary>
    /// <param name="variantWeight">Multiplies the weight of a piece carrying DecalVariants (the blood pack decals). 1 = unchanged.</param>
    private static PropPiece PickWeightedProp(PropPiece[] props, PropPiece.MountKind mount, System.Random rng, float variantWeight = 1f)
    {
        float totalWeight = 0f;
        foreach (PropPiece candidate in props)
        {
            if (candidate == null || candidate.Mount != mount || !candidate.EnabledInMaze) continue;
            totalWeight += PickWeight(candidate, variantWeight);
        }
        if (totalWeight <= 0f) return null;

        float roll = (float)(rng.NextDouble() * totalWeight);
        float cumulative = 0f;
        foreach (PropPiece candidate in props)
        {
            if (candidate == null || candidate.Mount != mount || !candidate.EnabledInMaze) continue;
            cumulative += PickWeight(candidate, variantWeight);
            if (roll <= cumulative) return candidate;
        }

        return null; // floating point edge case only
    }

    private static float PickWeight(PropPiece candidate, float variantWeight)
    {
        float weight = Mathf.Max(0.0001f, candidate.Weight);
        return variantWeight != 1f && candidate.GetComponent<DecalVariants>() != null ? weight * variantWeight : weight;
    }

    /// <summary>
    /// F70. Small vignettes built before the navmesh bake so their body colliders carve it like a locker
    /// does. Reserves a cell each: skipped afterwards by BuildWallProps, BuildCeilingProps, BuildFloorProps
    /// and BuildDecals (decision 9). Runs before BuildWallProps, so this is also where _propWalls is
    /// cleared (decision: BuildWallProps no longer clears it itself, since it needs to see the
    /// reservations this pass writes).
    ///
    /// Candidates are cells whose _distance is between 25% and 75% of the maze's max distance - far
    /// enough from the start to not spoil the opening seconds, near enough that a set piece is not
    /// stranded past where most runs reach. _starCells does not exist yet at this point in Awake (SpawnStars
    /// runs later), which is why the distance band substitutes for a "not on a star" rule.
    /// </summary>
    private void BuildSetPieces(System.Random rng)
    {
        _setPieceCells.Clear();
        _propWalls.Clear();
        if (_dressing.SetPieces == null || _dressing.SetPieces.Length == 0 || _dressing.SetPieceCount <= 0) return;

        Vector2Int aiCell = FarthestCell();
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);

        int maxDistance = 0;
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (_distance[x, z] > maxDistance) maxDistance = _distance[x, z];
            }
        }

        int minDistance = Mathf.RoundToInt(maxDistance * 0.25f);
        int maxAllowedDistance = Mathf.RoundToInt(maxDistance * 0.75f);

        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (x == 0 && z == 0) continue;
                if (cell == aiCell) continue;
                if (lockerCellSet.Contains(cell)) continue;
                if (_distance[x, z] < minDistance || _distance[x, z] > maxAllowedDistance) continue;
                if (WallCount(x, z) == 0) continue; // needs at least one closed wall to mount on
                candidates.Add(cell);
            }
        }

        Shuffle(candidates, rng);

        List<Vector2Int> chosenCells = new List<Vector2Int>();
        List<string> chosenNames = new List<string>();
        List<Vector3> walls = new List<Vector3>(4);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        float maxWidth = cellSize - 1.4f;

        foreach (Vector2Int cell in candidates)
        {
            if (chosenCells.Count >= _dressing.SetPieceCount) break;
            if (!ChebyshevFarEnough(cell, chosenCells, 3)) continue;

            walls.Clear();
            if (_wallN[cell.x, cell.y]) walls.Add(Vector3.forward);
            if (_wallE[cell.x, cell.y]) walls.Add(Vector3.right);
            if (_wallS[cell.x, cell.y]) walls.Add(Vector3.back);
            if (_wallW[cell.x, cell.y]) walls.Add(Vector3.left);
            if (walls.Count == 0) continue;

            Vector3 dir = walls[rng.Next(walls.Count)];
            SetPiece piece = PickWeightedSetPiece(_dressing.SetPieces, rng, maxWidth);
            if (piece == null) continue; // nothing fits this floor's cell size

            Vector3 pos = CellCenter(cell.x, cell.y) + dir * backFaceDistance;
            SetPiece clone = Instantiate(piece, pos, Quaternion.LookRotation(-dir), _setPiecesGroup);
            clone.gameObject.SetActive(true);
            clone.name = $"SetPiece_{piece.name}_{cell.x}_{cell.y}";
            // Look-alike models (the FallenRunner's corpses), picked by a hash of the cell - no rng draw.
            if (clone.TryGetComponent(out ModelVariants models)) models.Apply((cell.x * 73856093) ^ (cell.y * 19349663) ^ 0x5EED);

            foreach (FlickerLight flicker in clone.GetComponentsInChildren<FlickerLight>(true))
            {
                flicker.Configure(rng.Next());
            }

            chosenCells.Add(cell);
            chosenNames.Add(piece.name);
            _setPieceCells.Add(cell);

            if (!_propWalls.TryGetValue(cell, out List<Vector3> taken))
            {
                taken = new List<Vector3>();
                _propWalls[cell] = taken;
            }
            taken.Add(dir);
        }

        if (chosenNames.Count > 0)
        {
            Debug.Log($"MazeGenerator: {chosenNames.Count} set pieces: {string.Join(", ", chosenNames)}", this);
        }
    }

    /// <summary>Weighted pick among a set of pieces, honouring EnabledInMaze and Width &lt;= maxWidth (a floor with a small cellSize can't fit every set piece). Null if nothing fits or is enabled.</summary>
    private static SetPiece PickWeightedSetPiece(SetPiece[] pieces, System.Random rng, float maxWidth)
    {
        float totalWeight = 0f;
        foreach (SetPiece candidate in pieces)
        {
            if (candidate == null || !candidate.EnabledInMaze || candidate.Width > maxWidth) continue;
            totalWeight += Mathf.Max(0.0001f, candidate.Weight);
        }
        if (totalWeight <= 0f) return null;

        float roll = (float)(rng.NextDouble() * totalWeight);
        float cumulative = 0f;
        foreach (SetPiece candidate in pieces)
        {
            if (candidate == null || !candidate.EnabledInMaze || candidate.Width > maxWidth) continue;
            cumulative += Mathf.Max(0.0001f, candidate.Weight);
            if (roll <= cumulative) return candidate;
        }

        return null; // floating point edge case only
    }

    /// <summary>Chebyshev (grid/king-move) separation check, used by set pieces and kit trees so neither clusters in one corner. Manhattan (IsFarEnough) is what stars/lockers/shards use; Chebyshev is what the F70 plan specifies for these two.</summary>
    private static bool ChebyshevFarEnough(Vector2Int candidate, List<Vector2Int> chosen, int minSeparation)
    {
        foreach (Vector2Int other in chosen)
        {
            int chebyshev = Mathf.Max(Mathf.Abs(candidate.x - other.x), Mathf.Abs(candidate.y - other.y));
            if (chebyshev < minSeparation) return false;
        }

        return true;
    }

    /// <summary>
    /// F70, built after the navmesh bake: low clutter against the wall base, no collider (decision 4 -
    /// the player walks through it, the hunter never needs to path round it). New RNG stream (+6), never
    /// read by anything that existed before this feature.
    /// </summary>
    private void BuildFloorProps(System.Random rng)
    {
        if (_dressing.Props == null || _dressing.Props.Length == 0) return;

        Vector2Int aiCell = FarthestCell();
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        List<Vector3> walls = new List<Vector3>(4);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (x == 0 && z == 0) continue;
                if (x == aiCell.x && z == aiCell.y) continue;

                Vector2Int cell = new Vector2Int(x, z);
                if (lockerCellSet.Contains(cell)) continue;
                if (_setPieceCells.Contains(cell)) continue;
                if (rng.NextDouble() >= _dressing.FloorPropChance) continue;

                walls.Clear();
                if (_wallN[x, z]) walls.Add(Vector3.forward);
                if (_wallE[x, z]) walls.Add(Vector3.right);
                if (_wallS[x, z]) walls.Add(Vector3.back);
                if (_wallW[x, z]) walls.Add(Vector3.left);

                if (_propWalls.TryGetValue(cell, out List<Vector3> taken) && taken.Count > 0)
                {
                    walls.RemoveAll(dir => taken.Contains(dir));
                }
                if (walls.Count == 0) continue;

                Vector3 dir = walls[rng.Next(walls.Count)];
                PropPiece prop = PickWeightedProp(_dressing.Props, PropPiece.MountKind.Floor, rng);
                if (prop == null) return; // no floor props in this set, nothing more to try

                Vector3 tangent = Vector3.Cross(Vector3.up, dir);
                float lateral = (float)(rng.NextDouble() * 2.0 - 1.0) * (cellSize * 0.5f - 1.0f);
                Vector3 pos = CellCenter(x, z) + dir * backFaceDistance + tangent * lateral;
                float yaw = (float)(rng.NextDouble() * 2.0 - 1.0) * 25f;
                Quaternion rotation = Quaternion.LookRotation(-dir) * Quaternion.Euler(0f, yaw, 0f);

                PropPiece clone = Instantiate(prop, pos, rotation, _propsGroup);
                clone.gameObject.SetActive(true);
                clone.name = $"Prop_{prop.name}_{x}_{z}";
            }
        }
    }

    /// <summary>
    /// F70, built after the navmesh bake: alpha-clipped decal quads, analytic placement only (no
    /// raycasts - decision 4.5: colliders built earlier this same Awake are not reliably queryable before
    /// Physics.SyncTransforms, and a locker trigger would catch a ray anyway). New RNG stream (+7).
    /// </summary>
    private void BuildDecals(System.Random rng)
    {
        if (_dressing.Props == null || _dressing.Props.Length == 0) return;
        if (_dressing.MaxDecalsPerCell <= 0) return;

        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        List<Vector3> walls = new List<Vector3>(4);

        // decalDensity / bloodWeight: only this pass's own +7 stream changes, nothing after it reads it.
        float wallChance = Mathf.Min(0.9f, _dressing.WallDecalChance * decalDensity);
        float floorChance = Mathf.Min(0.9f, _dressing.FloorDecalChance * decalDensity);
        int maxPerCell = _dressing.MaxDecalsPerCell + (decalDensity > 1f ? 1 : 0);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (x == 0 && z == 0) continue;

                Vector2Int cell = new Vector2Int(x, z);
                if (_setPieceCells.Contains(cell)) continue;

                Vector3 lockerWallDir = _lockerWall.TryGetValue(cell, out Vector3 lw) ? lw : Vector3.zero;
                _propWalls.TryGetValue(cell, out List<Vector3> takenWalls);

                for (int i = 0; i < maxPerCell; i++)
                {
                    float threshold = wallChance * (i == 0 ? 1f : 0.4f);
                    if (rng.NextDouble() >= threshold) break;

                    walls.Clear();
                    if (_wallN[x, z] && lockerWallDir != Vector3.forward) walls.Add(Vector3.forward);
                    if (_wallE[x, z] && lockerWallDir != Vector3.right) walls.Add(Vector3.right);
                    if (_wallS[x, z] && lockerWallDir != Vector3.back) walls.Add(Vector3.back);
                    if (_wallW[x, z] && lockerWallDir != Vector3.left) walls.Add(Vector3.left);
                    if (takenWalls != null) walls.RemoveAll(dir => takenWalls.Contains(dir));
                    if (walls.Count == 0) continue;

                    Vector3 dir = walls[rng.Next(walls.Count)];
                    PropPiece decal = PickWeightedProp(_dressing.Props, PropPiece.MountKind.WallDecal, rng, bloodWeight);
                    if (decal == null) continue;

                    float lateral = (float)(rng.NextDouble() * 2.0 - 1.0) * (cellSize * 0.5f - 0.8f);
                    float scale = Mathf.Lerp(decal.DecalSizeRange.x, decal.DecalSizeRange.y, (float)rng.NextDouble());
                    float y = 0.3f + (float)rng.NextDouble() * 1.9f;
                    y = scale > 1.9f ? 1.25f : Mathf.Clamp(y, scale * 0.5f + 0.3f, 2.2f - scale * 0.5f);
                    float roll = decal.RandomRoll ? (float)rng.NextDouble() * 360f : 0f;

                    Vector3 tangent = Vector3.Cross(Vector3.up, dir);
                    Vector3 pos = CellCenter(x, z) + dir * (backFaceDistance - 0.04f) + tangent * lateral + Vector3.up * y;
                    Quaternion rotation = Quaternion.LookRotation(-dir) * Quaternion.Euler(0f, 0f, roll);

                    PropPiece clone = Instantiate(decal, pos, rotation, _decalsGroup);
                    clone.gameObject.SetActive(true);
                    clone.transform.localScale = Vector3.one * scale;
                    clone.name = $"Decal_{decal.name}_{x}_{z}_{i}";
                    ApplyDecalVariant(clone, x, z, i, 0);
                }

                for (int i = 0; i < maxPerCell; i++)
                {
                    float threshold = floorChance * (i == 0 ? 1f : 0.4f);
                    if (rng.NextDouble() >= threshold) break;

                    PropPiece decal = PickWeightedProp(_dressing.Props, PropPiece.MountKind.FloorDecal, rng, bloodWeight);
                    if (decal == null) continue;

                    float rawX = (float)(rng.NextDouble() * 2.0 - 1.0);
                    float rawZ = (float)(rng.NextDouble() * 2.0 - 1.0);
                    float scale = Mathf.Lerp(decal.DecalSizeRange.x, decal.DecalSizeRange.y, (float)rng.NextDouble());
                    float yaw = decal.RandomRoll ? (float)rng.NextDouble() * 360f : 0f;

                    // F79: a big decal (a rug) keeps nearer the cell centre, or its far end slides out
                    // under the wall into the next corridor. Same draws in the same order as before, and
                    // nothing changes for a decal up to 1.2 m.
                    float reach = Mathf.Max(0f, cellSize * 0.5f - 0.8f - Mathf.Max(0f, scale - 1.2f) * 0.5f);
                    float offsetX = rawX * reach;
                    float offsetZ = rawZ * reach;

                    Vector3 pos = CellCenter(x, z) + new Vector3(offsetX, 0f, offsetZ) + Vector3.up * (0.004f + 0.002f * i);
                    Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

                    PropPiece clone = Instantiate(decal, pos, rotation, _decalsGroup);
                    clone.gameObject.SetActive(true);
                    clone.transform.localScale = Vector3.one * scale;
                    clone.name = $"Decal_{decal.name}_{x}_{z}_{i}";
                    ApplyDecalVariant(clone, x, z, i, 1);
                }
            }
        }
    }

    /// <summary>
    /// A decal with DecalVariants (the blood pack splats/smears/pools) gets one of its look-alikes, picked by a
    /// hash of where it landed - like the bottle variants, no rng draw, so the +7 stream is unchanged.
    /// </summary>
    private static void ApplyDecalVariant(PropPiece clone, int x, int z, int i, int floor)
    {
        if (clone.TryGetComponent(out DecalVariants variants))
        {
            variants.Apply((x * 73856093) ^ (z * 19349663) ^ (i * 83492791) ^ (floor * 2654435));
        }
    }

    /// <summary>F70 decision 12: one camera-attached dust ParticleSystem per run. Not parented to the maze - DustMotes re-parents itself to the player camera lazily, once FirstPersonRig exists.</summary>
    private void BuildDust()
    {
        if (_dressing.Dust == null) return;

        DustMotes clone = Instantiate(_dressing.Dust);
        clone.gameObject.SetActive(true);
        clone.MarkRuntimeClone();
        clone.name = "DustMotes";
    }

    // ---------------------------------------------------------------- wall lamps

    /// <summary>
    /// Built after BuildCeiling, i.e. after the navmesh bake - a lamp at 2.6 m would not be carved out
    /// anyway (it sits above the 0.5 m agent), but this keeps the "after the bake" rule for anything
    /// that is not a wall.
    /// </summary>
    private void BuildWallLamps()
    {
        _lamps.Clear();
        _lampCells.Clear();
        if (!wallLamps) return;

        System.Random rng = new System.Random(UsedSeed + 2);
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);

        List<Vector2Int> cells = new List<Vector2Int>(_lockerCells);

        List<Vector2Int> others = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int c = new Vector2Int(x, z);
                if (lockerCellSet.Contains(c)) continue;
                others.Add(c);
            }
        }

        Shuffle(others, rng);
        int extraCount = Mathf.Max(0, (width * height) / Mathf.Max(1, cellsPerLamp));
        for (int i = 0; i < extraCount && i < others.Count; i++)
        {
            cells.Add(others[i]);
        }

        // Unused by the themed path (each clone brings its own fixture material), so skip building it.
        Material sharedMaterial = _theme == null ? BuildLampMaterial() : null;
        foreach (Vector2Int cell in cells)
        {
            BuildWallLamp(cell, lockerCellSet.Contains(cell), sharedMaterial, rng);
        }
    }

    private Material BuildLampMaterial()
    {
        Material source = FindStarMaterial();
        if (source == null) return null;

        Material material = new Material(source) { name = "Maze_LampFixture" };
        material.EnableKeyword("_EMISSION");
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", lampColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", lampColor);
        // F71: was 2.5, same as the star. Dimmer fixture glow so a lamp pool reads as a pool rather than
        // a second sun - the star keeps 2.5 so it still reads as the thing worth finding in the dark.
        material.SetColor("_EmissionColor", lampColor * 1.2f);

        _runtimeMaterials.Add(material);
        return material;
    }

    /// <summary>Direction from the cell centre toward the wall the lamp mounts on. Shared by both build paths so the RNG stream (UsedSeed + 2) stays identical whether or not a theme is in play - the set of lamp cells and their walls is unchanged by theming.</summary>
    private Vector3 ResolveLampWallDirection(Vector2Int cell, bool isLockerCell, System.Random rng)
    {
        if (isLockerCell)
        {
            // Above the locker: the same back wall the locker sits against.
            return _lockerWall[cell];
        }

        List<Vector3> options = new List<Vector3>();
        if (_wallN[cell.x, cell.y]) options.Add(Vector3.forward);
        if (_wallE[cell.x, cell.y]) options.Add(Vector3.right);
        if (_wallS[cell.x, cell.y]) options.Add(Vector3.back);
        if (_wallW[cell.x, cell.y]) options.Add(Vector3.left);
        if (options.Count == 0) return Vector3.zero; // every real cell has at least one wall
        return options[rng.Next(options.Count)];
    }

    private void BuildWallLamp(Vector2Int cell, bool isLockerCell, Material material, System.Random rng)
    {
        int x = cell.x;
        int z = cell.y;
        Vector3 cellCenter = CellCenter(x, z);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;

        Vector3 wallDirection = ResolveLampWallDirection(cell, isLockerCell, rng);
        if (wallDirection == Vector3.zero) return;

        Vector3 position = cellCenter + wallDirection * (backFaceDistance - 0.08f) + Vector3.up * lampHeight;
        Quaternion rotation = Quaternion.LookRotation(-wallDirection, Vector3.up);

        // F70 (4.6): kit mode gets its own plaque lamp instead of the grey primitive cube. _theme is
        // always null in kit mode (ResolveKitTheme), so this never competes with the themed branch below.
        // Draws faulty then the phase exactly like the primitive branch (rng.NextDouble() < chance, then
        // rng.Next(1000)), so the lamp RNG stream (UsedSeed + 2) is unchanged either way.
        if (_kitMode && kitTheme != null && kitTheme.WallLamp != null)
        {
            // Flush with the wall face: the plaque's back is at its pivot (z = 0) and it is only 0.1 m
            // deep, so the 0.08 m inset the other branches use would bury it in the wall.
            Vector3 plaquePosition = cellCenter + wallDirection * backFaceDistance + Vector3.up * lampHeight;
            GameObject plaque = Instantiate(kitTheme.WallLamp, plaquePosition, rotation, _lampsGroup);
            plaque.SetActive(true);
            plaque.name = $"WallLamp_{x}_{z}";

            GameObject kitLightHolder = new GameObject("Light");
            kitLightHolder.transform.SetParent(plaque.transform, false);
            kitLightHolder.transform.localPosition = new Vector3(0f, 0f, 0.25f);

            Light kitLight = kitLightHolder.AddComponent<Light>();
            kitLight.type = LightType.Point;
            kitLight.color = lampColor;
            kitLight.range = lampRange;
            kitLight.intensity = lampIntensity;
            kitLight.shadows = LightShadows.None;

            WallLamp kitLamp = plaque.AddComponent<WallLamp>();
            bool kitFaulty = rng.NextDouble() < faultyLampChance;
            kitLamp.Configure(kitLight, plaque.GetComponentInChildren<Renderer>(), lampIntensity, kitFaulty, rng.Next(1000), isLockerCell);
            _lamps.Add(kitLamp);
            _lampCells.Add(cell);
            return;
        }

        if (_theme != null && _theme.Lamp != null)
        {
            WallLamp themedLamp = Instantiate(_theme.Lamp, position, rotation, _lampsGroup);
            themedLamp.gameObject.SetActive(true);
            themedLamp.name = $"WallLamp_{x}_{z}";

            // The artist owns the fixture's look; the profile still owns brightness/range (stealth
            // exposure depends on them) and colour comes from the theme, not the Inspector default.
            Light themedLight = themedLamp.LightOrChild;
            if (themedLight != null)
            {
                themedLight.color = _theme.LampColor;
                themedLight.range = lampRange;
                themedLight.intensity = lampIntensity;
                themedLight.shadows = LightShadows.None;
            }

            bool themedFaulty = rng.NextDouble() < _theme.FaultyLampChance;
            themedLamp.Configure(lampIntensity, themedFaulty, rng.Next(1000), isLockerCell);
            _lamps.Add(themedLamp);
            _lampCells.Add(cell);
            return;
        }

        GameObject fixture = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fixture.name = "WallLamp";
        fixture.layer = 0;
        fixture.transform.SetParent(_lampsGroup, false);
        fixture.transform.position = position;
        fixture.transform.rotation = rotation;
        fixture.transform.localScale = new Vector3(0.28f, 0.10f, 0.14f);

        Collider fixtureCollider = fixture.GetComponent<Collider>();
        if (fixtureCollider != null) Destroy(fixtureCollider);

        MeshRenderer fixtureRenderer = fixture.GetComponent<MeshRenderer>();
        if (material != null) fixtureRenderer.sharedMaterial = material;

        GameObject lightHolder = new GameObject("Light");
        lightHolder.transform.SetParent(fixture.transform, false);
        lightHolder.transform.localPosition = Vector3.zero;

        Light light = lightHolder.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = lampColor;
        light.range = lampRange;
        light.intensity = lampIntensity;
        light.shadows = LightShadows.None;

        WallLamp lamp = fixture.AddComponent<WallLamp>();
        bool faulty = rng.NextDouble() < faultyLampChance;
        lamp.Configure(light, fixtureRenderer, lampIntensity, faulty, rng.Next(1000), isLockerCell);
        _lamps.Add(lamp);
        _lampCells.Add(cell);
    }

    // ---------------------------------------------------------------- geometry

    /// <summary>
    /// The source materials are shared with the rest of the tutorial scene, and Material_WhiteGrid is
    /// emissive white - the walls would self-illuminate no matter how dark the scene lighting got.
    /// Runtime copies keep the assets on disk untouched.
    /// </summary>
    private Material DarkCopy(Material source, Color tint, string copyName)
    {
        if (source == null) return null;

        Material copy = new Material(source) { name = copyName };
        copy.DisableKeyword("_EMISSION");
        copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        if (copy.HasProperty("_EmissionColor")) copy.SetColor("_EmissionColor", Color.black);
        if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", tint);
        if (copy.HasProperty("_Color")) copy.SetColor("_Color", tint);
        if (copy.HasProperty("_Smoothness")) copy.SetFloat("_Smoothness", surfaceSmoothness);
        if (copy.HasProperty("_Glossiness")) copy.SetFloat("_Glossiness", surfaceSmoothness);

        _runtimeMaterials.Add(copy);
        return copy;
    }

    private void BuildGeometry()
    {
        _mazeRoot = new GameObject("Maze").transform;
        _mazeRoot.position = origin;

        _wallsGroup = Group("Walls");
        _pillarsGroup = Group("Pillars");
        _floorGroup = Group("Floor");
        _ceilingGroup = Group("Ceiling");
        _lampsGroup = Group("Lamps");
        _lockersGroup = Group("Lockers");
        _propsGroup = Group("Props");
        _setPiecesGroup = Group("SetPieces");
        _decalsGroup = Group("Decals");

        // Always built, even when themed: cheap, and it is what PhantomDirector's silhouette falls back
        // to if the theme itself has no wall material assigned.
        _wallMat = DarkCopy(wallMaterial, wallTint, "Maze_Wall");
        _floorMat = DarkCopy(floorMaterial, floorTint, "Maze_Floor");
        _ceilingMat = DarkCopy(wallMaterial, ceilingTint, "Maze_Ceiling");

        if (_kitMode)
        {
            BuildKitGeometry();
        }
        else if (_theme != null)
        {
            BuildThemedGeometry();
        }
        else
        {
            BuildPrimitiveGeometry();
        }
    }

    private Transform Group(string groupName)
    {
        Transform group = new GameObject(groupName).transform;
        group.SetParent(_mazeRoot, false);
        return group;
    }

    /// <summary>One floor tile and one wall piece clone per cell, from the theme's prefabs.</summary>
    private void BuildThemedGeometry()
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                GameObject tile = Instantiate(_theme.FloorTile, CellCenter(x, z), Quaternion.identity, _floorGroup);
                tile.SetActive(true);
                tile.name = $"Floor_{x}_{z}";
                tile.transform.localScale = new Vector3(cellSize / 4.5f, 1f, cellSize / 4.5f);
            }
        }

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                // Walls are shared, so only the west and south sides are built per cell; the outer
                // east and north sides are added once on the last column / row.
                if (_wallW[x, z])
                {
                    SpawnThemedWall(
                        $"Wall_W_{x}_{z}",
                        new Vector3(origin.x + x * cellSize, FloorTop, origin.z + (z + 0.5f) * cellSize),
                        Quaternion.Euler(0f, 90f, 0f));
                }

                if (_wallS[x, z])
                {
                    SpawnThemedWall(
                        $"Wall_S_{x}_{z}",
                        new Vector3(origin.x + (x + 0.5f) * cellSize, FloorTop, origin.z + z * cellSize),
                        Quaternion.identity);
                }

                if (x == width - 1 && _wallE[x, z])
                {
                    SpawnThemedWall(
                        $"Wall_E_{x}_{z}",
                        new Vector3(origin.x + (x + 1) * cellSize, FloorTop, origin.z + (z + 0.5f) * cellSize),
                        Quaternion.Euler(0f, 90f, 0f));
                }

                if (z == height - 1 && _wallN[x, z])
                {
                    SpawnThemedWall(
                        $"Wall_N_{x}_{z}",
                        new Vector3(origin.x + (x + 0.5f) * cellSize, FloorTop, origin.z + (z + 1) * cellSize),
                        Quaternion.identity);
                }
            }
        }
    }

    private void SpawnThemedWall(string wallName, Vector3 floorCenter, Quaternion rotation)
    {
        WallPiece wall = Instantiate(_theme.Wall, floorCenter, rotation, _wallsGroup);
        wall.gameObject.SetActive(true);
        wall.name = wallName;
        wall.transform.localScale = wall.ScaleFor(cellSize + wallThickness, wallHeight, wallThickness);
    }

    private void BuildPrimitiveGeometry()
    {
        float spanX = width * cellSize + wallThickness;
        float spanZ = height * cellSize + wallThickness;

        // Floor: 0.2 thick, top exactly at FloorTop so the walls and the player stand on it.
        CreateBox(
            "Floor",
            new Vector3(origin.x + width * cellSize * 0.5f, FloorTop - 0.1f, origin.z + height * cellSize * 0.5f),
            new Vector3(spanX, 0.2f, spanZ),
            _floorMat,
            _floorGroup);

        float wallCenterY = FloorTop + wallHeight * 0.5f;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                // Walls are shared, so only the west and south sides are built per cell; the outer
                // east and north sides are added once on the last column / row.
                if (_wallW[x, z])
                {
                    CreateBox(
                        $"Wall_W_{x}_{z}",
                        new Vector3(origin.x + x * cellSize, wallCenterY, origin.z + (z + 0.5f) * cellSize),
                        new Vector3(wallThickness, wallHeight, cellSize + wallThickness),
                        _wallMat,
                        _wallsGroup);
                }

                if (_wallS[x, z])
                {
                    CreateBox(
                        $"Wall_S_{x}_{z}",
                        new Vector3(origin.x + (x + 0.5f) * cellSize, wallCenterY, origin.z + z * cellSize),
                        new Vector3(cellSize + wallThickness, wallHeight, wallThickness),
                        _wallMat,
                        _wallsGroup);
                }

                if (x == width - 1 && _wallE[x, z])
                {
                    CreateBox(
                        $"Wall_E_{x}_{z}",
                        new Vector3(origin.x + (x + 1) * cellSize, wallCenterY, origin.z + (z + 0.5f) * cellSize),
                        new Vector3(wallThickness, wallHeight, cellSize + wallThickness),
                        _wallMat,
                        _wallsGroup);
                }

                if (z == height - 1 && _wallN[x, z])
                {
                    CreateBox(
                        $"Wall_N_{x}_{z}",
                        new Vector3(origin.x + (x + 0.5f) * cellSize, wallCenterY, origin.z + (z + 1) * cellSize),
                        new Vector3(cellSize + wallThickness, wallHeight, wallThickness),
                        _wallMat,
                        _wallsGroup);
                }
            }
        }
    }

    // ---------------------------------------------------------------- kit maze (F48)

    /// <summary>
    /// Assembles the maze from the Maze Modular Puzzle Kit: floors are tiled per cell with the
    /// recursive square-tile filler (TileFloorRect), walls are laid out per shared segment with the
    /// greedy 3/2/1 span filler (FillSpan/SpawnKitWallRun) - the same west+south-per-cell,
    /// east-on-last-column, north-on-last-row walk BuildPrimitiveGeometry uses, just spanning a whole
    /// cell edge with multiple pieces instead of one box. All pieces sit at kitY = FloorTop - 0.1 (the
    /// kit convention: the bottom 0.1 m of a wall is buried in the floor slab).
    /// </summary>
    private void BuildKitGeometry()
    {
        float kitY = FloorTop - 0.1f;
        int cell = Mathf.RoundToInt(cellSize);
        int[] fill = FillSpan(cell);
        _kitWallSegments.Clear();

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                TileFloorRect(origin.x + x * cell, origin.z + z * cell, kitY, cell, cell);
            }
        }

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                // Walls are shared, so only the west and south sides are built per cell; the outer
                // east and north sides are added once on the last column / row - same walk as
                // BuildPrimitiveGeometry, but each side is a run of kit pieces rather than one box.
                // F70: each call also hands SpawnKitWallRun the (nullable) cell on either side of the
                // segment, so it can record which face of any 3 m piece looks into a real cell versus the
                // void beyond the maze - BuildKitWallLights and BuildKitGates need that later.
                if (_wallW[x, z])
                {
                    SpawnKitWallRun(
                        new Vector3(origin.x + x * cell, kitY, origin.z + z * cell),
                        alongX: false, fill, $"Wall_W_{x}_{z}",
                        new Vector2Int(x, z), x > 0 ? (Vector2Int?)new Vector2Int(x - 1, z) : null);
                }

                if (_wallS[x, z])
                {
                    SpawnKitWallRun(
                        new Vector3(origin.x + x * cell, kitY, origin.z + z * cell),
                        alongX: true, fill, $"Wall_S_{x}_{z}",
                        new Vector2Int(x, z), z > 0 ? (Vector2Int?)new Vector2Int(x, z - 1) : null);
                }

                if (x == width - 1 && _wallE[x, z])
                {
                    SpawnKitWallRun(
                        new Vector3(origin.x + (x + 1) * cell, kitY, origin.z + z * cell),
                        alongX: false, fill, $"Wall_E_{x}_{z}",
                        null, new Vector2Int(x, z));
                }

                if (z == height - 1 && _wallN[x, z])
                {
                    SpawnKitWallRun(
                        new Vector3(origin.x + x * cell, kitY, origin.z + (z + 1) * cell),
                        alongX: true, fill, $"Wall_N_{x}_{z}",
                        null, new Vector2Int(x, z));
                }
            }
        }

        LogKitBoundsCheck();
    }

    /// <summary>
    /// Recursively tiles a w x d integer-metre rectangle (a maze cell) with the kit's square floor
    /// tiles: the biggest square (3, 2 or 1 m) that fits in the corner, then the two leftover strips.
    /// Depth is bounded by the cell size; no allocation. Every clone's collider is repaired - Floor_3M
    /// ships with a degenerate (zero-thickness) collider, harmless to also touch on the 1M/2M ones.
    /// </summary>
    private void TileFloorRect(float x0, float z0, float kitY, int w, int d)
    {
        if (w <= 0 || d <= 0) return;

        int s = Mathf.Min(3, Mathf.Min(w, d));
        GameObject prefab = kitTheme.FloorFor(s);
        GameObject tile = Instantiate(prefab, new Vector3(x0 + s, kitY, z0), Quaternion.identity, _floorGroup);
        tile.SetActive(true);
        tile.name = $"Floor_{s}M_{x0:0}_{z0:0}";
        RepairKitFloorCollider(tile);

        TileFloorRect(x0 + s, z0, kitY, w - s, d);     // strip to the east, full depth
        TileFloorRect(x0, z0 + s, kitY, s, d - s);     // strip to the north, under the square just placed
    }

    /// <summary>Floor_3M's BoxCollider is degenerate (zero thickness at the top surface) straight out of the kit; fix it on every clone.</summary>
    private static void RepairKitFloorCollider(GameObject tile)
    {
        BoxCollider box = tile.GetComponent<BoxCollider>();
        if (box == null) return;

        Vector3 size = box.size;
        size.y = 0.1f;
        box.size = size;

        Vector3 center = box.center;
        center.y = 0.05f;
        box.center = center;
    }

    /// <summary>
    /// Lays a run of kit wall pieces along one shared cell-edge segment, from vertex A toward +X
    /// (alongX) or +Z (!alongX), using the lengths in fill (see FillSpan). Placement matches the
    /// measured pivot conventions: an east-west run's pivot sits at its +X end (position = A + offset +
    /// n along X, identity rotation); a north-south run's pivot sits at its start (position = A + offset
    /// along Z, rotated 90 degrees about Y so local +X extends toward world +Z).
    ///
    /// F70: cellPlus/cellMinus are the (nullable) cells on the local +Z / -Z side of this whole segment -
    /// constant for every piece in the run, since a run is one wall of one cell-pair just split into
    /// multiple prefab lengths. Every 3 m piece is recorded in _kitWallSegments (keyed by namePrefix) so
    /// BuildKitGates can replace one and BuildKitWallLights can dress the rest.
    /// </summary>
    private void SpawnKitWallRun(Vector3 a, bool alongX, int[] fill, string namePrefix, Vector2Int? cellPlus, Vector2Int? cellMinus)
    {
        float offset = 0f;
        for (int i = 0; i < fill.Length; i++)
        {
            int n = fill[i];
            GameObject prefab = kitTheme.WallFor(n);
            Vector3 position = alongX
                ? new Vector3(a.x + offset + n, a.y, a.z)
                : new Vector3(a.x, a.y, a.z + offset);
            Quaternion rotation = alongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);

            GameObject wall = Instantiate(prefab, position, rotation, _wallsGroup);
            wall.SetActive(true);
            wall.name = $"{namePrefix}_{i}";

            if (n == 3)
            {
                if (!_kitWallSegments.TryGetValue(namePrefix, out List<KitWallSegment> pieces))
                {
                    pieces = new List<KitWallSegment>();
                    _kitWallSegments[namePrefix] = pieces;
                }
                pieces.Add(new KitWallSegment { Piece = wall, CellPlus = cellPlus, CellMinus = cellMinus });
            }

            offset += n;
        }
    }

    /// <summary>Greedy fill of an integer span with 3/2/1 m kit pieces - as many 3s as fit, then 2s, then 1s. Empty for length &lt;= 0.</summary>
    private static int[] FillSpan(int length)
    {
        List<int> pieces = new List<int>();
        int remaining = length;
        while (remaining >= 3) { pieces.Add(3); remaining -= 3; }
        while (remaining >= 2) { pieces.Add(2); remaining -= 2; }
        while (remaining >= 1) { pieces.Add(1); remaining -= 1; }
        return pieces.ToArray();
    }

    /// <summary>Instantiates kitTheme.Pillar at every vertex a wall touches (closedCount &gt; 0) - every straight-run seam too, not only corners/junctions, since that is what hides the 3M|2M seams and is how the kit is meant to be assembled (decision 6).</summary>
    private void BuildKitPillars()
    {
        if (kitTheme == null || kitTheme.Pillar == null) return;
        float kitY = FloorTop - 0.1f;

        for (int vx = 0; vx <= width; vx++)
        {
            for (int vz = 0; vz <= height; vz++)
            {
                bool west = HorizontalWallClosed(vx - 1, vz);
                bool east = HorizontalWallClosed(vx, vz);
                bool south = VerticalWallClosed(vx, vz - 1);
                bool north = VerticalWallClosed(vx, vz);
                int closedCount = (west ? 1 : 0) + (east ? 1 : 0) + (south ? 1 : 0) + (north ? 1 : 0);
                if (closedCount == 0) continue;

                Vector3 position = new Vector3(origin.x + vx * cellSize, kitY, origin.z + vz * cellSize);
                GameObject pillar = Instantiate(kitTheme.Pillar, position, Quaternion.identity, _pillarsGroup);
                pillar.SetActive(true);
                pillar.name = $"Pillar_{vx}_{vz}";
            }
        }
    }

    /// <summary>
    /// Regression guard for the measured pivot conventions (plannings/kit-maze-plan.md): logs the
    /// combined renderer bounds of the Walls and Floor groups against the expected maze footprint. A
    /// flipped pivot shows up immediately as a bounds box shifted by roughly one cell.
    /// </summary>
    private void LogKitBoundsCheck()
    {
        Bounds? wallBounds = CombinedRendererBounds(_wallsGroup);
        Bounds? floorBounds = CombinedRendererBounds(_floorGroup);

        float expectedMinX = origin.x, expectedMaxX = origin.x + width * cellSize;
        float expectedMinZ = origin.z, expectedMaxZ = origin.z + height * cellSize;

        string wallText = wallBounds.HasValue
            ? $"x [{wallBounds.Value.min.x:0.00}, {wallBounds.Value.max.x:0.00}] z [{wallBounds.Value.min.z:0.00}, {wallBounds.Value.max.z:0.00}]"
            : "none";
        string floorText = floorBounds.HasValue
            ? $"x [{floorBounds.Value.min.x:0.00}, {floorBounds.Value.max.x:0.00}] z [{floorBounds.Value.min.z:0.00}, {floorBounds.Value.max.z:0.00}]"
            : "none";

        Debug.Log($"MazeGenerator: kit bounds check - expected x [{expectedMinX:0.00}, {expectedMaxX:0.00}] z [{expectedMinZ:0.00}, {expectedMaxZ:0.00}] (+/- {wallThickness * 0.5f:0.00} m for walls). Walls: {wallText}. Floor: {floorText}.", this);
    }

    private static Bounds? CombinedRendererBounds(Transform group)
    {
        if (group == null) return null;
        Renderer[] renderers = group.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return null;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    // ---------------------------------------------------------------- kit dressing (F70)

    /// <summary>Pre-bake kit-only dressing: gates and trees both carry a body collider, so both must go in before BuildRuntimeNavMesh.</summary>
    private void BuildKitDressingPreBake(System.Random rng)
    {
        BuildKitGates(rng);
        BuildKitTrees(rng);
    }

    /// <summary>
    /// Replaces one 3 m wall piece per chosen perimeter segment with a barred Wall_Door - an exact
    /// drop-in swap (same measured pivot as Wall_3M). Never a segment adjacent to the start cell or
    /// already claimed by a set piece. The outside of the maze is a void, so the gate shows fog/black
    /// through the bars - the intended "locked exit" look.
    /// </summary>
    private void BuildKitGates(System.Random rng)
    {
        if (kitTheme.WallDoor == null || kitTheme.GateCount <= 0) return;

        List<(string Prefix, Vector2Int Cell)> perimeter = new List<(string, Vector2Int)>();
        for (int z = 0; z < height; z++)
        {
            if (_wallW[0, z]) perimeter.Add(($"Wall_W_0_{z}", new Vector2Int(0, z)));
        }
        for (int x = 0; x < width; x++)
        {
            if (_wallS[x, 0]) perimeter.Add(($"Wall_S_{x}_0", new Vector2Int(x, 0)));
        }
        for (int z = 0; z < height; z++)
        {
            if (_wallE[width - 1, z]) perimeter.Add(($"Wall_E_{width - 1}_{z}", new Vector2Int(width - 1, z)));
        }
        for (int x = 0; x < width; x++)
        {
            if (_wallN[x, height - 1]) perimeter.Add(($"Wall_N_{x}_{height - 1}", new Vector2Int(x, height - 1)));
        }

        perimeter.RemoveAll(segment => IsAdjacentToStart(segment.Cell) || _setPieceCells.Contains(segment.Cell));
        ShuffleList(perimeter, rng);

        List<Vector2Int> chosenCells = new List<Vector2Int>();
        int placed = 0;
        foreach ((string prefix, Vector2Int cell) in perimeter)
        {
            if (placed >= kitTheme.GateCount) break;
            if (!ChebyshevFarEnough(cell, chosenCells, 3)) continue;

            BuildKitGate(prefix, cell);
            chosenCells.Add(cell);
            placed++;
        }
    }

    /// <summary>True for the start cell and its immediate (Chebyshev) neighbours - a gate must not be the first thing the player faces.</summary>
    private static bool IsAdjacentToStart(Vector2Int cell)
    {
        return Mathf.Max(Mathf.Abs(cell.x), Mathf.Abs(cell.y)) <= 1;
    }

    /// <summary>Swaps the first recorded 3 m piece of one wall segment for a barred gate: same parent/position/rotation/scale, old piece disabled immediately (Destroy is deferred to end of frame, and the bake runs before then) then destroyed.</summary>
    private void BuildKitGate(string prefix, Vector2Int cell)
    {
        if (!_kitWallSegments.TryGetValue(prefix, out List<KitWallSegment> pieces) || pieces.Count == 0) return;

        KitWallSegment segment = pieces[0];
        pieces.RemoveAt(0);
        GameObject old = segment.Piece;
        if (old == null) return;

        GameObject gate = Instantiate(kitTheme.WallDoor, old.transform.position, old.transform.rotation, old.transform.parent);
        gate.transform.localScale = old.transform.localScale;
        gate.SetActive(true);
        gate.name = old.name + "_Gate";

        // Defensive fallback: the prefab is expected to ship its own barred colliders (measured in the
        // plan), but if a hand edit ever strips them, the opening must still stop the player.
        if (gate.GetComponentInChildren<Collider>() == null)
        {
            BoxCollider box = gate.AddComponent<BoxCollider>();
            box.size = new Vector3(3f, 4f, 0.4f);
            box.center = new Vector3(-1.5f, 2f, 0f);
        }

        old.SetActive(false);
        Destroy(old);
    }

    /// <summary>
    /// One scaled-down tree per chosen L-corner cell (exactly two closed, perpendicular walls) - never
    /// the start/AI/locker/set-piece cell. Kept collider (trunk), so the hunter paths round it like any
    /// other pre-bake body. Reserves the cell's two walls in _propWalls and the cell itself in
    /// _setPieceCells, so later decal/clutter passes leave it alone.
    /// </summary>
    private void BuildKitTrees(System.Random rng)
    {
        if (kitTheme.Trees == null || kitTheme.Trees.Length == 0 || kitTheme.TreeCount <= 0) return;

        Vector2Int aiCell = FarthestCell();
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);

        List<(Vector2Int Cell, Vector3 DirA, Vector3 DirB)> candidates = new List<(Vector2Int, Vector3, Vector3)>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (x == 0 && z == 0) continue;
                if (cell == aiCell) continue;
                if (lockerCellSet.Contains(cell)) continue;
                if (_setPieceCells.Contains(cell)) continue;
                if (WallCount(x, z) != 2) continue;

                bool northSouth = _wallN[x, z] && _wallS[x, z];
                bool eastWest = _wallE[x, z] && _wallW[x, z];
                if (northSouth || eastWest) continue; // collinear - a straight wall, not a corner

                Vector3 dirA = Vector3.zero;
                Vector3 dirB = Vector3.zero;
                if (_wallN[x, z]) dirA = Vector3.forward;
                if (_wallE[x, z]) { if (dirA == Vector3.zero) dirA = Vector3.right; else dirB = Vector3.right; }
                if (_wallS[x, z]) { if (dirA == Vector3.zero) dirA = Vector3.back; else dirB = Vector3.back; }
                if (_wallW[x, z]) { if (dirA == Vector3.zero) dirA = Vector3.left; else dirB = Vector3.left; }

                candidates.Add((cell, dirA, dirB));
            }
        }

        ShuffleList(candidates, rng);

        List<Vector2Int> chosenCells = new List<Vector2Int>();
        int placed = 0;
        float maxHeight = FloorTop + wallHeight - 0.1f;

        foreach ((Vector2Int cell, Vector3 dirA, Vector3 dirB) in candidates)
        {
            if (placed >= kitTheme.TreeCount) break;
            if (!ChebyshevFarEnough(cell, chosenCells, 4)) continue;

            GameObject prefab = kitTheme.Trees[rng.Next(kitTheme.Trees.Length)];
            if (prefab == null) continue;

            Vector3 corner = CellCenter(cell.x, cell.y) + (dirA + dirB) * (cellSize * 0.5f - wallThickness * 0.5f - 0.9f);
            float yaw = (float)rng.NextDouble() * 360f;

            GameObject tree = Instantiate(prefab, corner, Quaternion.Euler(0f, yaw, 0f), _propsGroup);
            tree.SetActive(true);
            tree.name = $"Tree_{cell.x}_{cell.y}";
            tree.transform.localScale = Vector3.one * 0.55f;

            Renderer[] renderers = tree.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                float excess = bounds.max.y - corner.y;
                float allowed = maxHeight - corner.y;
                if (excess > 0.001f && allowed < excess)
                {
                    tree.transform.localScale *= Mathf.Clamp01(allowed / excess);
                }
            }

            if (!_propWalls.TryGetValue(cell, out List<Vector3> taken))
            {
                taken = new List<Vector3>();
                _propWalls[cell] = taken;
            }
            taken.Add(dirA);
            taken.Add(dirB);
            _setPieceCells.Add(cell);

            chosenCells.Add(cell);
            placed++;
        }
    }

    /// <summary>
    /// Post-bake: dresses every still-standing (not replaced by a gate) recorded 3 m wall piece with a
    /// chance of a Wall_Light emissive overlay on whichever face(s) look into a real, non-reserved cell -
    /// both faces for an interior wall (one picked at random), only the inward face for a perimeter one.
    /// No Light component of its own (decision 10) - it is glowing geometry only.
    /// </summary>
    private void BuildKitWallLights(System.Random rng)
    {
        if (kitTheme.WallLight == null || kitTheme.WallLightChance <= 0f) return;

        foreach (List<KitWallSegment> pieces in _kitWallSegments.Values)
        {
            foreach (KitWallSegment segment in pieces)
            {
                if (segment.Piece == null || !segment.Piece.activeSelf) continue; // replaced by a gate
                if (rng.NextDouble() >= kitTheme.WallLightChance) continue;

                bool plusInside = segment.CellPlus.HasValue && !_setPieceCells.Contains(segment.CellPlus.Value);
                bool minusInside = segment.CellMinus.HasValue && !_setPieceCells.Contains(segment.CellMinus.Value);
                if (!plusInside && !minusInside) continue; // both faces are void or reserved

                bool useMinus = plusInside && minusInside ? rng.NextDouble() < 0.5 : minusInside;

                GameObject overlay = Instantiate(kitTheme.WallLight, segment.Piece.transform);
                overlay.transform.localPosition = useMinus ? new Vector3(-3f, 0f, 0f) : Vector3.zero;
                overlay.transform.localRotation = useMinus ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                overlay.SetActive(true);
                overlay.name = segment.Piece.name + "_Light";
            }
        }
    }

    /// <summary>Same Fisher-Yates as Shuffle(List&lt;Vector2Int&gt;, rng), generic so the F70 kit-dressing passes can shuffle tuples without duplicating it.</summary>
    private static void ShuffleList<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// Built after the navmesh bake, so there is never a question of whether the slab reads as a
    /// walkable surface. Themed: one ceiling tile clone per cell. Primitive: a single scaled cube
    /// (rather than a quad or plane, so it has a real underside).
    /// </summary>
    private void BuildCeiling()
    {
        if (!buildCeiling) return;

        if (_theme != null)
        {
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    Vector3 position = CellCenter(x, z) + Vector3.up * wallHeight;
                    GameObject tile = Instantiate(_theme.CeilingTile, position, Quaternion.identity, _ceilingGroup);
                    tile.SetActive(true);
                    tile.name = $"Ceiling_{x}_{z}";
                    tile.transform.localScale = new Vector3(cellSize / 4.5f, 1f, cellSize / 4.5f);
                }
            }
            return;
        }

        CreateBox(
            "Ceiling",
            new Vector3(
                origin.x + width * cellSize * 0.5f,
                FloorTop + wallHeight + 0.1f,
                origin.z + height * cellSize * 0.5f),
            new Vector3(width * cellSize + wallThickness, 0.2f, height * cellSize + wallThickness),
            _ceilingMat,
            _ceilingGroup);
    }

    private void CreateBox(string boxName, Vector3 center, Vector3 size, Material material, Transform parent)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = boxName;
        // Layer 0 is what the player's GroundLayers mask and the camera's obstacle filter look at.
        box.layer = 0;
        box.transform.SetParent(parent, false);
        box.transform.position = center;
        box.transform.localScale = size;

        if (material != null)
        {
            // sharedMaterial: one instance for the whole maze instead of one per cube
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }

    /// <summary>Combines the static themed geometry groups after the bake and after the ceiling exists. Off by default (staticBatchThemedGeometry); never run on lamps, lockers or props.</summary>
    private void CombineStaticGroups()
    {
        if (_wallsGroup != null) StaticBatchingUtility.Combine(_wallsGroup.gameObject);
        if (_pillarsGroup != null) StaticBatchingUtility.Combine(_pillarsGroup.gameObject);
        if (_floorGroup != null) StaticBatchingUtility.Combine(_floorGroup.gameObject);
        if (_ceilingGroup != null) StaticBatchingUtility.Combine(_ceilingGroup.gameObject);
    }

    // ---------------------------------------------------------------- navmesh

    private void BuildRuntimeNavMesh()
    {
        if (navMeshSurface == null)
        {
            navMeshSurface = FindAnyObjectByType<NavMeshSurface>();
        }

        if (navMeshSurface == null)
        {
            Debug.LogWarning("MazeGenerator: no NavMeshSurface in the scene, the AI will not move.", this);
            return;
        }

        Vector3 center = new Vector3(
            origin.x + width * cellSize * 0.5f,
            FloorTop,
            origin.z + height * cellSize * 0.5f);

        // The surface bakes around its own transform, so move it onto the maze first.
        navMeshSurface.transform.position = center;
        navMeshSurface.transform.rotation = Quaternion.identity;
        navMeshSurface.collectObjects = CollectObjects.Volume;
        navMeshSurface.center = new Vector3(0f, 1.5f, 0f);
        navMeshSurface.size = new Vector3(
            width * cellSize + 2f * wallThickness + 2f,
            Mathf.Max(6f, wallHeight + 3f),
            height * cellSize + 2f * wallThickness + 2f);

        // Synchronous: RemoveData discards the stale baked asset, so no navmesh survives under the old scene.
        navMeshSurface.BuildNavMesh();
    }

    // ---------------------------------------------------------------- contents

    private void DisableExistingPickups()
    {
        if (!disableExistingPickups) return;

        Pickup[] pickups = FindObjectsByType<Pickup>(FindObjectsInactive.Exclude);
        foreach (Pickup pickup in pickups)
        {
            pickup.gameObject.SetActive(false);
        }
    }

    private void SpawnStars(int usedSeed)
    {
        if (starPrefab == null)
        {
            Debug.LogWarning("MazeGenerator: no star prefab assigned, the maze has nothing to collect.", this);
            return;
        }

        Vector2Int aiCell = FarthestCell();

        // Prefer cells at least two steps from the start so no star is visible from the spawn point.
        List<Vector2Int> preferred = new List<Vector2Int>();
        List<Vector2Int> fallback = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (x == 0 && z == 0) continue;
                if (x == aiCell.x && z == aiCell.y) continue;
                if (_lockerCells.Contains(new Vector2Int(x, z))) continue;

                // On a bigger grid, two steps from the start is still in plain sight of it
                if (_distance[x, z] >= Mathf.Max(2, width / 2)) preferred.Add(new Vector2Int(x, z));
                else fallback.Add(new Vector2Int(x, z));
            }
        }

        System.Random rng = new System.Random(usedSeed + 1);
        Shuffle(preferred, rng);
        Shuffle(fallback, rng);
        preferred.AddRange(fallback);

        // Keep the stars apart, or a shuffle can drop two of them in neighbouring cells
        int minSeparation = Mathf.Max(2, (width + height) / 4);
        List<Vector2Int> chosen = new List<Vector2Int>();

        for (int pass = 0; pass < 2 && chosen.Count < starCount; pass++)
        {
            // Second pass drops the separation constraint rather than spawning fewer stars
            foreach (Vector2Int candidate in preferred)
            {
                if (chosen.Count >= starCount) break;
                if (chosen.Contains(candidate)) continue;

                if (pass == 0 && !IsFarEnough(candidate, chosen, minSeparation)) continue;
                chosen.Add(candidate);
            }
        }

        foreach (Vector2Int cell in chosen)
        {
            // One call with the final position: Pickup.Start captures it as the anchor of the bobbing motion.
            GameObject star = Instantiate(
                starPrefab,
                CellCenter(cell.x, cell.y) + Vector3.up * starHeight,
                Quaternion.identity,
                _mazeRoot);

            _stars.Add(star.transform);
            if (starLights) AttachStarLight(star);
        }

        _starCells.Clear();
        _starCells.AddRange(chosen);

        if (chosen.Count < starCount)
        {
            Debug.LogWarning($"MazeGenerator: only {chosen.Count} of {starCount} stars fit in the maze.", this);
        }

        PrecomputeAmbushCells();
    }

    /// <summary>
    /// F45: for every star, every cell that sits two open-passage steps away with a turn in between -
    /// i.e. around a corner from it, never a straight line down the same corridor. Costs nothing at
    /// runtime since it is computed once here, right after the stars that define it exist.
    /// </summary>
    private void PrecomputeAmbushCells()
    {
        _ambushCells.Clear();

        foreach (Vector2Int star in _starCells)
        {
            List<Vector3> cells = new List<Vector3>();

            // A dead end (one open wall, three closed) has only one way in - never camp the only door.
            if (WallCount(star.x, star.y) != 3)
            {
                for (int dir = 0; dir < 4; dir++)
                {
                    if (IsWallClosed(star.x, star.y, dir)) continue;
                    Vector2Int n = Neighbour(star, dir);
                    if (n.x < 0 || n.x >= width || n.y < 0 || n.y >= height) continue;

                    for (int dir2 = 0; dir2 < 4; dir2++)
                    {
                        // Same direction twice is a straight line through n, not a corner.
                        if (dir2 == dir) continue;
                        if (IsWallClosed(n.x, n.y, dir2)) continue;

                        Vector2Int a = Neighbour(n, dir2);
                        if (a.x < 0 || a.x >= width || a.y < 0 || a.y >= height) continue;
                        // The reverse direction leads straight back to the star cell itself.
                        if (a == star) continue;

                        Vector3 position = CellCenter(a.x, a.y);
                        if (!cells.Contains(position)) cells.Add(position);
                    }
                }
            }

            _ambushCells.Add(cells);
        }
    }

    /// <summary>
    /// Glowing shards hidden off the direct path (F31). Anti-farm rule (decision 4a): a retried floor
    /// spawns only ShardCount minus however many were already taken on this floor this campaign, so a
    /// retry after a partial sweep has fewer lying about instead of a free refill.
    /// </summary>
    private void SpawnShards(int usedSeed)
    {
        int wanted = _profile != null ? _profile.ShardCount : shardCount;
        wanted -= PlayerWallet.MazeShardsAlreadyTakenOnFloor(GameFlow.CurrentFloor);
        if (wanted <= 0) return;

        Material shardMaterial = BuildShardMaterial();
        AudioClip chime = BuildShardChimeClip();
        System.Random rng = new System.Random(usedSeed + 5);

        // F72: a vault forces up to 2 of the floor's shards inside it instead of adding to the total -
        // decision "shard count and caps unchanged, just relocated". Both shards land in the same cell,
        // so the second call excludes the first shard's wall.
        int forced = Mathf.Min(_vaultForcedShardCells.Count, wanted);
        HashSet<Vector3> excludeWalls = new HashSet<Vector3>();
        Vector2Int previousVaultCell = default;
        for (int i = 0; i < forced; i++)
        {
            Vector2Int vaultCell = _vaultForcedShardCells[i];
            if (i == 0 || vaultCell != previousVaultCell) excludeWalls.Clear();
            Vector3 usedWall = BuildShard(vaultCell, rng, shardMaterial, chime, excludeWalls);
            excludeWalls.Add(usedWall);
            previousVaultCell = vaultCell;
        }
        wanted -= forced;
        if (wanted <= 0) return;

        Vector2Int aiCell = FarthestCell();
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);
        HashSet<Vector2Int> starCellSet = new HashSet<Vector2Int>(_starCells);
        HashSet<Vector2Int> vaultCellSet = new HashSet<Vector2Int>(_vaultForcedShardCells);

        // Preferred: nooks (two or more real walls). Fallback: anything else off the direct path.
        List<Vector2Int> preferred = new List<Vector2Int>();
        List<Vector2Int> fallback = new List<Vector2Int>();

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (x == 0 && z == 0) continue;
                if (cell == aiCell) continue;
                if (starCellSet.Contains(cell)) continue;
                if (lockerCellSet.Contains(cell)) continue;
                if (vaultCellSet.Contains(cell)) continue;
                if (_distance[x, z] < 2) continue;

                bool farFromStars = true;
                foreach (Vector2Int starCell in _starCells)
                {
                    int manhattan = Mathf.Abs(cell.x - starCell.x) + Mathf.Abs(cell.y - starCell.y);
                    if (manhattan < 2) { farFromStars = false; break; }
                }
                if (!farFromStars) continue;

                if (WallCount(x, z) >= 2) preferred.Add(cell);
                else fallback.Add(cell);
            }
        }

        Shuffle(preferred, rng);
        Shuffle(fallback, rng);
        List<Vector2Int> pool = new List<Vector2Int>(preferred);
        pool.AddRange(fallback);

        int minSeparation = Mathf.Max(2, (width + height) / 5);
        List<Vector2Int> chosen = new List<Vector2Int>();

        for (int pass = 0; pass < 2 && chosen.Count < wanted; pass++)
        {
            foreach (Vector2Int candidate in pool)
            {
                if (chosen.Count >= wanted) break;
                if (chosen.Contains(candidate)) continue;
                if (pass == 0 && !IsFarEnough(candidate, chosen, minSeparation)) continue;
                chosen.Add(candidate);
            }
        }

        if (chosen.Count == 0) return;

        foreach (Vector2Int cell in chosen)
        {
            BuildShard(cell, rng, shardMaterial, chime, null);
        }
    }

    private Material BuildShardMaterial()
    {
        Material source = FindStarMaterial();
        if (source == null) return null;

        Material material = new Material(source) { name = "Maze_Shard" };
        material.EnableKeyword("_EMISSION");
        Color baseColor = new Color(0.55f, 0.9f, 1f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        material.SetColor("_EmissionColor", baseColor * 2.5f);

        _runtimeMaterials.Add(material);
        return material;
    }

    /// <summary>
    /// A shard root sits unscaled at the wall (so its SphereCollider radius means what it says); the
    /// glassy cube it is built from is a scaled child instead of the root itself. Returns the wall
    /// direction it used, so a vault's second forced shard (F72) can avoid landing on the same wall as
    /// the first. excludeWalls may be null (the normal, non-forced path).
    /// </summary>
    private Vector3 BuildShard(Vector2Int cell, System.Random rng, Material material, AudioClip chime, HashSet<Vector3> excludeWalls)
    {
        int x = cell.x;
        int z = cell.y;
        Vector3 cellCenter = CellCenter(x, z);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;

        List<Vector3> options = new List<Vector3>();
        if (_wallN[x, z]) options.Add(Vector3.forward);
        if (_wallE[x, z]) options.Add(Vector3.right);
        if (_wallS[x, z]) options.Add(Vector3.back);
        if (_wallW[x, z]) options.Add(Vector3.left);
        if (options.Count == 0) return Vector3.zero; // every real cell has at least one wall

        // F72: a vault's second forced shard skips whichever wall the first one already took, so the
        // two don't overlap in the same tiny dead-end cell. Falls back to the full list if that would
        // empty it (a one-walled cell forced to hold two shards).
        if (excludeWalls != null && excludeWalls.Count > 0)
        {
            List<Vector3> notExcluded = new List<Vector3>(options.Count);
            foreach (Vector3 dir in options)
            {
                if (!excludeWalls.Contains(dir)) notExcluded.Add(dir);
            }
            if (notExcluded.Count > 0) options = notExcluded;
        }

        // Avoid a wall already dressed with a prop (a shard clipping a prop is ugly); if that would
        // empty the list, a shard on the same wall as a prop is harmless, so fall back to the full list.
        if (_propWalls.TryGetValue(cell, out List<Vector3> takenWalls) && takenWalls.Count > 0)
        {
            List<Vector3> avoiding = new List<Vector3>(options.Count);
            foreach (Vector3 dir in options)
            {
                if (!takenWalls.Contains(dir)) avoiding.Add(dir);
            }
            if (avoiding.Count > 0) options = avoiding;
        }

        Vector3 wallDirection = options[rng.Next(options.Count)];

        Vector3 position = cellCenter + wallDirection * (backFaceDistance * 0.55f) + Vector3.up * 0.85f;

        GameObject root = new GameObject($"Shard_{x}_{z}");
        root.transform.SetParent(_mazeRoot, false);
        root.transform.position = position;

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.layer = 0;
        body.transform.SetParent(root.transform, false);
        body.transform.localRotation = Quaternion.Euler(35f, rng.Next(360), 25f);
        body.transform.localScale = new Vector3(0.16f, 0.5f, 0.16f);

        Collider bodyCollider = body.GetComponent<Collider>();
        if (bodyCollider != null) Destroy(bodyCollider);
        if (material != null) body.GetComponent<MeshRenderer>().sharedMaterial = material;

        SphereCollider trigger = root.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 0.7f;

        GameObject lightHolder = new GameObject("Light");
        lightHolder.transform.SetParent(root.transform, false);

        Light light = lightHolder.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.6f, 0.9f, 1f);
        light.range = 2.5f;
        light.intensity = 0.7f;
        light.shadows = LightShadows.None;

        ShardPickup shard = root.AddComponent<ShardPickup>();
        shard.Configure(chime, (float)rng.NextDouble() * 10f);

        return wallDirection;
    }

    /// <summary>Two-partial glass chime, same construction technique as MazeEscape.BuildPingClip.</summary>
    private static AudioClip BuildShardChimeClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.22f;
        const float decay = 0.07f;

        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Exp(-t / decay);
            envelope *= Mathf.Clamp01(t / 0.003f); // 3 ms fade-in kills the attack click

            float value = 0.6f * Mathf.Sin(2f * Mathf.PI * 2100f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 3150f * t);
            samples[i] = value * envelope;
        }

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f)
        {
            float gain = 0.8f / peak;
            for (int i = 0; i < sampleCount; i++) samples[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("ShardChime", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private bool IsFarEnough(Vector2Int candidate, List<Vector2Int> chosen, int minSeparation)
    {
        foreach (Vector2Int other in chosen)
        {
            int manhattan = Mathf.Abs(candidate.x - other.x) + Mathf.Abs(candidate.y - other.y);
            if (manhattan < minSeparation) return false;
        }

        return true;
    }

    // ---------------------------------------------------------------- doors, buttons & vaults (F72)

    /// <summary>One open passage between two adjacent cells, as ReachableCells/CreateDoor key off it.</summary>
    private struct Passage
    {
        public Vector2Int CellA;
        public int Dir;
        public Vector2Int CellB;
    }

    /// <summary>
    /// Shutter doors, their buttons, and vault rooms. Post-bake (a door needs no NavMeshObstacle - see
    /// MazeDoor) and run between SpawnStars and SpawnShards: security placement tests whether blocking
    /// a passage actually cuts a star cell off (needs _starCells), and a vault's forced shards must be
    /// known before SpawnShards decides where the rest of the floor's shards go.
    /// </summary>
    private void BuildDoors(System.Random rng)
    {
        _vaultForcedShardCells.Clear();
        _doors.Clear();
        _buttons.Clear();
        _batteryPickups.Clear();

        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasDoorsAndButtons)
        {
            Debug.LogError("MazeGenerator: no InteractableKit (doors/buttons/battery cell) in the scene - skipping doors, buttons and vaults. Run LIGHTS OUT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        if (aiFollower == null) aiFollower = FindAnyObjectByType<AIFollower>();
        Transform player = FindPlayer();

        int wantSecurity = _profile != null ? _profile.SecurityDoors : 0;
        int wantManual = _profile != null ? _profile.ManualDoors : 1;
        int wantVaults = _profile != null ? _profile.Vaults : 0;
        bool wantTimed = _profile != null && _profile.HasTimedDoor;

        // Kept in a field (not a local), so BuildFuses (+12, after this) can avoid every cell a
        // door/button/vault already used - see the field doc comment.
        _doorUsedCells.Clear();
        HashSet<Vector2Int> usedCells = _doorUsedCells;
        List<Passage> placedDoorPassages = new List<Passage>();

        List<(MazeDoor door, Vector2Int startCell)> securityDoors =
            PlaceSecurityDoors(rng, wantSecurity, player, placedDoorPassages, usedCells);

        if (wantTimed && securityDoors.Count > 0)
        {
            MazeDoor farthest = null;
            int best = -1;
            foreach ((MazeDoor door, Vector2Int startCell) entry in securityDoors)
            {
                int dist = _distance[entry.startCell.x, entry.startCell.y];
                if (dist <= best) continue;
                best = dist;
                farthest = entry.door;
            }
            // A sealed star room is a LinkedDoor chain: convert every shutter in it, so the button's
            // Unlock starts the same 25 s timer on each and they all roll down together.
            for (MazeDoor d = farthest; d != null; d = d.LinkedDoor) d.ConvertToTimed();
        }

        // Decision D15: vault loot (the Second Wind roll) gets its own +15 stream, separate from +11's
        // door/button placement - so changing how many security doors a floor has never reshuffles
        // whether a vault happens to grant a Second Wind.
        System.Random lootRng = new System.Random(UsedSeed + 15);
        PlaceVaults(rng, lootRng, wantVaults, player, placedDoorPassages, usedCells);
        PlaceManualDoors(rng, wantManual, player, placedDoorPassages, usedCells);
    }

    /// <summary>Every open interior passage, each appearing exactly once (dir 0/North and 1/East from every cell covers every edge - the mirror image from the other cell's dir 2/South or 3/West would just be the same edge again).</summary>
    private List<Passage> AllOpenPassages()
    {
        List<Passage> list = new List<Passage>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (z + 1 < height && !_wallN[x, z]) list.Add(new Passage { CellA = new Vector2Int(x, z), Dir = 0, CellB = new Vector2Int(x, z + 1) });
                if (x + 1 < width && !_wallE[x, z]) list.Add(new Passage { CellA = new Vector2Int(x, z), Dir = 1, CellB = new Vector2Int(x + 1, z) });
            }
        }
        return list;
    }

    private static bool IsSamePassage(Vector2Int cell, int dir, Passage p)
    {
        Vector2Int neighbour = Neighbour(cell, dir);
        return (cell == p.CellA && neighbour == p.CellB) || (cell == p.CellB && neighbour == p.CellA);
    }

    /// <summary>BFS over open passages from `from`, with `blocked` (if given) treated as closed regardless of what it actually is - the security-door test: does blocking this one passage actually cut the maze in two.</summary>
    private HashSet<Vector2Int> ReachableCells(Vector2Int from, Passage? blocked)
    {
        HashSet<Vector2Int> visited = new HashSet<Vector2Int> { from };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(cell.x, cell.y, dir)) continue;
                Vector2Int n = Neighbour(cell, dir);
                if (n.x < 0 || n.x >= width || n.y < 0 || n.y >= height) continue;
                if (blocked.HasValue && IsSamePassage(cell, dir, blocked.Value)) continue;
                if (!visited.Add(n)) continue;
                queue.Enqueue(n);
            }
        }
        return visited;
    }

    /// <summary>Step distances from `from`, expanding only within `region` - used to keep a button at least 3 BFS steps from its door without wandering onto the locked side.</summary>
    private Dictionary<Vector2Int, int> BfsWithin(Vector2Int from, HashSet<Vector2Int> region)
    {
        Dictionary<Vector2Int, int> distance = new Dictionary<Vector2Int, int> { [from] = 0 };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            int next = distance[cell] + 1;
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(cell.x, cell.y, dir)) continue;
                Vector2Int n = Neighbour(cell, dir);
                if (n.x < 0 || n.x >= width || n.y < 0 || n.y >= height) continue;
                if (!region.Contains(n) || distance.ContainsKey(n)) continue;
                distance[n] = next;
                queue.Enqueue(n);
            }
        }
        return distance;
    }

    private static Vector3 DirVector(int dir)
    {
        switch (dir)
        {
            case 0: return Vector3.forward;
            case 1: return Vector3.right;
            case 2: return Vector3.back;
            default: return Vector3.left;
        }
    }

    /// <summary>World-space centre and facing for the shared wall line between a cell and its dir neighbour - the same line CreateBox would use for a real wall there.</summary>
    private (Vector3 center, Quaternion rotation) WallLine(Vector2Int cell, int dir)
    {
        switch (dir)
        {
            case 0: return (new Vector3(origin.x + (cell.x + 0.5f) * cellSize, FloorTop, origin.z + (cell.y + 1) * cellSize), Quaternion.identity);
            case 1: return (new Vector3(origin.x + (cell.x + 1) * cellSize, FloorTop, origin.z + (cell.y + 0.5f) * cellSize), Quaternion.Euler(0f, 90f, 0f));
            case 2: return (new Vector3(origin.x + (cell.x + 0.5f) * cellSize, FloorTop, origin.z + cell.y * cellSize), Quaternion.identity);
            default: return (new Vector3(origin.x + cell.x * cellSize, FloorTop, origin.z + (cell.y + 0.5f) * cellSize), Quaternion.Euler(0f, 90f, 0f));
        }
    }

    /// <summary>Approximate "is a wall lamp already here" test for button placement - lamps aren't indexed by cell/direction, so this just checks proximity to the candidate wall position.</summary>
    private bool IsLampWall(Vector2Int cell, Vector3 dirVec)
    {
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        Vector3 wallPos = CellCenter(cell.x, cell.y) + dirVec * backFaceDistance;
        float threshold = cellSize * 0.4f;

        foreach (WallLamp lamp in _lamps)
        {
            if (lamp != null && (lamp.transform.position - wallPos).sqrMagnitude < threshold * threshold) return true;
        }
        return false;
    }

    /// <summary>
    /// Security-door candidates are bridge passages (the maze is braided - RemoveDeadEnds adds loops -
    /// so most passages are not bridges) whose removal strands at least one star on the start's side.
    /// Prefers a cut-off region that is 3-40% of the maze; falls back to any qualifying candidate if too
    /// few of those exist. Each door gets a button in the start-side component (decision D6/F72 slice).
    /// </summary>
    private List<(MazeDoor door, Vector2Int startCell)> PlaceSecurityDoors(System.Random rng, int want, Transform player,
        List<Passage> placedDoorPassages, HashSet<Vector2Int> usedCells)
    {
        List<(MazeDoor door, Vector2Int startCell)> result = new List<(MazeDoor, Vector2Int)>();
        if (want <= 0) return result;

        Vector2Int start = Vector2Int.zero;
        int totalCells = width * height;

        List<(Passage passage, HashSet<Vector2Int> startSide, float cutFraction)> candidates =
            new List<(Passage, HashSet<Vector2Int>, float)>();

        foreach (Passage p in AllOpenPassages())
        {
            if (p.CellA == start || p.CellB == start) continue;

            HashSet<Vector2Int> reachable = ReachableCells(start, p);
            bool losesStar = false;
            foreach (Vector2Int star in _starCells)
            {
                if (!reachable.Contains(star)) { losesStar = true; break; }
            }
            if (!losesStar) continue;

            float cutFraction = 1f - reachable.Count / (float)totalCells;
            candidates.Add((p, reachable, cutFraction));
        }

        List<(Passage, HashSet<Vector2Int>, float)> preferred = candidates.FindAll(c => c.Item3 >= 0.03f && c.Item3 <= 0.40f);
        List<(Passage, HashSet<Vector2Int>, float)> pool = preferred.Count >= want ? preferred : candidates;

        List<int> order = new List<int>(pool.Count);
        for (int i = 0; i < pool.Count; i++) order.Add(i);
        ShuffleInts(order, rng);

        int minSeparation = Mathf.Max(2, (width + height) / 6);
        foreach (int idx in order)
        {
            if (result.Count >= want) break;

            (Passage passage, HashSet<Vector2Int> startSide, float cutFraction) candidate = pool[idx];
            if (usedCells.Contains(candidate.passage.CellA) || usedCells.Contains(candidate.passage.CellB)) continue;
            if (TooCloseToPlacedDoors(candidate.passage.CellA, placedDoorPassages, minSeparation)) continue;

            Vector2Int startCell = candidate.startSide.Contains(candidate.passage.CellA) ? candidate.passage.CellA : candidate.passage.CellB;
            Vector2Int insideCell = startCell == candidate.passage.CellA ? candidate.passage.CellB : candidate.passage.CellA;

            MazeDoor door = CreateDoor(MazeDoor.Kind.Security, candidate.passage, startCell, insideCell, player);
            WallButton button = PlaceButtonFor(door, candidate.startSide, startCell, rng, usedCells);
            if (button == null)
            {
                Debug.LogWarning($"MazeGenerator: security door at {candidate.passage.CellA}/{candidate.passage.CellB} has no room for a button; skipping it.", this);
                Destroy(door.gameObject);
                continue;
            }

            placedDoorPassages.Add(candidate.passage);
            usedCells.Add(candidate.passage.CellA);
            usedCells.Add(candidate.passage.CellB);
            result.Add((door, startCell));
        }

        // RemoveDeadEnds braids the maze, so a single-passage cut rarely strands a star and the loop above
        // often places nothing. Fallback: seal the star's own cell - a shutter on every opening, chained
        // through LinkedDoor to one button - provided the rest of the maze stays connected without it.
        if (result.Count < want)
        {
            List<Vector2Int> starCells = new List<Vector2Int>(_starCells);
            Shuffle(starCells, rng);

            foreach (Vector2Int starCell in starCells)
            {
                if (result.Count >= want) break;
                if (Mathf.Max(Mathf.Abs(starCell.x), Mathf.Abs(starCell.y)) <= 1) continue;
                if (usedCells.Contains(starCell)) continue;
                if (TooCloseToPlacedDoors(starCell, placedDoorPassages, minSeparation)) continue;

                List<int> openDirs = new List<int>(4);
                bool neighbourTaken = false;
                for (int dir = 0; dir < 4; dir++)
                {
                    if (IsWallClosed(starCell.x, starCell.y, dir)) continue;
                    openDirs.Add(dir);
                    if (usedCells.Contains(Neighbour(starCell, dir))) neighbourTaken = true;
                }
                if (openDirs.Count == 0 || neighbourTaken) continue;

                HashSet<Vector2Int> startSide = ReachableCellsAvoiding(start, starCell);
                if (startSide.Count != totalCells - 1) continue; // sealing it would cut the maze

                List<MazeDoor> doors = new List<MazeDoor>(openDirs.Count);
                List<Passage> passages = new List<Passage>(openDirs.Count);
                foreach (int dir in openDirs)
                {
                    Vector2Int outside = Neighbour(starCell, dir);
                    Passage p = new Passage { CellA = starCell, Dir = dir, CellB = outside };
                    MazeDoor door = CreateDoor(MazeDoor.Kind.Security, p, outside, starCell, player);
                    if (doors.Count > 0) doors[doors.Count - 1].LinkedDoor = door;
                    doors.Add(door);
                    passages.Add(p);
                }

                Vector2Int firstOutside = Neighbour(starCell, openDirs[0]);
                WallButton button = PlaceButtonFor(doors[0], startSide, firstOutside, rng, usedCells);
                if (button == null)
                {
                    foreach (MazeDoor door in doors) Destroy(door.gameObject);
                    continue;
                }

                placedDoorPassages.AddRange(passages);
                usedCells.Add(starCell);
                foreach (int dir in openDirs) usedCells.Add(Neighbour(starCell, dir));
                result.Add((doors[0], firstOutside));
            }
        }

        if (result.Count < want)
        {
            Debug.LogWarning($"MazeGenerator: only {result.Count} of {want} security doors fit in this maze.", this);
        }
        return result;
    }

    /// <summary>A dead-end cell (WallCount == 3, one open side) turned into a locked room with a battery cell, a shot at a Second Wind, and 2 of the floor's own shards moved inside.</summary>
    private void PlaceVaults(System.Random rng, System.Random lootRng, int want, Transform player, List<Passage> placedDoorPassages, HashSet<Vector2Int> usedCells)
    {
        if (want <= 0) return;

        Vector2Int start = Vector2Int.zero;
        HashSet<Vector2Int> starCellSet = new HashSet<Vector2Int>(_starCells);
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);

        List<Vector2Int> deadEnds = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (cell == start || WallCount(x, z) != 3) continue;
                if (starCellSet.Contains(cell) || lockerCellSet.Contains(cell) || _setPieceCells.Contains(cell)) continue;
                if (usedCells.Contains(cell)) continue;
                deadEnds.Add(cell);
            }
        }
        Shuffle(deadEnds, rng);

        int placed = 0;
        foreach (Vector2Int vaultCell in deadEnds)
        {
            if (placed >= want) break;

            int openDir = -1;
            for (int dir = 0; dir < 4; dir++)
            {
                if (!IsWallClosed(vaultCell.x, vaultCell.y, dir)) { openDir = dir; break; }
            }
            if (openDir < 0) continue;

            Vector2Int outside = Neighbour(vaultCell, openDir);
            Passage p = new Passage { CellA = vaultCell, Dir = openDir, CellB = outside };

            HashSet<Vector2Int> reachableFromStart = ReachableCells(start, p);
            if (reachableFromStart.Contains(vaultCell)) continue; // defensive - a true dead end never loops back

            MazeDoor door = CreateDoor(MazeDoor.Kind.Security, p, outside, vaultCell, player);
            door.IsVaultDoor = true; // F75 Fuse Map beacon
            WallButton button = PlaceButtonFor(door, reachableFromStart, outside, rng, usedCells);
            if (button == null)
            {
                Debug.LogWarning($"MazeGenerator: vault at {vaultCell} has no room for a button; skipping it.", this);
                Destroy(door.gameObject);
                continue;
            }

            placedDoorPassages.Add(p);
            usedCells.Add(vaultCell);
            usedCells.Add(outside);
            BuildVaultLoot(vaultCell, lootRng);
            placed++;
        }

        // RemoveDeadEnds braids the maze, so normally there are no dead ends at all. Fall back to a
        // two-opening cell whose removal leaves every other cell reachable from the start: a shutter
        // on each opening, one button, the first door's Unlock chaining to the second.
        if (placed < want)
        {
            List<Vector2Int> corridors = new List<Vector2Int>();
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    Vector2Int cell = new Vector2Int(x, z);
                    if (cell == start || WallCount(x, z) != 2) continue;
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) <= 1) continue; // not beside the start
                    if (starCellSet.Contains(cell) || lockerCellSet.Contains(cell) || _setPieceCells.Contains(cell)) continue;
                    if (usedCells.Contains(cell)) continue;
                    corridors.Add(cell);
                }
            }
            Shuffle(corridors, rng);

            foreach (Vector2Int vaultCell in corridors)
            {
                if (placed >= want) break;

                List<int> openDirs = new List<int>(2);
                for (int dir = 0; dir < 4; dir++)
                {
                    if (!IsWallClosed(vaultCell.x, vaultCell.y, dir)) openDirs.Add(dir);
                }
                if (openDirs.Count != 2) continue;

                Vector2Int outsideA = Neighbour(vaultCell, openDirs[0]);
                Vector2Int outsideB = Neighbour(vaultCell, openDirs[1]);
                if (usedCells.Contains(outsideA) || usedCells.Contains(outsideB)) continue;

                HashSet<Vector2Int> startSide = ReachableCellsAvoiding(start, vaultCell);
                if (startSide.Count != width * height - 1) continue; // sealing it would cut the maze

                Passage pA = new Passage { CellA = vaultCell, Dir = openDirs[0], CellB = outsideA };
                Passage pB = new Passage { CellA = vaultCell, Dir = openDirs[1], CellB = outsideB };

                MazeDoor doorA = CreateDoor(MazeDoor.Kind.Security, pA, outsideA, vaultCell, player);
                MazeDoor doorB = CreateDoor(MazeDoor.Kind.Security, pB, outsideB, vaultCell, player);
                doorA.LinkedDoor = doorB;
                doorA.IsVaultDoor = true; // F75 Fuse Map beacon
                doorB.IsVaultDoor = true;

                WallButton button = PlaceButtonFor(doorA, startSide, outsideA, rng, usedCells);
                if (button == null)
                {
                    Destroy(doorA.gameObject);
                    Destroy(doorB.gameObject);
                    continue;
                }

                placedDoorPassages.Add(pA);
                placedDoorPassages.Add(pB);
                usedCells.Add(vaultCell);
                usedCells.Add(outsideA);
                usedCells.Add(outsideB);
                BuildVaultLoot(vaultCell, lootRng);
                placed++;
            }
        }

        if (placed < want)
        {
            Debug.LogWarning($"MazeGenerator: only {placed} of {want} vaults fit in this maze.", this);
        }
    }

    /// <summary>Cells reachable from `from` through open passages without ever entering `avoid`.</summary>
    private HashSet<Vector2Int> ReachableCellsAvoiding(Vector2Int from, Vector2Int avoid)
    {
        HashSet<Vector2Int> visited = new HashSet<Vector2Int> { from };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(cell.x, cell.y, dir)) continue;
                Vector2Int n = Neighbour(cell, dir);
                if (n == avoid || n.x < 0 || n.x >= width || n.y < 0 || n.y >= height) continue;
                if (!visited.Add(n)) continue;
                queue.Enqueue(n);
            }
        }
        return visited;
    }

    /// <summary>Any open passage, away from the start and from a star's own doorway, spaced out from every other door - a free hiding/escape tool with nothing to gate (decision D6).</summary>
    private void PlaceManualDoors(System.Random rng, int want, Transform player, List<Passage> placedDoorPassages, HashSet<Vector2Int> usedCells)
    {
        if (want <= 0) return;

        Vector2Int start = Vector2Int.zero;
        HashSet<Vector2Int> starCellSet = new HashSet<Vector2Int>(_starCells);

        List<Passage> candidates = new List<Passage>();
        foreach (Passage p in AllOpenPassages())
        {
            if (p.CellA == start || p.CellB == start) continue;
            if (starCellSet.Contains(p.CellA) || starCellSet.Contains(p.CellB)) continue;
            candidates.Add(p);
        }

        List<int> order = new List<int>(candidates.Count);
        for (int i = 0; i < candidates.Count; i++) order.Add(i);
        ShuffleInts(order, rng);

        int minSeparation = Mathf.Max(2, (width + height) / 6);
        int placed = 0;
        foreach (int idx in order)
        {
            if (placed >= want) break;

            Passage p = candidates[idx];
            if (TooCloseToPlacedDoors(p.CellA, placedDoorPassages, minSeparation)) continue;

            CreateDoor(MazeDoor.Kind.Manual, p, p.CellA, p.CellB, player);
            placedDoorPassages.Add(p);
            placed++;
        }

        if (placed < want)
        {
            Debug.LogWarning($"MazeGenerator: only {placed} of {want} manual doors fit in this maze.", this);
        }
    }

    private bool TooCloseToPlacedDoors(Vector2Int cell, List<Passage> placedDoorPassages, int minSeparation)
    {
        foreach (Passage other in placedDoorPassages)
        {
            int manhattan = Mathf.Abs(cell.x - other.CellA.x) + Mathf.Abs(cell.y - other.CellA.y);
            if (manhattan < minSeparation) return true;
        }
        return false;
    }

    /// <summary>Clones InteractableKit.shutterDoor into the opening between p.CellA and p.CellB, sized to the corridor, and fills the lintel above it with a plain wall-coloured box.</summary>
    private MazeDoor CreateDoor(MazeDoor.Kind kind, Passage p, Vector2Int startCell, Vector2Int insideCell, Transform player)
    {
        (Vector3 center, Quaternion rotation) = WallLine(p.CellA, p.Dir);
        float doorHeight = Mathf.Min(wallHeight, 3.0f);
        float doorWidth = cellSize - wallThickness;

        MazeDoor door = Instantiate(interactableKit.ShutterDoor, center, rotation, _mazeRoot);
        door.gameObject.SetActive(true);
        door.name = $"Door_{p.CellA.x}_{p.CellA.y}_{p.Dir}_{kind}";

        Vector3 insideWorld = CellCenter(insideCell.x, insideCell.y);
        Vector3 insideDirection = insideWorld - center;
        insideDirection.y = 0f;

        door.Configure(kind, doorWidth, doorHeight, center, insideDirection, aiFollower, player);

        float lintelHeight = wallHeight - doorHeight;
        if (lintelHeight > 0.05f)
        {
            Material lintelMat = _theme != null && _theme.WallMaterial != null ? _theme.WallMaterial : _wallMat;
            bool alongX = p.Dir == 0 || p.Dir == 2; // N/S passage: the wall (and this lintel) spans X, thin along Z
            Vector3 size = alongX
                ? new Vector3(doorWidth + wallThickness, lintelHeight, wallThickness)
                : new Vector3(wallThickness, lintelHeight, doorWidth + wallThickness);
            Vector3 lintelCenter = center + Vector3.up * (doorHeight + lintelHeight * 0.5f);
            CreateBox($"Lintel_{door.name}", lintelCenter, size, lintelMat, _mazeRoot);
        }

        _doors.Add(door);
        return door;
    }

    /// <summary>A button somewhere in `startSide`, at least 3 BFS steps from the door, on a free closed wall - registered in _propWalls so nothing else dresses over it.</summary>
    private WallButton PlaceButtonFor(MazeDoor door, HashSet<Vector2Int> startSide, Vector2Int doorStartCell, System.Random rng, HashSet<Vector2Int> usedCells)
    {
        HashSet<Vector2Int> starCellSet = new HashSet<Vector2Int>(_starCells);
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);
        Dictionary<Vector2Int, int> distances = BfsWithin(doorStartCell, startSide);

        List<(Vector2Int cell, int dir)> options = new List<(Vector2Int, int)>();
        foreach (KeyValuePair<Vector2Int, int> kv in distances)
        {
            Vector2Int cell = kv.Key;
            if (kv.Value < 3) continue;
            if (starCellSet.Contains(cell) || lockerCellSet.Contains(cell) || _setPieceCells.Contains(cell)) continue;
            if (usedCells.Contains(cell)) continue;

            for (int dir = 0; dir < 4; dir++)
            {
                if (!IsWallClosed(cell.x, cell.y, dir)) continue;
                Vector3 dirVec = DirVector(dir);
                if (_propWalls.TryGetValue(cell, out List<Vector3> taken) && taken.Contains(dirVec)) continue;
                if (IsLampWall(cell, dirVec)) continue;
                options.Add((cell, dir));
            }
        }
        if (options.Count == 0) return null;

        (Vector2Int cell, int dir) chosen = options[rng.Next(options.Count)];
        WallButton button = CreateButtonInstance(chosen.cell, chosen.dir, door);

        Vector3 chosenDir = DirVector(chosen.dir);
        if (!_propWalls.TryGetValue(chosen.cell, out List<Vector3> takenList))
        {
            takenList = new List<Vector3>();
            _propWalls[chosen.cell] = takenList;
        }
        takenList.Add(chosenDir);
        usedCells.Add(chosen.cell);

        return button;
    }

    private WallButton CreateButtonInstance(Vector2Int cell, int dir, MazeDoor door)
    {
        Vector3 dirVec = DirVector(dir);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        // Hand height: the template's geometry is centred on its root, and CellCenter is floor level
        // (buttons used to spawn with their plate half in the floor - fixed 4 Oct 2026).
        Vector3 pos = CellCenter(cell.x, cell.y) + dirVec * backFaceDistance + Vector3.up * 1.2f;

        WallButton button = Instantiate(interactableKit.WallButton, pos, Quaternion.LookRotation(-dirVec, Vector3.up), _mazeRoot);
        button.gameObject.SetActive(true);
        button.name = $"Button_{cell.x}_{cell.y}";
        button.Configure(door, aiFollower);

        _buttons.Add(button);
        return button;
    }

    /// <summary>A battery cell (plus a seeded shot at a Second Wind) inside the vault; the 2 forced shards themselves are placed later by SpawnShards, from _vaultForcedShardCells.</summary>
    private void BuildVaultLoot(Vector2Int vaultCell, System.Random rng)
    {
        if (interactableKit.BatteryCell != null)
        {
            Vector3 pos = CellCenter(vaultCell.x, vaultCell.y) + Vector3.up * 0.4f;
            BatteryCellPickup pickup = Instantiate(interactableKit.BatteryCell, pos, Quaternion.identity, _mazeRoot);
            pickup.gameObject.SetActive(true);
            pickup.name = $"BatteryCell_{vaultCell.x}_{vaultCell.y}";

            bool grantsSecondWind = rng.NextDouble() < 0.2 && PlayerInventory.Count(ShopItem.SecondWind) == 0;
            pickup.SetGrantsSecondWind(grantsSecondWind);
            _batteryPickups.Add(pickup);
        }

        _vaultForcedShardCells.Add(vaultCell);
        _vaultForcedShardCells.Add(vaultCell);
    }

    private static void ShuffleInts(List<int> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ---------------------------------------------------------------- fuses (F73)

    /// <summary>
    /// A Blackout floor's fuse boxes: FuseCount cells, spread out, each owning a sector of the maze
    /// found by a multi-source BFS from every fuse cell at once (every cell/lamp belongs to whichever
    /// fuse reached it in the fewest steps). Post-bake, after BuildDoors - a fuse box needs a free wall
    /// the same way a button does, so it has to see _propWalls/lamp walls and every door/button/vault
    /// cell already claimed (_doorUsedCells). On a non-Blackout floor (FuseCount 0) this is a no-op.
    /// </summary>
    private void BuildFuses(System.Random rng)
    {
        _fuseBoxes.Clear();
        if (_profile == null || _profile.FuseCount <= 0) return;

        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasFuseBox)
        {
            Debug.LogError("MazeGenerator: no InteractableKit fuse box in the scene - skipping this Blackout floor's fuses. Run LIGHTS OUT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        if (aiFollower == null) aiFollower = FindAnyObjectByType<AIFollower>();

        Vector2Int start = Vector2Int.zero;
        HashSet<Vector2Int> starCellSet = new HashSet<Vector2Int>(_starCells);

        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (cell == start || starCellSet.Contains(cell) || _lockerCells.Contains(cell)) continue;
                if (_setPieceCells.Contains(cell) || _doorUsedCells.Contains(cell)) continue;
                if (HasFreeWall(cell)) candidates.Add(cell);
            }
        }
        Shuffle(candidates, rng);

        List<Vector2Int> chosenCells = new List<Vector2Int>();
        int minSeparation = Mathf.Max(3, (width + height) / 5);
        while (minSeparation >= 1 && chosenCells.Count < _profile.FuseCount)
        {
            foreach (Vector2Int cell in candidates)
            {
                if (chosenCells.Count >= _profile.FuseCount) break;
                if (chosenCells.Contains(cell) || !IsFarEnough(cell, chosenCells, minSeparation)) continue;
                chosenCells.Add(cell);
            }
            minSeparation--;
        }

        if (chosenCells.Count < _profile.FuseCount)
        {
            Debug.LogWarning($"MazeGenerator: only {chosenCells.Count} of {_profile.FuseCount} fuse boxes fit in this maze.", this);
        }

        // Multi-source BFS: every cell/lamp belongs to whichever fuse cell reached it in the fewest
        // steps, with door passages counting as open exactly like every other open passage (the wall
        // arrays a MazeDoor sits on were never touched - see MazeDoor's own doc comment).
        Dictionary<Vector2Int, int> sectorOf = new Dictionary<Vector2Int, int>();
        Dictionary<Vector2Int, int> stepOf = new Dictionary<Vector2Int, int>();
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        for (int i = 0; i < chosenCells.Count; i++)
        {
            sectorOf[chosenCells[i]] = i;
            stepOf[chosenCells[i]] = 0;
            queue.Enqueue(chosenCells[i]);
        }
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            int nextStep = stepOf[cell] + 1;
            int sector = sectorOf[cell];
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(cell.x, cell.y, dir)) continue;
                Vector2Int n = Neighbour(cell, dir);
                if (n.x < 0 || n.x >= width || n.y < 0 || n.y >= height) continue;
                if (sectorOf.ContainsKey(n)) continue;
                sectorOf[n] = sector;
                stepOf[n] = nextStep;
                queue.Enqueue(n);
            }
        }

        if (_profile.Has(FloorProfile.Objective.Blackout))
        {
            for (int i = 0; i < _lamps.Count; i++)
            {
                _lamps[i]?.SetPowered(false);
            }

            BuildEmergencyLight(start);
        }

        for (int i = 0; i < chosenCells.Count; i++)
        {
            BuildFuseBox(chosenCells[i], i, sectorOf, stepOf, rng);
        }
    }

    /// <summary>True if `cell` has at least one closed wall not already claimed by a wall prop, a button, or a lamp - the same free-wall test PlaceButtonFor uses.</summary>
    private bool HasFreeWall(Vector2Int cell)
    {
        for (int dir = 0; dir < 4; dir++)
        {
            if (!IsWallClosed(cell.x, cell.y, dir)) continue;
            Vector3 dirVec = DirVector(dir);
            if (_propWalls.TryGetValue(cell, out List<Vector3> taken) && taken.Contains(dirVec)) continue;
            if (IsLampWall(cell, dirVec)) continue;
            return true;
        }
        return false;
    }

    /// <summary>Picks (and does not yet reserve) one of `cell`'s free walls, seeded so a same-seed retry mounts the fuse box on the same wall.</summary>
    private bool TryChooseFreeWall(Vector2Int cell, System.Random rng, out int dir)
    {
        List<int> options = new List<int>(4);
        for (int d = 0; d < 4; d++)
        {
            if (!IsWallClosed(cell.x, cell.y, d)) continue;
            Vector3 dirVec = DirVector(d);
            if (_propWalls.TryGetValue(cell, out List<Vector3> taken) && taken.Contains(dirVec)) continue;
            if (IsLampWall(cell, dirVec)) continue;
            options.Add(d);
        }

        if (options.Count == 0)
        {
            dir = -1;
            return false;
        }

        dir = options[rng.Next(options.Count)];
        return true;
    }

    /// <summary>Clones InteractableKit.fuseBox onto a free wall of `cell`, and hands it every lamp in its sector plus each one's BFS step distance (the ripple delay).</summary>
    private void BuildFuseBox(Vector2Int cell, int sectorIndex, Dictionary<Vector2Int, int> sectorOf, Dictionary<Vector2Int, int> stepOf, System.Random rng)
    {
        if (!TryChooseFreeWall(cell, rng, out int dir))
        {
            Debug.LogWarning($"MazeGenerator: fuse box at {cell} has no free wall; skipping it.", this);
            return;
        }

        Vector3 dirVec = DirVector(dir);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        // Chest height, same reasoning as CreateButtonInstance (the cabinet used to sit half in the floor).
        Vector3 pos = CellCenter(cell.x, cell.y) + dirVec * backFaceDistance + Vector3.up * 1.25f;

        FuseBox box = Instantiate(interactableKit.FuseBox, pos, Quaternion.LookRotation(-dirVec, Vector3.up), _mazeRoot);
        box.gameObject.SetActive(true);
        box.name = $"FuseBox_{cell.x}_{cell.y}";

        List<WallLamp> sectorLamps = new List<WallLamp>();
        List<int> sectorSteps = new List<int>();
        for (int i = 0; i < _lamps.Count; i++)
        {
            Vector2Int lampCell = _lampCells[i];
            if (!sectorOf.TryGetValue(lampCell, out int owner) || owner != sectorIndex) continue;

            sectorLamps.Add(_lamps[i]);
            sectorSteps.Add(stepOf.TryGetValue(lampCell, out int steps) ? steps : 0);
        }

        box.Configure(sectorLamps, sectorSteps, aiFollower);

        if (!_propWalls.TryGetValue(cell, out List<Vector3> taken))
        {
            taken = new List<Vector3>();
            _propWalls[cell] = taken;
        }
        taken.Add(dirVec);

        _fuseBoxes.Add(box);
    }

    /// <summary>Blackout floors start dark - one faint red emergency light at the start cell so the first seconds aren't pure black.</summary>
    private void BuildEmergencyLight(Vector2Int cell)
    {
        GameObject holder = new GameObject("EmergencyLight");
        holder.transform.SetParent(_mazeRoot, false);
        holder.transform.position = CellCenter(cell.x, cell.y) + Vector3.up * 1.6f;

        Light light = holder.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.9f, 0.15f, 0.1f);
        light.range = 3f;
        light.intensity = 0.4f;
        light.shadows = LightShadows.None;

        LightPool.Register(holder.transform, light.range, () => 1f);
    }

    // ---------------------------------------------------------------- keys (F77 Key Hunt)

    /// <summary>
    /// A KeyHunt floor's brass keys: FloorProfile.KeyCount cells, spread out by the same decreasing-
    /// minSeparation loop BuildFuses uses, each mounted on a free wall. Post-bake, after BuildFuses (so a
    /// key never lands on a reserved fuse-box wall) and before the +13 throwable/notes passes (so they in
    /// turn never land on a key's wall). Gates on what was actually placed, not FloorProfile.KeyCount
    /// (decision 11): KeyRing.Configure (SetUpAtmosphere) sets Total from _keys.Count, so a maze that only
    /// fits 1 of 3 keys locks the hatch behind 1 key rather than one that can never unlock.
    /// </summary>
    private void BuildKeys(System.Random rng)
    {
        _keys.Clear();
        if (_profile == null || _profile.KeyCount <= 0) return;

        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasKey)
        {
            Debug.LogError("MazeGenerator: no InteractableKit key in the scene - skipping this Key Hunt floor's keys (the hatch will not be locked). Run LIGHTS OUT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        Vector2Int start = Vector2Int.zero;
        HashSet<Vector2Int> starCellSet = new HashSet<Vector2Int>(_starCells);
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);

        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (cell == start || starCellSet.Contains(cell) || lockerCellSet.Contains(cell)) continue;
                if (_setPieceCells.Contains(cell) || _doorUsedCells.Contains(cell)) continue;
                if (_distance[x, z] < 3) continue;
                if (HasFreeWall(cell)) candidates.Add(cell);
            }
        }
        Shuffle(candidates, rng);

        List<Vector2Int> chosenCells = new List<Vector2Int>();
        int minSeparation = Mathf.Max(3, (width + height) / 5);
        while (minSeparation >= 1 && chosenCells.Count < _profile.KeyCount)
        {
            foreach (Vector2Int cell in candidates)
            {
                if (chosenCells.Count >= _profile.KeyCount) break;
                if (chosenCells.Contains(cell) || !IsFarEnough(cell, chosenCells, minSeparation)) continue;
                chosenCells.Add(cell);
            }
            minSeparation--;
        }

        for (int i = 0; i < chosenCells.Count; i++)
        {
            Vector2Int cell = chosenCells[i];
            if (!TryChooseFreeWall(cell, rng, out int dir))
            {
                Debug.LogWarning($"MazeGenerator: key at {cell} has no free wall; skipping it.", this);
                continue;
            }

            Vector3 dirVec = DirVector(dir);
            float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
            Vector3 pos = CellCenter(cell.x, cell.y) + dirVec * (backFaceDistance - 0.03f) + Vector3.up * 1.3f;

            KeyPickup key = Instantiate(interactableKit.KeyPickup, pos, Quaternion.LookRotation(-dirVec, Vector3.up), _mazeRoot);
            key.gameObject.SetActive(true);
            key.name = $"Key_{cell.x}_{cell.y}";

            if (!_propWalls.TryGetValue(cell, out List<Vector3> taken))
            {
                taken = new List<Vector3>();
                _propWalls[cell] = taken;
            }
            taken.Add(dirVec);

            _keys.Add(key);
        }

        if (_keys.Count < _profile.KeyCount)
        {
            Debug.LogWarning($"MazeGenerator: only {_keys.Count} of {_profile.KeyCount} keys fit in this maze.", this);
        }
    }

    // ---------------------------------------------------------------- throwables, noisy floors, lantern, lore, theme interactables (F74)

    /// <summary>A free-wall cell not already claimed by a star/locker/set-piece/door/button/vault or a prior call this pass - the same free-wall test PlaceButtonFor/BuildFuseBox use, shared by every F74 wall-mounted interactable.</summary>
    private bool TryPickFreeWallCell(System.Random rng, HashSet<Vector2Int> localUsed, out Vector2Int cell, out int dir)
    {
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);
        List<Vector2Int> candidates = new List<Vector2Int>();

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int c = new Vector2Int(x, z);
                if (c == Vector2Int.zero) continue;
                if (_starCells.Contains(c) || lockerCellSet.Contains(c) || _setPieceCells.Contains(c)) continue;
                if (_doorUsedCells.Contains(c) || (localUsed != null && localUsed.Contains(c))) continue;
                if (HasFreeWall(c)) candidates.Add(c);
            }
        }

        if (candidates.Count == 0)
        {
            cell = default;
            dir = -1;
            return false;
        }

        cell = candidates[rng.Next(candidates.Count)];
        return TryChooseFreeWall(cell, rng, out dir);
    }

    /// <summary>TryPickFreeWallCell, plus reserving the chosen wall in _propWalls and the chosen cell in `localUsed` so a second call this same pass never lands on top of the first.</summary>
    private bool TryPlaceOnFreeWall(System.Random rng, HashSet<Vector2Int> localUsed, out Vector2Int cell, out int dir)
    {
        if (!TryPickFreeWallCell(rng, localUsed, out cell, out dir)) return false;

        Vector3 dirVec = DirVector(dir);
        if (!_propWalls.TryGetValue(cell, out List<Vector3> taken))
        {
            taken = new List<Vector3>();
            _propWalls[cell] = taken;
        }
        taken.Add(dirVec);
        localUsed?.Add(cell);
        return true;
    }

    /// <summary>Bottles and cans (Blend(3,5) by floor progress) on plain floor cells, post-bake. A trigger collider at rest - see ThrowableItem.Awake.</summary>
    private void BuildThrowables(System.Random rng)
    {
        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasThrowables)
        {
            Debug.LogError("MazeGenerator: no InteractableKit throwables in the scene - skipping bottles/cans. Run LIGHTS OUT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        float t = _profile != null ? _profile.Progress : 0f;
        int want = Mathf.RoundToInt(Mathf.Lerp(3, 5, t));

        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);
        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (cell == Vector2Int.zero) continue;
                if (_starCells.Contains(cell) || lockerCellSet.Contains(cell) || _setPieceCells.Contains(cell) || _doorUsedCells.Contains(cell)) continue;
                candidates.Add(cell);
            }
        }
        Shuffle(candidates, rng);

        for (int i = 0; i < want && i < candidates.Count; i++)
        {
            Vector2Int cell = candidates[i];
            bool bottle = rng.NextDouble() < 0.6;
            ThrowableItem template = bottle ? interactableKit.ThrowableBottle : interactableKit.ThrowableCan;
            // F79: optional bottle look-alikes, picked by a hash of the cell so no extra rng draw is made.
            ThrowableItem[] variants = interactableKit.BottleVariants;
            if (bottle && variants != null && variants.Length > 0)
            {
                ThrowableItem variant = variants[(((cell.x * 73856093) ^ (cell.y * 19349663)) & int.MaxValue) % variants.Length];
                if (variant != null) template = variant;
            }

            Vector3 lateral = new Vector3((float)(rng.NextDouble() * 2.0 - 1.0), 0f, (float)(rng.NextDouble() * 2.0 - 1.0)) * (cellSize * 0.25f);
            Vector3 pos = CellCenter(cell.x, cell.y) + lateral + Vector3.up * (bottle ? 0.15f : 0.08f);
            float yaw = (float)(rng.NextDouble() * 360.0);

            ThrowableItem clone = Instantiate(template, pos, Quaternion.Euler(0f, yaw, 0f), _mazeRoot);
            clone.gameObject.SetActive(true);
            clone.name = $"Throwable_{template.name}_{cell.x}_{cell.y}";
        }
    }

    /// <summary>Glass/puddle patches (Blend(2,6) by floor progress, x RunRules.NoisyPatchMultiplier) centred in ordinary cells, post-bake.</summary>
    private void BuildNoisySurfaces(System.Random rng)
    {
        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasNoisySurfaces)
        {
            Debug.LogError("MazeGenerator: no InteractableKit noisy floor patches in the scene - skipping glass/puddles. Run LIGHTS OUT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        float t = _profile != null ? _profile.Progress : 0f;
        int want = Mathf.RoundToInt(Mathf.Lerp(2, 6, t) * RunRules.NoisyPatchMultiplier);

        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);
        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (cell == Vector2Int.zero) continue;
                if (_starCells.Contains(cell) || lockerCellSet.Contains(cell)) continue;
                candidates.Add(cell);
            }
        }
        Shuffle(candidates, rng);

        for (int i = 0; i < want && i < candidates.Count; i++)
        {
            Vector2Int cell = candidates[i];
            bool glass = rng.NextDouble() < 0.5;
            NoisySurface template = glass ? interactableKit.GlassPatch : interactableKit.Puddle;
            float yaw = (float)(rng.NextDouble() * 360.0);

            NoisySurface clone = Instantiate(template, CellCenter(cell.x, cell.y), Quaternion.Euler(0f, yaw, 0f), _mazeRoot);
            clone.gameObject.SetActive(true);
            clone.name = $"NoisySurface_{template.name}_{cell.x}_{cell.y}";
            clone.Configure(glass ? NoisySurface.Kind.Glass : NoisySurface.Kind.Puddle);
        }
    }

    /// <summary>One lantern per floor from floor 2 on - near the start (<=2 BFS steps) on a Blackout floor, anywhere valid otherwise.</summary>
    private void BuildLantern(System.Random rng)
    {
        _lanterns.Clear();
        if (_profile == null || _profile.Floor < 2) return;

        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasLantern)
        {
            Debug.LogError("MazeGenerator: no InteractableKit lantern in the scene - skipping it. Run LIGHTS OUT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        bool blackout = _profile.Has(FloorProfile.Objective.Blackout);
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);
        List<Vector2Int> candidates = new List<Vector2Int>();

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (cell == Vector2Int.zero) continue;
                if (_starCells.Contains(cell) || lockerCellSet.Contains(cell) || _setPieceCells.Contains(cell) || _doorUsedCells.Contains(cell)) continue;
                if (blackout && _distance[x, z] > 2) continue;
                candidates.Add(cell);
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning("MazeGenerator: no room for this floor's lantern.", this);
            return;
        }

        Vector2Int chosen = candidates[rng.Next(candidates.Count)];
        Vector3 pos = CellCenter(chosen.x, chosen.y) + Vector3.up * 0.35f;

        Lantern lantern = Instantiate(interactableKit.Lantern, pos, Quaternion.identity, _mazeRoot);
        lantern.gameObject.SetActive(true);
        lantern.name = $"Lantern_{chosen.x}_{chosen.y}";
        lantern.Configure(rng);
        _lanterns.Add(lantern);
    }

    /// <summary>2 lore notes per floor, ids (floor-1)*2 + {0,1} (F74 slice spec's verbatim text table).</summary>
    private void BuildLoreNotes(System.Random rng)
    {
        _loreNotes.Clear();
        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasLoreNote)
        {
            Debug.LogError("MazeGenerator: no InteractableKit lore note in the scene - skipping this floor's notes. Run LIGHTS OUT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        int floor = _profile != null ? _profile.Floor : 1;
        HashSet<Vector2Int> localUsed = new HashSet<Vector2Int>();

        for (int i = 0; i < 2; i++)
        {
            if (!TryPlaceOnFreeWall(rng, localUsed, out Vector2Int cell, out int dir))
            {
                Debug.LogWarning($"MazeGenerator: only {i} of 2 lore notes fit on floor {floor}.", this);
                break;
            }

            int noteId = (floor - 1) * 2 + i;
            Vector3 dirVec = DirVector(dir);
            float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
            Vector3 pos = CellCenter(cell.x, cell.y) + dirVec * (backFaceDistance - 0.03f) + Vector3.up * 1.4f;

            LoreNote note = Instantiate(interactableKit.LoreNote, pos, Quaternion.LookRotation(-dirVec, Vector3.up), _mazeRoot);
            note.gameObject.SetActive(true);
            note.name = $"LoreNote_{noteId}";
            note.Configure(noteId, LoreNoteText(noteId));
            _loreNotes.Add(note);
        }
    }

    /// <summary>The 10 lore texts, verbatim from plannings/darkness-and-interactivity-spec.md F74 item 4.</summary>
    private static string LoreNoteText(int id)
    {
        switch (id)
        {
            case 0: return "Night shift rota. Rule 1: the lights stay on. Rule 2: if the lights go out, do not run. Running is how it finds you. — Supervisor Okafor";
            case 1: return "Patient 7 keeps drawing stars on the walls. Says they are \"the way down\". We paint over them. By morning they are back, and they glow.";
            case 2: return "Maintenance log: pressure nominal. Fuse 3 tripped again. Someone keeps pressing the lockdown buttons at 3 a.m. Nobody is on shift at 3 a.m.";
            case 3: return "To whoever finds this: the shutters were never meant to keep you in. They were built to slow it down. It hates doors. Close them behind you.";
            case 4: return "The chapel candles are the only lights they could not cut. Light one. It does not help much. It helps enough.";
            case 5: return "Burial register, final page: \"Subject A — released to lower levels.\" There is no grave. There was never a grave.";
            case 6: return "Service unit trial 14: the old robots freeze under direct observation. Engineering calls it a bug. I watched one move every time my torch flickered. It is not a bug.";
            case 7: return "We never switched the service units off. We turned off the lights in their bay and stopped looking. That was enough, for a while.";
            case 8: return "If you are reading this, you got further than we did. The way out at the bottom is real. It opens once. Take the stars with you.";
            case 9: return "Last entry. It is not hunting you. It is following. Every floor you clear, it comes one floor down. Do not stop.";
            default: return "";
        }
    }

    /// <summary>Ward -> bell, Boiler -> valve, Crypt -> candles x2, Lab -> terminal. Never in kit mode, and never on the primitive fallback (no theme to key off - see decision D11's graceful-degradation rule).</summary>
    private void BuildThemeInteractables(System.Random rng)
    {
        _themeInteractables.Clear();
        _pendingTerminals.Clear();
        if (_kitMode || _theme == null || _profile == null) return;

        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasThemeInteractables)
        {
            Debug.LogError("MazeGenerator: no InteractableKit theme interactables in the scene - skipping this floor's. Run LIGHTS OUT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        if (aiFollower == null) aiFollower = FindAnyObjectByType<AIFollower>();
        HashSet<Vector2Int> localUsed = new HashSet<Vector2Int>();

        switch (_profile.ThemeIndex)
        {
            case FloorProfile.Theme.Ward:
                BuildCallBellInstance(rng, localUsed);
                break;
            case FloorProfile.Theme.BoilerDeck:
                BuildSteamValveInstance(rng, localUsed);
                break;
            case FloorProfile.Theme.Crypt:
                BuildCandlesInstance(rng, localUsed);
                BuildCandlesInstance(rng, localUsed);
                break;
            case FloorProfile.Theme.Lab:
                BuildTerminalInstance(rng, localUsed);
                break;
        }
    }

    /// <summary>`mountHeight` is how far above the floor the template's root goes (every template's geometry is centred on its root): wall-mounted kinds sit at hand height, floor-standing ones at half their own height. Added 4 Oct 2026 - until then every theme interactable was spawned at floor level.</summary>
    private ThemeInteractable InstantiateThemeInteractable(ThemeInteractable template, Vector2Int cell, int dir, string label, float mountHeight)
    {
        Vector3 dirVec = DirVector(dir);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        Vector3 pos = CellCenter(cell.x, cell.y) + dirVec * backFaceDistance + Vector3.up * mountHeight;

        ThemeInteractable clone = Instantiate(template, pos, Quaternion.LookRotation(-dirVec, Vector3.up), _mazeRoot);
        clone.gameObject.SetActive(true);
        clone.name = $"{label}_{cell.x}_{cell.y}";
        return clone;
    }

    private void BuildCallBellInstance(System.Random rng, HashSet<Vector2Int> localUsed)
    {
        if (!TryPlaceOnFreeWall(rng, localUsed, out Vector2Int cell, out int dir))
        {
            Debug.LogWarning("MazeGenerator: no room for the call bell.", this);
            return;
        }

        ThemeInteractable bell = InstantiateThemeInteractable(interactableKit.CallBell, cell, dir, "CallBell", 1.3f);
        Vector2Int remote = FindCellAtLeastSteps(cell, 5) ?? FarthestCell();
        bell.ConfigureCallBell(CellCenter(remote.x, remote.y), aiFollower);
        _themeInteractables.Add(bell);
    }

    private void BuildSteamValveInstance(System.Random rng, HashSet<Vector2Int> localUsed)
    {
        if (!TryPlaceOnFreeWall(rng, localUsed, out Vector2Int cell, out int dir))
        {
            Debug.LogWarning("MazeGenerator: no room for the steam valve.", this);
            return;
        }

        Vector2Int adjacent = cell;
        for (int d = 0; d < 4; d++)
        {
            if (IsWallClosed(cell.x, cell.y, d)) continue;
            adjacent = Neighbour(cell, d);
            break;
        }

        GameObject anchor = new GameObject("SteamAnchor");
        anchor.transform.SetParent(_mazeRoot, false);
        anchor.transform.position = CellCenter(adjacent.x, adjacent.y);

        NavMeshObstacle obstacle = anchor.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.size = new Vector3(cellSize - wallThickness, 2f, cellSize - wallThickness);
        obstacle.carving = true;
        obstacle.enabled = false;

        ParticleSystem steam = BuildSteamParticles(anchor.transform);

        ThemeInteractable valve = InstantiateThemeInteractable(interactableKit.SteamValve, cell, dir, "SteamValve", 1.0f);
        valve.ConfigureSteamValve(obstacle, steam, aiFollower);
        _themeInteractables.Add(valve);
    }

    private ParticleSystem BuildSteamParticles(Transform parent)
    {
        GameObject go = new GameObject("Steam");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.up * 1.4f;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.startLifetime = 1.5f;
        main.startSpeed = 0.6f;
        main.startSize = 0.6f;
        main.startColor = new Color(1f, 1f, 1f, 0.35f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 60;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 20f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.15f;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = GetSteamMaterial();

        ps.Stop();
        return ps;
    }

    private static Material s_steamMaterial;

    private static Material GetSteamMaterial()
    {
        if (s_steamMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            s_steamMaterial = new Material(shader) { name = "SteamParticleMat" };
            if (s_steamMaterial.HasProperty("_BaseColor")) s_steamMaterial.SetColor("_BaseColor", Color.white);
        }
        return s_steamMaterial;
    }

    private void BuildCandlesInstance(System.Random rng, HashSet<Vector2Int> localUsed)
    {
        if (!TryPlaceOnFreeWall(rng, localUsed, out Vector2Int cell, out int dir))
        {
            Debug.LogWarning("MazeGenerator: no room for candles.", this);
            return;
        }

        ThemeInteractable candles = InstantiateThemeInteractable(interactableKit.Candles, cell, dir, "Candles", 0f); // floor candles
        candles.ConfigureCandles(aiFollower);
        _themeInteractables.Add(candles);
    }

    private void BuildTerminalInstance(System.Random rng, HashSet<Vector2Int> localUsed)
    {
        if (!TryPlaceOnFreeWall(rng, localUsed, out Vector2Int cell, out int dir))
        {
            Debug.LogWarning("MazeGenerator: no room for the terminal.", this);
            return;
        }

        // ConfigureTerminal needs the player camera, which does not exist yet this early in Awake -
        // deferred to SetUpAtmosphere via _pendingTerminals.
        ThemeInteractable terminal = InstantiateThemeInteractable(interactableKit.Terminal, cell, dir, "Terminal", 0.35f); // floor console, 0.7 m tall, centred on its root
        _themeInteractables.Add(terminal);
        _pendingTerminals.Add(terminal);
    }

    /// <summary>BFS from `from` over open passages; the first cell found at or beyond `minSteps`, or null if the whole reachable region is smaller than that.</summary>
    private Vector2Int? FindCellAtLeastSteps(Vector2Int from, int minSteps)
    {
        Dictionary<Vector2Int, int> distance = new Dictionary<Vector2Int, int> { [from] = 0 };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(from);
        Vector2Int? found = null;

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            int next = distance[cell] + 1;
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(cell.x, cell.y, dir)) continue;
                Vector2Int n = Neighbour(cell, dir);
                if (n.x < 0 || n.x >= width || n.y < 0 || n.y >= height || distance.ContainsKey(n)) continue;
                distance[n] = next;
                queue.Enqueue(n);
                if (next >= minSteps) found = n;
            }
        }
        return found;
    }

    /// <summary>F74 DyingGrid run rule: kills one random still-powered, non-locker, non-dead lamp every 60 s, for the rest of the floor. Started once, from SetUpAtmosphere; stops itself when the GameObject does.</summary>
    private IEnumerator DyingGridRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(60f);
            if (!GameFlow.IsRunActive || GameOutcome.IsOver) continue;
            KillRandomPoweredLamp();
        }
    }

    private void KillRandomPoweredLamp()
    {
        List<int> candidates = new List<int>();
        for (int i = 0; i < _lamps.Count; i++)
        {
            WallLamp lamp = _lamps[i];
            if (lamp == null || lamp.IsDead || lamp.IsLockerLamp || !lamp.IsPowered) continue;

            Vector2Int cell = i < _lampCells.Count ? _lampCells[i] : default;
            bool nearStar = false;
            foreach (Vector2Int star in _starCells)
            {
                if (Mathf.Abs(cell.x - star.x) + Mathf.Abs(cell.y - star.y) <= 1) { nearStar = true; break; }
            }
            if (nearStar) continue;

            candidates.Add(i);
        }
        if (candidates.Count == 0) return;

        // A cosmetic pick, not maze-affecting - UnityEngine.Random rather than a seeded stream is fine
        // here (see CLAUDE.md/D15: the RNG-offset discipline is about the maze layout, not this).
        int pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        _lamps[pick].Kill(2.5f);
    }

    /// <summary>
    /// The star's material is emissive, so it glows - but emission lights nothing. Without a lamp of
    /// its own a star is invisible until you are already on top of it, and the faint glow bleeding
    /// around a corner is what makes a pitch-black maze navigable rather than infuriating.
    /// </summary>
    private void AttachStarLight(GameObject star)
    {
        GameObject holder = new GameObject("StarLight");
        holder.transform.SetParent(star.transform, false);
        holder.transform.localPosition = Vector3.zero;

        Light light = holder.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.64f, 0.19f);
        light.range = starLightRange;
        light.intensity = starLightIntensity;
        // The flashlight is the only shadow caster in the maze: a shadowed point light costs six
        // atlas faces, and a few of them would subdivide the atlas and wreck the flashlight.
        light.shadows = LightShadows.None;

        // F71: a gentle pulse so a star reads as something alive rather than a static bulb, now that
        // everything else in the maze has gone much darker around it.
        holder.AddComponent<StarPulse>().Configure(light);
    }

    private void PlacePlayer()
    {
        Transform player = FindPlayer();
        if (player == null)
        {
            Debug.LogWarning("MazeGenerator: no Player-tagged object with a CharacterController.", this);
            return;
        }

        // Facing the middle of the maze from the corner cell - computed so it holds on any rectangle
        // (F40 slice A decision 7), not just the square grids this used to be a constant 45f for.
        Vector3 mazeCentre = new Vector3(origin.x + width * cellSize * 0.5f, 0f, origin.z + height * cellSize * 0.5f);
        Vector3 toCentre = mazeCentre - CellCenter(0, 0);
        float startYaw = Mathf.Atan2(toCentre.x, toCentre.z) * Mathf.Rad2Deg;

        // The CharacterController overwrites transform.position, so it has to be off while teleporting.
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;

        player.position = CellCenter(0, 0);
        player.rotation = Quaternion.Euler(0f, startYaw, 0f);

        if (controller != null) controller.enabled = true;

        if (player.GetComponent<PlayerStealthState>() == null)
        {
            player.gameObject.AddComponent<PlayerStealthState>();
        }

        if (player.GetComponent<PlayerStamina>() == null)
        {
            player.gameObject.AddComponent<PlayerStamina>();
        }

        if (firstPerson && player.GetComponent<FirstPersonRig>() == null)
        {
            // AddComponent runs its Awake synchronously, so the rig reparents the camera right here -
            // after the teleport above, and before the ResetCameraRotation below, which is the order
            // it needs. (Its execution-order attribute only matters if it is ever placed in a scene
            // by hand instead.)
            player.gameObject.AddComponent<FirstPersonRig>();
        }

        ThirdPersonController thirdPerson = player.GetComponent<ThirdPersonController>();
        if (thirdPerson != null)
        {
            thirdPerson.ResetCameraRotation(startYaw);

            if (disablePlayerJump)
            {
                thirdPerson.JumpEnabled = false;
            }
        }
    }

    private void PlaceAI()
    {
        if (aiFollower == null)
        {
            aiFollower = FindAnyObjectByType<AIFollower>();
        }

        if (aiFollower == null) return;

        Vector2Int cell = FarthestCell();
        Vector3 position = CellCenter(cell.x, cell.y);
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, cellSize, NavMesh.AllAreas))
        {
            position = hit.position;
        }

        // AIFollower.Awake has not run yet (this component is at execution order -100), so the agent
        // is fetched here rather than through the follower.
        NavMeshAgent agent = aiFollower.GetComponent<NavMeshAgent>();
        if (agent == null || !agent.Warp(position))
        {
            // Move it anyway, so AIFollower.Start finds the maze navmesh within its search radius
            aiFollower.transform.position = position;
            Debug.LogWarning($"MazeGenerator: could not warp the AI onto the navmesh at {position}.", this);
        }

        // The scene ships a 5 m bidirectional NavMeshLink under the follower. It is registered in its
        // own OnEnable, i.e. after this Awake, so inside the maze it would span two walls and let the
        // AI walk straight through them.
        foreach (NavMeshLink link in aiFollower.GetComponentsInChildren<NavMeshLink>(true))
        {
            link.enabled = false;
        }

        aiFollower.SetWanderPoints(_cellCenters);
        aiFollower.SetPatrolTargets(_stars);
        aiFollower.ApplyDifficulty(_profile);

        List<Vector3> hidingFronts = new List<Vector3>();
        foreach (Locker locker in _lockers)
        {
            hidingFronts.Add(locker.FrontPosition);
        }
        aiFollower.SetHidingSpots(hidingFronts);
        aiFollower.SetAmbushProvider(this);

        AIPresence presence = aiFollower.GetComponent<AIPresence>();
        if (presence == null) presence = aiFollower.gameObject.AddComponent<AIPresence>();

        // The hunter's footsteps are the player's own, pitched down - and the star material is the
        // only emissive one to hand, so the eyes are built from a copy of it.
        presence.Configure(FindPlayerFootsteps(), TakeOverSceneAmbience(), FindStarMaterial());
    }

    /// <summary>
    /// F75 (+16, post-PlaceAI): clones InteractableKit.stalker onto floors where FloorProfile.HasStalker
    /// (4 and 5), warped onto the navmesh at least 8 BFS steps from the start and at least 4 from the
    /// hunter's own cell (FarthestCell - the same cell PlaceAI warped the hunter onto). Falls back to the
    /// single farthest-from-start reachable cell if no cell satisfies both, rather than skip the floor's
    /// stalker outright on a small maze.
    /// </summary>
    private void BuildStalker(System.Random rng)
    {
        if (_profile == null || !_profile.HasStalker) return;

        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasStalker)
        {
            Debug.LogError("MazeGenerator: no InteractableKit (stalker) in the scene - skipping the stalker this run. Run LIGHTS OUT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        Vector2Int start = Vector2Int.zero;
        Vector2Int hunterCell = FarthestCell();

        HashSet<Vector2Int> reachable = ReachableCells(start, null);
        Dictionary<Vector2Int, int> distFromStart = BfsWithin(start, reachable);
        Dictionary<Vector2Int, int> distFromHunter = BfsWithin(hunterCell, reachable);

        List<Vector2Int> candidates = new List<Vector2Int>();
        foreach (Vector2Int cell in reachable)
        {
            if (!distFromStart.TryGetValue(cell, out int ds) || ds < 8) continue;
            if (!distFromHunter.TryGetValue(cell, out int dh) || dh < 4) continue;
            candidates.Add(cell);
        }

        if (candidates.Count == 0)
        {
            Vector2Int best = start;
            int bestDistance = -1;
            foreach (KeyValuePair<Vector2Int, int> entry in distFromStart)
            {
                if (entry.Value > bestDistance) { bestDistance = entry.Value; best = entry.Key; }
            }
            candidates.Add(best);
        }

        Shuffle(candidates, rng);
        Vector2Int chosenCell = candidates[0];
        Vector3 position = CellCenter(chosenCell.x, chosenCell.y);
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, cellSize, NavMesh.AllAreas))
        {
            position = hit.position;
        }

        GameObject instance = Instantiate(interactableKit.Stalker.gameObject, position, Quaternion.identity, _mazeRoot);
        instance.name = "Stalker";
        instance.SetActive(true);

        NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
        if (agent == null) agent = instance.AddComponent<NavMeshAgent>();
        agent.radius = 0.3f;
        agent.speed = 3.4f;
        agent.acceleration = 40f;
        agent.angularSpeed = 720f;
        if (!agent.Warp(position))
        {
            Debug.LogWarning($"MazeGenerator: could not warp the stalker onto the navmesh at {position}.", this);
        }

        _stalker = instance.GetComponent<Stalker>();
    }

    /// <summary>
    /// The scene ships a cheerful 2D sci-fi ambience on a loop, which is exactly wrong here - and its
    /// clip is a perfect mechanical hum for the hunter once it is pitched down and made positional.
    /// Silences the source and hands back its clip.
    /// </summary>
    private AudioClip TakeOverSceneAmbience()
    {
        AudioClip clip = null;

        foreach (AudioSource source in FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude))
        {
            if (!source.loop || !source.playOnAwake || source.clip == null) continue;

            clip = source.clip;
            source.Stop();
            source.enabled = false;
        }

        return clip;
    }

    private AudioClip[] FindPlayerFootsteps()
    {
        Transform player = FindPlayer();
        if (player == null) return null;

        ThirdPersonController controller = player.GetComponent<ThirdPersonController>();
        return controller != null ? controller.FootstepAudioClips : null;
    }

    private Material FindStarMaterial()
    {
        if (starPrefab == null) return null;

        Renderer renderer = starPrefab.GetComponentInChildren<Renderer>();
        return renderer != null ? renderer.sharedMaterial : null;
    }

    private void SetUpAtmosphere()
    {
        // Hoisted so the dread wiring below (added after `outcome` exists) can still reach it -
        // it is only built at all when darkness is on.
        HorrorAtmosphere atmosphere = null;
        if (darkness)
        {
            atmosphere = FindAnyObjectByType<HorrorAtmosphere>();
            if (atmosphere == null) atmosphere = gameObject.AddComponent<HorrorAtmosphere>();
            if (_profile != null)
            {
                if (_theme != null)
                {
                    atmosphere.ApplyProfile(_theme.Ambient, _profile.FogDensity * _theme.FogDensityScale, _theme.FogColor);
                }
                else
                {
                    atmosphere.ApplyProfile(_profile.Ambient, _profile.FogDensity);
                }
            }
        }

        HorrorAudioDirector director = FindAnyObjectByType<HorrorAudioDirector>();
        if (director == null) director = gameObject.AddComponent<HorrorAudioDirector>();

        AudioClip music = PickBaseMusic();
        director.Bind(FindPlayer(), aiFollower, stingClip, music != null ? music : droneClip, panicClip, droneIsMusic: music != null, spotted: spottedClip);

        TensionDirector tension = FindAnyObjectByType<TensionDirector>();
        if (tension == null) tension = gameObject.AddComponent<TensionDirector>();

        tension.Configure(aiFollower, director);

        Transform player = FindPlayer();
        FirstPersonRig rig = player != null ? player.GetComponent<FirstPersonRig>() : null;
        Flashlight flashlight = rig != null ? rig.Flashlight : null;
        PlayerStamina stamina = player != null ? player.GetComponent<PlayerStamina>() : null;
        PlayerStealthState stealth = player != null ? player.GetComponent<PlayerStealthState>() : null;

        PlayerHud hud = FindAnyObjectByType<PlayerHud>();
        if (hud == null) hud = gameObject.AddComponent<PlayerHud>();
        hud.Configure(flashlight, stamina);
        tension.BindHud(hud);

        // F73: PlayerStealthState now reads LightPool.ExposureAt directly - every WallLamp registers
        // itself in WallLamp.Configure, so it no longer needs this maze's lamp list handed to it.
        if (_profile != null && stamina != null) stamina.SetSprintSeconds(_profile.SprintSeconds);

        // F72: the one interaction system every door/button/vault pickup goes through. Built here
        // (rather than FirstPersonRig) because it needs the camera, the HUD and the stealth state, all
        // of which exist by this point in SetUpAtmosphere.
        PlayerInteractor interactor = player != null ? player.GetComponent<PlayerInteractor>() : null;
        if (interactor == null && player != null) interactor = player.gameObject.AddComponent<PlayerInteractor>();
        if (interactor != null) interactor.Configure(rig != null ? rig.PlayerCamera : null, hud, stealth);

        foreach (Locker locker in _lockers)
        {
            locker.Bind(player, stealth, flashlight, hud, interactor);
        }

        // F72: doors/buttons/vault loot were built before the flashlight, HUD and interactor existed
        // (BuildDoors runs in the middle of Awake, well before this method), so they are late-bound
        // here - the same pattern BuildLocker/Locker.Bind already uses.
        foreach (WallButton button in _buttons)
        {
            button.BindHud(hud);
        }
        foreach (BatteryCellPickup pickup in _batteryPickups)
        {
            pickup.BindPlayerSystems(flashlight, hud);
        }

        // Must exist before MazeEscape.Configure receives it
        GameOutcome outcome = FindAnyObjectByType<GameOutcome>();
        if (outcome == null) outcome = gameObject.AddComponent<GameOutcome>();

        // Tier 3: relocation, phantoms, F27's beats and the subtitles they all share. Both need
        // `outcome` for IsEnding, which is why they are wired here rather than beside TensionDirector.
        AIPresence presence = aiFollower != null ? aiFollower.GetComponent<AIPresence>() : null;
        Camera camera = rig != null ? rig.PlayerCamera : null;

        // F74: throwables, noisy floors, lantern, lore, theme interactables. All built earlier in Awake,
        // before the camera/HUD/interactor existed - late-bound here, same pattern as the block above.
        // Placed after `camera` exists (Terminal/ThrowController both need it).
        ThrowController throwController = player != null ? player.GetComponent<ThrowController>() : null;
        if (throwController == null && player != null) throwController = player.gameObject.AddComponent<ThrowController>();
        if (throwController != null)
        {
            throwController.Configure(camera, aiFollower, stealth, _mazeRoot,
                interactableKit != null ? interactableKit.ThrowableBottle : null,
                interactableKit != null ? interactableKit.ThrowableCan : null,
                interactableKit != null ? interactableKit.GlassPatch : null);
        }
        hud.BindThrowController(throwController);

        // F77: the keys held this floor - an instance component on the player (like ThrowController), so
        // a scene reload recreates it empty. Built earlier in Awake (BuildKeys, +18), before the player/
        // HUD/hunter existed - late-bound here, same pattern as the block above.
        KeyRing keyRing = player != null ? player.GetComponent<KeyRing>() : null;
        if (keyRing == null && player != null) keyRing = player.gameObject.AddComponent<KeyRing>();
        keyRing?.Configure(_keys);
        foreach (KeyPickup key in _keys)
        {
            key.Bind(keyRing, hud, aiFollower);
        }
        tension.BindKeyRing(keyRing);

        LoreReadingUi loreReader = FindAnyObjectByType<LoreReadingUi>();
        if (loreReader == null) loreReader = gameObject.AddComponent<LoreReadingUi>();
        loreReader.Configure(aiFollower);
        foreach (LoreNote note in _loreNotes)
        {
            note.Bind(loreReader, hud);
        }

        foreach (Lantern lantern in _lanterns)
        {
            lantern.BindPlayerSystems(player, stamina, stealth, aiFollower, hud);
        }

        foreach (ThemeInteractable theme in _themeInteractables)
        {
            theme.BindHud(hud);
        }
        foreach (ThemeInteractable terminal in _pendingTerminals)
        {
            terminal.ConfigureTerminal(aiFollower, camera);
        }

        // F74 WeakBattery run rule: applied once here, right after the flashlight exists and before the
        // player has had a chance to touch it.
        if (flashlight != null && RunRules.Current == RunRules.Rule.WeakBattery)
        {
            flashlight.SetStartFraction(RunRules.BatteryStartFraction);
            flashlight.SetRechargeMultiplier(RunRules.BatteryRechargeMultiplier);
        }

        // F74 DyingGrid run rule: a random still-powered lamp loses power every minute, for the rest of
        // the floor. Lives on this component (not DreadDirector) since it is a plain interval effect,
        // not something that keys off star progress.
        if (RunRules.Current == RunRules.Rule.DyingGrid) StartCoroutine(DyingGridRoutine());

        // F73: lets the hunter notice the torch beam itself, not just what it lights up.
        aiFollower?.BindTorch(flashlight, camera != null ? camera.transform : null);

        // F82: the hunter talks in speech bubbles too (same engine as Stan's). LIGHTS OUT > Build Phrase
        // Books puts a HunterVoice with its phrase book on the scene hunter; added here if missing.
        if (aiFollower != null)
        {
            HunterVoice hunterVoice = aiFollower.GetComponent<HunterVoice>();
            if (hunterVoice == null) hunterVoice = aiFollower.gameObject.AddComponent<HunterVoice>();
            hunterVoice.Configure(aiFollower, stealth, outcome);
        }

        // F75: the second creature (floors 4-5 only, FloorProfile.HasStalker). Built earlier in Awake
        // (BuildStalker, +16), before the player/camera/flashlight/hunter/HUD/outcome existed -
        // late-bound here, same pattern as the lantern/lore notes above.
        _stalker?.Configure(this, player, flashlight, camera != null ? camera.transform : null, stealth, aiFollower, hud, outcome);

        // F75 shop depth: Focus Lens widens the focused beam and drops its drain penalty for this
        // floor; Fuse Map lights up every fuse box, button, vault door and (F77) key with a small beacon
        // and lets the Star Compass (ConsumableController.NearestCompassTarget) point at them too.
        if (flashlight != null && PlayerInventory.HasActive(ShopItem.FocusLens))
        {
            flashlight.SetRangeMultiplier(1.35f);
            flashlight.SetFocusedDrainMultiplier(1f);
        }
        if (PlayerInventory.HasActive(ShopItem.FuseMap))
        {
            foreach (FuseBox box in _fuseBoxes) box?.EnableBeacon();
            foreach (WallButton button in _buttons) button?.EnableBeacon();
            foreach (MazeDoor door in _doors)
            {
                if (door != null && door.IsVaultDoor) door.EnableBeacon();
            }
            foreach (KeyPickup key in _keys) key?.EnableBeacon();
        }

        DreadDirector dread = FindAnyObjectByType<DreadDirector>();
        if (dread == null) dread = gameObject.AddComponent<DreadDirector>();
        dread.Configure(this, player, camera, aiFollower, presence, director, atmosphere, flashlight, stealth, hud, outcome, _profile);
        // F74 Restless run rule: relocations can chain tighter. PhantomInterval's own x0.6 is already
        // folded into _profile by RunRules.ApplyToProfile, read by PhantomDirector.Configure below.
        if (RunRules.Current == RunRules.Rule.Restless) dread.SetHardMinimumCooldownMultiplier(0.7f);

        PhantomDirector phantoms = FindAnyObjectByType<PhantomDirector>();
        if (phantoms == null) phantoms = gameObject.AddComponent<PhantomDirector>();
        Material phantomWallMat = _theme != null && _theme.WallMaterial != null ? _theme.WallMaterial : _wallMat;
        phantoms.Configure(this, player, camera, aiFollower, director, flashlight, stealth, outcome, dread, phantomWallMat, FindStarMaterial(), FindPlayerFootsteps(), _profile);

        // Tier 4: the room between floors. Unlike everything else here it is authored in the scene
        // (LIGHTS OUT > Build > Shop Room generates it once), so it can be inspected and edited in the
        // Editor. Only its panel is runtime UI.
        ShopMenu shopMenu = FindAnyObjectByType<ShopMenu>();
        if (shopMenu == null) shopMenu = gameObject.AddComponent<ShopMenu>();
        shopMenu.Configure(player, hud, flashlight);

        ShopRoom shop = FindAnyObjectByType<ShopRoom>();
        if (shop != null && !shop.IsComplete)
        {
            Debug.LogError("MazeGenerator: the ShopRoom in the scene has no Arrival Point or zones. Run LIGHTS OUT > Build > Shop Room in the Editor.", shop);
            shop = null;
        }
        else if (shop == null)
        {
            Debug.LogError("MazeGenerator: there is no ShopRoom in the scene, so clearing floors 1-4 will end the run as a win. Run LIGHTS OUT > Build > Shop Room in the Editor, then save the scene.", this);
        }
        if (shop != null) shop.Configure(player, hud, shopMenu);

        // F86: the Intake - the playable tutorial START GAME opens before floor 1. Authored in the scene like the shop
        // (LIGHTS OUT > Build > Intake Room); without it START GAME falls back to the old rules screen.
        IntakeRoom intake = FindAnyObjectByType<IntakeRoom>(FindObjectsInactive.Include);
        if (intake != null && !intake.IsComplete)
        {
            Debug.LogError("MazeGenerator: the IntakeRoom in the scene is incomplete. Run LIGHTS OUT > Build > Intake Room in the Editor.", intake);
            intake = null;
        }
        else if (intake == null && mainMenu)
        {
            Debug.LogError("MazeGenerator: there is no IntakeRoom in the scene, so START GAME shows the rules screen instead of the tutorial. Run LIGHTS OUT > Build > Intake Room in the Editor, then save the scene.", this);
        }
        if (intake != null) intake.Configure(player, camera, hud, flashlight, stealth, interactor, throwController, loreReader);

        MazeEscape escape = null;
        if (escapeSequence)
        {
            escape = FindAnyObjectByType<MazeEscape>();
            if (escape == null) escape = gameObject.AddComponent<MazeEscape>();

            escape.Configure(this, player, FindStarMaterial(), outcome, aiFollower);
            if (_profile != null) escape.SetEscapeSeconds(_profile.EscapeSeconds);
            // F77: the padlock - the hatch/lift refuses to complete while keys are still missing.
            escape.BindKeyRing(keyRing, hud);
        }

        outcome.Configure(player, aiFollower, director, escape, flashlight, UsedSeed);
        outcome.BindShop(shop, this, hud, stealth);

        MainMenu menu = null;
        if (mainMenu)
        {
            menu = FindAnyObjectByType<MainMenu>();
            if (menu == null) menu = gameObject.AddComponent<MainMenu>();

            menu.Configure(player, menuMusic);
        }
        else
        {
            // No title screen means nothing calls MainMenu.StartGame, which is what starts the run
            GameFlow.BeginRun();
        }

        PauseMenu pause = FindAnyObjectByType<PauseMenu>();
        if (pause == null) pause = gameObject.AddComponent<PauseMenu>();

        pause.Configure(player, flashlight, menu, outcome, UsedSeed, shopMenu);

        // F87: checks the contract taken in the shop for this floor (an instance component, like KeyRing: a reload recreates it).
        ContractTracker contracts = player != null ? player.GetComponent<ContractTracker>() : null;
        if (contracts == null && player != null) contracts = player.gameObject.AddComponent<ContractTracker>();
        if (contracts != null) contracts.Configure(player, stealth, flashlight, aiFollower, hud, menu);
        outcome.BindContracts(contracts);

        ConsumableController items = FindAnyObjectByType<ConsumableController>();
        if (items == null) items = gameObject.AddComponent<ConsumableController>();
        items.Configure(player, flashlight, stealth, this, hud, escape, outcome, menu, pause);

        // F76: the on-screen controls for the Web build on a tablet, and the mobile render-scale/shadow
        // trim. Built last of the two, once every system it drives (interactor, throwController, hud,
        // menu, pause, shopMenu, outcome, loreReader) already exists.
        StarterAssetsInputs starterInputs = player != null ? player.GetComponent<StarterAssetsInputs>() : null;
        TouchControls touchControls = FindAnyObjectByType<TouchControls>();
        if (touchControls == null) touchControls = gameObject.AddComponent<TouchControls>();
        touchControls.Configure(starterInputs, stamina, interactor, throwController, hud, menu, pause, shopMenu, outcome, loreReader, debugForceTouchControls);

        MobilePerformance mobilePerf = FindAnyObjectByType<MobilePerformance>();
        if (mobilePerf == null) mobilePerf = gameObject.AddComponent<MobilePerformance>();
        mobilePerf.Configure(flashlight);

        // Campaign cosmetics (F35), applied on every scene build so the choice survives a reload.
        if (flashlight != null) flashlight.SetBeamColor(ShopCatalogue.TorchColours[PlayerInventory.TorchColourIndex].Colour);
        if (hud != null) hud.SetTint(ShopCatalogue.HudTints[PlayerInventory.HudTintIndex].Colour);
        tension.SetCalmColor(ShopCatalogue.HudTints[PlayerInventory.HudTintIndex].Colour);
        if (_profile != null && stealth != null) stealth.NoiseScale = _profile.NoiseScale;

        // F74: the "FLOOR n - THE X" card. Built last, once every other system (theme, kit mode,
        // RunRules, FloorProfile) is settled - its own coroutine waits out the title screen itself.
        FloorIntroBanner banner = FindAnyObjectByType<FloorIntroBanner>();
        if (banner == null) banner = gameObject.AddComponent<FloorIntroBanner>();
        banner.Show(FloorTitleText(), ObjectiveLineText(), RuleLineText(), _profile != null ? _profile.TemperamentText : "");

        // Debug affordance: open the floor as if the hatch had just been reached.
        if (debugStartInShop) StartCoroutine(DebugEnterShop(outcome));
    }

    private static IEnumerator DebugEnterShop(GameOutcome outcome)
    {
        yield return null;
        outcome.FloorCleared();
    }

    /// <summary>F74 FloorIntroBanner title line. Kit maze and the primitive fallback both have no FloorTheme to name, so they get their own generic text instead.</summary>
    private string FloorTitleText()
    {
        int floor = GameFlow.CurrentFloor;
        if (_kitMode) return $"FLOOR {floor} — THE KIT";
        if (_theme != null) return $"FLOOR {floor} — {_theme.DisplayName.ToUpperInvariant()}";
        return $"FLOOR {floor}";
    }

    /// <summary>F74/F77 FloorIntroBanner objective line, from FloorProfile.Objectives (a flag set since F77).</summary>
    private string ObjectiveLineText()
    {
        if (_profile == null) return "";

        List<string> parts = new List<string> { $"FIND {_profile.StarCount} STARS" };
        if (_profile.Has(FloorProfile.Objective.Blackout)) parts.Add($"RESTORE {_profile.FuseCount} FUSES");
        if (_profile.Has(FloorProfile.Objective.KeyHunt)) parts.Add($"FIND {_profile.KeyCount} KEYS FOR THE HATCH");
        if (_profile.Has(FloorProfile.Objective.Lockdown)) parts.Add("SECURITY LOCKDOWN");
        return string.Join(" · ", parts);
    }

    /// <summary>F74 FloorIntroBanner rule line. Empty on floor 1 or when RunRules picked nothing.</summary>
    private static string RuleLineText()
    {
        if (RunRules.Current == RunRules.Rule.None) return "";
        return $"{RunRules.Name} — {RunRules.Description}";
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Cell centre farthest (flat) from a point. F36 warps the released hunter here.</summary>
    public Vector3 FarthestCellCenterFrom(Vector3 point)
    {
        Vector3 best = _cellCenters.Count > 0 ? _cellCenters[0] : point;
        float bestDistance = -1f;

        foreach (Vector3 cell in _cellCenters)
        {
            Vector3 flat = cell - point;
            flat.y = 0f;
            float distance = flat.sqrMagnitude;
            if (distance > bestDistance)
            {
                bestDistance = distance;
                best = cell;
            }
        }

        return best;
    }

    private Vector2Int FarthestCell()
    {
        Vector2Int best = new Vector2Int(width - 1, height - 1);
        int bestDistance = -1;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (_distance[x, z] > bestDistance)
                {
                    bestDistance = _distance[x, z];
                    best = new Vector2Int(x, z);
                }
            }
        }

        return best;
    }

    private static void Shuffle(List<Vector2Int> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>One of baseMusic for this floor, from the seed (a Retry of the same maze keeps it), or null if none are set.</summary>
    private AudioClip PickBaseMusic()
    {
        if (baseMusic == null || baseMusic.Length == 0) return null;
        // Salted ("MUSI") so it is its own draw, independent of RunRules' and the objective roll's.
        int hash = RunRules.HashSeedFloor(UsedSeed ^ unchecked((int)0x4D555349), GameFlow.CurrentFloor);
        int index = ((hash % baseMusic.Length) + baseMusic.Length) % baseMusic.Length;
        if (baseMusic[index] != null) return baseMusic[index];
        foreach (AudioClip clip in baseMusic) if (clip != null) return clip;
        return null;
    }

    private static Transform FindPlayer()
    {
        // Same rule as AIFollower.FindTarget: two objects carry the Player tag and only one moves.
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject player in players)
        {
            if (player.GetComponent<CharacterController>() != null)
            {
                return player.transform;
            }
        }

        return players.Length > 0 ? players[0].transform : null;
    }
}

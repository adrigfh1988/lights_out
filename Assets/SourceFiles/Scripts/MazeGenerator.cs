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
    [Tooltip("Looping bed, e.g. SFX_AmbienceClose")]
    [SerializeField] private AudioClip droneClip;
    [Tooltip("One-shot fired when the hunter spots you")]
    [SerializeField] private AudioClip stingClip;
    [Tooltip("Looping track for the escape, e.g. Music_Exciting")]
    [SerializeField] private AudioClip panicClip;

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
    [Tooltip("Scene gallery built by LIGHTS OUT > Build Floor Themes. Found automatically; assign only to override.")]
    [SerializeField] private FloorThemeSet themeSet;
    [Tooltip("Combine the static themed geometry (walls, pillars, floor, ceiling) after the navmesh bake. Off until profiled.")]
    [SerializeField] private bool staticBatchThemedGeometry = false;

    [Header("Kit maze (F48)")]
    [Tooltip("Scene object built by LIGHTS OUT > Build Kit Maze Theme. Found automatically; assign only to override.")]
    [SerializeField] private KitMazeTheme kitTheme;
    [Tooltip("Corridor pitch when GameFlow.UseKitMaze is set. Whole metres so 3M/2M/1M pieces fill a wall exactly.")]
    [SerializeField] private int kitCellSize = 5;

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
    private FloorProfile _profile;
    private Material _wallMat;
    private Material _floorMat;
    private Material _ceilingMat;

    private FloorTheme _theme;
    /// <summary>F48: true when GameFlow.UseKitMaze is set and a complete KitMazeTheme was found. _theme stays null in this mode - see ResolveKitTheme.</summary>
    private bool _kitMode;
    private Transform _wallsGroup, _pillarsGroup, _floorGroup, _ceilingGroup, _lampsGroup, _lockersGroup, _propsGroup;
    /// <summary>Wall directions already taken by a wall prop, per cell. Consulted by BuildCeilingProps (no double-dressing a cell) and SpawnShards (no shard clipping a prop).</summary>
    private readonly Dictionary<Vector2Int, List<Vector3>> _propWalls = new Dictionary<Vector2Int, List<Vector3>>();

    /// <summary>The theme this maze was built from, or null when the primitive fallback was used.</summary>
    public FloorTheme Theme => _theme;

    /// <summary>The spawned stars. Entries become null as they are collected.</summary>
    public IReadOnlyList<Transform> Stars => _stars;

    /// <summary>Every locker built for this maze.</summary>
    public IReadOnlyList<Locker> Lockers => _lockers;

    /// <summary>Every wall lamp built for this maze.</summary>
    public IReadOnlyList<WallLamp> Lamps => _lamps;

    /// <summary>Floor-level world centre of every cell, row-major (x + z * width).</summary>
    public IReadOnlyList<Vector3> CellCenters => _cellCenters;

    /// <summary>The seed this maze was actually built from, whether it came from the Inspector, a retry or the clock.</summary>
    public int UsedSeed { get; private set; }

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

        ApplyFloorProfile(freshStart);
        ResolveTheme();

        ResolveKitTheme(); // sets _kitMode; when true also forces _theme = null
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

        // A seed pending from a "try this maze again" wins over the Inspector, and is used up
        int usedSeed = GameFlow.PendingSeed != 0 ? GameFlow.PendingSeed
            : seed != 0 ? seed : System.Environment.TickCount;
        GameFlow.PendingSeed = 0;
        UsedSeed = usedSeed;
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

        // +4 is the RNG offset props own (+0 carve, +1 stars, +2 lamps, +3 lockers, +5 shards). One
        // instance is threaded through both prop passes below so the stream stays continuous across the
        // navmesh bake between them - same seed and floor always puts props on the same walls.
        System.Random propRng = new System.Random(usedSeed + 4);
        BuildWallProps(propRng);

        // Bake before anything else is placed inside the volume: the surface collects render meshes on
        // every layer, so a star, a ceiling or a robot standing in the maze would be carved out of the
        // navmesh. Lockers and wall props are built above, before this line, so their bodies are carved
        // out too and the hunter paths around them instead of through them.
        BuildRuntimeNavMesh();

        BuildCeiling();
        BuildCeilingProps(propRng);
        BuildWallLamps();
        DisableExistingPickups();
        SpawnStars(usedSeed);
        SpawnShards(usedSeed);
        PlacePlayer();
        PlaceAI();
        SetUpAtmosphere();

        if (staticBatchThemedGeometry && _theme != null) CombineStaticGroups();
        // Whatever the artist sees in the gallery is exactly what was cloned, including unapplied
        // overrides - so once cloning is done the gallery itself has no further reason to be visible.
        // Also hidden when the fallback ran: an incomplete gallery must not leave five rows of point
        // lights and locker triggers live in the scene during a run.
        if (themeSet != null) themeSet.gameObject.SetActive(false);
    }

    /// <summary>
    /// Picks the FloorThemes gallery row this maze clones, or leaves _theme null to fall back to the
    /// primitive geometry the game shipped with before theming existed. The Editor cannot run the
    /// LIGHTS OUT &gt; Build Floor Themes menu item on this implementer's behalf (it does not hold the
    /// project lock), so until the user runs it and saves the scene, every maze uses this fallback.
    /// </summary>
    private void ResolveTheme()
    {
        _theme = null;
        if (_profile == null) return; // Inspector mode (useFloorProfile off): primitives, as before

        if (themeSet == null) themeSet = FindAnyObjectByType<FloorThemeSet>(FindObjectsInactive.Include);
        if (themeSet == null)
        {
            Debug.LogError("MazeGenerator: no FloorThemes gallery in the scene, building the plain maze. Run LIGHTS OUT > Build Floor Themes in the Editor, then save the scene.", this);
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
            Debug.LogError("MazeGenerator: GameFlow.UseKitMaze is set but there is no complete KitMazeTheme in the scene. Run LIGHTS OUT > Build Kit Maze Theme, then save the scene. Building the normal maze.", this);
            return;
        }

        _kitMode = true;
        _theme = null;
    }

    /// <summary>
    /// Picks this run's floor and overwrites the world-size and lamp fields with its values. The
    /// hunter, light and timers are handed their values later, where each is wired up. Runs first in
    /// Awake, before anything reads width, height, starCount, lockerCount or the lamp settings.
    /// </summary>
    private void ApplyFloorProfile(bool freshStart)
    {
        _profile = null;
        if (!useFloorProfile) return;

        // A debug floor picks the floor a fresh start begins on, and is written back so the HUD, end
        // screens and Next Floor all agree with it. Only on a fresh start: a reload that came from the
        // shop door or a retry (SkipMenuOnLoad) must keep the floor the game set, or the shop's door
        // would lead straight back to the same floor forever.
        if (debugFloor > 0 && freshStart) GameFlow.CurrentFloor = Mathf.Clamp(debugFloor, 1, FloorProfile.FinalFloor);

        _profile = FloorProfile.For(GameFlow.CurrentFloor);
        PlayerInventory.ApplyActiveModifiers(_profile);

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
    /// Themed only, built before the navmesh bake so a prop's body collider carves the navmesh like a
    /// locker does. One prop per eligible cell at most. Skips the start cell, the AI cell and every
    /// locker cell (decision 6). Clears and repopulates _propWalls, which BuildCeilingProps and
    /// SpawnShards both consult afterwards.
    /// </summary>
    private void BuildWallProps(System.Random rng)
    {
        _propWalls.Clear();
        if (_theme == null || _theme.Props == null || _theme.Props.Length == 0) return;

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
                if (rng.NextDouble() >= _theme.WallPropChance) continue;

                walls.Clear();
                if (_wallN[x, z]) walls.Add(Vector3.forward);
                if (_wallE[x, z]) walls.Add(Vector3.right);
                if (_wallS[x, z]) walls.Add(Vector3.back);
                if (_wallW[x, z]) walls.Add(Vector3.left);
                if (walls.Count == 0) continue;

                Vector3 dir = walls[rng.Next(walls.Count)];
                PropPiece prop = PickWeightedProp(_theme.Props, PropPiece.MountKind.Wall, rng);
                if (prop == null) return; // no wall props in this theme, nothing more to try

                Vector3 pos = CellCenter(x, z) + dir * backFaceDistance;
                PropPiece clone = Instantiate(prop, pos, Quaternion.LookRotation(-dir), _propsGroup);
                clone.gameObject.SetActive(true);
                clone.name = $"Prop_{prop.name}_{x}_{z}";

                if (!_propWalls.TryGetValue(cell, out List<Vector3> taken))
                {
                    taken = new List<Vector3>();
                    _propWalls[cell] = taken;
                }
                taken.Add(dir);
            }
        }
    }

    /// <summary>Themed only, built after the navmesh bake. Never shares a cell with a wall prop (decision 6, "keeps clutter readable").</summary>
    private void BuildCeilingProps(System.Random rng)
    {
        if (_theme == null || _theme.Props == null || _theme.Props.Length == 0) return;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (x == 0 && z == 0) continue;

                Vector2Int cell = new Vector2Int(x, z);
                if (_lockerCells.Contains(cell)) continue;
                if (_propWalls.ContainsKey(cell)) continue;
                if (rng.NextDouble() >= _theme.CeilingPropChance) continue;

                PropPiece prop = PickWeightedProp(_theme.Props, PropPiece.MountKind.Ceiling, rng);
                if (prop == null) return; // no ceiling props in this theme, nothing more to try

                Vector3 pos = CellCenter(x, z) + Vector3.up * wallHeight;
                Quaternion rotation = Quaternion.Euler(0f, rng.Next(4) * 90f, 0f);

                // F40 slice A decision 6: a low ceiling (floor 3) can't fit every prop without brushing
                // the player. Checked after every RNG draw this cell makes (chance, pick, rotation) so
                // the seed stream is unchanged - a taller floor still gets exactly the props, in exactly
                // the same rotations, it would have before this guard existed.
                if (prop.Depth > wallHeight - 2.2f) continue;

                PropPiece clone = Instantiate(prop, pos, rotation, _propsGroup);
                clone.gameObject.SetActive(true);
                clone.name = $"Prop_{prop.name}_{x}_{z}";
            }
        }
    }

    /// <summary>Weighted pick among a theme's props of one mount kind, honouring EnabledInMaze. Null if the theme has none of that kind (or none currently enabled).</summary>
    private static PropPiece PickWeightedProp(PropPiece[] props, PropPiece.MountKind mount, System.Random rng)
    {
        float totalWeight = 0f;
        foreach (PropPiece candidate in props)
        {
            if (candidate == null || candidate.Mount != mount || !candidate.EnabledInMaze) continue;
            totalWeight += Mathf.Max(0.0001f, candidate.Weight);
        }
        if (totalWeight <= 0f) return null;

        float roll = (float)(rng.NextDouble() * totalWeight);
        float cumulative = 0f;
        foreach (PropPiece candidate in props)
        {
            if (candidate == null || candidate.Mount != mount || !candidate.EnabledInMaze) continue;
            cumulative += Mathf.Max(0.0001f, candidate.Weight);
            if (roll <= cumulative) return candidate;
        }

        return null; // floating point edge case only
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
        material.SetColor("_EmissionColor", lampColor * 2.5f);

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
                if (_wallW[x, z])
                {
                    SpawnKitWallRun(
                        new Vector3(origin.x + x * cell, kitY, origin.z + z * cell),
                        alongX: false, fill, $"Wall_W_{x}_{z}");
                }

                if (_wallS[x, z])
                {
                    SpawnKitWallRun(
                        new Vector3(origin.x + x * cell, kitY, origin.z + z * cell),
                        alongX: true, fill, $"Wall_S_{x}_{z}");
                }

                if (x == width - 1 && _wallE[x, z])
                {
                    SpawnKitWallRun(
                        new Vector3(origin.x + (x + 1) * cell, kitY, origin.z + z * cell),
                        alongX: false, fill, $"Wall_E_{x}_{z}");
                }

                if (z == height - 1 && _wallN[x, z])
                {
                    SpawnKitWallRun(
                        new Vector3(origin.x + x * cell, kitY, origin.z + (z + 1) * cell),
                        alongX: true, fill, $"Wall_N_{x}_{z}");
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
    /// </summary>
    private void SpawnKitWallRun(Vector3 a, bool alongX, int[] fill, string namePrefix)
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

        Vector2Int aiCell = FarthestCell();
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);
        HashSet<Vector2Int> starCellSet = new HashSet<Vector2Int>(_starCells);

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

        System.Random rng = new System.Random(usedSeed + 5);
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

        Material shardMaterial = BuildShardMaterial();
        AudioClip chime = BuildShardChimeClip();

        foreach (Vector2Int cell in chosen)
        {
            BuildShard(cell, rng, shardMaterial, chime);
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
    /// glassy cube it is built from is a scaled child instead of the root itself.
    /// </summary>
    private void BuildShard(Vector2Int cell, System.Random rng, Material material, AudioClip chime)
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
        if (options.Count == 0) return; // every real cell has at least one wall

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

        director.Bind(FindPlayer(), aiFollower, stingClip, droneClip, panicClip);

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

        if (stealth != null) stealth.SetLamps(_lamps);
        if (_profile != null && stamina != null) stamina.SetSprintSeconds(_profile.SprintSeconds);

        foreach (Locker locker in _lockers)
        {
            locker.Bind(player, stealth, flashlight, hud);
        }

        // Must exist before MazeEscape.Configure receives it
        GameOutcome outcome = FindAnyObjectByType<GameOutcome>();
        if (outcome == null) outcome = gameObject.AddComponent<GameOutcome>();

        // Tier 3: relocation, phantoms, F27's beats and the subtitles they all share. Both need
        // `outcome` for IsEnding, which is why they are wired here rather than beside TensionDirector.
        AIPresence presence = aiFollower != null ? aiFollower.GetComponent<AIPresence>() : null;
        Camera camera = rig != null ? rig.PlayerCamera : null;

        DreadDirector dread = FindAnyObjectByType<DreadDirector>();
        if (dread == null) dread = gameObject.AddComponent<DreadDirector>();
        dread.Configure(this, player, camera, aiFollower, presence, director, atmosphere, flashlight, stealth, hud, outcome, _profile);

        PhantomDirector phantoms = FindAnyObjectByType<PhantomDirector>();
        if (phantoms == null) phantoms = gameObject.AddComponent<PhantomDirector>();
        Material phantomWallMat = _theme != null && _theme.WallMaterial != null ? _theme.WallMaterial : _wallMat;
        phantoms.Configure(this, player, camera, aiFollower, director, flashlight, stealth, outcome, dread, phantomWallMat, FindStarMaterial(), FindPlayerFootsteps(), _profile);

        // Tier 4: the room between floors. Unlike everything else here it is authored in the scene
        // (LIGHTS OUT > Build Shop Room generates it once), so it can be inspected and edited in the
        // Editor. Only its panel is runtime UI.
        ShopMenu shopMenu = FindAnyObjectByType<ShopMenu>();
        if (shopMenu == null) shopMenu = gameObject.AddComponent<ShopMenu>();
        shopMenu.Configure(player, hud, flashlight);

        ShopRoom shop = FindAnyObjectByType<ShopRoom>();
        if (shop != null && !shop.IsComplete)
        {
            Debug.LogError("MazeGenerator: the ShopRoom in the scene has no Arrival Point or zones. Run LIGHTS OUT > Build Shop Room in the Editor.", shop);
            shop = null;
        }
        else if (shop == null)
        {
            Debug.LogError("MazeGenerator: there is no ShopRoom in the scene, so clearing floors 1-4 will end the run as a win. Run LIGHTS OUT > Build Shop Room in the Editor, then save the scene.", this);
        }
        if (shop != null) shop.Configure(player, hud, shopMenu);

        MazeEscape escape = null;
        if (escapeSequence)
        {
            escape = FindAnyObjectByType<MazeEscape>();
            if (escape == null) escape = gameObject.AddComponent<MazeEscape>();

            escape.Configure(this, player, FindStarMaterial(), outcome);
            if (_profile != null) escape.SetEscapeSeconds(_profile.EscapeSeconds);
        }

        outcome.Configure(player, aiFollower, director, escape, flashlight, UsedSeed);
        outcome.BindShop(shop, this, hud, stealth);

        MainMenu menu = null;
        if (mainMenu)
        {
            menu = FindAnyObjectByType<MainMenu>();
            if (menu == null) menu = gameObject.AddComponent<MainMenu>();

            menu.Configure(player);
        }
        else
        {
            // No title screen means nothing calls MainMenu.StartGame, which is what starts the run
            GameFlow.IsRunActive = true;
            GameFlow.RunStartTime = Time.time;
        }

        PauseMenu pause = FindAnyObjectByType<PauseMenu>();
        if (pause == null) pause = gameObject.AddComponent<PauseMenu>();

        pause.Configure(player, flashlight, menu, outcome, UsedSeed, shopMenu);

        ConsumableController items = FindAnyObjectByType<ConsumableController>();
        if (items == null) items = gameObject.AddComponent<ConsumableController>();
        items.Configure(player, flashlight, stealth, this, hud, escape, outcome, menu, pause);

        // Campaign cosmetics (F35), applied on every scene build so the choice survives a reload.
        if (flashlight != null) flashlight.SetBeamColor(ShopCatalogue.TorchColours[PlayerInventory.TorchColourIndex].Colour);
        if (hud != null) hud.SetTint(ShopCatalogue.HudTints[PlayerInventory.HudTintIndex].Colour);
        tension.SetCalmColor(ShopCatalogue.HudTints[PlayerInventory.HudTintIndex].Colour);
        if (_profile != null && stealth != null) stealth.NoiseScale = _profile.NoiseScale;

        // Debug affordance: open the floor as if the hatch had just been reached.
        if (debugStartInShop) StartCoroutine(DebugEnterShop(outcome));
    }

    private static IEnumerator DebugEnterShop(GameOutcome outcome)
    {
        yield return null;
        outcome.FloorCleared();
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

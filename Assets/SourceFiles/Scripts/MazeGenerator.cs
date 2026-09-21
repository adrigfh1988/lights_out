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
    [Tooltip("For testing: force a floor from 1 to 5 without playing up to it. 0 = follow the game.")]
    [SerializeField] private int debugFloor = 0;

    [Header("Lockers")]
    [SerializeField] private int lockerCount = 10;
    [Tooltip("Width across the corridor (m)")]
    [SerializeField] private float lockerWidth = 1.1f;
    [Tooltip("Depth out from the back wall (m). Keep <= 0.7 so the cell centre stays reachable for the 0.5 m agent.")]
    [SerializeField] private float lockerDepth = 0.6f;
    [SerializeField] private float lockerHeight = 2.1f;
    [SerializeField] private Color lockerTint = new Color(0.16f, 0.17f, 0.19f);

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

    /// <summary>The spawned stars. Entries become null as they are collected.</summary>
    public IReadOnlyList<Transform> Stars => _stars;

    /// <summary>Every locker built for this maze.</summary>
    public IReadOnlyList<Locker> Lockers => _lockers;

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

    private void Awake()
    {
        if (width < 2 || height < 2)
        {
            Debug.LogError("MazeGenerator needs at least a 2x2 grid.", this);
            return;
        }

        ApplyFloorProfile();

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

        Generate(usedSeed);
        CacheCellCenters();
        ChooseLockerCells(new System.Random(usedSeed + 3));
        BuildGeometry();
        BuildLockers();

        // Bake before anything is placed inside the volume: the surface collects render meshes on every
        // layer, so a star, a ceiling or a robot standing in the maze would be carved out of the navmesh.
        // Lockers are built above, before this line, so their bodies are carved out too and the hunter
        // paths around them instead of through them.
        BuildRuntimeNavMesh();

        BuildCeiling();
        BuildWallLamps();
        DisableExistingPickups();
        SpawnStars(usedSeed);
        PlacePlayer();
        PlaceAI();
        SetUpAtmosphere();
    }

    /// <summary>
    /// Picks this run's floor and overwrites the world-size and lamp fields with its values. The
    /// hunter, light and timers are handed their values later, where each is wired up. Runs first in
    /// Awake, before anything reads width, height, starCount, lockerCount or the lamp settings.
    /// </summary>
    private void ApplyFloorProfile()
    {
        _profile = null;
        if (!useFloorProfile) return;

        // A debug floor is written back so the HUD, end screens and Next Floor all agree with it
        if (debugFloor > 0) GameFlow.CurrentFloor = Mathf.Clamp(debugFloor, 1, FloorProfile.FinalFloor);

        _profile = FloorProfile.For(GameFlow.CurrentFloor);

        width = _profile.Width;
        height = _profile.Height;
        starCount = _profile.StarCount;
        lockerCount = _profile.LockerCount;
        cellsPerLamp = _profile.CellsPerLamp;
        lampIntensity = _profile.LampIntensity;
        lampRange = _profile.LampRange;

        Debug.Log($"MazeGenerator: {_profile}", this);
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
        int x = cell.x;
        int z = cell.y;
        Vector3 cellCenter = CellCenter(x, z);

        // The locker stands against the wall chosen in ChooseLockerCells and its door faces the
        // middle of the hall, i.e. away from that wall.
        Vector3 openDir = -_lockerWall[cell];

        GameObject root = new GameObject($"Locker_{x}_{z}");
        root.transform.SetParent(_mazeRoot, false);
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

        Material sharedMaterial = BuildLampMaterial();
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

    private void BuildWallLamp(Vector2Int cell, bool isLockerCell, Material material, System.Random rng)
    {
        int x = cell.x;
        int z = cell.y;
        Vector3 cellCenter = CellCenter(x, z);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;

        // Direction from the cell centre toward the wall the lamp mounts on.
        Vector3 wallDirection;

        if (isLockerCell)
        {
            // Above the locker: the same back wall the locker sits against.
            wallDirection = _lockerWall[cell];
        }
        else
        {
            List<Vector3> options = new List<Vector3>();
            if (_wallN[x, z]) options.Add(Vector3.forward);
            if (_wallE[x, z]) options.Add(Vector3.right);
            if (_wallS[x, z]) options.Add(Vector3.back);
            if (_wallW[x, z]) options.Add(Vector3.left);
            if (options.Count == 0) return; // every real cell has at least one wall
            wallDirection = options[rng.Next(options.Count)];
        }

        Vector3 position = cellCenter + wallDirection * (backFaceDistance - 0.08f) + Vector3.up * lampHeight;

        GameObject fixture = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fixture.name = "WallLamp";
        fixture.layer = 0;
        fixture.transform.SetParent(_mazeRoot, false);
        fixture.transform.position = position;
        fixture.transform.rotation = Quaternion.LookRotation(-wallDirection, Vector3.up);
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
        lamp.Configure(light, fixtureRenderer, lampIntensity, faulty, rng.Next(1000));
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

        _wallMat = DarkCopy(wallMaterial, wallTint, "Maze_Wall");
        _floorMat = DarkCopy(floorMaterial, floorTint, "Maze_Floor");
        _ceilingMat = DarkCopy(wallMaterial, ceilingTint, "Maze_Ceiling");

        float spanX = width * cellSize + wallThickness;
        float spanZ = height * cellSize + wallThickness;

        // Floor: 0.2 thick, top exactly at FloorTop so the walls and the player stand on it.
        CreateBox(
            "Floor",
            new Vector3(origin.x + width * cellSize * 0.5f, FloorTop - 0.1f, origin.z + height * cellSize * 0.5f),
            new Vector3(spanX, 0.2f, spanZ),
            _floorMat);

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
                        _wallMat);
                }

                if (_wallS[x, z])
                {
                    CreateBox(
                        $"Wall_S_{x}_{z}",
                        new Vector3(origin.x + (x + 0.5f) * cellSize, wallCenterY, origin.z + z * cellSize),
                        new Vector3(cellSize + wallThickness, wallHeight, wallThickness),
                        _wallMat);
                }

                if (x == width - 1 && _wallE[x, z])
                {
                    CreateBox(
                        $"Wall_E_{x}_{z}",
                        new Vector3(origin.x + (x + 1) * cellSize, wallCenterY, origin.z + (z + 0.5f) * cellSize),
                        new Vector3(wallThickness, wallHeight, cellSize + wallThickness),
                        _wallMat);
                }

                if (z == height - 1 && _wallN[x, z])
                {
                    CreateBox(
                        $"Wall_N_{x}_{z}",
                        new Vector3(origin.x + (x + 0.5f) * cellSize, wallCenterY, origin.z + (z + 1) * cellSize),
                        new Vector3(cellSize + wallThickness, wallHeight, wallThickness),
                        _wallMat);
                }
            }
        }
    }

    /// <summary>
    /// Built after the navmesh bake, so there is never a question of whether the slab reads as a
    /// walkable surface. A scaled cube rather than a quad or plane: it has a real underside.
    /// </summary>
    private void BuildCeiling()
    {
        if (!buildCeiling) return;

        CreateBox(
            "Ceiling",
            new Vector3(
                origin.x + width * cellSize * 0.5f,
                FloorTop + wallHeight + 0.1f,
                origin.z + height * cellSize * 0.5f),
            new Vector3(width * cellSize + wallThickness, 0.2f, height * cellSize + wallThickness),
            _ceilingMat);
    }

    private void CreateBox(string boxName, Vector3 center, Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = boxName;
        // Layer 0 is what the player's GroundLayers mask and the camera's obstacle filter look at.
        box.layer = 0;
        box.transform.SetParent(_mazeRoot, false);
        box.transform.position = center;
        box.transform.localScale = size;

        if (material != null)
        {
            // sharedMaterial: one instance for the whole maze instead of one per cube
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
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

        if (chosen.Count < starCount)
        {
            Debug.LogWarning($"MazeGenerator: only {chosen.Count} of {starCount} stars fit in the maze.", this);
        }
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

        // Facing the middle of the maze from the corner cell.
        const float startYaw = 45f;

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
        if (darkness)
        {
            HorrorAtmosphere atmosphere = FindAnyObjectByType<HorrorAtmosphere>();
            if (atmosphere == null) atmosphere = gameObject.AddComponent<HorrorAtmosphere>();
            if (_profile != null) atmosphere.ApplyProfile(_profile.Ambient, _profile.FogDensity);
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

        if (stealth != null) stealth.SetLamps(_lamps);
        if (_profile != null && stamina != null) stamina.SetSprintSeconds(_profile.SprintSeconds);

        foreach (Locker locker in _lockers)
        {
            locker.Bind(player, stealth, flashlight, hud);
        }

        // Must exist before MazeEscape.Configure receives it
        GameOutcome outcome = FindAnyObjectByType<GameOutcome>();
        if (outcome == null) outcome = gameObject.AddComponent<GameOutcome>();

        MazeEscape escape = null;
        if (escapeSequence)
        {
            escape = FindAnyObjectByType<MazeEscape>();
            if (escape == null) escape = gameObject.AddComponent<MazeEscape>();

            escape.Configure(this, player, FindStarMaterial(), outcome);
            if (_profile != null) escape.SetEscapeSeconds(_profile.EscapeSeconds);
        }

        outcome.Configure(player, aiFollower, director, escape, flashlight, UsedSeed);

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

        pause.Configure(player, flashlight, menu, outcome, UsedSeed);
    }

    // ---------------------------------------------------------------- helpers

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

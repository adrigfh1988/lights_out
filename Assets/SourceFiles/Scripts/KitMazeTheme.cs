using UnityEngine;

/// <summary>
/// F48: the Maze Modular Puzzle Kit pieces MazeGenerator assembles when GameFlow.UseKitMaze is set.
/// Authored in the scene by LIGHTS OUT &gt; Build Kit Maze Theme (Assets/Editor/KitMazeThemeBuilder.cs);
/// holds prefab *asset* references loaded from Assets/Maze/Prefabs, not live instances - MazeGenerator
/// clones these directly, unlike the FloorThemes gallery which clones scene instances.
/// </summary>
public class KitMazeTheme : MonoBehaviour
{
    [Header("Walls")]
    [SerializeField] private GameObject wall1M;
    [SerializeField] private GameObject wall2M;
    [SerializeField] private GameObject wall3M;

    [Header("Pillar")]
    [SerializeField] private GameObject pillar;

    [Header("Floors")]
    [SerializeField] private GameObject floor1M;
    [SerializeField] private GameObject floor2M;
    [SerializeField] private GameObject floor3M;

    [Header("Dressing pieces (F70)")]
    [Tooltip("Wall_Lamp: plaque lamp, no Light of its own - MazeGenerator adds a child point Light, same as the primitive lamp branch.")]
    [SerializeField] private GameObject wallLamp;
    [Tooltip("Wall_Light: 3 m emissive overlay for a Wall_3M face. No Light component (decision 10) - it is glowing geometry only.")]
    [SerializeField] private GameObject wallLight;
    [Tooltip("Wall_Door: barred gate, drop-in replacement for a 3 m wall piece.")]
    [SerializeField] private GameObject wallDoor;
    [Tooltip("Switch: floor lever, wrapped as a Floor PropPiece by the builder.")]
    [SerializeField] private GameObject switchLever;
    [Tooltip("Tree_1..4_V1/V2, scaled down at runtime to fit under the kit ceiling.")]
    [SerializeField] private GameObject[] trees;

    [Header("Common dressing (F70)")]
    [Tooltip("The shared Themes/Common prop set (decals, floor clutter, switch lever) - the kit maze gets no theme wall/ceiling props (decision 7).")]
    [SerializeField] private PropPiece[] props;
    [Tooltip("The shared set-piece set (just FallenRunner in kit mode).")]
    [SerializeField] private SetPiece[] setPieces;
    [Tooltip("Scene instance of the common dust prefab, cloned once per run.")]
    [SerializeField] private DustMotes dust;

    [Header("Common dressing chances (F70)")]
    [Range(0f, 1f)]
    [SerializeField] private float floorPropChance = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float wallDecalChance = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float floorDecalChance = 0f;
    [SerializeField] private int maxDecalsPerCell = 0;
    [SerializeField] private int setPieceCount = 0;
    [Tooltip("Chance a still-standing (non-gate) 3 m wall piece gets a Wall_Light overlay on one face.")]
    [Range(0f, 1f)]
    [SerializeField] private float wallLightChance = 0f;
    [Tooltip("How many perimeter wall segments become barred Wall_Door gates.")]
    [SerializeField] private int gateCount = 0;
    [Tooltip("How many L-corner cells get a scaled-down tree.")]
    [SerializeField] private int treeCount = 0;

    public GameObject Wall1M => wall1M;
    public GameObject Wall2M => wall2M;
    public GameObject Wall3M => wall3M;
    public GameObject Pillar => pillar;
    public GameObject Floor1M => floor1M;
    public GameObject Floor2M => floor2M;
    public GameObject Floor3M => floor3M;

    public GameObject WallLamp => wallLamp;
    public GameObject WallLight => wallLight;
    public GameObject WallDoor => wallDoor;
    public GameObject SwitchLever => switchLever;
    public GameObject[] Trees => trees;

    public PropPiece[] Props => props;
    public SetPiece[] SetPieces => setPieces;
    public int SetPieceCount => setPieceCount;
    public DustMotes Dust => dust;

    public float FloorPropChance => floorPropChance;
    public float WallDecalChance => wallDecalChance;
    public float FloorDecalChance => floorDecalChance;
    public int MaxDecalsPerCell => maxDecalsPerCell;
    public float WallLightChance => wallLightChance;
    public int GateCount => gateCount;
    public int TreeCount => treeCount;

    /// <summary>True only if all seven structural pieces are assigned. Dressing (props, set pieces, gates, trees, dust) is optional - MazeGenerator's null/empty checks skip whatever is missing.</summary>
    public bool IsComplete =>
        wall1M != null && wall2M != null && wall3M != null &&
        pillar != null &&
        floor1M != null && floor2M != null && floor3M != null;

    /// <summary>The wall prefab for a 1/2/3 m span. Length must be 1, 2 or 3 - callers (FillSpan) never ask for anything else.</summary>
    public GameObject WallFor(int length)
    {
        switch (length)
        {
            case 1: return wall1M;
            case 2: return wall2M;
            case 3: return wall3M;
            default: return null;
        }
    }

    /// <summary>The square floor tile prefab for a 1/2/3 m side. Side must be 1, 2 or 3 - callers (TileFloorRect) never ask for anything else.</summary>
    public GameObject FloorFor(int side)
    {
        switch (side)
        {
            case 1: return floor1M;
            case 2: return floor2M;
            case 3: return floor3M;
            default: return null;
        }
    }
}

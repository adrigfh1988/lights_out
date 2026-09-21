using UnityEngine;

/// <summary>
/// One floor's look, authored as a row of real prefab instances under FloorThemes in the scene (built by
/// LIGHTS OUT &gt; Build Floor Themes). Owns everything that is a matter of taste: palette, fog colour,
/// lamp colour, prop density, and which prefab MazeGenerator clones for each piece of the maze.
///
/// FloorProfile owns the opposite half - everything that affects play (lamp intensity/range, lamp count,
/// fog density, world size). MazeGenerator overwrites a cloned lamp's Light.intensity/range from the
/// profile every time, so the artist here is free to repaint without touching difficulty.
/// </summary>
public class FloorTheme : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private string displayName = "Untitled Theme";

    [Header("Light")]
    [SerializeField] private Color ambient = new Color(0.06f, 0.06f, 0.075f);
    [SerializeField] private Color fogColor = new Color(0.01f, 0.01f, 0.015f);
    [Tooltip("Multiplies FloorProfile.FogDensity")]
    [SerializeField] private float fogDensityScale = 1f;
    [SerializeField] private Color lampColor = new Color(1f, 0.72f, 0.42f);
    [Range(0f, 1f)]
    [SerializeField] private float faultyLampChance = 0.25f;

    [Header("Pieces")]
    [SerializeField] private WallPiece wall;
    [SerializeField] private GameObject pillar;
    [SerializeField] private GameObject floorTile;
    [SerializeField] private GameObject ceilingTile;
    [SerializeField] private WallLamp lamp;
    [SerializeField] private Locker locker;
    [SerializeField] private PropPiece[] props;

    [Header("Props")]
    [Tooltip("Chance an eligible cell gets a wall prop")]
    [Range(0f, 1f)]
    [SerializeField] private float wallPropChance = 0.35f;
    [Tooltip("Chance an eligible cell gets a ceiling prop")]
    [Range(0f, 1f)]
    [SerializeField] private float ceilingPropChance = 0.15f;

    [Header("Materials")]
    [Tooltip("Handed to PhantomDirector for its silhouette material")]
    [SerializeField] private Material wallMaterial;

    public string DisplayName => displayName;
    public Color Ambient => ambient;
    public Color FogColor => fogColor;
    public float FogDensityScale => fogDensityScale;
    public Color LampColor => lampColor;
    public float FaultyLampChance => faultyLampChance;

    public WallPiece Wall => wall;
    public GameObject Pillar => pillar;
    public GameObject FloorTile => floorTile;
    public GameObject CeilingTile => ceilingTile;
    public WallLamp Lamp => lamp;
    public Locker Locker => locker;
    public PropPiece[] Props => props;

    public float WallPropChance => wallPropChance;
    public float CeilingPropChance => ceilingPropChance;
    public Material WallMaterial => wallMaterial;

    /// <summary>The pieces every floor must have. Props may be empty - a theme with no props just builds none.</summary>
    public bool IsComplete =>
        wall != null && pillar != null && floorTile != null && ceilingTile != null && lamp != null && locker != null;
}

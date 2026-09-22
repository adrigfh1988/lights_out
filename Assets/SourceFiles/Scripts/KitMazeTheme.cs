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

    public GameObject Wall1M => wall1M;
    public GameObject Wall2M => wall2M;
    public GameObject Wall3M => wall3M;
    public GameObject Pillar => pillar;
    public GameObject Floor1M => floor1M;
    public GameObject Floor2M => floor2M;
    public GameObject Floor3M => floor3M;

    /// <summary>True only if all seven pieces are assigned.</summary>
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

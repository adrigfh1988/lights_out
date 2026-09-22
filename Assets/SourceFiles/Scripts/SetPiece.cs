using UnityEngine;

/// <summary>
/// Marks a prefab root as a themed "set piece" - a small vignette (an overturned gurney, a shrine, a
/// fallen body) rather than a single prop, built by MazeGenerator.BuildSetPieces before the navmesh bake
/// so its body colliders carve the navmesh like a locker does. Carries its own decals and, optionally,
/// one FlickerLight as children; MazeGenerator only reads the numbers below to decide whether it fits a
/// candidate cell and how to weight the pick among a theme's other set pieces.
///
/// Root convention is the same as a wall prop: pivot on the floor at the wall face, local +Z points into
/// the corridor. Width runs along the wall (local X) and must fit within cellSize - 1.4 so the piece
/// never spans into a neighbouring cell's doorway; Depth is how far the main body's collider intrudes
/// into the corridor and must stay &lt;= 0.9 m so the hunter and the player both fit past it.
/// </summary>
public class SetPiece : MonoBehaviour
{
    [Tooltip("Footprint along the wall (m). Must fit within cellSize - 1.4 or MazeGenerator skips this piece on that floor.")]
    [SerializeField] private float width = 2f;

    [Tooltip("How far the main body's collider intrudes into the corridor (m), keep <= 0.9.")]
    [SerializeField] private float depth = 0.7f;

    [Tooltip("Relative pick weight among this theme's other set pieces.")]
    [SerializeField] private float weight = 1f;

    [Tooltip("Keep off to retire a set piece without deleting it. The artist can turn it back on.")]
    [SerializeField] private bool enabledInMaze = true;

    public float Width => width;
    public float Depth => depth;
    public float Weight => weight;
    public bool EnabledInMaze => enabledInMaze;
}

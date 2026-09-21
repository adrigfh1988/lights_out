using UnityEngine;

/// <summary>
/// Marks a prefab root as a themed set-dressing prop and carries the numbers MazeGenerator.BuildWallProps
/// / BuildCeilingProps need to place and pick it: which surface it mounts on, how far it intrudes, its
/// pick weight among the theme's other props of the same mount kind, and whether it is allowed to spawn
/// in the maze at all.
///
/// Root conventions (for the artist). Wall prop: pivot on the floor at the wall face, local +Z points
/// into the corridor; children extend along +Z by at most Depth (keep &lt;= 0.7 m, the same rule as a
/// locker, so the hunter and the player both fit past it). Ceiling prop: pivot at the ceiling underside,
/// local -Y hangs down by at most Depth (keep &lt;= wallHeight - 2.2 m so nothing brushes the player's
/// head). Only the main body should keep a collider - it is what carves the navmesh and blocks the
/// player; a collider on thin dressing is wasted physics.
/// </summary>
public class PropPiece : MonoBehaviour
{
    public enum MountKind
    {
        Wall,
        Ceiling
    }

    [SerializeField] private MountKind mount = MountKind.Wall;

    [Tooltip("Wall: depth out from the wall face (m), keep <= 0.7. Ceiling: drop below the ceiling (m), keep <= wallHeight - 2.2.")]
    [SerializeField] private float depth = 0.5f;

    [Tooltip("Relative pick weight among this theme's props of the same mount kind.")]
    [SerializeField] private float weight = 1f;

    [Tooltip("Keep off by default for things that could be mistaken for the hunter, e.g. a standing silhouette. The artist can turn a disabled prop back on.")]
    [SerializeField] private bool enabledInMaze = true;

    public MountKind Mount => mount;
    public float Depth => depth;
    public float Weight => weight;
    public bool EnabledInMaze => enabledInMaze;
}

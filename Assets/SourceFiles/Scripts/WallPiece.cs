using UnityEngine;

/// <summary>
/// Marks a prefab root as a themed wall piece and tells MazeGenerator how big it was authored, so the
/// generator can scale the root to fit whatever wall span it needs (cellSize + wallThickness long,
/// wallHeight tall, wallThickness deep).
///
/// Root convention (for the artist): pivot on the floor at the centre of the wall's footprint. A
/// "Body" child cube sits at local (0, 2, 0) with scale (5, 4, 0.5) and keeps its BoxCollider - walls
/// must block the player and the hunter's line-of-sight raycasts. Every other child is dressing
/// (dado rails, pipes, rivets, ...) and should not carry a collider.
/// </summary>
public class WallPiece : MonoBehaviour
{
    [Tooltip("Authored length along local X (m). The generator scales the root by actual/authored per axis.")]
    [SerializeField] private float authoredLength = 5f;
    [Tooltip("Authored height along local Y (m), root sits at floor level.")]
    [SerializeField] private float authoredHeight = 4f;
    [Tooltip("Authored thickness along local Z (m).")]
    [SerializeField] private float authoredThickness = 0.5f;

    /// <summary>Local scale that stretches this piece to fill a wall span of the given world size.</summary>
    public Vector3 ScaleFor(float length, float height, float thickness)
    {
        return new Vector3(
            authoredLength > 0f ? length / authoredLength : 1f,
            authoredHeight > 0f ? height / authoredHeight : 1f,
            authoredThickness > 0f ? thickness / authoredThickness : 1f);
    }
}

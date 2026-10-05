using System;
using UnityEngine;

/// <summary>
/// A non-star objective marker (F73) - a fuse box, and later slices' own gated objectives. Deliberately
/// not a Pickup: GameManager.Start counts Pickup objects as stars, so one built on that base would
/// corrupt the star count and could fire AllStarsCollected early (see CLAUDE.md). GameManager instead
/// counts these alongside Pickups so a Blackout floor's fuses gate the hatch exactly like its stars do.
///
/// The GameObject that owns one keeps existing after Complete() - a fuse box does not vanish, it just
/// turns its indicator green - so this class never destroys anything, only raises the event once.
/// </summary>
public class ObjectiveToken : MonoBehaviour
{
    /// <summary>Raised once, with the token's world position, the first time Complete() is called.</summary>
    public static event Action<Vector3> Completed;

    private bool _done;

    /// <summary>True once this token has fired Completed.</summary>
    public bool IsDone => _done;

    /// <summary>Idempotent - a second call is a no-op, so a hold interactable that somehow re-fires cannot double-count.</summary>
    public void Complete()
    {
        if (_done) return;
        _done = true;
        Completed?.Invoke(transform.position);
    }
}

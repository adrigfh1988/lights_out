using StarterAssets;
using UnityEngine;

/// <summary>
/// Freezing the player is three flags, not one. Shared by the title screen, the pause menu and the
/// end screens.
/// </summary>
public static class PlayerLock
{
    public static void Freeze(Transform player, bool frozen)
    {
        if (player == null) return;

        ThirdPersonController controller = player.GetComponent<ThirdPersonController>();
        if (controller != null)
        {
            // A locker also locks movement, and Freeze(false) on Resume must not undo that -
            // otherwise pausing and resuming while hidden would let the player walk out of the locker.
            PlayerStealthState stealth = player.GetComponent<PlayerStealthState>();
            controller.MovementLocked = frozen || (stealth != null && stealth.Hidden);
            // Mouse look is not scaled by deltaTime, so a zeroed clock does not stop it - without
            // this the camera spins behind a menu as the pointer moves over the buttons.
            controller.LockCameraPosition = frozen;
        }

        StarterAssetsInputs inputs = player.GetComponent<StarterAssetsInputs>();
        if (inputs != null)
        {
            // Its own focus handler reads this, so flipping it stops the fight over the cursor
            inputs.cursorLocked = !frozen;
        }
    }

    /// <summary>
    /// Anything showing a menu must call this every frame, not once: StarterAssetsInputs re-locks the
    /// cursor on every focus change, so a single assignment loses the moment the window is alt-tabbed.
    /// </summary>
    public static void SetCursorFree(bool free)
    {
        Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = free;
    }
}

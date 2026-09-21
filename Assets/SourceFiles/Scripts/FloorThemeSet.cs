using UnityEngine;

/// <summary>
/// The FloorThemes gallery root, built once by LIGHTS OUT &gt; Build Floor Themes. Holds one FloorTheme
/// row per floor; MazeGenerator.ResolveTheme finds this in the scene and asks it for the theme to clone.
/// </summary>
public class FloorThemeSet : MonoBehaviour
{
    [Tooltip("Row 0 = floor 1, row 1 = floor 2, and so on up to FloorProfile.FinalFloor.")]
    [SerializeField] private FloorTheme[] floors = new FloorTheme[FloorProfile.FinalFloor];

    /// <summary>True only if every floor has a row and every row has every required piece.</summary>
    public bool IsComplete
    {
        get
        {
            if (floors == null || floors.Length < FloorProfile.FinalFloor) return false;

            for (int i = 0; i < FloorProfile.FinalFloor; i++)
            {
                if (floors[i] == null || !floors[i].IsComplete) return false;
            }

            return true;
        }
    }

    /// <summary>The theme for a 1-based floor number, or null if there is no row for it.</summary>
    public FloorTheme ForFloor(int floor)
    {
        if (floors == null || floors.Length == 0) return null;

        int index = Mathf.Clamp(floor - 1, 0, floors.Length - 1);
        return floors[index];
    }
}

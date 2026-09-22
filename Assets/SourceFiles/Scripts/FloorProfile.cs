using UnityEngine;

/// <summary>
/// Every number that makes a floor harder, in one place. Difficulty here is not Easy/Medium/Hard, it
/// is how far into the game the player is: floor 1 is a gentle walk in a small, lit maze, and the
/// final floor keeps the pre-floors gameplay tuning for the hunter's speed, senses and timers. Its look
/// is a separate concern, assigned by <see cref="FloorThemes"/> below, and the ambush bias (F45) is new
/// behaviour on every floor, floor 5 included.
///
/// Each gameplay value is a pair, "first floor" and "final floor", blended by <see cref="Blend"/>. To make
/// the early game kinder or the late game crueller, change the pair - nothing else needs to know.
/// Plain data, no MonoBehaviour and no statics: MazeGenerator builds one and hands it out.
/// </summary>
public class FloorProfile
{
    public const int FinalFloor = 5;

    /// <summary>
    /// Names the theme numbers <see cref="FloorThemes"/> assigns from - one row per "type of floor",
    /// matching a row built by LIGHTS OUT &gt; Build Floor Themes (<c>Assets/Editor/FloorThemeBuilder.cs</c>,
    /// its <c>rowNames</c> array). Add a new type here (and a matching builder/row) to grow the list;
    /// nothing else needs to change.
    /// </summary>
    public static class Theme
    {
        public const int Ward = 1;
        public const int BoilerDeck = 2;
        public const int Crypt = 3;
        public const int Lab = 4;
        public const int Hollow = 5;
    }

    /// <summary>
    /// Floor -&gt; theme number, one entry per floor (index 0 is floor 1). A floor's theme does not have
    /// to match its own number: reassign an entry to give that floor a different type's look - e.g.
    /// <c>FloorThemes[0] = Theme.Hollow</c> gives floor 1 the Hollow's aesthetic. Adding a floor to the
    /// game means adding an entry here (and bumping <see cref="FinalFloor"/> and the gallery row count).
    /// </summary>
    private static readonly int[] FloorThemes =
    {
        Theme.Ward,       // floor 1
        Theme.BoilerDeck, // floor 2
        Theme.Crypt,      // floor 3
        Theme.Lab,        // floor 4
        Theme.Crypt,      // floor 5 - borrowed: the Hollow's purple runes read murky, not scary, and swallowed the flashlight
    };

    /// <summary>Theme number for a 1-based floor, from <see cref="FloorThemes"/>. Falls back to the floor's own number if the list doesn't cover it.</summary>
    private static int ThemeFor(int floor)
    {
        int index = floor - 1;
        return index >= 0 && index < FloorThemes.Length ? FloorThemes[index] : floor;
    }

    public int Floor { get; private set; }

    /// <summary>0 on the first floor, 1 on the final one.</summary>
    public float Progress { get; private set; }

    /// <summary>Which FloorThemeSet row the maze clones. Set from <see cref="FloorThemes"/> in For() - the themes' looks are authored in the scene; this is only the floor-to-theme mapping.</summary>
    public int ThemeIndex;

    // ---- world
    public int Width, Height;
    public int StarCount;
    public int LockerCount;
    public int CellsPerLamp;
    public float LampIntensity;
    public float LampRange;
    /// <summary>Glowing shards hidden in the maze's nooks (F31). Never set by For(); Quiet Shoes touches NoiseScale, not this.</summary>
    public int ShardCount;

    // ---- light
    public Color Ambient;
    public float FogDensity;

    // ---- the player
    public float SprintSeconds;
    public float EscapeSeconds;

    // ---- the hunter
    public float DormantSpeed;
    public float WalkSpeed;
    public float RunSpeed;
    public float ViewDistance;
    public float ViewAngle;
    public float HearingScale;
    public float LoseSightTime;
    public float PatrolBias;
    public float SearchBudget;

    // ---- dread
    /// <summary>Seconds between relocations (F23). Also floored at a hard 20 s minimum in DreadDirector.</summary>
    /// <summary>How close a relocation may appear along the player's trail at full progress (m).</summary>
    public float RelocateNearDistance;
    /// <summary>Mean seconds between phantoms (F26).</summary>
    public float PhantomInterval;

    /// <summary>Footstep noise multiplier. Never set here - only PlayerInventory.ApplyActiveModifiers (Quiet Shoes) touches it.</summary>
    public float NoiseScale = 1f;

    // ---- ambush (F45)
    /// <summary>Chance a wander leg becomes an ambush. Read raw - see AIFollower.EffectiveAmbushBias.</summary>
    public float AmbushBias;
    /// <summary>Maximum time lying in wait.</summary>
    public float AmbushWaitSeconds;
    /// <summary>Hearing multiplier while lying in wait.</summary>
    public float AmbushHearingBoost;

    public static FloorProfile For(int floor)
    {
        floor = Mathf.Clamp(floor, 1, FinalFloor);
        float t = FinalFloor > 1 ? (floor - 1) / (float)(FinalFloor - 1) : 1f;

        FloorProfile p = new FloorProfile { Floor = floor, Progress = t, ThemeIndex = ThemeFor(floor) };

        // Pairs are (first floor, final floor). The final-floor values are the shipped tuning, except the
        // stare/ambush block below, which is new on every floor.
        p.Width = Blend(7, 11, t);
        p.Height = p.Width;
        p.StarCount = Blend(2, 5, t);
        p.LockerCount = Blend(5, 10, t);
        // Final-floor lamps were 1 per 5 cells at 1.1 - too dark to read the maze once F27 has killed
        // two waves of them. Denser and a little brighter; the fog still hides the far end of a hall.
        p.CellsPerLamp = Blend(2, 3, t);
        p.LampIntensity = Blend(1.9f, 1.5f, t);
        p.LampRange = Blend(9f, 8f, t);

        float ambient = Blend(0.10f, 0.035f, t);
        p.Ambient = new Color(ambient, ambient, ambient * 1.25f);
        p.FogDensity = Blend(0.03f, 0.07f, t);

        // 3x slower drain than the original 14 -> 8 s tuning (bar-flavor unchanged, just lasts longer).
        p.SprintSeconds = Blend(42f, 24f, t);
        p.EscapeSeconds = Blend(120f, 60f, t);

        // The hunter's walking pace on floor 1 is below the player's own walk (2.0 m/s), and it does
        // not really run even once the hatch opens: 1.8 m/s "running" is a brisk walk.
        p.DormantSpeed = Blend(0.5f, 0.7f, t);
        p.WalkSpeed = Blend(1.6f, 3.5f, t);
        p.RunSpeed = Blend(1.8f, 4.5f, t);
        p.ViewDistance = Blend(9f, 18f, t);
        p.ViewAngle = Blend(80f, 110f, t);
        p.HearingScale = Blend(0.4f, 1f, t);
        p.LoseSightTime = Blend(1.2f, 3f, t);
        p.PatrolBias = Blend(0.2f, 0.6f, t);
        p.SearchBudget = Blend(6f, 12f, t);

        p.RelocateNearDistance = Blend(11f, 7f, t);
        p.PhantomInterval = Blend(45f, 18f, t);

        p.ShardCount = Blend(4, 8, t);

        p.AmbushBias = Blend(0.35f, 0.75f, t);
        p.AmbushWaitSeconds = Blend(40f, 90f, t);
        p.AmbushHearingBoost = Blend(1.5f, 1.5f, t);

        return p;
    }

    public override string ToString()
    {
        return $"floor {Floor}/{FinalFloor}: {Width}x{Height}, {StarCount} stars, {LockerCount} lockers, " +
               $"hunter walk {WalkSpeed:0.0} run {RunSpeed:0.0} sees {ViewDistance:0}m, escape {EscapeSeconds:0}s, {ShardCount} shards, " +
               $"ambush {AmbushBias:0.00}";
    }

    private static float Blend(float first, float final, float t) => Mathf.Lerp(first, final, t);

    private static int Blend(int first, int final, float t) => Mathf.RoundToInt(Mathf.Lerp(first, final, t));
}

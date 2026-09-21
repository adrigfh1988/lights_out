using UnityEngine;

/// <summary>
/// Every number that makes a floor harder, in one place. Difficulty here is not Easy/Medium/Hard, it
/// is how far into the game the player is: floor 1 is a gentle walk in a small, lit maze, and the
/// final floor keeps the pre-floors gameplay tuning for the hunter's speed, senses and timers; its look
/// comes from theme 5, and the stare/ambush pairs (F44/F45) are new behaviour on every floor, floor 5 included.
///
/// Each value is a pair, "first floor" and "final floor", blended by <see cref="Blend"/>. To make the
/// early game kinder or the late game crueller, change the pair - nothing else needs to know.
/// Plain data, no MonoBehaviour and no statics: MazeGenerator builds one and hands it out.
/// </summary>
public class FloorProfile
{
    public const int FinalFloor = 5;

    public int Floor { get; private set; }

    /// <summary>0 on the first floor, 1 on the final one.</summary>
    public float Progress { get; private set; }

    /// <summary>Which FloorThemeSet row the maze clones. The themes' looks are authored in the scene; this is only the floor-to-theme mapping.</summary>
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

    // ---- stare and ambush (F44/F45)
    /// <summary>Sight below this progress stares instead of chasing. 1 = always, 0 = never.</summary>
    public float StareCeiling;
    /// <summary>Vantage distance from the player. Must be less than ViewDistance and more than StareBreakDistance.</summary>
    public float StareDistance;
    /// <summary>Inside this, a stare becomes a chase (and a first sighting inside it is a chase).</summary>
    public float StareBreakDistance;
    /// <summary>A stare this long becomes a chase.</summary>
    public float StareCommitSeconds;
    /// <summary>Speed while watched, as a fraction of WanderSpeed(). 0 = frozen.</summary>
    public float StareWatchedSpeedScale;
    /// <summary>Speed of the rush to the vantage point. Clamped by the sprint cap in AIFollower.Start.</summary>
    public float RushSpeed;
    /// <summary>The rush gives up and stares in place after this.</summary>
    public float RushMaxSeconds;
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

        FloorProfile p = new FloorProfile { Floor = floor, Progress = t, ThemeIndex = floor };

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

        p.SprintSeconds = Blend(14f, 8f, t);
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

        // Sight below StareCeiling stares instead of chasing; floor 5 never stares, sight is a chase.
        p.StareCeiling = Blend(1.0f, 0.0f, t);
        p.StareDistance = Blend(7f, 9f, t);
        p.StareBreakDistance = Blend(3.5f, 6f, t);
        p.StareCommitSeconds = Blend(20f, 5f, t);
        p.StareWatchedSpeedScale = Blend(0f, 0.3f, t);
        p.RushSpeed = Blend(4.0f, 4.5f, t);
        p.RushMaxSeconds = Blend(3.5f, 2.5f, t);
        p.AmbushBias = Blend(0.35f, 0.75f, t);
        p.AmbushWaitSeconds = Blend(40f, 90f, t);
        p.AmbushHearingBoost = Blend(1.5f, 1.5f, t);

        return p;
    }

    public override string ToString()
    {
        return $"floor {Floor}/{FinalFloor}: {Width}x{Height}, {StarCount} stars, {LockerCount} lockers, " +
               $"hunter walk {WalkSpeed:0.0} run {RunSpeed:0.0} sees {ViewDistance:0}m, escape {EscapeSeconds:0}s, {ShardCount} shards, " +
               $"stare {StareCeiling:0.00} rush {RushSpeed:0.0} ambush {AmbushBias:0.00}";
    }

    private static float Blend(float first, float final, float t) => Mathf.Lerp(first, final, t);

    private static int Blend(int first, int final, float t) => Mathf.RoundToInt(Mathf.Lerp(first, final, t));
}

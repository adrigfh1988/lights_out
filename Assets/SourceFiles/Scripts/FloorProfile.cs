using UnityEngine;

/// <summary>
/// Every number that makes a floor harder, in one place. Difficulty here is not Easy/Medium/Hard, it
/// is how far into the game the player is: floor 1 is a gentle walk in a small, lit maze, and the
/// final floor is the game exactly as it was tuned before floors existed.
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

    // ---- world
    public int Width, Height;
    public int StarCount;
    public int LockerCount;
    public int CellsPerLamp;
    public float LampIntensity;
    public float LampRange;

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

    public static FloorProfile For(int floor)
    {
        floor = Mathf.Clamp(floor, 1, FinalFloor);
        float t = FinalFloor > 1 ? (floor - 1) / (float)(FinalFloor - 1) : 1f;

        FloorProfile p = new FloorProfile { Floor = floor, Progress = t };

        // Pairs are (first floor, final floor). The final-floor values are the shipped tuning.
        p.Width = Blend(7, 11, t);
        p.Height = p.Width;
        p.StarCount = Blend(2, 5, t);
        p.LockerCount = Blend(5, 10, t);
        p.CellsPerLamp = Blend(2, 5, t);
        p.LampIntensity = Blend(1.9f, 1.1f, t);
        p.LampRange = Blend(9f, 7f, t);

        float ambient = Blend(0.10f, 0.02f, t);
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

        return p;
    }

    public override string ToString()
    {
        return $"floor {Floor}/{FinalFloor}: {Width}x{Height}, {StarCount} stars, {LockerCount} lockers, " +
               $"hunter walk {WalkSpeed:0.0} run {RunSpeed:0.0} sees {ViewDistance:0}m, escape {EscapeSeconds:0}s";
    }

    private static float Blend(float first, float final, float t) => Mathf.Lerp(first, final, t);

    private static int Blend(int first, int final, float t) => Mathf.RoundToInt(Mathf.Lerp(first, final, t));
}

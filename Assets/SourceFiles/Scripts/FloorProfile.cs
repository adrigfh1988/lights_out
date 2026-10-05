using System.Collections.Generic;
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
/// The footprint (grid, corridor pitch, wall height) is a per-floor table, not a blend (F40 slice A).
/// Plain data, no MonoBehaviour and no statics: MazeGenerator builds one and hands it out.
/// </summary>
public class FloorProfile
{
    public const int FinalFloor = 5;

    /// <summary>
    /// A floor's grid size and corridor scale - a "kind" of floor, not a point on a first/final slope
    /// (F40 slice A). Voids (L-shapes, rings) are a later slice; this is rectangles and scale only.
    /// </summary>
    private struct Footprint
    {
        public int Width, Height;
        public float CellSize, WallHeight;

        public Footprint(int width, int height, float cellSize, float wallHeight)
        {
            Width = width;
            Height = height;
            CellSize = cellSize;
            WallHeight = wallHeight;
        }
    }

    /// <summary>
    /// One row per floor (index 0 is floor 1). Not a (first, final) pair like the gameplay fields below -
    /// each floor is its own hand-picked shape. Cell counts: 49, 60, 81, 96, 121.
    /// </summary>
    private static readonly Footprint[] Footprints =
    {
        new Footprint(7, 7, 4.5f, 4.0f),   // floor 1 - as now
        new Footprint(10, 6, 4.5f, 4.0f),  // floor 2 - long east-west halls; the maze has a far side
        new Footprint(9, 9, 4.5f, 3.2f),   // floor 3 - low ceiling; the torch lights it and the sound is close
        new Footprint(12, 8, 5.5f, 6.0f),  // floor 4 - wide, tall halls; the beam is a small cone in a big dark space
        new Footprint(11, 11, 4.5f, 4.0f), // floor 5 - as now
    };

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

    // F72: doors, buttons and vaults are per-floor tables, like Footprints, not a first/final blend -
    // each floor's mix of "how many things gate progress" is hand-picked. Floor 1's one manual door
    // teaches the mechanic before anything is locked behind it.
    private static readonly int[] ManualDoorsTable = { 1, 2, 2, 3, 3 };
    private static readonly int[] SecurityDoorsTable = { 0, 2, 1, 3, 1 };
    private static readonly int[] VaultsTable = { 0, 1, 1, 1, 1 };

    /// <summary>
    /// F77: what a floor is actually about, beyond "collect the stars", as a flag set - every floor from
    /// FirstRolledFloor on rolls MechanicsPerFloor of these three, a pure function of (UsedSeed, floor)
    /// so a Retry keeps its pair (see RollObjectives). Stars are no longer a flag: they are always
    /// present, so None means "no extra mechanic" (floor 1, the teaching floor). Lockdown means the F72
    /// door/button table plus one Timed door. Blackout means the lights start off and FuseCount fuse
    /// boxes have to be found to bring them back. KeyHunt means KeyCount brass keys have to be found
    /// before the hatch/lift will complete (see MazeEscape's padlock).
    /// </summary>
    [System.Flags]
    public enum Objective { None = 0, Lockdown = 1, Blackout = 2, KeyHunt = 4 }

    /// <summary>F77 user decision 4 Oct 2026, "only 2 objectives per floor": how many of the three mechanics a rolled floor gets.</summary>
    public const int MechanicsPerFloor = 2;
    /// <summary>First floor that rolls mechanics; floor 1 stays stars-only as the teaching floor.</summary>
    public const int FirstRolledFloor = 2;

    private static int TableFor(int[] table, int floor, int fallback)
    {
        int index = floor - 1;
        return index >= 0 && index < table.Length ? table[index] : fallback;
    }

    /// <summary>
    /// F77: picks this floor's mechanics pair, a pure function of (usedSeed, floor) salted apart from
    /// RunRules' own roll ("KEYS" = 0x4B455953), so the two never correlate. Builds every MechanicsPerFloor-
    /// sized combination of the pool from a bitmask loop (not hard-coded), shuffles it with that seed, and
    /// picks the first entry that is not the pair the previous floor rolled (GameFlow.RolledObjectives),
    /// so two consecutive floors are never identical. Writes the result back to RolledObjectives[floor].
    /// </summary>
    private static Objective RollObjectives(int floor, int usedSeed)
    {
        if (floor < FirstRolledFloor) return Objective.None;

        Objective[] singles = { Objective.Lockdown, Objective.Blackout, Objective.KeyHunt };
        List<Objective> pairs = new List<Objective>();
        int comboCount = 1 << singles.Length;
        for (int mask = 1; mask < comboCount; mask++)
        {
            int popcount = 0;
            Objective combo = Objective.None;
            for (int bit = 0; bit < singles.Length; bit++)
            {
                if ((mask & (1 << bit)) == 0) continue;
                popcount++;
                combo |= singles[bit];
            }
            if (popcount == MechanicsPerFloor) pairs.Add(combo);
        }

        int hash = RunRules.HashSeedFloor(usedSeed ^ unchecked((int)0x4B455953), floor);
        System.Random rng = new System.Random(hash);
        for (int i = pairs.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pairs[i], pairs[j]) = (pairs[j], pairs[i]);
        }

        int[] rolled = GameFlow.RolledObjectives;
        int previous = floor - 1 >= 1 && floor - 1 < rolled.Length ? rolled[floor - 1] : 0;

        Objective chosen = pairs[0];
        for (int i = 0; i < pairs.Count; i++)
        {
            if ((int)pairs[i] == previous) continue;
            chosen = pairs[i];
            break;
        }

        if (floor < rolled.Length) rolled[floor] = (int)chosen;
        return chosen;
    }

    public int Floor { get; private set; }

    /// <summary>0 on the first floor, 1 on the final one.</summary>
    public float Progress { get; private set; }

    /// <summary>Which FloorThemeSet row the maze clones. Set from <see cref="FloorThemes"/> in For() - the themes' looks are authored in the scene; this is only the floor-to-theme mapping.</summary>
    public int ThemeIndex;

    // ---- world
    public int Width, Height;
    public float CellSize;
    public float WallHeight;
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
    // ---- doors, buttons and vaults (F72)
    public int ManualDoors;
    public int SecurityDoors;
    public int Vaults;
    public bool HasTimedDoor;

    // ---- objective (F73, flags since F77)
    public Objective Objectives;
    /// <summary>True when `o` is one of this floor's rolled (or forced) mechanics.</summary>
    public bool Has(Objective o) => (Objectives & o) != 0;
    /// <summary>Blackout only; 0 on every other floor.</summary>
    public int FuseCount;
    /// <summary>KeyHunt only; 0 on every other floor.</summary>
    public int KeyCount;

    /// <summary>Chance a wander leg becomes an ambush. Read raw - see AIFollower.EffectiveAmbushBias.</summary>
    public float AmbushBias;
    /// <summary>Maximum time lying in wait.</summary>
    public float AmbushWaitSeconds;
    /// <summary>Hearing multiplier while lying in wait.</summary>
    public float AmbushHearingBoost;

    // ---- hunter temperament (F74, B3.2)
    /// <summary>One line for the floor intro banner, e.g. "It hunts by sound here." Empty on a floor with no temperament twist.</summary>
    public string TemperamentText = "";

    // ---- the Stalker (F75, decision D7)
    /// <summary>True on floors 4 and 5 - MazeGenerator.BuildStalker gates on this.</summary>
    public bool HasStalker;

    /// <summary>
    /// Base profile, no mechanics rolled (Objectives = None). F77 decision 3: only PlayerWallet.
    /// ComputeConsolation should use this overload - the consolation cap is deliberately on the base
    /// star count, not a KeyHunt/Blackout-trimmed one (slightly generous on a trimmed floor; harmless).
    /// Everything else should go through the seeded overload below.
    /// </summary>
    public static FloorProfile For(int floor) => For(floor, 0, Objective.None);

    /// <summary>
    /// F77: usedSeed makes the mechanics roll a pure function of (seed, floor), so a Retry keeps its
    /// floor's pair. `forced` overrides the roll entirely (MazeGenerator's debugObjectives) - pass null to
    /// roll normally, or Objective.None explicitly to roll nothing (see the seedless For() above).
    /// </summary>
    public static FloorProfile For(int floor, int usedSeed, Objective? forced = null)
    {
        floor = Mathf.Clamp(floor, 1, FinalFloor);
        float t = FinalFloor > 1 ? (floor - 1) / (float)(FinalFloor - 1) : 1f;

        FloorProfile p = new FloorProfile { Floor = floor, Progress = t, ThemeIndex = ThemeFor(floor) };

        // Pairs are (first floor, final floor). The final-floor values are the shipped tuning, except the
        // stare/ambush block below, which is new on every floor.
        // Footprint is a per-floor table (decision 1), not a blend - but keep the old blend as a
        // fallback so a floor missing from the table (impossible with FinalFloor = 5) still builds.
        int footprintIndex = floor - 1;
        if (footprintIndex >= 0 && footprintIndex < Footprints.Length)
        {
            Footprint footprint = Footprints[footprintIndex];
            p.Width = footprint.Width;
            p.Height = footprint.Height;
            p.CellSize = footprint.CellSize;
            p.WallHeight = footprint.WallHeight;
        }
        else
        {
            p.Width = Blend(7, 11, t);
            p.Height = p.Width;
            p.CellSize = 4.5f;
            p.WallHeight = 4.0f;
        }
        p.StarCount = Blend(2, 5, t);
        p.LockerCount = Blend(5, 10, t);

        // F77: every floor from FirstRolledFloor on rolls MechanicsPerFloor of {Lockdown, Blackout,
        // KeyHunt} from the seed (RollObjectives), or `forced` overrides it (MazeGenerator debug field).
        // Blackout and KeyHunt each trade some of the floor's stars for their own objective - the
        // lights/keys themselves are the point, so the full star count on top would run long past D1's
        // per-floor time budget. One Max(2, ...) after both trims, not one per branch.
        p.Objectives = forced ?? RollObjectives(floor, usedSeed);
        if (p.Has(Objective.Blackout))
        {
            p.FuseCount = 3;
            p.StarCount -= 2;
        }
        if (p.Has(Objective.KeyHunt))
        {
            p.KeyCount = floor <= 3 ? 2 : 3;
            p.StarCount -= 1;
        }
        p.StarCount = Mathf.Max(2, p.StarCount);
        // F71 darkness pass: lamps are sparser, dimmer and shorter-range than the old tuning, so floor 1
        // has real pockets of dark between lamp pools (the torch is optional there) and floor 5 is
        // unreadable without it. Ambient/fog follow the same curve. See
        // plannings/darkness-and-interactivity-spec.md, decision D2 and slice F71.
        p.CellsPerLamp = Blend(5, 8, t);
        p.LampIntensity = Blend(1.0f, 0.7f, t);
        p.LampRange = Blend(5.0f, 4.0f, t);

        float ambient = Blend(0.015f, 0.006f, t);
        p.Ambient = new Color(ambient, ambient, ambient * 1.25f);
        p.FogDensity = Blend(0.07f, 0.12f, t);

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

        p.ManualDoors = TableFor(ManualDoorsTable, floor, 1);
        p.SecurityDoors = TableFor(SecurityDoorsTable, floor, 0);
        p.Vaults = TableFor(VaultsTable, floor, 0);
        // F73/F77: folded into Objectives - Lockdown means the door/button table above plus one Timed door.
        p.HasTimedDoor = p.Has(Objective.Lockdown);

        p.AmbushBias = Blend(0.35f, 0.75f, t);
        p.AmbushWaitSeconds = Blend(40f, 90f, t);
        p.AmbushHearingBoost = Blend(1.5f, 1.5f, t);

        // F74 hunter temperament (B3.2): per-floor multipliers layered on top of the blend above, plus
        // the banner's one-line flavour text. Every other floor is untouched (implicit x1, no line).
        if (floor == 3)
        {
            p.ViewDistance *= 0.6f;
            p.HearingScale *= 1.5f;
            p.TemperamentText = "It hunts by sound here.";
        }
        else if (floor == 4)
        {
            p.RunSpeed *= 1.15f;
            p.LoseSightTime *= 0.6f;
            p.TemperamentText = "It is fast here, but forgets quickly.";
        }
        else
        {
            p.TemperamentText = "";
        }

        // F75, decision D7: the Stalker appears on the last two floors only - by then the hunter alone
        // has had time to establish what "the one thing chasing you" feels like.
        p.HasStalker = floor >= 4;

        return p;
    }

    public override string ToString()
    {
        return $"floor {Floor}/{FinalFloor}: {Width}x{Height}, cell {CellSize:0.0} wall {WallHeight:0.0}, {StarCount} stars, {LockerCount} lockers, " +
               $"hunter walk {WalkSpeed:0.0} run {RunSpeed:0.0} sees {ViewDistance:0}m, escape {EscapeSeconds:0}s, {ShardCount} shards, " +
               $"ambush {AmbushBias:0.00}, objectives {Objectives}";
    }

    private static float Blend(float first, float final, float t) => Mathf.Lerp(first, final, t);

    private static int Blend(int first, int final, float t) => Mathf.RoundToInt(Mathf.Lerp(first, final, t));
}

using UnityEngine;

/// <summary>
/// Scene gallery of interactable templates (F72 decision D11), built once by LIGHTS OUT &gt; Build
/// Interactables (Assets/Editor/InteractableKitBuilder.cs). MazeGenerator.BuildDoors clones these at
/// Play time exactly like FloorThemes clones its rows; the gallery itself is deactivated afterwards.
///
/// One slot group per feature slice - this is F72's. Later slices (fuse box, lantern, notes, theme
/// interactables, the stalker) add their own slots to this same component without touching these.
/// </summary>
public class InteractableKit : MonoBehaviour
{
    [Header("Doors and buttons (F72)")]
    [Tooltip("Rolling shutter: MazeDoor on the root, a Panel child that slides up, and a StatusLight child (renderer + Light).")]
    [SerializeField] private MazeDoor shutterDoor;
    [Tooltip("Wall-mounted button: WallButton on the root, a Button child (the mushroom cap), and a StatusLight child.")]
    [SerializeField] private WallButton wallButton;
    [Tooltip("Vault-only battery top-up.")]
    [SerializeField] private BatteryCellPickup batteryCell;

    [Header("Fuses (F73)")]
    [Tooltip("Wall cabinet with a lever and a red/green indicator light. Blackout-floor objective.")]
    [SerializeField] private FuseBox fuseBox;

    [Header("Throwables and noisy floors (F74)")]
    [Tooltip("Picked up, carried (up to 3), thrown with G. Shatters into a glassPatch on impact.")]
    [SerializeField] private ThrowableItem throwableBottle;
    [Tooltip("Thrown like the bottle, but clatters and stays re-pickable instead of shattering.")]
    [SerializeField] private ThrowableItem throwableCan;
    [Tooltip("Flat floor quad. Authored/placed patches use a 1.6 m trigger; a thrown bottle's shatter spawns a smaller one at runtime (see NoisySurface.SpawnGlassShatter).")]
    [SerializeField] private NoisySurface glassPatch;
    [Tooltip("Flat floor quad, quieter than glass but still louder than bare floor.")]
    [SerializeField] private NoisySurface puddle;

    [Tooltip("Optional look-alikes of the bottle (F79). MazeGenerator.BuildThrowables picks one per placed bottle by a hash of its cell; empty = every bottle uses throwableBottle. A thrown bottle always comes from throwableBottle.")]
    [SerializeField] private ThrowableItem[] bottleVariants;

    [Header("Lantern (F74)")]
    [Tooltip("Carry/place light source. Blocks sprint while carried.")]
    [SerializeField] private Lantern lantern;

    [Header("Lore notes (F74)")]
    [Tooltip("A single paper-note template; MazeGenerator.BuildLoreNotes clones it 2x per floor with a different id/text each time.")]
    [SerializeField] private LoreNote loreNote;

    [Header("Theme interactables (F74)")]
    [Tooltip("Ward theme: a remote lure.")]
    [SerializeField] private ThemeInteractable callBell;
    [Tooltip("Boiler theme: a temporary NavMeshObstacle in the next cell over.")]
    [SerializeField] private ThemeInteractable steamValve;
    [Tooltip("Crypt theme: a small permanent light, single use. Placed twice per Crypt floor.")]
    [SerializeField] private ThemeInteractable candles;
    [Tooltip("Lab theme: a brief on-screen hunter marker.")]
    [SerializeField] private ThemeInteractable terminal;

    [Header("Stalker (F75)")]
    [Tooltip("The robot body (PlayerRobot.prefab), stripped to its visuals and blackened. MazeGenerator.BuildStalker clones it on floors 4-5, adds a NavMeshAgent and warps it onto the mesh.")]
    [SerializeField] private Stalker stalker;

    [Header("Key Hunt (F77)")]
    [Tooltip("Brass key on a wall hook. KeyHunt-floor objective; MazeGenerator.BuildKeys clones it.")]
    [SerializeField] private KeyPickup keyPickup;

    [Header("Escape hatch (F80)")]
    [Tooltip("Optional. The trapdoor MazeEscape.BuildHatch clones: a child 'Glow' renderer takes the hatch's emissive material, and children 'LidL'/'LidR' are the two leaves, hinged about their local Z. Empty = the old glowing slab.")]
    [SerializeField] private GameObject escapeHatch;

    public MazeDoor ShutterDoor => shutterDoor;
    public WallButton WallButton => wallButton;
    public BatteryCellPickup BatteryCell => batteryCell;
    public FuseBox FuseBox => fuseBox;
    public ThrowableItem ThrowableBottle => throwableBottle;
    public ThrowableItem ThrowableCan => throwableCan;
    public ThrowableItem[] BottleVariants => bottleVariants;
    public NoisySurface GlassPatch => glassPatch;
    public NoisySurface Puddle => puddle;
    public Lantern Lantern => lantern;
    public LoreNote LoreNote => loreNote;
    public ThemeInteractable CallBell => callBell;
    public ThemeInteractable SteamValve => steamValve;
    public ThemeInteractable Candles => candles;
    public ThemeInteractable Terminal => terminal;
    public Stalker Stalker => stalker;
    public KeyPickup KeyPickup => keyPickup;
    public GameObject EscapeHatch => escapeHatch;

    /// <summary>True once every F72 template slot is assigned - MazeGenerator.BuildDoors logs and skips the whole feature otherwise.</summary>
    public bool HasDoorsAndButtons => shutterDoor != null && wallButton != null && batteryCell != null;

    /// <summary>True once the F73 fuse box template is assigned - MazeGenerator.BuildFuses logs and skips a Blackout floor's fuses otherwise.</summary>
    public bool HasFuseBox => fuseBox != null;

    /// <summary>True once both throwable templates and the glass patch (its shatter needs one) are assigned - MazeGenerator.BuildThrowables logs and skips otherwise.</summary>
    public bool HasThrowables => throwableBottle != null && throwableCan != null && glassPatch != null;

    /// <summary>True once both noisy-floor templates are assigned - MazeGenerator.BuildNoisySurfaces logs and skips otherwise.</summary>
    public bool HasNoisySurfaces => glassPatch != null && puddle != null;

    /// <summary>True once the lantern template is assigned - MazeGenerator.BuildLantern logs and skips otherwise.</summary>
    public bool HasLantern => lantern != null;

    /// <summary>True once the lore note template is assigned - MazeGenerator.BuildLoreNotes logs and skips otherwise.</summary>
    public bool HasLoreNote => loreNote != null;

    /// <summary>True once all four theme interactable templates are assigned - MazeGenerator.BuildThemeInteractables logs and skips otherwise.</summary>
    public bool HasThemeInteractables => callBell != null && steamValve != null && candles != null && terminal != null;

    /// <summary>True once the stalker template is assigned - MazeGenerator.BuildStalker logs and skips otherwise.</summary>
    public bool HasStalker => stalker != null;

    /// <summary>True once the key pickup template is assigned - MazeGenerator.BuildKeys logs and skips otherwise.</summary>
    public bool HasKey => keyPickup != null;
}

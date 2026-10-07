using System;
using System.Collections;
using StarterAssets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F86 The Intake: the short, safe, authored room the player wakes up in before floor 1 of a new campaign. It teaches
/// the controls by doing them (look, move, torch, focus, battery, read, throw, hide, stars and the hatch), then drops
/// through a hatch into floor 1. Like ShopRoom it is AUTHORED in the scene, built once by LIGHTS OUT &gt; Build &gt;
/// Intake Room (Assets/Editor/IntakeRoomBuilder.cs); this component only drives it.
///
/// The maze already exists behind it (MazeGenerator.Awake built everything), so while GameFlow.IsInIntake is set the
/// hunter is held (AIFollower.Update), and everything else that would hurt stays inert because it already waits for
/// GameFlow.IsRunActive, which only flips when the player leaves (GameFlow.BeginRun). Nothing here can capture the
/// player and nothing here writes a save. Entered by MainMenu.BeginNewCampaign (START GAME only - CONTINUE, Retry,
/// START NEW MAZE and NEXT FLOOR never see it); left by walking onto the hatch or PauseMenu's SKIP TUTORIAL.
/// Spec: plannings/intake-and-contracts-plan.md.
/// </summary>
public class IntakeRoom : MonoBehaviour
{
    private static readonly Color Green = new Color(0.25f, 1f, 0.7f);
    private static readonly Color Grey = new Color(0.75f, 0.75f, 0.78f);
    private static readonly Color Amber = new Color(1f, 0.72f, 0.42f);
    private static readonly Color Red = new Color(0.9f, 0.15f, 0.1f);

    /// <summary>The Intake in this scene while it is running (set by Begin, cleared on leaving / destroy). PauseMenu reads it.</summary>
    public static IntakeRoom Active { get; private set; }

    [Header("Parts (wired by the builder)")]
    [Tooltip("Where the player wakes: on the bed. Its Y rotation is the way they face.")]
    [SerializeField] private Transform arrivalPoint;
    [Tooltip("Where they stand after getting up, beside the bed.")]
    [SerializeField] private Transform standPoint;
    [Tooltip("Blocks the bed once the player is out of it (it is off while they lie in it).")]
    [SerializeField] private Collider bedBlocker;
    [Tooltip("Floor marker for the walk step. Only its XZ footprint is used.")]
    [SerializeField] private Collider moveZone;
    [Tooltip("The second room. Entering it starts the hide step.")]
    [SerializeField] private Collider roomBZone;
    [Tooltip("The hatch. Only its XZ footprint is used.")]
    [SerializeField] private Collider hatchZone;
    [SerializeField] private Light mainLamp;
    [SerializeField] private Renderer mainLampFixture;
    [SerializeField] private Light lampB;
    [SerializeField] private Light lampC;
    [Tooltip("The sign only the focused beam makes legible.")]
    [SerializeField] private TextMeshPro focusSign;
    [SerializeField] private BatteryCellPickup battery;
    [SerializeField] private ThrowableItem bottle;
    [SerializeField] private LoreNote note;
    [SerializeField] private IntakeTarget bell;
    [Tooltip("Rises when the bell rings.")]
    [SerializeField] private Transform shutter;
    [SerializeField] private Locker locker;
    [Tooltip("A black Adam on the pack's walk cycle. No AIFollower, no collider, no capture logic.")]
    [SerializeField] private Transform silhouette;
    [Tooltip("Corridor beside the hiding room, in walking order: start, the doorway where it stops and looks, end.")]
    [SerializeField] private Transform[] route;
    [SerializeField] private Transform star;
    [Tooltip("The trapdoor: LidL / LidR children hinged about their local Z, and a Glow slab.")]
    [SerializeField] private Transform hatchLidL;
    [SerializeField] private Transform hatchLidR;
    [SerializeField] private Renderer hatchGlow;
    [SerializeField] private Light hatchLight;

    [Header("Tuning")]
    [SerializeField] private float humVolume = 0.16f;
    [SerializeField] private float silhouetteSpeed = 1.0f;
    [Tooltip("How far short of the doorway the silhouette waits until the player is hidden")]
    [SerializeField] private float holdDistance = 3.5f;

    // Player systems, handed over by MazeGenerator.SetUpAtmosphere.
    private Transform _player;
    private Camera _camera;
    private PlayerHud _hud;
    private Flashlight _flashlight;
    private PlayerStealthState _stealth;
    private ThrowController _throw;
    private LoreReadingUi _reader;
    private ThirdPersonController _controller;
    private CharacterController _characterController;

    private bool _running;
    private bool _leaving;
    private Vector3 _returnPosition;
    private float _returnYaw;
    private float _cameraBaseLocalY;

    private GameObject _uiRoot;
    private TextMeshProUGUI _instruction;
    private TextMeshProUGUI[] _rows;
    private Image _blackout;
    private Coroutine _run;

    private AudioSource _hum;
    private AudioClip _humClip;
    private AudioSource _stepSource;
    private AudioSource _dragSource;
    private AudioClip _dragClip;
    private AudioClip[] _stepClips;

    private float _lidAngle;
    private ThrowableItem _bottleTemplate;
    private Vector3 _bottlePosition;
    private Quaternion _bottleRotation;

    // Hide-step state.
    private bool _silhouetteActive;
    private bool _silhouettePassed;
    private int _routeIndex;
    private float _pauseLeft = -1f;
    private float _stepDistance;

    private static readonly string[] StepNames =
    {
        "LOOK AROUND", "MOVE", "TORCH", "FOCUS", "BATTERY", "READ", "THROW", "HIDE", "STARS AND THE HATCH"
    };

    /// <summary>The torch only works once the tutorial has handed it over (step 3). PauseMenu.Resume reads it.</summary>
    public bool TorchAllowed { get; private set; }

    /// <summary>True once the room has the parts it needs to run. False = the builder has not been run.</summary>
    public bool IsComplete => arrivalPoint != null && standPoint != null && moveZone != null && roomBZone != null
        && hatchZone != null && locker != null && shutter != null && bell != null && route != null && route.Length >= 3
        && silhouette != null && star != null;

    /// <summary>Called by MazeGenerator.SetUpAtmosphere. Everything physical already exists in the scene.</summary>
    public void Configure(Transform player, Camera camera, PlayerHud hud, Flashlight flashlight, PlayerStealthState stealth,
        PlayerInteractor interactor, ThrowController throwController, LoreReadingUi reader)
    {
        _player = player;
        _camera = camera;
        _hud = hud;
        _flashlight = flashlight;
        _stealth = stealth;
        _throw = throwController;
        _reader = reader;
        _controller = player != null ? player.GetComponent<ThirdPersonController>() : null;
        _characterController = player != null ? player.GetComponent<CharacterController>() : null;
        _stepClips = _controller != null ? _controller.FootstepAudioClips : null;

        // Late-bound like every maze interactable: these were cloned into the scene before the player systems existed.
        if (battery != null) battery.BindPlayerSystems(flashlight, hud);
        if (note != null) note.Bind(reader, hud);
        if (locker != null && player != null)
        {
            Vector3 inside = locker.InsideAnchor != null ? locker.InsideAnchor.position : locker.transform.TransformPoint(new Vector3(0f, 0f, 0.35f));
            Vector3 front = locker.FrontAnchor != null ? locker.FrontAnchor.position : locker.transform.TransformPoint(new Vector3(0f, 0f, 1.6f));
            locker.Configure(inside, front, locker.transform.eulerAngles.y, locker.Door);
            locker.Bind(player, stealth, flashlight, hud, interactor);
        }
    }

    private void Awake()
    {
        _humClip = ShopRoom.BuildHumClip();
        _hum = gameObject.AddComponent<AudioSource>();
        _hum.playOnAwake = false;
        _hum.loop = true;
        _hum.spatialBlend = 0f;
        _hum.volume = humVolume;
        _hum.pitch = 0.8f;
        _hum.clip = _humClip;

        if (note != null) note.Configure(-1, NoteText);
        if (silhouette != null) silhouette.gameObject.SetActive(false);
        if (star != null) star.gameObject.SetActive(true);
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        if (_humClip != null) Destroy(_humClip);
        if (_dragClip != null) Destroy(_dragClip);
    }

    private const string NoteText =
        "TO WHOEVER WAKES NEXT\n\n" +
        "They keep the lights low down here, and something walks the halls between the beds. " +
        "Five stars lie in the dark. Gather them and the hatch will open. " +
        "It always opens. It never stays open long.\n\n" +
        "- a night nurse, Ward 1";

    // ---------------------------------------------------------------- entry / exit

    /// <summary>
    /// START GAME: teleports the player onto the bed (the maze start they came from is remembered), takes the torch and
    /// the clock's dangers away, and runs the tutorial. Called after MainMenu.CloseMenus so the clock is running.
    /// </summary>
    public void Begin()
    {
        if (_running || _player == null || !IsComplete) return;
        _running = true;
        Active = this;
        GameFlow.IsInIntake = true;

        _returnPosition = _player.position;
        _returnYaw = _player.eulerAngles.y;
        if (_camera != null) _cameraBaseLocalY = _camera.transform.localPosition.y;

        TorchAllowed = false;
        if (_flashlight != null)
        {
            _flashlight.SetOn(false);
            _flashlight.InputEnabled = false;
        }

        // Black before anything else shows, so the teleport is never seen.
        BuildUi();
        _blackout.color = Color.black;

        if (bedBlocker != null) bedBlocker.enabled = false;
        float yaw = arrivalPoint.eulerAngles.y;
        TeleportPlayer(arrivalPoint.position, yaw);
        // Movement off, mouse look on: waking up is looking around.
        if (_controller != null)
        {
            _controller.MovementLocked = true;
            _controller.LockCameraPosition = false;
            _controller.LookAt(arrivalPoint.position + Vector3.up * 3f + arrivalPoint.forward * 0.6f, 1f);
        }
        SetCameraHeight(-0.625f);

        _bottleTemplate = null;
        if (bottle != null)
        {
            _bottlePosition = bottle.transform.position;
            _bottleRotation = bottle.transform.rotation;
            _bottleTemplate = Instantiate(bottle, transform);
            _bottleTemplate.gameObject.SetActive(false);
        }

        _hum.Play();
        _run = StartCoroutine(Run());
    }

    /// <summary>PauseMenu's SKIP TUTORIAL.</summary>
    public void Skip()
    {
        if (!_running || _leaving) return;
        if (_run != null) StopCoroutine(_run);
        StartCoroutine(LeaveRoutine(false));
    }

    private IEnumerator LeaveRoutine(bool viaHatch)
    {
        _leaving = true;
        GameFlow.TutorialDone = true;

        // Whatever the player was in the middle of ends here.
        if (_reader != null) _reader.Close();
        if (locker != null && locker.Occupied) locker.ForceLeave();
        if (_hud != null) { _hud.SetPrompt(null); _hud.SetFocusPrompt(null); }
        if (_instruction != null) _instruction.text = "";
        if (_stepSource != null) _stepSource.Stop();
        if (_dragSource != null) _dragSource.Stop();
        PlayerLock.Freeze(_player, true);
        if (_flashlight != null) _flashlight.InputEnabled = false;

        if (viaHatch) yield return HatchWalkIn();
        else yield return Fade(0f, 1f, 0.5f);

        // At black: back to the maze's start cell, torch full and on exactly as floor 1 always begins.
        TeleportPlayer(_returnPosition, _returnYaw);
        SetCameraHeight(_cameraBaseLocalY);
        if (_throw != null) _throw.ClearCarried();
        if (_flashlight != null)
        {
            TorchAllowed = true;
            _flashlight.Refill();
            _flashlight.InputEnabled = true;
        }
        if (_stealth != null) _stealth.SetHidden(null);
        if (_hum != null) _hum.Stop();
        if (_uiRoot != null) _uiRoot.SetActive(false);
        if (bedBlocker != null) bedBlocker.enabled = true;

        GameFlow.IsInIntake = false;
        if (_controller != null) _controller.MovementLocked = false;
        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);
        GameFlow.BeginRun(); // FloorIntroBanner, the hunter, the dread directors and the bed music all wake on this

        yield return Fade(1f, 0f, 0.7f);
        if (_uiRoot != null) Destroy(_uiRoot);
        if (Active == this) Active = null;
        _running = false;
    }

    // ---------------------------------------------------------------- the steps

    private IEnumerator Run()
    {
        bool again = GameFlow.TutorialDone;
        yield return Fade(1f, 0f, 1.6f);
        if (_hud != null) _hud.ShowSubtitle(again ? "Welcome back. You know the way - or skip it from the pause menu." : "You wake. The bed is cold. Nobody is here.", 4f);

        // 1. Wake up: lying down, looking around, then up.
        Vector3 lastForward = _camera != null ? _camera.transform.forward : Vector3.forward;
        float turned = 0f;
        yield return Step(0, () => $"LOOK AROUND  -  {TouchInput.Key("MOUSE", "RIGHT THUMB")}", () =>
        {
            if (_camera == null) return true;
            Vector3 f = _camera.transform.forward;
            turned += Vector3.Angle(lastForward, f);
            lastForward = f;
            return turned >= 60f;
        });
        yield return StandUp();

        // 2. Move.
        yield return Step(1, () => $"WALK TO THE MARKER  -  {TouchInput.Key("W A S D", "LEFT THUMB")}", () => InZone(moveZone));

        // 3. Torch: the lamp dies, the torch is handed over.
        TorchAllowed = true;
        if (_flashlight != null) _flashlight.InputEnabled = true;
        StartCoroutine(SparkOutLamp());
        yield return Step(2, () => $"THE LIGHTS ARE GOING.  TURN ON YOUR TORCH  -  {TouchInput.Key("F", "TORCH")}", () => _flashlight == null || _flashlight.IsOn);

        // 4. Focus: the far sign is only legible in the focused beam.
        float seen = 0f;
        if (focusSign != null) SetSignAlpha(0.07f);
        yield return Step(3, () => $"READ THE SIGN BY THE SHUTTER.  HOLD {TouchInput.Key("RIGHT MOUSE", "FOCUS")} TO NARROW THE BEAM", () =>
        {
            if (focusSign == null || _flashlight == null || _camera == null) return true;
            Vector3 to = focusSign.transform.position - _camera.transform.position;
            bool aimed = _flashlight.IsOn && _flashlight.FocusBlend > 0.85f && to.magnitude < 16f
                && Vector3.Dot(_camera.transform.forward, to.normalized) > 0.95f;
            seen = aimed ? seen + Time.deltaTime : Mathf.Max(0f, seen - Time.deltaTime * 0.5f);
            SetSignAlpha(Mathf.Lerp(0.07f, 1f, Mathf.Clamp01(seen / 1.2f)));
            return seen >= 1.2f;
        });
        SetSignAlpha(1f);

        // 5. Battery.
        if (_flashlight != null) _flashlight.Drain(Mathf.Max(0f, _flashlight.Charge - 0.3f));
        yield return Step(4, () => $"YOUR TORCH IS FADING.  TAKE THE BATTERY CELL  -  {TouchInput.Key("E", "USE")}", () => battery == null);
        if (_hud != null) _hud.ShowSubtitle("Switched off, the torch slowly recharges - but never all the way.", 5f);

        // 6. Read.
        bool noteOpened = false;
        yield return Step(5, () => _reader != null && _reader.IsOpen
            ? $"CLOSE IT  -  {TouchInput.Key("E", "USE")}"
            : $"READ THE NOTE ON THE WALL  -  {TouchInput.Key("E", "USE")}", () =>
        {
            if (_reader != null && _reader.IsOpen) noteOpened = true;
            return noteOpened && (_reader == null || !_reader.IsOpen);
        });

        // 7. Throw.
        float missingFor = 0f;
        yield return Step(6, () => _throw != null && _throw.Carried > 0
            ? $"THROW IT AT THE BELL  -  {TouchInput.Key("G", "THROW")}"
            : $"PICK UP THE BOTTLE  -  {TouchInput.Key("E", "USE")}", () =>
        {
            if (bell == null) return true;
            // Missed, or the glass broke on the wall: put another bottle on the table.
            bool holding = _throw != null && _throw.Carried > 0;
            if (!holding && bottle == null && !bell.Hit)
            {
                missingFor += Time.deltaTime;
                if (missingFor > 1.2f) RespawnBottle();
            }
            else missingFor = 0f;
            return bell.Hit;
        });
        yield return OpenShutter();

        // 8. Hide.
        yield return Step(7, HideText, UpdateHide, onStart: () => ResetHide());

        // 9. Stars and the hatch.
        bool starTaken = false;
        SetHatchLook(true);
        yield return Step(8, () => starTaken
            ? $"STEP ONTO THE HATCH"
            : "TAKE THE STAR  -  WALK INTO IT", () =>
        {
            if (!starTaken)
            {
                if (star == null || FlatDistance(_player.position, star.position) < 1.2f)
                {
                    starTaken = true;
                    if (star != null) star.gameObject.SetActive(false);
                    SetHatchLook(false);
                    SwingLid(16f, 0.8f);
                    AudioSource.PlayClipAtPoint(DoorAudio.BuildClunkClip(0.3f, 70f, 0.6f), hatchZone.transform.position, 0.8f);
                    if (_hud != null)
                    {
                        _hud.ShowSubtitle(again
                            ? "Stars open the hatch. Down you go."
                            : "\"Stars open the hatch. The hatch is the way down. Something down there is hunting.\"", 6f);
                    }
                }
                return false;
            }
            return InZone(hatchZone);
        });

        StartCoroutine(LeaveRoutine(true));
    }

    /// <summary>Marks the row current, shows the live instruction, waits for done, ticks the row. A beat after each step.</summary>
    private IEnumerator Step(int index, Func<string> instruction, Func<bool> done, Action onStart = null)
    {
        SetRows(index);
        onStart?.Invoke();
        while (true)
        {
            if (_instruction != null) _instruction.text = instruction();
            if (Time.timeScale > 0f && done()) break;
            yield return null;
        }

        MarkRow(index);
        if (_instruction != null) _instruction.text = "";
        yield return new WaitForSeconds(0.7f);
    }

    private IEnumerator StandUp()
    {
        if (_hud != null) _hud.ShowSubtitle("Get up.", 2f);
        Vector3 from = _player.position;
        Vector3 to = standPoint.position;
        float startHeight = _camera != null ? _camera.transform.localPosition.y : 0f;
        float t = 0f;
        const float seconds = 2.2f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            TeleportPlayer(Vector3.Lerp(from, to, k), _player.eulerAngles.y, false);
            SetCameraHeight(Mathf.Lerp(startHeight, _cameraBaseLocalY, k));
            yield return null;
        }
        if (bedBlocker != null) bedBlocker.enabled = true;
        SetCameraHeight(_cameraBaseLocalY);
        if (_controller != null) _controller.MovementLocked = false;
    }

    private IEnumerator SparkOutLamp()
    {
        if (_hud != null) _hud.ShowSubtitle("The lamp sparks. The room goes dark.", 3f);
        AudioSource.PlayClipAtPoint(DoorAudio.BuildClunkClip(0.18f, 1600f, 0.5f), mainLamp != null ? mainLamp.transform.position : transform.position, 0.7f);
        float start = mainLamp != null ? mainLamp.intensity : 0f;
        float t = 0f;
        while (t < 1.1f)
        {
            t += Time.deltaTime;
            if (mainLamp != null) mainLamp.intensity = UnityEngine.Random.value < 0.5f ? start * UnityEngine.Random.Range(0.2f, 1.1f) : 0f;
            yield return null;
        }
        if (mainLamp != null) mainLamp.intensity = 0f;
        if (mainLampFixture != null)
        {
            Material m = mainLampFixture.material;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
        }
    }

    private IEnumerator OpenShutter()
    {
        AudioSource.PlayClipAtPoint(DoorAudio.BuildClunkClip(0.5f, 90f, 0.7f), shutter.position, 0.9f);
        if (_hud != null) _hud.ShowSubtitle("The bell rings. Somewhere, a shutter rises.", 3f);
        Vector3 start = shutter.position;
        Vector3 end = start + Vector3.up * 2.7f;
        Collider blocker = shutter.GetComponentInChildren<Collider>();
        float t = 0f;
        while (t < 1.6f)
        {
            t += Time.deltaTime;
            shutter.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t / 1.6f));
            yield return null;
        }
        shutter.position = end;
        if (blocker != null) blocker.enabled = false;
    }

    private void RespawnBottle()
    {
        if (_bottleTemplate == null) return;
        bottle = Instantiate(_bottleTemplate, _bottlePosition, _bottleRotation, transform);
        bottle.gameObject.SetActive(true);
        if (_hud != null) _hud.ShowSubtitle("Another bottle on the table.", 2.5f);
    }

    // ---------------------------------------------------------------- the hide step

    private void ResetHide()
    {
        _silhouetteActive = false;
        _silhouettePassed = false;
        _routeIndex = 1;
        _pauseLeft = -1f;
    }

    private string HideText()
    {
        if (!InZone(roomBZone) && !_silhouetteActive)
        {
            return "GO THROUGH THE SHUTTER";
        }
        if (_silhouettePassed)
        {
            return locker != null && locker.Occupied ? $"IT IS GONE.  LEAVE THE LOCKER  -  {TouchInput.Key("E", "USE")}" : "";
        }
        if (locker != null && locker.Occupied) return "STAY STILL.  DO NOT MOVE.";
        return $"SOMETHING IS COMING.  GET IN THE LOCKER  -  {TouchInput.Key("E", "USE")}";
    }

    /// <summary>
    /// Walks the silhouette past the side doorway while (and only while) the player is hidden; before that it comes
    /// close and waits outside, footsteps and all. True once it has passed and the player has climbed back out.
    /// </summary>
    private bool UpdateHide()
    {
        bool hidden = locker != null && locker.Occupied;

        if (!_silhouetteActive)
        {
            if (!InZone(roomBZone)) return false;
            _silhouetteActive = true;
            StartCoroutine(FlickerLampB());
            silhouette.gameObject.SetActive(true);
            silhouette.position = route[0].position;
            silhouette.rotation = Quaternion.LookRotation(Flat(route[1].position - route[0].position));
            EnsureSilhouetteAudio();
            _stepDistance = 0f;
            if (_hud != null) _hud.ShowSubtitle("Footsteps. Something heavy is being dragged.", 3.5f);
        }

        if (_silhouettePassed) return !hidden;

        Animator animator = silhouette.GetComponentInChildren<Animator>();
        bool moving = false;

        // Where it may walk to right now: the whole route when the player is hidden, otherwise only up to the hold point.
        Vector3 target = route[_routeIndex].position;
        // Not hidden: come no closer to the doorway than holdDistance and wait there. Past the doorway it just walks on.
        bool holding = !hidden && _routeIndex == 1 && FlatDistance(silhouette.position, route[1].position) <= holdDistance;

        if (_pauseLeft >= 0f)
        {
            // Stopped in the doorway, looking in at the room.
            if (hidden)
            {
                _pauseLeft -= Time.deltaTime;
                Vector3 look = Flat(locker.transform.position - silhouette.position);
                if (look.sqrMagnitude > 0.01f) silhouette.rotation = Quaternion.Slerp(silhouette.rotation, Quaternion.LookRotation(look), 3f * Time.deltaTime);
                if (_pauseLeft <= 0f) { _pauseLeft = -1f; _routeIndex = 2; }
            }
        }
        else if (!holding)
        {
            Vector3 step = Vector3.MoveTowards(silhouette.position, target, silhouetteSpeed * Time.deltaTime);
            Vector3 delta = step - silhouette.position;
            if (delta.sqrMagnitude > 0.000001f)
            {
                moving = true;
                silhouette.rotation = Quaternion.Slerp(silhouette.rotation, Quaternion.LookRotation(Flat(delta)), 5f * Time.deltaTime);
                silhouette.position = step;
                _stepDistance += delta.magnitude;
            }

            if (FlatDistance(silhouette.position, route[_routeIndex].position) < 0.02f)
            {
                if (_routeIndex == 1) _pauseLeft = 2.2f;
                else
                {
                    _silhouettePassed = true;
                    silhouette.gameObject.SetActive(false);
                    if (_stepSource != null) _stepSource.Stop();
                    if (_dragSource != null) _dragSource.Stop();
                }
            }
        }

        if (animator != null) animator.speed = moving ? 1f : 0f;
        if (_stepSource != null) _stepSource.transform.position = silhouette.position;
        if (_dragSource != null)
        {
            if (moving && !_dragSource.isPlaying) _dragSource.Play();
            else if (!moving && _dragSource.isPlaying) _dragSource.Pause();
            _dragSource.transform.position = silhouette.position;
        }
        if (moving && _stepDistance >= 0.8f && _stepClips != null && _stepClips.Length > 0 && _stepSource != null)
        {
            _stepDistance = 0f;
            _stepSource.pitch = UnityEngine.Random.Range(0.55f, 0.7f);
            _stepSource.PlayOneShot(_stepClips[UnityEngine.Random.Range(0, _stepClips.Length)], 0.9f);
        }

        return false;
    }

    private void EnsureSilhouetteAudio()
    {
        if (_stepSource == null)
        {
            _stepSource = silhouette.gameObject.AddComponent<AudioSource>();
            _stepSource.spatialBlend = 1f;
            _stepSource.maxDistance = 25f;
            _stepSource.rolloffMode = AudioRolloffMode.Linear;
            _stepSource.playOnAwake = false;
        }
        if (_dragSource == null)
        {
            _dragClip = BuildDragClip();
            _dragSource = silhouette.gameObject.AddComponent<AudioSource>();
            _dragSource.clip = _dragClip;
            _dragSource.loop = true;
            _dragSource.spatialBlend = 1f;
            _dragSource.maxDistance = 22f;
            _dragSource.rolloffMode = AudioRolloffMode.Linear;
            _dragSource.volume = 0.5f;
            _dragSource.playOnAwake = false;
        }
    }

    private IEnumerator FlickerLampB()
    {
        if (lampB == null) yield break;
        float baseIntensity = lampB.intensity;
        while (!_silhouettePassed && !_leaving)
        {
            lampB.intensity = UnityEngine.Random.value < 0.35f ? baseIntensity * UnityEngine.Random.Range(0.05f, 0.5f) : baseIntensity;
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.05f, 0.25f));
        }
        lampB.intensity = baseIntensity;
    }

    /// <summary>1.5 s of low filtered noise with a slow scrape in it - the pattern every synthesised clip here follows.</summary>
    private static AudioClip BuildDragClip()
    {
        const int sampleRate = 22050;
        const float duration = 2f;
        int count = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[count];
        System.Random rng = new System.Random(7);
        float low = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)sampleRate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            low += (noise - low) * 0.06f;
            float scrape = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 1.5f * t);
            samples[i] = low * scrape * 3.5f;
        }
        float peak = 0f;
        for (int i = 0; i < count; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f) for (int i = 0; i < count; i++) samples[i] *= 0.6f / peak;
        // Loop-safe ends.
        for (int i = 0; i < 400; i++)
        {
            float k = i / 400f;
            samples[i] *= k;
            samples[count - 1 - i] *= k;
        }
        AudioClip clip = AudioClip.Create("IntakeDrag", count, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // ---------------------------------------------------------------- the hatch

    private void SetHatchLook(bool locked)
    {
        Color c = locked ? Red : Green;
        if (hatchLight != null) hatchLight.color = c;
        if (hatchGlow != null)
        {
            Material m = hatchGlow.material;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * 3f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }
    }

    private void SwingLid(float degrees, float seconds)
    {
        StartCoroutine(SwingLidRoutine(degrees, seconds));
    }

    private IEnumerator SwingLidRoutine(float target, float seconds)
    {
        float start = _lidAngle;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            _lidAngle = Mathf.SmoothStep(start, target, Mathf.Clamp01(t / seconds));
            ApplyLid();
            yield return null;
        }
        _lidAngle = target;
        ApplyLid();
    }

    private void ApplyLid()
    {
        if (hatchLidL != null) hatchLidL.localRotation = Quaternion.Euler(0f, 0f, _lidAngle);
        if (hatchLidR != null) hatchLidR.localRotation = Quaternion.Euler(0f, 0f, -_lidAngle);
    }

    /// <summary>MazeEscape.EnterHatch's walk-in: the leaves open, the player eases over the opening looking down, then is lowered through it into the blackout.</summary>
    private IEnumerator HatchWalkIn()
    {
        SwingLid(100f, 0.4f);
        AudioSource.PlayClipAtPoint(DoorAudio.BuildClunkClip(0.3f, 70f, 0.6f), hatchZone.transform.position, 0.8f);

        Vector3 start = _player.position;
        Vector3 centre = hatchZone.bounds.center;
        Vector3 over = new Vector3(centre.x, start.y, centre.z);
        Vector3 lookPoint = new Vector3(centre.x, start.y - 2f, centre.z);

        float t = 0f;
        while (t < 0.75f)
        {
            t += Time.deltaTime;
            TeleportPlayer(Vector3.Lerp(start, over, Mathf.SmoothStep(0f, 1f, t / 0.75f)), _player.eulerAngles.y, false);
            if (_controller != null) _controller.LookAt(lookPoint, 1f - Mathf.Exp(-7f * Time.deltaTime));
            yield return null;
        }

        if (_controller != null) _controller.MovementLocked = true;
        t = 0f;
        const float descend = 0.6f;
        while (t < descend)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / descend);
            TeleportPlayer(over + Vector3.down * (2.2f * k * k), _player.eulerAngles.y, false);
            if (_blackout != null) _blackout.color = new Color(0f, 0f, 0f, k);
            yield return null;
        }
        if (_blackout != null) _blackout.color = Color.black;
    }

    // ---------------------------------------------------------------- helpers

    private void TeleportPlayer(Vector3 position, float yaw, bool resetCamera = true)
    {
        if (_player == null) return;
        if (_characterController != null) _characterController.enabled = false;
        _player.position = position;
        _player.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (_characterController != null) _characterController.enabled = true;
        if (resetCamera && _controller != null) _controller.ResetCameraRotation(yaw);
    }

    private void SetCameraHeight(float localY)
    {
        if (_camera == null) return;
        Vector3 p = _camera.transform.localPosition;
        p.y = localY;
        _camera.transform.localPosition = p;
    }

    private bool InZone(Collider zone)
    {
        if (zone == null || _player == null) return false;
        Bounds b = zone.bounds;
        Vector3 p = _player.position;
        return p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    private static float FlatDistance(Vector3 a, Vector3 b) => Flat(a - b).magnitude;

    private void SetSignAlpha(float alpha)
    {
        if (focusSign == null) return;
        Color c = focusSign.color;
        c.a = alpha;
        focusSign.color = c;
    }

    private IEnumerator Fade(float from, float to, float seconds)
    {
        if (_blackout == null) yield break;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            _blackout.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, Mathf.Clamp01(t / seconds)));
            yield return null;
        }
        _blackout.color = new Color(0f, 0f, 0f, to);
    }

    // ---------------------------------------------------------------- ui

    private void BuildUi()
    {
        RuntimeUi.EnsureEventSystem();
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        _uiRoot = new GameObject("IntakeUi", typeof(RectTransform));
        _uiRoot.transform.SetParent(canvas.transform, false);
        _uiRoot.layer = canvas.gameObject.layer;
        RuntimeUi.Stretch((RectTransform)_uiRoot.transform);

        // The checklist, top-left.
        GameObject panel = RuntimeUi.CreatePanel(_uiRoot.transform, "Checklist", new Color(0f, 0f, 0f, 0.45f));
        RuntimeUi.Place(panel.GetComponent<Image>().rectTransform, new Vector2(0f, 1f), new Vector2(500f, -190f), new Vector2(440f, 360f));

        TextMeshProUGUI title = RuntimeUi.CreateText(panel.transform, "Title", "THE INTAKE", 30f, Amber);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 6f;
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(420f, 40f));

        _rows = new TextMeshProUGUI[StepNames.Length];
        for (int i = 0; i < StepNames.Length; i++)
        {
            TextMeshProUGUI row = RuntimeUi.CreateText(panel.transform, "Row" + i, "", 24f, Grey);
            row.alignment = TextAlignmentOptions.Left;
            RuntimeUi.Place(row.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -66f - 30f * i), new Vector2(400f, 30f));
            _rows[i] = row;
        }
        SetRows(-1);

        // The live instruction, top-centre.
        _instruction = RuntimeUi.CreateText(_uiRoot.transform, "Instruction", "", 38f, Color.white);
        _instruction.fontStyle = FontStyles.Bold;
        _instruction.outlineWidth = 0.2f;
        _instruction.outlineColor = new Color32(0, 0, 0, 255);
        RuntimeUi.Place(_instruction.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1500f, 120f));

        // Black cover, topmost.
        _blackout = RuntimeUi.CreatePanel(_uiRoot.transform, "IntakeBlackout", Color.black).GetComponent<Image>();
        _blackout.raycastTarget = false;
    }

    private int _current = -1;
    private readonly bool[] _done = new bool[StepNames.Length];

    private void SetRows(int current)
    {
        _current = current;
        RefreshRows();
    }

    private void MarkRow(int index)
    {
        if (index >= 0 && index < _done.Length) _done[index] = true;
        RefreshRows();
    }

    private void RefreshRows()
    {
        if (_rows == null) return;
        for (int i = 0; i < _rows.Length; i++)
        {
            if (_rows[i] == null) continue;
            string box = _done[i] ? "[x]" : "[  ]";
            _rows[i].text = $"{box}  {StepNames[i]}";
            _rows[i].color = _done[i] ? Green : (i == _current ? Color.white : new Color(Grey.r, Grey.g, Grey.b, 0.6f));
        }
    }
}

/// <summary>F86: the bell the thrown bottle has to hit. A solid collider; the flying bottle carries the Rigidbody, so it is told about the impact.</summary>
public class IntakeTarget : MonoBehaviour
{
    /// <summary>True from the first thrown item that touches it.</summary>
    public bool Hit { get; private set; }

    private void OnCollisionEnter(Collision collision)
    {
        if (Hit) return;
        if (collision.collider.GetComponentInParent<ThrowableItem>() == null) return;
        Strike();
    }

    // The builder gives the bell a generous TRIGGER sphere (a solid one would block the player), so a bottle flying
    // through it counts. Resting bottles and cans have trigger colliders themselves and are ignored.
    private void OnTriggerEnter(Collider other)
    {
        if (Hit || other.isTrigger) return;
        if (other.GetComponentInParent<ThrowableItem>() == null) return;
        Strike();
    }

    /// <summary>Rings the bell. Public so a test (or a future rope) can ring it without a throw.</summary>
    public void Strike()
    {
        if (Hit) return;
        Hit = true;
        AudioSource.PlayClipAtPoint(DoorAudio.BuildClunkClip(0.9f, 1250f, 0.7f), transform.position, 1f);
    }
}

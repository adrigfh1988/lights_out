using UnityEngine;

/// <summary>
/// F82: gives Stan a voice - no audio voice, a speech bubble that types a line out of the
/// StalkerPhraseBook when something happens. This class decides WHEN he talks (a sampled "he can see you"
/// test plus the events Stalker raises through Say) and supplies his lines, priorities and cooldowns; the
/// pacing, shuffle bags, bubble and per-letter blip/nod/flare live in CreatureVoice. Stalker.ApplyPose adds
/// NodDegrees to his head and Stalker.LateUpdate applies EyeFlare to his eyes.
///
/// Gameplay-inert by default: speechAlertsHunter is off, so talking never alerts the hunter. Added
/// to the stalker clone by Stalker.Configure (floors 4-5 only), so floors 1-3 create nothing.
/// </summary>
public class StalkerVoice : CreatureVoice
{
    private const float BubbleAboveHead = 0.45f;

    [Header("Sight")]
    [Tooltip("Flat metres. There is no view cone: his head already tracks the camera, so a clear line means he is looking at you.")]
    [SerializeField] private float sightRange = 18f;
    [SerializeField] private float sightSampleInterval = 0.2f;
    [Tooltip("Seconds without sight before regaining it counts as 'spotting' you.")]
    [SerializeField] private float spottedAfterUnseen = 4f;
    [Tooltip("Continuous sight needed before losing it can trigger a Lost line.")]
    [SerializeField] private float lostAfterSeen = 3f;
    [SerializeField] private float lostChance = 0.5f;
    [Tooltip("Continuous sight needed before an Idle line.")]
    [SerializeField] private float idleAfterSeen = 8f;
    [Tooltip("Idle also needs this long since the last line.")]
    [SerializeField] private float idleQuietSeconds = 8f;
    [SerializeField] private float lockerRange = 4f;

    [Header("Cooldowns")]
    [SerializeField] private float cooldownSpotted = 12f;
    [SerializeField] private float cooldownLit = 10f;
    [SerializeField] private float cooldownStaring = 10f;
    [SerializeField] private float cooldownLunge = 6f;
    [SerializeField] private float cooldownTouch = 2f;
    [SerializeField] private float cooldownLost = 15f;
    [SerializeField] private float cooldownLocker = 20f;
    [SerializeField] private float cooldownIdle = 14f;

    [Header("Gameplay")]
    [Tooltip("When on, a Spotted line calls HearNoise(15 m) on the hunter - Stan 'calls out'. Off: speech is inert.")]
    [SerializeField] private bool speechAlertsHunter = false;

    private Stalker _owner;
    private StalkerPhraseBook _book;
    private bool _bookIsOurs;
    private Transform _player;
    private Transform _camera;
    private PlayerStealthState _stealth;
    private AIFollower _hunter;
    private GameOutcome _outcome;
    private bool _configured;

    private bool _canSee;
    private float _sampleTimer;
    private float _lastSeenAt = -999f;
    private float _seenSince;

    /// <summary>Last sampled "Stan can see you" value; Stalker reads it for the Lit gate.</summary>
    public bool CanSeePlayer => _canSee;

    public void Configure(Stalker owner, StalkerPhraseBook book, Transform player, Transform camera,
        PlayerStealthState stealth, AIFollower hunter, GameOutcome outcome, AudioSource audio)
    {
        _owner = owner;
        _player = player;
        _camera = camera;
        _stealth = stealth;
        _hunter = hunter;
        _outcome = outcome;

        _book = book;
        if (_book == null)
        {
            Debug.LogWarning("StalkerVoice: no phrase book assigned on the Stalker template - using the built-in default lines. Run LIGHTS OUT > Build Phrase Books to make an editable one.", this);
            _book = StalkerPhraseBook.CreateDefault();
            _bookIsOurs = true;
        }

        InitVoice(audio);
        _configured = true;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (_bookIsOurs && _book != null) Destroy(_book);
    }

    /// <summary>Asks Stan to say a line from this category (dropped if the pacing rules refuse it).</summary>
    public void Say(StalkerPhraseBook.Category category)
    {
        if (!_configured) return;
        if (TrySay((int)category) && category == StalkerPhraseBook.Category.Spotted
            && speechAlertsHunter && _hunter != null && _owner != null)
        {
            _hunter.HearNoise(_owner.transform.position, 15f);
        }
    }

    // ---------------------------------------------------------------- CreatureVoice

    protected override GameOutcome Outcome => _outcome;
    protected override int CategoryCount => 8;
    protected override float BubbleRange => sightRange;
    protected override string[] LinesFor(int category) => _book != null ? _book.Get((StalkerPhraseBook.Category)category) : null;

    protected override Vector3 BubbleAnchor() =>
        _owner != null ? _owner.HeadWorldPosition + Vector3.up * BubbleAboveHead : transform.position;

    /// <summary>Hidden after a touch: nothing - except the Touch line, which is said just before he vanishes.</summary>
    protected override bool CanSpeak(int category) =>
        _owner == null || !_owner.IsHiddenAfterTouch || category == (int)StalkerPhraseBook.Category.Touch;

    protected override bool IsShout(int category) =>
        category == (int)StalkerPhraseBook.Category.Lunge || category == (int)StalkerPhraseBook.Category.Touch;

    /// <summary>The touch line must stay readable: no glitches.</summary>
    protected override bool GlitchFor(int category) => category != (int)StalkerPhraseBook.Category.Touch;

    /// <summary>Touch &gt; Lunge &gt; Lit/Staring &gt; Spotted/Locker &gt; Lost/Idle.</summary>
    protected override int PriorityOf(int category)
    {
        switch ((StalkerPhraseBook.Category)category)
        {
            case StalkerPhraseBook.Category.Touch: return 5;
            case StalkerPhraseBook.Category.Lunge: return 4;
            case StalkerPhraseBook.Category.Lit:
            case StalkerPhraseBook.Category.Staring: return 3;
            case StalkerPhraseBook.Category.Spotted:
            case StalkerPhraseBook.Category.Locker: return 2;
            default: return 1;
        }
    }

    protected override float CooldownOf(int category)
    {
        switch ((StalkerPhraseBook.Category)category)
        {
            case StalkerPhraseBook.Category.Spotted: return cooldownSpotted;
            case StalkerPhraseBook.Category.Lit: return cooldownLit;
            case StalkerPhraseBook.Category.Staring: return cooldownStaring;
            case StalkerPhraseBook.Category.Lunge: return cooldownLunge;
            case StalkerPhraseBook.Category.Touch: return cooldownTouch;
            case StalkerPhraseBook.Category.Lost: return cooldownLost;
            case StalkerPhraseBook.Category.Locker: return cooldownLocker;
            default: return cooldownIdle;
        }
    }

    protected override void Tick(float deltaTime)
    {
        _sampleTimer -= deltaTime;
        if (_sampleTimer > 0f) return;
        _sampleTimer = sightSampleInterval;
        SampleSight();
    }

    /// <summary>The "sees me" test, run every sightSampleInterval seconds (it costs a raycast) - and the
    /// Spotted / Lost / Locker / Idle triggers that hang off it.</summary>
    private void SampleSight()
    {
        float now = Time.time;
        bool hiddenAway = _owner == null || _owner.IsHiddenAfterTouch || _player == null || _camera == null;
        bool playerHiding = _stealth != null && _stealth.Hidden;

        bool seen = false;
        if (!hiddenAway && !playerHiding)
        {
            Vector3 head = _owner.HeadWorldPosition;
            Vector3 flat = _player.position - _owner.transform.position;
            flat.y = 0f;
            seen = flat.magnitude <= sightRange && _owner.HasClearLine(head, _camera.position);
        }

        if (seen && !_canSee)
        {
            // Sight acquired.
            if (now - _lastSeenAt >= spottedAfterUnseen) Say(StalkerPhraseBook.Category.Spotted);
            _seenSince = now;
        }
        else if (!seen && _canSee)
        {
            // Sight lost - only worth a remark after a good long look, and only sometimes.
            if (!hiddenAway && now - _seenSince >= lostAfterSeen && Random.value < lostChance)
                Say(StalkerPhraseBook.Category.Lost);
        }

        if (seen)
        {
            _lastSeenAt = now;
            if (now - _seenSince >= idleAfterSeen && now - LastSpokeAt >= idleQuietSeconds)
                Say(StalkerPhraseBook.Category.Idle);
        }
        _canSee = seen;

        // Locker: the player is hiding and he is right outside it. Distance only - a line test to the player
        // would hit the locker's own collider, which is the thing he is standing next to.
        if (!hiddenAway && playerHiding)
        {
            Vector3 flat = _player.position - _owner.transform.position;
            flat.y = 0f;
            if (flat.magnitude <= lockerRange) Say(StalkerPhraseBook.Category.Locker);
        }
    }
}

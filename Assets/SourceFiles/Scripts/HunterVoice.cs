using UnityEngine;

/// <summary>
/// F82: the hunter talks too - same speech-bubble engine as Stan (CreatureVoice), its
/// own HunterPhraseBook and a darker bubble (SpeechBubble.Palette.Night) with lower blips, so the two
/// voices never read as one. Everything it reacts to is read off AIFollower's public state (HasSight,
/// IsChasing, IsSearching, IsAmbushing, IsStalled) and its AmbushStateChanged / PlayerCaught events -
/// AIFollower's behaviour is untouched.
///
/// It never talks while lying in ambush (that would give the ambush away); the ambush line is shouted only
/// when it springs with you in sight. The capture line is allowed to outlive the ending gate and plays
/// into the capture blackout (GameOutcome.CaptureSequence creates its blackout panel later, as the topmost
/// sibling, so it covers the bubble). Gameplay-inert: speech never changes what the hunter does.
///
/// Lives on the hunter's own GameObject (LIGHTS OUT &gt; Build Phrase Books adds it there with the book
/// assigned, so its tunables are editable in the scene); MazeGenerator.SetUpAtmosphere adds it if missing
/// and calls Configure. No statics.
/// </summary>
public class HunterVoice : CreatureVoice
{
    [Tooltip("Hunter's lines (Assets/SourceFiles/Data/HunterPhrases.asset). Assigned by LIGHTS OUT > Build Phrase Books; empty = built-in defaults.")]
    [SerializeField] private HunterPhraseBook phraseBook;

    [Header("Triggers")]
    [Tooltip("Seconds without sight before regaining it counts as 'spotting' you.")]
    [SerializeField] private float spottedAfterUnseen = 4f;
    [Tooltip("Continuous sight during a chase before a taunt.")]
    [SerializeField] private float chasingAfterSeen = 6f;
    [SerializeField] private float chasingQuietSeconds = 7f;
    [SerializeField] private float lostChance = 0.7f;
    [Tooltip("Searching mutters only within this flat distance of the player.")]
    [SerializeField] private float searchTalkRange = 14f;
    [Tooltip("Chance per check (every searchTalkInterval) of a Searching mutter.")]
    [SerializeField] private float searchTalkChance = 0.35f;
    [SerializeField] private float searchTalkInterval = 2f;
    [SerializeField] private float lockerRange = 4f;
    [Tooltip("An ambush that ends within this many seconds of it having sight counts as 'springing'.")]
    [SerializeField] private float ambushSpringWindow = 1f;
    [Tooltip("Flat metres at which the bubble is drawn smallest.")]
    [SerializeField] private float bubbleRange = 25f;
    [SerializeField] private float bubbleAboveHead = 0.45f;

    [Header("Cooldowns")]
    [SerializeField] private float cooldownSpotted = 12f;
    [SerializeField] private float cooldownChasing = 10f;
    [SerializeField] private float cooldownLost = 15f;
    [SerializeField] private float cooldownSearching = 12f;
    [SerializeField] private float cooldownAmbush = 10f;
    [SerializeField] private float cooldownLocker = 20f;
    [SerializeField] private float cooldownDoor = 12f;
    [SerializeField] private float cooldownCaught = 2f;

    private AIFollower _hunter;
    private HunterPhraseBook _book;
    private bool _bookIsOurs;
    private PlayerStealthState _stealth;
    private GameOutcome _outcome;
    private bool _configured;

    private bool _hadSight;
    private float _lastSeenAt = -999f;
    private float _seenSince;
    private bool _wasChasing;
    private bool _wasStalled;
    private float _ambushEndedAt = -999f;
    private float _searchTalkTimer;

    public void Configure(AIFollower hunter, PlayerStealthState stealth, GameOutcome outcome)
    {
        if (_configured || hunter == null) return;
        _hunter = hunter;
        _stealth = stealth;
        _outcome = outcome;

        _book = phraseBook;
        if (_book == null)
        {
            Debug.LogWarning("HunterVoice: no phrase book assigned on the hunter - using the built-in default lines. Run LIGHTS OUT > Build Phrase Books to make an editable one.", this);
            _book = HunterPhraseBook.CreateDefault();
            _bookIsOurs = true;
        }

        _hunter.AmbushStateChanged += HandleAmbushChanged;
        _hunter.PlayerCaught += HandleCaught;

        InitVoice(null);
        _configured = true;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (_hunter != null)
        {
            _hunter.AmbushStateChanged -= HandleAmbushChanged;
            _hunter.PlayerCaught -= HandleCaught;
        }
        if (_bookIsOurs && _book != null) Destroy(_book);
    }

    private void HandleAmbushChanged(bool ambushing)
    {
        if (!ambushing) _ambushEndedAt = Time.time;
    }

    private void HandleCaught() => Say(HunterPhraseBook.Category.Caught);

    public void Say(HunterPhraseBook.Category category)
    {
        if (_configured) TrySay((int)category);
    }

    // ---------------------------------------------------------------- CreatureVoice

    protected override GameOutcome Outcome => _outcome;
    protected override int CategoryCount => 8;
    protected override float BubbleRange => bubbleRange;
    protected override SpeechBubble.Palette BubblePalette => SpeechBubble.Palette.Night;
    protected override float[] BlipFrequencies => new[] { 260f, 330f, 410f };
    protected override Vector2 BlipPitchRange => new Vector2(0.7f, 0.95f);
    protected override string[] LinesFor(int category) => _book != null ? _book.Get((HunterPhraseBook.Category)category) : null;

    protected override Vector3 BubbleAnchor() =>
        _hunter != null ? _hunter.transform.position + Vector3.up * (_hunter.EyeHeight + bubbleAboveHead) : transform.position;

    /// <summary>Silent while lying in ambush - only the capture line gets out once it has caught you.</summary>
    protected override bool CanSpeak(int category)
    {
        if (_hunter == null) return false;
        if (category == (int)HunterPhraseBook.Category.Caught) return true;
        return !_hunter.IsAmbushing && !_hunter.IsCaptured;
    }

    protected override bool OutlivesEnding(int category) => category == (int)HunterPhraseBook.Category.Caught;

    protected override bool IsShout(int category) =>
        category == (int)HunterPhraseBook.Category.Ambush || category == (int)HunterPhraseBook.Category.Caught;

    protected override bool GlitchFor(int category) => category != (int)HunterPhraseBook.Category.Caught;

    /// <summary>Caught &gt; Ambush &gt; Spotted/Door &gt; Lost/Locker &gt; Chasing/Searching.</summary>
    protected override int PriorityOf(int category)
    {
        switch ((HunterPhraseBook.Category)category)
        {
            case HunterPhraseBook.Category.Caught: return 5;
            case HunterPhraseBook.Category.Ambush: return 4;
            case HunterPhraseBook.Category.Spotted:
            case HunterPhraseBook.Category.Door: return 3;
            case HunterPhraseBook.Category.Lost:
            case HunterPhraseBook.Category.Locker: return 2;
            default: return 1;
        }
    }

    protected override float CooldownOf(int category)
    {
        switch ((HunterPhraseBook.Category)category)
        {
            case HunterPhraseBook.Category.Spotted: return cooldownSpotted;
            case HunterPhraseBook.Category.Chasing: return cooldownChasing;
            case HunterPhraseBook.Category.Lost: return cooldownLost;
            case HunterPhraseBook.Category.Searching: return cooldownSearching;
            case HunterPhraseBook.Category.Ambush: return cooldownAmbush;
            case HunterPhraseBook.Category.Locker: return cooldownLocker;
            case HunterPhraseBook.Category.Door: return cooldownDoor;
            default: return cooldownCaught;
        }
    }

    /// <summary>Every unpaused live frame: edges on the hunter's own perception/state (cheap property reads,
    /// no raycasts of its own - HasSight is AIFollower's real sight test).</summary>
    protected override void Tick(float deltaTime)
    {
        if (_hunter == null) return;
        float now = Time.time;

        bool sight = _hunter.HasSight && !_hunter.IsAmbushing;
        bool chasing = _hunter.IsChasing;
        bool stalled = _hunter.IsStalled;

        // Springing an ambush with you in sight beats a plain "spotted".
        bool sprang = sight && now - _ambushEndedAt <= ambushSpringWindow;
        if (sprang)
        {
            Say(HunterPhraseBook.Category.Ambush);
            _ambushEndedAt = -999f;
        }

        if (sight && !_hadSight)
        {
            if (!sprang && now - _lastSeenAt >= spottedAfterUnseen) Say(HunterPhraseBook.Category.Spotted);
            _seenSince = now;
        }
        if (sight)
        {
            _lastSeenAt = now;
            if (chasing && now - _seenSince >= chasingAfterSeen && now - LastSpokeAt >= chasingQuietSeconds)
                Say(HunterPhraseBook.Category.Chasing);
        }
        _hadSight = sight;

        // Chase -> search: it lost you.
        if (_wasChasing && !chasing && _hunter.IsSearching && Random.value < lostChance)
            Say(HunterPhraseBook.Category.Lost);
        _wasChasing = chasing;

        if (stalled && !_wasStalled) Say(HunterPhraseBook.Category.Door);
        _wasStalled = stalled;

        bool playerHiding = _stealth != null && _stealth.Hidden;
        float flat = _hunter.FlatDistanceToTarget;
        if (playerHiding && flat <= lockerRange) Say(HunterPhraseBook.Category.Locker);

        // Searching mutters: near you, not seeing you, every so often.
        _searchTalkTimer -= deltaTime;
        if (_searchTalkTimer <= 0f)
        {
            _searchTalkTimer = searchTalkInterval;
            if (_hunter.IsSearching && !sight && !playerHiding && flat <= searchTalkRange && Random.value < searchTalkChance)
                Say(HunterPhraseBook.Category.Searching);
        }
    }
}

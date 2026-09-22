using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Tier 3's orchestrator for two things that both key off the same progress number: F23's relocation
/// (the hunter appearing behind you, telegraphed, along your own trail) and F27's reaction to the star
/// count (lamp ripples/deaths, the world darkening, the last-star breath). Subtitles for both live here
/// too, via PlayerHud.ShowSubtitle.
///
/// No execution-order attribute: everything it needs (PlayerHud, the maze, the actors) is built earlier
/// in MazeGenerator.SetUpAtmosphere and handed in through Configure. Added at runtime, so OnEnable runs
/// during AddComponent, before Configure - every handler below null-checks accordingly.
/// </summary>
public class DreadDirector : MonoBehaviour
{
    private struct TrailSample
    {
        public Vector3 Position;
        public float Time;
    }

    [Header("Relocation - trigger")]
    [Tooltip("Progress (0..1) at which relocation may start")]
    [SerializeField] private float startProgress = 0.4f;
    [Tooltip("Seconds between relocation checks while a star has armed one")]
    [SerializeField] private float checkInterval = 0.5f;
    [Tooltip("Taking a star arms one relocation for this many seconds. It fires the first moment the rules allow inside the window, then disarms; nothing relocates outside a window, so every relocation is an answer to a pickup.")]
    [SerializeField] private float armedWindowSeconds = 10f;
    [Tooltip("Hard minimum seconds between two relocations, so two stars grabbed back to back cannot chain them")]
    [SerializeField] private float hardMinimumCooldown = 15f;
    [Tooltip("Seconds since the last chase ended before a relocation may be considered again")]
    [SerializeField] private float chaseCooldown = 8f;
    [Tooltip("The player has to have moved at least this far within the stillness window - never onto someone standing still reading the HUD")]
    [SerializeField] private float movementThreshold = 1f;
    [SerializeField] private float stillnessWindow = 3f;
    [Tooltip("The hunter has to be at least this much farther than the relocation distance, or it would be a nudge rather than a jump")]
    [SerializeField] private float jumpMargin = 5f;

    [Header("Relocation - distance")]
    [Tooltip("Relocation distance along the trail at startProgress (m)")]
    [SerializeField] private float farDistance = 16f;
    [Tooltip("...at full progress (m). Overridden per floor by FloorProfile.RelocateNearDistance when a profile is bound.")]
    [SerializeField] private float nearDistance = 7f;
    [Tooltip("Never relocate closer than this, whatever progress says (m)")]
    [SerializeField] private float minDistance = 6f;

    [Header("Relocation - trail")]
    [SerializeField] private float sampleInterval = 0.5f;
    [SerializeField] private int trailCapacity = 64;
    [Tooltip("A jump further than this between samples is a teleport (locker, hatch drop, respawn), not travel - dropped so the trail never runs through the inside of a locker")]
    [SerializeField] private float trailJumpThreshold = 2.5f;

    [Header("Relocation - candidate spot")]
    [SerializeField] private float navSampleRadius = 1.5f;
    [SerializeField] private float lockerExclusionRadius = 2f;
    [Tooltip("Reject a spot this close to the hunter's current position - nothing happens if it is already there")]
    [SerializeField] private float hunterExclusionRadius = 3f;
    [Tooltip("How far back along the trail, past the chosen point, to also try if it fails validation")]
    [SerializeField] private int candidateFallbackCount = 4;

    [Header("Relocation - telegraph timing (seconds from trigger)")]
    [SerializeField] private float humHoldSeconds = 1.4f;
    [SerializeField] private float stutterAt = 0.15f;
    [SerializeField] private float stutterSeconds = 0.9f;
    [SerializeField] private float cueAt = 0.30f;
    [SerializeField] private float warpAt = 1.10f;

    [Header("Subtitles")]
    [SerializeField] private float subtitleSeconds = 2.6f;
    [Tooltip("Relocation distance at or above this reads as the far band")]
    [SerializeField] private float farBand = 13f;
    [Tooltip("Relocation distance at or above this (and below farBand) reads as the mid band")]
    [SerializeField] private float midBand = 9f;
    [SerializeField] private string lineFar = "Something moved behind you.";
    [SerializeField] private string lineMid = "It's getting closer.";
    [SerializeField] private string lineNear = "It's right behind you.";
    [SerializeField] private string lineLightsDying = "The lights are dying.";
    [SerializeField] private string lineItKnows = "It knows.";
    [Tooltip("F45: shown once per campaign, the first time an ambush ends because the ambushed star was actually taken")]
    [SerializeField] private string lineAmbushed = "It was waiting.";

    [Header("F27 - reacting to the count")]
    [SerializeField] private float flickerWaveSpeed = 12f;
    [SerializeField] private float flickerDuration = 0.25f;
    [SerializeField] private float firstDeathProgress = 0.5f;
    [SerializeField] private float secondDeathProgress = 0.75f;
    [Tooltip("Fraction of the ORIGINAL live, non-locker lamp count killed at each death wave")]
    [SerializeField] private float deathWaveFraction = 0.2f;
    [SerializeField] private float deathFadeSeconds = 1.5f;
    [SerializeField] private float firstDarkenFraction = 0.35f;
    [SerializeField] private float secondDarkenFraction = 0.6f;
    [SerializeField] private float darkenSeconds = 2f;
    [SerializeField] private float eyeEmissionAtSecondDeath = 6f;
    [SerializeField] private float breathSeconds = 1.6f;

    private MazeGenerator _maze;
    private Transform _player;
    private Camera _camera;
    private AIFollower _follower;
    private AIPresence _presence;
    private HorrorAudioDirector _audio;
    private HorrorAtmosphere _atmosphere;
    private Flashlight _flashlight;
    private PlayerStealthState _stealth;
    private PlayerHud _hud;
    private GameOutcome _outcome;

    private readonly List<TrailSample> _trail = new List<TrailSample>();
    private Vector3 _lastRawSample;
    private bool _hasLastRaw;
    private Vector3 _lastAcceptedPosition;
    private bool _hasAccepted;

    private readonly RaycastHit[] _losHits = new RaycastHit[16];

    private float _sampleTimer;
    private float _checkTimer;
    // Time.time until which a star-taken relocation is armed; -1 = disarmed. Time.time stands still under
    // pause, so a window paused halfway resumes with its remainder intact.
    private float _armedUntil = -1f;
    private float _lastRelocationTime = -9999f;
    private float _lastChaseEndTime = -9999f;
    private bool _isTelegraphing;

    private int _collected;
    private int _total;
    private bool _firstDeathsFired;
    private bool _secondDeathsFired;
    private int _originalLiveLampCount = -1;
    private System.Random _lampRng;

    // F45: shown once per campaign. With Enter Play Mode > Reload Domain disabled this static would
    // otherwise survive between presses of Play, same as GameFlow's own statics - reset alongside them.
    private static bool s_ambushSubtitleShown;

    /// <summary>True while a relocation telegraph is playing out. PhantomDirector skips a phantom while this is true - a telegraph is already one.</summary>
    public bool IsTelegraphing => _isTelegraphing;

    /// <summary>F45's once-per-campaign subtitle flag. Cleared by GameFlow.ReturnToMenu - the economy and dread beats both live for one campaign only.</summary>
    public static void ResetCampaign()
    {
        s_ambushSubtitleShown = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_ambushSubtitleShown = false;
    }

    private float Progress => _total > 0 ? _collected / (float)_total : 0f;

    public void Configure(MazeGenerator maze, Transform player, Camera camera, AIFollower follower, AIPresence presence,
        HorrorAudioDirector audio, HorrorAtmosphere atmosphere, Flashlight flashlight, PlayerStealthState stealth,
        PlayerHud hud, GameOutcome outcome, FloorProfile profile)
    {
        _maze = maze;
        _player = player;
        _camera = camera;
        _presence = presence;
        _audio = audio;
        _atmosphere = atmosphere;
        _flashlight = flashlight;
        _stealth = stealth;
        _hud = hud;
        _outcome = outcome;

        // Configure runs after OnEnable when this is added at runtime, so unsubscribe first - harmless
        // if it was never subscribed, and required so a re-Configure cannot double-subscribe.
        if (_follower != null)
        {
            _follower.ChaseStateChanged -= HandleChaseStateChanged;
            _follower.AmbushStateChanged -= HandleAmbushStateChanged;
        }
        _follower = follower;
        if (_follower != null)
        {
            _follower.ChaseStateChanged += HandleChaseStateChanged;
            _follower.AmbushStateChanged += HandleAmbushStateChanged;
        }

        if (profile != null)
        {
            nearDistance = profile.RelocateNearDistance;
        }
    }

    private void OnEnable()
    {
        GameManager.ProgressChanged += HandleProgress;
        GameManager.AllStarsCollected += HandleAllStarsCollected;
        Pickup.OnCollectedAt += HandleStarTaken;
    }

    private void OnDisable()
    {
        GameManager.ProgressChanged -= HandleProgress;
        GameManager.AllStarsCollected -= HandleAllStarsCollected;
        Pickup.OnCollectedAt -= HandleStarTaken;
        if (_follower != null)
        {
            _follower.ChaseStateChanged -= HandleChaseStateChanged;
            _follower.AmbushStateChanged -= HandleAmbushStateChanged;
        }

        StopAllCoroutines();
        _isTelegraphing = false;
    }

    private void Update()
    {
        if (_maze == null || _player == null) return;

        _sampleTimer -= Time.deltaTime;
        if (_sampleTimer <= 0f)
        {
            _sampleTimer = sampleInterval;
            SampleTrail();
        }

        // Only a star arms a relocation. Outside a window there is nothing to check, so the hunter never
        // just "shows up" behind a player who is quietly exploring.
        if (!_isTelegraphing && Time.time <= _armedUntil)
        {
            _checkTimer -= Time.deltaTime;
            if (_checkTimer <= 0f)
            {
                _checkTimer = checkInterval;
                TryConsiderRelocation();
            }
        }
    }

    // ---------------------------------------------------------------- F23: trail

    private void SampleTrail()
    {
        // Lockers stay trustworthy: never record the inside of one, or the trail would offer it up
        // as a relocation spot.
        if (_stealth != null && _stealth.Hidden) return;

        Vector3 pos = _player.position;

        if (_hasAccepted && Vector3.Distance(_lastAcceptedPosition, pos) > trailJumpThreshold)
        {
            // A teleport (locker, hatch drop, respawn). If the very next sample settles near here too,
            // treat this as the new baseline instead of refusing every sample forever.
            if (_hasLastRaw && Vector3.Distance(_lastRawSample, pos) <= trailJumpThreshold)
            {
                _hasAccepted = false;
            }
            else
            {
                _lastRawSample = pos;
                _hasLastRaw = true;
                return;
            }
        }

        _lastRawSample = pos;
        _hasLastRaw = true;
        _lastAcceptedPosition = pos;
        _hasAccepted = true;

        _trail.Add(new TrailSample { Position = pos, Time = Time.time });
        if (_trail.Count > trailCapacity) _trail.RemoveAt(0);
    }

    /// <summary>Has the player covered at least movementThreshold metres within the last stillnessWindow seconds?</summary>
    private bool PlayerMovedRecently()
    {
        if (_trail.Count == 0) return false;

        Vector3 latest = _trail[_trail.Count - 1].Position;
        float cutoff = Time.time - stillnessWindow;

        for (int i = _trail.Count - 1; i >= 0; i--)
        {
            if (_trail[i].Time < cutoff) break;
            if (Vector3.Distance(_trail[i].Position, latest) >= movementThreshold) return true;
        }

        return false;
    }

    /// <summary>Walks the trail backwards accumulating segment length until it reaches distance, then tries that entry and a few older ones.</summary>
    private bool TryFindRelocationSpot(float distance, out Vector3 spot)
    {
        spot = default;
        if (_trail.Count < 2) return false;

        float accumulated = 0f;
        int startIndex = -1;
        for (int i = _trail.Count - 1; i > 0; i--)
        {
            accumulated += Vector3.Distance(_trail[i].Position, _trail[i - 1].Position);
            if (accumulated >= distance)
            {
                startIndex = i - 1;
                break;
            }
        }

        // The trail does not reach back far enough yet - no candidate, rather than quietly settling
        // for the oldest sample and mislabelling a near relocation as the far band.
        if (startIndex < 0) return false;

        for (int offset = 0; offset <= candidateFallbackCount; offset++)
        {
            int index = startIndex - offset;
            if (index < 0) break;

            if (ValidateCandidateFull(_trail[index].Position, out Vector3 navPosition))
            {
                spot = navPosition;
                return true;
            }
        }

        return false;
    }

    private bool ValidateCandidateFull(Vector3 rawPosition, out Vector3 navPosition)
    {
        navPosition = rawPosition;
        if (!NavMesh.SamplePosition(rawPosition, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas)) return false;
        navPosition = hit.position;

        if (IsInsideLockerCell(navPosition)) return false;

        return ValidateCandidateVisibilityAndDistance(navPosition);
    }

    /// <summary>23.3 steps 3-5, reused as-is for the re-validation at the warp instant.</summary>
    private bool ValidateCandidateVisibilityAndDistance(Vector3 navPosition)
    {
        if (!IsHiddenFromCamera(navPosition)) return false;

        if (FlatDistance(_player.position, navPosition) < minDistance) return false;

        if (_follower != null && Vector3.Distance(_follower.transform.position, navPosition) < hunterExclusionRadius) return false;

        return true;
    }

    private bool IsInsideLockerCell(Vector3 position)
    {
        if (_maze == null) return false;

        IReadOnlyList<Locker> lockers = _maze.Lockers;
        for (int i = 0; i < lockers.Count; i++)
        {
            if (lockers[i] != null && Vector3.Distance(lockers[i].FrontPosition, position) <= lockerExclusionRadius) return true;
        }

        return false;
    }

    /// <summary>
    /// True if the point is an acceptable relocation spot: both eye- and feet-height rays from the
    /// camera are blocked, or the point is genuinely behind the camera and outside its view frustum.
    /// Blocked-and-behind is the common case; blocked-only is accepted; a visible point is rejected.
    /// </summary>
    private bool IsHiddenFromCamera(Vector3 point)
    {
        if (_camera == null) return true;

        Vector3 camPos = _camera.transform.position;
        bool eyesBlocked = IsRayBlocked(camPos, point + Vector3.up * 1.5f);
        bool feetBlocked = IsRayBlocked(camPos, point + Vector3.up * 0.3f);
        if (eyesBlocked && feetBlocked) return true;

        Vector3 viewport = _camera.WorldToViewportPoint(point);
        bool outsideFrustum = viewport.z < 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f;

        Vector3 flatDir = point - camPos;
        flatDir.y = 0f;
        Vector3 flatForward = _camera.transform.forward;
        flatForward.y = 0f;
        bool genuinelyBehind = flatDir.sqrMagnitude > 0.0001f && flatForward.sqrMagnitude > 0.0001f
            && Vector3.Dot(flatForward.normalized, flatDir.normalized) < -0.2f;

        return outsideFrustum && genuinelyBehind;
    }

    private bool IsRayBlocked(Vector3 from, Vector3 to)
    {
        Vector3 diff = to - from;
        float distance = diff.magnitude;
        if (distance < 0.01f) return false;

        int count = Physics.RaycastNonAlloc(from, diff / distance, _losHits, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform hit = _losHits[i].transform;
            if (_player != null && IsPartOf(hit, _player)) continue;
            if (_follower != null && IsPartOf(hit, _follower.transform)) continue;
            return true;
        }

        return false;
    }

    private static bool IsPartOf(Transform candidate, Transform root)
    {
        while (candidate != null)
        {
            if (candidate == root) return true;
            candidate = candidate.parent;
        }

        return false;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        Vector3 d = a - b;
        d.y = 0f;
        return d.magnitude;
    }

    // ---------------------------------------------------------------- F23: trigger + telegraph

    private void TryConsiderRelocation()
    {
        if (Time.time - _lastRelocationTime < hardMinimumCooldown) return;

        float distance = ComputeRelocationDistance(Progress);
        if (!PassesCoreRules(distance)) return;
        if (!TryFindRelocationSpot(distance, out Vector3 spot)) return;

        StartCoroutine(RelocateRoutine(spot, distance));
    }

    private float ComputeRelocationDistance(float progress)
    {
        float t = Mathf.InverseLerp(startProgress, 1f, progress);
        return Mathf.Max(minDistance, Mathf.Lerp(farDistance, nearDistance, t));
    }

    /// <summary>Trigger rules 1-5 of 23.2 (6: the star-armed window, and 7: candidate search, are checked separately).</summary>
    private bool PassesCoreRules(float distance)
    {
        if (_maze == null || _player == null || _follower == null) return false;
        if (!GameFlow.IsRunActive || GameOutcome.IsOver || (_outcome != null && _outcome.IsEnding)) return false;

        float progress = Progress;
        if (progress < startProgress || _follower.IsHunting) return false;
        if (_follower.IsChasing || _follower.IsCaptured) return false;
        // F45: an ambush is a beat of its own - it should not be interrupted by a relocation telegraph.
        if (_follower.IsAmbushing) return false;
        if (Time.time - _lastChaseEndTime < chaseCooldown) return false;
        if (_stealth != null && _stealth.Hidden) return false;
        if (!PlayerMovedRecently()) return false;
        if (_follower.FlatDistanceToTarget <= distance + jumpMargin) return false;

        return true;
    }

    private IEnumerator RelocateRoutine(Vector3 spot, float distance)
    {
        _isTelegraphing = true;

        if (_presence != null) _presence.Hold(humHoldSeconds);
        if (_audio != null) _audio.SkipBeat();

        yield return WaitSeconds(stutterAt);
        if (BailedOut()) { _isTelegraphing = false; yield break; }
        if (_flashlight != null) _flashlight.Stutter(stutterSeconds);

        yield return WaitSeconds(cueAt - stutterAt);
        if (BailedOut()) { _isTelegraphing = false; yield break; }
        if (_audio != null) _audio.PlayRelocationCue();

        yield return WaitSeconds(warpAt - cueAt);
        if (BailedOut())
        {
            _isTelegraphing = false;
            yield break;
        }

        // Re-validate rules 1-5 and the spot with fresh positions. Failing here is not an error - the
        // telegraph simply reads as a phantom, and nothing warps.
        if (PassesCoreRules(distance) && ValidateCandidateVisibilityAndDistance(spot) && _follower.RelocateTo(spot, _player.position))
        {
            _lastRelocationTime = Time.time;
            // One relocation per star: disarm until the next pickup
            _armedUntil = -1f;

            string line = distance >= farBand ? lineFar : distance >= midBand ? lineMid : lineNear;
            if (_hud != null) _hud.ShowSubtitle(line, subtitleSeconds);
        }

        _isTelegraphing = false;
    }

    private bool BailedOut()
    {
        return GameOutcome.IsOver || (_outcome != null && _outcome.IsEnding) || !GameFlow.IsRunActive;
    }

    private IEnumerator WaitSeconds(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            if (BailedOut()) yield break;
            t += Time.deltaTime;
            yield return null;
        }
    }

    private void HandleChaseStateChanged(bool chasing)
    {
        if (!chasing) _lastChaseEndTime = Time.time;
    }


    /// <summary>F45: the first time an ambush ends because its star was actually taken, show the subtitle - once per campaign.</summary>
    private void HandleAmbushStateChanged(bool ambushing)
    {
        if (ambushing || s_ambushSubtitleShown || _follower == null || !_follower.LastAmbushEndedOnPickup) return;

        s_ambushSubtitleShown = true;
        if (_hud != null) _hud.ShowSubtitle(lineAmbushed, subtitleSeconds);
    }

    // ---------------------------------------------------------------- F27: reacting to the count

    private void HandleProgress(int collected, int total)
    {
        _collected = collected;
        _total = total;

        float progress = Progress;

        if (!_firstDeathsFired && progress >= firstDeathProgress)
        {
            _firstDeathsFired = true;
            KillLampWave();
            if (_atmosphere != null) _atmosphere.Darken(firstDarkenFraction, darkenSeconds);
            if (_hud != null) _hud.ShowSubtitle(lineLightsDying, subtitleSeconds);
        }

        if (!_secondDeathsFired && progress >= secondDeathProgress)
        {
            _secondDeathsFired = true;
            KillLampWave();
            if (_atmosphere != null) _atmosphere.Darken(secondDarkenFraction, darkenSeconds);
            if (_presence != null) _presence.SetEyeEmission(eyeEmissionAtSecondDeath);
        }
    }

    /// <summary>Raised before ProgressChanged for the same star - drives the flicker wave and arms one relocation.</summary>
    private void HandleStarTaken(Vector3 at)
    {
        if (_maze != null)
        {
            IReadOnlyList<WallLamp> lamps = _maze.Lamps;
            for (int i = 0; i < lamps.Count; i++)
            {
                WallLamp lamp = lamps[i];
                if (lamp == null || lamp.IsDead) continue;

                float delay = Vector3.Distance(lamp.transform.position, at) / flickerWaveSpeed;
                lamp.Blink(flickerDuration, delay);
            }
        }

        // F45: the hunter is already stepping out toward this exact pickup (TensionDirector's own
        // Pickup.OnCollectedAt subscription - added earlier, so it runs first - just called HearNoise,
        // which ends a matching ambush synchronously and stamps LastAmbushEndTime). Warping it elsewhere
        // would waste that beat, so this pickup arms no relocation.
        if (_follower != null && Time.time - _follower.LastAmbushEndTime < 0.5f) return;

        // Arm one relocation (23.2 rule 6). The rules (progress, not chasing, not hidden, not hunting, a
        // valid spot) are still checked every tick inside the window; if they never pass, the window
        // simply closes and this star produced no relocation.
        _armedUntil = Time.time + armedWindowSeconds;
        _checkTimer = 0f;
    }

    private void HandleAllStarsCollected()
    {
        if (_maze != null)
        {
            IReadOnlyList<WallLamp> lamps = _maze.Lamps;
            for (int i = 0; i < lamps.Count; i++)
            {
                WallLamp lamp = lamps[i];
                if (lamp != null && !lamp.IsDead) lamp.Blink(breathSeconds);
            }
        }

        if (_audio != null) _audio.HoldBreath(breathSeconds);
        if (_presence != null) _presence.Hold(breathSeconds);
        if (_hud != null) _hud.ShowSubtitle(lineItKnows, subtitleSeconds);
    }

    /// <summary>
    /// Kills ceil(originalLiveNonLockerLamps * deathWaveFraction) lamps, chosen by one RNG seeded from
    /// the maze's seed and reused across both waves - so a same-seed retry kills the same lamps at the
    /// same star both times, and wave 2 does not repeat wave 1's picks.
    /// </summary>
    private void KillLampWave()
    {
        if (_maze == null) return;
        IReadOnlyList<WallLamp> lamps = _maze.Lamps;

        if (_originalLiveLampCount < 0)
        {
            int count = 0;
            for (int i = 0; i < lamps.Count; i++)
            {
                if (lamps[i] != null && !lamps[i].IsLockerLamp) count++;
            }
            _originalLiveLampCount = count;
        }

        if (_lampRng == null) _lampRng = new System.Random(_maze.UsedSeed + 7);

        List<WallLamp> candidates = new List<WallLamp>();
        for (int i = 0; i < lamps.Count; i++)
        {
            WallLamp lamp = lamps[i];
            if (lamp != null && !lamp.IsLockerLamp && !lamp.IsDead) candidates.Add(lamp);
        }

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = _lampRng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int killCount = Mathf.CeilToInt(_originalLiveLampCount * deathWaveFraction);
        for (int i = 0; i < killCount && i < candidates.Count; i++)
        {
            candidates[i].Kill(deathFadeSeconds);
        }
    }
}

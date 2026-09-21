using UnityEngine;

/// <summary>
/// The player-facing dread cues: a heartbeat that quickens as the hunter closes, a sting when it spots
/// you, and a low drone underneath everything.
///
/// The heartbeat uses straight-line distance rather than path distance on purpose. The AI reasons about
/// the maze topologically because it has to walk it; the player feels space, so a heartbeat that spikes
/// when the thing is one wall away - even if that is thirty metres of corridor - is the correct lie.
/// </summary>
public class HorrorAudioDirector : MonoBehaviour
{
    [Header("Heartbeat")]
    [Tooltip("Distance at which the heartbeat becomes audible")]
    [SerializeField] private float heartbeatRange = 18f;
    [Tooltip("Seconds between beats at the edge of the range")]
    [SerializeField] private float slowestInterval = 1.2f;
    [Tooltip("Seconds between beats when it is right on top of you")]
    [SerializeField] private float fastestInterval = 0.45f;
    [Tooltip("Volume when the hunter is right on top of you")]
    [SerializeField] private float heartbeatVolume = 1f;
    [Tooltip("Volume at the very edge of the range - audible, but only just")]
    [SerializeField] private float minHeartbeatVolume = 0.12f;
    [Tooltip("Above 1 holds the heartbeat quiet until it is genuinely close, then ramps hard. 1 = straight line.")]
    [SerializeField] private float proximityFalloff = 2f;

    [Header("Sting")]
    [SerializeField] private AudioClip stingClip;
    [SerializeField] private float stingPitch = 0.55f;
    [SerializeField] private float stingVolume = 0.8f;
    [Tooltip("The clip to hand may be a minutes-long loop, so the sting is cut off after this many seconds")]
    [SerializeField] private float stingDuration = 1.5f;

    [Header("Drone")]
    [SerializeField] private AudioClip droneClip;
    [SerializeField] private float dronePitch = 0.6f;
    [SerializeField] private float droneVolume = 0.1f;

    [Header("Escalation")]
    [Tooltip("How much louder the drone gets at full progress")]
    [SerializeField] private float droneVolumeAtPeak = 0.3f;
    [Tooltip("The drone creeps upward in pitch as things get worse")]
    [SerializeField] private float dronePitchAtPeak = 0.72f;
    [Tooltip("Volume of the synthesised dissonant bed at full progress. It is silent at zero.")]
    [SerializeField] private float tensionVolume = 0.45f;
    [Tooltip("The heartbeat starts carrying from further away as progress climbs")]
    [SerializeField] private float heartbeatRangeAtPeak = 28f;

    [Header("Panic")]
    [Tooltip("Played on a loop once the hatch opens")]
    [SerializeField] private AudioClip panicClip;
    [SerializeField] private float panicVolume = 0.35f;
    [Tooltip("Music_Exciting at native pitch reads as a victory fanfare. Dropped, it reads as panic.")]
    [SerializeField] private float panicPitch = 0.78f;

    private Transform _player;
    private Transform _hunter;
    private AIFollower _follower;

    private AudioSource _oneShots;
    private AudioSource _sting;
    private AudioSource _drone;
    private AudioSource _tension;
    private AudioClip _heartbeat;
    private float _beatTimer;
    private float _intensity;
    private bool _panicking;

    /// <summary>Wired by MazeGenerator, which already knows about both actors.</summary>
    public void Bind(Transform player, AIFollower follower, AudioClip sting, AudioClip drone, AudioClip panic)
    {
        _player = player;
        _follower = follower;
        if (follower != null) _hunter = follower.transform;
        if (sting != null) stingClip = sting;
        if (drone != null) droneClip = drone;
        if (panic != null) panicClip = panic;
    }

    /// <summary>0 = nothing collected yet, 1 = every star taken. Drives everything that swells.</summary>
    public void SetIntensity(float normalized)
    {
        _intensity = Mathf.Clamp01(normalized);

        // Once the panic track is on the drone source, leave its pitch and volume alone
        if (_drone != null && !_panicking)
        {
            _drone.volume = Mathf.Lerp(droneVolume, droneVolumeAtPeak, _intensity);
            _drone.pitch = Mathf.Lerp(dronePitch, dronePitchAtPeak, _intensity);
        }

        if (_tension != null)
        {
            _tension.volume = tensionVolume * _intensity;
        }
    }

    /// <summary>The hatch is open and the hunter is loose. Bring in the panic loop.</summary>
    public void BeginPanic()
    {
        if (panicClip == null || _drone == null) return;

        // Reuse the drone source: the panic track replaces the bed rather than piling on top of it
        _panicking = true;
        _drone.clip = panicClip;
        _drone.pitch = panicPitch;
        _drone.volume = panicVolume;
        _drone.Play();
    }

    /// <summary>
    /// Stop everything this director is playing. Disabling the component is not enough on its own -
    /// the AudioSources keep running, which would leave the panic track playing over the win screen.
    /// </summary>
    public void Silence()
    {
        if (_drone != null) _drone.Stop();
        if (_tension != null) _tension.Stop();
        if (_sting != null) _sting.Stop();
        if (_oneShots != null) _oneShots.Stop();
    }

    private void Awake()
    {
        _oneShots = gameObject.AddComponent<AudioSource>();
        _oneShots.playOnAwake = false;
        _oneShots.spatialBlend = 0f;

        // Its own source: sharing with the heartbeat means the next beat's pitch reset yanks a
        // still-playing sting halfway through.
        _sting = gameObject.AddComponent<AudioSource>();
        _sting.playOnAwake = false;
        _sting.spatialBlend = 0f;

        _drone = gameObject.AddComponent<AudioSource>();
        _drone.playOnAwake = false;
        _drone.loop = true;
        _drone.spatialBlend = 0f;

        _tension = gameObject.AddComponent<AudioSource>();
        _tension.playOnAwake = false;
        _tension.loop = true;
        _tension.spatialBlend = 0f;
        _tension.volume = 0f;

        _heartbeat = BuildHeartbeatClip();
    }

    private void OnEnable()
    {
        if (_follower != null) _follower.ChaseStateChanged += HandleChaseStateChanged;
    }

    private void Start()
    {
        // Bind runs before OnEnable when the component is added at runtime, but subscribing twice is
        // harmless to guard against and missing the subscription is not.
        if (_follower != null)
        {
            _follower.ChaseStateChanged -= HandleChaseStateChanged;
            _follower.ChaseStateChanged += HandleChaseStateChanged;
        }

        if (droneClip != null)
        {
            _drone.clip = droneClip;
            _drone.pitch = dronePitch;
            _drone.volume = droneVolume;
            _drone.Play();
        }

        _tension.clip = BuildTensionBedClip();
        _tension.Play();
    }

    private void OnDisable()
    {
        if (_follower != null) _follower.ChaseStateChanged -= HandleChaseStateChanged;
    }

    private void Update()
    {
        if (_player == null || _hunter == null) return;

        // The heartbeat carries further as the maze empties out
        float range = Mathf.Lerp(heartbeatRange, heartbeatRangeAtPeak, _intensity);

        float distance = Vector3.Distance(_player.position, _hunter.position);
        if (distance > range)
        {
            _beatTimer = 0f;
            return;
        }

        float closeness = 1f - Mathf.Clamp01(distance / range);
        float interval = Mathf.Lerp(slowestInterval, fastestInterval, closeness);

        _beatTimer -= Time.deltaTime;
        if (_beatTimer > 0f) return;

        _beatTimer = interval;
        // One-shots on a variable interval rather than a looping clip with its pitch modulated:
        // pitching a loop couples tempo to pitch and the heartbeat turns into a chipmunk.
        _oneShots.pitch = 1f;

        // Curved rather than straight: a linear ramp spends most of the range around half volume,
        // which reads as a constant thud. This stays faint until it is close, then climbs fast.
        float loudness = Mathf.Pow(closeness, proximityFalloff);
        _oneShots.PlayOneShot(_heartbeat, Mathf.Lerp(minHeartbeatVolume, heartbeatVolume, loudness));
    }

    private void HandleChaseStateChanged(bool chasing)
    {
        if (!chasing || stingClip == null) return;

        _sting.clip = stingClip;
        _sting.pitch = stingPitch;
        _sting.volume = stingVolume;
        _sting.Play();
        // The clip may well be a long ambience loop rather than a stinger, so bound it rather than
        // trusting its length.
        _sting.SetScheduledEndTime(AudioSettings.dspTime + stingDuration);
    }

    /// <summary>The sting for being caught: louder and lower than the chase one, with the bed cut out under it.</summary>
    public void PlayCaptureSting()
    {
        if (_drone != null) _drone.Stop();
        if (_tension != null) _tension.Stop();
        if (stingClip == null || _sting == null) return;

        _sting.clip = stingClip;
        _sting.pitch = stingPitch * 0.8f;
        _sting.volume = 1f;
        _sting.Play();
        _sting.SetScheduledEndTime(AudioSettings.dspTime + 2.5f);
    }

    /// <summary>
    /// A two-thump heartbeat built from scratch, because the project has no horror source material.
    /// </summary>
    private static AudioClip BuildHeartbeatClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.45f;
        const float dubDelay = 0.20f;

        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        AddThump(samples, sampleRate, 0f, 55f, 0.045f, 1.0f);
        AddThump(samples, sampleRate, dubDelay, 48f, 0.035f, 0.8f);

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f)
        {
            float gain = 0.8f / peak;
            for (int i = 0; i < sampleCount; i++) samples[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("Heartbeat", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// The rising-dread bed, also built from scratch. Two pairs of detuned low tones beat against each
    /// other a few times a second, which the ear reads as something wrong rather than as music.
    ///
    /// Every frequency is a multiple of 0.25 Hz and the clip is exactly four seconds, so each one
    /// completes a whole number of cycles and the loop point is silent.
    /// </summary>
    private static AudioClip BuildTensionBedClip()
    {
        const int sampleRate = 44100;
        const float duration = 4f;

        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;

            // 55 against 58.25 beats about three times a second - a slow, uneasy throb
            float value = 0.5f * Mathf.Sin(2f * Mathf.PI * 55f * t)
                        + 0.5f * Mathf.Sin(2f * Mathf.PI * 58.25f * t)
                        + 0.18f * Mathf.Sin(2f * Mathf.PI * 110f * t)
                        + 0.18f * Mathf.Sin(2f * Mathf.PI * 116.5f * t)
                        + 0.10f * Mathf.Sin(2f * Mathf.PI * 233f * t);

            samples[i] = value;
        }

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f)
        {
            float gain = 0.7f / peak;
            for (int i = 0; i < sampleCount; i++) samples[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("TensionBed", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static void AddThump(float[] samples, int sampleRate, float startSeconds, float frequency, float decay, float amplitude)
    {
        int start = Mathf.RoundToInt(startSeconds * sampleRate);
        System.Random noise = new System.Random(unchecked((int)(frequency * 1000f)));

        for (int i = start; i < samples.Length; i++)
        {
            float t = (i - start) / (float)sampleRate;
            float envelope = Mathf.Exp(-t / decay);
            if (envelope < 0.0005f) break;

            // 3 ms fade-in kills the click that a hard attack on a sine produces
            envelope *= Mathf.Clamp01(t / 0.003f);

            float value = Mathf.Sin(2f * Mathf.PI * frequency * t);
            // A second harmonic, and a brief noise transient over the first few milliseconds. Without
            // these the 55 Hz fundamental is simply inaudible on laptop speakers.
            value += 0.35f * Mathf.Sin(2f * Mathf.PI * frequency * 2f * t);
            if (t < 0.008f)
            {
                value += 0.3f * (float)(noise.NextDouble() * 2.0 - 1.0) * (1f - t / 0.008f);
            }

            samples[i] += value * envelope * amplitude;
        }
    }
}

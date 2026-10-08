using TMPro;
using UnityEngine;

/// <summary>
/// F91: small life for the Intake - a swaying hanging lamp, buzzing fluorescents, bad-bulb signs and distant noises from
/// beyond the walls. Added to the IntakeRoom root and wired by IntakeRoomBuilder. It only does anything while
/// GameFlow.IsInIntake, so it costs nothing in the maze. Never alerts the hunter (no HearNoise).
/// </summary>
public class IntakeAmbience : MonoBehaviour
{
    [SerializeField] private Transform[] swayers;
    [SerializeField] private Light[] buzzLights;
    [SerializeField] private Renderer[] buzzRenderers;
    [SerializeField] private Transform[] soundPoints;
    [SerializeField] private TMP_Text[] flickerSigns;

    private Quaternion[] _swayBase;
    private float[] _lightBase;
    private Material[] _buzzMats;
    private Color[] _buzzEmission;
    private float[] _nextDip, _dipEnd;
    private Color[] _signBase;
    private float[] _nextStutter, _stutterEnd;
    private AudioClip[] _clips;
    private float _nextSound;
    private bool _ready;

    private void Init()
    {
        _ready = true;

        _swayBase = new Quaternion[swayers != null ? swayers.Length : 0];
        for (int i = 0; i < _swayBase.Length; i++) _swayBase[i] = swayers[i] != null ? swayers[i].localRotation : Quaternion.identity;

        int n = buzzLights != null ? buzzLights.Length : 0;
        _lightBase = new float[n];
        _buzzMats = new Material[n];
        _buzzEmission = new Color[n];
        _nextDip = new float[n];
        _dipEnd = new float[n];
        for (int i = 0; i < n; i++)
        {
            _lightBase[i] = buzzLights[i] != null ? buzzLights[i].intensity : 0f;
            Renderer r = buzzRenderers != null && i < buzzRenderers.Length ? buzzRenderers[i] : null;
            if (r != null)
            {
                _buzzMats[i] = r.material;
                if (_buzzMats[i].HasProperty("_EmissionColor")) _buzzEmission[i] = _buzzMats[i].GetColor("_EmissionColor");
            }
            _nextDip[i] = Time.time + Random.Range(3f, 10f);
        }

        int s = flickerSigns != null ? flickerSigns.Length : 0;
        _signBase = new Color[s];
        _nextStutter = new float[s];
        _stutterEnd = new float[s];
        for (int i = 0; i < s; i++)
        {
            _signBase[i] = flickerSigns[i] != null ? flickerSigns[i].color : Color.white;
            _nextStutter[i] = Time.time + Random.Range(4f, 12f);
        }

        // Synthesised once, the first time the Intake runs (not in Awake: the room exists in every maze scene too).
        _clips = new[]
        {
            DoorAudio.BuildClunkClip(0.45f, 140f, 0.6f),  // a far clank
            DoorAudio.BuildClunkClip(0.14f, 520f, 0.25f), // a pipe knock
            DoorAudio.BuildClunkClip(0.06f, 2400f, 0.3f), // a drip
            DoorAudio.BuildClunkClip(0.6f, 70f, 0.8f),    // something heavy, far off
        };
        _nextSound = Time.time + Random.Range(9f, 20f);
    }

    private void Update()
    {
        if (!GameFlow.IsInIntake) return;
        if (!_ready) Init();
        float time = Time.time;

        for (int i = 0; i < _swayBase.Length; i++)
        {
            if (swayers[i] == null) continue;
            float phase = i * 1.7f;
            float a = Mathf.Sin(time * Mathf.PI * 2f * 0.35f + phase) * 4f;
            float b = Mathf.Sin(time * Mathf.PI * 2f * 0.27f + phase * 0.6f) * 2.5f;
            swayers[i].localRotation = _swayBase[i] * Quaternion.Euler(a, 0f, b);
        }

        for (int i = 0; i < _lightBase.Length; i++)
        {
            if (buzzLights[i] == null) continue;
            float factor = 1f + (Mathf.PerlinNoise(time * 11f, i * 5.3f) - 0.5f) * 0.12f;
            if (time >= _nextDip[i])
            {
                _dipEnd[i] = time + Random.Range(0.08f, 0.2f);
                _nextDip[i] = _dipEnd[i] + Random.Range(6f, 14f);
            }
            if (time < _dipEnd[i]) factor = 0.15f;
            buzzLights[i].intensity = _lightBase[i] * factor;
            if (_buzzMats[i] != null && _buzzMats[i].HasProperty("_EmissionColor")) _buzzMats[i].SetColor("_EmissionColor", _buzzEmission[i] * factor);
        }

        for (int i = 0; i < _signBase.Length; i++)
        {
            if (flickerSigns[i] == null) continue;
            if (time >= _nextStutter[i])
            {
                _stutterEnd[i] = time + Random.Range(0.15f, 0.4f);
                _nextStutter[i] = _stutterEnd[i] + Random.Range(5f, 12f);
            }
            Color c = _signBase[i];
            if (time < _stutterEnd[i]) c.a *= Random.value < 0.5f ? Random.Range(0.1f, 0.5f) : 1f;
            flickerSigns[i].color = c;
        }

        if (time >= _nextSound && soundPoints != null && soundPoints.Length > 0)
        {
            _nextSound = time + Random.Range(9f, 20f);
            Transform point = soundPoints[Random.Range(0, soundPoints.Length)];
            if (point != null) AudioSource.PlayClipAtPoint(_clips[Random.Range(0, _clips.Length)], point.position, Random.Range(0.25f, 0.45f));
        }
    }
}

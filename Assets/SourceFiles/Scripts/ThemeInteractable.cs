using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>
/// F74's four theme-specific interactables (B2.8) - one class, one Kind per theme, since each is a
/// single simple press/hold behaviour rather than anything big enough to earn its own file: Ward's call
/// bell (a remote lure), Boiler's steam valve (a temporary NavMeshObstacle the hunter reroutes around),
/// Crypt's candles (a small permanent light, once), Lab's terminal (a brief on-screen hunter marker).
/// Cloned from the matching InteractableKit slot by MazeGenerator.BuildThemeInteractables - never built
/// in kit mode, and never on the primitive fallback (no FloorTheme to key off).
/// </summary>
public class ThemeInteractable : MonoBehaviour, IInteractable
{
    public enum Kind { CallBell, SteamValve, Candles, Terminal }

    [Tooltip("Recolours to say ready/cooling down. Either may be left unassigned.")]
    [SerializeField] private Renderer indicatorRenderer;
    [SerializeField] private Light indicatorLight;

    private static readonly Color ReadyColor = new Color(0.2f, 0.85f, 0.3f);
    private static readonly Color CoolingColor = new Color(0.85f, 0.15f, 0.1f);

    private Kind _kind;
    private AIFollower _hunter;
    private PlayerHud _hud;
    private AudioSource _audio;

    // Call bell
    private Vector3 _remoteCellPos;
    private AudioSource _remoteAudio;
    private AudioClip _chimeClip;

    // Steam valve
    private NavMeshObstacle _steamObstacle;
    private ParticleSystem _steamParticles;
    private float _steamUntil = -1f;

    // Candles
    private bool _candlesLit;

    // Terminal
    private Camera _playerCamera;
    private GameObject _markerHolder;
    private RectTransform _markerRect;
    private bool _terminalActive;
    private float _terminalUntil;

    private float _cooldownUntil;

    public void ConfigureCallBell(Vector3 remoteCellCenter, AIFollower hunter)
    {
        _kind = Kind.CallBell;
        _hunter = hunter;
        _remoteCellPos = remoteCellCenter;

        GameObject remote = new GameObject("CallBellChime");
        remote.transform.position = remoteCellCenter + Vector3.up * 1.6f;
        _remoteAudio = remote.AddComponent<AudioSource>();
        _remoteAudio.playOnAwake = false;
        _remoteAudio.spatialBlend = 1f;
        _remoteAudio.maxDistance = 22f;
        _chimeClip = DoorAudio.BuildClunkClip(0.5f, 720f, 0.05f);

        EnsureAudio();
    }

    public void ConfigureSteamValve(NavMeshObstacle obstacle, ParticleSystem particles, AIFollower hunter)
    {
        _kind = Kind.SteamValve;
        _hunter = hunter;
        _steamObstacle = obstacle;
        _steamParticles = particles;
        if (_steamObstacle != null) _steamObstacle.enabled = false;
        EnsureAudio();
    }

    public void ConfigureCandles(AIFollower hunter)
    {
        _kind = Kind.Candles;
        _hunter = hunter;
        if (indicatorLight != null) indicatorLight.enabled = false;
        EnsureAudio();
    }

    public void ConfigureTerminal(AIFollower hunter, Camera playerCamera)
    {
        _kind = Kind.Terminal;
        _hunter = hunter;
        _playerCamera = playerCamera;
        BuildMarker();
        EnsureAudio();
    }

    /// <summary>Called later by MazeGenerator.SetUpAtmosphere - same late-wiring pattern as WallButton.BindHud.</summary>
    public void BindHud(PlayerHud hud) => _hud = hud;

    private void EnsureAudio()
    {
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 1f;
        _audio.maxDistance = 16f;
    }

    private void BuildMarker()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        _markerHolder = new GameObject("HunterMarker");
        _markerHolder.transform.SetParent(canvas.transform, false);
        _markerHolder.layer = canvas.gameObject.layer;

        Image image = _markerHolder.AddComponent<Image>();
        image.color = new Color(0.9f, 0.15f, 0.1f, 0.9f);
        image.raycastTarget = false;

        _markerRect = image.rectTransform;
        _markerRect.anchorMin = Vector2.zero;
        _markerRect.anchorMax = Vector2.zero;
        _markerRect.pivot = new Vector2(0.5f, 0.5f);
        _markerRect.sizeDelta = new Vector2(18f, 18f);

        _markerHolder.SetActive(false);
    }

    string IInteractable.Prompt => _kind switch
    {
        Kind.CallBell => "RING BELL",
        Kind.SteamValve => "OPEN VALVE",
        Kind.Candles => _candlesLit ? "" : "LIGHT CANDLES",
        Kind.Terminal => "USE TERMINAL",
        _ => ""
    };

    float IInteractable.HoldSeconds => _kind == Kind.SteamValve ? 1f : 0f;

    bool IInteractable.CanInteract => _kind == Kind.Candles ? !_candlesLit : Time.time >= _cooldownUntil;

    void IInteractable.Interact(PlayerInteractor who)
    {
        switch (_kind)
        {
            case Kind.CallBell: RingBell(); break;
            case Kind.SteamValve: OpenValve(); break;
            case Kind.Candles: LightCandles(); break;
            case Kind.Terminal: UseTerminal(); break;
        }
    }

    void IInteractable.OnFocus(bool focused) { }

    private void RingBell()
    {
        _cooldownUntil = Time.time + 45f;
        if (_remoteAudio != null && _chimeClip != null) _remoteAudio.PlayOneShot(_chimeClip, 0.85f);
        _hunter?.HearNoise(_remoteCellPos, 40f);
    }

    private void OpenValve()
    {
        _cooldownUntil = Time.time + 40f;
        _steamUntil = Time.time + 12f;
        if (_steamObstacle != null) _steamObstacle.enabled = true;
        if (_steamParticles != null) _steamParticles.Play();
        _hunter?.HearNoise(transform.position, 14f);
    }

    private void LightCandles()
    {
        _candlesLit = true;
        if (indicatorLight != null)
        {
            indicatorLight.enabled = true;
            indicatorLight.range = 4f;
        }
        LightPool.Register(transform, 4f, () => 1f);
    }

    private void UseTerminal()
    {
        _cooldownUntil = Time.time + 50f;
        _terminalActive = true;
        _terminalUntil = Time.time + 6f;
        _hud?.ShowSubtitle("CAM 3: MOVEMENT", 3f);
    }

    private void Update()
    {
        if (_kind == Kind.SteamValve && _steamObstacle != null && _steamUntil > 0f && Time.time >= _steamUntil)
        {
            _steamUntil = -1f;
            _steamObstacle.enabled = false;
            if (_steamParticles != null) _steamParticles.Stop();
        }

        if (_kind == Kind.Terminal)
        {
            if (_terminalActive && Time.time >= _terminalUntil)
            {
                _terminalActive = false;
                if (_markerHolder != null) _markerHolder.SetActive(false);
            }
            else if (_terminalActive)
            {
                UpdateMarker();
            }
        }

        UpdateIndicator();
    }

    private void UpdateMarker()
    {
        if (_hunter == null || _playerCamera == null || _markerRect == null) return;

        Vector3 screenPoint = _playerCamera.WorldToScreenPoint(_hunter.transform.position);
        bool behind = screenPoint.z < 0f;
        if (behind)
        {
            screenPoint.x = Screen.width - screenPoint.x;
            screenPoint.y = Screen.height - screenPoint.y;
        }

        const float margin = 24f;
        float x = Mathf.Clamp(screenPoint.x, margin, Screen.width - margin);
        float y = Mathf.Clamp(screenPoint.y, margin, Screen.height - margin);

        _markerHolder.SetActive(true);
        _markerRect.position = new Vector3(x, y, 0f);
    }

    /// <summary>Ready/cooling colour only makes sense for the two cooldown-gated kinds - Candles' indicatorLight is the candle flame itself (see LightCandles) and Terminal has no status lamp.</summary>
    private void UpdateIndicator()
    {
        if (_kind == Kind.Candles || _kind == Kind.Terminal) return;
        if (indicatorRenderer == null && indicatorLight == null) return;

        Color c = Time.time >= _cooldownUntil ? ReadyColor : CoolingColor;
        if (indicatorRenderer != null && indicatorRenderer.material.HasProperty("_EmissionColor"))
        {
            indicatorRenderer.material.SetColor("_EmissionColor", c * 2f);
        }
        if (indicatorLight != null) indicatorLight.color = c;
    }

    private void OnDestroy()
    {
        if (_kind == Kind.Candles && _candlesLit) LightPool.Unregister(transform);
        if (_remoteAudio != null) Destroy(_remoteAudio.gameObject);
        if (_markerHolder != null) Destroy(_markerHolder);
    }
}

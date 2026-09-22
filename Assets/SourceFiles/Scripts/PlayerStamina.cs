using StarterAssets;
using UnityEngine;

/// <summary>
/// Sprint is not free. A full bar buys 24 s of sprint; emptying it locks sprint out until the bar
/// climbs back to a quarter, so a held Shift key cannot stutter in and out of sprint at zero.
/// Added to the player by MazeGenerator.PlacePlayer, next to PlayerStealthState.
/// </summary>
[RequireComponent(typeof(ThirdPersonController))]
public class PlayerStamina : MonoBehaviour
{
    [Tooltip("Seconds of continuous sprint from a full bar to empty")]
    [SerializeField] private float sprintSeconds = 24f;
    [Tooltip("Seconds to refill from empty to full once recovery starts")]
    [SerializeField] private float recoverSeconds = 16f;
    [Tooltip("Pause after releasing sprint before recovery starts")]
    [SerializeField] private float recoverDelay = 1.5f;
    [Tooltip("After hitting empty, sprint unlocks again once the bar reaches this fraction")]
    [Range(0f, 1f)]
    [SerializeField] private float reengageFraction = 0.25f;

    /// <summary>Per-floor budget: longer sprints on the early floors.</summary>
    public void SetSprintSeconds(float seconds) { sprintSeconds = Mathf.Max(1f, seconds); }

    private ThirdPersonController _controller;
    private float _delay;

    /// <summary>0 (empty) to 1 (full).</summary>
    public float Fraction { get; private set; } = 1f;

    /// <summary>True from the moment the bar hits empty until it climbs back to reengageFraction.</summary>
    public bool Exhausted { get; private set; }

    /// <summary>True while the controller is actually sprinting this frame.</summary>
    public bool IsSprinting => _controller != null && _controller.IsSprinting;

    private void Awake()
    {
        _controller = GetComponent<ThirdPersonController>();
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (_controller.IsSprinting)
        {
            Fraction -= dt / Mathf.Max(0.01f, sprintSeconds);
            _delay = recoverDelay;

            if (Fraction <= 0f)
            {
                Fraction = 0f;
                Exhausted = true;
            }
        }
        else
        {
            _delay -= dt;
            if (_delay <= 0f)
            {
                Fraction += dt / Mathf.Max(0.01f, recoverSeconds);
            }
        }

        Fraction = Mathf.Clamp01(Fraction);

        if (Exhausted && Fraction >= reengageFraction)
        {
            Exhausted = false;
        }

        _controller.SprintLocked = Exhausted;
    }
}

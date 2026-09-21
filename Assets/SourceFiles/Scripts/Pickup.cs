using System;
using UnityEngine;

public class Pickup : MonoBehaviour
{
    public static event Action OnCoinCollected;

    /// <summary>Raised with the star's world position, just before OnCoinCollected.</summary>
    public static event Action<Vector3> OnCollectedAt;

    [Header("Effects")]
    public GameObject particleEffectPrefab; // Assign particle system prefab in Inspector

    [Header("Motion Settings")]
    public float rotationSpeed = 100f; // Rotation speed in degrees per second
    public float bobbingAmount = 0.1f; // Amplitude of bobbing motion
    public float bobbingSpeed = 1f; // Speed of bobbing motion

    private Vector3 startPosition;
    private float timer;

    void Start()
    {
        // Remember the original position of the GameObject
        startPosition = transform.position;
    }

    void Update()
    {
        // Rotate the object around its up axis
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);

        // Create a bobbing motion up and down
        timer += Time.deltaTime * bobbingSpeed;
        float newY = startPosition.y + Mathf.Sin(timer) * bobbingAmount;
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }

    void OnTriggerEnter(Collider other)
    {
        // Check if the colliding object has the "Player" tag
        if (other.CompareTag("Player"))
        {
            // Instantiate the particle effect. The prefab's stop action is None and it never
            // self-destructs, so clean it up or every pickup leaks a GameObject and an AudioSource.
            if (particleEffectPrefab != null)
            {
                Destroy(Instantiate(particleEffectPrefab, transform.position, Quaternion.identity), 3f);
            }

            // Raised first so the hunter hears the star at the current (pre-pickup) threat level -
            // TensionDirector.HandleProgress has not re-applied threat for this star yet.
            OnCollectedAt?.Invoke(transform.position);

            // Notify listeners that a coin was collected
            OnCoinCollected?.Invoke();

            // Destroy the star
            Destroy(gameObject);

        }
    }
}

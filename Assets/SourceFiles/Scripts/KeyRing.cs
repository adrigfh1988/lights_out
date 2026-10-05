using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F77 Key Hunt: the keys the player holds this floor. An instance component on the player (like
/// ThrowController), so a scene reload recreates it empty - no static, no reset rule needed. Source of
/// truth for TensionDirector's counter, MazeEscape's padlock and the compass.
/// </summary>
public class KeyRing : MonoBehaviour
{
    /// <summary>Raised whenever Held/Total changes - MazeEscape and TensionDirector both subscribe.</summary>
    public event System.Action Changed;

    public int Held { get; private set; }
    public int Total { get; private set; }

    /// <summary>True on a floor with no keys, or once every placed key is held.</summary>
    public bool IsComplete => Held >= Total;

    private readonly List<KeyPickup> _missing = new List<KeyPickup>();

    /// <summary>Every still-missing key (F75 Fuse Map compass targeting).</summary>
    public IReadOnlyList<KeyPickup> Missing => _missing;

    /// <summary>
    /// Called once by MazeGenerator.SetUpAtmosphere with every key BuildKeys placed (F77 decision 11:
    /// Total is what was actually placed, not FloorProfile.KeyCount - a maze that only fits 1 of 3 keys
    /// locks the hatch behind 1 key).
    /// </summary>
    public void Configure(IReadOnlyList<KeyPickup> keys)
    {
        _missing.Clear();
        if (keys != null) _missing.AddRange(keys);
        Total = _missing.Count;
        Held = 0;
        Changed?.Invoke();
    }

    /// <summary>Called by KeyPickup.Interact. Idempotent per key.</summary>
    public void Collect(KeyPickup key)
    {
        if (!_missing.Remove(key)) return;
        Held++;
        Changed?.Invoke();
    }

    /// <summary>Nearest still-missing key (flat distance). False when none are missing.</summary>
    public bool TryNearestMissing(Vector3 from, out Vector3 position)
    {
        position = default;
        bool found = false;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < _missing.Count; i++)
        {
            KeyPickup key = _missing[i];
            if (key == null) continue;

            Vector3 flat = key.transform.position - from;
            flat.y = 0f;
            float sqr = flat.sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                position = key.transform.position;
                found = true;
            }
        }

        return found;
    }
}

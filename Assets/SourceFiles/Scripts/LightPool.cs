using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F73: a static registry of every light source that should count toward "how lit is this point" -
/// wall lamps register themselves in WallLamp.Configure; F74's candles and lanterns will do the same.
/// PlayerStealthState.LampExposure reads ExposureAt instead of walking MazeGenerator's lamp list
/// directly, so a future light source only has to register here once to matter for stealth.
///
/// Scene-scoped, not campaign-scoped (CLAUDE.md's static-reset rule): cleared by the
/// SubsystemRegistration reset AND by MazeGenerator.OnDestroy, so a maze rebuild never carries stale
/// entries over from the floor just left. A destroyed registrant that never called Unregister (should
/// not happen - WallLamp does in its own OnDestroy) is skipped and pruned lazily via Unity's fake-null.
/// </summary>
public static class LightPool
{
    private struct Entry
    {
        public Transform Source;
        public float Radius;
        public Func<float> Intensity01;
    }

    private static readonly List<Entry> Entries = new List<Entry>();

    /// <summary>`intensity01` is read every query, not cached, so a flickering/unpowered light stays correct without re-registering.</summary>
    public static void Register(Transform source, float radius, Func<float> intensity01)
    {
        if (source == null || intensity01 == null) return;
        Entries.Add(new Entry { Source = source, Radius = radius, Intensity01 = intensity01 });
    }

    public static void Unregister(Transform source)
    {
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            if (Entries[i].Source == source) Entries.RemoveAt(i);
        }
    }

    /// <summary>0..1, how strongly the nearest registered light that reaches `point` actually lights it.</summary>
    public static float ExposureAt(Vector3 point)
    {
        float best = 0f;
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            Entry entry = Entries[i];
            if (entry.Source == null)
            {
                Entries.RemoveAt(i); // a registrant destroyed without calling Unregister
                continue;
            }

            float radius = entry.Radius;
            if (radius <= 0f) continue;

            float d = Vector3.Distance(entry.Source.position, point);
            if (d < radius)
            {
                float k = entry.Intensity01();
                best = Mathf.Max(best, (1f - d / radius) * k);
            }
        }

        return best;
    }

    /// <summary>Called by MazeGenerator.OnDestroy - a scene-scoped static, per CLAUDE.md's reset rule.</summary>
    public static void Clear()
    {
        Entries.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Entries.Clear();
    }
}

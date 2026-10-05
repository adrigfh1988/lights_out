using UnityEngine;

/// <summary>
/// Procedural clunk/rattle/click clips for doors and buttons (F72), same construction technique as
/// MazeGenerator.BuildShardChimeClip - a short noise/tone mix under an exponential decay envelope. No
/// new audio assets needed.
/// </summary>
public static class DoorAudio
{
    /// <summary>A low tone/noise thud - used for the shutter's slide and the hunter's slam (louder, lower, noisier).</summary>
    public static AudioClip BuildClunkClip(float duration, float baseFrequency, float noiseAmount)
    {
        const int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        System.Random rng = new System.Random(1289);

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Exp(-t / Mathf.Max(0.02f, duration * 0.35f));
            envelope *= Mathf.Clamp01(t / 0.004f); // kills the attack click

            float tone = Mathf.Sin(2f * Mathf.PI * baseFrequency * t);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            samples[i] = Mathf.Lerp(tone, noise, noiseAmount) * envelope;
        }

        Normalise(samples, 0.85f);
        AudioClip clip = AudioClip.Create("DoorClunk", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>A tiny high click - the button press.</summary>
    public static AudioClip BuildClickClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.06f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Exp(-t / 0.015f);
            samples[i] = Mathf.Sin(2f * Mathf.PI * 1800f * t) * envelope;
        }

        Normalise(samples, 0.7f);
        AudioClip clip = AudioClip.Create("ButtonClick", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static void Normalise(float[] samples, float targetPeak)
    {
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak <= 0.0001f) return;

        float gain = targetPeak / peak;
        for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
    }
}

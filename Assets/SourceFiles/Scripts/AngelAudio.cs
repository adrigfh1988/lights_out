using UnityEngine;

/// <summary>
/// Synthesised sounds for the Weeping Angel: a dissonant stone-choir that follows it, and the sting
/// when it starts hunting you. Built in code like the rest of the dread audio, so there is no clip to
/// import - and nothing here is noise-based, which is what made the old pitched-down sci-fi hum read
/// as radio static.
/// </summary>
public static class AngelAudio
{
    private const int SampleRate = 44100;

    // A cluster with a minor second and a tritone in it: D3, E-flat3, G-flat3, A3, B-flat3.
    // Every frequency is a multiple of 0.25 Hz so it completes whole cycles in the 4 s loop.
    private static readonly float[] ChoirNotes = { 146.75f, 155.5f, 185f, 220f, 233f };

    /// <summary>A four-second seamless loop: wavering, hollow voices, like a distant choir in a stone hall.</summary>
    public static AudioClip BuildChoirLoop()
    {
        const float duration = 4f;
        int count = Mathf.RoundToInt(SampleRate * duration);
        float[] samples = new float[count];

        for (int n = 0; n < ChoirNotes.Length; n++)
        {
            float f = ChoirNotes[n];
            // Slow vibrato and swell, each at a whole number of cycles per loop, offset per voice.
            float vibratoRate = 4.75f + 0.25f * n;
            float swellRate = 0.25f * (1 + n % 3);
            float swellPhase = n * 1.3f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float phase = 2f * Mathf.PI * f * t + 0.012f * f * Mathf.Sin(2f * Mathf.PI * vibratoRate * t);

                // Odd-ish harmonics weighted towards an "ooh"/"ahh" vowel
                float voice = Mathf.Sin(phase)
                            + 0.55f * Mathf.Sin(2f * phase)
                            + 0.30f * Mathf.Sin(3f * phase)
                            + 0.12f * Mathf.Sin(4f * phase);

                float swell = 0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * swellRate * t + swellPhase);
                samples[i] += voice * swell;
            }
        }

        Normalise(samples, 0.7f);

        AudioClip clip = AudioClip.Create("AngelChoir", count, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>A stone crack followed by a swelling, screaming choir stab that falls away. One-shot.</summary>
    public static AudioClip BuildSting()
    {
        const float duration = 2.5f;
        int count = Mathf.RoundToInt(SampleRate * duration);
        float[] samples = new float[count];
        System.Random noise = new System.Random(1337);
        float lowpass = 0f;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;

            // Stone crack: low-passed noise that dies in ~120 ms
            float raw = (float)(noise.NextDouble() * 2.0 - 1.0);
            lowpass += (raw - lowpass) * 0.12f;
            float crack = lowpass * Mathf.Exp(-t * 30f) * 2.2f;

            // Choir stab: an octave up from the loop, sharp attack, long decay, pitch sagging slightly
            float attack = Mathf.Clamp01(t / 0.04f);
            float decay = Mathf.Exp(-t * 1.6f);
            float sag = 1f - 0.04f * t;
            float stab = 0f;
            for (int n = 0; n < ChoirNotes.Length; n++)
            {
                float phase = 2f * Mathf.PI * ChoirNotes[n] * 2f * sag * t
                            + 0.02f * ChoirNotes[n] * Mathf.Sin(2f * Mathf.PI * 6f * t);
                stab += Mathf.Sin(phase) + 0.5f * Mathf.Sin(2f * phase) + 0.25f * Mathf.Sin(3f * phase);
            }

            samples[i] = crack + stab * attack * decay * 0.35f;
        }

        Normalise(samples, 0.9f);

        AudioClip clip = AudioClip.Create("AngelSting", count, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static void Normalise(float[] samples, float target)
    {
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak < 0.0001f) return;

        float gain = target / peak;
        for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
    }
}

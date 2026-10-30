using UnityEngine;

// Shared state for the editor window: output samples (written on the audio thread) and which keys are lit.
public static class SynthScope
{
    public const int Size = 4096;
    public static readonly float[] buf = new float[Size];
    public static int pos;
    public static readonly bool[] keysDown = new bool[12];
    public static readonly float[] seqFrom = new float[12], seqUntil = new float[12];

    public static void Push(float v) { buf[pos & (Size - 1)] = v; pos++; }

    public static void Mark(float freq, double secondsFromNow, float len)
    {
        int pc = ((Mathf.RoundToInt(69f + 12f * Mathf.Log(freq / 440f, 2f)) % 12) + 12) % 12;
        float t = Time.realtimeSinceStartup + (float)secondsFromNow;
        seqFrom[pc] = t; seqUntil[pc] = t + len;
    }
}

// Global ADSR used by every note source (seconds; sustain is a 0..1 level).
public static class SynthAdsr
{
    public static float attack = 0.01f, decay = 0.15f, sustain = 0.7f, release = 0.3f;
}

// Current instrument (Juno-style presets). Read by ScriptableAudioPipelineAndEffects.NoteOn for events that don't set their own patch.
public static class SynthPatch
{
    public static readonly string[] Names = { "Classic", "Pad", "Pluck", "Bass", "Brass", "Strings" };
    public static int index = 1;
    public static int unison = 3, chorus = 2;
    public static float detune = 12f, sub = 0.4f, fcut = 1800f, fenvOct = 1.5f, fres = 0.15f, fdecay = 1.2f;

    // wave, unison, detune(cents), sub, fcut(Hz, 0 = off), fenvOct, fres, fdecay(s), chorus, A, D, S, R
    static readonly float[][] P =
    {
        new float[] { 1, 1, 0,  0,    0,    0,   0,    0,    0, 0.01f,  0.15f, 0.7f, 0.3f },
        new float[] { 2, 3, 12, 0.4f, 1800, 1.5f, 0.15f, 1.2f, 2, 0.35f,  0.6f,  0.7f, 1.2f },
        new float[] { 2, 2, 8,  0.3f, 500,  4f,   0.3f,  0.25f, 1, 0.005f, 0.25f, 0.2f, 0.3f },
        new float[] { 3, 1, 0,  0.8f, 300,  3f,   0.4f,  0.3f,  0, 0.005f, 0.2f,  0.8f, 0.15f },
        new float[] { 2, 3, 15, 0.2f, 700,  3f,   0.1f,  0.5f,  2, 0.08f,  0.3f,  0.8f, 0.3f },
        new float[] { 3, 2, 6,  0.3f, 3000, 0.5f, 0.1f,  1f,    3, 0.4f,   0.5f,  0.8f, 1f },
    };

    // Applies preset i (also sets the global ADSR) and returns its oscillator wave.
    public static int Apply(int i)
    {
        index = i;
        var p = P[i];
        unison = (int)p[1]; detune = p[2]; sub = p[3]; fcut = p[4]; fenvOct = p[5]; fres = p[6]; fdecay = p[7]; chorus = (int)p[8];
        SynthAdsr.attack = p[9]; SynthAdsr.decay = p[10]; SynthAdsr.sustain = p[11]; SynthAdsr.release = p[12];
        return (int)p[0];
    }
}

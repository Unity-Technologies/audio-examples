using UnityEngine;

// Simplest step sequencer: 16 steps, one note name each ("C4", "F#3", "" = rest).
public enum ArpMode { Off, Up, Down, UpDown }

[RequireComponent(typeof(ScriptableAudioPipelineAndEffects))]
public class StepSequencer : MonoBehaviour
{
    public bool run = false; // start/stop the sequence
    public float bpm = 120f;
    public string[] steps = { "C3", "", "E3", "", "G3", "", "E3", "", "C3", "", "E3", "", "A3", "", "G3", "" };
    [Range(0, 3)] public int wave = 2; // 0 sine, 1 tri, 2 saw, 3 square
    public float noteLength = 0.15f;
    [Header("Arp (steps can hold chords: \"C3 E3 G3\")")]
    public ArpMode arp = ArpMode.Off;
    [Range(1, 8)] public int arpDiv = 4; // notes per step
    ScriptableAudioPipelineAndEffects synth;
    double nextTime;
    int step;
    bool wasRunning;
    const double Lookahead = 0.15;

    void Start()
    {
        synth = GetComponent<ScriptableAudioPipelineAndEffects>();
        nextTime = AudioSettings.dspTime + 0.3;
    }

    void Update()
    {
        if (!run) { wasRunning = false; return; }
        if (!wasRunning) { wasRunning = true; nextTime = AudioSettings.dspTime + 0.1; step = 0; }
        double stepDur = 60.0 / bpm / 4.0; // 16th notes
        double now = AudioSettings.dspTime;
        while (nextTime < now + Lookahead)
        {
            var freqs = new System.Collections.Generic.List<float>();
            foreach (var tok in (steps.Length > 0 ? steps[step % steps.Length] : "").Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
                if (TryParse(tok, out float f)) freqs.Add(f);
            freqs.Sort();
            if (arp == ArpMode.Off)
                foreach (var f in freqs) Play(f, nextTime - now, noteLength);
            else if (freqs.Count > 0)
            {
                double sub = stepDur / arpDiv;
                for (int j = 0; j < arpDiv; j++)
                    Play(freqs[ArpIndex(j, freqs.Count)], nextTime - now + j * sub, Mathf.Min(noteLength, (float)sub * 0.9f));
            }
            nextTime += stepDur;
            step++;
        }
    }

    int ArpIndex(int j, int n)
    {
        if (arp == ArpMode.Up) return j % n;
        if (arp == ArpMode.Down) return n - 1 - j % n;
        int period = Mathf.Max(1, 2 * n - 2), k = j % period; // up then down
        return k < n ? k : period - k;
    }

    void Play(float freq, double secondsFromNow, float len)
    {
        SynthScope.Mark(freq, secondsFromNow, len);
        synth.NoteOn(new NoteEvent
        {
            freq = freq, wave = wave, gain = 1f, pan = 0.5f,
            attack = SynthAdsr.attack, decay = SynthAdsr.decay, sustainLevel = SynthAdsr.sustain, release = SynthAdsr.release, sustain = len,
            delaySamples = Mathf.Max(0, (int)(secondsFromNow * AudioSettings.outputSampleRate)),
        });
    }

    static bool TryParse(string s, out float freq)
    {
        freq = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim().ToUpperInvariant();
        int semi;
        switch (s[0]) { case 'C': semi = 0; break; case 'D': semi = 2; break; case 'E': semi = 4; break;
            case 'F': semi = 5; break; case 'G': semi = 7; break; case 'A': semi = 9; break; case 'B': semi = 11; break; default: return false; }
        int i = 1;
        if (i < s.Length && (s[i] == '#' || s[i] == 'B')) { semi += s[i] == '#' ? 1 : -1; i++; }
        if (!int.TryParse(s.Substring(i), out int oct)) oct = 4;
        freq = 440f * Mathf.Pow(2f, (semi + (oct + 1) * 12 - 69) / 12f);
        return true;
    }
}

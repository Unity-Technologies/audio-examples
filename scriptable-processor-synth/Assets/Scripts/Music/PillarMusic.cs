using UnityEngine;

public enum RootNote { C, CSharp, D, DSharp, E, F, FSharp, G, GSharp, A, ASharp, B }
public enum ProgType { Off, Pop, Fifties, Jazz, Minor, Epic }
public enum ScaleType { Major, Minor, Dorian, Phrygian, Lydian, Mixolydian, PentatonicMajor, PentatonicMinor, Blues, Chromatic }

// Turns a pillar landing into a note (higher pillar = higher pitch, within +-2 octaves of the key) plus optional chord notes.
public class PillarMusic : MonoBehaviour
{
    public static PillarMusic Instance;

    public RootNote key = RootNote.C;
    public ScaleType scale = ScaleType.Major;
    [Header("Chord (stacked in scale steps above the note)")]
    public bool third = true, fifth = true, seventh, ninth;
    public bool bassNote; // also play the pillar's note one octave lower
    public bool randomInversions; // each landing raises a random number of the lowest chord notes by an octave
    [Header("Progression (chord roots follow a loop; pillar height picks a chord tone)")]
    public ProgType progression = ProgType.Pop;
    [Range(1, 16)] public int landingsPerChord = 4;
    [Range(0, 1)] public float passingChance = 0.2f; // chance the melody note steps to a neighbouring scale tone
    [Header("Sound")]
    [Range(0, 3)] public int wave = 1;
    public float attack = 0.01f, length = 0.5f, release = 0.6f;

    static readonly int[][] Scales =
    {
        new[] { 0, 2, 4, 5, 7, 9, 11 }, new[] { 0, 2, 3, 5, 7, 8, 10 }, new[] { 0, 2, 3, 5, 7, 9, 10 },
        new[] { 0, 1, 3, 5, 7, 8, 10 }, new[] { 0, 2, 4, 6, 7, 9, 11 }, new[] { 0, 2, 4, 5, 7, 9, 10 },
        new[] { 0, 2, 4, 7, 9 }, new[] { 0, 3, 5, 7, 10 }, new[] { 0, 3, 5, 6, 7, 10 },
        new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },
    };

    // scale-degree loops (0 = I)
    static readonly int[][] Progs =
    {
        null, new[] { 0, 4, 5, 3 }, new[] { 0, 5, 3, 4 }, new[] { 1, 4, 0, 5 }, new[] { 0, 5, 2, 6 }, new[] { 0, 6, 5, 6 },
    };
    int landings;
    float lastCenter = 6f;
    public string CurrentChord { get; private set; } = "";

    ScriptableAudioPipelineAndEffects synth;
    MarbleBall ball;
    static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    void Awake() { Instance = this; }
    void Start() { synth = FindAnyObjectByType<ScriptableAudioPipelineAndEffects>(); }

    // t: 0..1 pillar height; jitter: small fixed per-pillar offset in scale steps.
    public void Play(float t, int jitter, Color color)
    {
        if (synth == null) return;
        if (progression != ProgType.Off) { PlayProgression(t, jitter, color); return; }
        var iv = Scales[(int)scale];
        int L = iv.Length;
        int d = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(-2 * L, 2 * L, t)) + jitter, -2 * L, 2 * L);

        var steps = new System.Collections.Generic.List<int> { d };
        if (third) steps.Add(d + 2);
        if (fifth) steps.Add(d + 4);
        if (seventh) steps.Add(d + 6);
        if (ninth) steps.Add(d + 8);

        var semisList = new System.Collections.Generic.List<int>();
        foreach (int s in steps)
        {
            int oct = Mathf.FloorToInt((float)s / L);
            semisList.Add(Fold(oct * 12 + iv[s - oct * L]));
        }
        int rootSemis = semisList[0];
        if (randomInversions && semisList.Count > 1)
        {
            semisList.Sort();
            int k = Random.Range(0, semisList.Count);
            for (int i = 0; i < k; i++) semisList[i] = Fold(semisList[i] + 12);
        }

        if (bassNote) semisList.Add(rootSemis - 12);

        Emit(semisList, rootSemis, color);
    }

    // Plays an absolute MIDI note with chord tones stacked on the scale (chromatic fallback off-scale). Returns the MIDI notes played, root first.
    public System.Collections.Generic.List<int> PlayNote(int midi, Color color)
    {
        var res = new System.Collections.Generic.List<int>();
        if (synth == null) return res;
        var iv = Scales[(int)scale];
        int L = iv.Length, semis = midi - 60 - (int)key, idx = System.Array.IndexOf(iv, ((semis % 12) + 12) % 12);
        var list = new System.Collections.Generic.List<int> { semis };
        var offs = new System.Collections.Generic.List<int>(); var chrom = new System.Collections.Generic.List<int>();
        if (third) { offs.Add(2); chrom.Add(4); }
        if (fifth) { offs.Add(4); chrom.Add(7); }
        if (seventh) { offs.Add(6); chrom.Add(10); }
        if (ninth) { offs.Add(8); chrom.Add(14); }
        for (int i = 0; i < offs.Count; i++)
        {
            if (idx < 0) { list.Add(semis + chrom[i]); continue; }
            int s = idx + offs[i], oct = Mathf.FloorToInt((float)s / L);
            list.Add(semis - iv[idx] + oct * 12 + iv[s - oct * L]);
        }
        if (bassNote) list.Add(semis - 12);
        Emit(list, semis, color);
        foreach (int x in list) res.Add(60 + (int)key + x);
        return res;
    }

    static int StepSemis(int[] iv, int L, int s)
    {
        int oct = Mathf.FloorToInt((float)s / L);
        return oct * 12 + iv[s - oct * L];
    }

    void PlayProgression(float t, int jitter, Color color)
    {
        var iv = Scales[(int)scale];
        int L = iv.Length;
        var prog = Progs[(int)progression];
        int root = prog[(landings / landingsPerChord) % prog.Length] % L;
        landings++;

        var offs = new System.Collections.Generic.List<int> { 0 };
        if (third) offs.Add(2);
        if (fifth) offs.Add(4);
        if (seventh) offs.Add(6);
        if (ninth) offs.Add(8);

        // melody: pillar height picks a chord tone across the octave range
        var pool = new System.Collections.Generic.List<int>();
        for (int o = -2; o <= 1; o++) foreach (int c in offs) pool.Add(root + c + o * L);
        pool.Sort();
        int melody = pool[Mathf.Clamp(Mathf.RoundToInt(t * (pool.Count - 1)) + jitter, 0, pool.Count - 1)];
        if (Random.value < passingChance) melody += Random.value < 0.5f ? -1 : 1;
        int melodySemis = Fold(StepSemis(iv, L, melody));

        // chord voicing: random inversion, or the closest inversion to the previous chord
        var chord = new System.Collections.Generic.List<int>();
        foreach (int c in offs) chord.Add(StepSemis(iv, L, root + c));
        int n = chord.Count;
        int bestK = 0, bestShift = 0;
        if (randomInversions) bestK = Random.Range(0, n);
        else
        {
            float bestD = float.MaxValue;
            for (int k = 0; k < n; k++)
                foreach (int shift in new[] { -12, 0 })
                {
                    float d = Mathf.Abs(Avg(chord, k, shift) - lastCenter);
                    if (d < bestD) { bestD = d; bestK = k; bestShift = shift; }
                }
        }
        lastCenter = Avg(chord, bestK, bestShift);

        var semisList = new System.Collections.Generic.List<int> { melodySemis };
        for (int i = 0; i < n; i++) semisList.Add(Fold(chord[i] + (i < bestK ? 12 : 0) + bestShift));
        if (bassNote) semisList.Add(Fold(chord[0] - 12));

        int third3 = StepSemis(iv, L, root + 2) - chord[0], fifth5 = StepSemis(iv, L, root + 4) - chord[0];
        CurrentChord = Names[(((int)key + chord[0]) % 12 + 12) % 12] + (fifth5 == 6 ? "dim" : third3 == 3 ? "m" : "");

        Emit(semisList, melodySemis, color);
    }

    static float Avg(System.Collections.Generic.List<int> chord, int k, int shift)
    {
        float sum = 0;
        for (int i = 0; i < chord.Count; i++) sum += chord[i] + (i < k ? 12 : 0) + shift;
        return sum / chord.Count;
    }

    // plays every note in the list; the first note equal to rootSemis names the floating label
    void Emit(System.Collections.Generic.List<int> semisList, int rootSemis, Color color)
    {
        var arp = ArpEngine.Instance;
        if (arp != null)
        {
            bool was = arp.Active;
            arp.Add(60 + (int)key + rootSemis);
        }
        var played = new System.Collections.Generic.List<string>();
        foreach (int semis in semisList)
        {
            int midi = 60 + (int)key + semis;
            if (semis == rootSemis && played.Count == 0) played.Add(Names[((midi % 12) + 12) % 12] + (Mathf.FloorToInt(midi / 12f) - 1));
            float freq = 440f * Mathf.Pow(2f, (60 + (int)key + semis - 69) / 12f);
            SynthScope.Mark(freq, 0, length);
            synth.NoteOn(new NoteEvent
            {
                freq = freq, wave = wave, gain = 1f / Mathf.Sqrt(semisList.Count), pan = 0.5f,
                attack = SynthAdsr.attack, decay = SynthAdsr.decay, sustain = length,
                sustainLevel = SynthAdsr.sustain, release = SynthAdsr.release,
            });
        }
        if (ball == null) ball = FindAnyObjectByType<MarbleBall>();
        if (ball != null) FloatingNote.Spawn(ball.transform.position + Vector3.up * 0.7f, played.Count > 0 ? played[0] : "", Color.Lerp(color, Color.white, 0.4f));
    }

    // keep within +-2 octaves of the key
    static int Fold(int semis)
    {
        while (semis > 24) semis -= 12;
        while (semis < -24) semis += 12;
        return semis;
    }
}

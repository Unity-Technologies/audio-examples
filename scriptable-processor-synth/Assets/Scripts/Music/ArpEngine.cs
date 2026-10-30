using System.Collections.Generic;
using UnityEngine;

// Minimal arp: remembers the last landed melody notes; from 4 notes it cycles them up-down on the audio clock.
// More notes = more delay and reverb. Pool empties after idleSeconds without a landing.
public class ArpEngine : MonoBehaviour
{
    public static ArpEngine Instance;
    public bool enabledArp = true;
    public int startAt = 4, maxNotes = 10;
    public float idleSeconds = 10f;
    public float gain = 0.35f, length = 0.18f;

    public bool Active => enabledArp && pool.Count >= startAt;
    public int Count => pool.Count;

    readonly List<int> pool = new List<int>(); // midi note numbers, oldest first
    ScriptableAudioPipelineAndEffects synth;
    StepSequencer seq;
    double nextTime;
    int step, dir = 1;
    float lastAdd;
    bool wasActive;
    float baseDelay, baseReverb;

    void Awake() { Instance = this; }

    void Start() { synth = FindAnyObjectByType<ScriptableAudioPipelineAndEffects>(); seq = FindAnyObjectByType<StepSequencer>(); }

    public void Add(int midi)
    {
        pool.Add(midi);
        if (pool.Count > maxNotes) pool.RemoveAt(0);
        lastAdd = Time.time;
    }

    void Update()
    {
        if (synth == null || seq == null || DelayEffect.Instance == null || ReverbEffect.Instance == null) return;
        if (pool.Count > 0 && Time.time - lastAdd > idleSeconds) pool.Clear();

        bool active = Active;
        if (active && !wasActive) { baseDelay = DelayEffect.Instance.mix; baseReverb = ReverbEffect.Instance.mix; nextTime = AudioSettings.dspTime + 0.1; step = 0; dir = 1; }
        if (!active && wasActive) { DelayEffect.Instance.mix = baseDelay; ReverbEffect.Instance.mix = baseReverb; }
        wasActive = active;
        if (!active) return;

        float d = Mathf.Clamp01((pool.Count - startAt) / (float)Mathf.Max(1, maxNotes - startAt));
        DelayEffect.Instance.mix = Mathf.Lerp(DelayEffect.Instance.mix, Mathf.Lerp(baseDelay, 0.3f, d), 0.05f);
        ReverbEffect.Instance.mix = Mathf.Lerp(ReverbEffect.Instance.mix, Mathf.Lerp(baseReverb, 0.35f, d), 0.05f);

        double stepDur = 60.0 / seq.bpm / 2.0 / (d > 0.5f ? 2.0 : 1.0); // 8ths, 16ths when dense
        double now = AudioSettings.dspTime;
        while (nextTime < now + 0.1)
        {
            var sorted = new List<int>(pool); sorted.Sort();
            step = Mathf.Clamp(step, 0, sorted.Count - 1);
            int midi = sorted[step];
            synth.NoteOn(new NoteEvent
            {
                freq = 440f * Mathf.Pow(2f, (midi - 69) / 12f), wave = seq.wave, gain = gain, pan = 0.5f,
                attack = 0.005f, decay = 0.12f, sustainLevel = 0.3f, release = 0.25f, sustain = length,
                delaySamples = Mathf.Max(0, (int)((nextTime - now) * AudioSettings.outputSampleRate)),
            });
            if (step + dir >= sorted.Count || step + dir < 0) dir = -dir;
            step = Mathf.Clamp(step + dir, 0, sorted.Count - 1);
            nextTime += stepDur;
        }
    }
}

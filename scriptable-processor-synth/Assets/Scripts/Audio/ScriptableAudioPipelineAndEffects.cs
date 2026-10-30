using Unity.IntegerTime;
using static UnityEngine.Audio.ProcessorInstance;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Audio;

// Messages (control thread -> audio thread). Must be unmanaged.
public struct NoteEvent
{
    public float freq, sustain, attack, decay, sustainLevel, release, gain, pan; // sustain = hold seconds for timed notes
    public int wave;          // 0 sine, 1 triangle, 2 saw, 3 square
    public int id; public bool held; // held notes sustain until NoteOff(id)
    public int delaySamples;  // start this many frames after the message is processed
    public int unison;        // 0 = fill the fields below from SynthPatch in NoteOn
    public float detune, sub, fcut, fenvOct, fres, fdecay; // cents, sub level, filter Hz (0 = off), filter env octaves, resonance, filter env decay s
}

public struct NoteOff { public int id; }

public struct SynthVoice
{
    public int id; public bool held;
    public bool active;
    public int delay;
    public double phase;
    public float freq, gain, pan;
    public int wave;
    public int age, sustainFrames, attackFrames, decayFrames, releaseFrames;
    public float sustainLevel;
    public double phaseB, phaseC, phaseSub;
    public int unison, fage, fdecayFrames;
    public float detune, sub, fcut, fenvOct, fres, ic1L, ic2L, ic1R, ic2R;
}

// Audio thread side.
public struct SynthRealtime : GeneratorInstance.IRealtime
{
    public NativeArray<SynthVoice> voices;
    public float sampleRate;
    public float lfo;

    public bool isFinite => false;
    public bool isRealtime => false;
    public DiscreteTime? length => null;

    public void Update(ProcessorInstance.UpdatedDataContext ctx, ProcessorInstance.Pipe pipe)
    {
        foreach (var element in pipe.GetAvailableData(ctx))
        {
            if (element.TryGetData(out NoteOff off))
            {
                for (int i = 0; i < voices.Length; i++)
                    if (voices[i].active && voices[i].held && voices[i].id == off.id) { var v = voices[i]; v.held = false; voices[i] = v; }
                continue;
            }
            if (!element.TryGetData(out NoteEvent n)) continue;
            int slot = -1, oldest = 0, oldestAge = -1;
            for (int i = 0; i < voices.Length; i++)
            {
                if (!voices[i].active) { slot = i; break; }
                if (voices[i].age > oldestAge) { oldestAge = voices[i].age; oldest = i; }
            }
            if (slot < 0) slot = oldest; // polyphony limit: steal the oldest voice
            voices[slot] = new SynthVoice
            {
                active = true, id = n.id, held = n.held, delay = n.delaySamples, freq = n.freq, gain = n.gain, pan = n.pan, wave = n.wave,
                attackFrames = Mathf.Max(1, (int)(n.attack * sampleRate)),
                decayFrames = Mathf.Max(1, (int)(n.decay * sampleRate)),
                sustainLevel = n.sustainLevel,
                sustainFrames = Mathf.Max(1, (int)(n.sustain * sampleRate)),
                releaseFrames = Mathf.Max(1, (int)(n.release * sampleRate)),
                unison = Mathf.Clamp(n.unison, 1, 3), detune = n.detune, sub = n.sub, fcut = n.fcut, fenvOct = n.fenvOct, fres = n.fres,
                fdecayFrames = Mathf.Max(1, (int)(n.fdecay * sampleRate)), phaseB = 0.31, phaseC = 0.67,
            };
        }
    }

    static float PolyBlep(float t, float dt)
    {
        if (t < dt) { t /= dt; return t + t - t * t - 1f; }
        if (t > 1f - dt) { t = (t - 1f) / dt; return t * t + t + t + 1f; }
        return 0f;
    }

    // 0 sine, 1 triangle, 2 band-limited saw, 3 band-limited pulse (width pw)
    static float Osc(int wave, ref double ph, float inc, float pw)
    {
        float p = (float)ph, s;
        if (wave == 0) s = Mathf.Sin(p * 2f * Mathf.PI);
        else if (wave == 1) s = 4f * Mathf.Abs(p - 0.5f) - 1f;
        else if (wave == 2) s = 2f * p - 1f - PolyBlep(p, inc);
        else s = (p < pw ? 1f : -1f) + PolyBlep(p, inc) - PolyBlep(p - pw < 0f ? p - pw + 1f : p - pw, inc);
        ph += inc; if (ph >= 1.0) ph -= 1.0;
        return s;
    }

    public GeneratorInstance.Result Process(in RealtimeContext ctx, ProcessorInstance.Pipe pipe, ChannelBuffer buffer, GeneratorInstance.Arguments args)
    {
        int ch = buffer.channelCount;
        for (int f = 0; f < buffer.frameCount; f++)
        {
            float l = 0f, r = 0f;
            for (int i = 0; i < voices.Length; i++)
            {
                var v = voices[i];
                if (!v.active) continue;
                if (v.delay > 0) { v.delay--; voices[i] = v; continue; }

                int total = v.attackFrames + v.decayFrames + v.sustainFrames + v.releaseFrames;
                if (v.age >= total) { v.active = false; voices[i] = v; continue; }

                int ad = v.attackFrames + v.decayFrames;
                float env = v.age < v.attackFrames ? (float)v.age / v.attackFrames
                          : v.age < ad ? 1f - (1f - v.sustainLevel) * (float)(v.age - v.attackFrames) / v.decayFrames
                          : v.age < ad + v.sustainFrames ? v.sustainLevel
                          : v.sustainLevel * (1f - (float)(v.age - ad - v.sustainFrames) / v.releaseFrames);

                float inc = v.freq / sampleRate;
                float pw = 0.5f + 0.38f * Mathf.Sin(lfo * 2f * Mathf.PI);
                float d = v.detune * 0.000577623f; // cents -> ln ratio
                float oL, oR;
                if (v.unison == 1) { oL = oR = Osc(v.wave, ref v.phase, inc, pw); }
                else if (v.unison == 2)
                {
                    float s0 = Osc(v.wave, ref v.phase, inc * Mathf.Exp(-d), pw), s1 = Osc(v.wave, ref v.phaseB, inc * Mathf.Exp(d), pw);
                    oL = (s0 * 0.8f + s1 * 0.2f) * 0.75f; oR = (s0 * 0.2f + s1 * 0.8f) * 0.75f;
                }
                else
                {
                    float s0 = Osc(v.wave, ref v.phase, inc * Mathf.Exp(-d), pw), s1 = Osc(v.wave, ref v.phaseB, inc, pw), s2 = Osc(v.wave, ref v.phaseC, inc * Mathf.Exp(d), pw);
                    oL = (s0 * 0.9f + s1 * 0.5f + s2 * 0.1f) * 0.6f; oR = (s2 * 0.9f + s1 * 0.5f + s0 * 0.1f) * 0.6f;
                }
                if (v.sub > 0f)
                {
                    float ss = (v.phaseSub < 0.5 ? 1f : -1f) * v.sub * 0.7f;
                    oL += ss; oR += ss;
                    v.phaseSub += inc * 0.5; if (v.phaseSub >= 1.0) v.phaseSub -= 1.0;
                }
                if (v.fcut > 0f)
                {
                    float fe = Mathf.Exp(-(float)v.fage / v.fdecayFrames);
                    float fc = Mathf.Clamp(v.fcut * Mathf.Exp(0.693147f * v.fenvOct * fe), 20f, sampleRate * 0.45f);
                    float g = Mathf.Tan(Mathf.PI * fc / sampleRate), k = 2f - 1.9f * Mathf.Clamp01(v.fres);
                    float a1 = 1f / (1f + g * (g + k)), a2 = g * a1, a3 = g * a2;
                    float v3 = oL - v.ic2L, v1 = a1 * v.ic1L + a2 * v3, v2 = v.ic2L + a2 * v.ic1L + a3 * v3;
                    v.ic1L = 2f * v1 - v.ic1L; v.ic2L = 2f * v2 - v.ic2L; oL = v2;
                    v3 = oR - v.ic2R; v1 = a1 * v.ic1R + a2 * v3; v2 = v.ic2R + a2 * v.ic1R + a3 * v3;
                    v.ic1R = 2f * v1 - v.ic1R; v.ic2R = 2f * v2 - v.ic2R; oR = v2;
                    v.fage++;
                }
                float amp = env * v.gain * 0.15f;
                if (v.unison == 1) { float m = oL * amp; l += m * (1f - v.pan); r += m * v.pan; }
                else { l += oL * amp; r += oR * amp; }
                if (!(v.held && v.age >= v.attackFrames + v.decayFrames)) v.age++;
                voices[i] = v;
            }
            l *= 2f; r *= 2f;
            lfo += 0.9f / sampleRate; if (lfo >= 1f) lfo -= 1f;
            if (ch >= 2) { buffer[0, f] = l; buffer[1, f] = r; for (int c = 2; c < ch; c++) buffer[c, f] = 0f; }
            else buffer[0, f] = (l + r) * 0.5f;
        }
        return buffer.frameCount;
    }
}

// Control thread side.
public struct SynthControl : GeneratorInstance.IControl<SynthRealtime>
{
    public void Dispose(ControlContext ctx, ref SynthRealtime rt) { if (rt.voices.IsCreated) rt.voices.Dispose(); }
    public void Update(ControlContext ctx, ProcessorInstance.Pipe pipe) { }

    public ProcessorInstance.Response OnMessage(ControlContext ctx, ProcessorInstance.Pipe pipe, ProcessorInstance.Message msg)
    {
        if (msg.Is<NoteOff>()) { pipe.SendData(ctx, msg.Get<NoteOff>()); return ProcessorInstance.Response.Handled; }
        if (msg.Is<NoteEvent>()) { pipe.SendData(ctx, msg.Get<NoteEvent>()); return ProcessorInstance.Response.Handled; }
        return ProcessorInstance.Response.Unhandled;
    }

    public void Configure(ControlContext ctx, ref SynthRealtime rt, in AudioFormat format,
        out GeneratorInstance.Setup setup, ref GeneratorInstance.Properties props)
    {
        rt.sampleRate = format.sampleRate;
        setup = new GeneratorInstance.Setup(AudioSpeakerMode.Stereo, format.sampleRate);
    }
}

[RequireComponent(typeof(AudioSource))]
public class ScriptableAudioPipelineAndEffects : MonoBehaviour, IAudioGenerator
{
    public const int MaxVoices = 12; // polyphony limit, oldest voice is stolen
    AudioSource source;
    bool warned;

    public bool isFinite => false;
    public bool isRealtime => false;
    public DiscreteTime? length => null;

    public GeneratorInstance CreateInstance(ControlContext ctx, AudioFormat? nested, GeneratorInstance.CreationParameters p)
    {
        var rt = new SynthRealtime { voices = new NativeArray<SynthVoice>(MaxVoices, Allocator.Persistent) };
        return ctx.AllocateGenerator(rt, new SynthControl());
    }

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.generator = this;
        // the effects are separate scriptable effect components, chained in this order after the generator
        Ensure<ChorusEffect>(); Ensure<LowPassEffect>(); Ensure<HighPassEffect>(); Ensure<DelayEffect>(); Ensure<ReverbEffect>(); Ensure<ScopeTapEffect>();
    }

    void Ensure<T>() where T : Component { if (GetComponent<T>() == null) gameObject.AddComponent<T>(); }

    void Start() { source.Play(); }

    public void NoteOn(NoteEvent n)
    {
        if (n.unison == 0)
        {
            n.unison = SynthPatch.unison; n.detune = SynthPatch.detune; n.sub = SynthPatch.sub; n.fcut = SynthPatch.fcut;
            n.fenvOct = SynthPatch.fenvOct; n.fres = SynthPatch.fres; n.fdecay = SynthPatch.fdecay;
        }
        var instance = source.generatorInstance;
        if (!ControlContext.builtIn.Exists(instance)) { if (!warned) { warned = true; Debug.LogWarning("[Generator] no live instance (isPlaying=" + source.isPlaying + ")"); } return; }
        ControlContext.builtIn.SendMessage(instance, ref n);
    }

    public void NoteOff(int id)
    {
        var instance = source.generatorInstance;
        if (!ControlContext.builtIn.Exists(instance)) return;
        var o = new NoteOff { id = id };
        ControlContext.builtIn.SendMessage(instance, ref o);
    }
}

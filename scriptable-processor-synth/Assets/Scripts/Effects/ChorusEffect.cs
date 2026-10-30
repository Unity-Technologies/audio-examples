using Unity.Collections;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;

public struct ChorusParams { public int mode; } // 0 off, 1 I, 2 II, 3 I+II

public struct ChorusRealtime : EffectInstance.IRealtime
{
    public float sampleRate, phase;
    public int mode, w;
    public NativeArray<float> bl, br;

    public void Update(UpdatedDataContext ctx, Pipe pipe)
    {
        foreach (var e in pipe.GetAvailableData(ctx)) if (e.TryGetData(out ChorusParams p)) mode = p.mode;
    }

    static float Read(NativeArray<float> buf, int w, float delay)
    {
        float pos = w - delay;
        int len = buf.Length, i0 = Mathf.FloorToInt(pos), i1 = i0 + 1;
        float fr = pos - i0;
        i0 = ((i0 % len) + len) % len; i1 = ((i1 % len) + len) % len;
        return buf[i0] * (1f - fr) + buf[i1] * fr;
    }

    public EffectInstance.Result Process(in RealtimeContext ctx, ChannelBuffer input, ChannelBuffer output, EffectInstance.Arguments args)
    {
        for (int f = 0; f < input.frameCount; f++)
        {
            EffectIO.Read(input, f, out float l, out float r);
            if (mode > 0)
            {
                float rate = mode == 1 ? 0.513f : mode == 2 ? 0.863f : 9.75f, depth = mode == 3 ? 0.0005f : 0.0018f;
                phase += rate / sampleRate; if (phase >= 1f) phase -= 1f;
                float m = Mathf.Sin(phase * 2f * Mathf.PI);
                bl[w] = l; br[w] = r;
                float wl = Read(bl, w, (0.0034f + depth * m) * sampleRate), wr = Read(br, w, (0.0034f - depth * m) * sampleRate);
                w = (w + 1) % bl.Length;
                l = 0.65f * (l + wl); r = 0.65f * (r + wr);
            }
            EffectIO.Write(output, f, l, r);
        }
        return default;
    }
}

public struct ChorusControl : EffectInstance.IControl<ChorusRealtime>
{
    public void Dispose(ControlContext ctx, ref ChorusRealtime rt) { if (rt.bl.IsCreated) rt.bl.Dispose(); if (rt.br.IsCreated) rt.br.Dispose(); }
    public void Update(ControlContext ctx, Pipe pipe) { }
    public Response OnMessage(ControlContext ctx, Pipe pipe, Message msg)
    {
        if (msg.Is<ChorusParams>()) { pipe.SendData(ctx, msg.Get<ChorusParams>()); return Response.Handled; }
        return Response.Unhandled;
    }
    public void Configure(ControlContext ctx, ref ChorusRealtime rt, in AudioConfiguration cfg, out EffectInstance.Setup setup) { setup = default; }
}

public class ChorusEffect : MonoBehaviour, IAudioEffect
{
    public static ChorusEffect Instance;
    [Range(0, 3)] public int mode = 0; // 0 off, 1 I, 2 II, 3 I+II
    public bool followPatch = true; // take the mode from the selected instrument / HUD
    AudioSource src;

    void Awake() { Instance = this; src = GetComponent<AudioSource>(); }

    public EffectInstance CreateInstance(ControlContext ctx, AudioFormat? nested, EffectInstance.CreationParameters cp)
        => ctx.AllocateEffect(new ChorusRealtime { sampleRate = AudioSettings.outputSampleRate, mode = mode,
            bl = new NativeArray<float>(AudioSettings.outputSampleRate / 10, Allocator.Persistent), br = new NativeArray<float>(AudioSettings.outputSampleRate / 10, Allocator.Persistent) }, new ChorusControl(), nested, cp);

    void Update()
    {
        if (followPatch) mode = SynthPatch.chorus;
        var p = new ChorusParams { mode = mode };
        EffectSend.Push(src, this, ref p);
    }
}

using Unity.Collections;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;

public struct DelayParams { public float time, feedback, mix; }

public struct DelayRealtime : EffectInstance.IRealtime
{
    public float sampleRate, time, feedback, mix;
    public int w;
    public NativeArray<float> dl, dr;

    public void Update(UpdatedDataContext ctx, Pipe pipe)
    {
        foreach (var e in pipe.GetAvailableData(ctx)) if (e.TryGetData(out DelayParams p)) { time = p.time; feedback = p.feedback; mix = p.mix; }
    }

    public EffectInstance.Result Process(in RealtimeContext ctx, ChannelBuffer input, ChannelBuffer output, EffectInstance.Arguments args)
    {
        int len = dl.Length, dn = Mathf.Clamp((int)(time * sampleRate), 1, len - 1);
        for (int f = 0; f < input.frameCount; f++)
        {
            EffectIO.Read(input, f, out float l, out float r);
            int rp = (w - dn + len) % len;
            float xl = dl[rp], xr = dr[rp];
            dl[w] = l + xl * feedback; dr[w] = r + xr * feedback;
            w = (w + 1) % len;
            EffectIO.Write(output, f, l + xl * mix, r + xr * mix);
        }
        return default;
    }
}

public struct DelayControl : EffectInstance.IControl<DelayRealtime>
{
    public void Dispose(ControlContext ctx, ref DelayRealtime rt) { if (rt.dl.IsCreated) rt.dl.Dispose(); if (rt.dr.IsCreated) rt.dr.Dispose(); }
    public void Update(ControlContext ctx, Pipe pipe) { }
    public Response OnMessage(ControlContext ctx, Pipe pipe, Message msg)
    {
        if (msg.Is<DelayParams>()) { pipe.SendData(ctx, msg.Get<DelayParams>()); return Response.Handled; }
        return Response.Unhandled;
    }
    public void Configure(ControlContext ctx, ref DelayRealtime rt, in AudioConfiguration cfg, out EffectInstance.Setup setup) { setup = default; }
}

public class DelayEffect : MonoBehaviour, IAudioEffect
{
    public static DelayEffect Instance;
    [Range(0.05f, 1.5f)] public float time = 0.375f;
    [Range(0, 0.9f)] public float feedback = 0.4f;
    [Range(0, 1)] public float mix = 0.3f;
    AudioSource src;

    void Awake() { Instance = this; src = GetComponent<AudioSource>(); }

    public EffectInstance CreateInstance(ControlContext ctx, AudioFormat? nested, EffectInstance.CreationParameters cp)
        => ctx.AllocateEffect(new DelayRealtime { sampleRate = AudioSettings.outputSampleRate, time = time, feedback = feedback, mix = mix,
            dl = new NativeArray<float>(AudioSettings.outputSampleRate * 2, Allocator.Persistent), dr = new NativeArray<float>(AudioSettings.outputSampleRate * 2, Allocator.Persistent) }, new DelayControl(), nested, cp);

    void Update()
    {
        var p = new DelayParams { time = time, feedback = feedback, mix = mix };
        EffectSend.Push(src, this, ref p);
    }
}

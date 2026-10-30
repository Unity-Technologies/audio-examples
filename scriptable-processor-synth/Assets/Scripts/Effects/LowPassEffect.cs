using Unity.Collections;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;

public struct LowPassParams { public float cutoff; }

public struct LowPassRealtime : EffectInstance.IRealtime
{
    public float sampleRate, cutoff, l1, l2, r1, r2;

    public void Update(UpdatedDataContext ctx, Pipe pipe)
    {
        foreach (var e in pipe.GetAvailableData(ctx)) if (e.TryGetData(out LowPassParams p)) cutoff = p.cutoff;
    }

    public EffectInstance.Result Process(in RealtimeContext ctx, ChannelBuffer input, ChannelBuffer output, EffectInstance.Arguments args)
    {
        float a = 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Clamp(cutoff, 20f, sampleRate * 0.45f) / sampleRate);
        for (int f = 0; f < input.frameCount; f++)
        {
            EffectIO.Read(input, f, out float l, out float r);
            l1 += a * (l - l1); l2 += a * (l1 - l2);
            r1 += a * (r - r1); r2 += a * (r1 - r2);
            EffectIO.Write(output, f, l2, r2);
        }
        return default;
    }
}

public struct LowPassControl : EffectInstance.IControl<LowPassRealtime>
{
    public void Dispose(ControlContext ctx, ref LowPassRealtime rt) { }
    public void Update(ControlContext ctx, Pipe pipe) { }
    public Response OnMessage(ControlContext ctx, Pipe pipe, Message msg)
    {
        if (msg.Is<LowPassParams>()) { pipe.SendData(ctx, msg.Get<LowPassParams>()); return Response.Handled; }
        return Response.Unhandled;
    }
    public void Configure(ControlContext ctx, ref LowPassRealtime rt, in AudioConfiguration cfg, out EffectInstance.Setup setup) { setup = default; }
}

public class LowPassEffect : MonoBehaviour, IAudioEffect
{
    public static LowPassEffect Instance;
    [Range(100, 20000)] public float cutoff = 20000f;
    AudioSource src;

    void Awake() { Instance = this; src = GetComponent<AudioSource>(); }

    public EffectInstance CreateInstance(ControlContext ctx, AudioFormat? nested, EffectInstance.CreationParameters cp)
        => ctx.AllocateEffect(new LowPassRealtime { sampleRate = AudioSettings.outputSampleRate, cutoff = cutoff }, new LowPassControl(), nested, cp);

    void Update()
    {
        var p = new LowPassParams { cutoff = cutoff };
        EffectSend.Push(src, this, ref p);
    }
}

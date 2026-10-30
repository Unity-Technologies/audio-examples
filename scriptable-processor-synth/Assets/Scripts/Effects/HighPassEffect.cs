using Unity.Collections;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;

public struct HighPassParams { public float cutoff; }

public struct HighPassRealtime : EffectInstance.IRealtime
{
    public float sampleRate, cutoff, hl, hr;

    public void Update(UpdatedDataContext ctx, Pipe pipe)
    {
        foreach (var e in pipe.GetAvailableData(ctx)) if (e.TryGetData(out HighPassParams p)) cutoff = p.cutoff;
    }

    public EffectInstance.Result Process(in RealtimeContext ctx, ChannelBuffer input, ChannelBuffer output, EffectInstance.Arguments args)
    {
        float a = 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Clamp(cutoff, 20f, sampleRate * 0.45f) / sampleRate);
        for (int f = 0; f < input.frameCount; f++)
        {
            EffectIO.Read(input, f, out float l, out float r);
            hl += a * (l - hl); l -= hl;
            hr += a * (r - hr); r -= hr;
            EffectIO.Write(output, f, l, r);
        }
        return default;
    }
}

public struct HighPassControl : EffectInstance.IControl<HighPassRealtime>
{
    public void Dispose(ControlContext ctx, ref HighPassRealtime rt) { }
    public void Update(ControlContext ctx, Pipe pipe) { }
    public Response OnMessage(ControlContext ctx, Pipe pipe, Message msg)
    {
        if (msg.Is<HighPassParams>()) { pipe.SendData(ctx, msg.Get<HighPassParams>()); return Response.Handled; }
        return Response.Unhandled;
    }
    public void Configure(ControlContext ctx, ref HighPassRealtime rt, in AudioConfiguration cfg, out EffectInstance.Setup setup) { setup = default; }
}

public class HighPassEffect : MonoBehaviour, IAudioEffect
{
    public static HighPassEffect Instance;
    [Range(20, 4000)] public float cutoff = 20f;
    AudioSource src;

    void Awake() { Instance = this; src = GetComponent<AudioSource>(); }

    public EffectInstance CreateInstance(ControlContext ctx, AudioFormat? nested, EffectInstance.CreationParameters cp)
        => ctx.AllocateEffect(new HighPassRealtime { sampleRate = AudioSettings.outputSampleRate, cutoff = cutoff }, new HighPassControl(), nested, cp);

    void Update()
    {
        var p = new HighPassParams { cutoff = cutoff };
        EffectSend.Push(src, this, ref p);
    }
}

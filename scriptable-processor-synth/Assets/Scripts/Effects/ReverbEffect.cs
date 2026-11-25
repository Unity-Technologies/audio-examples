using Unity.Collections;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;

public struct ReverbParams { public float mix, size, damp; }

public struct ReverbRealtime : EffectInstance.IRealtime
{
    public float mix, size, damp;
    public NativeArray<float> rev, combLp;
    public NativeArray<int> revInt; // [0..12) lengths, [12..24) offsets, [24..36) positions

    public void Update(UpdatedDataContext ctx, Pipe pipe)
    {
        foreach (var e in pipe.GetAvailableData(ctx)) if (e.TryGetData(out ReverbParams p)) { mix = p.mix; size = p.size; damp = p.damp; }
    }

    public EffectInstance.Result Process(in RealtimeContext ctx, ChannelBuffer input, ChannelBuffer output, EffectInstance.Arguments args)
    {
        float fb = 0.7f + 0.28f * size;
        for (int f = 0; f < input.frameCount; f++)
        {
            EffectIO.Read(input, f, out float l, out float r);
            if (mix > 0f)
            {
                float inp = (l + r) * 0.5f * 0.3f, wl = 0f, wr = 0f;
                for (int c = 0; c < 2; c++)
                {
                    float w = 0f;
                    for (int k = 0; k < 6; k++)
                    {
                        int i = c * 6 + k, idx = revInt[12 + i] + revInt[24 + i];
                        float o = rev[idx];
                        if (k < 4)
                        {
                            combLp[c * 4 + k] = o * (1f - damp) + combLp[c * 4 + k] * damp;
                            rev[idx] = inp + combLp[c * 4 + k] * fb;
                            w += o;
                        }
                        else { float x = w; w = -x + o; rev[idx] = x + o * 0.5f; }
                        revInt[24 + i] = (revInt[24 + i] + 1) % revInt[i];
                    }
                    if (c == 0) wl = w; else wr = w;
                }
                l += wl * mix; r += wr * mix;
            }
            EffectIO.Write(output, f, l, r);
        }
        return default;
    }
}

public struct ReverbControl : EffectInstance.IControl<ReverbRealtime>
{
    public void Dispose(ControlContext ctx, ref ReverbRealtime rt) { if (rt.rev.IsCreated) rt.rev.Dispose(); if (rt.combLp.IsCreated) rt.combLp.Dispose(); if (rt.revInt.IsCreated) rt.revInt.Dispose(); }
    public void Update(ControlContext ctx, Pipe pipe) { }
    public Response OnMessage(ControlContext ctx, Pipe pipe, Message msg)
    {
        if (msg.Is<ReverbParams>()) { pipe.SendData(ctx, msg.Get<ReverbParams>()); return Response.Handled; }
        return Response.Unhandled;
    }
    public void Configure(ControlContext ctx, ref ReverbRealtime rt, in AudioConfiguration cfg, out EffectInstance.Setup setup) { setup = default; }
}

public class ReverbEffect : MonoBehaviour, IAudioEffect
{
    public static ReverbEffect Instance;
    [Range(0, 1)] public float mix = 0.25f;
    [Range(0, 1)] public float size = 0.8f;
    [Range(0, 1)] public float damp = 0.5f;
    AudioSource src;

    void Awake() { Instance = this; src = GetComponent<AudioSource>(); }

    public EffectInstance CreateInstance(ControlContext ctx, AudioFormat? nested, EffectInstance.CreationParameters cp)
        => ctx.AllocateEffect(Make(mix, size, damp), new ReverbControl(), nested, cp);

    static ReverbRealtime Make(float mix, float size, float damp)
    {
        int[] baseLen = { 1116, 1188, 1277, 1356, 556, 441 };
        var ri = new NativeArray<int>(36, Allocator.Persistent);
        int total = 0;
        for (int c = 0; c < 2; c++)
            for (int k = 0; k < 6; k++)
            {
                int i = c * 6 + k;
                ri[i] = Mathf.Max(8, (int)((baseLen[k] + c * 23) * AudioSettings.outputSampleRate / 44100f));
                ri[12 + i] = total; total += ri[i];
            }
        return new ReverbRealtime { mix = mix, size = size, damp = damp, revInt = ri, rev = new NativeArray<float>(total, Allocator.Persistent), combLp = new NativeArray<float>(8, Allocator.Persistent) };
    }

    void Update()
    {
        var p = new ReverbParams { mix = mix, size = size, damp = damp };
        EffectSend.Push(src, this, ref p);
    }
}

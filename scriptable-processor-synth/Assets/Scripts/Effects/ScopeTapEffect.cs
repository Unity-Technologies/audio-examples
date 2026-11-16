using Unity.Collections;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;

public struct ScopeTapRealtime : EffectInstance.IRealtime
{
    public void Update(UpdatedDataContext ctx, Pipe pipe) { }

    // pass-through that feeds the final signal to the marble waveform
    public EffectInstance.Result Process(in RealtimeContext ctx, ChannelBuffer input, ChannelBuffer output, EffectInstance.Arguments args)
    {
        for (int f = 0; f < input.frameCount; f++)
        {
            EffectIO.Read(input, f, out float l, out float r);
            SynthScope.Push((l + r) * 0.5f);
            EffectIO.Write(output, f, l, r);
        }
        return default;
    }
}

public struct ScopeTapControl : EffectInstance.IControl<ScopeTapRealtime>
{
    public void Dispose(ControlContext ctx, ref ScopeTapRealtime rt) { }
    public void Update(ControlContext ctx, Pipe pipe) { }
    public Response OnMessage(ControlContext ctx, Pipe pipe, Message msg) => Response.Unhandled;
    public void Configure(ControlContext ctx, ref ScopeTapRealtime rt, in AudioConfiguration cfg, out EffectInstance.Setup setup) { setup = default; }
}

// Keep this last in the chain so the marble shows the final sound.
public class ScopeTapEffect : MonoBehaviour, IAudioEffect
{
    public EffectInstance CreateInstance(ControlContext ctx, AudioFormat? nested, EffectInstance.CreationParameters cp)
        => ctx.AllocateEffect(new ScopeTapRealtime(), new ScopeTapControl(), nested, cp);
}

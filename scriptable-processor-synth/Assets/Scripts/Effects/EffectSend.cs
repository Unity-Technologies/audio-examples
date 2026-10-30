using Unity.Collections;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;

// Sends a parameter message from an effect component to its live instance on the audio thread.
public static class EffectSend
{
    public static void Push<TFx, T>(AudioSource src, TFx fx, ref T msg) where TFx : Component, IAudioEffect where T : unmanaged
    {
        if (src == null) return;
        var inst = src.GetEffectInstance(fx);
        if (ControlContext.builtIn.Exists(inst)) ControlContext.builtIn.SendMessage(inst, ref msg);
    }
}

// Reads stereo in, writes stereo out (mono is duplicated).
public static class EffectIO
{
    public static void Read(ChannelBuffer b, int f, out float l, out float r) { l = b[0, f]; r = b.channelCount > 1 ? b[1, f] : l; }
    public static void Write(ChannelBuffer b, int f, float l, float r)
    {
        b[0, f] = l;
        if (b.channelCount > 1) { b[1, f] = r; for (int c = 2; c < b.channelCount; c++) b[c, f] = 0f; }
    }
}

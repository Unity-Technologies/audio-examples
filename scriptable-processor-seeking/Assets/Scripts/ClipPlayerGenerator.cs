using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Plays an <see cref="AudioClip"/> through the SampleProvider generator path, so it supports
/// seeking via <see cref="SeekMessage"/> (that path is the one that handles seeks).
///
/// Assign a clip, then let something play it on an AudioSource
/// (<see cref="SeekTester"/> does this: <c>audioSource.generator = thisComponent</c>).
/// </summary>
public class ClipPlayerGenerator : MonoBehaviour, IAudioGenerator
{
    [Tooltip("The clip to play through the sample provider (e.g. your 10s counting file).")]
    public AudioClip clip;

    // CreateInstance forwards to the clip, so the metadata reported here must match what the clip's
    // own generator reports; otherwise the pipeline logs an inconsistency. An AudioClip is itself an
    // IAudioGenerator, so we mirror it and stay consistent for any assigned clip.
    // Consequence: a clip is finite, so the AudioSource stops at end-of-clip. Seek while it is still
    // playing (or replay to seek again).
    public bool isFinite => clip != null && ((IAudioGenerator)clip).isFinite;
    public bool isRealtime => clip != null && ((IAudioGenerator)clip).isRealtime;
    public DiscreteTime? length => clip != null ? ((IAudioGenerator)clip).length : null;

    // Called by the audio system when the AudioSource starts playing this generator.
    // Instantiating the clip here produces a SampleProvider-backed generator instance.
    public GeneratorInstance CreateInstance(ControlContext context, AudioFormat? nestedFormat, ProcessorInstance.CreationParameters parameters)
    {
        if (clip == null)
        {
            Debug.LogError($"{nameof(ClipPlayerGenerator)}: no clip assigned.", this);
            return default;
        }

        return clip.CreateInstance(context, nestedFormat, parameters);
    }
}

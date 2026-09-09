using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Main-thread view of a playing <see cref="ClipPlayerGenerator"/> instance.
///
/// The control side is an unmanaged struct living in native memory, so it can't touch a
/// <see cref="MonoBehaviour"/> directly. It holds a <see cref="GCHandle"/> to one of these instead,
/// which gives the realtime side's "I'm done" report a way to reach <see cref="SeekTester"/>.
/// </summary>
public class ClipPlaybackState
{
    /// <summary>Set once the nested clip instance reports it has finished generating audio.</summary>
    public bool finished;
}

/// <summary>Realtime -> control notification: the nested clip instance has finished.</summary>
struct ClipFinishedEvent { }

/// <summary>
/// Realtime side. Forwards every <c>Process</c> call to the clip's own generator instance and watches
/// the returned <see cref="GeneratorInstance.Result"/> for completion.
///
/// This is the only place end-of-clip is observable: <see cref="GeneratorInstance.Result.isFinished"/>
/// is reported to whoever drives the nested instance, and nothing publishes it to the main thread.
/// </summary>
[BurstCompile(CompileSynchronously = true)]
struct ClipPlayerRealtime : GeneratorInstance.IRealtime
{
    // Handed over by Control.Configure, which may safely write here (realtime is suspended during it).
    internal GeneratorInstance nested;
    internal bool nestedValid;

    // Latched so the control side is told exactly once, however many times Process runs afterwards.
    internal bool reportedFinished;

    // Mirrors of the clip's own metadata, resolved on the managed side at creation.
    internal bool clipIsFinite;
    internal bool clipIsRealtime;
    internal bool clipHasLength;
    internal DiscreteTime clipLength;

    public bool isFinite => clipIsFinite;
    public bool isRealtime => clipIsRealtime;
    public DiscreteTime? length => clipHasLength ? clipLength : null;

    // Nothing is sent control -> realtime; the instance arrives via Configure.
    public void Update(ProcessorInstance.UpdatedDataContext context, ProcessorInstance.Pipe pipe) { }

    public GeneratorInstance.Result Process(in RealtimeContext context, ProcessorInstance.Pipe pipe,
        ChannelBuffer buffer, GeneratorInstance.Arguments args)
    {
        if (!nestedValid)
        {
            // Host buffer is not pre-zeroed.
            buffer.Clear();
            return buffer.frameCount;
        }

        var result = context.Process(nested, buffer, args);

        if (result.isFinished() && !reportedFinished)
        {
            reportedFinished = true;
            pipe.SendData(context, new ClipFinishedEvent());
        }

        return result;
    }
}

/// <summary>
/// Control side. Owns the nested clip instance, forwards <see cref="SeekMessage"/> to it, and relays
/// the realtime side's completion report to the managed <see cref="ClipPlaybackState"/>.
/// </summary>
struct ClipPlayerControl : GeneratorInstance.IControl<ClipPlayerRealtime>
{
    // The control struct is unmanaged, so managed references are held as GCHandles (see RandomContainer
    // for the same pattern). Freed in Dispose.
    GCHandle m_ClipHandle;
    GCHandle m_StateHandle;

    GeneratorInstance m_Nested;
    bool m_NestedCreated;

    public ClipPlayerControl(AudioClip clip, ClipPlaybackState state)
    {
        m_ClipHandle = GCHandle.Alloc(clip, GCHandleType.Normal);
        m_StateHandle = GCHandle.Alloc(state, GCHandleType.Normal);
        m_Nested = default;
        m_NestedCreated = false;
    }

    public void Configure(ControlContext context, ref ClipPlayerRealtime realtime, in AudioFormat format,
        out GeneratorInstance.Setup setup, ref GeneratorInstance.Properties properties)
    {
        if (!m_NestedCreated)
        {
            // Instantiating the clip produces the SampleProvider-backed generator instance -- the one
            // that handles seeks. Created here rather than in Update so it exists before the first
            // Process call, and before SeekTester can send anything.
            var clip = (IAudioGenerator)m_ClipHandle.Target;

            m_Nested = clip.CreateInstance(context, format, default(GeneratorInstance.CreationParameters));
            m_NestedCreated = true;
        }
        else
        {
            // Reconfiguration (device change): keep the existing instance, re-point it at the new format.
            context.Configure(m_Nested, format);
        }

        // Run at exactly the clip's own rate and channel layout, so this wrapper stays consistent with
        // the instance it drives and the host handles any conversion. If the nested instance could not
        // be created, fall back to the host's suggested format: reporting an unset setup here surfaces
        // as an invalid speakerMode inside Process and aborts the audio thread from Burst.
        setup = m_NestedCreated
            ? context.GetConfiguration(m_Nested).setup
            : new GeneratorInstance.Setup(format);

        realtime.nested = m_Nested;
        realtime.nestedValid = m_NestedCreated;
    }

    public ProcessorInstance.Response OnMessage(ControlContext context, ProcessorInstance.Pipe pipe,
        ProcessorInstance.Message message)
    {
        // The AudioSource's generatorInstance is now this wrapper, so seeks land here. Pass them down to
        // the sample provider, which is what actually implements seeking.
        if (m_NestedCreated && message.Is<SeekMessage>())
        {
            ref var seek = ref message.Get<SeekMessage>();

            return context.SendMessage(m_Nested, ref seek);
        }

        return ProcessorInstance.Response.Unhandled;
    }

    public void Update(ControlContext context, ProcessorInstance.Pipe pipe)
    {
        foreach (var element in pipe.GetAvailableData(context))
        {
            if (element.TryGetData<ClipFinishedEvent>(out _))
            {
                var state = (ClipPlaybackState)m_StateHandle.Target;

                state.finished = true;
            }
        }

        if (m_NestedCreated)
        {
            context.Update(m_Nested);
        }
    }

    public void Dispose(ControlContext context, ref ClipPlayerRealtime realtime)
    {
        if (m_NestedCreated)
        {
            context.Destroy(m_Nested);
            m_NestedCreated = false;
        }

        m_ClipHandle.Free();
        m_StateHandle.Free();
    }
}

/// <summary>
/// Plays an <see cref="AudioClip"/> through the SampleProvider generator path, so it supports
/// seeking via <see cref="SeekMessage"/> (that path is the one that handles seeks).
///
/// Rather than handing the clip's instance straight to the AudioSource, this wraps it, so the
/// realtime side can see the clip finish and report it back to the main thread via
/// <see cref="playbackState"/>. Seeks are forwarded down to the clip's instance unchanged.
///
/// Assign a clip, then let something play it on an AudioSource
/// (<see cref="SeekTester"/> does this: <c>audioSource.generator = thisComponent</c>).
/// </summary>
public class ClipPlayerGenerator : MonoBehaviour, IAudioGenerator
{
    [Tooltip("The clip to play through the sample provider (e.g. your 10s counting file).")]
    public AudioClip clip;

    /// <summary>
    /// State shared with the currently playing instance. Replaced on each <see cref="CreateInstance"/>,
    /// and null until the first one is created.
    /// </summary>
    public ClipPlaybackState playbackState { get; private set; }

    // Asset-level metadata, mirrored from the clip so this wrapper describes what it actually produces.
    public bool isFinite => clip != null && ((IAudioGenerator)clip).isFinite;
    public bool isRealtime => clip != null && ((IAudioGenerator)clip).isRealtime;
    public DiscreteTime? length => clip != null ? ((IAudioGenerator)clip).length : null;

    // Called by the audio system when the AudioSource starts playing this generator.
    public GeneratorInstance CreateInstance(ControlContext context, AudioFormat? nestedFormat, GeneratorInstance.CreationParameters parameters)
    {
        if (clip == null)
        {
            Debug.LogError($"{nameof(ClipPlayerGenerator)}: no clip assigned.", this);
            return default;
        }

        playbackState = new ClipPlaybackState();

        var asGenerator = (IAudioGenerator)clip;
        var clipLength = asGenerator.length;

        var realtime = new ClipPlayerRealtime
        {
            clipIsFinite = asGenerator.isFinite,
            clipIsRealtime = asGenerator.isRealtime,
            clipHasLength = clipLength.HasValue,
            clipLength = clipLength ?? default
        };

        // UpdateAlways so the control side pumps the nested instance every frame, rather than only when
        // pipe data happens to be waiting.
        var creationParameters = new GeneratorInstance.CreationParameters
        {
            controlUpdateSetting = ProcessorInstance.UpdateSetting.UpdateAlways,
            realtimeUpdateSetting = ProcessorInstance.UpdateSetting.UpdateAlways
        };

        return context.AllocateGenerator(realtime, new ClipPlayerControl(clip, playbackState), nestedFormat, creationParameters);
    }
}

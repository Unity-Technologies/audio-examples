using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Audio;
using System.Runtime.InteropServices;

namespace RandomContainer
{
    // Control implementation for the RandomContainer.
    //
    // Responsibilities:
    //
    // - Own the managed list of child generator definitions.
    // - Drive the Scheduler and create child generator instances when a TriggerEvent occurs.
    // - Destroy child instances when the realtime side reports they have finished or were replaced.
    //
    // Note on GCHandle:
    //
    // The control struct is unmanaged and stored in native memory. To hold references to managed objects
    // (the IAudioGenerator children), we store GCHandles in a NativeArray. This prevents the managed objects
    // from being collected and lets us retrieve them later via handle.Target.
    internal struct RandomContainerControl : GeneratorInstance.IControl<RandomContainerRealtime>
    {
        // This example enforces a fixed output format (mono, 44.1 kHz). The host's suggested format is
        // ignored for speaker mode and sample rate; the host is responsible for any required conversion.
        // We still honor the host's suggested buffer size (frames per Process call), falling back to 256.
        private static readonly GeneratorInstance.Setup k_FixedSetup = new GeneratorInstance.Setup(AudioSpeakerMode.Mono, 44100);

        NativeArray<GCHandle> m_GCHandles;
        NativeHashSet<GeneratorInstance> m_ActiveInstances;
        int m_BufferFrameCount;
        Scheduler m_Scheduler;

        public RandomContainerControl(Span<IAudioGenerator> children, AudioFormat? nestedFormat, Preset preset)
        {
            m_GCHandles = new NativeArray<GCHandle>(children.Length, Allocator.Persistent);
            m_ActiveInstances = new NativeHashSet<GeneratorInstance>(10, Allocator.Persistent);
            m_BufferFrameCount = nestedFormat?.bufferFrameCount ?? 256;
            m_Scheduler = new Scheduler(children.Length, preset);

            for (var index = 0; index < children.Length; index++)
            {
                m_GCHandles[index] = GCHandle.Alloc(children[index], GCHandleType.Normal);
            }
        }

        public void Dispose(ControlContext context, ref RandomContainerRealtime realtime)
        {
            foreach (var instance in m_ActiveInstances)
            {
                context.Destroy(instance);
            }

            if (realtime.m_Voices.IsCreated)
            {
                realtime.m_Voices.Dispose();
            }

            if (realtime.m_ScratchBacking.IsCreated)
            {
                realtime.m_ScratchBacking.Dispose();
            }

            foreach (var handle in m_GCHandles)
            {
                handle.Free();
            }

            m_ActiveInstances.Dispose();
            m_GCHandles.Dispose();
        }

        public void Configure(ControlContext context, ref RandomContainerRealtime realtime, in AudioFormat format, out GeneratorInstance.Setup setup, ref GeneratorInstance.Properties properties)
        {
            setup = k_FixedSetup;

            // Capture the host's actual buffer size. This is the size Process will receive and the size
            // we forward to children when we create them. Configure runs again on device change, so the
            // value stays correct across reconfiguration.
            m_BufferFrameCount = format.bufferFrameCount;

            // Allocate realtime-side native resources on first call. Voices are fixed size; the scratch
            // buffer is sized to one buffer's worth of samples (mono, so frames == samples) and grown if
            // the buffer size ever increases at reconfiguration.
            if (!realtime.m_Voices.IsCreated)
            {
                realtime.m_Voices = new NativeArray<RandomContainerRealtime.Voice>(RandomContainerRealtime.k_VoiceCount, Allocator.Persistent);
            }

            if (!realtime.m_ScratchBacking.IsCreated || realtime.m_ScratchBacking.Length < m_BufferFrameCount)
            {
                if (realtime.m_ScratchBacking.IsCreated)
                {
                    realtime.m_ScratchBacking.Dispose();
                }

                realtime.m_ScratchBacking = new NativeArray<float>(m_BufferFrameCount, Allocator.Persistent);
            }

            // Children are configured with the fixed format we render in, not the host's suggestion.
            var fixedFormat = new AudioFormat(k_FixedSetup.speakerMode, k_FixedSetup.sampleRate, m_BufferFrameCount);

            foreach (var instance in m_ActiveInstances)
            {
                context.Configure(instance, fixedFormat);

                var configuration = context.GetConfiguration(instance);

                Debug.Assert(k_FixedSetup.speakerMode == configuration.setup.speakerMode, "Speaker mode mismatch");
                Debug.Assert(k_FixedSetup.sampleRate == configuration.setup.sampleRate, "Sample rate mismatch");
            }
        }

        public ProcessorInstance.Response OnMessage(ControlContext context, ProcessorInstance.Pipe pipe,
            ProcessorInstance.Message message)
        {
            return ProcessorInstance.Response.Unhandled;
        }

        public void Update(ControlContext context, ProcessorInstance.Pipe pipe)
        {
            // Handle "destroy this instance" requests coming from the realtime side.
            // See Events.cs for notes about InstanceEvent being used in both directions.
            foreach (var element in pipe.GetAvailableData(context))
            {
                if (element.TryGetData<InstanceEvent>(out var instanceEvent))
                {
                    var oldInstance = instanceEvent.instance;

                    m_ActiveInstances.Remove(oldInstance);

                    context.Destroy(oldInstance);
                }
            }

            var optionalEvent = m_Scheduler.Update(Time.deltaTime);

            if (optionalEvent.HasValue)
            {
                var index = optionalEvent.Value.index;
                var level = optionalEvent.Value.level;
                var generator = (IAudioGenerator)m_GCHandles[index].Target;

                // UpdateAlways (not the default UpdateIfDataIsAvailable) because the scheduler
                // advances on every frame regardless of incoming pipe data.
                var creationParameters = new ProcessorInstance.CreationParameters
                {
                    controlUpdateSetting = ProcessorInstance.UpdateSetting.UpdateAlways,
                    realtimeUpdateSetting = ProcessorInstance.UpdateSetting.UpdateAlways
                };

                var format = new AudioFormat(k_FixedSetup.speakerMode, k_FixedSetup.sampleRate, m_BufferFrameCount);

                var newInstance = generator.CreateInstance(context, format, creationParameters);

                var configuration = context.GetConfiguration(newInstance);

                Debug.Assert(k_FixedSetup.speakerMode == configuration.setup.speakerMode, "Speaker mode mismatch");
                Debug.Assert(k_FixedSetup.sampleRate == configuration.setup.sampleRate, "Sample rate mismatch");

                m_ActiveInstances.Add(newInstance);

                pipe.SendData(context, new InstanceEvent { instance = newInstance, level = level });
            }

            foreach (var instance in m_ActiveInstances)
            {
                context.Update(instance);
            }
        }
    }
}

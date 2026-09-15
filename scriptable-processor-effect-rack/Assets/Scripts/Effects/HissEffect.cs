using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;
using Random = UnityEngine.Random;

namespace RadioEffectRack
{
    /// <summary>
    /// Mixes a bed of static under the signal, so the channel sounds open even between phrases.
    ///
    /// The noise comes from a nested <see cref="WhiteNoiseGenerator"/>. An effect that owns a nested
    /// processor owns all of it: create it with the format it will run at, tick it from the control
    /// part, render it from Process inside the same mix cycle, and destroy it in Dispose.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct HissProcessor : EffectInstance.IRealtime
    {
        // Sized for the widest speaker layout, so a device change cannot outgrow it. AudioSpeakerMode
        // tops out at 7.1.
        const int k_MaxChannels = 8;

        /// <summary>Static level, linear. Sent from the component and forwarded over the pipe.</summary>
        internal struct Level
        {
            internal float level;
        }

        GeneratorInstance m_Noise;
        internal NativeArray<float> scratch;
        internal float level;

        internal HissProcessor(GeneratorInstance noise, float level)
        {
            m_Noise = noise;
            scratch = default;
            this.level = level;
        } 

        public void Update(UpdatedDataContext context, Pipe pipe)
        {
            foreach (var element in pipe.GetAvailableData(context))
            {
                if (element.TryGetData(out Level data))
                    level = data.level;
            }
        }

        public EffectInstance.Result Process(in RealtimeContext context, ChannelBuffer inputBuffer,
            ChannelBuffer outputBuffer, EffectInstance.Arguments args)
        {
            var sampleCount = inputBuffer.frameCount * inputBuffer.channelCount;

            // If the host asks for more than Configure sized for, pass through: nothing on the audio thread
            // may allocate, and every output sample still has to be written.
            if (!scratch.IsCreated || scratch.Length < sampleCount)
            {
                for (var channel = 0; channel < inputBuffer.channelCount; channel++)
                {
                    for (var frame = 0; frame < inputBuffer.frameCount; frame++)
                    {
                        outputBuffer[channel, frame] = inputBuffer[channel, frame];
                    }
                }

                return default;
            }

            // A nested processor runs when its owner runs it, from inside the owner's Process.
            var scratchBuffer = new ChannelBuffer(scratch.AsSpan()[..sampleCount], inputBuffer.channelCount);
            var written = context.Process(m_Noise, scratchBuffer, default).processedFrames;

            for (var channel = 0; channel < inputBuffer.channelCount; channel++)
            {
                for (var frame = 0; frame < inputBuffer.frameCount; frame++)
                {
                    // Read before writing: input and output can be the same memory.
                    var input = inputBuffer[channel, frame];
                    var noise = frame < written ? scratchBuffer[channel, frame] : 0f;

                    outputBuffer[channel, frame] = input + noise * level;
                }
            }

            return default;
        }

        internal struct Control : EffectInstance.IControl<HissProcessor>
        {
            GeneratorInstance m_Noise;
            float m_Level;

            internal Control(GeneratorInstance noise, float level)
            {
                m_Noise = noise;
                m_Level = level;
            }

            public void Configure(ControlContext context, ref HissProcessor processor,
                in AudioConfiguration configuration, out EffectInstance.Setup setup)
            {
                setup = default;

                // Control side, realtime suspended: the place to allocate.
                var sampleCount = configuration.dspBufferSize * k_MaxChannels;

                if (processor.scratch.IsCreated && processor.scratch.Length != sampleCount)
                    processor.scratch.Dispose();

                if (!processor.scratch.IsCreated)
                    processor.scratch = new NativeArray<float>(sampleCount, Allocator.Persistent);

                processor.level = m_Level;

                // A child may only be reconfigured during a system-wide reconfiguration, which is the case that
                // matters: a new output device. Not so on the Configure that runs at creation, where the child
                // was just built with this format, and asking again is a recursive configuration change that
                // the audio system rejects.
                if (context.IsSystemWideReconfiguring)
                    context.Configure(m_Noise, new AudioFormat(configuration));
            }

            public void Dispose(ControlContext context, ref HissProcessor processor)
            {
                // The nested generator and the scratch buffer both belong to this effect.
                context.Destroy(m_Noise);

                if (processor.scratch.IsCreated)
                    processor.scratch.Dispose();
            }

            public void Update(ControlContext context, Pipe pipe)
            {
                // Nothing updates a nested processor on its own. Hence, UpdateSetting.UpdateAlways.
                context.Update(m_Noise);
            }

            public Response OnMessage(ControlContext context, Pipe pipe, Message message)
            {
                if (!message.Is<Level>())
                    return Response.Unhandled;

                m_Level = math.clamp(message.Get<Level>().level, 0f, 1f);
                pipe.SendData(context, new Level { level = m_Level });

                return Response.Handled;
            }
        }
    }

    /// <summary>Add this next to an AudioSource to mix static under whatever the source plays.</summary>
    [RequireComponent(typeof(AudioSource))]
    public class HissEffect : MonoBehaviour, IAudioEffect
    {
        [Tooltip("Level of the static bed, in dB.")]
        [Range(-80f, -12f)]
        public float levelDb = -44f;

        AudioSource m_Source;
        float m_SentLevelDb;

        public EffectInstance CreateInstance(ControlContext context, AudioFormat? nestedFormat,
            EffectInstance.CreationParameters creationParameters)
        {
            // A child is created with the format it will be rendered at:
            // the owner's when nested, otherwise the system configuration.
            var format = nestedFormat ?? new AudioFormat(AudioSettings.GetConfiguration());
            var level = DecibelToLinear(levelDb);
            var seed = (uint)Random.Range(1, int.MaxValue);

            var noise = context.AllocateGenerator(new WhiteNoiseGenerator(seed),
                new WhiteNoiseGenerator.Control(), format);

            // Assign both update settings, even where one of them is UpdateSetting.Default's documented
            // behavior. The two are not independent: the default is only applied when neither has been
            // assigned, so assigning just one leaves the other with no update behavior at all and its
            // Update is never called. Nothing warns when that happens, so assign both in your own
            // effects too. This is a known issue and will be fixed later.
            //
            // Control updates every tick, because that is where the nested generator is updated.
            // Realtime updates only when a parameter arrives.
            creationParameters.controlUpdateSetting = UpdateSetting.UpdateAlways;
            creationParameters.realtimeUpdateSetting = UpdateSetting.UpdateIfDataIsAvailable;

            return context.AllocateEffect(
                new HissProcessor(noise, level),
                new HissProcessor.Control(noise, level),
                nestedFormat,
                creationParameters);
        }

        void Awake()
        {
            m_Source = GetComponent<AudioSource>();
            m_SentLevelDb = levelDb;
        }

        void Update()
        {
            if (Mathf.Approximately(levelDb, m_SentLevelDb))
                return;

            var instance = m_Source.GetEffectInstance(this);

            if (!ControlContext.builtIn.Exists(instance))
                return;

            var message = new HissProcessor.Level { level = DecibelToLinear(levelDb) };
            ControlContext.builtIn.SendMessage(instance, ref message);

            m_SentLevelDb = levelDb;
        }

        static float DecibelToLinear(float decibels) => Mathf.Pow(10f, decibels / 20f);
    }
}

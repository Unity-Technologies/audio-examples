using System;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;

namespace RadioEffectRack
{
    /// <summary>
    /// Silences the signal between phrases, the way a squelch circuit does. An envelope follower tracks
    /// the loudest channel, and the gate opens while that envelope sits above the threshold.
    ///
    /// This effect is the example of data traveling the other way: the audio thread posts the envelope
    /// to the control side over the pipe, and the control side hands it back to the UI when asked.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct NoiseGateProcessor : EffectInstance.IRealtime
    {
        /// <summary>Gate parameters in the units a person thinks in. Sent from the component.</summary>
        internal struct Settings
        {
            internal float thresholdDb;
            internal float attackMs;
            internal float releaseMs;
        }

        /// <summary>The same settings converted for the audio thread. Sent over the pipe.</summary>
        struct Tuning
        {
            internal float threshold;
            internal float attack;
            internal float release;
        }

        /// <summary>What the audio thread reports back, once per real-time update.</summary>
        internal struct Status
        {
            internal float envelope;
            internal bool isOpen;
        }

        /// <summary>
        /// Sent by the UI to read the newest <see cref="Status"/> back out. Messages are passed by
        /// reference, so the control part answers by writing into the message.
        /// </summary>
        internal struct StatusQuery
        {
            internal Status status;
            internal bool hasStatus;
        }

        Tuning m_Tuning;
        float m_Envelope;
        float m_Gain;

        public void Update(UpdatedDataContext context, Pipe pipe)
        {
            foreach (var element in pipe.GetAvailableData(context))
            {
                if (element.TryGetData(out Tuning tuning))
                    m_Tuning = tuning;
            }

            // Post the gate state. Update is where the realtime part is handed the pipe; an effect's
            // Process is not, unlike a generator's. SendData returns false when the pipe is full, which
            // here only means nobody has read the meter for a while.
            pipe.SendData(context, new Status { envelope = m_Envelope, isOpen = m_Gain > 0.5f });
        }

        public EffectInstance.Result Process(in RealtimeContext context, ChannelBuffer inputBuffer,
            ChannelBuffer outputBuffer, EffectInstance.Arguments args)
        {
            for (var frame = 0; frame < inputBuffer.frameCount; frame++)
            {
                // Follow the loudest channel, so the gate opens and closes for all of them together. Every
                // input for a frame is read before any output is written: they can be the same memory.
                var peak = 0f;

                for (var channel = 0; channel < inputBuffer.channelCount; channel++)
                {
                    peak = math.max(peak, math.abs(inputBuffer[channel, frame]));
                }

                // Rise on attack, fall on release.
                m_Envelope += (peak > m_Envelope ? m_Tuning.attack : m_Tuning.release) * (peak - m_Envelope);

                // Smooth the decision too, so the gate fades rather than clicks.
                var target = m_Envelope >= m_Tuning.threshold ? 1f : 0f;
                m_Gain += (target > m_Gain ? m_Tuning.attack : m_Tuning.release) * (target - m_Gain);

                for (var channel = 0; channel < inputBuffer.channelCount; channel++)
                {
                    outputBuffer[channel, frame] = inputBuffer[channel, frame] * m_Gain;
                }
            }

            return default;
        }

        internal struct Control : EffectInstance.IControl<NoiseGateProcessor>
        {
            Settings m_Settings;
            int m_SampleRate;

            // The audio thread posts one reading per mix block, slower than a UI polls, so the newest is
            // kept and every query answered from it.
            Status m_LastStatus;
            bool m_HasStatus;

            internal Control(Settings settings)
            {
                m_Settings = settings;
                m_SampleRate = 0;
                m_LastStatus = default;
                m_HasStatus = false;
            }

            public void Configure(ControlContext context, ref NoiseGateProcessor processor,
                in AudioConfiguration configuration, out EffectInstance.Setup setup)
            {
                setup = default;

                // Times, so the coefficients depend on the sample rate.
                m_SampleRate = configuration.sampleRate;
                processor.m_Tuning = ComputeTuning();
            }

            public void Dispose(ControlContext context, ref NoiseGateProcessor processor) { }

            public void Update(ControlContext context, Pipe pipe)
            {
                // Consume as they arrive, so the pipe does not back up while nothing reads the meter.
                Drain(context, pipe);
            }

            /// <summary>Takes every reading the audio thread has posted and keeps the newest.</summary>
            void Drain(ControlContext context, Pipe pipe)
            {
                foreach (var element in pipe.GetAvailableData(context))
                {
                    if (element.TryGetData(out Status status))
                    {
                        m_LastStatus = status;
                        m_HasStatus = true;
                    }
                }
            }

            public Response OnMessage(ControlContext context, Pipe pipe, Message message)
            {
                if (message.Is<Settings>())
                {
                    var settings = message.Get<Settings>();

                    m_Settings.thresholdDb = math.clamp(settings.thresholdDb, -80f, 0f);
                    m_Settings.attackMs = math.clamp(settings.attackMs, 0.1f, 1000f);
                    m_Settings.releaseMs = math.clamp(settings.releaseMs, 1f, 5000f);

                    pipe.SendData(context, ComputeTuning());

                    return Response.Handled;
                }

                if (message.Is<StatusQuery>())
                {
                    // Get returns a reference to the caller's own message, so writing to it is the reply.
                    ref var query = ref message.Get<StatusQuery>();

                    // hasStatus means the effect has reported at least once, not that a reading arrived since you
                    // last asked. A caller polling faster than the mix rate gets the most recent value, not a gap.
                    Drain(context, pipe);

                    query.status = m_LastStatus;
                    query.hasStatus = m_HasStatus;

                    return Response.Handled;
                }

                return Response.Unhandled;
            }

            Tuning ComputeTuning() => new Tuning
            {
                threshold = math.pow(10f, m_Settings.thresholdDb / 20f),
                attack = TimeConstant(m_Settings.attackMs, m_SampleRate),
                release = TimeConstant(m_Settings.releaseMs, m_SampleRate),
            };

            /// <summary>One-pole coefficient for a time constant given in milliseconds.</summary>
            static float TimeConstant(float milliseconds, int sampleRate)
            {
                if (sampleRate <= 0)
                    return 1f;

                var samples = math.max(1f, milliseconds * 0.001f * sampleRate);

                return math.saturate(1f - math.exp(-1f / samples));
            }
        }
    }

    /// <summary>Add this next to an AudioSource to gate whatever the source plays.</summary>
    [RequireComponent(typeof(AudioSource))]
    public class NoiseGateEffect : MonoBehaviour, IAudioEffect
    {
        [Tooltip("The gate opens while the signal sits above this level.")]
        [Range(-60f, 0f)]
        public float thresholdDb = -34f;

        [Tooltip("How fast the gate opens.")]
        [Range(0.5f, 50f)]
        public float attackMs = 3f;

        [Tooltip("How fast the gate closes again.")]
        [Range(10f, 500f)]
        public float releaseMs = 140f;

        AudioSource m_Source;
        NoiseGateProcessor.Settings m_Sent;

        public EffectInstance CreateInstance(ControlContext context, AudioFormat? nestedFormat,
            EffectInstance.CreationParameters creationParameters)
        {
            // Both assigned together: see HissEffect for why assigning one alone disables the other.
            // Realtime every mix cycle, because that is where the envelope is posted.
            creationParameters.realtimeUpdateSetting = UpdateSetting.UpdateAlways;
            creationParameters.controlUpdateSetting = UpdateSetting.UpdateIfDataIsAvailable;

            return context.AllocateEffect(
                new NoiseGateProcessor(),
                new NoiseGateProcessor.Control(CurrentSettings()),
                nestedFormat,
                creationParameters);
        }

        /// <summary>
        /// Reads the newest envelope back out of the running effect. Returns false when the effect is
        /// not running, or has not reported anything yet.
        /// </summary>
        public bool TryGetEnvelope(out float envelopeDb, out bool isOpen)
        {
            envelopeDb = k_SilenceDb;
            isOpen = false;

            if (!enabled || !m_Source.isPlaying || m_Source.bypassEffects)
                return false;

            var instance = m_Source.GetEffectInstance(this);

            if (!ControlContext.builtIn.Exists(instance))
                return false;

            var query = new NoiseGateProcessor.StatusQuery();

            // Evaluated straight away, so the answer is in the message when it returns.
            if (ControlContext.builtIn.SendMessage(instance, ref query) != Response.Handled || !query.hasStatus)
                return false;

            envelopeDb = query.status.envelope > 0.0001f
                ? 20f * Mathf.Log10(query.status.envelope)
                : k_SilenceDb;
            isOpen = query.status.isOpen;

            return true;
        }

        internal const float k_SilenceDb = -80f;

        void Awake()
        {
            m_Source = GetComponent<AudioSource>();
            m_Sent = CurrentSettings();
        }

        void Update()
        {
            var settings = CurrentSettings();

            if (Mathf.Approximately(settings.thresholdDb, m_Sent.thresholdDb)
                && Mathf.Approximately(settings.attackMs, m_Sent.attackMs)
                && Mathf.Approximately(settings.releaseMs, m_Sent.releaseMs))
                return;

            var instance = m_Source.GetEffectInstance(this);

            if (!ControlContext.builtIn.Exists(instance))
                return;

            ControlContext.builtIn.SendMessage(instance, ref settings);

            m_Sent = settings;
        }

        NoiseGateProcessor.Settings CurrentSettings() => new NoiseGateProcessor.Settings
        {
            thresholdDb = thresholdDb,
            attackMs = attackMs,
            releaseMs = releaseMs,
        };
    }
}

using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;

namespace RadioEffectRack
{
    /// <summary>
    /// Measures the signal without changing it, and reports the level in each of sixteen frequency
    /// bands so the panel can draw them as a histogram.
    ///
    /// The frequency split is a bank of two-pole state-variable band-pass filters rather than an FFT,
    /// which keeps a reading down to sixteen numbers. That fits in a single <see cref="float4x4"/>, so
    /// it rides the pipe as one message per mix block and the audio thread and the UI share no memory.
    ///
    /// The filters need to be selective to be worth drawing. A pair of one-poles, as BandPassEffect
    /// uses to shape audio, only rejects about 6 dB two octaves from its centre, so every band would
    /// mostly report the overall level. These reject about 22 dB, which is enough to see a spectrum.
    ///
    /// It is an ordinary effect component, so where it sits in the chain decides what it measures.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct SpectrumProcessor : EffectInstance.IRealtime
    {
        internal const int k_BandCount = 16;

        internal const float k_LowestHz = 100f;
        internal const float k_HighestHz = 8000f;
        const float k_AttackMs = 5f;
        const float k_ReleaseMs = 90f;

        // Matched to the band spacing: no gaps, little overlap.
        const float k_Q = 3.5f;

        // Damping. The band-pass output peaks at Q, so the same number scales it back to unity.
        const float k_Damping = 1f / k_Q;

        /// <summary>
        /// One band: a topology-preserving state-variable filter and the envelope of its band-pass
        /// output. The three coefficients come from the centre frequency and Q; the two state values
        /// are the integrators.
        /// </summary>
        internal struct Band
        {
            internal float a1;
            internal float a2;
            internal float a3;
            internal float ic1eq;
            internal float ic2eq;
            internal float envelope;
        }

        /// <summary>One reading of every band, posted once per mix block.</summary>
        internal struct Reading
        {
            internal float4x4 levels;
        }

        /// <summary>
        /// Sent by the UI to read the newest levels out. The caller owns the destination array; the
        /// control part fills one entry per band, lowest first.
        /// </summary>
        internal struct LevelQuery
        {
            internal NativeArray<float> destination;
            internal bool hasLevels;
        }

        internal NativeArray<Band> m_Bands;
        float m_AttackCoefficient;
        float m_ReleaseCoefficient;
        Reading m_Reading;

        public void Update(UpdatedDataContext context, Pipe pipe)
        {
            // Update is where the realtime part is handed the pipe; an effect's Process is not.
            pipe.SendData(context, m_Reading);
        }

        public EffectInstance.Result Process(in RealtimeContext context, ChannelBuffer inputBuffer,
            ChannelBuffer outputBuffer, EffectInstance.Arguments args)
        {
            if (!m_Bands.IsCreated)
            {
                // Guards the gap before the first Configure.
                for (var channel = 0; channel < inputBuffer.channelCount; channel++)
                {
                    for (var frame = 0; frame < inputBuffer.frameCount; frame++)
                    {
                        outputBuffer[channel, frame] = inputBuffer[channel, frame];
                    }
                }

                return default;
            }

            var scale = 1f / inputBuffer.channelCount;

            for (var frame = 0; frame < inputBuffer.frameCount; frame++)
            {
                // Sum to mono for the analysis, pass every channel through untouched.
                // Read before writing: input and output can be the same memory.
                var mono = 0f;

                for (var channel = 0; channel < inputBuffer.channelCount; channel++)
                {
                    var input = inputBuffer[channel, frame];

                    mono += input;
                    outputBuffer[channel, frame] = input;
                }

                mono *= scale;

                for (var index = 0; index < k_BandCount; index++)
                {
                    var band = m_Bands[index];

                    // One filter step. v1 is the band-pass output, peaking at Q, so scale it back before measuring.
                    var v3 = mono - band.ic2eq;
                    var v1 = band.a1 * band.ic1eq + band.a2 * v3;
                    var v2 = band.ic2eq + band.a2 * band.ic1eq + band.a3 * v3;

                    band.ic1eq = 2f * v1 - band.ic1eq;
                    band.ic2eq = 2f * v2 - band.ic2eq;

                    var magnitude = math.abs(v1 * k_Damping);
                    var coefficient = magnitude > band.envelope ? m_AttackCoefficient : m_ReleaseCoefficient;

                    band.envelope += coefficient * (magnitude - band.envelope);

                    m_Bands[index] = band;
                }
            }

            m_Reading = new Reading { levels = new float4x4(Group(0), Group(4), Group(8), Group(12)) };

            return default;
        }

        /// <summary>Four band envelopes, so a whole column packs into one matrix.</summary>
        float4 Group(int first) => new float4(
            m_Bands[first].envelope,
            m_Bands[first + 1].envelope,
            m_Bands[first + 2].envelope,
            m_Bands[first + 3].envelope);

        internal struct Control : EffectInstance.IControl<SpectrumProcessor>
        {
            Reading m_Latest;
            bool m_HasReading;

            public void Configure(ControlContext context, ref SpectrumProcessor processor,
                in AudioConfiguration configuration, out EffectInstance.Setup setup)
            {
                setup = default;

                if (!processor.m_Bands.IsCreated)
                    processor.m_Bands = new NativeArray<Band>(k_BandCount, Allocator.Persistent);

                // Log spaced. The coefficients depend on the sample rate, so the bank is rebuilt here and its
                // state cleared: the old memory belongs to the old rate.
                var sampleRate = configuration.sampleRate;
                var octaves = math.log2(k_HighestHz / k_LowestHz);

                for (var index = 0; index < k_BandCount; index++)
                {
                    var centre = k_LowestHz * math.pow(2f, octaves * index / (k_BandCount - 1f));

                    processor.m_Bands[index] = Coefficients(centre, sampleRate);
                }

                processor.m_AttackCoefficient = TimeConstant(k_AttackMs, sampleRate);
                processor.m_ReleaseCoefficient = TimeConstant(k_ReleaseMs, sampleRate);
            }

            public void Dispose(ControlContext context, ref SpectrumProcessor processor)
            {
                if (processor.m_Bands.IsCreated)
                    processor.m_Bands.Dispose();
            }

            public void Update(ControlContext context, Pipe pipe)
            {
                Drain(context, pipe);
            }

            /// <summary>Keeps the newest reading the audio thread has posted.</summary>
            void Drain(ControlContext context, Pipe pipe)
            {
                foreach (var element in pipe.GetAvailableData(context))
                {
                    if (element.TryGetData(out Reading reading))
                    {
                        m_Latest = reading;
                        m_HasReading = true;
                    }
                }
            }

            public Response OnMessage(ControlContext context, Pipe pipe, Message message)
            {
                if (!message.Is<LevelQuery>())
                    return Response.Unhandled;

                Drain(context, pipe);

                ref var query = ref message.Get<LevelQuery>();
                var destination = query.destination;

                if (!destination.IsCreated || !m_HasReading || destination.Length < k_BandCount)
                {
                    query.hasLevels = false;

                    return Response.Handled;
                }

                // One entry per band, lowest first.
                for (var band = 0; band < k_BandCount; band++)
                {
                    destination[band] = m_Latest.levels[band >> 2][band & 3];
                }

                query.hasLevels = true;

                return Response.Handled;
            }

            /// <summary>
            /// Prewarped coefficients for one band, with its integrator state left at zero.
            /// </summary>
            static Band Coefficients(float centreHz, int sampleRate)
            {
                if (sampleRate <= 0)
                    return default;

                // Clear of Nyquist, where the prewarping runs away.
                var centre = math.min(centreHz, sampleRate * 0.45f);
                var g = math.tan(math.PI * centre / sampleRate);
                var a1 = 1f / (1f + g * (g + k_Damping));
                var a2 = g * a1;

                return new Band
                {
                    a1 = a1,
                    a2 = a2,
                    a3 = g * a2,
                };
            }

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

    /// <summary>Add this next to an AudioSource to watch the spectrum at that point in the chain.</summary>
    [RequireComponent(typeof(AudioSource))]
    public class SpectrumEffect : MonoBehaviour, IAudioEffect
    {
        AudioSource m_Source;
        NativeArray<float> m_Levels;

        /// <summary>How many bands a reading holds, lowest first.</summary>
        public int bandCount => SpectrumProcessor.k_BandCount;

        /// <summary>Centre frequency of the lowest band.</summary>
        public float lowestHz => SpectrumProcessor.k_LowestHz;

        /// <summary>Centre frequency of the highest band.</summary>
        public float highestHz => SpectrumProcessor.k_HighestHz;

        /// <summary>The level of one band from the last read.</summary>
        public float Level(int band) => m_Levels.IsCreated ? m_Levels[band] : 0f;

        public EffectInstance CreateInstance(ControlContext context, AudioFormat? nestedFormat,
            EffectInstance.CreationParameters creationParameters)
        {
            // Both spelled out together: see HissEffect for why setting one alone turns the other off.
            creationParameters.realtimeUpdateSetting = UpdateSetting.UpdateAlways;
            creationParameters.controlUpdateSetting = UpdateSetting.UpdateIfDataIsAvailable;

            return context.AllocateEffect(
                new SpectrumProcessor(),
                new SpectrumProcessor.Control(),
                nestedFormat,
                creationParameters);
        }

        /// <summary>Reads the newest levels out of the running effect. False when it isn't running.</summary>
        public bool TryReadLevels()
        {
            if (!m_Levels.IsCreated)
                return false;

            var instance = m_Source.GetEffectInstance(this);

            if (!ControlContext.builtIn.Exists(instance))
                return false;

            var query = new SpectrumProcessor.LevelQuery { destination = m_Levels };

            if (ControlContext.builtIn.SendMessage(instance, ref query) != Response.Handled)
                return false;

            return query.hasLevels;
        }

        void Awake()
        {
            m_Source = GetComponent<AudioSource>();
            m_Levels = new NativeArray<float>(SpectrumProcessor.k_BandCount, Allocator.Persistent);
        }

        void OnDestroy()
        {
            if (m_Levels.IsCreated)
                m_Levels.Dispose();
        }
    }
}

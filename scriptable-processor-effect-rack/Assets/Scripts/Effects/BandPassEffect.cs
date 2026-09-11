using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;

namespace RadioEffectRack
{
    /// <summary>
    /// Band-limits the signal to a narrow speech band. Throwing away the lowest and highest octaves is
    /// most of what makes a voice sound like it came out of a small radio speaker.
    ///
    /// This effect is the example of a filter whose coefficients depend on the sample rate: they are
    /// computed on the control side, in Configure and whenever a parameter changes, and the audio
    /// thread only ever sees finished numbers.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct BandPassProcessor : EffectInstance.IRealtime
    {
        // AudioSpeakerMode tops out at 7.1. Sized for the widest layout, so a device change to more
        // channels never needs a bigger allocation.
        internal const int k_MaxChannels = 8;

        /// <summary>Cutoffs in Hz. Sent from the component with ControlContext.SendMessage.</summary>
        internal struct Cutoffs
        {
            internal float lowCutHz;
            internal float highCutHz;

            internal Cutoffs(float lowCutHz, float highCutHz)
            {
                this.lowCutHz = lowCutHz;
                this.highCutHz = highCutHz;
            }
        }

        /// <summary>
        /// One-pole coefficients, which is what the audio thread actually needs. Sent over the pipe by
        /// the control part, so the conversion from Hz never happens in Process.
        /// </summary>
        internal struct Coefficients
        {
            internal float lowPass;
            internal float highPass;
        }

        Coefficients m_Coefficients;
        internal NativeArray<float> m_LowPassState;
        internal NativeArray<float> m_HighPassState;

        /// <summary>Zeroes the filter memory. Called from Configure, where the realtime part is idle.</summary>
        internal void ClearState()
        {
            for (var channel = 0; channel < m_LowPassState.Length; channel++)
            {
                m_LowPassState[channel] = 0f;
                m_HighPassState[channel] = 0f;
            }
        }

        public void Update(UpdatedDataContext context, Pipe pipe)
        {
            foreach (var element in pipe.GetAvailableData(context))
            {
                if (element.TryGetData(out Coefficients coefficients))
                    m_Coefficients = coefficients;
            }
        }

        public EffectInstance.Result Process(in RealtimeContext context, ChannelBuffer inputBuffer,
            ChannelBuffer outputBuffer, EffectInstance.Arguments args)
        {
            // Configure allocates the state, so this only guards the gap before the first Configure.
            var filteredChannels = m_LowPassState.IsCreated
                ? math.min(inputBuffer.channelCount, k_MaxChannels)
                : 0;

            for (var channel = 0; channel < filteredChannels; channel++)
            {
                // Into locals, so the inner loop stays in registers.
                var lowPass = m_LowPassState[channel];
                var highPass = m_HighPassState[channel];

                for (var frame = 0; frame < inputBuffer.frameCount; frame++)
                {
                    // Read before writing: input and output can be the same memory.
                    var input = inputBuffer[channel, frame];

                    // A one-pole lowpass minus a slower one leaves the band between the two cutoffs.
                    lowPass += m_Coefficients.lowPass * (input - lowPass);
                    highPass += m_Coefficients.highPass * (lowPass - highPass);

                    outputBuffer[channel, frame] = lowPass - highPass;
                }

                m_LowPassState[channel] = lowPass;
                m_HighPassState[channel] = highPass;
            }

            // Channels past the state pass through. The output buffer starts undefined, so write every
            // sample on every path, including this one.
            for (var channel = filteredChannels; channel < inputBuffer.channelCount; channel++)
            {
                for (var frame = 0; frame < inputBuffer.frameCount; frame++)
                {
                    outputBuffer[channel, frame] = inputBuffer[channel, frame];
                }
            }

            return default;
        }

        internal struct Control : EffectInstance.IControl<BandPassProcessor>
        {
            Cutoffs m_Cutoffs;
            int m_SampleRate;

            internal Control(Cutoffs cutoffs)
            {
                m_Cutoffs = cutoffs;
                m_SampleRate = 0;
            }

            public void Configure(ControlContext context, ref BandPassProcessor processor,
                in AudioConfiguration configuration, out EffectInstance.Setup setup)
            {
                setup = default;

                m_SampleRate = configuration.sampleRate;

                // The realtime part is suspended here, so its fields can be written directly and this is where
                // to allocate. Everywhere else the values travel over the pipe.
                processor.m_Coefficients = ComputeCoefficients();

                if (!processor.m_LowPassState.IsCreated)
                {
                    // Already zeroed.
                    processor.m_LowPassState = new NativeArray<float>(k_MaxChannels, Allocator.Persistent);
                    processor.m_HighPassState = new NativeArray<float>(k_MaxChannels, Allocator.Persistent);
                }
                else
                {
                    // Configure runs again on a device change, and the memory belongs to the old sample rate.
                    processor.ClearState();
                }
            }

            public void Dispose(ControlContext context, ref BandPassProcessor processor)
            {
                if (processor.m_LowPassState.IsCreated)
                    processor.m_LowPassState.Dispose();

                if (processor.m_HighPassState.IsCreated)
                    processor.m_HighPassState.Dispose();
            }

            public void Update(ControlContext context, Pipe pipe) { }

            public Response OnMessage(ControlContext context, Pipe pipe, Message message)
            {
                if (!message.Is<Cutoffs>())
                    return Response.Unhandled;

                var cutoffs = message.Get<Cutoffs>();

                // Clamp before the audio thread sees it. A one-pole coefficient means nothing above Nyquist,
                // and the band has to stay the right way round.
                var nyquist = m_SampleRate > 0 ? m_SampleRate * 0.5f : 24000f;

                m_Cutoffs.lowCutHz = math.clamp(cutoffs.lowCutHz, 20f, nyquist - 1f);
                m_Cutoffs.highCutHz = math.clamp(cutoffs.highCutHz, m_Cutoffs.lowCutHz + 50f, nyquist - 1f);

                pipe.SendData(context, ComputeCoefficients());

                return Response.Handled;
            }

            Coefficients ComputeCoefficients() => new Coefficients
            {
                lowPass = OnePole(m_Cutoffs.highCutHz, m_SampleRate),
                highPass = OnePole(m_Cutoffs.lowCutHz, m_SampleRate),
            };

            /// <summary>The usual one-pole coefficient, 1 - exp(-2*pi*f/sampleRate).</summary>
            static float OnePole(float cutoffHz, int sampleRate)
            {
                if (sampleRate <= 0)
                    return 1f;

                return math.saturate(1f - math.exp(-2f * math.PI * cutoffHz / sampleRate));
            }
        }
    }

    /// <summary>
    /// Add this next to an AudioSource to band-limit whatever the source plays. Unity finds the
    /// component on its own: there is no field to assign.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class BandPassEffect : MonoBehaviour, IAudioEffect
    {
        [Tooltip("Everything below this rolls off.")]
        [Range(50f, 2000f)] public float lowCutHz = 400f;

        [Tooltip("Everything above this rolls off.")]
        [Range(800f, 8000f)] public float highCutHz = 2600f;

        AudioSource m_Source;
        float m_SentLowCutHz;
        float m_SentHighCutHz;

        public EffectInstance CreateInstance(ControlContext context, AudioFormat? nestedFormat,
            EffectInstance.CreationParameters creationParameters)
        {
            return context.AllocateEffect(
                new BandPassProcessor(),
                new BandPassProcessor.Control(new BandPassProcessor.Cutoffs(lowCutHz, highCutHz)),
                nestedFormat,
                creationParameters);
        }

        void Awake()
        {
            m_Source = GetComponent<AudioSource>();
            m_SentLowCutHz = lowCutHz;
            m_SentHighCutHz = highCutHz;
        }

        void Update()
        {
            if (Mathf.Approximately(lowCutHz, m_SentLowCutHz) && Mathf.Approximately(highCutHz, m_SentHighCutHz))
                return;

            // An effect is addressed by its component, since a GameObject can carry several. The instance
            // only exists while the source plays, so nothing counts as sent until it goes out.
            var instance = m_Source.GetEffectInstance(this);

            if (!ControlContext.builtIn.Exists(instance))
                return;

            var message = new BandPassProcessor.Cutoffs(lowCutHz, highCutHz);
            ControlContext.builtIn.SendMessage(instance, ref message);

            m_SentLowCutHz = lowCutHz;
            m_SentHighCutHz = highCutHz;
        }
    }
}

using System;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;

namespace RadioEffectRack
{
    /// <summary>
    /// Pushes the signal into a soft clipper, then trims the result back down. Cheap transmitter
    /// distortion, and the simplest complete effect in this project: no state, no allocation.
    ///
    /// The clipper normalizes its own peak, but clipping raises the average level far more than the
    /// peak, so a driven signal still sounds much louder. The output trim is what makes it possible to
    /// match levels while dialing the drive around.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct DriveProcessor : EffectInstance.IRealtime
    {
        /// <summary>
        /// Pre-gain into the clipper, and the linear gain applied after it. Travels from the component
        /// and over the pipe unchanged.
        /// </summary>
        internal struct Settings
        {
            internal float drive;
            internal float output;

            internal Settings(float drive, float output)
            {
                this.drive = drive;
                this.output = output;
            }
        }

        float m_Drive;
        float m_Output;

        internal DriveProcessor(float drive, float output)
        {
            m_Drive = drive;
            m_Output = output;
        }

        public void Update(UpdatedDataContext context, Pipe pipe)
        {
            foreach (var element in pipe.GetAvailableData(context))
            {
                if (element.TryGetData(out Settings settings))
                {
                    m_Drive = settings.drive;
                    m_Output = settings.output;
                }
            }
        }

        public EffectInstance.Result Process(in RealtimeContext context, ChannelBuffer inputBuffer,
            ChannelBuffer outputBuffer, EffectInstance.Arguments args)
        {
            // One factor for two jobs: normalizing the clipper's peak, and the output trim.
            var scale = m_Output / math.tanh(m_Drive);

            for (var frame = 0; frame < inputBuffer.frameCount; frame++)
            {
                for (var channel = 0; channel < inputBuffer.channelCount; channel++)
                {
                    // Read before writing: input and output can be the same memory.
                    var input = inputBuffer[channel, frame];

                    outputBuffer[channel, frame] = math.tanh(input * m_Drive) * scale;
                }
            }

            return default;
        }

        internal struct Control : EffectInstance.IControl<DriveProcessor>
        {
            public void Configure(ControlContext context, ref DriveProcessor processor,
                in AudioConfiguration configuration, out EffectInstance.Setup setup)
            {
                setup = default;
            }

            public void Dispose(ControlContext context, ref DriveProcessor processor) { }

            public void Update(ControlContext context, Pipe pipe) { }

            public Response OnMessage(ControlContext context, Pipe pipe, Message message)
            {
                if (!message.Is<Settings>())
                    return Response.Unhandled;

                // Clamp before forwarding. A drive of 0 divides by zero in the normalizer.
                var settings = message.Get<Settings>();

                pipe.SendData(context, new Settings(
                    math.clamp(settings.drive, 1f, 60f),
                    math.clamp(settings.output, 0f, 4f)));

                return Response.Handled;
            }
        }
    }

    /// <summary>Add this next to an AudioSource to overdrive whatever the source plays.</summary>
    [RequireComponent(typeof(AudioSource))]
    public class DriveEffect : MonoBehaviour, IAudioEffect
    {
        [Tooltip("Pre-gain into the soft clipper. Higher values sound more crushed.")]
        [Range(1f, 40f)]
        public float drive = 9f;

        [Tooltip("Gain applied after the clipper, in dB. Use it to match levels as you change the drive.")]
        [Range(-40f, 6f)]
        public float outputDb;

        AudioSource m_Source;
        float m_SentDrive;
        float m_SentOutputDb;

        public EffectInstance CreateInstance(ControlContext context, AudioFormat? nestedFormat,
            EffectInstance.CreationParameters creationParameters)
        {
            // The values that bypass OnMessage's clamp.
            return context.AllocateEffect(
                new DriveProcessor(Mathf.Max(drive, 1f), DecibelToLinear(outputDb)),
                new DriveProcessor.Control(),
                nestedFormat,
                creationParameters);
        }

        void Awake()
        {
            m_Source = GetComponent<AudioSource>();
            m_SentDrive = drive;
            m_SentOutputDb = outputDb;
        }

        void Update()
        {
            if (Mathf.Approximately(drive, m_SentDrive) && Mathf.Approximately(outputDb, m_SentOutputDb))
                return;

            var instance = m_Source.GetEffectInstance(this);

            if (!ControlContext.builtIn.Exists(instance))
                return;

            var message = new DriveProcessor.Settings(drive, DecibelToLinear(outputDb));
            ControlContext.builtIn.SendMessage(instance, ref message);

            m_SentDrive = drive;
            m_SentOutputDb = outputDb;
        }

        static float DecibelToLinear(float decibels) => Mathf.Pow(10f, decibels / 20f);
    }
}

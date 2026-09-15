using System;
using Unity.Burst;
using Unity.IntegerTime;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;
using Random = Unity.Mathematics.Random;

namespace RadioEffectRack
{
    /// <summary>
    /// White noise, used by <see cref="HissEffect"/> as its static bed.
    ///
    /// Nothing in here knows that it runs nested inside an effect: a generator is written the same way
    /// whether an AudioSource drives it or another processor does. The difference is all on the owning
    /// side, which creates it, updates it, renders it, and destroys it.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct WhiteNoiseGenerator : GeneratorInstance.IRealtime
    {
        Random m_Random;

        // Never ends, no timeline to seek along.
        public bool isFinite => false;
        public bool isRealtime => false;
        public DiscreteTime? length => null;

        internal WhiteNoiseGenerator(uint seed)
        {
            // Random rejects a zero seed, and throwing here would surface while an effect is being set up.
            m_Random = new Random(seed == 0u ? 1u : seed);
        }

        public void Update(UpdatedDataContext context, Pipe pipe) { }

        public GeneratorInstance.Result Process(in RealtimeContext context, Pipe pipe, ChannelBuffer buffer,
            GeneratorInstance.Arguments args)
        {
            for (var frame = 0; frame < buffer.frameCount; frame++)
            {
                // One sample to every channel: correlated noise, like a single small speaker.
                var sample = m_Random.NextFloat(-1f, 1f);

                for (var channel = 0; channel < buffer.channelCount; channel++)
                {
                    buffer[channel, frame] = sample;
                }
            }

            return buffer.frameCount;
        }

        internal struct Control : GeneratorInstance.IControl<WhiteNoiseGenerator>
        {
            public void Configure(ControlContext context, ref WhiteNoiseGenerator realtime, in AudioFormat format,
                out GeneratorInstance.Setup setup, ref GeneratorInstance.Properties properties)
            {
                // Noise sounds the same at any rate, so match the owner and leave nothing to convert.
                setup = new GeneratorInstance.Setup(format);
            }

            public void Dispose(ControlContext context, ref WhiteNoiseGenerator realtime) { }

            public void Update(ControlContext context, Pipe pipe) { }

            public Response OnMessage(ControlContext context, Pipe pipe, Message message) => Response.Unhandled;
        }
    }
}

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.IntegerTime;
using Unity.Mathematics;
using UnityEngine.Audio;

namespace RandomContainer
{
    // The RandomContainer Realtime implementation is responsible for mixing the active child instances.
    //
    // Conceptually, the container has two distinct sizes:
    //
    // - The number of available generator definitions.
    // - The number of simultaneous active child instances (voices).
    //
    // Control creates instances based on the Scheduler and sends them over the pipe as InstanceEvent messages.
    // Realtime receives those instances and inserts them into one of the voice slots.
    [BurstCompile(CompileSynchronously = true)]
    internal struct RandomContainerRealtime : GeneratorInstance.IRealtime
    {
        // Maximum number of concurrent voices mixed at once (simple fixed polyphony for the example).
        internal const int k_VoiceCount = 4;

        internal struct Voice
        {
            public GeneratorInstance instance;
            public bool isValid;

            // Monotonic creation counter used to identify the "oldest" instance when replacing.
            public UInt64 creationCounter;

            // Per-instance level (dB) provided by the scheduler / control side.
            public float level;
        };

        public bool isFinite => false;
        public bool isRealtime => false;
        public DiscreteTime? length => null;

        internal NativeArray<Voice> m_Voices;

        // Persistent scratch buffer used by Process to collect each child's output before
        // accumulating into the host buffer. Allocated / resized from Configure.
        internal NativeArray<float> m_ScratchBacking;

        internal UInt64 m_CreationCounter;

        // Chooses which voice slot to use for a newly created instance.
        // - Prefer an unused slot if possible.
        // - Otherwise replace the oldest active instance.
        int NextAvailableChildIndex()
        {
            var bestIndex = 0;
            var lowerBound = UInt64.MaxValue;

            for (var index = 0; index < k_VoiceCount; index++)
            {
                if (!m_Voices[index].isValid)
                {
                    return index;
                }

                if (m_Voices[index].creationCounter < lowerBound)
                {
                    bestIndex = index;
                    lowerBound = m_Voices[index].creationCounter;
                }
            }

            return bestIndex;
        }

        public void Update(ProcessorInstance.UpdatedDataContext context, ProcessorInstance.Pipe pipe)
        {
            // Receive newly created instances from the control side and place them into a voice slot.
            foreach (var element in pipe.GetAvailableData(context))
            {
                if (element.TryGetData<InstanceEvent>(out var instanceEvent))
                {
                    var nextAvailableChildIndex = NextAvailableChildIndex();

                    // If we are about to replace an existing voice, ask the control side to destroy the old instance.
                    if (m_Voices[nextAvailableChildIndex].isValid)
                    {
                        var oldInstance = m_Voices[nextAvailableChildIndex].instance;

                        pipe.SendData(context, new InstanceEvent { instance = oldInstance });
                    }

                    var newInstance = instanceEvent.instance;

                    m_Voices[nextAvailableChildIndex] = new Voice
                    {
                        instance = newInstance,
                        isValid = true,
                        creationCounter = m_CreationCounter++,
                        level = instanceEvent.level,
                    };
                }
            }
        }

        public GeneratorInstance.Result Process(in RealtimeContext context, ProcessorInstance.Pipe pipe,
            ChannelBuffer buffer, GeneratorInstance.Arguments args)
        {
            // Slice the persistent scratch buffer down to the current Process call's frame count.
            var scratchSpan = m_ScratchBacking.AsSpan().Slice(0, buffer.frameCount * buffer.channelCount);
            var scratchBuffer = new ChannelBuffer(scratchSpan, buffer.channelCount);

            // Host buffer is not pre-zeroed; clear before mixing voices into it.
            buffer.Clear();

            for (var index = 0; index < k_VoiceCount; index++)
            {
                if (m_Voices[index].isValid)
                {
                    var gain = math.pow(10.0f, m_Voices[index].level / 20.0f);

                    var result = context.Process(m_Voices[index].instance, scratchBuffer, args);

                    for (var channel = 0; channel < buffer.channelCount; channel++)
                    {
                        for (var frame = 0; frame < buffer.frameCount; frame++)
                        {
                            buffer[channel, frame] += scratchBuffer[channel, frame] * gain;
                        }
                    }

                    // A finite child reports completion by returning 0 processed frames. Evict the
                    // slot and ask control to destroy the instance.
                    if (result.processedFrames == 0)
                    {
                        pipe.SendData(context, new InstanceEvent { instance = m_Voices[index].instance });
                        m_Voices[index] = new Voice { isValid = false };
                    }
                }
            }

            return buffer.frameCount;
        }
    }
}

using Random = Unity.Mathematics.Random;

namespace RandomContainer
{
    // Lightweight "scheduler". Produces TriggerEvent values that the control side turns into actual generator instances.
    internal struct Scheduler
    {
        int m_PlayedCount;
        int m_ChildIndex;
        int m_ChildCount;
        float m_Accumulator;

        static Random m_Random = new Random(0x12345678);

        public Mode mode;
        public int maxPlayCount;
        public float interval;
        public float randomizeLevel;

        public Scheduler(int childCount, Preset preset)
        {
            m_PlayedCount = 0;
            m_ChildIndex = 0;
            m_ChildCount = childCount;

            // Seed at interval so the first trigger fires immediately on the first Update, not after a delay.
            m_Accumulator = preset.interval;

            mode = preset.mode;
            maxPlayCount = preset.maxPlayCount;
            interval = preset.interval;
            randomizeLevel = preset.randomizeLevel;
        }

        // Advances the scheduler. When a trigger event occurs, returns a TriggerEvent describing which generator
        // index to play and the per-trigger randomized level.
        public TriggerEvent? Update(float deltaTime)
        {
            if (m_ChildCount == 0 || m_PlayedCount >= maxPlayCount) { return null; }

            TriggerEvent? optionalTriggerEvent = null;

            if (m_Accumulator >= interval)
            {
                m_Accumulator -= interval;

                var level = m_Random.NextFloat(-randomizeLevel, +randomizeLevel);

                switch (mode)
                {
                    case Mode.Sequential:

                        optionalTriggerEvent = new TriggerEvent
                        {
                            index = m_ChildIndex,
                            level = level
                        };

                        m_ChildIndex += 1;

                        if (m_ChildIndex >= m_ChildCount)
                        {
                            m_ChildIndex = 0;
                        }

                        break;

                    case Mode.Random:

                        optionalTriggerEvent = new TriggerEvent
                        {
                            index = m_Random.NextInt(0, m_ChildCount),
                            level = level
                        };

                        break;
                }

                m_PlayedCount += 1;
            }

            m_Accumulator += deltaTime;

            return optionalTriggerEvent;
        }
    }
}

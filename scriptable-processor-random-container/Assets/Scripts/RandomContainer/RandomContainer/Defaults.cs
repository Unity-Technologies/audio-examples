using UnityEngine.Audio;

namespace RandomContainer
{
    public enum Mode { Sequential, Random }

    // Defaults used both by the RandomContainerAsset inspector fields and by the runtime Scheduler.
    //
    // Field units:
    // - mode: How the next generator index is chosen (sequential vs random).
    // - maxPlayCount: Maximum number of triggers before the scheduler stops producing events.
    // - interval: Seconds between triggers.
    // - randomizeLevel: Random +/- range in decibels (dB) applied per trigger.
    public struct Preset
    {
        public static readonly Preset Default = new Preset
        {
            mode = Mode.Sequential,
            maxPlayCount = 10,
            interval = 0.5f,
            randomizeLevel = 0.0f
        };

        public Mode mode;
        public int maxPlayCount;
        public float interval;
        public float randomizeLevel;
    }
}

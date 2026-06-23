using UnityEngine.Audio;

namespace RandomContainer
{
    // Message payload shared between the control and realtime sides.
    //
    // Important: InstanceEvent is used in both directions:
    //
    // - Control -> Realtime: Newly created instance to be mixed.
    // - Realtime -> Control: Finished instance to be destroyed.
    public struct InstanceEvent { public GeneratorInstance instance; public float level; }

    // A scheduler output describing which generator to instantiate next and with which per-trigger level.
    public struct TriggerEvent
    {
        public int index;
        public float level;
    }
}

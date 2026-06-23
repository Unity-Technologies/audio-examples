# Random Container Example Project

This example demonstrates how to implement a simplified version of the Audio Random Container using nested generators. Specifically, it covers the following:

- A simple scheduler that picks children either sequentially or randomly and triggers them at a fixed interval.
- Polyphony with oldest-voice replacement; the realtime side maintains an array of voice slots and recycles the oldest as new trigger events arrive.
- A random level offset applied to each new trigger.
- A clean split between the control part and the realtime part, with bidirectional events used for lifetime coordination.

The project targets Unity 6.5 and is built on the [Scriptable audio pipeline][manual] APIs.

[manual]: https://docs.unity3d.com/6000.3/Documentation/Manual/audio-scriptable-processors.html

## Run the demo

Open the project in Unity 6.5, open `Assets/Scenes/Random Container.unity`, and enter Play Mode.

The scene wires three preset Random Containers in `Assets/RandomContainers/` to their own `AudioSource`s. Each button triggers one of them, demonstrating a different pattern:

- **`Forest.asset`**: many short clips chosen at random with level jitter; a classic ambience layer.
- **`Machine_Gun.asset`**: a single clip retriggered at tight intervals; shows voice overlap and decay.
- **`Count_Master.asset`**: nested containers. Its children are themselves Random Containers, demonstrating that an `IAudioGenerator` parent does not care what kind of generator its children are.

## The Random Container asset

A Random Container is a `ScriptableObject` (`RandomContainerAsset` in `Assets/Scripts/RandomContainer/RandomContainer.cs`) that you drop into the `Generator` field of an `AudioSource`. The inspector exposes five fields:

| Field            | Meaning                                                                                                  |
|------------------|----------------------------------------------------------------------------------------------------------|
| `children`       | The pool of generators to choose from. Any `IAudioGenerator` works: AudioClips, other Random Containers, or your own custom generators. |
| `mode`           | `Sequential` cycles through children; `Random` picks children uniformly.                                 |
| `maxPlayCount`   | How many total triggers to produce before going silent.                                                  |
| `interval`       | Interval between triggers in seconds.                                                                    |
| `randomizeLevel` | ± volume range in dB applied to each triggered child.                                                    |

## Implementation

The implementation is located in `Assets/Scripts/RandomContainer/` and is split across six files. The split follows the [control vs realtime model][concepts] described in the manual.

[concepts]: https://docs.unity3d.com/6000.3/Documentation/Manual/audio-scriptable-processors-concepts.html

| File                              | Role                                                                                                                                       |
|-----------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------|
| `RandomContainer.cs`              | The `ScriptableObject` entry point. Implements `IAudioGenerator.CreateInstance` to wire up the control / realtime pair.                    |
| `RandomContainer/Control.cs`      | Main-thread side; owns child references, drives the scheduler, creates and destroys child generators in response to triggers and realtime-side eviction requests. |
| `RandomContainer/Realtime.cs`     | Audio-thread side; mixes the active child voices into the output buffer. Burst-compiled.                                                  |
| `RandomContainer/Scheduler.cs`    | A small accumulator that emits a `TriggerEvent` every `interval` seconds.                                                                  |
| `RandomContainer/Events.cs`       | The message types exchanged over the pipe.                                                                                             |
| `RandomContainer/Defaults.cs`     | Default inspector values and the `Mode` enum.                                                                                              |

The two halves communicate through a single bidirectional pipe carrying `InstanceEvent` messages:

- **Control -> Realtime**: "Here is a new child instance, start mixing it into a voice slot."
- **Realtime -> Control**: "I am done with this instance (it either finished by itself or it was replaced), please destroy it."

Voices live in a fixed-size pool of slots. When a new instance arrives and all slots are full, the oldest one is kicked out and reused for the new instance.

The control side stores its children behind `GCHandle`s in a `NativeArray`. See the comment block at the top of `Control.cs` for why that pattern is necessary when bridging managed and unmanaged memory.

## Notes

This is meant as a teaching example. A few things are intentionally limited:

- **Fixed output format**: The container ignores the host's suggested speaker mode and sample rate, leaving any conversion to the host.
- **Hardcoded polyphony**: No voice-stealing heuristics beyond "pick oldest".
- **Not sample-accurate**: Trigger events are truncated to buffer boundaries rather than placed at exact sample positions.

The source files are commented. Reading them top-to-bottom alongside the [Generators][generators] and [Concepts][concepts] pages is the fastest way to understand the code.

[generators]: https://docs.unity3d.com/6000.3/Documentation/Manual/audio-scriptable-processors-generators.html

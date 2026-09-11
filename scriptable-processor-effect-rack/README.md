# Effect Rack Example Project

This example demonstrates **scriptable effects**: custom audio processors that transform the signal an `AudioSource` plays. A rack of small effects turns speech into radio chatter. It covers the following:

- A chain of effect components on a single `AudioSource`, processed in component order, assembled at runtime, while the audio source plays.
- Addressing one effect out of several with `AudioSource.GetEffectInstance`, and bypassing it by disabling its component.
- Filter coefficients derived from the audio configuration in `Configure`, and rebuilt when the output device changes.
- Data travelling out of the audio thread: the noise gate reports its envelope over the pipe, and the control part hands it back to the UI through a query message.
- A nested generator owned by an effect: the hiss effect creates a white noise generator, renders it into a scratch buffer from its own `Process`, and destroys it when the effect goes away.

The project targets Unity 6.7 and is built on the [scriptable audio pipeline][manual] APIs, specifically the part covered in the [effects][effects] section.

[manual]: https://docs.unity3d.com/6000.7/Documentation/Manual/audio-scriptable-processors.html
[effects]: https://docs.unity3d.com/6000.7/Documentation/Manual/audio-scriptable-processors-effects.html

## Run the demo

Open the project in Unity 6.7, open `Assets/Scenes/EffectRack.unity`, and enter Play Mode.

The scene contains a single **Radio** GameObject holding an `AudioSource` with `Count1To10.wav` and the `EffectRack` panel, plus a camera that provides the `AudioListener`. The rack starts empty and the clip plays dry until you build one. A GUI panel appears in the top left:

- Playback starts **stopped**. Use the **Play/Stop** button. The clip loops by default.
- **Bypass Effects** takes every effect out at once, which is the quickest way to hear the dry clip.
- Each effect has an **on/bypassed** toggle and its own parameters. Everything is live: drag a slider while the clip plays and you hear it immediately.
- **Handheld radio**, **Blown speaker** and **Spectrum only** each clear the rack and rebuild it. They differ in what they contain and in what order, not just in their settings, so loading one is an audible teardown and rebuild rather than a parameter change. **Clear** empties the rack.
- Each effect row has **^** and **v** buttons that move it up/down the chain, and an **x** button that removes it mid-playback. The audio source rediscovers its effects when the components change, so the chain rebuilds while the clip keeps playing.

## The effects

Effects run in the order their components were added, which the panel numbers top to bottom:

| Effect        | What it does                                                                                  |
|--------------|-----------------------------------------------------------------------------------------------|
| Band pass    | Rolls off everything outside a speech band, roughly 400 Hz to 2.6 kHz.                         |
| Drive        | Pushes the signal into a soft clipper, for cheap transmitter distortion, then trims the output back down. |
| Noise gate   | Silences the signal between phrases, and reports its envelope back for the meter.               |
| Hiss         | Mixes a bed of white noise under the signal, from a generator nested inside the effect.         |
| Spectrum     | Measures the signal without changing it, and reports the level in each of sixteen frequency bands for the panel to draw as a histogram. A bank of two-pole state-variable filters, not a transform. |

## Implementation

The implementation is in `Assets/Scripts/`. Each effect is one file holding the whole effect: the component, its real-time part, its control part, and the messages they exchange. The split follows the [control vs realtime model][concepts] described in the manual.

[concepts]: https://docs.unity3d.com/6000.7/Documentation/Manual/audio-scriptable-processors-concepts.html

| File                            | Role                                                                                                                                                 |
|---------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Effects/BandPassEffect.cs`       | Two one-pole filters. Shows coefficients computed on the control side, per-channel state allocated in `Configure`, and that state cleared when the device changes. |
| `Effects/DriveEffect.cs`          | A stateless soft clipper with an output trim. The simplest complete effect here: no state, no allocation, and both parameters folded into one per-block multiply. |
| `Effects/NoiseGateEffect.cs`      | An envelope follower and gate. Shows the pipe used in both directions, and a query message answered by writing into the caller's own message.          |
| `Effects/HissEffect.cs`           | A noise bed. Shows an effect owning a nested processor, and a scratch buffer allocated in `Configure` and released in `Dispose`.                        |
| `Effects/WhiteNoiseGenerator.cs` | The generator the hiss effect nests. Written exactly like a generator attached to an `AudioSource`.                                                     |
| `Effects/SpectrumEffect.cs`       | A pass-through effect that measures. A filterbank keeps a reading down to sixteen numbers, which packs into one `float4x4` and rides the pipe as a single message per block, so no shared memory is needed between the audio thread and the UI. |
| `EffectRack.cs`                 | The demo panel. Reads the chain back off the GameObject in component order, edits the effects while they play, and builds, reorders or clears the rack. Each preset is an ordered list of `AddComponent` calls, and a reorder rebuilds the chain from the swap onwards. |

Two rules shape all of the `Process` methods:

- **Read the input sample before writing the output sample.** When Unity runs an attached effect, the input and output buffers can reference the same memory.
- **Write every output sample on every code path**, including the paths that pass audio through untouched. The output buffer starts undefined, and Unity does not clear it.

## Notes

This is meant as a teaching example. A few things are intentionally limited:

- **One-pole filters**: The band pass is two first-order sections, so its slopes are gentle. A real radio effect would use steeper filters, at the cost of a longer example.
- **No parameter smoothing**: Parameters jump to their new value when a message arrives. Dragging a slider fast can produce a small click. Production effects ramp between values across a buffer.
- **Fixed channel budget**: The band pass and the hiss effect size their buffers for eight channels, which covers every `AudioSpeakerMode`. Anything wider passes through untouched.
- **Coarse spectrum**: The analysis splits the signal into sixteen log-spaced bands, which is enough to see the band pass and the drive at work but far short of a transform. A real analyser would use an FFT, at the cost of moving readings through shared memory rather than the pipe.
- **Correlated noise**: The hiss effect writes the same noise sample to every channel, so the static is mono. Decorrelated noise would need one random stream per channel.

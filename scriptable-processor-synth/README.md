# Scriptable Processor Synth

This example demonstrates what is possible with scriptable audio generators and scriptable effects in Unity 6.7. A polyphonic synth is generated live in C# as an `IAudioGenerator`, with no audio clips, and a chain of separate `IAudioEffect` components (chorus, low pass, high pass, delay, reverb) processes it. A marble you roll around the scene is the controller; the game is only a way to play the synth.

The project targets Unity 6.7 and is built on the [Scriptable audio pipeline][manual] APIs. Mac Editor only for now.

[manual]: https://docs.unity3d.com/6000.7/Documentation/Manual/audio-scriptable-processors.html

## What it shows

- **Generator:** a code-defined synth with a voice pool, oscillators, envelopes and sample-accurate note scheduling, driven live by gameplay.
- **Effects:** scriptable effect components after the generator, reorderable in the Inspector, with parameters changing in real time.
- **One engine, many instruments:** the presets (Classic, Pad, Pluck, Bass, Brass, Strings) are the same synth with different parameters.
- **Audio driven by anything:** notes come from physics (a marble landing), a chord progression, an arpeggiator and the keyboard, so the same pipeline can be driven by game events, not only by a music player.

## Run the demo

Open the project in Unity 6.7, open one of the two scenes under `Assets/Scenes/` and enter Play Mode. Both scenes build their objects at runtime, so the Scene view is empty outside Play Mode.

- `Synth.unity` (hex pillars): a field of basalt pillars. Pillar height picks the note and the chord follows a progression loop (Pop, 50s, Jazz, Min, Epic). It shows the pipeline as a generative instrument.
- `Piano.unity` (piano): a 3 octave piano from C3 to B5. Each key is a fixed pitch, so you play melodies on purpose. Landing on a key plays its note plus the chord tones from the 3rd/5th/7th/9th toggles.

The HUD at the top left controls the instrument, key, chords and effects. The marble has a waveform visualizer inside it, fed by the main audio.

### Controls

- Arrow keys: roll the marble
- Space: jump
- Mouse: use the on-screen HUD (selectors, knobs, buttons)
- Keyboard notes: E F T G Y H U J K, with Z/X changing the octave

## The scripts

Under `Assets/Scripts/`, grouped by folder:

| Folder | Role |
|--------|------|
| `Audio/` | `ScriptableAudioPipelineAndEffects.cs` is the `IAudioGenerator`: 12 voices, each with 1-3 detuned saw or pulse oscillators, a sub oscillator and a resonant filter with envelope. `SynthPatch.cs` holds the instrument presets, `StepSequencer.cs` a sequencer (off by default), `KeyboardPlayer.cs` the keyboard input. |
| `Effects/` | One `IAudioEffect` component per effect, chained in component order: `ChorusEffect`, `LowPassEffect`, `HighPassEffect`, `DelayEffect`, `ReverbEffect`, and `ScopeTapEffect` (feeds the marble waveform, so keep it last). Parameters are public fields sent to the audio thread as messages each frame by `EffectSend`. |
| `Music/` | `PillarMusic.cs` turns landings into notes. `ArpEngine.cs` keeps a pool of landed notes and arpeggiates them from four notes up; more notes means a faster arp and more delay and reverb. |
| `Game/` | The marble, its waveform display and the camera. |
| `Pillars/`, `Piano/` | Generation of the two scenes. |
| `UI/` | `SynthHud.cs`, a runtime IMGUI panel. |

`Assets/Editor/SynthWindow.cs` adds an editor window for the synth.

## Credits

Originally authored by Kjetil Kalla in the `ScriptableAudioPipelineAndEffects` repository.

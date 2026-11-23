# Scriptable Processor Synth

This example demonstrates what is possible with scriptable audio generators and scriptable effects in Unity 6.7. A polyphonic synth is generated live in C# as an `IAudioGenerator`, with no audio clips, and a chain of separate `IAudioEffect` components (chorus, low pass, high pass, delay, reverb) processes it. A marble you roll around the scene is the controller; the game is only a way to play the synth.

The project targets Unity 6.7 and is built on the [Scriptable audio pipeline][manual] APIs. See the manual pages on [generators][generators] and [effects][effects] for the concepts used here.

[manual]: https://docs.unity3d.com/6000.7/Documentation/Manual/audio-scriptable-processors.html
[generators]: https://docs.unity3d.com/6000.7/Documentation/Manual/audio-scriptable-processors-generators.html
[effects]: https://docs.unity3d.com/6000.7/Documentation/Manual/audio-scriptable-processors-effects.html

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

The marble is a faint glass sphere at the center of the screen, and the HUD has a fixed pixel size, so in a small Game view the HUD can cover it. Use **Maximize On Play** on the Game view.

### Controls

- Arrow keys: roll the marble
- Space: jump
- Mouse: use the on-screen HUD (selectors, knobs, buttons)
- Keyboard notes: the keys E F T G Y H U J K form one chromatic octave, laid out like a piano, with F G H J K as the white keys and E T Y U as the black keys. Starting from the default octave 4 they play D#4 F4 F#4 G4 G#4 A4 A#4 B4 C5. Z and X move the octave down and up.

### HUD reference

The HUD has two cards. Everything on it changes the sound live, and nothing is saved between Play Mode sessions.

**Left card (what gets played)**

| Control | What it does |
|---------|--------------|
| SEQ ON/OFF | Starts or stops the step sequencer (see [Sequencer](#sequencer)). Landings, the arp and the keyboard keep working either way. |
| Wave | Oscillator shape: Sine, Tri, Saw or Square. Choosing an instrument also sets the wave. |
| Key | Root note of the scale, C to B. Landing pitches are chosen within two octaves of it. |
| Scale | Which notes are allowed: Major, Minor, Dorian, Phrygian, Lydian, Mixolydian, Pentatonic Major (Pent+), Pentatonic Minor (Pent-), Blues or Chromatic. |
| Chord | Notes stacked on top of each landing, in scale steps: 3rd, 5th, 7th, 9th. Inv randomly raises some of the lowest chord notes an octave. Bass adds the root one octave lower. |
| Progression | Off plays the pillar or key you land on. Pop, 50s, Jazz, Min and Epic make the chord roots follow a looping progression, and the landing picks a chord tone as the melody. The current chord name is shown next to the title. |
| Hold xN | How many landings each chord lasts in a progression: 1, 2, 4 or 8. |
| Instrument | Loads a preset: Classic, Pad, Pluck, Bass, Brass or Strings. A preset sets the wave, the oscillator unison and detune, the sub oscillator, the filter envelope, the chorus mode and the ADSR knobs. Tweak the knobs afterwards to taste. |

**Right card (how it sounds)**

| Control | What it does |
|---------|--------------|
| Attack, Decay, Sustain, Release | The amplitude envelope. Attack, decay and release are times in seconds, sustain is a level from 0 to 1. Applies to new notes. |
| LPF | Low pass filter cutoff in Hz. Anything above the cutoff is removed. Fully open at the default. |
| HPF | High pass filter cutoff in Hz. Anything below the cutoff is removed. Fully open at the default. |
| Reverb Mix, Size, Damp | Mix is the wet amount, Size is the room size and Damp is how quickly high frequencies die away in the tail. |
| Delay Time, Feedback, Mix | Time is the gap between echoes in seconds, Feedback is how much of each echo is fed back in, and Mix is the wet amount. |
| BPM | Tempo of the step sequencer, 40 to 220. The arp follows it too. |
| Chorus | Off, I, II or I+II: the two classic chorus voicings, on their own or combined. |
| ARP ON/OFF and count | Turns the marble arpeggiator on or off. The number is how many landed notes it is holding (at most 4). It starts at 3, and the arp raises the delay and reverb mix while it plays. The pool empties after 10 seconds without a landing. |

### Sequencer

The step sequencer plays a looping 16 step pattern of 16th notes at the BPM knob, using the current wave and envelope. The default pattern is C3 E3 G3 E3 C3 E3 A3 G3 with rests between notes. It is off by default, and turning it on restarts from step 0. To change the pattern, edit the Steps list on the `StepSequencer` component: each step is a note name like `C4` or `F#3`, an empty string is a rest, and a step can hold a chord such as `C3 E3 G3`. The component's own Arp setting is separate from the ARP button above.

## The scripts

To learn the API, start with `Audio/ScriptableAudioPipelineAndEffects.cs` (the generator) and then `Effects/LowPassEffect.cs` (the smallest effect) and `Effects/EffectSend.cs` (how parameters reach the audio thread). The other folders are gameplay and UI that drive them.

Under `Assets/Scripts/`, grouped by folder:

| Folder | Role |
|--------|------|
| `Audio/` | `ScriptableAudioPipelineAndEffects.cs` is the `IAudioGenerator`: 12 voices, each with 1-3 detuned saw or pulse oscillators, a sub oscillator and a resonant filter with envelope. `SynthPatch.cs` holds the instrument presets, `StepSequencer.cs` a sequencer (off by default), `KeyboardPlayer.cs` the keyboard input. |
| `Effects/` | One `IAudioEffect` component per effect, chained in component order: `ChorusEffect`, `LowPassEffect`, `HighPassEffect`, `DelayEffect`, `ReverbEffect`, and `ScopeTapEffect` (feeds the marble waveform, so keep it last). Parameters are public fields sent to the audio thread as messages each frame by `EffectSend`. |
| `Music/` | `PillarMusic.cs` turns landings into notes. `ArpEngine.cs` is the marble arpeggiator (see the ARP row in the HUD reference). |
| `Game/` | `MarbleBall.cs` (rolling and jumping), `MarbleScope.cs` (the waveform inside the marble), `IsoCamera.cs` (the isometric follow camera) and `FloatingNote.cs` (the note names that rise from the marble). |
| `Pillars/` | `HexCauseway.cs` builds the `Synth.unity` field of hex pillars and the marble at runtime. `HexPillar.cs` is one pillar, which plays a note when landed on. |
| `Piano/` | `PianoRoll.cs` builds the `Piano.unity` piano and the marble at runtime. `PianoKey.cs` is one key, which plays its note when landed on. |
| `UI/` | `SynthHud.cs`, a runtime IMGUI panel. |

`Assets/Editor/SynthWindow.cs` adds a **Window > Synth** editor window with a live view of the synth. In Play Mode in the Synth scene it shows a sequencer toggle, a wave selector, knobs for the filters, reverb, delay and BPM, a piano, and a scope and spectrum display.

## Credits

Originally authored by Kjetil Kalla in the `ScriptableAudioPipelineAndEffects` repository.

# Seeking Example Project

This example demonstrates **seeking** for scriptable processors: repositioning a playing generator to a new point on its timeline, either immediately or scheduled to fire sample-accurately when playback reaches a chosen position.

The project targets Unity 6.7 and is built on the [Scriptable audio pipeline][manual] APIs. Seeking is delivered to a live generator instance with `SeekMessage`.

[manual]: https://docs.unity3d.com/6000.7/Documentation/Manual/audio-scriptable-processors.html

## Run the demo

Open the project in Unity 6.7, open `Assets/Scenes/Seeking.unity`, and enter Play Mode.

The scene contains a single **Seek Tester** GameObject (a `SeekTester` plus a `ClipPlayerGenerator` with `Count1To10.wav` assigned) and a Main Camera that provides the `AudioListener`. A GUI panel appears in the top-left in Play Mode:

- Playback starts **stopped**. Use the **Play/Stop** button.
- Pick **when** a seek should fire (the clip second at which it triggers) with the slider, then click a **number button** to enqueue "when the clip reaches that second, jump so I hear number N".
- Enable **Fire immediately** to ignore `when` and seek at the next process block instead.
- You can build a queue while stopped; it is delivered when you press **Play**, so several seeks end up scheduled together in the sample provider's own queue.
- The panel schedules on whole-second boundaries for legibility; `SeekMessage` itself accepts any `DiscreteTime`.

The clip counts "one" through "ten" over roughly ten seconds, so the audible result makes each seek easy to verify by ear.

## How seeking works

A seek is a message sent to a **live** generator instance. There are two forms:

| Call | Effect |
|------|--------|
| `new SeekMessage(offset)` | Immediate seek: jump to `offset` at the next process block. |
| `new SeekMessage(offset, when)` | Scheduled seek: jump to `offset` when the timeline reaches `when`. |

Both `offset` and `when` are [`DiscreteTime`][integertime] values; `DiscreteTime(int)` is interpreted as whole seconds here. The message is delivered with:

```csharp
var msg = new SeekMessage(offset, when);
var response = ControlContext.builtIn.SendMessage(instance, ref msg);
```

The instance only exists a frame or so after `AudioSource.Play()`, so `SeekTester` waits until `ControlContext.builtIn.Exists(source.generatorInstance)` is true before flushing its queued seeks.

Seeks can be scheduled in **any order** — the sample provider fires each one when playback reaches its `when`. A seek whose `when` playback has **already passed** (for example, one made unreachable by an earlier seek that jumped past it) is silently dropped rather than applied.

[integertime]: https://docs.unity3d.com/6000.7/Documentation/ScriptReference/Unity.IntegerTime.DiscreteTime.html

## The scripts

The implementation is two small `MonoBehaviour`s in `Assets/Scripts/`.

| File                       | Role                                                                                                                                          |
|----------------------------|---------------------------------------------------------------------------------------------------------------------------------------------|
| `ClipPlayerGenerator.cs`   | An `IAudioGenerator` that wraps the clip's own sample-provider instance (`clip.CreateInstance`), which is the path that handles seeks. Its realtime side forwards each `Process` call to that nested instance and watches the returned `Result`; `SeekMessage`s arriving at the wrapper are forwarded down unchanged. When the nested instance reports `isFinished()`, the realtime side sends a datum over the pipe and the control side relays it to `ClipPlaybackState` on the main thread. |
| `SeekTester.cs`            | A manual seek playground. Wires the generator onto an `AudioSource`, draws the GUI transport, sends `SeekMessage`s to the live instance, and stops the source when `ClipPlaybackState` reports the clip has finished. |

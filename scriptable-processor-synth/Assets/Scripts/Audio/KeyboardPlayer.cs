using UnityEngine;
using UnityEngine.InputSystem;

// Play the synth with the computer keyboard: E F T G Y H U J K (A W S D are disabled, they are free for other uses). Z/X = octave down/up.
[RequireComponent(typeof(ScriptableAudioPipelineAndEffects))]
public class KeyboardPlayer : MonoBehaviour
{
    [Range(0, 3)] public int wave = 2;
    public float attack = 0.01f, release = 0.2f;
    public int octave = 4;

    static readonly Key[] Keys = { Key.A, Key.W, Key.S, Key.E, Key.D, Key.F, Key.T, Key.G, Key.Y, Key.H, Key.U, Key.J, Key.K };
    ScriptableAudioPipelineAndEffects synth;

    void Start() { synth = GetComponent<ScriptableAudioPipelineAndEffects>(); }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb[Key.Z].wasPressedThisFrame) octave--;
        if (kb[Key.X].wasPressedThisFrame) octave++;
        System.Array.Clear(SynthScope.keysDown, 0, 12);
        for (int i = 0; i < Keys.Length; i++)
        {
            if (Keys[i] == Key.A || Keys[i] == Key.W || Keys[i] == Key.S || Keys[i] == Key.D) continue;
            if (kb[Keys[i]].isPressed) SynthScope.keysDown[i % 12] = true;
            int id = i + 100 * octave;
            if (kb[Keys[i]].wasPressedThisFrame)
                synth.NoteOn(new NoteEvent
                {
                    freq = 440f * Mathf.Pow(2f, (i + (octave + 1) * 12 - 69) / 12f), wave = wave, gain = 1f, pan = 0.5f,
                    attack = SynthAdsr.attack, decay = SynthAdsr.decay, sustainLevel = SynthAdsr.sustain, release = SynthAdsr.release, sustain = 0f, id = id, held = true,
                });
            if (kb[Keys[i]].wasReleasedThisFrame) synth.NoteOff(id);
        }
    }
}

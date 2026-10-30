using UnityEngine;

// In-game synth panel (runtime IMGUI). Two cards: sound/scale selectors on the left, knobs on the right.
[RequireComponent(typeof(StepSequencer))]
public class SynthHud : MonoBehaviour
{
    public Vector2 position = new Vector2(10, 10);
    public float uiScale = 1.3f;

    static readonly Color Card = new Color(0.06f, 0.07f, 0.09f, 0.9f), Off = new Color(0.16f, 0.18f, 0.22f), Hover = new Color(0.23f, 0.26f, 0.31f),
        Accent = new Color(0.25f, 0.8f, 1f), Dim = new Color(0.2f, 0.23f, 0.28f), Body = new Color(0.12f, 0.14f, 0.17f),
        Text = new Color(0.9f, 0.93f, 0.97f), Muted = new Color(0.5f, 0.56f, 0.65f), OnText = new Color(0.03f, 0.07f, 0.1f);
    static readonly string[] Keys = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    static readonly string[] Scales = { "Major", "Minor", "Dorian", "Phryg", "Lydian", "Mixo", "Pent+", "Pent-", "Blues", "Chrom" };
    static readonly string[] Waves = { "Sine", "Tri", "Saw", "Square" };

    StepSequencer seq;
    KeyboardPlayer kb;
    PillarMusic music;
    GUIStyle label, value, section, button;
    float styleU, u, ox, oy;

    void Start() { seq = GetComponent<StepSequencer>(); kb = GetComponent<KeyboardPlayer>(); SelectInstrument(SynthPatch.index); }

    void SelectInstrument(int i)
    {
        int w = SynthPatch.Apply(i);
        if (seq) seq.wave = w;
        if (kb) kb.wave = w;
        if (music) music.wave = w;
    }

    void Styles()
    {
        if (label != null && Mathf.Approximately(styleU, u)) return;
        styleU = u;
        GUIStyle Make(int size, FontStyle fs, TextAnchor a, Color c)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(size * u), fontStyle = fs, alignment = a, clipping = TextClipping.Overflow, wordWrap = false };
            st.normal.textColor = c; return st;
        }
        label = Make(11, FontStyle.Normal, TextAnchor.MiddleCenter, Text);
        value = Make(10, FontStyle.Normal, TextAnchor.MiddleCenter, Muted);
        section = Make(10, FontStyle.Bold, TextAnchor.MiddleLeft, Muted);
        button = Make(11, FontStyle.Bold, TextAnchor.MiddleCenter, Text);
    }

    Rect Rc(float x, float y, float w, float h) => new Rect(ox + x * u, oy + y * u, w * u, h * u);

    static void RR(Rect r, Color c, float radius)
    {
        if (Event.current.type != EventType.Repaint) return;
        GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, c, Vector4.zero, new Vector4(radius, radius, radius, radius));
    }

    static void Dot(Vector2 c, float rad, Color col) { RR(new Rect(c.x - rad, c.y - rad, rad * 2, rad * 2), col, rad); }

    bool Btn(Rect r, string text, bool on)
    {
        if (Event.current.type == EventType.Repaint)
        {
            RR(r, on ? Accent : (r.Contains(Event.current.mousePosition) ? Hover : Off), 4 * u);
            button.normal.textColor = on ? OnText : Text;
            button.Draw(r, text, false, false, false, false);
        }
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    void Section(float x, float y, string text) { GUI.Label(Rc(x, y, 200, 14), text, section); }

    // Rotary knob: ring of LEDs around a dark body with a pointer dot. Drag up/down.
    float Knob(float x, float y, string name, float v, float min, float max, bool log = false)
    {
        var cell = Rc(x, y, 58, 74);
        int id = GUIUtility.GetControlID(FocusType.Passive);
        var e = Event.current;
        float t = log ? Mathf.InverseLerp(Mathf.Log(min), Mathf.Log(max), Mathf.Log(Mathf.Max(v, min))) : Mathf.InverseLerp(min, max, v);
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown: if (cell.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); } break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id)
                {
                    t = Mathf.Clamp01(t - e.delta.y * 0.006f);
                    v = log ? Mathf.Exp(Mathf.Lerp(Mathf.Log(min), Mathf.Log(max), t)) : Mathf.Lerp(min, max, t);
                    e.Use();
                }
                break;
            case EventType.MouseUp: if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); } break;
            case EventType.Repaint:
                var c = new Vector2(cell.center.x, cell.y + 28 * u);
                float R = 17 * u;
                const int N = 21;
                for (int i = 0; i < N; i++)
                {
                    float f = i / (N - 1f), a = Mathf.Lerp(-135f, 135f, f) * Mathf.Deg2Rad;
                    Dot(c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * (R + 6 * u), 1.9f * u, f <= t + 0.001f ? Accent : Dim);
                }
                Dot(c, R, Body);
                float pa = Mathf.Lerp(-135f, 135f, t) * Mathf.Deg2Rad;
                Dot(c + new Vector2(Mathf.Sin(pa), -Mathf.Cos(pa)) * (R - 6 * u), 2.4f * u, Color.white);
                GUI.Label(new Rect(cell.x, cell.y + 52 * u, cell.width, 12 * u), name, label);
                GUI.Label(new Rect(cell.x, cell.y + 63 * u, cell.width, 11 * u), v.ToString(v >= 100 ? "0" : "0.##"), value);
                break;
        }
        return v;
    }

    void OnGUI()
    {
        if (seq == null) return;
        u = uiScale; ox = position.x; oy = position.y;
        Styles();
        if (music == null) { music = FindAnyObjectByType<PillarMusic>(); if (music) music.wave = seq.wave; }

        // ---- left card: selectors
        RR(Rc(0, 0, 262, 362), Card, 8 * u);
        GUI.Label(Rc(12, 8, 100, 18), "SYNTH", new GUIStyle(section) { fontSize = Mathf.RoundToInt(13 * u), normal = { textColor = Text } });
        if (Btn(Rc(150, 7, 100, 22), seq.run ? "SEQ ON" : "SEQ OFF", seq.run)) seq.run = !seq.run;

        Section(12, 36, "WAVE");
        for (int i = 0; i < 4; i++)
            if (Btn(Rc(12 + i * 60, 52, 58, 24), Waves[i], seq.wave == i))
            { seq.wave = i; if (kb) kb.wave = i; if (music) music.wave = i; }

        if (music != null)
        {
            Section(12, 84, "KEY");
            for (int i = 0; i < 12; i++)
                if (Btn(Rc(12 + (i % 6) * 40, 100 + (i / 6) * 24, 38, 22), Keys[i], (int)music.key == i)) music.key = (RootNote)i;

            Section(12, 152, "SCALE");
            for (int i = 0; i < Scales.Length; i++)
                if (Btn(Rc(12 + (i % 5) * 48, 168 + (i / 5) * 24, 46, 22), Scales[i], (int)music.scale == i)) music.scale = (ScaleType)i;

            Section(12, 220, "CHORD");
            string[] names = { "3rd", "5th", "7th", "9th", "Inv", "Bass" };
            for (int i = 0; i < 6; i++)
            {
                bool on = i == 0 ? music.third : i == 1 ? music.fifth : i == 2 ? music.seventh : i == 3 ? music.ninth : i == 4 ? music.randomInversions : music.bassNote;
                if (!Btn(Rc(12 + i * 40, 236, 38, 24), names[i], on)) continue;
                if (i == 0) music.third = !on; else if (i == 1) music.fifth = !on; else if (i == 2) music.seventh = !on;
                else if (i == 3) music.ninth = !on; else if (i == 4) music.randomInversions = !on; else music.bassNote = !on;
            }

            Section(12, 264, "PROGRESSION  " + (music.progression == ProgType.Off ? "" : music.CurrentChord));
            if (Btn(Rc(192, 262, 58, 16), "Hold x" + music.landingsPerChord, false))
                music.landingsPerChord = music.landingsPerChord >= 8 ? 1 : music.landingsPerChord * 2;
            string[] pn = { "Off", "Pop", "50s", "Jazz", "Min", "Epic" };
            for (int i = 0; i < 6; i++)
                if (Btn(Rc(12 + i * 40, 282, 38, 24), pn[i], (int)music.progression == i)) music.progression = (ProgType)i;
        }

        Section(12, 314, "INSTRUMENT");
        string[] inn = { "Clsc", "Pad", "Plck", "Bass", "Brs", "Strg" };
        for (int i = 0; i < inn.Length; i++)
            if (Btn(Rc(12 + i * 40, 330, 38, 24), inn[i], SynthPatch.index == i)) SelectInstrument(i);

        // ---- right card: knobs
        const float X = 272;
        RR(Rc(X, 0, 304, 362), Card, 8 * u);
        Section(X + 12, 8, "ENVELOPE");
        SynthAdsr.attack = Knob(X + 8, 22, "Attack", SynthAdsr.attack, 0.001f, 2f, true);
        SynthAdsr.decay = Knob(X + 66, 22, "Decay", SynthAdsr.decay, 0.01f, 2f, true);
        SynthAdsr.sustain = Knob(X + 124, 22, "Sustain", SynthAdsr.sustain, 0, 1);
        SynthAdsr.release = Knob(X + 182, 22, "Release", SynthAdsr.release, 0.01f, 4f, true);

        Section(X + 12, 100, "FILTER"); Section(X + 128, 100, "REVERB");
        LowPassEffect.Instance.cutoff = Knob(X + 8, 114, "LPF", LowPassEffect.Instance.cutoff, 100, 12000, true);
        HighPassEffect.Instance.cutoff = Knob(X + 66, 114, "HPF", HighPassEffect.Instance.cutoff, 20, 4000, true);
        ReverbEffect.Instance.mix = Knob(X + 124, 114, "Mix", ReverbEffect.Instance.mix, 0, 1);
        ReverbEffect.Instance.size = Knob(X + 182, 114, "Size", ReverbEffect.Instance.size, 0, 1);
        ReverbEffect.Instance.damp = Knob(X + 240, 114, "Damp", ReverbEffect.Instance.damp, 0, 1);

        Section(X + 12, 192, "DELAY"); Section(X + 186, 192, "SEQUENCER");
        DelayEffect.Instance.time = Knob(X + 8, 206, "Time", DelayEffect.Instance.time, 0.05f, 1.5f);
        DelayEffect.Instance.feedback = Knob(X + 66, 206, "Feedback", DelayEffect.Instance.feedback, 0, 0.9f);
        DelayEffect.Instance.mix = Knob(X + 124, 206, "Mix", DelayEffect.Instance.mix, 0, 1);
        seq.bpm = Knob(X + 182, 206, "BPM", seq.bpm, 40, 220);

        Section(X + 12, 314, "CHORUS");
        var arp = ArpEngine.Instance;
        if (arp != null && Btn(Rc(X + 196, 312, 96, 18), (arp.enabledArp ? "ARP ON " : "ARP OFF ") + arp.Count, arp.enabledArp)) arp.enabledArp = !arp.enabledArp;
        string[] cn = { "Off", "I", "II", "I+II" };
        for (int i = 0; i < cn.Length; i++)
            if (Btn(Rc(X + 12 + i * 60, 330, 58, 24), cn[i], SynthPatch.chorus == i)) SynthPatch.chorus = i;
    }
}

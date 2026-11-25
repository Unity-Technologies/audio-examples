using UnityEditor;
using UnityEngine;

public class SynthWindow : EditorWindow
{
    StepSequencer seq;
    KeyboardPlayer kb;

    [MenuItem("Window/Synth")]
    static void Open() { GetWindow<SynthWindow>("Synth"); }

    void OnEnable() { EditorApplication.update += Repaint; }
    void OnDisable() { EditorApplication.update -= Repaint; }

    void OnGUI()
    {
        if (!Application.isPlaying) { EditorGUILayout.HelpBox("Enter Play mode in the Synth scene.", MessageType.Info); return; }
        if (seq == null) seq = FindAnyObjectByType<StepSequencer>();
        if (kb == null) kb = FindAnyObjectByType<KeyboardPlayer>();
        if (seq == null) { EditorGUILayout.HelpBox("No StepSequencer in scene.", MessageType.Warning); return; }

        seq.run = GUILayout.Toggle(seq.run, seq.run ? "Sequencer: running" : "Sequencer: stopped", "Button");
        int w = GUILayout.Toolbar(seq.wave, new[] { "Sine", "Tri", "Saw", "Square" });
        if (w != seq.wave) { seq.wave = w; if (kb) kb.wave = w; }

        GUILayout.Label("Filter / Reverb / Delay", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        LowPassEffect.Instance.cutoff = Knob("LPF", LowPassEffect.Instance.cutoff, 100, 12000, true);
        HighPassEffect.Instance.cutoff = Knob("HPF", HighPassEffect.Instance.cutoff, 20, 4000, true);
        ReverbEffect.Instance.mix = Knob("Rev Mix", ReverbEffect.Instance.mix, 0, 1);
        ReverbEffect.Instance.size = Knob("Rev Size", ReverbEffect.Instance.size, 0, 1);
        ReverbEffect.Instance.damp = Knob("Rev Damp", ReverbEffect.Instance.damp, 0, 1);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        DelayEffect.Instance.time = Knob("Dly Time", DelayEffect.Instance.time, 0.05f, 1.5f);
        DelayEffect.Instance.feedback = Knob("Dly Fb", DelayEffect.Instance.feedback, 0, 0.9f);
        DelayEffect.Instance.mix = Knob("Dly Mix", DelayEffect.Instance.mix, 0, 1);
        seq.bpm = Knob("BPM", seq.bpm, 40, 220);
        GUILayout.EndHorizontal();

        Piano(GUILayoutUtility.GetRect(10, 80, GUILayout.ExpandWidth(true)));
        Scope(GUILayoutUtility.GetRect(10, 90, GUILayout.ExpandWidth(true)));
        Spectrum(GUILayoutUtility.GetRect(10, 90, GUILayout.ExpandWidth(true)));
    }

    static float Knob(string label, float v, float min, float max, bool log = false)
    {
        var r = GUILayoutUtility.GetRect(60, 76, GUILayout.Width(60));
        int id = GUIUtility.GetControlID(FocusType.Passive);
        var e = Event.current;
        float t = log ? Mathf.InverseLerp(Mathf.Log(min), Mathf.Log(max), Mathf.Log(Mathf.Max(v, min)))
                      : Mathf.InverseLerp(min, max, v);
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (r.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id)
                {
                    t = Mathf.Clamp01(t - e.delta.y * 0.006f);
                    v = log ? Mathf.Exp(Mathf.Lerp(Mathf.Log(min), Mathf.Log(max), t)) : Mathf.Lerp(min, max, t);
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                break;
            case EventType.Repaint:
                var c = new Vector3(r.center.x, r.y + 26, 0);
                Handles.color = new Color(0.15f, 0.15f, 0.15f); Handles.DrawSolidDisc(c, Vector3.forward, 21);
                Handles.color = new Color(0.3f, 0.8f, 1f);
                float a = Mathf.Lerp(-135f, 135f, t) * Mathf.Deg2Rad;
                Handles.DrawAAPolyLine(3f, c, c + new Vector3(Mathf.Sin(a), -Mathf.Cos(a), 0) * 18f);
                var st = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter };
                GUI.Label(new Rect(r.x, r.y + 50, r.width, 26), label + "\n" + v.ToString("0.##"), st);
                break;
        }
        return v;
    }

    static void Piano(Rect r)
    {
        if (Event.current.type != EventType.Repaint) return;
        float now = Time.realtimeSinceStartup, kw = r.width / 7f;
        int[] white = { 0, 2, 4, 5, 7, 9, 11 }, black = { 1, 3, -1, 6, 8, 10, -1 };
        for (int i = 0; i < 7; i++)
            Fill(new Rect(r.x + i * kw, r.y, kw - 1, r.height), Lit(white[i], now) ? new Color(0.3f, 0.8f, 1f) : Color.white);
        for (int i = 0; i < 7; i++)
            if (black[i] >= 0)
                Fill(new Rect(r.x + (i + 1) * kw - kw * 0.3f, r.y, kw * 0.6f, r.height * 0.6f), Lit(black[i], now) ? new Color(0.1f, 0.6f, 0.9f) : Color.black);
    }

    static bool Lit(int pc, float now) => SynthScope.keysDown[pc] || (now >= SynthScope.seqFrom[pc] && now < SynthScope.seqUntil[pc]);

    static void Fill(Rect r, Color c) { EditorGUI.DrawRect(r, c); }

    static void Scope(Rect r)
    {
        if (Event.current.type != EventType.Repaint) return;
        EditorGUI.DrawRect(r, new Color(0.08f, 0.08f, 0.08f));
        int n = 1024, end = SynthScope.pos - 1, start = end - 2048;
        // find a rising zero crossing so the trace holds still
        for (int i = start; i < end - n; i++)
            if (Sample(i) <= 0 && Sample(i + 1) > 0) { start = i; break; }
        var pts = new Vector3[n];
        for (int i = 0; i < n; i++)
            pts[i] = new Vector3(r.x + i * r.width / n, r.center.y - Mathf.Clamp(Sample(start + i) * 2f, -1f, 1f) * r.height * 0.45f, 0);
        Handles.color = new Color(0.3f, 1f, 0.5f); Handles.DrawAAPolyLine(2f, pts);
    }

    static float Sample(int i) => SynthScope.buf[i & (SynthScope.Size - 1)];

    static void Spectrum(Rect r)
    {
        if (Event.current.type != EventType.Repaint) return;
        EditorGUI.DrawRect(r, new Color(0.08f, 0.08f, 0.08f));
        const int N = 512, bars = 48;
        int end = SynthScope.pos - 1;
        float bw = r.width / bars;
        for (int b = 0; b < bars; b++)
        {
            int k = Mathf.RoundToInt(Mathf.Exp(Mathf.Lerp(0f, Mathf.Log(255f), b / (bars - 1f))));
            float re = 0, im = 0;
            for (int i = 0; i < N; i++)
            {
                float x = Sample(end - N + i) * (0.5f - 0.5f * Mathf.Cos(2 * Mathf.PI * i / N));
                float ang = 2 * Mathf.PI * k * i / N;
                re += x * Mathf.Cos(ang); im -= x * Mathf.Sin(ang);
            }
            float db = 20f * Mathf.Log10(Mathf.Sqrt(re * re + im * im) / N * 4f + 1e-6f);
            float h = Mathf.Clamp01((db + 80f) / 80f) * r.height;
            EditorGUI.DrawRect(new Rect(r.x + b * bw, r.yMax - h, bw - 1, h), new Color(1f, 0.6f, 0.2f));
        }
    }
}

using System.Collections.Generic;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Manual seek playground. Drag onto a GameObject (auto-adds <see cref="ClipPlayerGenerator"/>),
/// assign a clip on that component, enter Play mode.
///
/// - Starts STOPPED. Use the Play/Stop button.
/// - Build up a queue of seeks: pick "when" (the clip second at which the seek fires) with the slider,
///   then click a number button to enqueue "when the clip reaches that second, jump to number N".
/// - You can enqueue while stopped; the queue is delivered to the generator when you press Play,
///   so several seeks end up scheduled on top of each other in the SampleProvider's own queue.
/// - The staged list stays on screen after Play: entries grey out once sent to the instance, and
///   dim further when playback ends. (The demo can't observe which seeks actually fire or are
///   dropped inside the provider, so it shows "sent" / "played", not per-seek firing.)
/// - Each seek is a SeekMessage(offset, when); `when` maps to sample-accurate scheduling.
///
/// Requires an AudioListener in the scene (Main Camera has one by default).
/// </summary>
[RequireComponent(typeof(ClipPlayerGenerator))]
public class SeekTester : MonoBehaviour
{
    struct StagedSeek
    {
        public int destSecond;   // where to jump to (seconds into the clip)
        public int whenSecond;   // clip position (second) at which the seek fires
        public bool immediate;   // if true, fire at the next process block (ignore whenSecond)
        public bool sent;        // delivered to the current generator instance this play session
    }

    ClipPlayerGenerator m_Generator;
    AudioSource m_Source;

    readonly List<StagedSeek> m_Queued = new List<StagedSeek>();
    int m_WhenSecond;            // slider value
    bool m_Immediate;           // send with immediate 'when'
    bool m_FlushRequested;      // deliver the queue once the generator instance is live

    void Start()
    {
        m_Generator = GetComponent<ClipPlayerGenerator>();

        // No explicit LoadAudioData() here: the sample provider backend honors the clip's own import
        // settings (load type, preload, load in background) and loads it as part of CreateInstance.
        m_Source = GetComponent<AudioSource>();
        if (m_Source == null)
            m_Source = gameObject.AddComponent<AudioSource>();

        m_Source.playOnAwake = false;
        m_Source.spatialBlend = 0f;          // 2D so it's audible without positioning
        m_Source.generator = m_Generator;    // play the clip through the SampleProvider generator
        // Intentionally NOT playing here — starts stopped.
    }

    bool InstanceLive()
        => m_Source.isPlaying && ControlContext.builtIn.Exists(m_Source.generatorInstance);

    void Update()
    {
        // The generator instance only exists a frame or so after Play(), so deliver the queue here.
        if (m_FlushRequested && InstanceLive())
        {
            FlushQueue();
            m_FlushRequested = false;
        }
    }

    void FlushQueue()
    {
        var instance = m_Source.generatorInstance;

        // Seeks are sent in whatever order they were staged; scheduling order is up to the caller.
        // The sample provider fires each seek when playback reaches its 'when', and drops any seek
        // whose 'when' has already been passed. Sent entries are kept (and greyed in the UI) rather
        // than cleared, so the list stays visible; each is delivered once per play session.
        for (int i = 0; i < m_Queued.Count; i++)
        {
            var s = m_Queued[i];
            if (s.sent)
                continue;

            var offset = new DiscreteTime(s.destSecond);   // DiscreteTime(int) is seconds
            var msg = s.immediate
                ? new SeekMessage(offset)                              // when omitted = immediate
                : new SeekMessage(offset, new DiscreteTime(s.whenSecond));

            var response = ControlContext.builtIn.SendMessage(instance, ref msg);
            Debug.Log($"Sent seek: hear {s.destSecond + 1} " +
                      $"{(s.immediate ? "(immediate)" : $"when clip reaches {s.whenSecond}s")} -> {response}");

            s.sent = true;
            m_Queued[i] = s;
        }
    }

    void OnGUI()
    {
        var clip = m_Generator != null ? m_Generator.clip : null;
        if (clip == null)
        {
            GUI.Label(new Rect(10, 10, 460, 20), "Assign a clip on the ClipPlayerGenerator component.");
            return;
        }

        int maxSec = Mathf.Max(1, Mathf.CeilToInt(clip.length));

        GUILayout.BeginArea(new Rect(10, 10, 340, 620), GUI.skin.box);

        GUILayout.Label($"Clip: {clip.name}  ({clip.length:0.0}s @ {clip.frequency} Hz)");
        GUILayout.Label($"Playing: {m_Source.isPlaying}");

        // --- Transport ---
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(m_Source.isPlaying ? "Stop" : "Play"))
        {
            if (m_Source.isPlaying)
            {
                m_Source.Stop();
            }
            else
            {
                // Re-arm the whole list for the new instance so it re-sends and re-animates each Play.
                for (int i = 0; i < m_Queued.Count; i++)
                {
                    var s = m_Queued[i];
                    s.sent = false;
                    m_Queued[i] = s;
                }

                m_Source.Play();
                m_FlushRequested = true;   // deliver whatever is queued once it's live
            }
        }
        if (GUILayout.Button("Send queue now"))
            m_FlushRequested = true;
        GUILayout.EndHorizontal();

        GUILayout.Space(8);

        // --- "when" control ---
        m_Immediate = GUILayout.Toggle(m_Immediate, "Fire immediately (ignore 'when')");
        GUI.enabled = !m_Immediate;
        GUILayout.Label($"Fire when clip reaches second: {m_WhenSecond}");
        m_WhenSecond = Mathf.RoundToInt(GUILayout.HorizontalSlider(m_WhenSecond, 0, maxSec));  // stepped 0..N
        GUI.enabled = true;

        GUILayout.Space(8);

        // --- destination buttons (1-indexed to match the spoken numbers) ---
        GUILayout.Label("Enqueue: seek so I hear number");
        const int cols = 5;
        for (int i = 0; i < maxSec; i++)
        {
            if (i % cols == 0) GUILayout.BeginHorizontal();

            int number = i + 1;      // button label
            int destSecond = i;      // number N is spoken at second (N-1)

            if (GUILayout.Button(number.ToString()))
            {
                m_Queued.Add(new StagedSeek { destSecond = destSecond, whenSecond = m_WhenSecond, immediate = m_Immediate });
                if (InstanceLive())
                    m_FlushRequested = true;   // playing already — schedule it now (still honors 'when')
            }

            if (i % cols == cols - 1 || i == maxSec - 1) GUILayout.EndHorizontal();
        }

        GUILayout.Space(8);

        // --- queued list (kept visible; greyed once sent, dimmer once playback ends) ---
        GUILayout.Label($"Queued ({m_Queued.Count}):");
        var prevContentColor = GUI.contentColor;
        foreach (var s in m_Queued)
        {
            if (!s.sent)
                GUI.contentColor = Color.white;                      // staged, not yet sent
            else if (m_Source.isPlaying)
                GUI.contentColor = new Color(0.60f, 0.60f, 0.60f);   // sent to the playing instance
            else
                GUI.contentColor = new Color(0.40f, 0.40f, 0.40f);   // playback finished / stopped

            string state = !s.sent ? "" : (m_Source.isPlaying ? "  - sent" : "  - played");
            GUILayout.Label($"   hear {s.destSecond + 1}  {(s.immediate ? "(immediate)" : $"@ clip {s.whenSecond}s")}{state}");
        }
        GUI.contentColor = prevContentColor;

        if (m_Queued.Count > 0 && GUILayout.Button("Clear queue"))
            m_Queued.Clear();

        GUILayout.EndArea();
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace RadioEffectRack
{
    /// <summary>
    /// Transport and effect rack. Drives the AudioSource, lists the effect chain in the order the audio system sees it,
    /// edits each effect, and rebuilds the rack during playback.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EffectRack : MonoBehaviour
    {
        /// <summary>What an effect's header row was asked to do this frame.</summary>
        enum EffectAction
        {
            None,
            Remove,
            MoveUp,
            MoveDown,
        }

        /// <summary>A named rack, built by adding effects in the order they should run.</summary>
        readonly struct Preset
        {
            public string label { get; }
            public Action<GameObject> build { get; }

            public Preset(string label, Action<GameObject> build)
            {
                this.label = label;
                this.build = build;
            }
        }

        static readonly Type[] k_EffectTypes =
        {
            typeof(BandPassEffect),
            typeof(DriveEffect),
            typeof(NoiseGateEffect),
            typeof(HissEffect),
            typeof(SpectrumEffect),
        };

        // Added in the order they run in, so a rack reads top to bottom like the chain it builds.
        static readonly Preset[] k_Presets =
        {
            new("Handheld radio", BuildHandheldRadio),
            new("Blown speaker", BuildBlownSpeaker),
            new("Spectrum only", BuildSpectrumOnly),
        };

        const float k_PanelWidth = 440f;
        const float k_PanelMargin = 4f;

        // Three across. A fourth starts a new row rather than squeezing the labels.
        const int k_ButtonsPerRow = 3;
        const float k_ButtonWidth = 128f;

        AudioSource m_Source;

        // Applied once the outgoing effects are actually gone.
        Action<GameObject> m_PendingRack;
        int m_PendingKeepCount;

        readonly List<Behaviour> m_Reordered = new();

        readonly List<Type> m_AddableTypes = new();
        readonly List<string> m_AddableLabels = new();

        readonly List<string> m_PresetLabels = new();

        // Reused, so the per-frame refresh allocates nothing.
        readonly List<Component> m_Components = new();
        readonly List<Behaviour> m_Effects = new();

        // Sampled once per frame, not from OnGUI: IMGUI needs the same controls in its layout and
        // repaint passes, and OnGUI runs several times per frame.
        bool m_HasEnvelope;
        float m_EnvelopeDb;
        bool m_GateIsOpen;

        SpectrumEffect m_Spectrum;
        bool m_HasSpectrum;

        Vector2 m_Scroll;

        void Start()
        {
            m_Source = GetComponent<AudioSource>();

            foreach (var preset in k_Presets)
            {
                m_PresetLabels.Add(preset.label);
            }

            // Clear rides along as the last button.
            m_PresetLabels.Add("Clear");
        }

        void Update()
        {
            RefreshEffects();

            m_HasEnvelope = false;

            foreach (var effect in m_Effects)
            {
                if (effect && effect is NoiseGateEffect gate
                    && gate.TryGetEnvelope(out m_EnvelopeDb, out m_GateIsOpen))
                {
                    m_HasEnvelope = true;

                    break;
                }
            }

            m_HasSpectrum = m_Spectrum && m_Spectrum.TryReadLevels();

            // Destroy is deferred to end of frame, AddComponent is immediate. Build only once the old
            // effects have gone, or the dying ones sit in the chain ahead of the new ones.
            if (m_PendingRack != null && m_Effects.Count == m_PendingKeepCount)
            {
                var build = m_PendingRack;
                m_PendingRack = null;

                build(gameObject);
                RefreshEffects();
            }
        }

        /// <summary>
        /// Rebuilds the chain from the components on this GameObject. Reading it every frame is what
        /// keeps the panel honest when an effect is added or removed, whether from here or the Inspector.
        /// </summary>
        void RefreshEffects()
        {
            GetComponents(m_Components);

            m_Effects.Clear();
            m_Spectrum = null;

            foreach (var component in m_Components)
            {
                // A destroyed component can still be listed. Unity's == operator is what reports that.
                if (!component)
                    continue;

                if (component is IAudioEffect and Behaviour behaviour)
                    m_Effects.Add(behaviour);

                if (component is SpectrumEffect spectrum)
                    m_Spectrum = spectrum;
            }

            m_AddableTypes.Clear();
            m_AddableLabels.Clear();

            foreach (var type in k_EffectTypes)
            {
                if (HasEffect(type))
                    continue;

                m_AddableTypes.Add(type);
                m_AddableLabels.Add(Label(type));
            }
        }

        void OnGUI()
        {
            // The editor repaints the game view on its own schedule, so OnGUI can run after an effect was
            // destroyed and before Update rebuilt the list. Rebuild on Layout, reuse for Repaint.
            if (Event.current.type == EventType.Layout)
                RefreshEffects();

            // Fit the view rather than assume there is room, and centre what fits.
            var width = Mathf.Min(k_PanelWidth, Screen.width - 2f * k_PanelMargin);
            var height = Mathf.Max(0f, Screen.height - 2f * k_PanelMargin);

            var rect = new Rect(0.5f * (Screen.width - width), 0.5f * (Screen.height - height),
                width, height);

            GUILayout.BeginArea(rect, GUI.skin.box);

            // A full rack can be taller than the panel.
            m_Scroll = GUILayout.BeginScrollView(m_Scroll);

            var clip = m_Source.clip;
            GUILayout.Label(clip
                ? $"Clip: {clip.name}  ({clip.length:0.0}s @ {clip.frequency} Hz)"
                : "Assign a clip on the AudioSource.");

            GUILayout.BeginHorizontal();
            
            if (GUILayout.Button(m_Source.isPlaying ? "Stop" : "Play"))
            {
                if (m_Source.isPlaying)
                    m_Source.Stop();
                else
                    m_Source.Play();
            }

            m_Source.loop = GUILayout.Toggle(m_Source.loop, "Loop");
            GUILayout.EndHorizontal();

            m_Source.bypassEffects = GUILayout.Toggle(m_Source.bypassEffects, "Bypass Effects (hear the dry clip)");

            GUILayout.Space(6);

            var presetClicked = WrappedButtons(m_PresetLabels, k_ButtonWidth, k_ButtonsPerRow);

            if (presetClicked >= 0)
            {
                if (presetClicked < k_Presets.Length)
                    LoadPreset(k_Presets[presetClicked]);
                else
                    ClearRack();
            }

            GUILayout.Space(6);
            GUILayout.Label(m_Effects.Count > 0
                ? "Chain, in component order:"
                : "The rack is empty, so the clip plays dry. Load a preset or add an effect below.");

            Behaviour pendingRemoval = null;
            var pendingMove = -1;
            var pendingMoveDelta = 0;

            for (var index = 0; index < m_Effects.Count; index++)
            {
                var effect = m_Effects[index];

                if (!effect)
                    continue;

                GUILayout.BeginVertical(GUI.skin.box);

                switch (Header(index, m_Effects.Count, Label(effect.GetType()), effect))
                {
                    case EffectAction.Remove:
                        pendingRemoval = effect;
                        break;

                    case EffectAction.MoveUp:
                        pendingMove = index;
                        pendingMoveDelta = -1;
                        break;

                    case EffectAction.MoveDown:
                        pendingMove = index;
                        pendingMoveDelta = 1;
                        break;
                }

                switch (effect)
                {
                    case BandPassEffect bandPass:
                        GUILayout.Label($"Low cut: {bandPass.lowCutHz:0} Hz");
                        bandPass.lowCutHz = GUILayout.HorizontalSlider(bandPass.lowCutHz, 50f, 2000f);
                        GUILayout.Label($"High cut: {bandPass.highCutHz:0} Hz");
                        bandPass.highCutHz = GUILayout.HorizontalSlider(bandPass.highCutHz, 800f, 8000f);
                        break;

                    case DriveEffect drive:
                        GUILayout.Label($"Drive: {drive.drive:0.0}");
                        drive.drive = GUILayout.HorizontalSlider(drive.drive, 1f, 40f);
                        GUILayout.Label($"Output: {drive.outputDb:0.0} dB");
                        drive.outputDb = GUILayout.HorizontalSlider(drive.outputDb, -40f, 6f);
                        break;

                    case NoiseGateEffect gate:
                        GUILayout.Label($"Threshold: {gate.thresholdDb:0.0} dB");
                        gate.thresholdDb = GUILayout.HorizontalSlider(gate.thresholdDb, -60f, 0f);
                        GUILayout.Label($"Attack: {gate.attackMs:0.0} ms");
                        gate.attackMs = GUILayout.HorizontalSlider(gate.attackMs, 0.5f, 50f);
                        GUILayout.Label($"Release: {gate.releaseMs:0} ms");
                        gate.releaseMs = GUILayout.HorizontalSlider(gate.releaseMs, 10f, 500f);
                        DrawGateMeter();
                        break;

                    case HissEffect hiss:
                        GUILayout.Label($"Static: {hiss.levelDb:0.0} dB");
                        hiss.levelDb = GUILayout.HorizontalSlider(hiss.levelDb, -80f, -12f);
                        break;

                    case SpectrumEffect:
                        DrawSpectrum();
                        break;
                }

                GUILayout.EndVertical();
            }

            Type pendingAddition = null;

            GUILayout.Space(4);

            if (m_AddableLabels.Count > 0)
            {
                GUILayout.Label("Add an effect while it plays:");

                var addClicked = WrappedButtons(m_AddableLabels, k_ButtonWidth, k_ButtonsPerRow);

                if (addClicked >= 0)
                    pendingAddition = m_AddableTypes[addClicked];
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // After the layout, so the set of controls cannot change mid-frame. The audio source
            // rediscovers its effects when the components change.
            if (pendingRemoval)
            {
                // Forget it first: nothing drawn later this cycle should reach a component on its way out.
                m_Effects.Remove(pendingRemoval);

                if (pendingRemoval is SpectrumEffect)
                {
                    m_Spectrum = null;
                    m_HasSpectrum = false;
                }

                Destroy(pendingRemoval);
            }

            if (pendingMove >= 0)
                MoveEffect(pendingMove, pendingMoveDelta);

            if (pendingAddition != null)
                gameObject.AddComponent(pendingAddition);
        }

        /// <summary>Effect name, position in the chain, reorder buttons, bypass toggle, remove button.</summary>
        static EffectAction Header(int index, int count, string label, Behaviour effect)
        {
            var action = EffectAction.None;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{index + 1}. {label}");
            GUILayout.FlexibleSpace();

            var wasEnabled = GUI.enabled;

            GUI.enabled = wasEnabled && index > 0;
            if (GUILayout.Button("^", GUILayout.Width(24)))
                action = EffectAction.MoveUp;

            GUI.enabled = wasEnabled && index < count - 1;
            if (GUILayout.Button("v", GUILayout.Width(24)))
                action = EffectAction.MoveDown;

            GUI.enabled = wasEnabled;

            // Disabling bypasses and keeps the instance. Removing destroys it.
            effect.enabled = GUILayout.Toggle(effect.enabled, effect.enabled ? "on" : "bypassed",
                GUILayout.Width(78));

            if (GUILayout.Button("x", GUILayout.Width(22)))
                action = EffectAction.Remove;

            GUILayout.EndHorizontal();

            return action;
        }

        /// <summary>
        /// Shows the envelope the audio thread last reported, and whether the gate is open. The value
        /// comes back through a query message, so nothing here touches the audio thread directly.
        /// </summary>
        void DrawGateMeter()
        {
            GUILayout.Label(m_HasEnvelope
                ? $"Envelope: {m_EnvelopeDb:0.0} dB   gate {(m_GateIsOpen ? "OPEN" : "closed")}"
                : "Envelope: (not running)");

            var rect = GUILayoutUtility.GetRect(1f, 10f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none);

            if (!m_HasEnvelope)
                return;

            var normalized = Mathf.InverseLerp(NoiseGateEffect.k_SilenceDb, 0f, m_EnvelopeDb);
            var fill = new Rect(rect.x + 1f, rect.y + 1f, (rect.width - 2f) * normalized, rect.height - 2f);

            var previousColor = GUI.color;
            GUI.color = m_GateIsOpen ? Color.green : Color.grey;
            GUI.DrawTexture(fill, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        /// <summary>Draws the band levels as a histogram, lowest frequency on the left.</summary>
        void DrawSpectrum()
        {
            var bands = m_Spectrum ? SpectrumEffect.bandCount : 0;

            GUILayout.Label(m_HasSpectrum
                ? $"Spectrum, {SpectrumEffect.lowestHz:0} Hz to {SpectrumEffect.highestHz / 1000f:0.#} kHz:"
                : "Spectrum: (not running)");

            var rect = GUILayoutUtility.GetRect(1f, 58f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none);

            if (!m_HasSpectrum || bands <= 0)
                return;

            var inner = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f);
            var barWidth = inner.width / bands;

            var previousColor = GUI.color;
            GUI.color = new Color(0.45f, 0.85f, 0.5f);

            for (var band = 0; band < bands; band++)
            {
                var level = m_Spectrum.Level(band);
                var decibels = level > 0.00001f ? 20f * Mathf.Log10(level) : k_SpectrumFloorDb;
                var height = Mathf.InverseLerp(k_SpectrumFloorDb, k_SpectrumCeilingDb, decibels) * inner.height;

                if (height <= 0f)
                    continue;

                var bar = new Rect(inner.x + band * barWidth, inner.yMax - height,
                    Mathf.Max(barWidth - 1f, 1f), height);

                GUI.DrawTexture(bar, Texture2D.whiteTexture);
            }

            GUI.color = previousColor;
        }

        // Each band carries part of the energy, so the top of the scale sits below full scale.
        const float k_SpectrumFloorDb = -60f;
        const float k_SpectrumCeilingDb = -6f;

        /// <summary>
        /// Draws labels as buttons of one width, starting a new row every <paramref name="perRow"/>, so
        /// a long row is never squeezed until its text is clipped. Returns the index clicked, or -1.
        /// </summary>
        static int WrappedButtons(List<string> labels, float width, int perRow)
        {
            var clicked = -1;

            for (var index = 0; index < labels.Count; index++)
            {
                if (index % perRow == 0)
                    GUILayout.BeginHorizontal();

                if (GUILayout.Button(labels[index], GUILayout.Width(width)))
                    clicked = index;

                if (index % perRow == perRow - 1 || index == labels.Count - 1)
                    GUILayout.EndHorizontal();
            }

            return clicked;
        }

        bool HasEffect(Type type)
        {
            foreach (var effect in m_Effects)
            {
                if (effect.GetType() == type)
                    return true;
            }

            return false;
        }

        static string Label(Type type)
        {
            if (type == typeof(BandPassEffect))
                return "Band pass";
            if (type == typeof(DriveEffect))
                return "Drive";
            if (type == typeof(NoiseGateEffect))
                return "Noise gate";
            if (type == typeof(HissEffect))
                return "Hiss";
            if (type == typeof(SpectrumEffect))
                return "Spectrum";

            return type.Name;
        }

        /// <summary>Clears the rack, then rebuilds it from the preset once the old effects are gone.</summary>
        void LoadPreset(Preset preset)
        {
            ClearRack();

            m_PendingRack = preset.build;
            m_PendingKeepCount = 0;
        }

        /// <summary>
        /// Removes every effect. Destroying a component destroys its effect instance, so the whole
        /// chain and all of its state goes with it.
        /// </summary>
        void ClearRack()
        {
            foreach (var effect in m_Effects)
            {
                if (effect)
                    Destroy(effect);
            }

            m_Effects.Clear();
            m_Spectrum = null;
            m_HasSpectrum = false;
            m_HasEnvelope = false;
            m_PendingRack = null;
            m_PendingKeepCount = 0;
        }

        /// <summary>
        /// Moves an effect one place along the chain.
        ///
        /// Chain order is component order, and at runtime a component can only be appended: the
        /// reorder API is editor-only. So everything from the swap onwards is destroyed and added
        /// again in the new order, carrying its current settings across. Effects before the swap are
        /// left alone and keep their instances, and their filter state with them.
        /// </summary>
        void MoveEffect(int index, int delta)
        {
            var target = index + delta;

            if (index < 0 || index >= m_Effects.Count || target < 0 || target >= m_Effects.Count)
                return;

            var first = Mathf.Min(index, target);

            // Reorder the tail before destroying anything. Either direction swaps the first two.
            m_Reordered.Clear();

            for (var i = first; i < m_Effects.Count; i++)
            {
                m_Reordered.Add(m_Effects[i]);
            }

            (m_Reordered[0], m_Reordered[1]) = (m_Reordered[1], m_Reordered[0]);

            // Capture settings while the components exist. Allocating on a click is fine; this never runs
            // while audio is being processed.
            var builders = new List<Action<GameObject>>(m_Reordered.Count);

            foreach (var effect in m_Reordered)
            {
                var build = Capture(effect);

                // Destroying an effect Capture cannot rebuild would drop it from the chain.
                if (build == null)
                    return;

                builders.Add(build);
            }

            for (var i = first; i < m_Effects.Count; i++)
            {
                if (m_Effects[i])
                    Destroy(m_Effects[i]);
            }

            // Forget the tail now, rebuild once the destroys land.
            m_Effects.RemoveRange(first, m_Effects.Count - first);

            if (m_Spectrum && !m_Effects.Contains(m_Spectrum))
            {
                m_Spectrum = null;
                m_HasSpectrum = false;
            }

            m_PendingRack = host =>
            {
                foreach (var build in builders)
                {
                    build(host);
                }
            };

            m_PendingKeepCount = first;
        }

        /// <summary>
        /// Returns a step that adds one effect of the same type with the settings it has right now.
        /// This is what lets an effect survive being destroyed and added again in a new position.
        /// </summary>
        static Action<GameObject> Capture(Behaviour effect)
        {
            switch (effect)
            {
                case BandPassEffect band:
                {
                    var lowCutHz = band.lowCutHz;
                    var highCutHz = band.highCutHz;

                    return host =>
                    {
                        var added = host.AddComponent<BandPassEffect>();
                        added.lowCutHz = lowCutHz;
                        added.highCutHz = highCutHz;
                    };
                }

                case DriveEffect drive:
                {
                    var amount = drive.drive;
                    var outputDb = drive.outputDb;

                    return host =>
                    {
                        var added = host.AddComponent<DriveEffect>();
                        added.drive = amount;
                        added.outputDb = outputDb;
                    };
                }

                case NoiseGateEffect gate:
                {
                    var thresholdDb = gate.thresholdDb;
                    var attackMs = gate.attackMs;
                    var releaseMs = gate.releaseMs;

                    return host =>
                    {
                        var added = host.AddComponent<NoiseGateEffect>();
                        added.thresholdDb = thresholdDb;
                        added.attackMs = attackMs;
                        added.releaseMs = releaseMs;
                    };
                }

                case HissEffect hiss:
                {
                    var levelDb = hiss.levelDb;

                    return host =>
                    {
                        var added = host.AddComponent<HissEffect>();
                        added.levelDb = levelDb;
                    };
                }

                case SpectrumEffect:
                    return host => host.AddComponent<SpectrumEffect>();

                default:
                    return null;
            }
        }

        // Each effect is configured as it is added: the audio source reads those values when it
        // discovers the component.

        static void BuildHandheldRadio(GameObject host)
        {
            var band = host.AddComponent<BandPassEffect>();
            band.lowCutHz = 400f;
            band.highCutHz = 2600f;

            var drive = host.AddComponent<DriveEffect>();
            drive.drive = 9f;
            drive.outputDb = -3f;

            var gate = host.AddComponent<NoiseGateEffect>();
            gate.thresholdDb = -34f;
            gate.attackMs = 3f;
            gate.releaseMs = 140f;

            var hiss = host.AddComponent<HissEffect>();
            hiss.levelDb = -44f;

            host.AddComponent<SpectrumEffect>();
        }

        // Two drives, band pass between them, no gate. The duplicate is deliberate: an effect is
        // addressed by its component, not its type.
        static void BuildBlownSpeaker(GameObject host)
        {
            var first = host.AddComponent<DriveEffect>();
            first.drive = 14f;
            first.outputDb = -4f;

            var band = host.AddComponent<BandPassEffect>();
            band.lowCutHz = 700f;
            band.highCutHz = 1800f;

            var second = host.AddComponent<DriveEffect>();
            second.drive = 24f;
            second.outputDb = -10f;

            var hiss = host.AddComponent<HissEffect>();
            hiss.levelDb = -34f;

            host.AddComponent<SpectrumEffect>();
        }

        // Changes no audio. A way to watch the untouched signal.
        static void BuildSpectrumOnly(GameObject host)
        {
            host.AddComponent<SpectrumEffect>();
        }
    }
}

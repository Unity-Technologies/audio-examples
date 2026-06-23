using System.Collections.Generic;
using RandomContainer;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(fileName = "Random Container", menuName = "Audio/Generators/Random Container", order = 1)]
public class RandomContainerAsset : ScriptableObject, IAudioGenerator
{
    public bool isFinite => false;
    public bool isRealtime => false;
    public DiscreteTime? length => null;

    // IAudioGenerator is an interface, which Unity can't serialize directly. The Serializable wrapper
    // holds the polymorphic reference and exposes it via .definition for the runtime side.
    [SerializeField] public IAudioGenerator.Serializable[] children;
    [SerializeField] public Mode mode = Preset.Default.mode;
    [SerializeField, Range(1, 10000)] public int maxPlayCount = Preset.Default.maxPlayCount;
    [SerializeField, Range(0.1f, 10.0f)] public float interval = Preset.Default.interval;
    [SerializeField, Range(0.0f, 24.0f)] public float randomizeLevel = Preset.Default.randomizeLevel;

    public GeneratorInstance CreateInstance(
        ControlContext context,
        AudioFormat? nestedFormat,
        ProcessorInstance.CreationParameters _)
    {
        var generators = new List<IAudioGenerator>();

        foreach (var child in children) { generators.Add(child.definition); }

        // UpdateAlways (not the default UpdateIfDataIsAvailable) because the scheduler advances on
        // every frame regardless of incoming pipe data.
        var creationParameters = new ProcessorInstance.CreationParameters
        {
            controlUpdateSetting = ProcessorInstance.UpdateSetting.UpdateAlways,
            realtimeUpdateSetting = ProcessorInstance.UpdateSetting.UpdateAlways
        };

        Preset preset = new Preset
        {
            mode = mode,
            maxPlayCount = maxPlayCount,
            interval = interval,
            randomizeLevel = randomizeLevel
        };

        return context.AllocateGenerator(new RandomContainerRealtime(),
            new RandomContainerControl(generators.ToArray(), nestedFormat, preset), nestedFormat, creationParameters);
    }
}

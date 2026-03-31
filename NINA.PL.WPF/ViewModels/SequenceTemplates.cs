using NINA.PL.Sequencer;

namespace NINA.PL.WPF.ViewModels;

/// <summary>Palette entry for sequence instructions.</summary>
public sealed class InstructionTemplate
{
    public required string Name { get; init; }

    public required string Icon { get; init; }

    public required string Category { get; init; }

    public required Func<ISequenceItem> Factory { get; init; }
}

/// <summary>Palette entry for conditions or triggers (set exactly one factory).</summary>
public sealed class ConditionTemplate
{
    public required string Name { get; init; }

    public required string Icon { get; init; }

    public required string Category { get; init; }

    public Func<ISequenceCondition>? ConditionFactory { get; init; }

    public Func<ISequenceTrigger>? TriggerFactory { get; init; }
}

public sealed class ContainerTemplate
{
    public required string Name { get; init; }

    public required string Icon { get; init; }

    public required SequenceNodeType NodeType { get; init; }
}

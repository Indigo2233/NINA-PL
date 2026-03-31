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

public sealed class UserTemplate
{
    public required string Name { get; set; }

    public string Description { get; set; } = "";

    public required SequenceNodeViewModel Node { get; init; }
}

public sealed class SavedTarget
{
    public required string Name { get; set; }

    public double RA { get; set; }

    public double Dec { get; set; }

    public double PositionAngle { get; set; }

    public SequenceNodeViewModel? Node { get; init; }
}

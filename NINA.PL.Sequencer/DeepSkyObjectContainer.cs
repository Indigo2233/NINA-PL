namespace NINA.PL.Sequencer;

/// <summary>
/// Container for a deep-sky target: sets <see cref="SequenceContext"/> target fields, then runs children like a <see cref="SequenceContainer"/>.
/// </summary>
public sealed class DeepSkyObjectContainer : ISequenceItem
{
    public string Name { get; set; } = nameof(DeepSkyObjectContainer);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "General";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public string TargetName { get; set; } = string.Empty;

    /// <summary>Right ascension in hours.</summary>
    public double RA { get; set; }

    /// <summary>Declination in degrees.</summary>
    public double Dec { get; set; }

    /// <summary>Position angle in degrees.</summary>
    public double PositionAngle { get; set; }

    public int Iterations { get; set; } = 1;

    public List<ISequenceItem> Items { get; } = new();

    public List<ISequenceCondition> Conditions { get; } = new();

    public List<ISequenceTrigger> Triggers { get; } = new();

    public TimeSpan GetEstimatedDuration()
    {
        TimeSpan sum = TimeSpan.Zero;
        foreach (ISequenceItem item in Items)
            sum += item.GetEstimatedDuration();
        int passes = Conditions.Count == 0 ? Math.Max(1, Iterations) : 1;
        return TimeSpan.FromTicks(sum.Ticks * passes);
    }

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        context.CurrentTargetName = TargetName;
        context.CurrentTargetRAHours = RA;
        context.CurrentTargetDecDegrees = Dec;
        context.CurrentPositionAngleDegrees = PositionAngle;

        var inner = new SequenceContainer
        {
            Name = Name,
            Description = Description,
            Category = Category,
            ErrorBehavior = ErrorBehavior,
            Attempts = Attempts,
            IsEnabled = IsEnabled,
            Iterations = Iterations,
        };
        foreach (ISequenceItem i in Items)
            inner.Items.Add(i);
        foreach (ISequenceCondition c in Conditions)
            inner.Conditions.Add(c);
        foreach (ISequenceTrigger t in Triggers)
            inner.Triggers.Add(t);

        return inner.ExecuteAsync(context, ct);
    }
}

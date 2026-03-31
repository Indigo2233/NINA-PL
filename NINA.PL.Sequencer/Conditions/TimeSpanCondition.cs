namespace NINA.PL.Sequencer.Conditions;

/// <summary>Repeats the loop while elapsed time since first check is under <see cref="MaxSeconds"/>.</summary>
public sealed class TimeSpanCondition : ISequenceCondition
{
    public string Name { get; set; } = nameof(TimeSpanCondition);

    public string Category { get; set; } = "Loop";

    public double MaxSeconds { get; set; } = 3600;

    private DateTime? _startedUtc;

    public bool Check(SequenceContext context)
    {
        if (_startedUtc is null)
            _startedUtc = DateTime.UtcNow;

        return (DateTime.UtcNow - _startedUtc.Value).TotalSeconds < MaxSeconds;
    }
}

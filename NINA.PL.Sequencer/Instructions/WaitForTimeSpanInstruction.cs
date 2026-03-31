namespace NINA.PL.Sequencer.Instructions;

public sealed class WaitForTimeSpanInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(WaitForTimeSpanInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Utility";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public double Hours { get; set; }

    public double Minutes { get; set; }

    public double Seconds { get; set; }

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        var total = TimeSpan.FromHours(Hours)
            + TimeSpan.FromMinutes(Minutes)
            + TimeSpan.FromSeconds(Seconds);
        if (total <= TimeSpan.Zero)
            return Task.CompletedTask;
        return Task.Delay(total, ct);
    }

    public TimeSpan GetEstimatedDuration()
    {
        var total = TimeSpan.FromHours(Hours)
            + TimeSpan.FromMinutes(Minutes)
            + TimeSpan.FromSeconds(Seconds);
        return total > TimeSpan.Zero ? total : TimeSpan.Zero;
    }
}

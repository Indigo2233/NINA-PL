using System.Globalization;
using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class WaitForTimeInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(WaitForTimeInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Utility";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Target instant in UTC (ISO 8601, e.g. 2026-03-31T22:00:00Z).</summary>
    public string TargetTimeUtc { get; set; } = string.Empty;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(TargetTimeUtc))
            throw new InvalidOperationException($"{nameof(TargetTimeUtc)} is required.");

        DateTimeOffset target = DateTimeOffset.Parse(TargetTimeUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        DateTime targetUtc = target.UtcDateTime;
        TimeSpan remaining = targetUtc - DateTime.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            Logger.Info("Waiting until UTC {0} (~{1}s).", targetUtc.ToString("o", CultureInfo.InvariantCulture), (int)remaining.TotalSeconds);
            await Task.Delay(remaining, ct).ConfigureAwait(false);
        }
    }

    public TimeSpan GetEstimatedDuration()
    {
        if (string.IsNullOrWhiteSpace(TargetTimeUtc))
            return TimeSpan.Zero;
        try
        {
            DateTimeOffset target = DateTimeOffset.Parse(TargetTimeUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            TimeSpan remaining = target.UtcDateTime - DateTime.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }
}

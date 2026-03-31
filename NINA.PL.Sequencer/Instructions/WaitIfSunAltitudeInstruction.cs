using NINA.PL.Core;
using NINA.PL.Sequencer;

namespace NINA.PL.Sequencer.Instructions;

public sealed class WaitIfSunAltitudeInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(WaitIfSunAltitudeInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Utility";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public double AltitudeThreshold { get; set; } = -18;

    public string Comparator { get; set; } = "Above";

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        bool waitWhileCondition(double alt)
        {
            return string.Equals(Comparator, "Below", StringComparison.OrdinalIgnoreCase)
                ? alt < AltitudeThreshold
                : alt >= AltitudeThreshold;
        }

        while (!ct.IsCancellationRequested)
        {
            double alt = AstronomyUtil.SunAltitude(DateTime.UtcNow, context.Latitude, context.Longitude);
            if (!waitWhileCondition(alt))
                return;

            Logger.Info("WaitIfSunAltitude: Sun alt={0:F2}° (threshold={1}, {2}), waiting 30s", alt, AltitudeThreshold, Comparator);
            await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        }
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromMinutes(5);
}

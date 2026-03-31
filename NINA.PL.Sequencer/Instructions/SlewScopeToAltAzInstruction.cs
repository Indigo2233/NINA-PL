using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class SlewScopeToAltAzInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(SlewScopeToAltAzInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Telescope";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public double Altitude { get; set; }

    public double Azimuth { get; set; }

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        IMountProvider mount = context.Mount.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected mount.");

        await mount.SlewToAltAzAsync(Altitude, Azimuth).ConfigureAwait(false);
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(30);
}

using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

/// <summary>Placeholder: logs connection status of configured mediators.</summary>
public sealed class ConnectEquipmentInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(ConnectEquipmentInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Utility";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Logger.Info(
            "Connect equipment (placeholder): Camera={0}, Mount={1}, Focuser={2}, FilterWheel={3}",
            context.Camera.IsConnected,
            context.Mount.IsConnected,
            context.Focuser.IsConnected,
            context.FilterWheel.IsConnected);
        if (context.FlatDevice is { } fd)
            Logger.Info("  Flat device connected: {0}", fd.IsConnected);
        if (context.SwitchHub is { } sw)
            Logger.Info("  Switch connected: {0}", sw.IsConnected);
        if (context.Rotator is { } rot)
            Logger.Info("  Rotator connected: {0}", rot.IsConnected);
        return Task.CompletedTask;
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(1);
}

using NINA.PL.Core;
using NINA.PL.Sequencer;

namespace NINA.PL.Sequencer.Instructions;

public sealed class TrainedFlatExposureInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(TrainedFlatExposureInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "FlatDevice";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public string? FilterName { get; set; }

    public double ExposureSeconds { get; set; } = 1.0;

    public int Gain { get; set; } = -1;

    public int Offset { get; set; } = -1;

    public int FlatCount { get; set; } = 10;

    /// <summary>When true, flat panel cover stays closed (light-box style); when false, cover opens for sky flats.</summary>
    public bool KeepClosed { get; set; }

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ICameraProvider camera = context.Camera.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected camera.");

        _ = context.FlatDevice ?? throw new InvalidOperationException("Flat device mediator not configured.");
        IFlatDeviceProvider flat = context.FlatDevice.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected flat panel.");

        if (!string.IsNullOrWhiteSpace(FilterName) && context.FilterWheel is not null)
        {
            IFilterWheelProvider? wheel = context.FilterWheel.GetConnectedProvider();
            if (wheel is not null)
            {
                int idx = wheel.FilterNames.FindIndex(n =>
                    string.Equals(n, FilterName, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    await wheel.SetPositionAsync(idx).ConfigureAwait(false);
            }
        }

        if (KeepClosed)
            await flat.CloseCoverAsync().ConfigureAwait(false);
        else
            await flat.OpenCoverAsync().ConfigureAwait(false);

        await flat.ToggleLightAsync(true).ConfigureAwait(false);

        double usec = ExposureSeconds * 1_000_000.0;

        int n = Math.Max(1, FlatCount);
        Logger.Info(
            "Trained flat: {0} frames, {1}s, Gain={2}, Offset={3}, Filter={4}, KeepClosed={5}",
            n, ExposureSeconds, Gain, Offset, FilterName ?? "—", KeepClosed);

        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (Gain >= 0)
                camera.SetGain(Gain);

            camera.SetExposure(usec);

            await camera.StartCaptureAsync().ConfigureAwait(false);
            try
            {
                int waitMs = (int)Math.Ceiling(ExposureSeconds * 1000) + 500;
                if (waitMs < 100)
                    waitMs = 100;
                await Task.Delay(waitMs, ct).ConfigureAwait(false);
                context.CompletedExposureCount++;
            }
            finally
            {
                await camera.StopCaptureAsync().ConfigureAwait(false);
            }
        }
    }

    public TimeSpan GetEstimatedDuration() =>
        TimeSpan.FromSeconds(Math.Max(0.1, (ExposureSeconds + 5.0) * Math.Max(1, FlatCount)));
}

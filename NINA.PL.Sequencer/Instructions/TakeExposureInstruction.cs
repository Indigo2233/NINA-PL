using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class TakeExposureInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(TakeExposureInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Imaging";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public double ExposureSeconds { get; set; } = 1.0;

    public int Gain { get; set; } = -1;

    public int Offset { get; set; } = -1;

    public int BinningX { get; set; } = 1;

    public int BinningY { get; set; } = 1;

    /// <summary>LIGHT, DARK, FLAT, BIAS, SNAPSHOT</summary>
    public string ImageType { get; set; } = "LIGHT";

    public string? FilterName { get; set; }

    public string FilePrefix { get; set; } = "image";

    public int TotalExposureCount { get; set; } = 1;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ICameraProvider camera = context.Camera.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected camera.");

        if (Gain >= 0)
            camera.SetGain(Gain);
        if (BinningX > 0 && BinningY > 0)
            camera.SetBinning(BinningX, BinningY);

        double usec = ExposureSeconds * 1_000_000.0;
        camera.SetExposure(usec);

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

        Logger.Info("Exposure: {0}s, Gain={1}, Offset={2}, Bin={3}x{4}, Type={5}, Filter={6}, prefix={7}",
            ExposureSeconds, Gain, Offset, BinningX, BinningY, ImageType, FilterName ?? "—", FilePrefix);

        for (int i = 0; i < Math.Max(1, TotalExposureCount); i++)
        {
            ct.ThrowIfCancellationRequested();
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
        TimeSpan.FromSeconds(Math.Max(0.1, (ExposureSeconds + 1.0) * Math.Max(1, TotalExposureCount)));
}

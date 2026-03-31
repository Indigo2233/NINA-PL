using NINA.PL.Core;
using NINA.PL.Sequencer;

namespace NINA.PL.Sequencer.Instructions;

public sealed class TakeSubframeExposureInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(TakeSubframeExposureInstruction);

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

    /// <summary>Percentage of full sensor width/height (centered ROI).</summary>
    public int SubframePercentage { get; set; } = 100;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ICameraProvider camera = context.Camera.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected camera.");

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

        int pct = Math.Clamp(SubframePercentage, 1, 100);
        int sw = Math.Max(1, camera.SensorWidth);
        int sh = Math.Max(1, camera.SensorHeight);
        int w = Math.Max(1, sw * pct / 100);
        int h = Math.Max(1, sh * pct / 100);
        int x = Math.Max(0, (sw - w) / 2);
        int y = Math.Max(0, (sh - h) / 2);

        camera.SetROI(x, y, w, h);

        try
        {
            if (Gain >= 0)
                camera.SetGain(Gain);
            if (BinningX > 0 && BinningY > 0)
                camera.SetBinning(BinningX, BinningY);

            double usec = ExposureSeconds * 1_000_000.0;
            camera.SetExposure(usec);

            Logger.Info(
                "Subframe exposure: ROI=({0},{1}) {2}x{3} ({4}% of {5}x{6}), {7}s, Gain={8}, Offset={9}, Type={10}, Filter={11}, prefix={12}",
                x, y, w, h, pct, sw, sh, ExposureSeconds, Gain, Offset, ImageType, FilterName ?? "—", FilePrefix);

            int total = Math.Max(1, TotalExposureCount);
            for (int i = 0; i < total; i++)
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
        finally
        {
            camera.ResetROI();
        }
    }

    public TimeSpan GetEstimatedDuration() =>
        TimeSpan.FromSeconds(Math.Max(0.1, (ExposureSeconds + 1.0) * Math.Max(1, TotalExposureCount)));
}

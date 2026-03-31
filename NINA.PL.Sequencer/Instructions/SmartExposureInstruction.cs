using NINA.PL.Core;
using NINA.PL.Sequencer;

namespace NINA.PL.Sequencer.Instructions;

public sealed class SmartExposureInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(SmartExposureInstruction);

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

    public string? FilterName { get; set; }

    public int ExposureCount { get; set; } = 10;

    /// <summary>When greater than zero, pulse-guide dither after every N completed exposures (not after the last).</summary>
    public int DitherAfterExposures { get; set; }

    public string FilePrefix { get; set; } = "image";

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

        double usec = ExposureSeconds * 1_000_000.0;

        Logger.Info(
            "SmartExposure: count={0}, {1}s, Gain={2}, Offset={3}, Bin={4}x{5}, Filter={6}, DitherEvery={7}, prefix={8}",
            ExposureCount, ExposureSeconds, Gain, Offset, BinningX, BinningY, FilterName ?? "—", DitherAfterExposures, FilePrefix);

        int n = Math.Max(1, ExposureCount);
        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (Gain >= 0)
                camera.SetGain(Gain);
            if (BinningX > 0 && BinningY > 0)
                camera.SetBinning(BinningX, BinningY);

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

            int completed = i + 1;
            if (DitherAfterExposures > 0
                && completed % DitherAfterExposures == 0
                && completed < n)
            {
                IMountProvider? mount = context.Mount.GetConnectedProvider();
                if (mount is null)
                {
                    Logger.Warn("SmartExposure: dither skipped (no mount).");
                }
                else
                {
                    const int ditherPulseMs = 500;
                    const int ditherSettleSeconds = 5;
                    var direction = (GuideDirection)Random.Shared.Next(0, Enum.GetValues<GuideDirection>().Length);
                    await mount.PulseGuideAsync(direction, ditherPulseMs).ConfigureAwait(false);
                    Logger.Info("SmartExposure: dither after {0} exposures, direction={1}", completed, direction);
                    if (ditherSettleSeconds > 0)
                        await Task.Delay(TimeSpan.FromSeconds(ditherSettleSeconds), ct).ConfigureAwait(false);
                }
            }
        }
    }

    public TimeSpan GetEstimatedDuration() =>
        TimeSpan.FromSeconds(Math.Max(0.1, (ExposureSeconds + 1.0) * Math.Max(1, ExposureCount)));
}

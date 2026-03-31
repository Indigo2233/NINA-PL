using System.Diagnostics;
using NINA.PL.Capture;
using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class CaptureVideoInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(CaptureVideoInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Imaging";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public int FrameLimit { get; set; }

    public double TimeLimitSeconds { get; set; }

    public CaptureFormat Format { get; set; } = CaptureFormat.Ser;

    public string FilePrefix { get; set; } = "capture";

    public int Gain { get; set; } = -1;

    public double ExposureUs { get; set; } = 10000;

    public int BinningX { get; set; } = 1;

    public int BinningY { get; set; } = 1;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ICameraProvider? camera = context.Camera.GetConnectedProvider();
        if (camera is not null)
        {
            if (Gain >= 0)
                camera.SetGain(Gain);
            if (ExposureUs > 0)
                camera.SetExposure(ExposureUs);
            if (BinningX > 0 && BinningY > 0)
                camera.SetBinning(BinningX, BinningY);
        }

        CaptureEngine cap = context.CaptureEngine;
        cap.FrameLimit = FrameLimit;
        cap.TimeLimitSeconds = TimeLimitSeconds;
        cap.Format = Format;
        cap.FilePrefix = FilePrefix;

        cap.StartCapture(context.Camera);

        try
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (!cap.IsCapturing)
                    break;

                if (FrameLimit > 0 && cap.FramesCaptured >= FrameLimit)
                    break;

                if (TimeLimitSeconds > 0 && sw.Elapsed.TotalSeconds >= TimeLimitSeconds)
                    break;

                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            cap.StopCapture();
        }
    }

    public TimeSpan GetEstimatedDuration()
    {
        if (TimeLimitSeconds > 0)
            return TimeSpan.FromSeconds(TimeLimitSeconds);
        if (FrameLimit > 0)
            return TimeSpan.FromSeconds(FrameLimit / 30.0);
        return TimeSpan.FromSeconds(60);
    }
}

using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class SetReadoutModeInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(SetReadoutModeInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Camera";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public string PixelFormat { get; set; } = "RAW16";

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ICameraProvider camera = context.Camera.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected camera.");

        camera.SetPixelFormat(PixelFormat);
        Logger.Info("Camera pixel format set to {0}", PixelFormat);
        return Task.CompletedTask;
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(5);
}

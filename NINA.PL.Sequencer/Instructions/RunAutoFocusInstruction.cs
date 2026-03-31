using NINA.PL.AutoFocus;
using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class RunAutoFocusInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(RunAutoFocusInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Equipment";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public int StepSize { get; set; } = 50;

    public int InitialOffsetSteps { get; set; } = 4;

    public int NumberOfFramesPerPoint { get; set; } = 1;

    public string? FilterName { get; set; }

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Logger.Info("AutoFocus: StepSize={0}, InitOffset={1}, FramesPerPt={2}, Filter={3}",
            StepSize, InitialOffsetSteps, NumberOfFramesPerPoint, FilterName ?? "—");

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

        IFocuserProvider? focuser = context.Focuser.GetConnectedProvider();
        ICameraProvider? camera = context.Camera.GetConnectedProvider();
        if (focuser is null || camera is null)
            throw new InvalidOperationException("Focuser and camera must be connected for autofocus.");

        await context.AutoFocusEngine
            .RunAutoFocusAsync(focuser, camera, ct)
            .ConfigureAwait(false);
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(45);
}

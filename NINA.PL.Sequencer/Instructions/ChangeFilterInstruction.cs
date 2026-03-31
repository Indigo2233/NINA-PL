using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class ChangeFilterInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(ChangeFilterInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "FilterWheel";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    /// <summary>1-based or 0-based depending on driver; passed to <see cref="IFilterWheelProvider.SetPositionAsync"/>.</summary>
    public int FilterPosition { get; set; }

    /// <summary>When set, resolved against <see cref="IFilterWheelProvider.FilterNames"/>; takes precedence over <see cref="FilterPosition"/>.</summary>
    public string? FilterName { get; set; }

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        IFilterWheelProvider? wheel = context.FilterWheel.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected filter wheel.");

        int position = FilterPosition;
        if (!string.IsNullOrWhiteSpace(FilterName))
        {
            int idx = wheel.FilterNames.FindIndex(n =>
                string.Equals(n, FilterName, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
                throw new InvalidOperationException($"Filter name not found: {FilterName}");

            position = idx;
        }

        await wheel.SetPositionAsync(position).ConfigureAwait(false);
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(15);
}

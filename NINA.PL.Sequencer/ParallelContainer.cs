using NINA.PL.Core;

namespace NINA.PL.Sequencer;

/// <summary>
/// Runs child items concurrently. All items start together and the container waits for all to complete.
/// </summary>
public sealed class ParallelContainer : ISequenceItem
{
    public string Name { get; set; } = nameof(ParallelContainer);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "General";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public List<ISequenceItem> Items { get; } = new();

    public TimeSpan GetEstimatedDuration()
    {
        TimeSpan max = TimeSpan.Zero;
        foreach (ISequenceItem item in Items)
        {
            TimeSpan d = item.GetEstimatedDuration();
            if (d > max)
                max = d;
        }

        return max;
    }

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var tasks = new List<Task>();
        foreach (ISequenceItem item in Items)
        {
            ct.ThrowIfCancellationRequested();
            if (!item.IsEnabled)
                continue;

            context.RaiseItemStarted(item);
            tasks.Add(RunItemAsync(item, context, ct));
        }

        if (tasks.Count > 0)
            await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task RunItemAsync(ISequenceItem item, SequenceContext context, CancellationToken ct)
    {
        try
        {
            await ExecuteItemWithRetriesAsync(item, context, ct).ConfigureAwait(false);
        }
        finally
        {
            context.RaiseItemCompleted(item);
        }
    }

    private static async Task ExecuteItemWithRetriesAsync(ISequenceItem item, SequenceContext context, CancellationToken ct)
    {
        int attempts = Math.Max(1, item.Attempts);
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await item.ExecuteAsync(context, ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt < attempts)
                {
                    Logger.Warn(ex, "Parallel sequence item {0} failed attempt {1}/{2}.", item.Name, attempt, attempts);
                    continue;
                }

                Logger.Error(ex, "Parallel sequence item {0} failed after {1} attempts.", item.Name, attempts);
                switch (item.ErrorBehavior)
                {
                    case InstructionErrorBehavior.AbortOnError:
                        throw;
                    case InstructionErrorBehavior.SkipInstruction:
                        Logger.Info("Skipping parallel instruction {0} after error (SkipInstruction).", item.Name);
                        return;
                    case InstructionErrorBehavior.ContinueOnError:
                        return;
                }
            }
        }
    }
}

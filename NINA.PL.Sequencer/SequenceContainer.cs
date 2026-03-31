using NINA.PL.Core;

namespace NINA.PL.Sequencer;

/// <summary>
/// Runs child items in order. With no conditions, runs one pass (or <see cref="Iterations"/> passes). With conditions, repeats the pass while all conditions hold.
/// Triggers are evaluated before each pass, before each item, after each item, and for <see cref="ISequenceTrigger.ShouldTriggerAfter"/> after each item.
/// </summary>
public sealed class SequenceContainer : ISequenceItem
{
    public string Name { get; set; } = nameof(SequenceContainer);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "General";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    /// <summary>When there are no <see cref="Conditions"/>, the inner pass runs this many times (minimum 1).</summary>
    public int Iterations { get; set; } = 1;

    public List<ISequenceItem> Items { get; } = new();

    public List<ISequenceCondition> Conditions { get; } = new();

    public List<ISequenceTrigger> Triggers { get; } = new();

    public TimeSpan GetEstimatedDuration()
    {
        TimeSpan sum = TimeSpan.Zero;
        foreach (ISequenceItem item in Items)
            sum += item.GetEstimatedDuration();
        int passes = Conditions.Count == 0 ? Math.Max(1, Iterations) : 1;
        return TimeSpan.FromTicks(sum.Ticks * passes);
    }

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        if (Conditions.Count == 0)
        {
            int passes = Math.Max(1, Iterations);
            for (int p = 0; p < passes; p++)
            {
                ct.ThrowIfCancellationRequested();
                await RunSinglePassAsync(context, ct).ConfigureAwait(false);
            }

            return;
        }

        int savedIteration = context.CurrentLoopIteration;
        context.CurrentLoopIteration = 0;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                foreach (ISequenceCondition condition in Conditions)
                {
                    if (!condition.Check(context))
                        return;
                }

                await RunSinglePassAsync(context, ct).ConfigureAwait(false);
                context.CurrentLoopIteration++;
            }
        }
        finally
        {
            context.CurrentLoopIteration = savedIteration;
        }
    }

    private async Task RunSinglePassAsync(SequenceContext context, CancellationToken ct)
    {
        await FireTriggersAsync(context, ct).ConfigureAwait(false);

        foreach (ISequenceItem item in Items)
        {
            ct.ThrowIfCancellationRequested();
            await FireTriggersAsync(context, ct).ConfigureAwait(false);

            if (!item.IsEnabled)
                continue;

            context.RaiseItemStarted(item);
            try
            {
                await ExecuteItemWithRetriesAsync(item, context, ct).ConfigureAwait(false);
            }
            finally
            {
                context.RaiseItemCompleted(item);
            }

            await FireTriggersAfterAsync(context, ct).ConfigureAwait(false);
            await FireTriggersAsync(context, ct).ConfigureAwait(false);
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
                    Logger.Warn(ex, "Sequence item {0} failed attempt {1}/{2}.", item.Name, attempt, attempts);
                    continue;
                }

                Logger.Error(ex, "Sequence item {0} failed after {1} attempts.", item.Name, attempts);
                switch (item.ErrorBehavior)
                {
                    case InstructionErrorBehavior.AbortOnError:
                        throw;
                    case InstructionErrorBehavior.SkipInstruction:
                        Logger.Info("Skipping instruction {0} after error (SkipInstruction).", item.Name);
                        return;
                    case InstructionErrorBehavior.ContinueOnError:
                        return;
                }
            }
        }
    }

    private async Task FireTriggersAsync(SequenceContext context, CancellationToken ct)
    {
        foreach (ISequenceTrigger trigger in Triggers)
        {
            ct.ThrowIfCancellationRequested();
            if (!trigger.ShouldTrigger(context))
                continue;

            await trigger.ExecuteAsync(context, ct).ConfigureAwait(false);
        }
    }

    private async Task FireTriggersAfterAsync(SequenceContext context, CancellationToken ct)
    {
        foreach (ISequenceTrigger trigger in Triggers)
        {
            ct.ThrowIfCancellationRequested();
            if (!trigger.ShouldTriggerAfter(context))
                continue;

            await trigger.ExecuteAsync(context, ct).ConfigureAwait(false);
        }
    }
}

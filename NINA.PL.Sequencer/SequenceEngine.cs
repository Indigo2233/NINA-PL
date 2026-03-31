namespace NINA.PL.Sequencer;

/// <summary>
/// Runs a <see cref="SequenceContainer"/> against a <see cref="SequenceContext"/> with cancellation and status events.
/// </summary>
public sealed class SequenceEngine : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _runCts;
    private bool _disposed;
    private bool _isRunning;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
                return _isRunning;
        }
    }

    public SequenceContainer? RootContainer { get; set; }

    public event EventHandler<ISequenceItem>? ItemStarted;
    public event EventHandler<ISequenceItem>? ItemCompleted;
    public event EventHandler? SequenceStarted;
    public event EventHandler? SequenceCompleted;
    public event EventHandler<string>? SequenceFailed;
    public event EventHandler<string>? StatusChanged;

    public async Task RunAsync(SequenceContext context, CancellationToken ct)
    {
        if (RootContainer is null)
            throw new InvalidOperationException("RootContainer must be set before RunAsync.");

        CancellationTokenSource linked;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_isRunning)
                throw new InvalidOperationException("Sequence is already running.");

            _runCts = new CancellationTokenSource();
            linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _runCts.Token);
            _isRunning = true;
        }

        void OnItemStarted(object? s, ISequenceItem item)
        {
            ItemStarted?.Invoke(this, item);
            StatusChanged?.Invoke(this, item.Name);
        }

        void OnItemCompleted(object? s, ISequenceItem item) =>
            ItemCompleted?.Invoke(this, item);

        context.ItemExecutionStarted += OnItemStarted;
        context.ItemExecutionCompleted += OnItemCompleted;

        try
        {
            context.SequenceStartTime = DateTime.UtcNow;
            SequenceStarted?.Invoke(this, EventArgs.Empty);
            StatusChanged?.Invoke(this, "Running");

            try
            {
                await RootContainer.ExecuteAsync(context, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, "Stopped");
            }
            catch (Exception ex)
            {
                SequenceFailed?.Invoke(this, ex.Message);
                StatusChanged?.Invoke(this, $"Failed: {ex.Message}");
            }
        }
        finally
        {
            context.ItemExecutionStarted -= OnItemStarted;
            context.ItemExecutionCompleted -= OnItemCompleted;

            linked.Dispose();

            lock (_sync)
            {
                _isRunning = false;
                _runCts?.Dispose();
                _runCts = null;
            }

            SequenceCompleted?.Invoke(this, EventArgs.Empty);
            StatusChanged?.Invoke(this, "Idle");
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _runCts?.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _runCts?.Cancel();
            _runCts?.Dispose();
            _runCts = null;
        }
    }
}

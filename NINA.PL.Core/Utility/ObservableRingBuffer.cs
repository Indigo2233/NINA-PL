using System;
using System.Collections.Generic;

namespace NINA.PL.Core;

/// <summary>
/// Fixed-capacity FIFO queue of <see cref="FrameData"/> references. When full, the oldest frame is dropped.
/// Thread-safe; raises <see cref="FrameEnqueued"/> after each successful enqueue.
/// </summary>
public sealed class ObservableRingBuffer
{
    private readonly object _sync = new();
    private readonly Queue<FrameData> _queue;
    private readonly int _capacity;

    public ObservableRingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);
        _capacity = capacity;
        _queue = new Queue<FrameData>(capacity);
    }

    /// <summary>Maximum number of frames retained.</summary>
    public int Capacity => _capacity;

    /// <summary>Current number of frames in the buffer.</summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>Raised after a frame is stored (including when an older frame was evicted).</summary>
    public event EventHandler<FrameData>? FrameEnqueued;

    /// <summary>
    /// Adds a frame. If the buffer is at capacity, removes the oldest frame first.
    /// </summary>
    public void Enqueue(FrameData frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_sync)
        {
            while (_queue.Count >= _capacity)
            {
                _queue.Dequeue();
            }

            _queue.Enqueue(frame);
        }

        RaiseFrameEnqueued(frame);
    }

    /// <summary>
    /// Attempts to remove and return the oldest frame.
    /// </summary>
    /// <returns><see langword="true"/> if a frame was returned.</returns>
    public bool TryDequeue(out FrameData? frame)
    {
        lock (_sync)
        {
            if (_queue.Count == 0)
            {
                frame = null;
                return false;
            }

            frame = _queue.Dequeue();
            return true;
        }
    }

    /// <summary>Removes all frames.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _queue.Clear();
        }
    }

    private void RaiseFrameEnqueued(FrameData frame)
    {
        var handler = FrameEnqueued;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler.Invoke(this, frame);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "ObservableRingBuffer subscriber threw from FrameEnqueued.");
        }
    }
}

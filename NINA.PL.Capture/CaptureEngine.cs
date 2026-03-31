using System.Collections.Concurrent;
using System.Diagnostics;
using NINA.PL.Core;
using NINA.PL.Image;

namespace NINA.PL.Capture;

/// <summary>
/// Orchestrates high-speed capture: camera frames are queued and written on a dedicated thread.
/// </summary>
public sealed class CaptureEngine
{
    private const int FpsWindow = 30;
    private const int MaxQueueDepth = 512;

    private readonly object _writerLock = new();
    private readonly ConcurrentQueue<FrameData> _queue = new();
    private readonly ManualResetEventSlim _hasWork = new(false);
    private readonly long[] _fpsTicks = new long[FpsWindow];
    private int _fpsSeq;

    private CameraMediator? _camera;
    private CancellationTokenSource? _cts;
    private Thread? _worker;

    private SERWriter? _serWriter;
    private AVIWriter? _aviWriter;
    private string? _fitsSessionDirectory;
    private DateTime _sessionTimestamp;
    private DateTime _sessionStartUtc;
    private bool _writersInitialized;
    private volatile bool _capturing;
    private volatile bool _paused;
    private long _framesCaptured;
    private long _framesDropped;
    private int _queuedDepth;

    public bool IsCapturing => _capturing;
    public int FramesCaptured => (int)Interlocked.Read(ref _framesCaptured);
    public int FramesDropped => (int)Interlocked.Read(ref _framesDropped);

    public int FrameLimit { get; set; }
    public double TimeLimitSeconds { get; set; }
    public CaptureFormat Format { get; set; } = CaptureFormat.Ser;
    public string OutputDirectory { get; set; } = Path.GetTempPath();
    public string FilePrefix { get; set; } = "capture";
    public string Observer { get; set; } = string.Empty;
    public string Instrument { get; set; } = string.Empty;
    public string Telescope { get; set; } = string.Empty;
    public double AviFps { get; set; } = 30.0;

    public event EventHandler<FrameData>? FrameCaptured;
    public event EventHandler? CaptureStarted;
    public event EventHandler? CaptureStopped;

    private double _currentFps;
    public double CurrentFps => Volatile.Read(ref _currentFps);

    public void StartCapture(CameraMediator camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (_capturing)
            throw new InvalidOperationException("Capture is already active.");

        Directory.CreateDirectory(OutputDirectory);

        _camera = camera;
        _sessionTimestamp = DateTime.Now;
        _sessionStartUtc = DateTime.UtcNow;
        _writersInitialized = false;
        _serWriter?.Dispose();
        _aviWriter?.Dispose();
        _serWriter = null;
        _aviWriter = null;
        _fitsSessionDirectory = null;

        while (_queue.TryDequeue(out _))
            Interlocked.Decrement(ref _queuedDepth);

        Interlocked.Exchange(ref _framesCaptured, 0);
        Interlocked.Exchange(ref _framesDropped, 0);
        Interlocked.Exchange(ref _queuedDepth, 0);
        _fpsSeq = 0;
        Volatile.Write(ref _currentFps, 0);

        _paused = false;
        _cts = new CancellationTokenSource();
        _capturing = true;

        _worker = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "NINA.PL.Capture.Writer",
        };
        _worker.Start(_cts.Token);

        _camera.FrameReceived += OnFrameReceived;
        CaptureStarted?.Invoke(this, EventArgs.Empty);
    }

    public void StopCapture()
    {
        if (!_capturing)
            return;

        if (_camera is not null)
        {
            _camera.FrameReceived -= OnFrameReceived;
            _camera = null;
        }

        _capturing = false;
        _paused = false;
        _cts?.Cancel();
        _hasWork.Set();

        _worker?.Join();
        _worker = null;

        lock (_writerLock)
        {
            _serWriter?.Dispose();
            _serWriter = null;
            _aviWriter?.Dispose();
            _aviWriter = null;
        }

        _cts?.Dispose();
        _cts = null;
        _writersInitialized = false;

        CaptureStopped?.Invoke(this, EventArgs.Empty);
    }

    public void PauseCapture() => _paused = true;

    public void ResumeCapture() => _paused = false;

    private void OnFrameReceived(object? sender, FrameData frame)
    {
        if (!_capturing || _paused)
            return;

        if (FrameLimit > 0 && Interlocked.Read(ref _framesCaptured) >= FrameLimit)
            return;

        if (TimeLimitSeconds > 0 && (DateTime.UtcNow - _sessionStartUtc).TotalSeconds >= TimeLimitSeconds)
            return;

        if (Interlocked.Increment(ref _queuedDepth) > MaxQueueDepth)
        {
            Interlocked.Decrement(ref _queuedDepth);
            Interlocked.Increment(ref _framesDropped);
            return;
        }

        _queue.Enqueue(CloneFrame(frame));
        _hasWork.Set();
    }

    private static FrameData CloneFrame(FrameData f) =>
        new()
        {
            Data = f.Data.ToArray(),
            Width = f.Width,
            Height = f.Height,
            PixelFormat = f.PixelFormat,
            FrameId = f.FrameId,
            Timestamp = f.Timestamp,
            ExposureUs = f.ExposureUs,
            Gain = f.Gain,
        };

    private void WriterLoop(object? state)
    {
        var token = (CancellationToken)state!;

        try
        {
            while (true)
            {
                if (_queue.TryDequeue(out FrameData? frame))
                {
                    Interlocked.Decrement(ref _queuedDepth);
                    ProcessFrame(frame);
                    continue;
                }

                if (token.IsCancellationRequested)
                    break;

                try
                {
                    _hasWork.Wait(50, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                _hasWork.Reset();
            }

            while (_queue.TryDequeue(out FrameData? pending))
            {
                Interlocked.Decrement(ref _queuedDepth);
                ProcessFrame(pending);
            }
        }
        catch (OperationCanceledException)
        {
            while (_queue.TryDequeue(out FrameData? pending))
            {
                Interlocked.Decrement(ref _queuedDepth);
                ProcessFrame(pending);
            }
        }
    }

    private void ProcessFrame(FrameData frame)
    {
        if (FrameLimit > 0 && Interlocked.Read(ref _framesCaptured) >= FrameLimit)
            return;

        if (TimeLimitSeconds > 0 && (DateTime.UtcNow - _sessionStartUtc).TotalSeconds >= TimeLimitSeconds)
            return;

        lock (_writerLock)
        {
            EnsureWriters(frame);

            switch (Format)
            {
                case CaptureFormat.Ser:
                    _serWriter!.WriteFrame(frame);
                    break;
                case CaptureFormat.Avi:
                    _aviWriter!.WriteFrame(Debayer.ToPackedRgb24(frame));
                    break;
                case CaptureFormat.Fits:
                    FITSWriter.WriteFrame(_fitsSessionDirectory!, frame);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported format {Format}.");
            }
        }

        Interlocked.Increment(ref _framesCaptured);
        UpdateFpsWindow();
        FrameCaptured?.Invoke(this, frame);
    }

    private void EnsureWriters(FrameData frame)
    {
        if (_writersInitialized)
            return;

        string stamp = _sessionTimestamp.ToString("yyyy-MM-dd_HH-mm-ss");

        switch (Format)
        {
            case CaptureFormat.Ser:
            {
                if (frame.PixelFormat is PixelFormat.BGRA32)
                    throw new NotSupportedException("BGRA32 is not supported for SER recording.");

                bool isColor = frame.PixelFormat is PixelFormat.RGB24 or PixelFormat.RGB48 or PixelFormat.BGR24;
                int depth = frame.PixelFormat is PixelFormat.Mono16 or PixelFormat.BayerRG16 or PixelFormat.RGB48
                    ? 16
                    : 8;
                string path = Path.Combine(OutputDirectory, $"{FilePrefix}_{stamp}.ser");
                var ser = new SERWriter(path, frame.Width, frame.Height, depth, isColor, Observer, Instrument, Telescope);
                if (frame.PixelFormat is PixelFormat.BayerRG8 or PixelFormat.BayerRG16)
                    ser.SetColorId(8);
                else if (frame.PixelFormat is PixelFormat.BGR24)
                    ser.SetColorId(101);
                _serWriter = ser;
                break;
            }
            case CaptureFormat.Avi:
            {
                string path = Path.Combine(OutputDirectory, $"{FilePrefix}_{stamp}.avi");
                _aviWriter = new AVIWriter(path, frame.Width, frame.Height, AviFps);
                break;
            }
            case CaptureFormat.Fits:
            {
                _fitsSessionDirectory = Path.Combine(OutputDirectory, $"{FilePrefix}_{stamp}");
                Directory.CreateDirectory(_fitsSessionDirectory);
                break;
            }
        }

        _writersInitialized = true;
    }

    private void UpdateFpsWindow()
    {
        long now = Stopwatch.GetTimestamp();
        _fpsTicks[_fpsSeq % FpsWindow] = now;
        _fpsSeq++;
        int n = Math.Min(FpsWindow, _fpsSeq);
        if (n < 2)
        {
            Volatile.Write(ref _currentFps, 0);
            return;
        }

        long oldest = _fpsTicks[(_fpsSeq - n) % FpsWindow];
        long newest = _fpsTicks[(_fpsSeq - 1) % FpsWindow];
        double seconds = (newest - oldest) / (double)Stopwatch.Frequency;
        if (seconds <= 0)
            return;

        double fps = (n - 1) / seconds;
        Volatile.Write(ref _currentFps, fps);
    }
}

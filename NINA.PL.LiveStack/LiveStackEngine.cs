using System.Collections.Concurrent;
using NINA.PL.Core;
using NINA.PL.Image;
using OpenCvSharp;

namespace NINA.PL.LiveStack;

/// <summary>
/// Real-time live stacking for planetary imaging: quality gate, alignment, mean/median/sigma-clip stack, wavelet sharpen.
/// </summary>
public sealed class LiveStackEngine : IDisposable
{
    public const int MedianFrameCap = 500;

    private readonly object _sync = new();
    private BlockingCollection<FrameData>? _queue;
    private Thread? _worker;
    private CancellationTokenSource? _cts;
    private CameraMediator? _camera;
    private volatile bool _isRunning;

    private Mat? _reference;
    private Mat? _meanAcc;
    private Mat? _sigmaSum;
    private Mat? _sigmaSumSq;
    private int _sigmaCount;
    private readonly List<Mat> _medianBuffer = new();

    private int _totalFrames;
    private int _acceptedFrames;
    private int _rejectedFrames;

    private double _qualityThreshold = 0.3;
    private StackingMode _mode;
    private double[] _waveletWeights = { 1.5, 1.5, 1.0, 1.0 };

    private Mat? _currentResult;
    private Mat? _currentResultSharpened;

    public bool IsRunning => _isRunning;

    public int TotalFrames
    {
        get { lock (_sync) return _totalFrames; }
    }

    public int AcceptedFrames
    {
        get { lock (_sync) return _acceptedFrames; }
    }

    public int RejectedFrames
    {
        get { lock (_sync) return _rejectedFrames; }
    }

    /// <summary>Minimum <see cref="FrameQualityEvaluator.Evaluate"/> score to accept a frame (0–1).</summary>
    public double QualityThreshold
    {
        get { lock (_sync) return _qualityThreshold; }
        set
        {
            double v = Math.Clamp(value, 0.0, 1.0);
            lock (_sync) _qualityThreshold = v;
        }
    }

    public StackingMode Mode
    {
        get { lock (_sync) return _mode; }
        set { lock (_sync) _mode = value; }
    }

    /// <summary>Wavelet layer weights passed to <see cref="WaveletSharpener.Sharpen"/>.</summary>
    public double[] WaveletWeights
    {
        get { lock (_sync) return (double[])_waveletWeights.Clone(); }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length == 0)
                throw new ArgumentException("At least one weight is required.", nameof(value));
            lock (_sync) _waveletWeights = (double[])value.Clone();
        }
    }

    public Mat? CurrentResult
    {
        get
        {
            lock (_sync)
                return _currentResult?.Clone();
        }
    }

    public Mat? CurrentResultSharpened
    {
        get
        {
            lock (_sync)
                return _currentResultSharpened?.Clone();
        }
    }

    /// <summary>Fired on the worker thread when a new stacked (unsharpened) result is available.</summary>
    public event EventHandler<Mat>? StackUpdated;

    public event EventHandler? StackingStarted;
    public event EventHandler? StackingStopped;

    /// <summary>Subscribes to <paramref name="camera"/>.<see cref="CameraMediator.FrameReceived"/> and starts the background processor.</summary>
    public void Start(CameraMediator camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        lock (_sync)
        {
            if (_isRunning)
                return;

            _queue = new BlockingCollection<FrameData>(new ConcurrentQueue<FrameData>(), boundedCapacity: 8);
            _camera = camera;
            _cts = new CancellationTokenSource();
            _isRunning = true;
            _worker = new Thread(() => RunWorker(_cts.Token))
            {
                IsBackground = true,
                Name = "NINA.PL.LiveStack",
            };
            _worker.Start();
            camera.FrameReceived += OnCameraFrame;
        }

        StackingStarted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Unsubscribes and stops the worker; pending queue items may be dropped.</summary>
    public void Stop()
    {
        CameraMediator? cam;
        Thread? worker;
        BlockingCollection<FrameData>? q;
        lock (_sync)
        {
            if (!_isRunning)
                return;

            cam = _camera;
            _camera = null;
            _isRunning = false;
            worker = _worker;
            _worker = null;
            q = _queue;
            _queue = null;
            _cts?.Cancel();
            if (cam is not null)
                cam.FrameReceived -= OnCameraFrame;
        }

        q?.CompleteAdding();
        worker?.Join(TimeSpan.FromSeconds(5));
        _cts?.Dispose();
        _cts = null;
        q?.Dispose();

        StackingStopped?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears accumulated stack state and counters (does not stop the worker).</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _reference?.Dispose();
            _reference = null;
            _meanAcc?.Dispose();
            _meanAcc = null;
            _sigmaSum?.Dispose();
            _sigmaSum = null;
            _sigmaSumSq?.Dispose();
            _sigmaSumSq = null;
            _sigmaCount = 0;
            foreach (Mat m in _medianBuffer)
                m.Dispose();
            _medianBuffer.Clear();
            _acceptedFrames = 0;
            _rejectedFrames = 0;
            _totalFrames = 0;
            _currentResult?.Dispose();
            _currentResult = null;
            _currentResultSharpened?.Dispose();
            _currentResultSharpened = null;
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_sync)
        {
            _reference?.Dispose();
            _reference = null;
            _meanAcc?.Dispose();
            _meanAcc = null;
            _sigmaSum?.Dispose();
            _sigmaSum = null;
            _sigmaSumSq?.Dispose();
            _sigmaSumSq = null;
            foreach (Mat m in _medianBuffer)
                m.Dispose();
            _medianBuffer.Clear();
            _currentResult?.Dispose();
            _currentResult = null;
            _currentResultSharpened?.Dispose();
            _currentResultSharpened = null;
        }

        GC.SuppressFinalize(this);
    }

    private void OnCameraFrame(object? sender, FrameData frame)
    {
        FrameData copy = CloneFrame(frame);
        BlockingCollection<FrameData>? q;
        lock (_sync)
        {
            if (!_isRunning || _queue is null)
                return;
            _totalFrames++;
            q = _queue;
        }

        if (!q.TryAdd(copy, millisecondsTimeout: 0))
            Logger.Warn("LiveStackEngine: processing queue full; frame dropped.");
    }

    private static FrameData CloneFrame(FrameData f) => new()
    {
        Data = (byte[])f.Data.Clone(),
        Width = f.Width,
        Height = f.Height,
        PixelFormat = f.PixelFormat,
        FrameId = f.FrameId,
        Timestamp = f.Timestamp,
        ExposureUs = f.ExposureUs,
        Gain = f.Gain,
    };

    private void RunWorker(CancellationToken token)
    {
        BlockingCollection<FrameData>? q;
        lock (_sync)
            q = _queue;

        if (q is null)
            return;

        try
        {
            foreach (FrameData frame in q.GetConsumingEnumerable())
            {
                if (token.IsCancellationRequested)
                    break;
                try
                {
                    ProcessOneFrame(frame);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "LiveStackEngine: frame processing failed.");
                }
            }
        }
        catch (InvalidOperationException)
        {
            // CompleteAdding while enumerating.
        }
    }

    private void ProcessOneFrame(FrameData frameData)
    {
        using Mat mat = Debayer.ToMat(frameData);
        double quality = FrameQualityEvaluator.Evaluate(mat);
        double threshold;
        StackingMode mode;
        double[] weights;
        lock (_sync)
        {
            threshold = _qualityThreshold;
            mode = _mode;
            weights = (double[])_waveletWeights.Clone();
        }

        if (quality < threshold)
        {
            lock (_sync) _rejectedFrames++;
            return;
        }

        Mat? frameToStack = null;
        try
        {
            bool firstFrame;
            lock (_sync)
                firstFrame = _reference is null;

            if (firstFrame)
            {
                lock (_sync)
                {
                    _reference = mat.Clone();
                    _acceptedFrames++;
                }

                frameToStack = mat.Clone();
            }
            else
            {
                Mat refSnap;
                lock (_sync)
                    refSnap = _reference!.Clone();

                try
                {
                    frameToStack = AlignmentEngine.AlignToReference(refSnap, mat);
                }
                finally
                {
                    refSnap.Dispose();
                }

                lock (_sync)
                    _acceptedFrames++;
            }

            ArgumentNullException.ThrowIfNull(frameToStack);

            lock (_sync)
            {
                switch (mode)
                {
                    case StackingMode.Mean:
                        AccumulateMean(frameToStack);
                        break;
                    case StackingMode.Median:
                        AccumulateMedian(frameToStack);
                        break;
                    case StackingMode.SigmaClip:
                        AccumulateSigmaClip(frameToStack);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
                }
            }

            Mat stacked;
            lock (_sync)
                stacked = BuildStackPreview(mode);

            using (stacked)
            {
                Mat sharpened = WaveletSharpener.Sharpen(stacked, weights);
                try
                {
                    lock (_sync)
                    {
                        _currentResult?.Dispose();
                        _currentResult = stacked.Clone();
                        _currentResultSharpened?.Dispose();
                        _currentResultSharpened = sharpened.Clone();
                    }

                    Mat eventMat = stacked.Clone();
                    try
                    {
                        StackUpdated?.Invoke(this, eventMat);
                    }
                    finally
                    {
                        eventMat.Dispose();
                    }
                }
                finally
                {
                    sharpened.Dispose();
                }
            }
        }
        finally
        {
            frameToStack?.Dispose();
        }
    }

    private void AccumulateMean(Mat aligned)
    {
        int ch = aligned.Channels();
        MatType accType = ch switch
        {
            1 => MatType.CV_64FC1,
            3 => MatType.CV_64FC3,
            _ => throw new NotSupportedException($"Unsupported channel count for mean stack: {ch}."),
        };

        if (_meanAcc is null || _meanAcc.Size() != aligned.Size() || _meanAcc.Type() != accType)
        {
            _meanAcc?.Dispose();
            _meanAcc = new Mat(aligned.Size(), accType, Scalar.All(0));
        }

        using Mat f = new Mat();
        aligned.ConvertTo(f, accType);
        Cv2.Add(_meanAcc, f, _meanAcc);
    }

    private void AccumulateMedian(Mat aligned)
    {
        _medianBuffer.Add(aligned.Clone());
        while (_medianBuffer.Count > MedianFrameCap)
        {
            Mat old = _medianBuffer[0];
            _medianBuffer.RemoveAt(0);
            old.Dispose();
        }
    }

    private void AccumulateSigmaClip(Mat aligned)
    {
        const double kSigma = 2.5;
        int ch = aligned.Channels();
        MatType accType = ch switch
        {
            1 => MatType.CV_64FC1,
            3 => MatType.CV_64FC3,
            _ => throw new NotSupportedException($"Unsupported channel count for sigma stack: {ch}."),
        };

        if (_sigmaSum is null || _sigmaSumSq is null || _sigmaSum.Size() != aligned.Size() || _sigmaSum.Type() != accType)
        {
            _sigmaSum?.Dispose();
            _sigmaSumSq?.Dispose();
            _sigmaSum = new Mat(aligned.Size(), accType, Scalar.All(0));
            _sigmaSumSq = new Mat(aligned.Size(), accType, Scalar.All(0));
            _sigmaCount = 0;
        }

        Mat sum = _sigmaSum!;
        Mat sumSq = _sigmaSumSq!;

        using Mat f64 = new Mat();
        aligned.ConvertTo(f64, accType);

        Mat toAdd = f64;
        using Mat? disposableClip = _sigmaCount > 0 ? new Mat() : null;

        if (_sigmaCount > 0 && disposableClip is not null)
        {
            using Mat mean = new Mat();
            using Mat meanSq = new Mat();
            using Mat varMat = new Mat();
            using Mat std = new Mat();
            using Mat kStd = new Mat();
            using Mat low = new Mat();
            using Mat high = new Mat();
            using Mat t1 = new Mat();

            Cv2.Divide(sum, Scalar.All(_sigmaCount), mean);
            Cv2.Divide(sumSq, Scalar.All(_sigmaCount), meanSq);
            Cv2.Multiply(mean, mean, varMat);
            Cv2.Subtract(meanSq, varMat, varMat);
            Cv2.Max(varMat, Scalar.All(0), varMat);
            Cv2.Sqrt(varMat, std);
            Cv2.Max(std, Scalar.All(1e-6), std);
            Cv2.Multiply(std, Scalar.All(kSigma), kStd);
            Cv2.Subtract(mean, kStd, low);
            Cv2.Add(mean, kStd, high);
            Cv2.Max(f64, low, t1);
            Cv2.Min(t1, high, disposableClip);
            toAdd = disposableClip;
        }

        using Mat sq = new Mat();
        Cv2.Multiply(toAdd, toAdd, sq);
        Cv2.Add(sum, toAdd, sum);
        Cv2.Add(sumSq, sq, sumSq);
        _sigmaCount++;
    }

    private Mat BuildStackPreview(StackingMode mode)
    {
        return mode switch
        {
            StackingMode.Mean => BuildMeanPreview(),
            StackingMode.Median => BuildMedianPreview(),
            StackingMode.SigmaClip => BuildSigmaPreview(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    private Mat BuildMeanPreview()
    {
        if (_meanAcc is null || _acceptedFrames == 0)
            throw new InvalidOperationException("No mean accumulation.");

        int denom = Math.Max(1, _acceptedFrames);
        var out8 = new Mat();
        _meanAcc.ConvertTo(out8, MatType.CV_8UC(_meanAcc.Channels()), alpha: 1.0 / denom);
        return out8;
    }

    private Mat BuildMedianPreview()
    {
        if (_medianBuffer.Count == 0)
            throw new InvalidOperationException("No median frames.");

        Mat proto = _medianBuffer[0];
        if (proto.Type() != MatType.CV_8UC1 && proto.Type() != MatType.CV_8UC3)
            throw new NotSupportedException("Median live stack supports 8-bit grayscale or BGR frames from Debayer.ToMat only.");

        var dst = new Mat(proto.Size(), proto.Type());
        int rows = proto.Rows;
        int cols = proto.Cols;
        int ch = proto.Channels();
        int total = _medianBuffer.Count;

        Parallel.For(0, rows, y =>
        {
            Span<int> hist = stackalloc int[256];
            for (int x = 0; x < cols; x++)
            {
                if (ch == 1)
                {
                    hist.Clear();
                    for (int i = 0; i < total; i++)
                        hist[_medianBuffer[i].At<byte>(y, x)]++;
                    dst.Set(y, x, MedianFromHist(hist, total));
                }
                else
                {
                    for (int c = 0; c < 3; c++)
                    {
                        hist.Clear();
                        for (int i = 0; i < total; i++)
                        {
                            Vec3b v = _medianBuffer[i].At<Vec3b>(y, x);
                            byte b = c switch { 0 => v.Item0, 1 => v.Item1, _ => v.Item2 };
                            hist[b]++;
                        }

                        Vec3b cur = dst.At<Vec3b>(y, x);
                        byte med = MedianFromHist(hist, total);
                        cur = c switch
                        {
                            0 => new Vec3b(med, cur.Item1, cur.Item2),
                            1 => new Vec3b(cur.Item0, med, cur.Item2),
                            _ => new Vec3b(cur.Item0, cur.Item1, med),
                        };
                        dst.Set(y, x, cur);
                    }
                }
            }
        });

        return dst;
    }

    private static byte MedianFromHist(ReadOnlySpan<int> hist, int total)
    {
        int half = (total + 1) / 2;
        int cum = 0;
        for (int i = 0; i < 256; i++)
        {
            cum += hist[i];
            if (cum >= half)
                return (byte)i;
        }

        return 255;
    }

    private Mat BuildSigmaPreview()
    {
        if (_sigmaSum is null || _sigmaCount == 0)
            throw new InvalidOperationException("No sigma accumulation.");

        using Mat mean = new Mat();
        Cv2.Divide(_sigmaSum, Scalar.All(_sigmaCount), mean);
        var out8 = new Mat();
        mean.ConvertTo(out8, MatType.CV_8UC(_sigmaSum.Channels()));
        return out8;
    }
}

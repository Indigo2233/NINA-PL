namespace NINA.PL.Core;

/// <summary>
/// A single captured frame with raw pixel payload and acquisition metadata.
/// </summary>
public sealed class FrameData
{
    /// <summary>Raw pixel buffer in native layout for <see cref="PixelFormat"/>.</summary>
    public required byte[] Data { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required PixelFormat PixelFormat { get; init; }

    public required ulong FrameId { get; init; }

    public required DateTime Timestamp { get; init; }

    /// <summary>Exposure duration in microseconds.</summary>
    public required double ExposureUs { get; init; }

    public required double Gain { get; init; }
}

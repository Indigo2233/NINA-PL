namespace NINA.PL.Core;

/// <summary>
/// Describes the layout and bit depth of image pixels from a camera or frame buffer.
/// </summary>
public enum PixelFormat
{
    Mono8,
    Mono16,
    BayerRG8,
    BayerRG16,
    RGB24,
    RGB48,
    BGR24,
    BGRA32,
}

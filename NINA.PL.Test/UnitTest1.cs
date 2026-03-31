using Xunit;
using NINA.PL.Core;

namespace NINA.PL.Test;

public class FrameDataTests
{
    [Fact]
    public void FrameData_CreatesWithExpectedProperties()
    {
        var frame = new FrameData
        {
            Data = new byte[640 * 480],
            Width = 640,
            Height = 480,
            PixelFormat = PixelFormat.Mono8,
            FrameId = 1,
            Timestamp = DateTime.UtcNow,
            ExposureUs = 10000,
            Gain = 100
        };

        Assert.Equal(640, frame.Width);
        Assert.Equal(480, frame.Height);
        Assert.Equal(PixelFormat.Mono8, frame.PixelFormat);
    }
}

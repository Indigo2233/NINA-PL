namespace NINA.PL.Profile;

public class TargetPreset
{
    public string Name { get; set; } = "";

    public string FilterName { get; set; } = "";

    public double ExposureUs { get; set; }

    public double Gain { get; set; }

    public int RoiX { get; set; }

    public int RoiY { get; set; }

    public int RoiWidth { get; set; }

    public int RoiHeight { get; set; }

    public int BinX { get; set; } = 1;

    public int BinY { get; set; } = 1;

    public string PixelFormat { get; set; } = "";

    public int FrameLimit { get; set; }

    public double TimeLimitSeconds { get; set; }

    public string CaptureFormat { get; set; } = "SER";
}

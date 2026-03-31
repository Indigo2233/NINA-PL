namespace NINA.PL.Equipment.Camera;

public class NativeFrameData
{
    public byte[] Data = Array.Empty<byte>();
    public int Width;
    public int Height;
    public ulong FrameId;
    public string PixelFormatName = string.Empty;
}

public class NativeCameraInfo
{
    public string SerialNumber = string.Empty;
    public string ModelName = string.Empty;
    public string VendorName = string.Empty;
}

/// <summary>
/// Native camera SDK surface (Daheng, etc.) implemented by native wrappers without referencing external driver assemblies.
/// </summary>
public interface INativeCameraBackend : IDisposable
{
    event EventHandler<NativeFrameData>? FrameArrived;

    bool IsConnected { get; }

    int SensorWidth { get; }

    int SensorHeight { get; }

    double ExposureMin { get; }

    double ExposureMax { get; }

    double GainMin { get; }

    double GainMax { get; }

    int MaxBinX { get; }

    int MaxBinY { get; }

    double PixelSizeUm { get; }

    string ModelName { get; }

    string SensorName { get; }

    bool IsColorCamera { get; }

    string BayerPattern { get; }

    void Initialize();

    List<NativeCameraInfo> EnumerateCameras();

    void OpenCamera(string serialNumber);

    void CloseCamera();

    void SetExposureTime(double microseconds);

    double GetGain();

    void SetGain(double gainDb);

    void SetROI(int offsetX, int offsetY, int width, int height);

    void SetBinning(int binX, int binY);

    bool SetPixelFormat(string format);

    List<string> GetPixelFormatList();

    bool StartCapture(int timeoutMs = 5000);

    void StopCapture();
}

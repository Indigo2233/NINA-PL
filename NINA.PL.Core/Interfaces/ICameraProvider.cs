using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NINA.PL.Core;

/// <summary>
/// Abstraction over camera hardware or remote camera services.
/// </summary>
public interface ICameraProvider : IDisposable
{
    string DriverType { get; }

    bool IsConnected { get; }

    int SensorWidth { get; }

    int SensorHeight { get; }

    double PixelSizeUm { get; }

    string ModelName { get; }

    bool IsColor { get; }

    string BayerPattern { get; }

    double ExposureMin { get; }

    double ExposureMax { get; }

    double GainMin { get; }

    double GainMax { get; }

    int MaxBinX { get; }

    int MaxBinY { get; }

    event EventHandler<FrameData>? FrameReceived;

    Task<List<CameraDeviceInfo>> EnumerateAsync();

    Task ConnectAsync(string deviceId);

    Task DisconnectAsync();

    void SetExposure(double microseconds);

    void SetGain(double gain);

    void SetROI(int x, int y, int width, int height);

    void ResetROI();

    void SetBinning(int binX, int binY);

    List<string> GetPixelFormats();

    void SetPixelFormat(string format);

    Task StartCaptureAsync();

    Task StopCaptureAsync();
}

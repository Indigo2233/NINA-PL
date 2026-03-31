using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NINA.PL.Core;

/// <summary>
/// Abstraction over equatorial or alt-az mount hardware.
/// </summary>
public interface IMountProvider : IDisposable
{
    string DriverType { get; }

    bool IsConnected { get; }

    bool IsTracking { get; }

    double RightAscension { get; }

    double Declination { get; }

    double Altitude { get; }

    double Azimuth { get; }

    bool CanPulseGuide { get; }

    Task<List<MountDeviceInfo>> EnumerateAsync();

    Task ConnectAsync(string deviceId);

    Task DisconnectAsync();

    Task SlewToCoordinatesAsync(double ra, double dec);

    /// <summary>Slew to horizontal coordinates (ASCOM: azimuth, altitude in degrees).</summary>
    Task SlewToAltAzAsync(double altitudeDegrees, double azimuthDegrees);

    Task PulseGuideAsync(GuideDirection direction, int durationMs);

    Task SetTrackingAsync(bool enabled);
}

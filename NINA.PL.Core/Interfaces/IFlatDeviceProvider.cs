using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NINA.PL.Core;

public interface IFlatDeviceProvider : IDisposable
{
    string DriverType { get; }
    bool IsConnected { get; }
    string DeviceName { get; }
    bool CoverIsOpen { get; }
    int Brightness { get; }
    int MinBrightness { get; }
    int MaxBrightness { get; }
    bool LightOn { get; }

    Task<List<FlatDeviceInfo>> EnumerateAsync();
    Task ConnectAsync(string deviceId);
    Task DisconnectAsync();
    Task OpenCoverAsync();
    Task CloseCoverAsync();
    Task SetBrightnessAsync(int brightness);
    Task ToggleLightAsync(bool on);
}

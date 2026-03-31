using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NINA.PL.Core;

/// <summary>
/// Abstraction over filter wheel hardware.
/// </summary>
public interface IFilterWheelProvider : IDisposable
{
    string DriverType { get; }

    bool IsConnected { get; }

    int CurrentPosition { get; }

    List<string> FilterNames { get; }

    Task<List<FilterWheelDeviceInfo>> EnumerateAsync();

    Task ConnectAsync(string deviceId);

    Task DisconnectAsync();

    Task SetPositionAsync(int position);
}

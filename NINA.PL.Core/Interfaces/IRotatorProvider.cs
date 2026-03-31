using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NINA.PL.Core;

public interface IRotatorProvider : IDisposable
{
    string DriverType { get; }
    bool IsConnected { get; }
    string DeviceName { get; }
    double Position { get; }
    bool IsMoving { get; }

    Task<List<RotatorDeviceInfo>> EnumerateAsync();
    Task ConnectAsync(string deviceId);
    Task DisconnectAsync();
    Task MoveToAsync(double position);
    Task HaltAsync();
}

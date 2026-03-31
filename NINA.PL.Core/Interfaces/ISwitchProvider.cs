using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NINA.PL.Core;

public interface ISwitchProvider : IDisposable
{
    string DriverType { get; }
    bool IsConnected { get; }
    string DeviceName { get; }
    int SwitchCount { get; }

    Task<List<SwitchDeviceInfo>> EnumerateAsync();
    Task ConnectAsync(string deviceId);
    Task DisconnectAsync();
    Task<string> GetSwitchNameAsync(int index);
    Task<bool> GetSwitchStateAsync(int index);
    Task SetSwitchAsync(int index, bool state);
    Task<double> GetSwitchValueAsync(int index);
    Task SetSwitchValueAsync(int index, double value);
}

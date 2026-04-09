using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NINA.PL.Core;

/// <summary>
/// Abstraction over an etalon tuner device (typically an ASCOM Focuser controlling
/// air-gap pressure in a solar H-alpha etalon).
/// </summary>
public interface IEtalonProvider : IDisposable
{
    string DriverType { get; }

    bool IsConnected { get; }

    int Position { get; }

    int MaxPosition { get; }

    bool IsMoving { get; }

    double Temperature { get; }

    Task<List<EtalonDeviceInfo>> EnumerateAsync();

    Task ConnectAsync(string deviceId);

    Task DisconnectAsync();

    Task MoveAsync(int position);

    Task MoveRelativeAsync(int offset);

    Task HaltAsync();
}

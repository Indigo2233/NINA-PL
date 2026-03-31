using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NINA.PL.Core;

/// <summary>
/// Abstraction over electronic focuser hardware.
/// </summary>
public interface IFocuserProvider : IDisposable
{
    string DriverType { get; }

    bool IsConnected { get; }

    int Position { get; }

    int MaxPosition { get; }

    bool IsMoving { get; }

    double Temperature { get; }

    Task<List<FocuserDeviceInfo>> EnumerateAsync();

    Task ConnectAsync(string deviceId);

    Task DisconnectAsync();

    Task MoveAsync(int position);

    /// <summary>Moves to an absolute step position (same as <see cref="MoveAsync"/>).</summary>
    Task MoveAbsoluteAsync(int position) => MoveAsync(position);

    Task MoveRelativeAsync(int offset);

    Task HaltAsync();
}

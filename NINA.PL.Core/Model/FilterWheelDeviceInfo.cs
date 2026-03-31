using System.Collections.Generic;

namespace NINA.PL.Core;

/// <summary>
/// Describes a discoverable filter wheel instance and its slot labels.
/// </summary>
public sealed class FilterWheelDeviceInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string DriverType { get; init; } = string.Empty;

    public IReadOnlyList<string> FilterNames { get; init; } = [];
}

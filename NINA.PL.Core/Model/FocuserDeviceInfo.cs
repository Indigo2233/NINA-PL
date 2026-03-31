namespace NINA.PL.Core;

/// <summary>
/// Describes a discoverable electronic focuser instance.
/// </summary>
public sealed class FocuserDeviceInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string DriverType { get; init; } = string.Empty;
}

namespace NINA.PL.Core;

/// <summary>
/// Describes a discoverable telescope mount instance.
/// </summary>
public sealed class MountDeviceInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string DriverType { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}

namespace NINA.PL.Core;

/// <summary>
/// Describes a discoverable etalon tuner (solar H-alpha pressure tuner) device.
/// Internally these are ASCOM Focuser devices used to adjust etalon air-gap pressure.
/// </summary>
public sealed class EtalonDeviceInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string DriverType { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public override string ToString() => Name;
}

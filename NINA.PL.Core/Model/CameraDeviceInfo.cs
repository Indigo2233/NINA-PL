namespace NINA.PL.Core;

/// <summary>
/// Describes a discoverable camera instance (native, ASCOM, Alpaca, etc.).
/// </summary>
public sealed class CameraDeviceInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string SerialNumber { get; init; } = string.Empty;

    /// <summary>Driver stack identifier, e.g. <c>Native</c>, <c>ASCOM</c>, <c>Alpaca</c>.</summary>
    public string DriverType { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}

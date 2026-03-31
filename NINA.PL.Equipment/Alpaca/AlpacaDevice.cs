namespace NINA.PL.Equipment.Alpaca;

/// <summary>
/// A device entry discovered via Alpaca management API.
/// </summary>
public sealed class AlpacaDevice
{
    public required string ServerUrl { get; init; }

    public required string DeviceType { get; init; }

    public int DeviceNumber { get; init; }

    public required string Name { get; init; }

    public required string UniqueId { get; init; }
}

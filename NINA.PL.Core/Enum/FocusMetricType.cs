namespace NINA.PL.Core;

/// <summary>
/// Algorithm used to score focus quality from image data.
/// </summary>
public enum FocusMetricType
{
    ContrastSobel,
    FourierEnergy,
    BrennerGradient,
}

namespace NINA.PL.Profile;

public class UserProfile
{
    public string Name { get; set; } = "Default";

    public string OutputDirectory { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\NINA-PL";

    public string LastCameraId { get; set; } = "";

    public string LastMountId { get; set; } = "";

    public string LastFocuserId { get; set; } = "";

    public string LastFilterWheelId { get; set; } = "";

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double Elevation { get; set; }

    public List<TargetPreset> Presets { get; set; } = new();
}

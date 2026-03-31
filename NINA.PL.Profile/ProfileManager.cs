using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace NINA.PL.Profile;

public sealed class ProfileManager
{
    private static readonly Lazy<ProfileManager> LazyInstance = new(() => new ProfileManager());

    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
    };

    private string? _activeProfilePath;

    private ProfileManager()
    {
    }

    public static ProfileManager Instance => LazyInstance.Value;

    public UserProfile ActiveProfile { get; private set; } = new();

    public event EventHandler<TargetPreset>? PresetApplied;

    public static string GetProfilesDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NINA-PL",
            "profiles");

    public static string GetDefaultPath() => Path.Combine(GetProfilesDirectory(), "default.json");

    public UserProfile CreateDefault()
    {
        ActiveProfile = new UserProfile();
        _activeProfilePath = GetDefaultPath();
        return ActiveProfile;
    }

    public UserProfile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Profile file not found.", fullPath);
        }

        var json = File.ReadAllText(fullPath);
        var profile = JsonConvert.DeserializeObject<UserProfile>(json, _jsonSettings)
                      ?? new UserProfile();

        ActiveProfile = profile;
        _activeProfilePath = fullPath;
        return ActiveProfile;
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonConvert.SerializeObject(ActiveProfile, _jsonSettings);
        File.WriteAllText(fullPath, json);
        _activeProfilePath = fullPath;
    }

    public void Save()
    {
        var path = _activeProfilePath ?? GetDefaultPath();
        Save(path);
    }

    public TargetPreset? GetPreset(string targetName, string filterName)
    {
        if (ActiveProfile.Presets is null || ActiveProfile.Presets.Count == 0)
        {
            return null;
        }

        return ActiveProfile.Presets.FirstOrDefault(p =>
            string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.FilterName, filterName, StringComparison.OrdinalIgnoreCase));
    }

    public void ApplyPreset(TargetPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        PresetApplied?.Invoke(this, preset);
    }
}

using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NINA.PL.Profile;

namespace NINA.PL.WPF.ViewModels;

public sealed partial class SettingsPanelViewModel : ObservableObject
{
    [ObservableProperty]
    private string profilePath = ProfileManager.GetDefaultPath();

    [ObservableProperty]
    private double observerLatitude = 40.0;

    [ObservableProperty]
    private double observerLongitude = -74.0;

    [RelayCommand]
    private void BrowseProfile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "JSON profile (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(ProfilePath) ?? ProfileManager.GetProfilesDirectory(),
        };
        if (dlg.ShowDialog() == true)
            ProfilePath = dlg.FileName;
    }

    [RelayCommand]
    private void LoadProfile()
    {
        if (!File.Exists(ProfilePath))
            return;
        ProfileManager.Instance.Load(ProfilePath);
    }

    [RelayCommand]
    private void SaveProfile()
    {
        ProfileManager.Instance.Save(ProfilePath);
    }

    [RelayCommand]
    private void EnsureDefaultProfile()
    {
        Directory.CreateDirectory(ProfileManager.GetProfilesDirectory());
        if (!File.Exists(ProfilePath))
            ProfileManager.Instance.CreateDefault();
        else
            ProfileManager.Instance.Load(ProfilePath);
    }
}

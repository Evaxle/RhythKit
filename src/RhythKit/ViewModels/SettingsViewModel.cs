using Microsoft.Win32;
using RhythKit.Services;
using RhythKit.Windows;

namespace RhythKit.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private string _colorsetsPath;
    private string _status = "";
    private string _serverUrl;

    public SettingsViewModel(SettingsService settings, Action? profileChanged = null)
    {
        _settings = settings;
        _colorsetsPath = settings.ColorsetsPath;
        _serverUrl = settings.ServerUrl;
        _profileChanged = profileChanged;

        Username = settings.HasProfile ? settings.Username : "Not signed in";
        ProfileId = settings.HasProfile ? $"Player ID {settings.PlayerId}" : "";

        BrowseCommand = new RelayCommand(Browse);
        SaveCommand = new RelayCommand(Save);
        ChangeProfileCommand = new RelayCommand(ChangeProfile);
    }

    private readonly Action? _profileChanged;

    public string ColorsetsPath
    {
        get => _colorsetsPath;
        set => SetProperty(ref _colorsetsPath, value);
    }

    public string ServerUrl
    {
        get => _serverUrl;
        set => SetProperty(ref _serverUrl, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string Username { get; private set; }
    public string ProfileId { get; private set; }

    public string Initials
    {
        get
        {
            if (!_settings.HasProfile || string.IsNullOrWhiteSpace(Username)) return "?";
            return Username[..1].ToUpperInvariant();
        }
    }

    public RelayCommand BrowseCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ChangeProfileCommand { get; }

    private void Browse()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Colorsets Folder",
            InitialDirectory = ColorsetsPath
        };
        if (dialog.ShowDialog() == true)
        {
            ColorsetsPath = dialog.FolderName;
        }
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(ColorsetsPath))
        {
            Status = "Please enter a valid path.";
            return;
        }
        _settings.SaveColorsetsPath(ColorsetsPath);
        if (!string.IsNullOrWhiteSpace(ServerUrl))
            _settings.SaveServerUrl(ServerUrl);
        Status = "Settings saved.";
    }

    private void ChangeProfile()
    {
        var window = new ProfileSetupWindow(_settings)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        if (window.ShowDialog() == true)
        {
            Username = _settings.Username;
            ProfileId = $"Player ID {_settings.PlayerId}";
            ServerUrl = _settings.ServerUrl;
            OnPropertyChanged(nameof(Username));
            OnPropertyChanged(nameof(ProfileId));
            Status = "Profile updated.";
            _profileChanged?.Invoke();
        }
    }
}

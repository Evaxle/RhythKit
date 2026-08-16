using Microsoft.Win32;
using RhythKit.Services;

namespace RhythKit.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private string _colorsetsPath;
    private string _status = "";

    public SettingsViewModel(SettingsService settings)
    {
        _settings = settings;
        _colorsetsPath = settings.ColorsetsPath;
        BrowseCommand = new RelayCommand(Browse);
        SaveCommand = new RelayCommand(Save);
    }

    public string ColorsetsPath
    {
        get => _colorsetsPath;
        set => SetProperty(ref _colorsetsPath, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public RelayCommand BrowseCommand { get; }
    public RelayCommand SaveCommand { get; }

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
        Status = "Settings saved.";
    }
}

using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using Microsoft.Win32;
using RhythKit.Models;
using RhythKit.Services;

namespace RhythKit.ViewModels;

public class ColorsetMakerViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly ColorsetService _colorsets;
    private int _colorCount = 1;
    private string _colorsetName = "";
    private string _savePath;
    private string _status = "Ready.";
    private ColorItem? _selectedItem;
    private Color _wheelColor = Color.FromRgb(255, 255, 255);
    private string _selectedHistoryPath = "";

    public ObservableCollection<ColorItem> Colors { get; } = new();
    public ObservableCollection<string> HistoryFiles { get; } = new();

    public ColorsetMakerViewModel(SettingsService settings)
    {
        _settings = settings;
        _colorsets = new ColorsetService();
        _savePath = settings.ColorsetsPath;

        RebuildColors();
        LoadHistory();

        DownloadCommand = new RelayCommand(Download);
        BrowseCommand = new RelayCommand(Browse);
        LoadHistoryCommand = new RelayCommand(LoadSelectedHistory);
    }

    public int ColorCount
    {
        get => _colorCount;
        set
        {
            var v = value;
            if (v < 1) v = 1;
            if (v > 50) v = 50;
            if (SetProperty(ref _colorCount, v))
                RebuildColors();
        }
    }

    public string ColorsetName
    {
        get => _colorsetName;
        set => SetProperty(ref _colorsetName, value);
    }

    public string SavePath
    {
        get => _savePath;
        set => SetProperty(ref _savePath, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public ColorItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem != null)
                _selectedItem.IsSelected = false;
            if (SetProperty(ref _selectedItem, value))
            {
                if (_selectedItem != null)
                {
                    _selectedItem.IsSelected = true;
                    WheelColor = _selectedItem.Color;
                }
            }
        }
    }

    public Color WheelColor
    {
        get => _wheelColor;
        set
        {
            if (SetProperty(ref _wheelColor, value))
            {
                if (_selectedItem != null)
                    _selectedItem.Color = value;
                OnPropertyChanged(nameof(WheelHex));
                OnPropertyChanged(nameof(WheelColorBrush));
            }
        }
    }

    public string WheelHex => $"#{_wheelColor.R:X2}{_wheelColor.G:X2}{_wheelColor.B:X2}";

    public SolidColorBrush WheelColorBrush => new(_wheelColor);

    public string SelectedHistoryPath
    {
        get => _selectedHistoryPath;
        set => SetProperty(ref _selectedHistoryPath, value);
    }

    public RelayCommand DownloadCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand LoadHistoryCommand { get; }

    private void RebuildColors()
    {
        while (Colors.Count > _colorCount)
            Colors.RemoveAt(Colors.Count - 1);

        while (Colors.Count < _colorCount)
        {
            var item = new ColorItem { Index = Colors.Count + 1, Color = Color.FromRgb(255, 255, 255) };
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(WheelHex));
            Colors.Add(item);
        }

        if (Colors.Count > 0)
            SelectedItem = Colors[0];
    }

    private void Browse()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Save Folder",
            InitialDirectory = SavePath
        };
        if (dialog.ShowDialog() == true)
        {
            SavePath = dialog.FolderName;
        }
    }

    private void Download()
    {
        try
        {
            var fullPath = _colorsets.Save(SavePath, ColorsetName, Colors.ToList());
            Status = $"Saved to {fullPath}";
            LoadHistory();
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    private void LoadHistory()
    {
        HistoryFiles.Clear();
        foreach (var file in _colorsets.ListColorsets(_settings.ColorsetsPath))
            HistoryFiles.Add(file);
    }

    private void LoadSelectedHistory()
    {
        if (string.IsNullOrEmpty(SelectedHistoryPath))
            return;
        var colors = _colorsets.Load(SelectedHistoryPath);
        if (colors.Count == 0)
        {
            Status = "No valid colors found in selected file.";
            return;
        }

        ColorCount = colors.Count;
        for (int i = 0; i < Colors.Count && i < colors.Count; i++)
            Colors[i].Color = colors[i];
        SelectedItem = Colors.Count > 0 ? Colors[0] : null;
        Status = $"Loaded {Path.GetFileName(SelectedHistoryPath)}";
    }
}

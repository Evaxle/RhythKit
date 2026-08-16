using RhythKit.Services;

namespace RhythKit.ViewModels;

public class MainViewModel : ViewModelBase
{
    private object? _currentPage;
    private readonly SettingsService _settings;

    public MainViewModel()
    {
        _settings = new SettingsService();
        ColorsetMaker = new ColorsetMakerViewModel(_settings);
        Settings = new SettingsViewModel(_settings);

        GoToColorsetCommand = new RelayCommand(() => CurrentPage = ColorsetMaker);
        GoToSettingsCommand = new RelayCommand(() => CurrentPage = Settings);

        CurrentPage = ColorsetMaker;
    }

    public ColorsetMakerViewModel ColorsetMaker { get; }
    public SettingsViewModel Settings { get; }

    public object? CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    public RelayCommand GoToColorsetCommand { get; }
    public RelayCommand GoToSettingsCommand { get; }
}

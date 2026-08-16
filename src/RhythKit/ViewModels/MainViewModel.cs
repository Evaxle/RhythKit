using RhythKit.Services;

namespace RhythKit.ViewModels;

public class MainViewModel : ViewModelBase
{
    private object? _currentPage;
    private readonly SettingsService _settings;

    public MainViewModel(SettingsService? settings = null)
    {
        _settings = settings ?? new SettingsService();
        ColorsetMaker = new ColorsetMakerViewModel(_settings);
        Settings = new SettingsViewModel(_settings, ProfileChanged);
        Messages = new MessagesViewModel(_settings);

        GoToColorsetCommand = new RelayCommand(() => CurrentPage = ColorsetMaker);
        GoToSettingsCommand = new RelayCommand(() => CurrentPage = Settings);
        GoToMessagesCommand = new RelayCommand(() => CurrentPage = Messages);

        CurrentPage = ColorsetMaker;
    }

    public ColorsetMakerViewModel ColorsetMaker { get; }
    public SettingsViewModel Settings { get; }
    public MessagesViewModel Messages { get; }

    public string LoggedInAs => _settings.HasProfile ? _settings.Username : "Not signed in";

    private void ProfileChanged()
    {
        OnPropertyChanged(nameof(LoggedInAs));
    }

    public object? CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    public RelayCommand GoToColorsetCommand { get; }
    public RelayCommand GoToSettingsCommand { get; }
    public RelayCommand GoToMessagesCommand { get; }
}

using System.Windows;
using System.Windows.Input;
using RhythKit.Models;
using RhythKit.Services;

namespace RhythKit.Windows;

public partial class ProfileSetupWindow : Window
{
    private readonly SettingsService _settings;
    private readonly RhythiaApiService _rhythia = new();
    private RhythiaProfile? _profile;

    public ProfileSetupWindow(SettingsService settings)
    {
        InitializeComponent();
        _settings = settings;
        ServerUrlBox.Text = _settings.ServerUrl;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) return;
        DragMove();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private async void FetchBtn_Click(object sender, RoutedEventArgs e)
    {
        FetchBtn.IsEnabled = false;
        SaveBtn.IsEnabled = false;
        ProfilePreview.Visibility = Visibility.Collapsed;
        StatusText.Text = "Fetching profile...";

        var url = ProfileUrlBox.Text;
        if (!RhythiaApiService.TryParseProfileId(url, out var playerId))
        {
            StatusText.Text = "Could not read a player ID from that URL. It should look like https://www.rhythia.com/player/7564";
            FetchBtn.IsEnabled = true;
            return;
        }

        var profile = await _rhythia.GetProfileAsync(playerId);
        if (profile == null || string.IsNullOrEmpty(profile.Username))
        {
            StatusText.Text = "Could not find a Rhythia profile with that ID. Check the link and try again.";
            FetchBtn.IsEnabled = true;
            return;
        }

        _profile = profile;
        FoundName.Text = profile.Username;
        FoundId.Text = $"Player ID {profile.PlayerId}";
        AvatarInitials.Text = profile.Username.Length > 0 ? profile.Username[..1].ToUpperInvariant() : "?";
        ProfilePreview.Visibility = Visibility.Visible;
        StatusText.Text = "Profile found! Click Save & Continue to log in.";
        FetchBtn.IsEnabled = true;
        SaveBtn.IsEnabled = true;
    }

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_profile == null) return;

        var serverUrl = ServerUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            StatusText.Text = "Please enter the RhythKit server URL.";
            return;
        }

        SaveBtn.IsEnabled = false;
        StatusText.Text = "Connecting to server...";

        _settings.SaveServerUrl(serverUrl);
        _settings.SaveProfile(_profile.PlayerId, _profile.Username, _profile.AvatarUrl);

        var api = new RhythKitApiService(_settings);
        try
        {
            var reg = await api.RegisterAsync(_profile.PlayerId, _profile.Username, _profile.AvatarUrl);
            if (reg == null)
            {
                StatusText.Text = "Profile saved, but the RhythKit server is unreachable. Online messaging will not work until the server is running. You can change the server later in Settings.";
                await Task.Delay(900);
                DialogResult = true;
                return;
            }
            _settings.SaveProfile(_profile.PlayerId, reg.Value.User.Username, _profile.AvatarUrl);
            _settings.SaveServer(serverUrl, reg.Value.Token);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Profile saved, but the server connection failed: " + ex.Message +
                              " Online messaging will not work until the server is running.";
            await Task.Delay(900);
            DialogResult = true;
        }
    }
}

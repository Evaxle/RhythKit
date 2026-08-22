using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.NetworkInformation;

namespace RhythKit.Agent;

public sealed class RhythKitSettingsForm : Form
{
    private const string RhythiansUrl = "https://rhythians.vercel.app";
    private readonly string gameDirectory;
    private readonly string gameType;
    private readonly Label statusLabel = new() { AutoSize = true, Text = "Rhythians: Checking..." };
    private readonly Label accountLabel = new() { AutoSize = true, Text = "Account ID: Checking..." };
    private readonly Label networkLabel = new() { AutoSize = true, Text = "Internet: Checking..." };
    private readonly Label databaseLabel = new() { AutoSize = true, Text = "Database: Checking..." };
    private readonly Label integrationLabel = new() { AutoSize = true, Text = "Game integration: Checking..." };
    private readonly Label mapLabel = new() { AutoSize = true, Text = "Map capture: Checking..." };
    private readonly Label gameLabel = new() { AutoSize = true };
    private readonly Button connectButton = new() { Text = "Connect Rhythians", AutoSize = true };
    private readonly Button reconnectButton = new() { Text = "Reconnect Rhythians Account", AutoSize = true };
    private readonly Button testButton = new() { Text = "Test Connection", AutoSize = true };
    private readonly Button databaseTestButton = new() { Text = "Test Database", AutoSize = true };
    private readonly Button mapsButton = new() { Text = "Open Maps", AutoSize = true };
    private readonly System.Windows.Forms.Timer timer;
    private readonly HttpClient client;

    public RhythKitSettingsForm(string gameDirectory, string gameType)
    {
        this.gameDirectory = gameDirectory;
        this.gameType = gameType;
        client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{AgentPorts.For(gameType)}/") };
        Text = "RhythKit Settings";
        Width = 720;
        Height = 430;
        MinimumSize = new Size(720, 430);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ShowInTaskbar = true;
        Visible = false;
        SetLogoIcon();
        gameLabel.Text = $"Game: {DisplayGame(gameType)}";
        connectButton.Click += async (_, _) => await ConnectAsync();
        reconnectButton.Click += (_, _) => ReconnectAccount();
        testButton.Click += async (_, _) => await RefreshAsync(true);
        databaseTestButton.Click += async (_, _) => await TestDatabaseAsync();
        mapsButton.Click += (_, _) => OpenMaps();
        var title = new Label { Text = "RhythKit", AutoSize = true, Font = new Font(Font.FontFamily, 18, FontStyle.Bold) };
        var info = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
        info.Controls.Add(title);
        info.Controls.Add(gameLabel);
        info.Controls.Add(networkLabel);
        info.Controls.Add(statusLabel);
        info.Controls.Add(accountLabel);
        info.Controls.Add(databaseLabel);
        info.Controls.Add(integrationLabel);
        info.Controls.Add(mapLabel);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(20, 0, 20, 20) };
        buttons.Controls.Add(connectButton);
        buttons.Controls.Add(reconnectButton);
        buttons.Controls.Add(testButton);
        buttons.Controls.Add(databaseTestButton);
        buttons.Controls.Add(mapsButton);
        Controls.Add(buttons);
        Controls.Add(info);
        timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += async (_, _) => await RefreshAsync(false);
        Shown += async (_, _) => { timer.Start(); await RefreshAsync(false); };
        FormClosed += (_, _) => { timer.Stop(); client.Dispose(); };
    }

    private static string DisplayGame(string value) => value switch
    {
        "RhythiaSteam" => "Rhythia Steam",
        "SspNightly" => "Sound Space Plus",
        "Vulnus" => "Vulnus",
        "RewriteRhythia" => "Rewrite Rhythia",
        _ => value
    };

    private void SetLogoIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "rhythkitlogo.ico");
            if (File.Exists(path)) Icon = new Icon(path);
        }
        catch { }
    }

    public void ShowForGame()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(ShowForGame); return; }
        if (!Visible) Show();
        Activate();
    }

    private async Task RefreshAsync(bool showFailure)
    {
        var networkAvailable = NetworkInterface.GetIsNetworkAvailable();
        networkLabel.Text = networkAvailable ? "Internet: Connected" : "Internet: Not connected";
        try
        {
            using var response = await client.GetAsync("status");
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException();
            var result = await response.Content.ReadFromJsonAsync<StatusResponse>();
            var authenticated = result?.Authenticated == true;
            statusLabel.Text = authenticated ? $"Rhythians: Connected ({result!.Username ?? "Account"})" : "Rhythians: Not connected";
            accountLabel.Text = authenticated ? $"Account ID: {result!.UserId ?? "Unknown"}" : "Account ID: Not linked";
            databaseLabel.Text = authenticated && networkAvailable ? "Database: Connected" : networkAvailable ? "Database: Login required" : "Database: Waiting for internet";
            connectButton.Visible = !authenticated;
            reconnectButton.Visible = authenticated;
            databaseTestButton.Visible = true;
            integrationLabel.Text = result?.IntegrationConnected == true ? "Game integration: Connected" : "Game integration: Not connected";
            mapLabel.Text = result?.MapCaptureReady == true ? $"Map capture: Ready{(string.IsNullOrWhiteSpace(result.MapId) ? string.Empty : $" ({result.MapId})")}" : "Map capture: Not ready";
        }
        catch
        {
            statusLabel.Text = "Rhythians: Unreachable";
            accountLabel.Text = "Account ID: Unknown";
            databaseLabel.Text = networkAvailable ? "Database: Unreachable" : "Database: Waiting for internet";
            integrationLabel.Text = "Game integration: Unknown";
            mapLabel.Text = "Map capture: Unknown";
            connectButton.Visible = true;
            reconnectButton.Visible = true;
            databaseTestButton.Visible = true;
            if (showFailure) MessageBox.Show(this, networkAvailable ? "RhythKit could not contact Rhythians." : "This computer is not connected to the internet.", "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ConnectAsync()
    {
        connectButton.Enabled = false;
        try
        {
            using var response = await client.PostAsync("auth/start", null);
            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show(this, "Rhythians authorization could not be started.", "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var result = await response.Content.ReadFromJsonAsync<AuthStartResponse>();
            if (result == null || string.IsNullOrWhiteSpace(result.VerificationUrl)) return;
            Process.Start(new ProcessStartInfo { FileName = result.VerificationUrl, UseShellExecute = true });
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { connectButton.Enabled = true; await RefreshAsync(false); }
    }

    private void ReconnectAccount()
    {
        try
        {
            TokenStore.Clear();
            statusLabel.Text = "Rhythians: Not connected";
            accountLabel.Text = "Account ID: Not linked";
            databaseLabel.Text = "Database: Login required";
            connectButton.Visible = true;
            var url = $"{RhythiansUrl}/settings?rhythkitReconnect=1&game={Uri.EscapeDataString(gameType)}";
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task TestDatabaseAsync()
    {
        databaseTestButton.Enabled = false;
        try
        {
            var state = TokenStore.Load();
            if (string.IsNullOrWhiteSpace(state?.Token))
            {
                MessageBox.Show(this, "Connect a Rhythians account first.", "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{RhythiansUrl}/api/rhythkit/test-database");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.Token);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show(this, body, "RhythKit database test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            databaseLabel.Text = "Database: Test passed";
            MessageBox.Show(this, "The RhythKit Agent successfully wrote a test row to the Rhythians database.", "RhythKit database test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "RhythKit database test", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { databaseTestButton.Enabled = true; }
    }

    private void OpenMaps()
    {
        var path = gameType.Equals("Vulnus", StringComparison.OrdinalIgnoreCase) ? Path.Combine(gameDirectory, "maps") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rhythians", "maps");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    public void CloseFromGameExit()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(CloseFromGameExit); return; }
        Close();
    }

    private sealed record StatusResponse(bool Installed, bool Running, bool GameRunning, bool LoggedIn, bool Connected, bool Authenticated, string? Username, string? UserId, string? Game, bool IntegrationConnected, bool MapCaptureReady, string? MapId, string? LastEvent, DateTimeOffset LastSeenAt, string? GameVersion, string? RhythiansServerUrl);
    private sealed record AuthStartResponse(string DeviceCode, string UserCode, string VerificationUrl, int ExpiresIn, string? GameVersion);
}

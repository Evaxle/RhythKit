using System.Diagnostics;
using System.Net.Http.Json;

namespace RhythKit.Agent;

public sealed class RhythKitSettingsForm : Form
{
    private readonly string gameDirectory;
    private readonly string gameType;
    private readonly Label statusLabel = new() { AutoSize = true, Text = "Rhythians: Checking..." };
    private readonly Label integrationLabel = new() { AutoSize = true, Text = "Game integration: Checking..." };
    private readonly Label mapLabel = new() { AutoSize = true, Text = "Map capture: Checking..." };
    private readonly Label gameLabel = new() { AutoSize = true };
    private readonly TextBox serverUrlBox = new() { Width = 420 };
    private readonly Button saveServerButton = new() { Text = "Save Server URL", AutoSize = true };
    private readonly Button connectButton = new() { Text = "Connect Rhythians", AutoSize = true };
    private readonly Button testButton = new() { Text = "Test Connection", AutoSize = true };
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
        testButton.Click += async (_, _) => await RefreshAsync(true);
        saveServerButton.Click += async (_, _) => await SaveServerUrlAsync();
        mapsButton.Click += (_, _) => OpenMaps();
        var title = new Label { Text = "RhythKit", AutoSize = true, Font = new Font(Font.FontFamily, 18, FontStyle.Bold) };
        var info = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
        info.Controls.Add(title);
        info.Controls.Add(gameLabel);
        info.Controls.Add(statusLabel);
        info.Controls.Add(integrationLabel);
        info.Controls.Add(mapLabel);
        var serverLabel = new Label { Text = "Rhythians server/local relay URL", AutoSize = true, Margin = new Padding(3, 12, 3, 3) };
        info.Controls.Add(serverLabel);
        var serverRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        serverRow.Controls.Add(serverUrlBox);
        serverRow.Controls.Add(saveServerButton);
        info.Controls.Add(serverRow);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(20, 0, 20, 20) };
        buttons.Controls.Add(connectButton);
        buttons.Controls.Add(testButton);
        buttons.Controls.Add(mapsButton);
        Controls.Add(buttons);
        Controls.Add(info);
        timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += async (_, _) => await RefreshAsync(false);
        Shown += async (_, _) => { timer.Start(); await LoadSettingsAsync(); await RefreshAsync(false); };
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

    private async Task LoadSettingsAsync()
    {
        try
        {
            var result = await client.GetFromJsonAsync<SettingsResponse>("settings");
            if (result != null && !string.IsNullOrWhiteSpace(result.ServerUrl)) serverUrlBox.Text = result.ServerUrl;
        }
        catch { serverUrlBox.Text = "https://rhythians.vercel.app"; }
    }

    private async Task SaveServerUrlAsync()
    {
        saveServerButton.Enabled = false;
        try
        {
            using var response = await client.PostAsJsonAsync("settings/server-url", new { url = serverUrlBox.Text.Trim() });
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                MessageBox.Show(this, error, "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var result = await response.Content.ReadFromJsonAsync<SettingsResponse>();
            if (result != null) serverUrlBox.Text = result.ServerUrl;
            MessageBox.Show(this, "The Rhythians server URL was saved. New requests will use it.", "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { saveServerButton.Enabled = true; }
    }

    private async Task RefreshAsync(bool showFailure)
    {
        try
        {
            using var response = await client.GetAsync("status");
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException();
            var result = await response.Content.ReadFromJsonAsync<StatusResponse>();
            if (result?.Authenticated == true && !string.IsNullOrWhiteSpace(result.Username))
            {
                statusLabel.Text = $"Rhythians: Connected ({result.Username})";
                connectButton.Visible = false;
            }
            else
            {
                statusLabel.Text = "Rhythians: Not connected";
                connectButton.Visible = true;
            }
            integrationLabel.Text = result?.IntegrationConnected == true ? "Game integration: Connected" : "Game integration: Not connected";
            mapLabel.Text = result?.MapCaptureReady == true ? $"Map capture: Ready{(string.IsNullOrWhiteSpace(result.MapId) ? string.Empty : $" ({result.MapId})")}" : "Map capture: Not ready";
            if (!string.IsNullOrWhiteSpace(result?.RhythiansServerUrl) && !serverUrlBox.Focused) serverUrlBox.Text = result.RhythiansServerUrl;
        }
        catch
        {
            statusLabel.Text = "RhythKit: Starting...";
            integrationLabel.Text = "Game integration: Unknown";
            mapLabel.Text = "Map capture: Unknown";
            connectButton.Visible = true;
            if (showFailure) MessageBox.Show(this, "RhythKit could not contact its local agent.", "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    private void OpenMaps()
    {
        var path = gameType.Equals("Vulnus", StringComparison.OrdinalIgnoreCase) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vulnus", "maps") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CapoRhythia", "Rhythians", "maps");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    public void CloseFromGameExit()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(CloseFromGameExit); return; }
        Close();
    }

    private sealed record StatusResponse(bool Installed, bool Running, bool GameRunning, bool LoggedIn, bool Connected, bool Authenticated, string? Username, string? Game, bool IntegrationConnected, bool MapCaptureReady, string? MapId, string? LastEvent, DateTimeOffset LastSeenAt, string? GameVersion, string? RhythiansServerUrl);
    private sealed record AuthStartResponse(string DeviceCode, string UserCode, string VerificationUrl, int ExpiresIn, string? GameVersion);
    private sealed record SettingsResponse(string ServerUrl, string? Game, string? GameDirectory);
}

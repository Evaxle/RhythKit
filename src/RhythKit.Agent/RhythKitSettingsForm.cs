using System.Diagnostics;
using System.Net.Http.Json;

namespace RhythKit.Agent;

public sealed class RhythKitSettingsForm : Form
{
    private readonly string gameDirectory;
    private readonly string gameType;
    private readonly Label statusLabel = new() { AutoSize = true, Text = "Rhythians: Checking..." };
    private readonly Label gameLabel = new() { AutoSize = true };
    private readonly Button connectButton = new() { Text = "Connect Rhythians", AutoSize = true };
    private readonly Button testButton = new() { Text = "Test Connection", AutoSize = true };
    private readonly Button mapsButton = new() { Text = "Open Rhythians Maps", AutoSize = true };
    private readonly System.Windows.Forms.Timer timer;
    private readonly HttpClient client = new() { BaseAddress = new Uri("http://127.0.0.1:45872/") };

    public RhythKitSettingsForm(string gameDirectory, string gameType)
    {
        this.gameDirectory = gameDirectory;
        this.gameType = gameType;
        Text = "RhythKit Settings";
        Width = 520;
        Height = 280;
        MinimumSize = new Size(520, 280);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ShowInTaskbar = true;
        Visible = false;
        SetLogoIcon();

        gameLabel.Text = $"Game: {gameType}";
        connectButton.Click += async (_, _) => await ConnectAsync();
        testButton.Click += async (_, _) => await RefreshAsync(true);
        mapsButton.Click += (_, _) => OpenMaps();

        var title = new Label { Text = "RhythKit", AutoSize = true, Font = new Font(Font.FontFamily, 18, FontStyle.Bold) };
        var info = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
        info.Controls.Add(title);
        info.Controls.Add(gameLabel);
        info.Controls.Add(statusLabel);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(20, 0, 20, 20) };
        buttons.Controls.Add(connectButton);
        buttons.Controls.Add(testButton);
        buttons.Controls.Add(mapsButton);

        Controls.Add(buttons);
        Controls.Add(info);

        timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += async (_, _) => await RefreshAsync(false);
        Shown += async (_, _) =>
        {
            timer.Start();
            await RefreshAsync(false);
        };
        FormClosed += (_, _) => timer.Stop();
    }

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
        if (InvokeRequired)
        {
            BeginInvoke(ShowForGame);
            return;
        }
        if (!Visible) Show();
        Activate();
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
        }
        catch
        {
            statusLabel.Text = "RhythKit: Starting...";
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
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            connectButton.Enabled = true;
            await RefreshAsync(false);
        }
    }

    private void OpenMaps()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CapoRhythia", "Rhythians", "maps");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    public void CloseFromGameExit()
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(CloseFromGameExit);
            return;
        }
        Close();
    }

    private sealed record StatusResponse(bool Installed, bool Running, bool Authenticated, string? Username, string? Game);
    private sealed record AuthStartResponse(string DeviceCode, string UserCode, string VerificationUrl, int ExpiresIn, string? GameVersion);
}

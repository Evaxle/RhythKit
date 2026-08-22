using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Forms;

namespace RhythKit.Agent;

internal static class AgentUiBootstrap
{
    private static string? gameDirectory;
    private static string? gameExecutable;
    private static string gameType = "unknown";
    private static RhythKitSettingsForm? form;
    private static VulnusConverterForm? converterForm;
    private static int authorizationRunning;
    private static readonly CancellationTokenSource cancellation = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        gameDirectory = GetArgument("--game-dir");
        gameType = GetArgument("--game-type") ?? "unknown";
        gameExecutable = GetArgument("--game-exe") ?? LoadManifestExecutable(gameDirectory);
        var port = AgentPorts.For(gameType);
        var uiThread = new Thread(() =>
        {
            form = new RhythKitSettingsForm(gameDirectory ?? string.Empty, gameType);
            if (IsGameRunning()) ShowGameUi();
            Application.Run(form);
        });
        uiThread.IsBackground = true;
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        _ = Task.Run(() => WatchGameStartupAsync(port));
        if (gameType.Equals("SspNightly", StringComparison.OrdinalIgnoreCase)) _ = Task.Run(() => new SspScoreWatcher(gameDirectory, cancellation.Token).RunAsync());
    }

    private static async Task WatchGameStartupAsync(int port)
    {
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(5) };
        var wasRunning = IsGameRunning();
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                var running = IsGameRunning();
                if (running && !wasRunning)
                {
                    GameConnectionState.AddDebug("GameStarted", $"{DisplayGame(gameType)} detected starting at {gameExecutable ?? "the configured executable"}.");
                    ShowGameUi();
                }
                if (!running && wasRunning)
                {
                    GameConnectionState.Update(new GameConnectionUpdate(false, false, false, gameType, null, null, "GameStopped"));
                    GameConnectionState.AddDebug("GameStopped", $"{DisplayGame(gameType)} closed; RhythKit Agent is stopping.");
                    cancellation.Cancel();
                    CloseGameUi();
                    await Task.Delay(500);
                    Environment.Exit(0);
                    return;
                }
                if (running)
                {
                    using var response = await client.GetAsync("status");
                    if (response.IsSuccessStatusCode)
                    {
                        var status = await response.Content.ReadFromJsonAsync<StatusResponse>();
                        if (status?.Authenticated != true && Interlocked.CompareExchange(ref authorizationRunning, 1, 0) == 0)
                        {
                            try
                            {
                                using var authResponse = await client.PostAsync("auth/start", null);
                                if (authResponse.IsSuccessStatusCode)
                                {
                                    var authorization = await authResponse.Content.ReadFromJsonAsync<AuthStartResponse>();
                                    if (authorization != null && !string.IsNullOrWhiteSpace(authorization.VerificationUrl))
                                    {
                                        Process.Start(new ProcessStartInfo { FileName = authorization.VerificationUrl, UseShellExecute = true });
                                        await WaitForAuthorizationAsync(client, authorization.ExpiresIn);
                                    }
                                }
                            }
                            finally { Interlocked.Exchange(ref authorizationRunning, 0); }
                        }
                    }
                }
                wasRunning = running;
            }
            catch { }
            try { await Task.Delay(1000, cancellation.Token); } catch (OperationCanceledException) { }
        }
    }

    private static void ShowGameUi()
    {
        if (form == null || form.IsDisposed) return;
        if (form.InvokeRequired) { form.BeginInvoke(ShowGameUi); return; }
        form.ShowForGame();
        if (gameType.Equals("Vulnus", StringComparison.OrdinalIgnoreCase) && (converterForm == null || converterForm.IsDisposed))
        {
            converterForm = new VulnusConverterForm();
            converterForm.Show();
        }
    }

    private static void CloseGameUi()
    {
        if (form == null || form.IsDisposed) return;
        if (form.InvokeRequired) { form.BeginInvoke(CloseGameUi); return; }
        converterForm?.Close();
        converterForm = null;
        form.CloseFromGameExit();
    }

    private static async Task WaitForAuthorizationAsync(HttpClient client, int expiresIn)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn));
        while (DateTimeOffset.UtcNow < deadline && !cancellation.IsCancellationRequested)
        {
            await Task.Delay(2000);
            using var response = await client.GetAsync("status");
            if (!response.IsSuccessStatusCode) continue;
            var status = await response.Content.ReadFromJsonAsync<StatusResponse>();
            if (status?.Authenticated == true) return;
        }
    }

    private static bool IsGameRunning()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(gameExecutable) && File.Exists(gameExecutable))
            {
                var expected = Path.GetFullPath(gameExecutable).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(path) && string.Equals(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), expected, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    catch { }
                    finally { process.Dispose(); }
                }
                return false;
            }
            var name = gameType.Equals("SspNightly", StringComparison.OrdinalIgnoreCase) ? "soundspaceplus" : gameType.Equals("Vulnus", StringComparison.OrdinalIgnoreCase) ? "Vulnus" : "rhythia";
            return Process.GetProcessesByName(name).Length > 0;
        }
        catch { return false; }
    }

    private static string? LoadManifestExecutable(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        try
        {
            var manifestPath = Path.Combine(directory, "RhythKit", "manifest.json");
            if (!File.Exists(manifestPath)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!document.RootElement.TryGetProperty("executable", out var executable)) return null;
            var value = executable.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
        }
        catch { return null; }
    }

    private static string DisplayGame(string value) => value switch
    {
        "RhythiaSteam" => "Rhythia Steam",
        "SspNightly" => "Sound Space Plus",
        "Vulnus" => "Vulnus",
        "RewriteRhythia" => "Rewrite Rhythia",
        _ => value
    };

    private static string? GetArgument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++) if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private sealed record StatusResponse(bool Installed, bool Running, bool GameRunning, bool LoggedIn, bool Connected, bool Authenticated, string? Username, string? UserId, string? Game, bool IntegrationConnected, bool MapCaptureReady, string? MapId, string? LastEvent, DateTimeOffset LastSeenAt);
    private sealed record AuthStartResponse(string DeviceCode, string UserCode, string VerificationUrl, int ExpiresIn, string? GameVersion);
}

using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace RhythKit.Agent;

internal static class AgentUiBootstrap
{
    private static string? gameDirectory;
    private static string gameType = "unknown";
    private static RhythKitSettingsForm? form;
    private static VulnusConverterForm? converterForm;
    private static int authorizationRunning;

    [ModuleInitializer]
    internal static void Initialize()
    {
        gameDirectory = GetArgument("--game-dir");
        gameType = GetArgument("--game-type") ?? "unknown";
        var port = AgentPorts.For(gameType);
        var uiThread = new Thread(() =>
        {
            form = new RhythKitSettingsForm(gameDirectory ?? string.Empty, gameType);
            form.ShowForGame();
            if (gameType.Equals("Vulnus", StringComparison.OrdinalIgnoreCase))
            {
                converterForm = new VulnusConverterForm();
                converterForm.Show();
            }
            Application.Run(form);
        });
        uiThread.IsBackground = true;
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        _ = Task.Run(() => WatchGameStartupAsync(port));
    }

    private static async Task WatchGameStartupAsync(int port)
    {
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(5) };
        for (;;)
        {
            try
            {
                if (IsGameRunning())
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
            }
            catch { }
            await Task.Delay(1000);
        }
    }

    private static async Task WaitForAuthorizationAsync(HttpClient client, int expiresIn)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn));
        while (DateTimeOffset.UtcNow < deadline)
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
        var names = gameType.Equals("RhythiaSteam", StringComparison.OrdinalIgnoreCase) || gameType.Equals("Rhythia", StringComparison.OrdinalIgnoreCase)
            ? new[] { "rhythia", "rhythia.exe" }
            : gameType.Equals("SspNightly", StringComparison.OrdinalIgnoreCase)
                ? new[] { "sound-space-plus", "sound-space-plus.exe", "Sound Space Plus", "Sound Space Plus.exe", "SSP", "SSP.exe" }
                : gameType.Equals("Vulnus", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "Vulnus", "Vulnus.exe" }
                    : Array.Empty<string>();
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (names.Any(name => string.Equals(process.ProcessName, Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase))) return true;
                    if (!string.IsNullOrWhiteSpace(gameDirectory))
                    {
                        try
                        {
                            var path = process.MainModule?.FileName;
                            if (!string.IsNullOrWhiteSpace(path) && PathsEqualOrChild(path, gameDirectory)) return true;
                        }
                        catch { }
                    }
                }
                finally { process.Dispose(); }
            }
        }
        catch { }
        return false;
    }

    private static bool PathsEqualOrChild(string filePath, string directory)
    {
        try
        {
            var file = Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(file, root, StringComparison.OrdinalIgnoreCase) || file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string? GetArgument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++) if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private sealed record StatusResponse(bool Installed, bool Running, bool GameRunning, bool LoggedIn, bool Connected, bool Authenticated, string? Username, string? Game, bool IntegrationConnected, bool MapCaptureReady, string? MapId, string? LastEvent, DateTimeOffset LastSeenAt);
    private sealed record AuthStartResponse(string DeviceCode, string UserCode, string VerificationUrl, int ExpiresIn, string? GameVersion);
}

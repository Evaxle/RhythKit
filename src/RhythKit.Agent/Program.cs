using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;

var gameDirectory = GetArgument("--game-dir");
var gameType = GetArgument("--game-type") ?? "unknown";
TokenStore.SaveGame(gameType);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:45872");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.SetIsOriginAllowed(origin => origin.StartsWith("https://rhythians.vercel.app", StringComparison.OrdinalIgnoreCase) || origin.StartsWith("https://rhythians.com", StringComparison.OrdinalIgnoreCase)).AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.UseCors();

app.MapGet("/status", () =>
{
    var state = TokenStore.Load();
    return Results.Json(new { installed = true, running = true, authenticated = !string.IsNullOrWhiteSpace(state?.Token), game = state?.Game ?? "unknown" });
});

app.MapPost("/game", async (HttpRequest request) =>
{
    var body = await JsonSerializer.DeserializeAsync<GameState>(request.Body);
    if (body == null || string.IsNullOrWhiteSpace(body.Game)) return Results.BadRequest();
    TokenStore.SaveGame(body.Game);
    return Results.Ok(new { ok = true });
});

app.MapPost("/score", async (HttpRequest request) =>
{
    var body = await JsonSerializer.DeserializeAsync<ScoreRequest>(request.Body);
    var state = TokenStore.Load();
    if (body == null || state?.Token == null) return Results.Unauthorized();
    using var client = new HttpClient { BaseAddress = new Uri("https://rhythians.vercel.app/") };
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", state.Token);
    var result = await client.PostAsJsonAsync("api/rhythkit/scores", body);
    return Results.Content(await result.Content.ReadAsStringAsync(), "application/json", System.Text.Encoding.UTF8, (int)result.StatusCode);
});

app.MapPost("/auth/start", async () => await StartAuthorizationAsync(gameType));

app.MapPost("/auth/poll", async (AuthPoll request) => await PollAuthorizationAsync(request.DeviceCode));

var watcher = new RhythKit.Agent.SspScoreWatcher(gameDirectory, app.Lifetime.ApplicationStopping);
if (gameType.Equals("SspNightly", StringComparison.OrdinalIgnoreCase)) _ = watcher.RunAsync();
_ = MonitorGameAsync(gameDirectory, gameType, app.Lifetime.ApplicationStopping);

app.Run();

static async Task<IResult> StartAuthorizationAsync(string gameType)
{
    using var client = new HttpClient { BaseAddress = new Uri("https://rhythians.vercel.app/") };
    var result = await client.PostAsJsonAsync("api/rhythkit/device/start", new { gameVersion = gameType });
    var json = await result.Content.ReadAsStringAsync();
    if (!result.IsSuccessStatusCode) return Results.Content(json, "application/json", System.Text.Encoding.UTF8, (int)result.StatusCode);
    return Results.Content(json, "application/json", System.Text.Encoding.UTF8, (int)result.StatusCode);
}

static async Task<IResult> PollAuthorizationAsync(string deviceCode)
{
    using var client = new HttpClient { BaseAddress = new Uri("https://rhythians.vercel.app/") };
    var result = await client.PostAsJsonAsync("api/rhythkit/device/poll", new { deviceCode });
    var json = await result.Content.ReadAsStringAsync();
    if (result.IsSuccessStatusCode)
    {
        var auth = JsonSerializer.Deserialize<AuthResponse>(json);
        if (auth?.Status == "authorized" && !string.IsNullOrWhiteSpace(auth.Token)) TokenStore.Save(auth.Token);
    }
    return Results.Content(json, "application/json", System.Text.Encoding.UTF8, (int)result.StatusCode);
}

static async Task MonitorGameAsync(string? gameDirectory, string gameType, CancellationToken cancellationToken)
{
    var wasRunning = false;
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            var running = IsGameRunning(gameDirectory, gameType);
            if (running && !wasRunning && !TokenStore.IsAuthenticated()) await BeginBrowserAuthorizationAsync(gameType, cancellationToken);
            wasRunning = running;
        }
        catch { }
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
    }
}

static bool IsGameRunning(string? gameDirectory, string gameType)
{
    var names = GetTargetProcessNames(gameDirectory, gameType);
    var directory = string.IsNullOrWhiteSpace(gameDirectory) ? null : Path.GetFullPath(gameDirectory);
    foreach (var name in names)
    {
        foreach (var process in Process.GetProcessesByName(name))
        {
            try
            {
                if (directory == null) return true;
                var processPath = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(processPath) && processPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            finally { process.Dispose(); }
        }
    }
    return false;
}

static string[] GetTargetProcessNames(string? gameDirectory, string gameType)
{
    if (!string.IsNullOrWhiteSpace(gameDirectory))
    {
        var bridge = Path.Combine(gameDirectory, "RhythKit", "RhythKitBridge.txt");
        if (File.Exists(bridge))
        {
            try
            {
                var lines = File.ReadAllLines(bridge);
                if (lines.Length > 1 && File.Exists(lines[1].Trim()))
                {
                    var name = Path.GetFileNameWithoutExtension(lines[1].Trim());
                    if (!string.IsNullOrWhiteSpace(name)) return new[] { name };
                }
            }
            catch { }
        }
    }
    return gameType switch
    {
        "RhythiaSteam" => new[] { "rhythia" },
        "LegacyManaged" => new[] { "rhythia" },
        "SspNightly" => new[] { "sound-space-plus", "Sound Space Plus", "SSP" },
        _ => Array.Empty<string>()
    };
}

static async Task BeginBrowserAuthorizationAsync(string gameType, CancellationToken cancellationToken)
{
    using var client = new HttpClient { BaseAddress = new Uri("https://rhythians.vercel.app/") };
    var response = await client.PostAsJsonAsync("api/rhythkit/device/start", new { gameVersion = gameType }, cancellationToken);
    if (!response.IsSuccessStatusCode) return;
    var auth = await response.Content.ReadFromJsonAsync<AuthStartResponse>(cancellationToken: cancellationToken);
    if (auth == null || string.IsNullOrWhiteSpace(auth.DeviceCode) || string.IsNullOrWhiteSpace(auth.VerificationUrl)) return;
    Process.Start(new ProcessStartInfo { FileName = auth.VerificationUrl, UseShellExecute = true });
    for (var i = 0; i < 300 && !cancellationToken.IsCancellationRequested; i++)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        var poll = await client.PostAsJsonAsync("api/rhythkit/device/poll", new { deviceCode = auth.DeviceCode }, cancellationToken);
        if (!poll.IsSuccessStatusCode) continue;
        var result = await poll.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: cancellationToken);
        if (result?.Status != "authorized" || string.IsNullOrWhiteSpace(result.Token)) continue;
        TokenStore.Save(result.Token);
        ShowConnectedPopup();
        return;
    }
}

static void ShowConnectedPopup()
{
    try { MessageBox.Show("Rhythians is connected to RhythKit.", "Rhythians Connected", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
}

static string? GetArgument(string name)
{
    var args = Environment.GetCommandLineArgs();
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    return null;
}

record GameState(string Game);
record ScoreRequest(string challengeMapId, string clientScoreId, double? accuracy, int? misses, double? speed);
record AuthPoll(string DeviceCode);
record AuthStartResponse(string DeviceCode, string UserCode, string VerificationUrl, int ExpiresIn, string? GameVersion);
record AuthResponse(string Status, string? Token, string? InstallationId);
record TokenState(string Token, string Game);

static class TokenStore
{
    static readonly string Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rhythians", "rhythkit.json");
    public static TokenState? Load()
    {
        try { return File.Exists(Path) ? JsonSerializer.Deserialize<TokenState>(File.ReadAllText(Path)) : null; } catch { return null; }
    }
    public static bool IsAuthenticated() => !string.IsNullOrWhiteSpace(Load()?.Token);
    public static void Save(string token)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var game = Load()?.Game ?? "unknown";
        File.WriteAllText(Path, JsonSerializer.Serialize(new TokenState(token, game)));
    }
    public static void SaveGame(string game)
    {
        var token = Load()?.Token ?? "";
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(new TokenState(token, game)));
    }
}

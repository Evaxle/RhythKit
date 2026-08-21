using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RhythKit.Agent;

var gameDirectory = GetArgument("--game-dir");
var gameType = GetArgument("--game-type") ?? "unknown";
TokenStore.SaveGame(gameType);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:45872");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.SetIsOriginAllowed(origin => origin.StartsWith("https://rhythians.vercel.app", StringComparison.OrdinalIgnoreCase) || origin.StartsWith("https://rhythians.com", StringComparison.OrdinalIgnoreCase)).AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.UseCors();
using var client = new HttpClient { BaseAddress = new Uri("https://rhythians.vercel.app"), Timeout = TimeSpan.FromSeconds(10) };
using var gameBridgeDispatcher = new GameBridgeDispatcher();
gameBridgeDispatcher.Start();
var authorizationRunning = 0;

app.MapGet("/status", async () =>
{
    var state = TokenStore.Load();
    var gameRunning = IsGameRunning(gameDirectory, gameType);
    var game = GameConnectionState.Get();
    if (string.IsNullOrWhiteSpace(state?.Token))
        return Results.Json(new { installed = true, running = true, gameRunning, loggedIn = false, connected = false, authenticated = false, username = (string?)null, game = state?.Game ?? gameType, integrationConnected = game.IntegrationConnected, mapCaptureReady = game.MapCaptureReady, mapId = game.MapId, lastEvent = game.LastEvent, lastSeenAt = game.LastSeenAt, gameVersion = game.GameVersion });
    var connection = await TestRemoteConnectionAsync(state.Token);
    var connected = gameRunning && connection.Authenticated;
    return Results.Json(new { installed = true, running = true, gameRunning, loggedIn = connection.Authenticated, connected, authenticated = connection.Authenticated, username = connection.Username, game = state.Game, integrationConnected = game.IntegrationConnected, mapCaptureReady = game.MapCaptureReady, mapId = game.MapId, lastEvent = game.LastEvent, lastSeenAt = game.LastSeenAt, gameVersion = game.GameVersion });
});

app.MapGet("/game/status", () => Results.Json(GameConnectionState.Get()));

app.MapPost("/game", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<GamePayload>();
    if (payload == null) return Results.BadRequest(new { error = "Invalid game state." });
    if (payload.Running) TokenStore.SaveGame(payload.Game ?? gameType);
    GameConnectionState.Update(new GameConnectionUpdate(payload.Running, payload.IntegrationConnected, payload.MapCaptureReady, payload.Game ?? gameType, payload.GameVersion, payload.MapId, payload.LastEvent));
    return Results.Ok(GameConnectionState.Get());
});

app.MapPost("/game/event", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<GameEventPayload>();
    if (payload == null || string.IsNullOrWhiteSpace(payload.Event)) return Results.BadRequest(new { error = "Invalid game event." });
    var current = GameConnectionState.Get();
    GameConnectionState.Update(new GameConnectionUpdate(
        payload.Running ?? current.Running,
        payload.IntegrationConnected ?? current.IntegrationConnected,
        payload.MapCaptureReady ?? current.MapCaptureReady,
        payload.Game ?? current.Game,
        payload.GameVersion ?? current.GameVersion,
        payload.MapId ?? current.MapId,
        payload.Event));
    return Results.Ok(GameConnectionState.Get());
});

app.MapPost("/score", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<ScorePayload>();
    if (payload == null || string.IsNullOrWhiteSpace(payload.ChallengeMapId) || string.IsNullOrWhiteSpace(payload.ClientScoreId))
        return Results.BadRequest(new { error = "challengeMapId and clientScoreId are required." });
    if (payload.Accuracy is < 0 or > 100 || payload.Misses < 0 || payload.Speed is <= 0)
        return Results.BadRequest(new { error = "Invalid score values." });
    if (!payload.ResultQualified) return Results.Conflict(new { error = "The game did not qualify this result." });
    if (!RankedMapStore.Contains(payload.ChallengeMapId)) return Results.NotFound(new { error = "The map is not installed as a Rhythians map." });

    var state = TokenStore.Load();
    if (string.IsNullOrWhiteSpace(state?.Token)) return Results.Unauthorized();

    using var outbound = new HttpRequestMessage(HttpMethod.Post, "/api/rhythkit/scores");
    outbound.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.Token);
    outbound.Content = JsonContent.Create(new
    {
        challengeMapId = payload.ChallengeMapId,
        clientScoreId = payload.ClientScoreId,
        accuracy = payload.Accuracy,
        misses = payload.Misses,
        speed = payload.Speed,
        gameVersion = payload.GameVersion,
        integrationVersion = payload.IntegrationVersion,
        completedAt = payload.CompletedAt ?? DateTimeOffset.UtcNow,
        resultQualified = payload.ResultQualified
    });

    try
    {
        using var response = await client.SendAsync(outbound);
        var body = await response.Content.ReadAsStringAsync();
        GameConnectionState.Update(new GameConnectionUpdate(true, true, true, gameType, payload.GameVersion, payload.ChallengeMapId, response.IsSuccessStatusCode ? "ScoreSubmitted" : "ScoreRejected"));
        return Results.Content(body, response.Content.Headers.ContentType?.ToString() ?? "application/json", System.Text.Encoding.UTF8, response.StatusCode);
    }
    catch
    {
        GameConnectionState.Update(new GameConnectionUpdate(true, true, true, gameType, payload.GameVersion, payload.ChallengeMapId, "ScoreSubmissionFailed"));
        return Results.Problem("Rhythians could not be reached.", statusCode: 502);
    }
});

app.MapPost("/auth/start", async () =>
{
    if (Interlocked.Exchange(ref authorizationRunning, 1) == 1) return Results.Conflict(new { error = "Authorization is already running." });
    try
    {
        var authorization = await StartDeviceAuthorizationAsync();
        _ = Task.Run(async () =>
        {
            try { await CompleteAuthorizationAsync(authorization); }
            finally { Interlocked.Exchange(ref authorizationRunning, 0); }
        });
        return Results.Json(authorization);
    }
    catch (Exception exception)
    {
        Interlocked.Exchange(ref authorizationRunning, 0);
        return Results.Problem(exception.Message, statusCode: 502);
    }
});

app.MapPost("/login", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<LoginPayload>();
    if (string.IsNullOrWhiteSpace(payload?.Token)) return Results.BadRequest(new { error = "Token is required." });
    var username = payload.Username;
    if (string.IsNullOrWhiteSpace(username))
    {
        var connection = await TestRemoteConnectionAsync(payload.Token);
        if (!connection.Authenticated) return Results.Unauthorized();
        username = connection.Username;
    }
    TokenStore.Save(payload.Token, username, payload.Game ?? gameType);
    return Results.Ok(new { loggedIn = true, username, game = payload.Game ?? gameType });
});

app.MapPost("/logout", () =>
{
    TokenStore.Clear();
    return Results.Ok(new { loggedIn = false });
});

app.MapGet("/settings", () => Results.Json(TokenStore.LoadSettings()));
app.MapPost("/settings", async (HttpRequest request) =>
{
    var settings = await request.ReadFromJsonAsync<AgentSettings>() ?? new AgentSettings();
    TokenStore.SaveSettings(settings);
    return Results.Ok(settings);
});

app.MapPost("/exit", () =>
{
    _ = Task.Run(() =>
    {
        Thread.Sleep(100);
        Environment.Exit(0);
    });
    return Results.Ok();
});

var form = new RhythKitSettingsForm(gameDirectory ?? string.Empty, gameType);
_ = app.RunAsync();
_ = Task.Run(async () =>
{
    var seenGame = false;
    while (true)
    {
        var running = IsGameRunning(gameDirectory, gameType);
        if (running)
        {
            if (!seenGame)
            {
                seenGame = true;
                form.ShowForGame();
                OpenRhythians();
            }
            if (string.IsNullOrWhiteSpace(TokenStore.Load()?.Token)) _ = EnsureAuthorizationAsync();
        }
        else if (seenGame)
        {
            form.CloseFromGameExit();
            GameConnectionState.Update(new GameConnectionUpdate(false, false, false, gameType, null, null, "GameExited"));
            await Task.Delay(250);
            Environment.Exit(0);
            return;
        }
        await Task.Delay(2000);
    }
});

Application.Run(form);

async Task EnsureAuthorizationAsync()
{
    if (!string.IsNullOrWhiteSpace(TokenStore.Load()?.Token)) return;
    if (Interlocked.Exchange(ref authorizationRunning, 1) == 1) return;
    try
    {
        var authorization = await StartDeviceAuthorizationAsync();
        Process.Start(new ProcessStartInfo { FileName = authorization.VerificationUrl, UseShellExecute = true });
        await CompleteAuthorizationAsync(authorization);
    }
    catch { }
    finally { Interlocked.Exchange(ref authorizationRunning, 0); }
}

async Task<DeviceAuthorization> StartDeviceAuthorizationAsync()
{
    using var response = await client.PostAsJsonAsync("/api/rhythkit/device/start", new { gameVersion = GetGameVersion() });
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<DeviceAuthorization>() ?? throw new InvalidOperationException("Invalid authorization response.");
}

async Task CompleteAuthorizationAsync(DeviceAuthorization authorization)
{
    var deadline = DateTime.UtcNow.AddSeconds(authorization.ExpiresIn);
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            using var response = await client.PostAsJsonAsync("/api/rhythkit/device/poll", new { deviceCode = authorization.DeviceCode });
            if ((int)response.StatusCode == 404 || (int)response.StatusCode == 410) return;
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<DeviceTokenResponse>();
            if (result?.Status == "authorized" && !string.IsNullOrWhiteSpace(result.Token))
            {
                var connection = await TestRemoteConnectionAsync(result.Token);
                if (connection.Authenticated)
                {
                    TokenStore.Save(result.Token, connection.Username, gameType);
                    return;
                }
            }
        }
        catch { }
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
}

async Task<RemoteConnection> TestRemoteConnectionAsync(string token)
{
    try
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/rhythkit/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode) return new RemoteConnection(false, null);
        var result = await response.Content.ReadFromJsonAsync<RhythiansConnectionResponse>();
        return new RemoteConnection(result?.Authenticated == true, result?.Username);
    }
    catch
    {
        return new RemoteConnection(false, null);
    }
}

string GetGameVersion()
{
    var version = GameConnectionState.Get().GameVersion;
    return string.IsNullOrWhiteSpace(version) ? "unknown" : version;
}

void OpenRhythians()
{
    try
    {
        var state = TokenStore.Load();
        var url = string.IsNullOrWhiteSpace(state?.Token) ? "https://rhythians.vercel.app" : "https://rhythians.vercel.app/settings";
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
    catch { }
}

string? GetArgument(string name)
{
    var args = Environment.GetCommandLineArgs();
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    return null;
}

static bool IsGameRunning(string? gameDirectory, string gameType)
{
    var names = gameType.Equals("RhythiaSteam", StringComparison.OrdinalIgnoreCase) || gameType.Equals("Rhythia", StringComparison.OrdinalIgnoreCase)
        ? new[] { "Rhythia", "Rhythia.exe" }
        : gameType.Equals("SspNightly", StringComparison.OrdinalIgnoreCase)
            ? new[] { "sound-space-plus", "sound-space-plus.exe", "Sound Space Plus", "Sound Space Plus.exe", "SSP", "SSP.exe" }
            : new[] { "osu!", "osu", "osu!.exe", "osu.exe" };
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
                        if (!string.IsNullOrWhiteSpace(path) && path.StartsWith(gameDirectory, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    catch { }
                }
            }
            finally
            {
                process.Dispose();
            }
        }
    }
    catch { }
    return false;
}

record RemoteConnection(bool Authenticated, string? Username);
record LoginPayload(string? Token, string? Username, string? Game);
record GamePayload(bool Running, bool IntegrationConnected, bool MapCaptureReady, string? Game, string? GameVersion, string? MapId, string? LastEvent);
record GameEventPayload(string Event, bool? Running, bool? IntegrationConnected, bool? MapCaptureReady, string? Game, string? GameVersion, string? MapId);
record ScorePayload(string ChallengeMapId, string ClientScoreId, double Accuracy, int Misses, double? Speed, string? GameVersion, string? IntegrationVersion, DateTimeOffset? CompletedAt, bool ResultQualified);
record DeviceAuthorization(
    [property: JsonPropertyName("deviceCode")] string DeviceCode,
    [property: JsonPropertyName("userCode")] string UserCode,
    [property: JsonPropertyName("verificationUrl")] string VerificationUrl,
    [property: JsonPropertyName("expiresIn")] int ExpiresIn);
record DeviceTokenResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("installationId")] string? InstallationId);
record RhythiansConnectionResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("authenticated")] bool Authenticated,
    [property: JsonPropertyName("username")] string? Username);

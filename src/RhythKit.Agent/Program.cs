using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using RhythKit.Agent;

var gameDirectory = GetArgument("--game-dir");
var gameType = GetArgument("--game-type") ?? "unknown";
TokenStore.SaveGame(gameType);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, AgentPorts.For(gameType)));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.SetIsOriginAllowed(origin => origin.StartsWith("https://rhythians.vercel.app", StringComparison.OrdinalIgnoreCase) || origin.StartsWith("https://rhythians.com", StringComparison.OrdinalIgnoreCase)).AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.UseCors();
using var client = new HttpClient { BaseAddress = new Uri("https://rhythians.vercel.app"), Timeout = TimeSpan.FromSeconds(10) };
var authorizationRunning = 0;

app.MapGet("/status", async () =>
{
    var state = TokenStore.Load();
    var gameRunning = IsGameRunning(gameDirectory, gameType);
    var game = GameConnectionState.Get();
    if (string.IsNullOrWhiteSpace(state?.Token))
        return Results.Json(new { installed = true, running = true, gameRunning, loggedIn = false, connected = false, authenticated = false, username = (string?)null, game = gameType, integrationConnected = game.IntegrationConnected, mapCaptureReady = game.MapCaptureReady, mapId = game.MapId, lastEvent = game.LastEvent, lastSeenAt = game.LastSeenAt, gameVersion = game.GameVersion });
    var connection = await TestRemoteConnectionAsync(state.Token);
    return Results.Json(new { installed = true, running = true, gameRunning, loggedIn = connection.Authenticated, connected = connection.Authenticated, authenticated = connection.Authenticated, username = connection.Username, game = gameType, integrationConnected = game.IntegrationConnected, mapCaptureReady = game.MapCaptureReady, mapId = game.MapId, lastEvent = game.LastEvent, lastSeenAt = game.LastSeenAt, gameVersion = game.GameVersion });
});

app.MapGet("/game/status", () => Results.Json(GameConnectionState.Get()));

app.MapPost("/game", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<GamePayload>();
    if (payload == null) return Results.BadRequest(new { error = "Invalid game state." });
    GameConnectionState.Update(new GameConnectionUpdate(payload.Running, payload.IntegrationConnected, payload.MapCaptureReady, payload.Game ?? gameType, payload.GameVersion, payload.MapId, payload.LastEvent));
    return Results.Ok(GameConnectionState.Get());
});

app.MapPost("/game/event", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<GameEventPayload>();
    if (payload == null || string.IsNullOrWhiteSpace(payload.Event)) return Results.BadRequest(new { error = "Invalid game event." });
    var current = GameConnectionState.Get();
    var running = payload.Running ?? current.Running;
    var integrationConnected = payload.IntegrationConnected ?? current.IntegrationConnected;
    var mapCaptureReady = payload.MapCaptureReady ?? (!string.IsNullOrWhiteSpace(payload.MapId) || current.MapCaptureReady);
    var game = payload.Game ?? current.Game ?? gameType;
    var version = payload.GameVersion ?? current.GameVersion;
    var mapId = payload.MapId ?? current.MapId;
    GameConnectionState.Update(new GameConnectionUpdate(running, integrationConnected, mapCaptureReady, game, version, mapId, payload.Event));
    if (!string.Equals(payload.Event, "MapCompleted", StringComparison.OrdinalIgnoreCase)) return Results.Ok(GameConnectionState.Get());
    if (!payload.ResultQualified || payload.Passed != true || string.IsNullOrWhiteSpace(payload.MapId) || string.IsNullOrWhiteSpace(payload.ResultId)) return Results.Conflict(new { error = "The game result did not qualify." });
    if (payload.Accuracy is null or < 0 or > 100 || payload.Misses is null or < 0 || payload.Speed is <= 0) return Results.BadRequest(new { error = "Invalid score values." });
    if (!gameType.Equals("Vulnus", StringComparison.OrdinalIgnoreCase) && !RankedMapStore.Contains(payload.MapId!)) return Results.NotFound(new { error = "The map is not installed as a Rhythians map." });
    var state = TokenStore.Load();
    if (string.IsNullOrWhiteSpace(state?.Token)) return Results.Unauthorized();
    var result = await SubmitRemoteScoreAsync(state.Token, new ScorePayload(payload.MapId!, payload.ResultId!, payload.Accuracy.Value, payload.Misses.Value, payload.Speed, version, payload.IntegrationVersion, payload.CompletedAt, true));
    GameConnectionState.Update(new GameConnectionUpdate(true, true, true, game, version, payload.MapId, result.Success ? (result.AlreadyCompleted ? "CompletionAlreadyRecorded" : "ScoreSubmitted") : "ScoreRejected"));
    return Results.Content(result.Body, "application/json", System.Text.Encoding.UTF8, result.StatusCode);
});

app.MapPost("/score", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<ScorePayload>();
    if (payload == null || string.IsNullOrWhiteSpace(payload.ChallengeMapId) || string.IsNullOrWhiteSpace(payload.ClientScoreId)) return Results.BadRequest(new { error = "challengeMapId and clientScoreId are required." });
    if (payload.Accuracy is < 0 or > 100 || payload.Misses < 0 || payload.Speed is <= 0) return Results.BadRequest(new { error = "Invalid score values." });
    if (!payload.ResultQualified) return Results.Conflict(new { error = "The game did not qualify this result." });
    if (!gameType.Equals("Vulnus", StringComparison.OrdinalIgnoreCase) && !RankedMapStore.Contains(payload.ChallengeMapId)) return Results.NotFound(new { error = "The map is not installed as a Rhythians map." });
    var state = TokenStore.Load();
    if (string.IsNullOrWhiteSpace(state?.Token)) return Results.Unauthorized();
    var result = await SubmitRemoteScoreAsync(state.Token, payload);
    return Results.Content(result.Body, "application/json", System.Text.Encoding.UTF8, result.StatusCode);
});

app.MapPost("/vulnus/convert", async (HttpRequest request) =>
{
    if (!gameType.Equals("Vulnus", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "This agent is not installed for Vulnus." });
    if (string.IsNullOrWhiteSpace(gameDirectory)) return Results.BadRequest(new { error = "Vulnus installation directory is not configured." });
    if (!request.HasFormContentType) return Results.BadRequest(new { error = "An SSPM file upload is required." });
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
    if (file == null || file.Length == 0) return Results.BadRequest(new { error = "An SSPM file upload is required." });
    if (!string.Equals(Path.GetExtension(file.FileName), ".sspm", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "Only .sspm files can be converted." });
    var temp = Path.Combine(Path.GetTempPath(), $"rhythkit-{Guid.NewGuid():N}.sspm");
    try
    {
        await using (var stream = File.Create(temp)) await file.CopyToAsync(stream);
        var result = await VulnusConverterLauncher.ConvertAsync(gameDirectory, temp);
        return result.Success ? Results.Ok(new { ok = true, message = result.Message, path = result.OutputPath }) : Results.BadRequest(new { ok = false, error = result.Message });
    }
    catch (Exception exception)
    {
        return Results.Problem(exception.Message, statusCode: 500);
    }
    finally
    {
        try { if (File.Exists(temp)) File.Delete(temp); } catch { }
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
    if (payload == null || string.IsNullOrWhiteSpace(payload.Token)) return Results.BadRequest(new { error = "token is required." });
    TokenStore.Save(payload.Token, payload.Username, payload.Game ?? gameType);
    return Results.Ok(new { ok = true });
});

app.MapPost("/logout", () =>
{
    TokenStore.Clear();
    return Results.Ok(new { ok = true });
});

app.MapPost("/open", () =>
{
    OpenRhythians();
    return Results.Ok(new { ok = true });
});

app.MapPost("/shutdown", () =>
{
    Environment.Exit(0);
    return Results.Ok(new { ok = true });
});

using var dispatcher = new GameBridgeDispatcher();
dispatcher.Start();
await app.RunAsync();

async Task<DeviceAuthorization> StartDeviceAuthorizationAsync()
{
    using var response = await client.PostAsJsonAsync("/api/rhythkit/device/start", new { gameVersion = gameType });
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<DeviceAuthorization>() ?? throw new InvalidOperationException("Invalid device authorization response.");
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
    catch { return new RemoteConnection(false, null); }
}

async Task<RemoteScoreResult> SubmitRemoteScoreAsync(string token, ScorePayload payload)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rhythkit/scores");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    request.Content = JsonContent.Create(new { challengeMapId = payload.ChallengeMapId, clientScoreId = payload.ClientScoreId, accuracy = payload.Accuracy, misses = payload.Misses, speed = payload.Speed, gameVersion = payload.GameVersion, integrationVersion = payload.IntegrationVersion, completedAt = payload.CompletedAt ?? DateTimeOffset.UtcNow, resultQualified = payload.ResultQualified });
    try
    {
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        var result = System.Text.Json.JsonSerializer.Deserialize<ScoreResultBody>(body);
        return new RemoteScoreResult(response.IsSuccessStatusCode, (int)response.StatusCode, body, result?.AlreadyCompleted == true);
    }
    catch { return new RemoteScoreResult(false, 502, "{\"error\":\"Rhythians could not be reached.\"}", false); }
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
    for (var i = 0; i < args.Length - 1; i++) if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    return null;
}

static bool IsGameRunning(string? gameDirectory, string gameType)
{
    var names = gameType.Equals("RhythiaSteam", StringComparison.OrdinalIgnoreCase) || gameType.Equals("Rhythia", StringComparison.OrdinalIgnoreCase) ? new[] { "rhythia", "rhythia.exe" } : gameType.Equals("SspNightly", StringComparison.OrdinalIgnoreCase) ? new[] { "sound-space-plus", "sound-space-plus.exe", "Sound Space Plus", "Sound Space Plus.exe", "SSP", "SSP.exe" } : gameType.Equals("Vulnus", StringComparison.OrdinalIgnoreCase) ? new[] { "Vulnus", "Vulnus.exe" } : Array.Empty<string>();
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

static bool PathsEqualOrChild(string filePath, string directory)
{
    try
    {
        var file = Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(file, root, StringComparison.OrdinalIgnoreCase) || file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    catch { return false; }
}

record RemoteConnection(bool Authenticated, string? Username);
record RemoteScoreResult(bool Success, int? StatusCode, string Body, bool AlreadyCompleted);
record ScoreResultBody([property: JsonPropertyName("alreadyCompleted")] bool AlreadyCompleted);
record LoginPayload(string? Token, string? Username, string? Game);
record GamePayload(bool Running, bool IntegrationConnected, bool MapCaptureReady, string? Game, string? GameVersion, string? MapId, string? LastEvent);
record GameEventPayload(string Event, bool? Running, bool? IntegrationConnected, bool? MapCaptureReady, string? Game, string? GameVersion, string? IntegrationVersion, string? MapId, string? ResultId, double? Accuracy, int? Misses, double? Speed, bool? Passed, bool ResultQualified, DateTimeOffset? CompletedAt);
record ScorePayload(string ChallengeMapId, string ClientScoreId, double Accuracy, int Misses, double? Speed, string? GameVersion, string? IntegrationVersion, DateTimeOffset? CompletedAt, bool ResultQualified);
record DeviceAuthorization([property: JsonPropertyName("deviceCode")] string DeviceCode, [property: JsonPropertyName("userCode")] string UserCode, [property: JsonPropertyName("verificationUrl")] string VerificationUrl, [property: JsonPropertyName("expiresIn")] int ExpiresIn);
record DeviceTokenResponse([property: JsonPropertyName("status")] string Status, [property: JsonPropertyName("token")] string? Token, [property: JsonPropertyName("installationId")] string? InstallationId);
record RhythiansConnectionResponse([property: JsonPropertyName("ok")] bool Ok, [property: JsonPropertyName("authenticated")] bool Authenticated, [property: JsonPropertyName("username")] string? Username);
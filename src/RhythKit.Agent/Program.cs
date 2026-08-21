using System.Diagnostics;
using System.Net;
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
var agentSettings = TokenStore.LoadSettings();
agentSettings.GameDirectory = gameDirectory;
agentSettings.GameType = gameType;
TokenStore.SaveSettings(agentSettings);
TokenStore.SaveGame(gameType);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, AgentPorts.For(gameType)));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.UseCors();
using var client = new HttpClient { BaseAddress = NormalizeBaseUri(agentSettings.RhythiansServerUrl), Timeout = TimeSpan.FromSeconds(10) };
var authorizationRunning = 0;

app.MapGet("/status", async () =>
{
    var state = TokenStore.Load();
    var gameRunning = IsGameRunning(gameDirectory, gameType);
    var game = GameConnectionState.Get();
    if (string.IsNullOrWhiteSpace(state?.Token)) return Results.Json(new { installed = true, running = true, gameRunning, loggedIn = false, connected = false, authenticated = false, username = (string?)null, game = gameType, integrationConnected = game.IntegrationConnected, mapCaptureReady = game.MapCaptureReady, mapId = game.MapId, lastEvent = game.LastEvent, lastSeenAt = game.LastSeenAt, gameVersion = game.GameVersion, rhythiansServerUrl = agentSettings.RhythiansServerUrl });
    var connection = await TestRemoteConnectionAsync(state.Token);
    return Results.Json(new { installed = true, running = true, gameRunning, loggedIn = connection.Authenticated, connected = connection.Authenticated, authenticated = connection.Authenticated, username = connection.Username, game = gameType, integrationConnected = game.IntegrationConnected, mapCaptureReady = game.MapCaptureReady, mapId = game.MapId, lastEvent = game.LastEvent, lastSeenAt = game.LastSeenAt, gameVersion = game.GameVersion, rhythiansServerUrl = agentSettings.RhythiansServerUrl });
});

app.MapGet("/game/status", () => Results.Json(GameConnectionState.Get()));
app.MapGet("/debug/events", () => Results.Json(GameConnectionState.GetHistory()));
app.MapDelete("/debug/events", () => { GameConnectionState.ClearHistory(); return Results.Ok(new { ok = true }); });

app.MapPost("/settings/server-url", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<ServerUrlPayload>();
    if (payload == null || !Uri.TryCreate(payload.Url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return Results.BadRequest(new { error = "A valid HTTP or HTTPS server URL is required." });
    agentSettings.RhythiansServerUrl = uri.ToString().TrimEnd('/');
    TokenStore.SaveSettings(agentSettings);
    GameConnectionState.AddDebug("ServerUrlChanged", $"Rhythians server URL changed to {agentSettings.RhythiansServerUrl}.");
    return Results.Ok(new { ok = true, url = agentSettings.RhythiansServerUrl });
});

app.MapGet("/settings", () => Results.Json(new { serverUrl = agentSettings.RhythiansServerUrl, game = gameType, gameDirectory }));

app.MapPost("/test-connection", async () =>
{
    var state = TokenStore.Load();
    var gameRunning = IsGameRunning(gameDirectory, gameType);
    GameConnectionState.AddDebug("ConnectionTestStarted", $"Testing {gameType} connection.", data: new { gameRunning, agentPort = AgentPorts.For(gameType), serverUrl = agentSettings.RhythiansServerUrl });
    if (string.IsNullOrWhiteSpace(state?.Token))
    {
        GameConnectionState.AddDebug("AuthenticationRequired", "RhythKit is installed and reachable, but no Rhythians login is linked.");
        return Results.Json(new { ok = false, installed = true, running = true, gameRunning, authenticated = false, integrationConnected = GameConnectionState.Get().IntegrationConnected, message = "Rhythians login is required." }, statusCode: 401);
    }
    var remote = await TestRemoteConnectionAsync(state.Token);
    GameConnectionState.AddDebug(remote.Authenticated ? "ConnectionTestPassed" : "ConnectionTestFailed", remote.Authenticated ? "RhythKit authenticated with Rhythians successfully." : "Rhythians rejected the current RhythKit token.", data: new { gameRunning, authenticated = remote.Authenticated, username = remote.Username, serverUrl = agentSettings.RhythiansServerUrl });
    return Results.Json(new { ok = remote.Authenticated, installed = true, running = true, gameRunning, authenticated = remote.Authenticated, integrationConnected = GameConnectionState.Get().IntegrationConnected, username = remote.Username, message = remote.Authenticated ? "Connection test passed." : "Connection test failed.", serverUrl = agentSettings.RhythiansServerUrl });
});

app.MapPost("/game", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<GamePayload>();
    if (payload == null) return Results.BadRequest(new { error = "Invalid game state." });
    GameConnectionState.Update(new GameConnectionUpdate(payload.Running, payload.IntegrationConnected, payload.MapCaptureReady, payload.Game ?? gameType, payload.GameVersion, payload.MapId, payload.LastEvent));
    GameConnectionState.AddDebug(payload.LastEvent ?? "GameState", "Game state updated.", payload.MapId, new { payload.Running, payload.IntegrationConnected, payload.MapCaptureReady });
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
    GameConnectionState.AddDebug(payload.Event, DescribeEvent(payload), payload.MapId, new { payload.ResultId, payload.Accuracy, payload.Misses, payload.Speed, payload.Passed, payload.ResultQualified, payload.CompletedAt });
    if (!string.Equals(payload.Event, "MapCompleted", StringComparison.OrdinalIgnoreCase)) return Results.Ok(GameConnectionState.Get());
    if (!payload.ResultQualified || payload.Passed != true || string.IsNullOrWhiteSpace(payload.MapId) || string.IsNullOrWhiteSpace(payload.ResultId)) return Results.Conflict(new { error = "The game result did not qualify." });
    if (payload.Accuracy is null or < 0 or > 100 || payload.Misses is null or < 0 || payload.Speed is <= 0) return Results.BadRequest(new { error = "Invalid score values." });
    if (!gameType.Equals("Vulnus", StringComparison.OrdinalIgnoreCase) && !RankedMapStore.Contains(payload.MapId!)) return Results.NotFound(new { error = "The map is not installed as a Rhythians map." });
    GameConnectionState.AddDebug("RankedMapCheck", "Map accepted for score submission.", payload.MapId);
    var state = TokenStore.Load();
    if (string.IsNullOrWhiteSpace(state?.Token)) return Results.Unauthorized();
    var result = await SubmitRemoteScoreAsync(state.Token, new ScorePayload(payload.MapId!, payload.ResultId!, payload.Accuracy.Value, payload.Misses.Value, payload.Speed, version, payload.IntegrationVersion, payload.CompletedAt, true));
    GameConnectionState.AddDebug(result.Success ? "ScoreSubmitted" : "ScoreRejected", result.Success ? "Completion submitted to Rhythians." : "Rhythians rejected the completion.", payload.MapId, new { result.StatusCode, result.AlreadyCompleted, result.Body });
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
    GameConnectionState.AddDebug(result.Success ? "ScoreSubmitted" : "ScoreRejected", result.Success ? "Direct score submission completed." : "Direct score submission failed.", payload.ChallengeMapId, new { result.StatusCode, result.AlreadyCompleted });
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
    GameConnectionState.AddDebug("MapImportStarted", $"Importing SSPM {file.FileName}.", data: new { fileName = file.FileName, fileLength = file.Length });
    var temp = Path.Combine(Path.GetTempPath(), $"rhythkit-{Guid.NewGuid():N}.sspm");
    try
    {
        await using (var stream = File.Create(temp)) await file.CopyToAsync(stream);
        var result = await VulnusConverterLauncher.ConvertAsync(gameDirectory, temp);
        GameConnectionState.AddDebug(result.Success ? "MapImportCompleted" : "MapImportFailed", result.Message, data: new { fileName = file.FileName, result.OutputPath });
        return result.Success ? Results.Ok(new { ok = true, message = result.Message, path = result.OutputPath }) : Results.BadRequest(new { ok = false, error = result.Message });
    }
    catch (Exception exception)
    {
        GameConnectionState.AddDebug("MapImportFailed", exception.Message, data: new { fileName = file.FileName });
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
        GameConnectionState.AddDebug("AuthorizationStarted", "Rhythians authorization flow started.");
        _ = Task.Run(async () =>
        {
            try { await CompleteAuthorizationAsync(authorization); }
            finally { Interlocked.Exchange(ref authorizationRunning, 0); }
        });
        return Results.Json(authorization);
    }
    catch (Exception exception)
    {
        GameConnectionState.AddDebug("AuthorizationFailed", exception.Message);
        Interlocked.Exchange(ref authorizationRunning, 0);
        return Results.Problem(exception.Message, statusCode: 502);
    }
});

app.MapPost("/login", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<LoginPayload>();
    if (payload == null || string.IsNullOrWhiteSpace(payload.Token)) return Results.BadRequest(new { error = "token is required." });
    TokenStore.Save(payload.Token, payload.Username, payload.Game ?? gameType);
    GameConnectionState.AddDebug("Login", $"Rhythians account linked{(string.IsNullOrWhiteSpace(payload.Username) ? "" : $" as {payload.Username")}." );
    return Results.Ok(new { ok = true });
});

app.MapPost("/logout", () => { TokenStore.Clear(); GameConnectionState.AddDebug("Logout", "Rhythians account disconnected."); return Results.Ok(new { ok = true }); });
app.MapPost("/open", () => { OpenRhythians(); return Results.Ok(new { ok = true }); });
app.MapPost("/shutdown", () => { Environment.Exit(0); return Results.Ok(new { ok = true }); });

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
                    GameConnectionState.AddDebug("AuthorizationCompleted", $"Rhythians authorization completed{(string.IsNullOrWhiteSpace(connection.Username) ? "" : $" as {connection.Username}")}.");
                    return;
                }
            }
        }
        catch { }
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
    GameConnectionState.AddDebug("AuthorizationExpired", "Rhythians authorization expired before completion.");
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

static Uri NormalizeBaseUri(string? value)
{
    if (!Uri.TryCreate(string.IsNullOrWhiteSpace(value) ? "https://rhythians.vercel.app" : value.Trim(), UriKind.Absolute, out var uri)) return new Uri("https://rhythians.vercel.app");
    return new Uri(uri.ToString().TrimEnd('/') + "/");
}

static string DescribeEvent(GameEventPayload payload) => payload.Event switch
{
    "MapSelected" => $"Map selected{(string.IsNullOrWhiteSpace(payload.MapId) ? "" : $" ({payload.MapId})")}",
    "MapImported" => $"Map imported{(string.IsNullOrWhiteSpace(payload.MapId) ? "" : $" ({payload.MapId})")}",
    "MapCompleted" => $"Map completed{(string.IsNullOrWhiteSpace(payload.MapId) ? "" : $" ({payload.MapId})")}",
    "RankedMapCheck" => $"Checking ranked status{(string.IsNullOrWhiteSpace(payload.MapId) ? "" : $" for {payload.MapId}")}",
    _ => "Game integration event received."
};

static bool IsGameRunning(string? gameDirectory, string gameType)
{
    var names = gameType.Equals("RhythiaSteam", StringComparison.OrdinalIgnoreCase) || gameType.Equals("Rhythia", StringComparison.OrdinalIgnoreCase) || gameType.Equals("RewriteRhythia", StringComparison.OrdinalIgnoreCase) ? new[] { "rhythia", "rhythia.exe" } : gameType.Equals("SspNightly", StringComparison.OrdinalIgnoreCase) ? new[] { "sound-space-plus", "sound-space-plus.exe", "Sound Space Plus", "Sound Space Plus.exe", "SSP", "SSP.exe", "soundspaceplus", "soundspaceplus.exe" } : gameType.Equals("Vulnus", StringComparison.OrdinalIgnoreCase) ? new[] { "Vulnus", "Vulnus.exe" } : Array.Empty<string>();
    try
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (names.Any(name => string.Equals(process.ProcessName, Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase))) return true;
                if (!string.IsNullOrWhiteSpace(gameDirectory))
                {
                    try { var path = process.MainModule?.FileName; if (!string.IsNullOrWhiteSpace(path) && PathsEqualOrChild(path, gameDirectory)) return true; } catch { }
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
record ServerUrlPayload(string? Url);
record GamePayload(bool Running, bool IntegrationConnected, bool MapCaptureReady, string? Game, string? GameVersion, string? MapId, string? LastEvent);
record GameEventPayload(string Event, bool? Running, bool? IntegrationConnected, bool? MapCaptureReady, string? Game, string? GameVersion, string? IntegrationVersion, string? MapId, string? ResultId, double? Accuracy, int? Misses, double? Speed, bool? Passed, bool ResultQualified, DateTimeOffset? CompletedAt);
record ScorePayload(string ChallengeMapId, string ClientScoreId, double Accuracy, int Misses, double? Speed, string? GameVersion, string? IntegrationVersion, DateTimeOffset? CompletedAt, bool ResultQualified);
record DeviceAuthorization([property: JsonPropertyName("deviceCode")] string DeviceCode, [property: JsonPropertyName("userCode")] string UserCode, [property: JsonPropertyName("verificationUrl")] string VerificationUrl, [property: JsonPropertyName("expiresIn")] int ExpiresIn);
record DeviceTokenResponse([property: JsonPropertyName("status")] string Status, [property: JsonPropertyName("token")] string? Token, [property: JsonPropertyName("installationId")] string? InstallationId);
record RhythiansConnectionResponse([property: JsonPropertyName("ok")] bool Ok, [property: JsonPropertyName("authenticated")] bool Authenticated, [property: JsonPropertyName("username")] string? Username);
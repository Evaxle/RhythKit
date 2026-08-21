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
var authorizationRunning = 0;

app.MapGet("/status", async () =>
{
    var state = TokenStore.Load();
    var gameRunning = IsGameRunning(gameDirectory, gameType);
    if (string.IsNullOrWhiteSpace(state?.Token)) return Results.Json(new { installed = true, running = true, gameRunning, loggedIn = false, connected = false, authenticated = false, username = (string?)null, game = state?.Game ?? gameType });
    var connection = await TestRemoteConnectionAsync(state.Token);
    var connected = gameRunning && connection.Authenticated;
    return Results.Json(new { installed = true, running = true, gameRunning, loggedIn = connection.Authenticated, connected, authenticated = connection.Authenticated, username = connection.Username, game = state.Game });
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

app.MapPost("/game", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<GamePayload>();
    if (payload?.Running == true) TokenStore.SaveGame(payload.Game ?? gameType);
    return Results.Ok();
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
            }
            if (string.IsNullOrWhiteSpace(TokenStore.Load()?.Token)) _ = EnsureAuthorizationAsync();
        }
        else if (seenGame)
        {
            form.CloseFromGameExit();
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
    using var response = await client.PostAsJsonAsync("/api/rhythkit/device/start", new { gameVersion = "0.1.0" });
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
record GamePayload(bool Running, string? Game);
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

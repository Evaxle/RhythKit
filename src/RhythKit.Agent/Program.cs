using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
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

app.MapGet("/status", async () =>
{
    var state = TokenStore.Load();
    var gameRunning = IsGameRunning(gameDirectory, gameType);
    if (string.IsNullOrWhiteSpace(state?.Token)) return Results.Json(new { installed = true, running = true, gameRunning, loggedIn = false, connected = false, authenticated = false, username = (string?)null, game = state?.Game ?? gameType });
    var connection = await TestRemoteConnectionAsync(state.Token);
    var connected = gameRunning && connection.Authenticated;
    return Results.Json(new { installed = true, running = true, gameRunning, loggedIn = connection.Authenticated, connected, authenticated = connection.Authenticated, username = connection.Username, game = state.Game });
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

_ = Task.Run(() => Application.Run(new RhythKitSettingsForm()));

await app.RunAsync();

string? GetArgument(string name)
{
    var args = Environment.GetCommandLineArgs();
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    return null;
}

static bool IsGameRunning(string? gameDirectory, string gameType)
{
    var names = gameType.Equals("Rhythia", StringComparison.OrdinalIgnoreCase)
        ? new[] { "Rhythia", "Rhythia.exe" }
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

static async Task<RemoteConnection> TestRemoteConnectionAsync(string token)
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("https://rhythians.com/api/auth/me");
        if (!response.IsSuccessStatusCode) return new RemoteConnection(false, null);
        var data = await response.Content.ReadFromJsonAsync<UserResponse>();
        return new RemoteConnection(true, data?.Username);
    }
    catch
    {
        return new RemoteConnection(false, null);
    }
}

record RemoteConnection(bool Authenticated, string? Username);
record LoginPayload(string? Token, string? Username, string? Game);
record GamePayload(bool Running, string? Game);
record UserResponse(string? Username);

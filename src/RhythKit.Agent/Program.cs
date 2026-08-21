using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
    var body = await JsonSerializer.DeserializeAsync<GameState>(request.Body);

using System.Text.Json;

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

app.MapPost("/auth/start", async () =>
{
    using var client = new HttpClient { BaseAddress = new Uri("https://rhythians.vercel.app/") };
    var result = await client.PostAsJsonAsync("api/rhythkit/device/start", new { gameVersion = "rhythkit" });
    return Results.Content(await result.Content.ReadAsStringAsync(), "application/json", System.Text.Encoding.UTF8, (int)result.StatusCode);
});

app.MapPost("/auth/poll", async (AuthPoll request) =>
{
    using var client = new HttpClient { BaseAddress = new Uri("https://rhythians.vercel.app/") };
    var result = await client.PostAsJsonAsync("api/rhythkit/device/poll", request);
    var json = await result.Content.ReadAsStringAsync();
    if (result.IsSuccessStatusCode)
    {
        var auth = JsonSerializer.Deserialize<AuthResponse>(json);
        if (auth?.Status == "authorized" && !string.IsNullOrWhiteSpace(auth.Token)) TokenStore.Save(auth.Token);
    }
    return Results.Content(json, "application/json", System.Text.Encoding.UTF8, (int)result.StatusCode);
});

app.Run();

record GameState(string Game);
record ScoreRequest(string challengeMapId, string clientScoreId, double? accuracy, int? misses, double? speed);
record AuthPoll(string deviceCode);
record AuthResponse(string Status, string? Token);
record TokenState(string Token, string Game);

static class TokenStore
{
    static readonly string Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rhythians", "rhythkit.json");
    public static TokenState? Load()
    {
        try { return File.Exists(Path) ? JsonSerializer.Deserialize<TokenState>(File.ReadAllText(Path)) : null; } catch { return null; }
    }
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

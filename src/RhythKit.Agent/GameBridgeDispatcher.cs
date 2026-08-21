using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace RhythKit.Agent;

internal sealed class GameBridgeDispatcher : IDisposable
{
    private readonly HttpClient client = new() { BaseAddress = new Uri("https://rhythians.vercel.app"), Timeout = TimeSpan.FromSeconds(10) };
    private readonly GameBridgeInbox inbox;

    public GameBridgeDispatcher()
    {
        inbox = new GameBridgeInbox(GetGameType(), HandleAsync);
    }

    public void Start() => inbox.Start();

    private async Task HandleAsync(GameBridgeMessage message)
    {
        var game = message.Game ?? "Rhythia";
        var version = message.GameVersion ?? "unknown";
        GameConnectionState.Update(new GameConnectionUpdate(message.Running ?? true, message.IntegrationConnected ?? true, message.MapCaptureReady ?? !string.IsNullOrWhiteSpace(message.MapId), game, version, message.MapId, message.Event));
        if (!string.Equals(message.Event, "MapCompleted", StringComparison.OrdinalIgnoreCase)) return;
        if (message.ResultQualified != true || string.IsNullOrWhiteSpace(message.MapId) || string.IsNullOrWhiteSpace(message.ClientScoreId)) return;
        if (message.Accuracy is null || message.Accuracy < 0 || message.Accuracy > 100 || message.Misses is null || message.Misses < 0 || message.Speed is <= 0) return;
        if (!string.Equals(game, "Vulnus", StringComparison.OrdinalIgnoreCase) && !RankedMapStore.Contains(message.MapId))
        {
            GameConnectionState.Update(new GameConnectionUpdate(true, true, true, game, version, message.MapId, "MapNotInstalled"));
            return;
        }
        var state = TokenStore.Load();
        if (string.IsNullOrWhiteSpace(state?.Token))
        {
            GameConnectionState.Update(new GameConnectionUpdate(true, true, true, game, version, message.MapId, "AuthenticationRequired"));
            return;
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rhythkit/scores");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.Token);
        request.Content = JsonContent.Create(new { challengeMapId = message.MapId, clientScoreId = message.ClientScoreId, accuracy = message.Accuracy.Value, misses = message.Misses.Value, speed = message.Speed, gameVersion = version, integrationVersion = "1", completedAt = message.CompletedAt ?? DateTimeOffset.UtcNow, resultQualified = true });
        try
        {
            using var response = await client.SendAsync(request);
            GameConnectionState.Update(new GameConnectionUpdate(true, true, true, game, version, message.MapId, response.IsSuccessStatusCode ? "ScoreSubmitted" : "ScoreRejected"));
        }
        catch
        {
            GameConnectionState.Update(new GameConnectionUpdate(true, true, true, game, version, message.MapId, "ScoreSubmissionFailed"));
        }
    }

    private static string GetGameType()
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], "--game-type", StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return "unknown";
    }

    public void Dispose()
    {
        inbox.Dispose();
        client.Dispose();
    }
}

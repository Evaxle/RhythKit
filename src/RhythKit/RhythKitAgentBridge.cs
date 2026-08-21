using System.Net.Http.Json;

namespace RhythKit;

public sealed class RhythKitAgentBridge
{
    private readonly HttpClient client;

    public RhythKitAgentBridge()
    {
        client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:45872"), Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<GameConnectionSnapshot?> ReportStateAsync(GameConnectionState state, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync("/game", state, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GameConnectionSnapshot>(cancellationToken);
    }

    public async Task<GameConnectionSnapshot?> ReportEventAsync(GameEvent gameEvent, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync("/game/event", gameEvent, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GameConnectionSnapshot>(cancellationToken);
    }

    public async Task<ScoreSubmissionResponse> SubmitScoreAsync(ScoreSubmission score, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync("/score", score, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ScoreSubmissionResponse>(cancellationToken) ?? throw new InvalidOperationException("Invalid score response.");
    }
}

public sealed record GameConnectionState(
    bool Running,
    bool IntegrationConnected,
    bool MapCaptureReady,
    string? Game,
    string? GameVersion,
    string? MapId,
    string? LastEvent);

public sealed record GameEvent(
    string Event,
    bool? Running = null,
    bool? IntegrationConnected = null,
    bool? MapCaptureReady = null,
    string? Game = null,
    string? GameVersion = null,
    string? MapId = null);

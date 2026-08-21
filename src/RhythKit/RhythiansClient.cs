using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RhythKit;

public sealed class RhythiansClient(HttpClient httpClient)
{
    public static RhythiansClient CreateDefault() => new(new HttpClient { BaseAddress = new Uri("https://rhythians.vercel.app") });

    public async Task<DeviceAuthorization> StartDeviceAuthorizationAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/rhythkit/device/start", new { gameVersion = "0.1.0" }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DeviceAuthorization>(cancellationToken) ?? throw new InvalidOperationException("Invalid authorization response.");
    }

    public async Task<DeviceTokenResponse?> PollDeviceAuthorizationAsync(string deviceCode, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/rhythkit/device/poll", new { deviceCode }, cancellationToken);
        if ((int)response.StatusCode == 404 || (int)response.StatusCode == 410) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DeviceTokenResponse>(cancellationToken);
    }

    public async Task<RhythiansConnectionResponse?> TestConnectionAsync(string token, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/rhythkit/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<RhythiansConnectionResponse>(cancellationToken);
    }

    public async Task<ScoreSubmissionResponse> SubmitScoreAsync(string token, ScoreSubmission score, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rhythkit/scores") { Content = JsonContent.Create(score) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ScoreSubmissionResponse>(cancellationToken) ?? throw new InvalidOperationException("Invalid score response.");
    }
}

public sealed record DeviceAuthorization(
    [property: JsonPropertyName("deviceCode")] string DeviceCode,
    [property: JsonPropertyName("userCode")] string UserCode,
    [property: JsonPropertyName("verificationUrl")] string VerificationUrl,
    [property: JsonPropertyName("expiresIn")] int ExpiresIn);

public sealed record DeviceTokenResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("installationId")] string? InstallationId);

public sealed record RhythiansConnectionResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("authenticated")] bool Authenticated,
    [property: JsonPropertyName("username")] string? Username);

public sealed record ScoreSubmission(
    [property: JsonPropertyName("challengeMapId")] string ChallengeMapId,
    [property: JsonPropertyName("clientScoreId")] string ClientScoreId,
    [property: JsonPropertyName("accuracy")] double? Accuracy,
    [property: JsonPropertyName("misses")] int? Misses,
    [property: JsonPropertyName("speed")] double? Speed,
    [property: JsonPropertyName("gameVersion")] string? GameVersion = null,
    [property: JsonPropertyName("integrationVersion")] string? IntegrationVersion = null,
    [property: JsonPropertyName("completedAt")] DateTimeOffset? CompletedAt = null);

public sealed record ScoreSubmissionResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("duplicate")] bool Duplicate,
    [property: JsonPropertyName("counted")] bool Counted,
    [property: JsonPropertyName("points")] int Points,
    [property: JsonPropertyName("rhp")] int Rhp);

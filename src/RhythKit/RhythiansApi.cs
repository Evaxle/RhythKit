using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RhythKit;

public sealed class RhythiansApi(HttpClient httpClient)
{
    public static RhythiansApi CreateDefault() => new(new HttpClient { BaseAddress = new Uri("https://rhythians.vercel.app/") });

    public async Task<MapCheckResponse?> CheckMapAsync(string token, string mapId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/rhythkit/maps/{Uri.EscapeDataString(mapId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MapCheckResponse>(cancellationToken);
    }
}

public sealed record MapCheckResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("rhythiaMapId")] string RhythiaMapId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("rating")] double Rating,
    [property: JsonPropertyName("length")] double? Length,
    [property: JsonPropertyName("eligible")] bool Eligible);

using System.Net.Http;
using System.Text.Json;
using RhythKit.Models;

namespace RhythKit.Services;

public class RhythiaApiService
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://production.rhythia.com/api/"),
        Timeout = TimeSpan.FromSeconds(12)
    };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Extracts the numeric player id from a Rhythia profile URL
    /// such as "https://www.rhythia.com/player/7564".
    /// </summary>
    public static bool TryParseProfileId(string? input, out int playerId)
    {
        playerId = 0;
        var text = (input ?? "").Trim();
        if (text.Length == 0) return false;

        if (int.TryParse(text, out playerId))
            return playerId > 0;

        var idx = text.IndexOf("/player/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;

        var tail = text[(idx + "/player/".Length)..].Trim();
        var end = 0;
        while (end < tail.Length && char.IsDigit(tail[end])) end++;
        if (end == 0) return false;

        return int.TryParse(tail[..end], out playerId) && playerId > 0;
    }

    public async Task<RhythiaProfile?> GetProfileAsync(int playerId)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { session = "", id = playerId });
            using var req = new HttpRequestMessage(HttpMethod.Post, "getProfile")
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            };
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("user", out var user) || user.ValueKind == JsonValueKind.Null)
                return null;

            return new RhythiaProfile
            {
                PlayerId = user.TryGetProperty("id", out var id) ? id.GetInt32() : playerId,
                Username = user.TryGetProperty("username", out var un) && un.ValueKind == JsonValueKind.String
                    ? un.GetString() ?? ""
                    : "",
                AvatarUrl = user.TryGetProperty("avatar_url", out var av) && av.ValueKind == JsonValueKind.String
                    ? av.GetString()
                    : null
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<RhythiaProfile>> SearchUsersAsync(string query)
    {
        var results = new List<RhythiaProfile>();
        try
        {
            var payload = JsonSerializer.Serialize(new { text = query });
            using var req = new HttpRequestMessage(HttpMethod.Post, "enhancedSearch")
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            };
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return results;
            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("users", out var users)) return results;

            foreach (var u in users.EnumerateArray())
            {
                results.Add(new RhythiaProfile
                {
                    PlayerId = u.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                    Username = u.TryGetProperty("username", out var un) && un.ValueKind == JsonValueKind.String
                        ? un.GetString() ?? ""
                        : "",
                    AvatarUrl = u.TryGetProperty("avatar_url", out var av) && av.ValueKind == JsonValueKind.String
                        ? av.GetString()
                        : null
                });
            }
        }
        catch
        {
        }
        return results;
    }
}

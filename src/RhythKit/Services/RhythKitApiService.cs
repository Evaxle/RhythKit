using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RhythKit.Models;

namespace RhythKit.Services;

public class ServerUser
{
    public int PlayerId { get; set; }
    public string Username { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public FriendState FriendState { get; set; }
    public bool IsOnline { get; set; }
    public long LastActive { get; set; }
    public long FriendRequestedAt { get; set; }
}

public class ServerAttachment
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
}

public class ServerMessage
{
    public string Id { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public int AuthorId { get; set; }
    public string AuthorUsername { get; set; } = "";
    public string? Text { get; set; }
    public ServerAttachment? Attachment { get; set; }
    public long CreatedAt { get; set; }
}

public class ServerConversation
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "dm";
    public string? Name { get; set; }
    public List<ServerUser> Members { get; set; } = new();
    public ServerMessage? LastMessage { get; set; }
    public long LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public bool IsMutual { get; set; }
}

public class RhythKitApiService
{
    private readonly SettingsService _settings;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private HttpClient _http;

    public RhythKitApiService(SettingsService settings)
    {
        _settings = settings;
        _http = CreateClient();
    }

    private HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(_settings.ServerUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.Add("X-RhythKit-Token", _settings.ServerToken);
        return client;
    }

    public void RefreshClient()
    {
        try { _http.Dispose(); } catch { }
        _http = CreateClient();
    }

    private async Task<JsonDocument?> SendAsync(Func<HttpRequestMessage> build, bool throwOnError = true)
    {
        using var req = build();
        using var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var msg = await SafeReadError(resp);
            if (throwOnError)
                throw new RhythKitException((int)resp.StatusCode, msg);
            return null;
        }
        await using var stream = await resp.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static async Task<string> SafeReadError(HttpResponseMessage resp)
    {
        try
        {
            var text = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                return e.GetString() ?? resp.ReasonPhrase ?? "Request failed";
        }
        catch { }
        return resp.ReasonPhrase ?? "Request failed";
    }

    private static HttpRequestMessage JsonPost(string path, object? body)
    {
        return new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body ?? new { }), Encoding.UTF8, "application/json")
        };
    }

    private T? GetValue<T>(JsonElement root, string prop)
    {
        if (!root.TryGetProperty(prop, out var el) || el.ValueKind == JsonValueKind.Null)
            return default;
        return JsonSerializer.Deserialize<T>(el.GetRawText(), _json);
    }

    // ---------------- Auth ----------------

    public async Task<(string Token, ServerUser User)?> RegisterAsync(int playerId, string username, string? avatarUrl)
    {
        using var doc = await SendAsync(() => JsonPost("api/auth/register",
            new { playerId, username, avatarUrl }));
        if (doc == null) return null;
        var root = doc.RootElement;
        var token = root.TryGetProperty("token", out var t) ? t.GetString() : null;
        var user = GetValue<ServerUser>(root, "user");
        if (string.IsNullOrEmpty(token) || user == null) return null;
        return (token, user);
    }

    public async Task<ServerUser?> MeAsync()
    {
        using var doc = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "api/auth/me"), throwOnError: false);
        if (doc == null) return null;
        return GetValue<ServerUser>(doc.RootElement, "user");
    }

    public async Task EnsureUserAsync(int playerId, string username, string? avatarUrl)
    {
        await SendAsync(() => JsonPost("api/users/ensure", new { playerId, username, avatarUrl }));
    }

    // ---------------- Friends ----------------

    public async Task<List<ServerUser>> GetFriendsAsync()
    {
        using var doc = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "api/friends"));
        return GetValue<List<ServerUser>>(doc!.RootElement, "friends") ?? new();
    }

    public async Task SendFriendRequestAsync(int playerId, string username, string? avatarUrl)
    {
        await SendAsync(() => JsonPost("api/friends/request", new { playerId, username, avatarUrl }));
    }

    public async Task RespondFriendRequestAsync(int playerId, bool accept)
    {
        await SendAsync(() => JsonPost("api/friends/respond", new { playerId, accept }));
    }

    public async Task RemoveFriendAsync(int playerId)
    {
        await SendAsync(() => JsonPost("api/friends/remove", new { playerId }));
    }

    // ---------------- Conversations ----------------

    public async Task<List<ServerConversation>> GetConversationsAsync()
    {
        using var doc = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "api/conversations"));
        return GetValue<List<ServerConversation>>(doc!.RootElement, "conversations") ?? new();
    }

    public async Task<ServerConversation?> GetConversationAsync(string id)
    {
        using var doc = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"api/conversations/{id}"));
        return GetValue<ServerConversation>(doc!.RootElement, "conversation");
    }

    public async Task<ServerConversation?> CreateConversationAsync(string type, List<int> memberIds, string? name = null)
    {
        using var doc = await SendAsync(() => JsonPost("api/conversations", new { type, memberIds, name }));
        return GetValue<ServerConversation>(doc!.RootElement, "conversation");
    }

    public async Task<List<ServerMessage>> GetMessagesAsync(string conversationId)
    {
        using var doc = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"api/conversations/{conversationId}/messages"));
        return GetValue<List<ServerMessage>>(doc!.RootElement, "messages") ?? new();
    }

    public async Task<ServerMessage?> SendMessageAsync(string conversationId, string text)
    {
        using var doc = await SendAsync(() => JsonPost($"api/conversations/{conversationId}/messages", new { text }));
        return GetValue<ServerMessage>(doc!.RootElement, "message");
    }

    public async Task<ServerMessage?> SendAttachmentMessageAsync(string conversationId, string attachmentId)
    {
        using var doc = await SendAsync(() => JsonPost($"api/conversations/{conversationId}/messages", new { attachmentId }));
        return GetValue<ServerMessage>(doc!.RootElement, "message");
    }

    public async Task DeleteMessageAsync(string conversationId, string messageId)
    {
        await SendAsync(() => JsonPost($"api/conversations/{conversationId}/messages/{messageId}/delete", new { }));
    }

    public async Task MarkReadAsync(string conversationId)
    {
        await SendAsync(() => JsonPost($"api/conversations/{conversationId}/read", new { }), throwOnError: false);
    }

    public async Task LeaveGroupAsync(string conversationId)
    {
        await SendAsync(() => JsonPost($"api/conversations/{conversationId}/leave", new { }));
    }

    public async Task AddMemberAsync(string conversationId, int playerId)
    {
        await SendAsync(() => JsonPost($"api/conversations/{conversationId}/members", new { playerId }));
    }

    // ---------------- Files ----------------

    public async Task<ServerAttachment?> UploadFileAsync(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        using var content = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent(_settings.ServerToken), "token");

        using var resp = await _http.PostAsync("api/files/upload", content);
        if (!resp.IsSuccessStatusCode)
            throw new RhythKitException((int)resp.StatusCode, await SafeReadError(resp));
        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return GetValue<ServerAttachment>(doc.RootElement, "attachment");
    }

    public async Task DownloadFileAsync(ServerAttachment attachment, string destPath)
    {
        var url = $"api/files/{attachment.Id}/download?token={Uri.EscapeDataString(_settings.ServerToken)}";
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (!resp.IsSuccessStatusCode)
            throw new RhythKitException((int)resp.StatusCode, await SafeReadError(resp));
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(destPath);
        await src.CopyToAsync(dst);
    }

    public static ChatUser ToChatUser(ServerUser u) => new()
    {
        PlayerId = u.PlayerId,
        Username = u.Username,
        AvatarUrl = u.AvatarUrl,
        FriendState = u.FriendState,
        IsOnline = u.IsOnline,
        LastActive = u.LastActive,
        FriendRequestedAt = u.FriendRequestedAt
    };

    public static ChatMessage ToMessage(ServerMessage m, int selfId) => new()
    {
        Id = m.Id,
        ConversationId = m.ConversationId,
        AuthorId = m.AuthorId,
        AuthorUsername = m.AuthorUsername,
        Text = m.Text,
        Attachment = m.Attachment == null ? null : new FileAttachment
        {
            Id = m.Attachment.Id,
            FileName = m.Attachment.FileName,
            SizeBytes = m.Attachment.SizeBytes
        },
        CreatedAt = m.CreatedAt,
        IsMine = m.AuthorId == selfId
    };

    public static Conversation ToConversation(ServerConversation c, int selfId) => new()
    {
        Id = c.Id,
        Type = c.Type,
        Name = c.Name,
        Members = new System.Collections.ObjectModel.ObservableCollection<ChatUser>(c.Members.Select(ToChatUser)),
        LastMessage = c.LastMessage == null ? null : ToMessage(c.LastMessage, selfId),
        LastMessageAt = c.LastMessageAt,
        UnreadCount = c.UnreadCount,
        IsMutual = c.IsMutual,
        SelfPlayerId = selfId
    };
}

public class RhythKitException : Exception
{
    public int StatusCode { get; }
    public RhythKitException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}

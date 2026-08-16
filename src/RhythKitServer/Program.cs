using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
jsonOptions.Converters.Add(new JsonStringEnumConverter());

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSingleton(jsonOptions);

var contentRoot = builder.Environment.ContentRootPath;
var dataDir = Path.Combine(contentRoot, "data");
var uploadsDir = Path.Combine(dataDir, "uploads");
Directory.CreateDirectory(dataDir);
var store = new RhythKit.Server.Store(Path.Combine(dataDir, "rhythkit.db"), uploadsDir);
builder.Services.AddSingleton(store);

var app = builder.Build();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// ---------------- Error handling (must be registered before endpoints) ----------------

app.Use(async (context, next) =>
{
    try { await next(); }
    catch (ApiException ex)
    {
        context.Response.StatusCode = ex.StatusCode;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message }, jsonOptions);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[ERROR] {context.Request.Path}: {ex}");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { error = "Internal server error." }, jsonOptions);
    }
});

const long MaxFileBytes = 512L * 1024 * 1024;

int? Authenticate(RhythKit.Server.Store s, HttpRequest req)
{
    var token = req.Headers["X-RhythKit-Token"].FirstOrDefault()
                ?? req.Query["token"].FirstOrDefault();
    return s.FindByToken(token ?? "");
}

int Require(RhythKit.Server.Store s, HttpRequest req)
{
    var uid = Authenticate(s, req);
    if (uid == null) throw new ApiException(401, "Invalid or missing token.");
    return uid.Value;
}

// ---------------- Health / auth ----------------

app.MapGet("/api/health", () => Results.Json(new { ok = true, time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }));

app.MapPost("/api/auth/register", (RhythKit.Server.Store s, RegisterRequest body) =>
{
    if (body == null || body.PlayerId <= 0 || string.IsNullOrWhiteSpace(body.Username))
        return Results.Json(new { error = "playerId and username are required." }, statusCode: 400);
    var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    var user = s.Register(body.PlayerId, body.Username, body.AvatarUrl, token);
    return Results.Json(new { token, user });
});

app.MapGet("/api/auth/me", (RhythKit.Server.Store s, HttpRequest req) =>
{
    var uid = Authenticate(s, req);
    if (uid == null) return Results.Json(new { error = "Invalid token." }, statusCode: 401);
    var user = s.Touch(uid.Value, null, null);
    return Results.Json(new { user });
});

app.MapPost("/api/users/ensure", (RhythKit.Server.Store s, HttpRequest req, EnsureUser body) =>
{
    var uid = Require(s, req);
    if (body == null || body.PlayerId <= 0 || string.IsNullOrWhiteSpace(body.Username))
        return Results.Json(new { error = "playerId and username are required." }, statusCode: 400);
    s.UpsertUser(body.PlayerId, body.Username, body.AvatarUrl);
    return Results.Json(new { ok = true });
});

// ---------------- Friends ----------------

app.MapGet("/api/friends", (RhythKit.Server.Store s, HttpRequest req) =>
{
    var uid = Require(s, req);
    var friends = s.GetFriends(uid);
    return Results.Json(new { friends });
});

app.MapPost("/api/friends/request", (RhythKit.Server.Store s, HttpRequest req, FriendRequest body) =>
{
    var uid = Require(s, req);
    if (body == null || body.PlayerId <= 0 || string.IsNullOrWhiteSpace(body.Username))
        return Results.Json(new { error = "playerId and username are required." }, statusCode: 400);
    if (body.PlayerId == uid)
        return Results.Json(new { error = "You cannot add yourself." }, statusCode: 400);
    s.SendFriendRequest(uid, body.PlayerId, body.Username, body.AvatarUrl);
    return Results.Json(new { ok = true });
});

app.MapPost("/api/friends/respond", (RhythKit.Server.Store s, HttpRequest req, FriendRespond body) =>
{
    var uid = Require(s, req);
    if (body == null || body.PlayerId <= 0)
        return Results.Json(new { error = "playerId is required." }, statusCode: 400);
    s.RespondFriendRequest(uid, body.PlayerId, body.Accept);
    return Results.Json(new { ok = true });
});

app.MapPost("/api/friends/remove", (RhythKit.Server.Store s, HttpRequest req, FriendRemove body) =>
{
    var uid = Require(s, req);
    if (body == null || body.PlayerId <= 0)
        return Results.Json(new { error = "playerId is required." }, statusCode: 400);
    s.RemoveFriend(uid, body.PlayerId);
    return Results.Json(new { ok = true });
});

// ---------------- Conversations ----------------

app.MapGet("/api/conversations", (RhythKit.Server.Store s, HttpRequest req) =>
{
    var uid = Require(s, req);
    var conversations = s.GetConversations(uid);
    return Results.Json(new { conversations });
});

app.MapGet("/api/conversations/{id}", (RhythKit.Server.Store s, HttpRequest req, string id) =>
{
    var uid = Require(s, req);
    var conv = s.GetConversationForUser(uid, id);
    return conv == null
        ? Results.Json(new { error = "Not found." }, statusCode: 404)
        : Results.Json(new { conversation = conv });
});

app.MapPost("/api/conversations", (RhythKit.Server.Store s, HttpRequest req, CreateConversation body) =>
{
    var uid = Require(s, req);
    if (body == null || body.Type is null) return Results.Json(new { error = "type is required." }, statusCode: 400);

    RhythKit.Server.ConversationDto? conv;
    if (body.Type == "dm")
    {
        if (body.MemberIds == null || body.MemberIds.Count != 1 || body.MemberIds[0] == uid)
            return Results.Json(new { error = "dm requires exactly one other playerId." }, statusCode: 400);
        conv = s.GetOrCreateDm(uid, body.MemberIds[0]);
    }
    else
    {
        conv = s.CreateGroup(uid, body.MemberIds ?? new List<int>(), body.Name);
        if (conv == null)
            return Results.Json(new { error = "A group needs at least one other member." }, statusCode: 400);
    }
    return Results.Json(new { conversation = conv });
});

app.MapGet("/api/conversations/{id}/messages", (RhythKit.Server.Store s, HttpRequest req, string id) =>
{
    var uid = Require(s, req);
    var conv = s.GetConversationForUser(uid, id);
    if (conv == null) return Results.Json(new { error = "Not found." }, statusCode: 404);
    var messages = s.GetMessages(id);
    return Results.Json(new { messages });
});

app.MapPost("/api/conversations/{id}/messages", (RhythKit.Server.Store s, HttpRequest req, string id, SendMessage body) =>
{
    var uid = Require(s, req);
    var hasText = !string.IsNullOrWhiteSpace(body?.Text);
    var hasAtt = !string.IsNullOrWhiteSpace(body?.AttachmentId);
    if (!hasText && !hasAtt)
        return Results.Json(new { error = "Message must contain text or an attachment." }, statusCode: 400);
    var msg = s.AddMessage(uid, id, hasText ? body!.Text : null, hasAtt ? body!.AttachmentId : null);
    if (msg == null) return Results.Json(new { error = "Not found." }, statusCode: 404);
    return Results.Json(new { message = msg });
});

app.MapPost("/api/conversations/{id}/messages/{mid}/delete", (RhythKit.Server.Store s, HttpRequest req, string id, string mid) =>
{
    var uid = Require(s, req);
    return s.DeleteMessage(uid, id, mid)
        ? Results.Json(new { ok = true })
        : Results.Json(new { error = "Cannot delete message." }, statusCode: 403);
});

app.MapPost("/api/conversations/{id}/read", (RhythKit.Server.Store s, HttpRequest req, string id) =>
{
    var uid = Require(s, req);
    s.MarkRead(uid, id);
    return Results.Json(new { ok = true });
});

app.MapPost("/api/conversations/{id}/members", (RhythKit.Server.Store s, HttpRequest req, string id, AddMember body) =>
{
    var uid = Require(s, req);
    if (body == null || body.PlayerId <= 0) return Results.Json(new { error = "playerId required." }, statusCode: 400);
    return s.AddMember(uid, id, body.PlayerId)
        ? Results.Json(new { ok = true })
        : Results.Json(new { error = "Not allowed." }, statusCode: 403);
});

app.MapDelete("/api/conversations/{id}/members", (RhythKit.Server.Store s, HttpRequest req, string id, int playerId) =>
{
    var uid = Require(s, req);
    return s.RemoveMember(uid, id, playerId)
        ? Results.Json(new { ok = true })
        : Results.Json(new { error = "Not allowed." }, statusCode: 403);
});

app.MapPost("/api/conversations/{id}/leave", (RhythKit.Server.Store s, HttpRequest req, string id) =>
{
    var uid = Require(s, req);
    return s.LeaveGroup(uid, id)
        ? Results.Json(new { ok = true })
        : Results.Json(new { error = "Not found." }, statusCode: 404);
});

// ---------------- Files ----------------

app.MapPost("/api/files/upload", async (RhythKit.Server.Store s, HttpRequest req, [FromForm] string? token, IFormFile? file) =>
{
    var uid = string.IsNullOrWhiteSpace(token) ? null : s.FindByToken(token);
    if (uid == null) return Results.Json(new { error = "Invalid or missing token." }, statusCode: 401);
    if (file == null || file.Length == 0)
        return Results.Json(new { error = "No file uploaded." }, statusCode: 400);
    if (file.Length > MaxFileBytes)
        return Results.Json(new { error = "File too large (max 512MB)." }, statusCode: 413);

    var ext = Path.GetExtension(file.FileName);
    var storedName = Guid.NewGuid().ToString("N") + ext;
    var fullPath = Path.Combine(uploadsDir, storedName);
    await using (var fs = File.Create(fullPath))
    {
        await file.CopyToAsync(fs);
    }
    var att = s.AddAttachment(uid.Value, file.FileName, storedName, file.Length);
    return Results.Json(new { attachment = att });
}).DisableAntiforgery();

app.MapGet("/api/files/{id}/download", (RhythKit.Server.Store s, HttpRequest req, string id) =>
{
    var uid = Authenticate(s, req);
    if (uid == null) return Results.Json(new { error = "Invalid or missing token." }, statusCode: 401);
    var att = s.GetAttachment(id);
    if (att == null) return Results.Json(new { error = "Not found." }, statusCode: 404);
    var path = Path.Combine(uploadsDir, att.Value.StoredName);
    if (!File.Exists(path)) return Results.Json(new { error = "File missing." }, statusCode: 404);
    return Results.File(path, "application/octet-stream", att.Value.FileName, enableRangeProcessing: true);
});

var urls = builder.Configuration["urls"] ?? Environment.GetEnvironmentVariable("RHYTHKIT_URL") ?? "http://0.0.0.0:5210";
app.Urls.Add(urls);

Console.WriteLine("RhythKitServer listening on " + urls);
Console.WriteLine("Data directory: " + dataDir);
app.Run();

// ---------------- DTOs ----------------

public record RegisterRequest(int PlayerId, string Username, string? AvatarUrl);
public record EnsureUser(int PlayerId, string Username, string? AvatarUrl);
public record FriendRequest(int PlayerId, string Username, string? AvatarUrl);
public record FriendRespond(int PlayerId, bool Accept);
public record FriendRemove(int PlayerId);
public record CreateConversation(string Type, List<int>? MemberIds, string? Name);
public record SendMessage(string? Text, string? AttachmentId);
public record AddMember(int PlayerId);

public class ApiException : Exception
{
    public int StatusCode { get; }
    public ApiException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}

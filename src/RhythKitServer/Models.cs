namespace RhythKit.Server;

public enum FriendState
{
    None,
    Incoming,
    Outgoing,
    Mutual
}

public class UserDto
{
    public int PlayerId { get; set; }
    public string Username { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public FriendState FriendState { get; set; } = FriendState.None;
    public bool IsOnline { get; set; }
    public long LastActive { get; set; }
    public long FriendRequestedAt { get; set; }
}

public class FriendRequestDto
{
    public int PlayerId { get; set; }
    public string Username { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public long RequestedAt { get; set; }
}

public class AttachmentDto
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
}

public class MessageDto
{
    public string Id { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public int AuthorId { get; set; }
    public string AuthorUsername { get; set; } = "";
    public string? Text { get; set; }
    public AttachmentDto? Attachment { get; set; }
    public long CreatedAt { get; set; }
}

public class ConversationDto
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "dm";
    public string? Name { get; set; }
    public List<UserDto> Members { get; set; } = new();
    public MessageDto? LastMessage { get; set; }
    public long LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public bool IsMutual { get; set; }
}

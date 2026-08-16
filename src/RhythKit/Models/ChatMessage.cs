using System.ComponentModel;

namespace RhythKit.Models;

public class ChatMessage : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public int AuthorId { get; set; }
    public string AuthorUsername { get; set; } = "";
    public string? Text { get; set; }
    public FileAttachment? Attachment { get; set; }
    public long CreatedAt { get; set; }
    public bool IsMine { get; set; }
    public bool ShowAuthorInGroup { get; set; }

    public bool HasText => !string.IsNullOrEmpty(Text);

    public DateTime Time => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime;

    public string TimeDisplay => Time.ToString("HH:mm");

    public string DateDisplay => Time.ToString("MMM d, yyyy");

    public bool HasAttachment => Attachment != null;

    public string AttachmentLabel => Attachment != null
        ? $"{Attachment.FileName} ({Attachment.DisplaySize})"
        : "";

    public string AuthorInitial
    {
        get
        {
            if (string.IsNullOrEmpty(AuthorUsername)) return "?";
            return AuthorUsername[..1].ToUpperInvariant();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

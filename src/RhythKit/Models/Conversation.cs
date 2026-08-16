using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RhythKit.Models;

public class Conversation : INotifyPropertyChanged
{
    private ChatMessage? _lastMessage;
    private int _unreadCount;
    private bool _isMutual;

    public string Id { get; set; } = "";
    public string Type { get; set; } = "dm";
    public string? Name { get; set; }
    public ObservableCollection<ChatUser> Members { get; set; } = new();
    public long LastMessageAt { get; set; }
    public int SelfPlayerId { get; set; }

    public bool IsGroup => Type == "group";

    public ChatMessage? LastMessage
    {
        get => _lastMessage;
        set
        {
            if (!ReferenceEquals(_lastMessage, value))
            {
                _lastMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Subtitle));
            }
        }
    }

    public int UnreadCount
    {
        get => _unreadCount;
        set
        {
            if (_unreadCount != value)
            {
                _unreadCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnread));
                OnPropertyChanged(nameof(UnreadLabel));
            }
        }
    }

    public bool IsMutual
    {
        get => _isMutual;
        set
        {
            if (_isMutual != value)
            {
                _isMutual = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FriendBadge));
            }
        }
    }

    public string DisplayName
    {
        get
        {
            if (IsGroup)
                return string.IsNullOrWhiteSpace(Name) ? "Group Chat" : Name;
            var other = Members.FirstOrDefault(m => m.PlayerId != SelfPlayerId);
            return other?.Username ?? (Members.Count > 0 ? Members[0].Username : "Unknown");
        }
    }

    public string Initials
    {
        get
        {
            if (IsGroup) return "GRP";
            var other = Members.FirstOrDefault(m => m.PlayerId != SelfPlayerId);
            return other?.Initials ?? "?";
        }
    }

    public bool IsOtherOnline
    {
        get
        {
            if (IsGroup) return Members.Any(m => m.IsOnline);
            var other = Members.FirstOrDefault(m => m.PlayerId != SelfPlayerId);
            return other?.IsOnline ?? false;
        }
    }

    public string Subtitle
    {
        get
        {
            if (LastMessage == null) return "No messages yet";
            var prefix = LastMessage.IsMine ? "You: " : "";
            if (IsGroup && !LastMessage.IsMine)
                prefix = $"{LastMessage.AuthorUsername}: ";
            if (LastMessage.Attachment != null)
                return $"{prefix}\uD83D\uDCCE {LastMessage.Attachment.FileName}";
            return prefix + LastMessage.Text;
        }
    }

    public bool HasUnread => UnreadCount > 0;

    public string UnreadLabel => UnreadCount > 99 ? "99+" : UnreadCount.ToString();

    public string FriendBadge => IsMutual ? "Friend" : "";

    public void RaiseStatic()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(IsOtherOnline));
        OnPropertyChanged(nameof(IsGroup));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(FriendBadge));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

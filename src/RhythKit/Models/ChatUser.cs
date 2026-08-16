using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RhythKit.Models;

public enum FriendState
{
    None = 0,
    Incoming = 1,
    Outgoing = 2,
    Mutual = 3
}

public class ChatUser : INotifyPropertyChanged
{
    private FriendState _friendState;
    private bool _isOnline;

    public int PlayerId { get; set; }
    public string Username { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public long LastActive { get; set; }
    public long FriendRequestedAt { get; set; }

    public FriendState FriendState
    {
        get => _friendState;
        set
        {
            if (_friendState != value)
            {
                _friendState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsMutual));
            }
        }
    }

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (_isOnline != value)
            {
                _isOnline = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string Initials
    {
        get
        {
            var parts = Username.Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1) return parts[0].Length > 0 ? parts[0][..1].ToUpperInvariant() : "?";
            return (parts[0][..1] + parts[1][..1]).ToUpperInvariant();
        }
    }

    public string StatusText => FriendState switch
    {
        FriendState.Incoming => "Request received",
        FriendState.Outgoing => "Request sent",
        FriendState.Mutual => "Friends",
        _ => IsOnline ? "Online" : "Offline"
    };

    public bool IsMutual => FriendState == FriendState.Mutual;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using RhythKit.Models;
using RhythKit.Services;
using RhythKit.Windows;

namespace RhythKit.ViewModels;

public class MessagesViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly RhythiaApiService _rhythia;
    private readonly DispatcherTimer _timer;
    private RhythKitApiService _api;
    private int _selfId;
    private bool _connected;
    private bool _refreshing;
    private string _connectionStatus = "Connecting...";
    private string _messageText = "";
    private string _searchQuery = "";
    private string _searchStatus = "";
    private string _chatStatus = "";
    private bool _isSearching;
    private Conversation? _selectedConversation;
    private readonly Dictionary<int, ChatUser> _knownUsers = new();

    public ObservableCollection<Conversation> Conversations { get; } = new();
    public ObservableCollection<ChatUser> SearchResults { get; } = new();
    public ObservableCollection<ChatUser> FriendRequests { get; } = new();
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public MessagesViewModel(SettingsService settings)
    {
        _settings = settings;
        _rhythia = new RhythiaApiService();
        _api = new RhythKitApiService(settings);

        SearchCommand = new RelayCommand(SearchAsync);
        AddFriendCommand = new RelayCommand(u => _ = AddFriendAsync(u as ChatUser));
        MessageUserCommand = new RelayCommand(u => _ = OpenOrCreateDmAsync(u as ChatUser));
        AcceptRequestCommand = new RelayCommand(u => _ = RespondRequestAsync(u as ChatUser, true));
        DeclineRequestCommand = new RelayCommand(u => _ = RespondRequestAsync(u as ChatUser, false));
        RemoveFriendCommand = new RelayCommand(u => _ = RemoveFriendAsync(u as ChatUser));
        SendCommand = new RelayCommand(SendMessage, () => !string.IsNullOrWhiteSpace(MessageText));
        AttachCommand = new RelayCommand(AttachFile);
        DownloadCommand = new RelayCommand(m => _ = DownloadAttachmentAsync(m as ChatMessage));
        DeleteCommand = new RelayCommand(m => _ = DeleteMessageAsync(m as ChatMessage));
        CreateGroupCommand = new RelayCommand(CreateGroup);
        AddMemberCommand = new RelayCommand(AddMemberToGroup);
        LeaveGroupCommand = new RelayCommand(LeaveGroup);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) =>
        {
            if (_connected) await RefreshAsync();
            else await ConnectAsync();
        };
    }

    public string DisplayName => _settings.Username;
    public bool HasProfile => _settings.HasProfile;

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set
        {
            if (SetProperty(ref _connectionStatus, value))
                OnPropertyChanged(nameof(ConnectionColor));
        }
    }

    public SolidColorBrush ConnectionColor
        => _connected ? new SolidColorBrush(Color.FromRgb(0x4C, 0xC3, 0x8A)) : new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x5A));

    public string ChatStatus
    {
        get => _chatStatus;
        set => SetProperty(ref _chatStatus, value);
    }

    public string MessageText
    {
        get => _messageText;
        set
        {
            if (SetProperty(ref _messageText, value))
            {
                OnPropertyChanged(nameof(CanSend));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanSend => !string.IsNullOrWhiteSpace(MessageText);

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
                SearchStatus = "";
        }
    }

    public string SearchStatus
    {
        get => _searchStatus;
        set => SetProperty(ref _searchStatus, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        set => SetProperty(ref _isSearching, value);
    }

    public Conversation? SelectedConversation
    {
        get => _selectedConversation;
        set
        {
            if (SetProperty(ref _selectedConversation, value))
                _ = OnConversationSelectedAsync(value);
        }
    }

    public RelayCommand SearchCommand { get; }
    public RelayCommand AddFriendCommand { get; }
    public RelayCommand MessageUserCommand { get; }
    public RelayCommand AcceptRequestCommand { get; }
    public RelayCommand DeclineRequestCommand { get; }
    public RelayCommand RemoveFriendCommand { get; }
    public RelayCommand SendCommand { get; }
    public RelayCommand AttachCommand { get; }
    public RelayCommand DownloadCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand CreateGroupCommand { get; }
    public RelayCommand AddMemberCommand { get; }
    public RelayCommand LeaveGroupCommand { get; }

    public void Start()
    {
        _ = ConnectAsync();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    // ---------------- Connection ----------------

    private async Task ConnectAsync()
    {
        if (!_settings.HasProfile)
        {
            ConnectionStatus = "No profile configured";
            return;
        }
        _selfId = _settings.PlayerId ?? 0;
        try
        {
            _api.RefreshClient();
            var me = await _api.MeAsync();
            if (me == null)
            {
                var reg = await _api.RegisterAsync(_settings.PlayerId!.Value, _settings.Username, _settings.AvatarUrl);
                if (reg == null)
                {
                    ConnectionStatus = "Offline - server unreachable";
                    _connected = false;
                    return;
                }
                _settings.SaveServer(_settings.ServerUrl, reg.Value.Token);
                _api.RefreshClient();
            }
            _connected = true;
            ConnectionStatus = "Online";
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(HasProfile));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _connected = false;
            ConnectionStatus = "Offline - " + ex.Message;
        }
    }

    // ---------------- Polling ----------------

    private async Task RefreshAsync()
    {
        if (!_connected || _refreshing) return;
        _refreshing = true;
        try
        {
            var friendsTask = _api.GetFriendsAsync();
            var conversationsTask = _api.GetConversationsAsync();
            await Task.WhenAll(friendsTask, conversationsTask);

            UpdateFriends(friendsTask.Result);
            UpdateConversations(conversationsTask.Result);

            if (SelectedConversation != null)
            {
                await RefreshMessagesAsync(SelectedConversation.Id);
            }
        }
        catch (RhythKitException)
        {
            _connected = false;
            ConnectionStatus = "Offline - connection lost";
        }
        catch (HttpRequestException)
        {
            _connected = false;
            ConnectionStatus = "Offline - server unreachable";
        }
        catch (Exception)
        {
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateFriends(List<ServerUser> friends)
    {
        var users = new Dictionary<int, ChatUser>();
        foreach (var f in friends)
        {
            var u = RhythKitApiService.ToChatUser(f);
            users[u.PlayerId] = u;
            _knownUsers[u.PlayerId] = u;
        }

        var incoming = friends.Where(f => f.FriendState == FriendState.Incoming)
                              .Select(RhythKitApiService.ToChatUser)
                              .OrderByDescending(f => f.FriendRequestedAt)
                              .ToList();

        FriendRequests.Clear();
        foreach (var r in incoming) FriendRequests.Add(r);
        OnPropertyChanged(nameof(HasFriendRequests));
    }

    private void UpdateConversations(List<ServerConversation> serverConvs)
    {
        var incoming = serverConvs.Select(c => RhythKitApiService.ToConversation(c, _selfId)).ToList();
        var byId = incoming.ToDictionary(c => c.Id);

        var toRemove = Conversations.Where(c => !byId.ContainsKey(c.Id)).ToList();
        foreach (var c in toRemove) Conversations.Remove(c);

        var existing = Conversations.ToDictionary(c => c.Id);
        foreach (var c in incoming)
        {
            if (existing.TryGetValue(c.Id, out var cur))
            {
                cur.Name = c.Name;
                cur.Type = c.Type;
                cur.LastMessageAt = c.LastMessageAt;
                cur.UnreadCount = c.UnreadCount;
                cur.IsMutual = c.IsMutual;
                cur.Members.Clear();
                foreach (var m in c.Members) cur.Members.Add(m);
                cur.LastMessage = c.LastMessage;
                cur.RaiseStatic();
            }
            else
            {
                Conversations.Add(c);
            }
        }

        var sorted = Conversations
            .OrderByDescending(c => c.IsGroup ? 0 : (c.IsMutual ? 1 : 0))
            .ThenByDescending(c => c.LastMessageAt)
            .ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            var idx = Conversations.IndexOf(sorted[i]);
            if (idx != i)
                Conversations.Move(idx, i);
        }
    }

    private async Task RefreshMessagesAsync(string conversationId)
    {
        var fetched = await _api.GetMessagesAsync(conversationId);
        var isGroup = SelectedConversation?.IsGroup ?? false;
        var incoming = fetched.Select(m =>
        {
            var msg = RhythKitApiService.ToMessage(m, _selfId);
            msg.ShowAuthorInGroup = isGroup && !msg.IsMine;
            return msg;
        }).ToList();

        var existingIds = Messages.Select(m => m.Id).ToHashSet();
        var incomingIds = incoming.Select(m => m.Id).ToHashSet();

        for (int i = Messages.Count - 1; i >= 0; i--)
        {
            if (!incomingIds.Contains(Messages[i].Id))
                Messages.RemoveAt(i);
        }

        foreach (var msg in incoming)
        {
            if (!existingIds.Contains(msg.Id))
                Messages.Add(msg);
        }

        var last = incoming.LastOrDefault();
        var selected = SelectedConversation;
        if (selected != null && last != null && !last.IsMine)
        {
            selected.UnreadCount = 0;
            _ = _api.MarkReadAsync(conversationId);
        }
        OnPropertyChanged(nameof(ScrollToBottomToken));
    }

    private async Task OnConversationSelectedAsync(Conversation? conversation)
    {
        Messages.Clear();
        ChatStatus = "";
        if (conversation == null) return;

        var conv = await _api.GetConversationAsync(conversation.Id);
        if (conv != null)
        {
            conversation.Members.Clear();
            foreach (var m in conv.Members) conversation.Members.Add(RhythKitApiService.ToChatUser(m));
            conversation.Name = conv.Name;
            conversation.RaiseStatic();
        }

        await RefreshMessagesAsync(conversation.Id);
        if (conversation.Type == "dm")
        {
            var other = conversation.Members.FirstOrDefault(m => m.PlayerId != _selfId);
            ChatStatus = other?.StatusText ?? "";
        }
        else
        {
            var names = string.Join(", ", conversation.Members.Select(m => m.Username));
            ChatStatus = names;
        }
    }

    // ---------------- Search ----------------

    private async void SearchAsync()
    {
        var query = SearchQuery?.Trim() ?? "";
        if (query.Length == 0) return;

        IsSearching = true;
        SearchStatus = "Searching...";
        try
        {
            var results = await _rhythia.SearchUsersAsync(query);
            SearchResults.Clear();
            foreach (var r in results)
            {
                var user = new ChatUser
                {
                    PlayerId = r.PlayerId,
                    Username = r.Username,
                    AvatarUrl = r.AvatarUrl
                };
                if (_knownUsers.TryGetValue(user.PlayerId, out var known))
                    user.FriendState = known.FriendState;
                SearchResults.Add(user);
            }
            SearchStatus = results.Count == 0 ? "No users found." : $"{results.Count} result(s)";
            OnPropertyChanged(nameof(HasSearchResults));
        }
        catch
        {
            SearchStatus = "Search failed. Check your connection.";
        }
        finally
        {
            IsSearching = false;
        }
    }

    // ---------------- Friends ----------------

    private async Task AddFriendAsync(ChatUser? user)
    {
        if (user == null || user.PlayerId <= 0) return;
        try
        {
            await _api.SendFriendRequestAsync(user.PlayerId, user.Username, user.AvatarUrl);
            user.FriendState = FriendState.Outgoing;
            _knownUsers[user.PlayerId] = user;
            SearchStatus = $"Friend request sent to {user.Username}.";
        }
        catch (Exception ex)
        {
            SearchStatus = "Failed: " + ex.Message;
        }
    }

    private async Task RespondRequestAsync(ChatUser? user, bool accept)
    {
        if (user == null) return;
        try
        {
            await _api.RespondFriendRequestAsync(user.PlayerId, accept);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ChatStatus = "Failed: " + ex.Message;
        }
    }

    private async Task RemoveFriendAsync(ChatUser? user)
    {
        if (user == null) return;
        try
        {
            await _api.RemoveFriendAsync(user.PlayerId);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ChatStatus = "Failed: " + ex.Message;
        }
    }

    // ---------------- Conversations / DMs ----------------

    private async Task OpenOrCreateDmAsync(ChatUser? user)
    {
        if (user == null) return;
        try
        {
            await _api.EnsureUserAsync(user.PlayerId, user.Username, user.AvatarUrl);
            var conv = await _api.CreateConversationAsync("dm", new List<int> { user.PlayerId });
            if (conv == null) return;
            var existing = Conversations.FirstOrDefault(c => c.Id == conv.Id);
            if (existing == null)
            {
                existing = RhythKitApiService.ToConversation(conv, _selfId);
                Conversations.Add(existing);
            }
            SelectedConversation = existing;
        }
        catch (Exception ex)
        {
            ChatStatus = "Failed: " + ex.Message;
        }
    }

    // ---------------- Messaging ----------------

    private async void SendMessage()
    {
        var conv = SelectedConversation;
        if (conv == null) return;
        var text = MessageText.Trim();
        if (text.Length == 0) return;
        MessageText = "";
        try
        {
            var sent = await _api.SendMessageAsync(conv.Id, text);
            if (sent != null)
                AppendMessageLocal(RhythKitApiService.ToMessage(sent, _selfId));
        }
        catch (Exception ex)
        {
            ChatStatus = "Send failed: " + ex.Message;
        }
    }

    private async void AttachFile()
    {
        var conv = SelectedConversation;
        if (conv == null) return;

        var dialog = new OpenFileDialog
        {
            Title = "Choose a file to send",
            Filter = "All files (*.*)|*.*",
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        ChatStatus = "Uploading...";
        try
        {
            var attachment = await _api.UploadFileAsync(dialog.FileName);
            if (attachment == null)
            {
                ChatStatus = "Upload failed.";
                return;
            }
            var sent = await _api.SendAttachmentMessageAsync(conv.Id, attachment.Id);
            if (sent != null)
                AppendMessageLocal(RhythKitApiService.ToMessage(sent, _selfId));
            ChatStatus = "";
        }
        catch (Exception ex)
        {
            ChatStatus = "Upload failed: " + ex.Message;
        }
    }

    private async Task DownloadAttachmentAsync(ChatMessage? message)
    {
        if (message?.Attachment == null) return;

        var dialog = new SaveFileDialog
        {
            Title = "Save attachment",
            FileName = message.Attachment.FileName,
            Filter = "All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            await _api.DownloadFileAsync(new ServerAttachment
            {
                Id = message.Attachment.Id,
                FileName = message.Attachment.FileName,
                SizeBytes = message.Attachment.SizeBytes
            }, dialog.FileName);
            ChatStatus = $"Saved to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            ChatStatus = "Download failed: " + ex.Message;
        }
    }

    private async Task DeleteMessageAsync(ChatMessage? message)
    {
        var conv = SelectedConversation;
        if (message == null || conv == null || !message.IsMine) return;

        var result = MessageBox.Show("Delete this message?", "Delete Message", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _api.DeleteMessageAsync(conv.Id, message.Id);
            Messages.Remove(message);
        }
        catch (Exception ex)
        {
            ChatStatus = "Delete failed: " + ex.Message;
        }
    }

    private void AppendMessageLocal(ChatMessage msg)
    {
        var existing = Messages.FirstOrDefault(m => m.Id == msg.Id);
        if (existing != null) return;
        Messages.Add(msg);
        OnPropertyChanged(nameof(ScrollToBottomToken));
    }

    // ---------------- Groups ----------------

    private async void CreateGroup()
    {
        var picker = new ContactPickerWindow("New Group Chat", "Select friends to add to the group.", multiSelect: true, promptName: true)
        {
            Owner = Application.Current.MainWindow
        };
        picker.SetContacts(FriendRequests.Concat(_knownUsers.Values)
            .Where(u => u.PlayerId != _selfId && u.FriendState == FriendState.Mutual)
            .DistinctBy(u => u.PlayerId)
            .OrderBy(u => u.Username)
            .ToList());

        if (picker.ShowDialog() != true) return;

        try
        {
            var conv = await _api.CreateConversationAsync("group", picker.SelectedPlayerIds, picker.GroupName);
            if (conv == null)
            {
                ChatStatus = "A group needs at least one other member.";
                return;
            }
            var item = RhythKitApiService.ToConversation(conv, _selfId);
            Conversations.Add(item);
            SelectedConversation = item;
        }
        catch (Exception ex)
        {
            ChatStatus = "Failed to create group: " + ex.Message;
        }
    }

    private async void AddMemberToGroup()
    {
        var conv = SelectedConversation;
        if (conv == null || !conv.IsGroup) return;

        var existingIds = conv.Members.Select(m => m.PlayerId).ToHashSet();
        var picker = new ContactPickerWindow($"Add member to {conv.DisplayName}", "Select a friend to add.", multiSelect: false, promptName: false)
        {
            Owner = Application.Current.MainWindow
        };
        picker.SetContacts(_knownUsers.Values
            .Where(u => u.PlayerId != _selfId && !existingIds.Contains(u.PlayerId) && u.FriendState == FriendState.Mutual)
            .DistinctBy(u => u.PlayerId)
            .OrderBy(u => u.Username)
            .ToList());

        if (picker.ShowDialog() != true || picker.SelectedPlayerIds.Count == 0) return;

        try
        {
            await _api.AddMemberAsync(conv.Id, picker.SelectedPlayerIds[0]);
            await RefreshMessagesAsync(conv.Id);
            var refreshed = await _api.GetConversationAsync(conv.Id);
            if (refreshed != null)
            {
                conv.Members.Clear();
                foreach (var m in refreshed.Members) conv.Members.Add(RhythKitApiService.ToChatUser(m));
                conv.RaiseStatic();
            }
        }
        catch (Exception ex)
        {
            ChatStatus = "Failed to add member: " + ex.Message;
        }
    }

    private async void LeaveGroup()
    {
        var conv = SelectedConversation;
        if (conv == null || !conv.IsGroup) return;

        var result = MessageBox.Show($"Leave the group chat '{conv.DisplayName}'?", "Leave Group", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _api.LeaveGroupAsync(conv.Id);
            Conversations.Remove(conv);
            SelectedConversation = null;
        }
        catch (Exception ex)
        {
            ChatStatus = "Failed: " + ex.Message;
        }
    }

    // ---------------- Misc bound values ----------------

    public bool HasFriendRequests => FriendRequests.Count > 0;

    public bool HasSearchResults => SearchResults.Count > 0;

    public string ScrollToBottomToken => $"{Messages.Count}:{Messages.LastOrDefault()?.Id ?? ""}";
}

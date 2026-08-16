using Microsoft.Data.Sqlite;

namespace RhythKit.Server;

public class Store
{
    private readonly SqliteConnection _conn;
    private readonly object _sync = new();
    private readonly string _uploadsDir;

    public Store(string dataPath, string uploadsDir)
    {
        _conn = new SqliteConnection($"Data Source={dataPath}");
        _uploadsDir = uploadsDir;
        Directory.CreateDirectory(uploadsDir);
        _conn.Open();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;

            CREATE TABLE IF NOT EXISTS users (
                player_id INTEGER PRIMARY KEY,
                username TEXT NOT NULL,
                avatar_url TEXT,
                token TEXT,
                created_at INTEGER NOT NULL,
                last_active INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS friends (
                user_a INTEGER NOT NULL,
                user_b INTEGER NOT NULL,
                status TEXT NOT NULL,
                created_at INTEGER NOT NULL,
                PRIMARY KEY (user_a, user_b)
            );

            CREATE TABLE IF NOT EXISTS conversations (
                id TEXT PRIMARY KEY,
                type TEXT NOT NULL,
                name TEXT,
                created_by INTEGER NOT NULL,
                created_at INTEGER NOT NULL,
                last_message_at INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS conversation_members (
                conversation_id TEXT NOT NULL,
                player_id INTEGER NOT NULL,
                PRIMARY KEY (conversation_id, player_id)
            );

            CREATE TABLE IF NOT EXISTS messages (
                id TEXT PRIMARY KEY,
                conversation_id TEXT NOT NULL,
                author_id INTEGER NOT NULL,
                text TEXT,
                attachment_id TEXT,
                created_at INTEGER NOT NULL,
                deleted_at INTEGER
            );

            CREATE TABLE IF NOT EXISTS attachments (
                id TEXT PRIMARY KEY,
                file_name TEXT NOT NULL,
                stored_name TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                uploader_id INTEGER NOT NULL,
                created_at INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS read_state (
                conversation_id TEXT NOT NULL,
                player_id INTEGER NOT NULL,
                last_read_at INTEGER NOT NULL,
                PRIMARY KEY (conversation_id, player_id)
            );

            CREATE INDEX IF NOT EXISTS idx_messages_conv ON messages(conversation_id, created_at);
            CREATE INDEX IF NOT EXISTS idx_friends_b ON friends(user_b);
            """;
        cmd.ExecuteNonQuery();
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static string Clean(string? s) => (s ?? "").Trim();

    private static string CleanName(string? s)
    {
        var v = Clean(s);
        return v.Length > 60 ? v[..60] : v;
    }

    private T WithLock<T>(Func<SqliteConnection, T> fn)
    {
        lock (_sync)
        {
            return fn(_conn);
        }
    }

    private static UserDto? ReadUser(SqliteDataReader r, FriendState state = FriendState.None, long requestedAt = 0)
    {
        return new UserDto
        {
            PlayerId = r.GetInt32(0),
            Username = r.GetString(1),
            AvatarUrl = r.IsDBNull(2) ? null : r.GetString(2),
            FriendState = state,
            LastActive = r.GetInt64(3),
            IsOnline = r.GetInt64(3) > Now() - 45_000
        };
    }

    // ---------------- Users / auth ----------------

    public int? FindByToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        return WithLock(_conn =>
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT player_id FROM users WHERE token = @t";
            cmd.Parameters.AddWithValue("@t", token);
            var v = cmd.ExecuteScalar();
            return v is null ? null : (int?)Convert.ToInt32(v);
        });
    }

    public UserDto? Register(int playerId, string username, string? avatarUrl, string token)
    {
        WithLock(_conn =>
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO users(player_id, username, avatar_url, token, created_at, last_active)
                VALUES(@id, @u, @a, @t, @n, @n)
                ON CONFLICT(player_id) DO UPDATE SET
                    username = @u,
                    avatar_url = COALESCE(@a, avatar_url),
                    token = @t,
                    last_active = @n
                """;
            cmd.Parameters.AddWithValue("@id", playerId);
            cmd.Parameters.AddWithValue("@u", CleanName(username));
            cmd.Parameters.AddWithValue("@a", (object?)Clean(avatarUrl));
            cmd.Parameters.AddWithValue("@t", token);
            cmd.Parameters.AddWithValue("@n", Now());
            cmd.ExecuteNonQuery();
            return true;
        });
        return GetUser(playerId);
    }

    public UserDto? Touch(int playerId, string? username, string? avatarUrl)
    {
        WithLock(_conn =>
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                UPDATE users SET last_active = @n,
                    username = COALESCE(@u, username),
                    avatar_url = COALESCE(@a, avatar_url)
                WHERE player_id = @id
                """;
            cmd.Parameters.AddWithValue("@id", playerId);
            cmd.Parameters.AddWithValue("@n", Now());
            cmd.Parameters.AddWithValue("@u", (object?)CleanName(username));
            cmd.Parameters.AddWithValue("@a", (object?)Clean(avatarUrl));
            cmd.ExecuteNonQuery();
            return true;
        });
        return GetUser(playerId);
    }

    public UserDto? GetUser(int playerId)
    {
        return WithLock(_conn =>
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT player_id, username, avatar_url, last_active FROM users WHERE player_id = @id";
            cmd.Parameters.AddWithValue("@id", playerId);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadUser(r) : null;
        });
    }

    public void UpsertUser(int playerId, string username, string? avatarUrl)
    {
        WithLock(_conn =>
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO users(player_id, username, avatar_url, created_at, last_active)
                VALUES(@id, @u, @a, @n, @n)
                ON CONFLICT(player_id) DO UPDATE SET
                    username = @u,
                    avatar_url = COALESCE(@a, avatar_url),
                    last_active = @n
                """;
            cmd.Parameters.AddWithValue("@id", playerId);
            cmd.Parameters.AddWithValue("@u", CleanName(username));
            cmd.Parameters.AddWithValue("@a", (object?)Clean(avatarUrl));
            cmd.Parameters.AddWithValue("@n", Now());
            cmd.ExecuteNonQuery();
            return true;
        });
    }

    // ---------------- Friends ----------------

    public record Relation(string AToB, string BToA, long RequestedAt);

    public Dictionary<int, Relation> GetRelations(int userId)
    {
        return WithLock(_conn =>
        {
            var map = new Dictionary<int, Relation>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT user_a, user_b, status, created_at FROM friends WHERE user_a = @u OR user_b = @u";
            cmd.Parameters.AddWithValue("@u", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var a = r.GetInt32(0);
                var b = r.GetInt32(1);
                var status = r.GetString(2);
                var at = r.GetInt64(3);
                var other = a == userId ? b : a;
                var aToB = a == userId ? status : (map.TryGetValue(other, out var e) ? e.AToB : "none");
                var bToA = a == userId ? (map.TryGetValue(other, out var e2) ? e2.BToA : "none") : status;
                map[other] = new Relation(aToB, bToA, status == "pending" ? at : 0);
            }
            return map;
        });
    }

    public static FriendState ComputeState(Relation r)
    {
        if (r.AToB == "accepted" && r.BToA == "accepted") return FriendState.Mutual;
        if (r.AToB == "pending") return FriendState.Outgoing;
        if (r.BToA == "pending") return FriendState.Incoming;
        return FriendState.None;
    }

    public List<UserDto> GetFriends(int userId)
    {
        var relations = GetRelations(userId);
        var result = new List<UserDto>();
        foreach (var (otherId, rel) in relations)
        {
            var u = GetUser(otherId);
            if (u == null) continue;
            u.FriendState = ComputeState(rel);
            u.FriendRequestedAt = rel.RequestedAt;
            result.Add(u);
        }
        return result.OrderByDescending(x => x.FriendState == FriendState.Mutual)
                     .ThenByDescending(x => x.FriendState == FriendState.Incoming)
                     .ThenBy(x => x.Username)
                     .ToList();
    }

    public void SendFriendRequest(int userId, int targetId, string username, string? avatarUrl)
    {
        WithLock(_conn =>
        {
            UpsertUser(targetId, username, avatarUrl);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO friends(user_a, user_b, status, created_at)
                VALUES(@a, @b, 'pending', @n)
                ON CONFLICT(user_a, user_b) DO UPDATE SET status = 'pending'
                """;
            cmd.Parameters.AddWithValue("@a", userId);
            cmd.Parameters.AddWithValue("@b", targetId);
            cmd.Parameters.AddWithValue("@n", Now());
            cmd.ExecuteNonQuery();
            return true;
        });
    }

    public void RespondFriendRequest(int userId, int fromId, bool accept)
    {
        WithLock(_conn =>
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                UPDATE friends SET status = @s
                WHERE user_a = @from AND user_b = @me AND status = 'pending'
                """;
            cmd.Parameters.AddWithValue("@from", fromId);
            cmd.Parameters.AddWithValue("@me", userId);
            cmd.Parameters.AddWithValue("@s", accept ? "accepted" : "none");
            cmd.ExecuteNonQuery();

            if (accept)
            {
                using var cmd2 = _conn.CreateCommand();
                cmd2.CommandText = """
                    INSERT INTO friends(user_a, user_b, status, created_at)
                    VALUES(@me, @from, 'accepted', @n)
                    ON CONFLICT(user_a, user_b) DO UPDATE SET status = 'accepted'
                    """;
                cmd2.Parameters.AddWithValue("@me", userId);
                cmd2.Parameters.AddWithValue("@from", fromId);
                cmd2.Parameters.AddWithValue("@n", Now());
                cmd2.ExecuteNonQuery();
            }
            return true;
        });
    }

    public void RemoveFriend(int userId, int otherId)
    {
        WithLock(_conn =>
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                DELETE FROM friends WHERE (user_a = @a AND user_b = @b) OR (user_a = @b AND user_b = @a)
                """;
            cmd.Parameters.AddWithValue("@a", userId);
            cmd.Parameters.AddWithValue("@b", otherId);
            cmd.ExecuteNonQuery();
            return true;
        });
    }

    // ---------------- Conversations ----------------

    public ConversationDto? GetConversationForUser(int userId, string conversationId)
    {
        return WithLock(_conn =>
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM conversation_members WHERE conversation_id = @cid AND player_id = @uid";
            cmd.Parameters.AddWithValue("@cid", conversationId);
            cmd.Parameters.AddWithValue("@uid", userId);
            if (cmd.ExecuteScalar() == null) return null;

            using var cmd2 = _conn.CreateCommand();
            cmd2.CommandText = "SELECT id, type, name, last_message_at FROM conversations WHERE id = @cid";
            cmd2.Parameters.AddWithValue("@cid", conversationId);
            using var r = cmd2.ExecuteReader();
            if (!r.Read()) return null;

            var conv = new ConversationDto
            {
                Id = r.GetString(0),
                Type = r.GetString(1),
                Name = r.IsDBNull(2) ? null : r.GetString(2),
                LastMessageAt = r.GetInt64(3)
            };

            conv.Members = GetMembers(conversationId);
            conv.LastMessage = GetLastMessage(conversationId);
            if (conv.Type == "dm" && conv.Members.Count == 2)
            {
                var other = conv.Members.FirstOrDefault(m => m.PlayerId != userId);
                if (other != null)
                {
                    var relations = GetRelations(userId);
                    if (relations.TryGetValue(other.PlayerId, out var rel))
                        other.FriendState = ComputeState(rel);
                    conv.IsMutual = other.FriendState == FriendState.Mutual;
                }
            }
            conv.UnreadCount = GetUnreadCount(userId, conversationId);
            return conv;
        });
    }

    private List<UserDto> GetMembers(string conversationId)
    {
        var ids = new List<int>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT player_id FROM conversation_members WHERE conversation_id = @cid ORDER BY player_id";
            cmd.Parameters.AddWithValue("@cid", conversationId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) ids.Add(r.GetInt32(0));
        }

        var members = new List<UserDto>();
        foreach (var id in ids)
        {
            var u = GetUser(id);
            if (u == null) continue;
            u.FriendState = FriendState.None;
            members.Add(u);
        }
        return members;
    }

    private MessageDto? GetLastMessage(string conversationId) => GetMessagesInternal(conversationId, 1).FirstOrDefault();

    private List<MessageDto> GetMessagesInternal(string conversationId, int? limit = null)
    {
        var asc = "ORDER BY m.created_at ASC";
        if (limit.HasValue) asc = "ORDER BY m.created_at DESC LIMIT " + limit.Value;

        var list = new List<MessageDto>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT m.id, m.conversation_id, m.author_id, m.text, m.attachment_id, m.created_at,
                   COALESCE(u.username, ''), a.file_name, a.size_bytes
            FROM messages m
            LEFT JOIN users u ON u.player_id = m.author_id
            LEFT JOIN attachments a ON a.id = m.attachment_id
            WHERE m.conversation_id = @cid AND m.deleted_at IS NULL
            {asc}
            """;
        cmd.Parameters.AddWithValue("@cid", conversationId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var msg = new MessageDto
            {
                Id = r.GetString(0),
                ConversationId = r.GetString(1),
                AuthorId = r.GetInt32(2),
                Text = r.IsDBNull(3) ? null : r.GetString(3),
                CreatedAt = r.GetInt64(5),
                AuthorUsername = r.IsDBNull(6) ? "" : r.GetString(6)
            };
            if (!r.IsDBNull(4))
            {
                msg.Attachment = new AttachmentDto
                {
                    Id = r.GetString(4),
                    FileName = r.IsDBNull(7) ? "file" : r.GetString(7),
                    SizeBytes = r.IsDBNull(8) ? 0 : r.GetInt64(8)
                };
            }
            list.Add(msg);
        }
        if (limit.HasValue) list.Reverse();
        return list;
    }

    public List<MessageDto> GetMessages(string conversationId) => WithLock(_ => GetMessagesInternal(conversationId));

    private int GetUnreadCount(int userId, string conversationId)
    {
        long lastRead = 0;
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT last_read_at FROM read_state WHERE conversation_id = @cid AND player_id = @uid";
            cmd.Parameters.AddWithValue("@cid", conversationId);
            cmd.Parameters.AddWithValue("@uid", userId);
            var v = cmd.ExecuteScalar();
            if (v != null) lastRead = Convert.ToInt64(v);
        }

        using var cmd2 = _conn.CreateCommand();
        cmd2.CommandText = "SELECT COUNT(*) FROM messages WHERE conversation_id = @cid AND created_at > @lr AND deleted_at IS NULL";
        cmd2.Parameters.AddWithValue("@cid", conversationId);
        cmd2.Parameters.AddWithValue("@lr", lastRead);
        return Convert.ToInt32(cmd2.ExecuteScalar() ?? 0);
    }

    public List<ConversationDto> GetConversations(int userId)
    {
        var ids = new List<string>();
        WithLock(_conn =>
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT conversation_id FROM conversation_members WHERE player_id = @uid";
            cmd.Parameters.AddWithValue("@uid", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) ids.Add(r.GetString(0));
            return true;
        });

        var list = new List<ConversationDto>();
        foreach (var id in ids)
        {
            var c = GetConversationForUser(userId, id);
            if (c != null) list.Add(c);
        }
        return list;
    }

    public ConversationDto? GetOrCreateDm(int userId, int otherId)
    {
        return WithLock(_conn =>
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SELECT c.id FROM conversations c
                JOIN conversation_members m1 ON m1.conversation_id = c.id AND m1.player_id = @u
                JOIN conversation_members m2 ON m2.conversation_id = c.id AND m2.player_id = @o
                WHERE c.type = 'dm' LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@o", otherId);
            var existing = cmd.ExecuteScalar();
            if (existing != null)
                return GetConversationForUser(userId, existing.ToString()!);

            var cid = NewId();
            var now = Now();
            using (var ins = _conn.CreateCommand())
            {
                ins.CommandText = "INSERT INTO conversations(id, type, name, created_by, created_at, last_message_at) VALUES(@id, 'dm', NULL, @by, @n, 0)";
                ins.Parameters.AddWithValue("@id", cid);
                ins.Parameters.AddWithValue("@by", userId);
                ins.Parameters.AddWithValue("@n", now);
                ins.ExecuteNonQuery();
            }
            foreach (var member in new[] { userId, otherId })
            {
                using var m = _conn.CreateCommand();
                m.CommandText = "INSERT INTO conversation_members(conversation_id, player_id) VALUES(@cid, @pid)";
                m.Parameters.AddWithValue("@cid", cid);
                m.Parameters.AddWithValue("@pid", member);
                m.ExecuteNonQuery();
            }
            return GetConversationForUser(userId, cid);
        });
    }

    public ConversationDto? CreateGroup(int userId, List<int> memberIds, string? name)
    {
        var members = memberIds.Where(id => id != userId).Distinct().Take(50).ToList();
        members.Insert(0, userId);
        if (members.Count < 2) return null;

        var cid = NewId();
        var now = Now();
        return WithLock(_conn =>
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO conversations(id, type, name, created_by, created_at, last_message_at) VALUES(@id, 'group', @name, @by, @n, 0)";
                cmd.Parameters.AddWithValue("@id", cid);
                cmd.Parameters.AddWithValue("@name", (object?)CleanName(name));
                cmd.Parameters.AddWithValue("@by", userId);
                cmd.Parameters.AddWithValue("@n", now);
                cmd.ExecuteNonQuery();
            }
            foreach (var member in members)
            {
                using var m = _conn.CreateCommand();
                m.CommandText = "INSERT INTO conversation_members(conversation_id, player_id) VALUES(@cid, @pid)";
                m.Parameters.AddWithValue("@cid", cid);
                m.Parameters.AddWithValue("@pid", member);
                m.ExecuteNonQuery();
            }
            return GetConversationForUser(userId, cid);
        });
    }

    public bool AddMember(int userId, string conversationId, int playerId)
    {
        return WithLock(_conn =>
        {
            if (!IsMember(conversationId, userId)) return false;
            UpsertUser(playerId, "", null);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO conversation_members(conversation_id, player_id) VALUES(@cid, @pid)";
            cmd.Parameters.AddWithValue("@cid", conversationId);
            cmd.Parameters.AddWithValue("@pid", playerId);
            cmd.ExecuteNonQuery();
            return true;
        });
    }

    public bool RemoveMember(int userId, string conversationId, int playerId)
    {
        return WithLock(_conn =>
        {
            if (!IsMember(conversationId, userId)) return false;
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM conversation_members WHERE conversation_id = @cid AND player_id = @pid";
            cmd.Parameters.AddWithValue("@cid", conversationId);
            cmd.Parameters.AddWithValue("@pid", playerId);
            cmd.ExecuteNonQuery();
            return true;
        });
    }

    public bool LeaveGroup(int userId, string conversationId)
    {
        return WithLock(_conn =>
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM conversation_members WHERE conversation_id = @cid AND player_id = @pid";
            cmd.Parameters.AddWithValue("@cid", conversationId);
            cmd.Parameters.AddWithValue("@pid", userId);
            return cmd.ExecuteNonQuery() > 0;
        });
    }

    private bool IsMember(string conversationId, int playerId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM conversation_members WHERE conversation_id = @cid AND player_id = @pid";
        cmd.Parameters.AddWithValue("@cid", conversationId);
        cmd.Parameters.AddWithValue("@pid", playerId);
        return cmd.ExecuteScalar() != null;
    }

    // ---------------- Messages ----------------

    public MessageDto? AddMessage(int authorId, string conversationId, string? text, string? attachmentId)
    {
        var mid = NewId();
        var now = Now();
        return WithLock(_conn =>
        {
            if (!IsMember(conversationId, authorId)) return null;
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO messages(id, conversation_id, author_id, text, attachment_id, created_at)
                    VALUES(@id, @cid, @aid, @text, @att, @n)
                    """;
                cmd.Parameters.AddWithValue("@id", mid);
                cmd.Parameters.AddWithValue("@cid", conversationId);
                cmd.Parameters.AddWithValue("@aid", authorId);
                cmd.Parameters.AddWithValue("@text", string.IsNullOrWhiteSpace(text) ? DBNull.Value : text);
                cmd.Parameters.AddWithValue("@att", string.IsNullOrEmpty(attachmentId) ? DBNull.Value : attachmentId);
                cmd.Parameters.AddWithValue("@n", now);
                cmd.ExecuteNonQuery();
            }
            using (var upd = _conn.CreateCommand())
            {
                upd.CommandText = "UPDATE conversations SET last_message_at = @n WHERE id = @cid";
                upd.Parameters.AddWithValue("@n", now);
                upd.Parameters.AddWithValue("@cid", conversationId);
                upd.ExecuteNonQuery();
            }
            return GetMessagesInternal(conversationId).FirstOrDefault(m => m.Id == mid);
        });
    }

    public bool DeleteMessage(int userId, string conversationId, string messageId)
    {
        return WithLock(_conn =>
        {
            if (!IsMember(conversationId, userId)) return false;
            string? attachmentId = null;
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "SELECT author_id, attachment_id FROM messages WHERE id = @mid AND conversation_id = @cid";
                cmd.Parameters.AddWithValue("@mid", messageId);
                cmd.Parameters.AddWithValue("@cid", conversationId);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return false;
                if (r.GetInt32(0) != userId) return false;
                attachmentId = r.IsDBNull(1) ? null : r.GetString(1);
            }

            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE messages SET deleted_at = @n WHERE id = @mid";
                cmd.Parameters.AddWithValue("@mid", messageId);
                cmd.Parameters.AddWithValue("@n", Now());
                cmd.ExecuteNonQuery();
            }

            if (!string.IsNullOrEmpty(attachmentId))
            {
                string? stored = null;
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT stored_name FROM attachments WHERE id = @aid";
                    cmd.Parameters.AddWithValue("@aid", attachmentId);
                    stored = cmd.ExecuteScalar()?.ToString();
                }
                if (stored != null)
                {
                    try { File.Delete(Path.Combine(_uploadsDir, stored)); } catch { }
                }
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM attachments WHERE id = @aid";
                    cmd.Parameters.AddWithValue("@aid", attachmentId);
                    cmd.ExecuteNonQuery();
                }
            }
            return true;
        });
    }

    public void MarkRead(int userId, string conversationId)
    {
        WithLock(_conn =>
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO read_state(conversation_id, player_id, last_read_at)
                VALUES(@cid, @uid, @n)
                ON CONFLICT(conversation_id, player_id) DO UPDATE SET last_read_at = @n
                """;
            cmd.Parameters.AddWithValue("@cid", conversationId);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@n", Now());
            cmd.ExecuteNonQuery();
            return true;
        });
    }

    // ---------------- Attachments ----------------

    public AttachmentDto? AddAttachment(int uploaderId, string fileName, string storedName, long size)
    {
        var id = NewId();
        return WithLock(_conn =>
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO attachments(id, file_name, stored_name, size_bytes, uploader_id, created_at) VALUES(@id, @fn, @sn, @sz, @up, @n)";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@fn", CleanName(fileName));
            cmd.Parameters.AddWithValue("@sn", storedName);
            cmd.Parameters.AddWithValue("@sz", size);
            cmd.Parameters.AddWithValue("@up", uploaderId);
            cmd.Parameters.AddWithValue("@n", Now());
            cmd.ExecuteNonQuery();
            return new AttachmentDto { Id = id, FileName = CleanName(fileName), SizeBytes = size };
        });
    }

    public (string FileName, string StoredName)? GetAttachment(string attachmentId)
    {
        return WithLock(_conn =>
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT file_name, stored_name FROM attachments WHERE id = @aid";
            cmd.Parameters.AddWithValue("@aid", attachmentId);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ((string, string)?)(r.GetString(0), r.GetString(1)) : null;
        });
    }
}

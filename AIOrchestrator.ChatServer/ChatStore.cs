using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIOrchestrator.ChatServer;

public sealed class ChatStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath;
    private readonly string _adminKey;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly ChatDatabase _database;
    private readonly Dictionary<string, UserSession> _sessions = new(StringComparer.Ordinal);

    public ChatStore(string filePath, string adminKey)
    {
        _filePath = filePath;
        _adminKey = adminKey;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        _database = File.Exists(filePath)
            ? JsonSerializer.Deserialize<ChatDatabase>(File.ReadAllText(filePath)) ?? new ChatDatabase()
            : new ChatDatabase();
    }

    public bool IsAdminKeyValid(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return false;
        byte[] expected = Encoding.UTF8.GetBytes(_adminKey);
        byte[] actual = Encoding.UTF8.GetBytes(candidate);
        return expected.Length == actual.Length &&
               CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public async Task<AccessCodeResult> CreateAccessCodeAsync(string displayName, CancellationToken ct)
    {
        string code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var record = new AccessCodeRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            CodeHash = Hash(code),
            DisplayName = displayName.Trim(),
            ThreadId = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _gate.WaitAsync(ct);
        try
        {
            _database.AccessCodes.Add(record);
            _database.Threads.Add(new ChatThread
            {
                Id = record.ThreadId,
                DisplayName = record.DisplayName,
                AccessCodeId = record.Id
            });
            await SaveAsync(ct);
            return new AccessCodeResult(code, record.ThreadId, record.DisplayName);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UserSession?> CreateSessionAsync(string code, CancellationToken ct)
    {
        string hash = Hash(code.Trim());
        await _gate.WaitAsync(ct);
        try
        {
            AccessCodeRecord? access = _database.AccessCodes.FirstOrDefault(item =>
                FixedEquals(item.CodeHash, hash) && item.RevokedAt == null);
            if (access == null) return null;

            var session = new UserSession(
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                access.ThreadId,
                access.DisplayName,
                DateTimeOffset.UtcNow.AddDays(30));
            _sessions[session.Token] = session;
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public UserSession? FindSession(string? bearerToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken) ||
            !_sessions.TryGetValue(bearerToken, out UserSession? session) ||
            session.ExpiresAt <= DateTimeOffset.UtcNow)
            return null;
        return session;
    }

    public async Task<IReadOnlyList<ChatMessageRecord>> GetUserMessagesAsync(string threadId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ChatThread? thread = FindThread(threadId);
            if (thread == null) return Array.Empty<ChatMessageRecord>();
            foreach (ChatMessageRecord message in thread.Messages.Where(message => message.SenderRole == "admin"))
                message.IsRead = true;
            await SaveAsync(ct);
            return thread.Messages.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ChatMessageRecord> AddUserMessageAsync(string threadId, string content, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ChatThread thread = FindThread(threadId)
                ?? throw new InvalidOperationException("Không tìm thấy cuộc trò chuyện.");
            var message = new ChatMessageRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                SenderName = thread.DisplayName,
                SenderRole = "user",
                Content = content.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
                IsRead = false
            };
            thread.Messages.Add(message);
            thread.UpdatedAt = message.CreatedAt;
            await SaveAsync(ct);
            return message;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ChatThreadSummary>> ListThreadsAsync(bool unreadOnly, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return _database.Threads
                .Select(thread => new ChatThreadSummary(
                    thread.Id,
                    thread.DisplayName,
                    thread.UpdatedAt,
                    thread.Messages.Count(message => message.SenderRole == "user" && !message.IsRead),
                    thread.Messages.LastOrDefault()?.Content ?? "Chưa có tin nhắn"))
                .Where(thread => !unreadOnly || thread.UnreadCount > 0)
                .OrderByDescending(thread => thread.UpdatedAt)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ChatMessageRecord>?> GetAdminMessagesAsync(string threadId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ChatThread? thread = FindThread(threadId);
            if (thread == null) return null;
            foreach (ChatMessageRecord message in thread.Messages.Where(message => message.SenderRole == "user"))
                message.IsRead = true;
            await SaveAsync(ct);
            return thread.Messages.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ChatMessageRecord> AddAdminMessageAsync(
        string threadId, string senderName, string content, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ChatThread thread = FindThread(threadId)
                ?? throw new InvalidOperationException("Không tìm thấy cuộc trò chuyện.");
            var message = new ChatMessageRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                SenderName = senderName.Trim(),
                SenderRole = "admin",
                Content = content.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
                IsRead = false
            };
            thread.Messages.Add(message);
            thread.UpdatedAt = message.CreatedAt;
            await SaveAsync(ct);
            return message;
        }
        finally
        {
            _gate.Release();
        }
    }

    private ChatThread? FindThread(string threadId) =>
        _database.Threads.FirstOrDefault(thread => thread.Id == threadId);

    private async Task SaveAsync(CancellationToken ct)
    {
        string tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
            await JsonSerializer.SerializeAsync(stream, _database, _jsonOptions, ct);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedEquals(string left, string right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

public sealed record AccessCodeResult(string AccessCode, string ThreadId, string DisplayName);
public sealed record UserSession(string Token, string ThreadId, string DisplayName, DateTimeOffset ExpiresAt);
public sealed record ChatThreadSummary(
    string Id, string DisplayName, DateTimeOffset UpdatedAt, int UnreadCount, string Preview);

public sealed class ChatDatabase
{
    public List<AccessCodeRecord> AccessCodes { get; set; } = new();
    public List<ChatThread> Threads { get; set; } = new();
}

public sealed class AccessCodeRecord
{
    public string Id { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ThreadId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class ChatThread
{
    public string Id { get; set; } = "";
    public string AccessCodeId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ChatMessageRecord> Messages { get; set; } = new();
}

public sealed class ChatMessageRecord
{
    public string Id { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string SenderRole { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

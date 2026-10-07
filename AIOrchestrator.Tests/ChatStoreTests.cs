using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AIOrchestrator.ChatServer;
using Xunit;

namespace AIOrchestrator.Tests;

public class ChatStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "AIOrchestratorChatTests_" + Guid.NewGuid().ToString("N"));
    private readonly string _filePath;
    private const string AdminKey = "test-admin-key-with-at-least-32-characters";

    public ChatStoreTests()
    {
        Directory.CreateDirectory(_directory);
        _filePath = Path.Combine(_directory, "chat.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task ChatWorkflow_PersistsMessagesAndTracksUnreadState()
    {
        var store = new ChatStore(_filePath, AdminKey);
        Assert.True(store.IsAdminKeyValid(AdminKey));
        Assert.False(store.IsAdminKeyValid("incorrect"));

        AccessCodeResult access = await store.CreateAccessCodeAsync("Nguyễn An", CancellationToken.None);
        Assert.DoesNotContain(access.AccessCode, File.ReadAllText(_filePath));

        UserSession? session = await store.CreateSessionAsync(access.AccessCode, CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal(access.ThreadId, session!.ThreadId);
        Assert.Null(await store.CreateSessionAsync("invalid", CancellationToken.None));

        await store.AddUserMessageAsync(session.ThreadId, "Xin chào admin", CancellationToken.None);
        Assert.Equal(1, Assert.Single(await store.ListThreadsAsync(unreadOnly: true, CancellationToken.None)).UnreadCount);

        IReadOnlyList<ChatMessageRecord>? adminMessages =
            await store.GetAdminMessagesAsync(session.ThreadId, CancellationToken.None);
        Assert.Equal("Xin chào admin", Assert.Single(adminMessages!).Content);
        Assert.Empty(await store.ListThreadsAsync(unreadOnly: true, CancellationToken.None));

        await store.AddAdminMessageAsync(session.ThreadId, "Quản trị viên", "Tôi sẽ hỗ trợ bạn.", CancellationToken.None);
        IReadOnlyList<ChatMessageRecord> userMessages =
            await store.GetUserMessagesAsync(session.ThreadId, CancellationToken.None);
        Assert.Equal(2, userMessages.Count);
        Assert.Equal("Tôi sẽ hỗ trợ bạn.", userMessages[1].Content);

        var reloadedStore = new ChatStore(_filePath, AdminKey);
        IReadOnlyList<ChatThreadSummary> persistedThreads =
            await reloadedStore.ListThreadsAsync(unreadOnly: false, CancellationToken.None);
        IReadOnlyList<ChatMessageRecord> persistedMessages =
            (await reloadedStore.GetAdminMessagesAsync(persistedThreads[0].Id, CancellationToken.None))!;
        Assert.Equal(2, persistedMessages.Count);
    }
}

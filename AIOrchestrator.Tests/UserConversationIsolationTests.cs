using System.IO;
using AIOrchestrator.Models;
using AIOrchestrator.Services;
using Xunit;

namespace AIOrchestrator.Tests;

public sealed class UserConversationIsolationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"VinhAIUsers-{Guid.NewGuid():N}");

    [Fact]
    public void ForUser_OnlyListsAndLoadsThatAccountsConversations()
    {
        IConversationStore alice = JsonConversationStore.ForUser("alice", _root);
        IConversationStore bob = JsonConversationStore.ForUser("bob", _root);
        ConversationSession aliceSession = alice.Save(CreateSession("Alice's private search"));
        bob.Save(CreateSession("Bob's private search"));

        Assert.Single(alice.ListAll());
        Assert.Equal("Alice's private search", alice.ListAll()[0].Title);
        Assert.Null(bob.Load(aliceSession.Id));
        Assert.Equal("Bob's private search", Assert.Single(bob.ListAll()).Title);
    }

    [Fact]
    public void ForUser_TreatsUsernameCaseAsSameAccount()
    {
        IConversationStore lowerCase = JsonConversationStore.ForUser("alice", _root);
        IConversationStore upperCase = JsonConversationStore.ForUser("ALICE", _root);
        lowerCase.Save(CreateSession("Shared within same account"));

        Assert.Single(upperCase.ListAll());
    }

    [Fact]
    public void PersonalAssistantMemory_OnlyIncludesTheCurrentAccountsHistory()
    {
        IConversationStore alice = JsonConversationStore.ForUser("alice", _root);
        IConversationStore bob = JsonConversationStore.ForUser("bob", _root);
        alice.Save(CreateSession("Alice plan", "Tìm kế hoạch học tiếng Nhật", "Bắt đầu từ N5."));
        bob.Save(CreateSession("Bob secret", "Mật khẩu bí mật của Bob"));

        string aliceMemory = PersonalAssistantMemoryService.BuildContext(alice, null, "Alice");
        string bobMemory = PersonalAssistantMemoryService.BuildContext(bob, null, "Bob");

        Assert.Contains("Tìm kế hoạch học tiếng Nhật", aliceMemory);
        Assert.DoesNotContain("Mật khẩu bí mật của Bob", aliceMemory);
        Assert.Contains("Mật khẩu bí mật của Bob", bobMemory);
        Assert.DoesNotContain("Tìm kế hoạch học tiếng Nhật", bobMemory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static ConversationSession CreateSession(string title, params string[] messages)
    {
        var session = new ConversationSession { Title = title };
        session.Messages.Add(new ConversationMessage
        {
            Type = MessageType.User,
            SenderName = "Bạn",
            Content = messages.Length > 0 ? messages[0] : title
        });
        if (messages.Length > 1)
        {
            session.Messages.Add(new ConversationMessage
            {
                Type = MessageType.StepCompleted,
                SenderName = "Trợ lý",
                Content = messages[1]
            });
        }

        return session;
    }
}

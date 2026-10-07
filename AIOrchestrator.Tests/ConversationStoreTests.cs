using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIOrchestrator.Models;
using AIOrchestrator.Services;
using Xunit;

namespace AIOrchestrator.Tests
{
    /// <summary>
    /// Persistence + search behaviour of <see cref="JsonConversationStore"/>.
    /// Each test runs against its own temp directory, torn down afterwards.
    /// </summary>
    public class JsonConversationStoreTests : IDisposable
    {
        private readonly string _dir;
        private readonly JsonConversationStore _store;

        public JsonConversationStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "AIOrchTests_" + Guid.NewGuid().ToString("N"));
            _store = new JsonConversationStore(_dir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup; a leftover temp dir must not fail a test.
            }
        }

        private static ConversationSession MakeSession(string title, params string[] messageContents)
        {
            var session = new ConversationSession { Title = title };
            foreach (string content in messageContents)
            {
                session.Messages.Add(new ConversationMessage { SenderName = "Bạn", Content = content });
            }
            return session;
        }

        // ---------------------------------------------------------------------
        // Constructor / guards (negative)
        // ---------------------------------------------------------------------

        [Fact]
        public void Constructor_NullDirectory_Throws()
        {
            Assert.Throws<ArgumentException>(() => new JsonConversationStore(null!));
        }

        [Fact]
        public void Constructor_EmptyDirectory_Throws()
        {
            Assert.Throws<ArgumentException>(() => new JsonConversationStore("   "));
        }

        [Fact]
        public void Constructor_CreatesDirectoryIfMissing()
        {
            string dir = Path.Combine(_dir, "nested", "deeper");
            _ = new JsonConversationStore(dir);

            Assert.True(Directory.Exists(dir));
        }

        // ---------------------------------------------------------------------
        // Save + Load (positive)
        // ---------------------------------------------------------------------

        [Fact]
        public void Save_AssignsIdAndUpdatedAt()
        {
            var session = new ConversationSession { Title = "T", Id = "" };

            var saved = _store.Save(session);

            Assert.False(string.IsNullOrWhiteSpace(saved.Id));
            Assert.True(saved.UpdatedAt > DateTime.Now.AddMinutes(-1));
        }

        [Fact]
        public void SaveThenLoad_RoundTripsAllFields()
        {
            var session = MakeSession("Tiêu đề ABC", "câu hỏi một", "câu trả lời hai");
            session.Mode = "SmartRouter";
            session.FinalAnswer = "câu trả lời cuối cùng";
            session.FinalImagePath = "C:\\images\\chart.png";
            session.Messages[0].SenderIcon = "👤";
            session.Messages[0].Type = MessageType.User;
            session.Messages[0].FollowUpSection = "agent";
            session.Messages[0].FollowUpAgentId = "researcher_agent";

            var saved = _store.Save(session);
            var loaded = _store.Load(saved.Id);

            Assert.NotNull(loaded);
            Assert.Equal(saved.Id, loaded!.Id);
            Assert.Equal("Tiêu đề ABC", loaded.Title);
            Assert.Equal("SmartRouter", loaded.Mode);
            Assert.Equal("câu trả lời cuối cùng", loaded.FinalAnswer);
            Assert.Equal("C:\\images\\chart.png", loaded.FinalImagePath);
            Assert.Equal(2, loaded.Messages.Count);
            Assert.Equal("câu hỏi một", loaded.Messages[0].Content);
            Assert.Equal(MessageType.User, loaded.Messages[0].Type);
            Assert.Equal("agent", loaded.Messages[0].FollowUpSection);
            Assert.Equal("researcher_agent", loaded.Messages[0].FollowUpAgentId);
        }

        [Fact]
        public void Save_ReturnsSessionWithIdPersistedOnDisk()
        {
            var saved = _store.Save(MakeSession("T", "nội dung"));

            string expectedFile = Path.Combine(_dir, saved.Id + ".json");
            Assert.True(File.Exists(expectedFile));
        }

        [Fact]
        public void Save_SameIdTwice_OverwritesInsteadOfDuplicating()
        {
            var first = _store.Save(MakeSession("Ban đầu", "một"));
            first.Title = "Đã sửa";
            first.Messages.Add(new ConversationMessage { Content = "hai" });
            _store.Save(first);

            Assert.Single(_store.ListAll());
            var reloaded = _store.Load(first.Id);
            Assert.Equal("Đã sửa", reloaded!.Title);
            Assert.Equal(2, reloaded.Messages.Count);
        }

        [Fact]
        public void Save_NullSession_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _store.Save(null!));
        }

        [Fact]
        public void Save_MessageWithDefaultTimestamp_GetsStamped()
        {
            var session = new ConversationSession { Title = "T" };
            session.Messages.Add(new ConversationMessage { Content = "x", Timestamp = default });

            var saved = _store.Save(session);

            Assert.NotEqual(default, saved.Messages[0].Timestamp);
        }

        // ---------------------------------------------------------------------
        // Load: negative + edge
        // ---------------------------------------------------------------------

        [Fact]
        public void Load_UnknownId_ReturnsNull()
        {
            Assert.Null(_store.Load("khong_ton_tai"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Load_BlankId_ReturnsNull(string? id)
        {
            Assert.Null(_store.Load(id!));
        }

        [Fact]
        public void Load_CorruptJsonFile_ReturnsNullInsteadOfThrowing()
        {
            string path = Path.Combine(_dir, "broken.json");
            File.WriteAllText(path, "{ đây không phải json hợp lệ ");

            Assert.Null(_store.Load("broken"));
        }

        // ---------------------------------------------------------------------
        // ListAll
        // ---------------------------------------------------------------------

        [Fact]
        public void ListAll_EmptyStore_ReturnsEmpty()
        {
            Assert.Empty(_store.ListAll());
        }

        [Fact]
        public void ListAll_ReturnsNewestFirst()
        {
            var older = _store.Save(MakeSession("Cũ", "a"));
            older.UpdatedAt = DateTime.Now.AddHours(-2);
            // Persist the backdated timestamp directly.
            File.WriteAllText(Path.Combine(_dir, older.Id + ".json"),
                System.Text.Json.JsonSerializer.Serialize(older));

            var newer = _store.Save(MakeSession("Mới", "b"));

            var list = _store.ListAll();

            Assert.Equal(2, list.Count);
            Assert.Equal(newer.Id, list[0].Id);
        }

        [Fact]
        public void ListAll_SummarisesMessageCountAndPreview()
        {
            _store.Save(MakeSession("T", "một", "hai", "nội dung cuối"));

            var summary = _store.ListAll().Single();

            Assert.Equal(3, summary.MessageCount);
            Assert.Contains("nội dung cuối", summary.Preview);
        }

        [Fact]
        public void ListAll_IgnoresCorruptFiles()
        {
            _store.Save(MakeSession("Hợp lệ", "ok"));
            File.WriteAllText(Path.Combine(_dir, "rác.json"), "không phải json");

            Assert.Single(_store.ListAll());
        }

        [Fact]
        public void ListAll_IgnoresNonJsonFiles()
        {
            _store.Save(MakeSession("Hợp lệ", "ok"));
            File.WriteAllText(Path.Combine(_dir, "notes.txt"), "không phải cuộc trò chuyện");

            Assert.Single(_store.ListAll());
        }

        // ---------------------------------------------------------------------
        // Search
        // ---------------------------------------------------------------------

        [Fact]
        public void Search_MatchesMessageContent()
        {
            _store.Save(MakeSession("A", "hãy viết hàm fibonacci bằng C#"));
            _store.Save(MakeSession("B", "viết bài thơ về mùa thu"));

            var results = _store.Search("fibonacci");

            Assert.Single(results);
            Assert.Equal("A", results[0].Title);
        }

        [Fact]
        public void Search_MatchesTitle()
        {
            _store.Save(MakeSession("Thuật toán sắp xếp", "nội dung khác"));
            _store.Save(MakeSession("Bài thơ", "nội dung khác"));

            var results = _store.Search("sắp xếp");

            Assert.Single(results);
            Assert.Equal("Thuật toán sắp xếp", results[0].Title);
        }

        [Fact]
        public void Search_IsCaseInsensitive()
        {
            _store.Save(MakeSession("A", "Hàm FIBONACCI"));

            Assert.Single(_store.Search("fibonacci"));
            Assert.Single(_store.Search("Fibonacci"));
            Assert.Single(_store.Search("FiBoNaCcI"));
        }

        [Fact]
        public void Search_MatchesUnicodeVietnameseWithDiacritics()
        {
            _store.Save(MakeSession("A", "lập trình ứng dụng di động"));

            Assert.Single(_store.Search("ứng dụng"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Search_BlankQuery_ReturnsAll(string? query)
        {
            _store.Save(MakeSession("A", "x"));
            _store.Save(MakeSession("B", "y"));

            Assert.Equal(2, _store.Search(query).Count);
        }

        [Fact]
        public void Search_NoMatch_ReturnsEmpty()
        {
            _store.Save(MakeSession("A", "nội dung"));

            Assert.Empty(_store.Search("không-bao-giờ-xuất-hiện"));
        }

        [Fact]
        public void Search_FindsMultipleSessions()
        {
            _store.Save(MakeSession("A", "chủ đề AI"));
            _store.Save(MakeSession("B", "nói về AI agent"));
            _store.Save(MakeSession("C", "không liên quan"));

            Assert.Equal(2, _store.Search("AI").Count);
        }

        [Fact]
        public void Search_DoesNotMatchSenderNameOnly()
        {
            // Only content/title should drive matching; sender names are generic.
            _store.Save(MakeSession("A", "nội dung bình thường"));

            Assert.Empty(_store.Search("Nguyễn Văn Không Tồn Tại"));
        }

        // ---------------------------------------------------------------------
        // Delete
        // ---------------------------------------------------------------------

        [Fact]
        public void Delete_ExistingSession_ReturnsTrueAndRemovesIt()
        {
            var saved = _store.Save(MakeSession("T", "x"));

            Assert.True(_store.Delete(saved.Id));
            Assert.Null(_store.Load(saved.Id));
            Assert.Empty(_store.ListAll());
        }

        [Fact]
        public void Delete_UnknownId_ReturnsFalse()
        {
            Assert.False(_store.Delete("khong_ton_tai"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Delete_BlankId_ReturnsFalse(string id)
        {
            Assert.False(_store.Delete(id));
        }

        [Fact]
        public void DeleteAll_RemovesEverythingAndReturnsCount()
        {
            _store.Save(MakeSession("A", "x"));
            _store.Save(MakeSession("B", "y"));
            _store.Save(MakeSession("C", "z"));

            int removed = _store.DeleteAll();

            Assert.Equal(3, removed);
            Assert.Empty(_store.ListAll());
        }

        [Fact]
        public void DeleteAll_EmptyStore_ReturnsZero()
        {
            Assert.Equal(0, _store.DeleteAll());
        }

        // ---------------------------------------------------------------------
        // Id sanitisation (edge)
        // ---------------------------------------------------------------------

        [Fact]
        public void Save_IdWithInvalidPathCharacters_StillRoundTrips()
        {
            var session = new ConversationSession { Title = "T", Id = "a/b:c*?" };
            session.Messages.Add(new ConversationMessage { Content = "x" });

            var saved = _store.Save(session);
            var loaded = _store.Load(saved.Id);

            Assert.NotNull(loaded);
            Assert.Equal("a/b:c*?", loaded!.Id);
        }

        [Fact]
        public void DefaultDirectory_IsUnderLocalAppData()
        {
            string dir = JsonConversationStore.DefaultDirectory();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            Assert.StartsWith(appData, dir);
            Assert.Contains("AIOrchestrator", dir);
        }
    }
}

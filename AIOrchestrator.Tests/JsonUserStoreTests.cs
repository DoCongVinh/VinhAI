using System;
using System.IO;
using System.Linq;
using AIOrchestrator.Models;
using AIOrchestrator.Services;
using Xunit;

namespace AIOrchestrator.Tests
{
    /// <summary>
    /// Authentication + approval behaviour of <see cref="JsonUserStore"/>.
    /// Each test runs against its own temp directory, torn down afterwards.
    /// </summary>
    public class JsonUserStoreTests : IDisposable
    {
        private readonly string _dir;
        private readonly JsonUserStore _store;
        private int _registrationNumber;

        public JsonUserStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "VinhAIUserTests_" + Guid.NewGuid().ToString("N"));
            _store = new JsonUserStore(_dir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        private string? RegisterUser(string username, string password, string displayName)
        {
            int number = ++_registrationNumber;
            string normalizedUsername = username.Trim().ToLowerInvariant();
            return _store.Register(
                username,
                password,
                displayName,
                $"{normalizedUsername}{number}@example.com",
                $"+849000000{number:D2}");
        }

        // ---------------------------------------------------------------------
        // Constructor (negative)
        // ---------------------------------------------------------------------

        [Fact]
        public void Constructor_NullDirectory_Throws()
        {
            Assert.Throws<ArgumentException>(() => new JsonUserStore(null!));
        }

        [Fact]
        public void Constructor_EmptyDirectory_Throws()
        {
            Assert.Throws<ArgumentException>(() => new JsonUserStore("   "));
        }

        [Fact]
        public void Constructor_CreatesDirectoryIfMissing()
        {
            string dir = Path.Combine(_dir, "nested");
            _ = new JsonUserStore(dir);

            Assert.True(Directory.Exists(dir));
        }

        // ---------------------------------------------------------------------
        // Admin bootstrap (positive)
        // ---------------------------------------------------------------------

        [Fact]
        public void EnsureAdminAccount_CreatesApprovedAdmin()
        {
            _store.EnsureAdminAccount();

            var admin = _store.ListAll().Single();

            Assert.Equal("admin", admin.Username);
            Assert.Equal(UserRole.Admin, admin.Role);
            Assert.Equal(UserStatus.Approved, admin.Status);
            Assert.True(admin.IsAdmin);
        }

        [Fact]
        public void EnsureAdminAccount_IsIdempotent()
        {
            _store.EnsureAdminAccount();
            _store.EnsureAdminAccount();
            _store.EnsureAdminAccount();

            Assert.Single(_store.ListAll());
        }

        [Fact]
        public void Admin_CanLoginWithDefaultCredentials()
        {
            _store.EnsureAdminAccount();

            var result = _store.Login("admin", "Sonemcr123@");

            Assert.True(result.Success);
            Assert.NotNull(result.User);
            Assert.True(result.User!.IsAdmin);
        }

        [Fact]
        public void Password_IsNotStoredInPlainText()
        {
            _store.EnsureAdminAccount();

            string raw = File.ReadAllText(Path.Combine(_dir, "users.json"));

            Assert.DoesNotContain("Sonemcr123@", raw);
            Assert.Contains("PasswordHash", raw);
        }

        // ---------------------------------------------------------------------
        // Login (negative)
        // ---------------------------------------------------------------------

        [Fact]
        public void Login_WrongPassword_Fails()
        {
            _store.EnsureAdminAccount();

            var result = _store.Login("admin", "sai-mat-khau");

            Assert.False(result.Success);
            Assert.Null(result.User);
        }

        [Fact]
        public void Login_UnknownUser_Fails()
        {
            _store.EnsureAdminAccount();

            var result = _store.Login("khong_ton_tai", "whatever");

            Assert.False(result.Success);
        }

        [Theory]
        [InlineData("", "Sonemcr123@")]
        [InlineData("   ", "Sonemcr123@")]
        [InlineData("admin", "")]
        public void Login_BlankCredentials_Fail(string username, string password)
        {
            _store.EnsureAdminAccount();

            Assert.False(_store.Login(username, password).Success);
        }

        [Fact]
        public void Login_UsernameIsCaseInsensitive()
        {
            _store.EnsureAdminAccount();

            Assert.True(_store.Login("ADMIN", "Sonemcr123@").Success);
            Assert.True(_store.Login("Admin", "Sonemcr123@").Success);
        }

        [Fact]
        public void Login_PendingUser_IsRejectedWithPendingMessage()
        {
            _store.EnsureAdminAccount();
            RegisterUser("newbie", "matkhau123", "Người mới");

            var result = _store.Login("newbie", "matkhau123");

            Assert.False(result.Success);
            Assert.Contains("chờ", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ---------------------------------------------------------------------
        // Register (positive + negative + edge)
        // ---------------------------------------------------------------------

        [Fact]
        public void Register_ValidUser_StartsPending()
        {
            Assert.Null(RegisterUser("user1", "matkhau123", "Người dùng 1"));

            var user = _store.ListAll().Single(u => u.Username == "user1");
            Assert.Equal(UserStatus.Pending, user.Status);
            Assert.Equal(UserRole.User, user.Role);
            Assert.Equal("Người dùng 1", user.DisplayName);
        }

        [Fact]
        public void Register_MissingDisplayName_FallsBackToUsername()
        {
            RegisterUser("user2", "matkhau123", "   ");

            var user = _store.ListAll().Single(u => u.Username == "user2");
            Assert.Equal("user2", user.DisplayName);
        }

        [Fact]
        public void Register_DuplicateUsername_ReturnsError()
        {
            RegisterUser("user3", "matkhau123", "A");

            string? error = RegisterUser("USER3", "matkhau456", "B");

            Assert.NotNull(error);
            Assert.Single(_store.ListAll());
        }

        [Fact]
        public void Register_DuplicateEmail_ReturnsError()
        {
            Assert.Null(_store.Register("mailone", "matkhau123", "A", "same@example.com", "0901234567"));

            string? error = _store.Register("mailtwo", "matkhau123", "B", "SAME@example.com", "0907654321");

            Assert.Contains("Email", error);
            Assert.Single(_store.ListAll());
        }

        [Fact]
        public void Register_DuplicatePhoneNumber_ReturnsErrorAfterNormalization()
        {
            Assert.Null(_store.Register("phoneone", "matkhau123", "A", "one@example.com", "+84 901 234 567"));

            string? error = _store.Register("phonetwo", "matkhau123", "B", "two@example.com", "0901234567");

            Assert.Contains("điện thoại", error, StringComparison.OrdinalIgnoreCase);
            Assert.Single(_store.ListAll());
        }

        [Fact]
        public void Login_AcceptsEmailAndPhoneNumber()
        {
            _store.Register("contactuser", "matkhau123", "A", "contact@example.com", "0901234567");
            _store.Approve("contactuser");

            Assert.True(_store.Login("CONTACT@example.com", "matkhau123").Success);
            Assert.True(_store.Login("+84 901 234 567", "matkhau123").Success);
        }

        [Fact]
        public void ChangePassword_RequiresCurrentPasswordAndReplacesCredential()
        {
            _store.EnsureAdminAccount();

            Assert.False(_store.ChangePassword("admin", "wrong", "newpass123"));
            Assert.True(_store.ChangePassword("admin", "Sonemcr123@", "newpass123"));
            Assert.False(_store.Login("admin", "Sonemcr123@").Success);
            Assert.True(_store.Login("admin", "newpass123").Success);
        }

        [Fact]
        public void Register_ReservedAdminUsername_ReturnsError()
        {
            string? error = RegisterUser("admin", "matkhau123", "Kẻ mạo danh");

            Assert.NotNull(error);
        }

        [Theory]
        [InlineData("", "matkhau123")]      // empty username
        [InlineData("ab", "matkhau123")]    // too short
        [InlineData("user4", "12345")]      // password too short
        [InlineData("user4", "")]           // empty password
        public void Register_InvalidInput_ReturnsErrorAndStoresNothing(string username, string password)
        {
            string? error = RegisterUser(username, password, "X");

            Assert.NotNull(error);
            Assert.Empty(_store.ListAll());
        }

        [Fact]
        public void Register_TrimsUsername()
        {
            RegisterUser("  spaced  ", "matkhau123", "X");

            Assert.Single(_store.ListAll());
            Assert.Equal("spaced", _store.ListAll()[0].Username);
        }

        // ---------------------------------------------------------------------
        // Approval workflow (positive)
        // ---------------------------------------------------------------------

        [Fact]
        public void Approve_PendingUser_AllowsLogin()
        {
            RegisterUser("user5", "matkhau123", "User 5");

            Assert.True(_store.Approve("user5"));

            var result = _store.Login("user5", "matkhau123");
            Assert.True(result.Success);
            Assert.Equal(UserStatus.Approved, result.User!.Status);
        }

        [Fact]
        public void Approve_SetsApprovedAtTimestamp()
        {
            RegisterUser("user6", "matkhau123", "User 6");
            _store.Approve("user6");

            var user = _store.ListAll().Single(u => u.Username == "user6");
            Assert.NotNull(user.ApprovedAt);
        }

        [Fact]
        public void Reject_PendingUser_BlocksLogin()
        {
            RegisterUser("user7", "matkhau123", "User 7");

            Assert.True(_store.Reject("user7"));

            var result = _store.Login("user7", "matkhau123");
            Assert.False(result.Success);
        }

        [Fact]
        public void Reject_AfterApprove_BlocksPreviouslyValidUser()
        {
            RegisterUser("user8", "matkhau123", "User 8");
            _store.Approve("user8");
            Assert.True(_store.Login("user8", "matkhau123").Success);

            _store.Reject("user8");

            Assert.False(_store.Login("user8", "matkhau123").Success);
        }

        [Fact]
        public void Approve_UnknownUser_ReturnsFalse()
        {
            Assert.False(_store.Approve("khong_ton_tai"));
        }

        [Fact]
        public void Reject_UnknownUser_ReturnsFalse()
        {
            Assert.False(_store.Reject("khong_ton_tai"));
        }

        // ---------------------------------------------------------------------
        // Delete (negative + edge)
        // ---------------------------------------------------------------------

        [Fact]
        public void Delete_RegularUser_RemovesThem()
        {
            RegisterUser("user9", "matkhau123", "User 9");

            Assert.True(_store.Delete("user9"));
            Assert.Empty(_store.ListAll());
        }

        [Fact]
        public void Delete_Admin_IsRefused()
        {
            _store.EnsureAdminAccount();

            Assert.False(_store.Delete("admin"));
            Assert.Single(_store.ListAll());
        }

        [Fact]
        public void Delete_UnknownUser_ReturnsFalse()
        {
            Assert.False(_store.Delete("khong_ton_tai"));
        }

        // ---------------------------------------------------------------------
        // Persistence (edge)
        // ---------------------------------------------------------------------

        [Fact]
        public void Users_PersistAcrossStoreInstances()
        {
            RegisterUser("user10", "matkhau123", "User 10");
            _store.Approve("user10");

            var reopened = new JsonUserStore(_dir);

            Assert.True(reopened.Login("user10", "matkhau123").Success);
        }

        [Fact]
        public void ListAll_EmptyStore_ReturnsEmpty()
        {
            Assert.Empty(_store.ListAll());
        }

        [Fact]
        public void ListAll_NewestFirst()
        {
            RegisterUser("first", "matkhau123", "First");
            RegisterUser("second", "matkhau123", "Second");

            var list = _store.ListAll();

            Assert.Equal("second", list[0].Username);
        }

        [Fact]
        public void CorruptUsersFile_TreatedAsEmptyWithoutThrowing()
        {
            File.WriteAllText(Path.Combine(_dir, "users.json"), "{ không phải json ");

            Assert.Empty(_store.ListAll());
        }

        [Fact]
        public void CorruptUsersFile_RegisterRecoversAndWritesValidJson()
        {
            File.WriteAllText(Path.Combine(_dir, "users.json"), "rác");

            Assert.Null(RegisterUser("recovered", "matkhau123", "Recovered"));
            Assert.Single(_store.ListAll());
        }
    }
}
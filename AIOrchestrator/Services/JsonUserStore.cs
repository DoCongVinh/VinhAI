using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using AIOrchestrator.Models;

namespace AIOrchestrator.Services
{
    /// <summary>
    /// Lưu trữ tài khoản dưới dạng một file JSON trong thư mục dữ liệu của ứng dụng.
    /// Mật khẩu được băm bằng PBKDF2 (SHA-256, 100k vòng lặp) kèm salt ngẫu nhiên.
    /// Thư mục có thể truyền vào để test dùng thư mục tạm.
    /// </summary>
    public class JsonUserStore : IUserStore
    {
        /// <summary>Tên đăng nhập mặc định của quản trị viên.</summary>
        public const string DefaultAdminUsername = "admin";

        /// <summary>Mật khẩu mặc định của quản trị viên (chỉ dùng để tạo lần đầu).</summary>
        public const string DefaultAdminPassword = "Sonemcr123@";

        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100_000;

        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
        private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly string _directory;
        private readonly string _filePath;

        public JsonUserStore() : this(DefaultDirectory()) { }

        public JsonUserStore(string directory)
        {
            _directory = string.IsNullOrWhiteSpace(directory)
                ? throw new ArgumentException("Directory must not be empty.", nameof(directory))
                : directory;
            Directory.CreateDirectory(_directory);
            _filePath = Path.Combine(_directory, "users.json");
        }

        public static string DefaultDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "VinhAI");
        }

        // ---------------------------------------------------------------------
        // Admin bootstrap
        // ---------------------------------------------------------------------

        public void EnsureAdminAccount()
        {
            var users = ReadUsers();
            if (users.Any(u => string.Equals(u.Username, DefaultAdminUsername, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var (hash, salt) = HashPassword(DefaultAdminPassword);
            users.Add(new UserAccount
            {
                Username = DefaultAdminUsername,
                DisplayName = "Quản trị viên",
                PasswordHash = hash,
                PasswordSalt = salt,
                Role = UserRole.Admin,
                Status = UserStatus.Approved,
                CreatedAt = DateTime.Now,
                ApprovedAt = DateTime.Now
            });

            WriteUsers(users);
        }

        // ---------------------------------------------------------------------
        // Login / register
        // ---------------------------------------------------------------------

        public LoginResult Login(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                return LoginResult.Fail("Vui lòng nhập tên đăng nhập, email hoặc số điện thoại cùng mật khẩu.");
            }

            var user = Find(username);
            if (user == null || !VerifyPassword(password, user.PasswordHash, user.PasswordSalt))
            {
                return LoginResult.Fail("Tên đăng nhập hoặc mật khẩu không đúng.");
            }

            return user.Status switch
            {
                UserStatus.Pending => LoginResult.Fail("Tài khoản đang chờ quản trị viên duyệt."),
                UserStatus.Rejected => LoginResult.Fail("Tài khoản đã bị từ chối. Vui lòng liên hệ quản trị viên."),
                _ => LoginResult.Ok(user)
            };
        }

        public string? Register(string username, string password, string displayName, string email, string phoneNumber)
        {
            username = (username ?? "").Trim();
            displayName = (displayName ?? "").Trim();
            email = (email ?? "").Trim();
            phoneNumber = (phoneNumber ?? "").Trim();

            if (string.IsNullOrWhiteSpace(username))
                return "Tên đăng nhập không được để trống.";
            if (username.Length < 3)
                return "Tên đăng nhập phải có ít nhất 3 ký tự.";
            if (!IsValidEmail(email))
                return "Vui lòng nhập địa chỉ email hợp lệ.";
            if (!IsValidPhoneNumber(phoneNumber))
                return "Vui lòng nhập số điện thoại hợp lệ (10–15 chữ số).";
            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
                return "Mật khẩu phải có ít nhất 6 ký tự.";
            if (string.Equals(username, DefaultAdminUsername, StringComparison.OrdinalIgnoreCase))
                return "Tên đăng nhập này đã được sử dụng.";

            var users = ReadUsers();
            string normalizedPhoneNumber = NormalizePhoneNumber(phoneNumber);
            string normalizedUsernamePhone = NormalizePhoneNumber(username);
            if (users.Any(u =>
                    string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(u.Email, username, StringComparison.OrdinalIgnoreCase) ||
                    (normalizedUsernamePhone.Length > 0 &&
                     NormalizePhoneNumber(u.PhoneNumber) == normalizedUsernamePhone)))
                return "Tên đăng nhập đã tồn tại. Vui lòng chọn tên khác.";
            if (users.Any(u =>
                    string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(u.Username, email, StringComparison.OrdinalIgnoreCase)))
                return "Email đã được sử dụng.";
            if (users.Any(u =>
                    NormalizePhoneNumber(u.PhoneNumber) == normalizedPhoneNumber ||
                    NormalizePhoneNumber(u.Username) == normalizedPhoneNumber))
                return "Số điện thoại đã được sử dụng.";

            var (hash, salt) = HashPassword(password);
            users.Add(new UserAccount
            {
                Username = username,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName,
                Email = email,
                PhoneNumber = phoneNumber,
                PasswordHash = hash,
                PasswordSalt = salt,
                Role = UserRole.User,
                Status = UserStatus.Pending,
                CreatedAt = DateTime.Now
            });

            WriteUsers(users);
            return null;
        }

        public bool ChangePassword(string username, string currentPassword, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrEmpty(currentPassword) ||
                string.IsNullOrWhiteSpace(newPassword) ||
                newPassword.Length < 6)
            {
                return false;
            }

            var users = ReadUsers();
            var user = users.FirstOrDefault(u =>
                string.Equals(u.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));
            if (user == null || !VerifyPassword(currentPassword, user.PasswordHash, user.PasswordSalt))
                return false;

            var (hash, salt) = HashPassword(newPassword);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
            WriteUsers(users);
            return true;
        }

        public bool UpdateAvatarPath(string username, string avatarPath)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;

            var users = ReadUsers();
            var user = users.FirstOrDefault(u =>
                string.Equals(u.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));
            if (user == null) return false;

            user.AvatarPath = avatarPath?.Trim() ?? "";
            WriteUsers(users);
            return true;
        }

        // ---------------------------------------------------------------------
        // Admin operations
        // ---------------------------------------------------------------------

        public IReadOnlyList<UserAccount> ListAll()
        {
            return ReadUsers()
                .OrderByDescending(u => u.CreatedAt)
                .ToList();
        }

        public bool Approve(string username)
        {
            return UpdateStatus(username, UserStatus.Approved);
        }

        public bool Reject(string username)
        {
            return UpdateStatus(username, UserStatus.Rejected);
        }

        public bool Delete(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;
            if (string.Equals(username, DefaultAdminUsername, StringComparison.OrdinalIgnoreCase)) return false;

            var users = ReadUsers();
            int removed = users.RemoveAll(u =>
                string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return false;

            WriteUsers(users);
            return true;
        }

        // ---------------------------------------------------------------------
        // Internals
        // ---------------------------------------------------------------------

        private bool UpdateStatus(string username, UserStatus status)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;

            var users = ReadUsers();
            var user = users.FirstOrDefault(u =>
                string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
            if (user == null) return false;

            user.Status = status;
            user.ApprovedAt = status == UserStatus.Approved ? DateTime.Now : null;
            WriteUsers(users);
            return true;
        }

        private UserAccount? Find(string username)
        {
            string identifier = username?.Trim() ?? "";
            string normalizedPhone = NormalizePhoneNumber(identifier);
            return ReadUsers().FirstOrDefault(u =>
                string.Equals(u.Username, identifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(u.Email, identifier, StringComparison.OrdinalIgnoreCase) ||
                (normalizedPhone.Length > 0 &&
                 NormalizePhoneNumber(u.PhoneNumber) == normalizedPhone));
        }

        private static bool IsValidEmail(string email) =>
            email.Length <= 254 &&
            System.Net.Mail.MailAddress.TryCreate(email, out var address) &&
            string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);

        private static bool IsValidPhoneNumber(string phoneNumber)
        {
            string normalized = NormalizePhoneNumber(phoneNumber);
            return normalized.Length is >= 10 and <= 15 && normalized.All(char.IsDigit);
        }

        private static string NormalizePhoneNumber(string? phoneNumber)
        {
            string digits = new((phoneNumber ?? "").Where(char.IsDigit).ToArray());
            if (digits.StartsWith("0084", StringComparison.Ordinal) && digits.Length == 13)
                return "0" + digits[4..];
            if (digits.StartsWith("84", StringComparison.Ordinal) && digits.Length == 11)
                return "0" + digits[2..];
            return digits;
        }

        private List<UserAccount> ReadUsers()
        {
            if (!File.Exists(_filePath)) return new List<UserAccount>();

            try
            {
                string json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<UserAccount>>(json, ReadOptions)
                       ?? new List<UserAccount>();
            }
            catch (Exception)
            {
                // File hỏng không được làm sập ứng dụng; coi như chưa có tài khoản.
                return new List<UserAccount>();
            }
        }

        private void WriteUsers(List<UserAccount> users)
        {
            string json = JsonSerializer.Serialize(users, WriteOptions);
            string temp = _filePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _filePath, overwrite: true);
        }

        // ---------------------------------------------------------------------
        // Password hashing (PBKDF2)
        // ---------------------------------------------------------------------

        internal static (string Hash, string Salt) HashPassword(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
            return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
        }

        internal static bool VerifyPassword(string password, string hashBase64, string saltBase64)
        {
            if (string.IsNullOrEmpty(password) ||
                string.IsNullOrEmpty(hashBase64) ||
                string.IsNullOrEmpty(saltBase64))
            {
                return false;
            }

            try
            {
                byte[] salt = Convert.FromBase64String(saltBase64);
                byte[] expected = Convert.FromBase64String(hashBase64);
                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                    password, salt, Iterations, HashAlgorithmName.SHA256, expected.Length);

                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}

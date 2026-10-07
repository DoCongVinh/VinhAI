using System;

namespace AIOrchestrator.Models
{
    /// <summary>Vai trò của một tài khoản trong ứng dụng Vinh-AI.</summary>
    public enum UserRole
    {
        Admin,
        User
    }

    /// <summary>Trạng thái phê duyệt của tài khoản do người dùng tự đăng ký.</summary>
    public enum UserStatus
    {
        Pending,   // Chờ quản trị viên duyệt
        Approved,  // Đã được duyệt, có thể đăng nhập
        Rejected   // Bị từ chối
    }

    /// <summary>
    /// Một tài khoản người dùng. Mật khẩu không bao giờ được lưu ở dạng thô;
    /// chỉ lưu <see cref="PasswordHash"/> + <see cref="PasswordSalt"/> (PBKDF2).
    /// </summary>
    public class UserAccount
    {
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Email { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public string AvatarPath { get; set; } = "";

        /// <summary>Chỉ dùng cho mật khẩu đã băm (không lưu mật khẩu gốc).</summary>
        public string PasswordHash { get; set; } = "";

        /// <summary>Salt dạng Base64 dùng cho PBKDF2.</summary>
        public string PasswordSalt { get; set; } = "";

        public UserRole Role { get; set; } = UserRole.User;
        public UserStatus Status { get; set; } = UserStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ApprovedAt { get; set; }

        public bool IsAdmin => Role == UserRole.Admin;
        public bool CanLogin => Status == UserStatus.Approved;

        public string StatusDisplayName => Status switch
        {
            UserStatus.Pending => "⏳ Chờ duyệt",
            UserStatus.Approved => "✅ Đã duyệt",
            UserStatus.Rejected => "❌ Bị từ chối",
            _ => "Không rõ"
        };
    }
}

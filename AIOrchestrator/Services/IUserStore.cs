using System.Collections.Generic;
using AIOrchestrator.Models;

namespace AIOrchestrator.Services
{
    /// <summary>Kết quả của một lần đăng nhập.</summary>
    public class LoginResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public UserAccount? User { get; init; }

        public static LoginResult Ok(UserAccount user) =>
            new() { Success = true, User = user, Message = "Đăng nhập thành công." };

        public static LoginResult Fail(string message) =>
            new() { Success = false, Message = message };
    }

    /// <summary>
    /// Lưu trữ tài khoản người dùng và logic đăng nhập/đăng ký/duyệt.
    /// Tách thành interface để UI phụ thuộc vào hợp đồng và test dễ dàng.
    /// </summary>
    public interface IUserStore
    {
        /// <summary>Tài khoản quản trị viên mặc định (admin).</summary>
        const string AdminUsername = "admin";

        /// <summary>Tạo tài khoản admin mặc định nếu chưa tồn tại.</summary>
        void EnsureAdminAccount();

        /// <summary>Xác thực thông tin đăng nhập.</summary>
        LoginResult Login(string username, string password);

        /// <summary>
        /// Đăng ký tài khoản mới ở trạng thái chờ duyệt.
        /// Trả về thông báo lỗi, hoặc null nếu thành công.
        /// </summary>
        string? Register(string username, string password, string displayName, string email, string phoneNumber);

        /// <summary>Đổi mật khẩu sau khi xác thực mật khẩu hiện tại.</summary>
        bool ChangePassword(string username, string currentPassword, string newPassword);

        /// <summary>Cập nhật ảnh đại diện của tài khoản.</summary>
        bool UpdateAvatarPath(string username, string avatarPath);

        /// <summary>Toàn bộ tài khoản (mới nhất trước).</summary>
        IReadOnlyList<UserAccount> ListAll();

        /// <summary>Duyệt một tài khoản đang chờ.</summary>
        bool Approve(string username);

        /// <summary>Từ chối một tài khoản đang chờ.</summary>
        bool Reject(string username);

        /// <summary>Xóa tài khoản. Không thể xóa chính admin.</summary>
        bool Delete(string username);
    }
}

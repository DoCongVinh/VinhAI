using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AIOrchestrator.Models;
using AIOrchestrator.Services;

namespace AIOrchestrator.Views
{
    /// <summary>
    /// Cửa sổ đăng nhập / đăng ký của Vinh-AI.
    /// Chỉ khi đăng nhập thành công thì ứng dụng chính mới được mở.
    /// </summary>
    public partial class LoginWindow : Window
    {
        private readonly IUserStore _userStore;

        /// <summary>Tài khoản đăng nhập thành công (null nếu chưa đăng nhập).</summary>
        public UserAccount? AuthenticatedUser { get; private set; }

        public LoginWindow() : this(new JsonUserStore()) { }

        public LoginWindow(IUserStore userStore)
        {
            _userStore = userStore;
            InitializeComponent();

            _userStore.EnsureAdminAccount();
            SavedLoginStore.SavedLogin? savedLogin;
            try
            {
                savedLogin = SavedLoginStore.Load();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       JsonException or FormatException or CryptographicException or Win32Exception)
            {
                savedLogin = null;
                MessageBox.Show($"Không đọc được mật khẩu đã lưu. Hãy đăng nhập thủ công. Chi tiết: {ex.Message}",
                    "Thông tin đăng nhập đã lưu", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            if (savedLogin != null)
            {
                TxtLoginUsername.Text = savedLogin.Identifier;
                PwdLoginPassword.Password = savedLogin.Password;
                ChkRememberPassword.IsChecked = true;
            }
            Loaded += (_, _) =>
            {
                if (savedLogin == null)
                    TxtLoginUsername.Focus();
                else
                    PwdLoginPassword.Focus();
            };
        }

        private void Login_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                AttemptLogin();
            }
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e) => AttemptLogin();

        private void AttemptLogin()
        {
            var result = _userStore.Login(TxtLoginUsername.Text, PwdLoginPassword.Password);

            if (!result.Success)
            {
                ShowLoginError(result.Message);
                return;
            }

            try
            {
                if (ChkRememberPassword.IsChecked == true)
                    SavedLoginStore.Save(TxtLoginUsername.Text.Trim(), PwdLoginPassword.Password);
                else
                    SavedLoginStore.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể lưu thông tin đăng nhập trên máy này: {ex.Message}",
                    "Không lưu được mật khẩu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AuthenticatedUser = result.User;
            DialogResult = true;
            Close();
        }

        private void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            if (PwdRegPassword.Password != PwdRegConfirm.Password)
            {
                ShowRegisterMessage("Mật khẩu nhập lại không khớp.", isError: true);
                return;
            }

            string? error = _userStore.Register(
                TxtRegUsername.Text,
                PwdRegPassword.Password,
                TxtRegDisplayName.Text,
                TxtRegEmail.Text,
                TxtRegPhone.Text);

            if (error != null)
            {
                ShowRegisterMessage(error, isError: true);
                return;
            }

            ShowRegisterMessage(
                "Đăng ký thành công! Tài khoản đang chờ quản trị viên phê duyệt.", isError: false);

            TxtRegUsername.Clear();
            TxtRegDisplayName.Clear();
            PwdRegPassword.Clear();
            PwdRegConfirm.Clear();
        }

        private void ShowLoginError(string message)
        {
            TxtLoginError.Text = "❌ " + message;
            TxtLoginError.Visibility = Visibility.Visible;
        }

        private void ShowRegisterMessage(string message, bool isError)
        {
            TxtRegMessage.Text = (isError ? "❌ " : "✅ ") + message;
            TxtRegMessage.Foreground = new SolidColorBrush(
                isError ? Color.FromRgb(0xF8, 0x71, 0x71) : Color.FromRgb(0x34, 0xD3, 0x99));
            TxtRegMessage.Visibility = Visibility.Visible;
        }
    }
}
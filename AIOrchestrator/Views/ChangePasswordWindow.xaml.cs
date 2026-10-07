using System.Windows;
using AIOrchestrator.Services;

namespace AIOrchestrator.Views
{
    public partial class ChangePasswordWindow : Window
    {
        private readonly IUserStore _userStore;
        private readonly string _username;

        public string? NewPassword { get; private set; }

        public ChangePasswordWindow(IUserStore userStore, string username)
        {
            _userStore = userStore;
            _username = username;
            InitializeComponent();
            Loaded += (_, _) => PwdCurrent.Focus();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (PwdNew.Password.Length < 6)
            {
                ShowError("Mật khẩu mới phải có ít nhất 6 ký tự.");
                return;
            }

            if (PwdNew.Password != PwdConfirm.Password)
            {
                ShowError("Mật khẩu nhập lại không khớp.");
                return;
            }

            if (!_userStore.ChangePassword(_username, PwdCurrent.Password, PwdNew.Password))
            {
                ShowError("Mật khẩu hiện tại không đúng hoặc dữ liệu nhập chưa hợp lệ.");
                return;
            }

            NewPassword = PwdNew.Password;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        private void ShowError(string message)
        {
            TxtError.Text = message;
            TxtError.Visibility = Visibility.Visible;
        }
    }
}

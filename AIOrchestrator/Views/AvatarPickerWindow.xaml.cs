using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using AIOrchestrator.Models;
using AIOrchestrator.Services;
using Microsoft.Win32;

namespace AIOrchestrator.Views
{
    public partial class AvatarPickerWindow : Window
    {
        private const long MaxAvatarBytes = 5 * 1024 * 1024;
        private readonly IUserStore _userStore;
        private readonly UserAccount _user;
        private string _avatarPath;

        public AvatarPickerWindow(IUserStore userStore, UserAccount user)
        {
            _userStore = userStore;
            _user = user;
            _avatarPath = user.AvatarPath;
            InitializeComponent();

            TxtProfileName.Text = $"{user.DisplayName} (@{user.Username})";
            TxtProfileDetails.Text = $"{user.Email}\n{user.PhoneNumber}\n{user.Role}";
            ShowAvatar(_avatarPath);
        }

        private void BtnChoose_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Chọn ảnh đại diện",
                Filter = "Ảnh PNG, JPEG hoặc BMP|*.png;*.jpg;*.jpeg;*.bmp",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                var file = new FileInfo(dialog.FileName);
                if (file.Length > MaxAvatarBytes)
                {
                    MessageBox.Show("Ảnh đại diện phải nhỏ hơn 5 MB.", "Ảnh quá lớn",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(file.FullName);
                image.EndInit();
                image.Freeze();

                string avatarDirectory = Path.Combine(JsonUserStore.DefaultDirectory(), "avatars");
                Directory.CreateDirectory(avatarDirectory);
                string extension = file.Extension.ToLowerInvariant();
                string safeFileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_user.Username)));
                string destination = Path.Combine(avatarDirectory, $"{safeFileName}{extension}");
                File.Copy(file.FullName, destination, overwrite: true);

                _avatarPath = destination;
                if (!_userStore.UpdateAvatarPath(_user.Username, _avatarPath))
                    throw new InvalidOperationException("Không tìm thấy tài khoản để lưu ảnh đại diện.");

                _user.AvatarPath = _avatarPath;
                ImgAvatar.Source = image;
                ImgAvatar.Visibility = Visibility.Visible;
                TxtAvatarFallback.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể lưu ảnh đại diện: {ex.Message}", "Lỗi ảnh đại diện",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (!_userStore.UpdateAvatarPath(_user.Username, ""))
            {
                MessageBox.Show("Không tìm thấy tài khoản để cập nhật.", "Lỗi",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _avatarPath = "";
            _user.AvatarPath = "";
            ImgAvatar.Source = null;
            ImgAvatar.Visibility = Visibility.Collapsed;
            TxtAvatarFallback.Visibility = Visibility.Visible;
        }

        private void ShowAvatar(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                ImgAvatar.Source = image;
                ImgAvatar.Visibility = Visibility.Visible;
                TxtAvatarFallback.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể đọc ảnh đại diện: {ex.Message}", "Lỗi ảnh đại diện",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}

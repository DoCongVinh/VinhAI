using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIOrchestrator.Models;
using AIOrchestrator.Services;
using System.Media;
using System.Windows.Threading;

namespace AIOrchestrator.Views
{
    /// <summary>
    /// Màn hình quản trị: duyệt / từ chối / xóa tài khoản người dùng.
    /// Chỉ quản trị viên mới mở được cửa sổ này.
    /// </summary>
    public partial class AdminWindow : Window
    {
        private readonly IUserStore _userStore;
        private readonly DispatcherTimer _chatRefreshTimer;
        private readonly SemaphoreSlim _refreshGate = new(1, 1);
        private readonly CancellationTokenSource _lifetime = new();
        private AdminChatApiClient? _chatClient;
        private bool _isRefreshingThreads;
        private string? _selectedThreadId;
        private int _lastUnreadCount;

        public AdminWindow(IUserStore userStore, AppSettings settings)
        {
            _userStore = userStore;
            InitializeComponent();
            ReloadUsers();

            if (!string.IsNullOrWhiteSpace(settings.ChatServerUrl) &&
                !string.IsNullOrWhiteSpace(settings.ChatAdminApiKey))
            {
                try
                {
                    _chatClient = new AdminChatApiClient(settings.ChatServerUrl, settings.ChatAdminApiKey);
                }
                catch (ArgumentException ex)
                {
                    TxtUnreadNotice.Text = ex.Message;
                }
            }
            else
            {
                TxtUnreadNotice.Text = "Cấu hình Chat server URL và Admin API key trong Cài đặt.";
            }

            _chatRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _chatRefreshTimer.Tick += async (_, _) => await RefreshThreadsAsync();
            Loaded += async (_, _) =>
            {
                await RefreshThreadsAsync();
                _chatRefreshTimer.Start();
            };
            Closed += (_, _) =>
            {
                _chatRefreshTimer.Stop();
                _lifetime.Cancel();
            };
        }

        private void ReloadUsers()
        {
            ItemsUsers.ItemsSource = _userStore.ListAll();
        }

        private async Task RefreshThreadsAsync()
        {
            if (_chatClient == null || !await _refreshGate.WaitAsync(0)) return;
            try
            {
                string? previousThreadId = (ListChatThreads.SelectedItem as AdminChatThread)?.Id ?? _selectedThreadId;
                IReadOnlyList<AdminChatThread> threads = await _chatClient.ListThreadsAsync(
                    unreadOnly: false, _lifetime.Token);
                int unreadCount = threads.Sum(thread => thread.UnreadCount);
                if (unreadCount > _lastUnreadCount)
                    SystemSounds.Asterisk.Play();
                _lastUnreadCount = unreadCount;
                TabAdminMessages.Header = unreadCount == 0
                    ? "💬 Tin nhắn"
                    : $"💬 Tin nhắn ({unreadCount} chưa đọc)";
                TxtUnreadNotice.Text = unreadCount == 0
                    ? "Không có tin nhắn chưa đọc"
                    : $"🔔 {unreadCount} tin nhắn chưa đọc";

                _isRefreshingThreads = true;
                ListChatThreads.ItemsSource = threads;
                ListChatThreads.SelectedItem = threads.FirstOrDefault(thread => thread.Id == previousThreadId);
                _isRefreshingThreads = false;

                if (ListChatThreads.SelectedItem is AdminChatThread selected)
                    await LoadThreadMessagesAsync(selected.Id);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or
                                       ArgumentException or System.Text.Json.JsonException or TaskCanceledException)
            {
                _isRefreshingThreads = false;
                TxtUnreadNotice.Text = $"Không thể cập nhật hộp thư: {ex.Message}";
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        private async Task LoadThreadMessagesAsync(string threadId)
        {
            if (_chatClient == null) return;
            try
            {
                IReadOnlyList<AdminChatMessage> messages = await _chatClient.GetMessagesAsync(threadId, _lifetime.Token);
                ItemsAdminChat.ItemsSource = messages.ToList();
                ScrollAdminChat.ScrollToEnd();
                _selectedThreadId = threadId;
                TxtAdminReply.IsEnabled = true;
                BtnAdminReply.IsEnabled = true;
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or
                                       System.Text.Json.JsonException or TaskCanceledException)
            {
                TxtUnreadNotice.Text = $"Không thể mở cuộc trò chuyện: {ex.Message}";
            }
        }

        private async void ListChatThreads_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingThreads || ListChatThreads.SelectedItem is not AdminChatThread thread) return;
            await LoadThreadMessagesAsync(thread.Id);
            await RefreshThreadsAsync();
        }

        private async void BtnAdminReply_Click(object sender, RoutedEventArgs e)
        {
            if (_chatClient == null || string.IsNullOrWhiteSpace(_selectedThreadId)) return;
            string content = TxtAdminReply.Text.Trim();
            if (string.IsNullOrWhiteSpace(content)) return;

            BtnAdminReply.IsEnabled = false;
            try
            {
                await _chatClient.SendReplyAsync(
                    _selectedThreadId, "Quản trị viên", content, _lifetime.Token);
                TxtAdminReply.Clear();
                await LoadThreadMessagesAsync(_selectedThreadId);
                await RefreshThreadsAsync();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or
                                       System.Text.Json.JsonException or TaskCanceledException)
            {
                MessageBox.Show($"Không gửi được tin nhắn: {ex.Message}", "Lỗi chat",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnAdminReply.IsEnabled = !string.IsNullOrWhiteSpace(_selectedThreadId);
            }
        }

        private async void BtnCreateAccessCode_Click(object sender, RoutedEventArgs e)
        {
            if (_chatClient == null)
            {
                MessageBox.Show("Hãy cấu hình Chat server URL và Admin API key trong Cài đặt trước.",
                    "Chưa cấu hình chat server", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string displayName = TxtAccessCodeDisplayName.Text.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                MessageBox.Show("Nhập tên hiển thị của người dùng trước khi tạo mã.",
                    "Thiếu tên", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                AdminAccessCode accessCode = await _chatClient.CreateAccessCodeAsync(displayName, _lifetime.Token);
                TxtAccessCodeDisplayName.Clear();
                MessageBox.Show(
                    $"Mã truy cập chat của {accessCode.DisplayName}:\n\n{accessCode.AccessCode}\n\nHãy gửi mã này riêng cho người dùng. Mã chỉ được hiển thị lần này.",
                    "Đã tạo mã truy cập",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                await RefreshThreadsAsync();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or
                                       System.Text.Json.JsonException or TaskCanceledException)
            {
                MessageBox.Show($"Không thể tạo mã truy cập: {ex.Message}", "Lỗi chat",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnApprove_Click(object sender, RoutedEventArgs e)
        {
            if (TagOf(sender) is UserAccount user)
            {
                _userStore.Approve(user.Username);
                ReloadUsers();
            }
        }

        private void BtnReject_Click(object sender, RoutedEventArgs e)
        {
            if (TagOf(sender) is UserAccount user)
            {
                _userStore.Reject(user.Username);
                ReloadUsers();
            }
        }

        private void BtnDeleteUser_Click(object sender, RoutedEventArgs e)
        {
            if (TagOf(sender) is not UserAccount user) return;

            if (user.IsAdmin)
            {
                MessageBox.Show("Không thể xóa tài khoản quản trị viên.", "Thông báo",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show($"Xóa tài khoản '{user.Username}'?", "Xác nhận",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _userStore.Delete(user.Username);
            ReloadUsers();
        }

        private static UserAccount? TagOf(object sender) =>
            (sender as Button)?.Tag as UserAccount;
    }
}
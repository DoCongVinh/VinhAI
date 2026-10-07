using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AIOrchestrator.Models;
using AIOrchestrator.Services;

namespace AIOrchestrator.Views
{
    public partial class UserAdminChatWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _refreshTimer;
        private readonly SemaphoreSlim _refreshGate = new(1, 1);
        private readonly CancellationTokenSource _lifetime = new();
        private string? _sessionToken;
        private bool _connected;

        public UserAdminChatWindow(AppSettings settings, UserAccount user)
        {
            _settings = settings;
            InitializeComponent();
            Title = $"Liên hệ quản trị viên — {user.DisplayName}";
            ItemsMessages.ItemsSource = new List<AdminChatMessage>();
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _refreshTimer.Tick += async (_, _) => await RefreshMessagesAsync();
            Closed += (_, _) =>
            {
                _refreshTimer.Stop();
                _lifetime.Cancel();
            };
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_connected) return;
            if (string.IsNullOrWhiteSpace(_settings.ChatServerUrl))
            {
                ShowChatError("Chưa cấu hình URL chat server. Vui lòng liên hệ quản trị viên.");
                return;
            }
            if (string.IsNullOrWhiteSpace(TxtAccessCode.Password))
            {
                ShowChatError("Hãy nhập mã truy cập chat do quản trị viên cấp.");
                return;
            }

            BtnConnect.IsEnabled = false;
            TxtChatStatus.Text = "Đang kết nối...";
            try
            {
                UserChatSession session = await AdminChatApiClient.SendAnonymousAsync<UserChatSession>(
                    _settings.ChatServerUrl,
                    HttpMethod.Post,
                    "api/sessions",
                    new { accessCode = TxtAccessCode.Password },
                    _lifetime.Token);
                _sessionToken = session.Token;
                _connected = true;
                TxtAccessCode.IsEnabled = false;
                TxtMessage.IsEnabled = true;
                BtnSend.IsEnabled = true;
                TxtChatStatus.Text = $"Đã kết nối • {session.DisplayName}";
                await RefreshMessagesAsync();
                _refreshTimer.Start();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or ArgumentException)
            {
                ShowChatError(ex.Message);
            }
            catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
            {
                ShowChatError("Kết nối tới chat server đã hết thời gian chờ.");
            }
            catch (System.Text.Json.JsonException ex)
            {
                ShowChatError($"Chat server trả về dữ liệu không hợp lệ: {ex.Message}");
            }
            finally
            {
                BtnConnect.IsEnabled = true;
            }
        }

        private async void BtnSend_Click(object sender, RoutedEventArgs e) => await SendMessageAsync();

        private async void TxtMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.IsKeyDown(Key.LeftShift)) return;
            e.Handled = true;
            await SendMessageAsync();
        }

        private async Task SendMessageAsync()
        {
            string content = TxtMessage.Text.Trim();
            if (string.IsNullOrWhiteSpace(content) || _sessionToken == null) return;
            if (content.Length > 8000)
            {
                ShowChatError("Tin nhắn không được dài quá 8.000 ký tự.");
                return;
            }

            BtnSend.IsEnabled = false;
            try
            {
                await AdminChatApiClient.SendUserAsync<AdminChatMessage>(
                    _settings.ChatServerUrl,
                    _sessionToken,
                    HttpMethod.Post,
                    "api/messages",
                    new { content },
                    _lifetime.Token);
                TxtMessage.Clear();
                await RefreshMessagesAsync();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or ArgumentException)
            {
                ShowChatError(ex.Message);
            }
            catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
            {
                ShowChatError("Gửi tin nhắn hết thời gian chờ. Vui lòng thử lại.");
            }
            catch (System.Text.Json.JsonException ex)
            {
                ShowChatError($"Chat server trả về dữ liệu không hợp lệ: {ex.Message}");
            }
            finally
            {
                BtnSend.IsEnabled = _connected;
            }
        }

        private async Task RefreshMessagesAsync()
        {
            if (_sessionToken == null || !await _refreshGate.WaitAsync(0)) return;
            try
            {
                IReadOnlyList<AdminChatMessage> messages = await AdminChatApiClient.SendUserAsync<IReadOnlyList<AdminChatMessage>>(
                    _settings.ChatServerUrl,
                    _sessionToken,
                    HttpMethod.Get,
                    "api/messages",
                    null,
                    _lifetime.Token);
                ItemsMessages.ItemsSource = messages.ToList();
                ScrollMessages.ScrollToEnd();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException)
            {
                TxtChatStatus.Text = "Kết nối chat hết thời gian chờ.";
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or ArgumentException)
            {
                TxtChatStatus.Text = $"Mất kết nối: {ex.Message}";
            }
            catch (System.Text.Json.JsonException ex)
            {
                TxtChatStatus.Text = $"Dữ liệu chat server không hợp lệ: {ex.Message}";
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        private void ShowChatError(string message)
        {
            TxtChatStatus.Text = "Lỗi";
            MessageBox.Show(message, "Chat với quản trị viên",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal sealed record UserChatSession(
        string Token, string ThreadId, string DisplayName, DateTimeOffset ExpiresAt);
}

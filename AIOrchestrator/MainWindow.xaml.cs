using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Windows.Threading;
using AIOrchestrator.Models;
using AIOrchestrator.Services;
using AIOrchestrator.Views;
using Markdig;

namespace AIOrchestrator
{
    public partial class MainWindow : Window
    {
        private readonly StorageService _storageService = new();
        private IConversationStore? _conversationStore;
        private readonly IUserStore _userStore = new JsonUserStore();
        private readonly OrchestrationEngine _engine = new();
        private readonly PollinationsImageGenerationService _imageGenerationService = new();
        private readonly VinhAIUpdateService _updateService = new();
        private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };
        private readonly CancellationTokenSource _updateCancellation = new();
        private AppSettings _settings;
        private ObservableCollection<AiAgent> _agentsCollection = new();
        private ObservableCollection<ChatMessage> _chatMessages = new();
        private readonly ObservableCollection<ChatMessage> _finalFollowUpMessages = new();
        private readonly ObservableCollection<ChatMessage> _agentFollowUpMessages = new();
        private readonly ObservableCollection<ChatMessage> _logsFollowUpMessages = new();
        private readonly ObservableCollection<PromptAttachment> _promptAttachments = new();
        private readonly ObservableCollection<PromptAttachment> _replyAttachments = new();
        private CancellationTokenSource? _cts;
        private DispatcherTimer _timer;
        private Stopwatch _stopwatch = new();
        private string _latestMarkdownResult = "";
        private string _latestImagePath = "";

        private UserAccount? _currentUser;

        /// <summary>Phiên trò chuyện đang được mở (null khi chưa lưu lần nào).</summary>
        private ConversationSession? _currentSession;

        private TaskCompletionSource<string>? _userReplyTcs;
        private bool _updateCheckRunning;
        private readonly AsyncLocal<long?> _requestAccountGeneration = new();
        private long _accountGeneration;

        public MainWindow(UserAccount currentUser)
        {
            ArgumentNullException.ThrowIfNull(currentUser);
            InitializeComponent();

            _currentUser = currentUser;
            _conversationStore = JsonConversationStore.ForUser(currentUser.Username);
            ApplyUserIdentity();

            _settings = _storageService.LoadSettings();
            SetPersonalAssistantIdentity();
            foreach (AiAgent agent in _settings.Agents)
                agent.ResetExecutionState();
            AddMissingVisualAgents();

            // Setup timer
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _timer.Tick += (s, e) =>
            {
                TxtTimer.Text = $"{_stopwatch.Elapsed.TotalSeconds:F1}s";
            };

            // Setup Engine events
            _engine.OnLogMessage += Engine_OnLogMessage;
            _engine.OnAgentUpdated += Engine_OnAgentUpdated;
            _engine.OnChatMessage += Engine_OnChatMessage;

            LoadAgentsToUi();
            UpdateDirectAgentPicker();
            PopulateFollowUpAgentPickers();

            // Bind chat messages
            ItemsChatMessages.ItemsSource = _chatMessages;
            ItemsFinalFollowUp.ItemsSource = _finalFollowUpMessages;
            ItemsAgentFollowUp.ItemsSource = _agentFollowUpMessages;
            ItemsLogsFollowUp.ItemsSource = _logsFollowUpMessages;
            ItemsPromptAttachments.ItemsSource = _promptAttachments;
            ItemsReplyAttachments.ItemsSource = _replyAttachments;

            StartNewConversation();
            RefreshHistoryList();
            Closing += MainWindow_Closing;
            Loaded += MainWindow_Loaded;
            _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync();
        }

        private void AddMissingVisualAgents()
        {
            var existingIds = _settings.Agents.Select(agent => agent.Id).ToHashSet(StringComparer.Ordinal);
            var missingAgents = AppSettings.CreateDefault().Agents
                .Where(agent => agent.Id != "router_agent" &&
                                !existingIds.Contains(agent.Id) &&
                                agent.Id is ("chart_agent" or "graphic_designer_agent" or
                                    "office_agent" or "video_agent" or "presentation_agent" or
                                    "object_analysis_agent" or "personal_assistant_agent"))
                .ToList();

            if (missingAgents.Count == 0) return;

            _settings.Agents.AddRange(missingAgents);
            _storageService.SaveSettings(_settings);
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _updateTimer.Stop();
            _updateCancellation.Cancel();
            PersistCurrentSession();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _updateTimer.Start();
            await CheckForUpdatesAsync();
        }

        private async Task CheckForUpdatesAsync()
        {
            if (_updateCheckRunning || string.IsNullOrWhiteSpace(_settings.UpdateManifestUrl))
                return;

            _updateCheckRunning = true;
            try
            {
                VinhAIUpdateManifest? update = await _updateService.CheckForUpdateAsync(
                    _settings.UpdateManifestUrl, _updateCancellation.Token);
                if (update == null ||
                    string.Equals(_settings.SkippedUpdateVersion, update.Version.ToString(), StringComparison.OrdinalIgnoreCase))
                    return;

                string? executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath) ||
                    !VinhAIUpdateService.IsSupportedExecutablePath(executablePath))
                {
                    TxtStatusSummary.Text = "Có bản cập nhật mới nhưng không thể thay thế tệp ứng dụng hiện tại.";
                    return;
                }

                string notes = string.IsNullOrWhiteSpace(update.ReleaseNotes)
                    ? "Không có ghi chú phát hành."
                    : update.ReleaseNotes.Trim();
                MessageBoxResult choice = MessageBox.Show(
                    this,
                    $"Có phiên bản Vinh-AI {update.Version} (hiện tại {VinhAIUpdateService.CurrentVersion}).\n\n" +
                    $"{notes}\n\nBạn có muốn tải và cài đặt không? Ứng dụng chỉ cập nhật sau khi bạn chọn Có.",
                    "Có bản cập nhật Vinh-AI",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (choice != MessageBoxResult.Yes)
                {
                    _settings.SkippedUpdateVersion = update.Version.ToString();
                    _storageService.SaveSettings(_settings);
                    TxtStatusSummary.Text = $"Đã bỏ qua bản cập nhật Vinh-AI {update.Version}.";
                    return;
                }

                TxtStatusSummary.Text = $"Đang tải Vinh-AI {update.Version}...";
                string stagedPath = await _updateService.DownloadAndVerifyAsync(
                    update, Path.GetTempPath(), _updateCancellation.Token);
                try
                {
                    VinhAIUpdateService.StartVerifiedUpdate(
                        stagedPath, executablePath, Process.GetCurrentProcess().Id);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    File.Delete(stagedPath);
                    throw;
                }

                MessageBox.Show(
                    this,
                    "Tệp cập nhật đã được tải và kiểm tra an toàn. Vinh-AI sẽ đóng, thay thế chương trình và mở lại.",
                    "Đang cập nhật",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Close();
            }
            catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or
                                       JsonException or InvalidOperationException or UnauthorizedAccessException or
                                       System.ComponentModel.Win32Exception or TaskCanceledException)
            {
                TxtStatusSummary.Text = $"Không thể kiểm tra/cài đặt bản cập nhật: {ex.Message}";
            }
            finally
            {
                _updateCheckRunning = false;
            }
        }

        private void ApplyUserIdentity()
        {
            MainContentArea.IsEnabled = _currentUser != null;

            if (_currentUser != null)
            {
                TxtCurrentUser.Text = $"{(_currentUser.IsAdmin ? "🛡️" : "👤")} {_currentUser.DisplayName} ({_currentUser.Role})";
                UpdateAccountAvatar();
                BtnAdminPanel.Visibility = _currentUser.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
                BtnContactAdmin.Visibility = _currentUser.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
                BtnChangePassword.Visibility = Visibility.Visible;
                BtnLogout.Content = "🚪 Đăng xuất";
                RefreshHistoryList(TxtSearchBox.Text);
            }
            else
            {
                TxtCurrentUser.Text = "👤 Khách (chưa đăng nhập)";
                ImgAccountAvatar.Source = null;
                ImgAccountAvatar.Visibility = Visibility.Collapsed;
                TxtAccountAvatarFallback.Visibility = Visibility.Visible;
                BtnAdminPanel.Visibility = Visibility.Collapsed;
                BtnContactAdmin.Visibility = Visibility.Collapsed;
                BtnChangePassword.Visibility = Visibility.Collapsed;
                BtnLogout.Content = "🔐 Đăng nhập";
                ItemsHistoryList.ItemsSource = Array.Empty<ConversationSummary>();
            }
        }

        // ---------------------------------------------------------------------
        // Conversation history, search & new chat
        // ---------------------------------------------------------------------

        /// <summary>Bắt đầu một cuộc trò chuyện mới, xoá trạng thái phiên hiện tại.</summary>
        private void StartNewConversation()
        {
            _currentSession = null;
            _latestMarkdownResult = "";
            _latestImagePath = "";
            _chatMessages.Clear();
            _finalFollowUpMessages.Clear();
            _agentFollowUpMessages.Clear();
            _logsFollowUpMessages.Clear();

            _chatMessages.Add(new ChatMessage
            {
                SenderName = "Hệ thống Vinh-AI",
                SenderIcon = "⚡",
                Content = _currentUser == null
                    ? "Chào bạn! Tôi là trợ lý điều phối đa AI của Vinh-AI. Hãy đăng nhập để bắt đầu cuộc trò chuyện riêng tư của bạn."
                    : $"Chào {_settings.AssistantUserDisplayName}! Tôi là trợ lý riêng của bạn. Tôi có thể dùng ngữ cảnh các cuộc trò chuyện trước của chính tài khoản này để bạn không phải nhắc lại yêu cầu cũ.",
                Type = MessageType.SystemInfo,
                QuickSuggestions = new List<string> { "Lập trình C#", "Viết bài sáng tạo", "So sánh công nghệ" }
            });

            RenderMarkdownView(_currentUser == null
                ? "# Chào mừng bạn đến với Vinh-AI!\n\nĐăng nhập để bắt đầu cuộc trò chuyện riêng của bạn."
                : $"# Chào mừng {_settings.AssistantUserDisplayName}!\n\nNhập yêu cầu, chọn một mẫu nhanh hoặc chọn **Trợ lý riêng của bạn** để tiếp tục dựa trên các cuộc trò chuyện trước.");
            TxtStatusSummary.Text = "🆕 Đã tạo cuộc trò chuyện mới.";
        }

        /// <summary>Lưu phiên hiện tại xuống đĩa (nếu có nội dung).</summary>
        private void PersistCurrentSession()
        {
            if (_currentUser == null || _conversationStore == null || _chatMessages.Count == 0) return;
            if (_currentSession == null && !_chatMessages.Any(m => m.Type == MessageType.User)) return;

            try
            {
                if (_currentSession == null)
                {
                    var firstUserMessage = _chatMessages.FirstOrDefault(m => m.Type == MessageType.User);
                    _currentSession = new ConversationSession
                    {
                        Title = ConversationSession.BuildTitle(firstUserMessage?.Content),
                        Mode = GetSelectedMode().ToString()
                    };
                }

                _currentSession.Messages = _chatMessages
                    .Select(ConversationMessage.FromChatMessage)
                    .ToList();
                _currentSession.Mode = GetSelectedMode().ToString();
                    _currentSession.FinalImagePath = _latestImagePath;
                    if (!string.IsNullOrWhiteSpace(_latestMarkdownResult))
                        _currentSession.FinalAnswer = _latestMarkdownResult;

                _conversationStore.Save(_currentSession);
                RefreshHistoryList();
            }
            catch (Exception ex)
            {
                TxtStatusSummary.Text = $"⚠️ Không thể lưu lịch sử trò chuyện: {ex.Message}";
            }
        }

        private ExecutionMode GetSelectedMode()
        {
            if (RbCustomPipeline.IsChecked == true) return ExecutionMode.CustomPipeline;
            if (RbDirectAgent.IsChecked == true) return ExecutionMode.DirectAgent;
            return ExecutionMode.SmartRouter;
        }

        private void RefreshHistoryList(string? query = null)
        {
            if (_currentUser == null || _conversationStore == null)
            {
                ItemsHistoryList.ItemsSource = Array.Empty<ConversationSummary>();
                return;
            }

            ItemsHistoryList.ItemsSource = string.IsNullOrWhiteSpace(query)
                ? _conversationStore.ListAll()
                : _conversationStore.Search(query);
        }

        private void BtnNewChat_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAuthenticated()) return;

            PersistCurrentSession();
            StartNewConversation();
        }

        private void TxtSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ItemsHistoryList == null) return;
            if (!EnsureAuthenticated()) return;
            RefreshHistoryList(TxtSearchBox.Text);
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAuthenticated()) return;
            RefreshHistoryList(TxtSearchBox.Text);
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAuthenticated()) return;
            TxtSearchBox.Clear();
            RefreshHistoryList();
        }

        private void BtnHistoryRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAuthenticated()) return;
            RefreshHistoryList(TxtSearchBox.Text);
        }

        private void BtnHistoryItem_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAuthenticated()) return;
            if (sender is not Button btn || btn.Tag is not ConversationSummary summary) return;

            PersistCurrentSession();

            var session = _conversationStore?.Load(summary.Id);
            if (session == null)
            {
                MessageBox.Show("Không thể mở cuộc trò chuyện này.", "Thông báo",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OpenSession(session);
        }

        private void OpenSession(ConversationSession session)
        {
            _currentSession = session;
            _chatMessages.Clear();
            _finalFollowUpMessages.Clear();
            _agentFollowUpMessages.Clear();
            _logsFollowUpMessages.Clear();

            foreach (var message in session.Messages)
            {
                ChatMessage chatMessage = message.ToChatMessage();
                _chatMessages.Add(chatMessage);
                switch (chatMessage.FollowUpSection)
                {
                    case "final":
                        _finalFollowUpMessages.Add(chatMessage);
                        break;
                    case "agent":
                        _agentFollowUpMessages.Add(chatMessage);
                        break;
                    case "logs":
                        _logsFollowUpMessages.Add(chatMessage);
                        break;
                }
            }

            RestoreFollowUpAgent(CmbAgentFollowUp, session.Messages, "agent");
            RestoreFollowUpAgent(CmbLogsFollowUp, session.Messages, "logs");
            _latestMarkdownResult = !string.IsNullOrWhiteSpace(session.FinalAnswer)
                ? session.FinalAnswer
                : session.Messages.LastOrDefault(m =>
                    m.Type is MessageType.StepCompleted or MessageType.Agent)?.Content ?? "";
            _latestImagePath = File.Exists(session.FinalImagePath) ? session.FinalImagePath : "";
            TxtRawOutput.Text = _latestMarkdownResult;
            RbRenderedView.IsChecked = true;
            RenderMarkdownView(_latestMarkdownResult);
            TxtStatusSummary.Text = $"📂 Đã mở: {session.Title}";
        }

        private static void RestoreFollowUpAgent(
            ComboBox picker, IEnumerable<ConversationMessage> messages, string section)
        {
            string? agentId = messages.LastOrDefault(message =>
                message.FollowUpSection == section && !string.IsNullOrWhiteSpace(message.FollowUpAgentId))
                ?.FollowUpAgentId;
            if (agentId == null) return;

            picker.SelectedItem = picker.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), agentId, StringComparison.Ordinal));
        }

        private void BtnRenameHistory_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAuthenticated() || _conversationStore is not { } conversationStore) return;
            if (sender is not Button btn || btn.Tag is not ConversationSummary summary) return;

            var dialog = new RenameConversationWindow(summary.Title) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            var session = conversationStore.Load(summary.Id);
            if (session == null)
            {
                MessageBox.Show("Không thể tìm thấy cuộc trò chuyện cần đổi tên.", "Thông báo",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshHistoryList(TxtSearchBox.Text);
                return;
            }

            session.Title = dialog.ConversationTitle;
            try
            {
                conversationStore.Save(session);
                if (_currentSession?.Id == session.Id)
                    _currentSession.Title = session.Title;
                RefreshHistoryList(TxtSearchBox.Text);
                TxtStatusSummary.Text = "✏️ Đã đổi tên cuộc trò chuyện.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể đổi tên cuộc trò chuyện: {ex.Message}", "Lỗi lưu lịch sử",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDeleteHistory_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAuthenticated()) return;
            if (sender is not Button btn || btn.Tag is not ConversationSummary summary) return;

            var confirm = MessageBox.Show($"Xóa cuộc trò chuyện '{summary.Title}'?", "Xác nhận xóa",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _conversationStore?.Delete(summary.Id);
            if (_currentSession?.Id == summary.Id) _currentSession = null;
            RefreshHistoryList(TxtSearchBox.Text);
            TxtStatusSummary.Text = "🗑️ Đã xóa cuộc trò chuyện.";
        }

        private void BtnAccount_Click(object sender, RoutedEventArgs e)
        {
            // Nếu chưa đăng nhập, mở màn hình đăng nhập.
            if (_currentUser == null)
            {
                EnsureAuthenticated();
                return;
            }

            var profile = new AvatarPickerWindow(_userStore, _currentUser) { Owner = this };
            profile.ShowDialog();
            UpdateAccountAvatar();
        }

        private void UpdateAccountAvatar()
        {
            if (_currentUser == null ||
                string.IsNullOrWhiteSpace(_currentUser.AvatarPath) ||
                !File.Exists(_currentUser.AvatarPath))
            {
                ImgAccountAvatar.Source = null;
                ImgAccountAvatar.Visibility = Visibility.Collapsed;
                TxtAccountAvatarFallback.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                var avatar = new BitmapImage();
                avatar.BeginInit();
                avatar.CacheOption = BitmapCacheOption.OnLoad;
                avatar.UriSource = new Uri(_currentUser.AvatarPath, UriKind.Absolute);
                avatar.EndInit();
                avatar.Freeze();
                ImgAccountAvatar.Source = avatar;
                ImgAccountAvatar.Visibility = Visibility.Visible;
                TxtAccountAvatarFallback.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                TxtStatusSummary.Text = $"Không thể tải ảnh đại diện: {ex.Message}";
            }
        }

        private void BtnChangePassword_Click(object sender, RoutedEventArgs e)
        {
            if (_currentUser == null) return;

            var dialog = new ChangePasswordWindow(_userStore, _currentUser.Username) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.NewPassword == null) return;

            try
            {
                var savedLogin = SavedLoginStore.Load();
                if (savedLogin != null && IsCurrentUserIdentifier(savedLogin.Identifier, _currentUser))
                    SavedLoginStore.Save(savedLogin.Identifier, dialog.NewPassword);
                MessageBox.Show("Đã đổi mật khẩu thành công.", "Tài khoản",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Đã đổi mật khẩu nhưng không thể cập nhật mật khẩu đã lưu: {ex.Message}",
                    "Đổi mật khẩu", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static bool IsCurrentUserIdentifier(string identifier, UserAccount user)
        {
            if (string.Equals(identifier, user.Username, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(identifier, user.Email, StringComparison.OrdinalIgnoreCase))
                return true;

            string savedPhone = NormalizePhoneNumber(identifier);
            string userPhone = NormalizePhoneNumber(user.PhoneNumber);
            return savedPhone.Length > 0 && savedPhone == userPhone;
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

        private void BtnAdminPanel_Click(object sender, RoutedEventArgs e)
        {
            if (_currentUser?.IsAdmin != true)
            {
                MessageBox.Show("Chỉ quản trị viên mới có quyền truy cập.", "Thông báo",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            new AdminWindow(_userStore, _settings) { Owner = this }.ShowDialog();
        }

        private void BtnContactAdmin_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAuthenticated() || _currentUser == null) return;
            new UserAdminChatWindow(_settings, _currentUser) { Owner = this }.ShowDialog();
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            // Chưa đăng nhập -> nút này đóng vai trò "Đăng nhập".
            if (_currentUser == null)
            {
                BtnAccount_Click(sender, e);
                return;
            }

            var confirm = MessageBox.Show("Đăng xuất khỏi tài khoản hiện tại?", "Xác nhận",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            Interlocked.Increment(ref _accountGeneration);
            _cts?.Cancel();
            if (_userReplyTcs != null && !_userReplyTcs.Task.IsCompleted)
            {
                _userReplyTcs.TrySetCanceled();
                _userReplyTcs = null;
            }

            PersistCurrentSession();
            _currentUser = null;
            _conversationStore = null;
            _settings.AssistantUserDisplayName = "";
            _settings.PersonalAssistantMemoryContext = "";
            foreach (AiAgent agent in _settings.Agents)
                agent.ResetExecutionState();
            StartNewConversation();
            ApplyUserIdentity();
            TxtStatusSummary.Text = "🔐 Đã đăng xuất. Bạn có thể đăng nhập tài khoản khác bất cứ lúc nào.";
        }

        private bool EnsureAuthenticated()
        {
            if (_currentUser != null) return true;

            var login = new LoginWindow(_userStore) { Owner = this };
            if (login.ShowDialog() != true || login.AuthenticatedUser == null)
                return false;

            _currentUser = login.AuthenticatedUser;
            Interlocked.Increment(ref _accountGeneration);
            _conversationStore = JsonConversationStore.ForUser(_currentUser.Username);
            SetPersonalAssistantIdentity();
            StartNewConversation();
            ApplyUserIdentity();
            TxtStatusSummary.Text = $"👤 Đã đăng nhập: {_currentUser.DisplayName}";
            return true;
        }

        private void SetPersonalAssistantIdentity()
        {
            if (_currentUser == null) return;
            _settings.AssistantUserDisplayName = string.IsNullOrWhiteSpace(_currentUser.DisplayName)
                ? _currentUser.Username
                : _currentUser.DisplayName.Trim();
        }

        private void RefreshPersonalAssistantMemory()
        {
            if (_currentUser == null || _conversationStore == null)
            {
                _settings.PersonalAssistantMemoryContext = "";
                return;
            }

            SetPersonalAssistantIdentity();
            _settings.PersonalAssistantMemoryContext = PersonalAssistantMemoryService.BuildContext(
                _conversationStore, _currentSession, _settings.AssistantUserDisplayName);
        }

        private void LoadAgentsToUi()
        {
            _agentsCollection = new ObservableCollection<AiAgent>(_settings.Agents);
            ItemsAgentsList.ItemsSource = _agentsCollection;
            ChkInteractiveMode.IsChecked = _settings.InteractiveMode;
        }

        private void UpdateDirectAgentPicker()
        {
            CmbDirectAgentPicker.Items.Clear();
            foreach (var agent in _settings.Agents.Where(a => a.IsEnabled && a.Id != "router_agent"))
            {
                CmbDirectAgentPicker.Items.Add(new ComboBoxItem
                {
                    Content = $"{agent.Icon} {agent.Name}",
                    Tag = agent.Id
                });
            }

            if (CmbDirectAgentPicker.Items.Count > 0)
            {
                string preferredAgentId = string.IsNullOrWhiteSpace(_settings.SelectedDirectAgentId)
                    ? "personal_assistant_agent"
                    : _settings.SelectedDirectAgentId;
                CmbDirectAgentPicker.SelectedItem = CmbDirectAgentPicker.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(item => string.Equals(
                        item.Tag?.ToString(), preferredAgentId, StringComparison.Ordinal));
                if (CmbDirectAgentPicker.SelectedIndex < 0)
                    CmbDirectAgentPicker.SelectedItem = CmbDirectAgentPicker.Items
                        .OfType<ComboBoxItem>()
                        .FirstOrDefault(item => Equals(item.Tag?.ToString(), "personal_assistant_agent"))
                        ?? CmbDirectAgentPicker.Items[0];
            }
        }

        private void PopulateFollowUpAgentPickers()
        {
            foreach (ComboBox picker in new[] { CmbAgentFollowUp, CmbLogsFollowUp })
            {
                picker.Items.Clear();
                foreach (AiAgent agent in _settings.Agents.Where(a => a.IsEnabled && a.Id != "router_agent"))
                {
                    picker.Items.Add(new ComboBoxItem { Content = $"{agent.Icon} {agent.Name}", Tag = agent.Id });
                }

                if (picker.Items.Count > 0)
                {
                    picker.SelectedItem = picker.Items
                        .OfType<ComboBoxItem>()
                        .FirstOrDefault(item => Equals(item.Tag?.ToString(), "personal_assistant_agent"))
                        ?? picker.Items[0];
                }
            }
        }

        private void Engine_OnLogMessage(string msg)
        {
            long eventGeneration = _requestAccountGeneration.Value ?? Interlocked.Read(ref _accountGeneration);
            Dispatcher.Invoke(() =>
            {
                if (_currentUser == null || eventGeneration != Interlocked.Read(ref _accountGeneration)) return;
                TxtExecutionLogs.AppendText(msg + Environment.NewLine);
                TxtExecutionLogs.ScrollToEnd();
                TxtStatusSummary.Text = msg;
            });
        }

        private void Engine_OnAgentUpdated(AiAgent agent)
        {
            long eventGeneration = _requestAccountGeneration.Value ?? Interlocked.Read(ref _accountGeneration);
            Dispatcher.Invoke(() =>
            {
                if (_currentUser == null || eventGeneration != Interlocked.Read(ref _accountGeneration)) return;
                if (agent.Status == AgentStatus.Running)
                {
                    TxtProgressStatus.Text = $"{agent.Icon} {agent.Name}: Đang chạy...";
                }
            });
        }

        private void Engine_OnChatMessage(ChatMessage message)
        {
            long eventGeneration = _requestAccountGeneration.Value ?? Interlocked.Read(ref _accountGeneration);
            Dispatcher.Invoke(() =>
            {
                if (_currentUser == null || eventGeneration != Interlocked.Read(ref _accountGeneration)) return;
                _chatMessages.Add(message);
                ScrollChatMessages.ScrollToEnd();
                PersistCurrentSession();
            });
        }

        private void BtnAttachFiles_Click(object sender, RoutedEventArgs e)
        {
            AddFilesFromPicker(_promptAttachments, "Chọn tệp hoặc hình ảnh để AI điều phối phân tích");
        }

        private void BtnAttachReplyFiles_Click(object sender, RoutedEventArgs e)
        {
            AddFilesFromPicker(_replyAttachments, "Đính kèm tệp hoặc hình ảnh vào phản hồi");
        }

        private void AddFilesFromPicker(ObservableCollection<PromptAttachment> destination, string title)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = $"Tệp được hỗ trợ ({PromptAttachmentReader.SupportedExtensions})|{PromptAttachmentReader.SupportedExtensions}|Tất cả tệp (*.*)|*.*",
                Multiselect = true,
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != true) return;
            long existingBytes = destination.Sum(attachment => new FileInfo(attachment.FullPath).Length);
            foreach (string path in dialog.FileNames)
            {
                if (destination.Any(attachment =>
                    string.Equals(attachment.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (!PromptAttachmentReader.IsSupported(path))
                {
                    MessageBox.Show(
                        $"Định dạng chưa hỗ trợ: {Path.GetFileName(path)}",
                        "Định dạng tệp không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }

                long fileBytes = new FileInfo(path).Length;
                if (fileBytes > PromptAttachmentReader.MaxFileBytes)
                {
                    MessageBox.Show(
                        $"{Path.GetFileName(path)} vượt quá giới hạn 10 MB.",
                        "Tệp quá lớn", MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }
                if (destination.Count >= PromptAttachmentReader.MaxFileCount)
                {
                    MessageBox.Show(
                        $"Chỉ có thể đính kèm tối đa {PromptAttachmentReader.MaxFileCount} tệp mỗi lần.",
                        "Đã đạt giới hạn", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                }
                if (existingBytes + fileBytes > PromptAttachmentReader.MaxTotalBytes)
                {
                    MessageBox.Show("Tổng dung lượng tệp đính kèm không được vượt quá 20 MB.",
                        "Đã đạt giới hạn dung lượng", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }

                destination.Add(new PromptAttachment(path));
                existingBytes += fileBytes;
            }
        }

        private void BtnRemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is PromptAttachment attachment)
                _promptAttachments.Remove(attachment);
        }

        private void BtnPastePromptImage_Click(object sender, RoutedEventArgs e) =>
            PasteClipboardImage(_promptAttachments);

        private void BtnPasteReplyImage_Click(object sender, RoutedEventArgs e) =>
            PasteClipboardImage(_replyAttachments);

        private void TxtPromptInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                ClipboardHasImage())
            {
                e.Handled = true;
                PasteClipboardImage(_promptAttachments);
            }
        }

        private void TxtReply_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                ClipboardHasImage())
            {
                e.Handled = true;
                PasteClipboardImage(_replyAttachments);
            }
        }

        private void PasteClipboardImage(ObservableCollection<PromptAttachment> destination)
        {
            if (destination.Count >= PromptAttachmentReader.MaxFileCount)
            {
                MessageBox.Show($"Chỉ có thể đính kèm tối đa {PromptAttachmentReader.MaxFileCount} tệp mỗi lần.",
                    "Đã đạt giới hạn", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (!Clipboard.ContainsImage())
                {
                    MessageBox.Show("Clipboard hiện không có hình ảnh. Hãy sao chép ảnh trước rồi thử lại.",
                        "Không tìm thấy ảnh", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                BitmapSource? image = Clipboard.GetImage();
                if (image == null)
                {
                    MessageBox.Show("Không thể đọc hình ảnh từ Clipboard. Hãy sao chép lại ảnh rồi thử lại.",
                        "Không đọc được ảnh", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AIOrchestrator", "clipboard-images");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"clipboard_{Guid.NewGuid():N}.png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (FileStream output = File.Create(path))
                    encoder.Save(output);

                if (new FileInfo(path).Length > PromptAttachmentReader.MaxFileBytes)
                {
                    File.Delete(path);
                    MessageBox.Show("Ảnh trong Clipboard vượt quá giới hạn 10 MB.",
                        "Ảnh quá lớn", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                destination.Add(new PromptAttachment(path));
                long totalBytes = destination.Sum(attachment => new FileInfo(attachment.FullPath).Length);
                if (totalBytes > PromptAttachmentReader.MaxTotalBytes)
                {
                    destination.RemoveAt(destination.Count - 1);
                    File.Delete(path);
                    MessageBox.Show("Tổng dung lượng tệp đính kèm không được vượt quá 20 MB.",
                        "Đã đạt giới hạn dung lượng", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or
                                       UnauthorizedAccessException or ArgumentException or
                                       System.Runtime.InteropServices.COMException)
            {
                MessageBox.Show($"Không thể đính kèm ảnh từ Clipboard: {ex.Message}",
                    "Lỗi dán ảnh", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRemoveReplyAttachment_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is PromptAttachment attachment)
                _replyAttachments.Remove(attachment);
        }

        private bool ClipboardHasImage()
        {
            try
            {
                return Clipboard.ContainsImage();
            }
            catch (Exception ex) when (ex is InvalidOperationException or
                                       System.Runtime.InteropServices.COMException)
            {
                MessageBox.Show($"Không thể truy cập Clipboard: {ex.Message}",
                    "Lỗi Clipboard", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private string AddAttachmentNamesToPrompt(string prompt)
        {
            if (_promptAttachments.Count == 0) return prompt;
            string names = string.Join(", ", _promptAttachments.Select(attachment => attachment.FileName));
            return $"{prompt}\n\n📎 Tệp đính kèm: {names}";
        }

        private async void BtnStartRun_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAuthenticated()) return;
            long requestGeneration = Interlocked.Read(ref _accountGeneration);
            if (_cts != null)
            {
                MessageBox.Show("Đang có một tác vụ AI chạy. Hãy đợi tác vụ đó hoàn tất trước.",
                    "Đang xử lý", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string prompt = TxtPromptInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(prompt) && _promptAttachments.Count == 0)
            {
                MessageBox.Show("Vui lòng nhập câu hỏi hoặc đính kèm ít nhất một tệp.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(prompt))
                prompt = "Hãy đọc và phân tích các tệp đính kèm, sau đó trả lời hoặc thực hiện yêu cầu phù hợp dựa trên nội dung trong đó.";

            _settings.InteractiveMode = ChkInteractiveMode.IsChecked == true;

            // Xác định chế độ
            ExecutionMode mode = ExecutionMode.SmartRouter;
            string? targetAgentId = null;

            if (RbCustomPipeline.IsChecked == true)
            {
                mode = ExecutionMode.CustomPipeline;
            }
            else if (RbDirectAgent.IsChecked == true)
            {
                mode = ExecutionMode.DirectAgent;
                if (CmbDirectAgentPicker.SelectedItem is ComboBoxItem item)
                {
                    targetAgentId = item.Tag?.ToString();
                }
            }

            // Giao diện: Đang chạy
            SetUiRunningState(true);
            TxtExecutionLogs.Clear();
            _latestMarkdownResult = "";
            _latestImagePath = "";
            BrowserMarkdownView.Visibility = Visibility.Visible;
            TxtRawOutput.Text = "";
            RenderMarkdownView("*Đang trong quá trình điều phối các AI, vui lòng theo dõi tab Trao Đổi Tương Tác...*");

            // Thêm tin nhắn của User vào khung Chat
            RefreshPersonalAssistantMemory();
            string chatPrompt = AddAttachmentNamesToPrompt(prompt);
            _engine.PostChat("Bạn", "👤", chatPrompt, MessageType.User);
            TabsMainResults.SelectedIndex = 0; // Chuyển sang Tab Trao đổi

            var runCancellation = new CancellationTokenSource();
            _cts = runCancellation;
            var cancellationToken = runCancellation.Token;
            _stopwatch.Restart();
            _timer.Start();

            try
            {
                _requestAccountGeneration.Value = requestGeneration;
                TxtStatusSummary.Text = _promptAttachments.Count > 0
                    ? "📎 Đang đọc tệp đính kèm..."
                    : "Đang gửi yêu cầu tới các AI...";
                string promptWithAttachments = await PromptAttachmentReader.BuildPromptAsync(
                    prompt, _promptAttachments.ToList(), _settings.GeminiApiKey, cancellationToken);
                if (requestGeneration != Interlocked.Read(ref _accountGeneration)) return;
                var result = await _engine.ExecuteAsync(
                    promptWithAttachments,
                    _settings,
                    mode,
                    targetAgentId,
                    promptUserCallback: RequestUserClarificationAsync,
                    cancellationToken);
                if (requestGeneration != Interlocked.Read(ref _accountGeneration)) return;

                if (result.Success)
                {
                    _latestMarkdownResult = result.FinalAnswer;
                    TxtRawOutput.Text = result.FinalAnswer;
                    RenderMarkdownView(result.FinalAnswer);

                    ItemsExecutedAgentsBreakdown.ItemsSource = result.ExecutedAgents;
                    TxtStatusSummary.Text = $"✅ Hoàn tất thành công trong {result.TotalDuration.TotalSeconds:F2} giây. Đã huy động {result.ExecutedAgents.Count} AI.";
                    
                    _engine.PostChat("Hệ thống", "🎉", "Đã hoàn thành toàn bộ quy trình! Bạn có thể xem kết quả chi tiết ở tab 'Kết Quả Cuối Cùng' hoặc tiếp tục gõ phản hồi bên dưới.", MessageType.SystemInfo,
                        new List<string> { "Giải thích thêm", "Thêm ví dụ test case", "Tối ưu hóa hơn nữa" });
                }
                else
                {
                    _latestMarkdownResult = $"### ❌ Gặp sự cố khi thực thi:\n\n{result.ErrorMessage}\n\n*Gợi ý: Kiểm tra lại API Key hoặc kết nối mạng trong phần Cài Đặt (⚙️).*";
                    TxtRawOutput.Text = _latestMarkdownResult;
                    RenderMarkdownView(_latestMarkdownResult);
                    TxtStatusSummary.Text = $"❌ Lỗi: {result.ErrorMessage}";
                }

                PersistCurrentSession();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                TxtStatusSummary.Text = $"❌ Lỗi ngoại lệ: {ex.Message}";
                MessageBox.Show($"Lỗi trong quá trình điều phối: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _requestAccountGeneration.Value = null;
                _stopwatch.Stop();
                _timer.Stop();
                SetUiRunningState(false);
                if (ReferenceEquals(_cts, runCancellation))
                    _cts = null;
                runCancellation.Dispose();
            }
        }

        private void BtnGenerateChart_Click(object sender, RoutedEventArgs e) =>
            _ = GenerateImageAsync(isChart: true);

        private void BtnGenerateDesign_Click(object sender, RoutedEventArgs e) =>
            _ = GenerateImageAsync(isChart: false);

        private async Task GenerateImageAsync(bool isChart)
        {
            if (!EnsureAuthenticated()) return;
            long requestGeneration = Interlocked.Read(ref _accountGeneration);
            if (_cts != null)
            {
                MessageBox.Show("Đang có một tác vụ AI chạy. Hãy đợi tác vụ đó hoàn tất trước.",
                    "Đang xử lý", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string userPrompt = TxtPromptInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(userPrompt) && _promptAttachments.Count == 0)
            {
                MessageBox.Show("Hãy mô tả đồ thị/thiết kế hoặc đính kèm tệp, hình ảnh trước.",
                    "Thiếu mô tả", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(userPrompt))
                userPrompt = "Dựa trên các tệp đính kèm, hãy tạo hình ảnh phù hợp nhất với nội dung và mục tiêu được mô tả.";

            var generationCancellation = new CancellationTokenSource();
            _cts = generationCancellation;
            BtnGenerateChart.IsEnabled = false;
            BtnGenerateDesign.IsEnabled = false;
            BtnStartRun.IsEnabled = false;
            BtnAttachFiles.IsEnabled = false;
            ItemsPromptAttachments.IsEnabled = false;
            TxtPromptInput.IsEnabled = false;
            TxtStatusSummary.Text = _promptAttachments.Count > 0
                ? "📎 Đang đọc tệp đính kèm trước khi gửi yêu cầu tạo ảnh..."
                : isChart
                    ? "📊 Đang tạo đồ thị bằng Pollinations AI..."
                    : "🎨 Đang tạo thiết kế bằng Pollinations AI...";

            try
            {
                string imageContext = await PromptAttachmentReader.BuildPromptAsync(
                    userPrompt, _promptAttachments.ToList(), _settings.GeminiApiKey, generationCancellation.Token);
                if (requestGeneration != Interlocked.Read(ref _accountGeneration)) return;
                string imagePrompt = isChart
                    ? $"Create a clear, polished data chart or graph as an image based on this exact request. Preserve all supplied labels, values, units, and relationships; do not invent data. If the request omits data, create a clearly labeled illustrative conceptual diagram instead. Use legible labels and a clean professional visual style. Do not output code, explanations, or markup; return only the finished image.\n\nRequest:\n{imageContext}"
                    : $"Create a polished professional graphic design as an image based on the following brief. Follow the requested subject, text, layout, palette, and style. Keep any requested text legible and do not add unrelated text. Do not output code, explanations, or markup; return only the finished image.\n\nDesign brief:\n{imageContext}";

                GeneratedImage generatedImage = await _imageGenerationService.GenerateAsync(
                    imagePrompt,
                    isChart ? 1280 : 1024,
                    isChart ? 768 : 1024,
                    generationCancellation.Token);
                generationCancellation.Token.ThrowIfCancellationRequested();
                if (_currentUser == null || requestGeneration != Interlocked.Read(ref _accountGeneration)) return;

                string imageDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AIOrchestrator",
                    "generated-images");
                Directory.CreateDirectory(imageDirectory);
                string imagePath = Path.Combine(
                    imageDirectory, $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{generatedImage.FileExtension}");
                await File.WriteAllBytesAsync(imagePath, generatedImage.Bytes, generationCancellation.Token);

                string imageTitle = isChart ? "Đồ thị đã tạo" : "Thiết kế đồ họa đã tạo";
                _latestImagePath = imagePath;
                _latestMarkdownResult = $"## {imageTitle}\n\nYêu cầu: {userPrompt}";
                TxtRawOutput.Text = _latestMarkdownResult;
                _engine.PostChat("Bạn", "👤", AddAttachmentNamesToPrompt(userPrompt), MessageType.User);
                RbRenderedView.IsChecked = true;
                RenderMarkdownView(_latestMarkdownResult);
                _engine.PostChat("Vinh-AI", isChart ? "📊" : "🎨",
                    $"{imageTitle}. Hình ảnh đã hiển thị trong mục Kết Quả Cuối Cùng.", MessageType.StepCompleted);
                PersistCurrentSession();
                TabsMainResults.SelectedIndex = 1;
                TxtStatusSummary.Text = $"✅ {imageTitle}.";
            }
            catch (OperationCanceledException) when (generationCancellation.IsCancellationRequested)
            {
                TxtStatusSummary.Text = "⏹️ Đã hủy tác vụ tạo ảnh.";
            }
            catch (Exception ex)
            {
                TxtStatusSummary.Text = $"❌ Không thể tạo ảnh: {ex.Message}";
                MessageBox.Show($"Không thể tạo ảnh: {ex.Message}", "Lỗi tạo ảnh",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (ReferenceEquals(_cts, generationCancellation))
                    _cts = null;
                generationCancellation.Dispose();
                BtnGenerateChart.IsEnabled = true;
                BtnGenerateDesign.IsEnabled = true;
                BtnStartRun.IsEnabled = true;
                BtnAttachFiles.IsEnabled = true;
                ItemsPromptAttachments.IsEnabled = true;
                TxtPromptInput.IsEnabled = true;
            }
        }

        private Task<string> RequestUserClarificationAsync(string question, List<string> suggestions)
        {
            var tcs = new TaskCompletionSource<string>();
            _userReplyTcs = tcs;

            Dispatcher.Invoke(() =>
            {
                BorderClarificationBanner.Visibility = Visibility.Visible;
                TabsMainResults.SelectedIndex = 0;
                TxtInteractiveReplyInput.Focus();
            });

            return tcs.Task;
        }

        private void BtnSendReply_Click(object sender, RoutedEventArgs e)
        {
            SubmitUserReply();
        }

        private void TxtInteractiveReplyInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift))
            {
                e.Handled = true;
                SubmitUserReply();
            }
        }

        private async void SubmitUserReply()
        {
            if (!EnsureAuthenticated()) return;
            long requestGeneration = Interlocked.Read(ref _accountGeneration);

            string reply = TxtInteractiveReplyInput.Text.Trim();
            List<PromptAttachment> attachments = _replyAttachments.ToList();
            if (string.IsNullOrWhiteSpace(reply) && attachments.Count == 0) return;
            if (string.IsNullOrWhiteSpace(reply))
                reply = "Hãy phân tích hình ảnh đính kèm và trả lời dựa trên nội dung nhìn thấy.";

            string replyWithAttachments;
            BtnSendReply.IsEnabled = false;
            BtnPasteReplyImage.IsEnabled = false;
            try
            {
                replyWithAttachments = await PromptAttachmentReader.BuildPromptAsync(
                    reply, attachments, _settings.GeminiApiKey, CancellationToken.None);
                if (requestGeneration != Interlocked.Read(ref _accountGeneration)) return;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể đọc tệp đính kèm trong phản hồi: {ex.Message}",
                    "Lỗi đính kèm", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                BtnSendReply.IsEnabled = true;
                BtnPasteReplyImage.IsEnabled = true;
            }

            string visibleReply = AddAttachmentNamesToPrompt(reply);
            TxtInteractiveReplyInput.Clear();
            _replyAttachments.Clear();
            BorderClarificationBanner.Visibility = Visibility.Collapsed;

            // Nếu Engine đang chờ câu trả lời cho bước làm rõ (Clarification)
            if (_userReplyTcs != null && !_userReplyTcs.Task.IsCompleted)
            {
                if (_userReplyTcs.TrySetResult(replyWithAttachments))
                {
                    _engine.PostChat("Bạn", "👤", visibleReply, MessageType.User);
                    _userReplyTcs = null;
                }
                return;
            }

            // Nếu quy trình đã xong hoặc đang rảnh, người dùng muốn chat tiếp nối (Follow-up conversation)
            RefreshPersonalAssistantMemory();
            _requestAccountGeneration.Value = requestGeneration;
            _engine.PostChat("Bạn", "👤", visibleReply, MessageType.User);

            // Tìm AI phù hợp để trả lời tiếp
            var activeAgent = _settings.Agents.FirstOrDefault(a => a.Id == "personal_assistant_agent")
                              ?? _settings.Agents.First();
            
            _engine.PostChat(activeAgent.Name, activeAgent.Icon, $"Đang xử lý phản hồi: \"{reply}\"...", MessageType.Agent);

            var followUpCancellation = new CancellationTokenSource();
            _cts = followUpCancellation;
            var cancellationToken = followUpCancellation.Token;
            try
            {
                string prompt = $"[NGỮ CẢNH TRƯỚC ĐÓ]:\n{_latestMarkdownResult}\n\n[YÊU CẦU TIẾP NỐI CỦA NGƯỜI DÙNG]:\n{replyWithAttachments}\n\nHãy phản hồi và thực hiện điều chỉnh theo đúng yêu cầu.";
                string response = await _engine.CallAgentAsync(activeAgent, prompt, _settings, cancellationToken);
                if (requestGeneration != Interlocked.Read(ref _accountGeneration)) return;
                
                _latestMarkdownResult = response;
                TxtRawOutput.Text = response;
                RenderMarkdownView(response);

                _engine.PostChat(activeAgent.Name, activeAgent.Icon, response, MessageType.StepCompleted);
                PersistCurrentSession();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _engine.PostChat("Hệ thống", "❌", $"Lỗi khi xử lý phản hồi: {ex.Message}", MessageType.SystemInfo);
            }
            finally
            {
                _requestAccountGeneration.Value = null;
                if (ReferenceEquals(_cts, followUpCancellation))
                    _cts = null;
                followUpCancellation.Dispose();
            }
        }

        private void BtnQuickSuggestion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string suggestion)
            {
                TxtInteractiveReplyInput.Text = suggestion;
                SubmitUserReply();
            }
        }

        private void BtnCancelRun_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                TxtStatusSummary.Text = "⏳ Đang gửi tín hiệu hủy...";
            }

            if (_userReplyTcs != null && !_userReplyTcs.Task.IsCompleted)
            {
                _userReplyTcs.TrySetCanceled();
                _userReplyTcs = null;
            }
        }

        private void SetUiRunningState(bool isRunning)
        {
            BtnStartRun.IsEnabled = !isRunning;
            BtnStartRun.Visibility = isRunning ? Visibility.Collapsed : Visibility.Visible;
            BtnCancelRun.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            PanelProgress.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            TxtPromptInput.IsEnabled = !isRunning;
            BtnAttachFiles.IsEnabled = !isRunning;
            ItemsPromptAttachments.IsEnabled = !isRunning;
        }

        private void RenderMarkdownView(string markdown)
        {
            try
            {
                BrowserMarkdownView.Visibility = Visibility.Visible;
                var pipeline = new MarkdownPipelineBuilder()
                    .UseAdvancedExtensions()
                    .Build();

                string htmlBody = Markdown.ToHtml(markdown ?? "", pipeline);
                if (!string.IsNullOrWhiteSpace(_latestImagePath) && File.Exists(_latestImagePath))
                {
                    string imageUri = new Uri(_latestImagePath, UriKind.Absolute).AbsoluteUri;
                    htmlBody += $@"<figure><img src=""{imageUri}"" alt=""Hình ảnh được tạo bởi Pollinations AI"" />
<figcaption>Hình ảnh kết quả</figcaption></figure>";
                }

                string fullHtml = $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<style>
body {{
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
    background-color: #0F172A;
    color: #F1F5F9;
    padding: 16px 20px;
    line-height: 1.65;
    font-size: 14px;
}}
h1, h2, h3, h4 {{
    color: #38BDF8;
    margin-top: 18px;
    margin-bottom: 8px;
    border-bottom: 1px solid #334155;
    padding-bottom: 4px;
}}
h1 {{ font-size: 20px; }}
h2 {{ font-size: 17px; }}
h3 {{ font-size: 15px; }}
p {{ margin: 8px 0; }}
pre {{
    background-color: #1E293B;
    padding: 12px 14px;
    border-radius: 6px;
    overflow-x: auto;
    border: 1px solid #334155;
}}
code {{
    font-family: Consolas, 'Courier New', monospace;
    background-color: #1E293B;
    color: #38BDF8;
    padding: 2px 6px;
    border-radius: 4px;
    font-size: 13px;
}}
pre code {{
    background-color: transparent;
    padding: 0;
    color: #E2E8F0;
}}
blockquote {{
    border-left: 4px solid #38BDF8;
    margin: 8px 0;
    padding-left: 14px;
    color: #94A3B8;
}}
table {{
    border-collapse: collapse;
    width: 100%;
    margin: 14px 0;
}}
th, td {{
    border: 1px solid #334155;
    padding: 8px 12px;
}}
th {{
    background-color: #1E293B;
    color: #38BDF8;
    text-align: left;
}}
ul, ol {{
    padding-left: 24px;
    margin: 6px 0;
}}
li {{
    margin-bottom: 4px;
}}
img {{
display: block;
max-width: 100%;
height: auto;
max-height: 900px;
margin: 12px auto;
border-radius: 8px;
border: 1px solid #334155;
}}
figure {{
margin: 16px 0;
text-align: center;
}}
figcaption {{
color: #94A3B8;
font-size: 12px;
}}
a {{
    color: #38BDF8;
    text-decoration: underline;
}}
hr {{
    border: 0;
    border-top: 1px solid #334155;
    margin: 16px 0;
}}
</style>
</head>
<body>
{htmlBody}
</body>
</html>";

                BrowserMarkdownView.NavigateToString(fullHtml);
            }
            catch (Exception)
            {
                TxtRawOutput.Text = markdown;
                TxtRawOutput.Visibility = Visibility.Visible;
                BorderWebBrowser.Visibility = Visibility.Collapsed;
            }
        }

        private void RenderView_Changed(object sender, RoutedEventArgs e)
        {
            if (RbRenderedView == null || RbRawTextView == null) return;

            if (RbRenderedView.IsChecked == true)
            {
                RenderMarkdownView(_latestMarkdownResult);
                TxtRawOutput.Visibility = Visibility.Collapsed;
            }
            else
            {
                BrowserMarkdownView.Visibility = Visibility.Collapsed;
                TxtRawOutput.Visibility = Visibility.Visible;
            }
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (CmbDirectAgentPicker == null) return;

            if (RbDirectAgent.IsChecked == true)
            {
                CmbDirectAgentPicker.Visibility = Visibility.Visible;
            }
            else
            {
                CmbDirectAgentPicker.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SettingsWindow(_settings) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _settings = dialog.Settings;
                _storageService.SaveSettings(_settings);
                TxtStatusSummary.Text = "✅ Đã lưu cấu hình API Keys thành công.";
            }
        }

        private void BtnAddAgent_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AgentEditDialog { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _settings.Agents.Add(dialog.Agent);
                _storageService.SaveSettings(_settings);
                LoadAgentsToUi();
                UpdateDirectAgentPicker();
                TxtStatusSummary.Text = $"✅ Đã thêm AI '{dialog.Agent.Name}'.";
            }
        }

        private void BtnEditAgent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AiAgent agent)
            {
                var dialog = new AgentEditDialog(agent) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    agent.Name = dialog.Agent.Name;
                    agent.Role = dialog.Agent.Role;
                    agent.Icon = dialog.Agent.Icon;
                    agent.SystemPrompt = dialog.Agent.SystemPrompt;
                    agent.Provider = dialog.Agent.Provider;
                    agent.ModelName = dialog.Agent.ModelName;
                    agent.Temperature = dialog.Agent.Temperature;

                    _storageService.SaveSettings(_settings);
                    LoadAgentsToUi();
                    UpdateDirectAgentPicker();
                    TxtStatusSummary.Text = $"✅ Đã cập nhật AI '{agent.Name}'.";
                }
            }
        }

        private void BtnDeleteAgent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AiAgent agent)
            {
                if (agent.Id == "router_agent")
                {
                    MessageBox.Show("Không thể xóa AI Router mặc định vì cần thiết cho việc điều phối.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var confirm = MessageBox.Show($"Bạn có chắc chắn muốn xóa '{agent.Name}' không?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm == MessageBoxResult.Yes)
                {
                    _settings.Agents.Remove(agent);
                    _storageService.SaveSettings(_settings);
                    LoadAgentsToUi();
                    UpdateDirectAgentPicker();
                    TxtStatusSummary.Text = $"🗑️ Đã xóa AI '{agent.Name}'.";
                }
            }
        }

        private void OnAgentSelectionChanged(object sender, RoutedEventArgs e)
        {
            _storageService.SaveSettings(_settings);
            UpdateDirectAgentPicker();
        }

        private void BtnCopyResult_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_latestMarkdownResult))
            {
                Clipboard.SetText(_latestMarkdownResult);
                MessageBox.Show("Đã sao chép kết quả vào bộ nhớ tạm (Clipboard)!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Chưa có kết quả để sao chép.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SectionFollowUp_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.IsKeyDown(Key.LeftShift) ||
                sender is not TextBox input || input.Tag is not string section)
                return;

            e.Handled = true;
            _ = AskAboutSectionAsync(section);
        }

        private void SectionFollowUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string section })
                _ = AskAboutSectionAsync(section);
        }

        private async Task AskAboutSectionAsync(string section)
        {
            if (!EnsureAuthenticated()) return;
            if (_cts != null)
            {
                MessageBox.Show("Một tác vụ AI khác đang chạy. Hãy đợi tác vụ hoàn tất rồi thử lại.",
                    "Đang xử lý", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            TextBox input;
            ObservableCollection<ChatMessage> sectionMessages;
            string sectionContext;
            string title;
            ComboBox? agentPicker = null;

            switch (section)
            {
                case "final":
                    input = TxtFinalFollowUpInput;
                    sectionMessages = _finalFollowUpMessages;
                    sectionContext = _latestMarkdownResult;
                    title = "kết quả cuối cùng";
                    break;
                case "agent":
                    input = TxtAgentFollowUpInput;
                    sectionMessages = _agentFollowUpMessages;
                    agentPicker = CmbAgentFollowUp;
                    title = "kết quả AI";
                    if (agentPicker.SelectedItem is not ComboBoxItem { Tag: string selectedAgentId } ||
                        _settings.Agents.FirstOrDefault(a => a.Id == selectedAgentId) is not AiAgent selectedAgent)
                    {
                        MessageBox.Show("Hãy chọn AI cần hỏi.", "Chưa chọn AI",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    sectionContext = $"AI: {selectedAgent.Name}\nNhiệm vụ: {selectedAgent.AssignedSubtask}\n\nKết quả:\n{selectedAgent.OutputResult}";
                    break;
                case "logs":
                    input = TxtLogsFollowUpInput;
                    sectionMessages = _logsFollowUpMessages;
                    agentPicker = CmbLogsFollowUp;
                    title = "nhật ký điều phối";
                    sectionContext = TxtExecutionLogs.Text;
                    break;
                default:
                    return;
            }

            string question = input.Text.Trim();
            if (string.IsNullOrWhiteSpace(question)) return;

            AiAgent? agent = agentPicker?.SelectedItem is ComboBoxItem { Tag: string agentId }
                ? _settings.Agents.FirstOrDefault(a => a.Id == agentId)
                : _settings.Agents.FirstOrDefault(a => a.Id == "researcher_agent" && a.IsEnabled)
                    ?? _settings.Agents.FirstOrDefault(a => a.IsEnabled && a.Id != "router_agent");
            if (agent == null)
            {
                MessageBox.Show("Không có AI nào đang bật để trả lời. Hãy bật ít nhất một AI trong danh sách.",
                    "Không có AI khả dụng", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string boundedContext = sectionContext.Length > 18000
                ? sectionContext[^18000..]
                : sectionContext;
            input.Clear();
            input.IsEnabled = false;
            var sectionCancellation = new CancellationTokenSource();
            _cts = sectionCancellation;

            var userMessage = new ChatMessage
            {
                SenderName = "Bạn",
                SenderIcon = "👤",
                Content = question,
                Type = MessageType.User,
                FollowUpSection = section,
                FollowUpAgentId = agent.Id
            };
            sectionMessages.Add(userMessage);
            _chatMessages.Add(userMessage);

            try
            {
                string prompt = $"Bạn đang trả lời câu hỏi của người dùng về mục “{title}”. " +
                    "Trả lời rõ ràng, dựa trên nội dung mục đó; nếu nhật ký chỉ ra lỗi, nêu nguyên nhân và các bước khắc phục.\n\n" +
                    $"[NỘI DUNG MỤC ĐANG ĐƯỢC HỎI]\n{boundedContext}\n\n[CÂU HỎI]\n{question}";
                string response = await _engine.CallAgentAsync(agent, prompt, _settings, sectionCancellation.Token);
                var assistantMessage = new ChatMessage
                {
                    SenderName = agent.Name,
                    SenderIcon = agent.Icon,
                    Content = response,
                    Type = MessageType.Agent,
                    FollowUpSection = section,
                    FollowUpAgentId = agent.Id
                };
                sectionMessages.Add(assistantMessage);
                _chatMessages.Add(assistantMessage);
                PersistCurrentSession();
            }
            catch (OperationCanceledException) when (sectionCancellation.IsCancellationRequested)
            {
                TxtStatusSummary.Text = "⏹️ Đã hủy câu trả lời.";
            }
            catch (Exception ex)
            {
                string errorText = $"Không thể trả lời về {title}: {ex.Message}";
                sectionMessages.Add(new ChatMessage
                {
                    SenderName = "Lỗi",
                    SenderIcon = "⚠️",
                    Content = errorText,
                    Type = MessageType.SystemInfo,
                    FollowUpSection = section,
                    FollowUpAgentId = agent.Id
                });
                TxtStatusSummary.Text = errorText;
                PersistCurrentSession();
            }
            finally
            {
                if (ReferenceEquals(_cts, sectionCancellation))
                    _cts = null;
                sectionCancellation.Dispose();
                input.IsEnabled = true;
                ScrollChatMessages.ScrollToEnd();
            }
        }

        private void TemplateCode_Click(object sender, RoutedEventArgs e)
        {
            TxtPromptInput.Text = "Lập trình một thuật toán sắp xếp mảng tùy chỉnh bằng C# và viết một bài giải thích chi tiết, kèm các test case.";
        }

        private void TemplateWriting_Click(object sender, RoutedEventArgs e)
        {
            TxtPromptInput.Text = "Hãy viết một bài viết phân tích xu hướng trí tuệ nhân tạo (AI Agent) trong năm 2026. Bố cục gồm: Giới thiệu, Lợi ích thực tế, Các thách thức và Lời kết luận truyền cảm hứng.";
        }

        private void TemplateResearch_Click(object sender, RoutedEventArgs e)
        {
            TxtPromptInput.Text = "So sánh chi tiết ưu và nhược điểm giữa việc chạy Local LLM (mô hình AI chạy trên máy) và Cloud API (gọi qua mạng). Tiêu chí: Dung lượng ổ cứng, RAM, tốc độ, chi phí và bảo mật.";
        }

        private void TemplateChart_Click(object sender, RoutedEventArgs e)
        {
            TxtPromptInput.Text =
                "Tạo biểu đồ cột so sánh doanh thu theo quý của cửa hàng: Quý 1 = 120 triệu đồng, " +
                "Quý 2 = 150 triệu đồng, Quý 3 = 135 triệu đồng, Quý 4 = 190 triệu đồng. " +
                "Đặt tiêu đề “Doanh thu theo quý năm 2026”, ghi đơn vị triệu đồng trên trục tung, " +
                "hiện nhãn giá trị trên từng cột, dùng màu xanh dương chuyên nghiệp và không thay đổi số liệu.";
            TxtPromptInput.Focus();
            TxtPromptInput.CaretIndex = TxtPromptInput.Text.Length;
        }

        private void TemplateDesign_Click(object sender, RoutedEventArgs e)
        {
            TxtPromptInput.Text =
                "Thiết kế poster quảng bá sự kiện “Ngày hội Sáng tạo”, khổ dọc 1080 x 1350 px. " +
                "Nội dung cần hiển thị chính xác: “NGÀY HỘI SÁNG TẠO”, “Thứ Bảy, 20/06/2026”, " +
                "“Trung tâm Văn hóa Thành phố”. Phong cách hiện đại, trẻ trung, bố cục rõ tiêu đề, " +
                "thời gian và địa điểm; phối màu tím-xanh, minh họa ánh sáng và hình khối sáng tạo, " +
                "không thêm chữ ngoài nội dung đã cung cấp.";
            TxtPromptInput.Focus();
            TxtPromptInput.CaretIndex = TxtPromptInput.Text.Length;
        }

        private void BtnQuickHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "⚡ CƠ CHẾ HOẠT ĐỘNG & TRAO ĐỔI:\n\n" +
                "1. Không tốn dung lượng máy: Sử dụng Cloud API (Groq siêu tốc 0.1s và Google Gemini). 0 MB mô hình trên ổ cứng.\n\n" +
                "2. Trao đổi theo từng trường hợp:\n" +
                "   - Nếu câu hỏi chưa rõ ràng hoặc có nhiều nhánh lựa chọn: AI Router sẽ hiển thị câu hỏi và các gợi ý nhanh để bạn chọn.\n" +
                "   - Bạn có thể gõ câu trả lời trực tiếp trong tab 'Trao Đổi Tương Tác'.\n" +
                "   - Sau khi hoàn thành, bạn có thể gõ tiếp để yêu cầu AI sửa đổi, thêm test case hay giải thích thêm.\n\n" +
                "3. AI chỉ chạy khi đến lượt: Các AI không liên quan sẽ ở trạng thái Nghỉ ngơi.",
                "Hướng Dẫn Sử Dụng",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
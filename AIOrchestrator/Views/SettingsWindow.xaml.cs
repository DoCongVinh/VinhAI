using System;
using System.Diagnostics;
using System.Windows;
using AIOrchestrator.Models;

namespace AIOrchestrator.Views
{
    public partial class SettingsWindow : Window
    {
        public AppSettings Settings { get; private set; }

        public SettingsWindow(AppSettings currentSettings)
        {
            InitializeComponent();
            Settings = currentSettings;

            TxtGeminiKey.Password = Settings.GeminiApiKey ?? "";
            TxtGroqKey.Password = Settings.GroqApiKey ?? "";
            TxtOpenAiKey.Password = Settings.OpenAiApiKey ?? "";
            TxtOpenAiBaseUrl.Text = string.IsNullOrWhiteSpace(Settings.OpenAiBaseUrl) ? "https://api.openai.com/v1" : Settings.OpenAiBaseUrl;
            TxtChatServerUrl.Text = Settings.ChatServerUrl ?? "";
            TxtChatAdminApiKey.Password = Settings.ChatAdminApiKey ?? "";
            TxtUpdateManifestUrl.Text = Settings.UpdateManifestUrl ?? "";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string updateManifestUrl = TxtUpdateManifestUrl.Text.Trim();
            if (!string.Equals(Settings.UpdateManifestUrl, updateManifestUrl, StringComparison.OrdinalIgnoreCase))
                Settings.SkippedUpdateVersion = "";

            Settings.GeminiApiKey = TxtGeminiKey.Password.Trim();
            Settings.GroqApiKey = TxtGroqKey.Password.Trim();
            Settings.OpenAiApiKey = TxtOpenAiKey.Password.Trim();
            Settings.OpenAiBaseUrl = TxtOpenAiBaseUrl.Text.Trim();
            Settings.ChatServerUrl = TxtChatServerUrl.Text.Trim().TrimEnd('/');
            Settings.ChatAdminApiKey = TxtChatAdminApiKey.Password.Trim();
            Settings.UpdateManifestUrl = updateManifestUrl;

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OpenGeminiLink_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            OpenBrowser("https://aistudio.google.com/apikey");
        }

        private void OpenGroqLink_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            OpenBrowser("https://console.groq.com/keys");
        }

        private void OpenBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể mở trình duyệt: {ex.Message}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}

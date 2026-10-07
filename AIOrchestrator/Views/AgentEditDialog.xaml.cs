using System;
using System.Windows;
using System.Windows.Controls;
using AIOrchestrator.Models;

namespace AIOrchestrator.Views
{
    public partial class AgentEditDialog : Window
    {
        public AiAgent Agent { get; private set; }

        public AgentEditDialog(AiAgent? existingAgent = null)
        {
            InitializeComponent();

            if (existingAgent != null)
            {
                TxtHeaderTitle.Text = $"✏️ Chỉnh sửa: {existingAgent.Name}";
                Agent = new AiAgent
                {
                    Id = existingAgent.Id,
                    Name = existingAgent.Name,
                    Role = existingAgent.Role,
                    Icon = existingAgent.Icon,
                    SystemPrompt = existingAgent.SystemPrompt,
                    Provider = existingAgent.Provider,
                    ModelName = existingAgent.ModelName,
                    Temperature = existingAgent.Temperature,
                    IsEnabled = existingAgent.IsEnabled
                };
            }
            else
            {
                TxtHeaderTitle.Text = "➕ Thêm AI Chuyên dụng mới";
                Agent = new AiAgent
                {
                    Id = Guid.NewGuid().ToString("N")[..8] + "_agent",
                    Name = "AI Chuyên viên",
                    Role = "Nhiệm vụ chuyên biệt...",
                    Icon = "💡",
                    Provider = AgentProvider.Gemini,
                    ModelName = "gemini-3.8-flash",
                    Temperature = 0.7,
                    IsEnabled = true
                };
            }

            TxtIcon.Text = Agent.Icon;
            TxtName.Text = Agent.Name;
            TxtRole.Text = Agent.Role;
            TxtModelName.Text = Agent.ModelName;
            TxtSystemPrompt.Text = Agent.SystemPrompt;
            SliderTemp.Value = Agent.Temperature;
            TxtTempValue.Text = Agent.Temperature.ToString("F1");

            // Set provider combo
            foreach (ComboBoxItem item in CmbProvider.Items)
            {
                if (item.Tag?.ToString() == Agent.Provider.ToString())
                {
                    CmbProvider.SelectedItem = item;
                    break;
                }
            }
        }

        private void CmbProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbProvider.SelectedItem is ComboBoxItem selected)
            {
                string tag = selected.Tag?.ToString() ?? "";
                if (tag == "Gemini")
                {
                    TxtModelName.Text = "gemini-3.8-flash";
                }
                else if (tag == "Groq")
                {
                    TxtModelName.Text = "openai/gpt-oss-120b";
                }
                else if (tag == "OpenAICompatible")
                {
                    TxtModelName.Text = "gpt-4o-mini";
                }
            }
        }

        private void SliderTemp_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtTempValue != null)
            {
                TxtTempValue.Text = e.NewValue.ToString("F1");
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtName.Text))
            {
                MessageBox.Show("Vui lòng nhập tên cho AI.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Agent.Icon = string.IsNullOrWhiteSpace(TxtIcon.Text) ? "🤖" : TxtIcon.Text.Trim();
            Agent.Name = TxtName.Text.Trim();
            Agent.Role = TxtRole.Text.Trim();
            Agent.ModelName = TxtModelName.Text.Trim();
            Agent.SystemPrompt = TxtSystemPrompt.Text.Trim();
            Agent.Temperature = Math.Round(SliderTemp.Value, 1);

            if (CmbProvider.SelectedItem is ComboBoxItem item && Enum.TryParse<AgentProvider>(item.Tag?.ToString(), out var p))
            {
                Agent.Provider = p;
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIOrchestrator.Models
{
    public enum AgentProvider
    {
        Gemini,
        Groq,
        OpenAICompatible
    }

    public enum AgentStatus
    {
        Idle,       // Chờ lượt
        Queued,     // Đã lên lịch
        Running,    // Đang xử lý
        Done,       // Đã hoàn thành
        Skipped,    // Bỏ qua (không thuộc phần việc)
        Error       // Bị lỗi
    }

    public class AiAgent : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _name = "AI Chuyên dụng";
        private string _role = "Chuyên viên";
        private string _icon = "🤖";
        private string _systemPrompt = "";
        private AgentProvider _provider = AgentProvider.Gemini;
        private string _modelName = "gemini-3.8-flash";
        private double _temperature = 0.7;
        private bool _isEnabled = true;
        private AgentStatus _status = AgentStatus.Idle;
        private string _statusMessage = "Sẵn sàng";
        private string _outputResult = "";
        private long _executionTimeMs = 0;
        private string _assignedSubtask = "";

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Role
        {
            get => _role;
            set { _role = value; OnPropertyChanged(); }
        }

        public string Icon
        {
            get => _icon;
            set { _icon = value; OnPropertyChanged(); }
        }

        public string SystemPrompt
        {
            get => _systemPrompt;
            set { _systemPrompt = value; OnPropertyChanged(); }
        }

        public AgentProvider Provider
        {
            get => _provider;
            set { _provider = value; OnPropertyChanged(); }
        }

        public string ModelName
        {
            get => _modelName;
            set { _modelName = value; OnPropertyChanged(); }
        }

        public double Temperature
        {
            get => _temperature;
            set { _temperature = value; OnPropertyChanged(); }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        public AgentStatus Status
        {
            get => _status;
            set 
            { 
                _status = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(StatusDisplayName));
                OnPropertyChanged(nameof(StatusBadgeColor));
                OnPropertyChanged(nameof(IsRunning));
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string OutputResult
        {
            get => _outputResult;
            set { _outputResult = value; OnPropertyChanged(); }
        }

        public long ExecutionTimeMs
        {
            get => _executionTimeMs;
            set { _executionTimeMs = value; OnPropertyChanged(); }
        }

        public string AssignedSubtask
        {
            get => _assignedSubtask;
            set { _assignedSubtask = value; OnPropertyChanged(); }
        }

        public bool IsRunning => Status == AgentStatus.Running;

        public string StatusDisplayName => Status switch
        {
            AgentStatus.Idle => "⏸️ Chờ lượt",
            AgentStatus.Queued => "⏳ Đã lên lịch",
            AgentStatus.Running => "⚡ Đang xử lý...",
            AgentStatus.Done => $"✅ Hoàn thành ({ExecutionTimeMs}ms)",
            AgentStatus.Skipped => "⏭️ Bỏ qua (Không cần thiết)",
            AgentStatus.Error => "❌ Bị lỗi",
            _ => "Chờ"
        };

        public string StatusBadgeColor => Status switch
        {
            AgentStatus.Idle => "#4B5563",      // Gray
            AgentStatus.Queued => "#D97706",    // Amber
            AgentStatus.Running => "#2563EB",   // Blue
            AgentStatus.Done => "#16A34A",      // Green
            AgentStatus.Skipped => "#6B7280",   // Cool Gray
            AgentStatus.Error => "#DC2626",     // Red
            _ => "#4B5563"
        };

        public void ResetExecutionState()
        {
            Status = AgentStatus.Idle;
            StatusMessage = "Sẵn sàng";
            OutputResult = "";
            ExecutionTimeMs = 0;
            AssignedSubtask = "";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

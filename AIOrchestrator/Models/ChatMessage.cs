using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace AIOrchestrator.Models
{
    public enum MessageType
    {
        User,
        Agent,
        ClarificationRequest, // AI hỏi người dùng làm rõ
        PlanApproval,         // AI hỏi duyệt kế hoạch
        StepCompleted,        // AI thông báo xong 1 bước
        SystemInfo
    }

    public class ChatMessage
    {
        public string SenderName { get; set; } = "";
        public string SenderIcon { get; set; } = "🤖";
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public MessageType Type { get; set; } = MessageType.Agent;
        public string? FollowUpSection { get; set; }
        public string? FollowUpAgentId { get; set; }
        public bool IsUser => Type == MessageType.User;

        // Các nút hành động nhanh cho người dùng bấm trực tiếp
        public List<string> QuickSuggestions { get; set; } = new();
        public bool HasQuickSuggestions => QuickSuggestions != null && QuickSuggestions.Count > 0;

        public string BubbleBackground => Type switch
        {
            MessageType.User => "#1E3A8A",                   // Blue
            MessageType.ClarificationRequest => "#7C2D12",   // Warm Amber / Orange (Cần người dùng can thiệp)
            MessageType.PlanApproval => "#365314",           // Olive green
            MessageType.StepCompleted => "#1E293B",          // Dark slate
            _ => "#1E293B"
        };

        public string HeaderColor => Type switch
        {
            MessageType.User => "#93C5FD",
            MessageType.ClarificationRequest => "#F97316",
            MessageType.PlanApproval => "#84CC16",
            _ => "#38BDF8"
        };
    }
}

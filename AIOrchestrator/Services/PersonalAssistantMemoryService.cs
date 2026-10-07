using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AIOrchestrator.Models;

namespace AIOrchestrator.Services
{
    public static class PersonalAssistantMemoryService
    {
        private const int MaxHistorySessions = 6;
        private const int MaxUserMessageLength = 500;
        private const int MaxAssistantMessageLength = 900;
        private const int MaxContextLength = 6000;

        public static string BuildContext(
            IConversationStore conversationStore,
            ConversationSession? currentSession,
            string displayName)
        {
            ArgumentNullException.ThrowIfNull(conversationStore);

            var sessions = new List<ConversationSession>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            if (currentSession != null && HasUserContent(currentSession) && seenIds.Add(currentSession.Id))
                sessions.Add(currentSession);

            foreach (ConversationSummary summary in conversationStore.ListAll())
            {
                if (sessions.Count >= MaxHistorySessions)
                    break;
                if (!seenIds.Add(summary.Id))
                    continue;

                ConversationSession? savedSession = conversationStore.Load(summary.Id);
                if (savedSession != null && HasUserContent(savedSession))
                    sessions.Add(savedSession);
            }

            if (sessions.Count == 0)
                return "";

            var context = new StringBuilder();
            context.AppendLine($"Ngữ cảnh riêng của {displayName} từ các cuộc trò chuyện gần đây (chỉ dùng khi liên quan):");
            foreach (ConversationSession session in sessions)
            {
                string section = BuildSessionContext(session);
                if (string.IsNullOrWhiteSpace(section))
                    continue;

                if (context.Length + section.Length > MaxContextLength)
                    break;
                context.AppendLine(section);
            }

            if (context.Length <= MaxContextLength)
                return context.ToString().Trim();
            return context.ToString(0, MaxContextLength).TrimEnd();
        }

        private static string BuildSessionContext(ConversationSession session)
        {
            var lines = new List<string>();
            foreach (ConversationMessage message in session.Messages
                         .Where(message => message.Type == MessageType.User && !string.IsNullOrWhiteSpace(message.Content))
                         .TakeLast(2))
            {
                lines.Add($"- Người dùng đã hỏi: {Trim(message.Content, MaxUserMessageLength)}");
            }

            ConversationMessage? lastAnswer = session.Messages.LastOrDefault(message =>
                (message.Type is MessageType.Agent or MessageType.StepCompleted) &&
                !string.IsNullOrWhiteSpace(message.Content));
            if (lastAnswer != null)
                lines.Add($"- Ngữ cảnh trả lời gần nhất: {Trim(lastAnswer.Content, MaxAssistantMessageLength)}");

            return lines.Count == 0
                ? ""
                : $"Cuộc trò chuyện “{Trim(session.Title, 120)}”:\n{string.Join("\n", lines)}";
        }

        private static bool HasUserContent(ConversationSession session) =>
            session.Messages.Any(message =>
                message.Type == MessageType.User && !string.IsNullOrWhiteSpace(message.Content));

        private static string Trim(string value, int maxLength)
        {
            string normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return normalized.Length <= maxLength
                ? normalized
                : normalized[..(maxLength - 1)] + "…";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace AIOrchestrator.Models
{
    /// <summary>
    /// A single persisted message inside a <see cref="ConversationSession"/>.
    /// Mirrors <see cref="ChatMessage"/> but is a plain, serializable record.
    /// </summary>
    public class ConversationMessage
    {
        public string SenderName { get; set; } = "";
        public string SenderIcon { get; set; } = "🤖";
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public MessageType Type { get; set; } = MessageType.Agent;
        public string? FollowUpSection { get; set; }
        public string? FollowUpAgentId { get; set; }

        public static ConversationMessage FromChatMessage(ChatMessage message) => new()
        {
            SenderName = message.SenderName,
            SenderIcon = message.SenderIcon,
            Content = message.Content,
            Timestamp = message.Timestamp,
            Type = message.Type,
            FollowUpSection = message.FollowUpSection,
            FollowUpAgentId = message.FollowUpAgentId
        };

        public ChatMessage ToChatMessage() => new()
        {
            SenderName = SenderName,
            SenderIcon = SenderIcon,
            Content = Content,
            Timestamp = Timestamp,
            Type = Type,
            FollowUpSection = FollowUpSection,
            FollowUpAgentId = FollowUpAgentId,
            QuickSuggestions = new List<string>()
        };
    }

    /// <summary>
    /// One saved conversation: the originating prompt, the final answer and the
    /// full transcript. This is the unit that gets serialized to disk.
    /// </summary>
    public class ConversationSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "Cuộc trò chuyện mới";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public string Mode { get; set; } = "";
        public string FinalAnswer { get; set; } = "";
        public string FinalImagePath { get; set; } = "";
        public List<ConversationMessage> Messages { get; set; } = new();

        /// <summary>Full transcript rendered as searchable plain text.</summary>
        public string FullText =>
            string.Join("\n", Messages.Select(m => $"{m.SenderName}: {m.Content}"));

        /// <summary>Last non-empty agent/user message, used as a list preview.</summary>
        public string Preview
        {
            get
            {
                var last = Messages.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.Content));
                if (last == null) return "";
                string text = last.Content.Replace("\r", " ").Replace("\n", " ").Trim();
                return text.Length <= 120 ? text : text.Substring(0, 117) + "...";
            }
        }

        /// <summary>
        /// Case-insensitive search across the title and every message.
        /// A blank query matches everything.
        /// </summary>
        public bool Matches(string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            string q = query.Trim();

            return Contains(Title, q)
                || Messages.Any(m => Contains(m.Content, q) || Contains(m.SenderName, q));
        }

        private static bool Contains(string? haystack, string needle) =>
            !string.IsNullOrEmpty(haystack)
            && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Derives a short readable title from the first user message.
        /// </summary>
        public static string BuildTitle(string? firstPrompt)
        {
            if (string.IsNullOrWhiteSpace(firstPrompt)) return "Cuộc trò chuyện mới";
            string text = firstPrompt.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= 60 ? text : text.Substring(0, 57) + "...";
        }
    }

    /// <summary>
    /// Lightweight projection used by the history list so the UI does not have
    /// to load full transcripts just to render the sidebar.
    /// </summary>
    public class ConversationSummary
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTime UpdatedAt { get; set; }
        public int MessageCount { get; set; }
        public string Preview { get; set; } = "";

        public string DisplayName => $"{Title}";

        public string Subtitle =>
            $"{UpdatedAt:dd/MM/yyyy HH:mm} • {MessageCount} tin nhắn";

        public static ConversationSummary FromSession(ConversationSession session) => new()
        {
            Id = session.Id,
            Title = session.Title,
            UpdatedAt = session.UpdatedAt,
            MessageCount = session.Messages.Count,
            Preview = session.Preview
        };
    }
}

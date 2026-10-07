using System.Collections.Generic;
using AIOrchestrator.Models;

namespace AIOrchestrator.Services
{
    /// <summary>
    /// Persistence + search abstraction for saved conversations.
    /// Abstracted so the UI depends on the contract, and tests can use
    /// an in-memory implementation.
    /// </summary>
    public interface IConversationStore
    {
        /// <summary>Saves (or overwrites) a session and returns it with refreshed timestamps.</summary>
        ConversationSession Save(ConversationSession session);

        /// <summary>Returns the full session, or null when the id is unknown.</summary>
        ConversationSession? Load(string id);

        /// <summary>All session summaries, newest first.</summary>
        IReadOnlyList<ConversationSummary> ListAll();

        /// <summary>
        /// Case-insensitive search over titles and message contents.
        /// A blank query returns all sessions (newest first).
        /// </summary>
        IReadOnlyList<ConversationSummary> Search(string? query);

        /// <summary>Removes a session. Returns false when it did not exist.</summary>
        bool Delete(string id);

        /// <summary>Removes every stored session. Returns the number removed.</summary>
        int DeleteAll();
    }
}

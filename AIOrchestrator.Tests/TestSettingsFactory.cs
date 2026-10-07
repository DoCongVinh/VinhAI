using System.Collections.Generic;
using AIOrchestrator.Models;

namespace AIOrchestrator.Tests
{
    /// <summary>
    /// Builds small, deterministic <see cref="AppSettings"/> graphs so tests do not
    /// depend on the large hard-coded defaults from AppSettings.CreateDefault().
    /// </summary>
    internal static class TestSettingsFactory
    {
        public const string RouterId = "router_agent";
        public const string CoderId = "coder_agent";
        public const string WriterId = "writer_agent";

        public static AppSettings CreateSettings(
            bool interactiveMode = true,
            bool coderEnabled = true,
            bool writerEnabled = true)
        {
            return new AppSettings
            {
                GeminiApiKey = "test-gemini-key",
                GroqApiKey = "test-groq-key",
                InteractiveMode = interactiveMode,
                Agents = new List<AiAgent>
                {
                    new AiAgent { Id = RouterId, Name = "Router", Icon = "🎯", IsEnabled = true, Provider = AgentProvider.Gemini },
                    new AiAgent { Id = CoderId, Name = "Coder", Icon = "💻", IsEnabled = coderEnabled, Provider = AgentProvider.Gemini },
                    new AiAgent { Id = WriterId, Name = "Writer", Icon = "✍️", IsEnabled = writerEnabled, Provider = AgentProvider.Gemini },
                }
            };
        }

        /// <summary>A well-formed router plan that selects the coder agent.</summary>
        public static string CoderPlanJson(string summary = "Kế hoạch test", string subtask = "Viết hàm cộng") =>
            $$"""
            {
              "needsClarification": false,
              "clarificationQuestion": "",
              "quickSuggestions": [],
              "summary": "{{summary}}",
              "selectedAgents": [
                { "agentId": "coder_agent", "reason": "cần code", "subtaskPrompt": "{{subtask}}" }
              ]
            }
            """;
    }
}

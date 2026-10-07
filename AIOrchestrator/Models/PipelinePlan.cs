using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AIOrchestrator.Models
{
    public class PlannedAgentStep
    {
        [JsonPropertyName("agentId")]
        public string AgentId { get; set; } = "";

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = "";

        [JsonPropertyName("subtaskPrompt")]
        public string SubtaskPrompt { get; set; } = "";
    }

    public class RouterPlan
    {
        [JsonPropertyName("needsClarification")]
        public bool NeedsClarification { get; set; } = false;

        [JsonPropertyName("clarificationQuestion")]
        public string ClarificationQuestion { get; set; } = "";

        [JsonPropertyName("quickSuggestions")]
        public List<string> QuickSuggestions { get; set; } = new();

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = "";

        [JsonPropertyName("selectedAgents")]
        public List<PlannedAgentStep> SelectedAgents { get; set; } = new();
    }
}

using System.Collections.Generic;
using System.ComponentModel;
using AIOrchestrator.Models;
using Xunit;

namespace AIOrchestrator.Tests
{
    /// <summary>
    /// Model-level behaviour: notification, computed display properties and
    /// the default settings graph used to bootstrap the application.
    /// </summary>
    public class AiAgentTests
    {
        [Fact]
        public void NewAgent_HasSaneDefaults()
        {
            var agent = new AiAgent();

            Assert.False(string.IsNullOrWhiteSpace(agent.Id));
            Assert.Equal(AgentStatus.Idle, agent.Status);
            Assert.True(agent.IsEnabled);
            Assert.Equal("Sẵn sàng", agent.StatusMessage);
            Assert.Equal("", agent.OutputResult);
            Assert.Equal(0, agent.ExecutionTimeMs);
        }

        [Fact]
        public void Id_DefaultsToUniqueValue()
        {
            var a = new AiAgent();
            var b = new AiAgent();

            Assert.NotEqual(a.Id, b.Id);
        }

        [Fact]
        public void IsRunning_ReflectsStatus()
        {
            var agent = new AiAgent { Status = AgentStatus.Running };
            Assert.True(agent.IsRunning);

            agent.Status = AgentStatus.Done;
            Assert.False(agent.IsRunning);
        }

        [Theory]
        [InlineData(AgentStatus.Idle)]
        [InlineData(AgentStatus.Queued)]
        [InlineData(AgentStatus.Running)]
        [InlineData(AgentStatus.Done)]
        [InlineData(AgentStatus.Skipped)]
        [InlineData(AgentStatus.Error)]
        public void StatusDisplayName_IsNeverEmpty(AgentStatus status)
        {
            var agent = new AiAgent { Status = status };
            Assert.False(string.IsNullOrWhiteSpace(agent.StatusDisplayName));
        }

        [Fact]
        public void StatusDisplayName_ForDone_IncludesExecutionTime()
        {
            var agent = new AiAgent { Status = AgentStatus.Done, ExecutionTimeMs = 123 };
            Assert.Contains("123", agent.StatusDisplayName);
        }

        [Fact]
        public void ResetExecutionState_ClearsPersistedPreviousRunError()
        {
            var agent = new AiAgent
            {
                Status = AgentStatus.Error,
                StatusMessage = "Groq HTTP 429",
                OutputResult = "rate limited",
                ExecutionTimeMs = 4310,
                AssignedSubtask = "previous run"
            };

            agent.ResetExecutionState();

            Assert.Equal(AgentStatus.Idle, agent.Status);
            Assert.Equal("Sẵn sàng", agent.StatusMessage);
            Assert.Empty(agent.OutputResult);
            Assert.Equal(0, agent.ExecutionTimeMs);
            Assert.Empty(agent.AssignedSubtask);
        }

        [Theory]
        [InlineData(AgentStatus.Idle)]
        [InlineData(AgentStatus.Queued)]
        [InlineData(AgentStatus.Running)]
        [InlineData(AgentStatus.Done)]
        [InlineData(AgentStatus.Skipped)]
        [InlineData(AgentStatus.Error)]
        public void StatusBadgeColor_IsHexColor(AgentStatus status)
        {
            var agent = new AiAgent { Status = status };
            Assert.Matches("^#[0-9A-Fa-f]{6}$", agent.StatusBadgeColor);
        }

        [Fact]
        public void ResetExecutionState_ClearsAllExecutionFields()
        {
            var agent = new AiAgent
            {
                Status = AgentStatus.Error,
                StatusMessage = "lỗi",
                OutputResult = "kết quả",
                ExecutionTimeMs = 42,
                AssignedSubtask = "việc"
            };

            agent.ResetExecutionState();

            Assert.Equal(AgentStatus.Idle, agent.Status);
            Assert.Equal("Sẵn sàng", agent.StatusMessage);
            Assert.Equal("", agent.OutputResult);
            Assert.Equal(0, agent.ExecutionTimeMs);
            Assert.Equal("", agent.AssignedSubtask);
        }

        [Fact]
        public void PropertyChanged_FiresForAssignedProperty()
        {
            var agent = new AiAgent();
            var changed = new List<string?>();
            agent.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            agent.Name = "Tên mới";

            Assert.Contains(nameof(AiAgent.Name), changed);
        }

        [Fact]
        public void PropertyChanged_ForStatus_AlsoRaisesComputedProperties()
        {
            var agent = new AiAgent();
            var changed = new List<string?>();
            agent.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            agent.Status = AgentStatus.Running;

            Assert.Contains(nameof(AiAgent.Status), changed);
            Assert.Contains(nameof(AiAgent.StatusDisplayName), changed);
            Assert.Contains(nameof(AiAgent.StatusBadgeColor), changed);
            Assert.Contains(nameof(AiAgent.IsRunning), changed);
        }

        [Fact]
        public void SettingSameValue_StillRaisesNotification()
        {
            // Documents the (simple) setter behaviour: no equality short-circuit.
            var agent = new AiAgent { Name = "X" };
            int raised = 0;
            agent.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AiAgent.Name)) raised++;
            };

            agent.Name = "X";

            Assert.Equal(1, raised);
        }
    }

    public class ChatMessageTests
    {
        [Fact]
        public void NewMessage_HasDefaults()
        {
            var msg = new ChatMessage();

            Assert.Equal("", msg.SenderName);
            Assert.Equal("🤖", msg.SenderIcon);
            Assert.Equal(MessageType.Agent, msg.Type);
            Assert.False(msg.IsUser);
            Assert.Empty(msg.QuickSuggestions);
            Assert.False(msg.HasQuickSuggestions);
        }

        [Fact]
        public void IsUser_TrueOnlyForUserType()
        {
            var msg = new ChatMessage { Type = MessageType.User };
            Assert.True(msg.IsUser);
        }

        [Fact]
        public void HasQuickSuggestions_TrueWhenPopulated()
        {
            var msg = new ChatMessage { QuickSuggestions = new List<string> { "a" } };
            Assert.True(msg.HasQuickSuggestions);
        }

        [Fact]
        public void HasQuickSuggestions_NullList_IsFalse()
        {
            var msg = new ChatMessage { QuickSuggestions = null! };
            Assert.False(msg.HasQuickSuggestions);
        }

        [Theory]
        [InlineData(MessageType.User)]
        [InlineData(MessageType.ClarificationRequest)]
        [InlineData(MessageType.PlanApproval)]
        [InlineData(MessageType.StepCompleted)]
        [InlineData(MessageType.SystemInfo)]
        [InlineData(MessageType.Agent)]
        public void BubbleBackground_IsHexColor(MessageType type)
        {
            var msg = new ChatMessage { Type = type };
            Assert.Matches("^#[0-9A-Fa-f]{6}$", msg.BubbleBackground);
        }

        [Theory]
        [InlineData(MessageType.User)]
        [InlineData(MessageType.ClarificationRequest)]
        [InlineData(MessageType.PlanApproval)]
        [InlineData(MessageType.StepCompleted)]
        [InlineData(MessageType.SystemInfo)]
        public void HeaderColor_IsHexColor(MessageType type)
        {
            var msg = new ChatMessage { Type = type };
            Assert.Matches("^#[0-9A-Fa-f]{6}$", msg.HeaderColor);
        }
    }

    public class AppSettingsTests
    {
        [Fact]
        public void CreateDefault_UsesGitHubUpdateFeedAndDoesNotContainProviderKeys()
        {
            var settings = AppSettings.CreateDefault();

            Assert.Equal(AppSettings.DefaultUpdateManifestUrl, settings.UpdateManifestUrl);
            Assert.Empty(settings.GeminiApiKey);
            Assert.Empty(settings.GroqApiKey);
        }

        [Fact]
        public void CreateDefault_HasTwelveAgentsIncludingNewSpecialists()
        {
            var settings = AppSettings.CreateDefault();
            Assert.Equal(12, settings.Agents.Count);
            Assert.Contains(settings.Agents, agent => agent.Id == "chart_agent");
            Assert.Contains(settings.Agents, agent => agent.Id == "graphic_designer_agent");
            Assert.Contains(settings.Agents, agent => agent.Id == "office_agent");
            Assert.Contains(settings.Agents, agent => agent.Id == "video_agent");
            Assert.Contains(settings.Agents, agent => agent.Id == "presentation_agent");
            Assert.Contains(settings.Agents, agent => agent.Id == "object_analysis_agent");
            Assert.Contains(settings.Agents, agent => agent.Id == "personal_assistant_agent");
        }

        [Fact]
        public void CreateDefault_IncludesRouterAgent()
        {
            var settings = AppSettings.CreateDefault();
            Assert.Contains(settings.Agents, a => a.Id == "router_agent");
        }

        [Fact]
        public void CreateDefault_AllAgentIdsAreUnique()
        {
            var settings = AppSettings.CreateDefault();
            var ids = new HashSet<string>();
            foreach (var agent in settings.Agents)
            {
                Assert.True(ids.Add(agent.Id), $"Duplicate agent id: {agent.Id}");
            }
        }

        [Fact]
        public void CreateDefault_EnablesInteractiveMode()
        {
            var settings = AppSettings.CreateDefault();
            Assert.True(settings.InteractiveMode);
        }

        [Fact]
        public void CreateDefault_DefaultModeIsSmartRouter()
        {
            var settings = AppSettings.CreateDefault();
            Assert.Equal(ExecutionMode.SmartRouter, settings.DefaultMode);
        }

        [Fact]
        public void CreateDefault_EveryAgentHasNameAndRole()
        {
            var settings = AppSettings.CreateDefault();
            foreach (var agent in settings.Agents)
            {
                Assert.False(string.IsNullOrWhiteSpace(agent.Name));
                Assert.False(string.IsNullOrWhiteSpace(agent.Role));
            }
        }

        [Fact]
        public void PersonalAssistantRuntimeIdentityIsNotSavedInSettingsJson()
        {
            var settings = AppSettings.CreateDefault();
            settings.AssistantUserDisplayName = "Alice";
            settings.PersonalAssistantMemoryContext = "private history";

            string json = System.Text.Json.JsonSerializer.Serialize(settings);

            Assert.DoesNotContain("Alice", json);
            Assert.DoesNotContain("private history", json);
        }
    }
}

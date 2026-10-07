using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIOrchestrator.Models;
using AIOrchestrator.Services;
using AIOrchestrator.Tests.Fakes;
using Xunit;

namespace AIOrchestrator.Tests
{
    /// <summary>
    /// Behaviour of the DirectAgent and CustomPipeline execution modes, plus the
    /// engine's error handling, cancellation and event/callback surface.
    /// </summary>
    public class OrchestrationEngineExecutionTests
    {
        private static OrchestrationEngine CreateEngine(FakeProviderService provider)
            => new OrchestrationEngine(provider);

        // ---------------------------------------------------------------------
        // Constructor / guard clauses (negative)
        // ---------------------------------------------------------------------

        [Fact]
        public void Constructor_NullGeminiService_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new OrchestrationEngine((IAiProviderService)null!));
        }

        [Fact]
        public void Constructor_Default_UsesRealProviders()
        {
            // Must not throw and must remain constructible for existing call sites.
            var engine = new OrchestrationEngine();
            Assert.NotNull(engine);
        }

        // ---------------------------------------------------------------------
        // DirectAgent mode
        // ---------------------------------------------------------------------

        [Fact]
        public async Task ExecuteAsync_DirectAgent_UsesSpecifiedTargetAgent()
        {
            var provider = new FakeProviderService().EnqueueResponse("WRITER_REPLY");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            OrchestrationResult result = await engine.ExecuteAsync(
                "hỏi riêng", settings, ExecutionMode.DirectAgent, targetAgentId: TestSettingsFactory.WriterId);

            Assert.True(result.Success);
            Assert.Equal("WRITER_REPLY", result.FinalAnswer);
            Assert.Single(provider.Calls);
            Assert.Equal(TestSettingsFactory.WriterId, provider.Calls[0].Agent.Id);
        }

        [Fact]
        public async Task ExecuteAsync_DirectAgent_PassesUserPromptVerbatim()
        {
            var provider = new FakeProviderService().EnqueueResponse("X");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            await engine.ExecuteAsync("PROMPT_GỐC_CHÍNH_XÁC", settings, ExecutionMode.DirectAgent,
                targetAgentId: TestSettingsFactory.CoderId);

            Assert.Equal("PROMPT_GỐC_CHÍNH_XÁC", provider.Calls[0].Prompt);
        }

        [Fact]
        public async Task ExecuteAsync_DirectAgent_MarksOtherAgentsSkipped()
        {
            var provider = new FakeProviderService().EnqueueResponse("X");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            await engine.ExecuteAsync("hỏi riêng", settings, ExecutionMode.DirectAgent,
                targetAgentId: TestSettingsFactory.CoderId);

            Assert.Equal(AgentStatus.Skipped, settings.Agents.First(a => a.Id == TestSettingsFactory.WriterId).Status);
            Assert.Equal(AgentStatus.Skipped, settings.Agents.First(a => a.Id == TestSettingsFactory.RouterId).Status);
            Assert.Equal(AgentStatus.Done, settings.Agents.First(a => a.Id == TestSettingsFactory.CoderId).Status);
        }

        [Fact]
        public async Task ExecuteAsync_DirectAgent_NullTargetId_FallsBackToFirstEnabledNonRouter()
        {
            var provider = new FakeProviderService().EnqueueResponse("X");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            await engine.ExecuteAsync("hỏi riêng", settings, ExecutionMode.DirectAgent, targetAgentId: null);

            Assert.Equal(TestSettingsFactory.CoderId, provider.Calls[0].Agent.Id);
        }

        [Fact]
        public async Task ExecuteAsync_DirectAgent_UnknownTargetId_FallsBackToFirstEnabledNonRouter()
        {
            var provider = new FakeProviderService().EnqueueResponse("X");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            await engine.ExecuteAsync("hỏi riêng", settings, ExecutionMode.DirectAgent, targetAgentId: "does_not_exist");

            Assert.Equal(TestSettingsFactory.CoderId, provider.Calls[0].Agent.Id);
        }

        [Fact]
        public async Task ExecuteAsync_DirectAgent_TargetDisabled_StillUsesItWhenExplicitlyRequested()
        {
            var provider = new FakeProviderService().EnqueueResponse("X");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings(writerEnabled: false);

            await engine.ExecuteAsync("hỏi riêng", settings, ExecutionMode.DirectAgent,
                targetAgentId: TestSettingsFactory.WriterId);

            Assert.Equal(TestSettingsFactory.WriterId, provider.Calls[0].Agent.Id);
        }

        // ---------------------------------------------------------------------
        // CustomPipeline mode
        // ---------------------------------------------------------------------

        [Fact]
        public async Task ExecuteAsync_CustomPipeline_RunsAllEnabledNonRouterAgentsInOrder()
        {
            var provider = new FakeProviderService().EnqueueResponse("A").EnqueueResponse("B");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            OrchestrationResult result = await engine.ExecuteAsync("pipeline", settings, ExecutionMode.CustomPipeline);

            Assert.True(result.Success);
            Assert.Equal(new[] { TestSettingsFactory.CoderId, TestSettingsFactory.WriterId },
                result.ExecutedAgents.Select(a => a.Id).ToArray());
            Assert.Equal("B", result.FinalAnswer);
        }

        [Fact]
        public async Task ExecuteAsync_CustomPipeline_ExcludesRouterAgent()
        {
            var provider = new FakeProviderService().EnqueueResponse("A").EnqueueResponse("B");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            await engine.ExecuteAsync("pipeline", settings, ExecutionMode.CustomPipeline);

            Assert.DoesNotContain(provider.Calls, c => c.Agent.Id == TestSettingsFactory.RouterId);
        }

        [Fact]
        public async Task ExecuteAsync_CustomPipeline_SkipsDisabledAgents()
        {
            var provider = new FakeProviderService().EnqueueResponse("A");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings(writerEnabled: false);

            OrchestrationResult result = await engine.ExecuteAsync("pipeline", settings, ExecutionMode.CustomPipeline);

            Assert.Single(result.ExecutedAgents);
            Assert.Equal(TestSettingsFactory.CoderId, result.ExecutedAgents[0].Id);
        }

        [Fact]
        public async Task ExecuteAsync_CustomPipeline_NoEnabledAgents_ReturnsFailureWithErrorMessage()
        {
            var provider = new FakeProviderService();
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings(coderEnabled: false, writerEnabled: false);

            OrchestrationResult result = await engine.ExecuteAsync("pipeline", settings, ExecutionMode.CustomPipeline);

            Assert.False(result.Success);
            Assert.Contains("Không có AI chuyên dụng nào", result.ErrorMessage);
            Assert.Equal(0, provider.CallCount);
        }

        // ---------------------------------------------------------------------
        // Error handling / negative
        // ---------------------------------------------------------------------

        [Fact]
        public async Task ExecuteAsync_ProviderThrows_ReturnsFailureAndCapturesMessage()
        {
            var provider = new FakeProviderService()
                .EnqueueException(new InvalidOperationException("mạng lỗi"));
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            OrchestrationResult result = await engine.ExecuteAsync("x", settings, ExecutionMode.DirectAgent,
                targetAgentId: TestSettingsFactory.CoderId);

            Assert.False(result.Success);
            Assert.Equal("mạng lỗi", result.ErrorMessage);
        }

        [Fact]
        public async Task ExecuteAsync_ProviderThrows_MarksAgentAsError()
        {
            var provider = new FakeProviderService()
                .EnqueueException(new Exception("boom"));
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            await engine.ExecuteAsync("x", settings, ExecutionMode.DirectAgent,
                targetAgentId: TestSettingsFactory.CoderId);

            AiAgent coder = settings.Agents.First(a => a.Id == TestSettingsFactory.CoderId);
            Assert.Equal(AgentStatus.Error, coder.Status);
            Assert.Contains("boom", coder.StatusMessage);
        }

        [Fact]
        public async Task ExecuteAsync_TotalDuration_IsAlwaysMeasured()
        {
            var provider = new FakeProviderService().EnqueueResponse("x");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            OrchestrationResult result = await engine.ExecuteAsync("x", settings, ExecutionMode.DirectAgent,
                targetAgentId: TestSettingsFactory.CoderId);

            Assert.True(result.TotalDuration >= TimeSpan.Zero);
        }

        // ---------------------------------------------------------------------
        // Cancellation
        // ---------------------------------------------------------------------

        [Fact]
        public async Task ExecuteAsync_AlreadyCancelledToken_ReturnsFailureWithCancellationMessage()
        {
            var provider = new FakeProviderService().EnqueueResponse("should not run");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            OrchestrationResult result = await engine.ExecuteAsync("x", settings, ExecutionMode.SmartRouter,
                targetAgentId: null, promptUserCallback: null, ct: cts.Token);

            Assert.False(result.Success);
            Assert.Contains("bị hủy", result.ErrorMessage);
        }

        [Fact]
        public async Task ExecuteAsync_ProviderReportsCancellation_ReturnsFailure()
        {
            // Models a real cancellation: the provider observes the token and
            // surfaces OperationCanceledException, which the engine must catch
            // and translate into a failure result (rather than crashing).
            var provider = new FakeProviderService()
                .EnqueueException(new OperationCanceledException("cancelled by user"));

            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            OrchestrationResult result = await engine.ExecuteAsync("x", settings, ExecutionMode.CustomPipeline,
                targetAgentId: null, promptUserCallback: null, ct: CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("bị hủy", result.ErrorMessage);
        }

        [Fact]
        public async Task ExecuteAsync_CancellationCallbackThrows_ReturnsFailure()
        {
            // The clarification callback can observe cancellation (e.g. the user
            // closes the dialog); the engine must handle that gracefully too.
            var provider = new FakeProviderService().EnqueueResponse(ClarificationJson());
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings(interactiveMode: true);

            OrchestrationResult result = await engine.ExecuteAsync("x", settings, ExecutionMode.SmartRouter,
                targetAgentId: null,
                promptUserCallback: (_, _) => throw new OperationCanceledException("user closed dialog"),
                ct: CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("bị hủy", result.ErrorMessage);
        }

        private static string ClarificationJson() => """
            {
              "needsClarification": true,
              "clarificationQuestion": "Cần làm rõ",
              "quickSuggestions": ["A"]
            }
            """;

        // ---------------------------------------------------------------------
        // Reset + events (positive)
        // ---------------------------------------------------------------------

        [Fact]
        public async Task ExecuteAsync_ResetsDirtyAgentStateBeforeRunning()
        {
            var provider = new FakeProviderService().EnqueueResponse("X");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            AiAgent coder = settings.Agents.First(a => a.Id == TestSettingsFactory.CoderId);
            coder.Status = AgentStatus.Error;
            coder.OutputResult = "rác cũ";
            coder.ExecutionTimeMs = 9999;
            coder.AssignedSubtask = "rác cũ";

            await engine.ExecuteAsync("x", settings, ExecutionMode.DirectAgent,
                targetAgentId: TestSettingsFactory.CoderId);

            // After a successful run the stale error fields must be gone.
            Assert.Equal(AgentStatus.Done, coder.Status);
            Assert.Equal("X", coder.OutputResult);
            Assert.NotEqual(9999, coder.ExecutionTimeMs);
        }

        [Fact]
        public async Task ExecuteAsync_RaisesAgentUpdatedEventsForEveryAgent()
        {
            var provider = new FakeProviderService().EnqueueResponse("X");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            var updatedIds = new List<string>();
            engine.OnAgentUpdated += a => updatedIds.Add(a.Id);

            await engine.ExecuteAsync("x", settings, ExecutionMode.DirectAgent,
                targetAgentId: TestSettingsFactory.CoderId);

            Assert.Contains(TestSettingsFactory.CoderId, updatedIds);
            Assert.Contains(TestSettingsFactory.WriterId, updatedIds);
        }

        [Fact]
        public async Task ExecuteAsync_RaisesChatMessageEventsDuringRun()
        {
            var provider = new FakeProviderService().EnqueueResponse("KẾT QUẢ_TỪ_AGENT");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            var messages = new List<ChatMessage>();
            engine.OnChatMessage += m => messages.Add(m);

            await engine.ExecuteAsync("x", settings, ExecutionMode.DirectAgent,
                targetAgentId: TestSettingsFactory.CoderId);

            Assert.Contains(messages, m => m.Content == "KẾT QUẢ_TỪ_AGENT" && m.Type == MessageType.StepCompleted);
        }

        [Fact]
        public async Task ExecuteAsync_RaisesLogMessageEvents()
        {
            var provider = new FakeProviderService().EnqueueResponse("X");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            var logs = new List<string>();
            engine.OnLogMessage += l => logs.Add(l);

            await engine.ExecuteAsync("x", settings, ExecutionMode.DirectAgent,
                targetAgentId: TestSettingsFactory.CoderId);

            Assert.NotEmpty(logs);
        }

        [Fact]
        public async Task ExecuteAsync_NoSubscribers_DoesNotThrow()
        {
            var provider = new FakeProviderService().EnqueueResponse("X");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            // No event handlers attached at all.
            OrchestrationResult result = await engine.ExecuteAsync("x", settings, ExecutionMode.DirectAgent,
                targetAgentId: TestSettingsFactory.CoderId);

            Assert.True(result.Success);
        }

        // ---------------------------------------------------------------------
        // CallAgentAsync (public surface)
        // ---------------------------------------------------------------------

        [Fact]
        public async Task CallAgentAsync_RoutesGeminiAgent_ToGeminiService()
        {
            var gemini = new FakeProviderService().EnqueueResponse("TỪ_GEMINI");
            var groq = new FakeProviderService().EnqueueResponse("TỪ_GROQ");
            var engine = new OrchestrationEngine(gemini, groq);
            var settings = TestSettingsFactory.CreateSettings();

            var agent = new AiAgent { Id = "g", Provider = AgentProvider.Gemini };
            string output = await engine.CallAgentAsync(agent, "prompt", settings, CancellationToken.None);

            Assert.Equal("TỪ_GEMINI", output);
            Assert.Equal(1, gemini.CallCount);
            Assert.Equal(0, groq.CallCount);
        }

        [Fact]
        public async Task CallAgentAsync_RoutesGroqAgent_ToGroqService()
        {
            var gemini = new FakeProviderService().EnqueueResponse("TỪ_GEMINI");
            var groq = new FakeProviderService().EnqueueResponse("TỪ_GROQ");
            var engine = new OrchestrationEngine(gemini, groq);
            var settings = TestSettingsFactory.CreateSettings();

            var agent = new AiAgent { Id = "q", Provider = AgentProvider.Groq };
            string output = await engine.CallAgentAsync(agent, "prompt", settings, CancellationToken.None);

            Assert.Equal("TỪ_GROQ", output);
            Assert.Equal(0, gemini.CallCount);
            Assert.Equal(1, groq.CallCount);
        }

        [Fact]
        public async Task CallAgentAsync_WhenGeminiServiceNull_DefaultsToRealGroqProvider()
        {
            // The single-arg constructor keeps the production Groq service, but the
            // injected Gemini double must still be used for Gemini agents.
            var gemini = new FakeProviderService().EnqueueResponse("TỪ_GEMINI");
            var engine = new OrchestrationEngine(gemini);
            var settings = TestSettingsFactory.CreateSettings();

            var agent = new AiAgent { Id = "g", Provider = AgentProvider.Gemini };
            string output = await engine.CallAgentAsync(agent, "prompt", settings, CancellationToken.None);

            Assert.Equal("TỪ_GEMINI", output);
        }

        // ---------------------------------------------------------------------
        // PostChat (positive + edge)
        // ---------------------------------------------------------------------

        [Fact]
        public void PostChat_RaisesChatMessageWithProvidedFields()
        {
            var engine = new OrchestrationEngine(new FakeProviderService());
            ChatMessage? captured = null;
            engine.OnChatMessage += m => captured = m;

            engine.PostChat("Tác giả", "🎯", "nội dung", MessageType.Agent, new List<string> { "gợi ý 1" });

            Assert.NotNull(captured);
            Assert.Equal("Tác giả", captured!.SenderName);
            Assert.Equal("🎯", captured.SenderIcon);
            Assert.Equal("nội dung", captured.Content);
            Assert.Equal(MessageType.Agent, captured.Type);
            Assert.Single(captured.QuickSuggestions);
        }

        [Fact]
        public void PostChat_NullSuggestions_YieldsEmptyList()
        {
            var engine = new OrchestrationEngine(new FakeProviderService());
            ChatMessage? captured = null;
            engine.OnChatMessage += m => captured = m;

            engine.PostChat("a", "b", "c", MessageType.SystemInfo, suggestions: null);

            Assert.NotNull(captured);
            Assert.NotNull(captured!.QuickSuggestions);
            Assert.Empty(captured.QuickSuggestions);
        }
    }
}

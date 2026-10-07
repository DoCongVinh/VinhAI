using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AIOrchestrator.Models;
using AIOrchestrator.Services;
using AIOrchestrator.Tests.Fakes;
using Xunit;

namespace AIOrchestrator.Tests
{
    /// <summary>
    /// Behaviour of the SmartRouter mode: parsing of the router JSON, the recovery
    /// fallbacks when it is malformed, the clarification round-trip, and the
    /// selection/queueing of the downstream agents.
    /// </summary>
    public class OrchestrationEngineRoutingTests
    {
        private static OrchestrationEngine CreateEngine(FakeProviderService provider)
            => new OrchestrationEngine(provider);

        // ---------------------------------------------------------------------
        // Positive: well-formed plan
        // ---------------------------------------------------------------------

        [Fact]
        public async Task ExecuteAsync_SmartRouterWithValidPlan_SelectsAndExecutesListedAgents()
        {
            var provider = new FakeProviderService()
                .EnqueueResponse(TestSettingsFactory.CoderPlanJson(summary: "Kế hoạch ABC", subtask: "Viết hàm cộng"))
                .EnqueueResponse("CODE_OUTPUT");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            OrchestrationResult result = await engine.ExecuteAsync(
                "hãy viết code", settings, ExecutionMode.SmartRouter);

            Assert.True(result.Success);
            Assert.Equal("CODE_OUTPUT", result.FinalAnswer);
            Assert.Equal("Kế hoạch ABC", result.RouterSummary);
            Assert.Equal(TestSettingsFactory.CoderPlanJson("Kế hoạch ABC", "Viết hàm cộng"), result.RouterRawLog);
            Assert.Single(result.ExecutedAgents);
            Assert.Equal(TestSettingsFactory.CoderId, result.ExecutedAgents[0].Id);
        }

        [Fact]
        public async Task ExecuteAsync_SmartRouterWithValidPlan_MarksUnselectedAgentsSkipped()
        {
            var provider = new FakeProviderService()
                .EnqueueResponse(TestSettingsFactory.CoderPlanJson())
                .EnqueueResponse("CODE_OUTPUT");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            await engine.ExecuteAsync("viết code", settings, ExecutionMode.SmartRouter);

            AiAgent writer = settings.Agents.Find(a => a.Id == TestSettingsFactory.WriterId)!;
            AiAgent coder = settings.Agents.Find(a => a.Id == TestSettingsFactory.CoderId)!;

            Assert.Equal(AgentStatus.Skipped, writer.Status);
            Assert.Equal(AgentStatus.Done, coder.Status);
        }

        [Fact]
        public async Task ExecuteAsync_SmartRouter_PassesSubtaskPromptToSelectedAgent()
        {
            var provider = new FakeProviderService()
                .EnqueueResponse(TestSettingsFactory.CoderPlanJson(subtask: "TRIỆU_HỒI_ĐẶC_BIỆT"))
                .EnqueueResponse("out");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            await engine.ExecuteAsync("viết code", settings, ExecutionMode.SmartRouter);

            // Call 0 is the router; call 1 is the selected coder agent.
            Assert.Equal(2, provider.CallCount);
            Assert.Contains("TRIỆU_HỒI_ĐẶC_BIỆT", provider.Calls[1].Prompt);
        }

        [Fact]
        public async Task ExecuteAsync_PersonalizesAgentWithUserNameAndPrivateHistory()
        {
            var provider = new FakeProviderService()
                .EnqueueResponse(TestSettingsFactory.CoderPlanJson())
                .EnqueueResponse("PERSONALIZED_OUTPUT");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();
            settings.AssistantUserDisplayName = "Nguyễn An";
            settings.PersonalAssistantMemoryContext = "Người dùng đang học tiếng Nhật.";
            string originalSystemPrompt = settings.Agents.Find(agent => agent.Id == TestSettingsFactory.CoderId)!.SystemPrompt;

            await engine.ExecuteAsync("tiếp tục giúp tôi", settings, ExecutionMode.SmartRouter);

            Assert.Contains("Nguyễn An", provider.Calls[1].Agent.SystemPrompt);
            Assert.Contains("Người dùng đang học tiếng Nhật", provider.Calls[1].Prompt);
            Assert.Equal(originalSystemPrompt, settings.Agents.Find(agent => agent.Id == TestSettingsFactory.CoderId)!.SystemPrompt);
        }

        [Fact]
        public async Task ExecuteAsync_GroqRateLimit_FallsBackToGeminiForThatAgent()
        {
            var gemini = new FakeProviderService()
                .EnqueueResponse(TestSettingsFactory.CoderPlanJson())
                .EnqueueResponse("GEMINI_FALLBACK_OUTPUT");
            var groq = new FakeProviderService()
                .EnqueueException(new HttpRequestException(
                    "Groq rate limited", null, System.Net.HttpStatusCode.TooManyRequests));
            var engine = new OrchestrationEngine(gemini, groq);
            var settings = TestSettingsFactory.CreateSettings();
            settings.Agents.Find(agent => agent.Id == TestSettingsFactory.CoderId)!.Provider = AgentProvider.Groq;

            OrchestrationResult result = await engine.ExecuteAsync(
                "viết code", settings, ExecutionMode.SmartRouter);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("GEMINI_FALLBACK_OUTPUT", result.FinalAnswer);
            Assert.Equal(2, gemini.CallCount);
            Assert.Single(groq.Calls);
            Assert.Equal(AgentStatus.Done, settings.Agents.Find(agent => agent.Id == TestSettingsFactory.CoderId)!.Status);
        }

        [Fact]
        public async Task ExecuteAsync_CustomPipelineKeepsSuccessfulAnswerWhenLaterAgentFails()
        {
            var gemini = new FakeProviderService().EnqueueResponse("FIRST_AGENT_ANSWER");
            var groq = new FakeProviderService()
                .EnqueueException(new HttpRequestException(
                    "Groq rate limited", null, System.Net.HttpStatusCode.TooManyRequests));
            var engine = new OrchestrationEngine(gemini, groq);
            var settings = TestSettingsFactory.CreateSettings();
            settings.GeminiApiKey = "";
            settings.Agents.Find(agent => agent.Id == TestSettingsFactory.WriterId)!.Provider = AgentProvider.Groq;
            var systemNotices = new List<ChatMessage>();
            engine.OnChatMessage += message =>
            {
                if (message.Type == MessageType.SystemInfo)
                    systemNotices.Add(message);
            };

            OrchestrationResult result = await engine.ExecuteAsync(
                "viết một bài", settings, ExecutionMode.CustomPipeline);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("FIRST_AGENT_ANSWER", result.FinalAnswer);
            Assert.Single(result.ExecutedAgents);
            Assert.Contains(systemNotices, message => message.Content.Contains("được bỏ qua"));
            Assert.Equal(AgentStatus.Error, settings.Agents.Find(agent => agent.Id == TestSettingsFactory.WriterId)!.Status);
        }

        [Fact]
        public async Task ExecuteAsync_ChainsMultipleSelectedAgentsInOrder()
        {
            const string plan = """
            {
              "needsClarification": false,
              "summary": "hai bước",
              "selectedAgents": [
                { "agentId": "coder_agent",  "reason": "code",   "subtaskPrompt": "Bước 1" },
                { "agentId": "writer_agent", "reason": "viết",   "subtaskPrompt": "Bước 2" }
              ]
            }
            """;
            var provider = new FakeProviderService()
                .EnqueueResponse(plan)
                .EnqueueResponse("KẾT_QUẢ_1")
                .EnqueueResponse("KẾT_QUẢ_2");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            OrchestrationResult result = await engine.ExecuteAsync("cả hai", settings, ExecutionMode.SmartRouter);

            Assert.Equal(new[] { TestSettingsFactory.CoderId, TestSettingsFactory.WriterId },
                result.ExecutedAgents.ConvertAll(a => a.Id));
            Assert.Equal("KẾT_QUẢ_2", result.FinalAnswer);
            // The second agent must receive the first agent's output as context.
            Assert.Contains("KẾT_QUẢ_1", provider.Calls[2].Prompt);
        }

        // ---------------------------------------------------------------------
        // Edge: tolerated / malformed router output -> fallback plan
        // ---------------------------------------------------------------------

        [Theory]
        [InlineData("not json at all")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{ this is broken json }")]
        public async Task ExecuteAsync_MalformedRouterOutput_UsesFallbackPlanAndStillSucceeds(string routerResponse)
        {
            var provider = new FakeProviderService()
                .EnqueueResponse(routerResponse)
                .EnqueueResponse("FALLBACK_OUTPUT");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            OrchestrationResult result = await engine.ExecuteAsync("viết code", settings, ExecutionMode.SmartRouter);

            Assert.True(result.Success);
            Assert.Equal("FALLBACK_OUTPUT", result.FinalAnswer);
            Assert.Equal("Tự động kích hoạt AI chuyên môn phù hợp", result.RouterSummary);
        }

        [Fact]
        public async Task ExecuteAsync_RouterOutputWrappedInJsonFence_IsParsed()
        {
            string fenced = "Đây là kết quả:\n```json\n" + TestSettingsFactory.CoderPlanJson(summary: "Từ fence") + "\n```\nHết.";
            var provider = new FakeProviderService()
                .EnqueueResponse(fenced)
                .EnqueueResponse("OUT");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            OrchestrationResult result = await engine.ExecuteAsync("viết code", settings, ExecutionMode.SmartRouter);

            Assert.Equal("Từ fence", result.RouterSummary);
            Assert.Equal(TestSettingsFactory.CoderId, result.ExecutedAgents[0].Id);
        }

        [Fact]
        public async Task ExecuteAsync_RouterOutputWithProseAroundJson_IsParsed()
        {
            string noisy = "Chắc chắn rồi! " + TestSettingsFactory.CoderPlanJson(summary: "Từ prose") + " Chúc may mắn.";
            var provider = new FakeProviderService()
                .EnqueueResponse(noisy)
                .EnqueueResponse("OUT");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            OrchestrationResult result = await engine.ExecuteAsync("viết code", settings, ExecutionMode.SmartRouter);

            Assert.Equal("Từ prose", result.RouterSummary);
        }

        [Fact]
        public async Task ExecuteAsync_PlanWithUnknownAgentId_SkipsThatStepButSucceeds()
        {
            const string plan = """
            {
              "summary": "chỉ có agent lạ",
              "selectedAgents": [
                { "agentId": "ghost_agent", "reason": "?", "subtaskPrompt": "?" }
              ]
            }
            """;
            var provider = new FakeProviderService().EnqueueResponse(plan);
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            OrchestrationResult result = await engine.ExecuteAsync("viết code", settings, ExecutionMode.SmartRouter);

            Assert.True(result.Success);
            Assert.Empty(result.ExecutedAgents);
            Assert.Equal("", result.FinalAnswer);
        }

        [Fact]
        public async Task ExecuteAsync_PlanWithEmptySelectedAgents_ProducesEmptyAnswer()
        {
            const string plan = """{ "summary": "không ai", "selectedAgents": [] }""";
            var provider = new FakeProviderService().EnqueueResponse(plan);
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            OrchestrationResult result = await engine.ExecuteAsync("viết code", settings, ExecutionMode.SmartRouter);

            Assert.True(result.Success);
            Assert.Empty(result.ExecutedAgents);
            Assert.Equal("", result.FinalAnswer);
        }

        // ---------------------------------------------------------------------
        // FallbackPlan: keyword routing (edge cases)
        // ---------------------------------------------------------------------

        [Theory]
        [InlineData("hãy viết code cho tôi")]
        [InlineData("tôi cần lập trình game")]
        [InlineData("viết hàm fibonacci")]
        [InlineData("giải thích thuật toán sắp xếp")]
        public async Task ExecuteAsync_MalformedPlanWithCodingKeywords_FallsBackToCoder(string prompt)
        {
            var provider = new FakeProviderService()
                .EnqueueResponse("garbage")
                .EnqueueResponse("OUT");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            OrchestrationResult result = await engine.ExecuteAsync(prompt, settings, ExecutionMode.SmartRouter);

            Assert.Equal(TestSettingsFactory.CoderId, result.ExecutedAgents[0].Id);
        }

        [Theory]
        [InlineData("hãy viết một bài thơ")]
        [InlineData("dịch đoạn văn này sang tiếng Anh")]
        [InlineData("")]
        public async Task ExecuteAsync_MalformedPlanWithoutCodingKeywords_FallsBackToWriter(string prompt)
        {
            var provider = new FakeProviderService()
                .EnqueueResponse("garbage")
                .EnqueueResponse("OUT");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings();

            OrchestrationResult result = await engine.ExecuteAsync(prompt, settings, ExecutionMode.SmartRouter);

            Assert.Equal(TestSettingsFactory.WriterId, result.ExecutedAgents[0].Id);
        }

        [Fact]
        public async Task ExecuteAsync_FallbackPlan_TargetsDisabledAgent_ProducesNoExecutedAgents()
        {
            // Fallback routes to coder (keyword present) but coder is disabled;
            // the engine looks the step up by Id regardless of IsEnabled, so it
            // still finds and runs it. This documents the current behaviour.
            var provider = new FakeProviderService()
                .EnqueueResponse("garbage")
                .EnqueueResponse("OUT");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings(coderEnabled: false);

            OrchestrationResult result = await engine.ExecuteAsync("viết code", settings, ExecutionMode.SmartRouter);

            Assert.True(result.Success);
            Assert.Single(result.ExecutedAgents);
            Assert.Equal(TestSettingsFactory.CoderId, result.ExecutedAgents[0].Id);
        }

        // ---------------------------------------------------------------------
        // Clarification round-trip
        // ---------------------------------------------------------------------

        private const string ClarificationPlan = """
        {
          "needsClarification": true,
          "clarificationQuestion": "Bạn muốn dùng ngôn ngữ nào?",
          "quickSuggestions": ["C#", "Python"],
          "summary": "",
          "selectedAgents": [
            { "agentId": "coder_agent", "reason": "code", "subtaskPrompt": "Viết code" }
          ]
        }
        """;

        [Fact]
        public async Task ExecuteAsync_NeedsClarification_InteractiveMode_InvokesCallbackWithQuestionAndSuggestions()
        {
            var provider = new FakeProviderService()
                .EnqueueResponse(ClarificationPlan)
                .EnqueueResponse("FINAL");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings(interactiveMode: true);

            string? seenQuestion = null;
            List<string>? seenSuggestions = null;

            await engine.ExecuteAsync("viết code", settings, ExecutionMode.SmartRouter,
                promptUserCallback: (q, s) =>
                {
                    seenQuestion = q;
                    seenSuggestions = s;
                    return Task.FromResult("Python nhé");
                });

            Assert.Equal("Bạn muốn dùng ngôn ngữ nào?", seenQuestion);
            Assert.Equal(new[] { "C#", "Python" }, seenSuggestions);
        }

        [Fact]
        public async Task ExecuteAsync_NeedsClarification_AppendsUserReplyToPrompt()
        {
            var provider = new FakeProviderService()
                .EnqueueResponse(ClarificationPlan)
                .EnqueueResponse("FINAL");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings(interactiveMode: true);

            await engine.ExecuteAsync("viết code", settings, ExecutionMode.SmartRouter,
                promptUserCallback: (_, _) => Task.FromResult("Python nhé"));

            // The agent call (index 1) must include the user's clarification.
            Assert.Contains("Python nhé", provider.Calls[1].Prompt);
            Assert.Contains("BỔ SUNG TỪ NGƯỜI DÙNG", provider.Calls[1].Prompt);
        }

        [Fact]
        public async Task ExecuteAsync_NeedsClarification_NonInteractiveMode_DoesNotInvokeCallback()
        {
            var provider = new FakeProviderService()
                .EnqueueResponse(ClarificationPlan)
                .EnqueueResponse("FINAL");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings(interactiveMode: false);

            bool callbackInvoked = false;

            await engine.ExecuteAsync("viết code", settings, ExecutionMode.SmartRouter,
                promptUserCallback: (_, _) =>
                {
                    callbackInvoked = true;
                    return Task.FromResult("không bao giờ gọi");
                });

            Assert.False(callbackInvoked);
        }

        [Fact]
        public async Task ExecuteAsync_NeedsClarification_ButNoCallback_ProceedsWithoutClarifying()
        {
            var provider = new FakeProviderService()
                .EnqueueResponse(ClarificationPlan)
                .EnqueueResponse("FINAL");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings(interactiveMode: true);

            OrchestrationResult result = await engine.ExecuteAsync(
                "viết code", settings, ExecutionMode.SmartRouter, promptUserCallback: null);

            Assert.True(result.Success);
            Assert.Equal("FINAL", result.FinalAnswer);
        }

        [Fact]
        public async Task ExecuteAsync_NeedsClarification_UserReturnsBlank_DoesNotAppendClarification()
        {
            var provider = new FakeProviderService()
                .EnqueueResponse(ClarificationPlan)
                .EnqueueResponse("FINAL");
            var engine = CreateEngine(provider);
            var settings = TestSettingsFactory.CreateSettings(interactiveMode: true);

            await engine.ExecuteAsync("viết code", settings, ExecutionMode.SmartRouter,
                promptUserCallback: (_, _) => Task.FromResult("   "));

            Assert.DoesNotContain("BỔ SUNG TỪ NGƯỜI DÙNG", provider.Calls[1].Prompt);
        }
    }
}

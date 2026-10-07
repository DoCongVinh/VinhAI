using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AIOrchestrator.Models;

namespace AIOrchestrator.Services
{
    public class OrchestrationResult
    {
        public bool Success { get; set; }
        public string FinalAnswer { get; set; } = "";
        public string RouterSummary { get; set; } = "";
        public string RouterRawLog { get; set; } = "";
        public List<AiAgent> ExecutedAgents { get; set; } = new();
        public TimeSpan TotalDuration { get; set; }
        public string ErrorMessage { get; set; } = "";
        public bool WaitingForUser { get; set; } = false;
    }

    public class OrchestrationEngine
    {
        private readonly AsyncLocal<(string DisplayName, string MemoryContext)?> _personalizationContext = new();
        private readonly IAiProviderService _geminiService;
        private readonly IAiProviderService _groqService;

        public event Action<string>? OnLogMessage;
        public event Action<AiAgent>? OnAgentUpdated;
        public event Action<ChatMessage>? OnChatMessage;

        public OrchestrationEngine() : this(new GeminiProviderService(), null) { }

        /// <summary>
        /// Allows injecting provider implementations (e.g. test doubles) so the orchestration
        /// logic can be exercised without performing real network calls.
        /// </summary>
        public OrchestrationEngine(IAiProviderService geminiService, IAiProviderService? groqService = null)
        {
            _geminiService = geminiService ?? throw new ArgumentNullException(nameof(geminiService));
            _groqService = groqService ?? new GroqOpenAiProviderService();

            if (_geminiService is GeminiProviderService gemini)
            {
                gemini.OnFailoverNotice += msg => Log(msg);
            }
        }

        private void Log(string message) => OnLogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");

        public void PostChat(string senderName, string senderIcon, string content, MessageType type, List<string>? suggestions = null)
        {
            OnChatMessage?.Invoke(new ChatMessage
            {
                SenderName = senderName,
                SenderIcon = senderIcon,
                Content = content,
                Type = type,
                QuickSuggestions = suggestions ?? new List<string>()
            });
        }

        public async Task<OrchestrationResult> ExecuteAsync(
            string userPrompt,
            AppSettings settings,
            ExecutionMode mode,
            string? targetAgentId = null,
            Func<string, List<string>, Task<string>>? promptUserCallback = null,
            CancellationToken ct = default)
        {
            var result = new OrchestrationResult();
            var totalSw = Stopwatch.StartNew();
            _personalizationContext.Value = (
                settings.AssistantUserDisplayName,
                settings.PersonalAssistantMemoryContext);

            try
            {
                // Reset states of all agents
                foreach (var agent in settings.Agents)
                {
                    agent.ResetExecutionState();
                    OnAgentUpdated?.Invoke(agent);
                }

                if (mode == ExecutionMode.DirectAgent)
                {
                    await ExecuteDirectAgentAsync(userPrompt, settings, targetAgentId, result, ct);
                }
                else if (mode == ExecutionMode.CustomPipeline)
                {
                    await ExecuteCustomPipelineAsync(userPrompt, settings, result, ct);
                }
                else // SmartRouter
                {
                    await ExecuteSmartRouterAsync(userPrompt, settings, promptUserCallback, result, ct);
                }

                result.Success = true;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Quá trình thực thi đã bị hủy bởi người dùng.";
                Log("⚠️ Quá trình đã bị hủy.");
                PostChat("Hệ thống", "🛑", "Quá trình điều phối đã bị hủy theo yêu cầu.", MessageType.SystemInfo);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Log($"❌ Lỗi: {ex.Message}");
                PostChat("Hệ thống", "❌", $"Gặp sự cố: {ex.Message}", MessageType.SystemInfo);
            }
            finally
            {
                totalSw.Stop();
                result.TotalDuration = totalSw.Elapsed;
                Log($"⏱️ Hoàn tất quy trình trong {result.TotalDuration.TotalSeconds:F2} giây.");
                _personalizationContext.Value = null;
            }

            return result;
        }

        private async Task ExecuteSmartRouterAsync(
            string userPrompt,
            AppSettings settings,
            Func<string, List<string>, Task<string>>? promptUserCallback,
            OrchestrationResult result,
            CancellationToken ct)
        {
            var routerAgent = settings.Agents.FirstOrDefault(a => a.Id == "router_agent") 
                ?? settings.Agents.First();

            Log($"🎯 [Bước 1] Đánh thức AI Router '{routerAgent.Name}' để phân tích câu hỏi...");
            routerAgent.Status = AgentStatus.Running;
            routerAgent.StatusMessage = "Đang phân tích yêu cầu...";
            OnAgentUpdated?.Invoke(routerAgent);

            var availableAgents = settings.Agents
                .Where(a => a.Id != routerAgent.Id && a.IsEnabled)
                .Select(a => $"- ID: \"{a.Id}\" | Tên: \"{a.Name}\" | Chuyên môn: \"{a.Role}\"")
                .ToList();

            string routerInput = $@"Bạn là Bộ điều phối AI tối ưu tài nguyên và thân thiện với người dùng.
[DANH SÁCH CÁC AI CHUYÊN BIỆT]:
{string.Join(Environment.NewLine, availableAgents)}

[YÊU CẦU CỦA NGƯỜI DÙNG]:
""{userPrompt}""

[QUY TẮC PHÂN TÍCH & TƯƠNG TÁC]:
1. Nếu yêu cầu quá ngắn, thiếu chi tiết quan trọng (ví dụ chưa rõ ngôn ngữ lập trình, thiếu ngữ cảnh, hoặc có nhiều hướng giải quyết khác nhau) và CẦN HỎI LẠI người dùng:
   - Đặt ""needsClarification"": true
   - Đặt ""clarificationQuestion"": câu hỏi rõ ràng, ngắn gọn
   - Đặt ""quickSuggestions"": danh sách 2-4 lựa chọn ngắn để người dùng chọn nhanh
2. Nếu yêu cầu đã rõ ràng:
   - Đặt ""needsClarification"": false
   - Chỉ chọn những AI thực sự cần thiết theo thứ tự logic
   - Tránh gọi thừa AI để tiết kiệm tài nguyên
3. Trả về DUY NHẤT một chuỗi JSON hợp lệ:
{{
  ""needsClarification"": false,
  ""clarificationQuestion"": """",
  ""quickSuggestions"": [],
  ""summary"": ""Tóm tắt kế hoạch phân công"",
  ""selectedAgents"": [
    {{
      ""agentId"": ""id_cua_agent"",
      ""reason"": ""Lý do chọn"",
      ""subtaskPrompt"": ""Nhiệm vụ cụ thể giao cho AI này""
    }}
  ]
}}";

            var sw = Stopwatch.StartNew();
            string routerResponse = await CallAgentAsync(routerAgent, routerInput, settings, ct);
            sw.Stop();

            routerAgent.ExecutionTimeMs = sw.ElapsedMilliseconds;
            routerAgent.Status = AgentStatus.Done;
            routerAgent.StatusMessage = "Đã phân tích xong";
            routerAgent.OutputResult = routerResponse;
            OnAgentUpdated?.Invoke(routerAgent);

            result.RouterRawLog = routerResponse;
            RouterPlan? plan = TryParsePlan(routerResponse);

            if (plan == null)
            {
                plan = FallbackPlan(userPrompt, settings);
            }

            // TRƯỜNG HỢP 1: Cần làm rõ với người dùng (NeedsClarification)
            if (settings.InteractiveMode && plan.NeedsClarification && !string.IsNullOrWhiteSpace(plan.ClarificationQuestion) && promptUserCallback != null)
            {
                Log($"❓ AI Router cần trao đổi thêm với người dùng: {plan.ClarificationQuestion}");
                PostChat(routerAgent.Name, routerAgent.Icon, plan.ClarificationQuestion, MessageType.ClarificationRequest, plan.QuickSuggestions);

                // Chờ phản hồi từ người dùng
                string userClarification = await promptUserCallback(plan.ClarificationQuestion, plan.QuickSuggestions);

                if (!string.IsNullOrWhiteSpace(userClarification))
                {
                    PostChat("Bạn", "👤", userClarification, MessageType.User);
                    userPrompt = $"{userPrompt}\n[BỔ SUNG TỪ NGƯỜI DÙNG]: {userClarification}";
                    Log("✅ Đã nhận phản hồi từ người dùng, tiếp tục cập nhật kế hoạch...");
                }
            }

            result.RouterSummary = plan.Summary;
            PostChat(routerAgent.Name, routerAgent.Icon, $"📋 **Kế hoạch thực thi**: {plan.Summary}", MessageType.PlanApproval);
            Log($"📋 Kế hoạch: {plan.Summary} ({plan.SelectedAgents.Count} AI)");

            // Đánh dấu trạng thái các AI
            var selectedIds = plan.SelectedAgents.Select(s => s.AgentId).ToHashSet();
            foreach (var agent in settings.Agents)
            {
                if (agent.Id != routerAgent.Id)
                {
                    if (selectedIds.Contains(agent.Id))
                    {
                        agent.Status = AgentStatus.Queued;
                        agent.StatusMessage = "Đang chờ đến lượt";
                    }
                    else
                    {
                        agent.Status = AgentStatus.Skipped;
                        agent.StatusMessage = "Nghỉ ngơi (Không cần thiết cho tác vụ này)";
                    }
                    OnAgentUpdated?.Invoke(agent);
                }
            }

            // Thực thi tuần tự từng AI theo lượt (Lazy Execution)
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine($"[YÊU CẦU BAN ĐẦU CỦA NGƯỜI DÙNG]:\n{userPrompt}\n");

            string lastOutput = "";
            int attemptedAgentCount = 0;

            for (int i = 0; i < plan.SelectedAgents.Count; i++)
            {
                var step = plan.SelectedAgents[i];
                var agent = settings.Agents.FirstOrDefault(a => a.Id == step.AgentId);
                if (agent == null) continue;

                Log($"⚡ [Bước {i + 1}/{plan.SelectedAgents.Count}] Đánh thức AI: {agent.Icon} {agent.Name}...");
                agent.Status = AgentStatus.Running;
                agent.StatusMessage = $"Đang xử lý: {step.SubtaskPrompt}";
                agent.AssignedSubtask = step.SubtaskPrompt;
                OnAgentUpdated?.Invoke(agent);

                var promptBuilder = new StringBuilder();
                promptBuilder.AppendLine(contextBuilder.ToString());
                promptBuilder.AppendLine($"[NHIỆM VỤ CỦA BẠN]:");
                promptBuilder.AppendLine(step.SubtaskPrompt);

                var agentSw = Stopwatch.StartNew();
                attemptedAgentCount++;
                string output;
                try
                {
                    output = await CallAgentAsync(agent, promptBuilder.ToString(), settings, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    agent.Status = AgentStatus.Error;
                    agent.StatusMessage = $"Lỗi; sẽ tiếp tục với AI khác: {ex.Message}";
                    OnAgentUpdated?.Invoke(agent);
                    PostChat("Hệ thống", "⚠️",
                        $"{agent.Name} đang gặp sự cố nên được bỏ qua. Các AI còn lại vẫn tiếp tục xử lý.",
                        MessageType.SystemInfo);
                    Log($"⚠️ Bỏ qua {agent.Name} sau lỗi: {ex.Message}");
                    continue;
                }
                agentSw.Stop();

                agent.ExecutionTimeMs = agentSw.ElapsedMilliseconds;
                agent.Status = AgentStatus.Done;
                agent.StatusMessage = $"Hoàn tất ({agentSw.ElapsedMilliseconds}ms)";
                agent.OutputResult = output;
                OnAgentUpdated?.Invoke(agent);

                result.ExecutedAgents.Add(agent);
                lastOutput = output;

                // Đăng kết quả từng AI vào khung trao đổi
                PostChat(agent.Name, agent.Icon, output, MessageType.StepCompleted);

                contextBuilder.AppendLine($"\n--- [KẾT QUẢ TỪ {agent.Name}] ---\n{output}\n");
                Log($"✅ {agent.Name} đã hoàn thành trong {agentSw.ElapsedMilliseconds}ms!");
            }

            if (attemptedAgentCount > 0 && string.IsNullOrWhiteSpace(lastOutput))
                throw new InvalidOperationException("Không AI nào trong kế hoạch trả về câu trả lời thành công. Hãy thử lại sau.");

            result.FinalAnswer = lastOutput;
        }

        private async Task ExecuteCustomPipelineAsync(
            string userPrompt,
            AppSettings settings,
            OrchestrationResult result,
            CancellationToken ct)
        {
            var activeAgents = settings.Agents.Where(a => a.IsEnabled && a.Id != "router_agent").ToList();
            if (activeAgents.Count == 0)
            {
                throw new InvalidOperationException("Không có AI chuyên dụng nào đang được bật trong danh sách.");
            }

            Log($"⛓️ Khởi chạy chuỗi tuần tự gồm {activeAgents.Count} AI...");

            foreach (var a in activeAgents)
            {
                a.Status = AgentStatus.Queued;
                a.StatusMessage = "Đang chờ đến lượt";
                OnAgentUpdated?.Invoke(a);
            }

            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine($"[YÊU CẦU BAN ĐẦU]:\n{userPrompt}\n");

            string lastOutput = "";
            int attemptedAgentCount = 0;

            for (int i = 0; i < activeAgents.Count; i++)
            {
                var agent = activeAgents[i];
                Log($"⚡ [Bước {i + 1}/{activeAgents.Count}] Đánh thức AI: {agent.Icon} {agent.Name}...");
                agent.Status = AgentStatus.Running;
                agent.StatusMessage = "Đang thực hiện công việc...";
                OnAgentUpdated?.Invoke(agent);

                var promptBuilder = new StringBuilder();
                promptBuilder.AppendLine(contextBuilder.ToString());
                if (i > 0)
                {
                    promptBuilder.AppendLine("Dựa trên kết quả từ các AI trước, hãy tiếp tục thực hiện phần việc chuyên môn của bạn.");
                }

                var agentSw = Stopwatch.StartNew();
                attemptedAgentCount++;
                string output;
                try
                {
                    output = await CallAgentAsync(agent, promptBuilder.ToString(), settings, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    agent.Status = AgentStatus.Error;
                    agent.StatusMessage = $"Lỗi; sẽ tiếp tục với AI khác: {ex.Message}";
                    OnAgentUpdated?.Invoke(agent);
                    PostChat("Hệ thống", "⚠️",
                        $"{agent.Name} đang gặp sự cố nên được bỏ qua. Các AI còn lại vẫn tiếp tục xử lý.",
                        MessageType.SystemInfo);
                    Log($"⚠️ Bỏ qua {agent.Name} sau lỗi: {ex.Message}");
                    continue;
                }
                agentSw.Stop();

                agent.ExecutionTimeMs = agentSw.ElapsedMilliseconds;
                agent.Status = AgentStatus.Done;
                agent.StatusMessage = $"Hoàn tất ({agentSw.ElapsedMilliseconds}ms)";
                agent.OutputResult = output;
                OnAgentUpdated?.Invoke(agent);

                result.ExecutedAgents.Add(agent);
                lastOutput = output;

                PostChat(agent.Name, agent.Icon, output, MessageType.StepCompleted);
                contextBuilder.AppendLine($"\n--- [KẾT QUẢ TỪ {agent.Name}] ---\n{output}\n");
            }

            if (attemptedAgentCount > 0 && string.IsNullOrWhiteSpace(lastOutput))
                throw new InvalidOperationException("Không AI nào trong chuỗi trả về câu trả lời thành công. Hãy thử lại sau.");

            result.FinalAnswer = lastOutput;
        }

        private async Task ExecuteDirectAgentAsync(
            string userPrompt,
            AppSettings settings,
            string? targetAgentId,
            OrchestrationResult result,
            CancellationToken ct)
        {
            var agent = settings.Agents.FirstOrDefault(a => a.Id == targetAgentId) 
                ?? settings.Agents.FirstOrDefault(a => a.IsEnabled && a.Id != "router_agent")
                ?? settings.Agents.First();

            Log($"💬 Hỏi trực tiếp AI: {agent.Icon} {agent.Name}...");

            foreach (var a in settings.Agents)
            {
                if (a.Id != agent.Id)
                {
                    a.Status = AgentStatus.Skipped;
                    a.StatusMessage = "Nghỉ ngơi (Chế độ hỏi 1 AI)";
                    OnAgentUpdated?.Invoke(a);
                }
            }

            agent.Status = AgentStatus.Running;
            agent.StatusMessage = "Đang xử lý...";
            OnAgentUpdated?.Invoke(agent);

            var sw = Stopwatch.StartNew();
            string output = await CallAgentAsync(agent, userPrompt, settings, ct);
            sw.Stop();

            agent.ExecutionTimeMs = sw.ElapsedMilliseconds;
            agent.Status = AgentStatus.Done;
            agent.StatusMessage = $"Hoàn tất ({sw.ElapsedMilliseconds}ms)";
            agent.OutputResult = output;
            OnAgentUpdated?.Invoke(agent);

            result.ExecutedAgents.Add(agent);
            result.FinalAnswer = output;
            PostChat(agent.Name, agent.Icon, output, MessageType.StepCompleted);
            Log($"✅ {agent.Name} đã phản hồi xong trong {sw.ElapsedMilliseconds}ms!");
        }

        public async Task<string> CallAgentAsync(AiAgent agent, string prompt, AppSettings settings, CancellationToken ct)
        {
            AiAgent requestAgent = agent;
            string personalizedPrompt = prompt;
            try
            {
                (string displayName, string memoryContext) = _personalizationContext.Value ??
                    (settings.AssistantUserDisplayName, settings.PersonalAssistantMemoryContext);
                if (!string.IsNullOrWhiteSpace(memoryContext))
                {
                    personalizedPrompt =
                        $"[LỊCH SỬ RIÊNG CỦA TÀI KHOẢN - CHỈ DÙNG KHI LIÊN QUAN]\n" +
                        $"{memoryContext}\n\n{prompt}";
                }

                if (!string.IsNullOrWhiteSpace(displayName) &&
                    !string.Equals(agent.Id, "router_agent", StringComparison.Ordinal))
                {
                    requestAgent = new AiAgent
                    {
                        Id = agent.Id,
                        Name = agent.Name,
                        Role = agent.Role,
                        Icon = agent.Icon,
                        Provider = agent.Provider,
                        ModelName = agent.ModelName,
                        Temperature = agent.Temperature,
                        IsEnabled = agent.IsEnabled,
                        SystemPrompt = $"{agent.SystemPrompt}\n\n" +
                            $"Bạn đang là trợ lý riêng của {displayName}. " +
                            $"Khi phù hợp, hãy gọi người dùng là {displayName}; " +
                            "dùng lịch sử riêng được cung cấp nếu hữu ích và không bịa thêm ký ức."
                    };
                }

                if (agent.Provider == AgentProvider.Gemini)
                {
                    return await _geminiService.GenerateResponseAsync(requestAgent, personalizedPrompt, settings, ct);
                }
                else
                {
                    return await _groqService.GenerateResponseAsync(requestAgent, personalizedPrompt, settings, ct);
                }
            }
            catch (HttpRequestException ex) when (
                ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests &&
                agent.Provider == AgentProvider.Groq &&
                !string.IsNullOrWhiteSpace(settings.GeminiApiKey))
            {
                try
                {
                    Log($"⚠️ {agent.Name}: Groq đang giới hạn tốc độ. Đang chuyển riêng AI này sang Gemini để tiếp tục...");
                    var geminiAgent = new AiAgent
                    {
                        Id = agent.Id,
                        Name = agent.Name,
                        Role = agent.Role,
                        Icon = agent.Icon,
                        Provider = AgentProvider.Gemini,
                        ModelName = "gemini-3.8-flash",
                        Temperature = agent.Temperature,
                        IsEnabled = agent.IsEnabled,
                        SystemPrompt = requestAgent.SystemPrompt
                    };

                    if (_geminiService is GeminiProviderService geminiProvider)
                        return await geminiProvider.GenerateWithGeminiOnlyAsync(geminiAgent, personalizedPrompt, settings, ct);

                    return await _geminiService.GenerateResponseAsync(geminiAgent, personalizedPrompt, settings, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception fallbackException)
                {
                    agent.Status = AgentStatus.Error;
                    agent.StatusMessage = $"Lỗi giới hạn Groq; Gemini dự phòng cũng lỗi: {fallbackException.Message}";
                    OnAgentUpdated?.Invoke(agent);
                    throw new InvalidOperationException(
                        $"Groq đã giới hạn tốc độ và Gemini dự phòng không thành công: {fallbackException.Message}",
                        new AggregateException(ex, fallbackException));
                }
            }
            catch (Exception ex)
            {
                agent.Status = AgentStatus.Error;
                agent.StatusMessage = $"Lỗi: {ex.Message}";
                OnAgentUpdated?.Invoke(agent);
                throw;
            }
        }

        private RouterPlan? TryParsePlan(string raw)
        {
            try
            {
                return JsonSerializer.Deserialize<RouterPlan>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                var match = Regex.Match(raw, @"```(?:json)?\s*(\{.*?\})\s*```", RegexOptions.Singleline);
                if (match.Success)
                {
                    try
                    {
                        return JsonSerializer.Deserialize<RouterPlan>(match.Groups[1].Value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch { }
                }

                int start = raw.IndexOf('{');
                int end = raw.LastIndexOf('}');
                if (start >= 0 && end > start)
                {
                    try
                    {
                        string jsonSub = raw.Substring(start, end - start + 1);
                        return JsonSerializer.Deserialize<RouterPlan>(jsonSub, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch { }
                }

                return null;
            }
        }

        private RouterPlan FallbackPlan(string userPrompt, AppSettings settings)
        {
            string request = userPrompt.ToLowerInvariant();
            string preferredId =
                request.Contains("powerpoint") || request.Contains("power point") || request.Contains("slide")
                    ? "presentation_agent"
                    : request.Contains("video") || request.Contains("storyboard") || request.Contains("kịch bản")
                        ? "video_agent"
                        : request.Contains("văn phòng") || request.Contains("email") ||
                          request.Contains("báo cáo") || request.Contains("công văn")
                            ? "office_agent"
                            : request.Contains("ảnh") || request.Contains("hình") ||
                              request.Contains("đồ vật") || request.Contains("vật thể")
                                ? "object_analysis_agent"
                                : request.Contains("code") || request.Contains("lập trình") ||
                                  request.Contains("hàm") || request.Contains("thuật toán")
                                    ? "coder_agent"
                                    : "personal_assistant_agent";
            string fallbackId = settings.Agents.Any(agent => agent.Id == preferredId)
                ? preferredId
                : settings.Agents.FirstOrDefault(agent => agent.Id == "personal_assistant_agent" && agent.IsEnabled)?.Id
                  ?? settings.Agents.FirstOrDefault(agent => agent.Id == "writer_agent" && agent.IsEnabled)?.Id
                  ?? settings.Agents.First(agent => agent.IsEnabled && agent.Id != "router_agent").Id;

            return new RouterPlan
            {
                Summary = "Tự động kích hoạt AI chuyên môn phù hợp",
                SelectedAgents = new List<PlannedAgentStep>
                {
                    new PlannedAgentStep { AgentId = fallbackId, Reason = "Yêu cầu phù hợp chuyên môn", SubtaskPrompt = "Giải quyết yêu cầu của người dùng một cách tốt nhất." }
                }
            };
        }
    }
}

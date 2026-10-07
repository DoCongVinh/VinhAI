using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIOrchestrator.Models;

namespace AIOrchestrator.Services
{
    public class GeminiProviderService : IAiProviderService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15) // Giảm từ 90s xuống 15s để không bao giờ bị đơ giao diện
        };

        private readonly GroqOpenAiProviderService _groqFailoverService = new();

        public event Action<string>? OnFailoverNotice;

        public Task<string> GenerateWithGeminiOnlyAsync(
            AiAgent agent, string prompt, AppSettings settings, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(settings.GeminiApiKey))
                throw new InvalidOperationException("Chưa cấu hình Google Gemini API key.");

            string model = string.IsNullOrWhiteSpace(agent.ModelName)
                ? "gemini-3.8-flash"
                : agent.ModelName.Trim().Replace("models/", "");
            if (model.Contains("gemini-2.5-flash", StringComparison.OrdinalIgnoreCase) ||
                model.Contains("gemini-1.5-flash", StringComparison.OrdinalIgnoreCase))
            {
                model = "gemini-3.8-flash";
            }

            return SendGeminiRequestAsync(model, agent, prompt, settings.GeminiApiKey.Trim(), ct);
        }

        public async Task<string> GenerateResponseAsync(AiAgent agent, string prompt, AppSettings settings, CancellationToken ct = default)
        {
            string model = string.IsNullOrWhiteSpace(agent.ModelName) ? "gemini-3.8-flash" : agent.ModelName.Trim().Replace("models/", "");

            if (model.Contains("gemini-2.5-flash") || model.Contains("gemini-1.5-flash"))
            {
                model = "gemini-3.8-flash";
            }

            // Thử gọi Gemini trước (nếu có key)
            if (!string.IsNullOrWhiteSpace(settings.GeminiApiKey))
            {
                try
                {
                    return await SendGeminiRequestAsync(model, agent, prompt, settings.GeminiApiKey.Trim(), ct);
                }
                catch (Exception ex)
                {
                    OnFailoverNotice?.Invoke($"⚠️ Google Gemini ({model}) phản hồi chậm hoặc quá tải: {ex.Message}. Đang thử model dự phòng...");

                    // Thử model gemini-3.5-flash
                    try
                    {
                        return await SendGeminiRequestAsync("gemini-3.5-flash", agent, prompt, settings.GeminiApiKey.Trim(), ct);
                    }
                    catch (Exception ex2)
                    {
                        // Nếu vẫn lỗi và có key Groq -> TỰ ĐỘNG CHUYỂN SANG GROQ SIÊU TỐC
                        if (!string.IsNullOrWhiteSpace(settings.GroqApiKey))
                        {
                            OnFailoverNotice?.Invoke("⚡ Máy chủ Google Gemini tạm thời không phản hồi. Tự động chuyển tiếp sang Groq siêu tốc để hoàn thành công việc ngay lập tức!");
                            var groqFallbackAgent = new AiAgent
                            {
                                Provider = AgentProvider.Groq,
                                ModelName = "openai/gpt-oss-120b",
                                SystemPrompt = agent.SystemPrompt,
                                Temperature = agent.Temperature
                            };
                            return await _groqFailoverService.GenerateResponseAsync(groqFallbackAgent, prompt, settings, ct);
                        }

                        throw new Exception($"Không thể kết nối Google Gemini: {ex2.Message}. Gợi ý: Hãy sử dụng Groq trong Cài Đặt (⚙️) vì Groq chạy siêu tốc và ổn định 100%.", ex2);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(settings.GroqApiKey))
            {
                // Chưa có key Gemini nhưng có key Groq -> chạy qua Groq
                OnFailoverNotice?.Invoke("ℹ️ Chưa có Gemini Key, tự động chạy qua Groq Cloud...");
                var groqAgent = new AiAgent
                {
                    Provider = AgentProvider.Groq,
                    ModelName = "openai/gpt-oss-120b",
                    SystemPrompt = agent.SystemPrompt,
                    Temperature = agent.Temperature
                };
                return await _groqFailoverService.GenerateResponseAsync(groqAgent, prompt, settings, ct);
            }
            else
            {
                throw new InvalidOperationException("Chưa cấu hình API Key nào. Vui lòng vào Cài đặt (⚙️) để nhập API key miễn phí từ Google hoặc Groq.");
            }
        }

        private async Task<string> SendGeminiRequestAsync(string model, AiAgent agent, string prompt, string apiKey, CancellationToken ct)
        {
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            var payload = new
            {
                system_instruction = string.IsNullOrWhiteSpace(agent.SystemPrompt) ? null : new
                {
                    parts = new[] { new { text = agent.SystemPrompt } }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = prompt } }
                    }
                },
                generationConfig = new
                {
                    temperature = agent.Temperature
                }
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsync(url, content, ct);
            }
            catch (Exception ex)
            {
                throw new Exception($"Lỗi kết nối ({ex.Message})", ex);
            }

            string responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    using var errDoc = JsonDocument.Parse(responseBody);
                    if (errDoc.RootElement.TryGetProperty("error", out var errElement) &&
                        errElement.TryGetProperty("message", out var msgElement))
                    {
                        throw new Exception($"Google Gemini lỗi ({response.StatusCode}): {msgElement.GetString()}");
                    }
                }
                catch (JsonException) { }

                throw new Exception($"Google Gemini trả về mã lỗi {response.StatusCode}: {responseBody}");
            }

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var candidates = doc.RootElement.GetProperty("candidates");
                if (candidates.GetArrayLength() == 0)
                {
                    return "(Không nhận được phản hồi từ Gemini)";
                }

                var parts = candidates[0].GetProperty("content").GetProperty("parts");
                if (parts.GetArrayLength() == 0)
                {
                    return "(Phản hồi rỗng)";
                }

                return parts[0].GetProperty("text").GetString() ?? "";
            }
            catch (Exception ex)
            {
                throw new Exception($"Không thể phân tích phản hồi từ Gemini API: {ex.Message}", ex);
            }
        }
    }
}

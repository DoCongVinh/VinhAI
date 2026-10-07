using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using AIOrchestrator.Models;

namespace AIOrchestrator.Services
{
    public class GroqOpenAiProviderService : IAiProviderService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(90)
        };

        public async Task<string> GenerateResponseAsync(AiAgent agent, string prompt, AppSettings settings, CancellationToken ct = default)
        {
            string apiKey;
            string endpointUrl;

            if (agent.Provider == AgentProvider.Groq)
            {
                apiKey = settings.GroqApiKey?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new InvalidOperationException("Chưa cấu hình Groq API Key. Vui lòng vào Cài đặt (⚙️) để nhập API key miễn phí từ console.groq.com.");
                }
                endpointUrl = "https://api.groq.com/openai/v1/chat/completions";
            }
            else // OpenAICompatible
            {
                apiKey = settings.OpenAiApiKey?.Trim() ?? "";
                string baseEndpoint = string.IsNullOrWhiteSpace(settings.OpenAiBaseUrl) ? "https://api.openai.com/v1" : settings.OpenAiBaseUrl.Trim().TrimEnd('/');
                endpointUrl = $"{baseEndpoint}/chat/completions";
            }

            string model = string.IsNullOrWhiteSpace(agent.ModelName)
                ? (agent.Provider == AgentProvider.Groq ? "openai/gpt-oss-120b" : "gpt-4o-mini")
                : agent.ModelName.Trim();

            // Tự động nâng cấp model Groq cũ nếu có
            if (agent.Provider == AgentProvider.Groq && (model.Contains("llama-3.3-70b-versatile") || model.Contains("llama-3.1-8b")))
            {
                model = "openai/gpt-oss-120b";
            }

            var messages = new List<object>();

            if (!string.IsNullOrWhiteSpace(agent.SystemPrompt))
            {
                messages.Add(new { role = "system", content = agent.SystemPrompt });
            }

            messages.Add(new { role = "user", content = prompt });

            var payload = new
            {
                model = model,
                messages = messages,
                temperature = agent.Temperature
            };

            var json = JsonSerializer.Serialize(payload);
            using HttpResponseMessage response = await SendWithRateLimitRetryAsync(
                endpointUrl, apiKey, json, agent.Provider, ct);
            string responseBody = await response.Content.ReadAsStringAsync(ct);

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var choices = doc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() == 0)
                {
                    return "(Không nhận được phản hồi)";
                }

                var message = choices[0].GetProperty("message");
                return message.GetProperty("content").GetString() ?? "";
            }
            catch (Exception ex)
            {
                throw new Exception($"Không thể phân tích phản hồi API: {ex.Message}. Nội dung thô: {responseBody}", ex);
            }
        }

        private static async Task<HttpResponseMessage> SendWithRateLimitRetryAsync(
            string endpointUrl,
            string apiKey,
            string json,
            AgentProvider provider,
            CancellationToken ct)
        {
            const int maxRetries = 2;

            for (int attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.SendAsync(request, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    string providerName = provider == AgentProvider.Groq ? "Groq" : "OpenAI/Custom";
                    throw new Exception($"Lỗi kết nối tới {providerName} API: {ex.Message}", ex);
                }

                if ((int)response.StatusCode != 429 || attempt >= maxRetries)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string errorBody = await response.Content.ReadAsStringAsync(ct);
                        string message = GetApiErrorMessage(response, errorBody);
                        response.Dispose();
                        throw new HttpRequestException(message, null, response.StatusCode);
                    }

                    return response;
                }

                TimeSpan retryDelay = await GetRetryDelayAsync(response, ct);
                response.Dispose();
                await Task.Delay(retryDelay, ct);
            }
        }

        private static string GetApiErrorMessage(HttpResponseMessage response, string responseBody)
        {
            string? apiMessage = null;
            try
            {
                using JsonDocument errorDocument = JsonDocument.Parse(responseBody);
                if (errorDocument.RootElement.TryGetProperty("error", out JsonElement error) &&
                    error.TryGetProperty("message", out JsonElement message))
                {
                    apiMessage = message.GetString();
                }
            }
            catch (JsonException)
            {
            }

            string providerName = response.RequestMessage?.RequestUri?.Host.Contains("groq", StringComparison.OrdinalIgnoreCase) == true
                ? "Groq"
                : "API";
            string detail = string.IsNullOrWhiteSpace(apiMessage) ? response.ReasonPhrase ?? "Lỗi không xác định" : apiMessage;

            if ((int)response.StatusCode == 429)
            {
                return $"{providerName} đang giới hạn tốc độ yêu cầu (HTTP 429). Ứng dụng đã tự thử lại nhưng chưa thành công. Hãy chờ khoảng một phút rồi thử lại, hoặc giảm số AI đang bật/chọn một AI chuyên biệt. Chi tiết: {detail}";
            }

            return $"Lỗi ({response.StatusCode}): {detail}";
        }

        private static async Task<TimeSpan> GetRetryDelayAsync(HttpResponseMessage response, CancellationToken ct)
        {
            TimeSpan? delay = null;
            if (response.Headers.RetryAfter?.Delta is TimeSpan retryAfter)
            {
                delay = retryAfter;
            }

            if (delay == null && response.Content != null)
            {
                string body = await response.Content.ReadAsStringAsync(ct);
                Match match = Regex.Match(body, @"try again in\s+([0-9]+(?:\.[0-9]+)?(?:m|s))", RegexOptions.IgnoreCase);
                if (match.Success)
                    delay = ParseDuration(match.Groups[1].Value);
            }

            if (delay == null && response.Headers.TryGetValues("x-ratelimit-reset-tokens", out var tokenResetValues))
                delay = ParseDuration(tokenResetValues.FirstOrDefault());

            TimeSpan boundedDelay = delay ?? TimeSpan.FromSeconds(5);
            if (boundedDelay < TimeSpan.FromSeconds(1))
                return TimeSpan.FromSeconds(1);
            if (boundedDelay > TimeSpan.FromSeconds(60))
                return TimeSpan.FromSeconds(60);
            return boundedDelay;
        }

        private static TimeSpan? ParseDuration(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string duration = value.Trim();

            if (double.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
                return TimeSpan.FromSeconds(seconds);

            Match match = Regex.Match(duration, @"^(?:(?<minutes>\d+(?:\.\d+)?)m)?(?:(?<seconds>\d+(?:\.\d+)?)s)?$");
            if (!match.Success || (!match.Groups["minutes"].Success && !match.Groups["seconds"].Success))
                return null;

            double minutes = match.Groups["minutes"].Success
                ? double.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture)
                : 0;
            double remainingSeconds = match.Groups["seconds"].Success
                ? double.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture)
                : 0;

            return TimeSpan.FromSeconds(minutes * 60 + remainingSeconds);
        }
    }
}

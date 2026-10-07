using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AIOrchestrator.Services
{
    public sealed class AdminChatApiClient
    {
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly Uri _baseUri;
        private readonly string _adminApiKey;

        public AdminChatApiClient(string serverUrl, string adminApiKey)
        {
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttps &&
                 !(uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp)))
                throw new ArgumentException("Chat server URL must use HTTPS (HTTP is allowed only for localhost).", nameof(serverUrl));
            if (string.IsNullOrWhiteSpace(adminApiKey))
                throw new ArgumentException("Admin API key is required.", nameof(adminApiKey));

            _baseUri = new Uri(uri.ToString().TrimEnd('/') + "/");
            _adminApiKey = adminApiKey.Trim();
        }

        public Task<IReadOnlyList<AdminChatThread>> ListThreadsAsync(bool unreadOnly, CancellationToken ct) =>
            SendAdminAsync<IReadOnlyList<AdminChatThread>>(
                HttpMethod.Get,
                $"admin/conversations?unreadOnly={unreadOnly.ToString().ToLowerInvariant()}",
                null,
                ct);

        public Task<IReadOnlyList<AdminChatMessage>> GetMessagesAsync(string threadId, CancellationToken ct) =>
            SendAdminAsync<IReadOnlyList<AdminChatMessage>>(
                HttpMethod.Get,
                $"admin/conversations/{Uri.EscapeDataString(threadId)}/messages",
                null,
                ct);

        public Task<AdminChatMessage> SendReplyAsync(
            string threadId, string senderName, string content, CancellationToken ct) =>
            SendAdminAsync<AdminChatMessage>(
                HttpMethod.Post,
                $"admin/conversations/{Uri.EscapeDataString(threadId)}/messages",
                new { senderName, content },
                ct);

        public Task<AdminAccessCode> CreateAccessCodeAsync(string displayName, CancellationToken ct) =>
            SendAdminAsync<AdminAccessCode>(
                HttpMethod.Post,
                "admin/access-codes",
                new { displayName },
                ct);

        private async Task<T> SendAdminAsync<T>(
            HttpMethod method, string path, object? body, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
            request.Headers.Add("X-VinhAI-Admin-Key", _adminApiKey);
            if (body != null)
                request.Content = JsonContent.Create(body, options: JsonOptions);

            using HttpResponseMessage response = await HttpClient.SendAsync(request, ct);
            return await ReadResponseAsync<T>(response, ct);
        }

        internal static async Task<T> SendUserAsync<T>(
            string serverUrl, string bearerToken, HttpMethod method, string path, object? body, CancellationToken ct)
        {
            Uri baseUri = ValidateServerUrl(serverUrl);
            using var request = new HttpRequestMessage(method, new Uri(baseUri, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            if (body != null)
                request.Content = JsonContent.Create(body, options: JsonOptions);

            using HttpResponseMessage response = await HttpClient.SendAsync(request, ct);
            return await ReadResponseAsync<T>(response, ct);
        }

        internal static async Task<T> SendAnonymousAsync<T>(
            string serverUrl, HttpMethod method, string path, object? body, CancellationToken ct)
        {
            Uri baseUri = ValidateServerUrl(serverUrl);
            using var request = new HttpRequestMessage(method, new Uri(baseUri, path));
            if (body != null)
                request.Content = JsonContent.Create(body, options: JsonOptions);

            using HttpResponseMessage response = await HttpClient.SendAsync(request, ct);
            return await ReadResponseAsync<T>(response, ct);
        }

        internal static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken ct)
        {
            if (!response.IsSuccessStatusCode)
            {
                string content = await response.Content.ReadAsStringAsync(ct);
                string detail = content;
                try
                {
                    using JsonDocument document = JsonDocument.Parse(content);
                    if (document.RootElement.TryGetProperty("detail", out JsonElement problemDetail))
                        detail = problemDetail.GetString() ?? content;
                    else if (document.RootElement.TryGetProperty("title", out JsonElement title))
                        detail = title.GetString() ?? content;
                }
                catch (JsonException)
                {
                }

                throw new HttpRequestException(
                    $"Chat server returned {(int)response.StatusCode} ({response.StatusCode}): {detail}",
                    null,
                    response.StatusCode);
            }

            T? result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
            return result ?? throw new InvalidOperationException("Chat server returned an empty response.");
        }

        internal static Uri ValidateServerUrl(string serverUrl)
        {
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttps &&
                 !(uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp)))
                throw new ArgumentException("Chat server URL must use HTTPS (HTTP is allowed only for localhost).", nameof(serverUrl));
            return new Uri(uri.ToString().TrimEnd('/') + "/");
        }
    }

    public sealed record AdminChatThread(
        string Id, string DisplayName, DateTimeOffset UpdatedAt, int UnreadCount, string Preview);

    public sealed record AdminChatMessage(
        string Id, string SenderName, string SenderRole, string Content, DateTimeOffset CreatedAt, bool IsRead);

    public sealed record AdminAccessCode(string AccessCode, string ThreadId, string DisplayName);
}

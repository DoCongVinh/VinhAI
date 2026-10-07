using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AIOrchestrator.Services
{
    public sealed record GeneratedImage(byte[] Bytes, string FileExtension);

    public sealed class PollinationsImageGenerationService
    {
        private const long MaxImageBytes = 30L * 1024 * 1024;
        private static readonly HttpClient SharedHttpClient = new()
        {
            Timeout = TimeSpan.FromMinutes(3)
        };

        private readonly HttpClient _httpClient;

        public PollinationsImageGenerationService() : this(SharedHttpClient)
        {
        }

        public PollinationsImageGenerationService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<GeneratedImage> GenerateAsync(
            string prompt,
            int width,
            int height,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                throw new ArgumentException("Mô tả ảnh không được để trống.", nameof(prompt));
            if (width is < 256 or > 2048)
                throw new ArgumentOutOfRangeException(nameof(width), "Chiều rộng ảnh phải từ 256 đến 2048 pixel.");
            if (height is < 256 or > 2048)
                throw new ArgumentOutOfRangeException(nameof(height), "Chiều cao ảnh phải từ 256 đến 2048 pixel.");

            var builder = new UriBuilder(
                $"https://image.pollinations.ai/prompt/{Uri.EscapeDataString(prompt)}")
            {
                Query = $"model=flux&width={width}&height={height}&private=true"
            };

            using HttpResponseMessage response = await _httpClient.GetAsync(
                builder.Uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType == null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Pollinations không trả về dữ liệu hình ảnh (Content-Type: {mediaType ?? "không xác định"}).");

            if (response.Content.Headers.ContentLength is long length && length > MaxImageBytes)
                throw new InvalidDataException("Ảnh Pollinations trả về vượt quá giới hạn 30 MB.");

            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxImageBytes)
                    throw new InvalidDataException("Ảnh Pollinations trả về vượt quá giới hạn 30 MB.");
                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }

            string extension = mediaType.ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/jpeg" or "image/jpg" => ".jpg",
                "image/webp" => ".webp",
                _ => throw new InvalidDataException($"Pollinations trả về định dạng ảnh không được hỗ trợ: {mediaType}.")
            };
            return new GeneratedImage(buffer.ToArray(), extension);
        }
    }
}

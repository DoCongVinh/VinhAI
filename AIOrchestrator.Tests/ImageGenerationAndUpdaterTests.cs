using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIOrchestrator.Services;
using Xunit;

namespace AIOrchestrator.Tests;

public class PollinationsImageGenerationServiceTests
{
    [Fact]
    public async Task GenerateAsync_RequestsPrivateFluxImageAndReturnsImageBytes()
    {
        HttpRequestMessage? capturedRequest = null;
        byte[] imageBytes = { 0xFF, 0xD8, 0xFF, 0xD9 };
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(imageBytes)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(response);
        }));
        var service = new PollinationsImageGenerationService(client);

        GeneratedImage result = await service.GenerateAsync(
            "chart of cats & dogs", 1280, 768, CancellationToken.None);

        Assert.Equal(imageBytes, result.Bytes);
        Assert.Equal(".jpg", result.FileExtension);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Get, capturedRequest.Method);
        Assert.Equal("image.pollinations.ai", capturedRequest.RequestUri!.Host);
        Assert.Contains("chart%20of%20cats%20%26%20dogs", capturedRequest.RequestUri.AbsoluteUri);
        Assert.Contains("model=flux", capturedRequest.RequestUri.Query);
        Assert.Contains("width=1280", capturedRequest.RequestUri.Query);
        Assert.Contains("height=768", capturedRequest.RequestUri.Query);
        Assert.Contains("private=true", capturedRequest.RequestUri.Query);
    }

    [Fact]
    public async Task GenerateAsync_RejectsNonImageResponse()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not an image")
            })));
        var service = new PollinationsImageGenerationService(client);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.GenerateAsync("chart", 1024, 768, CancellationToken.None));
    }

    [Theory]
    [InlineData(255, 1024)]
    [InlineData(1024, 2049)]
    public async Task GenerateAsync_RejectsUnsupportedImageDimensions(int width, int height)
    {
        var service = new PollinationsImageGenerationService(new HttpClient(new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Request should not be sent for invalid dimensions."))));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.GenerateAsync("chart", width, height, CancellationToken.None));
    }
}

public class VinhAIUpdateServiceTests
{
    [Fact]
    public async Task DownloadAndVerifyAsync_RejectsHashMismatchAndRemovesPartialFile()
    {
        byte[] fileData = Encoding.UTF8.GetBytes("not-the-expected-hash");
        var manifest = new VinhAIUpdateManifest(
            new Version(99, 0, 0),
            new Uri("https://example.com/VinhAI.exe"),
            new string('0', 64),
            "");
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fileData)
            })));
        var service = new VinhAIUpdateService(client);
        string targetDirectory = Path.Combine(Path.GetTempPath(), $"VinhAI-update-test-{Guid.NewGuid():N}");

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.DownloadAndVerifyAsync(manifest, targetDirectory));
            Assert.Empty(Directory.GetFiles(targetDirectory));
        }
        finally
        {
            if (Directory.Exists(targetDirectory))
                Directory.Delete(targetDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_StoresFileOnlyAfterHashMatches()
    {
        byte[] fileData = Encoding.UTF8.GetBytes("verified VinhAI update");
        var manifest = new VinhAIUpdateManifest(
            new Version(99, 0, 0),
            new Uri("https://example.com/VinhAI.exe"),
            Convert.ToHexString(SHA256.HashData(fileData)),
            "");
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fileData)
            })));
        var service = new VinhAIUpdateService(client);
        string targetDirectory = Path.Combine(Path.GetTempPath(), $"VinhAI-update-test-{Guid.NewGuid():N}");

        try
        {
            string stagedPath = await service.DownloadAndVerifyAsync(manifest, targetDirectory);
            Assert.Equal(fileData, await File.ReadAllBytesAsync(stagedPath));
        }
        finally
        {
            if (Directory.Exists(targetDirectory))
                Directory.Delete(targetDirectory, recursive: true);
        }
    }

    [Fact]
    public void ParseManifest_ValidatesHttpsAndSha256()
    {
        byte[] validManifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = "v1.2.3",
            downloadUrl = "https://github.com/example/VinhAI.exe",
            sha256 = new string('a', 64),
            releaseNotes = "Fixes"
        });

        VinhAIUpdateManifest manifest = VinhAIUpdateService.ParseManifest(validManifest);

        Assert.Equal(new Version(1, 2, 3), manifest.Version);
        Assert.Equal("A" + new string('A', 63), manifest.Sha256);
        Assert.Equal("Fixes", manifest.ReleaseNotes);

        byte[] insecureManifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = "1.2.3",
            downloadUrl = "http://example.com/app.exe",
            sha256 = new string('a', 64)
        });
        Assert.Throws<InvalidDataException>(() => VinhAIUpdateService.ParseManifest(insecureManifest));
    }
}

internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) => sendAsync(request);
}

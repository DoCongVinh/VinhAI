using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AIOrchestrator.Services
{
    public sealed record VinhAIUpdateManifest(
        Version Version,
        Uri DownloadUrl,
        string Sha256,
        string ReleaseNotes);

    public sealed class VinhAIUpdateService
    {
        private const int MaxManifestBytes = 64 * 1024;
        private const long MaxInstallerBytes = 500L * 1024 * 1024;
        private static readonly HttpClient SharedHttpClient = new()
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        private readonly HttpClient _httpClient;

        public VinhAIUpdateService() : this(SharedHttpClient)
        {
        }

        public VinhAIUpdateService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public static Version CurrentVersion =>
            Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);

        public static bool IsSupportedExecutablePath(string path)
        {
            string fileName = Path.GetFileName(path);
            return fileName.Equals("VinhAI.exe", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals("VinhAI-Pollinations.exe", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<VinhAIUpdateManifest?> CheckForUpdateAsync(
            string manifestUrl, CancellationToken cancellationToken = default)
        {
            Uri url = ValidateHttpsUri(manifestUrl, "Đường dẫn manifest cập nhật");
            using HttpResponseMessage response = await _httpClient.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is long length && length > MaxManifestBytes)
                throw new InvalidDataException("Manifest cập nhật vượt quá kích thước cho phép.");

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxManifestBytes)
                    throw new InvalidDataException("Manifest cập nhật vượt quá kích thước cho phép.");
                buffer.Write(chunk, 0, read);
            }

            VinhAIUpdateManifest manifest = ParseManifest(buffer.ToArray());
            return manifest.Version > CurrentVersion ? manifest : null;
        }

        public async Task<string> DownloadAndVerifyAsync(
            VinhAIUpdateManifest manifest, string targetDirectory, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            ValidateHttpsUri(manifest.DownloadUrl.AbsoluteUri, "Đường dẫn tải bản cập nhật");

            using HttpResponseMessage response = await _httpClient.GetAsync(
                manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is long length && length > MaxInstallerBytes)
                throw new InvalidDataException("Tệp cập nhật vượt quá kích thước tối đa 500 MB.");

            Directory.CreateDirectory(targetDirectory);
            string stagedPath = Path.Combine(targetDirectory, $"VinhAI-update-{Guid.NewGuid():N}.exe");
            bool keepStagedFile = false;
            try
            {
                await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using (var output = new FileStream(
                    stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                using (var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var chunk = new byte[81920];
                    long totalBytes = 0;
                    int read;
                    while ((read = await input.ReadAsync(chunk, cancellationToken)) > 0)
                    {
                        totalBytes += read;
                        if (totalBytes > MaxInstallerBytes)
                            throw new InvalidDataException("Tệp cập nhật vượt quá kích thước tối đa 500 MB.");
                        sha256.AppendData(chunk, 0, read);
                        await output.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
                    }

                    string actualHash = Convert.ToHexString(sha256.GetHashAndReset());
                    if (!actualHash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("SHA-256 của tệp tải về không khớp manifest. Bản cập nhật đã bị từ chối.");
                }

                keepStagedFile = true;
                return stagedPath;
            }
            finally
            {
                if (!keepStagedFile && File.Exists(stagedPath))
                    File.Delete(stagedPath);
            }
        }

        public static VinhAIUpdateManifest ParseManifest(byte[] json)
        {
            ArgumentNullException.ThrowIfNull(json);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string versionText = GetRequiredString(root, "version").Trim().TrimStart('v', 'V');
            if (!Version.TryParse(versionText, out Version? version))
                throw new InvalidDataException("Phiên bản trong manifest không hợp lệ (dùng dạng 1.2.3).");

            Uri downloadUrl = ValidateHttpsUri(GetRequiredString(root, "downloadUrl"), "Đường dẫn tải bản cập nhật");
            string sha256 = GetRequiredString(root, "sha256").Trim();
            if (sha256.Length != 64 || !IsHex(sha256))
                throw new InvalidDataException("Manifest phải chứa SHA-256 gồm đúng 64 ký tự hex.");

            string notes = root.TryGetProperty("releaseNotes", out JsonElement notesElement) &&
                           notesElement.ValueKind == JsonValueKind.String
                ? notesElement.GetString() ?? ""
                : "";
            return new VinhAIUpdateManifest(version, downloadUrl, sha256.ToUpperInvariant(), notes);
        }

        public static void StartVerifiedUpdate(string stagedPath, string targetExecutablePath, int processId)
        {
            if (!File.Exists(stagedPath))
                throw new FileNotFoundException("Không tìm thấy tệp cập nhật đã tải.", stagedPath);
            if (!IsSupportedExecutablePath(targetExecutablePath))
                throw new InvalidOperationException(
                    "Chỉ có thể tự cập nhật khi ứng dụng đang chạy từ VinhAI.exe hoặc VinhAI-Pollinations.exe.");

            string target = Path.GetFullPath(targetExecutablePath);
            string staged = Path.GetFullPath(stagedPath);
            string encodedTarget = Convert.ToBase64String(Encoding.UTF8.GetBytes(target));
            string encodedStaged = Convert.ToBase64String(Encoding.UTF8.GetBytes(staged));
            string script =
                "$target=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encodedTarget + "')); " +
                "$staged=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encodedStaged + "')); " +
                "while (Get-Process -Id " + processId + " -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 }; " +
                "Move-Item -LiteralPath $staged -Destination $target -Force; " +
                "Start-Process -FilePath $target";

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " +
                            Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (Process.Start(startInfo) is null)
                throw new InvalidOperationException("Không thể khởi động trình cài bản cập nhật.");
        }

        private static Uri ValidateHttpsUri(string value, string fieldName)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidDataException($"{fieldName} phải là URL HTTPS hợp lệ.");
            return uri;
        }

        private static string GetRequiredString(JsonElement root, string propertyName)
        {
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty(propertyName, out JsonElement element) ||
                element.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(element.GetString()))
                throw new InvalidDataException($"Manifest thiếu trường '{propertyName}'.");
            return element.GetString()!;
        }

        private static bool IsHex(string value)
        {
            foreach (char character in value)
            {
                if (!Uri.IsHexDigit(character))
                    return false;
            }
            return true;
        }
    }
}

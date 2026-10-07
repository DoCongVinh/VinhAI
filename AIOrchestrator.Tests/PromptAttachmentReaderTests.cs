using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using AIOrchestrator.Models;
using AIOrchestrator.Services;
using Xunit;

namespace AIOrchestrator.Tests
{
    public sealed class PromptAttachmentReaderTests : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(), "AIOrchestratorAttachments_" + Guid.NewGuid().ToString("N"));

        public PromptAttachmentReaderTests() => Directory.CreateDirectory(_directory);

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        [Fact]
        public async Task BuildPromptAsync_IncludesTextAttachmentWithoutCallingGemini()
        {
            string path = Path.Combine(_directory, "notes.txt");
            await File.WriteAllTextAsync(path, "Nội dung cần AI phân tích.");

            string prompt = await PromptAttachmentReader.BuildPromptAsync(
                "Tóm tắt tài liệu", new[] { new PromptAttachment(path) }, "", CancellationToken.None);

            Assert.Contains("Tóm tắt tài liệu", prompt);
            Assert.Contains("notes.txt", prompt);
            Assert.Contains("Nội dung cần AI phân tích.", prompt);
        }

        [Fact]
        public async Task BuildPromptAsync_ExtractsWordDocumentText()
        {
            string path = Path.Combine(_directory, "brief.docx");
            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                ZipArchiveEntry document = archive.CreateEntry("word/document.xml");
                await using Stream stream = document.Open();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(
                    "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                    "<w:body><w:p><w:r><w:t>Nội dung trong Word</w:t></w:r></w:p></w:body></w:document>");
            }

            string prompt = await PromptAttachmentReader.BuildPromptAsync(
                "Đọc tệp", new[] { new PromptAttachment(path) }, "", CancellationToken.None);

            Assert.Contains("brief.docx", prompt);
            Assert.Contains("Nội dung trong Word", prompt);
        }

        [Theory]
        [InlineData("chart.png")]
        [InlineData("report.pdf")]
        [InlineData("notes.txt")]
        [InlineData("document.docx")]
        [InlineData("sheet.xlsx")]
        [InlineData("slides.pptx")]
        public void IsSupported_AcceptsCommonDocumentAndImageFormats(string fileName)
        {
            Assert.True(PromptAttachmentReader.IsSupported(fileName));
        }

        [Fact]
        public void IsSupported_RejectsExecutableFiles()
        {
            Assert.False(PromptAttachmentReader.IsSupported("program.exe"));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AIOrchestrator.Models;
using AIOrchestrator.Services;
using Xunit;

namespace AIOrchestrator.Tests
{
    /// <summary>
    /// Persistence behaviour of <see cref="StorageService"/>.
    ///
    /// <para>
    /// <see cref="StorageService"/> hard-codes its path to
    /// <c>%LocalAppData%\AIOrchestrator\config.json</c> and offers no injectable
    /// path, so these tests operate on that real location. To keep them isolated
    /// and repeatable each test snapshots the pre-existing config file on
    /// construction and restores it on <see cref="Dispose"/>.
    /// </para>
    /// </summary>
    [Collection("StorageService")]
    public class StorageServiceTests : IDisposable
    {
        // Resolved exactly the way the production type resolves it, so the test
        // and the code under test can never disagree about the target file.
        private static readonly string ConfigDirectory =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIOrchestrator");

        private static readonly string ConfigFile = Path.Combine(ConfigDirectory, "config.json");

        private readonly StorageService _sut;
        private readonly bool _hadExistingFile;
        private readonly string? _backup;

        public StorageServiceTests()
        {
            // Snapshot whatever the user already had, so we never destroy it.
            _hadExistingFile = File.Exists(ConfigFile);
            _backup = _hadExistingFile ? File.ReadAllText(ConfigFile) : null;

            _sut = new StorageService();
        }

        public void Dispose()
        {
            try
            {
                if (_hadExistingFile)
                {
                    Directory.CreateDirectory(ConfigDirectory);
                    File.WriteAllText(ConfigFile, _backup!);
                }
                else if (File.Exists(ConfigFile))
                {
                    File.Delete(ConfigFile);
                }
            }
            catch
            {
                // Best-effort restore; a cleanup failure must not fail a test.
            }
        }

        private static AppSettings MakeSettings(
            string geminiKey = "g-key",
            string groqKey = "q-key",
            string openAiKey = "o-key",
            ExecutionMode mode = ExecutionMode.CustomPipeline,
            bool interactive = false,
            int agentCount = 1)
        {
            var settings = new AppSettings
            {
                GeminiApiKey = geminiKey,
                GroqApiKey = groqKey,
                OpenAiApiKey = openAiKey,
                OpenAiBaseUrl = "https://example.test/v1",
                DefaultMode = mode,
                InteractiveMode = interactive,
                SelectedDirectAgentId = "agent-1",
                Agents = new List<AiAgent>()
            };

            for (int i = 0; i < agentCount; i++)
            {
                settings.Agents.Add(new AiAgent
                {
                    Id = $"agent-{i + 1}",
                    Name = $"Agent {i + 1}",
                    Role = "role",
                    Icon = "🤖",
                    Provider = AgentProvider.OpenAICompatible,
                    ModelName = "model-x",
                    Temperature = 0.42,
                    IsEnabled = i % 2 == 0,
                    SystemPrompt = "prompt"
                });
            }

            return settings;
        }

        private static void WriteRawConfig(string json)
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(ConfigFile, json);
        }

        // ---------------------------------------------------------------------
        // Constructor (positive + edge)
        // ---------------------------------------------------------------------

        [Fact]
        public void Constructor_CreatesConfigDirectoryWhenMissing()
        {
            // The directory must exist after construction, whatever its prior state.
            _ = new StorageService();

            Assert.True(Directory.Exists(ConfigDirectory));
        }

        // ---------------------------------------------------------------------
        // SaveSettings + LoadSettings round-trip (positive)
        // ---------------------------------------------------------------------

        [Fact]
        public void SaveThenLoad_RoundTripsAllScalarFields()
        {
            var original = MakeSettings(
                geminiKey: "gemini-abc",
                groqKey: "groq-xyz",
                openAiKey: "openai-123",
                mode: ExecutionMode.DirectAgent,
                interactive: false);

            _sut.SaveSettings(original);
            var loaded = _sut.LoadSettings();

            Assert.Equal("gemini-abc", loaded.GeminiApiKey);
            Assert.Equal("groq-xyz", loaded.GroqApiKey);
            Assert.Equal("openai-123", loaded.OpenAiApiKey);
            Assert.Equal("https://example.test/v1", loaded.OpenAiBaseUrl);
            Assert.Equal(ExecutionMode.DirectAgent, loaded.DefaultMode);
            Assert.False(loaded.InteractiveMode);
            Assert.Equal("agent-1", loaded.SelectedDirectAgentId);
        }

        [Fact]
        public void SaveThenLoad_RoundTripsAgentsAndTheirProperties()
        {
            var original = MakeSettings(agentCount: 3);

            _sut.SaveSettings(original);
            var loaded = _sut.LoadSettings();

            Assert.Equal(3, loaded.Agents.Count);
            Assert.Equal("agent-1", loaded.Agents[0].Id);
            Assert.Equal("Agent 2", loaded.Agents[1].Name);
            Assert.Equal(AgentProvider.OpenAICompatible, loaded.Agents[0].Provider);
            Assert.Equal("model-x", loaded.Agents[0].ModelName);
            Assert.Equal(0.42, loaded.Agents[0].Temperature, precision: 5);
            Assert.True(loaded.Agents[0].IsEnabled);
            Assert.False(loaded.Agents[1].IsEnabled); // odd index -> disabled
        }

        [Fact]
        public void SaveSettings_WritesFileAtExpectedLocation()
        {
            _sut.SaveSettings(MakeSettings());

            Assert.True(File.Exists(ConfigFile));
            Assert.Equal(ConfigFile, Path.Combine(ConfigDirectory, "config.json"));
        }

        [Fact]
        public void SaveSettings_WritesIndentedJson()
        {
            _sut.SaveSettings(MakeSettings());

            string json = File.ReadAllText(ConfigFile);

            // WriteIndented produces newlines + indentation, not a single-line blob.
            Assert.Contains(Environment.NewLine, json);
            Assert.Contains("\n  ", json.Replace("\r\n", "\n"));
        }

        [Fact]
        public void SaveSettings_OverwritesPreviousContent()
        {
            _sut.SaveSettings(MakeSettings(geminiKey: "first"));
            _sut.SaveSettings(MakeSettings(geminiKey: "second"));

            var loaded = _sut.LoadSettings();

            Assert.Equal("second", loaded.GeminiApiKey);
        }

        [Fact]
        public void SaveSettings_PersistsNonAsciiTextLosslessly()
        {
            var settings = MakeSettings(agentCount: 1);
            settings.Agents[0].Name = "AI Điều phối viên 🎯";
            settings.Agents[0].SystemPrompt = "Phân tích yêu cầu — trả về JSON hợp lệ.";

            _sut.SaveSettings(settings);
            var loaded = _sut.LoadSettings();

            Assert.Equal("AI Điều phối viên 🎯", loaded.Agents[0].Name);
            Assert.Equal("Phân tích yêu cầu — trả về JSON hợp lệ.", loaded.Agents[0].SystemPrompt);
        }

        // ---------------------------------------------------------------------
        // LoadSettings when no file exists (positive fallback)
        // ---------------------------------------------------------------------

        [Fact]
        public void LoadSettings_NoFile_ReturnsDefaults()
        {
            if (File.Exists(ConfigFile)) File.Delete(ConfigFile);

            var loaded = _sut.LoadSettings();

            Assert.NotNull(loaded);
            Assert.NotEmpty(loaded.Agents);
            Assert.True(loaded.InteractiveMode);
        }

        [Fact]
        public void LoadSettings_NoFile_WritesDefaultConfigToDisk()
        {
            if (File.Exists(ConfigFile)) File.Delete(ConfigFile);

            _sut.LoadSettings();

            Assert.True(File.Exists(ConfigFile));
        }

        [Fact]
        public void LoadSettings_NoFile_ReturnedDefaultsMatchWhatWasPersisted()
        {
            if (File.Exists(ConfigFile)) File.Delete(ConfigFile);

            var returned = _sut.LoadSettings();
            var reloaded = _sut.LoadSettings();

            Assert.Equal(returned.Agents.Count, reloaded.Agents.Count);
            Assert.Equal(returned.GeminiApiKey, reloaded.GeminiApiKey);
        }

        // ---------------------------------------------------------------------
        // LoadSettings: fallback / negative / edge cases
        // ---------------------------------------------------------------------

        [Fact]
        public void LoadSettings_CorruptJson_FallsBackToDefaults()
        {
            WriteRawConfig("{ đây không phải là json hợp lệ ");

            var loaded = _sut.LoadSettings();

            Assert.NotNull(loaded);
            Assert.NotEmpty(loaded.Agents);
        }

        [Fact]
        public void LoadSettings_CorruptJson_RepairsFileOnDisk()
        {
            WriteRawConfig("<<<not json>>>");

            _sut.LoadSettings();

            // Fallback path calls SaveSettings, so the file should be valid JSON again.
            string repaired = File.ReadAllText(ConfigFile);
            var reparsed = JsonSerializer.Deserialize<AppSettings>(repaired);
            Assert.NotNull(reparsed);
        }

        [Fact]
        public void LoadSettings_JsonLiteralNull_FallsBackToDefaults()
        {
            WriteRawConfig("null");

            var loaded = _sut.LoadSettings();

            Assert.NotNull(loaded);
            Assert.NotEmpty(loaded.Agents);
        }

        [Fact]
        public void LoadSettings_EmptyObject_FallsBackToDefaults()
        {
            // Deserialises fine, but Agents is empty -> treated as "no usable config".
            WriteRawConfig("{}");

            var loaded = _sut.LoadSettings();

            Assert.NotEmpty(loaded.Agents);
        }

        [Fact]
        public void LoadSettings_EmptyAgentsArray_FallsBackToDefaults()
        {
            WriteRawConfig("""{ "Agents": [] }""");

            var loaded = _sut.LoadSettings();

            Assert.NotEmpty(loaded.Agents);
        }

        [Fact]
        public void LoadSettings_EmptyFile_FallsBackToDefaults()
        {
            WriteRawConfig(string.Empty);

            var loaded = _sut.LoadSettings();

            Assert.NotNull(loaded);
            Assert.NotEmpty(loaded.Agents);
        }

        [Fact]
        public void LoadSettings_UnknownJsonProperties_AreIgnoredAndStillLoad()
        {
            // A config written by a newer app version must not break loading.
            WriteRawConfig("""
                {
                  "GeminiApiKey": "kept",
                  "Agents": [ { "Id": "a1", "Name": "A1" } ],
                  "FutureFeatureFlag": true,
                  "NestedUnknown": { "a": 1 }
                }
                """);

            var loaded = _sut.LoadSettings();

            Assert.Equal("kept", loaded.GeminiApiKey);
            Assert.Single(loaded.Agents);
            Assert.Equal("a1", loaded.Agents[0].Id);
        }

        [Fact]
        public void LoadSettings_PartialJson_UsesPropertyDefaultsForMissingFields()
        {
            // Only Agents supplied; every other field should keep its type default.
            WriteRawConfig("""{ "Agents": [ { "Id": "only", "Name": "Only" } ] }""");

            var loaded = _sut.LoadSettings();

            Assert.Single(loaded.Agents);
            Assert.Equal(ExecutionMode.SmartRouter, loaded.DefaultMode);
            Assert.True(loaded.InteractiveMode);
        }

        [Fact]
        public void LoadSettings_ValidConfig_DoesNotOverwriteIt()
        {
            var saved = MakeSettings(geminiKey: "keep-me", agentCount: 2);
            _sut.SaveSettings(saved);
            string before = File.ReadAllText(ConfigFile);

            var loaded = _sut.LoadSettings();

            Assert.Equal("keep-me", loaded.GeminiApiKey);
            Assert.Equal(before, File.ReadAllText(ConfigFile));
        }

        // ---------------------------------------------------------------------
        // Null handling (negative)
        // ---------------------------------------------------------------------

        [Fact]
        public void SaveSettings_Null_DoesNotThrow()
        {
            // The implementation swallows serialization failures; assert it is safe.
            var ex = Record.Exception(() => _sut.SaveSettings(null!));

            Assert.Null(ex);
        }

        [Fact]
        public void LoadSettings_NullApiKeys_ArePreservedAsEmpty()
        {
            WriteRawConfig("""{ "GeminiApiKey": null, "GroqApiKey": null, "Agents": [ { "Id": "a" } ] }""");

            var loaded = _sut.LoadSettings();

            Assert.Null(loaded.GeminiApiKey);
            Assert.Null(loaded.GroqApiKey);
            Assert.Single(loaded.Agents);
        }

        // ---------------------------------------------------------------------
        // Robustness: repeated calls (edge)
        // ---------------------------------------------------------------------

        [Fact]
        public void LoadSettings_CalledTwice_IsStable()
        {
            _sut.SaveSettings(MakeSettings(geminiKey: "stable", agentCount: 2));

            var first = _sut.LoadSettings();
            var second = _sut.LoadSettings();

            Assert.Equal(first.GeminiApiKey, second.GeminiApiKey);
            Assert.Equal(first.Agents.Count, second.Agents.Count);
        }

        [Fact]
        public void SaveLoadSave_ProducesEquivalentFile()
        {
            _sut.SaveSettings(MakeSettings(agentCount: 2));
            var first = _sut.LoadSettings();

            _sut.SaveSettings(first);
            var second = _sut.LoadSettings();

            Assert.Equal(first.GeminiApiKey, second.GeminiApiKey);
            Assert.Equal(first.DefaultMode, second.DefaultMode);
            Assert.Equal(first.Agents.Count, second.Agents.Count);
            Assert.Equal(first.Agents[1].Id, second.Agents[1].Id);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIOrchestrator.Models;

namespace AIOrchestrator.Services
{
    /// <summary>
    /// File-backed conversation store. Each session is written as one JSON file
    /// under a "conversations" folder next to the app's config.
    ///
    /// The directory is injectable so tests can point at a temp folder instead
    /// of the real user profile.
    /// </summary>
    public class JsonConversationStore : IConversationStore
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
        private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly string _directory;

        public JsonConversationStore() : this(DefaultDirectory()) { }

        public JsonConversationStore(string directory)
        {
            _directory = string.IsNullOrWhiteSpace(directory)
                ? throw new ArgumentException("Directory must not be empty.", nameof(directory))
                : directory;
            Directory.CreateDirectory(_directory);
        }

        public static string DefaultDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "AIOrchestrator", "conversations");
        }

        public static JsonConversationStore ForUser(string username)
        {
            return ForUser(username, DefaultDirectory());
        }

        public static JsonConversationStore ForUser(string username, string conversationsRoot)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Tên tài khoản không được để trống.", nameof(username));
            if (string.IsNullOrWhiteSpace(conversationsRoot))
                throw new ArgumentException("Thư mục lưu lịch sử không được để trống.", nameof(conversationsRoot));

            string accountKey = username.Trim().ToUpperInvariant();
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(accountKey));
            string accountDirectory = Convert.ToHexString(hash);
            return new JsonConversationStore(Path.Combine(conversationsRoot, "accounts", accountDirectory));
        }

        public ConversationSession Save(ConversationSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            if (string.IsNullOrWhiteSpace(session.Id))
            {
                session.Id = Guid.NewGuid().ToString("N");
            }

            session.UpdatedAt = DateTime.Now;

            // Assign an id/timestamp to any message that somehow lacks one.
            foreach (var message in session.Messages)
            {
                if (message.Timestamp == default)
                {
                    message.Timestamp = DateTime.Now;
                }
            }

            string path = PathFor(session.Id);
            string json = JsonSerializer.Serialize(session, WriteOptions);

            // Write to a temp file then move, so a crash mid-write cannot corrupt
            // an existing session.
            string temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);

            return session;
        }

        public ConversationSession? Load(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            string path = PathFor(id);
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<ConversationSession>(json, ReadOptions);
            }
            catch (Exception)
            {
                // A corrupt file should not take the app down; treat it as missing.
                return null;
            }
        }

        public IReadOnlyList<ConversationSummary> ListAll() => Search(null);

        public IReadOnlyList<ConversationSummary> Search(string? query)
        {
            if (!Directory.Exists(_directory)) return Array.Empty<ConversationSummary>();

            var results = new List<ConversationSummary>();

            foreach (string file in Directory.EnumerateFiles(_directory, "*.json"))
            {
                ConversationSession? session = TryRead(file);
                if (session == null) continue;
                if (!session.Matches(query)) continue;

                results.Add(ConversationSummary.FromSession(session));
            }

            return results
                .OrderByDescending(s => s.UpdatedAt)
                .ToList();
        }

        public bool Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            string path = PathFor(id);
            if (!File.Exists(path)) return false;

            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public int DeleteAll()
        {
            if (!Directory.Exists(_directory)) return 0;

            int removed = 0;
            foreach (string file in Directory.EnumerateFiles(_directory, "*.json").ToList())
            {
                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (Exception)
                {
                    // Skip files we cannot remove (locked, etc.).
                }
            }

            return removed;
        }

        private ConversationSession? TryRead(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                var session = JsonSerializer.Deserialize<ConversationSession>(json, ReadOptions);

                // Fall back to the filename as the id if the payload is missing one.
                if (session != null && string.IsNullOrWhiteSpace(session.Id))
                {
                    session.Id = Path.GetFileNameWithoutExtension(path);
                }

                return session;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private string PathFor(string id) => Path.Combine(_directory, SanitizeFileName(id) + ".json");

        private static string SanitizeFileName(string id)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = id.Where(c => !invalid.Contains(c)).ToArray();
            string safe = new string(chars);
            return string.IsNullOrWhiteSpace(safe) ? Guid.NewGuid().ToString("N") : safe;
        }
    }
}

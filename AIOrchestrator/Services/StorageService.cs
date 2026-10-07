using System;
using System.IO;
using System.Text.Json;
using AIOrchestrator.Models;

namespace AIOrchestrator.Services
{
    public class StorageService
    {
        private readonly string _filePath;

        public StorageService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(appData, "AIOrchestrator");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "config.json");
        }

        public AppSettings LoadSettings()
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    string json = File.ReadAllText(_filePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null && settings.Agents.Count > 0)
                    {
                        return settings;
                    }
                }
                catch (Exception)
                {
                    // Fall back to default
                }
            }

            var defaultSettings = AppSettings.CreateDefault();
            SaveSettings(defaultSettings);
            return defaultSettings;
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception)
            {
                // Silently ignore or log
            }
        }
    }
}

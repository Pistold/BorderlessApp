using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BorderlessApp
{
    public static class ConfigManager
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BorderlessGameApp");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

        public static List<GameProfile> Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return new List<GameProfile>();

                string json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<List<GameProfile>>(json) ?? new List<GameProfile>();
            }
            catch
            {
                // Corrupt or unreadable config shouldn't crash the app on startup.
                return new List<GameProfile>();
            }
        }

        public static void Save(List<GameProfile> profiles)
        {
            Directory.CreateDirectory(ConfigDir);
            string json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }

        // Wipes the whole %AppData%\BorderlessGameApp folder - used by the
        // in-app Uninstall button.
        public static void DeleteAll()
        {
            try
            {
                if (Directory.Exists(ConfigDir))
                    Directory.Delete(ConfigDir, recursive: true);
            }
            catch
            {
                // Best effort - not worth blocking the uninstall flow over.
            }
        }
    }
}

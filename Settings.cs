using System;
using System.IO;

namespace DiscordQuestCompleter
{
    // Minimal local settings store. The Discord token is the user's own account token,
    // used only to call the quests endpoint on their own behalf (same self-automation model
    // as the rest of the app). Stored as plain text next to the exe; this is a personal-use
    // desktop tool, not a multi-user or networked service, so this matches the app's existing
    // threat model rather than introducing a new one.
    public static class Settings
    {
        private static readonly string SettingsPath = Path.Combine(Environment.CurrentDirectory, "dqc_settings.json");

        public static string LoadToken()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return "";
                var json = File.ReadAllText(SettingsPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("discordToken", out var tokenEl))
                    return tokenEl.GetString() ?? "";
            }
            catch { /* corrupt or missing settings file: treat as no token saved */ }
            return "";
        }

        public static void SaveToken(string token)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new { discordToken = token ?? "" });
                File.WriteAllText(SettingsPath, json);
            }
            catch { /* non-fatal: token just won't persist across restarts */ }
        }
    }
}

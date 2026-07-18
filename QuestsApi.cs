using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace DiscordQuestCompleter
{
    public class DetectedQuest
    {
        public string QuestId { get; set; } = "";
        public string ApplicationId { get; set; } = "";
        public string QuestName { get; set; } = "";
        public double TargetSeconds { get; set; }
        public double ProgressSeconds { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    // Talks to Discord's undocumented quests endpoint using a token the user supplies for
    // their own account (same self-automation model as the rest of this app: it only ever
    // reads the operator's own quest progress, never touches another account).
    //
    // NOTE: Discord's quest response shape isn't publicly documented and has shifted before
    // (task_config vs task_config_v2, etc). This parses defensively: any quest that doesn't
    // match a known desktop-playtime shape is skipped rather than throwing, but the exact
    // field names below should be re-verified against a live account before relying on this,
    // since there is no way to test against Discord's authenticated API from a build sandbox.
    public static class QuestsApi
    {
        public static async Task<(List<DetectedQuest> Quests, string Error)> FetchActiveQuestsAsync(string token)
        {
            var result = new List<DetectedQuest>();
            if (string.IsNullOrWhiteSpace(token)) return (result, "No Discord token configured.");

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                client.DefaultRequestHeaders.Add("Authorization", token);

                string json = await client.GetStringAsync("https://discord.com/api/v9/quests/@me");
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("quests", out var questsEl))
                    return (result, "Unexpected response shape from Discord (no 'quests' field).");

                foreach (var q in questsEl.EnumerateArray())
                {
                    try
                    {
                        var dq = ParseQuest(q);
                        if (dq != null) result.Add(dq);
                    }
                    catch
                    {
                        // Skip any single quest that doesn't match the expected shape
                        // rather than failing the whole fetch.
                    }
                }

                return (result, "");
            }
            catch (HttpRequestException ex)
            {
                return (result, "Failed to reach Discord: " + ex.Message);
            }
            catch (Exception ex)
            {
                return (result, "Failed to fetch quests: " + ex.Message);
            }
        }

        private static DetectedQuest? ParseQuest(JsonElement q)
        {
            if (!q.TryGetProperty("id", out var idEl)) return null;
            if (!q.TryGetProperty("config", out var config)) return null;
            if (!config.TryGetProperty("application_id", out var appIdEl)) return null;

            string questId = idEl.GetString() ?? "";
            string applicationId = appIdEl.GetString() ?? "";
            if (string.IsNullOrEmpty(questId) || string.IsNullOrEmpty(applicationId)) return null;

            string questName = "";
            if (config.TryGetProperty("messages", out var messages) && messages.TryGetProperty("quest_name", out var qn))
                questName = qn.GetString() ?? "";

            // The "play the game on desktop" task has appeared under both task_config and
            // task_config_v2 keys across Discord API revisions; try both.
            if (!TryGetDesktopTaskTarget(config, "task_config_v2", out double target) &&
                !TryGetDesktopTaskTarget(config, "task_config", out target))
            {
                return null; // not a desktop-playtime quest; nothing this app can help with
            }

            DateTime? expiresAt = null;
            if (config.TryGetProperty("expires_at", out var expEl) &&
                expEl.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(expEl.GetString(), out var exp))
            {
                expiresAt = exp.ToUniversalTime();
            }
            if (expiresAt.HasValue && expiresAt.Value < DateTime.UtcNow) return null; // expired

            double progress = 0;
            bool completed = false;
            if (q.TryGetProperty("user_status", out var status))
            {
                if (status.TryGetProperty("completed_at", out var compEl) && compEl.ValueKind != JsonValueKind.Null)
                    completed = true;

                if (status.TryGetProperty("progress", out var progressEl) &&
                    progressEl.TryGetProperty("PLAY_ON_DESKTOP", out var playProgress) &&
                    playProgress.TryGetProperty("value", out var valueEl) &&
                    valueEl.ValueKind == JsonValueKind.Number)
                {
                    progress = valueEl.GetDouble();
                }
            }

            return new DetectedQuest
            {
                QuestId = questId,
                ApplicationId = applicationId,
                QuestName = questName,
                TargetSeconds = target,
                ProgressSeconds = progress,
                IsCompleted = completed,
                ExpiresAt = expiresAt
            };
        }

        private static bool TryGetDesktopTaskTarget(JsonElement config, string key, out double target)
        {
            target = 0;
            if (config.TryGetProperty(key, out var taskConfig) &&
                taskConfig.TryGetProperty("tasks", out var tasks) &&
                tasks.TryGetProperty("PLAY_ON_DESKTOP", out var play) &&
                play.TryGetProperty("target", out var targetEl) &&
                targetEl.ValueKind == JsonValueKind.Number)
            {
                target = targetEl.GetDouble();
                return true;
            }
            return false;
        }
    }
}

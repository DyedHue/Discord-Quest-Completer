using System;
using System.Text.Json.Serialization;

namespace DiscordQuestCompleter
{
    public class DiscordGame
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("aliases")]
        public string[] Aliases { get; set; } = Array.Empty<string>();

        [JsonPropertyName("executables")]
        public GameExecutable[] Executables { get; set; } = Array.Empty<GameExecutable>();
    }

    public class GameExecutable
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("os")]
        public string Os { get; set; } = "";
    }
}

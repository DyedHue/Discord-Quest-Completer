using System;

namespace DiscordQuestCompleter
{
    public class DiscordGame
    {
        public string name { get; set; } = "";
        public string[] aliases { get; set; } = new string[0];
        public GameExecutable[] executables { get; set; } = new GameExecutable[0];
    }

    public class GameExecutable
    {
        public string name { get; set; } = "";
        public string os { get; set; } = "";
    }
}

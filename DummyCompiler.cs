using System;
using System.IO;
using System.Reflection;

namespace DiscordQuestCompleter
{
    public static class DummyCompiler
    {
        // The stub is a generic, prebuilt exe (see Resources/dummy_stub.exe) embedded as a
        // resource at build time. It reads its own exe path + a sibling ".txt" file at
        // runtime for the window title, so "creating a game" is just extracting this
        // resource once and copying it, never compiling anything on the user's machine.
        // This avoids a hard dependency on legacy .NET Framework's csc.exe being present,
        // and avoids repeatedly writing a brand-new, never-before-seen executable, a
        // pattern antivirus/SmartScreen heuristics commonly flag as dropper-like behavior.
        private const string StubResourceName = "DiscordQuestCompleter.dummy_stub.exe";

        public static bool CompileDummyExe(string exePath, string gameName, string targetRelPath, out string error)
        {
            error = "";
            try
            {
                string targetDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;
                Directory.CreateDirectory(targetDir);

                if (!ExtractStub(exePath, out error)) return false;

                if (!string.IsNullOrEmpty(gameName))
                {
                    string txtPath = Path.Combine(targetDir, Path.GetFileNameWithoutExtension(exePath) + ".txt");
                    File.WriteAllText(txtPath, gameName);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool ExtractStub(string destExePath, out string error)
        {
            error = "";
            var assembly = Assembly.GetExecutingAssembly();
            using var resourceStream = assembly.GetManifestResourceStream(StubResourceName);
            if (resourceStream == null)
            {
                error = $"Embedded stub resource '{StubResourceName}' not found. The build may be corrupt.";
                return false;
            }

            using var fileStream = new FileStream(destExePath, FileMode.Create, FileAccess.Write);
            resourceStream.CopyTo(fileStream);
            return true;
        }
    }
}

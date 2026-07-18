using System;
using System.Diagnostics;
using System.IO;

namespace DiscordQuestCompleter
{
    public static class DummyCompiler
    {
        /// <summary>
        /// Ensures the generic default_game.exe exists in the application root.
        /// If it does not exist, it is compiled once using csc.exe.
        /// </summary>
        public static bool EnsureDefaultExe(string defaultExePath, out string error)
        {
            error = "";
            if (File.Exists(defaultExePath))
                return true;

            return CompileGenericExe(defaultExePath, out error);
        }

        /// <summary>
        /// Compiles a single generic exe that reads its configuration from a
        /// sibling .txt file at runtime. The .txt file is expected to have:
        ///   Line 1: game name (display title)
        ///   Line 2: target relative path (shown in the window body)
        /// </summary>
        private static bool CompileGenericExe(string exePath, out string error)
        {
            error = "";
            string windir = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
            string compilerPath = Path.Combine(windir, @"Microsoft.NET\Framework64\v4.0.30319\csc.exe");

            if (!File.Exists(compilerPath))
            {
                compilerPath = Path.Combine(windir, @"Microsoft.NET\Framework\v4.0.30319\csc.exe");
                if (!File.Exists(compilerPath))
                {
                    error = "Windows C# compiler (csc.exe) not found. Please ensure .NET Framework is installed.";
                    return false;
                }
            }

            try
            {
                string targetDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;
                Directory.CreateDirectory(targetDir);

                string exeName = Path.GetFileNameWithoutExtension(exePath);
                string csPath = Path.Combine(targetDir, exeName + ".cs");

                // Generic C# code: reads name and path from the sibling .txt file at runtime.
                string csCode = @"
using System;
using System.IO;
using System.Windows.Forms;

class DummyGame : Form
{
    public DummyGame()
    {
        string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        string txtPath = Path.ChangeExtension(exePath, "".txt"");

        string gameName = """";
        string targetRelPath = """";

        if (File.Exists(txtPath))
        {
            string[] lines = File.ReadAllLines(txtPath);
            if (lines.Length > 0) gameName = lines[0].Trim();
            if (lines.Length > 1) targetRelPath = lines[1].Trim();
        }

        this.Text = string.IsNullOrEmpty(gameName) ? targetRelPath : gameName;
        this.Width = 600;
        this.Height = 200;
        this.StartPosition = FormStartPosition.CenterScreen;

        Label lbl = new Label();
        lbl.Text = ""Target Path: "" + targetRelPath + ""\n\nDummy game process is running.\nKeep this window open to progress the quest."";
        lbl.Top = 20;
        lbl.Left = 20;
        lbl.Width = 550;
        lbl.Height = 120;

        this.Controls.Add(lbl);
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.Run(new DummyGame());
    }
}";
                File.WriteAllText(csPath, csCode);

                var startInfo = new ProcessStartInfo
                {
                    FileName = compilerPath,
                    Arguments = $"/nologo /target:winexe /r:System.Windows.Forms.dll /out:\"{exePath}\" \"{csPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    error = "Failed to start compiler process.";
                    return false;
                }

                process.WaitForExit();
                string output = process.StandardOutput.ReadToEnd();
                string errOut = process.StandardError.ReadToEnd();

                if (File.Exists(csPath)) File.Delete(csPath);

                if (process.ExitCode == 0)
                {
                    return true;
                }
                else
                {
                    error = $"Compilation failed:\n{errOut}\n{output}";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Creates a game exe by copying the generic default_game.exe to the target path,
        /// then writes a .txt metadata file with the game name on line 1 and the relative
        /// path on line 2. Compiles default_game.exe first if it doesn't exist yet.
        /// </summary>
        public static bool CreateGameExe(string defaultExePath, string exePath, string gameName, string targetRelPath, out string error)
        {
            error = "";

            // Ensure the generic default exe exists (compiles once if missing)
            if (!EnsureDefaultExe(defaultExePath, out error))
                return false;

            try
            {
                string targetDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;
                Directory.CreateDirectory(targetDir);

                // Copy the generic exe to the desired location
                File.Copy(defaultExePath, exePath, overwrite: true);

                // Write the metadata txt file: line 1 = name, line 2 = relative path
                string txtPath = Path.ChangeExtension(exePath, ".txt");
                File.WriteAllLines(txtPath, new[] { gameName ?? "", targetRelPath ?? "" });

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}

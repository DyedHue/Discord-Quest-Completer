using System;
using System.Diagnostics;
using System.IO;

namespace DiscordQuestCompleter
{
    public static class DummyCompiler
    {
        // The stub is fully generic (it reads its own exe path + a sibling ".txt" file at
        // runtime for the window title), so it only ever needs to be compiled once and then
        // reused for every game by copying the file. This avoids spawning the legacy csc.exe
        // compiler on every single "Create Game" / "Edit Game" click, which was slow, depended
        // on legacy .NET Framework tooling being present, and repeatedly wrote a brand-new,
        // never-before-seen executable to disk, a pattern antivirus/SmartScreen heuristics
        // commonly flag as dropper-like behavior.
        private static readonly string TemplateExePath = Path.Combine(Path.GetTempPath(), "DiscordQuestCompleter", "dummy_template.exe");

        public static bool CompileDummyExe(string exePath, string gameName, string targetRelPath, out string error)
        {
            error = "";
            try
            {
                if (!EnsureTemplateCompiled(out error)) return false;

                string targetDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;
                Directory.CreateDirectory(targetDir);

                File.Copy(TemplateExePath, exePath, overwrite: true);

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

        private static bool EnsureTemplateCompiled(out string error)
        {
            error = "";
            if (File.Exists(TemplateExePath)) return true;

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

            string templateDir = Path.GetDirectoryName(TemplateExePath) ?? Path.GetTempPath();
            Directory.CreateDirectory(templateDir);
            string csPath = Path.Combine(templateDir, "dummy_template.cs");

            const string csCode = @"
using System;
using System.IO;
using System.Windows.Forms;

class DummyGame : Form {
    public DummyGame() {
        string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
        string txtPath = Path.ChangeExtension(exePath, "".txt"");
        string title = File.Exists(txtPath) ? File.ReadAllText(txtPath).Trim() : Path.GetFileName(exePath);
        if (string.IsNullOrEmpty(title)) title = Path.GetFileName(exePath);

        this.Text = title;
        this.Width = 600;
        this.Height = 200;
        this.StartPosition = FormStartPosition.CenterScreen;

        Label lbl = new Label();
        lbl.Text = ""Path: "" + exePath + ""\n\nDummy game process is running.\nKeep this window open to progress the quest."";
        lbl.Top = 20;
        lbl.Left = 20;
        lbl.Width = 550;
        lbl.Height = 120;

        this.Controls.Add(lbl);
    }

    [STAThread]
    static void Main() {
        Application.EnableVisualStyles();
        Application.Run(new DummyGame());
    }
}";
            try
            {
                File.WriteAllText(csPath, csCode);

                var startInfo = new ProcessStartInfo
                {
                    FileName = compilerPath,
                    Arguments = $"/nologo /target:winexe /r:System.Windows.Forms.dll /out:\"{TemplateExePath}\" \"{csPath}\"",
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

                if (process.ExitCode == 0 && File.Exists(TemplateExePath))
                {
                    return true;
                }

                error = $"Compilation failed:\n{errOut}\n{output}";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}

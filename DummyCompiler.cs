using System;
using System.Diagnostics;
using System.IO;

namespace DiscordQuestCompleter
{
    public static class DummyCompiler
    {
        public static bool CompileDummyExe(string exePath, string gameName, string targetRelPath, out string error)
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
                string txtPath = Path.Combine(targetDir, exeName + ".txt");

                string windowTitle = string.IsNullOrEmpty(gameName) ? targetRelPath : gameName;

                string csCode = $@"
using System;
using System.Windows.Forms;

class DummyGame : Form {{
    public DummyGame() {{
        this.Text = @""{windowTitle.Replace("\"", "\"\"")}"";
        this.Width = 600;
        this.Height = 200;
        this.StartPosition = FormStartPosition.CenterScreen;

        Label lbl = new Label();
        lbl.Text = ""Target Path: "" + @""{targetRelPath.Replace("\"", "\"\"")}"" + ""\n\nDummy game process is running.\nKeep this window open to progress the quest."";
        lbl.Top = 20;
        lbl.Left = 20;
        lbl.Width = 550;
        lbl.Height = 120;

        this.Controls.Add(lbl);
    }}

    [STAThread]
    static void Main() {{
        Application.EnableVisualStyles();
        Application.Run(new DummyGame());
    }}
}}";
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
                    if (!string.IsNullOrEmpty(gameName))
                    {
                        File.WriteAllText(txtPath, gameName);
                    }
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
    }
}

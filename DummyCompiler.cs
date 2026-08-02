using System;
using System.Diagnostics;
using System.IO;

namespace DiscordQuestCompleter
{
	public static class DummyCompiler
	{
		/// <summary>
		/// Ensures the generic game_template.exe exists in the application root.
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
using System.Text.RegularExpressions;

class DummyGame : Form
{
    private Timer timer;
    private double minutesElapsed = 0;
    private double timerMinutes = 15;
    private bool closeGameTimer = false;
    private bool sendNotification = false;

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

        string settingsPath = """";
        if (!string.IsNullOrEmpty(targetRelPath) && !Path.IsPathRooted(targetRelPath))
        {
            string exeDir = Path.GetDirectoryName(exePath);
            string targetDir = Path.GetDirectoryName(targetRelPath);
            DirectoryInfo currentDir = new DirectoryInfo(exeDir);
            
            if (!string.IsNullOrEmpty(targetDir))
            {
                string[] segments = targetDir.Split(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < segments.Length; i++)
                {
                    if (currentDir.Parent != null)
                        currentDir = currentDir.Parent;
                }
            }
            
            if (currentDir.Parent != null)
            {
                currentDir = currentDir.Parent;
                settingsPath = Path.Combine(currentDir.FullName, ""settings.json"");
            }
        }

        this.Text = string.IsNullOrEmpty(gameName) ? targetRelPath : gameName;
        this.Width = 600;
        this.Height = 200;
        this.StartPosition = FormStartPosition.CenterScreen;

        bool startMinimized = false;

        if (File.Exists(settingsPath))
        {
            try
            {
                string json = File.ReadAllText(settingsPath);
                Match mMin = Regex.Match(json, ""\""StartMinimized\""\\s*:\\s*(true|false)"", RegexOptions.IgnoreCase);
                if (mMin.Success && mMin.Groups[1].Value.ToLower() == ""true"") startMinimized = true;

                Match mTime = Regex.Match(json, ""\""TimerMinutes\""\\s*:\\s*([\\d\\.]+)"");
                if (mTime.Success) double.TryParse(mTime.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out timerMinutes);

                Match mClose = Regex.Match(json, ""\""CloseGameTimer\""\\s*:\\s*(true|false)"", RegexOptions.IgnoreCase);
                if (mClose.Success && mClose.Groups[1].Value.ToLower() == ""true"") closeGameTimer = true;

                Match mNotify = Regex.Match(json, ""\""SendNotification\""\\s*:\\s*(true|false)"", RegexOptions.IgnoreCase);
                if (mNotify.Success && mNotify.Groups[1].Value.ToLower() == ""true"") sendNotification = true;
            }
            catch { }
        }

        if (startMinimized)
        {
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = true;
        }

        RichTextBox lbl = new RichTextBox();
        lbl.Top = 20;
        lbl.Left = 20;
        lbl.Width = 550;
        lbl.Height = 120;
        lbl.ReadOnly = true;
        lbl.BorderStyle = BorderStyle.None;
        lbl.BackColor = this.BackColor;

        string labelText = ""Target Path: "" + targetRelPath + ""\n\nDummy game process is running.\n"";
        labelText += ""Keep this window open to progress the quest.\n"";

        DateTime startTime = DateTime.Now;
        DateTime endTime = startTime;
        
        if (closeGameTimer || sendNotification)
        {
            endTime = startTime.AddMinutes(timerMinutes);
            labelText += ""\nTimer set to "" + timerMinutes + "" minutes. Started at "" + startTime.ToString(""h:mm:ss tt"") + "", will end at "" + endTime.ToString(""h:mm:ss tt"") + "".\n\nAfter the timer, "";
            
            if (closeGameTimer)
            {
                labelText += ""this window will automatically close"";
                if (sendNotification)
                    labelText += "" and you will receive a notification"";
            }
            else
                labelText += ""you will receive a notification.\nClose this window manually"";
            labelText += ""."";
        }
        else
        {
            labelText += ""Close this window once you are done."";
        }

        lbl.Text = labelText;
        
        if (closeGameTimer || sendNotification)
        {
            string tMinStr = timerMinutes.ToString();
            string sTimeStr = startTime.ToString(""h:mm:ss tt"");
            string eTimeStr = endTime.ToString(""h:mm:ss tt"");
            
            int idx = lbl.Text.IndexOf(tMinStr);
            if (idx >= 0) { lbl.Select(idx, tMinStr.Length); lbl.SelectionColor = System.Drawing.Color.RoyalBlue; lbl.SelectionFont = new System.Drawing.Font(lbl.Font, System.Drawing.FontStyle.Bold); }
            
            idx = lbl.Text.IndexOf(sTimeStr);
            if (idx >= 0) { lbl.Select(idx, sTimeStr.Length); lbl.SelectionColor = System.Drawing.Color.RoyalBlue; lbl.SelectionFont = new System.Drawing.Font(lbl.Font, System.Drawing.FontStyle.Bold); }
            
            idx = lbl.Text.IndexOf(eTimeStr, lbl.Text.IndexOf(sTimeStr) + sTimeStr.Length);
            if (idx >= 0) { lbl.Select(idx, eTimeStr.Length); lbl.SelectionColor = System.Drawing.Color.RoyalBlue; lbl.SelectionFont = new System.Drawing.Font(lbl.Font, System.Drawing.FontStyle.Bold); }
            lbl.Select(0, 0);
        }

        this.Controls.Add(lbl);

        if (closeGameTimer || sendNotification)
        {
            timer = new Timer();
            int intervalMs = (int)(timerMinutes * 60000);
            if (intervalMs < 100) intervalMs = 100; // Minimum 100ms
            timer.Interval = intervalMs;
            timer.Tick += (s, e) => {
                timer.Stop();
                if (sendNotification)
                {
                    var notifyIcon = new NotifyIcon();
                    notifyIcon.Icon = System.Drawing.SystemIcons.Information;
                    notifyIcon.Visible = true;
                    notifyIcon.BalloonTipTitle = ""Discord Quest Completer"";
                    notifyIcon.BalloonTipText = ""Finished quest for "" + this.Text;
                    notifyIcon.ShowBalloonTip(3000);
                    var cleanupTimer = new Timer();
                    cleanupTimer.Interval = 3500;
                    cleanupTimer.Tick += (cs, ce) => { notifyIcon.Visible = false; notifyIcon.Dispose(); cleanupTimer.Stop(); };
                    cleanupTimer.Start();
                }
                if (closeGameTimer)
                {
                    var closeTimer = new Timer();
                    closeTimer.Interval = 1000;
                    closeTimer.Tick += (cs, ce) => { this.Close(); };
                    closeTimer.Start();
                }
            };
            timer.Start();
        }
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
					Arguments = $"/nologo /target:winexe /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:\"{exePath}\" \"{csPath}\"",
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true
				};

				using (var process = Process.Start(startInfo))
				{
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
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}
		}

		/// <summary>
		/// Creates a game exe by copying the generic game_template.exe to the target path,
		/// then writes a .txt metadata file with the game name on line 1 and the relative
		/// path on line 2. Compiles game_template.exe first if it doesn't exist yet.
		/// </summary>
		public static bool CreateGameExe(string defaultExePath, string exePath, string gameName, string targetRelPath, string id, string icon, out string error)
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

				// Write the metadata txt file: line 1 = name, line 2 = relative path, line 3 = id, line 4 = icon
				string txtPath = Path.ChangeExtension(exePath, ".txt");
				File.WriteAllLines(txtPath, new[] { gameName ?? "", targetRelPath ?? "", id ?? "", icon ?? "" });

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

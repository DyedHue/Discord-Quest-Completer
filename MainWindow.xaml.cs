using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DiscordQuestCompleter
{
	public class GeneratedGame : INotifyPropertyChanged
	{
		public string FullPath { get; set; } = "";
		public string RelativePath { get; set; } = "";
		public string DisplayName { get; set; } = "";

		private bool _isRunning;
		public bool IsRunning
		{
			get => _isRunning;
			set
			{
				if (_isRunning != value)
				{
					_isRunning = value;
					OnPropertyChanged();
					OnPropertyChanged(nameof(StatusText));
					OnPropertyChanged(nameof(StatusColor));
				}
			}
		}

		public string StatusText => IsRunning ? "Running" : "";
		public string StatusColor => IsRunning ? "#28a745" : "Transparent";

		public event PropertyChangedEventHandler PropertyChanged;
		protected void OnPropertyChanged([CallerMemberName] string name = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		}
	}

	public partial class MainWindow : Window
	{
		private List<DiscordGame> _discordCache = new();
		private readonly string _baseDir;
		private readonly string _defaultExePath;
		private readonly string _localDbPath;
		private bool _isDatabaseLoaded = false;
		private bool _isSearchPlaceholder = true;
		private DispatcherTimer _processTimer;

		public MainWindow()
		{
			InitializeComponent();
			_baseDir = Path.Combine(Environment.CurrentDirectory, "DQC Game Folders");
			_defaultExePath = Path.Combine(Environment.CurrentDirectory, "default_game.exe");
			_localDbPath = Path.Combine(Environment.CurrentDirectory, "discord_database.json");
			Directory.CreateDirectory(_baseDir);
			LoadGames();
			// Try to load local database if present. Do not auto-fetch from network to save users' data.
			LoadLocalDatabase();

			_processTimer = new DispatcherTimer();
			_processTimer.Interval = TimeSpan.FromSeconds(1);
			_processTimer.Tick += ProcessTimer_Tick;
			_processTimer.Start();
		}

		private void ProcessTimer_Tick(object sender, EventArgs e)
		{
			UpdateRunningStatus();
		}

		private void UpdateStatus(string text, StatusLevel level = StatusLevel.Neutral)
		{
			Dispatcher.Invoke(() =>
			{
				StatusText.Text = text;
				switch (level)
				{
					case StatusLevel.Success:
						StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#28a745"));
						break;
					case StatusLevel.Error:
						StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ed4245"));
						break;
					case StatusLevel.Neutral:
					default:
						StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#949ba4"));
						break;
				}
			});
		}

		private enum StatusLevel { Neutral, Success, Error }

		private void UpdateRunningStatus()
		{
			bool anyChanged = false;
			var runningPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			try
			{
				var processes = Process.GetProcesses();
				foreach (var p in processes)
				{
					try
					{
						var path = p.MainModule?.FileName;
						if (path != null && path.StartsWith(_baseDir, StringComparison.OrdinalIgnoreCase))
						{
							runningPaths.Add(path);
						}
					}
					catch { } // Ignore access denied exceptions
				}
			}
			catch { }

			foreach (GeneratedGame game in GeneratedGamesList.Items)
			{
				bool isRunning = runningPaths.Contains(game.FullPath);
				if (game.IsRunning != isRunning)
				{
					game.IsRunning = isRunning;
					anyChanged = true;
				}
			}

			if (anyChanged)
			{
				UpdateActionButtonsState();
			}
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			// Only focus the search box if the local search database was successfully loaded.
			if (_isDatabaseLoaded)
			{
				SearchBox.Focus();
			}
		}

		private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			// Disable global keyboard shortcuts when Manual Creation tab is selected
			if (Tabs.SelectedIndex == 1)
			{
				return;
			}
			if (e.Key == Key.Enter)
			{
				if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
				{
					if (Tabs.SelectedIndex == 0) DoCreateGame();
				}
				else
				{
					if (Tabs.SelectedIndex == 0) CreateAndRun_Click(null, null);
					else if (Tabs.SelectedIndex == 1) CreateAndRunManual();
				}
				e.Handled = true;
			}
			else if (e.Key == Key.Up || e.Key == Key.Down)
			{
				if (Tabs.SelectedIndex == 0)
				{
					bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
					if (isShift)
					{
						if (PathsList.Items.Count > 0)
						{
							int newIdx = PathsList.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
							if (newIdx >= 0 && newIdx < PathsList.Items.Count)
							{
								PathsList.SelectedIndex = newIdx;
								PathsList.ScrollIntoView(PathsList.SelectedItem);
							}
							e.Handled = true;
						}
					}
					else
					{
						if (GamesList.Items.Count > 0)
						{
							int newIdx = GamesList.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
							if (newIdx >= 0 && newIdx < GamesList.Items.Count)
							{
								GamesList.SelectedIndex = newIdx;
								GamesList.ScrollIntoView(GamesList.SelectedItem);
							}
							e.Handled = true;
						}
					}
				}
			}
		}

		private (bool IsValid, string Message) IsValidPath(string pathStr)
		{
			pathStr = pathStr.Trim();
			if (string.IsNullOrEmpty(pathStr)) return (false, "Path cannot be empty.");
			if (pathStr.Contains("://") || pathStr.Contains("?") || pathStr.Contains("=")) return (false, "URIs or web links are not valid paths.");
			if (Regex.IsMatch(pathStr, @"[<>:""|?*]")) return (false, "Path contains invalid Windows characters (< > : \" | ? *).");
			if (!pathStr.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return (false, "Path must end with '.exe'.");

			try
			{
				string baseFull = Path.GetFullPath(_baseDir);
				if (!baseFull.EndsWith(Path.DirectorySeparatorChar.ToString()))
				{
					baseFull += Path.DirectorySeparatorChar;
				}
				string resolvedPath = Path.GetFullPath(Path.Combine(baseFull, pathStr));
				if (!resolvedPath.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
				{
					return (false, "Path must reside inside the game folders directory.");
				}
			}
			catch (Exception ex)
			{
				return (false, "Invalid path resolution: " + ex.Message);
			}

			return (true, "Valid");
		}

		private bool IsPathInBaseDir(string path)
		{
			if (string.IsNullOrEmpty(path)) return false;
			try
			{
				string baseFull = Path.GetFullPath(_baseDir);
				if (!baseFull.EndsWith(Path.DirectorySeparatorChar.ToString()))
				{
					baseFull += Path.DirectorySeparatorChar;
				}
				string pathFull = Path.GetFullPath(path);
				return pathFull.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase);
			}
			catch
			{
				return false;
			}
		}

		private int CalculateMatchScore(string query, string target)
		{
			if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(target)) return 0;

			// Allow spaces and alphanumeric characters
			string qNorm = Regex.Replace(query.ToLower(), @"[^a-z0-9\s]", " ").Trim();
			string tNorm = Regex.Replace(target.ToLower(), @"[^a-z0-9\s]", " ").Trim();

			if (string.IsNullOrWhiteSpace(qNorm) || string.IsNullOrWhiteSpace(tNorm)) return 0;
			if (tNorm == qNorm) return 1000;

			int score = 0;
			if (tNorm.StartsWith(qNorm)) score += 500;
			else if (tNorm.Contains(qNorm)) score += 200;

			var qTokens = qNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			int tokensMatched = 0;
			foreach (var token in qTokens)
			{
				if (tNorm.Contains(token))
				{
					score += token.Length * 10;
					tokensMatched++;
				}
			}
			if (tokensMatched > 0 && tokensMatched == qTokens.Length) score += 100;

			int maxLcs = 0;
			string qNormNoSpace = qNorm.Replace(" ", "");
			string tNormNoSpace = tNorm.Replace(" ", "");

			for (int i = 0; i < qNormNoSpace.Length; i++)
			{
				for (int len = qNormNoSpace.Length - i; len > maxLcs; len--)
				{
					if (tNormNoSpace.Contains(qNormNoSpace.Substring(i, len)))
					{
						maxLcs = len;
						break;
					}
				}
			}

			score += maxLcs * 5;

			// Requires at least 3 character match sequence, OR a full token of length 3+ matched
			if (maxLcs >= 3 || (tokensMatched > 0 && qTokens.Any(t => t.Length >= 3 && tNorm.Contains(t))))
			{
				return score;
			}
			return 0;
		}

		private string NormalizeString(string text)
		{
			if (string.IsNullOrEmpty(text)) return "";
			return Regex.Replace(text.ToLower(), @"[^a-z0-9]", "");
		}

		private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
		{
			if (_isSearchPlaceholder)
			{
				SearchBox.Text = "";
				SearchBox.Foreground = new SolidColorBrush(Color.FromRgb(219, 222, 225)); // #dbdee1
				_isSearchPlaceholder = false;

				// If we don't have a loaded DB and local file doesn't exist, prompt user to fetch
				if ((_discordCache == null || _discordCache.Count == 0) && !File.Exists(_localDbPath))
				{
					var result = MessageBox.Show("Search database not found. Do you want to fetch? (~11MB download)", "Search DB Missing", MessageBoxButton.YesNo);
					if (result == MessageBoxResult.Yes)
					{
						_ = FetchDiscordDataAsync_Internal(true);
					}
					else
					{
						// User declined to fetch: restore placeholder state and move focus away from search box
						_isSearchPlaceholder = true;
						SearchBox.Text = "Type at least 3 characters to search...";
						SearchBox.Foreground = Brushes.Gray;
						// Move focus to the tab control to avoid re-triggering the prompt
						try { Tabs.Focus(); } catch { this.Focus(); }
					}
				}
			}
		}

		private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
		{
			if (string.IsNullOrWhiteSpace(SearchBox.Text))
			{
				_isSearchPlaceholder = true;
				SearchBox.Foreground = Brushes.Gray;
				SearchBox.Text = "Type at least 3 characters to search...";
			}
		}

		private async void RefetchButton_Click(object sender, RoutedEventArgs e)
		{
			RefetchButton.IsEnabled = false;
			UpdateStatus("Downloading Discord games database...", StatusLevel.Neutral);
			await FetchDiscordDataAsync_Internal(true);
			RefetchButton.IsEnabled = true;
		}

		private async Task FetchDiscordDataAsync()
		{
			// Default non-UI triggered fetch: call FetchDiscordDataAsync(true) instead when wanting to save locally.
			await FetchDiscordDataAsync_Internal(true);
		}

		private async Task FetchDiscordDataAsync_Internal(bool saveToLocal)
		{
			try
			{
			UpdateStatus("Fetching Discord games database...", StatusLevel.Neutral);
				using var client = new HttpClient();
				client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

				string json = await client.GetStringAsync("https://discord.com/api/applications/detectable");
				_discordCache = JsonSerializer.Deserialize<List<DiscordGame>>(json) ?? new();
				if (saveToLocal)
				{
					try { File.WriteAllText(_localDbPath, json); } catch { }
				}
				UpdateStatus("Ready. Discord database loaded.", StatusLevel.Success);
			}
			catch (Exception ex)
			{
				UpdateStatus("Failed to fetch Discord API. Check your connection.", StatusLevel.Error);
				MessageBox.Show(ex.Message, "API Error");
			}
		}

		private void LoadLocalDatabase()
		{
			try
			{
				if (File.Exists(_localDbPath))
				{
					string json = File.ReadAllText(_localDbPath);
				_discordCache = JsonSerializer.Deserialize<List<DiscordGame>>(json) ?? new();
				_isDatabaseLoaded = true;
				UpdateStatus("Ready. Local Discord database loaded.", StatusLevel.Success);
				}
				else
				{
					// Local DB missing: indicate manual mode and instruct user how to fetch
					_isDatabaseLoaded = false;
					UpdateStatus("Search DB missing. Manual mode available. Fetch search DB from the button next to the search bar.", StatusLevel.Error);
				}
			}
			catch { /* non-fatal */ }
		}

		private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (_isSearchPlaceholder) return;

			string query = SearchBox.Text.Trim();
			if (query.Length < 3)
			{
				GamesList.ItemsSource = null;
				PathsList.ItemsSource = null;
				return;
			}

			var matchesWithScores = new List<(DiscordGame Game, int Score)>();

			foreach (var app in _discordCache)
			{
				int maxScore = CalculateMatchScore(query, app.Name);
				foreach (var alias in app.Aliases)
				{
					int aliasScore = CalculateMatchScore(query, alias);
					if (aliasScore > maxScore) maxScore = aliasScore;
				}

				if (maxScore > 0)
				{
					var cleanPaths = new List<GameExecutable>();
					foreach (var exec in app.Executables)
					{
						if (exec.Os == "win32")
						{
							string p = exec.Name.Replace("\\", "/");
							var valid = IsValidPath(p);
							if (valid.IsValid || (!p.Contains("://") && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
							{
								cleanPaths.Add(exec);
							}
						}
					}
					if (cleanPaths.Count > 0)
					{
						matchesWithScores.Add((new DiscordGame { Name = app.Name, Aliases = app.Aliases, Executables = cleanPaths.ToArray() }, maxScore));
					}
				}
			}

			var matches = matchesWithScores
				.OrderByDescending(x => x.Score)
				.ThenBy(x => x.Game.Name.Length)
				.Select(x => x.Game)
				.ToList();

			GamesList.ItemsSource = matches;
			if (matches.Count > 0)
			{
				GamesList.SelectedIndex = 0; // Auto select first game
			}
		}

		private void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (GamesList.SelectedItem is DiscordGame game)
			{
				var validPaths = new List<string>();
				foreach (var exec in game.Executables)
				{
					if (exec.Os == "win32")
					{
						string p = exec.Name.Replace("\\", "/");
						var valid = IsValidPath(p);
						if (valid.IsValid) validPaths.Add(p);
						else if (!p.Contains("://") && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) validPaths.Add(p);
					}
				}
				PathsList.ItemsSource = validPaths;
				if (validPaths.Count > 0)
				{
					PathsList.SelectedIndex = 0; // Auto select first path
				}
			}
		}

		private void CopyToManual_Click(object sender, RoutedEventArgs e)
		{
			if (GamesList.SelectedItem is DiscordGame game && PathsList.SelectedItem is string path)
			{
				ManualName.Text = game.Name;
				ManualPath.Text = path;
				Tabs.SelectedIndex = 1;
			UpdateStatus("Copied to manual entry.", StatusLevel.Success);
			}
		}

		private void CreateGame_Click(object sender, RoutedEventArgs e)
		{
			string path = DoCreateGame();
			if (path != null)
			{
				SelectGeneratedGame(path);
			}
		}

		private void CreateAndRun_Click(object sender, RoutedEventArgs e)
		{
			string path = DoCreateGame();
			if (path != null)
			{
				SelectGeneratedGame(path);
				RunExe(path);
			}
		}

		private void CreateAndRunManual()
		{
			string path = DoCreateGame();
			if (path != null)
			{
				SelectGeneratedGame(path);
				RunExe(path);
			}
		}

		private void SelectGeneratedGame(string fullPath)
		{
			if (string.IsNullOrEmpty(fullPath)) return;
			string normalizedPath;
			try { normalizedPath = Path.GetFullPath(fullPath); } catch { normalizedPath = fullPath; }
			foreach (GeneratedGame g in GeneratedGamesList.Items)
			{
				string gFull;
				try { gFull = Path.GetFullPath(g.FullPath); } catch { gFull = g.FullPath; }
				if (gFull.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
				{
					GeneratedGamesList.SelectedItem = g;
					GeneratedGamesList.ScrollIntoView(g);
					return;
				}
			}
		}

		private string DoCreateGame()
		{
			string gameName = "";
			string targetPath = "";

			if (Tabs.SelectedIndex == 0) // Auto Search
			{
				if (GamesList.SelectedItem is DiscordGame game && PathsList.SelectedItem is string path)
				{
					gameName = game.Name;
					targetPath = path;
				}
				else
				{
					MessageBox.Show("Please select a game and a path.", "Missing Selection");
					return null;
				}
			}
			else // Manual
			{
				gameName = ManualName.Text.Trim();
				targetPath = ManualPath.Text.Trim();
				if (string.IsNullOrEmpty(targetPath))
				{
					MessageBox.Show("EXE Path is required.", "Missing Input");
					return null;
				}
			}

			return ProcessCreation(gameName, targetPath);
		}

		private string ProcessCreation(string gameName, string targetPath)
		{
			if (string.IsNullOrEmpty(targetPath)) return null;

			if (!targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
				targetPath += ".exe";

			var valid = IsValidPath(targetPath);
			if (!valid.IsValid)
			{
				MessageBox.Show($"The path provided is invalid:\n{valid.Message}", "Invalid Path");
				return null;
			}

			string fullPath = Path.Combine(_baseDir, targetPath);

			bool defaultExistedBefore = File.Exists(_defaultExePath);
			UpdateStatus(defaultExistedBefore ? "Creating game..." : "Compiling base executable (one-time)...", StatusLevel.Neutral);

			if (DummyCompiler.CreateGameExe(_defaultExePath, fullPath, gameName, targetPath, out string error))
			{
				UpdateStatus("Successfully created: " + Path.GetFileName(fullPath), StatusLevel.Success);
				LoadGames(fullPath);
				return fullPath;
			}
			else
			{
				UpdateStatus("Error creating game executable.", StatusLevel.Error);
				MessageBox.Show(error, "Creation Error");
				return null;
			}
		}

		private void LoadGames(string forceSelectPath = null)
		{
			var oldSelectedPath = forceSelectPath ?? (GeneratedGamesList.SelectedItem as GeneratedGame)?.FullPath;
			// Normalize the old/forced path to full path form to avoid mismatches
			string normalizedOldSelectedPath = null;
			if (!string.IsNullOrEmpty(oldSelectedPath))
			{
				try { normalizedOldSelectedPath = Path.GetFullPath(oldSelectedPath); } catch { normalizedOldSelectedPath = oldSelectedPath; }
			}
			GeneratedGamesList.Items.Clear();
			if (!Directory.Exists(_baseDir)) return;

			GeneratedGame toSelect = null;
			var files = Directory.GetFiles(_baseDir, "*.exe", SearchOption.AllDirectories);
			foreach (var file in files)
			{
				string relPath = Path.GetRelativePath(_baseDir, file).Replace("\\", "/");
				string txtPath = Path.ChangeExtension(file, ".txt");
				string gameName = "";
				if (File.Exists(txtPath))
				{
					// Line 1 = game name, Line 2 = relative path (backwards-compat: single-line = name only)
					var lines = File.ReadAllLines(txtPath);
					if (lines.Length > 0) gameName = lines[0].Trim();
				}
				string displayName = string.IsNullOrEmpty(gameName) ? "Unnamed Game" : gameName;

				var game = new GeneratedGame
				{
					FullPath = file,
					RelativePath = relPath,
					DisplayName = displayName
				};
				GeneratedGamesList.Items.Add(game);

				// Compare normalized full paths to avoid separator or relative path issues
				string fileFull = null;
				try { fileFull = Path.GetFullPath(file); } catch { fileFull = file; }
				if (normalizedOldSelectedPath != null && fileFull.Equals(normalizedOldSelectedPath, StringComparison.OrdinalIgnoreCase))
				{
					toSelect = game;
				}
			}

			// If we found the previously selected or forced path, select it.
			if (toSelect != null)
			{
				GeneratedGamesList.SelectedItem = toSelect;
				GeneratedGamesList.ScrollIntoView(toSelect);
			}
			else
			{
				// If nothing was selected, default to the first game when available.
				if (GeneratedGamesList.Items.Count > 0)
				{
					GeneratedGamesList.SelectedIndex = 0;
					GeneratedGamesList.ScrollIntoView(GeneratedGamesList.SelectedItem);
				}
			}
		}

		private void GeneratedGamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			UpdateActionButtonsState();
		}

		private void UpdateActionButtonsState()
		{
			if (GeneratedGamesList.SelectedItem is GeneratedGame game)
			{
				if (game.IsRunning)
				{
					NotRunningButtons.Visibility = Visibility.Collapsed;
					StopButton.Visibility = Visibility.Visible;
				}
				else
				{
					NotRunningButtons.Visibility = Visibility.Visible;
					StopButton.Visibility = Visibility.Collapsed;
				}
			}
			else
			{
				NotRunningButtons.Visibility = Visibility.Visible;
				StopButton.Visibility = Visibility.Collapsed;
			}
		}

		private void RunGame_Click(object sender, RoutedEventArgs e)
		{
			if (GeneratedGamesList.SelectedItem is GeneratedGame game)
			{
				RunExe(game.FullPath);
			}
		}

		private void StopGame_Click(object sender, RoutedEventArgs e)
		{
			if (GeneratedGamesList.SelectedItem is GeneratedGame game)
			{
				try
				{
					var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(game.FullPath));
					foreach (var p in processes)
					{
						try
						{
							if (p.MainModule?.FileName.Equals(game.FullPath, StringComparison.OrdinalIgnoreCase) == true)
							{
								p.Kill();
							}
						}
						catch { }
					}
				}
				catch { }

				Task.Delay(500).ContinueWith(_ => Dispatcher.Invoke(UpdateRunningStatus));
			}
		}

		private void RunExe(string fullPath)
		{
			try
			{
				var processName = Path.GetFileName(fullPath);
				var existing = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
				bool alreadyRunning = false;
				foreach (var p in existing)
				{
					try
					{
						if (p.MainModule?.FileName.Equals(fullPath, StringComparison.OrdinalIgnoreCase) == true)
						{
							alreadyRunning = true;
							break;
						}
					}
					catch { }
				}

				if (alreadyRunning)
				{
			UpdateStatus($"{processName} is already running.", StatusLevel.Error);
					return;
				}

				Process.Start(new ProcessStartInfo
				{
					FileName = fullPath,
					UseShellExecute = true
				});
			UpdateStatus("Started process: " + processName, StatusLevel.Success);
				Task.Delay(500).ContinueWith(_ => Dispatcher.Invoke(UpdateRunningStatus));
			}
			catch (Exception ex)
			{
				MessageBox.Show("Failed to run executable: " + ex.Message, "Launch Error");
			}
		}

		private bool IsGameActuallyRunning(string fullPath)
		{
			try
			{
				var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(fullPath));
				foreach (var p in processes)
				{
					try
					{
						if (p.MainModule?.FileName.Equals(fullPath, StringComparison.OrdinalIgnoreCase) == true)
						{
							return true;
						}
					}
					catch { }
				}
			}
			catch { }
			return false;
		}

		private void EditGame_Click(object sender, RoutedEventArgs e)
		{
			if (GeneratedGamesList.SelectedItem is GeneratedGame game)
			{
				if (game.IsRunning || IsGameActuallyRunning(game.FullPath))
				{
					MessageBox.Show("Cannot edit a running game. Stop it first.", "Game is Running");
					return;
				}

				string currentName = game.DisplayName == "Unnamed Game" ? "" : game.DisplayName;
				var dialog = new EditGameWindow(currentName, game.RelativePath);
				dialog.Owner = this;
				if (dialog.ShowDialog() == true)
				{
					string newName = dialog.ResultName;
					string newPath = dialog.ResultPath;

					if (string.IsNullOrEmpty(newPath)) return;
					if (!newPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) newPath += ".exe";

					var valid = IsValidPath(newPath);
					if (!valid.IsValid)
					{
						MessageBox.Show($"The new path is invalid:\n{valid.Message}", "Invalid Path");
						return;
					}

					string newFullPath = Path.Combine(_baseDir, newPath);
					string newTxtPath = Path.ChangeExtension(newFullPath, ".txt");

					// Normalise old path for comparison
					string oldFullNorm = Path.GetFullPath(game.FullPath);
					string newFullNorm = Path.GetFullPath(newFullPath);
					bool pathChanged = !oldFullNorm.Equals(newFullNorm, StringComparison.OrdinalIgnoreCase);

					try
					{
						UpdateStatus("Applying edits...", StatusLevel.Neutral);

						if (pathChanged)
						{
							// Path changed: copy exe to new location, write new txt, delete old files
							if (DummyCompiler.CreateGameExe(_defaultExePath, newFullPath, newName, newPath, out string createError))
							{
								if (IsPathInBaseDir(game.FullPath))
								{
									File.Delete(game.FullPath);
									string oldTxtPath = Path.ChangeExtension(game.FullPath, ".txt");
									if (File.Exists(oldTxtPath)) File.Delete(oldTxtPath);
								}
								UpdateStatus("Game updated successfully.", StatusLevel.Success);
								LoadGames(newFullPath);
							}
							else
							{
								UpdateStatus("Failed to apply edits.", StatusLevel.Error);
								MessageBox.Show(createError, "Edit Error");
							}
						}
						else
						{
							// Only name changed: just rewrite the .txt file — no recompile needed
							File.WriteAllLines(newTxtPath, new[] { newName ?? "", newPath });
							game.DisplayName = string.IsNullOrEmpty(newName) ? "Unnamed Game" : newName;
							UpdateStatus("Game renamed successfully.", StatusLevel.Success);
							LoadGames(game.FullPath);
						}
					}
					catch (Exception ex)
					{
						UpdateStatus("Failed to apply edits.", StatusLevel.Error);
						MessageBox.Show(ex.Message, "Edit Error");
					}
				}
			}
		}

		private void DeleteGame_Click(object sender, RoutedEventArgs e)
		{
			if (GeneratedGamesList.SelectedItem is GeneratedGame game)
			{
				if (game.IsRunning || IsGameActuallyRunning(game.FullPath))
				{
					MessageBox.Show("Cannot delete a running game. Stop it first.", "Game is Running");
					return;
				}

				var result = MessageBox.Show($"Are you sure you want to delete this game?\n\n{Path.GetFileName(game.FullPath)}", "Confirm Delete", MessageBoxButton.YesNo);
				if (result != MessageBoxResult.Yes) return;

				try
				{
					if (!IsPathInBaseDir(game.FullPath))
					{
						MessageBox.Show("Security violation: Cannot delete files outside the game folders directory.", "Delete Blocked");
						return;
					}

					// Delete the exe and its associated .txt metadata file
					File.Delete(game.FullPath);
					string txtPath = Path.ChangeExtension(game.FullPath, ".txt");
					if (File.Exists(txtPath)) File.Delete(txtPath);

					// Clean up empty directories up to (but not including) the base directory
					try
					{
						string dir = Path.GetDirectoryName(game.FullPath);
						if (!string.IsNullOrEmpty(dir))
						{
							string baseFull = Path.GetFullPath(_baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
							while (!string.IsNullOrEmpty(dir))
							{
								string dirFull = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
								if (string.Equals(dirFull, baseFull, StringComparison.OrdinalIgnoreCase)) break;
								if (!IsPathInBaseDir(dirFull)) break; // Stop cleanup if it escapes baseDir

								// If directory no longer exists, move up
								if (!Directory.Exists(dirFull))
								{
									dir = Path.GetDirectoryName(dirFull);
									continue;
								}

								// If directory is empty (no files and no subdirectories), delete it
								var entries = Directory.EnumerateFileSystemEntries(dirFull);
								if (!entries.Any())
								{
									Directory.Delete(dirFull);
									dir = Path.GetDirectoryName(dirFull);
								}
								else
								{
									break; // not empty, stop cleaning up
								}
							}
						}
					}
					catch { /* Non-fatal cleanup errors ignored */ }

					LoadGames();
			UpdateStatus("Deleted " + Path.GetFileName(game.FullPath), StatusLevel.Success);
				}
				catch (Exception ex)
				{
					MessageBox.Show("Failed to delete file. It might be running.\n" + ex.Message, "Delete Error");
				}
			}
		}
	}
}

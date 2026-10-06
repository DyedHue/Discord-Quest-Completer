using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace DiscordQuestCompleter
{
	public partial class SettingsWindow : Window
	{
		public event Action<string> SteamDirectoryChanged;

		public SettingsWindow(string steamDirectory)
		{
			InitializeComponent();
			SteamDirectoryTextBox.Text = NormalizeSteamDirectory(steamDirectory);
		}

		public static string NormalizeSteamDirectory(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				return "";
			}
			string normalized = path.Trim().Replace("/", "\\").TrimEnd(Path.DirectorySeparatorChar);
			string commonSuffix = "\\steamapps\\common";
			string steamAppsSuffix = "\\steamapps";
			if (normalized.EndsWith(commonSuffix, StringComparison.OrdinalIgnoreCase))
			{
				normalized = normalized.Substring(0, normalized.Length - commonSuffix.Length);
			}
			else if (normalized.EndsWith(steamAppsSuffix, StringComparison.OrdinalIgnoreCase))
			{
				normalized = normalized.Substring(0, normalized.Length - steamAppsSuffix.Length);
			}
			if (normalized.Length == 2 && normalized.EndsWith(":", StringComparison.Ordinal))
			{
				normalized += Path.DirectorySeparatorChar;
			}
			return normalized;
		}

		private void SteamDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			SteamDirectoryChanged?.Invoke(NormalizeSteamDirectory(SteamDirectoryTextBox.Text));
		}

		private void BrowseSteamDirectory_Click(object sender, RoutedEventArgs e)
		{
			using (var dialog = new Forms.FolderBrowserDialog())
			{
				dialog.Description = "Select your Steam directory";
				if (Directory.Exists(SteamDirectoryTextBox.Text))
				{
					dialog.SelectedPath = SteamDirectoryTextBox.Text;
				}

				if (dialog.ShowDialog() == Forms.DialogResult.OK)
				{
					SteamDirectoryTextBox.Text = NormalizeSteamDirectory(dialog.SelectedPath);
					SteamDirectoryTextBox.Focus();
				}
			}
		}

		private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter && SteamDirectoryTextBox.IsKeyboardFocusWithin)
			{
				SteamDirectoryTextBox.Text = NormalizeSteamDirectory(SteamDirectoryTextBox.Text);
				BrowseSteamDirectoryButton.Focus();
				e.Handled = true;
			}
		}
	}
}

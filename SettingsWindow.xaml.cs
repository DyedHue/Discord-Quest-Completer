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
			SteamDirectoryTextBox.Text = steamDirectory ?? "";
		}

		private void SteamDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			SteamDirectoryChanged?.Invoke(SteamDirectoryTextBox.Text);
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
					SteamDirectoryTextBox.Text = dialog.SelectedPath.Replace('/', '\\');
					SteamDirectoryTextBox.Focus();
				}
			}
		}

		private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter && SteamDirectoryTextBox.IsKeyboardFocusWithin)
			{
				SteamDirectoryTextBox.Text = SteamDirectoryTextBox.Text.Replace('/', '\\');
				BrowseSteamDirectoryButton.Focus();
				e.Handled = true;
			}
		}
	}
}

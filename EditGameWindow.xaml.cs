using System.Windows;

namespace DiscordQuestCompleter
{
    public partial class EditGameWindow : Window
    {
        public string ResultName { get; private set; } = "";
        public string ResultPath { get; private set; } = "";

        public EditGameWindow(string currentName, string currentPath)
        {
            InitializeComponent();
            NameInput.Text = currentName;
            PathInput.Text = currentPath;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ResultName = NameInput.Text.Trim();
            ResultPath = PathInput.Text.Trim();
            DialogResult = true;
        }
    }
}

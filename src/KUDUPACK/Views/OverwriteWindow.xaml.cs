using System.IO;
using System.Linq;
using System.Windows;
using Kudupack.Core;

namespace Kudupack.Views
{
    /// <summary>Asks what to do when extracted files would land on files that are already there.</summary>
    public partial class OverwriteWindow : Window
    {
        private OverwriteChoice _choice = OverwriteChoice.Cancel;

        private OverwriteWindow(OverwriteQuestion question)
        {
            InitializeComponent();
            ThemeManager.Attach(this);

            var count = question.Names.Count;
            var folder = Path.GetFileName(question.Folder.TrimEnd('\\'));
            if (string.IsNullOrEmpty(folder)) folder = question.Folder;
            HeadingText.Text = count == 1 ? "A file with this name is already there" : $"{count} files with these names are already there";
            MessageText.Text = (count == 1 ? "It's" : "They're") + " in \"" + folder + "\". What should KUDUPACK do?";
            NamesText.Text = string.Join("\n", question.Names.Take(6)) + (count > 6 ? $"\n…and {count - 6} more" : string.Empty);
            if (count > 1)
            {
                SkipButton.Content = "Skip them";
                ReplaceButton.Content = "Replace them";
            }
            else
            {
                SkipButton.Content = "Skip it";
                ReplaceButton.Content = "Replace it";
            }
        }

        public static OverwriteChoice Ask(Window owner, OverwriteQuestion question)
        {
            var window = new OverwriteWindow(question);
            if (owner != null && owner.IsVisible) window.Owner = owner;
            else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.ShowDialog();
            return window._choice;
        }

        private void Choose(OverwriteChoice choice)
        {
            _choice = choice;
            DialogResult = true;
        }

        private void Skip_Click(object sender, RoutedEventArgs e) => Choose(OverwriteChoice.Skip);
        private void Replace_Click(object sender, RoutedEventArgs e) => Choose(OverwriteChoice.Replace);
        private void Keep_Click(object sender, RoutedEventArgs e) => Choose(OverwriteChoice.KeepBoth);
    }
}

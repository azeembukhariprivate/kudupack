using System.Windows;
using Kudupack.Core;

namespace Kudupack.Views
{
    public partial class PasswordWindow : Window
    {
        private PasswordWindow(string message)
        {
            InitializeComponent();
            ThemeManager.Attach(this);
            MessageText.Text = message;
        }

        /// <summary>Returns the password, or null if the user cancelled.</summary>
        public static string Ask(Window owner, string message)
        {
            var window = new PasswordWindow(message);
            if (owner != null && owner.IsVisible) window.Owner = owner;
            else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return window.ShowDialog() == true ? window.PasswordInput.Password : null;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void ShowPassword_Changed(object sender, RoutedEventArgs e)
        {
            var show = ShowPassword.IsChecked == true;
            PlainInput.Text = show ? PasswordInput.Password : string.Empty;
            PlainInput.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}

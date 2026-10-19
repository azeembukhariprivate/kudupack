using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Kudupack.Core;

namespace Kudupack.Views
{
    public partial class AboutWindow : Window
    {
        private UpdateInfo _update;

        public AboutWindow()
        {
            InitializeComponent();
            ThemeManager.Attach(this);
            VersionText.Text = "Version " + AppInfo.VersionText;
        }

        private void Repo_Click(object sender, RoutedEventArgs e) => AppInfo.OpenUrl(AppInfo.RepoUrl);

        private void Licenses_Click(object sender, RoutedEventArgs e)
        {
            var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
            if (File.Exists(file))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                    return;
                }
                catch { }
            }
            AppInfo.OpenUrl(AppInfo.RepoUrl + "/blob/main/THIRD-PARTY-NOTICES.txt");
        }

        private async void Check_Click(object sender, RoutedEventArgs e)
        {
            if (_update?.IsNewer == true)
            {
                AppInfo.OpenUrl(_update.Url);
                return;
            }

            CheckButton.IsEnabled = false;
            UpdateStatus.Text = "Checking…";
            try
            {
                _update = await UpdateChecker.CheckAsync();
                if (_update == null)
                {
                    UpdateStatus.Text = "No releases have been published yet.";
                }
                else if (_update.IsNewer)
                {
                    UpdateStatus.Text = $"Version {_update.Latest.ToString(3)} is available.";
                    CheckButton.Content = "Download update";
                }
                else
                {
                    UpdateStatus.Text = "You have the latest version.";
                }
            }
            catch
            {
                UpdateStatus.Text = "Couldn't reach GitHub. Check your internet connection.";
            }
            finally
            {
                CheckButton.IsEnabled = true;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => Close();
    }
}

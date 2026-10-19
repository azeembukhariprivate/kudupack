using System;
using System.Windows;
using Kudupack.Core;

namespace Kudupack.Views
{
    public partial class SettingsWindow : Window
    {
        private bool _loading = true;

        public SettingsWindow()
        {
            InitializeComponent();
            ThemeManager.Attach(this);

            switch (Settings.Theme)
            {
                case ThemeMode.Light: ThemeLight.IsChecked = true; break;
                case ThemeMode.Dark: ThemeDark.IsChecked = true; break;
                default: ThemeSystem.IsChecked = true; break;
            }

            OpenFolder.IsChecked = Settings.OpenFolderAfterExtract;
            DeleteArchive.IsChecked = Settings.DeleteAfterExtract;
            CheckUpdates.IsChecked = Settings.CheckForUpdates;
            ContextMenuBox.IsChecked = ShellIntegration.IsContextMenuEnabled;
            Associate.IsChecked = ShellIntegration.IsAssociated;
            _loading = false;
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var mode = ThemeDark.IsChecked == true ? ThemeMode.Dark
                : ThemeLight.IsChecked == true ? ThemeMode.Light
                : ThemeMode.System;
            Settings.Theme = mode;
            ThemeManager.Apply(mode);
        }

        private void Option_Click(object sender, RoutedEventArgs e)
        {
            Settings.OpenFolderAfterExtract = OpenFolder.IsChecked == true;
            Settings.DeleteAfterExtract = DeleteArchive.IsChecked == true;
            Settings.CheckForUpdates = CheckUpdates.IsChecked == true;
        }

        private void ContextMenu_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShellIntegration.SetContextMenu(ContextMenuBox.IsChecked == true);
            }
            catch (Exception ex)
            {
                Dialogs.Error(this, Dialogs.Describe(ex));
                ContextMenuBox.IsChecked = ShellIntegration.IsContextMenuEnabled;
            }
        }

        private void Associate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShellIntegration.SetAssociations(Associate.IsChecked == true);
            }
            catch (Exception ex)
            {
                Dialogs.Error(this, Dialogs.Describe(ex));
                Associate.IsChecked = ShellIntegration.IsAssociated;
            }
        }

        private void DefaultApps_Click(object sender, RoutedEventArgs e) => ShellIntegration.OpenDefaultAppsSettings();

        private void Done_Click(object sender, RoutedEventArgs e) => Close();
    }
}

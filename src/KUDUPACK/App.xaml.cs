using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Kudupack.Core;
using Kudupack.Views;

namespace Kudupack
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnUnhandledException;

            var cmd = CommandLine.Parse(e.Args);

            // Installer / uninstaller hooks. No window is shown.
            if (cmd.IsSetupOnly)
            {
                if (cmd.Has("unregister")) ShellIntegration.RemoveAll();
                if (cmd.Has("register-shell")) ShellIntegration.SetContextMenu(true);
                if (cmd.Has("register-assoc")) ShellIntegration.SetAssociations(true);
                Shutdown();
                return;
            }

            ThemeManager.Apply(Settings.Theme);
            ThemeManager.WatchSystem();
            Paths.CleanOldTemp();

            try
            {
                ArchiveEngine.Init();
            }
            catch (Exception ex)
            {
                Dialogs.Error(null, ex.Message);
                Shutdown();
                return;
            }

            if (cmd.Has("extract-here") || cmd.Has("extract-folder"))
            {
                var archives = cmd.Paths.Where(ArchiveEngine.LooksLikeArchive).ToList();
                if (archives.Count > 0)
                    Operations.ExtractArchives(null, archives, intoOwnFolder: cmd.Has("extract-folder"));
                Shutdown();
                return;
            }

            if (cmd.Has("add"))
            {
                var all = AddCollector.Collect(cmd.Paths);
                if (all != null && all.Count > 0)
                {
                    var window = new CompressWindow(all) { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                    window.ShowDialog();
                }
                Shutdown();
                return;
            }

            ShellIntegration.Refresh();

            var main = new Views.MainWindow(cmd.Paths.FirstOrDefault());
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }

        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Dialogs.Error(MainWindow?.IsVisible == true ? MainWindow : null,
                "Something went wrong:\n\n" + e.Exception.Message);
            e.Handled = true;
        }
    }
}

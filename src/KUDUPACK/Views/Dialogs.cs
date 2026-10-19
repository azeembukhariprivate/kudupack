using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Kudupack.Core;
using SevenZip;
using Forms = System.Windows.Forms;

namespace Kudupack.Views
{
    public static class Dialogs
    {
        public static void Info(Window owner, string message, string title = "KUDUPACK") =>
            Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public static void Error(Window owner, string message, string title = "KUDUPACK") =>
            Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        public static bool Confirm(Window owner, string message, string title = "KUDUPACK") =>
            Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        private static MessageBoxResult Show(Window owner, string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            return owner != null && owner.IsVisible
                ? MessageBox.Show(owner, message, title, buttons, image)
                : MessageBox.Show(message, title, buttons, image);
        }

        /// <summary>A short, human message for an error.</summary>
        public static string Describe(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is NeedsAdminException) return e.Message;
                if (e is UnauthorizedAccessException)
                    return "Windows blocked this action for a normal user.\n\n" +
                           "Choose a folder in your user profile, or close KUDUPACK and start it with \"Run as administrator\".";
            }

            if (ex is SevenZipArchiveException)
                return "This file couldn't be opened. It may be damaged, incomplete, or not an archive KUDUPACK supports.";
            if (ex is SevenZipException)
                return "The archive engine reported a problem:\n\n" + ex.Message;
            return ex.Message;
        }

        public static string PickFolder(Window owner, string description, string startFolder = null)
        {
            using (var dialog = new Forms.FolderBrowserDialog
                   {
                       Description = description,
                       ShowNewFolderButton = true,
                       SelectedPath = startFolder ?? string.Empty
                   })
            {
                return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
            }
        }

        public static List<string> PickArchives(Window owner, bool multiple)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = multiple ? "Choose archives to extract" : "Open an archive",
                Filter = ArchiveEngine.OpenFileFilter,
                Multiselect = multiple
            };
            return Show(dialog, owner) ? dialog.FileNames.ToList() : new List<string>();
        }

        private static bool Show(Microsoft.Win32.CommonDialog dialog, Window owner) =>
            (owner != null && owner.IsVisible ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;

        public static List<string> PickFiles(Window owner, string title)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = title,
                Filter = "All files|*.*",
                Multiselect = true
            };
            return Show(dialog, owner) ? dialog.FileNames.ToList() : new List<string>();
        }
    }
}

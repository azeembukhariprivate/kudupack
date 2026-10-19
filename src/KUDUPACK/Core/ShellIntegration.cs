using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Kudupack.Core
{
    /// <summary>
    /// File Explorer right-click menu and "Open with" registration.
    /// Everything is written under HKEY_CURRENT_USER\Software\Classes, so no admin rights are needed.
    /// </summary>
    public static class ShellIntegration
    {
        private const string ClassesPath = @"Software\Classes";
        private const string ProgId = "KUDUPACK.Archive";
        private const string AddVerb = "KUDUPACK.Add";
        private const string ExtractHereVerb = "KUDUPACK.ExtractHere";
        private const string ExtractFolderVerb = "KUDUPACK.ExtractFolder";

        private static readonly string[] ArchiveExtensions =
        {
            ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz",
            ".iso", ".cab", ".arj", ".lzh", ".wim", ".cpio", ".001"
        };

        private static string Exe => AppInfo.ExePath;

        public static bool IsContextMenuEnabled => KeyExists($@"{ClassesPath}\*\shell\{AddVerb}");

        public static bool IsAssociated => KeyExists($@"{ClassesPath}\{ProgId}");

        public static void SetContextMenu(bool enabled)
        {
            using (var classes = Registry.CurrentUser.CreateSubKey(ClassesPath))
            {
                if (classes == null) return;

                foreach (var ext in ArchiveExtensions)
                {
                    var shell = $@"SystemFileAssociations\{ext}\shell";
                    if (enabled)
                    {
                        SetVerb(classes, $@"{shell}\{ExtractHereVerb}", "KUDUPACK: Extract here", "--extract-here \"%1\"");
                        SetVerb(classes, $@"{shell}\{ExtractFolderVerb}", "KUDUPACK: Extract to folder", "--extract-folder \"%1\"");
                    }
                    else
                    {
                        DeleteTree(classes, $@"{shell}\{ExtractHereVerb}");
                        DeleteTree(classes, $@"{shell}\{ExtractFolderVerb}");
                    }
                }

                foreach (var target in new[] { @"*\shell", @"Directory\shell" })
                {
                    if (enabled)
                        SetVerb(classes, $@"{target}\{AddVerb}", "KUDUPACK: Add to archive…", "--add \"%1\"");
                    else
                        DeleteTree(classes, $@"{target}\{AddVerb}");
                }
            }

            NotifyShell();
        }

        public static void SetAssociations(bool enabled)
        {
            using (var classes = Registry.CurrentUser.CreateSubKey(ClassesPath))
            {
                if (classes == null) return;

                if (enabled)
                {
                    using (var prog = classes.CreateSubKey(ProgId))
                    {
                        prog?.SetValue(string.Empty, "KUDUPACK archive");
                        using (var icon = prog?.CreateSubKey("DefaultIcon")) icon?.SetValue(string.Empty, $"\"{Exe}\",0");
                        using (var cmd = prog?.CreateSubKey(@"shell\open\command")) cmd?.SetValue(string.Empty, $"\"{Exe}\" \"%1\"");
                    }

                    using (var app = classes.CreateSubKey(@"Applications\KUDUPACK.exe"))
                    {
                        app?.SetValue("FriendlyAppName", "KUDUPACK");
                        using (var cmd = app?.CreateSubKey(@"shell\open\command")) cmd?.SetValue(string.Empty, $"\"{Exe}\" \"%1\"");
                        using (var types = app?.CreateSubKey("SupportedTypes"))
                        {
                            foreach (var ext in ArchiveExtensions) types?.SetValue(ext, string.Empty);
                        }
                    }

                    foreach (var ext in ArchiveExtensions)
                    {
                        using (var key = classes.CreateSubKey(ext))
                        {
                            if (key == null) continue;
                            using (var owp = key.CreateSubKey("OpenWithProgids")) owp?.SetValue(ProgId, string.Empty);
                            // Only become the default when no other app has claimed the type.
                            if (string.IsNullOrEmpty(key.GetValue(string.Empty) as string))
                                key.SetValue(string.Empty, ProgId);
                        }
                    }
                }
                else
                {
                    foreach (var ext in ArchiveExtensions)
                    {
                        using (var key = classes.OpenSubKey(ext, true))
                        {
                            if (key == null) continue;
                            using (var owp = key.OpenSubKey("OpenWithProgids", true)) owp?.DeleteValue(ProgId, false);
                            if (string.Equals(key.GetValue(string.Empty) as string, ProgId, StringComparison.OrdinalIgnoreCase))
                                key.DeleteValue(string.Empty, false);
                        }
                    }
                    DeleteTree(classes, ProgId);
                    DeleteTree(classes, @"Applications\KUDUPACK.exe");
                }
            }

            NotifyShell();
        }

        /// <summary>Re-points existing registrations at this copy of the app (e.g. after it was moved).</summary>
        public static void Refresh()
        {
            try
            {
                if (IsContextMenuEnabled) SetContextMenu(true);
                if (IsAssociated) SetAssociations(true);
            }
            catch { /* best effort */ }
        }

        public static void RemoveAll()
        {
            try { SetContextMenu(false); } catch { }
            try { SetAssociations(false); } catch { }
        }

        public static void OpenDefaultAppsSettings() => AppInfo.OpenUrl("ms-settings:defaultapps");

        private static void SetVerb(RegistryKey classes, string keyPath, string text, string args)
        {
            using (var key = classes.CreateSubKey(keyPath))
            {
                if (key == null) return;
                key.SetValue("MUIVerb", text);
                key.SetValue("Icon", $"\"{Exe}\",0");
                using (var cmd = key.CreateSubKey("command"))
                    cmd?.SetValue(string.Empty, $"\"{Exe}\" {args}");
            }
        }

        private static void DeleteTree(RegistryKey parent, string path)
        {
            try { parent.DeleteSubKeyTree(path, false); } catch { }
        }

        private static bool KeyExists(string path)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(path)) return key != null;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        private static void NotifyShell()
        {
            try { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { }
        }
    }
}

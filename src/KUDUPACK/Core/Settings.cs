using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Kudupack.Core
{
    public enum ThemeMode { System, Light, Dark }

    /// <summary>
    /// Settings are stored under HKEY_CURRENT_USER, so no administrator rights are needed.
    /// </summary>
    public static class Settings
    {
        private const string KeyPath = @"Software\KUDUPACK";
        private const int MaxRecent = 8;

        private static string Get(string name, string fallback)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    return key?.GetValue(name) as string ?? fallback;
                }
            }
            catch
            {
                return fallback;
            }
        }

        private static void Set(string name, string value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    key?.SetValue(name, value ?? string.Empty);
                }
            }
            catch
            {
                // Settings are best-effort.
            }
        }

        private static bool GetBool(string name, bool fallback) => Get(name, fallback ? "1" : "0") == "1";
        private static void SetBool(string name, bool value) => Set(name, value ? "1" : "0");

        public static ThemeMode Theme
        {
            get => Enum.TryParse(Get("Theme", "System"), out ThemeMode mode) ? mode : ThemeMode.System;
            set => Set("Theme", value.ToString());
        }

        public static bool OpenFolderAfterExtract
        {
            get => GetBool("OpenFolderAfterExtract", true);
            set => SetBool("OpenFolderAfterExtract", value);
        }

        public static bool DeleteAfterExtract
        {
            get => GetBool("DeleteAfterExtract", false);
            set => SetBool("DeleteAfterExtract", value);
        }

        public static bool CheckForUpdates
        {
            get => GetBool("CheckForUpdates", true);
            set => SetBool("CheckForUpdates", value);
        }

        public static bool ShowPreview
        {
            get => GetBool("ShowPreview", true);
            set => SetBool("ShowPreview", value);
        }

        public static CreateFormat LastFormat
        {
            get => Enum.TryParse(Get("LastFormat", "Zip"), out CreateFormat f) ? f : CreateFormat.Zip;
            set => Set("LastFormat", value.ToString());
        }

        public static int LastLevel
        {
            get => int.TryParse(Get("LastLevel", "2"), out var l) && l >= 0 && l <= 4 ? l : 2;
            set => Set("LastLevel", value.ToString());
        }

        public static string SkippedVersion
        {
            get => Get("SkippedVersion", string.Empty);
            set => Set("SkippedVersion", value);
        }

        public static List<string> Recent
        {
            get
            {
                return Get("Recent", string.Empty)
                    .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
            }
        }

        public static void AddRecent(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var list = Recent;
            list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, path);
            Set("Recent", string.Join("|", list.Take(MaxRecent)));
        }

        public static void RemoveRecent(string path)
        {
            var list = Recent;
            list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            Set("Recent", string.Join("|", list));
        }

        public static void ClearRecent() => Set("Recent", string.Empty);

        public static List<string> ExistingRecent() => Recent.Where(File.Exists).ToList();
    }
}

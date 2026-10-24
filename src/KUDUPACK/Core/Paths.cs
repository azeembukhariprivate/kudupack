using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Kudupack.Core
{
    /// <summary>Thrown when Windows blocks writing to a folder for a normal (non-admin) user.</summary>
    public class NeedsAdminException : Exception
    {
        public string Folder { get; }

        public NeedsAdminException(string folder, Exception inner = null)
            : base("Windows didn't allow KUDUPACK to write to this folder:\n" + folder +
                   "\n\nPick a folder in your user profile (like Desktop or Documents), " +
                   "or close KUDUPACK and start it with \"Run as administrator\".", inner)
        {
            Folder = folder;
        }
    }

    public static class Paths
    {
        private static readonly string[] DoubleExtensions =
        {
            ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.lzma", ".tar.z"
        };

        public static string TempRoot => Path.Combine(Path.GetTempPath(), "KUDUPACK");

        public static string NewTempFolder(string purpose)
        {
            var dir = Path.Combine(TempRoot, purpose, Guid.NewGuid().ToString("N").Substring(0, 12));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Removes leftovers from earlier sessions (older than one day).</summary>
        public static void CleanOldTemp()
        {
            try
            {
                if (!Directory.Exists(TempRoot)) return;
                foreach (var purpose in Directory.GetDirectories(TempRoot))
                {
                    foreach (var dir in Directory.GetDirectories(purpose))
                    {
                        try
                        {
                            if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddDays(-1))
                                Directory.Delete(dir, true);
                        }
                        catch { /* in use */ }
                    }
                }
            }
            catch { /* best effort */ }
        }

        public static void TryDeleteFolder(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        /// <summary>"photos.tar.gz" -> "photos", "backup.7z.001" -> "backup", "game.part1.rar" -> "game".</summary>
        public static string ArchiveBaseName(string archivePath)
        {
            var name = Path.GetFileName(archivePath) ?? "archive";

            if (Regex.IsMatch(name, @"\.\d{3}$")) name = name.Substring(0, name.Length - 4);

            foreach (var ext in DoubleExtensions)
            {
                if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return name.Substring(0, name.Length - ext.Length);
            }

            name = Path.GetFileNameWithoutExtension(name);
            name = Regex.Replace(name, @"\.part\d+$", string.Empty, RegexOptions.IgnoreCase);
            return string.IsNullOrWhiteSpace(name) ? "archive" : name;
        }

        public static string UniqueFolder(string parent, string name)
        {
            var path = Path.Combine(parent, name);
            var i = 2;
            while (Directory.Exists(path) || File.Exists(path))
                path = Path.Combine(parent, $"{name} ({i++})");
            return path;
        }

        public static string UniqueFilePath(string folder, string name)
        {
            var path = Path.Combine(folder, name);
            var stem = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            var i = 2;
            while (File.Exists(path) || Directory.Exists(path)) path = Path.Combine(folder, $"{stem} ({i++}){ext}");
            return path;
        }

        // Characters the archive engine replaces with "_" in the names it writes (Windows' invalid file name characters).
        private const string EngineReplaces = "\"<>|:*?";

        private static readonly Regex DeviceName = new Regex(@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9]|CONIN\$|CONOUT\$) *(\..*)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Where the archive engine writes an entry when extracting into a folder, or null when the entry's name would put
        /// it anywhere else: a path starting with "\", ".." parts (the "zip slip" trick) or a name Windows reserves for a
        /// device (CON, NUL, COM1…). Mirrors how the engine builds the path, so the answer matches where the file lands.
        /// </summary>
        public static string EntryTarget(string folder, string entryName)
        {
            var name = (entryName ?? string.Empty).Replace('/', '\\');
            if (name.StartsWith("\\", StringComparison.Ordinal)) return null;

            var parts = new System.Collections.Generic.List<string>();
            foreach (var raw in name.Split('\\'))
            {
                if (raw.Length == 0 || raw == ".") continue;
                // Windows drops trailing dots and spaces, so "..", "..." and ". ." all lead upwards.
                var trimmed = raw.TrimEnd('.', ' ');
                if (trimmed.Length == 0 || DeviceName.IsMatch(trimmed)) return null;
                var part = new System.Text.StringBuilder(raw.Length);
                foreach (var c in raw) part.Append(c < 32 || EngineReplaces.IndexOf(c) >= 0 ? '_' : c);
                parts.Add(part.ToString());
            }
            if (parts.Count == 0) return null;

            var target = Path.Combine(folder, string.Join(Path.DirectorySeparatorChar.ToString(), parts));
            return IsInside(target, folder) ? target : null;
        }

        /// <summary>True when a path is inside a folder (not the folder itself).</summary>
        public static bool IsInside(string path, string folder)
        {
            try
            {
                var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Creates the folder and checks we can write to it, without admin rights.</summary>
        public static void EnsureWritable(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".kudupack-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new NeedsAdminException(dir, ex);
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == 5)
            {
                throw new NeedsAdminException(dir, ex);
            }
        }

        public static bool IsWritable(string dir)
        {
            try { EnsureWritable(dir); return true; }
            catch { return false; }
        }

        public static string FormatSize(long bytes) => FormatSize(bytes < 0 ? 0UL : (ulong)bytes);

        public static string FormatSize(ulong bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
        }

        public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }
}

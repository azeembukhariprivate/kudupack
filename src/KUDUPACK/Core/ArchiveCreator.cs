using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SevenZip;

namespace Kudupack.Core
{
    public enum CreateFormat { Zip, SevenZip, Tar, TarGz, TarBz2, TarXz }

    public sealed class CreateOptions
    {
        public List<string> Inputs { get; } = new List<string>();
        public string OutputPath { get; set; }
        public CreateFormat Format { get; set; } = CreateFormat.Zip;

        /// <summary>0 = Store, 1 = Fast, 2 = Normal, 3 = Maximum, 4 = Ultra.</summary>
        public int Level { get; set; } = 2;

        public string Password { get; set; }
        public bool EncryptNames { get; set; }

        /// <summary>Split size in bytes, or 0 for a single file.</summary>
        public long VolumeSize { get; set; }
    }

    public static class ArchiveCreator
    {
        public static string Extension(CreateFormat f)
        {
            switch (f)
            {
                case CreateFormat.SevenZip: return ".7z";
                case CreateFormat.Tar: return ".tar";
                case CreateFormat.TarGz: return ".tar.gz";
                case CreateFormat.TarBz2: return ".tar.bz2";
                case CreateFormat.TarXz: return ".tar.xz";
                default: return ".zip";
            }
        }

        public static bool SupportsPassword(CreateFormat f) => f == CreateFormat.Zip || f == CreateFormat.SevenZip;
        public static bool SupportsVolumes(CreateFormat f) => f == CreateFormat.Zip || f == CreateFormat.SevenZip;
        public static bool SupportsLevel(CreateFormat f) => f != CreateFormat.Tar;

        private static CompressionLevel ToLevel(int level)
        {
            switch (level)
            {
                case 0: return CompressionLevel.None;
                case 1: return CompressionLevel.Fast;
                case 3: return CompressionLevel.High;
                case 4: return CompressionLevel.Ultra;
                default: return CompressionLevel.Normal;
            }
        }

        public static void Create(CreateOptions o, ProgressReporter r)
        {
            ArchiveEngine.Init();
            r.Status("Collecting files…");
            var map = BuildEntryMap(o.Inputs);
            if (map.Count == 0) throw new InvalidOperationException("There are no files to add.");

            var outDir = Path.GetDirectoryName(o.OutputPath);
            Paths.EnsureWritable(outDir);

            var password = SupportsPassword(o.Format) ? (o.Password ?? string.Empty) : string.Empty;
            string temp = null;

            try
            {
                switch (o.Format)
                {
                    case CreateFormat.Zip:
                    case CreateFormat.SevenZip:
                    {
                        var c = NewCompressor(o.Format == CreateFormat.Zip ? OutArchiveFormat.Zip : OutArchiveFormat.SevenZip, o.Level);
                        if (o.Format == CreateFormat.Zip && password.Length > 0)
                            c.ZipEncryptionMethod = ZipEncryptionMethod.Aes256;
                        if (o.Format == CreateFormat.SevenZip && password.Length > 0 && o.EncryptNames)
                            c.EncryptHeaders = true;
                        if (o.VolumeSize > 0 && SupportsVolumes(o.Format))
                            c.VolumeSize = o.VolumeSize;
                        r.Status("Compressing…");
                        Hook(c, r);
                        c.CompressFileDictionary(map, o.OutputPath, password);
                        break;
                    }

                    case CreateFormat.Tar:
                    {
                        var c = NewCompressor(OutArchiveFormat.Tar, 0);
                        r.Status("Packing…");
                        Hook(c, r);
                        c.CompressFileDictionary(map, o.OutputPath);
                        break;
                    }

                    default:
                    {
                        // Two steps: build the .tar, then compress it with gzip, bzip2 or xz.
                        temp = Paths.NewTempFolder("create");
                        var tarName = Path.GetFileNameWithoutExtension(o.OutputPath);
                        if (!tarName.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) tarName += ".tar";
                        var tarPath = Path.Combine(temp, tarName);

                        r.Status("Packing…");
                        var tar = NewCompressor(OutArchiveFormat.Tar, 0);
                        Hook(tar, r.Slice(0, 40));
                        tar.CompressFileDictionary(map, tarPath);
                        r.Token.ThrowIfCancellationRequested();

                        var format = o.Format == CreateFormat.TarGz ? OutArchiveFormat.GZip
                            : o.Format == CreateFormat.TarBz2 ? OutArchiveFormat.BZip2
                            : OutArchiveFormat.XZ;
                        r.Status("Compressing…");
                        var squeeze = NewCompressor(format, Math.Max(1, o.Level));
                        Hook(squeeze, r.Slice(40, 100));
                        squeeze.CompressFiles(o.OutputPath, tarPath);
                        break;
                    }
                }

                r.Token.ThrowIfCancellationRequested();
                r.Percent(100);
            }
            catch
            {
                TryDelete(o.OutputPath);
                throw;
            }
            finally
            {
                if (temp != null) Paths.TryDeleteFolder(temp);
            }
        }

        private static SevenZipCompressor NewCompressor(OutArchiveFormat format, int level) =>
            new SevenZipCompressor
            {
                ArchiveFormat = format,
                CompressionLevel = format == OutArchiveFormat.Tar ? CompressionLevel.None : ToLevel(level),
                DirectoryStructure = true,
                IncludeEmptyDirectories = true,
                PreserveDirectoryRoot = false
            };

        public static void Hook(SevenZipCompressor c, ProgressReporter r)
        {
            c.Compressing += (s, e) => r.Percent(e.PercentDone);
            c.FileCompressionStarted += (s, e) =>
            {
                if (r.Token.IsCancellationRequested) e.Cancel = true;
            };
        }

        /// <summary>
        /// Maps entry names inside the archive to files on disk. Folders keep their name as the top level.
        /// A null value means an empty folder.
        /// </summary>
        public static Dictionary<string, string> BuildEntryMap(IEnumerable<string> inputs, string prefix = "")
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            prefix = (prefix ?? string.Empty).Trim('\\');

            foreach (var input in inputs.Where(i => !string.IsNullOrWhiteSpace(i)))
            {
                var full = Path.GetFullPath(input);
                if (full.Length > 3) full = full.TrimEnd('\\');

                if (File.Exists(full))
                {
                    AddUnique(map, Join(prefix, Path.GetFileName(full)), full);
                }
                else if (Directory.Exists(full))
                {
                    var rootName = Path.GetFileName(full);
                    if (string.IsNullOrEmpty(rootName)) rootName = full.Substring(0, 1);
                    var root = Join(prefix, rootName);
                    var before = map.Count;
                    Walk(full, full, root, map);
                    if (map.Count == before) AddUnique(map, root, null);
                }
            }

            return map;
        }

        private static void Walk(string baseDir, string dir, string root, Dictionary<string, string> map)
        {
            string[] files;
            string[] dirs;
            try
            {
                files = Directory.GetFiles(dir);
                dirs = Directory.GetDirectories(dir);
            }
            catch (UnauthorizedAccessException)
            {
                return; // Skip folders Windows won't let a normal user read.
            }

            foreach (var file in files)
                AddUnique(map, Join(root, file.Substring(baseDir.Length).TrimStart('\\')), file);

            foreach (var sub in dirs)
            {
                var before = map.Count;
                Walk(baseDir, sub, root, map);
                if (map.Count == before)
                    AddUnique(map, Join(root, sub.Substring(baseDir.Length).TrimStart('\\')), null);
            }
        }

        private static string Join(string a, string b) =>
            string.IsNullOrEmpty(a) ? b : a + "\\" + b;

        private static void AddUnique(Dictionary<string, string> map, string name, string path)
        {
            var candidate = name;
            var i = 2;
            while (map.ContainsKey(candidate))
            {
                var ext = Path.GetExtension(name);
                var stem = name.Substring(0, name.Length - ext.Length);
                candidate = $"{stem} ({i++}){ext}";
            }
            map[candidate] = path;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (File.Exists(path)) File.Delete(path);
                // Split archives: name.7z.001, name.7z.002, ...
                var dir = Path.GetDirectoryName(path);
                var name = Path.GetFileName(path);
                if (dir != null && Directory.Exists(dir))
                {
                    foreach (var part in Directory.GetFiles(dir, name + ".*"))
                    {
                        if (System.Text.RegularExpressions.Regex.IsMatch(part, @"\.\d{3}$")) File.Delete(part);
                    }
                }
            }
            catch { /* best effort */ }
        }
    }
}

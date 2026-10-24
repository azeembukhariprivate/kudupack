using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SevenZip;

namespace Kudupack.Core
{
    /// <summary>What an extraction left out or changed.</summary>
    public sealed class ExtractReport
    {
        /// <summary>Entries not extracted because their names would put them outside the folder (see <see cref="Paths.EntryTarget"/>).</summary>
        public List<string> Unsafe { get; } = new List<string>();
        public int Replaced { get; set; }
        public int Skipped { get; set; }
        public int KeptBoth { get; set; }

        public void Add(ExtractReport other)
        {
            Unsafe.AddRange(other.Unsafe);
            Replaced += other.Replaced;
            Skipped += other.Skipped;
            KeptBoth += other.KeptBoth;
        }

        /// <summary>A warning about the entries that were left out, or null when there were none.</summary>
        public string UnsafeWarning()
        {
            if (Unsafe.Count == 0) return null;
            var list = string.Join("\n", Unsafe.Take(8)) + (Unsafe.Count > 8 ? $"\n…and {Unsafe.Count - 8} more" : string.Empty);
            return (Unsafe.Count == 1 ? "1 item wasn't" : $"{Unsafe.Count} items weren't") +
                   " extracted because its name would put it outside the folder you chose, or uses a name Windows reserves. " +
                   "Archives made to harm a PC use names like these, so be careful with the rest of this archive.\n\n" + list;
        }
    }

    /// <summary>An opened archive: its file tree plus the operations that act on it.</summary>
    public sealed class ArchiveSession : IDisposable
    {
        private string _tempDir;
        private List<ArchiveFileInfo> _entries = new List<ArchiveFileInfo>();

        private ArchiveSession(string path)
        {
            SourcePath = path;
            WorkingPath = path;
        }

        /// <summary>The file the user opened.</summary>
        public string SourcePath { get; }

        /// <summary>The archive actually read. Differs from SourcePath for .tar.gz and similar.</summary>
        public string WorkingPath { get; private set; }

        public string DisplayName => Path.GetFileName(SourcePath);
        public string Password { get; private set; }
        public InArchiveFormat Format { get; private set; }
        public string FormatName { get; private set; } = string.Empty;
        public ArchiveNode Root { get; private set; }
        public List<ArchiveNode> AllFiles { get; } = new List<ArchiveNode>();
        public int FolderCount { get; private set; }
        public ulong UnpackedSize { get; private set; }
        public long PackedSize { get; private set; }
        public bool HasEncrypted { get; private set; }
        public bool IsMultiVolume { get; private set; }
        public List<string> VolumeFiles { get; private set; } = new List<string>();

        public bool IsNested => !string.Equals(WorkingPath, SourcePath, StringComparison.OrdinalIgnoreCase);

        /// <summary>Adding and deleting files works for single-volume ZIP and 7Z archives.</summary>
        public bool CanModify =>
            !IsNested && !IsMultiVolume &&
            (Format == InArchiveFormat.Zip || Format == InArchiveFormat.SevenZip);

        public bool NeedsPassword => HasEncrypted && string.IsNullOrEmpty(Password);

        public static ArchiveSession Open(string path, ProgressReporter reporter, string knownPassword = null)
        {
            ArchiveEngine.Init();
            var session = new ArchiveSession(Path.GetFullPath(path)) { Password = knownPassword };
            try
            {
                session.Load(reporter);
                return session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        private void Load(ProgressReporter r)
        {
            r.Status("Reading " + DisplayName + "…");
            ReadIndex(SourcePath, r, out var data, out var format, out var volumes);
            Format = format;
            VolumeFiles = volumes;
            IsMultiVolume = volumes.Count > 1;
            PackedSize = volumes.Where(File.Exists).Sum(v => new FileInfo(v).Length);
            if (PackedSize == 0 && File.Exists(SourcePath)) PackedSize = new FileInfo(SourcePath).Length;
            FormatName = Label(format);

            // .tar.gz, .tgz, .tar.bz2, .tar.xz: open the TAR inside so the user sees the real files.
            if (IsSingleFileWrapper(format) && data.Count == 1 && !data[0].IsDirectory && InnerLooksLikeTar(data[0].FileName))
            {
                r.Status("Unpacking the TAR inside " + DisplayName + "…");
                _tempDir = Paths.NewTempFolder("open");
                var innerName = NameOf(Normalize(data[0].FileName));
                if (!innerName.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
                    innerName = Paths.ArchiveBaseName(SourcePath) + ".tar";
                var tarPath = Path.Combine(_tempDir, innerName);

                using (var ex = NewExtractor(SourcePath))
                using (var fs = File.Create(tarPath))
                {
                    Hook(ex, r, null);
                    ex.ExtractFile(data[0].Index, fs);
                }
                r.Token.ThrowIfCancellationRequested();

                WorkingPath = tarPath;
                ReadIndex(WorkingPath, r, out data, out _, out _);
                FormatName = "TAR." + Label(format);
            }

            BuildTree(data);
        }

        private void ReadIndex(string path, ProgressReporter r, out List<ArchiveFileInfo> data,
            out InArchiveFormat format, out List<string> volumes)
        {
            for (var attempt = 0; ; attempt++)
            {
                r.Token.ThrowIfCancellationRequested();
                try
                {
                    using (var ex = NewExtractor(path))
                    {
                        data = ex.ArchiveFileData.ToList();
                        format = ex.Format;
                        volumes = ex.VolumeFileNames?.ToList() ?? new List<string>();
                        if (volumes.Count == 0) volumes.Add(path);
                        return;
                    }
                }
                catch (SevenZipException) when (attempt < 3 && MayHaveEncryptedNames(SourcePath))
                {
                    var hint = attempt == 0
                        ? DisplayName + " may be password-protected."
                        : "That password didn't work for " + DisplayName + ". Try again.";
                    var pw = r.AskPassword(hint);
                    if (pw == null) throw new OperationCanceledException();
                    Password = pw;
                }
            }
        }

        private void BuildTree(List<ArchiveFileInfo> data)
        {
            _entries = data;
            Root = new ArchiveNode { Name = DisplayName, IsFolder = true, FullPath = string.Empty, IsExpanded = true };
            var folders = new Dictionary<string, ArchiveNode>(StringComparer.OrdinalIgnoreCase) { [string.Empty] = Root };

            foreach (var e in data)
            {
                var p = Normalize(e.FileName);
                if (p.Length == 0)
                {
                    // A single compressed file (e.g. notes.txt.gz) can have no stored name.
                    if (e.IsDirectory || data.Count != 1) continue;
                    p = Paths.ArchiveBaseName(SourcePath);
                }

                if (e.IsDirectory)
                {
                    var folder = GetFolder(folders, p);
                    folder.Index = e.Index;
                    folder.Modified = e.LastWriteTime;
                    continue;
                }

                var parent = GetFolder(folders, ParentOf(p));
                var node = new ArchiveNode
                {
                    Name = NameOf(p),
                    FullPath = p,
                    IsFolder = false,
                    Size = e.Size,
                    Modified = e.LastWriteTime,
                    Encrypted = e.Encrypted,
                    Index = e.Index,
                    Parent = parent
                };
                parent.Children.Add(node);
                AllFiles.Add(node);
                UnpackedSize += e.Size;
                if (e.Encrypted) HasEncrypted = true;
            }

            FolderCount = folders.Count - 1;
            Root.SortChildren();
        }

        private static ArchiveNode GetFolder(Dictionary<string, ArchiveNode> folders, string path)
        {
            if (folders.TryGetValue(path, out var existing)) return existing;
            var parent = GetFolder(folders, ParentOf(path));
            var node = new ArchiveNode { Name = NameOf(path), FullPath = path, IsFolder = true, Parent = parent };
            parent.Children.Add(node);
            parent.SubFolders.Add(node);
            folders[path] = node;
            return node;
        }

        // ---------- Operations ----------

        public ExtractReport ExtractAll(string destination, ProgressReporter r) => Extract(destination, null, r);

        public ExtractReport ExtractIndexes(string destination, IEnumerable<int> indexes, ProgressReporter r)
        {
            var list = indexes.Distinct().OrderBy(i => i).ToArray();
            return list.Length == 0 ? new ExtractReport() : Extract(destination, list, r);
        }

        /// <summary>
        /// Extracts everything (indexes null) or some entries into a folder. Entries whose names would land outside the
        /// folder are never written, and when files already exist the user chooses to replace, keep both or skip them.
        /// </summary>
        private ExtractReport Extract(string destination, int[] indexes, ProgressReporter r)
        {
            Paths.EnsureWritable(destination);
            var report = new ExtractReport();
            var wanted = indexes == null ? null : new HashSet<int>(indexes);
            var skip = new HashSet<int>();
            var existing = new List<(int Index, string Name, string Target)>();

            foreach (var e in _entries)
            {
                if (wanted != null && !wanted.Contains(e.Index)) continue;
                var name = EngineName(e);
                var target = Paths.EntryTarget(destination, name);
                if (target == null)
                {
                    skip.Add(e.Index);
                    report.Unsafe.Add(name);
                }
                else if (!e.IsDirectory && File.Exists(target))
                {
                    existing.Add((e.Index, name, target));
                }
            }

            var choice = OverwriteChoice.Replace;
            if (existing.Count > 0)
            {
                choice = r.AskOverwrite(new OverwriteQuestion
                {
                    Folder = destination,
                    Names = existing.Select(x => x.Target.Substring(destination.TrimEnd('\\', '/').Length).TrimStart('\\', '/')).ToList()
                });
                if (choice == OverwriteChoice.Cancel) throw new OperationCanceledException();
                if (choice == OverwriteChoice.Replace) report.Replaced = existing.Count;
                else
                {
                    // Skipped here; "keep both" extracts them again below, under new names.
                    foreach (var x in existing) skip.Add(x.Index);
                    if (choice == OverwriteChoice.Skip) report.Skipped = existing.Count;
                }
            }

            WithPassword(r, () =>
            {
                using (var ex = NewExtractor(WorkingPath))
                {
                    Hook(ex, r, skip);
                    if (indexes == null) ex.ExtractArchive(destination);
                    else ex.ExtractFiles(destination, indexes);
                }
            });

            if (choice == OverwriteChoice.KeepBoth)
            {
                // Extract the clashing files on their own, then move each one in beside the file it would have replaced.
                var temp = Paths.NewTempFolder("keep");
                try
                {
                    var list = existing.Select(x => x.Index).Distinct().OrderBy(i => i).ToArray();
                    WithPassword(r, () =>
                    {
                        using (var ex = NewExtractor(WorkingPath))
                        {
                            Hook(ex, r, null);
                            ex.ExtractFiles(temp, list);
                        }
                    });
                    foreach (var x in existing)
                    {
                        var extracted = Paths.EntryTarget(temp, x.Name);
                        if (extracted == null || !File.Exists(extracted)) continue;
                        File.Move(extracted, Paths.UniqueFilePath(Path.GetDirectoryName(x.Target), Path.GetFileName(x.Target)));
                        report.KeptBoth++;
                    }
                }
                finally
                {
                    Paths.TryDeleteFolder(temp);
                }
            }

            return report;
        }

        /// <summary>The name the archive engine extracts an entry under (it makes one up for entries without a name).</summary>
        private string EngineName(ArchiveFileInfo e)
        {
            if (!string.IsNullOrEmpty(e.FileName)) return e.FileName;
            if (_entries.Count != 1) return "[no name] " + e.Index;
            var name = Path.GetFileName(WorkingPath);
            var dot = name.LastIndexOf('.');
            if (dot > 0) name = name.Substring(0, dot);
            return name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ? name : name + ".tar";
        }

        public bool Test(ProgressReporter r)
        {
            var ok = false;
            WithPassword(r, () =>
            {
                using (var ex = NewExtractor(WorkingPath))
                {
                    Hook(ex, r, null);
                    ok = ex.Check();
                }
            });
            return ok;
        }

        public byte[] ReadFile(ArchiveNode node, ProgressReporter r)
        {
            byte[] result = null;
            WithPassword(r, () =>
            {
                using (var ex = NewExtractor(WorkingPath))
                using (var ms = new MemoryStream())
                {
                    ex.ExtractFile(node.Index, ms);
                    result = ms.ToArray();
                }
            });
            return result;
        }

        public void AddFiles(IEnumerable<string> paths, string intoFolder, ProgressReporter r)
        {
            if (!CanModify) throw new InvalidOperationException("Files can only be added to ZIP and 7Z archives.");
            var map = ArchiveCreator.BuildEntryMap(paths, intoFolder);
            if (map.Count == 0) return;

            WithPassword(r, () =>
            {
                var c = new SevenZipCompressor
                {
                    ArchiveFormat = Format == InArchiveFormat.Zip ? OutArchiveFormat.Zip : OutArchiveFormat.SevenZip,
                    CompressionMode = CompressionMode.Append,
                    CompressionLevel = CompressionLevel.Normal,
                    DirectoryStructure = true,
                    IncludeEmptyDirectories = true
                };
                if (Format == InArchiveFormat.Zip && !string.IsNullOrEmpty(Password))
                    c.ZipEncryptionMethod = ZipEncryptionMethod.Aes256;
                ArchiveCreator.Hook(c, r);
                c.CompressFileDictionary(map, SourcePath, Password ?? string.Empty);
            });
        }

        public void Delete(IEnumerable<int> indexes, ProgressReporter r)
        {
            if (!CanModify) throw new InvalidOperationException("Files can only be deleted from ZIP and 7Z archives.");
            var map = indexes.Distinct().ToDictionary(i => i, i => (string)null);
            if (map.Count == 0) return;

            WithPassword(r, () =>
            {
                var c = new SevenZipCompressor
                {
                    ArchiveFormat = Format == InArchiveFormat.Zip ? OutArchiveFormat.Zip : OutArchiveFormat.SevenZip
                };
                c.ModifyArchive(SourcePath, map, Password ?? string.Empty);
            });
        }

        public string Describe()
        {
            var ratio = UnpackedSize > 0 && PackedSize > 0
                ? $" ({Math.Max(0, 100 - (int)(PackedSize * 100.0 / UnpackedSize))}% smaller)"
                : string.Empty;
            var lines = new List<string>
            {
                "File: " + SourcePath,
                "Format: " + FormatName,
                $"Contents: {AllFiles.Count} files, {FolderCount} folders",
                "Original size: " + Paths.FormatSize(UnpackedSize),
                "Archive size: " + Paths.FormatSize(PackedSize) + ratio,
                "Password protected: " + (HasEncrypted || !string.IsNullOrEmpty(Password) ? "Yes" : "No")
            };
            if (IsMultiVolume) lines.Add("Parts: " + VolumeFiles.Count);
            return string.Join(Environment.NewLine, lines);
        }

        // ---------- Helpers ----------

        /// <summary>Runs an action, asking for the password first or again when needed.</summary>
        private void WithPassword(ProgressReporter r, Action action)
        {
            if (HasEncrypted && string.IsNullOrEmpty(Password))
            {
                var first = r.AskPassword(DisplayName + " is password-protected.");
                if (first == null) throw new OperationCanceledException();
                Password = first;
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    action();
                    r.Token.ThrowIfCancellationRequested();
                    return;
                }
                catch (Exception) when (r.Token.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }
                catch (SevenZipException) when (HasEncrypted && attempt < 3)
                {
                    var pw = r.AskPassword("That password didn't work for " + DisplayName + ". Try again.");
                    if (pw == null) throw new OperationCanceledException();
                    Password = pw;
                }
            }
        }

        private SevenZipExtractor NewExtractor(string path) =>
            string.IsNullOrEmpty(Password) ? new SevenZipExtractor(path) : new SevenZipExtractor(path, Password);

        private static void Hook(SevenZipExtractor ex, ProgressReporter r, HashSet<int> skip)
        {
            ex.Extracting += (s, e) => r.Percent(e.PercentDone);
            // The engine raises this before it creates the file or folder, so skipped entries are never written.
            ex.FileExtractionStarted += (s, e) =>
            {
                if (r.Token.IsCancellationRequested) e.Cancel = true;
                else if (skip != null && skip.Contains(e.FileInfo.Index)) e.Skip = true;
            };
        }

        private static bool IsSingleFileWrapper(InArchiveFormat f) =>
            f == InArchiveFormat.GZip || f == InArchiveFormat.BZip2 || f == InArchiveFormat.XZ ||
            f == InArchiveFormat.Lzma || f == InArchiveFormat.Lzw;

        private bool InnerLooksLikeTar(string innerName)
        {
            if (Normalize(innerName).EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) return true;
            var src = SourcePath.ToLowerInvariant();
            return src.EndsWith(".tgz") || src.EndsWith(".tbz") || src.EndsWith(".tbz2") ||
                   src.EndsWith(".txz") || src.EndsWith(".taz") || src.Contains(".tar.");
        }

        private static bool MayHaveEncryptedNames(string path)
        {
            var p = path.ToLowerInvariant();
            return p.EndsWith(".7z") || p.EndsWith(".rar") || p.EndsWith(".001") || p.EndsWith(".zip");
        }

        private static string Label(InArchiveFormat f)
        {
            switch (f)
            {
                case InArchiveFormat.SevenZip: return "7Z";
                case InArchiveFormat.Zip: return "ZIP";
                case InArchiveFormat.Rar: return "RAR";
                case InArchiveFormat.Rar4: return "RAR";
                case InArchiveFormat.Tar: return "TAR";
                case InArchiveFormat.GZip: return "GZ";
                case InArchiveFormat.BZip2: return "BZ2";
                case InArchiveFormat.XZ: return "XZ";
                case InArchiveFormat.Iso: return "ISO";
                case InArchiveFormat.Cab: return "CAB";
                default: return f.ToString().ToUpperInvariant();
            }
        }

        private static string Normalize(string s) => (s ?? string.Empty).Replace('/', '\\').Trim('\\');

        private static string ParentOf(string p)
        {
            var i = p.LastIndexOf('\\');
            return i < 0 ? string.Empty : p.Substring(0, i);
        }

        private static string NameOf(string p)
        {
            var i = p.LastIndexOf('\\');
            return i < 0 ? p : p.Substring(i + 1);
        }

        public void Dispose()
        {
            if (_tempDir != null) Paths.TryDeleteFolder(_tempDir);
            _tempDir = null;
        }
    }
}

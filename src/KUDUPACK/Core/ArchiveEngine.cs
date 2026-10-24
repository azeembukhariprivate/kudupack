using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SevenZip;

namespace Kudupack.Core
{
    /// <summary>What to do with files that already exist where an archive is being extracted.</summary>
    public enum OverwriteChoice { Replace, KeepBoth, Skip, Cancel }

    /// <summary>Files an extraction would replace, for asking the user what to do.</summary>
    public sealed class OverwriteQuestion
    {
        public string Folder { get; set; }

        /// <summary>The existing files' paths relative to <see cref="Folder"/>.</summary>
        public List<string> Names { get; set; }
    }

    /// <summary>Progress, status text and cancellation passed to background work.</summary>
    public sealed class ProgressReporter
    {
        private readonly IProgress<int> _percent;
        private readonly IProgress<string> _status;
        private readonly Func<OverwriteQuestion, OverwriteChoice> _askOverwrite;

        public ProgressReporter(IProgress<int> percent, IProgress<string> status, CancellationToken token, Func<string, string> askPassword,
            Func<OverwriteQuestion, OverwriteChoice> askOverwrite = null)
        {
            _percent = percent;
            _status = status;
            Token = token;
            AskPassword = askPassword;
            _askOverwrite = askOverwrite;
        }

        public CancellationToken Token { get; }

        /// <summary>Asks the user for a password on the UI thread. Returns null if cancelled.</summary>
        public Func<string, string> AskPassword { get; }

        /// <summary>Asks the user on the UI thread what to do with existing files. Without a window to ask, both are kept.</summary>
        public OverwriteChoice AskOverwrite(OverwriteQuestion question) =>
            _askOverwrite == null ? OverwriteChoice.KeepBoth : _askOverwrite(question);

        public void Percent(int value) => _percent?.Report(Math.Max(0, Math.Min(100, value)));
        public void Status(string text) => _status?.Report(text);

        /// <summary>A reporter that maps 0-100 into a slice of this reporter's range.</summary>
        public ProgressReporter Slice(int from, int to)
        {
            var parent = this;
            return new ProgressReporter(
                new Progress<int>(p => parent.Percent(from + (to - from) * p / 100)),
                _status, Token, AskPassword, _askOverwrite);
        }

        public static ProgressReporter None => new ProgressReporter(null, null, CancellationToken.None, _ => null);
    }

    public static class ArchiveEngine
    {
        private static bool _initialized;
        private static readonly object Gate = new object();

        /// <summary>Extensions KUDUPACK can open.</summary>
        public static readonly string[] OpenExtensions =
        {
            ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz", ".tbz2", ".xz", ".txz",
            ".lzma", ".z", ".taz", ".iso", ".cab", ".arj", ".lzh", ".lha", ".wim", ".cpio", ".rpm",
            ".deb", ".001", ".jar", ".apk", ".vhd", ".dmg"
        };

        public static readonly string[] OpenFormatLabels =
        {
            "ZIP", "7Z", "RAR", "TAR", "GZ", "TGZ", "BZ2", "XZ", "ISO", "CAB", "ARJ", "LZH", "WIM", "CPIO", "DEB", "RPM"
        };

        public static readonly string[] CreateFormatLabels =
        {
            "ZIP", "7Z", "TAR", "TAR.GZ", "TAR.BZ2", "TAR.XZ"
        };

        public static string OpenFileFilter
        {
            get
            {
                var patterns = string.Join(";", OpenExtensions.Select(e => "*" + e));
                return $"Archives|{patterns}|All files|*.*";
            }
        }

        public static void Init()
        {
            lock (Gate)
            {
                if (_initialized) return;
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var lib = Path.Combine(baseDir, Environment.Is64BitProcess ? "x64" : "x86", "7z.dll");
                if (!File.Exists(lib)) lib = Path.Combine(baseDir, "7z.dll");
                if (!File.Exists(lib))
                    throw new FileNotFoundException("The archive engine (7z.dll) is missing. Please reinstall KUDUPACK.", lib);
                SevenZipBase.SetLibraryPath(lib);
                _initialized = true;
            }
        }

        public static bool LooksLikeArchive(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            var name = path.ToLowerInvariant();
            return OpenExtensions.Any(ext => name.EndsWith(ext, StringComparison.Ordinal)) || System.Text.RegularExpressions.Regex.IsMatch(name, @"\.(part\d+\.rar|r\d\d|z\d\d)$");
        }
    }
}

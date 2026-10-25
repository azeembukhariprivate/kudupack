using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Kudupack.Core;

namespace Kudupack.Tests
{
    public static class Program
    {
        private static int _failed;
        private static int _passed;
        private static string _root;

        public static int Main()
        {
            _root = Path.Combine(Path.GetTempPath(), "kudupack-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_root);
            try
            {
                foreach (var m in typeof(Program).GetMethods(BindingFlags.NonPublic | BindingFlags.Static).Where(m => m.Name.StartsWith("Test")))
                {
                    if (m.Name.StartsWith("TestWindows") && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        Console.WriteLine("SKIP " + m.Name + " (needs Windows)");
                        continue;
                    }
                    try
                    {
                        m.Invoke(null, null);
                        _passed++;
                        Console.WriteLine("PASS " + m.Name);
                    }
                    catch (TargetInvocationException ex)
                    {
                        _failed++;
                        Console.WriteLine("FAIL " + m.Name + ": " + ex.InnerException);
                    }
                }
            }
            finally
            {
                try { Directory.Delete(_root, true); }
                catch { /* ignore */ }
            }
            Console.WriteLine($"{_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }

        // ================================================================== helpers

        private static void Check(bool condition, string what)
        {
            if (!condition) throw new Exception("Check failed: " + what);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what}: expected {expected}, got {actual}");
        }

        private static string Folder(string name)
        {
            var dir = Path.Combine(_root, name);
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string Sep(string path) => path.Replace('\\', Path.DirectorySeparatorChar);

        /// <summary>Writes a ZIP whose entries have exactly these names (ZipArchive stores them as given).</summary>
        private static string MakeZip(string path, params (string Name, string Text)[] entries)
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (var (name, text) in entries)
                {
                    using (var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false))) w.Write(text);
                }
            }
            return path;
        }

        private static ProgressReporter Answer(OverwriteChoice choice, List<OverwriteQuestion> asked = null) =>
            new ProgressReporter(null, null, CancellationToken.None, _ => null, q =>
            {
                asked?.Add(q);
                return choice;
            });

        // ================================================================== path checks (any OS)

        private static void TestSafeNamesLandInsideTheFolder()
        {
            var dest = Folder("dest");
            Equal(Path.Combine(dest, "a.txt"), Paths.EntryTarget(dest, "a.txt"), "plain name");
            Equal(Path.Combine(dest, Sep("sub\\b.txt")), Paths.EntryTarget(dest, "sub\\b.txt"), "backslash folder");
            Equal(Path.Combine(dest, Sep("sub\\b.txt")), Paths.EntryTarget(dest, "sub/b.txt"), "slash folder");
            Equal(Path.Combine(dest, Sep("sub\\b.txt")), Paths.EntryTarget(dest, "sub\\.\\b.txt"), "dot part");
            Equal(Path.Combine(dest, "..data"), Paths.EntryTarget(dest, "..data"), "name starting with dots");
            Equal(Path.Combine(dest, "notes..txt"), Paths.EntryTarget(dest, "notes..txt"), "double dot inside a name");
            // The engine writes ':' and other invalid characters as '_', so a drive letter becomes a folder name.
            Equal(Path.Combine(dest, Sep("C_\\Windows\\x.dll")), Paths.EntryTarget(dest, "C:\\Windows\\x.dll"), "drive letter");
            Equal(Path.Combine(dest, "a_b_.txt"), Paths.EntryTarget(dest, "a:b?.txt"), "invalid characters");
            Equal(Path.Combine(dest, "console.txt"), Paths.EntryTarget(dest, "console.txt"), "name that only starts like a device");
        }

        private static void TestHarmfulNamesAreRefused()
        {
            var dest = Folder("dest");
            foreach (var name in new[]
                     {
                         "..\\evil.exe", "../evil.exe", "sub\\..\\..\\evil.exe", "sub/../../evil.exe", "..",
                         "\\Windows\\evil.exe", "/etc/evil", "\\\\server\\share\\evil.exe", "//server/share/evil.exe",
                         "...\\evil.exe", ". .\\evil.exe", ".. \\evil.exe", "sub\\..\\..",
                         "CON", "nul.txt", "Lpt1", "sub\\COM3.log", "aux .txt",
                         "", "\\", "."
                     })
            {
                Check(Paths.EntryTarget(dest, name) == null, "refused: \"" + name + "\"");
            }
        }

        private static void TestWarningListsTheRefusedNames()
        {
            var report = new ExtractReport();
            Check(report.UnsafeWarning() == null, "no warning without refused names");
            report.Unsafe.Add("..\\evil.exe");
            Check(report.UnsafeWarning().Contains("..\\evil.exe") && report.UnsafeWarning().StartsWith("1 item wasn't"), "one name");
            for (var i = 0; i < 10; i++) report.Unsafe.Add("..\\x" + i);
            Check(report.UnsafeWarning().StartsWith("11 items weren't") && report.UnsafeWarning().Contains("…and 3 more"), "many names");
        }

        // ================================================================== real archives (Windows)

        private static void TestWindowsHarmfulZipIsExtractedSafely()
        {
            var work = Folder("slip");
            var zip = MakeZip(Path.Combine(work, "evil.zip"),
                ("good.txt", "good"),
                ("sub/fine.txt", "fine"),
                ("../evil1.txt", "evil"),
                ("..\\evil2.txt", "evil"),
                ("sub/../../evil3.txt", "evil"),
                ("CON", "evil"));
            var dest = Path.Combine(work, "out");

            ExtractReport report;
            using (var session = ArchiveSession.Open(zip, ProgressReporter.None))
                report = session.ExtractAll(dest, Answer(OverwriteChoice.Replace));

            Equal("good", File.ReadAllText(Path.Combine(dest, "good.txt")), "good.txt");
            Equal("fine", File.ReadAllText(Path.Combine(dest, "sub", "fine.txt")), "sub\\fine.txt");
            foreach (var evil in new[] { "evil1.txt", "evil2.txt", "evil3.txt" })
                Check(!File.Exists(Path.Combine(work, evil)), evil + " must not be written outside the folder");
            Equal(4, report.Unsafe.Count, "refused entries");
            Equal(2, Directory.GetFiles(work, "*", SearchOption.AllDirectories).Count(f => !f.EndsWith(".zip")), "files written");

            // Extracting only some entries (what "Open" and dragging out use) is checked the same way.
            var some = Path.Combine(work, "some");
            using (var session = ArchiveSession.Open(zip, ProgressReporter.None))
            {
                var all = session.AllFiles.Select(f => f.Index).ToList();
                report = session.ExtractIndexes(some, all, Answer(OverwriteChoice.Replace));
            }
            Equal(4, report.Unsafe.Count, "refused entries (selection)");
            Check(File.Exists(Path.Combine(some, "good.txt")), "selection: good.txt");
            Check(!File.Exists(Path.Combine(work, "evil1.txt")), "selection: nothing outside");
        }

        private static void TestWindowsDownloadedMarkIsKept()
        {
            const string mark = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.com/a.zip\r\n";
            var work = Folder("zone");
            var zip = MakeZip(Path.Combine(work, "downloaded.zip"), ("a.txt", "a"), ("sub/b.exe", "b"));
            ZoneMark.Apply(new[] { zip }, mark);
            Check(ZoneMark.Read(zip)?.Contains("ZoneId=3") == true, "test archive has the mark");

            var dest = Path.Combine(work, "out");
            using (var s = ArchiveSession.Open(zip, ProgressReporter.None)) s.ExtractAll(dest, Answer(OverwriteChoice.Replace));
            Equal(mark, ZoneMark.Read(Path.Combine(dest, "a.txt")), "a.txt keeps the mark");
            Equal(mark, ZoneMark.Read(Path.Combine(dest, "sub", "b.exe")), "sub\\b.exe keeps the mark");

            // "Keep both" copies are marked too.
            using (var s = ArchiveSession.Open(zip, ProgressReporter.None)) s.ExtractAll(dest, Answer(OverwriteChoice.KeepBoth));
            Equal(mark, ZoneMark.Read(Path.Combine(dest, "a (2).txt")), "a (2).txt keeps the mark");

            // An archive without the mark gives files without it.
            var local = MakeZip(Path.Combine(work, "local.zip"), ("c.txt", "c"));
            using (var s = ArchiveSession.Open(local, ProgressReporter.None)) s.ExtractAll(dest, Answer(OverwriteChoice.Replace));
            Check(ZoneMark.Read(Path.Combine(dest, "c.txt")) == null, "c.txt has no mark");
        }

        private static void TestWindowsExistingFilesFollowTheChoice()
        {
            var work = Folder("clash");
            var zip = MakeZip(Path.Combine(work, "a.zip"), ("report.txt", "from archive"), ("new.txt", "new"), ("sub/deep.txt", "deep"));
            var dest = Folder(Path.Combine("clash", "out"));
            Directory.CreateDirectory(Path.Combine(dest, "sub"));
            File.WriteAllText(Path.Combine(dest, "report.txt"), "mine");
            File.WriteAllText(Path.Combine(dest, "sub", "deep.txt"), "mine too");

            void Reset()
            {
                File.Delete(Path.Combine(dest, "new.txt"));
                foreach (var f in Directory.GetFiles(dest, "* (2).txt", SearchOption.AllDirectories)) File.Delete(f);
                File.WriteAllText(Path.Combine(dest, "report.txt"), "mine");
                File.WriteAllText(Path.Combine(dest, "sub", "deep.txt"), "mine too");
            }

            // Skip: the existing files stay as they are, the new one is extracted.
            var asked = new List<OverwriteQuestion>();
            using (var s = ArchiveSession.Open(zip, ProgressReporter.None))
            {
                var r = s.ExtractAll(dest, Answer(OverwriteChoice.Skip, asked));
                Equal(2, r.Skipped, "skipped");
            }
            Equal(1, asked.Count, "asked once");
            Equal(2, asked[0].Names.Count, "files in the question");
            Check(asked[0].Names.Contains("report.txt") && asked[0].Names.Contains(Path.Combine("sub", "deep.txt")), "names relative to the folder");
            Equal("mine", File.ReadAllText(Path.Combine(dest, "report.txt")), "skip keeps report.txt");
            Equal("mine too", File.ReadAllText(Path.Combine(dest, "sub", "deep.txt")), "skip keeps sub\\deep.txt");
            Equal("new", File.ReadAllText(Path.Combine(dest, "new.txt")), "skip still extracts new.txt");

            // Keep both: the archive's copies arrive with a number added.
            Reset();
            using (var s = ArchiveSession.Open(zip, ProgressReporter.None))
                Equal(2, s.ExtractAll(dest, Answer(OverwriteChoice.KeepBoth)).KeptBoth, "kept both");
            Equal("mine", File.ReadAllText(Path.Combine(dest, "report.txt")), "keep both keeps report.txt");
            Equal("from archive", File.ReadAllText(Path.Combine(dest, "report (2).txt")), "keep both adds report (2).txt");
            Equal("deep", File.ReadAllText(Path.Combine(dest, "sub", "deep (2).txt")), "keep both adds sub\\deep (2).txt");
            Equal("new", File.ReadAllText(Path.Combine(dest, "new.txt")), "keep both extracts new.txt");

            // Replace: the archive's copies win.
            Reset();
            using (var s = ArchiveSession.Open(zip, ProgressReporter.None))
                Equal(2, s.ExtractAll(dest, Answer(OverwriteChoice.Replace)).Replaced, "replaced");
            Equal("from archive", File.ReadAllText(Path.Combine(dest, "report.txt")), "replace overwrites report.txt");
            Equal("deep", File.ReadAllText(Path.Combine(dest, "sub", "deep.txt")), "replace overwrites sub\\deep.txt");

            // Cancel: nothing is written.
            Reset();
            var cancelled = false;
            using (var s = ArchiveSession.Open(zip, ProgressReporter.None))
            {
                try { s.ExtractAll(dest, Answer(OverwriteChoice.Cancel)); }
                catch (OperationCanceledException) { cancelled = true; }
            }
            Check(cancelled, "cancel stops the extraction");
            Check(!File.Exists(Path.Combine(dest, "new.txt")), "cancel writes nothing");
            Equal("mine", File.ReadAllText(Path.Combine(dest, "report.txt")), "cancel keeps report.txt");

            // A new folder: nothing to ask.
            asked.Clear();
            using (var s = ArchiveSession.Open(zip, ProgressReporter.None))
                s.ExtractAll(Path.Combine(work, "fresh"), Answer(OverwriteChoice.Cancel, asked));
            Equal(0, asked.Count, "no question for a new folder");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Kudupack.Core;
using Microsoft.VisualBasic.FileIO;

namespace Kudupack.Views
{
    /// <summary>Extraction flows shared by the main window and the Explorer right-click menu.</summary>
    public static class Operations
    {
        /// <summary>
        /// Extracts one or more archives. With intoOwnFolder each archive goes into a new folder
        /// named after it; otherwise files go straight next to the archive.
        /// </summary>
        public static void ExtractArchives(Window owner, IList<string> archives, bool intoOwnFolder)
        {
            if (archives == null || archives.Count == 0) return;

            var done = new List<(string Archive, string Destination, bool MultiVolume)>();
            var failures = new List<string>();
            var title = archives.Count == 1 ? "Extracting " + Path.GetFileName(archives[0]) : $"Extracting {archives.Count} archives";

            var result = ProgressWindow.Run(owner, title, "Starting…", r =>
            {
                for (var i = 0; i < archives.Count; i++)
                {
                    r.Token.ThrowIfCancellationRequested();
                    var archive = archives[i];
                    var slice = r.Slice(i * 100 / archives.Count, (i + 1) * 100 / archives.Count);
                    try
                    {
                        using (var session = ArchiveSession.Open(archive, slice))
                        {
                            var parent = Path.GetDirectoryName(archive) ?? Paths.Desktop;
                            var destination = intoOwnFolder
                                ? Paths.UniqueFolder(parent, Paths.ArchiveBaseName(archive))
                                : parent;
                            r.Status($"Extracting {Path.GetFileName(archive)} ({i + 1} of {archives.Count})");
                            session.ExtractAll(destination, slice);
                            done.Add((archive, destination, session.IsMultiVolume));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (archives.Count == 1) throw;
                        failures.Add(Path.GetFileName(archive) + ": " + Dialogs.Describe(ex));
                    }
                }
            });

            if (result.Outcome == OperationOutcome.Failed)
            {
                Dialogs.Error(owner, Dialogs.Describe(result.Error));
                return;
            }

            AfterExtract(owner, done.Select(d => (d.Archive, d.Destination, d.MultiVolume)).ToList());

            if (failures.Count > 0)
            {
                Dialogs.Error(owner, $"{done.Count} extracted, {failures.Count} couldn't be extracted:\n\n" +
                                     string.Join("\n", failures));
            }
        }

        /// <summary>Opens the result folder and, if the user turned it on, recycles the archive.</summary>
        public static void AfterExtract(Window owner, IList<(string Archive, string Destination, bool MultiVolume)> done)
        {
            if (done.Count == 0) return;

            if (Settings.OpenFolderAfterExtract)
            {
                foreach (var folder in done.Select(d => d.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Take(5))
                    OpenFolder(folder);
            }

            if (!Settings.DeleteAfterExtract) return;

            foreach (var item in done)
            {
                if (item.MultiVolume) continue; // Never remove split archives part by part.
                RecycleFile(item.Archive);
                Settings.RemoveRecent(item.Archive);
            }
        }

        /// <summary>Moves a file to the Recycle Bin, so it can be restored.</summary>
        public static bool RecycleFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void OpenFolder(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
            catch { }
        }

        public static void ShowInFolder(string file)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
            }
            catch { }
        }
    }
}

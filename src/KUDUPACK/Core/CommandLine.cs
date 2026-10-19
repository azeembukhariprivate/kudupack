using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;

namespace Kudupack.Core
{
    /// <summary>
    /// KUDUPACK.exe [archive]                 open an archive
    /// KUDUPACK.exe --extract-here  "file"    extract next to the archive
    /// KUDUPACK.exe --extract-folder "file"   extract into a new folder named after the archive
    /// KUDUPACK.exe --add "file or folder"    open the Create archive window
    /// KUDUPACK.exe --register-shell | --register-assoc | --unregister
    /// </summary>
    public sealed class CommandLine
    {
        public HashSet<string> Flags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<string> Paths { get; } = new List<string>();

        public bool Has(string flag) => Flags.Contains(flag);

        public bool IsSetupOnly =>
            Paths.Count == 0 && Flags.Count > 0 &&
            Flags.All(f => f == "register-shell" || f == "register-assoc" || f == "unregister");

        public static CommandLine Parse(string[] args)
        {
            var cmd = new CommandLine();
            foreach (var arg in args ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(arg)) continue;
                if (arg.StartsWith("--"))
                {
                    cmd.Flags.Add(arg.Substring(2).Trim());
                    continue;
                }

                try { cmd.Paths.Add(Path.GetFullPath(arg.Trim('"'))); }
                catch { /* ignore invalid paths */ }
            }
            return cmd;
        }
    }

    /// <summary>
    /// When several files are selected in Explorer and "Add to archive" is chosen, Windows starts
    /// KUDUPACK once per file. The first copy collects the others' paths so one window opens.
    /// </summary>
    public static class AddCollector
    {
        private static string UserKey => System.Text.RegularExpressions.Regex.Replace(Environment.UserName ?? "user", "[^A-Za-z0-9]", "_");
        private static string PipeName => "KUDUPACK.Add." + UserKey;
        private static string MutexName => @"Local\KUDUPACK.Add." + UserKey;

        /// <summary>Returns all collected paths, or null if they were handed to an already-running copy.</summary>
        public static List<string> Collect(List<string> initial)
        {
            Mutex mutex;
            bool isFirst;
            try
            {
                mutex = new Mutex(true, MutexName, out isFirst);
            }
            catch
            {
                return initial;
            }

            if (!isFirst)
            {
                mutex.Dispose();
                try
                {
                    using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                    {
                        client.Connect(2500);
                        using (var writer = new StreamWriter(client))
                        {
                            foreach (var p in initial) writer.WriteLine(p);
                        }
                    }
                    return null;
                }
                catch
                {
                    return initial; // The first copy has already opened its window; open our own.
                }
            }

            var all = new List<string>(initial);
            try
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(700);
                while (true)
                {
                    var wait = deadline - DateTime.UtcNow;
                    if (wait <= TimeSpan.Zero) break;

                    using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                               PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                    {
                        var pending = server.BeginWaitForConnection(null, null);
                        if (!pending.AsyncWaitHandle.WaitOne(wait)) break;
                        server.EndWaitForConnection(pending);

                        using (var reader = new StreamReader(server))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                if (line.Length > 0) all.Add(line);
                            }
                        }
                    }

                    deadline = DateTime.UtcNow.AddMilliseconds(500);
                }
            }
            catch
            {
                // Collect what we have.
            }
            finally
            {
                try { mutex.ReleaseMutex(); } catch { }
                mutex.Dispose();
            }

            return all.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}

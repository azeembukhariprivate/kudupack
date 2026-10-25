using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Kudupack.Core
{
    /// <summary>
    /// The "downloaded from the internet" mark (the Zone.Identifier stream Windows adds to downloads). Copying it from an
    /// archive to the files extracted from it keeps SmartScreen and Office's Protected View warning about them, like
    /// File Explorer and 7-Zip do.
    /// </summary>
    public static class ZoneMark
    {
        private const string Stream = ":Zone.Identifier";

        /// <summary>The mark's text, or null when the file has none (or the drive can't hold one, like FAT).</summary>
        public static string Read(string path)
        {
            try
            {
                using (var handle = Open(path + Stream, GenericRead, OpenExisting))
                {
                    if (handle == null) return null;
                    using (var fs = new FileStream(handle, FileAccess.Read))
                    using (var reader = new StreamReader(fs, Encoding.Default, true))
                    {
                        var text = reader.ReadToEnd();
                        return text.IndexOf("ZoneId", StringComparison.OrdinalIgnoreCase) >= 0 ? text : null;
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Gives each file the mark; files that can't take it (read-only, FAT drives) are left as they are.</summary>
        public static void Apply(IEnumerable<string> files, string mark)
        {
            if (string.IsNullOrEmpty(mark)) return;
            var bytes = Encoding.ASCII.GetBytes(mark);
            foreach (var file in files)
            {
                try
                {
                    if (!File.Exists(file)) continue;
                    using (var handle = Open(file + Stream, GenericWrite, CreateAlways))
                    {
                        if (handle == null) continue;
                        using (var fs = new FileStream(handle, FileAccess.Write)) fs.Write(bytes, 0, bytes.Length);
                    }
                }
                catch (Exception)
                {
                    // Best effort, like File Explorer.
                }
            }
        }

        // .NET Framework refuses "file:stream" paths, so the stream is opened directly.
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint CreateAlways = 2;
        private const uint OpenExisting = 3;

        private static SafeFileHandle Open(string path, uint access, uint disposition)
        {
            var handle = CreateFile(path, access, FileShare.Read, IntPtr.Zero, disposition, 0x80, IntPtr.Zero);
            if (!handle.IsInvalid) return handle;
            handle.Dispose();
            return null;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, FileShare share, IntPtr security,
            uint disposition, uint flags, IntPtr template);
    }
}

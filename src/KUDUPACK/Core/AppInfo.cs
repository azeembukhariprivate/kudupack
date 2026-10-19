using System;
using System.Diagnostics;
using System.Reflection;

namespace Kudupack.Core
{
    public static class AppInfo
    {
        public const string Name = "KUDUPACK";
        public const string Author = "Azeem";
        public const string RepoOwner = "azeembukhariprivate";
        public const string RepoName = "KUDUPACK";

        public static string RepoUrl => $"https://github.com/{RepoOwner}/{RepoName}";
        public static string ReleasesUrl => RepoUrl + "/releases";

        public static Version Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            }
        }

        public static string VersionText => Version.ToString(3);

        public static string ExePath
        {
            get
            {
                try { return Process.GetCurrentProcess().MainModule.FileName; }
                catch { return Assembly.GetExecutingAssembly().Location; }
            }
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* no browser available */ }
        }
    }
}

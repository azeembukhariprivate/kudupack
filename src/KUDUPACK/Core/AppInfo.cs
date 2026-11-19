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

        /// <summary>Opens a web page in the default browser. Only http and https links are opened, never files or programs.</summary>
        public static void OpenUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return;
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch { /* no browser available */ }
        }
    }
}

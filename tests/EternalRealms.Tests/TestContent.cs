using System;
using System.IO;
using EternalRealms.Shared.Content;

namespace EternalRealms.Tests
{
    /// <summary>Lädt den echten Content einmal pro Testlauf.</summary>
    public static class TestContent
    {
        private static readonly Lazy<ContentDatabase> Lazy = new(() => ContentDatabase.LoadFromDirectory(ContentRoot));

        public static ContentDatabase Db => Lazy.Value;

        public static string ContentRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "content"))) dir = dir.Parent;
                if (dir == null) throw new DirectoryNotFoundException("content-Verzeichnis nicht gefunden");
                return Path.Combine(dir.FullName, "content");
            }
        }
    }
}

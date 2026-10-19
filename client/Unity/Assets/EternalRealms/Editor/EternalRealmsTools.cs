using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EternalRealms.Client.Editor
{
    /// <summary>Editor-Menü: baut die gemeinsame Bibliothek und kopiert sie samt Content ins Unity-Projekt.</summary>
    public static class EternalRealmsTools
    {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        [MenuItem("Eternal Realms/Shared-DLL und Content kopieren")]
        public static void CopySharedAndContent()
        {
            var repo = RepoRoot;
            var outDir = Path.Combine(repo, "build", "unity-plugins");
            var psi = new ProcessStartInfo("dotnet", $"publish \"{Path.Combine(repo, "src", "EternalRealms.Shared")}\" -c Release -o \"{outDir}\"")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            using (var p = Process.Start(psi))
            {
                string log = p!.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0) { UnityEngine.Debug.LogError("dotnet publish fehlgeschlagen:\n" + log); return; }
            }
            var plugins = Path.Combine(Application.dataPath, "Plugins", "EternalRealms");
            Directory.CreateDirectory(plugins);
            // Diese Bibliotheken bringt Unitys .NET-Standard-2.1-Profil bereits mit; doppelt führen sie zu Konflikten.
            var skip = new[] { "System.Buffers.dll", "System.Memory.dll", "System.Numerics.Vectors.dll", "System.Threading.Tasks.Extensions.dll" };
            foreach (var dll in Directory.GetFiles(outDir, "*.dll"))
                if (System.Array.IndexOf(skip, Path.GetFileName(dll)) < 0) File.Copy(dll, Path.Combine(plugins, Path.GetFileName(dll)), true);
            var contentTarget = Path.Combine(Application.streamingAssetsPath, "content");
            if (Directory.Exists(contentTarget)) Directory.Delete(contentTarget, true);
            CopyDir(Path.Combine(repo, "content"), contentTarget);
            AssetDatabase.Refresh();
            UnityEngine.Debug.Log("Eternal Realms: Bibliotheken und Content aktualisiert.");
        }

        private static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }
    }
}

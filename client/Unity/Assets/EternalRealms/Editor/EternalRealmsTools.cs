using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EternalRealms.Client.Editor
{
    /// <summary>
    /// Editor-Menü „Eternal Realms“: gemeinsame Bibliothek und Content ins Projekt kopieren und Alpha-Builds erstellen.
    /// Bewusst ohne Verweis auf die Client-Skripte: Das Menü funktioniert auch dann, wenn diese noch nicht kompilieren
    /// (frisch geklont, DLLs fehlen noch).
    /// </summary>
    public static class EternalRealmsTools
    {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
        private const string ScenePath = "Assets/EternalRealms/Scenes/Alpha.unity";
        private const string ServerPref = "EternalRealms.AlphaServer";

        [MenuItem("Eternal Realms/Shared-DLL und Content kopieren", priority = 1)]
        public static void CopySharedAndContentMenu() => CopySharedAndContent();

        /// <summary>Baut EternalRealms.Shared (dotnet publish) und kopiert DLLs und Content. Rückgabe: erfolgreich.</summary>
        public static bool CopySharedAndContent()
        {
            var repo = RepoRoot;
            var outDir = Path.Combine(repo, "build", "unity-plugins");
            var psi = new ProcessStartInfo("dotnet", $"publish \"{Path.Combine(repo, "src", "EternalRealms.Shared")}\" -c Release -o \"{outDir}\"")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    string log = p!.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0) { UnityEngine.Debug.LogError("dotnet publish fehlgeschlagen:\n" + log); return false; }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("„dotnet“ nicht gefunden – bitte das .NET-8-SDK installieren oder tools/build-unity-plugins.ps1 bzw. .sh ausführen. " + ex.Message);
                return false;
            }
            var plugins = Path.Combine(Application.dataPath, "Plugins", "EternalRealms");
            Directory.CreateDirectory(plugins);
            // Diese Bibliotheken bringt Unitys .NET-Standard-2.1-Profil bereits mit; doppelt führen sie zu Konflikten.
            var skip = new[] { "System.Buffers.dll", "System.Memory.dll", "System.Numerics.Vectors.dll", "System.Threading.Tasks.Extensions.dll" };
            foreach (var dll in Directory.GetFiles(outDir, "*.dll"))
                if (Array.IndexOf(skip, Path.GetFileName(dll)) < 0) File.Copy(dll, Path.Combine(plugins, Path.GetFileName(dll)), true);
            CopyContent();
            AssetDatabase.Refresh();
            UnityEngine.Debug.Log("Eternal Realms: Bibliotheken und Content aktualisiert.");
            return true;
        }

        private static void CopyContent()
        {
            var contentTarget = Path.Combine(Application.streamingAssetsPath, "content");
            if (Directory.Exists(contentTarget)) Directory.Delete(contentTarget, true);
            CopyDir(Path.Combine(RepoRoot, "content"), contentTarget);
        }

        private static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }

        // ================================================================== Alpha-Builds

        [MenuItem("Eternal Realms/Alpha-Build/Serveradresse festlegen …", priority = 20)]
        public static void SetServer() => ServerWindow.Open();

        [MenuItem("Eternal Realms/Alpha-Build/Windows (64 Bit)", priority = 31)]
        public static void BuildWindowsMenu() => Build(BuildTarget.StandaloneWindows64, EditorPrefs.GetString(ServerPref, ""), null, copy: true);

        [MenuItem("Eternal Realms/Alpha-Build/Linux (64 Bit)", priority = 32)]
        public static void BuildLinuxMenu() => Build(BuildTarget.StandaloneLinux64, EditorPrefs.GetString(ServerPref, ""), null, copy: true);

        [MenuItem("Eternal Realms/Alpha-Build/macOS", priority = 33)]
        public static void BuildMacMenu() => Build(BuildTarget.StandaloneOSX, EditorPrefs.GetString(ServerPref, ""), null, copy: true);

        /// <summary>
        /// Für die Kommandozeile und die CI:
        /// Unity -batchmode -quit -projectPath client/Unity -executeMethod EternalRealms.Client.Editor.EternalRealmsTools.BuildFromCommandLine
        ///       -erTarget Windows|Linux|Mac  [-erServer host:port]  [-erOut Ordner]  [-erSkipCopy]
        /// Mit -erSkipCopy müssen DLLs und Content vorher per tools/build-unity-plugins.sh kopiert worden sein.
        /// </summary>
        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : ""; }
            var target = Arg("-erTarget").ToLowerInvariant() switch
            {
                "linux" => BuildTarget.StandaloneLinux64,
                "mac" or "macos" => BuildTarget.StandaloneOSX,
                _ => BuildTarget.StandaloneWindows64,
            };
            string outDir = Arg("-erOut");
            bool ok = Build(target, Arg("-erServer"), outDir.Length > 0 ? outDir : null, copy: Array.IndexOf(args, "-erSkipCopy") < 0);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Bereitet das Projekt vor (Szene, Spielereinstellungen, Serveradresse) und baut den Client.</summary>
        public static bool Build(BuildTarget target, string server, string? outDir, bool copy)
        {
            if (copy && !CopySharedAndContent()) return false;
            if (!copy) CopyContent();
            if (!File.Exists(Path.Combine(Application.dataPath, "Plugins", "EternalRealms", "EternalRealms.Shared.dll")))
            {
                UnityEngine.Debug.LogError("EternalRealms.Shared.dll fehlt – erst „Shared-DLL und Content kopieren“ ausführen.");
                return false;
            }
            EnsureScene();
            var (version, stage) = ReadManifest();
            PlayerSettings.companyName = "Eternal Realms";
            PlayerSettings.productName = "Eternal Realms";
            PlayerSettings.bundleVersion = stage.Length > 0 ? $"{version}-{stage.ToLowerInvariant()}" : version;
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            SetInputHandlingBoth();
            WriteServerFile(server);

            string folder = target switch { BuildTarget.StandaloneLinux64 => "linux", BuildTarget.StandaloneOSX => "macos", _ => "windows" };
            outDir ??= Path.Combine(RepoRoot, "build", "client", $"{version}-{folder}");
            string exe = target switch
            {
                BuildTarget.StandaloneLinux64 => "EternalRealms.x86_64",
                BuildTarget.StandaloneOSX => "EternalRealms.app",
                _ => "EternalRealms.exe",
            };
            var options = new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = Path.Combine(outDir, exe), target = target, options = BuildOptions.None };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                UnityEngine.Debug.LogError($"Alpha-Build fehlgeschlagen: {report.summary.result}, {report.summary.totalErrors} Fehler. " +
                                           "Fehlt das Build-Modul für diese Plattform? (Unity Hub → Installs → Add modules)");
                return false;
            }
            File.WriteAllText(Path.Combine(outDir, "LIESMICH.txt"),
                $"Eternal Realms {stage} {version} – Testversion\n\n" +
                $"Server: {(server.Length > 0 ? server : "beim Anmelden eintragen")}\n" +
                "Konto anlegen: Kontoname, Passwort (mind. 8 Zeichen, Buchstaben und Ziffern) und Einladungscode eintragen, dann „Konto erstellen“.\n" +
                "Fehler melden: im Chat /bug Text – oder mit Kategorie: /bug balance: Text\n" +
                "Absturzprotokoll fehler.log – Windows: %USERPROFILE%\\AppData\\LocalLow\\Eternal Realms\\Eternal Realms, Linux: ~/.config/unity3d/Eternal Realms/Eternal Realms\n");
            UnityEngine.Debug.Log($"Alpha-Build fertig: {outDir} ({report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime.TotalSeconds:0} s)");
            if (!Application.isBatchMode) EditorUtility.RevealInFinder(Path.Combine(outDir, exe));
            return true;
        }

        /// <summary>Leere Startszene: Der Client baut sich beim Start selbst auf (Bootstrap).</summary>
        private static void EnsureScene()
        {
            var full = Path.Combine(RepoRoot, "client", "Unity", ScenePath);
            if (!File.Exists(full))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static (string version, string stage) ReadManifest()
        {
            var json = File.ReadAllText(Path.Combine(RepoRoot, "content", "manifest.json"));
            string Field(string name) { var m = Regex.Match(json, $"\"{name}\"\\s*:\\s*\"([^\"]*)\""); return m.Success ? m.Groups[1].Value : ""; }
            var version = Field("version");
            return (version.Length > 0 ? version : "0.0.0", Field("stage"));
        }

        /// <summary>Der Client nutzt das klassische Input-System; „Both“ verhindert Ausnahmen, wenn das neue Paket aktiv ist.</summary>
        private static void SetInputHandlingBoth()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (settings == null) return;
            var so = new SerializedObject(settings);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null || prop.intValue == 2) return;
            prop.intValue = 2; // 0 = alt, 1 = neu, 2 = beide
            so.ApplyModifiedPropertiesWithoutUndo();
            UnityEngine.Debug.Log("Eingabe auf „Both“ gestellt (wirkt im Editor nach einem Neustart, im Build sofort).");
        }

        /// <summary>StreamingAssets/server.txt: voreingestellte Serveradresse des Builds (leer = keine).</summary>
        private static void WriteServerFile(string server)
        {
            var file = Path.Combine(Application.streamingAssetsPath, "server.txt");
            if (server.Trim().Length == 0) { if (File.Exists(file)) File.Delete(file); return; }
            File.WriteAllText(file, server.Trim() + "\n");
        }

        private sealed class ServerWindow : EditorWindow
        {
            private string _server = "";

            public static void Open()
            {
                var w = GetWindow<ServerWindow>(true, "Alpha-Server", true);
                w._server = EditorPrefs.GetString(ServerPref, "");
                w.minSize = new Vector2(380, 110);
            }

            private void OnGUI()
            {
                EditorGUILayout.LabelField("Adresse des Alpha-Servers, die der Build voreinstellt:", EditorStyles.wordWrappedLabel);
                _server = EditorGUILayout.TextField("Server (host:port)", _server);
                EditorGUILayout.HelpBox("Leer lassen, wenn Tester die Adresse selbst eintragen sollen. Standardport 7777.", MessageType.None);
                if (GUILayout.Button("Speichern")) { EditorPrefs.SetString(ServerPref, _server.Trim()); Close(); }
            }
        }
    }
}

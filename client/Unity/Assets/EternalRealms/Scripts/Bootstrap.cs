using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>
    /// Baut den Client in jeder (auch leeren) Szene auf: Netzwerk, Welt, Kamera, Steuerung, Oberfläche.
    /// So ist das Projekt ohne vorbereitete Szenen oder Prefabs sofort spielbar.
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (Object.FindFirstObjectByType<NetworkManager>() != null) return;
            var root = new GameObject("EternalRealms");
            Object.DontDestroyOnLoad(root);
            root.AddComponent<NetworkManager>();
            root.AddComponent<WorldRenderer>();
            root.AddComponent<PlayerController>();
            root.AddComponent<GameUI>();
            root.AddComponent<AudioManager>();
            root.AddComponent<GraphicsApplier>();
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            cam.farClipPlane = 600;
            cam.clearFlags = CameraClearFlags.SolidColor;
            if (cam.GetComponent<ThirdPersonCamera>() == null) cam.gameObject.AddComponent<ThirdPersonCamera>();
            Application.targetFrameRate = 60; // danach bestimmt GraphicsApplier die Bildrate
            Application.runInBackground = true;
            ErrorLog.Install();
        }
    }

    /// <summary>
    /// Schreibt Fehler und Ausnahmen in „fehler.log“ (Application.persistentDataPath), damit Alpha-Tester sie einer
    /// /bug-Meldung beilegen können. Die Datei wird beim Start ab 1 MB neu begonnen.
    /// </summary>
    public static class ErrorLog
    {
        public static string FilePath => System.IO.Path.Combine(Application.persistentDataPath, "fehler.log");

        public static void Install()
        {
            try
            {
                var info = new System.IO.FileInfo(FilePath);
                if (info.Exists && info.Length > 1_000_000) info.Delete();
            }
            catch (System.Exception) { }
            Application.logMessageReceived += (text, stack, type) =>
            {
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                try { System.IO.File.AppendAllText(FilePath, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} [{type}] {text}\n{stack}\n"); }
                catch (System.Exception) { }
            };
        }
    }
}

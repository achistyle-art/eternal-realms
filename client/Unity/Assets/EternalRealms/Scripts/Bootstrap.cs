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
            Application.targetFrameRate = 120;
            Application.runInBackground = true;
        }
    }
}

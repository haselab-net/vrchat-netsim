// Chooses which client's copy of the world the Game view shows.
//
// Every client's copy is a loaded scene at the same position, so without this the Game view draws all copies on top of
// each other. Each client's scene gets its own scene culling mask bit (editor only, EditorSceneManager.SetSceneCullingMask)
// and the cameras of the local player (C0's scene and DontDestroyOnLoad) only draw the chosen client's bit. The Scene view
// still draws every copy. Input in the Game view always acts as C0 (the ClientSim local player).
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Haselab.NetSim
{
    [InitializeOnLoad]
    public static class NetSimView
    {
        /// <summary>Index of the client shown in the Game view, or -1 for all clients overlaid.</summary>
        public static int Shown { get; private set; } = -1;
        const int FirstBit = 8;   // well below Unity's own bits (the top ones)
        const int MaxClients = 40;

        static NetSimView() { EditorApplication.update += Apply; }

        static ulong Bit(int clientIndex) => 1UL << (FirstBit + Mathf.Min(clientIndex, MaxClients));

        /// <summary>Shows one client's copy (index into NetSim.Clients), or all copies overlaid (-1).</summary>
        public static void Show(int clientIndex) { Shown = clientIndex; Apply(); }

        public static void OnLeave(NetSimClient c) { if (Shown == c.index) Show(0); }

        /// <summary>Restores Unity's default culling (called when NetSim stops).</summary>
        public static void Reset()
        {
            Shown = -1;
            foreach (var c in NetSim.Clients)
                if (c.scene.IsValid() && c.scene.isLoaded) EditorSceneManager.SetSceneCullingMask(c.scene, SceneCullingMasks.DefaultSceneCullingMask);
            foreach (var cam in Camera.allCameras) cam.overrideSceneCullingMask = 0;
        }

        static void Apply()
        {
            if (!EditorApplication.isPlaying || !NetSim.Active || NetSim.Clients.Count == 0) return;
            ulong all = 0;
            foreach (var c in NetSim.Clients)
            {
                if (!c.scene.IsValid() || !c.scene.isLoaded) continue;
                // keep the Scene view showing everything; the game view bit is replaced by the client's own bit
                ulong mask = Bit(c.index) | SceneCullingMasks.MainStageSceneViewObjects;
                if (EditorSceneManager.GetSceneCullingMask(c.scene) != mask) EditorSceneManager.SetSceneCullingMask(c.scene, mask);
                all |= Bit(c.index);
            }
            // GameViewObjects keeps DontDestroyOnLoad and other scenes (ClientSim's player, UI) visible
            ulong cameraMask = SceneCullingMasks.GameViewObjects | (Shown < 0 ? all : Bit(Shown));
            var copies = NetSim.Clients.Where(c => !c.isOriginal).Select(c => c.scene).ToList();
            foreach (var cam in Camera.allCameras)
            {
                if (copies.Contains(cam.gameObject.scene)) continue;   // cameras of copies are disabled anyway
                if (cam.overrideSceneCullingMask != cameraMask) cam.overrideSceneCullingMask = cameraMask;
            }
        }

        /// <summary>The client whose scene contains `go` (null when it is not in a client's scene).</summary>
        public static NetSimClient ClientOfScene(Scene s) => NetSim.Clients.FirstOrDefault(c => c.scene == s);
    }
}
#endif

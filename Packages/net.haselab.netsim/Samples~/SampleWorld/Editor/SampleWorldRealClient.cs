// Real VRChat client test of the sample world: builds SampleWorld.unity with SampleWorldBot and starts 3 clients.
//
//   Menu: Tools > NetSim > Sample World > Build & Test with Bot (3 clients)
//   then: python <package>/Tools~/analyze_logs.py --since "<start time>" --watch --must-match good,synced,goals
//
// Needs Steam running, VRChat installed and the VRChat SDK logged in. See the sample's README.
#if UNITY_EDITOR
using Haselab.NetSim.RealClient;
using UnityEditor;
using UnityEngine;

namespace Haselab.NetSim.SampleWorld
{
    public static class SampleWorldRealClient
    {
        const string BotPrefabGuid = "6b0e5f1c2a8d4e7fb3c19d0a54e2f871";

        [MenuItem("Tools/NetSim/Sample World/Build & Test with Bot (3 clients)")]
        public static void BuildAndTest3() => BuildAndTest(3);

        public static void BuildAndTest(int clients, string config = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(BotPrefabGuid));
            if (prefab == null) { Debug.LogError("[NetSim] SampleWorldBot.prefab not found"); return; }
            if (!UnityEngine.SceneManagement.SceneManager.GetActiveScene().path.EndsWith("SampleWorld.unity"))
            {
                Debug.LogError("[NetSim] open SampleWorld.unity first");
                return;
            }
            RealClientTest.BuildAndTest(prefab, clients, config);
        }
    }
}
#endif

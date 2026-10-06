// Multi-client test in the real VRChat client with a debug bot (local test builds only).
//
//   Haselab.NetSim.RealClient.RealClientTest.BuildAndTest(botPrefab, clients: 3, config: "actionProbability=0.5");
//
// Adds an instance of the bot prefab to the open scene, runs the VRChat SDK "Build & Test" with N clients and removes
// the bot again. The SDK saves the open scene before building, so the scene file is backed up first and restored (and
// the scene reopened) afterwards: unsaved changes in the open scene are discarded. A build callback refuses every other
// build while a bot is in the scene, so a bot can never end up in an upload.
//
// The bot is an ordinary Udon / UdonSharp behaviour that every client runs as its local player and that writes its
// actions and the world state to the VRChat log (see Documentation~/README.md, "Testing in the real VRChat client").
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharpEditor;
using VRC.SDK3.Editor;
using VRC.SDKBase.Editor;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;

namespace Haselab.NetSim.RealClient
{
    [InitializeOnLoad]
    public static class RealClientTest
    {
        /// <summary>Name prefix of bot instances in the scene; the build guard looks for it.</summary>
        public const string BotPrefix = "[NetSimBot] ";
        public static bool InTestBuild { get; private set; }
        public static string LastStatus = "";
        public static string LogPath => Path.GetFullPath("Logs/NetSim/realclient.log");
        // scene path and backup path (two lines) while a test build is running; left behind if the Editor hangs or crashes
        static string PendingRestorePath => Path.GetFullPath("Logs/NetSim/realclient_pending_restore.txt");

        static RealClientTest()
        {
            EditorApplication.delayCall += RestoreAfterInterruptedBuild;
        }

        // A build that never finished (Editor hung or crashed) left the bot in the saved scene: put the backup back.
        static void RestoreAfterInterruptedBuild()
        {
            if (InTestBuild || !File.Exists(PendingRestorePath)) return;
            var lines = File.ReadAllLines(PendingRestorePath);
            File.Delete(PendingRestorePath);
            if (lines.Length < 2 || !File.Exists(lines[0]) || !File.Exists(lines[1])) return;
            if (!File.ReadAllText(lines[0]).Contains(BotPrefix)) return;
            File.Copy(lines[1], lines[0], true);
            AssetDatabase.ImportAsset(lines[0], ImportAssetOptions.ForceUpdate);
            if (SceneManager.GetActiveScene().path == lines[0]) EditorSceneManager.OpenScene(lines[0], OpenSceneMode.Single);
            Log("an interrupted test build left the bot in the scene; restored " + lines[0] + " from " + lines[1]);
        }

        static void Log(string s)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {s}\n");
            Debug.Log("[NetSim RealClient] " + s);
            LastStatus = s;
        }

        [MenuItem("Tools/NetSim/Real Client Test/Build & Test with Selected Bot Prefab (3 clients)")]
        static void MenuSelected()
        {
            var prefab = Selection.activeObject as GameObject;
            if (prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab)) { Log("select a bot prefab in the Project window first"); return; }
            BuildAndTest(prefab, 3);
        }

        [MenuItem("Tools/NetSim/Real Client Test/Remove Bots From Scene")]
        public static void RemoveBots()
        {
            bool removed = false;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name.StartsWith(BotPrefix)) { Log("bot removed from scene: " + root.name); UnityEngine.Object.DestroyImmediate(root); removed = true; }
            if (removed) RemoveDanglingNetworkIds();
        }

        // A bot that was in the scene during Play Mode got a network ID in the scene descriptor; once the bot is gone the
        // entry points to nothing and VRChat reports "errors while configuring network IDs".
        static void RemoveDanglingNetworkIds()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var desc in UnityEngine.Object.FindObjectsOfType<VRC.SDKBase.VRC_SceneDescriptor>())
            {
                if (!(typeof(VRC.SDKBase.VRC_SceneDescriptor).GetField("NetworkIDs", flags)?.GetValue(desc) is IList ids)) continue;
                int n = 0;
                for (int i = ids.Count - 1; i >= 0; i--)
                {
                    var go = ids[i]?.GetType().GetField("gameObject", flags)?.GetValue(ids[i]) as GameObject;
                    if (go == null) { ids.RemoveAt(i); n++; }
                }
                if (n > 0) { EditorUtility.SetDirty(desc); Log($"removed {n} dangling network ID(s)"); }
            }
        }

        public static bool SceneHasBot()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                foreach (var root in s.GetRootGameObjects()) if (root.name.StartsWith(BotPrefix)) return true;
            }
            return false;
        }

        /// <summary>
        /// Builds the open scene with the bot and starts <paramref name="clients"/> VRChat clients.
        /// <paramref name="config"/>: "name=value;name=value" overrides public fields of the bot's behaviours.
        /// Returns at once; the build runs from the Editor update loop (safe to call from automation, works while the
        /// Editor is in the background). Needs Steam running and the VRChat SDK logged in (opening the SDK control
        /// panel restores a previous login). Progress: Logs/NetSim/realclient.log.
        /// </summary>
        public static void BuildAndTest(GameObject botPrefab, int clients = 3, string config = null, bool forceNonVR = true)
        {
            if (InTestBuild) { Log("a test build is already running"); return; }
            if (botPrefab == null) { Log("no bot prefab given"); return; }
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path)) { Log("save the scene first"); return; }
            InTestBuild = true;
            double giveUp = EditorApplication.timeSinceStartup + 60;
            bool panelOpened = false;
            EditorApplication.CallbackFunction wait = null;
            wait = () => {
                IVRCSdkWorldBuilderApi builder = null;
                bool ready = VRC.Core.APIUser.IsLoggedIn && VRCSdkControlPanel.TryGetBuilder(out builder);
                if (!ready)
                {
                    if (!panelOpened) { panelOpened = true; EditorWindow.GetWindow<VRCSdkControlPanel>(); Log("opened the SDK control panel; waiting for the SDK login / builder"); }
                    if (EditorApplication.timeSinceStartup < giveUp) return;
                    EditorApplication.update -= wait;
                    InTestBuild = false;
                    Log("the VRChat SDK is not logged in or not ready (VRChat SDK > Show Control Panel); nothing was built");
                    return;
                }
                EditorApplication.update -= wait;
                Run(builder, botPrefab, clients, config, forceNonVR);
            };
            EditorApplication.update += wait;
        }

        static async void Run(IVRCSdkWorldBuilderApi builder, GameObject botPrefab, int clients, string config, bool forceNonVR)
        {
            if (!EnsureClientPath()) { InTestBuild = false; return; }
            var scene = SceneManager.GetActiveScene();
            string scenePath = scene.path, backup = null;
            if (scene.isDirty) Log("WARNING: the open scene has unsaved changes; they will be discarded (the scene is restored from disk after the build)");
            try
            {
                backup = Path.Combine(Path.GetTempPath(), "NetSim_scene_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity");
                File.Copy(scenePath, backup, true);
                File.WriteAllText(PendingRestorePath, scenePath + "\n" + backup);
                Log("scene backed up: " + backup);
                RemoveBots();
                var bot = (GameObject)PrefabUtility.InstantiatePrefab(botPrefab, scene);
                PrefabUtility.UnpackPrefabInstance(bot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                bot.name = BotPrefix + botPrefab.name;
                ApplyConfig(bot, config);
                Log($"bot: {botPrefab.name}, config: {config}");

                VRCSettings.NumClients = Mathf.Clamp(clients, 1, 8);
                VRCSettings.ForceNoVR = forceNonVR;
                Log($"Build & Test: clients={VRCSettings.NumClients} forceNoVR={VRCSettings.ForceNoVR}; analyze with: analyze_logs.py --since \"{DateTime.Now:yyyy-MM-dd HH:mm}\"");
                await builder.BuildAndTest();
                Log("build finished, clients launched");
            }
            catch (Exception e)
            {
                Log("build failed: " + e.GetType().Name + ": " + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                // the SDK saved the scene with the bot: put the file back and reload it
                if (backup != null && File.Exists(backup))
                {
                    File.Copy(backup, scenePath, true);
                    AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceUpdate);
                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    Log("scene file restored from backup and reopened");
                }
                if (File.Exists(PendingRestorePath)) File.Delete(PendingRestorePath);
                RemoveBots();
                InTestBuild = false;
            }
        }

        // The SDK starts the clients from its saved VRChat path; when that is unset (e.g. on a new PC) Build & Test builds
        // but silently starts nothing. Fill it in from the Steam libraries if needed.
        static bool EnsureClientPath()
        {
            Type t = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType("VRC.Core.SDKClientUtilities"); if (t != null) break; }
            var get = t?.GetMethod("GetSavedVRCInstallPath", BindingFlags.Public | BindingFlags.Static);
            var set = t?.GetMethod("SetVRCInstallPath", BindingFlags.Public | BindingFlags.Static);
            if (get == null) return true;   // unknown SDK version: leave it to the SDK
            var current = get.Invoke(null, null) as string;
            if (!string.IsNullOrEmpty(current) && File.Exists(current)) return true;
            foreach (var lib in SteamLibraries())
            {
                var exe = Path.GetFullPath(Path.Combine(lib, "steamapps", "common", "VRChat", "VRChat.exe"));
                if (!File.Exists(exe)) continue;
                set?.Invoke(null, new object[] { exe });
                Log("VRChat client path was not set; using " + exe);
                return true;
            }
            Log("VRChat client not found: install VRChat (Steam) or set its path in VRChat SDK > Settings; nothing was built");
            return false;
        }

        static System.Collections.Generic.IEnumerable<string> SteamLibraries()
        {
            string steam = null;
            try { steam = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string; } catch (Exception) { }
            if (string.IsNullOrEmpty(steam)) steam = @"C:\Program Files (x86)\Steam";
            yield return steam;
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) yield break;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                yield return m.Groups[1].Value.Replace(@"\\", @"\");
        }

        // Sets public fields of every (UdonSharp) behaviour in the bot from "name=value;name=value".
        public static void ApplyConfig(GameObject bot, string config)
        {
            if (string.IsNullOrWhiteSpace(config)) return;
            foreach (var kv in config.Split(';'))
            {
                var p = kv.Split('=');
                if (p.Length != 2) continue;
                string name = p[0].Trim(), value = p[1].Trim();
                bool applied = false;
                foreach (var ub in bot.GetComponentsInChildren<UdonBehaviour>(true))
                {
                    if (UdonSharpEditorUtility.IsUdonSharpBehaviour(ub))
                    {
                        var proxy = UdonSharpEditorUtility.GetProxyBehaviour(ub);
                        var f = proxy.GetType().GetField(name);
                        if (f == null) continue;
                        var v = Convert.ChangeType(value, f.FieldType, CultureInfo.InvariantCulture);
                        f.SetValue(proxy, v);
                        UdonSharpEditorUtility.CopyProxyToUdon(proxy);
                        // fields left at their default are not stored on the UdonBehaviour; make sure the value is
                        if (!ub.publicVariables.TrySetVariableValue(name, v))
                        {
                            var variable = (IUdonVariable)Activator.CreateInstance(typeof(UdonVariable<>).MakeGenericType(f.FieldType), name, v);
                            ub.publicVariables.TryAddVariable(variable);
                        }
                        applied = true;
                    }
                    else if (ub.publicVariables.TryGetVariableType(name, out var t))
                    {
                        ub.publicVariables.TrySetVariableValue(name, Convert.ChangeType(value, t, CultureInfo.InvariantCulture));
                        applied = true;
                    }
                }
                if (!applied) Log($"WARNING: no public field '{name}' on the bot");
            }
        }
    }

    /// <summary>Safety net: never let a debug bot into an upload.</summary>
    public class RealClientBuildGuard : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => -1000;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (requestedBuildType != VRCSDKRequestedBuildType.Scene) return true;
            if (RealClientTest.SceneHasBot() && !RealClientTest.InTestBuild)
            {
                Debug.LogError("[NetSim RealClient] A NetSim bot is in the scene. Remove it (Tools > NetSim > Real Client Test > Remove Bots From Scene) before building or uploading.");
                return false;
            }
            return true;
        }
    }
}
#endif

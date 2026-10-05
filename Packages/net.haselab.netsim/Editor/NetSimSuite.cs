// Runs a queue of NetSim steps unattended across play-mode restarts and script recompiles.
// Steps (strings):
//   "swap:<dir>"                         copy every file under <dir> (mirrors the project layout) into the project, recompile
//   "<scenario>|<label>|latMin|latMax|eventDrop|serDrop|objDrop|seed|timeScale[|trace[|teleport]]"
//                                        run one scenario in a fresh play session (trace: comma separated path
//                                        substrings to log network traffic for; "teleport": bots move objects instantly)
// Start:  Haselab.NetSim.NetSimSuite.Start(new[]{ ... });   Progress: Logs/NetSim/suite.log
#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Haselab.NetSim
{
    [InitializeOnLoad]
    public static class NetSimSuite
    {
        const string KeyQueue = "Haselab.NetSim.Suite.Queue";
        const string KeyPhase = "Haselab.NetSim.Suite.Phase";   // "", "waitCompile", "playing"
        static string LogPath => Path.GetFullPath("Logs/NetSim/suite.log");

        static readonly double loadedAt;
        static NetSimSuite()
        {
            loadedAt = EditorApplication.timeSinceStartup;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Tick;
            RequestPump();
        }

        // delayCall does not fire while the editor sits unfocused in the background; drive the suite from update
        static bool pumpRequested; static double nextPumpAt;
        static void RequestPump() { pumpRequested = true; }
        static void Tick()
        {
            if (!pumpRequested && !(Peek() != null && !EditorApplication.isPlaying && EditorApplication.timeSinceStartup > nextPumpAt)) return;
            if (EditorApplication.timeSinceStartup < nextPumpAt) return;
            nextPumpAt = EditorApplication.timeSinceStartup + 1.0;
            pumpRequested = false;
            Pump();
        }

        public static void Start(string[] steps)
        {
            Directory.CreateDirectory("Logs/NetSim");
            File.AppendAllText(LogPath, $"\n== suite started {DateTime.Now:HH:mm:ss}: {steps.Length} steps\n");
            SessionState.SetString(KeyQueue, string.Join("\n", steps));
            SessionState.SetString(KeyPhase, "");
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; else Pump();
        }

        public static void Cancel() { SessionState.SetString(KeyQueue, ""); SessionState.SetString(KeyPhase, ""); Log("cancelled"); }
        public static string Remaining => SessionState.GetString(KeyQueue, "");

        static void Log(string s) { Directory.CreateDirectory("Logs/NetSim"); File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {s}\n"); Debug.Log("[NetSimSuite] " + s); }

        static string Peek() { var q = Remaining; if (string.IsNullOrEmpty(q)) return null; return q.Split('\n')[0]; }
        static void PopStep() { var q = Remaining.Split('\n').Skip(1); SessionState.SetString(KeyQueue, string.Join("\n", q)); }

        // edit mode: decide the next action
        static void Pump()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) { if (Peek() != null) RequestPump(); return; }
            var phase = SessionState.GetString(KeyPhase, "");
            if (phase == "done") { PopStep(); SessionState.SetString(KeyPhase, ""); phase = ""; }
            if (phase == "playing") { SessionState.SetString(KeyPhase, ""); phase = ""; Log("play session ended without result; retrying step"); }
            var step = Peek();
            if (step == null) { Log("suite finished"); return; }
            if (step.StartsWith("swap:"))
            {
                if (phase == "waitCompile")
                {
                    if (EditorUtility.scriptCompilationFailed) { Log("compile FAILED after " + step + "; stopping suite"); Cancel(); return; }
                    Log("swap done: " + step); PopStep(); SessionState.SetString(KeyPhase, ""); RequestPump(); return;
                }
                var dir = step.Substring(5);
                int n = 0;
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var rel = f.Substring(dir.Length).TrimStart('/', '\\');
                    File.Copy(f, Path.Combine(Directory.GetCurrentDirectory(), rel), true); n++;
                }
                Log($"swap {dir}: {n} files copied, recompiling");
                SessionState.SetString(KeyPhase, "waitCompile");
                AssetDatabase.Refresh();
                RequestPump();
                return;
            }
            // scenario step -> enter play (give UdonSharp time to finish compiling after a reload)
            if (EditorApplication.timeSinceStartup - loadedAt < 10) { RequestPump(); return; }
            SessionState.SetString(KeyPhase, "playing");
            Log("enter play for: " + step);
            EditorApplication.isPlaying = true;
        }

        static int waitFrames;
        static void WaitCompileThenPump()
        {
            // a domain reload re-runs the static constructor (which pumps); otherwise poll a few frames
            if (EditorApplication.isCompiling || ++waitFrames < 30) { RequestPump(); return; }
            waitFrames = 0; Pump();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            var step = Peek();
            if (step == null) return;
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetString(KeyPhase, "") == "playing" && !step.StartsWith("swap:"))
            {
                var a = step.Split('|');
                float F(int i, float d) => a.Length > i && float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;
                var cfg = new NetSimConfig { latencyMin = F(2, 0.1f), latencyMax = F(3, 0.3f), eventDropRate = F(4, 0), serializationDropRate = F(5, 0), objectSyncDropRate = F(6, 0), seed = (int)F(7, 1), trace = a.Length > 9 ? a[9] : "", carry = !(a.Length > 10 && a[10] == "teleport") };
                Log("running " + step);
                NetSimScenarioRunner.RunWhenReady(a[0], cfg, F(8, 4f), a.Length > 1 ? a[1] : "");
                EditorApplication.update += WatchDone;
            }
            if (s == PlayModeStateChange.EnteredEditMode) RequestPump();
        }

        static float startedAt = -1;
        static void WatchDone()
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= WatchDone; return; }
            if (startedAt < 0 && NetSimScenarioRunner.Running) startedAt = Time.realtimeSinceStartup;
            if (startedAt >= 0 && !NetSimScenarioRunner.Running)
            {
                EditorApplication.update -= WatchDone; startedAt = -1;
                Log("finished: " + Peek() + " -> " + NetSimScenarioRunner.LastReportPath);
                SessionState.SetString(KeyPhase, "done");
                EditorApplication.isPlaying = false;
            }
        }
    }
}
#endif

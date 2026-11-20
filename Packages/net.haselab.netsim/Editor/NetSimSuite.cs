// Runs a queue of NetSim steps unattended across play-mode restarts and script recompiles.
// Steps (strings):
//   "swap:<dir>"                         copy every file under <dir> (mirrors the project layout) into the project, recompile
//   "<scenario>|<label>|latMin|latMax|eventDrop|serDrop|objDrop|seed|timeScale[|trace[|teleport]]"
//                                        run one scenario in a fresh play session (trace: comma separated path
//                                        substrings to log network traffic for; "teleport": bots move objects instantly).
//                                        Fields left out (or empty) come from the NetSim window's settings, so
//                                        "<scenario>" alone runs it with those settings.
// Start:  Haselab.NetSim.NetSimSuite.Start(new[]{ ... });   Progress: Logs/NetSim/suite.log
// Each finished step is logged as "finished: <step> -> <report> [PASS|FAIL|ERROR]".
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
        const string KeyPhase = "Haselab.NetSim.Suite.Phase";   // "", "waitCompile", "playing", "done"
        const string KeyResults = "Haselab.NetSim.Suite.Results"; // one "<verdict>\t<step>\t<report>" line per finished step
        const string KeyRetries = "Haselab.NetSim.Suite.Retries";
        const string KeyTotal = "Haselab.NetSim.Suite.Total";
        /// <summary>Called once when the queue becomes empty (after the last step or a cancel), with the result lines.</summary>
        public static event Action<string[]> Finished;
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
            steps = steps.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            Directory.CreateDirectory("Logs/NetSim");
            File.AppendAllText(LogPath, $"\n== suite started {DateTime.Now:HH:mm:ss}: {steps.Length} steps\n");
            SessionState.SetString(KeyQueue, string.Join("\n", steps));
            SessionState.SetString(KeyPhase, "");
            SessionState.SetString(KeyResults, "");
            SessionState.SetInt(KeyRetries, 0);
            SessionState.SetInt(KeyTotal, steps.Length);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; else Pump();
        }

        public static void Cancel()
        {
            bool wasRunning = Peek() != null;
            SessionState.SetString(KeyQueue, ""); SessionState.SetString(KeyPhase, ""); Log("cancelled");
            if (EditorApplication.isPlaying && NetSimScenarioRunner.Running) EditorApplication.isPlaying = false;
            if (wasRunning) RaiseFinished();
        }
        public static string Remaining => SessionState.GetString(KeyQueue, "");
        public static bool IsRunning => Peek() != null;
        public static int TotalSteps => SessionState.GetInt(KeyTotal, 0);
        public static int RemainingSteps => string.IsNullOrEmpty(Remaining) ? 0 : Remaining.Split('\n').Length;
        public static string CurrentStep => Peek();
        /// <summary>"&lt;verdict&gt;\t&lt;step&gt;\t&lt;report path&gt;" for every finished step of the current / last suite.</summary>
        public static string[] Results => SessionState.GetString(KeyResults, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        static void AddResult(string verdict, string step, string report)
            => SessionState.SetString(KeyResults, SessionState.GetString(KeyResults, "") + $"{(verdict == "" ? "-" : verdict)}\t{step}\t{report}\n");

        static void RaiseFinished()
        {
            var r = Results;
            Log("results: " + (r.Length == 0 ? "none" : string.Join("; ", r.Select(x => x.Split('\t')[0] + " " + x.Split('\t')[1]))));
            try { Finished?.Invoke(r); } catch (Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------------ step strings
        /// <summary>
        /// Parses "scenario|label|latMin|latMax|eventDrop|serDrop|objDrop|seed|timeScale|trace|teleport". Missing or empty
        /// fields come from NetSimSettings (the NetSim window), so a bare scenario name is a valid step.
        /// </summary>
        public static void ParseStep(string step, out string scenario, out string label, out NetSimConfig cfg, out float timeScale)
        {
            var a = step.Split('|');
            var st = NetSimSettings.Instance;
            cfg = st.CloneConfig();
            bool Has(int i) => a.Length > i && a[i].Trim().Length > 0;
            float F(int i, float d) => Has(i) && float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;
            scenario = a[0].Trim();
            label = Has(1) ? a[1].Trim() : "";
            cfg.latencyMin = F(2, cfg.latencyMin); cfg.latencyMax = F(3, cfg.latencyMax);
            cfg.eventDropRate = F(4, cfg.eventDropRate); cfg.serializationDropRate = F(5, cfg.serializationDropRate); cfg.objectSyncDropRate = F(6, cfg.objectSyncDropRate);
            cfg.seed = (int)F(7, cfg.seed);
            timeScale = F(8, st.timeScale);
            if (Has(9)) cfg.trace = a[9];
            if (Has(10)) cfg.carry = a[10].Trim() != "teleport";
        }

        /// <summary>The step string for a scenario run with the given configuration (inverse of ParseStep).</summary>
        public static string FormatStep(string scenario, string label, NetSimConfig c, float timeScale)
        {
            string N(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            var s = $"{scenario}|{label}|{N(c.latencyMin)}|{N(c.latencyMax)}|{N(c.eventDropRate)}|{N(c.serializationDropRate)}|{N(c.objectSyncDropRate)}|{c.seed}|{N(timeScale)}";
            if (!string.IsNullOrEmpty(c.trace) || !c.carry) s += "|" + c.trace;
            if (!c.carry) s += "|teleport";
            return s;
        }

        static void Log(string s) { Directory.CreateDirectory("Logs/NetSim"); File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {s}\n"); Debug.Log("[NetSimSuite] " + s); }

        static string Peek() { var q = Remaining; if (string.IsNullOrEmpty(q)) return null; return q.Split('\n')[0]; }
        static void PopStep() { var q = Remaining.Split('\n').Skip(1); SessionState.SetString(KeyQueue, string.Join("\n", q)); }

        // edit mode: decide the next action
        static void Pump()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) { if (Peek() != null) RequestPump(); return; }
            var phase = SessionState.GetString(KeyPhase, "");
            if (phase == "done") { PopStep(); SessionState.SetString(KeyPhase, ""); SessionState.SetInt(KeyRetries, 0); phase = ""; if (Peek() == null) { Log("suite finished"); RaiseFinished(); return; } }
            if (phase == "playing")
            {
                SessionState.SetString(KeyPhase, ""); phase = "";
                int retries = SessionState.GetInt(KeyRetries, 0) + 1;
                SessionState.SetInt(KeyRetries, retries);
                if (retries >= 3) { Log("play session ended without result 3 times; skipping step " + Peek()); AddResult("ERROR", Peek(), ""); PopStep(); SessionState.SetInt(KeyRetries, 0); if (Peek() == null) { Log("suite finished"); RaiseFinished(); return; } }
                else Log("play session ended without result; retrying step");
            }
            var step = Peek();
            if (step == null) return;
            if (step.StartsWith("swap:"))
            {
                if (phase == "waitCompile")
                {
                    if (EditorUtility.scriptCompilationFailed) { Log("compile FAILED after " + step + "; stopping suite"); AddResult("ERROR", step, ""); Cancel(); return; }
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
                ParseStep(step, out var scenario, out var label, out var cfg, out var timeScale);
                Log("running " + step);
                try { NetSimScenarioRunner.RunWhenReady(scenario, cfg, timeScale, label); }
                catch (Exception e) { Log("cannot run " + step + ": " + e.Message); AddResult("ERROR", step, ""); SessionState.SetString(KeyPhase, "done"); EditorApplication.isPlaying = false; return; }
                EditorApplication.update += WatchDone;
            }
            if (s == PlayModeStateChange.EnteredEditMode) RequestPump();
        }

        static float startedAt = -1, watchSince = -1;
        static void WatchDone()
        {
            // play mode ended, or the suite was cancelled (queue emptied): nothing to record
            if (!EditorApplication.isPlaying || Peek() == null) { EditorApplication.update -= WatchDone; startedAt = watchSince = -1; return; }
            if (watchSince < 0) watchSince = Time.realtimeSinceStartup;
            if (startedAt < 0 && NetSimScenarioRunner.Running) startedAt = Time.realtimeSinceStartup;
            if (startedAt < 0 && Time.realtimeSinceStartup - watchSince > 180)
            {
                // the scenario never started (world not ready, or Run threw: see the console)
                EditorApplication.update -= WatchDone; watchSince = -1;
                Log("scenario did not start within 180 s: " + Peek());
                AddResult("ERROR", Peek(), "");
                SessionState.SetString(KeyPhase, "done");
                EditorApplication.isPlaying = false;
                return;
            }
            if (startedAt >= 0 && !NetSimScenarioRunner.Running)
            {
                EditorApplication.update -= WatchDone; startedAt = watchSince = -1;
                var v = NetSimScenarioRunner.LastVerdict;
                Log("finished: " + Peek() + " -> " + NetSimScenarioRunner.LastReportPath + (v != "" ? " [" + v + "]" : ""));
                AddResult(v, Peek(), NetSimScenarioRunner.LastReportPath);
                SessionState.SetString(KeyPhase, "done");
                EditorApplication.isPlaying = false;
            }
        }
    }
}
#endif

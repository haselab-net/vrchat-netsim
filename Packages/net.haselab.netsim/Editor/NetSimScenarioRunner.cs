// Scenario framework for NetSim.
//
// A scenario is a static method returning IEnumerator that takes a NetSimScenarioContext and is marked with
// [NetSimScenario("name")]. It runs as a coroutine in play mode while NetSim is active, adds clients, drives the
// world (for example with a bot), waits and writes its findings to a Markdown report:
//
//   [NetSimScenario("my-check")]
//   static IEnumerator MyCheck(NetSimScenarioContext ctx)
//   {
//       yield return ctx.AddClients(2);
//       yield return ctx.Settle(10);
//       ctx.ReportConsistency("after 10 s");
//   }
//
// Run one scenario (play mode):  NetSimScenarioRunner.RunWhenReady("my-check", new NetSimConfig { latencyMin = 0.1f, latencyMax = 0.3f });
// Run many unattended:           NetSimSuite.Start(new[] { "my-check|label|0.1|0.3|0|0|0|1|4", ... });
// Reports: Logs/NetSim/<scenario>_<label>_<time>.md
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.Udon;

namespace Haselab.NetSim
{
    [AttributeUsage(AttributeTargets.Method)]
    public class NetSimScenarioAttribute : Attribute
    {
        public readonly string Name;
        public NetSimScenarioAttribute(string name) { Name = name; }
    }

    public static class NetSimScenarioRunner
    {
        public static string LastReportPath;
        public static bool Running;
        /// <summary>Called every frame before a scenario starts; return true when the world has finished initializing.</summary>
        public static Func<bool> WorldReady = DefaultWorldReady;
        /// <summary>Log lines starting with these strings are counted and listed in the report (e.g. "GameStarted").</summary>
        public static readonly List<string> CountedLogPrefixes = new List<string>();

        public static IEnumerable<string> ScenarioNames => Scenarios().Keys;

        static Dictionary<string, MethodInfo> Scenarios()
        {
            var d = new Dictionary<string, MethodInfo>();
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types; try { types = a.GetTypes(); } catch { continue; }
                foreach (var t in types)
                    foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        var attr = m.GetCustomAttribute<NetSimScenarioAttribute>();
                        if (attr != null) d[attr.Name] = m;
                    }
            }
            return d;
        }

        static bool DefaultWorldReady()
        {
            // every UdonBehaviour in the open scene has run Start (or 10 s have passed)
            if (Time.time > 10f) return true;
            var f = typeof(UdonBehaviour).GetField("_hasDoneStart", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var ub in UnityEngine.Object.FindObjectsOfType<UdonBehaviour>())
                if (ub.enabled && ub.gameObject.activeInHierarchy && f != null && !(bool)f.GetValue(ub)) return false;
            return Time.time > 2f;
        }

        /// <summary>Call in play mode: waits until the world is ready, then runs the scenario.</summary>
        public static void RunWhenReady(string scenario, NetSimConfig cfg = null, float timeScale = 4f, string label = "")
        {
            EditorApplication.CallbackFunction wait = null;
            wait = () => {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= wait; return; }
                if (!WorldReady()) return;
                EditorApplication.update -= wait;
                Run(scenario, cfg, timeScale, label);
            };
            EditorApplication.update += wait;
        }

        public static void Run(string scenario, NetSimConfig cfg = null, float timeScale = 4f, string label = "")
        {
            if (Running) { Debug.LogWarning("[NetSim] a scenario is already running"); return; }
            if (!Scenarios().TryGetValue(scenario, out var method)) throw new ArgumentException("unknown scenario: " + scenario + " (known: " + string.Join(", ", ScenarioNames) + ")");
            Running = true;
            var ctx = new NetSimScenarioContext(scenario + (string.IsNullOrEmpty(label) ? "" : "_" + label));
            Time.timeScale = timeScale;
            try { NetSim.Start(cfg ?? new NetSimConfig()); }
            catch { Running = false; Time.timeScale = 1f; throw; }
            var body = (IEnumerator)method.Invoke(null, new object[] { ctx });
            var driver = UnityEngine.Object.FindObjectOfType<NetSimDriver>();
            driver.StartCoroutine(ctx.Wrap(body, () => {
                LastReportPath = ctx.ReportPath;
                Time.timeScale = 1f;
                NetSim.Stop();
                Running = false;
                Debug.Log("[NetSim] SCENARIO DONE " + ctx.Name);
            }));
        }
    }

    /// <summary>Helpers available to scenarios; everything written with R() ends up in the report.</summary>
    public class NetSimScenarioContext
    {
        public readonly string Name;
        public string ReportPath { get; private set; }
        public bool Stalled { get; private set; }
        /// <summary>Simulated seconds without any change of the progress signature before a stall is reported.</summary>
        public float StallSeconds = 240f;
        /// <summary>Returns a string that changes whenever the game makes progress (used for stall detection).</summary>
        public Func<NetSimClient, string> ProgressSignature;

        readonly StringBuilder report = new StringBuilder();
        readonly Dictionary<string, int> logCounts = new Dictionary<string, int>();

        public NetSimScenarioContext(string name) { Name = name; ProgressSignature = DefaultProgressSignature; }

        public NetSimClient Master => NetSim.Clients.First(c => c.joined && c.player.playerId == NetSim.MasterId);
        public IEnumerable<NetSimClient> Joined => NetSim.Clients.Where(c => c.joined);

        public void R(string line) => report.AppendLine(line);

        internal IEnumerator Wrap(IEnumerator body, Action onDone)
        {
            Application.logMessageReceived += OnLog;
            R($"# NetSim scenario: {Name}");
            R($"- date: {DateTime.Now:yyyy-MM-dd HH:mm}");
            var c = NetSim.Config;
            R($"- config: latency {c.latencyMin * 1000:F0}-{c.latencyMax * 1000:F0} ms (one-way), eventDrop {c.eventDropRate:P0}, serializationDrop {c.serializationDropRate:P0}, objectSyncDrop {c.objectSyncDropRate:P0}, seed {c.seed}");
            R("");
            var t0 = Time.realtimeSinceStartup;
            while (true)
            {
                bool moved;
                try { moved = body.MoveNext(); }
                catch (Exception e) { R($"- SCENARIO EXCEPTION: {e}"); break; }
                if (!moved) break;
                yield return body.Current;
            }
            R("");
            R("## Totals");
            R($"- real time: {Time.realtimeSinceStartup - t0:F0} s, sim time: {Time.time:F0} s");
            if (logCounts.Count > 0) R($"- log counts: {string.Join(", ", logCounts.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value))}");
            R($"- net stats: {NetSim.StatsString()}");
            R($"- SetOwner hot spots: {NetSim.SetOwnerHotSpots()}");
            var halted = NetSim.HaltedBehaviours();
            R($"- halted UdonBehaviours: {halted.Count}"); foreach (var h in halted.Take(20)) R($"  - {h}");
            Application.logMessageReceived -= OnLog;
            Directory.CreateDirectory("Logs/NetSim");
            ReportPath = Path.GetFullPath($"Logs/NetSim/{Name}_{DateTime.Now:HHmmss}.md");
            File.WriteAllText(ReportPath, report.ToString());
            Debug.Log("[NetSim] REPORT " + ReportPath + "\n" + report);
            onDone();
        }

        void OnLog(string msg, string st, LogType t)
        {
            foreach (var k in NetSimScenarioRunner.CountedLogPrefixes) if (msg.StartsWith(k)) { logCounts.TryGetValue(k, out var v); logCounts[k] = v + 1; }
            if (t == LogType.Exception || (t == LogType.Error && msg.Contains("Udon")))
            {
                logCounts.TryGetValue("!error", out var e); logCounts["!error"] = e + 1;
                if (e < 5) R($"- ERROR: {msg.Split('\n')[0]}");
            }
        }

        // ------------------------------------------------------------------ clients
        static readonly FieldInfo fHasDoneStart = typeof(UdonBehaviour).GetField("_hasDoneStart", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>Waits until the client's copy has started its UdonBehaviours (or 30 s).</summary>
        public IEnumerator WaitReady(NetSimClient c)
        {
            float until = Time.time + 30;
            while (Time.time < until)
            {
                if (c.ready && c.ubByKey.Count > 0)
                {
                    int notStarted = c.ubByKey.Values.Count(ub => ub && ub.enabled && ub.gameObject.activeInHierarchy && !(bool)fHasDoneStart.GetValue(ub));
                    if (notStarted == 0) yield break;
                }
                yield return null;
            }
            R($"- WARNING: {c} did not finish starting in time");
        }

        /// <summary>Adds remote clients one at a time (copies loaded in the same frame do not start reliably).</summary>
        public IEnumerator AddClients(int n, string prefix = "Remote", bool lateJoin = false)
        {
            for (int i = 0; i < n; i++)
            {
                var c = NetSim.AddClient(prefix + (NetSim.Clients.Count), lateJoin);
                yield return WaitReady(c);
            }
            yield return Settle(2f);
        }

        /// <summary>Waits a fixed simulated time (continuous sync keeps a steady trickle of messages).</summary>
        public IEnumerator Settle(float seconds = 3f)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        /// <summary>
        /// Calls bot(client, rng) for every joined client once per simulated second until stop() is true, the timeout
        /// expires or the game stops making progress (see ProgressSignature / StallSeconds).
        /// </summary>
        public IEnumerator PlayUntil(Func<bool> stop, float timeoutSim, Action<NetSimClient, System.Random> bot, Action perTick = null)
        {
            var rngs = new Dictionary<NetSimClient, System.Random>();
            float until = Time.time + timeoutSim, nextStatus = 0;
            string lastSig = null; float lastChange = Time.time;
            while (!stop() && Time.time < until)
            {
                var sig = ProgressSignature(Master);
                if (sig != lastSig) { lastSig = sig; lastChange = Time.time; }
                else if (Time.time - lastChange > StallSeconds)
                {
                    Stalled = true;
                    R($"## STALL detected (no progress for {StallSeconds:F0}s sim)");
                    yield break;
                }
                foreach (var c in Joined.ToList())
                {
                    if (!rngs.TryGetValue(c, out var r)) rngs[c] = r = new System.Random(NetSim.Config.seed * 100 + c.index);
                    bot?.Invoke(c, r);
                }
                perTick?.Invoke();
                if (Time.time > nextStatus) { nextStatus = Time.time + 60; Debug.Log($"[NetSim] status t={Time.time:F0} pending={NetSim.PendingMessages}"); }
                float wait = Time.time + 1f; while (Time.time < wait && !stop()) yield return null;
            }
        }

        /// <summary>Default progress signature: the synced variables of every behaviour on the master.</summary>
        public static string DefaultProgressSignature(NetSimClient m)
        {
            var sb = new StringBuilder();
            foreach (var kv in m.ubByKey)
            {
                var st = NetSim.CaptureSynced(kv.Value);
                if (st == null) continue;
                foreach (var v in st) sb.Append(v.Value is Array a ? string.Join(",", a.Cast<object>()) : v.Value).Append('|');
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ reporting
        /// <summary>Synced-variable differences between the master and every other client.</summary>
        public int ReportConsistency(string title, int maxLines = 15)
        {
            var m = Master; int total = 0;
            R($"### {title}");
            foreach (var c in Joined.Where(x => x != m))
            {
                var d = NetSim.DiffSynced(m, c); total += d.Count;
                R($"- synced vars {m} vs {c}: {d.Count} differences"); foreach (var x in d.Take(maxLines)) R($"  - {x}");
            }
            return total;
        }

        /// <summary>Visual differences (active / collider / renderer / text / interactable) that only a late joiner has.</summary>
        public List<string> LateJoinOnlyDifferences(NetSimClient early, NetSimClient late, string rootName, Func<string, bool> ignore = null)
        {
            var dEarly = NetSim.DiffVisual(Master, early, rootName, ignore).Select(s => s.Replace(early.ToString(), "X"));
            var dLate = NetSim.DiffVisual(Master, late, rootName, ignore).Select(s => s.Replace(late.ToString(), "X"));
            return dLate.Except(dEarly).ToList();
        }
    }
}
#endif

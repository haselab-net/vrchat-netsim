// A manual NetSim session: started from the NetSim window (or code) in play mode without a scenario. A person
// adds / removes clients, plays as C0 in the Game view, makes other clients interact and compares the clients.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.Udon;

namespace Haselab.NetSim
{
    [InitializeOnLoad]
    public static class NetSimSession
    {
        /// <summary>NetSim was started by Start() (not by a scenario).</summary>
        public static bool Manual { get; private set; }
        /// <summary>Start() was called and waits for the world to finish initializing.</summary>
        public static bool Starting { get; private set; }
        /// <summary>A client's scene copy is still loading (add the next one after it is ready).</summary>
        public static bool Loading => NetSim.Active && NetSim.Clients.Any(c => c.joined && !c.ready);

        static EditorApplication.CallbackFunction waiting;

        static NetSimSession()
        {
            EditorApplication.playModeStateChanged += s => {
                if (s != PlayModeStateChange.ExitingPlayMode) return;
                CancelStart();
                if (NetSim.Active) { NetSimView.Reset(); NetSim.Stop(); }   // never leave the hooks installed in edit mode
                Manual = false;
            };
        }

        /// <summary>Starts NetSim with the window's settings once the world is ready (play mode only).</summary>
        public static void Start()
        {
            if (!EditorApplication.isPlaying || NetSim.Active || Starting || NetSimScenarioRunner.Running) return;
            Starting = true;
            waiting = () => {
                if (!EditorApplication.isPlaying) { CancelStart(); return; }
                if (!NetSimScenarioRunner.WorldReady()) return;
                CancelStart();
                NetSim.Start(NetSimSettings.Instance.CloneConfig());
                Manual = true;
                NetSimView.Show(0);
            };
            EditorApplication.update += waiting;
        }

        static void CancelStart()
        {
            if (waiting != null) EditorApplication.update -= waiting;
            waiting = null; Starting = false;
        }

        public static void Stop()
        {
            CancelStart();
            if (!NetSim.Active) return;
            NetSimView.Reset();
            NetSim.Stop();
            Manual = false;
        }

        /// <summary>Adds a remote client (a copy of the scene). Late joiners get the server's state snapshot.</summary>
        public static NetSimClient AddClient(bool lateJoin = false)
        {
            if (!NetSim.Active) throw new InvalidOperationException("NetSim is not running");
            return NetSim.AddClient((lateJoin ? "Late" : "Remote") + NetSim.Clients.Count, lateJoin);
        }

        /// <summary>Makes the client leave (for the master: master migration).</summary>
        public static void Leave(NetSimClient c)
        {
            NetSimView.OnLeave(c);
            NetSim.RemoveClient(c);
        }

        public static NetSimClient Master => NetSim.Clients.FirstOrDefault(c => c.joined && c.player != null && c.player.playerId == NetSim.MasterId);

        /// <summary>Synced-variable differences between the master and every other joined client.</summary>
        public static List<string> CompareAll()
        {
            var m = Master; var res = new List<string>();
            if (m == null) return res;
            foreach (var c in NetSim.Clients.Where(x => x.joined && x != m)) res.AddRange(NetSim.DiffSynced(m, c));
            return res;
        }

        /// <summary>Interactable UdonBehaviours of a client a player could use now (active, collider enabled, interactive).</summary>
        public static List<UdonBehaviour> Interactables(NetSimClient c)
        {
            return c.ubByKey.Values.Where(u => u != null && u.enabled && u.gameObject.activeInHierarchy && !u.DisableInteractive && IsInteractive(u)
                && HasEnabledCollider(u.gameObject)).ToList();
        }

        // players can only interact through a collider (GetComponent returns a fake null object in the editor: compare with ==)
        static bool HasEnabledCollider(GameObject g) { var col = g.GetComponent<Collider>(); return col != null && col.enabled; }

        public static bool IsInteractive(UdonBehaviour u) { try { return u.IsInteractive; } catch (Exception) { return false; } }

        /// <summary>Runs `eventName` (e.g. "_interact") on the client's copy of `ub`, as that client.</summary>
        public static void SendEvent(NetSimClient c, UdonBehaviour ub, string eventName)
        {
            NetSim.As(c, () => ub.SendCustomEvent(eventName));
        }

        /// <summary>Writes the current state of all clients (differences, stats, halted behaviours) to Logs/NetSim.</summary>
        public static string WriteSnapshot(string note = "")
        {
            var sb = new StringBuilder();
            var cfg = NetSim.Config;
            sb.AppendLine("# NetSim snapshot (manual session)");
            sb.AppendLine($"- date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}, sim time {Time.time:F1} s");
            sb.AppendLine($"- config: latency {cfg.latencyMin * 1000:F0}-{cfg.latencyMax * 1000:F0} ms (one-way), eventDrop {cfg.eventDropRate:P0}, serializationDrop {cfg.serializationDropRate:P0}, objectSyncDrop {cfg.objectSyncDropRate:P0}, seed {cfg.seed}");
            if (!string.IsNullOrEmpty(note)) sb.AppendLine($"- note: {note}");
            sb.AppendLine("- clients: " + string.Join(", ", NetSim.Clients.Select(c => $"{c} {c.player?.displayName}{(c.player != null && c.player.playerId == NetSim.MasterId ? " (master)" : "")}{(c.joined ? "" : " (left)")}")));
            sb.AppendLine();
            var m = Master;
            int total = 0;
            sb.AppendLine("## Synced variables (master vs others)");
            if (m != null)
                foreach (var c in NetSim.Clients.Where(x => x.joined && x != m))
                {
                    var d = NetSim.DiffSynced(m, c); total += d.Count;
                    sb.AppendLine($"- {m} vs {c}: {d.Count} differences");
                    foreach (var x in d.Take(50)) sb.AppendLine($"  - {x}");
                }
            sb.AppendLine();
            sb.AppendLine("## Visible differences (active / collider / renderer / text / interactable)");
            if (m != null)
                foreach (var c in NetSim.Clients.Where(x => x.joined && x != m))
                {
                    var d = m.scene.GetRootGameObjects().SelectMany(r => NetSim.DiffVisual(m, c, r.name)).Distinct().ToList();
                    sb.AppendLine($"- {m} vs {c}: {d.Count} differences");
                    foreach (var x in d.Take(50)) sb.AppendLine($"  - {x}");
                }
            sb.AppendLine();
            sb.AppendLine("## Totals");
            sb.AppendLine($"- synced variable differences: {total}");
            sb.AppendLine($"- in flight: {NetSim.PendingMessages} messages (differences may still be settling)");
            sb.AppendLine($"- net stats: {NetSim.StatsString()}");
            sb.AppendLine($"- SetOwner hot spots: {NetSim.SetOwnerHotSpots()}");
            var halted = NetSim.HaltedBehaviours();
            sb.AppendLine($"- halted UdonBehaviours: {halted.Count}"); foreach (var h in halted.Take(20)) sb.AppendLine($"  - {h}");
            sb.AppendLine();
            sb.AppendLine("## NetSim log (last 100 lines)");
            foreach (var l in NetSim.Log.Skip(Math.Max(0, NetSim.Log.Count - 100))) sb.AppendLine("    " + l);
            Directory.CreateDirectory("Logs/NetSim");
            var path = Path.GetFullPath($"Logs/NetSim/snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.md");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[NetSim] SNAPSHOT " + path);
            return path;
        }
    }
}
#endif

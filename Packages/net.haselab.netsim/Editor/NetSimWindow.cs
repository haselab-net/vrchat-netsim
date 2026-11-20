// Tools > NetSim > NetSim Window: everything NetSim does, without writing code.
//
//   Network     latency / jitter / loss (presets or values); saved to ProjectSettings/NetSimSettings.json
//   Manual      in play mode: start NetSim, add / remove clients, choose whose copy the Game view shows,
//               make a client interact with the selected object, compare clients, write a snapshot report
//   Scenarios   run a scenario now, or a batch of scenarios x network conditions unattended
//   Reports     the reports in Logs/NetSim with their verdicts
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace Haselab.NetSim
{
    public class NetSimWindow : EditorWindow
    {
        [MenuItem("Tools/NetSim/NetSim Window", priority = 0)]
        public static void Open() { var w = GetWindow<NetSimWindow>(); w.titleContent = new GUIContent("NetSim"); w.minSize = new Vector2(380, 300); w.Show(); }

        [MenuItem("Tools/NetSim/Open Reports Folder", priority = 20)]
        static void OpenReports() { Directory.CreateDirectory("Logs/NetSim"); EditorUtility.RevealInFinder(Path.GetFullPath("Logs/NetSim") + Path.DirectorySeparatorChar); }

        static readonly string[] Tabs = { "Manual test", "Scenarios", "Reports" };
        [SerializeField] int tab;
        [SerializeField] bool showNetwork = true, showLog, showInteractables = true;
        [SerializeField] string eventName = "_interact";
        [SerializeField] int interactClient = 1;
        [SerializeField] bool autoCompare;
        // per session only (a script reload would otherwise restore them as empty values)
        [NonSerialized] Vector2 scroll, reportScroll;
        [NonSerialized] List<string> diffs; [NonSerialized] string diffsAt = "";
        [NonSerialized] double nextAutoCompare;
        [NonSerialized] string lastSnapshot;
        [NonSerialized] string viewedReport, viewedReportText;
        [NonSerialized] List<(string path, string name, DateTime time, string verdict)> reports; [NonSerialized] double nextReportScan;

        GUIStyle wrap, bold, red, green, gray, mono;
        void Styles()
        {
            if (wrap != null) return;
            wrap = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };
            bold = new GUIStyle(EditorStyles.boldLabel);
            red = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.95f, 0.35f, 0.3f) }, wordWrap = true };
            green = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.3f, 0.75f, 0.35f) } };
            gray = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            mono = new GUIStyle(EditorStyles.textArea) { wordWrap = false, richText = false, font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Menlo", "Courier New" }, 11) };
        }

        void OnInspectorUpdate() { if (EditorApplication.isPlaying || NetSimSuite.IsRunning) Repaint(); }
        void OnFocus() { nextReportScan = 0; }

        void OnGUI()
        {
            Styles();
            EditorGUIUtility.labelWidth = Mathf.Clamp(position.width * 0.42f, 150f, 260f);
            var st = NetSimSettings.Instance;
            DrawStatus();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            showNetwork = EditorGUILayout.Foldout(showNetwork, "Network conditions", true, EditorStyles.foldoutHeader);
            if (showNetwork) DrawNetwork(st);
            EditorGUILayout.Space(6);
            tab = GUILayout.Toolbar(tab, Tabs);
            EditorGUILayout.Space(4);
            switch (tab)
            {
                case 0: DrawManual(); break;
                case 1: DrawScenarios(st); break;
                default: DrawReports(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ status
        void DrawStatus()
        {
            string s; MessageType t = MessageType.None;
            var sc = NetSimScenarioRunner.Current;
            if (NetSimSuite.IsRunning) { s = $"Batch running: step {NetSimSuite.TotalSteps - NetSimSuite.RemainingSteps + 1}/{NetSimSuite.TotalSteps}: {NetSimSuite.CurrentStep}" + (sc != null ? $"\n{sc.Status}" : ""); t = MessageType.Info; }
            else if (sc != null) { s = $"Scenario running: {sc.Name} — {sc.Status}  (sim time {Time.time:F0} s, {NetSim.Clients.Count(c => c.joined)} clients)"; t = MessageType.Info; }
            else if (NetSimSession.Manual && NetSim.Active) s = $"Manual session: {NetSim.Clients.Count(c => c.joined)} clients, {NetSim.PendingMessages} messages in flight";
            else if (NetSimSession.Starting) s = "Waiting for the world to finish initializing…";
            else if (EditorApplication.isPlaying) s = "Play mode — NetSim is not running";
            else s = "Edit mode";
            EditorGUILayout.HelpBox(s, t);
        }

        // ------------------------------------------------------------------ network
        void DrawNetwork(NetSimSettings st)
        {
            var c = st.config;
            EditorGUI.BeginChangeCheck();
            int cur = st.MatchingPreset();
            var names = NetSimSettings.Presets.Select(p => p.name).Concat(new[] { "Custom" }).ToArray();
            int sel = EditorGUILayout.Popup("Preset", cur < 0 ? names.Length - 1 : cur, names);
            if (sel != cur && sel < NetSimSettings.Presets.Length) st.Apply(NetSimSettings.Presets[sel]);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("Latency (ms)", "One-way delay of each hop (client -> server, server -> client), chosen uniformly per message. The range is the jitter: messages of different kinds can overtake each other."));
                float min = EditorGUILayout.FloatField(c.latencyMin * 1000f, GUILayout.MinWidth(40));
                GUILayout.Label("to", GUILayout.Width(18));
                float max = EditorGUILayout.FloatField(c.latencyMax * 1000f, GUILayout.MinWidth(40));
                c.latencyMin = Mathf.Max(0, min) / 1000f; c.latencyMax = Mathf.Max(c.latencyMin, max / 1000f);
            }
            c.eventDropRate = Percent("Event loss", "Chance that a SendCustomNetworkEvent delivery to one receiver is lost.", c.eventDropRate);
            c.serializationDropRate = Percent("Synced-variable loss", "Chance that a serialization (Manual / Continuous) delivery to one receiver is lost.", c.serializationDropRate);
            c.objectSyncDropRate = Percent("Object-sync loss", "Chance that a position update of a VRCObjectSync / pickup to one receiver is lost.", c.objectSyncDropRate);
            c.seed = EditorGUILayout.IntField(new GUIContent("Random seed", "Same seed = same delays, losses and bot choices (frame timing still varies a little)."), c.seed);
            c.continuousInterval = EditorGUILayout.FloatField(new GUIContent("Continuous interval (s)", "Send interval of Continuous sync and object sync."), c.continuousInterval);
            c.lateJoinStateDelay = EditorGUILayout.FloatField(new GUIContent("Late-join delay (s)", "When a joining client receives the server's state."), c.lateJoinStateDelay);
            c.carry = EditorGUILayout.Toggle(new GUIContent("Bots carry objects", "Scenario bots pick up, walk and drop objects; off = teleport them."), c.carry);
            c.trace = EditorGUILayout.TextField(new GUIContent("Trace paths", "Comma-separated parts of hierarchy paths; network traffic of matching objects is logged."), c.trace);
            if (EditorGUI.EndChangeCheck())
            {
                NetSimSettings.Save();
                if (NetSim.Active && NetSimSession.Manual) ApplyLive(c);
            }
            EditorGUILayout.LabelField(NetSim.Active && NetSimSession.Manual ? "Changes apply to the running session immediately (except the seed)." : "Used by the next manual session or scenario run.", gray);
        }

        static void ApplyLive(NetSimConfig c)
        {
            var n = NetSim.Config;
            n.latencyMin = c.latencyMin; n.latencyMax = c.latencyMax; n.eventDropRate = c.eventDropRate; n.serializationDropRate = c.serializationDropRate;
            n.objectSyncDropRate = c.objectSyncDropRate; n.continuousInterval = c.continuousInterval; n.lateJoinStateDelay = c.lateJoinStateDelay; n.carry = c.carry;
            NetSim.TraceSubstrings.Clear(); if (!string.IsNullOrEmpty(c.trace)) NetSim.TraceSubstrings.AddRange(c.trace.Split(','));
        }

        static float Percent(string label, string tip, float v) => EditorGUILayout.Slider(new GUIContent(label + " (%)", tip), v * 100f, 0f, 50f) / 100f;

        // ------------------------------------------------------------------ manual test
        void DrawManual()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.LabelField("Open your world scene and enter play mode (ClientSim). Then start NetSim here: you play as <b>C0</b> in the Game view, and add more clients, each a copy of the world in the same editor.", wrap);
                if (GUILayout.Button("Enter Play Mode", GUILayout.Height(28))) EditorApplication.isPlaying = true;
                return;
            }
            if (NetSimScenarioRunner.Running) { EditorGUILayout.LabelField("A scenario is running (see the status above). Manual control is available when it is done.", wrap); return; }
            if (!NetSim.Active)
            {
                using (new EditorGUI.DisabledScope(NetSimSession.Starting))
                    if (GUILayout.Button(NetSimSession.Starting ? "Starting…" : "Start NetSim", GUILayout.Height(28))) NetSimSession.Start();
                EditorGUILayout.LabelField("Starts the simulated network with the conditions above. You are C0 (the ClientSim player).", gray);
                return;
            }

            // clients
            EditorGUILayout.LabelField("Clients", bold);
            foreach (var c in NetSim.Clients.ToList())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool master = c.player != null && c.player.playerId == NetSim.MasterId && c.joined;
                    string state = !c.joined ? "left" : !c.ready ? "loading…" : "";
                    var label = $"C{c.index}  {(c.isOriginal ? "You (ClientSim)" : c.player?.displayName)}  p{c.player?.playerId}{(master ? "  ★ master" : "")}{(state != "" ? "  — " + state : "")}";
                    using (new EditorGUI.DisabledScope(!c.joined)) GUILayout.Label(label, NetSimView.Shown == c.index ? EditorStyles.boldLabel : EditorStyles.label);
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(!c.joined || !c.ready))
                    {
                        if (GUILayout.Toggle(NetSimView.Shown == c.index, new GUIContent("View", "Show this client's copy in the Game view"), EditorStyles.miniButtonLeft, GUILayout.Width(48)) && NetSimView.Shown != c.index) NetSimView.Show(c.index);
                        using (new EditorGUI.DisabledScope(c.isOriginal))
                            if (GUILayout.Button(new GUIContent("Leave", master ? "The master leaves: ownership moves to the new master" : "This client leaves the instance"), EditorStyles.miniButtonRight, GUILayout.Width(48))) NetSimSession.Leave(c);
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(NetSimSession.Loading))
                {
                    if (GUILayout.Button(new GUIContent("+ Client", "A player who is in the instance from the start (receives the current state like any joiner)"))) NetSimSession.AddClient(false);
                    if (GUILayout.Button(new GUIContent("+ Late joiner", "Same as + Client; named 'Late' so it is easy to find in reports"))) NetSimSession.AddClient(true);
                }
                if (GUILayout.Toggle(NetSimView.Shown < 0, new GUIContent("View all", "Draw every client's copy on top of each other"), "Button") && NetSimView.Shown >= 0) NetSimView.Show(-1);
            }
            EditorGUILayout.LabelField("The Game view shows " + (NetSimView.Shown < 0 ? "all copies overlaid" : $"C{NetSimView.Shown}'s copy") + ". Your mouse / keyboard input in the Game view always acts as C0. Each copy is a separate scene in the Hierarchy (labelled C1, C2, …).", gray);

            EditorGUILayout.Space(6);
            DrawSelected();
            EditorGUILayout.Space(6);
            DrawInteractables();
            EditorGUILayout.Space(6);
            DrawCompare();

            EditorGUILayout.Space(6);
            showLog = EditorGUILayout.Foldout(showLog, "NetSim log", true);
            if (showLog)
            {
                var lines = NetSim.Log.Skip(Math.Max(0, NetSim.Log.Count - 25));
                EditorGUILayout.TextArea(string.Join("\n", lines), mono);
            }
            EditorGUILayout.Space(8);
            if (GUILayout.Button("Stop NetSim")) NetSimSession.Stop();
        }

        void DrawSelected()
        {
            EditorGUILayout.LabelField("Selected object", bold);
            var sel = Selection.activeGameObject;
            var owner = sel != null ? NetSimView.ClientOfScene(sel.scene) : null;
            if (sel == null || owner == null) { EditorGUILayout.LabelField("Select an object of the world in the Hierarchy (in any client's copy) to compare it across clients and act on it as any client.", gray); return; }
            var path = NetSim.PathOf(sel.transform);
            EditorGUILayout.LabelField(path + $"   (selected in C{owner.index})", wrap);
            int nUb = sel.GetComponents<UdonBehaviour>().Length;
            var joined = NetSim.Clients.Where(c => c.joined && c.ready).ToList();
            var master = NetSimSession.Master;

            // one row per client: owner, synced values, actions
            for (int comp = 0; comp < Math.Max(1, nUb); comp++)
            {
                if (nUb > 1) EditorGUILayout.LabelField($"UdonBehaviour #{comp}", EditorStyles.miniBoldLabel);
                Dictionary<string, object> masterVals = null;
                if (master != null && master.ubByKey.TryGetValue(path + "#" + comp, out var mub) && mub) masterVals = NetSim.CaptureSynced(mub);
                foreach (var c in joined)
                {
                    c.goByPath.TryGetValue(path, out var go);
                    c.ubByKey.TryGetValue(path + "#" + comp, out var ub);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        int ownerId = go != null ? OwnerOf(c, go) : -1;
                        GUILayout.Label($"C{c.index}", c == owner ? EditorStyles.boldLabel : EditorStyles.label, GUILayout.Width(28));
                        GUILayout.Label(new GUIContent($"owner p{ownerId}", "Owner as this client currently sees it"), EditorStyles.miniLabel, GUILayout.Width(60));
                        var vals = ub ? NetSim.CaptureSynced(ub) : null;
                        if (go == null) GUILayout.Label("(missing in this copy)", red);
                        else if (vals == null || vals.Count == 0) GUILayout.Label(go.activeInHierarchy ? "active" : "inactive", EditorStyles.miniLabel);
                        else
                        {
                            var text = string.Join("  ", vals.Select(kv => $"{kv.Key}={Fmt(kv.Value)}"));
                            bool differs = masterVals != null && c != master && vals.Any(kv => !masterVals.TryGetValue(kv.Key, out var mv) || Fmt(mv) != Fmt(kv.Value));
                            GUILayout.Label(new GUIContent(text, text), differs ? red : EditorStyles.label, GUILayout.MinWidth(60));
                        }
                        GUILayout.FlexibleSpace();
                        if (go != null)
                        {
                            if (comp == 0 && GUILayout.Button(new GUIContent("Select", "Select this client's copy of the object"), EditorStyles.miniButtonLeft, GUILayout.Width(46))) { Selection.activeGameObject = go; EditorGUIUtility.PingObject(go); }
                            using (new EditorGUI.DisabledScope(ub == null || !NetSimSession.IsInteractive(ub)))
                                if (GUILayout.Button(new GUIContent("Interact", "Interact with it as this client (like clicking it in VRChat)"), EditorStyles.miniButtonMid, GUILayout.Width(56))) NetSimSession.SendEvent(c, ub, "_interact");
                            if (comp == 0 && GUILayout.Button(new GUIContent("Own", "Networking.SetOwner as this client"), EditorStyles.miniButtonRight, GUILayout.Width(40))) { var cc = c; NetSim.As(cc, () => Networking.SetOwner(cc.player, go)); }
                        }
                    }
                }
            }
            if (nUb > 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    eventName = EditorGUILayout.TextField(new GUIContent("Custom event", "Runs this event of the UdonBehaviour (#0) in the chosen client's copy, as that client"), eventName);
                    var labels = joined.Select(c => "C" + c.index).ToArray();
                    int idx = Mathf.Clamp(joined.FindIndex(c => c.index == interactClient), 0, Math.Max(0, joined.Count - 1));
                    idx = EditorGUILayout.Popup(idx, labels, GUILayout.Width(50));
                    if (joined.Count > 0) interactClient = joined[idx].index;
                    if (GUILayout.Button("Run", GUILayout.Width(40)) && joined.Count > 0 && joined[idx].ubByKey.TryGetValue(path + "#0", out var eub) && eub)
                        NetSimSession.SendEvent(joined[idx], eub, eventName);
                }
            }
        }

        static int OwnerOf(NetSimClient c, GameObject go)
        {
            int id = -1;
            NetSim.As(c, () => { var p = Networking.GetOwner(go); id = p != null ? p.playerId : -1; });
            return id;
        }

        void DrawInteractables()
        {
            showInteractables = EditorGUILayout.Foldout(showInteractables, "Interact as another client", true);
            if (!showInteractables) return;
            var joined = NetSim.Clients.Where(c => c.joined && c.ready).ToList();
            if (joined.Count == 0) return;
            int idx = Mathf.Clamp(joined.FindIndex(c => c.index == interactClient), 0, joined.Count - 1);
            idx = EditorGUILayout.Popup("Client", idx, joined.Select(c => $"C{c.index} {(c.isOriginal ? "You" : c.player?.displayName)}").ToArray());
            var cl = joined[idx]; interactClient = cl.index;
            var list = NetSimSession.Interactables(cl);
            if (list.Count == 0) { EditorGUILayout.LabelField("Nothing this client can interact with right now.", gray); return; }
            foreach (var ub in list.Take(40))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var p = cl.keyByUb.TryGetValue(ub, out var k) ? k : ub.name;
                    GUILayout.Label(new GUIContent(p, ub.InteractionText), EditorStyles.label, GUILayout.MinWidth(80));
                    if (GUILayout.Button("Interact", EditorStyles.miniButton, GUILayout.Width(60))) NetSimSession.SendEvent(cl, ub, "_interact");
                }
            }
            if (list.Count > 40) EditorGUILayout.LabelField($"… and {list.Count - 40} more", gray);
        }

        void DrawCompare()
        {
            EditorGUILayout.LabelField("Compare clients", bold);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Compare now", "Synced variables of the master vs every other client"))) Compare();
                autoCompare = GUILayout.Toggle(autoCompare, "every 2 s", GUILayout.Width(80));
                if (GUILayout.Button(new GUIContent("Write snapshot report", "Differences, network stats and the NetSim log to Logs/NetSim/snapshot_*.md"))) { lastSnapshot = NetSimSession.WriteSnapshot(); nextReportScan = 0; }
            }
            if (autoCompare && EditorApplication.timeSinceStartup > nextAutoCompare) { nextAutoCompare = EditorApplication.timeSinceStartup + 2; Compare(); }
            if (diffs != null)
            {
                if (diffs.Count == 0) GUILayout.Label($"No differences in synced variables ({diffsAt}).", green);
                else
                {
                    GUILayout.Label($"{diffs.Count} differences ({diffsAt}){(NetSim.PendingMessages > 0 ? " — some may still be in flight" : "")}:", red);
                    foreach (var d in diffs.Take(30)) EditorGUILayout.LabelField("• " + d, wrap);
                    if (diffs.Count > 30) EditorGUILayout.LabelField($"… and {diffs.Count - 30} more", gray);
                }
            }
            if (lastSnapshot != null)
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Snapshot: " + Path.GetFileName(lastSnapshot), gray);
                    if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(50))) EditorUtility.OpenWithDefaultApp(lastSnapshot);
                }
        }

        void Compare() { diffs = NetSimSession.CompareAll(); diffsAt = $"sim time {Time.time:F1} s"; }

        static string Fmt(object v)
        {
            if (v is Array a) return "[" + string.Join(",", a.Cast<object>().Select(x => Convert.ToString(x, CultureInfo.InvariantCulture))) + "]";
            return v == null ? "null" : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ scenarios
        void DrawScenarios(NetSimSettings st)
        {
            var names = NetSimScenarioRunner.ScenarioNames.OrderBy(n => Builtin(n) ? 0 : 1).ThenBy(n => n).ToList();
            if (names.Count == 0) { EditorGUILayout.LabelField("No scenarios found.", gray); return; }
            EditorGUI.BeginChangeCheck();
            int idx = Math.Max(0, names.IndexOf(st.scenario));
            idx = EditorGUILayout.Popup("Scenario", idx, names.Select(n => Builtin(n) ? n + "  (built-in)" : n).ToArray());
            st.scenario = names[idx];
            var desc = NetSimScenarioRunner.DescriptionOf(st.scenario);
            if (desc != "") EditorGUILayout.LabelField(desc, gray);
            if (Builtin(st.scenario))
            {
                st.clients = EditorGUILayout.IntSlider(new GUIContent("Clients", "In total, including C0"), st.clients, 2, 8);
                if (st.scenario != "join-check")
                {
                    st.playSeconds = EditorGUILayout.FloatField(new GUIContent("Play time (s)", "Simulated seconds the random bot plays"), st.playSeconds);
                    st.actionProbability = EditorGUILayout.Slider(new GUIContent("Bot activity", "Chance per client per second to press a button and to interact"), st.actionProbability, 0f, 1f);
                }
            }
            st.timeScale = EditorGUILayout.Slider(new GUIContent("Speed (time scale)", "The editor runs this much faster than real time during a scenario (network delays stay in simulated time)"), st.timeScale, 1f, 10f);
            if (EditorGUI.EndChangeCheck()) NetSimSettings.Save();

            bool busy = NetSimScenarioRunner.Running || NetSimSuite.IsRunning;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(busy))
                {
                    if (GUILayout.Button(new GUIContent("Run now", "Runs in play mode (entered if needed) and stays in play mode afterwards so you can look at the clients"), GUILayout.Height(26)))
                        NetSimScenarioRunner.RunInPlayMode(CurrentStep(st));
                    if (GUILayout.Button(new GUIContent("Add to batch", "Adds this scenario with the current network conditions to the batch list"), GUILayout.Height(26)))
                    { st.batch = st.batch.Concat(new[] { CurrentStep(st) }).ToArray(); NetSimSettings.Save(); }
                }
                using (new EditorGUI.DisabledScope(!busy))
                    if (GUILayout.Button("Stop", GUILayout.Height(26), GUILayout.Width(60)))
                    {
                        if (NetSimSuite.IsRunning) NetSimSuite.Cancel();
                        else if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                    }
            }
            if (!Builtin(st.scenario)) EditorGUILayout.LabelField("Write your own scenarios in C# (see the Scenario Template sample); they appear in this list.", gray);

            // batch
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Batch (unattended: each step in a fresh play session)", bold);
            var batch = st.batch.ToList();
            int remove = -1, up = -1;
            for (int i = 0; i < batch.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent($"{i + 1}. {Describe(batch[i])}", batch[i]), EditorStyles.label, GUILayout.MinWidth(80));
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(busy))
                    {
                        if (i > 0 && GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(22))) up = i;
                        if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(22))) remove = i;
                    }
                }
            }
            if (remove >= 0) { batch.RemoveAt(remove); st.batch = batch.ToArray(); NetSimSettings.Save(); }
            if (up > 0) { (batch[up - 1], batch[up]) = (batch[up], batch[up - 1]); st.batch = batch.ToArray(); NetSimSettings.Save(); }
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(busy))
            {
                if (GUILayout.Button(new GUIContent("+ this scenario × every preset", "Adds the selected scenario once per network preset")))
                {
                    var add = new List<string>();
                    foreach (var p in NetSimSettings.Presets)
                    {
                        var cfg = st.CloneConfig(); cfg.latencyMin = p.latMin; cfg.latencyMax = p.latMax; cfg.eventDropRate = cfg.serializationDropRate = cfg.objectSyncDropRate = p.drop;
                        add.Add(NetSimSuite.FormatStep(st.scenario, Slug(p.name), cfg, st.timeScale));
                    }
                    st.batch = st.batch.Concat(add).ToArray(); NetSimSettings.Save();
                }
                if (batch.Count > 0 && GUILayout.Button("Clear", GUILayout.Width(50))) { st.batch = new string[0]; NetSimSettings.Save(); }
            }
            using (new EditorGUI.DisabledScope(busy || batch.Count == 0))
                if (GUILayout.Button($"Run batch ({batch.Count} steps)", GUILayout.Height(26))) NetSimSuite.Start(batch.ToArray());
            EditorGUILayout.LabelField("A batch keeps running when the editor is in the background and across script reloads. Progress: Logs/NetSim/suite.log.", gray);

            var results = NetSimSuite.Results;
            if (results.Length > 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(NetSimSuite.IsRunning ? "Results so far" : "Results of the last batch", bold);
                foreach (var r in results)
                {
                    var f = r.Split('\t');
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(f[0], VerdictStyle(f[0]), GUILayout.Width(48));
                        GUILayout.Label(new GUIContent(Describe(f.Length > 1 ? f[1] : ""), f.Length > 1 ? f[1] : ""), GUILayout.MinWidth(80));
                        if (f.Length > 2 && File.Exists(f[2]) && GUILayout.Button("View", EditorStyles.miniButton, GUILayout.Width(44))) { ShowReport(f[2]); tab = 2; }
                    }
                }
            }

            EditorGUILayout.Space(8);
            if (GUILayout.Button(new GUIContent("Copy command line for this batch", "A Unity command line that runs the batch on the open scene without the GUI (for scripts, CI or an AI agent)"), EditorStyles.miniButton))
            {
                EditorGUIUtility.systemCopyBuffer = CommandLine(batch.Count > 0 ? batch : new List<string> { CurrentStep(st) });
                ShowNotification(new GUIContent("Copied"));
            }
        }

        static bool Builtin(string n) => n == "join-check" || n == "random-play" || n == "late-join" || n == "master-leave";
        static string CurrentStep(NetSimSettings st) => NetSimSuite.FormatStep(st.scenario, "", st.config, st.timeScale);
        static string Slug(string s) => new string(s.TakeWhile(ch => ch != '(').ToArray()).Trim().ToLowerInvariant().Replace(' ', '-');

        static string Describe(string step)
        {
            if (string.IsNullOrEmpty(step) || step.StartsWith("swap:")) return step;
            NetSimSuite.ParseStep(step, out var sc, out var label, out var c, out var ts);
            float drop = Mathf.Max(c.eventDropRate, c.serializationDropRate, c.objectSyncDropRate);
            return $"{sc}{(label != "" ? " [" + label + "]" : "")} — {c.latencyMin * 1000:F0}-{c.latencyMax * 1000:F0} ms{(drop > 0 ? $", {drop:P0} loss" : "")}, seed {c.seed}, ×{ts:0.#}";
        }

        static string CommandLine(List<string> steps)
        {
            var unity = EditorApplication.applicationPath;   // …/Unity.exe (Windows) or Unity.app (macOS)
            if (Application.platform == RuntimePlatform.OSXEditor) unity += "/Contents/MacOS/Unity";
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            var project = Path.GetDirectoryName(Application.dataPath);
            var runs = string.Join(" ", steps.Select(s => "-netsimRun \"" + s + "\""));
            return $"\"{unity}\" -batchmode -projectPath \"{project}\" -executeMethod Haselab.NetSim.NetSimCli.Run -netsimScene \"{scene}\" {runs} -logFile \"{project}/Logs/NetSim/cli-editor.log\"";
        }

        // ------------------------------------------------------------------ reports
        GUIStyle VerdictStyle(string v) => v == "PASS" ? green : v == "FAIL" || v == "ERROR" ? red : EditorStyles.label;

        void ScanReports()
        {
            if (EditorApplication.timeSinceStartup < nextReportScan && reports != null) return;
            nextReportScan = EditorApplication.timeSinceStartup + 3;
            reports = new List<(string, string, DateTime, string)>();
            if (!Directory.Exists("Logs/NetSim")) return;
            foreach (var f in new DirectoryInfo("Logs/NetSim").GetFiles("*.md").OrderByDescending(f => f.LastWriteTime).Take(40))
            {
                string verdict = "";
                try
                {
                    var text = File.ReadAllText(f.FullName);
                    verdict = text.Contains("**PASS**") ? "PASS" : text.Contains("**FAIL**") ? "FAIL" : text.Contains("**ERROR**") ? "ERROR" : "";
                }
                catch { }
                reports.Add((f.FullName, Path.GetFileNameWithoutExtension(f.Name), f.LastWriteTime, verdict));
            }
        }

        void ShowReport(string path)
        {
            viewedReport = path;
            try { viewedReportText = File.ReadAllText(path); } catch (Exception e) { viewedReportText = e.Message; }
            reportScroll = Vector2.zero;
        }

        void DrawReports()
        {
            ScanReports();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Logs/NetSim (newest first)", bold);
                if (GUILayout.Button("Open folder", EditorStyles.miniButton, GUILayout.Width(80))) OpenReports();
                if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(60))) nextReportScan = 0;
            }
            if (reports.Count == 0) { EditorGUILayout.LabelField("No reports yet. Run a scenario or write a snapshot.", gray); return; }
            foreach (var r in reports)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(r.verdict == "" ? "—" : r.verdict, VerdictStyle(r.verdict), GUILayout.Width(48));
                    GUILayout.Label(r.time.ToString("MM-dd HH:mm"), EditorStyles.miniLabel, GUILayout.Width(70));
                    GUILayout.Label(r.name, r.path == viewedReport ? EditorStyles.boldLabel : EditorStyles.label, GUILayout.MinWidth(60));
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("View", EditorStyles.miniButtonLeft, GUILayout.Width(44))) ShowReport(r.path);
                    if (GUILayout.Button("Open", EditorStyles.miniButtonRight, GUILayout.Width(44))) EditorUtility.OpenWithDefaultApp(r.path);
                }
            }
            if (viewedReport != null)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField(Path.GetFileName(viewedReport), bold);
                reportScroll = EditorGUILayout.BeginScrollView(reportScroll, GUILayout.MinHeight(200), GUILayout.MaxHeight(600));
                EditorGUILayout.TextArea(viewedReportText, mono, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }
    }

    /// <summary>Labels each client's scene in the Hierarchy ("C1 Remote1 ★") while NetSim runs.</summary>
    [InitializeOnLoad]
    static class NetSimHierarchyLabels
    {
        static GUIStyle style;
        static NetSimHierarchyLabels()
        {
            EditorApplication.hierarchyWindowItemOnGUI += (id, rect) => {
                if (!NetSim.Active) return;
                foreach (var c in NetSim.Clients)
                {
                    if (!c.scene.IsValid() || c.scene.handle != id) continue;
                    if (style == null) style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.35f, 0.7f, 1f) } };
                    bool master = c.player != null && c.player.playerId == NetSim.MasterId;
                    var text = $"C{c.index} {(c.isOriginal ? "You" : c.player?.displayName)}{(master ? " ★" : "")}{(c.joined ? "" : " (left)")}{(NetSimView.Shown == c.index ? "  [viewing]" : "")}";
                    GUI.Label(new Rect(rect.x, rect.y, rect.width - 24, rect.height), text, style);
                }
            };
        }
    }
}
#endif

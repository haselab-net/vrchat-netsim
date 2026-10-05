// Multi-client network simulator for Udon worlds (Editor / ClientSim only).
//
// Loads the world scene additively once per simulated remote client (each copy in its own
// physics scene) and routes Udon networking between the copies through a simulated server
// with configurable latency, jitter (reordering) and loss:
//   - SendCustomNetworkEvent (All / Others / Owner / Self, with [NetworkCallable] parameters)
//   - Manual / Continuous synced variables (RequestSerialization), rejected when the sender
//     is not the authoritative owner at the server (lost writes)
//   - Ownership (SetOwner / GetOwner / IsOwner / OnOwnershipTransferred), master migration
//   - Late join (a new copy receives the server's latest state of every behaviour)
//   - Minimal VRCObjectSync emulation for objects moved by test bots (MoveSynced)
// Networking.LocalPlayer / IsMaster / VRCPlayerApi.isLocal are answered per client, based on
// the scene of the UdonBehaviour currently executing.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace Haselab.NetSim
{
    [Serializable]
    public class NetSimConfig
    {
        public float latencyMin = 0.05f;      // one-way, seconds (sim time)
        public float latencyMax = 0.15f;      // jitter => reordering
        public float eventDropRate = 0f;      // probability a network event delivery is lost
        public float serializationDropRate = 0f; // probability a serialization delivery is lost
        public float objectSyncDropRate = 0f;
        public float continuousInterval = 0.2f;
        public float lateJoinStateDelay = 0.3f; // when the late joiner receives the state snapshot
        public int seed = 1;
        public string trace = "";   // comma separated path substrings to trace
        public bool carry = true;     // bots carry objects (pickup -> walk -> drop); false = teleport
    }

    public class NetSimClient
    {
        public int index;
        public VRCPlayerApi player;
        public Scene scene;
        public bool isOriginal;
        public bool joined = true;
        public bool ready;   // scene loaded and indexed; messages that arrive earlier wait (VRChat buffers them)
        public readonly Dictionary<string, UdonBehaviour> ubByKey = new Dictionary<string, UdonBehaviour>();
        public readonly Dictionary<UdonBehaviour, string> keyByUb = new Dictionary<UdonBehaviour, string>();
        public readonly Dictionary<string, GameObject> goByPath = new Dictionary<string, GameObject>();
        public readonly Dictionary<string, int> ownerView = new Dictionary<string, int>();
        public readonly HashSet<string> pendingSerialization = new HashSet<string>();
        public readonly Dictionary<string, string> lastContinuousHash = new Dictionary<string, string>();
        public override string ToString() => $"C{index}(p{player?.playerId})";
    }

    public static class NetSim
    {
        public static NetSimConfig Config = new NetSimConfig();
        public static readonly List<NetSimClient> Clients = new List<NetSimClient>();
        public static bool Active { get; private set; }
        public static int MasterId;
        public static readonly Dictionary<string, int> ServerOwner = new Dictionary<string, int>();
        public static readonly Dictionary<string, Dictionary<string, object>> ServerState = new Dictionary<string, Dictionary<string, object>>();
        static readonly Dictionary<string, long> ServerSeq = new Dictionary<string, long>();          // per behaviour, increases on every accepted serialization
        static readonly Dictionary<(int, string), long> appliedSeq = new Dictionary<(int, string), long>(); // (client, key) -> last applied
        static long serverSeqCounter;
        public static readonly Dictionary<string, int> Stats = new Dictionary<string, int>();
        /// <summary>Networking.SetOwner calls per object path (to find objects whose ownership is requested in a loop).</summary>
        public static readonly Dictionary<string, int> SetOwnerCalls = new Dictionary<string, int>();
        public static string SetOwnerHotSpots(int n = 5) => string.Join(", ", SetOwnerCalls.OrderByDescending(k => k.Value).Take(n).Select(k => $"{k.Key}={k.Value}"));
        public static readonly List<string> Log = new List<string>();

        static System.Random rng;
        static Harmony harmony;
        static readonly List<(float t, long seq, Action a)> queue = new List<(float, long, Action)>();
        static long seqCounter;
        static readonly Stack<NetSimClient> ctxStack = new Stack<NetSimClient>();
        static NetSimDriver driver;
        static string scenePath;

        // saved originals
        static Func<VRCPlayerApi> origLocalPlayer; static Func<bool> origIsMaster; static Func<VRCPlayerApi> origGetMaster;
        static Func<GameObject, VRCPlayerApi> origGetOwner; static Func<VRCPlayerApi, GameObject, bool> origIsOwner; static Action<VRCPlayerApi, GameObject> origSetOwner;
        static Func<VRCPlayerApi, bool> origIsMasterDelegate; static Action<UdonBehaviour> origRsHook;
        static Delegate origProxy;
        static Func<VRCPlayerApi, VRCPlayerApi.TrackingDataType, VRCPlayerApi.TrackingData> origGetTrackingData;
        static Func<VRC_Pickup, VRCPlayerApi> origGetCurrentPlayer;
        static MethodInfo clientSimProxy;

        const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static readonly FieldInfo fProgram = typeof(UdonBehaviour).GetField("_program", BF);
        static readonly FieldInfo fHasDoneStart = typeof(UdonBehaviour).GetField("_hasDoneStart", BF);
        static readonly FieldInfo fHasError = typeof(UdonBehaviour).GetField("_hasError", BF);

        public static NetSimClient Current => ctxStack.Count > 0 ? ctxStack.Peek() : (Clients.Count > 0 ? Clients[0] : null);
        static void Count(string k, int n = 1) { Stats.TryGetValue(k, out var v); Stats[k] = v + n; }
        public static void Note(string s) { var line = $"[{Time.time:F2}] {s}"; Log.Add(line); Debug.Log("[NetSim] " + line); }

        // ---------------------------------------------------------------- lifecycle
        public static void Start(NetSimConfig cfg, int masterClientIndex = 0)
        {
            if (Active) Stop();
            Config = cfg ?? new NetSimConfig();
            rng = new System.Random(Config.seed);
            TraceSubstrings.Clear(); if (!string.IsNullOrEmpty(Config.trace)) TraceSubstrings.AddRange(Config.trace.Split(','));
            carries.Clear(); remoteTargets.Clear();
            Clients.Clear(); ServerSeq.Clear(); appliedSeq.Clear(); pathCache.Clear(); lastObjSync.Clear(); objSyncCache.Clear(); lastEventArrival.Clear(); ServerOwner.Clear(); ServerState.Clear(); Stats.Clear(); SetOwnerCalls.Clear(); Log.Clear(); queue.Clear(); ctxStack.Clear();
            scenePath = SceneManager.GetActiveScene().path;

            var c0 = new NetSimClient { index = 0, player = Networking.LocalPlayer, scene = SceneManager.GetActiveScene(), isOriginal = true };
            Clients.Add(c0);
            Index(c0);
            c0.ready = true;
            foreach (var kv in c0.ubByKey)
            {
                if (kv.Value == null || kv.Value.SyncMethod == Networking.SyncType.None) continue;
                var st0 = Capture(kv.Value); if (st0 == null || st0.Count == 0) continue;
                ServerState[kv.Key] = st0; ServerSeq[kv.Key] = ++serverSeqCounter;
            }
            MasterId = c0.player.playerId;

            try { InstallHooks(); }
            catch { UninstallHooks(); throw; }
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
            var go = new GameObject("NetSimDriver"); driver = go.AddComponent<NetSimDriver>();
            Active = true;
            Note($"started, master=p{MasterId}, cfg latency={Config.latencyMin}-{Config.latencyMax}s eventDrop={Config.eventDropRate} serDrop={Config.serializationDropRate}");
        }

        public static void Stop()
        {
            if (!Active) return;
            Active = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            UninstallHooks();
            if (driver) UnityEngine.Object.Destroy(driver.gameObject);
            Note("stopped");
        }

        // Adds a remote client: spawns a ClientSim remote player and loads a copy of the scene.
        public static NetSimClient AddClient(string name, bool lateJoin)
        {
            var player = SpawnRemotePlayer(name);
            var p = new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics3D);
            var scene = EditorSceneManager.LoadSceneInPlayMode(scenePath, p);
            var c = new NetSimClient { index = Clients.Count, player = player, scene = scene };
            Clients.Add(c);
            driver.StartCoroutine(driver.AfterSceneLoaded(c, lateJoin));
            Note($"client {c} joining (lateJoin={lateJoin})");
            return c;
        }

        // Called after Awake/OnEnable and before Start of the loaded objects. UdonSharp only copies the
        // proxy (C#) field values into the UdonBehaviours of scenes that are open when entering play mode,
        // so do the same for the additively loaded copy.
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Clients.Any(c => !c.isOriginal && c.scene == scene)) return;
            if (usmBuildInternal != null)
            {
                try
                {
                    usmBuildInternal.Invoke(null, new object[] { false, scene.GetRootGameObjects() });
                    Note($"scene copy loaded (handle {scene.handle}), UdonSharp scene processing applied");
                    return;
                }
                catch (Exception e) { Debug.LogWarning("[NetSim] UdonSharp scene processing failed, falling back: " + (e.InnerException ?? e).Message); }
            }
            int n = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var proxy in root.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true))
                {
                    try { UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(proxy); n++; }
                    catch (Exception e) { Debug.LogWarning($"[NetSim] CopyProxyToUdon failed on {proxy.name}: {e.Message}"); }
                }
            Note($"scene copy loaded (handle {scene.handle}), {n} UdonSharp behaviours initialized from proxies");
        }

        internal static void OnClientSceneReady(NetSimClient c, bool lateJoin)
        {
            foreach (var root in c.scene.GetRootGameObjects())
            {
                foreach (var cam in root.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
                foreach (var al in root.GetComponentsInChildren<AudioListener>(true)) al.enabled = false;
                foreach (var a in root.GetComponentsInChildren<AudioSource>(true)) a.mute = true;
            }
            Index(c);
            c.ready = true;
            // ownership view: what the server knows (objects never transferred stay with the master)
            foreach (var kv in ServerOwner) c.ownerView[kv.Key] = kv.Value;
            // every joining client receives the latest state the server holds (VRChat join sync)
            Schedule(Config.lateJoinStateDelay, () => {
                int n = 0;
                foreach (var kv in ServerState.ToList()) { if (DeliverState(c, kv.Key, kv.Value, "join", ServerSeq.TryGetValue(kv.Key, out var sq) ? sq : 0)) n++; }
                Note($"join snapshot delivered to {c}: {n} behaviours (lateJoin={lateJoin})");
            });
        }

        public static void RemoveClient(NetSimClient c)
        {
            if (c.isOriginal) throw new InvalidOperationException("cannot remove the ClientSim local client");
            c.joined = false;
            // stop the leaving copy first so it does not react to its own leave
            foreach (var root in c.scene.GetRootGameObjects()) root.SetActive(false);
            int leaving = c.player.playerId;
            var remaining = Clients.Where(x => x.joined).ToList();
            bool masterLeft = leaving == MasterId;
            if (masterLeft)
            {
                // objects never transferred were implicitly owned by the old master: make that explicit before the master changes
                var networked = Clients[0].ubByKey.Keys.Select(GoPathOfKey).Concat(Clients[0].goByPath.Where(kv => kv.Value && IsObjectSynced(kv.Value)).Select(kv => kv.Key)).Distinct().ToList();
                foreach (var x in remaining) foreach (var path in networked) if (!x.ownerView.ContainsKey(path)) x.ownerView[path] = leaving;
                foreach (var path in networked) if (!ServerOwner.ContainsKey(path)) ServerOwner[path] = leaving;
            }
            if (masterLeft) MasterId = remaining.OrderBy(x => x.index).First().player.playerId;
            Note($"client {c} leaving (masterLeft={masterLeft}, newMaster=p{MasterId})");
            // server hands every object owned by the leaver to the (new) master
            var owned = ServerOwner.Where(kv => kv.Value == leaving).Select(kv => kv.Key).ToList();
            RemoveRemotePlayer(c.player);   // dispatches OnPlayerLeft to every copy
            foreach (var path in owned) BroadcastOwnership(path, MasterId, null);
        }

        // ---------------------------------------------------------------- indexing
        static void Index(NetSimClient c)
        {
            c.ubByKey.Clear(); c.keyByUb.Clear(); c.goByPath.Clear();
            foreach (var root in c.scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var path = PathOf(t);
                    c.goByPath[path] = t.gameObject;
                    var ubs = t.GetComponents<UdonBehaviour>();
                    for (int i = 0; i < ubs.Length; i++) { var k = path + "#" + i; c.ubByKey[k] = ubs[i]; c.keyByUb[ubs[i]] = k; }
                }
        }
        // hierarchy path; siblings sharing a name get "~n" (n = ordinal among same-named siblings) so the path is unique
        static readonly Dictionary<Transform, string> pathCache = new Dictionary<Transform, string>();
        public static string PathOf(Transform t)
        {
            if (pathCache.TryGetValue(t, out var cached)) return cached;
            var start = t;
            var sb = new StringBuilder(Segment(t));
            while ((t = t.parent) != null) sb.Insert(0, Segment(t) + "/");
            return pathCache[start] = sb.ToString();
        }
        static string Segment(Transform t)
        {
            var p = t.parent;
            int ordinal = 0, same = 0;
            if (p != null)
            {
                for (int i = 0; i < p.childCount; i++) { var ch = p.GetChild(i); if (ch.name != t.name) continue; if (ch == t) ordinal = same; same++; }
            }
            else
            {
                foreach (var r in t.gameObject.scene.GetRootGameObjects()) { if (r.name != t.name) continue; if (r.transform == t) ordinal = same; same++; }
            }
            return same > 1 ? t.name + "~" + ordinal : t.name;
        }
        public static NetSimClient ClientOf(GameObject go) { if (go == null) return Current; var s = go.scene; foreach (var c in Clients) if (c.scene == s) return c; return Current; }
        static string KeyOf(UdonBehaviour ub, out NetSimClient c) { c = ClientOf(ub.gameObject); if (c == null) return null; if (!c.keyByUb.TryGetValue(ub, out var k)) { Index(c); c.keyByUb.TryGetValue(ub, out k); } return k; }
        static string GoPathOfKey(string key) => key.Substring(0, key.LastIndexOf('#'));
        static NetSimClient ClientByPlayer(int id) => Clients.FirstOrDefault(x => x.player != null && x.player.playerId == id && x.joined);

        // ---------------------------------------------------------------- context
        public static void PushContext(NetSimClient c)
        {
            ctxStack.Push(c);
            ApplyIsLocal(c);
        }
        public static void PopContext()
        {
            if (ctxStack.Count > 0) ctxStack.Pop();
            ApplyIsLocal(Current);
        }
        static void ApplyIsLocal(NetSimClient c)
        {
            if (c == null) return;
            foreach (var x in Clients) if (x.player != null) x.player.isLocal = x == c;
        }
        public static void As(NetSimClient c, Action a) { PushContext(c); try { a(); } finally { PopContext(); } }

        // Harmony: every Udon event runs in the context of the client owning its scene
        static void RunProgramPrefix(UdonBehaviour __instance, out bool __state)
        {
            __state = false;
            if (!Active) return;
            var c = ClientOf(__instance.gameObject);
            if (c == null) return;
            PushContext(c); __state = true;
        }
        static bool UdonManagerSceneLoadedPrefix(UdonManager __instance, Scene scene, LoadSceneMode loadSceneMode)
        {
            if (!Active || loadSceneMode != LoadSceneMode.Additive) return true;
            var t = typeof(UdonManager);
            t.GetProperty("IsSceneLoading", BF).GetSetMethod(true).Invoke(__instance, new object[] { true });
            try
            {
                var directory = new Dictionary<GameObject, HashSet<UdonBehaviour>>();
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                    {
                        var ubs = tr.GetComponents<UdonBehaviour>();
                        if (ubs.Length > 0) directory[tr.gameObject] = new HashSet<UdonBehaviour>(ubs);
                    }
                var dirs = (System.Collections.IDictionary)t.GetField("_sceneUdonBehaviourDirectories", BF).GetValue(__instance);
                dirs[scene] = directory;
                var preload = typeof(UdonBehaviour).GetMethod("PreloadUdonProgram", BF);
                var init = typeof(UdonBehaviour).GetMethod("InitializeUdonContent", BF);
                var netSupported = typeof(UdonBehaviour).GetProperty("IsNetworkingSupported", BF);
                foreach (var set in directory.Values) foreach (var ub in set) preload.Invoke(ub, new object[] { __instance });
                foreach (var set in directory.Values) foreach (var ub in set) { netSupported.SetValue(ub, true); init.Invoke(ub, null); }
            }
            finally
            {
                t.GetMethod("PurgeSerializationCaches", BF).Invoke(__instance, null);
                (t.GetField("OnUdonReady", BF).GetValue(__instance) as Action)?.Invoke();
                t.GetProperty("IsSceneLoading", BF).GetSetMethod(true).Invoke(__instance, new object[] { false });
            }
            Count("sim.additiveSceneInit");
            return false;
        }

        static MethodInfo usmBuildInternal;
        static bool SkipWhenActivePrefix() => !Active;

        static bool DescriptorAwakePrefix(MonoBehaviour __instance)
        {
            if (!Active || Clients.Count == 0 || __instance.gameObject.scene == Clients[0].scene) return true;
            __instance.enabled = false;
            return false;
        }
        static Exception RunProgramFinalizer(Exception __exception, bool __state)
        {
            if (__state) PopContext();
            return __exception;
        }

        // ---------------------------------------------------------------- hooks
        static void InstallHooks()
        {
            var nt = typeof(Networking);
            origLocalPlayer = (Func<VRCPlayerApi>)nt.GetField("_LocalPlayer", BF).GetValue(null);
            origIsMaster = (Func<bool>)nt.GetField("_IsMaster", BF).GetValue(null);
            origGetMaster = (Func<VRCPlayerApi>)nt.GetField("_GetMaster", BF).GetValue(null);
            origGetOwner = (Func<GameObject, VRCPlayerApi>)nt.GetField("_GetOwner", BF).GetValue(null);
            origIsOwner = (Func<VRCPlayerApi, GameObject, bool>)nt.GetField("_IsOwner", BF).GetValue(null);
            origSetOwner = (Action<VRCPlayerApi, GameObject>)nt.GetField("_SetOwner", BF).GetValue(null);
            var fIsMasterDel = typeof(VRCPlayerApi).GetField("_isMasterDelegate", BF);
            origIsMasterDelegate = (Func<VRCPlayerApi, bool>)fIsMasterDel.GetValue(null);
            var pHook = typeof(UdonBehaviour).GetProperty("RequestSerializationHook", BF);
            origRsHook = (Action<UdonBehaviour>)pHook.GetValue(null);
            var pProxy = typeof(NetworkCalling).GetProperty("SendCustomNetworkEventProxy", BF);
            origProxy = (Delegate)pProxy.GetValue(null);
            clientSimProxy = origProxy.Method;

            nt.GetField("_LocalPlayer", BF).SetValue(null, (Func<VRCPlayerApi>)(() => Current?.player ?? origLocalPlayer()));
            nt.GetField("_IsMaster", BF).SetValue(null, (Func<bool>)(() => Current != null && Current.player.playerId == MasterId));
            nt.GetField("_GetMaster", BF).SetValue(null, (Func<VRCPlayerApi>)(() => VRCPlayerApi.GetPlayerById(MasterId)));
            nt.GetField("_GetOwner", BF).SetValue(null, (Func<GameObject, VRCPlayerApi>)HookGetOwner);
            nt.GetField("_IsOwner", BF).SetValue(null, (Func<VRCPlayerApi, GameObject, bool>)((p, g) => p != null && HookGetOwner(g)?.playerId == p.playerId));
            nt.GetField("_SetOwner", BF).SetValue(null, (Action<VRCPlayerApi, GameObject>)HookSetOwner);
            fIsMasterDel.SetValue(null, (Func<VRCPlayerApi, bool>)(p => p != null && p.playerId == MasterId));
            pHook.SetValue(null, (Action<UdonBehaviour>)HookRequestSerialization);
            var dt = pProxy.PropertyType;
            pProxy.SetValue(null, Delegate.CreateDelegate(dt, typeof(NetSim).GetMethod(nameof(HookSendNetworkEvent), BF)));

            // VRC_Pickup.IsHeld / currentPlayer: objects carried by a simulated player are held by that player
            var fCur = typeof(VRC_Pickup).GetField("_GetCurrentPlayer", BF);
            origGetCurrentPlayer = (Func<VRC_Pickup, VRCPlayerApi>)fCur.GetValue(null);
            fCur.SetValue(null, (Func<VRC_Pickup, VRCPlayerApi>)(pk => pk != null && carries.TryGetValue(pk.gameObject, out var cr) ? cr.client.player : origGetCurrentPlayer?.Invoke(pk)));
            // ClientSim remote players have no tracking data; answer with the player's position instead of throwing
            var fTrack = typeof(VRCPlayerApi).GetField("_GetTrackingData", BF);
            origGetTrackingData = (Func<VRCPlayerApi, VRCPlayerApi.TrackingDataType, VRCPlayerApi.TrackingData>)fTrack.GetValue(null);
            fTrack.SetValue(null, (Func<VRCPlayerApi, VRCPlayerApi.TrackingDataType, VRCPlayerApi.TrackingData>)((p, t) => {
                try { return origGetTrackingData(p, t); }
                catch { var pos = Vector3.zero; var rot = Quaternion.identity; try { pos = p.GetPosition(); rot = p.GetRotation(); } catch { } return new VRCPlayerApi.TrackingData(pos + Vector3.up * 1.6f, rot); }
            }));

            harmony = new Harmony("net.haselab.netsim");
            var target = typeof(UdonBehaviour).GetMethod("RunProgram", BF, null, new[] { typeof(uint) }, null);
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(NetSim).GetMethod(nameof(RunProgramPrefix), BF)),
                finalizer: new HarmonyMethod(typeof(NetSim).GetMethod(nameof(RunProgramFinalizer), BF)));
            // UdonSharp's [PostProcessScene] handler always processes the *active* scene (the master's), which
            // re-copies proxy defaults into its running behaviours whenever a copy is loaded. Skip it while
            // simulating and run it for the loaded copy instead (OnSceneLoaded).
            var usm = Type.GetType("UdonSharpEditor.UdonSharpEditorManager, UdonSharp.Editor");
            var onBuild = usm?.GetMethod("OnSceneBuild", BF);
            usmBuildInternal = usm?.GetMethod("OnSceneBuildInternal", BF);
            if (onBuild != null) harmony.Patch(onBuild, prefix: new HarmonyMethod(typeof(NetSim).GetMethod(nameof(SkipWhenActivePrefix), BF)));
            // UdonManager.OnSceneLoaded clears every scheduled (delayed) event and every Update registration of
            // all scenes whenever any scene loads. For an additive copy, only initialize the copy's behaviours.
            var umOnLoaded = typeof(UdonManager).GetMethod("OnSceneLoaded", BF);
            harmony.Patch(umOnLoaded, prefix: new HarmonyMethod(typeof(NetSim).GetMethod(nameof(UdonManagerSceneLoadedPrefix), BF)));
            // Pickup.Drop() called by world code on an object a simulated player carries: end the carry (fires OnDrop like VRChat)
            foreach (var m in typeof(VRC_Pickup).GetMethods(BF | BindingFlags.DeclaredOnly).Where(x => x.Name == "Drop"))
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(NetSim).GetMethod(nameof(PickupDropPrefix), BF)));
            // a scene copy must not replace the world's scene descriptor (it destroys the previous one)
            foreach (var t in new[] { typeof(VRC_SceneDescriptor), typeof(VRC.SDK3.Components.VRCSceneDescriptor) })
            {
                var awake = t.GetMethod("Awake", BF | BindingFlags.DeclaredOnly);
                if (awake != null) harmony.Patch(awake, prefix: new HarmonyMethod(typeof(NetSim).GetMethod(nameof(DescriptorAwakePrefix), BF)));
            }
        }

        static void UninstallHooks()
        {
            var nt = typeof(Networking);
            nt.GetField("_LocalPlayer", BF).SetValue(null, origLocalPlayer);
            nt.GetField("_IsMaster", BF).SetValue(null, origIsMaster);
            nt.GetField("_GetMaster", BF).SetValue(null, origGetMaster);
            nt.GetField("_GetOwner", BF).SetValue(null, origGetOwner);
            nt.GetField("_IsOwner", BF).SetValue(null, origIsOwner);
            nt.GetField("_SetOwner", BF).SetValue(null, origSetOwner);
            typeof(VRCPlayerApi).GetField("_isMasterDelegate", BF).SetValue(null, origIsMasterDelegate);
            typeof(UdonBehaviour).GetProperty("RequestSerializationHook", BF).SetValue(null, origRsHook);
            typeof(NetworkCalling).GetProperty("SendCustomNetworkEventProxy", BF).SetValue(null, origProxy);
            if (origGetTrackingData != null) typeof(VRCPlayerApi).GetField("_GetTrackingData", BF).SetValue(null, origGetTrackingData);
            if (origGetCurrentPlayer != null) typeof(VRC_Pickup).GetField("_GetCurrentPlayer", BF).SetValue(null, origGetCurrentPlayer);
            foreach (var cr in carries.Values.ToList()) EndCarry(cr, false, "stop");
            harmony?.UnpatchAll("net.haselab.netsim");
            foreach (var c in Clients) if (c.player != null) c.player.isLocal = c.isOriginal;
        }

        // ---------------------------------------------------------------- ownership
        static int OwnerIdInView(NetSimClient c, string path) => c.ownerView.TryGetValue(path, out var id) ? id : MasterIdFor(c);
        // objects never transferred are owned by the master (as known by that client)
        static int MasterIdFor(NetSimClient c) => MasterId;

        static VRCPlayerApi HookGetOwner(GameObject go)
        {
            if (go == null) return null;
            var c = ClientOf(go);
            var path = PathOf(go.transform);
            return VRCPlayerApi.GetPlayerById(OwnerIdInView(c, path));
        }

        static void HookSetOwner(VRCPlayerApi p, GameObject go)
        {
            if (p == null || go == null) return;
            var c = ClientOf(go);
            var path = PathOf(go.transform);
            int prev = OwnerIdInView(c, path);
            c.ownerView[path] = p.playerId;   // requester assumes ownership immediately
            Trace(path, $"{c} SetOwner {path} -> p{p.playerId} (prev p{prev})");
            if (prev != p.playerId) FireOwnershipTransferred(c, path, p);
            Count("setOwner"); SetOwnerCalls.TryGetValue(path, out var n); SetOwnerCalls[path] = n + 1;
            // request to the server
            var from = c;
            Schedule(Lat(), () => {
                ServerOwner[path] = p.playerId;   // server applies in arrival order
                BroadcastOwnership(path, p.playerId, from);
            });
        }

        static void BroadcastOwnership(string path, int ownerId, NetSimClient requester)
        {
            ServerOwner[path] = ownerId;
            foreach (var c in Clients.Where(x => x.joined))
            {
                var cc = c;
                Schedule(Lat(), () => {
                    if (!cc.joined) return;
                    if (ServerOwner.TryGetValue(path, out var cur) && cur != ownerId) return; // superseded
                    int prev = OwnerIdInView(cc, path);
                    cc.ownerView[path] = ownerId;
                    Trace(path, $"{cc} ownership broadcast {path} -> p{ownerId} (prev p{prev})");
                    if (prev != ownerId) FireOwnershipTransferred(cc, path, VRCPlayerApi.GetPlayerById(ownerId));
                });
            }
        }

        static void FireOwnershipTransferred(NetSimClient c, string path, VRCPlayerApi newOwner)
        {
            if (!c.goByPath.TryGetValue(path, out var go) || go == null) return;
            foreach (var ub in go.GetComponents<UdonBehaviour>())
                As(c, () => ub.RunEvent("_onOwnershipTransferred", ("player", newOwner)));
        }

        // ---------------------------------------------------------------- serialization
        static void HookRequestSerialization(UdonBehaviour ub)
        {
            try { origRsHook?.Invoke(ub); } catch { }
            if (!Active || ub == null || ub.SyncMethod == Networking.SyncType.None) return;
            var key = KeyOf(ub, out var c);
            if (key == null) return;
            c.pendingSerialization.Add(key);   // VRChat serializes at the end of the frame
        }

        internal static void FlushSerializations()
        {
            foreach (var c in Clients.Where(x => x.joined))
            {
                if (c.pendingSerialization.Count == 0) continue;
                var keys = c.pendingSerialization.ToList(); c.pendingSerialization.Clear();
                foreach (var key in keys) SendState(c, key, "manual");
            }
        }

        internal static void TickContinuous()
        {
            foreach (var c in Clients.Where(x => x.joined))
                foreach (var kv in c.ubByKey)
                {
                    var ub = kv.Value; if (ub == null || ub.SyncMethod != Networking.SyncType.Continuous) continue;
                    if (OwnerIdInView(c, GoPathOfKey(kv.Key)) != c.player.playerId) continue;
                    var st = Capture(ub); if (st == null || st.Count == 0) continue;
                    var h = Hash(st);
                    if (c.lastContinuousHash.TryGetValue(kv.Key, out var prev) && prev == h) continue;
                    c.lastContinuousHash[kv.Key] = h;
                    SendState(c, kv.Key, "continuous");
                }
        }

        static void SendState(NetSimClient c, string key, string kind)
        {
            if (!c.ubByKey.TryGetValue(key, out var ub) || ub == null) return;
            var path = GoPathOfKey(key);
            if (OwnerIdInView(c, path) != c.player.playerId) { Count("ser.notOwnerLocal"); return; }  // VRChat ignores RequestSerialization from non-owners
            var state = Capture(ub);
            if (state == null) return;
            As(c, () => ub.RunEvent("_onPreSerialization"));
            state = Capture(ub);
            Count("ser.sent");
            int sender = c.player.playerId;
            Schedule(Lat(), () => {
                int owner = ServerOwner.TryGetValue(path, out var o) ? o : MasterId;
                if (owner != sender) { Count("ser.rejectedAtServer"); Note($"serialization of {key} from p{sender} rejected (owner p{owner})"); return; }
                ServerState[key] = state;
                long seq = ServerSeq[key] = ++serverSeqCounter;
                foreach (var r in Clients.Where(x => x.joined && x != c))
                {
                    var rr = r;
                    if (Drop(Config.serializationDropRate)) { Count("ser.dropped"); continue; }
                    Schedule(Lat(), () => {
                        if (!rr.joined) return;
                        int ownerNow = ServerOwner.TryGetValue(path, out var o2) ? o2 : MasterId;
                        if (ownerNow != sender) { Count("ser.staleDiscarded"); return; }  // newer owner's data supersedes
                        DeliverState(rr, key, state, kind, seq);
                    });
                }
            });
        }

        static Dictionary<string, object> Capture(UdonBehaviour ub)
        {
            var prog = fProgram.GetValue(ub) as IUdonProgram;
            if (prog?.SyncMetadataTable == null) return null;
            var d = new Dictionary<string, object>();
            foreach (var md in prog.SyncMetadataTable.GetAllSyncMetadata())
            {
                var v = ub.GetProgramVariable(md.Name);
                d[md.Name] = v is Array a ? a.Clone() : v;
            }
            return d;
        }

        static bool DeliverState(NetSimClient c, string key, Dictionary<string, object> state, string kind, long seq)
        {
            if (!c.ready) { Schedule(0.05f, () => DeliverState(c, key, state, kind, seq)); return true; }
            if (!c.ubByKey.TryGetValue(key, out var ub) || ub == null) return false;
            // the owner never receives (stale) data for an object it owns
            if (OwnerIdInView(c, GoPathOfKey(key)) == c.player.playerId) { Count("ser.ownerIgnored"); Trace(key, $"{c} ignores data for owned {key} seq{seq}"); return false; }
            // VRChat never applies an older serialization over a newer one
            if (appliedSeq.TryGetValue((c.index, key), out var last) && seq <= last) { Count("ser.outOfOrderDiscarded"); return false; }
            appliedSeq[(c.index, key)] = seq;
            if (TraceSubstrings.Count > 0) Trace(key, $"{c} applies {kind} {key} seq{seq}: {Hash(state)}");
            foreach (var kv in state) ub.SetProgramVariable(kv.Key, kv.Value is Array a ? a.Clone() : kv.Value);
            Count("ser.delivered." + kind);
            RunWhenStarted(c, ub, "_onDeserialization");
            return true;
        }

        static void RunWhenStarted(NetSimClient c, UdonBehaviour ub, string ev)
        {
            if (ub == null) return;
            if (!(bool)fHasDoneStart.GetValue(ub)) { Schedule(0.05f, () => RunWhenStarted(c, ub, ev)); return; }
            As(c, () => ub.RunEvent(ev));
        }

        static string Hash(Dictionary<string, object> st)
        {
            var sb = new StringBuilder();
            foreach (var kv in st) { sb.Append(kv.Key).Append('='); if (kv.Value is Array a) foreach (var x in a) sb.Append(x).Append(','); else sb.Append(kv.Value); sb.Append(';'); }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- network events
        static void HookSendNetworkEvent(IUdonEventReceiver receiver, VRC.Udon.Common.Interfaces.NetworkEventTarget target, string eventName, Memory<object> parameters)
        {
            var ub = receiver as UdonBehaviour;
            if (!Active || ub == null) { origProxy.DynamicInvoke(receiver, target, eventName, parameters); return; }
            var key = KeyOf(ub, out var sender);
            var args = parameters.ToArray();
            Count("ev.sent");
            switch (target)
            {
                case VRC.Udon.Common.Interfaces.NetworkEventTarget.All:
                    RunLocalEvent(sender, ub, eventName, args);
                    foreach (var r in Clients.Where(x => x.joined && x != sender)) SendEventTo(sender, r, key, eventName, args);
                    break;
                case VRC.Udon.Common.Interfaces.NetworkEventTarget.Others:
                    foreach (var r in Clients.Where(x => x.joined && x != sender)) SendEventTo(sender, r, key, eventName, args);
                    break;
                case VRC.Udon.Common.Interfaces.NetworkEventTarget.Self:
                    RunLocalEvent(sender, ub, eventName, args);
                    break;
                default: // Owner
                    var path = GoPathOfKey(key);
                    if (OwnerIdInView(sender, path) == sender.player.playerId) { RunLocalEvent(sender, ub, eventName, args); break; }
                    if (Drop(Config.eventDropRate)) { Count("ev.dropped"); Note($"event {eventName} to owner dropped"); break; }
                    Schedule(Lat(), () => {   // via server to the authoritative owner
                        int owner = ServerOwner.TryGetValue(path, out var o) ? o : MasterId;
                        var r = ClientByPlayer(owner);
                        if (r == null) { Count("ev.ownerGone"); return; }
                        Schedule(OrderedArrival(sender, r, Lat()), () => DeliverEvent(r, key, eventName, args));
                    });
                    break;
            }
        }

        // network events are reliable and ordered per sender -> receiver (jitter must not reorder them)
        static readonly Dictionary<(int, int), float> lastEventArrival = new Dictionary<(int, int), float>();
        static float OrderedArrival(NetSimClient s, NetSimClient r, float delay)
        {
            var k = (s.index, r.index);
            float t = Time.time + delay;
            if (lastEventArrival.TryGetValue(k, out var last) && t <= last) t = last + 0.0001f;
            lastEventArrival[k] = t;
            return t - Time.time;
        }

        static void SendEventTo(NetSimClient s, NetSimClient r, string key, string eventName, object[] args)
        {
            if (Drop(Config.eventDropRate)) { Count("ev.dropped"); Note($"event {eventName} to {r} dropped"); return; }
            Schedule(OrderedArrival(s, r, Lat() + Lat()), () => DeliverEvent(r, key, eventName, args));   // client -> server -> client
        }

        static void DeliverEvent(NetSimClient r, string key, string eventName, object[] args)
        {
            if (r.joined && !r.ready) { Schedule(0.05f, () => DeliverEvent(r, key, eventName, args)); return; }
            if (!r.joined || !r.ubByKey.TryGetValue(key, out var ub) || ub == null) return;
            Count("ev.delivered");
            Trace(key, $"{r} receives event {eventName} on {key}");
            RunLocalEvent(r, ub, eventName, args);
        }

        static void RunLocalEvent(NetSimClient c, UdonBehaviour ub, string eventName, object[] args)
        {
            As(c, () => {
                try { clientSimProxy.Invoke(null, new object[] { ub, VRC.Udon.Common.Interfaces.NetworkEventTarget.Self, eventName, new Memory<object>(args) }); }
                catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
            });
        }

        // ---------------------------------------------------------------- object sync emulation (for bots)
        // A bot "picks up" an object (ownership transfer) and puts it at pos in its own copy.
        // Other copies follow the owner's transform through TickObjectSync (VRCObjectSync emulation).
        public static void MoveSynced(NetSimClient c, GameObject go, Vector3 pos)
        {
            As(c, () => Networking.SetOwner(c.player, go));   // picking up transfers ownership
            // a hand moves the object in from outside: take it away first so triggers see an exit/enter
            Place(go, pos + Vector3.up * 5f);
            Schedule(0.1f, () => { if (go) Place(go, pos); });
        }

        // Every interval: the owner's position/rotation of each synced object (VRCObjectSync / VRC_Pickup)
        // is sent to the other copies (also replicates script-driven moves such as ResetPosition).
        static readonly Dictionary<string, (Vector3, Quaternion)> lastObjSync = new Dictionary<string, (Vector3, Quaternion)>();
        internal static void TickObjectSync()
        {
            var c0 = Clients[0];
            foreach (var kv in c0.goByPath)
            {
                var g0 = kv.Value;
                if (g0 == null || !IsObjectSynced(g0)) continue;
                var path = kv.Key;
                int ownerId = ServerOwner.TryGetValue(path, out var o) ? o : MasterId;
                var owner = ClientByPlayer(ownerId); if (owner == null) continue;
                if (!owner.goByPath.TryGetValue(path, out var og) || og == null) continue;
                var st = (og.transform.position, og.transform.rotation);
                if (lastObjSync.TryGetValue(path, out var prev) && (prev.Item1 - st.Item1).sqrMagnitude < 1e-6f && Quaternion.Angle(prev.Item2, st.Item2) < 0.1f) continue;
                lastObjSync[path] = st;
                foreach (var r in Clients.Where(x => x.joined && x != owner))
                {
                    if (Drop(Config.objectSyncDropRate)) { Count("objsync.dropped"); continue; }
                    var rr = r; var sender = ownerId;
                    Schedule(Lat() + Lat(), () => {
                        int cur = ServerOwner.TryGetValue(path, out var o2) ? o2 : MasterId;
                        if (cur != sender) return;   // a newer owner's data supersedes
                        if (rr.goByPath.TryGetValue(path, out var g) && g)
                        {
                            if (carries.ContainsKey(g)) return;   // held locally (theft in flight): the holder's hand wins
                            remoteTargets[g] = st;                 // VRCObjectSync interpolates towards the received pose
                        }
                    });
                }
            }
        }
        static readonly Dictionary<GameObject, bool> objSyncCache = new Dictionary<GameObject, bool>();
        static bool IsObjectSynced(GameObject g)
        {
            if (objSyncCache.TryGetValue(g, out var b)) return b;
            b = g.GetComponents<Component>().Any(x => x != null && (x.GetType().Name == "VRCObjectSync" || x is VRC_Pickup));
            return objSyncCache[g] = b;
        }
        static void Place(GameObject go, Vector3 pos) { var rb = go.GetComponent<Rigidbody>(); go.transform.position = pos; if (rb) rb.position = pos; remoteTargets.Remove(go); }

        // ---------------------------------------------------------------- carrying (pickup -> walk -> drop)
        public class Carry
        {
            public NetSimClient client; public GameObject go; public string path;
            public List<Vector3> points = new List<Vector3>(); public int next; public float speed;
            public bool wasKinematic; public Action<bool> done; public float startedAt; public bool finishing;
        }
        static readonly Dictionary<GameObject, Carry> carries = new Dictionary<GameObject, Carry>();
        static readonly Dictionary<GameObject, (Vector3, Quaternion)> remoteTargets = new Dictionary<GameObject, (Vector3, Quaternion)>();
        public static float HandHeight = 1.0f;
        public static bool IsCarrying(NetSimClient c) => carries.Values.Any(x => x.client == c);
        public static NetSimClient CarriedBy(string path) => carries.Values.FirstOrDefault(x => x.path == path)?.client;

        // A simulated player picks up `go` (in its own copy), walks along the NavMesh at `speed` (m/s) holding it at hand
        // height and drops it at `target`. Triggers on the way see continuous movement; other copies follow via object sync.
        public static bool StartCarry(NetSimClient c, GameObject go, Vector3 target, float speed = 2.5f, Action<bool> done = null)
        {
            if (go == null || IsCarrying(c) || carries.ContainsKey(go)) return false;
            var path = PathOf(go.transform);
            // theft: another player holds the same object -> that player drops it (VRChat fires OnDrop for them)
            foreach (var other in carries.Values.Where(x => x.path == path).ToList()) { EndCarry(other, false, "stolen"); Count("carry.stolen"); }
            var cr = new Carry { client = c, go = go, path = path, speed = speed, done = done, startedAt = Time.time };
            var rb = go.GetComponent<Rigidbody>();
            if (rb) { cr.wasKinematic = rb.isKinematic; rb.isKinematic = true; }   // held pickups are kinematic
            // route: up to hand height, along the NavMesh, then into the target
            var start = go.transform.position;
            cr.points.Add(new Vector3(start.x, Mathf.Max(start.y, GroundY(start) + HandHeight), start.z));
            if (UnityEngine.AI.NavMesh.SamplePosition(start, out var hs, 3f, UnityEngine.AI.NavMesh.AllAreas) &&
                UnityEngine.AI.NavMesh.SamplePosition(target, out var ht, 3f, UnityEngine.AI.NavMesh.AllAreas))
            {
                var nav = new UnityEngine.AI.NavMeshPath();
                if (UnityEngine.AI.NavMesh.CalculatePath(hs.position, ht.position, UnityEngine.AI.NavMesh.AllAreas, nav) && nav.status != UnityEngine.AI.NavMeshPathStatus.PathInvalid)
                    foreach (var corner in nav.corners) cr.points.Add(corner + Vector3.up * HandHeight);
            }
            cr.points.Add(target);
            carries[go] = cr;
            remoteTargets.Remove(go);
            As(c, () => Networking.SetOwner(c.player, go));   // picking up transfers ownership
            FirePickupEvent(c, go, "_onPickup");
            Count("carry.started");
            return true;
        }

        static void FirePickupEvent(NetSimClient c, GameObject go, string ev)
        {
            foreach (var ub in go.GetComponents<UdonBehaviour>()) As(c, () => ub.RunEvent(ev));
        }

        static void EndCarry(Carry cr, bool arrived, string why)
        {
            if (!carries.Remove(cr.go)) return;
            if (cr.go)
            {
                var rb = cr.go.GetComponent<Rigidbody>();
                if (rb) rb.isKinematic = cr.wasKinematic;
                if (Active) FirePickupEvent(cr.client, cr.go, "_onDrop");
            }
            Count(arrived ? "carry.delivered" : "carry.ended." + why);
            try { cr.done?.Invoke(arrived); } catch (Exception e) { Debug.LogException(e); }
        }

        static bool PickupDropPrefix(VRC_Pickup __instance)
        {
            if (__instance != null && carries.TryGetValue(__instance.gameObject, out var cr)) { EndCarry(cr, false, "forcedDrop"); return false; }
            return true;
        }

        static float GroundY(Vector3 p) => UnityEngine.AI.NavMesh.SamplePosition(p, out var h, 5f, UnityEngine.AI.NavMesh.AllAreas) ? h.position.y : p.y - HandHeight;

        internal static void TickCarries(float dt)
        {
            foreach (var cr in carries.Values.ToList())
            {
                if (!cr.go || !cr.client.joined || !cr.go.activeInHierarchy) { EndCarry(cr, false, "lost"); continue; }
                if (cr.finishing) continue;
                var t = cr.go.transform; var rb = cr.go.GetComponent<Rigidbody>();
                float step = cr.speed * dt;
                var pos = t.position;
                while (step > 0 && cr.next < cr.points.Count)
                {
                    var goal = cr.points[cr.next];
                    float d = Vector3.Distance(pos, goal);
                    if (d <= step) { pos = goal; step -= d; cr.next++; }
                    else { pos = Vector3.MoveTowards(pos, goal, step); step = 0; }
                }
                if (rb) rb.MovePosition(pos); else t.position = pos;
                if (cr.next >= cr.points.Count) { cr.finishing = true; var c2 = cr; Schedule(0.1f, () => EndCarry(c2, true, "")); }   // let the physics step see the target first
                else if (Time.time - cr.startedAt > 60f) EndCarry(cr, false, "timeout");
            }
        }

        internal static void TickRemoteInterpolation(float dt)
        {
            float k = 1f - Mathf.Exp(-dt * 15f);
            foreach (var kv in remoteTargets.ToList())
            {
                var g = kv.Key; if (!g) { remoteTargets.Remove(g); continue; }
                var rb = g.GetComponent<Rigidbody>();
                var p = Vector3.Lerp(g.transform.position, kv.Value.Item1, k);
                if ((p - kv.Value.Item1).sqrMagnitude < 1e-6f) { p = kv.Value.Item1; remoteTargets.Remove(g); }
                if (rb && rb.isKinematic) rb.MovePosition(p); else { g.transform.position = p; if (rb) rb.position = p; }
                g.transform.rotation = Quaternion.Slerp(g.transform.rotation, kv.Value.Item2, k);
            }
        }

        // ---------------------------------------------------------------- scheduling
        public static void Schedule(float delay, Action a) { queue.Add((Time.time + delay, seqCounter++, a)); }
        static float Lat() => Config.latencyMin + (float)rng.NextDouble() * Math.Max(0f, Config.latencyMax - Config.latencyMin);
        static bool Drop(float p) => p > 0 && rng.NextDouble() < p;

        internal static void ProcessQueue()
        {
            int guard = 0;
            while (guard++ < 10000)
            {
                int best = -1;
                for (int i = 0; i < queue.Count; i++)
                    if (queue[i].t <= Time.time && (best < 0 || queue[i].t < queue[best].t || (queue[i].t == queue[best].t && queue[i].seq < queue[best].seq))) best = i;
                if (best < 0) break;
                var item = queue[best]; queue.RemoveAt(best);
                try { item.a(); } catch (Exception e) { Debug.LogException(e); Count("sim.exception"); }
            }
        }
        public static int PendingMessages => queue.Count;

        // ---------------------------------------------------------------- ClientSim players
        static object ClientSimInstance()
        {
            var t = typeof(VRC.SDK3.ClientSim.ClientSimMain); var a = new object[] { null };
            t.GetMethod("TryGetInstance", BF).Invoke(null, a); return a[0];
        }
        static VRCPlayerApi SpawnRemotePlayer(string name)
        {
            var t = typeof(VRC.SDK3.ClientSim.ClientSimMain);
            t.GetMethod("SpawnRemotePlayer", BF).Invoke(ClientSimInstance(), new object[] { name });
            var list = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()]; VRCPlayerApi.GetPlayers(list);
            return list.Where(p => p.displayName == name).OrderByDescending(p => p.playerId).First();
        }
        static void RemoveRemotePlayer(VRCPlayerApi p)
        {
            typeof(VRC.SDK3.ClientSim.ClientSimMain).GetMethod("RemovePlayer", BF).Invoke(ClientSimInstance(), new object[] { p });
        }

        // ---------------------------------------------------------------- inspection
        public static List<string> HaltedBehaviours()
        {
            var res = new List<string>();
            foreach (var c in Clients) foreach (var kv in c.ubByKey) if (kv.Value != null && (bool)fHasError.GetValue(kv.Value)) res.Add($"{c}: {kv.Key}");
            return res;
        }

        /// <summary>Synced variable values of one behaviour (null when it has none).</summary>
        public static Dictionary<string, object> CaptureSynced(UdonBehaviour ub) => ub == null ? null : Capture(ub);

        public static object Var(NetSimClient c, string goPath, string varName, int comp = 0)
            => c.ubByKey.TryGetValue(goPath + "#" + comp, out var ub) && ub ? ub.GetProgramVariable(varName) : null;

        // Compares synced variables of every behaviour between two clients.
        public static List<string> DiffSynced(NetSimClient a, NetSimClient b)
        {
            var res = new List<string>();
            foreach (var kv in a.ubByKey)
            {
                if (!b.ubByKey.TryGetValue(kv.Key, out var ub2) || kv.Value == null || ub2 == null) continue;
                var s1 = Capture(kv.Value); var s2 = Capture(ub2); if (s1 == null || s2 == null) continue;
                foreach (var v in s1) { var x = Fmt(v.Value); var y = Fmt(s2.TryGetValue(v.Key, out var w) ? w : null); if (x != y) res.Add($"{kv.Key}.{v.Key}: {a}={x} {b}={y}"); }
            }
            return res;
        }

        // Compares what players would see/interact with under `rootPath` between two clients.
        public static List<string> DiffVisual(NetSimClient a, NetSimClient b, string rootName, Func<string, bool> ignore = null)
        {
            var res = new List<string>();
            foreach (var kv in a.goByPath)
            {
                if (!kv.Key.StartsWith(rootName)) continue;
                if (ignore != null && ignore(kv.Key)) continue;
                if (!b.goByPath.TryGetValue(kv.Key, out var g2) || kv.Value == null || g2 == null) continue;
                var g1 = kv.Value;
                if (g1.activeInHierarchy != g2.activeInHierarchy) { res.Add($"{kv.Key} active: {a}={g1.activeInHierarchy} {b}={g2.activeInHierarchy}"); continue; }
                if (!g1.activeInHierarchy) continue;
                var c1 = g1.GetComponent<Collider>(); var c2 = g2.GetComponent<Collider>();
                if (c1 && c2 && c1.enabled != c2.enabled) res.Add($"{kv.Key} collider: {a}={c1.enabled} {b}={c2.enabled}");
                var r1 = g1.GetComponent<Renderer>(); var r2 = g2.GetComponent<Renderer>();
                if (r1 && r2 && r1.enabled != r2.enabled) res.Add($"{kv.Key} renderer: {a}={r1.enabled} {b}={r2.enabled}");
                var t1 = g1.GetComponent<UnityEngine.UI.Text>(); var t2 = g2.GetComponent<UnityEngine.UI.Text>();
                if (t1 && t2 && t1.text != t2.text) res.Add($"{kv.Key} text: {a}='{Short(t1.text)}' {b}='{Short(t2.text)}'");
                var u1 = g1.GetComponent<UdonBehaviour>(); var u2 = g2.GetComponent<UdonBehaviour>();
                if (u1 && u2 && u1.DisableInteractive != u2.DisableInteractive) res.Add($"{kv.Key} interactive: {a}={!u1.DisableInteractive} {b}={!u2.DisableInteractive}");
            }
            return res;
        }
        static string Short(string s) => s == null ? "" : (s.Length > 30 ? s.Substring(0, 30) + "…" : s).Replace("\n", " ");
        static string Fmt(object v) { if (v is Array a) { var sb = new StringBuilder("["); foreach (var x in a) sb.Append(x).Append(','); return sb.Append(']').ToString(); } return v?.ToString() ?? "null"; }

        // ---- tracing: log network activity for objects whose path contains one of these substrings
        public static readonly List<string> TraceSubstrings = new List<string>();
        static void Trace(string path, string msg) { if (TraceSubstrings.Count > 0 && TraceSubstrings.Any(t => path.Contains(t))) Note("TRACE " + msg); }

        public static string StatsString() => string.Join(", ", Stats.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));
    }

    public class NetSimDriver : MonoBehaviour
    {
        float nextContinuous;
        void Update() { if (NetSim.Active) NetSim.ProcessQueue(); }
        void LateUpdate()
        {
            if (!NetSim.Active) return;
            NetSim.FlushSerializations();
            if (Time.time >= nextContinuous) { nextContinuous = Time.time + NetSim.Config.continuousInterval; NetSim.TickContinuous(); NetSim.TickObjectSync(); }
        }
        void FixedUpdate()
        {
            if (!NetSim.Active) return;
            NetSim.TickCarries(Time.fixedDeltaTime);
            NetSim.TickRemoteInterpolation(Time.fixedDeltaTime);
            foreach (var c in NetSim.Clients)
                if (!c.isOriginal && c.joined && c.scene.IsValid() && c.scene.isLoaded)
                    c.scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
        }
        internal System.Collections.IEnumerator AfterSceneLoaded(NetSimClient c, bool lateJoin)
        {
            while (!c.scene.isLoaded) yield return null;
            NetSim.OnClientSceneReady(c, lateJoin);
        }
    }
}
#endif

using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

namespace Haselab.NetSim.SampleWorld
{
    /// <summary>
    /// Debug bot for a multi-client test of the sample world in the real VRChat client (local test builds only;
    /// added to the scene by RealClientTest.BuildAndTest and removed again afterwards).
    ///
    /// Every client runs its own bot as the local player. For playSeconds it clicks random stations and carries the
    /// ball between its home and the goal; then it stops ("done") and keeps logging the state, so the final state of
    /// all clients can be compared with Tools~/analyze_logs.py.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SampleWorldBot : UdonSharpBehaviour
    {
        public string logPrefix = "[NSBOT]";
        [Range(0, 1)] public float actionProbability = 0.4f;
        [Range(0, 1)] public float carryProbability = 0.1f;
        public float tickInterval = 1f;
        public float startDelay = 8f;
        public float playSeconds = 120f;
        public float stateLogInterval = 10f;
        public float carrySpeed = 2.5f;
        public float inUseSeconds = 3f;   // the ball moved this recently by someone else is "in use"
        public float giveUpMin = 8f, giveUpMax = 20f;   // after losing the ball to someone else, leave it alone this long

        readonly string[] stationNames = { "GoodCounter", "RacyCounter", "LostWriteCounter", "SyncedToggle", "EventToggle" };
        UdonBehaviour[] stations;
        UdonBehaviour goodCounter, racyCounter, lostWriteCounter, syncedToggle, goal;
        GameObject eventLamp, ball;
        Rigidbody ballRb;
        VRC_Pickup ballPickup;
        bool ballWasKinematic;
        float ballBackoffUntil;
        Vector3 ballHome, goalPos;

        bool ready, done;
        int myId, actions, carries;
        float playUntil, nextStateLog;
        string lastState = "";

        Vector3 lastBallPos; float lastBallMove = -100f;
        bool carrying; Vector3 carryTarget, carryLastSet; float carryStart, carryArrived;

        void Start()
        {
            SendCustomEventDelayedSeconds(nameof(Tick), startDelay + Random.Range(0f, 3f));
        }

        void Log(string msg)
        {
            Debug.Log(logPrefix + " t=" + Time.time.ToString("F1") + " p=" + myId + " " + msg);
        }

        bool Init()
        {
            if (ready) return true;
            GameObject root = GameObject.Find("SampleWorld");
            if (root == null) return false;
            stations = new UdonBehaviour[stationNames.Length];
            for (int i = 0; i < stationNames.Length; i++) stations[i] = Find(root, stationNames[i]);
            goodCounter = stations[0]; racyCounter = stations[1]; lostWriteCounter = stations[2]; syncedToggle = stations[3];
            goal = Find(root, "Goal");
            Transform lamp = root.transform.Find("EventToggleLamp");
            eventLamp = lamp != null ? lamp.gameObject : null;
            Transform b = root.transform.Find("Ball");
            if (b == null || goal == null || eventLamp == null) return false;
            ball = b.gameObject;
            ballRb = ball.GetComponent<Rigidbody>();
            ballPickup = (VRC_Pickup)ball.GetComponent(typeof(VRC_Pickup));
            ballHome = ball.transform.position;
            goalPos = goal.transform.position;
            lastBallPos = ballHome;
            if (!Utilities.IsValid(Networking.LocalPlayer)) return false;
            myId = Networking.LocalPlayer.playerId;
            playUntil = Time.time + playSeconds;
            ready = true;
            Log("ready name=" + (Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "?")
                + " actionProbability=" + actionProbability + " carryProbability=" + carryProbability + " playSeconds=" + playSeconds);
            return true;
        }

        UdonBehaviour Find(GameObject root, string name)
        {
            Transform t = root.transform.Find(name);
            return t != null ? (UdonBehaviour)t.GetComponent(typeof(UdonBehaviour)) : null;
        }

        public void Tick()
        {
            SendCustomEventDelayedSeconds(nameof(Tick), tickInterval);
            if (!Init()) return;
            TrackBall();
            if (!done && Time.time >= playUntil && !carrying)
            {
                done = true;
                Log("done actions=" + actions + " carries=" + carries);
            }
            if (!done) Act();
            LogState();
        }

        void Act()
        {
            if (Random.value < actionProbability)
            {
                int i = Random.Range(0, stations.Length);
                if (CanInteract(stations[i]))
                {
                    stations[i].SendCustomEvent("_interact");   // what clicking the station does
                    actions++;
                    Log("act click " + stationNames[i]);
                }
            }
            if (!carrying && Random.value < carryProbability && CanCarry())
            {
                bool inGoal = Vector3.Distance(ball.transform.position, goalPos) < 2f;
                StartCarry(inGoal ? ballHome : goalPos);
            }
        }

        // Only what a player could click: active, with an enabled collider, and interactive.
        bool CanInteract(UdonBehaviour ub)
        {
            if (ub == null || !ub.gameObject.activeInHierarchy || ub.DisableInteractive) return false;
            Collider c = ub.GetComponent<Collider>();
            return c != null && c.enabled;
        }

        // ---------------------------------------------------------------- carrying
        void TrackBall()
        {
            Vector3 p = ball.transform.position;
            if ((p - lastBallPos).sqrMagnitude > 0.0004f) { lastBallMove = Time.time; lastBallPos = p; }
        }

        // Only what a player could pick up, and not while someone else is carrying it: bots that keep taking a single
        // object from each other stop the game.
        bool CanCarry()
        {
            if (!ball.activeInHierarchy || Time.time < ballBackoffUntil) return false;
            if (ballPickup != null && !ballPickup.pickupable) return false;
            return Networking.IsOwner(ball) || Time.time - lastBallMove >= inUseSeconds;
        }

        void StartCarry(Vector3 target)
        {
            Networking.SetOwner(Networking.LocalPlayer, ball);   // what picking it up does
            if (ballRb != null) { ballWasKinematic = ballRb.isKinematic; ballRb.isKinematic = true; }   // or it falls
            Vector3 p = ball.transform.position; p.y = 1f;   // hand height
            ball.transform.position = p;
            carryLastSet = p;
            carryTarget = new Vector3(target.x, 1f, target.z);
            carryStart = Time.time; carryArrived = -1f;
            carrying = true; carries++;
            Log("carry start ball to " + (target == goalPos ? "goal" : "home"));
        }

        void EndCarry(string why)
        {
            carrying = false;
            if (ballRb != null) ballRb.isKinematic = ballWasKinematic;
            Log("carry end (" + why + ")");
        }

        void Update()
        {
            if (!carrying) return;
            if (!Networking.IsOwner(ball)) { ballBackoffUntil = Time.time + Random.Range(giveUpMin, giveUpMax); EndCarry("lost ownership"); return; }
            if (!ball.activeInHierarchy) { EndCarry("hidden"); return; }
            Transform t = ball.transform;
            if (Vector3.Distance(t.position, carryLastSet) > 0.5f) { EndCarry("moved by the world"); return; }   // e.g. reset
            if (carryArrived >= 0f)
            {
                if (Time.time - carryArrived > 0.5f) EndCarry("delivered");
                return;
            }
            Vector3 p = Vector3.MoveTowards(t.position, carryTarget, carrySpeed * Time.deltaTime);
            t.position = p; carryLastSet = p;
            if ((p - carryTarget).sqrMagnitude < 0.0001f) carryArrived = Time.time;
            else if (Time.time - carryStart > 30f) EndCarry("timeout");
        }

        // ---------------------------------------------------------------- state log
        void LogState()
        {
            VRCPlayerApi master = Networking.GetOwner(gameObject);
            string s = "good=" + Var(goodCounter, "count") + " racy=" + Var(racyCounter, "count") + " lost=" + Var(lostWriteCounter, "count")
                + " synced=" + Var(syncedToggle, "isOn") + " eventLamp=" + (eventLamp.activeSelf ? "True" : "False")
                + " goals=" + Var(goal, "goals");
            string head = "master=" + (master != null ? master.playerId : -1) + " players=" + VRCPlayerApi.GetPlayerCount();
            if (s != lastState || Time.time >= nextStateLog)
            {
                lastState = s; nextStateLog = Time.time + stateLogInterval;
                Log("state " + head + " " + s);
            }
        }

        string Var(UdonBehaviour ub, string name)
        {
            if (ub == null) return "?";
            object v = ub.GetProgramVariable(name);
            return v == null ? "?" : v.ToString();
        }
    }
}

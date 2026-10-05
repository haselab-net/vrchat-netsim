// NetSim scenarios for the sample world (SampleWorld.unity).
//
// The sample world contains correct sync patterns and planted bugs:
//   GoodCounter       non-owners ask the owner (network event), only the owner writes -> must stay consistent
//   RacyCounter       "take ownership, then write" (bug when two players click at once) -> NetSim must report a difference
//   LostWriteCounter  writes without taking ownership (bug)                  -> NetSim must report a synced-variable difference
//   SyncedToggle      state in a synced variable, changed by the owner only  -> must stay consistent, also for late joiners
//   EventToggle       state only in a network event (bug)                    -> NetSim must report a late-join-only difference
//   Ball + Goal       VRCObjectSync pickup carried into a trigger, owner counts -> goals must be counted once on every client
//
// Open SampleWorld.unity, enter Play Mode, then e.g.:
//   Haselab.NetSim.NetSimScenarioRunner.RunWhenReady("sample-world");
// The report (Logs/NetSim/) ends with "## Verdict" listing whether every expectation above was met.
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.Udon;

namespace Haselab.NetSim.SampleWorld
{
    public static class SampleWorldScenarios
    {
        const string Root = "SampleWorld";
        static readonly string[] Stations = { "GoodCounter", "RacyCounter", "LostWriteCounter", "SyncedToggle", "EventToggle" };
        static readonly Vector3 BallHome = new Vector3(8, 0.3f, -4), GoalPos = new Vector3(8, 1, 5);

        /// <summary>Chance per client per simulated second to click a random station.</summary>
        public static float ClickProbability = 0.4f;
        /// <summary>Chance per client per simulated second to start carrying the ball (when nobody carries it).</summary>
        public static float CarryProbability = 0.15f;

        [NetSimScenario("sample-world")]
        static IEnumerator SampleWorld(NetSimScenarioContext ctx)
        {
            yield return ctx.AddClients(2, "Early");
            yield return ctx.PlayUntil(() => false, 60, Bot);

            // make the planted bugs observable regardless of the random play:
            // two players click the racy counter in the same frame (one of them twice), a non-owner clicks the
            // lost-write counter, and the event-only lamp ends up on.
            // Whether a race leaves the counters different depends on message timing, so race a few times and keep
            // the first round after which the clients still disagree.
            var others = ctx.Joined.Where(c => c != ctx.Master).ToList();
            int racyRounds = 0;
            for (int round = 0; round < 10 && racyRounds == 0; round++)
            {
                var a = others[round % 2]; var b = others[(round + 1) % 2];
                Interact(a, "RacyCounter"); Interact(a, "RacyCounter"); Interact(b, "RacyCounter");
                yield return ctx.Settle(3);
                var values = ctx.Joined.Select(c => NetSim.Var(c, $"{Root}/RacyCounter", "count")).Distinct().Count();
                if (values > 1) racyRounds = round + 1;
            }
            Interact(others[0], "LostWriteCounter");
            yield return ctx.Settle(3);
            if (!Lamp(ctx.Master, "EventToggleLamp")) Interact(ctx.Master, "EventToggle");
            yield return WaitForCarry();
            yield return ctx.Settle(5);

            var late = NetSim.AddClient("Late", true);
            yield return ctx.WaitReady(late);
            yield return ctx.Settle(10);

            ctx.R("## Result");
            ctx.R($"- racy counter disagreed after round: {(racyRounds > 0 ? racyRounds.ToString() : "never")}");
            ctx.ReportConsistency("synced variables after play + late join");
            var early = ctx.Joined.First(c => c != ctx.Master && c != late);
            var lateOnly = ctx.LateJoinOnlyDifferences(early, late, Root);
            ctx.R($"### late joiner only ({lateOnly.Count})");
            foreach (var x in lateOnly) ctx.R($"  - {x}");

            var synced = ctx.Joined.Where(c => c != ctx.Master).SelectMany(c => NetSim.DiffSynced(ctx.Master, c)).ToList();
            bool SyncedDiff(string name) => synced.Any(x => x.StartsWith($"{Root}/{name}#"));
            bool LateDiff(string name) => lateOnly.Any(x => x.StartsWith($"{Root}/{name}"));
            var goals = ctx.Joined.Select(c => NetSim.Var(c, $"{Root}/Goal", "goals")).Select(v => v is int i ? i : -1).ToList();

            var checks = new List<(string, bool)>
            {
                ("GoodCounter is consistent", !SyncedDiff("GoodCounter")),
                ("SyncedToggle is consistent (incl. late joiner)", !SyncedDiff("SyncedToggle") && !LateDiff("SyncedToggleLamp")),
                ("Goal count is consistent and > 0", !SyncedDiff("Goal") && goals.Distinct().Count() == 1 && goals[0] > 0),
                ("bug detected: RacyCounter differs after simultaneous clicks", racyRounds > 0),
                ("bug detected: LostWriteCounter differs between clients", SyncedDiff("LostWriteCounter")),
                ("bug detected: EventToggle lamp differs for the late joiner", LateDiff("EventToggleLamp")),
                ("no halted UdonBehaviours", NetSim.HaltedBehaviours().Count == 0),
            };
            ctx.R("## Verdict");
            ctx.R($"- goals per client: {string.Join(", ", goals)}");
            foreach (var (name, ok) in checks) ctx.R($"- [{(ok ? "x" : " ")}] {name}");
            ctx.R($"- **{(checks.All(c => c.Item2) ? "PASS" : "FAIL")}**");
        }

        // ------------------------------------------------------------------ bot
        static void Bot(NetSimClient c, System.Random rng)
        {
            if (rng.NextDouble() < ClickProbability) Interact(c, Stations[rng.Next(Stations.Length)]);

            if (rng.NextDouble() < CarryProbability && NetSim.CarriedBy($"{Root}/Ball") == null)
            {
                var ball = c.goByPath[$"{Root}/Ball"];
                bool inGoal = Vector3.Distance(ball.transform.position, GoalPos) < 2f;
                NetSim.StartCarry(c, ball, inGoal ? BallHome : GoalPos);
            }
        }

        static IEnumerator WaitForCarry()
        {
            float until = Time.time + 30;
            while (NetSim.CarriedBy($"{Root}/Ball") != null && Time.time < until) yield return null;
        }

        static void Interact(NetSimClient c, string station)
        {
            if (!c.ubByKey.TryGetValue($"{Root}/{station}#0", out var ub) || ub == null) return;
            NetSim.As(c, () => ub.SendCustomEvent("_interact"));
        }

        static bool Lamp(NetSimClient c, string lamp) => c.goByPath.TryGetValue($"{Root}/{lamp}", out var g) && g != null && g.activeSelf;
    }
}
#endif

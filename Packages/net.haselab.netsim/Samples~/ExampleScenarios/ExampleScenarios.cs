// Example NetSim scenarios that work on any Udon world without knowing its game logic.
//
// They drive the world with a "monkey" bot: every simulated second, each client clicks a random active UI button
// and interacts with a random enabled interactable. Afterwards they compare the synced variables of the master with
// every other client. Copy this file and replace MonkeyBot / ProgressSignature with knowledge of your own world to
// get meaningful results (for example "press the start button, then carry item X to place Y").
//
// Play mode, then e.g.:  Haselab.NetSim.NetSimScenarioRunner.RunWhenReady("example-consistency");
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using VRC.Udon;

namespace Haselab.NetSim.Samples
{
    public static class ExampleScenarios
    {
        /// <summary>Chance per client per second to click a button / interact.</summary>
        public static float ActionProbability = 0.3f;

        // 3 clients play randomly under the configured latency / loss; all clients must end with the same synced state.
        [NetSimScenario("example-consistency")]
        static IEnumerator Consistency(NetSimScenarioContext ctx)
        {
            yield return ctx.AddClients(2);
            yield return ctx.PlayUntil(() => false, 120, MonkeyBot);
            yield return ctx.Settle(10);   // let in-flight messages arrive
            ctx.R("## Result");
            int d = ctx.ReportConsistency("synced variables after play + 10 s");
            ctx.R($"- consistent: {d == 0}");
        }

        // A client joins after the others have played for a while; it must catch up with the current state.
        [NetSimScenario("example-latejoin")]
        static IEnumerator LateJoin(NetSimScenarioContext ctx)
        {
            yield return ctx.AddClients(1, "Early");
            yield return ctx.PlayUntil(() => false, 60, MonkeyBot);
            var late = NetSim.AddClient("Late", true);
            yield return ctx.WaitReady(late);
            yield return ctx.Settle(10);
            ctx.R("## Result");
            int d = ctx.ReportConsistency("synced variables after the late join");
            var early = NetSim.Clients.First(c => c.joined && c != ctx.Master && c != late);
            // objects that look different only for the late joiner (state restored from synced variables incompletely)
            var root = ctx.Master.scene.GetRootGameObjects().Select(g => g.name).ToArray();
            int visual = 0;
            foreach (var r in root)
                foreach (var x in ctx.LateJoinOnlyDifferences(early, late, r)) { if (visual++ < 20) ctx.R($"  - late joiner only: {x}"); }
            ctx.R($"- late-join-only visual differences: {visual}");
            ctx.R($"- consistent: {d == 0 && visual == 0}");
        }

        // The master leaves while the game is running; the remaining clients must keep a consistent, progressing game.
        [NetSimScenario("example-masterleave")]
        static IEnumerator MasterLeave(NetSimScenarioContext ctx)
        {
            yield return ctx.AddClients(2);
            var first = NetSim.Clients[1];
            NetSim.MasterId = first.player.playerId;   // a remote client created the instance, so it can leave
            yield return ctx.Settle(1);
            ctx.R($"- initial master: {first}");
            yield return ctx.PlayUntil(() => false, 60, MonkeyBot);
            NetSim.RemoveClient(first);
            yield return ctx.Settle(2);
            ctx.R($"- new master: {ctx.Master}");
            yield return ctx.PlayUntil(() => false, 60, MonkeyBot);
            yield return ctx.Settle(10);
            ctx.R("## Result");
            int d = ctx.ReportConsistency("synced variables after the master left");
            ctx.R($"- consistent: {d == 0}, halted behaviours: {NetSim.HaltedBehaviours().Count}");
        }

        // ------------------------------------------------------------------ monkey bot
        public static void MonkeyBot(NetSimClient c, System.Random rng)
        {
            if (rng.NextDouble() < ActionProbability)
            {
                var buttons = c.goByPath.Values.Where(g => g != null && g.activeInHierarchy)
                    .Select(g => g.GetComponent<Button>()).Where(b => b != null && b.interactable && b.enabled).ToList();
                if (buttons.Count > 0)
                {
                    var b = buttons[rng.Next(buttons.Count)];
                    NetSim.As(c, () => b.onClick.Invoke());
                }
            }
            if (rng.NextDouble() < ActionProbability)
            {
                var interactables = c.ubByKey.Values.Where(u => u != null && u.gameObject.activeInHierarchy && u.enabled && !u.DisableInteractive
                    && u.HasInteractiveEvents()).ToList();
                if (interactables.Count > 0)
                {
                    var u = interactables[rng.Next(interactables.Count)];
                    var col = u.GetComponent<Collider>();
                    if (col != null && col.enabled) NetSim.As(c, () => u.SendCustomEvent("_interact"));   // players can only interact through a collider
                }
            }
        }

        static bool HasInteractiveEvents(this UdonBehaviour u)
        {
            try { return u.IsInteractive; } catch (Exception) { return false; }
        }
    }
}
#endif

// Scenarios that work on any Udon world without code: a random bot presses buttons and interactables on every
// client, then the clients are compared. Parameters (clients, play time, action probability) come from the NetSim
// window / ProjectSettings/NetSimSettings.json.
//
// A world-specific scenario (see the "Scenario Template" sample) finds far more: a bot that knows the game reaches
// states a random bot never does.
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine.UI;

namespace Haselab.NetSim
{
    public static class NetSimBuiltinScenarios
    {
        static NetSimSettings S => NetSimSettings.Instance;
        static int Remotes => Math.Max(1, S.clients - 1);

        [NetSimScenario("join-check", Description = "Clients join without anyone playing; their initial synced state must match.")]
        static IEnumerator JoinCheck(NetSimScenarioContext ctx)
        {
            yield return ctx.AddClients(Remotes);
            yield return ctx.Settle(5);
            ctx.R("## Result");
            int d = ctx.ReportConsistency("synced variables after joining");
            ctx.Check("synced variables match on every client", d == 0);
            ctx.Check("no halted UdonBehaviours", NetSim.HaltedBehaviours().Count == 0);
        }

        [NetSimScenario("random-play", Description = "Every client presses random buttons / interactables; afterwards all clients must agree.")]
        static IEnumerator RandomPlay(NetSimScenarioContext ctx)
        {
            yield return ctx.AddClients(Remotes);
            yield return ctx.PlayUntil(() => false, S.playSeconds, NetSimBots.Random);
            yield return ctx.Settle(10);   // let in-flight messages arrive
            ctx.R("## Result");
            int d = ctx.ReportConsistency("synced variables after play + 10 s");
            ctx.Check("synced variables match on every client", d == 0);
            ctx.Check("no halted UdonBehaviours", NetSim.HaltedBehaviours().Count == 0);
        }

        [NetSimScenario("late-join", Description = "A client joins after the others played; it must see the same world.")]
        static IEnumerator LateJoin(NetSimScenarioContext ctx)
        {
            yield return ctx.AddClients(Math.Max(1, Remotes - 1), "Early");
            yield return ctx.PlayUntil(() => false, S.playSeconds, NetSimBots.Random);
            var late = NetSim.AddClient("Late", true);
            yield return ctx.WaitReady(late);
            yield return ctx.Settle(10);
            ctx.R("## Result");
            int d = ctx.ReportConsistency("synced variables after the late join");
            var early = NetSim.Clients.First(c => c.joined && c != ctx.Master && c != late);
            // objects that look different only for the late joiner (state restored from synced variables incompletely)
            int visual = 0;
            ctx.R("### late joiner only (local-only UI state such as a selected page also shows up here)");
            foreach (var r in ctx.Master.scene.GetRootGameObjects().Select(g => g.name).Distinct())
                foreach (var x in ctx.LateJoinOnlyDifferences(early, late, r)) { if (visual++ < 30) ctx.R($"  - {x}"); }
            ctx.R($"- late-join-only visible differences: {visual}");
            ctx.Check("synced variables match on every client", d == 0);
            ctx.Check("the late joiner sees the same objects (active / collider / renderer / text / interactable)", visual == 0);
            ctx.Check("no halted UdonBehaviours", NetSim.HaltedBehaviours().Count == 0);
        }

        [NetSimScenario("master-leave", Description = "The master leaves in the middle of play; the others must stay consistent and keep working.")]
        static IEnumerator MasterLeave(NetSimScenarioContext ctx)
        {
            yield return ctx.AddClients(Math.Max(2, Remotes));
            var first = NetSim.Clients[1];
            NetSim.MasterId = first.player.playerId;   // a remote client created the instance, so it can leave
            yield return ctx.Settle(1);
            ctx.R($"- initial master: {first}");
            yield return ctx.PlayUntil(() => false, S.playSeconds / 2, NetSimBots.Random);
            NetSim.RemoveClient(first);
            yield return ctx.Settle(2);
            ctx.R($"- new master: {ctx.Master}");
            yield return ctx.PlayUntil(() => false, S.playSeconds / 2, NetSimBots.Random);
            yield return ctx.Settle(10);
            ctx.R("## Result");
            int d = ctx.ReportConsistency("synced variables after the master left");
            ctx.Check("synced variables match on every remaining client", d == 0);
            ctx.Check("no halted UdonBehaviours", NetSim.HaltedBehaviours().Count == 0);
        }
    }

    public static class NetSimBots
    {
        /// <summary>
        /// Knows nothing about the world: with the settings' action probability it clicks a random visible UI button
        /// and interacts with a random interactable the player could reach (active, collider enabled, interactive).
        /// </summary>
        public static void Random(NetSimClient c, System.Random rng)
        {
            float p = NetSimSettings.Instance.actionProbability;
            if (rng.NextDouble() < p)
            {
                var buttons = c.goByPath.Values.Where(g => g != null && g.activeInHierarchy)
                    .Select(g => g.GetComponent<Button>()).Where(b => b != null && b.interactable && b.enabled).ToList();
                if (buttons.Count > 0)
                {
                    var b = buttons[rng.Next(buttons.Count)];
                    NetSim.As(c, () => b.onClick.Invoke());
                }
            }
            if (rng.NextDouble() < p)
            {
                var interactables = NetSimSession.Interactables(c);
                if (interactables.Count > 0) NetSimSession.SendEvent(c, interactables[rng.Next(interactables.Count)], "_interact");
            }
        }
    }
}
#endif

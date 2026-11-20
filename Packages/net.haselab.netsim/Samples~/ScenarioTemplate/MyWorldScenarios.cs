// Template for a scenario that knows your world. Copy this file into an Editor folder of your project (or keep it
// here), rename the scenario and replace the bot and the checks. Scenarios appear in Tools > NetSim > NetSim Window >
// Scenarios as soon as the scripts compile.
//
// The built-in scenarios (random-play, late-join, ...) press random buttons. A bot that plays the game the way people
// do (start a round, pick up the right item, carry it to the right place) reaches far more states and finds far more
// sync bugs.
#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEngine;
using VRC.Udon;

namespace Haselab.NetSim.Samples
{
    public static class MyWorldScenarios
    {
        [NetSimScenario("my-world", Description = "Template: two remote clients play, a late joiner arrives, everybody must agree.")]
        static IEnumerator MyWorld(NetSimScenarioContext ctx)
        {
            // 1. clients: C0 is the ClientSim player, C1.. are copies of the scene
            yield return ctx.AddClients(2);

            // 2. play: the bot is called once per simulated second for every client
            yield return ctx.PlayUntil(() => false, 60, Bot);

            // 3. something special, e.g. two players press the same button in the same frame
            //    Press(NetSim.Clients[1], "World/StartButton"); Press(NetSim.Clients[2], "World/StartButton");
            yield return ctx.Settle(5);

            // 4. a late joiner
            var late = NetSim.AddClient("Late", true);
            yield return ctx.WaitReady(late);
            yield return ctx.Settle(10);

            // 5. checks -> "## Verdict" in the report (PASS / FAIL)
            ctx.R("## Result");
            int d = ctx.ReportConsistency("synced variables at the end");
            ctx.Check("synced variables match on every client", d == 0);
            ctx.Check("no halted UdonBehaviours", NetSim.HaltedBehaviours().Count == 0);
            //   ctx.Check("score is the same everywhere", ctx.Joined.Select(c => NetSim.Var(c, "World/Score", "score")).Distinct().Count() == 1);
        }

        // Replace with knowledge of your world. Only do what a player could do: interact with active objects that have
        // an enabled collider, press visible buttons, carry pickups (NetSim.StartCarry).
        static void Bot(NetSimClient c, System.Random rng)
        {
            NetSimBots.Random(c, rng);
            //   if (rng.NextDouble() < 0.1 && NetSim.CarriedBy("World/Ball") == null)
            //       NetSim.StartCarry(c, c.goByPath["World/Ball"], new Vector3(0, 1, 5));
        }

        // Interact with an object (by hierarchy path) as a client, like clicking it in VRChat.
        static void Press(NetSimClient c, string path)
        {
            if (c.ubByKey.TryGetValue(path + "#0", out var ub) && ub != null) NetSim.As(c, () => ub.SendCustomEvent("_interact"));
        }
    }
}
#endif

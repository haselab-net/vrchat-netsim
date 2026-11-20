# Changelog

## [0.3.0] - 2026-11-21

NetSim is now only the in-editor multi-client sync test, usable without code.

- **NetSim window** (*Tools > NetSim > NetSim Window*): network presets and values; a manual session in play mode
  (start NetSim, add / remove clients, late joiners, master leave); choose whose copy the Game view shows; per-client
  owner and synced variables of the selected object with differences highlighted; interact / take ownership / run an
  event as any client; compare clients and write snapshot reports; run scenarios now or as an unattended batch
  (e.g. one scenario x every preset); report list with verdicts. The Hierarchy labels each client's scene (C1, C2, ...).
- Built-in scenarios that need no code: `join-check`, `random-play`, `late-join`, `master-leave`
  (`NetSimBots.Random` is the random bot).
- Verdicts: `ctx.Check(name, ok)` collects expectations into `## Verdict` with **PASS** / **FAIL** (ERROR when the scenario
  throws). `NetSimScenarioRunner.LastVerdict`, `NetSimSuite.Results`, `NetSimSuite.Finished`; suite.log shows the verdict.
- Command line: `Unity.exe [-batchmode] -executeMethod Haselab.NetSim.NetSimCli.Run -netsimScene ... -netsimRun ...`,
  exit code 0 / 2 (a check failed) / 1 (error), `Logs/NetSim/cli-result.txt`; `-netsimList`.
- Shared settings in `ProjectSettings/NetSimSettings.json` (window, code with `cfg = null`, suite steps and the command
  line). Suite steps may leave fields out (a bare scenario name is a valid step).
- `NetSimScenarioRunner.RunInPlayMode(step)` runs a scenario from edit mode too and stays in play mode afterwards.
- `[NetSimScenario(name, Description = ...)]`.
- The Game view shows one client's copy (C0 by default) instead of all copies overlaid (scene culling masks).
- `Haselab.NetSim.Editor` is auto-referenced: scenarios can live in any `Editor` folder without an assembly definition.
- Suite: a step whose play session ends without a result three times, or whose scenario never starts, is reported as
  ERROR and skipped instead of retried forever.
- In `-batchmode`, ClientSim's player controller (no input device there) is disabled so it does not fill reports with errors.
- Leaving play mode in the middle of a manual session or scenario uninstalls NetSim's hooks and resets the runner.
- **Moved out**: the real VRChat client test (`RealClientTest`, `Tools~/analyze_logs.py`, `Tools~/vrc_clients.py`, the
  sample world's bot) is now the separate optional package `net.haselab.netsim.realclient`.
- **Replaced**: the *Example Scenarios* sample (`example-consistency`, `example-latejoin`, `example-masterleave`) by the
  built-in scenarios above and the *Scenario Template* sample.

## [0.2.0] - 2026-10-06

- Real VRChat client test: `RealClientTest.BuildAndTest(botPrefab, clients, config)` (Build & Test with a temporary
  debug bot, scene backup / restore, recovery after an interrupted build, upload guard, VRChat client path detection),
  `Tools~/analyze_logs.py` (compare the clients' final state, verdict) and `Tools~/vrc_clients.py` (late join, kill a client).
- Sample world: `SampleWorldBot` prefab and *Tools > NetSim > Sample World > Build & Test with Bot (3 clients)*.
- Sample world: the goal no longer counts a ball twice when it is picked up inside the goal.
- Manual: testing in the real VRChat client, network conditions (`NetSimConfig`), one Editor for all clients.

## [0.1.0] - 2026-10-05

- Multi-client simulator: additive scene copies per client, per-client Networking answers, simulated server with
  latency / jitter / loss for network events, synced variables, ownership, late join and master leave.
- Minimal VRCObjectSync emulation and pickup -> walk -> drop carrying.
- Scenario framework (`[NetSimScenario]`, `NetSimScenarioContext`) with consistency / late-join reports and stall detection.
- Unattended suite runner (`NetSimSuite`).
- Example scenarios with a world-agnostic random bot.
- Sample world with correct sync patterns, planted sync bugs and a self-checking scenario (`sample-world`).
- Japanese documentation.

# Changelog

## [Unreleased]

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

# Changelog

## [0.1.0] - Unreleased

- Multi-client simulator: additive scene copies per client, per-client Networking answers, simulated server with
  latency / jitter / loss for network events, synced variables, ownership, late join and master leave.
- Minimal VRCObjectSync emulation and pickup -> walk -> drop carrying.
- Scenario framework (`[NetSimScenario]`, `NetSimScenarioContext`) with consistency / late-join reports and stall detection.
- Unattended suite runner (`NetSimSuite`).
- Example scenarios with a world-agnostic random bot.

# vrchat-netsim

**NetSim** is an Editor-only multi-client network simulator for VRChat world development.
It runs several simulated clients side by side inside Unity Editor (ClientSim) and routes
Udon networking between them through a simulated server with configurable latency, jitter
and packet loss — so you can reproduce and debug sync problems without uploading the world.

> Status: early preview (0.1.0). APIs may change.

## Features

- Each simulated client is an additive copy of the world scene in its own physics scene
- `SendCustomNetworkEvent` (All / Others / Owner / Self, including `[NetworkCallable]` parameters)
- Synced variables (Manual / Continuous, `RequestSerialization`), including lost writes from non-owners
- Ownership (`SetOwner` / `GetOwner` / `IsOwner` / `OnOwnershipTransferred`) and master migration
- Late join and master leave
- Minimal `VRCObjectSync` emulation and pickup → walk → drop carrying by test bots
- Latency / jitter (reordering) / drop rate settings
- Unattended suite runner that survives Play Mode transitions and domain reloads

## Quick start

1. Install the package (below) and import **Example Scenarios** from its Package Manager page.
2. Open your world scene and enter Play Mode (ClientSim).
3. Run a scenario, e.g. from a small Editor script or an "execute C#" tool:

```csharp
Haselab.NetSim.NetSimScenarioRunner.RunWhenReady("example-consistency",
    new Haselab.NetSim.NetSimConfig { latencyMin = 0.1f, latencyMax = 0.3f, eventDropRate = 0.02f });
```

4. Read the report in `Logs/NetSim/`.

The example scenarios use a world-agnostic random bot. Write your own scenarios and bots for your world's
game flow; see the [manual](Packages/net.haselab.netsim/Documentation~/README.md).

## Requirements

- Unity 2022.3
- VRChat SDK3 Worlds 3.10.x (includes ClientSim and UdonSharp)
- Harmony (`0Harmony` bundled with the VRChat SDK)

NetSim lives in an Editor-only assembly and is never included in world builds.

## Installation

### Unity Package Manager (git URL)

`Window > Package Manager > + > Add package from git URL...`

```
https://github.com/haselab-net/vrchat-netsim.git?path=Packages/net.haselab.netsim
```

### VRChat Creator Companion

A VPM listing will be provided after the first release.

## Repository layout

```
Packages/net.haselab.netsim/
  package.json
  Editor/                 NetSim core and suite runner (Editor-only asmdef)
  Samples~/ExampleScenarios/  Example scenarios / test bots (importable from Package Manager)
  Documentation~/         Manual: how it works, writing scenarios, limitations
  CHANGELOG.md
```

## Development

This project was developed with [Claude Code](https://claude.com/claude-code); much of the code and documentation was written by Claude (Anthropic) under human direction and review.

## License

[MIT](LICENSE)

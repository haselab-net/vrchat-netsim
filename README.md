# vrchat-netsim

English | [日本語](README.ja.md)

**NetSim** is an Editor-only multi-client network simulator for VRChat world development.
It runs several simulated clients side by side inside Unity Editor (ClientSim) and routes
Udon networking between them through a simulated server with configurable latency, jitter
and packet loss — so you can reproduce and debug sync problems without uploading the world.

You only start **one** Unity Editor: every simulated client is a copy of the world scene inside the same play session,
so no extra Editor instances or VRChat clients are needed. See [how it works](Packages/net.haselab.netsim/Documentation~/README.md#how-it-works).

> Status: early preview (0.1.0). APIs may change.

## Features

- Each simulated client is an additive copy of the world scene in its own physics scene
- `SendCustomNetworkEvent` (All / Others / Owner / Self, including `[NetworkCallable]` parameters)
- Synced variables (Manual / Continuous, `RequestSerialization`), including lost writes from non-owners
- Ownership (`SetOwner` / `GetOwner` / `IsOwner` / `OnOwnershipTransferred`) and master migration
- Late join and master leave
- Minimal `VRCObjectSync` emulation and pickup → walk → drop carrying by test bots
- Configurable latency / jitter (reordering) / loss per message type ([`NetSimConfig`](Packages/net.haselab.netsim/Documentation~/README.md#network-conditions-latency-jitter-loss))
- Unattended suite runner that survives Play Mode transitions and domain reloads

## Try it with the sample world

1. Install the package (below) and import **Sample World** from its Package Manager page.
2. Open `SampleWorld.unity` from the imported sample and enter Play Mode (ClientSim).
3. Run `Haselab.NetSim.NetSimScenarioRunner.RunWhenReady("sample-world");`
4. Read the report in `Logs/NetSim/`. The world contains correct sync patterns and planted bugs (a write without
   ownership, a "take ownership, then write" race, state that only lives in a network event); the report ends with a
   checklist showing that NetSim finds exactly the bugs. See the [sample's README](Packages/net.haselab.netsim/Samples~/SampleWorld/README.md).

## Quick start (your own world)

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

Append `#v0.1.0` (a release tag) to the URL to pin a version.

### VRChat Creator Companion (VPM)

1. Add the listing: open [this link](https://haselab-net.github.io/vrchat-netsim/) and click **Add to VRChat Creator Companion**,
   or in VCC go to `Settings > Packages > Add Repository` and enter

   ```
   https://haselab-net.github.io/vrchat-netsim/index.json
   ```

2. In your world project's **Manage Project**, add **NetSim - Multi-client Network Simulator for VRChat Worlds**.

## Repository layout

```
Packages/net.haselab.netsim/
  package.json
  Editor/                 NetSim core and suite runner (Editor-only asmdef)
  Samples~/SampleWorld/       Sample world with planted sync bugs and its scenario (importable from Package Manager)
  Samples~/ExampleScenarios/  World-agnostic example scenarios / random test bot (importable from Package Manager)
  Documentation~/         Manual: how it works, writing scenarios, limitations
  CHANGELOG.md
.github/workflows/release.yml  Tag v<version> -> GitHub release (zip) + VPM listing on GitHub Pages
tools/build_listing.py         Builds the VPM listing from the releases
```

## Development

This project was developed with [Claude Code](https://claude.com/claude-code); much of the code and documentation was written by Claude (Anthropic) under human direction and review.

## License

[MIT](LICENSE)

# vrchat-netsim

**NetSim** is an Editor-only multi-client network simulator for VRChat world development.
It runs several simulated clients side by side inside Unity Editor (ClientSim) and routes
Udon networking between them through a simulated server with configurable latency, jitter
and packet loss — so you can reproduce and debug sync problems without uploading the world.

> Status: work in progress. The package skeleton is in place; the simulator code is being ported.

## Features (planned for the first release)

- Each simulated client is an additive copy of the world scene in its own physics scene
- `SendCustomNetworkEvent` (All / Others / Owner / Self, including `[NetworkCallable]` parameters)
- Synced variables (Manual / Continuous, `RequestSerialization`), including lost writes from non-owners
- Ownership (`SetOwner` / `GetOwner` / `IsOwner` / `OnOwnershipTransferred`) and master migration
- Late join and master leave
- Minimal `VRCObjectSync` emulation and pickup → walk → drop carrying by test bots
- Latency / jitter (reordering) / drop rate settings
- Unattended suite runner that survives Play Mode transitions and domain reloads

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
  Documentation~/         Design notes and limitations
  CHANGELOG.md
```

## Development

This project was developed with [Claude Code](https://claude.com/claude-code); much of the code and documentation was written by Claude (Anthropic) under human direction and review.

## License

[MIT](LICENSE)

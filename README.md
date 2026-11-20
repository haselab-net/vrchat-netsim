# vrchat-netsim

English | [日本語](README.ja.md)

**NetSim** tests the networking of a VRChat world inside the Unity Editor. It runs several clients side by side as
copies of your world scene in one play session (ClientSim) and routes Udon networking between them through a simulated
server with configurable latency, jitter and packet loss — so you can reproduce and debug sync problems without
uploading the world or starting more Editors or VRChat clients.

Use it from the **NetSim window** (no code), from C# scenarios, or from the command line (scripts, CI, AI agents).

> Status: early preview (0.3.1). APIs may change.

## Features

- **NetSim window** (*Tools > NetSim > NetSim Window*): network presets, add / remove clients in play mode, choose whose
  copy the Game view shows, interact or take ownership as any client, compare clients, run scenarios and batches,
  read reports
- Built-in scenarios that work on any world: `join-check`, `random-play`, `late-join`, `master-leave` (PASS / FAIL)
- Each client is an additive copy of the world scene in its own physics scene
- `SendCustomNetworkEvent` (All / Others / Owner / Self, including `[NetworkCallable]` parameters)
- Synced variables (Manual / Continuous, `RequestSerialization`), including lost writes from non-owners
- Ownership (`SetOwner` / `GetOwner` / `IsOwner` / `OnOwnershipTransferred`) and master migration
- Late join and master leave
- Minimal `VRCObjectSync` emulation and pickup → walk → drop carrying by test bots
- Configurable latency / jitter (reordering) / loss per message type, shared by the window, code and command line
  (`ProjectSettings/NetSimSettings.json`)
- Unattended batches that survive play-mode transitions and domain reloads; a command line with exit codes

## Quick start

**[Tutorial with screenshots](docs/tutorial/README.md)** — a 20-minute walk through the window with the sample world.

1. Install the package (below) and open your world scene.
2. *Tools > NetSim > NetSim Window*.
3. **Manual test**: *Enter Play Mode* → *Start NetSim* → *+ Client* (twice). Play as C0 in the Game view, select an
   object in the Hierarchy and press *Interact* for C1 and C2, then *Compare now*.
4. **Scenarios**: pick `random-play` and *Run now*; read the result in **Reports** (`Logs/NetSim/`).

To see NetSim find real bugs, import the **Sample World** sample from the package's Package Manager page, open
`SampleWorld.unity` and run the `sample-world` scenario: the world contains correct sync patterns and planted bugs, and
the report's checklist confirms that NetSim found exactly the planted ones.

The built-in scenarios press random buttons. A scenario that plays your game the way people do finds far more; start
from the **Scenario Template** sample and see the [manual](Packages/net.haselab.netsim/Documentation~/README.md).

## For scripts, CI and AI agents

```
Unity.exe -batchmode -projectPath <project> -executeMethod Haselab.NetSim.NetSimCli.Run \
          -netsimScene Assets/MyWorld.unity -netsimRun random-play -netsimRun late-join
```

Exit code 0 = all scenarios passed, 2 = a check failed, 1 = error; reports in `Logs/NetSim/`, one result line per
scenario in `Logs/NetSim/cli-result.txt`. The window's *Copy command line for this batch* writes this command for
you. In an open Editor, `Haselab.NetSim.NetSimScenarioRunner.RunInPlayMode("random-play")` does the same for one
scenario. See [Command line](Packages/net.haselab.netsim/Documentation~/README.md#command-line).

## Requirements

- Unity 2022.3
- VRChat SDK3 Worlds 3.10.x (includes ClientSim and UdonSharp)
- Harmony (`0Harmony` bundled with the VRChat SDK)

NetSim lives in an Editor-only assembly and is never included in world builds.

## Installation

### VRChat Creator Companion (VPM)

1. Add the listing: open [this link](https://haselab-net.github.io/vrchat-netsim/) and click **Add to VRChat Creator Companion**,
   or in VCC go to `Settings > Packages > Add Repository` and enter

   ```
   https://haselab-net.github.io/vrchat-netsim/index.json
   ```

2. In your world project's **Manage Project**, add **NetSim - Multi-client Sync Test for VRChat Worlds**.

### Unity Package Manager (git URL)

`Window > Package Manager > + > Add package from git URL...`

```
https://github.com/haselab-net/vrchat-netsim.git?path=Packages/net.haselab.netsim
```

Append `#v0.3.1` (a release tag) to the URL to pin a version.

## Optional: test in real VRChat clients

NetSim is fast but simplified. The separate package **NetSim Real Client Test** (`net.haselab.netsim.realclient`) runs a
debug bot in several real VRChat clients (SDK *Build & Test*) and compares their logs — see its
[README](Packages/net.haselab.netsim.realclient/README.md). It is not needed for anything above. (Up to 0.2.0 it was
part of the NetSim package.)

## Repository layout

```
Packages/net.haselab.netsim/            NetSim: in-editor multi-client sync test
  Editor/                               simulator, scenarios, window, command line (Editor-only asmdef)
  Samples~/SampleWorld/                 world with planted sync bugs + its scenario
  Samples~/ScenarioTemplate/            starting point for your own scenario
  Documentation~/                       manual
Packages/net.haselab.netsim.realclient/ optional: real VRChat client test (bot + Build & Test, Tools~/*.py)
.github/workflows/release.yml           tag v<version> -> GitHub release (both zips) and the VPM listing on GitHub Pages
tools/build_listing.py                  builds the VPM listing from the releases
```

## Development

This project was developed with [Claude Code](https://claude.com/claude-code). Much of the code and documentation was
written by Claude (Anthropic) under human direction and review.

## License

[MIT](LICENSE)

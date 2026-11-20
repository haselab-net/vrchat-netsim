# NetSim Real Client Test

English | [日本語](README.ja.md)

Optional companion of [NetSim](../net.haselab.netsim/Documentation~/README.md): a multi-client test in **real VRChat
clients** with a debug bot. NetSim (the in-editor simulation) does not need this package; until 0.2.0 it was part of it.

Install it like NetSim (VCC listing, or the git URL with `?path=Packages/net.haselab.netsim.realclient`).
The `Tools~` folder (`analyze_logs.py`, `vrc_clients.py`) is inside this package
(`Packages/net.haselab.netsim.realclient/Tools~`, or under `Library/PackageCache/` when installed from a git URL).

NetSim is fast but simplified. For the final check, run the same kind of test in the real VRChat client: the VRChat SDK
"Build & Test" starts several clients on this PC, and a **bot** inside the world plays on every client and writes what
it does and sees to the VRChat log. A script then compares the clients' logs.

| Part | Role |
|---|---|
| Your bot (Udon / UdonSharp prefab) | Runs on every client as the local player; acts, then writes `state` lines with the synced world state |
| `RealClientTest.BuildAndTest(botPrefab, clients, config)` | Adds the bot to the open scene, runs Build & Test with N clients (Force Non-VR), restores the scene afterwards |
| `RealClientBuildGuard` | Refuses any other build (upload) while a bot is in the scene |
| `Tools~/analyze_logs.py` | Reads the client logs, lists each client's last state and every key that differs; `--must-match` gives a verdict |
| `Tools~/vrc_clients.py` | Lists the running clients, launches more into the same instance (late join), kills one (e.g. the master) |

Requirements: Windows, Steam running, VRChat installed and started at least once, and the VRChat SDK logged in
(VRChat SDK > Show Control Panel). A run takes real time (the sample bot plays for 2 minutes).

## Writing a bot

A bot is an ordinary UdonSharp behaviour on a prefab (`[UdonBehaviourSyncMode(None)]`). Every client runs its own copy
as `Networking.LocalPlayer`. It should:

- find the world's objects at runtime (`GameObject.Find`, `transform.Find`): the prefab cannot reference scene objects;
- act like a player: `SendCustomEvent("_interact")` on interactables, take ownership before moving a pickup and move it
  at hand height every frame, and leave objects another player is moving alone;
- write log lines in this format (prefix `[NSBOT]` by default):

```
[NSBOT] t=<Time.time> p=<local playerId> ready ...
[NSBOT] t=... p=... act <what>
[NSBOT] t=... p=... carry start <what>   /   carry end (<reason>)
[NSBOT] t=... p=... state key=value key=value ...   (synced world state as this client sees it; on change and every 10 s)
[NSBOT] t=... p=... done                            (the bot stopped acting; keep writing state lines)
```

Lessons from real runs:

- Interact only with what a player could: `activeInHierarchy`, an enabled collider and not `DisableInteractive`.
  Otherwise the bot presses things players cannot, and world-side guards (e.g. "ignore clicks within 2 s") still apply.
- Wait until the world has initialized (`startDelay`, or a "ready" flag of the world) and check
  `Utilities.IsValid(Networking.LocalPlayer)`; acting earlier can halt behaviours.
- Carrying by setting the transform does not raise `OnPickup` / `OnDrop` / `OnPickupUseDown`, and
  `VRC_Pickup.IsHeld` stays false; world logic that relies on them is not exercised. Set the Rigidbody kinematic while
  carrying (restore it afterwards); triggers on the way still fire. Do not carry objects with `pickupable = false`.
- End a carry when ownership is lost, the object is hidden, or the world moved it (e.g. a reset).
- Bots fighting over a single object can stop the game: leave objects someone else moved in the last few seconds alone,
  and back off for a while after losing one.
- UdonSharp cannot click a UI `Button`; call the method the button would call. The UI itself is then not tested.

Only put values into `state` that should be equal on every client (no per-client counters). Copy the sample world's
`SampleWorldBot.cs` as a starting point. Public fields can be overridden per run with
`config: "actionProbability=0.5;playSeconds=300"`.

## Running

```csharp
Haselab.NetSim.RealClient.RealClientTest.BuildAndTest(botPrefab, 3, "playSeconds=180");
```

or select the bot prefab and use *Tools > NetSim > Real Client Test > Build & Test with Selected Bot Prefab*.
Then, from the package's `Tools~` folder (`Packages/net.haselab.netsim.realclient/Tools~` for a VCC install, under
`Library/PackageCache/` for a git URL install):

```
python analyze_logs.py --since "2026-10-06 10:28" --watch --must-match good,synced,goals --out report.md
python vrc_clients.py launch 1        # optional: a late joiner
python vrc_clients.py kill 1          # optional: the master (player 1) leaves
```

Notes:

- The SDK starts the clients from its saved VRChat path. If that is not set (e.g. on a new PC), `BuildAndTest` fills it in
  from the Steam libraries, or stops with a message when VRChat is not installed.
- `python vrc_clients.py killall` closes every VRChat process, including one you are playing in; use `kill <player>` to
  close only test clients.
- All clients run on one PC, so network latency is almost zero: this tests real ownership, sync and timing of VRChat,
  but not latency or loss. Use NetSim for those.
- To make one particular client leave (e.g. the master), build with 1 client and add the others with
  `python vrc_clients.py launch 2` (6 s apart), so that every client has its own log file and can be killed by player id.
- The SDK saves the open scene before building. `BuildAndTest` backs the file up and restores it afterwards, so
  **unsaved changes in the open scene are lost**. Save first.
- Clients started in the same second write to the same log file; the tools group lines by the player id the bot writes.
- Extra clients log `SteamApi_Init returned false` or `VRCNP: Failed to create server`; these come from running several
  clients on one PC and do not affect the world.
- Keep the scene under version control: the restore copies a backup file, and a file broken by a crash during the build
  is easiest to recover from git.
- If UdonSharp stops compiling because of an old error in the Console, clear the Console and recompile
  (`UdonSharpProgramAsset.CompileAllCsPrograms`).
- If something interrupted a build and a bot was left in the scene, remove it with
  *Tools > NetSim > Real Client Test > Remove Bots From Scene*; the build guard blocks uploads until then.

## Sample: Sample World Bot

Import **Sample World** from the NetSim package first, then **Sample World Bot** from this package.

`SampleWorldBot.prefab` is a bot for NetSim's Sample World in the real VRChat client. Each client's bot clicks random stations and carries the ball for 2 minutes, then stops and keeps
logging `state good=.. racy=.. lost=.. synced=.. eventLamp=.. goals=..`.

1. Start Steam, log in to the VRChat SDK (VRChat SDK > Show Control Panel) and save `SampleWorld.unity`.
2. *Tools > NetSim > Sample World > Build & Test with Bot (3 clients)*.
3. Optionally, about a minute later, add a late joiner: `python vrc_clients.py launch 1` (in the package's `Tools~`).
4. `python analyze_logs.py --since "<start time>" --watch --must-match good,synced,goals`

`good`, `synced` and `goals` must be equal on every client (PASS). `racy` and `lost` may differ, and `eventLamp` differs
for a late joiner: those are the planted bugs, now observed in real VRChat networking.

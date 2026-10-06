# NetSim manual

English | [日本語](README.ja.md)

NetSim runs several VRChat clients inside one Unity Editor play session (ClientSim) and routes Udon networking
between them through a simulated server with latency, jitter and loss. Scenarios (C# coroutines) add clients,
drive the world with bots and report differences between clients.

It is a debugging aid, not a faithful reimplementation of VRChat networking: confirm important results with a
multi-client test in the real VRChat client ("Build & Test" with several clients).

## How it works

### One Editor, many clients

You start **one** Unity Editor, even for 3 or 4 clients. No extra Editor instances, project clones or VRChat clients
are needed. All simulated clients live in the same play session:

- Their state can be read and compared directly, so scenarios detect differences automatically and write them to a report.
- Time can run faster than real time (`timeScale`; the sample world's ~90 s scenario takes about 20 s).
- A fixed `seed` reuses the same random choices for latency, loss and bot actions, which makes problems much easier
  to reproduce (frame timing still varies a little between runs).

The cost is that every client's physics, animation and audio also run in that Editor (see Limitations), and the
networking is a simplified model, so confirm important results with several real VRChat clients ("Build & Test").

### Clients

- The world scene is loaded additively once per remote client. Each copy lives in its own physics scene, so
  triggers and colliders of different clients do not interact.
- The original scene is ClientSim's local player (`C0`); the copies are ClientSim remote players (`C1`, `C2`, ...).
- Every time Udon runs (`UdonBehaviour.RunProgram`, patched with Harmony) NetSim selects the client from the scene
  of the running behaviour, and answers these per client:
  - `Networking.LocalPlayer`, `Networking.IsMaster`, `VRCPlayerApi.isLocal`
  - `Networking.GetOwner` / `IsOwner` / `SetOwner`
  - `VRC_Pickup.currentPlayer` / `IsHeld`

### Simulated network

| Feature | Behaviour |
|---|---|
| Network events (`SendCustomNetworkEvent`) | All / Others / Owner / Self, `[NetworkCallable]` parameters. Ordered per sender-receiver pair; optional drop rate |
| Synced variables (Manual / Continuous) | Sent at the end of the frame. The server rejects data from a client that is not the owner it knows (lost writes). Older data arriving late is discarded. The owner does not receive its own data. Optional drop rate |
| Ownership | `SetOwner` takes effect locally at once; everyone is notified (`OnOwnershipTransferred`) after the server confirms. When the master leaves, its objects go to the new master |
| Late join | The new client receives the server's latest state of every behaviour. Messages that arrive before its scene is ready are held back |
| Object positions (minimal `VRCObjectSync`) | The owner's position / rotation is sent at a fixed interval and interpolated on the other clients |

### Network conditions (latency, jitter, loss)

The simulated network is configured with `NetSimConfig`, passed when a scenario starts:

```csharp
Haselab.NetSim.NetSimScenarioRunner.RunWhenReady("sample-world", new Haselab.NetSim.NetSimConfig {
    latencyMin = 0.1f, latencyMax = 0.4f,      // one-way delay per hop, seconds; the spread is the jitter
    eventDropRate = 0.05f,                     // 5% of network-event deliveries are lost
    serializationDropRate = 0.05f,             // 5% of synced-variable deliveries are lost
    objectSyncDropRate = 0.05f,                // 5% of object position updates are lost
    seed = 2,
});
```

| Field | Default | Meaning |
|---|---|---|
| `latencyMin` / `latencyMax` | 0.05 / 0.15 s | One-way delay of each hop (client -> server, server -> client), chosen uniformly per message. A message between two clients takes two hops. The range is the jitter: messages of different kinds can overtake each other (network events between the same sender and receiver stay in order) |
| `eventDropRate` | 0 | Probability that a `SendCustomNetworkEvent` delivery to one receiver is lost |
| `serializationDropRate` | 0 | Probability that a synced-variable delivery (Manual / Continuous) to one receiver is lost |
| `objectSyncDropRate` | 0 | Probability that an object position update to one receiver is lost |
| `continuousInterval` | 0.2 s | Send interval of Continuous sync and object sync |
| `lateJoinStateDelay` | 0.3 s | Delay before a late joiner receives the state snapshot |
| `seed` | 1 | Random seed for latency, loss and the bots' random numbers |
| `trace` | "" | Comma-separated path substrings; network traffic of matching objects is logged |
| `carry` | true | Bots carry objects (pick up -> walk -> drop); `false` moves them instantly |

Losses are decided per receiver, so one client can miss a message that the others get. Times are simulated time:
with `timeScale: 4` the Editor runs four times faster, but a latency of 0.2 s is still 0.2 s for the world's scripts.
In a suite (`NetSimSuite`) the same values are given in each step string (see Running). Each report starts with the
configuration it ran with, and its totals count dropped messages (`ev.dropped`, `ser.dropped`, `objsync.dropped`).

### Editor behaviour that NetSim changes while it runs

- **UdonSharp scene processing** normally writes C# field values into the *active* scene when a scene is
  loaded. While NetSim runs this is skipped for the active scene and applied to each copy instead.
- **`UdonManager.OnSceneLoaded`** clears delayed events and Update registrations of all scenes on every
  additive load. For copies NetSim only initializes the new scene.
- **`VRC_SceneDescriptor`** of a copy does not replace the original one.
- **`VRCPlayerApi.GetTrackingData`** of a ClientSim remote player throws; NetSim returns the player's position.

### Carrying objects

`NetSim.StartCarry(client, go, target)` emulates pick up -> walk -> drop:

- Ownership moves to the carrier; only the carrier gets `OnPickup`; `VRC_Pickup.IsHeld` / `currentPlayer` report it.
  Objects with `pickupable = false` are not picked up.
- The object follows a NavMesh path at hand height (`NetSim.HandHeight`, 1 m) and walking speed (2.5 m/s) with a
  kinematic `Rigidbody.MovePosition`, so triggers on the way see it enter and exit.
- `OnDrop` runs on arrival, or earlier when world code calls `Pickup.Drop()`.
- Other clients see it move through the object-sync emulation.

## Writing scenarios

```csharp
using System.Collections;
using Haselab.NetSim;

static class MyWorldScenarios
{
    [NetSimScenario("doors")]
    static IEnumerator Doors(NetSimScenarioContext ctx)
    {
        yield return ctx.AddClients(2);                                  // C1, C2 (one at a time)
        var door = ctx.Master.goByPath["World/Door"];                    // objects by hierarchy path
        NetSim.As(ctx.Master, () => door.GetComponent<VRC.Udon.UdonBehaviour>().SendCustomEvent("_interact"));
        yield return ctx.Settle(3);                                      // simulated seconds
        ctx.R($"- door open on C2: {NetSim.Var(NetSim.Clients[2], "World/Door", "isOpen")}");
        ctx.ReportConsistency("after opening the door");                // synced-variable diff, master vs others
    }
}
```

The assembly that contains the scenarios must be Editor-only and reference `Haselab.NetSim.Editor`
(see `Samples~/ExampleScenarios/Haselab.NetSim.Samples.Editor.asmdef`).

Useful members:

| Member | Purpose |
|---|---|
| `ctx.AddClients(n, prefix, lateJoin)` | Add remote clients and wait until their behaviours have started |
| `NetSim.AddClient(name, lateJoin)` / `NetSim.RemoveClient(c)` | Join / leave (leave the master to test master migration) |
| `NetSim.MasterId = c.player.playerId` | Make a remote client the instance creator before the game starts, so it can leave later |
| `ctx.PlayUntil(stop, timeout, bot)` | Call `bot(client, rng)` for every client once per simulated second; stops on stall (`ctx.ProgressSignature`, `ctx.StallSeconds`) |
| `NetSim.StartCarry(c, go, target)` | Carry an object as client `c` |
| `NetSim.As(c, action)` | Run code as client `c` (needed when calling Unity UI / non-Udon code) |
| `NetSim.Var(c, path, name)` | Read a program variable on one client |
| `NetSim.DiffSynced(a, b)` / `ctx.ReportConsistency(title)` | Synced-variable differences |
| `NetSim.DiffVisual(a, b, root)` / `ctx.LateJoinOnlyDifferences(early, late, root)` | Active / collider / renderer / text / interactable differences |
| `NetSim.HaltedBehaviours()` | Behaviours halted by an Udon exception |
| `NetSim.SetOwnerCalls` / `NetSim.SetOwnerHotSpots()` | `SetOwner` calls per object (also in the report), to spot ownership requested in a loop |
| `NetSim.TraceSubstrings` or `NetSimConfig.trace` | Log the network traffic of matching object paths |
| `NetSimScenarioRunner.CountedLogPrefixes` | Count log lines (e.g. game events) in the report |
| `NetSimScenarioRunner.WorldReady` | Replace the "world finished initializing" check |

Hierarchy paths that occur more than once get a `~n` suffix (`Root/Item~1`, `Root/Item~2`).

### Bots

A bot is a function called once per simulated second per client. Because the clients act independently,
simultaneous button presses and fights over the same object happen naturally. Write bots the way a player acts:
only interact with objects that are active and have an enabled collider, press only visible, interactable buttons,
and carry only pickupable objects. The sample `MonkeyBot` does this without any knowledge of the world;
a bot that knows the game's goal finds far more problems.

## Running

Play mode, one scenario:

```csharp
Haselab.NetSim.NetSimScenarioRunner.RunWhenReady("doors",
    new Haselab.NetSim.NetSimConfig { latencyMin = 0.1f, latencyMax = 0.4f, eventDropRate = 0.02f }, timeScale: 4);
```

Unattended, many scenarios (survives play-mode restarts and domain reloads, works while the Editor is unfocused):

```csharp
Haselab.NetSim.NetSimSuite.Start(new[] {
    // scenario|label|latMin|latMax|eventDrop|serializationDrop|objectSyncDrop|seed|timeScale[|trace[|teleport]]
    "doors|fast|0.03|0.08|0|0|0|1|4",
    "doors|slow-lossy|0.2|0.6|0.05|0.05|0.05|2|4",
});
```

- Reports: `Logs/NetSim/<scenario>_<label>_<time>.md`; suite progress: `Logs/NetSim/suite.log`.
- A `swap:<dir>` step copies every file under `<dir>` (laid out like the project) into the project and recompiles,
  which makes it possible to run the same scenarios against two versions of the world scripts in one suite.

### Reading a report

- **synced vars ... N differences**: after the network settled, a client holds different synced values from the
  master. Usually a real bug (lost write, missing `RequestSerialization`, state changed by a network event on
  some clients only).
- **late joiner only**: a difference only the late joiner has; its state was not restored completely from synced
  variables. Purely local UI state (pages of an information board, settings menus) also shows up here and is expected.
- **halted UdonBehaviours**: an Udon exception halted the behaviour on that client; the first errors are listed
  at the top of the report.
- **STALL**: the progress signature did not change for `StallSeconds`; the game may be stuck.

### Values that diverge under simultaneous interaction

The common "take ownership, then write" pattern (`Networking.SetOwner`, then change a synced variable and call
`RequestSerialization`) works while one player acts at a time. When two players act at nearly the same time, both take
ownership and write, and only one write survives; the other player keeps showing its own value until the next change.
NetSim reports this as **synced vars ... differences**. Avoid it by letting non-owners ask the owner with a network event
and having only the owner write. Compare `RacyCounter` and `GoodCounter` in the sample world.

## Testing in the real VRChat client

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

### Writing a bot

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

### Running

```csharp
Haselab.NetSim.RealClient.RealClientTest.BuildAndTest(botPrefab, 3, "playSeconds=180");
```

or select the bot prefab and use *Tools > NetSim > Real Client Test > Build & Test with Selected Bot Prefab*.
Then, from the package's `Tools~` folder (`Packages/net.haselab.netsim/Tools~` for a VCC install, under
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

## Limitations

- Not VRChat's real networking: bandwidth, batching, reliability and ownership arbitration are simplified.
- Object sync is minimal; carrying moves in straight NavMesh segments without hand motion, throwing or
  player locomotion (teleporters etc.).
- NavMesh agents are simulated independently on every client (as in VRChat, they are not synced).
- Some APIs of remote ClientSim players (avatar, tracking) are not available.
- Physics, animation and audio still run in every copy and cost Editor performance; 3-4 clients are practical.

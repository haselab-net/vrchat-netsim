# NetSim manual

English | [日本語](README.ja.md)

NetSim runs several VRChat clients inside one Unity Editor play session (ClientSim) and routes Udon networking
between them through a simulated server with latency, jitter and loss. You can drive the clients by hand from the
NetSim window, or let scenarios (built in, or C# coroutines you write) add clients, play the world with bots and
report differences between clients, from the window, from code or from the command line.

It is a debugging aid, not a faithful reimplementation of VRChat networking: confirm important results with a
multi-client test in the real VRChat client ("Build & Test" with several clients; the optional
[NetSim Real Client Test](../../net.haselab.netsim.realclient/README.md) package automates that with a bot).

## Quick start

1. Install the package (see the repository README) and open your world scene.
2. Open **Tools > NetSim > NetSim Window**.
3. Pick the network conditions (a preset such as *Typical* or *Bad Wi-Fi*, or your own values).
4. Either test by hand (**Manual test** tab) or let a scenario play (**Scenarios** tab). Results are listed in the
   **Reports** tab and written to `Logs/NetSim/`.

No code is needed for either. To try it on a world with known sync bugs, import the **Sample World** sample, open
`SampleWorld.unity` and run the `sample-world` scenario.

## The NetSim window

### Network conditions

Latency (one way, per hop), loss rates, seed and the other `NetSimConfig` fields ([below](#network-conditions-latency-jitter-loss)).
They are saved to `ProjectSettings/NetSimSettings.json` and used by every manual session and every scenario that is
started without its own configuration (also from code and the command line), so a team can commit them. During a
manual session, changes apply immediately (except the seed).

### Manual test

1. Enter play mode (the button does it). You are **C0**, the ClientSim player, and play in the Game view as usual.
2. **Start NetSim**, then **+ Client** for every other player. Each client is a copy of the scene; the copies appear in
   the Hierarchy as separate scenes labelled `C1 Remote1`, `C2 Remote2`, … (`★` = master).
3. **View** chooses whose copy the Game view shows (*View all* draws all copies on top of each other). Mouse and
   keyboard input in the Game view always act as C0.
4. Select an object in the Hierarchy (in any copy). **Selected object** shows its owner and synced variables in every
   client, values that differ from the master in red, and lets each client **Interact** with it, take ownership
   (**Own**) or run a custom event. Pressing *Interact* for two clients right after each other is the quickest way
   to reproduce "two players clicked at once".
5. **Interact as another client** lists everything a client can interact with right now.
6. **Compare now** lists the synced variables that differ between the master and the others (*every 2 s* keeps it
   updated). **Write snapshot report** saves the differences, visible differences, network statistics and the NetSim
   log to `Logs/NetSim/snapshot_*.md`.
7. **Leave** makes a client leave; leaving the master tests master migration. **+ Late joiner** joins a client later.

### Scenarios

Choose a scenario and press **Run now** (enters play mode if needed and stays there, so you can look at the clients
afterwards), or **Add to batch** to collect several runs, for example *+ this scenario × every preset*, and
**Run batch** to run them unattended, each in a fresh play session. **Copy command line for this batch** gives the
equivalent command line (see [Command line](#command-line)).

Built-in scenarios (no code; they work on any world):

| Scenario | What happens | PASS when |
|---|---|---|
| `join-check` | Clients join; nobody plays | all clients have the same synced variables |
| `random-play` | Every client presses random buttons / interactables for *Play time* | all clients agree afterwards |
| `late-join` | Clients play, then one more joins | everybody agrees and the late joiner sees the same objects |
| `master-leave` | The master leaves in the middle of play | the remaining clients agree and keep working |

*Clients* (in total, including C0), *Play time* and *Bot activity* apply to the built-in scenarios. They only press
what a player could press, but they know nothing about your game; a scenario written for your world
([Writing scenarios](#writing-scenarios)) reaches far more states.

### Reports

Every scenario writes `Logs/NetSim/<scenario>[_<label>]_<time>.md` ending in `## Verdict` with **PASS** / **FAIL**
(see [Reading a report](#reading-a-report)). The **Reports** tab lists them, newest first, and shows them inside the
editor.

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

The simulated network is configured in the NetSim window (saved to `ProjectSettings/NetSimSettings.json`), or with a
`NetSimConfig` passed when a scenario starts:

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

Put the file in any `Editor` folder (NetSim's assembly is referenced automatically), or in an Editor-only assembly
definition that references `Haselab.NetSim.Editor`. The **Scenario Template** sample is a starting point. A scenario
appears in the NetSim window as soon as the scripts compile; `[NetSimScenario("doors", Description = "...")]` adds a
line of explanation there.

Useful members:

| Member | Purpose |
|---|---|
| `ctx.AddClients(n, prefix, lateJoin)` | Add remote clients and wait until their behaviours have started |
| `ctx.Check(name, ok)` | Record an expectation; all of them are listed under `## Verdict` (PASS only when all hold) |
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
and carry only pickupable objects. The built-in `NetSimBots.Random` does this without any knowledge of the world;
a bot that knows the game's goal finds far more problems.

## Running scenarios

From the NetSim window: see [Scenarios](#scenarios). From code:

```csharp
// play mode or edit mode: enters play mode if needed and stays there afterwards
Haselab.NetSim.NetSimScenarioRunner.RunInPlayMode("doors");
// play mode, with explicit conditions (null = the window's settings, timeScale < 0 = the window's speed)
Haselab.NetSim.NetSimScenarioRunner.RunWhenReady("doors",
    new Haselab.NetSim.NetSimConfig { latencyMin = 0.1f, latencyMax = 0.4f, eventDropRate = 0.02f }, timeScale: 4);
```

Unattended, many scenarios (survives play-mode restarts and domain reloads, works while the Editor is unfocused):

```csharp
Haselab.NetSim.NetSimSuite.Start(new[] {
    // scenario|label|latMin|latMax|eventDrop|serializationDrop|objectSyncDrop|seed|timeScale[|trace[|teleport]]
    "doors|fast|0.03|0.08|0|0|0|1|4",
    "doors|slow-lossy|0.2|0.6|0.05|0.05|0.05|2|4",
    "doors",                                   // fields left out come from the window's settings
});
```

- Reports: `Logs/NetSim/<scenario>_<label>_<time>.md`; suite progress and one line per finished step with its verdict:
  `Logs/NetSim/suite.log`. `NetSimSuite.Results` / `NetSimSuite.Finished` give the results to code.
- A `swap:<dir>` step copies every file under `<dir>` (laid out like the project) into the project and recompiles,
  which makes it possible to run the same scenarios against two versions of the world scripts in one suite.

### Command line

For scripts, CI and AI agents. The project must not be open in another Unity Editor; do not pass `-quit`.

```
Unity.exe -batchmode -projectPath <project> -executeMethod Haselab.NetSim.NetSimCli.Run
          -netsimScene Assets/MyWorld.unity
          -netsimRun random-play -netsimRun "late-join|slow|0.2|0.6|0.05|0.05|0.05|2|4"
          -logFile <project>/Logs/NetSim/cli-editor.log
```

- `-netsimRun` takes a scenario name or a suite step (above) and can be repeated. Without it, the batch list saved in
  the NetSim window runs. Anything a step leaves out comes from `ProjectSettings/NetSimSettings.json`.
- `-netsimList` writes the known scenarios to `Logs/NetSim/scenarios.txt` and quits.
- Exit code: **0** every scenario passed (or made no checks), **2** a check failed, **1** error (unknown scenario,
  compile error, exception, a scenario that never started). `Logs/NetSim/cli-result.txt` has one
  `<verdict>\t<step>\t<report>` line per step.
- Without `-batchmode` the same command opens the editor window, runs and quits.

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

## Limitations

- Not VRChat's real networking: bandwidth, batching, reliability and ownership arbitration are simplified.
- Object sync is minimal; carrying moves in straight NavMesh segments without hand motion, throwing or
  player locomotion (teleporters etc.).
- NavMesh agents are simulated independently on every client (as in VRChat, they are not synced).
- Some APIs of remote ClientSim players (avatar, tracking) are not available.
- Physics, animation and audio still run in every copy and cost Editor performance; 3-4 clients are practical.

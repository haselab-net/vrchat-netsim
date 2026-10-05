# NetSim manual

NetSim runs several VRChat clients inside one Unity Editor play session (ClientSim) and routes Udon networking
between them through a simulated server with latency, jitter and loss. Scenarios (C# coroutines) add clients,
drive the world with bots and report differences between clients.

It is a debugging aid, not a faithful reimplementation of VRChat networking: confirm important results with a
multi-client test in the real VRChat client ("Build & Test" with several clients).

## How it works

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

Latency is one-way and chosen uniformly between `latencyMin` and `latencyMax` for each message, so messages of
different kinds can overtake each other.

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

## Limitations

- Not VRChat's real networking: bandwidth, batching, reliability and ownership arbitration are simplified.
- Object sync is minimal; carrying moves in straight NavMesh segments without hand motion, throwing or
  player locomotion (teleporters etc.).
- NavMesh agents are simulated independently on every client (as in VRChat, they are not synced).
- Some APIs of remote ClientSim players (avatar, tracking) are not available.
- Physics, animation and audio still run in every copy and cost Editor performance; 3-4 clients are practical.

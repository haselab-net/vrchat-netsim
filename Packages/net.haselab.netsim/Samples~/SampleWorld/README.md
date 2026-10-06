# Sample world

English | [日本語](README.ja.md)

A small world with correct sync patterns, planted sync bugs and a scenario that checks NetSim finds exactly the bugs.

| Object | Pattern | Expected NetSim result |
|---|---|---|
| `GoodCounter` | Non-owners ask the owner with a network event; only the owner writes the synced value | consistent |
| `RacyCounter` | "Take ownership, then write" (bug when two players click at nearly the same time) | clients disagree after a race |
| `LostWriteCounter` | Writes the synced value without taking ownership (bug) | synced-variable difference |
| `SyncedToggle` | State in a synced variable, changed by the owner only | consistent, also for a late joiner |
| `EventToggle` | State only changed by a network event (bug) | late joiner sees a different lamp |
| `Ball` + `Goal` | `VRCObjectSync` pickup carried into a trigger; the goal's owner counts | same goal count on every client |

## Run

1. Open `SampleWorld.unity` and enter Play Mode (ClientSim).
2. Run the scenario (from a small Editor script or an "execute C#" tool):

```csharp
Haselab.NetSim.NetSimScenarioRunner.RunWhenReady("sample-world");
```

3. Read `Logs/NetSim/sample-world_*.md`. It ends with a `## Verdict` checklist and **PASS** when every expectation
   in the table above was met.

The example scenarios (`example-consistency`, `example-latejoin`, `example-masterleave`) from the
**Example Scenarios** sample also run on this world.

The scenario expects no message loss (the default configuration). With `eventDropRate > 0` the event-only toggle
also differs between early clients, and lost requests make the good counter count fewer clicks, which is the
intended behaviour of those patterns under loss.

## Real VRChat client test

`SampleWorldBot.prefab` is a bot for the same world in the real VRChat client (see the manual, "Testing in the real
VRChat client"). Each client's bot clicks random stations and carries the ball for 2 minutes, then stops and keeps
logging `state good=.. racy=.. lost=.. synced=.. eventLamp=.. goals=..`.

1. Start Steam, log in to the VRChat SDK (VRChat SDK > Show Control Panel) and save `SampleWorld.unity`.
2. *Tools > NetSim > Sample World > Build & Test with Bot (3 clients)*.
3. Optionally, about a minute later, add a late joiner: `python vrc_clients.py launch 1` (in the package's `Tools~`).
4. `python analyze_logs.py --since "<start time>" --watch --must-match good,synced,goals`

`good`, `synced` and `goals` must be equal on every client (PASS). `racy` and `lost` may differ, and `eventLamp` differs
for a late joiner: those are the planted bugs, now observed in real VRChat networking.

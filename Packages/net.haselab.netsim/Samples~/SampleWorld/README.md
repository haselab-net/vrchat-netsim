# Sample world

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

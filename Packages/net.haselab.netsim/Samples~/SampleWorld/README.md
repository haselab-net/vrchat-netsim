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

1. Open `SampleWorld.unity`.
2. *Tools > NetSim > NetSim Window* > **Scenarios** > `sample-world` > **Run now**
   (from code: `Haselab.NetSim.NetSimScenarioRunner.RunInPlayMode("sample-world");`).
3. The report (**Reports** tab, `Logs/NetSim/sample-world_*.md`) ends with a `## Verdict` checklist and **PASS** when
   every expectation in the table above was met.

The built-in scenarios (`random-play`, `late-join`, ...) also run on this world; they report the planted bugs as FAIL.

To see the bugs by hand: **Manual test** > *Start NetSim* > *+ Client* twice, select `RacyCounter` in the Hierarchy and
press *Interact* for C1 and C2 right after each other, or *Interact* on `LostWriteCounter` as C1, then *Compare now*.

The scenario expects no message loss (the default configuration). With `eventDropRate > 0` the event-only toggle
also differs between early clients, and lost requests make the good counter count fewer clicks, which is the
intended behaviour of those patterns under loss.

To run the same world in real VRChat clients, see the **Sample World Bot** sample of the optional
NetSim Real Client Test package.

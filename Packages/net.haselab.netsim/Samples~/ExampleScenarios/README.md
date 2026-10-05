# Example scenarios

World-agnostic scenarios driven by a random "monkey" bot that clicks visible UI buttons and interacts with
enabled interactables on every client.

| Scenario | What it checks |
|---|---|
| `example-consistency` | 3 clients play for 120 s (simulated); all clients end with the same synced variables |
| `example-latejoin` | a client joins after 60 s; it matches the others (synced variables and visible state) |
| `example-masterleave` | the master leaves mid-game; the remaining clients stay consistent, nothing halts |

Run in Play Mode:

```csharp
Haselab.NetSim.NetSimScenarioRunner.RunWhenReady("example-consistency");
```

Copy `ExampleScenarios.cs` and replace `MonkeyBot` with a bot that plays your game to find real problems.

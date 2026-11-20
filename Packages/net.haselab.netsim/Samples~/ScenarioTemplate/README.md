# Scenario Template

English | [日本語](README.ja.md)

`MyWorldScenarios.cs` is a starting point for a scenario that knows your world: it adds clients, lets a bot play,
adds a late joiner and checks that every client agrees (`ctx.Check` -> `## Verdict` PASS / FAIL in the report).

1. Copy the file into an `Editor` folder of your project (or keep it here) and rename the scenario.
2. Replace `Bot` with what a player does in your world (start a round, press the right buttons, carry items with
   `NetSim.StartCarry`) and add checks for what must be the same on every client (score, game state, ...).
3. Run it from *Tools > NetSim > NetSim Window* > **Scenarios**; it appears in the list once the scripts compile.

The built-in scenarios (`join-check`, `random-play`, `late-join`, `master-leave`) need no code but only press random
buttons. See the [manual](../../Documentation~/README.md#writing-scenarios) for the scenario API.

# YourBuddy documentation

How the mod works and why it is built the way it is. For playing and installing, see the
[main README](../README.md). For contributing, see [CONTRIBUTING.md](../CONTRIBUTING.md).

| Doc | Read it for |
|---|---|
| [architecture.md](architecture.md) | which file does what, per-frame flow, caches, save files, what to update when you change something |
| [invariants.md](invariants.md) | **rules that must not be broken**, each with an anchor |
| [navigation.md](navigation.md) | node graph, A\*, following a path, stairs |
| [probes.md](probes.md) | floor heights, line of sight, doorways (`NavProbe`) |
| [doors.md](doors.md) | opening, password doors, closing behind |
| [dialog.md](dialog.md) | the talk window and order words |
| [behaviour.md](behaviour.md) | orders vs. modes, the decider, bouts |
| [fear.md](fear.md) | seeing the Breathless, stress, fleeing, hiding |
| [terminals.md](terminals.md) | switching on oxygen and climate control |
| [snacks.md](snacks.md) | eating from containers and loose food |
| [items.md](items.md) | carrying, tidying, selling trash boxes, idle play |
| [lifecare.md](lifecare.md) | the scanner terminal and the game's player-icon bug |
| [game-model.md](game-model.md) | how the *game* works: rooms, doors, docking, ship stages, atmosphere |
| [logging.md](logging.md) | debug levels, log tags, capturing and trimming logs |
| [troubleshooting.md](troubleshooting.md) | from a symptom (stuck, door loop, `On: world`, missing icon) to the log line and the doc |
| [reference.md](reference.md) | tuning constants, debug commands, node editor details |
| [known-issues.md](known-issues.md) | open issues, dead ends, abandoned approaches |

Start with `architecture.md` for "where is it", `invariants.md` for "why is it like this", and the
topic doc for "how does it work". For "what is wrong with it", start with `troubleshooting.md`.

**Conventions**

- Each rule is stated once, in `invariants.md`. Other docs and code link to it.
- Docs explain *why* and *what breaks if you change it*, not what the code already says.
- Docs describe the current code. No dates, status notes or history - see
  [AGENTS.md §4](../AGENTS.md#4-documentation-rules).
- `python tools/doccheck.py` checks every link and anchor, and the constants in `reference.md`
  against the code. CI runs it on every push.

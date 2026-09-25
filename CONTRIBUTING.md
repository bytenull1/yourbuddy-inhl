# Contributing to YourBuddy

Thanks for helping. Bug reports, nav-graph fixes and code changes are all welcome.

---

## Reporting a bug

Open an issue on the [issue tracker](https://github.com/bytenull1/yourbuddy-inhl/issues) with the
bug report form. [docs/troubleshooting.md](docs/troubleshooting.md) may already explain the symptom.
A good report makes the difference between a quick fix and guesswork:

1. **What happened, and where.** Station or ship, room, what the buddy was doing (following,
   tidying, fleeing…). A screenshot with `buddy_hud on` helps a lot.
2. **A log.** In the game console:
   ```
   debug_level 2
   ```
   Reproduce the problem, then take `BepInEx/LogOutput.log`. If you can, trim it with NPC.Core's
   [trimlog.py](https://github.com/bytenull1/npc-core-inhl/blob/main/tools/trimlog.py):
   ```bash
   python trimlog.py BepInEx/LogOutput.log --fold > capture.txt
   ```
   Add a plain-text note in the file where things went wrong ("here it walks into the wall").
3. **Positions, if it's about navigation.** Where you stood, where the buddy stood (the HUD shows
   it), and which nav nodes are nearby.

More on logs: [docs/logging.md](docs/logging.md).

---

## Setting up

1. Install the [.NET SDK](https://dotnet.microsoft.com/download) and own a copy of the game with
   [BepInEx 5](https://github.com/BepInEx/BepInEx/releases).
2. Copy the reference DLLs from your game into `YourBuddy/lib/` - the list is in
   [YourBuddy/lib/README.md](YourBuddy/lib/README.md). They are not redistributed. YourBuddy is built
   on [NPC.Core](https://github.com/bytenull1/npc-core-inhl): clone it beside this repository
   (`../npc-core-inhl`) and both build together, or put its `NPC.Core.dll` into `YourBuddy/lib/` too.
3. Build:
   ```bash
   dotnet build YourBuddy/YourBuddy.csproj -c Release
   ```
4. Copy `YourBuddy/bin/Release/netstandard2.1/YourBuddy.dll` to `BepInEx/plugins/`, with NPC.Core's
   `NPC.Core.dll` beside it (close the game first - it locks the file).

Optional, for reading the game's own code and scene: [decompiled/README.md](decompiled/README.md).
The decompile is the game developer's code and must never be committed.

---

## Where to start reading

- [docs/README.md](docs/README.md) - map of all docs.
- [docs/architecture.md](docs/architecture.md) - which file does what.
- [docs/invariants.md](docs/invariants.md) - rules that must not be broken. Each one exists because
  breaking it caused a bug. Navigation especially is full of these; read the relevant ones before
  changing pathfinding, probes or stairs.

---

## Making a change

- **Keep changes small** and focused on one problem.
- **Build clean.** Debug and Release must both build; every warning is an error. The SDK is pinned
  in `global.json` and the language version in the project file.
- **Run the checks** CI runs: `python tools/doccheck.py` (doc links, anchors, `reference.md`
  constants).
- **Test in the game.** Say in the pull request what you tried. For navigation, test stairs and doorways
  on both the ship and a station - they are where regressions show up.
- **Follow the code style** in [AGENTS.md §3](AGENTS.md#3-code-conventions): short comments that
  link to the rule, all reflection in `GameInternals.cs`, no second raycast outside `NavProbe`.
- **Update the docs** that own the subject, following [AGENTS.md §4](AGENTS.md#4-documentation-rules):
  describe the code as it is, short sentences, no dates or status notes.

### Changing the shipped nav graph

The graph ships with NPC.Core; see [its CONTRIBUTING](https://github.com/bytenull1/npc-core-inhl/blob/main/CONTRIBUTING.md#changing-the-shipped-nav-graph).

### Saves

Never write mod state into the game's own save - use the `.buddy` sidecar, which NPC.Core writes. Uninstalling the mod must
leave saves intact ([NPC.Core's invariants](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#mod-state-never-enters-the-vanilla-save)).

---

## Using an AI assistant

Fine, and the docs are written to help one. Point it at [AGENTS.md](AGENTS.md) first. Review what it
writes into docs: remove status notes, dates and long narratives before committing.

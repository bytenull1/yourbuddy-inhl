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
   Reproduce the problem, then take `BepInEx/LogOutput.log`. If you can, trim it:
   ```bash
   python tools/trimlog.py BepInEx/LogOutput.log --fold > capture.txt
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
   [YourBuddy/lib/README.md](YourBuddy/lib/README.md). They are not redistributed.
3. Build:
   ```bash
   dotnet build YourBuddy/YourBuddy.csproj -c Release
   ```
4. Copy `YourBuddy/bin/Release/netstandard2.1/YourBuddy.dll` to `BepInEx/plugins/` (close the game
   first - it locks the file).

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
  constants) and `python tools/bundle_nodegraph.py --check`.
- **Test in the game.** Say in the pull request what you tried. For navigation, test stairs and doorways
  on both the ship and a station - they are where regressions show up.
- **Follow the code style** in [AGENTS.md §3](AGENTS.md#3-code-conventions): short comments that
  link to the rule, all reflection in `GameInternals.cs`, no second raycast outside `NavProbe`.
- **Update the docs** that own the subject, following [AGENTS.md §4](AGENTS.md#4-documentation-rules):
  describe the code as it is, short sentences, no dates or status notes.

### Changing the shipped nav graph

1. Edit the graph in game (F8 editor, see the [README](README.md#the-nav-graph--node-editor)). It
   saves to `BepInEx/config/YourBuddyRoutes/nodegraph.json`.
2. Regenerate the bundled copy - this also bumps `BundleVersion`, without which no one gets the update:
   ```bash
   python tools/bundle_nodegraph.py
   ```
3. Rebuild and commit `YourBuddy/Resources/nodegraph.bundled.json`.

### Saves

Never write mod state into the game's own save - use the `.buddy` sidecar. Uninstalling the mod must
leave saves intact ([invariants](docs/invariants.md#mod-state-never-enters-the-vanilla-save)).

---

## Using an AI assistant

Fine, and the docs are written to help one. Point it at [AGENTS.md](AGENTS.md) first. Review what it
writes into docs: remove status notes, dates and long narratives before committing.

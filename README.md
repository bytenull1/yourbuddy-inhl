# YourBuddy Mod

A BepInEx 5 plugin that adds a player-like NPC companion to Isolated Inhale.

It was primarily created as a fun alternative to multiplayer, as the game's creator has no plans to add it anytime soon.

In theory, the mod should reduce anxiety; in practice, after playing alone for a long time, having someone constantly nearby is a bit unnerving. Anyway, try it yourself.

> ⚠️ **Disclaimer**: This mod may break immersion, the game is single‑player by design. Use after finishing the storyline for best experience.

---

## Features

**The buddy**
- A companion NPC that uses the player's model.
- A new game starts with it asleep in the cryo capsule next to yours; it wakes shortly after you step out. In an existing save, `spawn_buddy` brings one in.
- Can be killed by deadly atmospheres or the Breathless, and becomes a ragdoll you can pick up and carry with the Grab key.

**Orders**
- Look at it and press Interact to open an order window: follow, wander, stay, walk to a node, hide, tidy up, sell, play, or decide for itself. Wording is matched loosely.
- Give it door PIN codes; it uses each one only on the keypad it belongs to.
- Orders hold until you revoke them, or expire after a while if you prefer.

**Its own mind**
- With no order in force, it **weighs** what is worth doing - how long since it last did it, how much there is to do, how far away - and picks between the best few at random, so it never runs the same routine twice.
- Switches the oxygen generator or climate control back on when the air turns dangerous and the unit is off. It never switches anything off or clears a fault.
- Now and then eats or drinks something nearby, from a fridge, cabinet, chest or locker (closing it again) or off the floor. It leaves the food alone while you are hungry.
- Tidies up: collects several pieces of rubbish in a row, loose or out of a cupboard, and takes them to a trash can.
- Carries every trash box it can find to a sell station and sells the lot in one press - the money is yours.
- Messes about with loose objects when bored: carries one across the room or into the next, or throws it about and fetches it.

**Fear**
- Reacts to the Breathless: watches it, refuses to walk toward it, and when it has stared too long or the monster comes too close, either **runs** - first away, then to you - or **hides** in a closet or locker until the monster is gone.

**Navigation**
- Ships with a ready-made nav graph for the ship and every station.
- Opens room doors in its path and routes around the ones it cannot open; stays out of airlocks and open space.
- Build or adjust your own graph with the in-game node editor, automatically or by hand.

**Saves and tools**
- Its state is saved in a `.buddy` sidecar file next to your save, so removing the mod leaves your save untouched.
- Console commands for full control, an on-screen HUD, debug visuals and adjustable log verbosity.

---

## Installation

1. Install **[BepInEx 5](https://github.com/BepInEx/BepInEx/releases)** for Isolated Inhale - the Windows x64 build (`BepInEx_win_x64_5.x.x.zip`), extracted into the game folder next to `Isolated Inhale.exe`.
2. Download the latest `YourBuddy.dll` from the [Releases](https://github.com/bytenull1/yourbuddy-inhl/releases) page.
3. Place the `.dll` into `BepInEx/plugins/`.
4. Launch the game. The mod will create a configuration file at `BepInEx/config/com.bytenull1.yourbuddy.cfg`.

> For debugging, you can enable the console in `BepInEx/config/BepInEx.cfg`, the `[Logging.Console]` block is responsible for this, change the value of the parameter `Enabled = false` to `Enabled = true`

---

## Building from Source

The project is an SDK-style class library targeting **.NET Standard 2.1**, so no Visual Studio project system or `.NET Framework` targeting pack is required - the .NET SDK is enough to build it.

1. **Install required tools**
   - [.NET SDK](https://dotnet.microsoft.com/en-us/download) (a recent version)
   - Optional: **Visual Studio**, **Rider**, or **Visual Studio Code** with the C# extension.

2. **Clone the repository** (or extract the source archive).

3. **Provide the reference assemblies**
   Copy the DLLs the project compiles against out of your own game install into
   `YourBuddy/lib/`. The list and where each one comes from is in
   [`YourBuddy/lib/README.md`](YourBuddy/lib/README.md). They are not redistributed
   here and are not copied into the build output.

4. **Build the project**
   - From the command line, in the repository root: `dotnet build YourBuddy/YourBuddy.csproj -c Release`
   - In Visual Studio / Rider: open `YourBuddy.slnx` and build the `Release` configuration.
   - There is no framework/toolset mismatch to worry about - `netstandard2.1` builds the same way on any platform with the .NET SDK installed.

5. **Output file**
   After a successful build, `YourBuddy.dll` will appear in `YourBuddy/bin/Release/netstandard2.1/`. Copy it to `BepInEx/plugins/` in your game directory and launch the game.

---

## Configuration

General settings (section `General` unless noted; the node editor keys live in section `NodeEditor`, documented in the Usage section below):

| Setting | Section | Default | Description |
|-----------------------|-----------|---------|-------------|
| `SaveSupport` | General | `true` | Writes/reads the buddy's location to a sidecar `.buddy` file next to your save. |
| `SpawnOnNewGame` | General | `true` | A new game starts with a buddy: it sleeps in a cryo capsule next to yours and wakes after you step out, or stands in the Shipyard's hallway if no capsule can be opened. |
| `WakeAfterPodOpenSeconds` | General | `10` | How long that buddy keeps sleeping after your own pod starts opening. |
| `MortalNPC` | General | `true` | Allows the buddy to die (ragdoll) from deadly conditions. |
| `PreventSpace` | General | `true` | Prevents the buddy from opening airlocks, walking into open space, and teleports it back inside if it ends up there anyway. |
| `AutoDoors` | General | `true` | Allows the buddy to open room doors (Gate type) in front of it. |
| `Dialog` | General | `true` | Look at the buddy and press Interact to open the order window (follow, do its own thing, stay, hide, tidy up, sell, play, walk to a node, give it a door password). |
| `Fear` | General | `true` | The buddy reacts to the Breathless: watches it, keeps away from it, runs from it or hides. Off: it ignores the monster (which can still kill it). |
| `HideInClosets` | General | `true` | A frightened buddy may get into a closet or locker instead of running, shut the doors, and wait there until the Breathless is no longer about. It never uses one you are in. |
| `FleeHideBias` | General | `0.5` | How readily it hides rather than runs: `0` never, `1` always try. The chance drops while the Breathless can see it - it would watch the buddy climb in - or is too close to beat to the doors, and rises when there is nowhere to run to. |
| `Autonomy` | General | `true` | The buddy decides for itself whenever no order is in force. Off: it only ever does what it was last told. Also `buddy_auto`. |
| `OrderPersistence` | General | `UntilRevoked` | `UntilRevoked`: an order holds until you give another one or tell it to decide for itself. `Expires`: it decides for itself again after `OrderExpirySeconds`. A goto always runs to completion. |
| `OrderExpirySeconds` | General | `90` | How long an order holds under `Expires`. |
| `Terminals` | General | `true` | When oxygen runs low or it gets too cold or hot aboard and the oxygen generator or climate control is off, a buddy deciding for itself walks over and switches it on. |
| `Snacks` | General | `true` | Now and then a buddy deciding for itself eats or drinks something nearby: from a container with food in it, or lying about. Never while you are hungry. `buddy_snack` triggers one now. |
| `SnackIntervalMinutes` | General | `20` | Roughly how many minutes pass between snacks (25% more or less each time). |
| `Tidying` | General | `true` | Now and then a buddy deciding for itself clears the rubbish: two to five pieces in a row, loose or out of a cupboard it closes again, into a trash can. `buddy_tidy` starts a round now. |
| `TidyIntervalMinutes` | General | `5` | Roughly how many minutes pass between tidying rounds (25% more or less each time). |
| `SellTrash` | General | `true` | When trash boxes have piled up and a sell station is in reach, the buddy carries every one it can to the station, loads them and presses the button once - the money is yours. It only presses when nothing else sellable is in there and nobody is standing inside. `buddy_sell` starts a run now. |
| `ItemPlay` | General | `true` | Now and then, with nothing better to do, the buddy plays with something loose: carries it across the room, takes it into another room, or throws it about and fetches it back. One to three games in a row. `buddy_play` starts a session now. |
| `ItemPlayAnything` | General | `false` | What it may play with. Off: rubbish only. On: any loose object, your tools, food and cells included, which it will throw about like anything else. |
| `ItemPlayIntervalMinutes` | General | `5` | Roughly how many minutes pass between play sessions (25% more or less each time). |
| `DebugLevel` | General | `1` | Debug log verbosity: `0` quiet (warnings only), `1` normal (stuck diagnostics, door logs), `2` thinking (path planning details, the gate inventory and gate-frame audit), `3` obstacle diagnostics (per-tick obstacle reports). Also changeable at runtime with `debug_level`. |
| `DebugVisuals` | General | `false` | Draws navigation probes, target markers, and path lines. |
| `ShowHud` | General | `false` | Displays an on‑screen status panel (mode, orders, fear, room, environment, threat, target). |
| `MoveSpeed` | General | `3.5` | Default movement speed in m/s. |
| `MaxEdgeDist` | Navigation | `80` | Maximum distance (m) at which two nav nodes auto-connect. Manual Force links work at any distance. |
| `BundledGraph` | Navigation | `true` | Use the ready-made nav graph shipped with the mod for any ship or station you have not edited yourself. |

You can change these values in the config file or via console commands (see below).

---

## Console Commands Reference

| Command | Arguments | Description |
|---------|-----------|-------------|
| `spawn_buddy` | – | Spawns a buddy in front of you (replaces any existing buddy). |
| `buddy_despawn` | – | Despawns the current buddy. |
| `kill_buddy` | `[force]` | Kills the buddy (ragdoll), with an optional forward impulse force (0–100). |
| `buddy_follow` | – | Switch to follow mode. |
| `buddy_wander` | – | Switch to wander mode (uses nav nodes as points of interest). |
| `buddy_stay` | – | Hold position until told otherwise. |
| `buddy_stop` | – | Stop current route/wander and follow. |
| `buddy_goto` | `<node_index>` | Walk the buddy to a specific nav-graph node. |
| `buddy_snack` | – | Make the buddy get a snack nearby right now (ignores the schedule and the `Snacks` setting). |
| `buddy_tidy` | – | Make it clear the rubbish nearby into a trash can right now, several pieces in a row. |
| `buddy_sell` | – | Make it take every trash box nearby to a sell station and sell them in one press. |
| `buddy_play` | – | Make it go and mess about with something loose right now. |
| `buddy_hide` | – | Make it get into a closet or locker now, whatever it is feeling. It stays in there **until you give it another order** - no timer, and the Breathless leaving does not bring it out. |
| `buddy_mind` | – | Show what the buddy is weighing and when it acts next (the HUD's Mind / Why / Air / Snack / Tidy / Sell / Play lines). |
| `buddy_bout` | – | End the current follow or wander stretch now, so the other one weighs full at its next decision (for testing). |
| `buddy_terminal` | `<oxygen\|climate>` | Make the buddy switch that unit on now, whatever the air (for testing; only if it is off and not broken or faulted). |
| `buddy_auto` | `[on\|off]` | Let the buddy decide for itself (`on` also cancels the order in force), or stop it deciding. Saved in the config. |
| `buddy_password` | `<code>` | Tell the buddy a door PIN code. It is used only on keypads whose own code matches, and is saved with your game. |
| `buddy_speed` | `<value>` | Set movement speed (0.5–10 m/s). |
| `buddy_node` | `add`, `count`, `clear`, `save`, `list`, `remove <index>` (alias `delete`), `link <a> <b> [force\|block\|priority\|auto]`, `unlink <index>`, `type <index> <ground\|stair>`, `auto <index> <on\|off>`, `bundled`, `unfork <owner>` | Manually manage the nav graph, including forced/blocked/priority links and stair nodes. `unlink` removes every manual link of one node. `bundled` shows which ships and stations use the shipped graph; `unfork` hands one back to it. |
| `ai_disable` | `[buddy\|monster\|all] [on\|off]` | Debug: freeze the buddy's AI, the Breathless's, or both. Nothing is written to your save, so a reload always clears it. |
| `ai_notarget` | `[on\|off]` | Debug: the Breathless stops noticing **you** - it keeps wandering, and it still hunts the buddy. |
| `node_editor` | – | Toggle the interactive node editor overlay (all its keys are configurable in the config, section `NodeEditor`). |
| `buddy_gates` | – | List every gate in the scene and the doorway carve-out it was given (opening width, whether it counts as a walkable passage). |
| `debug_level` | `<0-3>` | Set debug log verbosity at runtime. |
| `buddy_debug` | `[on/off]` | Toggle debug visuals (path, probes, target markers). |
| `buddy_hud` | `[on/off]` | Toggle status HUD. |

---

## Usage

### Spawning the Buddy

Start a new game: your buddy sleeps in the cryo capsule next to yours and wakes a few seconds after you step out of your own. In an existing save, open the in‑game console (`~`) and type `spawn_buddy` - the NPC appears a few meters in front of you. There is only one buddy at a time: running the command again replaces it.

### Talking to the Buddy

Walk up close, look at it and press **Interact**. Type an order, or pick one from the list behind the speech-bubble button; **Escape** closes the window. Wording is matched loosely, so "follow me" or "wait here" work too.

| Order | What it does |
|---|---|
| `follow` | Follows you. |
| `wander` | Walks between nav nodes, doing its own thing. |
| `stay` | Holds position. It still steps aside if it is blocking a door. |
| `goto 12` | Walks to nav node 12. |
| `decide for yourself` | Cancels your order and lets it choose again. |
| `hide` | Gets into a closet or locker and stays there until you say something else - another order, or "decide for yourself". |
| `tidy up` | Clears the rubbish nearby into a trash can, several pieces in a row. |
| `sell` | Takes every trash box nearby to a sell station and sells them in one press. |
| `play` | Goes and messes about with something loose. |
| `password 1423` | Remembers a door PIN code. A bare number works too. |

**Orders and its own mind.** With no order in force - which is how it starts, and how it comes back after loading a save - the buddy decides for itself every few seconds. It scores everything it might do: switching the life support back on (which always wins), selling trash boxes, tidying up, a snack, messing about with something, wandering off, or coming back to you. Each score is made of how overdue the thing is, how much of it there is, and how far away - so distance makes something *less* attractive rather than invisible, and it will cross a room or two for the only job going. Then it picks between the best few **at random**, which is why it does not repeat itself. Following you and wandering off still take turns, but the switch no longer lands on the same second every time.

The first five words above are orders; `hide`, `tidy up`, `sell` and `play` just start that job now. An order holds until you give another one or tell it to decide for itself; with `OrderPersistence = Expires` it also runs out, and a `goto` ends when the buddy arrives. An order given while it is running from the Breathless is carried out once it has got away. `Autonomy = false` turns its own decisions off.

**Doors.** The buddy only uses codes you gave it, and only on keypads whose own code matches; the window tells you straight away whether a code opens any door it knows about. Codes are saved with your game. It plans a route around doors it cannot open, and if there is no other way, it waits for you.

### The Nav Graph & Node Editor

Wandering and long walks follow a node graph. **The mod ships one for the ship and every station**, so there is nothing to set up. The moment you edit anything on a ship or station, that one becomes yours: it is saved to `BepInEx/config/YourBuddyRoutes/nodegraph.json` (automatically, about 20 seconds after a change) and mod updates no longer change it. `buddy_node bundled` shows which are yours, and `buddy_node unfork <owner>` returns one to the shipped graph (after a restart).

Open the editor with `F8` or `node_editor` (every key can be changed in the config, section `NodeEditor`):

| Key | Action |
|-----|--------|
| `F8` | Toggle the editor overlay |
| `Insert` / `Numpad0` | Place a node at your position |
| `Delete` | Remove the nearest node on your deck |
| `L` | Toggle connection lines |
| `K` / `B` / `O` | **Force** / **Block** / **Priority** link - press once at one node, again at another |
| `U` | Remove every Force/Block/Priority link of the nearest node |
| `T` | Ground <-> **Stair** node |
| `N` | Auto-connect on/off for this node (blue = manual links only) |
| `F6` | Save nodes |

Nodes within `MaxEdgeDist` (80 m) connect automatically when the line of sight along the floor is clear. A plain node connects at most ~0.8 m upward, a **Stair** node up to 2 m; anything steeper needs a manual link:

- **Force** (yellow) always connects - for tight ramps and steep stairs the probe rejects.
- **Block** (red) removes a wrong automatic connection, e.g. across a railing.
- **Priority** (magenta) makes any path arriving at the first node leave through the second; return trips are unaffected.

Grey nodes belong to a station you are not docked to. To make the buddy use a staircase, put a Stair node (`T`) on the bottom and top landings and check with `L` that they connect - if not, Force-link them, or use `O` to make the stairs mandatory.

---

## Development Notes

Navigation - the plugin's biggest headache.

Isolated Inhale has no NavMesh, AI nodes, or any NPC navigation (that's why Breathless moves like an amoeba). I had to build the navigation code from scratch, and it turned out harder than expected.

The approach went through several iterations: raycast-based steering and auto‑door detection, pre‑recorded routes, auto-generated nodes, and a failed NavMesh attempt. Finally, I settled on an improved node‑based solution with debugging, manual editing, and better route planning.

Then came endless bug fixes: strict checks broke valid paths, relaxing them introduced line-of-sight (LOS) errors, fixing LOS triggered new edge cases - and the cycle repeated. This is probably the best I can do.

> ⚠️ **Before touching this code, read this**: The navigation/pathfinding system is cursed. If you want to make any changes there, you'd better have a PhD in mathematics. It's difficult to debug, involves many magic constants, and carries a high risk of regression. Known AI regression hot spots: stairs, elevated railings, long sections, the spacecraft‑station airlock, and the spacecraft itself.

---

## Known Issues / Limitations

- Navigation is good, but not perfect - the buddy can still get stuck sometimes.
- It will not follow you on a spacewalk, and it dies if it ends up in space.
- Doors may misbehave during special events.
- Room names in the HUD are unreliable - you may see `Front_M00` for most of the ship. The game has no room volumes; a "room" is whichever doorway sensor you last walked through. Cosmetic only, and not fixable.

---

## Plans

- Make meaningful room names instead of node numbers for buddy_goto.
- Add translation into Russian and other languages.
- Wear an EVA suit in a dangerous atmosphere, space walk through FuelStation (redrawn pilot suit texture is needed, since the game does not have an isolated suit skin for the player model, only as an item texture <_<).
- Support for multiple NPCs.
- Funny, strange, or scary events involving the NPC.
- Converting the mod into a public library for NPC mods.

---

## When will the mod be updated?

- If major updates break something important (tested on version v0.8.9).
- If the map changes (AI node updates, such as when ObservingStation is released).
- If enemy behavior changes or new enemies are added (aggro on the NPC).
- If the AI needs improvement (the main focus is on navigation, the set of supported actions, and behavior).

If you encounter bugs, please report them on the [issue tracker](https://github.com/bytenull1/yourbuddy-inhl/issues) (or the mod's discussion thread). You can also make changes to the mod yourself by creating a PR - see [CONTRIBUTING.md](CONTRIBUTING.md) for how to report bugs usefully, build, and test. Technical documentation is in [docs/](docs/README.md).

# Architecture - what lives where

Read this before opening source files, so you go straight to the two or three that matter.

YourBuddy is built on [NPC.Core](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/architecture.md), a separate plugin that owns the
nav graph and its A\*, every navigation probe, the walking agent (`NpcAgent`: path following,
steering, doors, rooms, vessels, space protection, air, the catch, death, footsteps, hands), the NPC
registry and the world every NPC shares: the game's events, door knowledge, kept rooms, the monster's
debug overrides, save sidecars, corpses, the lifecare terminal and the talk window. This file covers
the buddy: its body, and the **brain** that drives the agent.

`BuddyBehaviour` is the brain, one class split across partial files. A field or constant lives in the
partial that uses it; `BuddyBehaviour.cs` holds only what several partials share. Other classes reach
the buddy through a few `internal` members, never its fields.

---

## 1. Module map

| Folder | What is in it |
|---|---|
| `YourBuddy/` | `YourBuddyPlugin.cs`, the entry point |
| `Errands/ResourceDuty/` | resource duty settings, conversation controls and the shared refill/purchase routine; [resources](resources.md) |
| `Core/` | the buddies' list and save, spawning, skins, settings, `GameInternals` |
| `Brain/` | `BuddyBehaviour` and its partials, but for the anomalies |
| `Anomalies/` | the director, the catalogue, and `BuddyBehaviour.Anomaly.*.cs`: one partial per set piece |
| `Errands/` | the errand base, each errand, and the item rules they share |
| `Eva/` | the suit and the airlock run |
| `Commands/` | the console, the shared order bodies and the talk window |

| File | Owns | Talks to | Doc |
|---|---|---|---|
| `YourBuddyPlugin.cs` | BepInEx entry (depends on NPC.Core), config, `Log`, NPC.Core wiring (events, sidecar, room keeper), `SpawnBuddy` (one more buddy: clones the player prefab, strips components, shrinks the controller to 0.22 m, attaches the brain and NPC.Core's `NpcAgent`, registers the agent and its conversation) | `BuddyManager`, `GameInternals` | [game-model](game-model.md) |
| `BuddyAgentSettings.cs` | the agent's `NpcAgentSettings`, read live from the config | - | [NPC.Core agent](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/agent.md#1-attaching-an-agent) |
| `BuddyConsole.cs` | the console: `buddy` (help) and one command per category with subcommands, registered through `NpcConsole`; `@2` / `@name` / `@all` targeting | `BuddyCommands`, `BuddyManager` | [reference](reference.md#2-debug-commands) |
| `BuddyManager.cs` | the buddies: their list (`All`; their agents are in `NpcRegistry`), `Focus`, numbers and names; `Tick` (on `NpcEvents.Tick`), the `.buddy` sidecar's contents and the restore after a load | `BuddyBehaviour`, `NpcRegistry`, `NpcSaves`, `NpcVessels` | §5 |
| `BuddyBehaviour.cs` | brain state shared across partials, `Init`, the `INpcBrain` hooks, the HUD box and text, `ListLine` | `NpcAgent` | [NPC.Core agent](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/agent.md#3-the-brain) |
| `BuddyBehaviour.Modes.cs` | `UpdateFollow` (on the agent's `Pursue`), `UpdateRoute` (on `SimpleAdvance`), `FinishRoute`, `StartRoute`, `SetMode`, the reach task in hand | `NpcAgent` | [behaviour](behaviour.md) |
| `BuddyBehaviour.Fear.cs` | seeing the Breathless, stress, `FearState`, `HoldBackFromMonster`, the `Flee` mode | `NavProbe`, `NavGraph`, `GameInternals` | [fear](fear.md) |
| `BuddyBehaviour.Hide.cs` | hiding in a closet or locker: walk, teleport in/out, doors, who ends it | `NpcAgent` | [fear](fear.md) |
| `BuddyBehaviour.Anomaly.cs` | acting out an anomaly: starting one (`TryStartAnomaly`), the per-frame update, holds while watched, `EndAnomaly`, `Speak`; each set piece's own fields, constants and steps are in `BuddyBehaviour.Anomaly.<Name>.cs` (Sleeper, Shipyard, Doors, Stare, Vanish, Pipe, Move, Flicker, Nodes) | `PlayerView`, `BuddyGore`, `ScareSounds`, NPC.Core's `NpcInteraction` | [anomalies](anomalies.md) |
| `AnomalyDirector.cs` | when an anomaly happens and how far it may go: difficulty, the game's event tier, the roll, cooldown, the draw; the player's stress | `BuddyBehaviour`, `GameManager.EventSystem` | [anomalies](anomalies.md#1-how-often-and-how-far) |
| `Anomaly.cs`, `AnomalyLines.cs` | the catalogue (`AnomalyKind`, severity, weight, once only) and what the buddy says | - | [anomalies](anomalies.md#2-the-anomalies) |
| `AnomalyMemory.cs` | the kinds a save has had and the lines said, kept in the sidecar | `BuddySaveFile` | [anomalies](anomalies.md#once-per-save) |
| `BuddyDouble.cs` | a copy of the buddy's body with nothing else: the sleeper | - | [anomalies](anomalies.md#sleeper) |
| `AnomalyProps.cs`, `BbModel.cs` | the meat and blood an anomaly leaves, never saved; a Blockbench model read from the dll | `GameInternals`, Newtonsoft | [anomalies](anomalies.md#the-mess-and-the-meat-model) |
| `PlayerView.cs`, `ScareSounds.cs`, `BuddyGore.cs` | what the player sees; the game's FMOD events borrowed; the bloody texture and the flickers' overlays | `NavProbe`, `GameInternals`, `BuddySkin` | [anomalies](anomalies.md#4-being-seen) |
| `BuddyBehaviour.Autonomy.cs` | orders (`ApplyOrder`, `ApplyRouteOrder`, `RevokeOrder`), persistence, expiry, stand-down, bouts | `NavGraph` | [behaviour](behaviour.md) |
| `BuddyBehaviour.ChoreOrder.cs` | continuing Tidy, Sell and Play assignments between individual errand batches | `Errand` | [behaviour](behaviour.md#2-persistence) |
| `BuddyBehaviour.Mind.cs` | the utility decider: scores urges, draws one, starts its errand | `Errand`, `LifeSupport` | [behaviour](behaviour.md#3-the-decider) |
| `BuddyBehaviour.Errands.cs` | owns the errands; implements `IErrandBody`, their only way into the buddy | every errand | [behaviour](behaviour.md) |
| `ErrandLeg.cs` | `ErrandLeg`, NPC.Core's `ReachTask` as the current Route's reason: `Approach`, `End`, `Describe`, `EndsOnFlee`, `Holds` | `ReachTask` | [terminals](terminals.md) |
| `Errand.cs` | `IErrandBody`, and the `Errand` base: due time, last result, skip list, `Defer`, `StartNow`, `Describe`, `Count` | - | [behaviour](behaviour.md#3-the-decider) |
| `LifeSupport.cs` | switching on the oxygen generator or climate control | `IErrandBody` | [terminals](terminals.md) |
| `BuddySuit.cs` | the buddy's EVA suit: wearing a spare (the game's own equip semantics, the embedded skin), the spare rule, the watcher, the sidecar's suit id | `IErrandBody`, `LifeSupport`, `BuddySkin` | [eva](eva.md) |
| `EvaRun.cs` | one trip through an airlock, out or in: suit, chamber, wait, step through | `BuddySuit`, `IErrandBody`, `GameInternals` | [eva](eva.md#4-through-the-airlock) |
| `SuitFetchErrand.cs` | carrying a suit you forgot on the docked station back aboard | `IErrandBody`, `BuddySuit` | [eva](eva.md#6-bringing-a-forgotten-suit-home) |
| `SnackErrand.cs` | opening a container or finding loose food, eating one thing | `IErrandBody`, `Items`, `GameInternals` | [snacks](snacks.md) |
| `TidyErrand.cs` | a tidying round: several pieces of trash to a trash can | `IErrandBody`, `Items`, `GameInternals` | [items](items.md) |
| `SellErrand.cs` | a selling run: nearby trash boxes into one sell station, one press per load | `IErrandBody`, `Items`, NPC.Core's `SellPens`, `GameInternals` | [items](items.md) |
| `PlayErrand.cs` | a play session: carry near, carry far, or throw and fetch | `IErrandBody`, `Items`, `NavProbe` | [items](items.md) |
| `Items.cs` | item rules every errand shares: `IsTrash`, `IsPlaything`, `TakeBlocker`, containers and their doors, `ShuffleNearest`, the item-reach constants | `GameInternals` | [items](items.md) |
| `SkipList.cs` | what a task leaves out for a while, per object: `Skip`, `Has`, `Prune` | - | [items](items.md) |
| `GameInternals.cs` | **every** reflection accessor YourBuddy needs into game types (NPC.Core has its own) | - | [game-model](game-model.md) |
| `BuddyConversation.cs` | the buddy's side of NPC.Core's talk window: when it can talk, its title, commands and answers | `BuddyDialogCommands`, `BuddyManager` | [dialog](dialog.md) |
| `BuddyBehaviour.Conversation.cs` | player-facing status for greetings and the Status command | `BuddyConversation`, `BuddyDialogCommands` | [dialog](dialog.md) |
| `StoreErrand.cs`, `FurnitureStorage.cs`, `StorageOverflow.cs`, `StoragePolicy.cs`, `StorageItems.cs`, `StorageShape.cs`, `StorageAccess.cs` | sorting ship items into category homes or bounded floor overflow, reserving space and checking physical placement | `Errand`, `GameInternals.StorageZoneAccess` | [storing](storing.md) |
| `BuddyDialogCommands.cs` | keyword-matching typed text to an order, for one buddy or everyone | `BuddyCommands`, `BuddyRooms` | [dialog](dialog.md) |
| `BuddyRooms.cs` | the buddy's words for NPC.Core's `StationRooms` (the docked station's rooms by name) | `StationRooms` | [dialog](dialog.md#goto-by-room) |
| `BuddyCommands.cs` | the orders, shared by dialog and console, each for the buddy the caller chose; the only caller of `ApplyOrder` / `ApplyRouteOrder` / `RevokeOrder` | `BuddyBehaviour`, `NavGraph` | [behaviour](behaviour.md) |
| `BuddyCryoSpawn.cs` | a new game's buddy asleep in a prop cryo capsule until after the player's pod opens | `YourBuddyPlugin`, `BuddyManager`, `GameInternals` | [game-model](game-model.md#the-cryo-room) |
| `BuddyMode.cs`, `BuddySaveFile.cs` | enums (`BuddyMode`, `FearState`, `OrderPersistence`) and the save DTOs (`BuddySaveFile`, `BuddyState`) | - | - |
| `IsExternalInit.cs` | lets netstandard2.1 compile `init` and records | - | - |

YourBuddy patches nothing: every game hook it needs is NPC.Core's
([one-patch-per-game-hook](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#one-patch-per-game-hook)).

---

## 2. Dependency direction

```
NPC.Core (NpcEvents, NpcAgent) â”€â”€â–¶ BuddyManager, BuddyBehaviour (INpcBrain, INpcHider),
                                   BuddyConversation, the '.buddy' callback
BuddyManager â”€â”€â–¶ BuddyBehaviour.* â”€â”€â–¶ NpcAgent and the rest of NPC.Core (NavGraph, NavProbe, NpcRegistry, World)
      â”‚                  â”‚
      â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”´â”€â”€â–¶ GameInternals

BuddyBehaviour.Mind â”€â”€â–¶ Errand / LifeSupport â”€â”€â–¶ IErrandBody â—€â”€â”€ BuddyBehaviour.Errands
                                  â”‚
                                  â””â”€â”€â–¶ Items, SellPens, NpcHands, SkipList
```

Errands never see `BuddyBehaviour` or the agent: everything they may do to the buddy is a member of
`IErrandBody`, which the brain implements explicitly.

Every buddy's agent is registered with NPC.Core's `NpcRegistry` for as long as it exists: that is how
`NavProbe` ignores its colliders, how NPCs of other mods see it, and how NPC.Core's patches reach it
(door events, docking and rebuilds, the lifecare terminal, hiding spots, the talk window).

**`NavProbe` is the bottom of the stack.** Nothing else may duplicate its collider filter
([one-probe-basis](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#one-probe-basis)).

---

## 3. The brain

The agent's frame and slow phases, and when it calls the brain, are
[NPC.Core's agent.md §2-3](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/agent.md#2-the-frame).
The buddy's answers:

| Hook | The buddy |
|---|---|
| `SlowPhase` | 0: loads every room holding a sell station while `SellTrash` is on; 2: `UpdateFear`; 3: the decider (`UpdateAutonomy`) and `LifeSupport.Update` |
| `OverrideMovement` | in the talk window: hold still facing you; an anomaly that holds the body (`AnomalyHoldsBody`); hiding, but not on the walk there: `UpdateHide` |
| `Steer` | the walk to a hiding spot or an anomaly's walk (`WalkAnomalyLeg`: to a window, a wall, through a door), else the mode: Follow â†’ `UpdateFollow` (`Pursue`), Wander â†’ `Wander` on `wanderOwner`, avoiding nodes near the monster while not Calm, Route â†’ `UpdateRoute`, Stay â†’ `Stay`, Flee â†’ `UpdateFlee` |
| `Constrain` | `HoldBackFromMonster` |
| `Activity` | per mode: [behaviour.md §4](behaviour.md#4-every-place-that-reads-mode-outside-the-dispatch-switch) |
| `TryIdleFacing` | the monster above Calm, else where an anomaly looks (`AnomalyFaces`), else you in Follow |
| `OnWalkAbandoned` | a flee: `AbandonRetreat`; else `FinishRoute` |
| `Holds` | its errand leg or hiding spot |
| `Sheltered` | hidden in a closet |
| `OnInterrupted` | `EndAnomaly`, `ForceLeaveHidingSpot` |
| `OnDied` | `EndAnomaly`, mode `Dead` |
| `OnShipRebuilt` | a goto ends; see [NPC.Core's an-npc-rides-its-own-floor](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#an-npc-rides-its-own-floor) |

The planning loop behind Follow is the agent's `Pursue`
([NPC.Core's agent.md §5](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/agent.md#pursues-planning-loop)).

---

## 4. Caches and their lifetimes

| Cache | Where | Invalidated by |
|---|---|---|
| Every sell station, switched off or not | `SellErrand._allStations` | swept again every `SellStationRescan` (30 s) |
| Scene arrays for the collectors | `SceneScan.ThisFrame` | every frame |

NPC.Core's own caches (gates, graph edges, probes, doorway sensors, airlocks, pin panels, sell pens,
and each agent's environments and footstep doorways) are in
[its architecture](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/architecture.md#3-caches-and-their-lifetimes).

TTL caches heal themselves; don't add invalidation hooks. A static cache would outlive a scene: drop it
on `NpcEvents.WorldReset`.

A timed rescan asks `SceneScan.MayRescan` first, so at most one runs per frame; the others keep their
list a frame longer ([scene-sweeps-are-budgeted](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#scene-sweeps-are-budgeted)).

Owner transforms and docked state are **not** cached: `OwnerSnapshot` resolves them per graph query
([never-cache-node-world-positions](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#never-cache-node-world-positions)).

---

## 5. Persistence

| File | Written by | Content |
|---|---|---|
| `<save>.buddy` sidecar | NPC.Core's `NpcSaves`, after the game writes the save, from `BuddyManager.SidecarContents` | every buddy (`Buddies`: number, name, owner and owner-local position, rotation, alive, sleeping capsule), the door codes, the opened cryo capsule |

The game never reads `.buddy` files, so uninstalling the mod is save-safe
([mod-state-never-enters-the-vanilla-save](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#mod-state-never-enters-the-vanilla-save)).
With `SaveSupport` off the callback returns nothing, and NPC.Core deletes an old sidecar instead.
NPC.Core saves the door codes in its own `<save>.npccore` too; a load learns both.
Mode, orders and stress are not saved: a loaded buddy is in Follow, with no order, Calm. The sidecar
also writes the first buddy into the old single-buddy fields, so an older mod version restores that one;
a sidecar without `Buddies` is read as one buddy (`BuddySaveFile.StatesOf`).

The nav graph is NPC.Core's, in `BepInEx/config/NPC.Core/`
([navigation.md §1](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/navigation.md#where-the-nodes-come-from)).

NPC.Core deletes sidecars with their save and sweeps orphans
([one-sidecar-per-mod](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#one-sidecar-per-mod)). A load reaches the buddy as
`NpcEvents.SaveLoaded` â†’ `ArmPendingSpawn`; `Tick` restores the buddies once the scene is ready.

---

## 6. Buddy lifecycle

Three ways in, all through `YourBuddyPlugin.SpawnBuddy`, which adds one buddy to `BuddyManager.All`
(no cap):

- `buddy_spawn [N]`, which despawns every buddy first and spawns N in a row;
- a save's sidecar (`BuddyManager.Tick`), every buddy it holds;
- **a new game** (`SpawnOnNewGame`): the buddy spawns **asleep** (the agent's `Asleep`: no AI, no slow
  phases, no dialog) in the nearest shut prop capsule beside the player's pod, or at the Shipyard's
  origin if none can be used ([game-model.md](game-model.md#the-cryo-room)). It wakes
  `WakeAfterPodOpenSeconds` after the player's pod starts opening: the monitor fades, the door opens,
  then the AI starts. A save made while asleep restores it asleep. A new game has one buddy, so at most
  one ever sleeps.

`SpawnBuddy`:

1. clone the player prefab;
2. capture ragdoll parts via `GameInternals.PlayerControllerAccess` **before** stripping;
3. destroy every MonoBehaviour outside the animation whitelist, `Player` included
   ([an NPC is not a Player](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/game-model.md#1-an-npc-is-not-a-player));
4. shrink the `CharacterController` radius to 0.22 m so it fits any doorway;
5. add `BuddyBehaviour`, attach NPC.Core's `NpcAgent` with the body parts and `BuddyAgentSettings`,
   register the agent with `NpcRegistry`, and register its `BuddyConversation` with `NpcInteraction`.

Each buddy has a number (the lowest free; `@2` in commands) and a name from it (`Buddy`, `Buddy 2`),
both saved, and both the agent's `NpcIdentity`. Commands without a target go to `BuddyManager.Focus`:
the buddy last talked to or named, else the nearest living one. `Despawn` leaves the registry at once,
so a respawn in the same frame gets the numbers back. Ragdoll bodies stay kinematic until the agent's
`Die`, which attaches NPC.Core's `NpcCorpse`.

Buddies pass through each other ([npcs-never-block-each-other](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#npcs-never-block-each-other)),
share what they know about doors ([door-knowledge-is-shared](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#door-knowledge-is-shared)) and
leave alone what another is working on ([one-buddy-per-target](invariants.md#one-buddy-per-target)).

Space protection, riding a station and being parked with it are the agent's
([NPC.Core's agent.md](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/agent.md),
[an-npc-rides-its-own-floor](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#an-npc-rides-its-own-floor)).
A parked buddy is inactive: no update, HUD, lifecare icon or atmosphere damage. The same happens aboard
during a spacewalk away from a station. Docked, a buddy aboard stays awake while you are on the station,
though the game switches the ship's rooms off then ([an-unloaded-ship-parks-the-npc](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#an-unloaded-ship-parks-the-npc)).
A suited buddy standing in the chamber when you cycle out goes out with you, and floats in zero
gravity ([eva.md §7](eva.md#7-floating)).

---

## 7. Change impact - what else to update

| If you change | Also update |
|---|---|
| `FindPath` signature or `NavPath` | NPC.Core's agent, and here `UpdateFlee` / `TryPlanRetreat` / `PlanKeepsClear` / `EndFlee`, `WalkToHide`, `BuddyCommands.GoToNode`. Every construction must fill `WaypointFloorY`. `==` on a `NavPath` compares list references |
| an order the player can give | `BuddyCommands.cs`, via `ApplyOrder` / `ApplyRouteOrder`, never `SetMode` ([an-order-is-not-a-mode](invariants.md#an-order-is-not-a-mode)); and `BuddyDialogCommands.Names` **and its match order** |
| an urge the buddy can choose | `Mind.cs`: the `Urge` enum, `ScoreUrges` (its weight), `ActOn`, `ErrandOf`, `UrgeName`; a new errand is an `Errand` subclass created in `BuddyBehaviour.Errands.cs` |
| the buddy and the outside (space, airlocks, the suit) | `BuddySuit` / `EvaRun` / `SuitFetchErrand` ([eva.md](eva.md)); NPC.Core reads the suit through `BuddyAgentSettings.Suited` / `MayGoOutside`, and owns the side of the airlocks (`NpcAgent.IsOutside`, `Gravity`) and flight (`Floating`, `FlyFollow`, `FlyTo`) |
| what an errand may do to the buddy | only through `IErrandBody` (`Errand.cs`); a new need is a new member there, never a buddy field made internal |
| a search radius | its owner filter, the agent's `OnMyVessel` ([behaviour.md §3](behaviour.md#range)) |
| a `BuddyMode`, or anything writing `mode` | every site in [behaviour.md §4](behaviour.md#4-every-place-that-reads-mode-outside-the-dispatch-switch), above all `INpcBrain.Activity`, and [fear-owns-the-buddy](invariants.md#fear-owns-the-buddy) |
| something the agent should do differently | NPC.Core, for every NPC; a brain hook or setting if only the buddy wants it ([NPC.Core's agent.md](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/agent.md)) |
| whether the buddy can **see** something | only `NavProbe.CanSee` ([sight-stops-at-a-shut-door](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#sight-stops-at-a-shut-door)) |
| anything reflected from the game | an accessor in `GameInternals.cs`, never `GetField` elsewhere |
| a tuning constant | [reference.md](reference.md), and [same-level-tolerance-ceiling](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#same-level-tolerance-ceiling) |
| which partials use a field | declare it in the one partial that uses it, or in `BuddyBehaviour.cs` once two do |
| what the sidecar stores | `BuddySaveFile` / `BuddyState`, `CaptureState` and `RestorePosition` together |
| the buddy's worn suit across a load | `BuddyState.SuitId` and `BuddySuit.WornSuitId` / `RestoreWorn` together |
| something a buddy works on that others must leave alone | `ErrandLeg.Holds`, and a `TakenByAnother` check where candidates are collected ([one-buddy-per-target](invariants.md#one-buddy-per-target)) |
| state every buddy should share | a static in the owning partial, dropped on `NpcEvents.WorldReset`; state every NPC of any mod should share belongs in NPC.Core |
| a new entry point into buddy code (a Unity message, an event, a command) | wrap it in `NpcRegistry.Acting(buddy.Agent)`, or its lines lose the buddy's name ([logging.md §4](logging.md#4-rules-for-adding-logs)) |
| a game hook | NPC.Core's `NpcEvents` or an optional interface ([one-patch-per-game-hook](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#one-patch-per-game-hook)) |
| where the player's floor height comes from | only the agent's `FloorUnderPlayer`, through `OnPlayersDeck` ([follow-arrival-is-level-aware](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#follow-arrival-is-level-aware)) |

Continuing player storage orders live in `BuddyBehaviour.StorageOrder.cs`; physical jobs remain in `StoreErrand`.

`StorageRepackPlan` bounds staging/refill order; `StoreErrand.Repacking` validates and schedules
those moves through the existing storage state machine.

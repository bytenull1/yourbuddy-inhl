# Architecture - what lives where

Read this before opening source files, so you go straight to the two or three that matter.

`BuddyBehaviour` is one class split across partial files. A field or constant lives in the partial
that uses it; `BuddyBehaviour.cs` holds only what several partials, or its own `Update`, share.
Other classes reach the buddy through a few `internal` members, never its fields.

---

## 1. Module map

| File | Owns | Talks to | Doc |
|---|---|---|---|
| `YourBuddyPlugin.cs` | BepInEx entry, config, `SpawnBuddy` (clones the player prefab, strips components, shrinks the controller to 0.22 m), despawn | `BuddyManager`, `GameInternals` | [game-model](game-model.md) |
| `Patches.cs` | every Harmony patch (one nested class per feature) and every console command | all | [game-model](game-model.md) |
| `BuddyManager.cs` | singleton lifecycle: `Tick`, `.buddy` sidecar, lifecare, `FloorOwner`, buddy-closed gates | `BuddyBehaviour`, `BuddyNodeGraph`, `GameInternals` | [lifecare](lifecare.md) |
| `BuddyBehaviour.cs` | state shared across partials, `Init`, `Update` / `SlowUpdate` dispatch, `FloorUnderBuddy`, `GroundPos`, HUD box and text | - | [reference](reference.md) |
| `BuddyBehaviour.Navigation.cs` | `UpdateFollow` / `UpdateWander` / `UpdateRoute`, plan commitment, `AdvancePastReachedWaypoints`, `TryStepOff`, `StartRoute`, `SetMode` | `BuddyNodeGraph`, `NavProbe` | [navigation](navigation.md) |
| `BuddyBehaviour.Obstacles.cs` | body probes, whisker steering, auto-jump, `ApplyMovement`, stuck and oscillation recovery, `EmergencyUnstick`, blocker diagnostics | `NavProbe` | [navigation](navigation.md) |
| `BuddyBehaviour.Doors.cs` | `GateIsPassable` / `GateBlocksRouting`, `HandleDoors`, close-behind, `DoorwayBlocker`, `StepOutOfDoorway`, room loading, door codes, `SegmentBlockedByDoor` | `NavProbe`, `GameInternals`, `BuddyManager` | [doors](doors.md) |
| `BuddyBehaviour.Environment.cs` | room tracking, floor plane, environment and space protection, atmosphere damage, the Breathless catch, `IsAboardPlayerShip`, `ParkWithShip`, `Die` | `GameInternals`, `BuddyManager` | [game-model](game-model.md) |
| `BuddyBehaviour.Fear.cs` | seeing the Breathless, stress, `FearState`, `HoldBackFromMonster`, the `Flee` mode | `NavProbe`, `BuddyNodeGraph`, `GameInternals` | [fear](fear.md) |
| `BuddyBehaviour.Autonomy.cs` | orders (`ApplyOrder`, `ApplyRouteOrder`, `RevokeOrder`), persistence, expiry, stand-down, bouts | `BuddyNodeGraph` | [behaviour](behaviour.md) |
| `BuddyBehaviour.Mind.cs` | the utility decider: scores urges, draws one, starts its errand | `Errand`, `LifeSupport` | [behaviour](behaviour.md#3-the-decider) |
| `BuddyBehaviour.Reach.cs` | the walk into reach: route to a node, probed straight walk, `InReach`, `Defer` / `Recover` on failure, `DropReachTask` | `BuddyNodeGraph`, `NavProbe` | [terminals](terminals.md) |
| `BuddyBehaviour.Errands.cs` | owns the errands; implements `IErrandBody`, their only way into the buddy | every errand | [behaviour](behaviour.md) |
| `BuddyBehaviour.Carry.cs` | the hands, putting down before a save, `OnMyVessel` | `BuddyHands` | [items](items.md) |
| `ReachTask.cs` | `ReachTask` (what to reach, stand points, `StandAllowed`) and `ErrandLeg` (`Approach`, `End`, `Describe`, `EndsOnFlee`) | `SellPens` | [terminals](terminals.md) |
| `Errand.cs` | `IErrandBody`, and the `Errand` base: due time, last result, skip list, `Defer`, `StartNow`, `Describe`, `Count` | - | [behaviour](behaviour.md#3-the-decider) |
| `LifeSupport.cs` | switching on the oxygen generator or climate control | `IErrandBody` | [terminals](terminals.md) |
| `SnackErrand.cs` | opening a container or finding loose food, eating one thing | `IErrandBody`, `Items`, `GameInternals` | [snacks](snacks.md) |
| `TidyErrand.cs` | a tidying round: several pieces of trash to a trash can | `IErrandBody`, `Items`, `GameInternals` | [items](items.md) |
| `SellErrand.cs` | a selling run: nearby trash boxes into one sell station, one press | `IErrandBody`, `Items`, `SellPens`, `GameInternals` | [items](items.md) |
| `PlayErrand.cs` | a play session: carry near, carry far, or throw and fetch | `IErrandBody`, `Items`, `NavProbe` | [items](items.md) |
| `Items.cs` | item rules every errand shares: `IsTrash`, `IsPlaything`, `TakeBlocker`, containers and their doors, `ShuffleNearest`, the item-reach constants | `GameInternals` | [items](items.md) |
| `SellPens.cs` | the fenced footprint of every sell station: `InAFencedPen` | `GameInternals` | [items](items.md#4-selling-trash-boxes) |
| `BuddyHands.cs` | holding one item: `PickUp`, hold point, re-owning to the carrier's room, `ReachTo`, `PutDown`, `Drop`, `Release` | - | [items](items.md) |
| `SkipList.cs` | what a task leaves out for a while, per object: `Skip`, `Has`, `Prune` | - | [items](items.md) |
| `BuddyBehaviour.Hide.cs` | hiding in a closet or locker: walk, teleport in/out, doors, who ends it | - | [fear](fear.md) |
| `BuddyBehaviour.Presentation.cs` | footstep sounds, animator parameters, debug line renderers | `GameInternals` | - |
| `BuddyNodeGraph.cs` | node storage and ownership, `TryVesselOf`, JSON persistence, edge cache, node floor cache, A\* (`FindPath`), `CanReachEntry` | `NavProbe`, `GameInternals` | [navigation](navigation.md) |
| `BuddyNodeEditor.cs` | F8 editor overlay, node and link placement | `BuddyNodeGraph` | [reference](reference.md) |
| `NavProbe.cs` | **every physics probe**: floors, line of sight, `CanSee`, collider filter, gate cache | `BuddyManager` (buddy transform) | [probes](probes.md) |
| `GameInternals.cs` | **every** reflection accessor into game types | - | [game-model](game-model.md) |
| `BuddyDialog.cs` | the talk window: interaction hook, sight check, input handover, layout | `DialogSkin`, `BuddyDialogCommands` | [dialog](dialog.md) |
| `DialogSkin.cs` | how the window is drawn | - | [dialog](dialog.md) |
| `BuddyDialogCommands.cs` | keyword-matching typed text to an order | `BuddyCommands` | [dialog](dialog.md) |
| `BuddyCommands.cs` | the orders, shared by dialog and console; the only caller of `ApplyOrder` / `ApplyRouteOrder` / `RevokeOrder` | `BuddyBehaviour`, `BuddyNodeGraph` | [behaviour](behaviour.md) |
| `BuddyCryoSpawn.cs` | a new game's buddy asleep in a prop cryo capsule until after the player's pod opens | `YourBuddyPlugin`, `BuddyManager`, `GameInternals` | [game-model](game-model.md#the-cryo-room) |
| `BuddyCorpse.cs` | makes the dead ragdoll carryable, without the game's `Grabbable` | - | [invariants](invariants.md#mod-state-never-enters-the-vanilla-save) |
| `BuddyMode.cs`, `BuddySaveFile.cs` | enums (`BuddyMode`, `FearState`, `OrderPersistence`) and the save DTO | - | - |
| `AiDebug.cs` | `ai_disable` / `ai_notarget`; sole owner of the monster's component flags | - | [reference](reference.md) |
| `IsExternalInit.cs` | lets netstandard2.1 compile `init` and records | - | - |

---

## 2. Dependency direction

```
Patches ──▶ BuddyManager ──▶ BuddyBehaviour.* ──▶ BuddyNodeGraph ──▶ NavProbe
   │             │                  │                    │             │
   └─────────────┴──────────────────┴────────────────────┴─────────────┘
                          all ──▶ GameInternals

BuddyNodeEditor ──▶ BuddyNodeGraph

BuddyBehaviour.Mind ──▶ Errand / LifeSupport ──▶ IErrandBody ◀── BuddyBehaviour.Errands
                                  │
                                  └──▶ Items, SellPens, BuddyHands, SkipList
```

Errands never see `BuddyBehaviour`: everything they may do to it is a member of `IErrandBody`,
which the buddy implements explicitly.

One back-edge: `NavProbe` reads `BuddyManager.CurrentBuddy` to ignore the buddy's own colliders. It
works with no buddy spawned.

**`NavProbe` is the bottom of the stack.** Nothing else may duplicate its collider filter
([one-probe-basis](invariants.md#one-probe-basis)).

---

## 3. Per-frame flow

### `BuddyBehaviour.Update()`

```
mode dispatch ─ Follow / Wander / Route / Stay / Flee   → desired velocity, walkingStairLeg
     ↓
stuck sidestep                                not on a stair leg
     ↓
HoldBackFromMonster(desired)                  Alert or Scared: no step toward the Breathless
     ↓
HandleDoors(desired)                          may open a gate or hold position
     ↓
TryAutoJump(desired)                          on the raw direction, before steering; not on a stair leg
     ↓
SteerAroundObstacles(desired)                 skipped mid-jump and on a stair leg
     ↓
UpdateStuckDetection → ApplyMovement → UpdateAnimation → UpdateDebugVisuals
```

Auto-jump reads the raw direction because steering would turn it away from the obstacle it should
hop; steering pauses mid-jump so it cannot cancel the hop. `walkingStairLeg` is reset before dispatch
and set only by Follow and Wander ([a-stair-leg-is-walked-not-improvised](invariants.md#a-stair-leg-is-walked-not-improvised)).

Atmosphere damage is not a phase: `AtmosphereTick` listens to `GameManager.OnTick`
([game-model.md](game-model.md#atmosphere-kills-by-the-players-rule)).

### `BuddyBehaviour.SlowUpdate()`

Runs every 0.06 s, one phase per call, so each phase runs every ~0.24 s. **Don't add a fifth phase** -
it slows every phase to ~0.30 s; add work to an existing one. `SlowUpdate` runs before `Update`'s
catch and dialog early-outs, so its work must guard against both.

| Phase | Work |
|---|---|
| 0 | floor plane, room tracking, environment |
| 1 | open-space state and protection |
| 2 | Breathless catch, then fear ([fear.md](fear.md)) |
| 3 | door close-behind, the decider ([behaviour.md](behaviour.md)), the terminal flip check |

### Planning loop (Follow)

```
every NavPathRecalcInterval (0.22 s):
    is the plan still valid?   goal drift · entry stretch · on route · stair leg
        ↓ no
    FindPath(start = FloorUnderBuddy(), goal = player, cameFrom, avoidEntry)
        seeding → A* over cached edges → finish leg or approach node
        ↓
    replace plan; navPathIndex = 0 on a blocked entry
every frame:
    AdvancePastReachedWaypoints() → steer at waypoint[navPathIndex]
```

Details: [navigation.md](navigation.md).

---

## 4. Caches and their lifetimes

| Cache | Where | Invalidated by |
|---|---|---|
| Gate list | `NavProbe.CachedGates` | 5 s TTL; `InvalidateGates()` on scene load and dock change |
| Gate placement per frame | `NavProbe.FrameGates` | every frame ([probes.md](probes.md#caches)) |
| Gate openings | `NavProbe.GateOpenings` | `InvalidateGates()` only |
| Impassable gates | `BuddyBehaviour.impassableGates` | 1 s TTL |
| Node edges, priority exits, id index | `BuddyNodeGraph._edges`, `NodesById` | `_edgesDirty`, dock change, ship layout change, `MaxEdgeDist` change |
| Node hover | `BuddyNodeGraph.NodeHover` | 3 s TTL; cleared on edge rebuild |
| Space objects | `BuddyNodeGraph.SpaceObjects` | 5 s TTL, or at once when one is destroyed |
| Detectors / airlocks / environments | `BuddyBehaviour` | 5 s TTL each |
| Password gates → panels | `BuddyBehaviour.passwordGates` | rebuilt every 5 s |
| Buddy-closed gates | `BuddyManager.BuddyClosedGates` | 6 s window |

TTL caches heal themselves; don't add invalidation hooks. Gates and edges need explicit invalidation
because a dock or scene change alters the *set* of objects.

Owner transforms and docked state are **not** cached: `OwnerSnapshot` resolves them per graph query
([never-cache-node-world-positions](invariants.md#never-cache-node-world-positions)).

---

## 5. Persistence

| File | Written by | Content |
|---|---|---|
| `BepInEx/config/YourBuddyRoutes/nodegraph.json` | `BuddyNodeGraph.Save` (F6, or 20 s after a change) | your own nodes and manual links per owner, and which owners you forked |
| `BepInEx/config/YourBuddyRoutes/nodegraph.bundled.json` | `BuddyNodeGraph.LoadBundled`, from an embedded resource | the shipped graph; rewritten when `BundleVersion` changes |
| `<save>.buddy` sidecar | `SaveParser.WriteSaveFile` postfix | owner and owner-local position, rotation, alive, door codes, cryo capsule state |

The game never reads `.buddy` files, so uninstalling the mod is save-safe
([mod-state-never-enters-the-vanilla-save](invariants.md#mod-state-never-enters-the-vanilla-save)).
Mode, orders and stress are not saved: a loaded buddy is in Follow, with no order, Calm.

`nodegraph.json` is user data and **evidence** for routing questions - read it. The bundled file is a
build input: regenerate it with `tools/bundle_nodegraph.py`, never by hand
([navigation.md §1](navigation.md#where-the-nodes-come-from)).

Sidecars are deleted with their save (`DeleteSaveFile` / `ClearSaveFiles` postfixes), and orphans are
swept on `SyncSavePreviews`.

---

## 6. Buddy lifecycle

Three ways in, all through `YourBuddyPlugin.SpawnBuddy`:

- `spawn_buddy`;
- a save's sidecar (`BuddyManager.Tick`);
- **a new game** (`SpawnOnNewGame`): the buddy spawns **asleep** (no AI, no `SlowUpdate`, no dialog)
  in the nearest shut prop capsule beside the player's pod, or at the Shipyard's origin if none can
  be used ([game-model.md](game-model.md#the-cryo-room)). It wakes `WakeAfterPodOpenSeconds` after
  the player's pod starts opening: the monitor fades, the door opens, then the AI starts. A save made
  while asleep restores it asleep.

`SpawnBuddy`:

1. clone the player prefab;
2. capture ragdoll parts via `GameInternals.PlayerControllerAccess` **before** stripping;
3. destroy every MonoBehaviour outside the animation whitelist, `Player` included
   ([game-model.md §1](game-model.md#1-the-buddy-is-not-a-player));
4. shrink the `CharacterController` radius to 0.22 m so it fits any doorway;
5. add `BuddyBehaviour` and `BuddyDialog`.

Only one buddy exists. Ragdoll bodies stay kinematic until `Die()`, which attaches `BuddyCorpse`.

**Space protection:** the buddy remembers a safe spot in the frame it rides; if it ends up in open
space it is teleported back and replans. It will not follow the player onto a spacewalk.

**Ownership** (`UpdateOwnerAnchor`, phase 0): the buddy is parented to the floor it stands on, so it
rides a station and is parked with it when you leave
([the-buddy-rides-its-own-floor](invariants.md#the-buddy-rides-its-own-floor)). A parked buddy is
inactive: no update, HUD, lifecare icon or atmosphere damage. The same happens aboard during a
spacewalk ([an-unloaded-ship-parks-the-buddy](invariants.md#an-unloaded-ship-parks-the-buddy)).

---

## 7. Change impact - what else to update

| If you change | Also update |
|---|---|
| `FindPath` signature or `NavPath` | `UpdateFollow` (and `SameRemainingPath`), `UpdateWander`, `UpdateRoute`, `StartRoute`, `UpdateFlee` / `TryPlanRetreat` / `PlanKeepsClear` / `EndFlee`, `UpdateDebugVisuals`, `BuddyCommands.GoToNode`. Every construction must fill `WaypointFloorY`. `==` on a `NavPath` compares list references |
| how a stair leg is walked or abandoned | `TryStairLegBand` is the one definition; `HeadingAlongPlan`, `HasLeftStairLeg`, `AdvancePastReachedWaypoints`, `Update` and `UpdateStuckDetection` read it |
| what counts as a reachable entry | only `BuddyNodeGraph.CanReachEntry` ([one-entry-predicate](invariants.md#one-entry-predicate)) |
| which doors the buddy may **open** | `GateIsPassable` - and check the routing answer ([opening-is-not-passing](invariants.md#opening-is-not-passing)) |
| what routing goes **around** | `GateBlocksRouting` - airlock and docking gates must stay routable |
| an order the player can give | `BuddyCommands.cs`, via `ApplyOrder` / `ApplyRouteOrder`, never `SetMode` ([an-order-is-not-a-mode](invariants.md#an-order-is-not-a-mode)); and `BuddyDialogCommands.Names` **and its match order** |
| an urge the buddy can choose | `Mind.cs`: the `Urge` enum, `ScoreUrges` (its weight), `ActOn`, `ErrandOf`, `UrgeName`; a new errand is an `Errand` subclass created in `BuddyBehaviour.Errands.cs` |
| what an errand may do to the buddy | only through `IErrandBody` (`Errand.cs`); a new need is a new member there, never a buddy field made internal |
| a search radius | its owner filter, `OnMyVessel` ([behaviour.md §3](behaviour.md#range)) |
| a `BuddyMode`, or anything writing `mode` | every site in [behaviour.md §4](behaviour.md#4-every-place-that-reads-mode-outside-the-dispatch-switch), and [fear-owns-the-buddy](invariants.md#fear-owns-the-buddy) |
| whether the buddy can **see** something | only `NavProbe.CanSee` ([sight-stops-at-a-shut-door](invariants.md#sight-stops-at-a-shut-door)) |
| a `ThinLos` parameter | re-check the entry allowance and the stair seed cap |
| the gate-frame rule | keep it hit-point based and the *opening* ([gate-frame-hit-point](invariants.md#gate-frame-hit-point), [gate-carveout-is-the-opening](invariants.md#gate-carveout-is-the-opening)) |
| which layers a probe sees | only `NavProbe.ProbeLayers` ([probe-what-the-body-collides-with](invariants.md#probe-what-the-body-collides-with)) |
| a plan-invalidation reason | does it fire on Force/Priority segments? Then it needs an `IsForced` guard |
| anything reflected from the game | an accessor in `GameInternals.cs`, never `GetField` elsewhere |
| a tuning constant | [reference.md](reference.md), and [same-level-tolerance-ceiling](invariants.md#same-level-tolerance-ceiling) |
| which partials use a field | declare it in the one partial that uses it, or in `BuddyBehaviour.cs` once two do |
| the whisker capsule | only `BodyCastCapsule` ([whiskers-are-body-shaped](invariants.md#whiskers-are-body-shaped)) |
| what steering treats as an obstacle | only `NavProbe.HitIsWalkableGround` ([walkable-ground-is-not-an-obstacle](invariants.md#walkable-ground-is-not-an-obstacle)) |
| the shipped node graph | regenerate with `tools/bundle_nodegraph.py` **and bump `BundleVersion`**, or installs won't pick it up |
| what the sidecar stores | `BuddySaveFile`, `CaptureSaveFile` and `RestorePosition` together |
| which ship nodes are live | only `OwnerSnapshot` (`IsLive`, `WorldOf`, `LinkIsLive`) ([a-ship-node-rides-its-room](invariants.md#a-ship-node-rides-its-room)) |
| matching a scene object to its vessel | only `TryVesselOf` ([a-station-owns-its-interior-by-reference](invariants.md#a-station-owns-its-interior-by-reference)) |
| where a body's floor height comes from | only `FloorUnderBuddy` / `FloorUnderPlayer`; `FindPath` takes `start.y` as given ([the-start-point-is-already-a-floor](invariants.md#the-start-point-is-already-a-floor)) |

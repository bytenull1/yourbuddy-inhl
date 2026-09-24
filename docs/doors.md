# Doors

`BuddyBehaviour.Doors.cs`, plus the `Gate.FailClose` postfix in `Patches.cs`. Read the game's door
model first: [game-model.md §3](game-model.md#3-gate-the-door-state-machine) - most of the difficulty
comes from it.

---

## 1. Two different questions

"May the buddy **open** it?" and "should routing go **around** it?" are different
([opening-is-not-passing](invariants.md#opening-is-not-passing)).

| Gate | `GateIsPassable` - open it? | `GateBlocksRouting` - avoid it? |
|---|---|---|
| already open | yes | no |
| `Gate.Locked` | no | **yes** |
| driven by detectors, but none is switched on, or every one needs a suit the player lacks ([the-buddy-opens-only-what-the-player-could](invariants.md#the-buddy-opens-only-what-the-player-could)) | no | **yes** |
| airlock door, docking hatch | no - venting risk | **no** - the player opens these |
| `ElectricityPanelGate` | no ([keep-electricitypanelgate-excluded](invariants.md#keep-electricitypanelgate-excluded)) | no - a wall panel |
| pin-code door | only with a known code | **yes**, unless the code is known |
| anything else | yes | no |

`HandleDoors` uses the left column; path search uses the right. At a door closed to the player it
logs why, at most every 5 s:

```
[ai] Not opening 'Door02' - its doorway sensor is switched off, so you cannot walk it open either
[ai] Not opening 'Door02' - it opens only for someone in a suit, and you have none
```

---

## 2. Opening

Every 0.2 s `HandleDoors` casts 1.2 m ahead at chest height. If it hits a `Gate` that
`GateIsPassable` allows, the buddy opens it, waits for `FullyOpened` (5 s timeout) and arms a
close-behind. Airlock and docking gates it never opens; it waits for the player.

Opening a gate also enables the room content on both sides, so the buddy never walks into an
unloaded room.

### Which rooms the buddy loads

The game switches rooms on and off as the player passes doorways: both sides while the door is
open, only the player's side once it shuts. The buddy follows the same door rule ([the-buddy-loads-rooms-by-the-door-rule](invariants.md#the-buddy-loads-rooms-by-the-door-rule)):

| Room | Loaded when |
|---|---|
| the room the buddy is in | always; kept on if the game switches it off |
| the room behind the nearest doorway (within 3.5 m) | only while that door is open |
| both rooms of a door | the buddy opens it |
| an errand's box or sell station room | the errand needs it |
| a room holding a sell station | always, while `SellTrash` is on ([a-sell-station-room-stays-loaded](invariants.md#a-sell-station-room-stays-loaded)) |

It switches rooms off when a door it shut finishes its close (`Gate.OnClosed`, which drives
`EntryDetector.DoorCheckForEnter`), away from the player; at the doorway the game switches the rooms
around the player itself. Both rooms of that doorway are checked, whoever switched them on: a save
restores every room as it was saved ([game-model.md](game-model.md#which-rooms-are-loaded-is-saved)).
Checking both matters when the buddy walks through two rooms in a row. The first door can finish
closing only after the buddy has left the second room, and the room between is re-checked once its
last door shuts. A room stays on when:

- the buddy is in it: its tracked room, or the side of this doorway it has just come in by;
- the player's room is it;
- the player is outside on a spacewalk (`SpaceStation.OnStationExit` shows every room then);
- its content holds a `SellStation` ([a-sell-station-room-stays-loaded](invariants.md#a-sell-station-room-stays-loaded));
- any other doorway into it has an open door, or no door;
- that doorway is one the game keeps loaded on both sides (`optimize` off, as at the docking airlock).

The side test (`TryInnerSide`) only counts while the tracked room is one of this doorway's two rooms.
It is a plane: most of YardHallway lies on YardCryo's side of the YardCryo door.

A room it leaves with the door open stays on until someone shuts that door, as for the player. Ship
rooms are never switched off this way: they have no doorway sensors. A room the buddy never walks out
of keeps the state the save gave it.

Each switch-on and switch-off is logged at level 1, with how many rooms around the doorways are on;
switch-ons at most once per room per 5 s. Every station door is named `Door02`, so lines name the
room across instead. Level 2 adds why a room stayed on:

```
[ai] Loaded room 'YardCryo' (opening the door to 'YardLibrary') - 7 rooms loaded
[ai] Unloaded room 'YardCryo' (door to 'YardLibrary' shut) - 6 rooms loaded
[ai] Keeping room 'YardLibrary' loaded: its door to 'YardCryo' is open
[ai] Keeping room 'YardHallway' loaded: you are in it
```

### Password doors

The player gives a code through the [dialog](dialog.md) or `buddy_password`. Codes live in
`knownPinCodes` and are saved in the `.buddy` sidecar
([mod-state-never-enters-the-vanilla-save](invariants.md#mod-state-never-enters-the-vanilla-save)).

At a shut pin-code door whose code it knows, the buddy walks within `PinPanelReachDist` of the panel
and calls `PinCode.ForceValidate()`. The door opens through the game's own wiring
([the-buddy-only-knows-codes-it-was-told](invariants.md#the-buddy-only-knows-codes-it-was-told)).

**Limitation:** the buddy must reach a panel. A door with a panel only on the far side cannot be
opened from the near side.

---

## 3. Routing around what it cannot open

`FindPath` rejects any stretch that passes **through** a blocking gate's own volume - in seeding,
fallback, expansion and the finish leg, Force and Priority edges included
([locked-doors-block-edges](invariants.md#locked-doors-block-edges)). It tests the volume, not a
radius: a radius also blocked the corridor running past a shut airlock.

With no route that avoids the door, the buddy **stands still** and says so
([stand-still-when-door-blocked](invariants.md#stand-still-when-door-blocked)):

```
[ai] No route that avoids a door I cannot open - waiting here (1 closed to me)
```

---

## 4. Closing behind itself

`pendingDoorCloses` is a list of doors the buddy owes a close
([pending-closes-are-a-list](invariants.md#pending-closes-are-a-list)), worked in `SlowUpdate`
phase 3.

- **Dropped** when the gate is destroyed, already shut, locked, auto-doors are off, or 90 s have
  passed since arming.
- **Not fired** until the buddy has crossed to the far side
  ([close-only-what-you-walked-through](invariants.md#close-only-what-you-walked-through)).
- **Deferred** when the doorway is occupied:

| Situation | Response |
|---|---|
| the buddy is within `DoorwaySelfRadius` | `StepOutOfDoorway`, retry in 1 s; never force it ([never-force-a-close-into-the-buddy](invariants.md#never-force-a-close-into-the-buddy)) |
| something else is in the opening | log the blocker (throttled), retry in 1.5 s; after `DoorDeferGiveUp`, close anyway |

Deferral tracks `DeferredSince` and never touches `ArmedAt`
([no-permanent-deferral](invariants.md#no-permanent-deferral)).

Occupancy uses the gate's own `AntiCrasher` bounds, on that sensor's layer row
([occupancy-asks-the-anticrasher](invariants.md#occupancy-asks-the-anticrasher)), falling back to a
1.2 m sphere only if reflection fails. It skips the gate's own children and the buddy.

A successful close is registered with `BuddyManager.NoteBuddyClosedGate` so the `EntryDetector`
patch knows it was ours ([buddy-closes-must-not-move-the-player](invariants.md#buddy-closes-must-not-move-the-player)).

**Why retry at all:** `Gate.CloseRoutine` re-enables the `AntiCrasher` triggers, and a trigger that
already overlaps a body fires `FailClose → Open`. So: check clearance, then retry every 3 s, up to
ten times.

---

## 5. Closes the buddy blocked

The buddy never closes a door it did not open - that would shut doors behind the player.

**One exception.** The game never retries a failed close
([game-model](game-model.md#3-gate-the-door-state-machine)), so a door the buddy stood in stays open
for the rest of the session. `Patches.Gate_FailClose_Postfix → NoteCloseFailed` arms a close when the
buddy is within `DoorwaySelfRadius` and the gate is not an airlock or password gate. It must not
re-arm a gate already pending ([fail-close-must-not-rearm](invariants.md#fail-close-must-not-rearm)).

---

## 6. Getting out of the way

`StepOutOfDoorway` walks the buddy 1.8 m clear for 1.5 s (4 s cooldown), only when it is not already
walking somewhere. Without it a buddy standing near the player could hold its own door open forever.

The step-off works in every mode ([step-off-applies-in-every-mode](invariants.md#step-off-applies-in-every-mode)).
`followStepOffTarget` / `followStepOffUntil` are shared with obstacle recovery and the no-route
step-off; the last writer wins, which is fine - all three just want to move briefly.

---

## 7. What a healthy capture looks like

```
[ai] Opening door 'Door02'
[ai] Standing in 'Door02' - stepping clear so it can close      (only if it was in the way)
[ai] Closing door 'Door02' behind itself (attempt 1)
```

If the close never happens, look for the blocker line:

```
[ai] Waiting to close 'Door02': 'PinCodePanel' (layer 'Interactable') is in the doorway
```

---

## 8. Known limitations

- **Doors the buddy did not open are not closed**, except the case in §5.
- **Owed closes are lost on despawn, death or load.** Those doors stay open. Not worth saving.

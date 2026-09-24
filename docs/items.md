# Items - carrying, tidying, selling, idle play

`BuddyHands.cs` (holding one item), `TidyErrand.cs` (trash into a trash can), `SellErrand.cs` (trash
boxes to a sell station), `PlayErrand.cs` (idle play), all walking on `BuddyBehaviour.Reach.cs`, with
the shared item rules in `Items.cs`.
Config (General): `Tidying`, `TidyIntervalMinutes` (5), `SellTrash`, `ItemPlay`, `ItemPlayAnything`
(off), `ItemPlayIntervalMinutes` (5). Console: `buddy_tidy`, `buddy_sell`, `buddy_play`, `buddy_mind`.

---

## 1. What the game has

**Items are `Grabbable`s on layer 9.** "Held" means `IsGrabbed` (`grabPoint != null`);
`restrictGrab` blocks grabbing. A player throw is `Release` plus an impulse of 10 along the view.

**The game's own carry is `Crate.ParentItems`:** `restrictGrab`, velocities zeroed, interpolation off
(which also silences impact sounds), `FreezePhysics`, child colliders off, parented.
`UnparentItems` reverses it, also on save.

**The trash can** (`TrashCan`) takes items through a child `ItemDestroyer`: a 0.25 × 0.1 × 0.1 m
trigger at local (0, 0.54, −0.08). Any non-trigger collider on layer 9 with a `Grabbable` entering it
is deactivated; one without `CanTrash` is thrown back out along `−forward`. At 25 volume the can drops
a `Trash_Box` (signature `TRASH_BOX`, sellable).

**`CanTrash` is not "is trash"** - first-aid kits and energy cells have it. Trash is signature `TRASH`,
or something `TurnToTrash` changed: used-up or spoiled `Food`, an empty `SeedPack`, a broken
`LootBox`. A partly drained `ResourceContainer` is left alone (it may still hold resources).

**Machines load items through `ItemDetector` zones** (sell station, furnace, airlocks, elevators).
What is in one was put there.

**The sell station** (`SellStation`; at the Fuel, Oxygen, Shipyard and Solar stations) is a 2 × 2 m
platform with a cage `Gate` that drops over it. Fields, via `GameInternals.SellStationAccess`:

| Field | What |
|---|---|
| `itemDetector` | trigger over the platform: what is loaded (1.85 × 1.5 × 1.85 m) |
| `instantDetector` | a **player** in it when the gate closes dies (2 × 2 × 2 m) |
| `gate` | the cage; its `AntiCrasher` reopens it if anything is underneath while closing |
| `sellButton` | `Space.Button` on a panel 1.6 m out, behind a short fence |

Pressing: `Button.Interact(player)` → `SellStation.Close(player)` (only when fully open) → gate closes
→ `Sell`: **every** `CanSell && Enabled` item in the zone pays `SellPrice` to that player and is
destroyed. A `Trash_Box` sells for 459 on Normal.

---

## 2. Carrying

One item at a time (`held`). Picking up is `Crate.ParentItems` **without parenting to the buddy**: the
item stays under a room's content, and `LateUpdate` moves it to the hold point (`HoldHeight` 0.95 m
above the **feet**, `HoldForward` 0.45 m ahead) at walking pace plus 1.5 m/s. Not parented, it never
disappears with a deactivated buddy, and its save record never names the buddy.

**Reaching out.** `BuddyHands.ReachTo(point)` moves the held item to a point with its colliders **on**, so it
touches triggers like the player's held item does. `null` brings it back and turns them off.

**Putting down** restores colliders, `restrictGrab`, interpolation, wakes physics and calls
`SavePosition`. Item and buddy ignore each other for 1 s. While held, the player cannot grab it.

**Which room it belongs to.** Each frame the held item is re-owned to the buddy's current room
([a-carried-item-belongs-to-the-room-its-carrier-is-in](invariants.md#a-carried-item-belongs-to-the-room-its-carrier-is-in)),
because the game's own re-parenting skips `restrictGrab` items. An item already inactive was destroyed
(trash can, sale) and is left alone.

**Every ending puts the item down** in front of the buddy (`[ai] Put down '…' - why`): task end,
flee, order, rebuild, death, parking, despawn, and saving
([a-carried-item-is-put-down-before-a-save](invariants.md#a-carried-item-is-put-down-before-a-save)).
An item deactivated by something else is forgotten (`is gone from my hands`).

No arm animation yet.

---

## 3. Tidying

From the decider, as the `Tidy` urge; the amount of trash nearby raises its score
([behaviour.md §3](behaviour.md#3-the-decider)).

1. **Schedule.** `TidyIntervalMinutes` ± 25 % (at least 60 s). Only a round that ran schedules the
   full interval; anything else retries in 120 s.
2. **A round** clears `TidyRoundMin`–`TidyRoundMax` (2–5) pieces back to back within
   `TidyRoundSeconds` (180 s). Each piece is chosen fresh after the last went in. The round ends on
   the budget, the clock, no more trash, or any failure; flees and orders end it via `SetMode`.
   The chain point is right after an insert, when the hands are empty; it replaces `reachTask`
   directly. Never call `FinishRoute` mid-round - it would drop the held item.
3. **Candidates**, within `TidySearchRadius` flat, reachable height, on the buddy's own vessel
   ([behaviour.md §3](behaviour.md#range)), nearest three shuffled - trash (§1) not held:
   - **in a container** (found as snacks find them, [snacks.md §1](snacks.md#1-what-the-game-has)):
     one piece per container. Room built, not a hiding spot you are in;
   - **loose**: in no container zone and no `ItemDetector` zone.

   It needs a trash can within `TidyBinRadius` with a reach node. At most three plans; a failed item
   or container is skipped for 10 minutes.
4. **Leg one**, up close like a snack (stand points 0.55–0.85 m, reach 0.9 m). **Loose:** 0.5 s,
   `PickUp`; an item moved over 0.5 m, taken or gone ends the walk. **In a container:** open its
   doors, 1.5 s, check the item is still inside, `PickUp`, close the doors it opened at once
   ([a-snack-closes-what-it-opened](invariants.md#a-snack-closes-what-it-opened)).
5. **Leg two**, holding it, to the trash can's slot: a second `TidyTask` replaces the first directly
   (not via `FinishRoute`, which would put the item down). A failed plan puts it down and skips that
   can.
6. **Into the slot.** Face the can, 0.4 s, then the held item reaches to the slot centre
   (`ReachTo`). The can deactivating it is the success signal. Still active after 1.5 s: try
   once more, then give up and skip that can.

**Why the item is held in the slot, not tossed:** a released toss never reliably went in. Touching
the trigger while holding the item, as the player does, needs no release inside the can.

A flee ends tidying like a snack; the item is put down. `buddy_tidy` and the dialog's **Tidy** start
a round now, skipping schedule, setting and order.

---

## 4. Selling trash boxes

From the decider, as the `Sell` urge (need rises with the number of boxes). It looks every
`SellCheckInterval`; there is no interval setting.

**One press sells a load.** `SellStation.Sell` pays for everything in the zone at once, so a run
loads as many boxes as the zone takes and presses once, then loads again for the rest. `SellRun` holds
the station, the `Queue` to fetch, the `Loaded` list, its `Capacity` and the `Current` box; each leg is
a `SellTask`.

1. **Candidates.** A `Trash_Box` not in hands, within `SellSearchRadius` flat, reachable, on the
   buddy's vessel, not in a container or other machine. A box **already in a sell station** counts
   too - it just needs the button. "Already in" (`SellStationParts.HasBox`) means listed by the
   `ItemDetector` **or** inside the zone bounds: the detector's list is cleared by every sale, so a
   box can sit there unlisted. A loose box needs a station within `SellStationRadius`.
2. **The run** (`BuildSellRun`): the chosen box plus every other candidate for the same station.
   Boxes already inside go straight into `Loaded`. One load is capped by `SellMaxBoxes` (4;
   `SellMaxBoxesTransit` (2) at the Oxygen, Solar and Fuel stations, whose sales area is smaller) and
   by the room left in the zone (`SellZoneMaxItems`); the rest stay queued for the next load.
3. **Before setting off:** the station holds nothing sellable but trash boxes, and both the load
   point and the button have a reach node with a stand point **outside** the station.
4. **Box leg**, up close like tidying: 0.5 s, `PickUp`. Big items are held clear of the body.
   `PickUp` calls `Grabbable.AwakeNearItems` (as the game does when the player takes an item): resting
   items are kinematic, so whatever lay on the box would hover instead of falling. That call only covers the
   item's own detector zone, so `WakeStackedOn` also wakes, body included, everything lying on the box's
   old place and on that in turn (`AwakePhysics` + `Rigidbody.WakeUp`).
5. **Load leg:** stand points 1.35 / 1.6 m from the zone centre, reach 1.8 m, never within 0.35 m of
   the zone. Wait for the gate to be fully open (up to 20 s). The box reaches into the zone until the
   `ItemDetector` lists it (1.5 s max), is let go, and settles 1 s. Then fetch the next box
   (`TryNextBox`), or go to the button once the zone is full or the queue is empty. Every box
   `TryNextBox` leaves out is logged with the reason.
6. **Button leg:** terminal reach (stand points 0.8–1.4 m, reach 1.6 m), outside the station. Press
   only if ([the-buddy-sells-only-trash-boxes](invariants.md#the-buddy-sells-only-trash-boxes)):
   - at least one run box is still in the cage (`SellRun.InCage`);
   - nothing sellable but trash boxes is in the zone;
   - the gate is fully open, **you are not in the catch zone** and no other buddy stands in the zone
     (waits up to 20 s);
   - then `Button.Interact(pilot)` - your press, your money.
7. **Confirm:** every loaded box destroyed within 10 s → `Sold 3 trash boxes for N`. The gate open
   again 1.5 s after the press with boxes still there means something was under it; that station is
   skipped for `SellRetrySeconds`. Boxes still queued: the next load starts at once
   ([a-selling-run-keeps-its-boxes](invariants.md#a-selling-run-keeps-its-boxes)), wherever they
   lie from the station. Queue empty: `TryStart` looks for new boxes around the buddy, with no
   `SellCheckInterval` wait; none, or no way to them, ends the errand.

**Placement** (`LoadPlacement`). The Oxygen, Solar and Fuel zones are a 0.5 m cube and a trash box is
0.375 x 0.1875 x 0.25 m: one box per layer, two stacked. A box at an angle or across the edge is not sold,
so each box is turned upright and square to the zone (yaw 0, or 90 where that takes more boxes) and put in
the first free slot of a grid of box footprints - lowest layer first, then nearest the buddy - with 2 cm
clear of the walls and of its neighbours. No free slot: that box is left (`SellRetrySeconds`). The
Shipyard's 1.85 x 1.5 x 1.85 m zone uses the same grid.

**Letting go.** A box is released only when its centre is within 5 cm of the drop point. Before
pressing, each box is re-seated once at most (`SellRun.Reseated`):

- a box lying **across the zone edge** (the gate would land on it); still astray → the run ends,
  boxes stay loaded;
- a box the station **has not listed** (it would not be sold); still unlisted → it is left out and
  the rest are sold.

A gate that reopens logs the zone size and the offsets of the worst box and the buddy.

**Unloaded rooms.** The game switches a room's content off when the player walks on
(`EntryDetector`, `optimize`), with everything in it. The Shipyard's sell station is content of
`YardHallway`, so it goes off once the buddy carries a box away and nobody is left in the hallway.

- A box in such a room (`Items.InUnloadedRoom`: its own `activeSelf` still on) stays in the run. In
  reach, the buddy switches its room back on (`IErrandBody.LoadRoomOf`). A sold or trashed item clears
  its own flag and counts as gone.
- The run's station is loaded again whenever a leg finds it off (`StationBlocker`), and before a load
  or button leg is measured: a switched-off collider has no bounds. Undocked, its room cannot load, and
  the run ends.

**One box's failure is not the run's.** `AbandonBox` (a blocked leg, a failed `PickUp`, a box
that never seated, a reach that gave up) drops that box, then fetches the next or presses for what is
already loaded. Only a run with nothing loaded ends there.

**How long a station is skipped.** No nav node with a clear walk from outside → `SellSkipSeconds`
(10 min). Anything else (timeout, gate never opened, press didn't sell) → `SellRetrySeconds` (90 s).
`NearestSellStation` says which applies.

**Sighting the button.** The button leg's sight test ignores the **whole station**: the console
`BottomPart` has a `MeshCollider` between the eye and the button. Side effect: a stand point on the
far side could "see" the button through the cage, but `StandAllowed` and `WalkLos` still keep it to
genuinely walkable points outside the zone.

**Fences.** No task stands inside a sell station's fences, except that station's own sell legs
([a-fenced-sell-station-is-not-somewhere-to-stand](invariants.md#a-fenced-sell-station-is-not-somewhere-to-stand)).
The pen is the `Fence*` colliders plus the item zone plus `SellStationClearance`, re-measured every
`SellPenRefresh` (fences load with their room). Follow and Wander don't use reach tasks, so a buddy
inside a pen that can't get closer to a target outside it for 3 s is teleported clear by
`EmergencyUnstick` (`[ai] Penned in a sell station's fences … - climbing out`), never into another
pen. The fence colliders are never touched.

A flee or order ends the run: a box in hand is put down, loaded boxes stay. `buddy_sell` and the
dialog's **Sell** start a run now, skipping schedule, setting and order.

---

## 5. Idle play

From the decider, as the `Play` urge - the lightest weight, so it wins only when nothing else is on
offer.

1. **Schedule.** `ItemPlayIntervalMinutes` ± 25 % (at least 60 s); only a finished session schedules
   the full interval, anything else retries in 120 s.
2. **A session** is `PlayGamesMin`–`PlayGamesMax` (1–3) games in a row, each with a fresh item and
   game (`Done` starts the next leg in place of this one; `FinishRoute` would end the session).
3. **Candidates:** within `PlaySearchRadius` flat, reachable, on the buddy's vessel, nearest three
   shuffled, not held, **loose only** - never in a container or `ItemDetector` zone, and never within
   `PlayMachineClearance` (1 m) of one or of a trash can slot. Trash only, or any loose `Grabbable`
   with `ItemPlayAnything`. At most three plans; an unreachable item is skipped 10 minutes.
4. **A game**, drawn by weight (`PlayCarryNearWeight` 3, `PlayCarryFarWeight` 1, `PlayThrowWeight` 3).
   With no spot for a carry, it throws instead:
   - **Carry nearby** (`CarryNear`), 3–6 m. The spot: 0.6 m in front of an active node on the buddy's
     level, knee height clear to 0.9 m, floor within 0.1 m of the node's (not onto a step or table),
     not beside a machine, and **in sight of where the item lay** (so the same room).
   - **Carry to another room** (`CarryFar`), 8–25 m. The same test **without** the sight line, so the
     graph can route the item out of the room.
   - **Throw** across the room. Hold 0.5 s, turn to the throw direction, let go at 5–9 m/s tilted up
     1.6 m/s. The direction is the clearest of eight flat ones (a sphere of the item's size swept
     2.5 m; a machine along the way counts as blocked), **never within 35° of the player** within
     8 m. Then watch 2 s.

     **What it throws, it fetches.** A game has 1–3 throws. After each, the buddy walks to where the
     item landed (up to `PlayFetchRadius`, 12 m) and picks it up; after the last it puts it down
     there. An item it cannot get back to is left, with a line saying why.

   A carry's put-down is **lowered**, not dropped: 0.8 m/s to its place, released within 3 cm or
   after 2 s.
5. The walk to the item is tidying's leg one.

A flee, order or save puts a held item down (§2). Nothing is ever thrown at the Breathless or the
player. `buddy_play` and the dialog's **Play** start a session now.

---

## 6. Reading a capture

Tidying:

```
[mind] Decided: tidy up 'FoodCereal' - lying about, 3.4m away, via node (1.2, 0.0, 4.5), then to the trash can at (2.0, 0.6, 7.1)
[ai] Opened the fridge
[ai] Picked up 'Energy_Drink(Clone)' from the fridge
[ai] Closed the fridge
[ai] Put 'FoodCereal' into the trash can (1 of 3 this round)
[ai] Tidied up: 3 pieces of trash in the trash can
[ai] 'FoodCereal' did not go into the trash can (0.02m from the slot, slot trigger on, CanTrash True) - trying again
[ai] Put down 'FoodCereal' - stopped tidying
[ai] Leaving 'FoodCereal' - someone took it
[mind] Thought of tidying up, but there is no trash within 12m, lying about or in a container - trying again in 120s   <- level 2
```

HUD: `Route (tidying up 'FoodCereal')`, `Route (carrying 'FoodCereal' to the trash can)`,
`Tidy: in 280s (last: put 3 pieces of trash in the trash can)`.

Selling:

```
[mind] Decided: sell 3 trash boxes, lying about - 4.2m away, via node (…), at the sell station at (12.0, 0.8, -3.1)
[ai] Picked up the trash box (2 more to fetch)
[ai] Loaded the trash box into the sell station
[ai] Waiting at the sell station for you to step out of it
[ai] Pressed the sell station's button for 3 trash boxes
[ai] Sold 3 trash boxes for 1377
[ai] Sold 4 trash boxes for 1840 - 2 more of this run to fetch
[ai] Selling: another load - 2 trash boxes left, 4 at a time
[ai] Selling: leaving the trash box at (-0.2, 0.6, 1.3) out of this run - someone took it
[ai] A trash box lies 0.08m across the sell station's edge - loading it again
[ai] Selling: your 'Energy_Cell' is in the sell station too - the trash box stays loaded, you sell
[ai] Selling: the sell station opened again without selling - something is under its gate
[mind] Sell: not the sell station at (…): no nav node near the sell station has a clear walk to it from outside its gate - skipping it for 600s   <- level 2
```

If a station always fails with `no nav node … from outside its gate`, place a node outside by the
panel (F8).

Idle play:

```
[mind] Decided: play with 'Trash' - carry it to another room, 3.8m away, via node (…), to put it down at (…)
[mind] Decided: play with 'Trash' - throw it across the room, right here
[ai] Threw 'Trash' at 7.4m/s (2.5m clear ahead), throw 1 of 2
[ai] Playing: threw 'Trash' 2 time(s), then put it down (game 2 of 3)
[ai] Playing: carried 'Trash' 14.1m and put it down (game 3 of 3)
[mind] Play: no spot 3-6m from 'Trash' in the same room to put it down - throwing it instead   <- level 2
```

HUD: `Route (throwing 'Trash' about)`, `Play: in 280s (last: carried 'Trash' 4.1m and put it down - 3 games)`.

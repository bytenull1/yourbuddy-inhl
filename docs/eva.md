# EVA suits and going outside

`BuddySuit.cs` (the suit state, the spare rule, the watcher), `EvaRun.cs` (the trip through an
airlock and its legs), `SuitFetchErrand.cs` (fetching a forgotten suit home). The walking into
reach is NPC.Core's `NpcAgent.Reach.cs`; which side of the airlocks the buddy is on is NPC.Core's
too ([an-airlock-is-crossed-by-its-cycle](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#an-airlock-is-crossed-by-its-cycle)),
and the graph keeps an unsuited buddy inside
([node types](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/navigation.md#node-types)).
Config: `EvaSuit`, `SuitFetch` (General, default on). Commands: `buddy_order outside`, `buddy_order inside`, `buddy_order suit`,
`buddy_order fetchsuit`.

---

## 1. What the game has

- **The suit is an item** (`Suit : Equipment`, the `Space_Suit` prefab): `Isolated`,
  `MovementSpeedModifier` 0.4, `TemperatureResistance` 100. The player equips it by interacting;
  `EquipmentSystem.ApplyEquipment` only swaps the material on the body's helmet and chestplate
  renderers, sets `CachedSuitItem`, and switches the item **off**. `UnequipSuit` re-enables it at
  the player's feet.
- **Isolated means flat 2200/2200**: `Player.Temperature`/`Oxygen` return room temperature and
  22 % oxygen whatever the environment when the worn suit is isolated. Space is vacuum (0 %,
  −270 °C) and the FuelStation surface is no exception: `Airlock.Exit` sets whoever it cycles out
  to `SpaceEnvironment` unconditionally. The suit is the enabler for any walk outside.
- **The airlock cycle is player-driven.** `Airlock.StartTransition` runs from the panel inside the
  chamber; `Exit` and `Enter` abort unless a real `Player` stands in it (`PlayerDetector` only sees
  `Player` components, so a buddy in the chamber blocks nothing). The chamber is no room: walking
  through it changes no tracked room
  ([NPC.Core's game model](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/game-model.md#an-airlock-moves-only-the-player)).
  After `Enter` the surface's floor content (`FuelStation.asteroidRoom`) is switched off. A docking
  leaves the stuff airlock open inside and the refinery airlock open to the surface.
- **`Airlock.ExitGravity`** is the player's gravity outside (`Airlock.Exit` sets it): 0.5 at the
  FuelStation's two airlocks, a surface to walk; 0 at the ship's own, Oxygen's two, Solar's and the
  Shipyard's, where the player floats. The buddy walks the first and flies the second (§7).

## 2. The suit on the buddy

Wearing mirrors the game's own equip (`BuddySuit.Wear`/`TakeOff`):

- the spare suit item is switched **off** (as `Equipment.Equip` does), so it is invisible to item
  scans while worn and cannot be grabbed by anyone;
- the body is drawn with the skin embedded in the dll
  (`YourBuddy.Resources.PilotSuit_eva.png`, applied through `BuddySkin`'s material machinery), and
  the game's own equip sound plays;
- it is slowed by the suit's `MovementSpeedModifier` (Space_Suit 0.4) and never jumps, as
  `PlayerController` treats the player in an isolated suit (`BuddyAgentSettings.SpeedFactor`,
  `CanJump`). `SuitedPace` (1.3) lets it keep up with you sprinting in a suit (1.8 m/s);
- taking it off restores the look and lands the item at the buddy's feet, re-parented into the room
  it lands in, as `UnequipSuit` does - judged by the floor under it (`NpcAgent.ItemParentAt`), since
  the buddy's tracked room is stale aboard the ship. A suit filed under a room it is not in vanishes
  when that room is switched off.

Why the suit is on is kept (`BuddySuit.SuitReason`): **ordered** (the "outside" order, or
`buddy_order suit on`) or **survival** (the deadly-air urge). Only the survival suit comes off by itself.
Coming back in from outside by any way - your cycle, "inside", pulled aboard, a rescue - turns an
ordered suit into a survival one: the walk out is over. So only a suit you ordered on inside
(`buddy_order suit on`) waits for "unsuit", or death, or a despawn. The sidecar stores the worn item's
id (`SuitId`), why it is on (`SuitReason`) and the side of the airlocks (`Outside`), so a save
and load gives all three back - a survival suit loaded aboard in safe air comes off at the watcher's
next look. A sidecar without a reason restores survival.

## 3. The spare rule

[the-buddy-never-takes-your-last-suit](invariants.md#the-buddy-never-takes-your-last-suit):
a candidate is an enabled, ungrabbed, **isolated** `Suit` (a Pilot_Suit would not protect in
vacuum), lying free or displayed in a locker - a displayed suit is released through its
`EquipmentHolder.TryDropItem`, the game's own way. Never a locker on a station you are not docked
at (`Items.Loadable`). While you wear a suit, one free suit is a spare;
while you do not, two must be free - **unless you ordered the run**, where your order is the
consent. The rule is checked when a suit is chosen and again at the suit itself.

## 4. Through the airlock

The buddy is inside or outside, and only your cycle moves it across. A shut airlock door is never
planned through, and a wander picks nodes on the buddy's own side. Both orders run an `EvaRun`,
one crossing each:

| | "outside", "eva", "space walk" (`buddy_order outside`) | "inside", "come in", "back in" (`buddy_order inside`) |
|---|---|---|
| airlock | the one you stand in, else the nearest the buddy can walk to: a station's, or the ship's own while undocked | the one you stand in; floating, the nearest chamber; else as going out |
| first | a spare suit, unless already suited | - |
| door into the chamber | the inner door | the outer door |
| waits for | the outer door to open: you cycled out | the inner door to open: you cycled in |
| refused when | already outside; the ship is moving (its airlock is locked) | already inside |

The walkable test matters at the FuelStation: its refinery is reached from the rest of the station
only across the surface, so from the refinery the only airlock the buddy can walk to is the
refinery's own. An airlock counts as walkable when a route toward it ends within 6 m of it.

1. **Suit leg** (outside only) - walk to the nearest spare and wear it.
2. **Chamber leg** - walk onto the chamber's stand point and stand there, clear of both doors: the
   graph node you placed inside the chamber (inside its `PlayerDetector` volume, nearest its centre),
   else the volume's centre. The airlock's own transform can sit by a door, so it is never the
   measure. It walks the graph's route toward the stand point, replanning from each arrival while
   more than 6 m off; within 6 m it walks straight on and never gives up. Waiting is the leg's
   work, so the agent's idle recovery leaves it be (`ErrandLeg.Waits`). While the door
   into the chamber is shut it waits beside it: only your cycle opens it. The buddy never opens an
   airlock door. Floating, it flies to the stand point instead (§7).
3. **Waiting** - in the chamber, with a throttled log, until your cycle opens the far door.
4. **Crossing leg** - straight through the open far door, a couple of metres clear, never planned
   on the graph; floating, it flies out at its own height. The buddy is then on the new side, and
   the run ends: the mode in force takes over.
   If the far door shuts first, the run goes back to the stand point and waits for the next cycle.
   The run only ends by crossing, by another order, or far from the airlock with no route to it -
   never by falling back to Follow while it waits.

Following needs no order. A suited buddy follows you into the chamber while its door is open, and
the cycle moves whoever stands in the chamber - NPC.Core moves the buddy with you, and gives it the
airlock's gravity. Outside,
the decider only weighs Follow and Wander, and switches to Follow the moment you step into an
airlock's chamber. A buddy left on the surface when you cycle in loses its floor with the surface
content, and NPC.Core's fall rescue (25 m below the player) teleports it to its last safe inside spot.

Orders that stay on the buddy's side: a goto to a node on the other side is refused ("tell it to
go outside first" / "come inside first"), a room goto is refused outside, and every job order
(tidy, sell, snack, play, hide, terminal) waits until it is back in.

## 5. Suiting up for deadly air

From the decider, as the `Suit` urge ([behaviour.md §3](behaviour.md#3-the-decider)), and even
under an order ([survival-outranks-an-order](invariants.md#survival-outranks-an-order)): the air
aboard is dangerous, no terminal can be tried right now (the terminal urge outranks it 1.0 to 0.9
and wins the first round), and a spare suit is free. Once the air is killing the buddy
(`NpcAgent.LifeInDanger`), the suit outranks the terminal (1.2) and, checked every second, it drops
whatever it is doing - the walk to a terminal included - to put a spare on (`TrySaveOwnLife`, log
`[suit] Buddy is suffocating - suiting up now`). The buddy walks to the suit, wears it, and
shelters aboard - it keeps trying the terminal while suited - and takes the suit off when the
watcher sees it breathing safe air again, aboard or on a station, out of any airlock chamber
(`BuddySuit.Update`, the terminal's danger bands on `NpcAgent.Air`). Only this survival suit
comes off by itself; an ordered one waits for "unsuit" ([§2](#2-the-suit-on-the-buddy)).

"Unsuit" / "take the suit off" ([dialog.md](dialog.md)), or `buddy_order suit off`, takes the suit off
wherever there is air, whatever its quality. Outside, or standing in an airlock chamber that the
next cycle may open to space, the buddy refuses and keeps the suit on.

The watcher also takes the suit off when the worn item vanished (nothing behind the skin) - not
outside or in a chamber, where it stays suited until it is back in the air - when the buddy dies,
and when it is despawned. If the body's EVA skin is lost to something rewriting its materials, the
watcher puts it back and logs `EVA skin was lost` - that line in a capture names the culprit.

## 6. Bringing a forgotten suit home

The `SuitFetch` urge, scheduled like tidying and weighed only while the buddy decides for itself and
both `EvaSuit` and `SuitFetch` are on (`buddy_order fetchsuit` starts one now, whatever the settings):
an isolated suit left on the docked station is carried back aboard and set down 1.5 m inside the
ship's inner airlock door - not in the chamber, whose next cycle would carry it out with you, and not
at the airlock's own transform, which no node can see (the walk home was refused: `no way back to the
ship`). It looks only while the buddy is aboard. A suit counts as left on the station by the room
the game filed it under, else the floor under it - station rooms whose content is off while you are
aboard included; the buddy loads the room when it gets there. A suit on a station you are not docked
at is never a target: all four station interiors lie in the same place behind the ship, so it would
lie inside the docked one, and its room cannot load (`Items.Loadable`). A suit it gave up on is left
out for `FetchSkipSeconds`, so the next try takes another. It stops once two isolated suits are
already yours or aboard (`EnoughSuitsAtHome`): the one you wear or carry (hands, belt, backpack),
any lying aboard or shown in a locker aboard, and any a buddy aboard wears. It fetches while you
are on the station too. Skipped while you hold the suit or stand within
2 m of it. A suit outside stays there - the buddy does not cross an airlock on its own.

Once the fetch is due, the HUD's `Fetch suit:` line says what the last look saw instead of a timer
at 0 s (`due - 4 isolated suit(s): 1 to fetch, 1 already aboard, 2 on a station you are not docked at`); at level 2 the
same line is logged as `[suit] Buddy suit fetch: ...` whenever it changes. A manual Fetch suit
request with no eligible target reports this scan reason too, including when the buddy is not aboard.

## 7. Floating

Out of a zero-g airlock - the ship's own while undocked (the wreck, or wherever the ship stopped),
Oxygen's, Solar's or the Shipyard's - the buddy floats, as you do. How it flies and finds its way is
NPC.Core's ([agent.md §8](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/agent.md#8-floating)):
with inertia like yours, straight at you when it can see you, else along the path you took. There are
no nodes out there.

- **Going out** is the same run (§4): suit, chamber, your cycle. The cycle takes it out with you, and
  it floats out through the door and follows you.
- **Following**, it keeps 2.2 to 3 m from you. When you float into a chamber whose outer door is
  open, it follows you all the way in to the stand point and holds still there: short of it, your
  cycle would leave it outside, or shut the door on it (a door that hits something fails to close).
- **Coming in**: "inside" flies it into the nearest airlock and waits for your cycle. Once it is in and
  the air is safe, it takes the suit off by itself (§2). Floating into the chamber yourself does the same without an order.
- **Only Follow, Stay and Inside.** The decider only follows. Wander and goto are refused (`... has
  nothing to walk on out here`), no flee starts, and Stay hovers where it is. The HUD shows
  `[OUTSIDE, ZERO-G]`.
- **Left outside**, it hovers where it is until you come out for it. Once the ship moves - you undock
  or fly off - NPC.Core moves it into the ship's airlock.
- **Stuck or lost**: getting nowhere for 4 s, or 40 m behind you, it is moved onto your path behind
  you, with a warning in the log.
- A save made while it floats loads it floating (`BuddyState.Gravity`).

## 8. Known limitations

- The surface is walkable only where the graph has Outdoor nodes; without them the buddy stands at
  the exit pad.
- Floating, the buddy does no jobs and carries nothing.
- It finds its way outside only along your path: to an airlock your path does not pass, it flies
  straight and slides along whatever is in the way.
- A buddy left outside when the ship undocks parks with the station, like any NPC.
- The buddy does not model suit charge.

A failed route to a suit excludes that suit from autonomous selection for 30 seconds, so
another free suit can be considered. Explicit suit orders may retry immediately; ownership
and the rule reserving the player’s only suit still apply.

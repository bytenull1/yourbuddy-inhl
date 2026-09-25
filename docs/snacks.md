# Snacks

`SnackErrand.cs`, walking into reach on NPC.Core's `NpcAgent.Reach.cs`. Config (General): `Snacks`
(default on), `SnackIntervalMinutes` (13 - the player's own satiety drains from a full stomach to the
game's Hunger threshold in about that long,
[NPC.Core's game-model.md](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/game-model.md#atmosphere-kills-by-the-players-rule)). Console: `buddy_snack` (one
now), `buddy_mind` (the timer). Dialog: "snack" / "eat" / "food" / "hungry"
([dialog.md §3](dialog.md#3-the-orders)).

---

## 1. What the game has

Every container is a `Door : Powerable` on a piece of furniture:

| Container | Door(s) | Furniture child with `itemMover` | Also |
|---|---|---|---|
| `Fridge` | `FridgeDoor` (its `Interactable` is the child `Door`) | `Fridge` | `Freezer`, `Lights` |
| `Cabinet`, `Closet` | two `GameObject/CabinetDoor` | `Cabinet` (`Closet`) | root is a `HidingSpot` |
| `Chest` | `ChestHood` | `FurnitureLayer` | - |
| `Locker` | `DoorAnchor/Door` | `Locker` | root is a `HidingSpot`; `EquipmentHolder` |

Aboard the ship: kitchen `Core_R10E` (fridge, cabinet), `Back_L08` (two closets), `Core_L07` /
`Back_R05` (chests), `Core_M01` (locker). Loose food: `Core_R04`, `Front_M00`, `Front_R03`, and the
stations' kitchens.

**The player's path is `Door.Switch`**, which calls `Open` / `Close`. The Breathless uses it too.
`OnOpen` / `OnClose` drive `HidingSpot.SetHidden` on closets and lockers, and the light and
`Freezer` on the fridge - a fridge left open stops freezing.

**"Inside" is a physics zone.** Food is a loose `Grabbable` (`Food`, `Drink : Food`) under the room
content. `Furniture.itemMover` is an `InstantItemDetector` whose zone is what the container holds.
Food in no container's zone is **loose**. Vending machines spawn ordinary loose `Food`.

**Eating needs no eater.** `Food.Consume()` plays the sound, sets `usages` to 0 and turns the item to
trash, with no `Player` needed.

---

## 2. What the buddy does

From the decider, as the `Snack` urge ([behaviour.md §3](behaviour.md#3-the-decider)).

1. **Schedule.** `SnackIntervalMinutes` ± 25 % (at least 60 s); nothing straight after a spawn or
   load. Only **eating** schedules the full interval; anything else retries in 120 s.
2. **Never when the player is hungry** (satiety ≤ 250, the game's Hunger band). Checked again right
   before eating.
3. **Candidates - food only**, within `SnackSearchRadius` (25 m) flat, at a reachable height, on the
   buddy's own vessel ([behaviour.md §3](behaviour.md#range)); the nearest three shuffled:
   - a container **holding edible food** - room built, not a hiding spot the player is in. Empty
     containers are never opened;
   - **loose food** - not used up, not in someone's hands, in no container's zone.

   A candidate it could not plan to or reach is skipped for 10 minutes.
4. **The walk** is the terminals' ([terminals.md §2](terminals.md#2-what-the-buddy-does)) but **up
   close**: stand points 0.55 / 0.7 / 0.85 m, reach 0.9 m. At most three plans.
5. **At a container:** open its closed doors (remembered), look 1.5 s, eat or drink one random item
   with `Consume`, wait 2 s, close the doors it opened. A door found open is left open.
   **Loose food:** 0.8 s, `Consume`, 2 s. Food moved over 0.5 m, picked up or used up on the way
   ends the walk.

**How it ends.** The task belongs to the Route. Unlike a terminal, **a flee ends it** - a buddy that
ran from the Breathless does not come back for a snack. However it ends, `DropReachTask` closes the
doors it opened, unless the player has climbed into that hiding spot since
([a-snack-closes-what-it-opened](invariants.md#a-snack-closes-what-it-opened)). A buddy that dies
mid-snack leaves them open.

`buddy_snack` skips the schedule, the `Snacks` setting and any order in force (the Route then returns
to that order). It refuses while asleep, scared, busy or on a goto, and when the player is hungry.

---

## 3. What it deliberately does not do

- carry food elsewhere; the empty wrapper stays put (tidying may bin it later - [items.md](items.md));
- eat food in someone's hands, or used-up or spoiled food;
- open anything the player hides in, or shut them in;
- gain anything: nothing on the buddy reads what it ate.

---

## 4. Reading a capture

```
[mind] Decided: get a snack from the fridge - 3 thing(s) to eat in it, via node (1.2, 0.0, 4.5), then (1.8, 0.0, 5.3)
[mind] Decided: get a snack from the FoodCereal - lying about, via node …
[ai] Opened the fridge
[ai] Drank 'DrinkEnergy (2)' from the fridge (2 left)
[ai] Closed the fridge
[ai] Leaving the cabinet alone - you are hiding in it
[ai] Leaving the FoodPack alone - it has been moved
[mind] Fancied a snack, but you are hungry (satiety 212) - the food is yours - trying again in 120s   <- level 2
[mind] Fancied a snack, but there is no food within 25m (2 empty container(s) left shut) - trying again in 120s   <- level 2
[ai] Could not get within reach of the fridge (1.3m) - giving up, retrying in 60s
```

HUD: `Mode: Route (getting a snack from the fridge)` and
`Snack: in 1180s (last: drank 'DrinkEnergy (2)' from the fridge (2 left))`. A dead buddy's HUD
omits the Snack, Fear, Mind and Air lines.

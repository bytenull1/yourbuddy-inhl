# Life-support terminals

`LifeSupport.cs`; the walk into reach is `BuddyBehaviour.Reach.cs`, shared with
[snacks](snacks.md) and [items](items.md). Config: `Terminals` (General, default on).
Test command: `buddy_terminal <oxygen|climate>`.

---

## 1. What the game has

Two units aboard the player ship, both `Controller : Powerable`, reached through
`SpaceShip.OxygenController` and `SpaceShip.ClimatController` (sic):

| Unit | Scene path | Power switch |
|---|---|---|
| `OxygenController` | `SpaceShip/Rooms/Core_L13E/Core_L13EContent/OxygenController` | child `Switch` |
| `ClimateController` | `SpaceShip/Rooms/Front_R03/Front_R03Content/ClimatController` | child `Switch` (`Switch CF`, the °C/°F toggle, is one level deeper) |

A unit regulates while `Enabled`. The power `Switch`'s UnityEvents are wired to the controller in the
scene.

`OxygenError` / `ClimateError` turn the unit off with its switch left **on**, and want a button
sequence. `Emission` breaks both (`Breakable.Break`). `ShipController` raises alerts on
`!Enabled || Data.broken`.

---

## 2. What the buddy does

From the decider, as the `Terminal` urge ([behaviour.md §3](behaviour.md#3-the-decider)):

1. only aboard the player ship, reading `SpaceShip.Environment`;
2. **danger** uses the player's own debuff bands: oxygen below 19.5 %, temperature below 14 °C or
   above 32 °C as felt in a PilotSuit (room + 1 °C). Oxygen is checked first;
3. the unit must pass `TerminalBlocker` - present, room built, not broken, powered, not running,
   switch found and **off** ([a-terminal-is-only-switched-on](invariants.md#a-terminal-is-only-switched-on));
4. `TryFindReachNode` picks the nearest active node within 8 m of the switch that has a clear walk
   (`WalkLos`) to a stand point 0.8 / 1.1 / 1.4 m in front of it, from which the switch is visible
   (`CanSee`);
5. the buddy walks a **Route** to that node, then straight to the stand point and the switch, until
   within 1.6 m, 0–2 m below it and in sight;
6. `Switch.SwitchState()` - the player's own path, so sound, handle and wiring all run. 1.5 s later
   it logs whether the unit is running.

If the Route ends more than 1.2 m from the node (stuck recovery can skip waypoints), it plans again,
at most twice.

**Why the route ends at a node.** Planning straight to a point by the machine gave an *approach*
plan ending in the next room, and the final straight walk went through the wall. The task does not
replan, so its last stretch must be probed before it is chosen; everything before it comes from the
graph.

**Retries.** A flip, or an 8 s approach that never gets in reach: 60 s per unit. A failed plan: 5 s
(often transient, e.g. perched on furniture).

**Life of the task.** `reachTask` belongs to the Route. `FinishRoute`, `SetMode` to anything but
`Flee`, and `ApplyRouteOrder` all drop it (`DropReachTask`), so a flee resumes it through `EndFlee`'s
replan and any order ends it. The HUD shows `Mode: Route (switching on the …)`.

**It is never a random draw.** Life support scores 1.0 whenever `LifeSupport.AirIsDangerous`, which is above
`UrgeDecisive`, so it is taken outright ([behaviour.md §3](behaviour.md#choosing)). If no unit can
be switched on, the next-best urge gets the round.

**Only from the decider.** An order holds ([an-order-is-not-a-mode](invariants.md#an-order-is-not-a-mode)):
a buddy told to follow won't leave you to fix the air. Fear and spacewalks stand the decider down too.

`buddy_terminal oxygen|climate` starts the walk now, ignoring the danger bands, retry timer and any
order. The blocker still applies. The HUD's `Air:` line shows the air and each unit's retry timer.

---

## 3. What it deliberately does not do

- switch anything **off**, or cycle a switch that is on (that would skip the error puzzle);
- press the target up/down buttons;
- repair, restore power, or act on a station.

---

## 4. Reading a capture

```
[mind] Decided: switch on the climate control - temperature at 33.0C, via node (-2.7, 1.0, -3.9), then (-3.2, 0.1, -5.6)
[ai] Switched on the oxygen generator ('OxygenController')
[ai] The oxygen generator is running
[mind] Not switching on the climate control: temperature at 11.0C, but it is broken and needs a repair
[mind] Wanted to switch on the climate control (temperature at 33,0C), but there is no path to it - retrying in 5s
[ai] Route to the climate control ended 3.1m short of node (-2.7, 1.0, -3.9) - planning again (1 of 2)
[ai] Could not get within reach of the climate control (2.4m) - giving up, retrying in 60s
[ai] Flipped the oxygen generator's switch, but it is still off (switch=True, powered=True, broken=False)
```

Blocker lines are throttled to one per unit per 30 s.

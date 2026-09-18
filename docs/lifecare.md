# The lifecare scanner terminal

`BuddyManager.cs` (integration) and three Harmony patches in `Patches.cs`.

---

## 1. What the game does

`LifecareController.StartScan` shows a ~3 s progress bar, then `EndScan` takes a **snapshot**: it
sets two booleans straight into two existing `Image`s under `LifeformIcons`. No icons are created;
"showing" one is `Image.enabled = true`.

There is no registry, layer, tag or room query. A lifeform is exactly:

1. `playerIcon.enabled` - the player;
2. `breathlessIcon.enabled` - the monster, or a `Warn(target)` ping;
3. any active entry in `tempObjects` - scripted one-off blips.

The count is `(breathless || any temp ? 1 : 0) + (player ? 1 : 0)`, so vanilla never shows more than
2. Horror events use the same three channels.

Icon position is `-(x·scale, z·scale, 0)`. The player icon uses a ship-local point, the breathless
icon a raw world position (a game inconsistency).

---

## 2. What the mod adds

- `TryHookLifecare` clones the breathless icon as `BuddyLifeIcon`, and re-creates it if destroyed,
  keeping the last snapshot.
- `UpdateBuddyLifeIcon` polls at 10 Hz. When a scan ends it snapshots the buddy's ship-local
  position, like `EndScan`, then re-asserts the icon's state, position and count on every poll.
- `RecountLifeforms` (postfix on `UpdateLifeformsCount`) shows vanilla's count plus the buddy
  ([mirror-the-vanilla-lifeform-clamp](invariants.md#mirror-the-vanilla-lifeform-clamp)), only on
  the display its clone lives in.

"Is the buddy aboard" comes from the floor, never a room
([aboard-is-answered-by-the-floor](invariants.md#aboard-is-answered-by-the-floor)). The mod writes
only its clone and the label; it never touches `playerIcon`, `breathlessIcon` or `tempObjects`.

A buddy that has never crossed a doorway takes the nearest `EntryDetector` at any range, once, so it
has a room label from the start.

---

## 3. The missing player icon (game bug, worked around)

`LifecareController.EndScan` decides whether the player counts:

```csharp
CustomRoom[] rooms = base.Owner.Rooms;
for (int i = 0; i < rooms.Length; i++)
{
    _ = rooms[i];                                                  // iterator discarded
    if (base.Owner.Pilot.CurrentRoom == base.Owner.Rooms.First())  // always rooms[0]
        flag = true;
}
```

A typo for `rooms[i]`: **the player registers only while their tracked room is room 0**
(`Front_M00`). Two things move `CurrentRoom` off room 0:

1. **Docking** - the game skips the room transition
   ([game-model](game-model.md#docking-does-not-run-the-room-transition)).
2. **The buddy closing a door elsewhere** - `EntryDetector` re-files the player
   ([game-model](game-model.md#entrydetector-re-files-the-player-when-a-door-closes)).

Fixes:

- `Patches.EntryDetector_DoorCheckForEnter_Prefix` / `_Postfix` hide the detector's remembered player
  during the call - only for a gate the buddy just closed, and only when the player is beyond
  `PlayerAtDoorwayRadius`. Item re-parenting still runs.
- `Patches.LifecareDisplay_UpdatePlayerIcon_Prefix` restores the icon
  ([player-icon-fix-is-one-directional](invariants.md#player-icon-fix-is-one-directional)).

### Reading a capture

```
[mgr] Lifecare scan finished: buddy aboard=True, pos=(2.5, 0.6, -1.8),
      trackedRoom=Front_M00 -> icon shown
[mgr] Lifecare icon state: enabled=True, activeInHierarchy=True, parent=LifeformIcons,
      localPos=(-3.7, 3.7, 0.0), playerIcon=False, label=1
```

The second line is what is actually **rendered** - read it first. `aboard=False` with the buddy
plainly aboard is a floor-probe problem; `aboard=True` with no icon is a clone problem.
`playerIcon=False` while the mod is correct points at the game bug above.

---

## 4. Known limitations

- The terminal shows the snapshot from the last scan, like the game's own icons. Re-scan to refresh.
- A terminal powered **off** (`Emission`, a blackout) is not detected; the mod keeps writing into a
  hidden UI. Harmless.

# Storing loose items

Choose **Store items** on the flat Orders page, or use `buddy_order store`, to keep putting away
loose items and sorting misplaced supplies aboard until another order is given. Physical items must fit: these are
not inventory slots.

With `General.Storing` enabled (default on), storing also participates in the normal decider.
Choose **Decide** to release a standing order. Buying permissions and resource levels do not
control storing. A completed job waits three seconds; exhausted placement searches defer the item for 30 seconds,
or until an item aboard moves over 5 cm, rotates over 5 degrees, is added, removed or reparented.
Pickup, carry and settling failures retain the 120-second item cooldown. Route-planning deferrals retain their 60-second backoff. Other errands and safety priorities still compete normally.

## Selection and placement

- Only usable food, non-empty resource cells, seed packs with seeds, and stocked aid/repair kits
  within 30 metres aboard are considered. Unknown objects, ore, mission/story items and empty
  supplies are left alone. Eligibility is checked again before pickup.
  Enabled storage rooms are loaded to inspect contents. Neatly aligned stored items, machine contents,
  held items, suits, rubbish and trash boxes are left to the player or their existing errands.
- A category's best built container type is its normal home: food prefers fridge then cabinet
  then chest; resource cells prefer chest then cabinet then locker; other supplies prefer cabinet
  then chest then locker. Non-food never uses a fridge. A smaller ship uses the best type it has.
  The preferred home stays stable. Loose items try lower-ranked suitable containers before
  overflow; stored items can move to a better-ranked home or be aligned within the same container.
- Enabled rooms supply their actual furniture and detector dimensions, so upgrades and
  moved furniture change the available storage. A ship without suitable furniture can use its checked overflow area. Arbitrary floor drops are not successful storage.
- Among normal homes, containers holding the same category are preferred: food together,
  resource cells by type, and other supplies by signature. Empty homes precede mixed ones;
  distance breaks ties. Equal-quality homes do not trigger rearrangement.
- Misplaced contents are extracted only after a destination is found. The source and destination
  are claimed together. Buddy opens the source, checks a reverse insertion path, carries the item
  out, and closes only doors it opened. Occupied hiding spots are not disturbed.
- Items try six orientations; large or awkwardly shaped items may not fit. Columns adapt
  to the item footprint, up to five in each horizontal direction and four height slices. At most four
  candidates are probed per frame across storing buddies. Support must be actual furniture,
  the full footprint must fit, and occupancy buffers must not overflow.
- Buddy reserves the item and container, checks the approach before pickup, opens the doors,
  and checks the placement again. Chests use a lift-over-the-rim motion. A blocked insertion
  path moves on to another container while the item is still in hand. A changed slot before
  pickup resumes searching. Each search keeps its cursor and the original job deadline.
- The item is released only after reaching the placement point, parented to the destination
  room and saved. After a settling delay, its entire bounds must remain inside the destination
  and it must be sleeping or moving slowly before success is logged.
- If every candidate fails before pickup, the item stays untouched. If alternatives run out
  during a carry, cleanup tries supported clear floor nearby, never counting that as storage.
  Without a safe floor destination, Buddy releases the item at its current position with physics restored. Cleanup closes only doors
  Buddy opened. An insertion already partway through furniture stops rather than rerouting.

The geometry rules are in [the storing invariant](invariants.md#storing-checks-space-before-pickup).
`[store]` lines report selection, completed storage and blocked jobs. The debug HUD's Store
line shows the last result. Open shelving without a supported furniture detector is not a
destination; resource restocking retains its [designated floor areas](resources.md#scans-and-storage).

## Floor overflow

If suitable containers have no usable space, Buddy tries short side strips. Original centres are 0.65 m apart:

| Priority | Enabled room | Original centres |
| --- | --- | --- |
| First | Fuel room (`Front_L06`), outer side | 3 |
| Second | `Core_R04`, outer side | 2 |
| Last | Cockpit (`Front_M00`), side of the aisle | 2 |

The fuel room is preferred even when another fallback is nearer. Disabled rooms are excluded;
room-local positions follow upgrades. Approach positions sit alongside the rows. Each row
keeps its existing 0.6 m width. After the original centres, probes try midpoints and two
parallel lanes offset by 0.15 m. This gives 9 or 15 candidates, not guaranteed item capacity.
Every footprint must remain inside the strip; a blocked strip falls back to the next enabled room.
The old central grids remain recognisable for collecting existing supplies, but receive no
new placements. Already staged items stay there until suitable normal storage is available.
Each slot needs level support under the whole footprint, clear space,
doorway clearance, and a reachable approach. Large items may occupy several slots' space;
items are never stacked to manufacture capacity.

An item counts as overflow only near a designated row slot or a legacy grid centre. Items already there are considered only
for promotion to a normal home, never another overflow slot. This is reconstructed from the
room and item positions after loading a save. When normal storage remains full, those items
stay put. An item in a suitable fallback container stays there until a better home is usable.
Outside a bounded repacking sequence, only loose items or items in an incompatible container may move to overflow; if all
destinations fail, nothing is taken out. A full overflow area never expands into arbitrary floor space.

If source furniture moves during approach, Buddy leaves the item and checks again later.

## Access and movement

Hand movements advance on arrival, with a four-second timeout per segment. A blocked
insertion waits up to three seconds, checking four times per second, before trying another
slot. At most three failed insertions are tried per container within the original job deadline.
A straight swept path is tried if the usual staged insertion is blocked. Both paths must pass
full item-clearance checks. During extraction and insertion, each remaining hand segment is
checked again; temporary obstructions pause the hand at its current position. Persistent
obstructions end the carry safely. Player and furniture collisions are never ignored.

Shelf probes pack from one side with a 2 cm gap between padded footprints and wall clearance,
retaining centre positions as fallback. Nearby eligible items are considered first; failed items are skipped so inaccessible stored
contents do not monopolise the order. Support probes ignore only the item being rearranged, so its current
position does not hide the shelf beneath it. Other items remain obstructions.
Storage logs include measured item size and the collider blocking an insertion sweep.

Storage carries initially keep the measured world orientation using NPC.Core 1.0.3 `TurnWorld`,
so carrier turns do not invalidate the planned footprint. Six axis-aligned orientations are considered when planning a destination. Rotation happens
outside storage only after checking a clear volume around the item.

## Rearranging a container

A useful item that is tilted, protruding, or off its size-based packing rows can be extracted
and replaced within the same container. A supported, clear destination is required first.
Known useful items in the wrong category still prefer a better home. Mission/story and
unrecognised items are not moved to make room. Recognised rubbish belongs to the tidy job.

Fit is calculated from collider bounds in each candidate orientation, shelf support, and
current occupied space. Item count and container volume alone do not establish capacity.
Search is limited to six orientations, at most 5-by-5 horizontal probes and four vertical
slices per orientation, with four candidate probes per frame shared by storage jobs.
This is a bounded packing search, not a guarantee of the mathematical maximum capacity.

## Opening geometry

Approach positions use the door collider centre transformed back to its closed pose, so
starting with an open door does not move the target to its swung-out leaf. Buddy waits for
the opening animation to finish. Side-opening furniture aligns with the slot outside the
opening, beyond the open door leaves, then moves straight in along that lane; chests retain their over-the-rim path.
Every segment is swept using the planned item footprint, including extraction in reverse.
A clear slot alone is not a clear insertion route.

After release, container placement allows 5 cm on each horizontal axis, matching tidiness
recognition, and 4 cm vertically for release clearance to settle. Overflow allows 8 cm per axis, matching horizontal grid recognition and allowing the
release clearance to settle onto the floor. Both require at most 8 degrees of rotation change. Drift or tipping is reported as a failed placement.

## Retry and cancellation ownership

A placement retry clears any world-space reach target before the next walking leg. The
item must follow the carrier again; an old rotation hold is not a new carrying state.
Doors Buddy opens remain open across retries and consecutive nearby storage jobs. They close
when work runs out, is interrupted, or Buddy moves more than 2.5 m away; an eight-second idle
timeout covers a pause between jobs. Active insertion/extraction retains its door until safe cleanup.
Doors already open before Buddy uses them remain open. This avoids opening a door while its own
retry-triggered close animation is still running. Player-opened doors are not closed.

Ending a job uses the item's current rotated footprint for a checked floor drop. If no
floor destination passes, it releases at the current position and restores physics. A
failed or cancelled job must not retain a frozen, detached item. This is not storage success.

## Continuing order

Store items stays active between jobs, even with Autonomy off. It waits when nothing is
eligible and rechecks after three seconds; failed placements retain the item cooldown without delaying other items for a full minute. It does not bypass skips as repeated one-shot commands do. Follow, Stay, Decide,
a goto or another task cancels the continuing order and safely ends its active storage leg.
Status queries and resource-menu browsing do not cancel it.

Danger, fear, dialogue and another active route defer the next job. Survival can interrupt
storage, which resumes when safe. This command does not enable Autonomy or persist across
save/load. While waiting, Buddy stays put. Ordinary autonomous storing still competes with
other errands when no continuing order is active.

Tidiness recognises the same centre fallback positions as placement, allowing up to 5 cm
of settling drift on each packing axis. A valid fallback should not cause repeated sorting.

World interruptions end the active storage leg and close its owned doors. The continuing
order remains available to resume when the ship and Buddy are active again. Status replies
identify the continuing order while it waits between jobs.

Waiting states are reported in status replies and throttled `[store]` logs. Failed settlement
logs include the measured position drift, rotation difference and settled check.

Rows fill from the back of each container toward its opening, across each row first. Door
orientation selects the depth axis and direction; tidiness uses that same grid. Preferred
container types still rank ahead of matching-category or nearby fallback containers.

Storage hand movements run at 0.75 m/s. Door readiness releases the opening wait as soon
as the animation finishes, including doors already open on arrival. A door still opening
retains its bounded wait. Walking speed, collision sweeps and the settling check are unchanged.

## Making room

If a loose item or a supply being moved from another storage area cannot
use its preferred container, and that container has eligible untidy, misplaced or path-blocking contents, Buddy can stage the incoming item in checked overflow first.
An incoming item already in overflow stays there while space is made.
It then evacuates up to three useful items into separately checked overflow positions,
returns those supplies to suitable normal storage, and retries the incoming item last.
Each move uses the ordinary reservation, extraction, placement and settlement checks.

The sequence does not take unknown/protected items or bypass occupancy. If overflow has no
room, extraction fails, the player moves a planned item, the target is unavailable or another
order cancels the work, repacking stops. Already staged supplies remain in overflow for later
normal storage; Buddy does not force them back into a blocked cupboard.

A sequence expires after five minutes and cannot recursively start another one. The target
has a five-minute repacking cooldown. One buddy claims it during each active move and
rechecks availability between moves. Progress is session-local, not saved as a queue.
This bounded approach can make room; it does not promise a complete or optimal repack.

A useful item identified by an insertion sweep as blocking the opening may be staged even
when it is aligned. Furniture itself and protected objects never qualify as removable blockers.

Successfully stored, still-neat supplies wait 120 seconds before another home promotion.
Loose or disturbed items and the next move in a repacking sequence remain eligible. This
prevents an immediate return trip while other supplies need attention; it is not a guarantee
that a previously blocked preferred home has become usable.

Hand waypoints require arrival within 1 cm before the next segment. Moving on while still
above a shelf-height waypoint can cut diagonally into the shelf or frame. Movement speed
and collision checks remain the same.

Loose-supply pickup uses a 1.35 m reach and tries stand-off distances of 1.3 m and 1.15 m
before the closer alternatives. This avoids requiring the buddy to press against nearby
furniture for an otherwise visible item. Line-of-sight, route, item ownership and height
checks still apply. Furniture extraction and delivery keep their original reach.

When ordered from the station currently docked to the player ship, Buddy first walks to the
ship cockpit, then starts the continuing storage order. Only supplies aboard the player ship
are selected. A disconnected ship or incomplete route produces a waiting reply, with a
15-second return-planning backoff. New orders cancel the return trip; undocking while he is
still on the station interrupts it. This does not teleport Buddy aboard or enable spacewalks.

A loose pickup must reach its normal carrying point before a rotation hold begins. The lift
has a four-second timeout; failure uses ordinary carry cleanup instead of pinning it at floor height.

Cupboard extraction starts with a checked 3 cm vertical lift. Only that initial lift uses measured vertical half-height with a 2 mm contact skin; lateral padding is retained. Later sweeps use the full padded footprint. A blocked lift or exit leaves the item in place.

Pickup routes are checked before searching storage slots and checked again before walking.
Loose-item pickup also sweeps a short lift and the path to the hand before taking the item;
a visible item behind solid furniture is not pulled through it. A temporary blocker gets the
ordinary bounded clearance wait.

Overflow floor rays begin near the room's deck, below upper wall slopes. The complete item
footprint still needs level support and clear space. Failed floor and shelf checks report
which collider, slope or missing support prevented placement.

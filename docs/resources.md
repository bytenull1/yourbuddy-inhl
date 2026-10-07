# Resource duties

The normal orders stay on one page. Choose **Resources** for six buttons:

- **oxygen**, **fuel**, **energy**: toggle each duty.
- **Buying**: allow or forbid purchases for enabled duties.
- **Limit**: cycle the remaining shared spending allowance through 0, 1500, 3000, 6000 and 12000.
  One cell costs 750-1050 at the default price multiplier.
- **Orders**: return to the flat orders page.

Buttons update in place with NPC.Core's optional command-page interface. Replies are one line;
Back shows the conversation log. There is no number editor: typed orders and door codes keep
working. Duties and buying default off; the spending allowance defaults to zero.

The console has the same page: `buddy_order resources` shows the settings; `oxygen`, `fuel`,
`energy` and `buying` toggle (or take `on`/`off`); `limit <amount>` sets any allowance.

## Policy and saves

An enabled duty refills at or below 30% towards 80%. Oxygen means stored ship oxygen,
not room atmosphere. The game's fixed transfer increments can overshoot the target.
With Buying on and fewer than one usable matching cell aboard, Buddy recovers a loose
station cell or buys one. A cell aboard he failed to reach in the last minute does not count. A shop cell priced over the allowance or your cash is named in the
report with its price. Supplies aboard are preferred, then reachable loose supplies; closed containers are not opened.
A charged cell in the loader counts once, even before the cached scene scan includes it.
When a refill is due, the cell it recovers or buys goes straight into the loader; otherwise
it is stored aboard as a spare.
A run brings back one cell. Every purchase must fit both the remaining allowance and the
player's cash. Spending reduces the allowance; toggling Buying does not replenish it.

Settings belong to the save and are stored in the `.buddy` sidecar. A new game or a save
without settings starts with defaults. A world reset cancels runtime work but preserves
settings already read from a sidecar. In-progress routes are not restored.

## Errands and interruptions

`ResourceErrand` participates in the ordinary decider alongside other errands, using its
planning, skip list and deferral helpers. It requires autonomy and respects existing orders,
fear and survival priorities. Resource buttons do not change the Autonomy configuration or
release a standing order. **Decide** on the normal orders page uses the existing autonomy order.

Only one buddy owns the shared loader duty. What is already in the loader when a refill is due:

| In the loader | Buddy |
|---|---|
| a cell that is loading | leaves it; the refill waits ("loader occupied") |
| a spent cell | ejects it, then inserts his own |
| an idle charged cell of the due kind | switches loading on and uses it |
| an idle charged cell of another kind | ejects it and stores it as a spare; the refill follows on the next check |

A cell that was in the loader stays there if the run is interrupted. Restocking remains independent.
The loader is used from inside its airlock: a stand point on the far side of one of the airlock's
floor-level doors is refused, since the walk to it ends in the docking collar.
When a refill reaches its target, or the loader stops, Buddy ejects his cell and stores it as a
spare, so it is not left in the airlock. Interruption ejects only its own cell; saving does not interrupt a run. Purchases and deliveries are rechecked before acting.
An uncertain purchase pauses duties; interrupted purchase verification turns Buying off.
Inspect the shop and cash before enabling work again.

## Storing items already aboard

Resource restocking brings missing spare cells aboard. It does not reorganise existing stock.
The separate [storing errand](storing.md) puts suitable loose supplies into furniture storage,
including resource cells. **Store items** continues until another order is given; it requires neither Buying nor
low resource levels. Refilling and storing compete through the normal decider.

## Scans and storage

Spare cells use designated floor areas in enabled rooms, in this preference order:

| Resource | Preferred rooms, then fallback |
| --- | --- |
| Oxygen | `Core_L13E`, `Core_L07`, `Front_R03`, cockpit (`Front_M00`) |
| Fuel | `Front_L06`, cockpit |
| Energy | `Core_R10E`, `Core_R04`, cockpit |

`StoragePlaces` defines three candidate positions per area. They are room-local, so they
move with the ship and its upgrade layout. The cockpit has different positions per resource;
all positions still require enough space for the actual cell. Built-in layouts select rooms
automatically; these are not player-placed markers or cupboard slots.

Resource and shop searches include switched-off rooms, like the sell and suit errands: the room
is loaded before Buddy plans to it. Active objects come from `SceneScan.ThisFrame`; switched-off ones
from a 5 s cache (`ResourceScan`).
Cell candidates reuse a list; staged-item membership refreshes twice per second, immediately
before buying, and is checked live before pickup.
After buying, verification waits for the next frame's snapshot instead of forcing a new scan.
Storage checks designated rooms and the carried item's footprint. A shared budget permits at
most four candidate probes per frame; a pending search holds its errand while continuing
from its cursor. Exhausted searches are cached briefly; layout changes invalidate candidates. Placement is checked again on arrival.
Of the first four reachable spots, Buddy carries the cell to the one with the shortest walk;
the nearest by straight line can lie behind the airlock wall. Before setting off he loads the
switched-off ship rooms under that route, so their furniture is seen. A spot that is then
blocked or unreachable is dropped and the search goes on.

Cells are placed on supported clear floor, outside airlocks and restricted item zones.
Storage does not put cells inside cupboards or create paths through furniture. If no safe
local drop exists on interruption, Buddy holds the item and pauses duties. Move it to clear
floor before toggling a resource to retry.

See [the ownership invariant](invariants.md#resource-duties-own-only-their-cell).
Resource progress and blocked jobs are logged with the `[resources]` prefix; ordinary decider
failures use the existing `[mind]` retry trace.

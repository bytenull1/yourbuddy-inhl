# Known issues and dead ends

Three lists:

1. **Open issues** - known defects or weak spots, not fixed yet.
2. **Dead ends** - explanations that turned out wrong. Don't chase them again.
3. **Abandoned approaches** - designs that were tried and dropped, and why.

When you fix an open issue, delete its entry and document the result in the topic doc.
Limitations accepted by design are listed in each topic doc, not here.

---

## Open issues

### The floor probe repeats work within a frame

*Performance, not a bug.* `NavProbe.TryFloor` runs ~7 times per frame and is the mod's largest
remaining probe cost. `FloorUnderBuddy` alone asks ~2 times per frame from the same position.

- **Possible fix:** a one-frame memo on quantised position, like `NavProbe.FrameGates`
  ([probes.md](probes.md#caches)).
- **Check first:** trace whether the calls really ask about the *same* point. If not, a memo only
  adds stale-value risk.
- **Risk:** floor heights drive every level test ([floor-to-floor](invariants.md#floor-to-floor)).
  Quantisation must be far below the deck spacing, and the memo must not outlive the frame
  ([never-cache-node-world-positions](invariants.md#never-cache-node-world-positions)).

### The gate-frame rule can hide stair treads from the floor probe

Next to a doorway, the rule can ignore a stair tread, so an **ungrounded** `FloorUnderBuddy` reports
the surface below it. Seen on the `OxygenStation` stairs beside `Door02`.

- **Worked around** for stair legs, which read the feet instead
  ([off-the-flight-is-off-the-plan](invariants.md#off-the-flight-is-off-the-plan)).
- **Still exposed:** other ungrounded floor reads near doorways - `sameLevel` in
  `AdvancePastReachedWaypoints`, Follow's level checks, `NodeFloorY`. No symptom seen.
- **Possible fix:** don't apply the rule to downward floor rays, or only near the gate's own
  threshold height. Test doorways and both stations' stairs.

### Two definitions of a waypoint's deck

`AdvancePastReachedWaypoints` computes `sameLevel` from a fresh probe (`NavProbe.FloorHeight(wp)`),
while the stair-leg code reads the plan's measured deck (`NavPath.WaypointFloorY`). On a flight they
can disagree.

- **Impact today:** none seen; the off-the-flight gate stops the over-advance either way.
- **Fix:** use `WaypointFloorY` everywhere. It touches every waypoint advance in Follow, Wander and
  Flee, so test all three on both stations' stairs.

### The goal floor in `FindPath` is still probed

`start.y` is trusted ([the-start-point-is-already-a-floor](invariants.md#the-start-point-is-already-a-floor)),
but the goal's floor is probed. A player standing in a stairwell doorway could get a floor two
storeys down. Fix: pass the goal floor in (`FloorUnderPlayer`). This changes the `FindPath`
signature - see [architecture.md §7](architecture.md#7-change-impact---what-else-to-update).

### The airlock-chamber test may match the room next to the airlock

`RoomIsAirlockChamber` compares against `Airlock.connectedRoom`, which the game uses for the room
*beside* the airlock (where the player is put on entering). If so, no safe spot is ever remembered
in that room, and a buddy that reaches space is placed next to the player instead of where it stood.

- **To confirm:** read every `Airlock.connectedRoom` from `level1`.
- **Minor effect.** A wrong fix could store a safe spot inside a chamber, so don't guess.

### The 0.85 m carve-out floor is wider than any real doorway

Every door opening is 0.5625 m + margin, but the clamp floor is 0.85 m, so ~0.29 m of wall either
side of each doorway is forgiven. No routing through walls has been seen. **Don't narrow it** on
this alone: too narrow makes the buddy pace in front of open doorways, which is worse.

---

## Dead ends

Explanations that were checked and are wrong.

- **"The gate carve-out needs a name list / a bigger radius."** See
  [below](#the-gate-carve-out). The carve-out is the gate's opening; nothing else works.
- **"Links need line of sight."** No -
  [links-have-no-los](invariants.md#links-have-no-los).
- **"Follow's look-at-the-player pause stalls stair climbs."** No. Rotation is cosmetic; movement
  goes through `cc.Move`. The real cause was [one-entry-predicate](invariants.md#one-entry-predicate).
- **"Moving nodes can fix routing through a wall."** No. If a probe cannot see the wall, edges link
  across it wherever the nodes are. Fix the probe.
- **"The buddy's lifecare icon disappears."** It was the *player's* icon, a game bug -
  [lifecare.md §3](lifecare.md#3-the-missing-player-icon-game-bug-worked-around).
- **"The OxygenStation bot pushes the buddy off the stairs."** No. The jump was the floor probe
  answering two storeys down ([a-grounded-buddy-stands-on-its-own-feet](invariants.md#a-grounded-buddy-stands-on-its-own-feet)).
  The bot's colliders have no script to key on anyway.
- **"The stairs are too steep / step offset too small."** No. Flights are 21–28°, and the buddy
  uses the player's own `stepOffset` and `slopeLimit`.

### The gate carve-out

The buddy walked through the solid mid-ship wall in `Core_M01`. Every rule below failed:

| Rule | Result |
|---|---|
| collider-wide carve-out (`bounds.ClosestPoint`) | any wall *containing* a door vanished |
| name keywords (`door`, `airlock`, …) → 2.5 m radius | `"wall_long_door"` contains `door` |
| measured leaf radius | 2.04 m - leaves are measured retracted in the jamb |
| "a passage has ≥ 1.6 m of leaf" | broke every ship door (1.44–1.54 m) |
| "a passage has `gateAnchors`" | `ElectricityPanelGate` has anchors but no leaf; station doors carved 1.95 m |
| **the leaves' span while shut; a passage needs an anchor and a leaf** | **current rule** |

Lessons:
- **A radius erases wall along the wall.** Overlapping radii erased the whole mid-ship pier.
- **Size cannot tell a door from a cabinet.** Any leaf-height cut breaks either all ship doors or
  all station doors.
- **A recovery that fires every time is a wrong plan.** The buddy "recovered" after touching the
  wall, which looked like a steering glitch; A\* was planning through the wall every time.
- **Throttle audit logs per subject, never globally.** A single global throttle logged one ignore
  in ten thousand, and the offending wall never appeared in a capture
  ([probes.md](probes.md#the-audit)).

---

## Abandoned approaches

| Version | Approach | Why it ended |
|---|---|---|
| v1 | Raycast steering (forward ray, turn left/right) | failed at every door, corner and staircase |
| v2 | Routes recorded by walking them | not maintainable |
| v3 | Auto node graph, ship only | worked on the ship; could not navigate stations |
| v4 | Unity NavMesh baked at runtime | the game's geometry could not be baked |
| v5 | **Multi-owner node graph** (current) | nodes per owner, manual links, node types, A\* |

Every version failed at level changes and doorways until the graph became hand-placed and the
level rule explicit.

Also dropped:
- **Walking to better air elsewhere aboard.** Environments are per vessel, so there is nowhere
  better to go. The buddy switches the unit on instead ([terminals.md](terminals.md)).
- **Throwing things at the Breathless.** Not done; nothing is thrown at the monster or the player.

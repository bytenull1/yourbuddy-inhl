# Invariants - rules that must not be broken

Each rule is stated once, here. Code and other docs link to its anchor.
Every entry exists because breaking it caused a bug.

Format: **Rule** - what must hold. **Why** - what breaks without it. **Enforced in** - where it lives.

---

## Heights and probes

### floor-to-floor

**Rule.** Every vertical comparison resolves *both* sides to the floor beneath them
(`NavProbe.TryFloorHeight`, or `BuddyNodeGraph.NodeFloorY` for a node) before comparing.

**Why.** Positions have three different vertical offsets: a plan `start` is a floor point, a
body position is ~0.6 m above the feet, and a node is wherever the editor dropped it (shipped
nodes hover ~1 m above their deck). A raw `|a.y - b.y|` measures the offset, not the deck.
Violating this *inverted* the level test: nodes on the buddy's own deck were rejected, nodes a
deck below passed as "same level", and the buddy walked into a railing.

**Enforced in.** `NavProbe.TryFloorHeight`, `NavProbe.ThinLos`, `BuddyNodeGraph.NodeFloorY` /
`EntryDeltaYAllowance` / `GoalCost`, `BuddyBehaviour.FloorUnderBuddy`,
`AdvancePastReachedWaypoints`. See [navigation.md §2](navigation.md#2-nodes-are-not-on-the-floor).

### node-hover-not-height

**Rule.** Cache a node's *hover* (`node.y − floorY`), never its absolute floor height.

**Why.** The hover survives the owner moving during flight; an absolute height goes stale
every frame the world moves.

**Enforced in.** `BuddyNodeGraph.NodeHover` / `NodeFloorY`.

### probe-buffers-must-not-truncate

**Rule.** Every `*NonAlloc` buffer is large enough for the busiest spot in the game, and logs
when it fills.

**Why.** A full `RaycastNonAlloc` buffer drops hits arbitrarily, not the farthest ones. With 8
slots, the docking corridor's many overlapping colliders pushed the deck itself out, giving
"no floor" at one point and a deck 2 m down a metre away.

**Enforced in.** `NavProbe.FloorHits` / `LosHits` (64), `NavProbe.WarnIfTruncated`.

### a-missing-floor-is-a-last-resort-not-an-answer

**Rule.** When the filtered floor probe finds nothing, `TryFloorHeight` casts once more across
**every** layer before reporting failure.

**Why.** Some decks (the docking corridor) have no collider the normal probe sees. "No floor"
makes each side of a comparison fall back to a different base - the buddy to its feet, a node
to its marker - which breaks [floor-to-floor](#floor-to-floor). In the corridor this rejected
every nearby node and sent the buddy back through the door it came from. The wide pass only
replaces a non-answer, so it cannot change a case that already works.

**Enforced in.** `NavProbe.TryAnyLayerFloor`, called from `TryFloorHeight`.

### unprobeable-floors-keep-the-last-hover

**Rule.** When no floor is found under a node, `NodeFloorY` uses that node's last measured
hover, else the median hover of all measured nodes (`TypicalHover`). Never the marker's own Y,
and never drop the node.

**Why.** Falling back to the marker put corridor nodes ~1 m "off deck", so they were rejected
as entries while distant station nodes passed. Dropping unprobeable nodes was worse: it removed
the docking corridor and the first node aboard. All markers are placed the same way, so the
median is a real estimate.

**Enforced in.** `BuddyNodeGraph.LastKnownHover` / `TypicalHover`, read by `NodeFloorY`.

### the-start-point-is-already-a-floor

**Rule.** `FindPath` and `CanReachEntry` take `start.y` as the start floor and never re-probe it.
Only `end` (a body point) is probed.

**Why.** Callers pass `FloorUnderBuddy()`, which is already correct
([a-grounded-buddy-stands-on-its-own-feet](#a-grounded-buddy-stands-on-its-own-feet)).
Re-probing in a doorway returned a deck two storeys down, so the landing below read as "same
deck" and the buddy stepped off the stairs to reach it.

**Enforced in.** `BuddyNodeGraph.FindPath`, the public `CanReachEntry` overload.

### a-grounded-buddy-stands-on-its-own-feet

**Rule.** `FloorUnderBuddy` returns the buddy's **feet** whenever
`CharacterController.isGrounded`. The floor probe is used only while airborne. The player gets
the same rule in `FloorUnderPlayer`.

**Why.** The floor probe can answer with a surface below the one the body is on - at the
`OxygenStation` stair head it once answered the landing two storeys down
([floors-ignore-the-gate-frame-rule](#floors-ignore-the-gate-frame-rule)). The buddy stepped off
the top of the stairs, climbed back, and repeated. A grounded controller is physics reporting
what the body stands on, so it is not a [second probe basis](#one-probe-basis).

**Enforced in.** `BuddyBehaviour.FloorUnderBuddy`, `BuddyBehaviour.FloorUnderPlayer`.
The goal floor in `FindPath` is still probed - see [known-issues.md](known-issues.md).

### probe-what-the-body-collides-with

**Rule.** `NavProbe.ProbeLayers` is exactly the set of layers the buddy's body collides with,
read from the game's collision matrix for its `CharacterController` layer. Never a hand-kept
list, never `DefaultRaycastLayers`.

**Why.** A probe that sees what the body walks through invents obstacles. The docking hatch lid
sits on `Interactable`, which the body ignores; the whiskers hit it and the buddy hugged the
door jamb through every airlock. A question about another collider uses *that* collider's row:
doorway occupancy asks the `AntiCrasher`'s layer (`CollisionMaskFor`), which also stops for
dropped items ([occupancy-asks-the-anticrasher](#occupancy-asks-the-anticrasher)).

**Enforced in.** `NavProbe.ProbeLayers`, `BodyLayer`, `MaskFor`, `CollisionMaskFor`.

### whiskers-are-body-shaped

**Rule.** Every whisker capsule (`BodyBlocked`, both stuck diagnostics) comes from
`BuddyBehaviour.BodyCastCapsule`: 0.25 m above the floor up to the top of the controller
(`height + skinWidth`), never a fixed height.

**Why.** The controller is 1.0 m tall; the capsule used to reach 1.69 m. Ungated ship doorways
have a lintel at 1.44 m, so they read as walls from every heading and the buddy paced along the
wall.

**Enforced in.** `BuddyBehaviour.BodyCastCapsule`.

### a-low-ceiling-is-measured-from-the-body

**Rule.** `UnderLowCeiling` (the gate on `EmergencyUnstick`) looks up only to the top of the body
plus `CeilingHeadroom` (0.1 m), from 0.25 m above the feet, ignoring the buddy and player.

**Why.** The old ray reached 2.0 m above the feet, and every ship ceiling is at 1.81 m, so the
check was always true aboard and Follow teleported the buddy after three slow cycles.

**Enforced in.** `BuddyBehaviour.UnderLowCeiling`.

### walkable-ground-is-not-an-obstacle

**Rule.** Steering avoids only what the `CharacterController` cannot traverse. A hit within
`slopeLimit` of horizontal, or a step no higher than `stepOffset` with clear space above, is
ground.

**Why.** The whisker capsule sees any slope steeper than 14.7°, and every staircase is steeper
(the `OxygenStation` first flight is 21°). The whiskers treated stairs as walls and detoured
into the railing. The buddy uses the player's own `stepOffset` and `slopeLimit`, so it can walk
anything the player can. The clearance check stops a wall's bottom edge passing as a step.

**Enforced in.** `NavProbe.HitIsWalkableGround` via `BuddyBehaviour.IsWalkableGround`; used by
`BodyBlocked`, `TryAutoJump` and both stuck diagnostics.

### a-detour-may-not-step-off-a-ledge

**Rule.** A whisker detour or stuck sidestep may not head toward ground more than
`DetourLedgeDrop` (0.7 m) below the buddy's floor. The direct heading is never tested this way.
An unprobeable floor is not a ledge.

**Why.** Detouring around the station bot at the top of the `OxygenStation` stairs took the
buddy over the railing, two decks down. 0.7 m allows stair treads and the 0.63 m platform, and
rejects a 1.25 m deck.

**Enforced in.** `BuddyBehaviour.StepsOffALedge` (from `SteerAroundObstacles`,
`UpdateStuckDetection`, `TryBackAway`).

### one-probe-basis

**Rule.** All navigation probes go through `NavProbe`. Never write a second raycast with its own
collider filter.

**Why.** Two filters on two sides of a comparison measure the difference between the filters -
the same bug as [floor-to-floor](#floor-to-floor). `BuddyManager.FloorOwner` once had its own
ray, missed the [wide fallback](#a-missing-floor-is-a-last-resort-not-an-answer), and hid the
buddy from the lifecare terminal.

**Enforced in.** `NavProbe`; `FloorUnderBuddy` and `BuddyManager.FloorOwner` delegate to it.

### gate-frame-hit-point

**Rule.** The gate-frame carve-out judges **the point the cast touched**, never the collider as
a whole. Route hit points through `NavProbe.ContactPoint(hit, origin)` (casts that start inside
a collider report `point == zero`).

**Why.** `bounds.ClosestPoint(gatePos)` is 0 for any collider that *contains* a gate - such as
the wall that holds the door. The whole mid-ship wall vanished and the buddy walked into it.

**Enforced in.** `NavProbe.HitIsGateOpening`, `IsEdgeProbeIgnorable`.

### gate-carveout-is-the-opening

**Rule.** A gate carves out only **the opening it makes**, as a slot in the gate's own frame:
`openingHalfWidth` across (local x), `GateOpeningHalfHeight` vertical, `GateOpeningHalfDepth`
through the wall (local z). A gate with no anchors, or no leaf to slide, is not a door and
carves nothing.

**Why.**
- *Shape.* A radius around the gate origin erases wall along the wall in both directions.
  Overlapping radii erased the whole mid-ship pier.
- *Passage test.* `ElectricityPanelGate` (a breaker cabinet) has anchors `[Empty, Empty,
  itself]` - no leaf. Leaf *size* cannot separate doors from cabinets: ship leaves are
  1.44–1.54 m, station leaves 1.95 m.
- *Width.* The opening is the span the leaves cover when shut, plus `GateOpeningMargin`, clamped
  to `[GateOpeningRadius, MaxGateFootprintRadius]`. Every real door measures 0.5625 m and lands
  on the 0.85 m floor. Station doors slide one leaf *up*; reading that 1.70 m stroke as a width
  carved 3.9 m of wall from every station doorway.
- *Fallback.* If `gateAnchors` cannot be read at all (a game update), every gate keeps a 0.85 m
  cylinder and stays a passage, so doors do not all close at once.

Docking collars and airlocks do not use the carve-out: any hit with a `Docker`, `Airlock` or
`Gate` in its parents is ignored outright. Do not restore name matching, leaf-position radii or
size-based passage tests - all three were tried ([known-issues.md](known-issues.md#the-gate-carve-out)).

**Enforced in.** `NavProbe.MeasureOpening`, `HitIsGateOpening`.

### a-doorway-is-crossed-not-grazed

**Rule.** For `WalkLos` and `ThinLos`, a hit is forgiven by the carve-out only when the **whole
line under test** crosses the gate's plane inside the opening. A line whose ends lie on the same
side never passes through, so its hit point alone decides. The whiskers (probes that stop in the
doorway) keep the point-only test; `TryFloor` does not use the carve-out at all
([floors-ignore-the-gate-frame-rule](#floors-ignore-the-gate-frame-rule)).

**Why.** The carve-out is wider than the hole it stands for - a station doorway is 1.25 m across
and the slot is 1.70 m - and the wall blocks beside it are 1.25 m deep. The jamb face therefore
lies *inside* the slot along its whole depth. A line that clips that face reports its one entry
hit there, Unity reports nothing for the sub-casts that start inside the block, and 1.5 m of solid
wall becomes invisible. A\* then buys a straight finish leg through the wall, and the buddy paces
in front of it. Widening the hit-point test cannot fix this: the hit point is genuinely in the
opening. Only the line's own crossing tells doorway from jamb.

**Enforced in.** `NavProbe.ChordCrossesOpening`, `HitIsGateOpening`, `WalkLos`, `ThinLos`.

### floors-ignore-the-gate-frame-rule

**Rule.** The floor probe (`TryFloor`) never applies the gate-frame carve-out. It skips only bodies
and passable interfaces: docking collars, airlocks and door leaves.

**Why.** The carve-out is 5 m tall and reaches 1 m either side of the wall. On a downward ray it
dropped the deck under a doorway and kept a floor lower down. Beside the `OxygenStation` top-deck
door (`Door02`) it dropped the 6.25 deck and kept the landing at 3.75, exactly 2.5 m below. The
entry probes re-floor their start point, so from the top deck they judged the stairwell below:
nodes on the buddy's own deck failed as "los", and `#406` on the landing below passed across the
railing.

**Enforced in.** `NavProbe.TryFloor`, `IsPassableInterface`.

### doorway-floor-survives-the-filter

**Rule.** When the filter rejects every hit, the floor probe falls back to the highest solid
non-body hit.

**Why.** Airlock and docking-collar floors are filtered as passable interfaces. Without the
fallback, a point on one reports "no floor".

**Enforced in.** `NavProbe.TryFloorHeight`.

---

## Pathfinding

### entry-seeds-on-own-deck

**Rule.** A route entry seed must be on the buddy's own deck. The one exception: a `Stair` node
within `StairSeedRadius`, up to `MaxStairDeltaY` off-level.

**Why.** If a node one level up counts as an entry, A\* walks straight at it in one hop, and the
user's links - including `Block` and `Priority` - are never used. On its own deck the buddy must
traverse the edges the user drew through the stairs.

**Enforced in.** `BuddyNodeGraph.EntryDeltaYAllowance` (`SameLevelDeltaY`).

### a-stair-entry-is-committed-once-taken

**Rule.** When a plan's first waypoint is a `Stair` node entered off-level, it counts as forced,
so commitment does not re-probe it. Line of sight only - a locked door still blocks it.

**Why.** `forced[i]` describes the edge arriving at `i`; the first node has none. A flight longer
than `StairSeedRadius` let the buddy walk out of seed range between two ticks, so every plan was
rejected on the next tick.

**Enforced in.** `BuddyNodeGraph.FindPath`, where `forced[]` is built.

### an-entry-must-be-walkable-not-merely-visible

**Rule.** An off-level entry (the `Stair` exception) also needs continuous ground
(`GroundIsContinuous`: no break over `StairEntryGroundStep`, 0.8 m, sampled every 0.35 m) **and**
a clear knee-height line (`WalkLos`, 0.4 m).

**Why.** `ThinLos` looks from 1 m up and samples coarsely, so it flies over an 0.85 m railing and
dips through a stairwell. The buddy seeded the other flight's head across the well, crossed the
railing and fell two decks. Ground continuity alone still let it enter through a wall, hence
both tests. Same-deck entries and graph edges are unaffected.

**Enforced in.** `NavProbe.GroundIsContinuous`, `NavProbe.WalkLos`, from
`BuddyNodeGraph.CanReachEntry`.

### one-entry-predicate

**Rule.** `FindPath` seeding and `UpdateFollow` commitment both call
`BuddyNodeGraph.CanReachEntry`. Never inline a threshold on either side.

**Why.** If commitment is stricter than seeding, every plan is rejected the tick after it is made
and the buddy replans forever without moving.

**Enforced in.** `BuddyNodeGraph.CanReachEntry`.

### seed-radius-is-not-finish-length

**Rule.** `MaxSeedDist` (how far an entry may be) and `MaxGoalFinishDist` (how long the final
straight walk may be) are separate constants.

**Why.** As one 60 m value, any node with a lucky sight line ended the search in a 25 m beeline
through glass and across decks.

**Enforced in.** `BuddyNodeGraph.MaxSeedDist`, `MaxGoalFinishDist`.

### a-clear-short-goal-beats-the-graph

**Rule.** Before seeding, `FindPath` returns a one-waypoint plan when the goal is within
`MaxGoalFinishDist`, on the same deck, door-clear and endpoint-clear
([an-endpoint-must-be-walkable-not-merely-visible](#an-endpoint-must-be-walkable-not-merely-visible)).

**Why.** The graph could answer a 3.7 m walk with a 17.6 m detour. This is the finish-leg
predicate anchored at the buddy, so it can beeline no further than a finish already may.

**Enforced in.** `BuddyNodeGraph.FindPath`, before seeding.

### an-endpoint-must-be-walkable-not-merely-visible

**Rule.** The short clear goal and the finish leg share one predicate, `HasLineOfSight`:
`ThinLos` **and** `WalkLos` (knee height).

**Why.** `ThinLos` looks over anything under 1 m. The buddy walked into the 0.52 m sell-station
railing beside nodes that went round it. Graph edges keep `ThinLos` alone: a strict sweep on
edges rejected the links the user drew.

**Enforced in.** `BuddyNodeGraph.HasLineOfSight`.

### same-level-tolerance-ceiling

**Rule.** `SameLevelDeltaY` and `WaypointAdvanceMaxDeltaY` stay **below** the shallowest real
level change in the game (the 0.63 m railed platform).

**Why.** Raise either and a deck reads as a step.

**Enforced in.** `BuddyNodeGraph.SameLevelDeltaY`, `BuddyBehaviour.WaypointAdvanceMaxDeltaY`,
`FollowSameLevelDeltaY`.

### links-have-no-los

**Rule.** Every graph edge is a link the player drew, and it skips line of sight by design. Never
add a line-of-sight check to commitment for one. Locked doors still block them
([locked-doors-block-edges](#locked-doors-block-edges)).

**Why.** Users draw them exactly where geometry blocks sight (railings, airlock collars). A "next
stretch blocked" check fired every 0.22 s mid-stair and made links useless. Runtime blockage is
the stuck detector's job.

**Enforced in.** `NavPath.IsForced`, checked before the commitment probe.

### fallback-respects-backtrack

**Rule.** The nearest-node fallback applies the same `BacktrackPenalty` as seeding.

**Why.** On raw distance it picked a node just behind the buddy over one just ahead, causing
sudden turns backwards.

**Enforced in.** `BuddyNodeGraph.FindPath` nearest-node fallback.

### backtrack-memory-outlives-the-plan

**Rule.** The anti-backtrack reference is a persistent field, not `navPlan[navPathIndex - 1]`.

**Why.** An entry-blocked replan resets the index to 0, so the memory vanished exactly when a
tight replan loop needed it.

**Enforced in.** `BuddyBehaviour.lastDepartedWaypoint`, set in `AdvancePastReachedWaypoints` for
real nodes only.

### never-cache-node-world-positions

**Rule.** Call `ToWorld` fresh in each graph query. Never cache world positions, owner
transforms or docked state across frames.

**Why.** The game moves the world around the ship, so station node positions drift during flight
while their owner-local coordinates stay valid.

**Enforced in.** `BuddyNodeGraph.FindPath`, `RebuildEdges`, via a per-call `OwnerSnapshot`.

### avoid-entry-bars-the-whole-search

**Rule.** `avoidEntry` excludes matching nodes from seeding, the nearest-node fallback **and**
neighbour expansion. Matching is identity (`AvoidEntryRadius`, 0.5 m), not proximity.

**Why.** The unreachable waypoint is usually several hops in; excluding it from seeding alone lets
A\* plan through it again. With the 1.5 m `BacktrackRadius`, barring one `OxygenStation` landing
also barred its twin 1.4 m away and deleted the stairs from the search.

**Enforced in.** `BuddyNodeGraph.FindPathCore` (`excluded`).

### a-barred-waypoint-is-a-preference-not-a-wall

**Rule.** If a search with `avoidEntry` finds nothing, `FindPath` retries once without it.

**Why.** A staircase chain is linear; barring one node cuts the graph in two. The buddy then kept
an old plan pointing two decks down and ground along the railing. The escalation ladder still
ends any loop this could cause ([escalate-over-a-window](#escalate-over-a-window)).

**Enforced in.** `BuddyNodeGraph.FindPath`, wrapping `FindPathCore`.

### a-stale-plan-is-worse-than-none

**Rule.** When `FindPath` returns nothing, `UpdateFollow` keeps the previous plan only while its
current waypoint is within one flight (`MaxStairDeltaY`) of the buddy's floor.

**Why.** A plan made two decks ago walks the buddy into whatever lies between. The test is
deliberately weak; on a stair leg the exact test is
[off-the-flight-is-off-the-plan](#off-the-flight-is-off-the-plan).

**Enforced in.** `BuddyBehaviour.UpdateFollow` via `BuddyNodeGraph.WaypointIsOnAReachableDeck`.

### opening-is-not-passing

**Rule.** "May the buddy **open** this gate?" (`GateIsPassable`) and "should routing go **around**
it?" (`GateBlocksRouting`) are different questions. Only a gate that will still be shut when the
buddy arrives - locked, a door the player could not walk open, a pin door without the code - blocks
routing.

**Why.** As one predicate, the buddy's refusal to open airlock and docking gates made the ship
unreachable from a station. The player opens those. `ElectricityPanelGate` never blocks routing.

**Enforced in.** `BuddyBehaviour.GateBlocksRouting`, `GateIsPassable`; `RefreshImpassableGates`
uses the former.

### locked-doors-block-edges

**Rule.** A route stretch through a gate that will still be shut is rejected in seeding, the
fallback, neighbour expansion and the finish leg - **Force and Priority edges included**. The test
is the gate's own volume, never a radius.

**Why.** Without it A\* planned through locked doors and the buddy shoved at them. The Force
exemption is about sight, not doors. A radius around the origin also blocked the corridor running
past a door; `NavProbe.SegmentCrossesGate` slab-tests the gate in its local space. Nothing is cast:
this runs on every relaxed edge.

**Enforced in.** `BuddyBehaviour.GateBlocksRouting` / `SegmentBlockedByDoor`,
`NavProbe.SegmentCrossesGate`, via `BuddyNodeGraph.DoorBlocks`.

### stand-still-when-door-blocked

**Rule.** When `FindPath` fails *and* `LastPathBlockedByDoor` is set, the buddy holds position and
logs why (throttled). No random step-off, and Wander does not count it as a failure.

**Why.** The step-off is for a buddy wedged on furniture. For a door it cannot open it only turns
"waiting for you" into milling about.

**Enforced in.** `BuddyBehaviour.UpdateFollow`, `UpdateWander`, via `LogDoorBlockedRoute`.

---

## Path following

### commitment-skips-on-route

**Rule.** The entry-stretch probe is skipped while the buddy is within `NavPathOnRouteRadius` of the
previous waypoint.

**Why.** That stretch is an edge already validated between node positions. Re-probing from the
buddy's offset position clipped railings and made it shuttle between two nodes.

**Enforced in.** `BuddyBehaviour.IsStandingOnRoute`.

### escalate-over-a-window

**Rule.** Blocked entries are counted over a sliding time window, never as a consecutive run.

**Why.** The failure is a ping-pong with healthy ticks in between, which reset a run counter.

**Enforced in.** `BuddyBehaviour.entryBlockedCount`, `EntryBlockedWindow`.

### drop-the-index-on-a-blocked-entry

**Rule.** `navPathIndex` is never kept across an entry-blocked or
[off-the-flight](#off-the-flight-is-off-the-plan) replan, and a kept index never points past the
waypoint being walked (`min(old, resumeAt)`).

**Why.** The index is the unreachable part. And a new plan with fewer waypoints before the matched
tail made a kept index skip ahead, steering at the goal from the foot of the flight.

**Enforced in.** `BuddyBehaviour.UpdateFollow` (`SameRemainingPath`).

### waypoint-advance-is-dual

**Rule.** Advance on `(distXZ < WaypointReachedXZ && sameLevel) || dist3D < WaypointReached3D`, with
`sameLevel` measured [floor to floor](#floor-to-floor), or when the buddy is already on the stair
leg leaving the waypoint. Never advance past a waypoint whose *outgoing* leg is a flight the
buddy's feet are off.

**Why.** A pure 3D test needs the buddy on the landing before it may target it (oscillation at the
stair foot). XZ without `sameLevel` eats waypoints overhead. Going down, the deck-blind 3D branch
reaches a stair node's marker (~1 m above its deck) while the feet are still on the flight above.
[off-the-flight-is-off-the-plan](#off-the-flight-is-off-the-plan) catches that advance, but only as
a recovery: Wander drops the plan, steps back and re-picks, so the buddy paused at every
`ShipyardStation` landing on the way down.

**Enforced in.** `BuddyBehaviour.AdvancePastReachedWaypoints`.

### a-skipped-waypoint-is-not-an-arrival

**Rule.** A Route waypoint abandoned for being unreachable marks the plan. When such a plan runs
out of waypoints, the route ends as a failure - a warning naming the goal, and an order that is
reported *not* done. Only a plan walked to its end is an arrival.

**Why.** Stuck and no-progress recovery skip the waypoint underway. Skipping the last one falls
straight into `FinishRoute`, which said "the goto order is done" and cleared the order, so a
buddy that never left the wall it was pacing reported success. A recovery that hides a wrong plan
is how the [gate carve-out](known-issues.md#the-gate-carve-out) stayed hidden once already.

**Enforced in.** `BuddyBehaviour.SkipRouteWaypoint`, `FinishRoute`, `DropPlan`, `StartRoute`.

### a-stair-leg-is-walked-not-improvised

**Rule.** A *stair leg* is a plan stretch whose ends are more than `WaypointAdvanceMaxDeltaY` apart,
floor to floor. On one, the buddy steers `StairLegLookahead` along the leg rather than at its end,
and whisker detours, the stuck sidestep and auto-jump do not run. A waypoint counts as reached once
the buddy is on its outgoing stair leg (between its decks, past its start, within
`StairLegCorridor`).

**Why.** All three improvisers think on the flat, and at a stair head every flat answer leads off
the stairs:
- the horizontal whiskers hit the underside of the flight above and read it as a wall;
- aiming at the far end cut the corner into the railing end;
- the sidestep ran along the landing, into the wall;
- auto-jump hopped the 0.86 m railing as if it were low furniture. Heading along the leg does not
  prevent it: a wrong off-level entry is a leg across a railing, and the buddy hopped the fence to
  `#406` from the top deck ([floors-ignore-the-gate-frame-rule](#floors-ignore-the-gate-frame-rule)).

The advance half stops a mid-flight replan from sending the buddy back up to the flight head.

**Scope.** Follow, Wander and a flee's retreat. Route mode (`buddy_goto`) keeps its own advance.
Decks come from `FindPath` (`NavPath.WaypointFloorY`), never from re-probing waypoints.

**Enforced in.** `BuddyBehaviour.HeadingAlongPlan` (`walkingStairLeg`), `Update`,
`UpdateStuckDetection`, `AdvancePastReachedWaypoints`.

### off-the-flight-is-off-the-plan

**Rule.** On a stair leg, the buddy's **feet** stay between the two decks the leg joins (each
widened by `WaypointAdvanceMaxDeltaY`). If not, the plan is invalid: Follow replans at once with no
kept index and no backtrack penalty, dropping the plan if nothing routes; Wander drops the plan
**and steps off toward the leg's head**; a flee's retreat replans. Applies to Force and Priority
legs too.

**Why.** Nothing else caught a buddy knocked onto the neighbouring flight: Force legs skip the
commitment probe and progress is measured flat, so it climbed the wrong flight and every step
counted as progress. Wander must also *move*, because it waits in place and would pick the same
bad leg forever. Feet, not `FloorUnderBuddy`: ungrounded on treads, the floor probe can answer
with a surface below.

**Enforced in.** `BuddyBehaviour.HasLeftStairLeg` from `UpdateFollow`, `UpdateWander`
(`TryStepOffTowardLegHead`) and `UpdateFlee`; the next leg from `AdvancePastReachedWaypoints`.

### follow-arrival-is-level-aware

**Rule.** Follow's "close enough" test compares floors, not body heights.

**Why.** Distance is XZ, so a player on the deck above reads as 0 m and the buddy parks underneath.

**Enforced in.** `BuddyBehaviour.UpdateFollow` (`FollowSameLevelDeltaY`).

### step-off-applies-in-every-mode

**Rule.** `followStepOffUntil` is honoured by Follow, Wander, Route, Stay and Flee.

**Why.** Only Follow read it, so a wandering buddy sat in the doorway it was blocking. A flee's
back-away is a step-off too.

**Enforced in.** `BuddyBehaviour.TryStepOff`.

### idle-above-the-floor-plane-is-a-stall

**Rule.** No movement for `IdleNoRouteGiveUp` while more than `IdleAboveFloorDelta` above its own
floor plane means stalled: drop the plan and step off.

**Why.** Every other recovery is gated on `wantMove`, so a buddy perched on furniture with no
seedable node was watched by nothing. The floor-plane test keeps it from firing on normal waiting,
and `UpdateBaseFloor` adopts a real raised deck after ~4 s.

**Enforced in.** `BuddyBehaviour.UpdateIdleRecovery`.

---

## Doors

### pending-closes-are-a-list

**Rule.** `pendingDoorCloses` is a list, never a single slot.

**Why.** One slot dropped the first door when a second opened.

**Enforced in.** `BuddyBehaviour.pendingDoorCloses`.

### no-permanent-deferral

**Rule.** A deferred close must end. Never refresh `ArmedAt` while waiting; track `DeferredSince`
and act at `DoorDeferGiveUp`.

**Why.** Refreshing `ArmedAt` defeated the 90 s cap, and a door held by something static was
deferred silently forever.

**Enforced in.** `BuddyBehaviour.UpdateDoorCloseBehind`.

### never-force-a-close-into-the-buddy

**Rule.** The give-up path does not apply while the buddy itself is the blocker.

**Why.** It would fail, and the `Gate.FailClose` hook would re-arm it - a loop made of two correct
fixes. Keep stepping out; the 90 s cap ends it.

**Enforced in.** `BuddyBehaviour.UpdateDoorCloseBehind`.

### close-only-what-you-walked-through

**Rule.** A pending close fires only once the buddy is on the **opposite side** of the gate from
where it opened it. `NoteCloseFailed` entries start already crossed.

**Why.** A 3 s timer from opening shut doors in the buddy's face when it had not got through yet,
then it reopened them, forever.

**Enforced in.** `BuddyBehaviour.HasCrossed`, in `UpdateDoorCloseBehind`.

### fail-close-must-not-rearm

**Rule.** `NoteCloseFailed` does not call `ArmDoorClose` for a gate already pending.

**Why.** Re-arming resets `ArmedAt` and `Attempts`: unlimited retries and no 90 s backstop.

**Enforced in.** `BuddyBehaviour.NoteCloseFailed`.

### occupancy-asks-the-anticrasher

**Rule.** Doorway occupancy tests the gate's own `AntiCrasher` bounds on that sensor's layer row,
not a radius around the gate.

**Why.** A 1.2 m sphere always contained keypads, panels and the player, so the doorway was
"occupied" forever. The `AntiCrasher` *is* the game's test.

**Enforced in.** `BuddyBehaviour.DoorwayBlocker` via `GameInternals.GateAccess`.

### keep-electricitypanelgate-excluded

**Rule.** `ElectricityPanelGate` stays on the ignore list.

**Why.** Otherwise the buddy opens electrical panels.

**Enforced in.** `BuddyBehaviour.GateIsPassable`.

### the-buddy-opens-only-what-the-player-could

**Rule.** A gate that doorway detectors drive opens only through one of them that is switched on,
and whose `helmetRequired` the player meets. Otherwise it is treated as locked: not opened, routed
around. A gate no detector drives is not judged by this rule.

**Why.** The player opens a door only by walking into its detector. The tutorial's first door needs
a suit; the buddy opened it, and a player who followed was locked out of the room with the suits.
The Oxygen Station's sealed rooms have their detectors switched off, and only the seal panel opens
them; the buddy opened them anyway. Both are judged by the game's state, not door names. Airlock
and docking gates are checked first, so they stay routable.

**Enforced in.** `BuddyBehaviour.WhyClosedToPlayer`, read by `GateIsPassable` and
`GateBlocksRouting`; `RefreshDetectors` (`detectorsByGate`, switched-off detectors included);
`GameInternals.PlayerDetectorAccess`.

### the-buddy-only-knows-codes-it-was-told

**Rule.** The buddy opens a pin-code door only with `PinCode.ForceValidate()`, on a panel whose code
is in the shared `KnownCodes`. Never `PinCode.Interact(null)`.

**Why.** `Interact(null)` is the monster's branch: a random guess stored in the monster's save
data. `ForceValidate` fires the panel's own `OnValidated → Gate.InvokeOpen`, so close-behind works
unchanged.

**Enforced in.** `BuddyBehaviour.TryUnlockPasswordGate`.

### the-buddy-loads-rooms-by-the-door-rule

**Rule.** The buddy switches a room's content on only as the game does for the player: the room it
is in, the room behind a doorway while that door is open, both rooms of a door it opens, and an
errand's target room. It never loads the room behind a shut door it only walks past. It switches
rooms off only as the game does when the player fully leaves one: once a door it shut has finished
closing, away from the player, **both** rooms that doorway joins go off, whoever switched them on.
A room stays on while any active buddy or the player is in it, while the player is outside, while it
holds a sell station ([a-sell-station-room-stays-loaded](#a-sell-station-room-stays-loaded)), or while
another open door looks into it. A buddy's room is its tracked room. The release is done by the buddy
that shut the door, and `EntryDetector`'s side test may only overrule its tracked room for the two
rooms of its own doorway. Never across a doorway the game keeps loaded (`optimize` off).

**Why.**
- It used to load both rooms at every doorway within 3.5 m, open or shut, and switch none of them
  off. A walk down a corridor left every side room loaded. The game kept running them and saved them
  as loaded (`Room.Data.enabled`), so every load brought them back.
- The game's own unload on that close never runs, because a buddy close away from the player is
  hidden from `EntryDetector`
  ([buddy-closes-must-not-move-the-player](#buddy-closes-must-not-move-the-player)).
- "Only rooms the buddy loaded" is no test: a room restored from a save is nobody's.
- "Only the room it left, by the side test" failed twice:
  - A late close was judged from a third room. The YardCryo door's plane puts most of YardHallway on
    YardCryo's side, so it switched off YardLibrary.
  - A room kept for a door still closing was never looked at again.

**Enforced in.** `BuddyBehaviour.UpdateRoomTracking`, `LoadRoom`, `ReleaseRoomsAt` /
`ReleaseRoom` / `ReasonToKeepLoaded` (with `BuddyManager.OtherTrackedIn`), `LoadRoomsAroundGate`,
`SetForcedRoom`, `IErrandBody.LoadRoomOf`; `Patches.EntryDetector_DoorCheckForEnter_Postfix`, which
finds the closer through `BuddyManager.GateWasClosedByBuddy`.

---

## Fear and orders

### sight-stops-at-a-shut-door

**Rule.** Whether the buddy can *see* something is `NavProbe.CanSee` only: a straight line from the
eye, blocked by any gate that is not open, failing on any hit except bodies, the target's own
colliders, or frame trim in a fully open doorway.

**Why.** Walkability probes forgive gate openings whether or not the door is shut, re-floor each
sample and look from 1 m. As a sight test they see through closed doors. Gates are tested
explicitly because an airlock pair stands closer together than the carve-out is deep.

**Enforced in.** `NavProbe.CanSee`, from `BuddyBehaviour.MonsterInSight`.

### fear-owns-the-buddy

**Rule.** While fear is above Calm the decider does nothing. While Scared, only `UpdateFear` enters
and leaves `Flee`; an order given meanwhile replaces `modeBeforeFlee`, never `mode`.

**Why.** Orders, the decider and fear all write `mode`. Without precedence they take turns and the
buddy walks back at the monster. Flee is its own mode because `FinishRoute` forces Follow.

**Enforced in.** `ApplyOrder` / `ApplyRouteOrder`, `StandDownReason`, `UpdateFear` / `StartFlee` /
`EndFlee`.

### an-order-is-not-a-mode

**Rule.** What the player ordered (`orderedMode`) and what the buddy does (`mode`) are separate
fields. `orderedMode` is written only through `BuddyCommands` → `ApplyOrder` / `ApplyRouteOrder` /
`RevokeOrder`, and cleared otherwise only by expiry, an arrived goto, or a goto a flee cannot
resume.

**Why.** `mode` cannot say whether it was *told*. Inferring orders from it would either obey the
buddy's own choices or overrule the player's.

**Enforced in.** `BuddyBehaviour.Autonomy.cs`; `SetMode` and `StartRoute` are private.

### a-terminal-is-only-switched-on

**Rule.** The buddy flips a life-support power `Switch` only when it is **off**, on a unit that is
not running, not broken and powered, through `Switch.SwitchState()`. It never switches anything off
or cycles a switch that is on.

**Why.** Switch on with the unit off is the `OxygenError` / `ClimateError` puzzle; cycling the
switch would skip it.

**Enforced in.** `BuddyBehaviour.TerminalBlocker`, before the walk and at every approach step.

### a-snack-closes-what-it-opened

**Rule.** The buddy opens a container only with `Door.Open`, never one the player is hiding in, and
closes exactly the doors it opened whenever the task ends - unless the player has since climbed
into that spot. Snacks, tidying and hiding all follow this.

**Why.** Container doors drive the player's hidden flag (`HidingSpot.SetHidden`) and the fridge's
`Freezer`. A door the player left open is theirs.

**Enforced in.** `SnackErrand.SnackBlocker` / `TidyErrand.ContainerBlocker`, `Items.OpenContainerDoors`,
`Items.CloseOpenedDoors` (via a leg's `End`, after a tidy pick-up, and in `EnterHidingSpot` / `StepOutOfHidingSpot` /
`ForceLeaveHidingSpot`).

### a-hidden-buddy-is-saved-outside-its-hiding-spot

**Rule.** While hidden, every position the sidecar records is the floor point outside the spot,
never `MountPos`.

**Why.** A load knows nothing of hiding and would drop the buddy inside the furniture.

**Enforced in.** `BuddyBehaviour.HiddenSavePoint`, read by `BuddyManager.CaptureState`.

### a-hidden-buddy-waits-out-a-monster-it-can-hear

**Rule.** A buddy hiding **out of fear** does not come out while the Breathless is within
`HideMonsterNearDist` (5 m), however calm it is. The distance is `monsterDist`, with no sight test.
Only `HideMaxSeconds` (240 s) overrides it.

**Why.** Stress decays while hidden by design, so a calm-only exit stepped out into a room the
monster had not left. From inside a closet `CanSee` always fails, so the test must not use sight.
The ceiling and a 15 s log line keep the wait from being silent or endless.

**Enforced in.** `BuddyBehaviour.StayHidden`; distance from `UpdateFear`.

### an-ordered-hide-ends-only-on-an-order

**Rule.** A hide the player asked for ends **only** on another order (`ApplyOrder`, a goto,
`RevokeOrder`), on being found, or on a forced leave (death, parking, rebuild, despawn). No timer,
distance, calm or `Fear` setting ends it.

**Why.** Three things used to let the buddy straight back out:
1. `Door.Opened` stays true until the close animation ends, so its own close looked like being
   found. Now nothing counts as found until the doors were seen shut once (`hideDoorsShut`),
   bounded by `HideDoorShutSeconds`.
2. The no-monster branch let it out after `HideMinSeconds`.
3. `UpdateFear` force-left any hide when `Fear` was off.

**Enforced in.** `StayHidden`, `ConfirmHideDoorsShut`, `LeaveAnOrderedHide` (from `ApplyOrder` /
`ApplyRouteOrder` / `RevokeOrder`), the `hideOrdered` guard in `UpdateFear`.

### a-hide-that-worked-ends-the-flee

**Rule.** A hide that ended because the fear passed ends the flee too. Only a hide cut short
(found, doors would not shut, never got in) sends the flee back to `Retreat`.

**Why.** `UpdateFlee` runs every frame and beat `UpdateFear` to it, so a calm buddy climbed straight
into the next closet, again and again.

**Enforced in.** `LeaveHidingSpot(why, stillAfraid)`, read by `EndHide`.

### a-command-outranks-an-errand

**Rule.** A command given now takes the buddy off an errand it chose itself. A previous order (a
goto) is not an errand and still stands.

**Why.** "Busy tidying" in answer to "hide" was wrong: the errand was the buddy's idea. Only hiding
pre-empts today (`BusyForCommand(whileAlert: true, preemptErrand: true)`).

**Enforced in.** `BusyForCommand`, `StartHideNow`, `BeginHide`.

### a-fenced-sell-station-is-not-somewhere-to-stand

**Rule.** No reach task stands inside a sell station's fences (the `Fence*` colliders plus the item
zone, plus `SellStationClearance`), except that station's own sell legs. A buddy stuck inside a pen
for 3 s, failing to get closer to something outside it, is teleported clear by `EmergencyUnstick`,
which never lands in a pen.

**Why.** At the ShipyardStation the fences leave a 0.7 m pocket behind a 0.8 m rail. The buddy got
caught there in Follow. The stuck detector counts not moving, and sliding along a rail is moving, so
the pen joins the existing no-progress check. The fence colliders are never disabled: the player
would walk through them too.

**Enforced in.** `ReachTask.StandAllowed`, `SellTask.StandAllowed`, `SellPens.InAFencedPen`,
`PennedIn`, `UpdateGoalProgress`, `EmergencyUnstick`.

### a-carried-item-is-put-down-before-a-save

**Rule.** A held item is never parented to the buddy, and is put down (physics, `restrictGrab`,
colliders restored, `SavePosition`) on `SaveParser.OnFileSaveInitiated` and whenever carrying ends.

**Why.** Saved while held it would keep frozen physics, a mid-air position, and - if parented - a
parent name that does not exist in a vanilla load.

**Enforced in.** `BuddyHands.PickUp`, `OnGameSaving`, `BuddyHands.Drop` (from `DropReachTask`, `Die`, `ParkWithShip`,
`OnDestroy`).

### a-carried-item-belongs-to-the-room-its-carrier-is-in

**Rule.** A held item's parent is the `ContentParent` of the buddy's current room, re-owned through
`Grabbable.SetParent` whenever they differ, and always before `SavePosition`. An item already
inactive (`activeSelf` false) is left alone - a trash can or a sale destroyed it.

**Why.** `EntryDetector.CheckItemParents` skips items with `restrictGrab`, so a carried box kept its
old room, went inactive when that room was culled, and vanished from the buddy's hands.

**Enforced in.** `BuddyHands.ReownToCarrierRoom`, from `Follow` and `Release`.

---

## Game state

### the-buddy-rides-its-own-floor

**Rule.** The buddy is parented to the frame of the floor under it - a station's `contentParent`,
the world container, or the scene root for the player ship - and every remembered position (safe
spot, save sidecar) is stored in that frame. Owners are stored by GameObject name.

**Why.** The game moves the world, not the ship. An unparented buddy stayed behind on undock, fell
into "open space" and was teleported to the player. Parenting also parks the buddy for free: the
game deactivates `contentParent` when a station unloads.

Two special cases:
- **Undock:** a buddy in the undocking station's collar goes aboard instead of being parked in the
  airlock. Judged in the prefix, before the collar's colliders switch off.
- **Ship rebuild:** `SetShipLevel` moves some rooms 3.75 m. A buddy aboard moves with its room; one
  whose room is gone stays and is left to space protection. The plan is dropped, a goto ends, and
  the safe spot becomes the new position, or is forgotten when its floor is gone.

**Enforced in.** `UpdateOwnerAnchor`, `BuddyManager.FloorOwner`, `RememberSafePosition` /
`TryRecallSafePosition`, `BuddySaveFile.Owner` / `OwnerLocalPosition`, the `Docker.Undock` and
`StorymodeShipBuilder.SetShipLevel` patches, `RideShipRebuild`.

### an-unloaded-ship-parks-the-buddy

**Rule.** When the ship unloads for a spacewalk, every buddy aboard is parked (`SetActive(false)`)
before any room goes dark, and woken when the ship loads. A parked buddy keeps no room content
loaded.

**Why.** The buddy's room listener re-enabled the bridge, whose `Autopilot` trigger then unloaded the
wreck the player had gone out to - no collision outside.

**Enforced in.** `Patches.SpaceShip_SetContentEnabled_Prefix`, `ParkWithShip` / `UnparkFromShip`,
the `isActiveAndEnabled` guard in `OnForcedRoomContentChanged`.

### a-ship-node-rides-its-room

**Rule.** A ship node is anchored to the room whose floor it stands on (`Node.Anchor`, or `hull`),
with where that room stood (`AnchorAt`). It is live only while that room is built and moves with
it. A manual link records the room offset it was drawn in (`ManualLink.Offset`) and is live only in
that layout.

**Why.** The ship's rooms depend on its upgrade stage. The shipped graph is drawn at stage 5; at
stage 0 most of its ship nodes stand in rooms that do not exist. Links need their own offset
because the cockpit connects to the airlock differently at stages 0–2. The room comes from the
floor collider, never the tracked room. Nodes from an old file are anchored on first load.

**Enforced in.** `BuddyNodeGraph.OwnerSnapshot.IsLive` / `ShiftOf` / `WorldOf` / `LinkIsLive`,
`ToggleLink`, `SameOffset`, `TryAnchorShipNode`, `ShipLayoutSignature`.

### a-station-owns-its-interior-by-reference

**Rule.** A scene object belongs to its nearest `SpaceShip` or `SpaceObject` ancestor, or else to
the `SpaceObject` whose `contentParent` contains it. Always ask `BuddyNodeGraph.TryVesselOf`.

**Why.** Station interiors are root-level objects the station only references
([game-model.md](game-model.md#a-stations-interior-is-not-under-the-station)). An ancestry walk read
every station as `world`: the buddy did not park on undock, got no room, and never wandered.

**Enforced in.** `BuddyNodeGraph.TryVesselOf`, from `FloorOwner`, `OwnerOfTransform`,
`UpdateEnvironment`, `TryOwnerFromTransform`.

### aboard-is-answered-by-the-floor

**Rule.** "Is this aboard the player ship?" is answered by the floor under it, never a tracked
room. The answer is tri-state: no floor to judge by is not "not the ship".

**Why.** The game's room is the last doorway crossed and can be stale for minutes.

**Enforced in.** `BuddyManager.FloorOwner`; `IsAboardPlayerShip` delegates to it.

### buddy-closes-must-not-move-the-player

**Rule.** A door the buddy closes must not re-file the player's current room.

**Why.** `EntryDetector` re-evaluates the player's side on every door close, with no distance check.
A buddy closing a door across the ship moved the player's room, dropped their lifecare icon and
loaded or unloaded rooms around them.

**Enforced in.** `Patches.EntryDetector_DoorCheckForEnter_Prefix` / `_Postfix`, gated on
`GateWasClosedByBuddy` and `PlayerAtDoorwayRadius`.

### player-icon-fix-is-one-directional

**Rule.** The `UpdatePlayerIcon` prefix may only turn the icon **on**, and only when the floor under
the player belongs to the player ship.

**Why.** It fixes a game bug without taking over the display, so no scripted event can be masked.

**Enforced in.** `Patches.LifecareDisplay_UpdatePlayerIcon_Prefix`.

### mirror-the-vanilla-lifeform-clamp

**Rule.** `RecountLifeforms` reproduces the game's clamp, `(breathless || any temp) ? 1 : 0` plus
the player, then adds each buddy icon shown on that display.

**Why.** Summing separately over-reports during scripted events that use both.

**Enforced in.** `BuddyManager.RecountLifeforms`.

### mod-state-never-enters-the-vanilla-save

**Rule.** Mod state goes in the `.buddy` sidecar. Never attach a game `SaveObject<T>` to anything the
mod creates, and never write `SceneLoader.Instance.SaveData`.

**Why.** Uninstalling the mod must leave saves intact. That is why the corpse uses `BuddyCorpse`, not
the game's `Grabbable` (which allocates a save ID), and the cryo capsule is a prop, not a `CryoPod`.

**Enforced in.** `BuddyCorpse`, `BuddySaveFile`, `BuddyManager.WriteSidecar`, `BuddyCryoSpawn`.

### the-buddy-sells-only-trash-boxes

**Rule.** The buddy presses a sell button only when every `CanSell && Enabled` item in the zone is a
`Trash_Box`, the player is outside the catch zone, the gate is fully open, and every buddy stands
outside the zone. It presses with `Button.Interact(pilot)`, so the player is paid. Otherwise the
boxes stay loaded. A run presses once per load; the rule is checked right before each press.

**Why.** `SellStation.Sell` sells everything in the zone and kills a player standing inside. A body
under the gate trips its `AntiCrasher` and the sale silently fails.

**Enforced in.** `SellErrand.PressButton` (with `IErrandBody.AnotherBuddyWhere`) and `TryStart`,
`SellTask.StandAllowed`.

### a-sell-station-room-stays-loaded

**Rule.** While a buddy exists and `SellTrash` is on, a room whose content holds a `SellStation` is
never switched off, by the buddy or by the game. The buddy also switches one back on if it finds it
off (a save restores it that way).

**Why.** `FindObjectsOfType` skips a switched-off station, so the sell run found "no sell station
within 80m" of four boxes. The game switches YardHallway off at its doorways, on docking
(`Docker.Dock`) and on leaving by the Shipyard airlock (`ShipyardStation.DisableHallway`).

**Enforced in.** `Patches.SellRooms.Room_SetContentEnabled_Prefix`, `SellPens.HoldsSellStation`,
`BuddyBehaviour.ReasonToKeepLoaded`, `UpdateRoomTracking` (`sellRooms`).

### a-selling-run-keeps-its-boxes

**Rule.** The search radius and the vessel filter choose a run's boxes; they never end it. A queued
box leaves the run only when it is gone, taken, loaded, or has no way to it, and each exit is logged.
A box in an unloaded room is still queued, and the run's station is reloaded if its room goes off.

**Why.** After a load the buddy stands at the station. A fresh search from there cannot see boxes
left on the ship: they are on another vessel, and their room may be unloaded, so `FindObjectsOfType`
skips them. The run then ended after one box of four. And the Shipyard's station unloads with its
hallway as soon as the buddy leaves for the next box.

**Enforced in.** `SellErrand.ConfirmSale`, `TryNextBox`, `FetchBlocker`, `StationBlocker`; `Items.InUnloadedRoom`.

### reflection-lives-in-gameinternals

**Rule.** Every `GetField` / `GetMethod` into a game type lives in `GameInternals.cs`, resolved once
at plugin load (`ResolveAll`), with a one-time warning naming the feature it degrades. Fields declare
their type (`Field<T>`); a retyped field counts as missing. Harmony patches are applied per feature
group, never in one `PatchAll`.

**Why.** A game update renaming a member would otherwise fail silently deep in behaviour code, and
one missing patch target would take every other patch down with it.

**Enforced in.** `GameInternals`, `YourBuddyPlugin.ApplyPatches`.

---

## Several buddies

### buddies-never-block-each-other

**Rule.** Buddies walk through each other, alive or dead. Their controllers ignore each other's
controller, item blocker and ragdoll. Every `NavProbe` filter and the buddy's own body probes (whiskers,
auto-jump, stuck diagnostics, low ceiling) treat any buddy's body as a body.
An idle buddy in Follow or Wander standing within `BuddySpacing` of a lower-numbered one steps aside;
the lower one stays.

**Why.** The probes ignore bodies. A controller that still collided with one would walk into a buddy
its whiskers cannot see, and two buddies in one doorway would wedge each other. The player and the
buddy already pass through each other for the same reason. The body probes once skipped only the
buddy's own body and the player's, so a buddy ahead read as low furniture - shin blocked, head clear -
and the one behind jumped. Without the spacing, two buddies that stop
at the same spot stand inside each other; only one moves, or both would step apart forever.

**Enforced in.** `BuddyManager.IgnoreBodies` (from `OnEnable`, `Start` and `Die`),
`NavProbe.BelongsToABody` and `BuddyBehaviour.IsIgnorableCollider` via `BuddyManager.IsBuddyBody`,
`BuddyBehaviour.TrySpaceOut`.

### door-knowledge-is-shared

**Rule.** Which gates block routing, the detector, pin panel and airlock caches behind that answer,
and the door codes are static: one set for every buddy. `BuddyNodeGraph.SegmentBlockedByDoor` is bound
once, at plugin load. The caches reset on every scene load; the codes reset with their `GameManager`.

**Why.** Bound in each buddy's `Start` and cleared in its `OnDestroy`, the delegate belonged to one
buddy: despawning it switched door routing off, and the others planned through locked doors. Per-buddy
caches also repeated the same scene sweeps once per buddy.

**Enforced in.** `BuddyBehaviour.SegmentBlockedByDoor` / `GateBlocksRouting` / `Codes`,
`BuddyBehaviour.ResetWorldCaches` (from the `LoadGame` postfix and the `GameManager.Start` prefix),
`YourBuddyPlugin.Awake`.

### one-buddy-per-target

**Rule.** What another buddy's errand leg or hide is about - an item, a container, a sell station and
every box of its run, a life-support unit, a hiding spot - is not a candidate. The claim is read from
that buddy's live state (`ErrandLeg.Holds`, `hideSpot`), never stored.

**Why.** Two buddies raced for one box, and a second selling run at the same station would have its
boxes sold by the first run's press. A buddy opening a closet another hides in reads to the hider as
being found. A stored claim can outlive the errand that made it; a derived one cannot.

**Enforced in.** `BuddyManager.TakenByAnother`, `BuddyBehaviour.Holds`, `SellTask.Holds`,
`Errand.TakeBlocker`, `SnackBlocker`, `ContainerBlocker`, `SellErrand.TryStart` /
`NearestSellStation`, `LifeSupport.TryStartTerminal`, `HideSpotBlocker`.

---

## Frame cost

### read-only-panels-build-on-repaint

**Rule.** An IMGUI panel that only draws returns unless `Event.current.type` is `Repaint`, and costly
text is cached behind a TTL. A panel with controls (`BuddyDialog`) must run on every event - cache
its strings instead.

**Why.** `OnGUI` runs twice per frame. The status HUD rebuilt its text on both and was most of the
mod's allocation, causing a GC every few hundred ms.

**Enforced in.** `BuddyBehaviour.OnGUI`, `BuddyNodeEditor.OnGUI`, `BuddyBehaviour.StatusText`,
`BuddyNodeGraph.DescribeNodes`.

### unity-name-reads-allocate

**Rule.** Never read `Object.name` inside a per-element loop that runs every frame. Resolve names
once and cache the map, keyed on something that actually changes.

**Why.** Each `name` read allocates a new string. Room and owner lookups by name were the mod's
largest allocation after the HUD. `SpaceShip.rooms` is a fixed array, so its reference is the key.

**Enforced in.** `BuddyNodeGraph.ShipRoom` (`RoomsByName`), `RefreshSpaceObjects` /
`FindOwnerObject` (`SpaceObjectNames`).

### scene-sweeps-are-budgeted

**Rule.** A cache that re-runs `FindObjectsOfType` on a timer asks `SceneScan.MayRescan` first. A
sweep made for one decision reads `SceneScan.ThisFrame`. Only a first fill or a forced rescan
(`InvalidateGates`, a destroyed entry) skips the budget.

**Why.** Each sweep costs milliseconds. Expired 5 s caches could all rescan inside one `FindPath`
call, and an errand's `Count` and `TryStart` swept the scene twice in one frame. Both landed on the
mod's slowest frames.

**Enforced in.** `SceneScan`; `NavProbe.EnsureGates`, `BuddyNodeGraph.RefreshSpaceObjects`,
`RefreshDetectors` / `RefreshAirlocks` / `RefreshEnvironments`, `PinPanelFor`,
`TrackFootstepDoors`, `SellPens.EnsurePens`; the errand collectors.

# Troubleshooting - from a symptom to the log line

Where to look first when the buddy misbehaves. Each row names the log line that tells the cases apart
and the doc that explains it. Capture at `debug_level 2` and trim first
([logging.md §3](logging.md#3-capture-workflow)); a line's tag (`[ai]`, `[nav]`, …) says which
subsystem wrote it ([logging.md §2](logging.md#2-subsystem-tags)).

Search the capture with `trimlog.py capture.txt --stats`: the line shape that dominates is
often the answer.

---

## Stuck or not moving

| Log line | Means | Read |
|---|---|---|
| `[ai] No route that avoids a door I cannot open - waiting here` | every route needs a door it can't open; standing still is intended | [stand-still-when-door-blocked](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#stand-still-when-door-blocked) |
| `[fear] Alert: not walking toward the Breathless …m away - holding (…)` | fear is holding it back, not navigation | [fear.md §4](fear.md#4-alert---holding-back) |
| `[nav] FindPath failed: no node reachable from the buddy` | no node in range and in sight - missing nodes, or the buddy is off the graph | [navigation.md §7](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/navigation.md#7-reading-a-findpath-capture) |
| `[nav] FindPath: no clear entry seed, falling back to nearest active node` repeated | it keeps entering the route somewhere it can't walk to | [entry-seeds-on-own-deck](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#entry-seeds-on-own-deck) |
| `[ai] Stuck near … while moving to …` with `[ai] Blocked by '…'` nearby | a collider is in the way; the `Blocked by` line names it | [navigation.md §5](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/navigation.md#recovery-ladder) |
| `[ai] Stuck but nothing blocking ahead - AI logic issue` | it wants to move and nothing blocks it - a planning bug, worth a report | [navigation.md §5](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/navigation.md#recovery-ladder) |
| `[ai] Blocked by '…Walls/…'` beside a doorway, then `[ai] Gave up on the route to …` | the plan's leg crossed the wall next to the door, not the door | [a-doorway-is-crossed-not-grazed](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#a-doorway-is-crossed-not-grazed) |
| `[ai] Skipping waypoint N/M at …` more than once on one plan | recovery is eating the plan; the route will end as a failure, not an arrival | [a-skipped-waypoint-is-not-an-arrival](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#a-skipped-waypoint-is-not-an-arrival) |
| `[ai] Cannot reach the player right now, stepping off` repeating | the step-off keeps firing without a new route | [step-off-applies-in-every-mode](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#step-off-applies-in-every-mode) |
| nothing at all while it stands on furniture | a perched stall | [idle-above-the-floor-plane-is-a-stall](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#idle-above-the-floor-plane-is-a-stall) |

A position that never changes across replans is a livelock. One that swings back and forth is a
ping-pong between two plans ([logging.md §3](logging.md#3-capture-workflow)).

## Stairs

| Log line | Means | Read |
|---|---|---|
| `[ai] Left the stair leg to …: feet … ` | it fell or stepped off the flight and replanned | [off-the-flight-is-off-the-plan](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#off-the-flight-is-off-the-plan) |
| `[ai] Walking the stair leg … -> …` then no progress | it is on the leg but can't climb it | [navigation.md §6a](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/navigation.md#6a-stair-legs) |
| no stair leg in the route chain at all | no link joins the two decks - a graph problem, fix it in the F8 editor | [navigation.md §1](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/navigation.md#links) |

The `OxygenStation` stairwell has its own notes: [navigation.md §8](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/navigation.md#8-the-oxygenstation-stairwell).

## Doors

| Log line | Means | Read |
|---|---|---|
| `[ai] Waiting to close '…': '…' (layer '…') is in the doorway` | something named in the line keeps the door open | [NPC.Core's doors.md §9](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/doors.md#9-what-a-healthy-capture-looks-like) |
| `[ai] Closing door '…' behind itself (attempt N)` with N climbing | the close keeps failing and re-arming | [fail-close-must-not-rearm](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#fail-close-must-not-rearm) |
| `[ai] Opening door` / `Closing door` alternating on one gate | an open–close loop; check who opened it - the buddy never closes a door it did not open | [NPC.Core's doors.md §4](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/doors.md#4-closes-an-npc-blocked) |
| `[ai] Timed out waiting for a door, moving on` | a door never opened in time | [NPC.Core's doors.md §6](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/doors.md#6-opening) |

## Where the buddy is

| Symptom | Log line | Read |
|---|---|---|
| HUD's `Pos:` line ends `on world` while the buddy is aboard | `[ai] Riding 'world' now` - the floor under it belongs to no ship or station, so it rides the world container and can be carried off on undock | [an-npc-rides-its-own-floor](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#an-npc-rides-its-own-floor) |
| buddy missing after undock | `[ai] Undocked with nothing underfoot - moved to the ship airlock` | [game-model.md](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/game-model.md#undocking-does-not-unload-a-station) |
| no buddy on a new game | `[mgr] New game: …, … - no buddy. Use 'spawn_buddy'.` | [reference.md §2](reference.md#2-debug-commands) |

## Lifecare scanner

| Log line | Means | Read |
|---|---|---|
| `[lifecare] Lifecare scan finished: aboard=False` with the buddy aboard | a floor-probe problem | [lifecare.md](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/lifecare.md#reading-a-capture) |
| `aboard=True` but no buddy icon | a problem cloning the icon | [lifecare.md](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/lifecare.md#reading-a-capture) |
| `[lifecare] Lifecare icon state: … playerIcon=False` | the game's own player-icon bug | [lifecare.md §3](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/lifecare.md#3-the-missing-player-icon-game-bug-worked-around) |

## Orders and fear

| Symptom | Log line | Read |
|---|---|---|
| it stops obeying an order | `[mind] Order '…' expired after …s - deciding for myself again` | [behaviour.md §6](behaviour.md#6-reading-a-capture) |
| it picks odd tasks | the decider's lines under `[mind]` | [behaviour.md §6](behaviour.md#6-reading-a-capture) |
| it freezes facing the monster | `[fear] Nowhere left to run from the Breathless …` (stalemate) | [fear.md §5](fear.md#stalemate---nowhere-left-to-run) |
| it runs the wrong way or not at all | `[fear] No retreat plan: …` | [fear.md §8](fear.md#8-reading-a-capture) |

Nothing here? Check [known-issues.md](known-issues.md), then open an issue with the capture
([CONTRIBUTING.md](../CONTRIBUTING.md#reporting-a-bug)).

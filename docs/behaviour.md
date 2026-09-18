# Orders and independent behaviour

`BuddyBehaviour.Autonomy.cs` (orders, stand-down), `BuddyBehaviour.Mind.cs` (the decider, §3) and
`BuddyCommands.cs`. Config (General): `Autonomy` (default on), `OrderPersistence` (default
`UntilRevoked`), `OrderExpirySeconds` (90).

---

## 1. An order is not a mode

Two fields, two questions ([an-order-is-not-a-mode](invariants.md#an-order-is-not-a-mode)):

| Field | Answers | Written by |
|---|---|---|
| `orderedMode`, `orderedAt` | what the player told it, and when; null = no order | `ApplyOrder`, `ApplyRouteOrder`, `RevokeOrder` (only via `BuddyCommands`), plus the endings below |
| `mode` | what it is doing | orders, the decider, fear, `FinishRoute`, a ship rebuild, `Die` |

Console and dialog both go through `BuddyCommands`. `SetMode` and `StartRoute` are private.

An order ends when it is:

- **revoked** - "decide for yourself" or `buddy_auto on`;
- **expired** - `OrderPersistence = Expires`, after `OrderExpirySeconds`, only while autonomy is on;
- **a goto that arrived** (`FinishRoute`), or one a flee interrupted and that can no longer be
  planned (`EndFlee`);
- **a goto during a ship rebuild** - its goal belongs to the old layout
  ([the-buddy-rides-its-own-floor](invariants.md#the-buddy-rides-its-own-floor)).

Otherwise a goto always runs to completion; revoking one lets it finish first.

An order given during a flee is recorded at once but only replaces what the flee will resume
([fear-owns-the-buddy](invariants.md#fear-owns-the-buddy)). The reply says so.

Neither order nor mode is saved. A loaded buddy starts in Follow with no order.

---

## 2. Persistence

| `OrderPersistence` | An order holds | Default |
|---|---|---|
| `UntilRevoked` | until another order, or "decide for yourself" | **yes** |
| `Expires` | `OrderExpirySeconds` (at least 5 s), then the decider takes over | |

`Autonomy = false`: the buddy never decides; it keeps its last order, or Follow. `buddy_auto on`
also revokes the current order, or switching it on would visibly do nothing.

---

## 3. The decider

`UpdateAutonomy` owns stand-down and order bookkeeping; `BuddyBehaviour.Mind.cs` decides **what to
do**. Every `DecideInterval` it scores competing **urges** and acts on one.

It **stands down** (logged at level 2 every 15 s) while:

| Reason | Why |
|---|---|
| an order is in force | that is what an order means |
| being caught, or talked to | `Update` holds the buddy still anyway |
| fear above Calm, or fleeing | [fear-owns-the-buddy](invariants.md#fear-owns-the-buddy) |
| walking a route | a goto, a resumed route, or an errand in progress |
| hiding | hiding owns the buddy ([fear.md §6](fear.md#6-hiding-in-a-closet-or-locker)) |
| the player is on a spacewalk | Follow already waits inside |

### Why utility scoring

The earlier fixed priority chain (`terminal || snack || sell || tidy || play`, then Follow/Wander)
always ran in the same order with hard thresholds, and felt random rather than thoughtful.

| Option | Verdict |
|---|---|
| Behaviour tree | replaces the execution layer, which already works (`SellPhase`, `TidyPhase`, `PlayPhase`, `HideState`, `FleePhase`); node order is just as fixed |
| GOAP / HTN | pays off when actions chain into plans; the buddy's errands are single steps |
| **Utility AI** | replaces only the priority chain; nothing below it changes |

So: utility scoring for *choosing*, and every task state machine unchanged.

### The score

```
Score = Weight × Need × Opportunity × Readiness × Novelty × jitter
```

Any factor at zero vetoes; anything under `UrgeFloor` is not worth doing.

| Urge | Weight | Need | Opportunity | Readiness |
|---|---|---|---|---|
| `Terminal` | 1.00 | air in a band a unit could fix (`LifeSupport.AirIsDangerous`) | - | - |
| `Sell` | 0.75 | `0.4 + 0.2 ×` boxes | nearest box | since last look / `SellCheckInterval` |
| `Tidy` | 0.65 | `0.5 + 0.25 ×` (pieces − 1) | nearest piece | since last round / `TidyIntervalMinutes` |
| `Snack` | 0.55 | 1 | nearest food | since last snack / `SnackIntervalMinutes` |
| `Play` | 0.35 | 1 | nearest plaything | since last session / `ItemPlayIntervalMinutes` |
| `Wander` | 0.30 | 1 | an active node owner within `DecideNodeOwnerRadius` | Follow bout so far |
| `Follow` | 0.30 | rises with distance to the player; 1 off their deck | the player reachable | Wander bout so far |

- **Readiness is a ramp, not a gate:** `1 − (dueAt − now) / interval`, clamped. A task can run a
  little early when nothing else wants the buddy, or late when better things keep winning.
- **Opportunity is distance:** `1 / (1 + dist / UrgeRangeSoftness)` - 1 at the feet, ½ at 8 m, ¼ at
  24 m. It never reaches zero, so distance loses arguments but never deletes candidates.
- **Novelty:** the last urge acted on is worth `1 − UrgeRepeatPenalty`.
- **Jitter:** `± UrgeNoise` on every score.

### Choosing

```
best ≥ UrgeDecisive  →  take it outright
otherwise            →  draw one of the best UrgePickTop, weighted by score^UrgeSharpness
```

Only `Terminal` reaches 1, so bad air is never left to chance. Everything else lands around 0.1–0.4
and is **sampled**, so the same situation can lead to different choices.

An urge that turns out impossible (no route, no usable station) is dropped and the draw repeated, up
to three times per round.

### Keeping the scan cheap

Collectors sweep every `Grabbable` in the scene, so:

1. **Readiness floor** - an urge below `UrgeScanFloor` is not looked for at all.
2. **Budget** - at most `UrgeScansPerDecision` collectors per round; first claim rotates
   (`urgeScanCursor`) so none starves.
3. **Cache** - results stand for `UrgeOpportunityTtl`.

Collectors share static buffers (`PutAwayItems`, `CheckedContainers`, `ContainedItems`), so only one
runs at a time and its summary is taken before the next. The winner's `StartX` collects again - one
extra sweep per decision, so no task body had to change.

### Bouts

Follow and Wander take turns, each for a **bout** of random length:

| Mode | Length | Clock starts |
|---|---|---|
| Follow | `FollowBoutMin`..`Max` (20–45 s) | once caught up (within 4 m, on the player's deck), or after 60 s of trying |
| Wander | `WanderBoutMin`..`Max` (40–90 s) | at once |

The other mode is not offered until `BoutReadyFrom` (70 %) of the bout has run, then ramps to full
at the end. With nothing else on offer the switch lands around 75–80 % of the bout. Any stand-down
clears the bout.

### Which owner a wander stays on

The owner of the nearest active node - never `CurrentOwner`. A wander needs nodes to walk to, and
only the graph knows where they are.

### Range

Search radii are a cheap broad phase. What keeps them safe is an **owner filter**: a candidate must
be on the vessel the buddy is riding (`OnMyVessel`, `BuddyBehaviour.Carry.cs`). It checks the
object's ancestry first (`BuddyManager.OwnerOfTransform`) and probes the floor only if needed. A
non-answer allows the candidate
([a-missing-floor-is-a-last-resort-not-an-answer](invariants.md#a-missing-floor-is-a-last-resort-not-an-answer)).

Without the filter a 30 m radius reaches across a docking collar and the buddy leaves the ship for a
wrapper. Change radius and filter together.

Within a task, the nearest three candidates are shuffled (`ShuffleNearest`, `PickNearest`).

### Watching and nudging it

The HUD's `Mind`, `Why`, `Air`, `Snack`, `Tidy`, `Sell` and `Play` lines (also `buddy_mind`):

```
Mind: Follow 12s of 34, Wander weighed from 24s; next look in 2s
Why: tidying up 0.41, playing 0.22, following you 0.19
Mind: standing down - order 'Stay' in force
Air: O2 21.0%, 22.0C - fine; climate control retry in 40s
Snack: in 812s (last: ate 'FoodPack (1)' from the fridge (2 left))
```

Test commands (each says why when it refuses; all work under an order and return to it after):

| Command | Does |
|---|---|
| `buddy_bout` | ends the current bout; the other mode weighs full at the next look |
| `buddy_terminal <oxygen\|climate>` | switch that unit on now, if [allowed](invariants.md#a-terminal-is-only-switched-on) |
| `buddy_snack` | a snack now ([snacks.md](snacks.md)) |
| `buddy_tidy` | a tidying round now ([items.md §3](items.md#3-tidying)) |
| `buddy_sell` | a selling run now ([items.md §4](items.md#4-selling-trash-boxes)) |
| `buddy_play` | a play session now ([items.md §5](items.md#5-idle-play)) |
| `buddy_hide` | hide now, until the next order ([fear.md §6](fear.md#6-hiding-in-a-closet-or-locker)) |

A parked buddy (undocked station, or aboard during a spacewalk) is inactive, so nothing here runs.

---

## 4. Every place that reads `mode` outside the dispatch switch

Check a new mode against every row.

| Site | What it does | `Flee` |
|---|---|---|
| `Navigation.cs` `UpdateIdleRecovery` | skips `Stay` and `Dead` | not skipped |
| `Obstacles.cs` `UpdateStuckDetection` | stuck recovery on `Route` / `Wander` | `Retreat` with a plan: re-plan |
| `Obstacles.cs` `UpdateGoalProgress` | no-progress recovery on `Wander` / `Route` / `Follow` | `Retreat`: re-plan; `ToPlayer`: Follow's branch |
| `Presentation.cs` `UpdateAnimation` | idle facing: the monster above Calm, else the player in Follow | watches the monster |
| `Doors.cs` `StepOutOfDoorway` | skipped in `Route` | allowed |
| `Navigation.cs` `HasRoute()` | `Route` only | - |
| `Fear.cs` `HoldBackFromMonster` | exempts `Flee` in `Retreat` | the retreat was vetted |
| `Fear.cs` `UpdateFear`, `ResetFear`, `TraceFear`, `DescribeFear` | fear's own mode handling | owner |
| `Autonomy.cs` `ApplyOrder`, `ApplyRouteOrder` | mid-flee, replace `modeBeforeFlee` | defers |
| `Autonomy.cs` `StandDownReason`, `GotoUnderway`, `UpdateAutonomy` | stand down on `Flee` / `Route`; branch on `Wander` / `Follow` | stands down |
| `Mind.cs` `ScoreCompany`, `ActOn` | which of Follow / Wander is weighed | not reached |
| `Environment.cs` `RideShipRebuild` | a rebuild turns `Route` into `Follow` | same for `modeBeforeFlee` |
| `StatusText`, `ReportObstacles` | print it | - |

Writers of `mode`: the initialiser, `SetMode` (from `ApplyOrder`, `Decide`, `StartFlee`, `EndFlee`,
`RideShipRebuild`), `StartRoute`, `FinishRoute`, `Die`. `SetMode` does **not** clear
`followStepOffUntil`, `wanderIdleUntil`, `unreachableWaypointUntil`, `followWaitUntil` or
`sidestepUntil`; `StartFlee` clears the step-off and Follow wait itself.

---

## 5. Giving orders

| Say | Console | Effect |
|---|---|---|
| decide / yourself / your call | - | `BuddyCommands.DecideForYourself` → `RevokeOrder`; refuses while `Autonomy` is off |
| - | `buddy_auto [on\|off]` | sets `Autonomy`; `on` also revokes |

"Decide" is matched **before** Follow, so "decide for yourself whether to follow" is not a follow
order. Other words: [dialog.md §3](dialog.md#3-the-orders).

---

## 6. Reading a capture

Tag `[mind]`. Choices, decisions, revocations and expiries at level 1; score tables, stand-down
reasons and empty searches at level 2 (every 15 s).

```
[mind] Chose tidying up (0.41) - 3 within 6.1m - over playing 0.22, following you 0.19
[mind] Decided: tidy up 'FoodCereal' - lying about, 3.4m away, via node (1.2, 0.0, 4.5), then to the trash can at (2.0, 0.6, 7.1)
[mind] Chose life support (1.00) - the air aboard is dangerous - over a snack 0.31
[mind] Chose wandering off (0.28) - followed you 31s of 34, nodes of 'OxygenStation' here
[mind] Decided: Follow - wandered 68s, coming back to you
[mind] Decider standing down: fear is Alert                                        <- level 2
[mind] Wander not weighed: catching up, 23.4m away, 12s of 60                      <- level 2
[mind] Thought of tidying up, but there is no trash within 30m - trying again in 120s   <- level 2
[mind] Order 'Stay' expired after 90s - deciding for myself again
[mind] Order 'Follow' revoked - deciding for myself
[ai] Route finished (the goto order is done), switching to Follow mode
```

A `Chose` with no `Decided:` after it, then another `Chose`, is an impossible urge being redrawn; the
level-2 line between says why.

HUD `Orders:` shows `none - deciding for itself`, `none (autonomy off)`, `Stay, until revoked`,
`Follow, 42s left`, or `Goto, until it arrives`.

---

## 7. Known limitations

- **Autonomous Wander stays on the owner it started on**, and is not chosen with no active node
  within `DecideNodeOwnerRadius`. Near a docking collar the nearest node may be the other vessel's.
- **Nothing is chosen during a spacewalk.**
- **A running task is not interrupted** by a better opportunity; the decider stands down until it
  ends.
- **Opportunity uses straight-line distance**, not walk length. The plan attempt finds out the rest.

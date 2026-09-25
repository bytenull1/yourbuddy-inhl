# Reference - constants, commands, editor

This file explains what each constant *means*. The code holds the actual number; if they disagree,
the code wins - fix this file. Player-facing settings and the full command list are in the
[README](../README.md#configuration).

---

## 1. Tuning constants

Before changing any height threshold, read
[same-level-tolerance-ceiling](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#same-level-tolerance-ceiling).

### Following - `BuddyBehaviour.cs`

Walking, doors, wandering, reach, carrying, spacing, air and footsteps are NPC.Core's agent
([its reference](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/reference.md#1-tuning-constants)). The brain adds:

| Constant | Value | Meaning |
|---|---|---|
| `FollowStartDistance` / `FollowStopDistance` | 2.0 / 1.8 m | Follow hysteresis (XZ) |

### Fear - `BuddyBehaviour.cs`

Rates are per second; resulting timings in [fear.md §3](fear.md#3-stress).

| Constant | Value | Meaning |
|---|---|---|
| `FearSightRange` | 18 m | nothing further is checked |
| `FearPanicDist` | 2.5 m | this close in a clear line: Scared at once, from any side |
| `FearViewCos` | 0.42 | Calm: noticed only within 65° of facing |
| `FearStressRate` | 0.15 | base stress rate at the edge of sight |
| `FearProximityGain` | 2.5 | rate × up to `1 + this` as it closes to `FearPanicDist` |
| `FearApproachGain` / `FearApproachSpeed` | 0.5 / 2 m/s | rate × up to `1 + this` while it approaches |
| `FearDecayRate` | 0.4 | decay out of sight |
| `FearStressCap` | 5 | ceiling; a panic sets it |
| `FearAlertEnter` / `FearAlertExit` | 1 / 0.5 | Alert hysteresis |
| `FearScaredEnter` | 3 | Scared enters here, leaves below `FearAlertEnter` |
| `FearEyeHeight` | 1.5 m | eye height above the feet |
| `FearRestraintDist` / `FearRestraintCos` | 10 m / 0.5 | no step within 60° of the monster while this near |

### Flee - `BuddyBehaviour.cs`

| Constant | Value | Meaning |
|---|---|---|
| `FleeClearance` | 3 m | a retreat leg may not come back within this of the monster |
| `FleeMinGain` | 4 m | a retreat node must be this much further from the monster |
| `FleeSafeDist` | 15 m | beyond this, further is no safer |
| `FleePlayerWeight` / `FleeTripWeight` | 0.5 / 0.1 | score cost per metre to the player / per metre of trip |
| `FleePickAttempts` | 5 | retreat nodes planned per attempt |
| `FleeSpeedFactor` | 1.15 | speed multiplier while fleeing |
| `FleeRetryDelay` / `FleeHoldRetryDelay` | 0.5 / 1.5 s | re-plan after a dropped retreat / while holding |
| `FleeBackAwayDist` / `FleeBackAwayTime` | 3 m / 1 s | the back-away step |
| `FleeStalemateSeconds` | 6 s | nowhere to run this long → stalemate |
| `FearStalemateStress` | 0.75 | stress eases to this (Alert) while still in sight |
| `FleeStalemateClosed` | 2 m | the monster closing this much breaks the stalemate |
| `HideSeenFactor` / `HideMonsterClose` | 0.35 / 6 m | hide-or-run draw ([fear.md §5](fear.md#hiding-or-running)) |
| `HideNoRetreatBias` | 0.3 | added when the last retreat found nowhere to run |

### Orders and the decider - `BuddyBehaviour.cs`

See [behaviour.md §3](behaviour.md#3-the-decider).

| Constant | Value | Meaning |
|---|---|---|
| `DecideInterval` | 3 s | how often the decider may change its mind |
| `FollowBoutMin` / `FollowBoutMax` | 20 / 45 s | Follow bout, counted from catching up |
| `WanderBoutMin` / `WanderBoutMax` | 40 / 90 s | Wander bout |
| `BoutReadyFrom` | 0.7 | the other mode is offered after this share of the bout |
| `DecideCaughtUpDist` | 4 m | this near on your deck starts the Follow clock… |
| `DecideCatchUpSeconds` | 60 s | …or this long trying |
| `DecideNodeOwnerRadius` | 30 m | wander stays on the owner of the nearest node within this |
| `UrgeRangeSoftness` | 8 m | distance at which opportunity halves |
| `UrgeDecisive` | 0.75 | at or above: taken outright. Only bad air reaches it |
| `UrgePickTop` / `UrgeSharpness` | 3 / 2 | otherwise one of the best three, weighted by score² |
| `UrgeFloor` | 0.05 | below this, not worth doing |
| `UrgeRepeatPenalty` / `UrgeNoise` | 0.35 / ±15 % | last urge is worth less; every score jittered |
| `UrgeScanFloor` | 0.5 | an urge less ready than this is not searched for |
| `UrgeScansPerDecision` | 2 | collectors run per round |
| `UrgeOpportunityTtl` | 10 s | how long a search result stands |
| `PickNearest` | 3 | nearest candidates shuffled between |

### Life-support terminals - `LifeSupport.cs`

| Constant | Value | Meaning |
|---|---|---|
| `OxygenDangerBelow` | 1950 | oxygen (1/100 %) below which it acts |
| `ColdDangerBelow` / `HeatDangerAbove` | 1400 / 3200 | temperature (1/100 °C) bounds |
| `TerminalRetryDelay` | 60 s | per unit, after a flip |
| `TerminalVerifyDelay` | 1.5 s | then it checks the unit runs |

### Snacks - `SnackErrand.cs`

The item-reach constants every errand shares (`SnackStandOffs`, `SnackReachDist`, `SnackReachBelow`,
`SnackItemMovedDist`, `SnackOpenSeconds`, `SnackContainerMargin`, `SnackIntervalJitter`,
`ContainerSearchDepth`) live in `Items.cs`.

| Constant | Value | Meaning |
|---|---|---|
| `SnackIntervalJitter` / `SnackMinInterval` | ±25 % / 60 s | around `SnackIntervalMinutes`, and the minimum |
| `SnackRetryDelay` | 120 s | after anything but eating |
| `SnackSkipSeconds` | 600 s | an unreachable container or item is skipped this long |
| `SnackSearchRadius` / `SnackContainerMargin` | 25 m / 2 m | search radius; containers this much further still count for "loose" |
| `SnackMaxPlans` | 3 | candidates tried with a plan |
| `SnackStandOffs` / `SnackReachDist` | 0.55 / 0.7 / 0.85 m / 0.9 m | up-close stand points and reach |
| `SnackReachBelow` | 0.3 m | how far below the floor probe an item's top may be |
| `SnackItemMovedDist` | 0.5 m | loose food moved further ends the walk |
| `SnackPickSeconds` | 0.8 s | loose food: before eating |
| `ContainerSearchDepth` | 2 | parents up from a `Door` to its container |
| `SnackOpenSeconds` / `SnackEatSeconds` | 1.5 / 2 s | looking in; after eating |
| `PlayerHungryAtOrBelow` | 250 | the player's Hunger band |

### Tidying - `TidyErrand.cs`

Walking up to an item uses the `Snack*` reach constants. `TidyReachSeconds`, `TidyLiftSeconds` and
`TidyAimSeconds` are shared by selling and play, and live in `Items.cs`.

| Constant | Value | Meaning |
|---|---|---|
| `TidyMinInterval` / `TidyRetryDelay` | 60 s / 120 s | minimum interval; retry after anything but a finished round |
| `TidySkipSeconds` | 600 s | an item or can it could not use |
| `TidySearchRadius` / `TidyBinRadius` | 30 m / 40 m | trash from the buddy; a can from the trash |
| `TidyRoundMin` / `TidyRoundMax` / `TidyRoundSeconds` | 2 / 5 / 180 s | pieces per round, and the round's time limit |
| `TidyMaxPlans` | 3 | items tried with a plan |
| `TidyReachSeconds` / `TidyLiftSeconds` / `TidyAimSeconds` | 0.5 / 0.5 / 0.4 s | before pick-up; after, if the can is in reach; before the slot |
| `TidyInsertSeconds` / `TidyMaxInserts` | 1.5 s / 2 | time in the slot; attempts |

### Selling - `SellErrand.cs`

See [items.md §4](items.md#4-selling-trash-boxes). `SellStationClearance` and `SellPenRefresh` are
NPC.Core's ([its reference](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/reference.md#sell-pens---sellpenscs)).

| Constant | Value | Meaning |
|---|---|---|
| `SellCheckInterval` | 60 s | between looks, and after a sale |
| `SellSkipSeconds` | 600 s | a box or station with no clear way to it |
| `SellRetrySeconds` | 90 s | one that failed this time only |
| `SellSearchRadius` / `SellStationRadius` | 30 m / 80 m | boxes from the buddy when a run is chosen; a station from the box |
| `SellMaxBoxes` / `SellZoneMaxItems` | 4 / 4 | boxes per press; most items the zone may hold |
| `SellMaxBoxesTransit` | 2 | boxes per press at the Oxygen, Solar and Fuel stations |
| `SellSlotMargin` / `SellSlotGap` / `SellDropHeight` | 0.02 / 0.02 / 0.03 m | box slots in the zone: from its walls, from each other, above the floor or box below |
| `SellMaxPlans` | 3 | boxes tried with a plan |
| `SellLoadStandOffs` / `SellLoadReach` | 1.35 / 1.6 m / 1.8 m | loading, from the zone centre |
| `SellLoadSeconds` / `SellSettleSeconds` | 1.5 s / 1 s | held in the zone until detected; settling |
| `SellWaitSeconds` | 20 s | for the gate to open, or you to step out |
| `SellReopenedAfter` / `SellConfirmSeconds` | 1.5 s / 10 s | gate open again after this = failed; sale must happen within this |

### Idle play - `PlayErrand.cs`

See [items.md §5](items.md#5-idle-play). Walking up uses the `Snack*` reach constants.

| Constant | Value | Meaning |
|---|---|---|
| `PlaySearchRadius` / `PlayMaxPlans` | 20 m / 3 | search radius; items tried |
| `PlayGamesMin` / `PlayGamesMax` | 1 / 3 | games per session |
| `PlayCarryNearWeight` / `PlayCarryFarWeight` / `PlayThrowWeight` | 3 / 1 / 3 | game draw weights |
| `PlayMinInterval` / `PlayRetryDelay` / `PlaySkipSeconds` | 60 / 120 / 600 s | minimum interval; retry; unreachable item skip |
| `PlayCarryMin` / `PlayCarryMax` | 3 m / 6 m | near carry distance, in sight |
| `PlayFarMin` / `PlayFarMax` | 8 m / 25 m | far carry distance, no sight needed |
| `PlayDropOut` / `PlayDropClear` / `PlayDropFlat` | 0.6 / 0.9 / 0.1 m | spot ahead of its node; clear distance; floor tolerance |
| `PlayMachineClearance` | 1 m | keep-away from item zones and trash-can slots |
| `PlayLiftSeconds` / `PlayAimSeconds` / `PlayAimAngle` | 0.5 / 1 s / 20° | hold before a throw; turn time; aim tolerance |
| `PlayThrowSpeedMin` / `PlayThrowSpeedMax` / `PlayThrowUpSpeed` | 5 / 9 / 1.6 m/s | throw speed and upward tilt |
| `PlayThrowClear` / `PlayThrowDirections` | 2.5 m / 8 | sweep length; directions tried |
| `PlayThrowPlayerAngle` / `PlayThrowPlayerDist` | 35° / 8 m | never thrown toward you this close |
| `PlayWatchSeconds` | 2 s | watching after a throw |
| `PlayLowerSpeed` / `PlayLowerArrival` / `PlayLowerSeconds` | 0.8 m/s / 3 cm / 2 s | lowering an item into place |

### Hiding - `BuddyBehaviour.cs`

See [fear.md §6](fear.md#6-hiding-in-a-closet-or-locker). Walking up uses the `Snack*` reach constants.

| Constant | Value | Meaning |
|---|---|---|
| `HideSearchRadius` / `HideMaxPlans` | 14 m / 3 | search radius; spots tried |
| `HidePreferWalk` | 16 m | a longer walk isn't worth it; it runs instead |
| `HideMonsterClearance` | 3 m | never a spot this near the monster |
| `HideWalkSeconds` / `HideWalkSecondsPerMetre` | 8 s / 1.2 s/m | walk time limit |
| `HideMinSeconds` / `HideCalmSeconds` | 25 / 15 s | frightened hide: minimum inside; calm and unseen this long before leaving |
| `HideMonsterNearDist` | 5 m | frightened hide never ends with the monster this near. The number to tune |
| `HideMaxSeconds` | 240 s | ceiling on that wait |
| `HideDoorShutSeconds` | 6 s | grace for the doors' close animation |
| `HideWaitLogSeconds` | 15 s | between "still in here" lines |
| `HideSkipSeconds` | 600 s | a spot it could not use |

### Spawning several - `BuddyConsole.cs`

| Constant | Value | Meaning |
|---|---|---|
| `SpawnSpacing` | 0.8 m | gap between buddies in `spawn_buddy N`'s row |
| `SpawnSameDeck` | 0.5 m | a row point is used only on the middle point's deck, else it falls back to the middle |

---

## 2. Debug commands

The graph, gate and editor commands, `debug_level`, `ai_disable` and `ai_notarget` are NPC.Core's
([its reference](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/reference.md#2-console-commands)); `buddy_node` and `buddy_gates`
are kept as names for `npc_node` and `npc_gates`, and `ai_disable buddy` still means `ai_disable npc`.

The full command list is in the [README](../README.md#console-commands-reference). Commands live in
`BuddyConsole.cs`; order bodies live in `BuddyCommands.cs` so the [dialog](dialog.md) shares them.
`buddy_goto` calls `FindPath` then `ApplyRouteOrder`, so `NavPath` changes must update it.

**Which buddy.** A per-buddy command takes `@2` (a number), `@buddy2` (a name, case and spaces
ignored) or `@all` anywhere among its arguments (`BuddyConsole.ForTargets`). Without one it goes to
`BuddyManager.Focus`: the buddy last talked to or named, else the nearest living one. Naming exactly
one moves the focus, which is also the buddy the HUD shows. `buddy_list` lists numbers and names and
marks the focus. `spawn_buddy N` replaces every buddy with N.

**`ai_notarget` leaves the buddy huntable.** Its catch is the agent's own `BreathlessCheck`, which only
`ai_disable` stops; `CatchRoutine` restores the aggressor flag it borrowed only while
`NpcMonster.PlayerIgnored` is false, so a catch never re-arms a monster the player switched off
([the-ai-overrides-have-one-owner](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#the-ai-overrides-have-one-owner)).

# Reference - constants, commands, editor

This file explains what each constant *means*. The code holds the actual number; if they disagree,
the code wins - fix this file. Player-facing settings and the full command list are in the
[README](../README.md#configuration).

---

## 1. Tuning constants

Before changing any height threshold, read
[same-level-tolerance-ceiling](invariants.md#same-level-tolerance-ceiling).

### Pathfinding - `BuddyNodeGraph.cs`

| Constant | Value | Meaning |
|---|---|---|
| `MaxSeedDist` | 60 m | how far a node may be and still be a route **entry** |
| `MaxGoalFinishDist` | 6 m | longest final straight walk to the goal - [never the same as above](invariants.md#seed-radius-is-not-finish-length) |
| `SameLevelDeltaY` | 0.5 m | **same deck?** floor to floor |
| `StairSeedRadius` | 2.5 m | how near a `Stair` node must be to enter it off-level |
| `StairEntryGroundStep` | 0.8 m | largest floor break along an off-level entry |
| `MaxStairDeltaY` | 2.0 m | climb cap for a `Stair` entry |
| `SeedCloseDist` / `SeedCloseDeltaY` | 1.25 m / 0.5 m | arm's-length entry, no line of sight needed |
| `VerticalCostFactor` | 4 | height multiplier in `GoalCost` |
| `ApproachMinGain` | 1.5 m | how much nearer an approach node must get to be worth it |
| `ApproachRouteWeight` | 0.1 | tie-break on route length among approach nodes |
| `BacktrackRadius` / `BacktrackPenalty` | 1.5 m / 3 | anti-backtrack around the last departed node |
| `AvoidEntryRadius` | 0.5 m | how near counts as *that* node for `avoidEntry` |
| `UserIdBase` | 100000 | first id for nodes you place |
| `NodeHoverTtl` | 3 s | node floor offset cache |
| `LinkShiftTolerance` | 0.1 m | how far a link's room offset may drift and still hold |
| `AnchorRetryInterval` | 2 s | retry anchoring ship nodes with no floor |

### Following - `BuddyBehaviour.cs`

| Constant | Value | Meaning |
|---|---|---|
| `NavPathRecalcInterval` | 0.22 s | minimum time between replans |
| `NavPathGoalDrift` | 1.5 m | goal movement that invalidates a plan |
| `NavPathOnRouteRadius` | 1.2 m | [still on the route](invariants.md#commitment-skips-on-route) |
| `WaypointReachedXZ` / `WaypointReached3D` | 0.55 / 0.65 m | [dual advance](invariants.md#waypoint-advance-is-dual) radii |
| `WaypointAdvanceMaxDeltaY` | 0.5 m | same-deck cap for the XZ advance; larger differences make a stair leg |
| `StairLegLookahead` | 0.35 m | how far along a stair leg the buddy aims |
| `StairLegCorridor` | 0.5 m | how far beside a stair leg still counts as on it |
| `FollowStartDistance` / `FollowStopDistance` | 2.0 / 1.8 m | Follow hysteresis (XZ) |
| `FollowSameLevelDeltaY` | 0.5 m | deck gap above which Follow never counts as arrived |
| `EntryBlockedWindow` | 4 s | window for blocked-entry escalation |
| `EntryBlockedAvoidAfter` / `EntryBlockedStepOffAfter` | 4 / 8 | escalation thresholds |
| `UnreachableWaypointHold` | 6 s | how long an unwalkable waypoint stays barred |
| `JumpVelocity` | 4.5 | auto-jump impulse |
| `DetourLedgeDrop` | 0.7 m | drop that makes a sideways detour a fall |
| `CeilingHeadroom` | 0.1 m | ceiling this close above the body counts as wedging it |

Steering and auto-jump use the controller's own `stepOffset` and `slopeLimit`, inherited from the
player prefab.

### Doors - `BuddyBehaviour.cs`

| Constant | Value | Meaning |
|---|---|---|
| `DoorwaySelfRadius` | 1.4 m | how near a gate the buddy counts as blocking it |
| `DoorwayClearRadius` | 1.2 m | fallback occupancy radius if `AntiCrasher` can't be read |
| `DoorDeferGiveUp` | 20 s | longest a close may be deferred |
| `DoorBroadPhaseRadius` | 3 m | cheap first pass before a gate volume test |
| `DoorBlockPadding` | 0.3 m | body clearance added to that volume |
| `ImpassableGatesTtl` | 1 s | impassable-gate cache |
| `PinPanelReachDist` | 1.6 m | how near a pin panel the buddy must get |

### Wander and idle recovery - `BuddyBehaviour.cs`

| Constant | Value | Meaning |
|---|---|---|
| `WanderIdleMin` / `WanderIdleMax` | 0.3 / 1.4 s | pause after arriving |
| `WanderRetryDelay` | 0.5 s | pause after a failed pick |
| `WanderPickAttempts` | 4 | destinations tried per pick |
| `IdleNoRouteGiveUp` | 2.5 s | motionless while perched before stepping off |
| `IdleAboveFloorDelta` | 0.35 m | how far above its floor plane counts as perched |

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

### Walking into reach - `BuddyBehaviour.cs`

Shared by terminals, snacks, tidying, selling and hiding ([terminals.md §2](terminals.md#2-what-the-buddy-does)).
`ReachStandOffs`, `ReachDist`, `ReachHeight` and `ReachReplanDelay` are on `ReachTask` (`ReachTask.cs`).

| Constant | Value | Meaning |
|---|---|---|
| `ReachStandOffs` | 0.8 / 1.1 / 1.4 m | stand points in front of the target |
| `ReachNodeRadius` | 10 m | nodes further from the target are ignored |
| `ReachEyeHeight` | 1.5 m | eye height for seeing the target |
| `ReachNodeArrival` / `ReachMaxReplans` | 1.2 m / 2 | route counts as walked this near its node; else replan this often |
| `ReachStandArrival` | 0.3 m | then turn to the target |
| `ReachDist` / `ReachHeight` | 1.6 m / 2 m | flat distance and height from which the target is used |
| `ReachApproachTimeout` | 8 s | final straight walk limit |
| `ReachGiveUpDelay` | 60 s | per unit, after giving up |
| `ReachReplanDelay` | 5 s | after no plan |

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

### Carrying - `BuddyHands.cs`

| Constant | Value | Meaning |
|---|---|---|
| `HoldHeight` / `HoldForward` | 0.95 m / 0.45 m | held item centre: above the feet, and ahead |
| `HoldMoveSpeed` / `HoldSnapDist` | 1.5 m/s / 2 m | follow speed on top of walking; further than this it jumps |
| `HoldDropHeight` | 0.5 m | release height above the feet |
| `HoldBodyClearance` | 0.3 m | big items held at least this clear of the body |
| `PutDownIgnoreSeconds` | 1 s | put-down item and buddy ignore each other |

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

See [items.md §4](items.md#4-selling-trash-boxes). `SellStationClearance` and `SellPenRefresh` are in
`SellPens.cs`.

| Constant | Value | Meaning |
|---|---|---|
| `SellCheckInterval` | 60 s | between looks, and after a sale |
| `SellSkipSeconds` | 600 s | a box or station with no clear way to it |
| `SellRetrySeconds` | 90 s | one that failed this time only |
| `SellSearchRadius` / `SellStationRadius` | 30 m / 80 m | boxes from the buddy; a station from the box |
| `SellMaxBoxes` / `SellZoneMaxItems` | 4 / 4 | boxes per run; most items the zone may hold |
| `SellMaxBoxesTransit` | 2 | boxes per run at the Oxygen, Solar and Fuel stations |
| `SellSlotMargin` / `SellSlotGap` / `SellDropHeight` | 0.02 / 0.02 / 0.03 m | box slots in the zone: from its walls, from each other, above the floor or box below |
| `SellMaxPlans` | 3 | boxes tried with a plan |
| `SellStationClearance` | 0.35 m | margin around the zone and fences the buddy never stands in |
| `SellPenRefresh` | 10 s | how often fence footprints are re-measured |
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

### Dialog - `BuddyDialog.cs`

| Constant | Value | Meaning |
|---|---|---|
| `TalkRange` | 2.4 m | to the buddy's capsule; shorter than the player's reach |
| `LookAngle` | 30° | cone to the *nearest* point of the capsule; a chest point fails close up |

### Carrying the corpse - `BuddyCorpse.cs`

| Constant | Value | Meaning |
|---|---|---|
| `FollowSpeed` | 50 | how fast the body tracks the hold point, like the game's `Grabbable` |
| `CarryDrag` | 10 | damping while held; gravity off |
| `HoldDistance` / `HoldDrop` | 1.6 / 0.3 m | in front of and below the camera |
| `SnapDistance` | 1.8 m | further than this, it is dropped |
| `LookAngle` | 45° | cone to the nearest limb collider |

A capped spring against gravity made the body unliftable: it pulls one rigidbody against the whole
ragdoll's mass through the joints.

### Life threat - `BuddyBehaviour.cs`

The player's own rule ([game-model.md](game-model.md#atmosphere-kills-by-the-players-rule)).

| Constant | Value | Meaning |
|---|---|---|
| `LifeThreatLethal` | 10 | summed threat at which the death counter rises |
| `DeathCounterArmed` / `DeathCounterCap` | 5 / 10 | past this, a 1-in-5 roll per tick; ceiling |
| `VacuumThreat` | 100 | no environment at all |
| `BuddySuitTemperatureResistance` | 100 | a PilotSuit's +1 °C |

### Footsteps - `BuddyBehaviour.cs`

Which sound: [game-model.md §6](game-model.md#6-footstep-sounds).

| Constant | Value | Meaning |
|---|---|---|
| `ShipFootsteps` / `StationFootsteps` | 0 / 1 | indices into the player's `footstepEvents` |
| `FootstepVolume` | 0.5 | step volume within `FootstepFullVolumeDist` of the listener |
| `FootstepFullVolumeDist` / `FootstepSilentDist` | 2 / 12 m | fades out between these, quadratically |
| `FootstepDoorHalfWidth` / `FootstepDoorHeight` | 1.5 / 2.5 m | how near a detector's centre a plane crossing counts as through its door |
| `FootstepDetectorsTtl` | 5 s | detector list cache |

### Probes - `NavProbe.cs`

| Constant | Value | Meaning |
|---|---|---|
| `EyeHeight` | 1.0 m | line-of-sight height above the floor |
| `GroundSampleStep` | 0.35 m | floor sample spacing (a tread is ~0.3 m) |
| `KneeHeight` | 0.4 m | `WalkLos` height; low enough that railings block it |
| `MaxWalkableSlope` | 45° | flatter than this is floor |
| `GateOpeningRadius` | 0.85 m | minimum carve-out, and the fallback |
| `GateOpeningMargin` | 0.25 m | added to the leaves' span |
| `GateOpeningHalfDepth` | 1.0 m | half-depth through the wall |
| `GateOpeningHalfHeight` | 2.5 m | vertical half-extent |
| `MaxGateFootprintRadius` | 2.5 m | maximum carve-out |
| `AuditPairCooldown` / `AuditMaxPairs` | 10 s / 64 | audit dedupe per collider × gate pair |
| `SightGateBroadPhase` | 6 m | `CanSee` skips shut gates further than this from the line |

---

## 2. Debug commands

The full command list is in the [README](../README.md#console-commands-reference). Commands live in
`Patches.cs`; order bodies live in `BuddyCommands.cs` so the [dialog](dialog.md) shares them.
`buddy_goto` calls `FindPath` then `ApplyRouteOrder`, so `NavPath` changes must update it.

**`ai_notarget` is player-only on purpose.** Both monster AI components kill from an
`ObjectDetector` UnityEvent that fires even when the component is disabled. So the two `DetectItem`
prefixes suppress a `Player` detection instead, leaving item handling intact. The buddy never reaches
those handlers (it has no `Player`); its catch is the mod's `BreathlessCheck`, which only
`ai_disable` stops.

**Neither override touches the save.** Both avoid `Breathless.Enabled` and `SetAggressive`, which
write the player's save ([mod-state-never-enters-the-vanilla-save](invariants.md#mod-state-never-enters-the-vanilla-save)).
They live in `AiDebug`, which `CatchRoutine` also asks before restoring the aggressor flag - otherwise
a catch would re-arm the monster. Loading a save clears them (`AiDebug.Reset`, from the `LoadGame`
postfix), restoring the current monster first in case the load is refused.

---

## 3. Node editor

Toggle with **F8** or `node_editor`. Keys are listed in the [README](../README.md#the-nav-graph--node-editor)
and configurable in the `NodeEditor` config section.

- **F6 saves, never F5** - F5 is the game's QuickSave.
- `U` removes every manual link of the selected node. It forks only that node's owner.
- Node colours: green normal, orange stair, red selected, magenta pending link, grey inactive
  (other station, or unbuilt ship room - cannot be selected).
- The console-open check uses `GameManager.Instance?.GameCanvas?.PlayerOverlay?.ConsoleMenu?.Opened`.
  Never reflect for it.

Nodes are saved per owner in owner-local coordinates in
`BepInEx/config/YourBuddyRoutes/nodegraph.json` (20 s after a change; F6 forces it). The shipped
graph and how the two files combine: [navigation.md §1](navigation.md#where-the-nodes-come-from).

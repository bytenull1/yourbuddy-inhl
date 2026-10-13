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
| `FollowStartDistance` / `FollowStopDistance` | 2.4 / 1.8 m | Follow hysteresis (XZ) |
| `ChamberHoldArrival` | 0.3 m | floating, how near the chamber's stand point it holds when you float into an airlock ([eva.md §7](eva.md#7-floating)) |

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

### The EVA suit - `BuddySuit.cs`, `EvaRun.cs`, `SuitFetchErrand.cs`

| Constant | Value | Meaning |
|---|---|---|
| `PlayerNearSuitDist` | 2 m | a stranded suit within this of you is left alone |
| `AirlockReachedWithin` | 6 m | an airlock the buddy's route ends this near is one it can walk to |
| `EnoughSuitsAtHome` | 2 | the fetch stops once this many isolated suits are yours or aboard |
| `HomeInsideInnerDoor` / `HomeAboveFloor` | 1.5 m / 0.5 m | where a fetched suit is set down: this far inside the ship's inner airlock door, targeted this high over the floor |
| `InChamberArrival` / `InChamberLeave` | 0.3 m / 0.8 m | arrived on the chamber's stand point within this, flat; still there until this far off |
| `StandNodeFallbackDist` | 1.5 m | without a node inside the chamber volume, a node this near its centre is the stand point |
| `SuitedPace` | 1.3 | times the suit's `MovementSpeedModifier`: keeps up with a sprinting suited player (1.8 m/s) |
| `StraightEnterMaxDist` | 6 m | the straight walk into the chamber starts within this, and a shut door into it is waited beside |
| `ClearDistance` / `ClearArrival` | 2.2 m / 1.4 m | the crossing leg walks to this far beyond the opened door, and is through within this of it |
| `WaitTraceSeconds` | 15 s | the throttled "waits in the airlock" log line |
| `FetchInterval` / `FetchRetryDelay` / `FetchSkipSeconds` | 120 s / 180 s / 45 s | the suit-fetch errand's schedule |

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
| `TidyMaxBinPlans` | 3 | bin routes tried per tidy selection |
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
| `SellStationRescan` | 30 s | how often every sell station, switched off or not, is swept again |
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
| `AmbushTriggerDist` / `AmbushMaxSeconds` | 1.5 m / 240 s | the closet ambush jumps out with a shriek once you are this near the spot; with nobody near by then, it gives up and leaves the closet without jumping ([anomalies.md](anomalies.md#closetambush)) |

### The anomaly director - `AnomalyDirector.cs`

See [anomalies.md §1](anomalies.md#1-how-often-and-how-far).

| Constant | Value | Meaning |
|---|---|---|
| `CheckSeconds` / `FirstCheckSeconds` | 60 / 300 s | between rolls; before the first after a load |
| `RetrySeconds` | 20 s | a roll whose draw fit nowhere tries again this soon |
| `CooldownSeconds` | 600 s | quiet after one, at Normal and tier 0; shorter on Expert and higher tiers |
| `MonsterClearance` | 25 m | none with the Breathless this near you |
| `NormalScaryTasks` / `NormalExtremeTasks` | 1 / 3 | story tasks done before scary, then extreme, are allowed |
| `ExpertAllTasks` | 1 | story tasks done before Expert allows scary and extreme |

### Anomalies - `BuddyBehaviour.Anomaly.cs`

| Constant | Value | Meaning |
|---|---|---|
| `SightSampleSeconds` | 0.1 s | how often a running anomaly asks whether you see it |
| `StareMinSeconds` / `StareMaxSeconds` | 50 / 140 s | a window or corner stare, once there |
| `StareSearchRadius` | 20 m | a window this near |
| `CornerRays` / `WallSearchDist` | 16 / 4 m | rays for the walls of a corner, and how far they reach |
| `CornerSearchDist` / `CornerStandOff` | 5 / 0.65 m | a corner this near; it stops this far from it |
| `CornerSlack` | 0.35 m | how near the corner a ray towards it must hit |
| `VanishMinSeconds` / `VanishMaxSeconds` | 40 / 110 s | gone this long |
| `ReappearMinDist` / `ReappearMaxDist` | 6 / 16 m | back at a node this far from you, out of your sight |
| `BloodySeconds` | 150 s | how long the blood lasts unwatched |
| `BloodySeenRate` | 15 | each second you watch it uses this many seconds of that |
| `BloodyUnseenSeconds` | 5 s | it goes only once you have not seen the buddy this long |
| `BloodyRunRetrySeconds` | 6 s | its time up and still seen, it tries to run out of your sight this often |
| `BehindYouDist` | 1.2 m | how far behind you it stands |
| `StalkerSounds` | 3 | the most sounds a stalker makes at your back |
| `NoisesGapMin` / `NoisesGapMax` | 3 / 5 s | silence between Noises' sounds, after each has ended |
| `NoisesMinDist` / `NoisesMaxDist` | 1.2 / 7 m | behind you this near, Noises makes its sounds |
| `ComeMaxDist` / `ComeSeconds` | 30 m / 60 s | Whisper and Noises not near: it comes up to you from this far, for at most this long |
| `WhisperDist` | 4 m | Whisper says its line this near |
| `NoisesVolume` | 1.25 | Noises' sounds over their own volume |
| `DoorPassDist` | 1.6 m | how far past a doorway it walks before the door is shut behind it |
| `DoorLegSeconds` / `DoorSettleSeconds` | 20 / 3 s | a walk through one door gives up; at the end of a pass, the most it waits for the doors still owed to shut |
| `MaxDoors` | 4 | doors in one round |
| `WrongNameSeconds` | 900 s | the wrong name waits this long for the window |
| `BotTalkMinDist` / `BotTalkMaxDist` | 12 / 45 m | you this far from the Shipyard's robot ([anomalies.md](anomalies.md#bottalk)) |
| `BotStandMin` / `BotStandMax` | 1.2 / 2.2 m | where it stands from the robot |
| `BotTalkGlanceDist` / `BotTalkCloseDist` | 4 / 2.5 m | it looks round when you see it this near, or come this near |
| `BotTalkWaitSeconds` / `BotTalkGlanceSeconds` | 240 / 1.2 s | how long it waits for you; how long it looks at you |
| `BotBlipGap` | 0.035 s | between the robot's talk sounds |
| `RunOffMinDist` / `RunOffMaxDist` / `RunOffGain` | 6 / 20 / 4 m | where it runs: this far, and this much further from you |
| `RunOffLegSeconds` / `RunOffUnseenSeconds` | 20 / 3 s | the run gives up; there, it ends once unseen this long |
| `CaughtMinDist` / `CaughtMaxDist` | 8 / 60 m | you this far from the cryo room's door ([anomalies.md](anomalies.md#meat)) |
| `CaughtViewInset` | 0.8 m | where you first see in: this far inside the doorway |
| `CaughtStandMin` / `CaughtStandMax` / `CaughtStandDist` | 2 / 5.5 / 3 m | where it stands, in from that point; nearest 3 m |
| `CaughtMeatAhead` | 0.7 m | the meat, in front of it (else 0.6 of that, else 0.3 m) |
| `MessProbeAbove` / `MessLevelTolerance` | 0.05 / 0.05 m | each stain and the meat: the floor probed from this high, and this level with its feet |
| `CaughtWaitSeconds` | 480 s | how long it waits for you |
| `CaughtNearDist` | 3 m | you this near give it away, door shut or not |
| `CaughtEnterDist` | 3 m | on the room's side and this near the doorway, you are in: it runs |
| `CaughtStareSeconds` | 3 s | watched this long when caught, it runs even if you do not step in |
| `CaughtTurnSeconds` / `CaughtCorneredSeconds` / `CaughtGoneUnseenSeconds` | 0.6 / 120 / 1 s | the least it stares at you; the most it stares or waits cornered; unseen this long (after you saw it), it is gone |
| `CaughtRunMin` / `CaughtRunGain` | 2 / 1 m | its run inside the room; else 1 / 0 m |
| `PipeMinDist` / `PipeMaxDist` | 5 / 30 m | Pipe: you this far when it starts ([anomalies.md](anomalies.md#pipe)) |
| `PipeGiveUpSeconds` | 120 s | never spotted this long, it puts the pipe down unseen |
| `PipeStartleDist` | 4 m | spotted this near, your stress |
| `PipeGoneUnseenSeconds` / `PipeWatchedSeconds` | 1 / 60 s | after the drop: unseen this long, it vanishes; watched this long, it ends |
| `MoveWaitSeconds` | 300 s | Move: waits this long to be found ([anomalies.md](anomalies.md#move)) |
| `MoveStartleDist` | 4 m | found this near, your stress |
| `MoveStareSeconds` | 3 s | found, it stares this long, silent, before it follows |
| `LaterLineSeconds` | 10 s | Move's and BehindYou's line comes this long after ([anomalies.md](anomalies.md#3-what-it-says)) |
| `LaterLineDist` / `LaterLineGiveUpSeconds` | 8 m / 120 s | said within this of you, out of your sight; not said this long after due, only logged |
| `FlickerWaitSeconds` | 240 s | Smile, UnderTheSuit: the most it waits for you to look ([anomalies.md](anomalies.md#smile-and-underthesuit)) |
| `FlickerMinDist` / `FlickerMaxDist` | 1.5 / 7 m | you this far from it |
| `FlickerLookAngle` / `FlickerFacingAngle` | 25° / 50° | its head this near the middle of your view; it faces you this squarely |
| `SmileBeats` / `FleshBeats` | 0.2, 0.12, 0.12 / 0.12 s | the flicker: on, off, on |
| `SleeperMinDist` | 8 m | Sleeper: you this far from its capsule; it half that ([anomalies.md](anomalies.md#sleeper)) |
| `SleeperWithYouDist` | 25 m | the buddy this near you, on your ship |
| `SleeperWaitSeconds` | 900 s | the most the capsule waits for you |
| `SleeperOpenDist` / `SleeperOpenAngle` | 2.5 m / 60° | it opens with you this near the sleeper's head, it this near the middle of your view |
| `SleeperSeeDist` / `SleeperLookAngle` / `SleeperGoneUnseenSeconds` | 6 m / 45° / 1 s | found, you still look at it within these; not for 1 s, the capsule is empty |
| `SleeperStareSeconds` | 30 s | the most the buddy stands facing you, waiting to be seen |
| `MeatScale` | 1.5 | the meat's size over its model ([anomalies.md](anomalies.md#the-mess-and-the-meat-model)) |
| `MessSeenDist` / `MessNearDist` | 10 / 4 m | a mess counts as visited once you see it within, or come within |
| `MessGoneDist` | 15 m | a visited mess's pieces go this far from you, out of sight |
| `LandSpeed` / `GapSeconds` | 1.5 m/s / 0.4 s | the meat squelches landing faster than this; at most this often |
| `DoorClearDist` | 1.6 m | the least a ShutDoors stand is from the door |
| `MaxDoorPasses` | 2 | passes over the doors still open |

### Sounds - `ScareSounds.cs`

| Constant | Value | Meaning |
|---|---|---|
| `SoundCapSeconds` | 3 s | a creature or shriek sound stops here, silent by then |
| `SoundFadeSeconds` | 1.5 s | it fades to silence over this, before the cap |
| `FadeOutSeconds` | 0.25 s | a buddy's sounds die away this fast when FadeOut is called (you turned round) |
| `BlipGapMin` / `BlipGapMax` | 0.07 / 0.12 s | between the voice blips of a spoken line |
| `BlipHeight` | 1.5 m | where they play, above the speaker's origin |
| `RecentCount` | 2 | a category's last picks, not picked again yet |

### The mod's clips - `ModSounds.cs`

| Constant | Value | Meaning |
|---|---|---|
| `MinDistance` / `MaxDistance` | 1.5 / 20 m | full volume within, silent past |
| `Volume` | 0.85 | a clip's volume, under the sfx bus |
| `OddVolume` / `SlimeVolume` | 0.35 / 0.25 | the `odd_` and `slime` clips' instead |
| `MuffledCutoff` / `MuffledVolume` | 1400 Hz / 0.8 | a clip behind a wall: low-pass cutoff, and volume over `Volume` |

### Being seen - `PlayerView.cs`

| Constant | Value | Meaning |
|---|---|---|
| `ViewHalfAngle` | 55° | in view within this of the camera's forward |
| `ViewRange` | 40 m | nothing further counts as seen |
| `BehindAngle` | 115° | off the view by this much is behind you |

### Blood - `BuddyGore.cs`

| Constant | Value | Meaning |
|---|---|---|
| `Blots` | 16 | blots painted on the texture |
| `BlotMinRadius` / `BlotMaxRadius` | 1 / 4 texels | their size on a 64x64 atlas, scaled with the texture |

### Spawning several - `BuddyConsole.cs`

| Constant | Value | Meaning |
|---|---|---|
| `SpawnSpacing` | 0.8 m | gap between buddies in `buddy_spawn N`'s row |
| `SpawnSameDeck` | 0.5 m | a row point is used only on the middle point's deck, else it falls back to the middle |

---

## 2. Debug commands

The graph, gate and editor commands, `debug_level`, `ai_disable` and `ai_notarget` are NPC.Core's
([its reference](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/reference.md#2-console-commands)); `ai_disable buddy`
still means `ai_disable npc`.

The full command list is in the [README](../README.md#console-commands-reference); `buddy` prints it in
game. `buddy_spawn`, `buddy_despawn`, `buddy_kill` and `buddy_list` are commands of their own
(`BuddyConsole.Singles`): they are used most. Each category (`buddy_order`, `buddy_anomaly`, `buddy_dev`)
is one console command whose first non-`@` argument picks the subcommand (`BuddyConsole.Dispatch`). Why
categories: about 30 flat `buddy_*` names made the console list hard to read. Commands live in
`BuddyConsole.cs`; order bodies live in `BuddyCommands.cs` so the [dialog](dialog.md) shares them.
`buddy_dev drain <oxygen|fuel|energy>` lowers stored ship reserves to 20% through the game's
resource controllers. It does not enable duties or alter purchase permissions.
`buddy_dev goto` calls `FindPath` then `ApplyRouteOrder`, so `NavPath` changes must update it.

**Which buddy.** A per-buddy command takes `@2` (a number), `@buddy2` (a name, case and spaces
ignored) or `@all` anywhere among its arguments (`BuddyConsole.ForTargets`). Without one it goes to
`BuddyManager.Focus`: the buddy last talked to or named, else the nearest living one. Naming exactly
one moves the focus, which is also the buddy the HUD shows. `buddy_list` lists numbers and names and
marks the focus. `buddy_spawn N` replaces every buddy with N.

**`ai_notarget` leaves the buddy huntable.** Its catch is the agent's own `BreathlessCheck`, which only
`ai_disable` stops; `CatchRoutine` restores the aggressor flag it borrowed only while
`NpcMonster.PlayerIgnored` is false, so a catch never re-arms a monster the player switched off
([the-ai-overrides-have-one-owner](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#the-ai-overrides-have-one-owner)).

### Sorting overflow - `StoragePolicy.cs`

| Constant | Value | Purpose |
|---|---|---|
| `OverflowSpacing` | 0.65 m | spacing between centres of the fixed 3-by-3 overflow grid |

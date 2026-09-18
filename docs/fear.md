# Fear - reacting to the Breathless

`BuddyBehaviour.Fear.cs`, `BuddyBehaviour.Hide.cs`, `NavProbe.CanSee`. Config (General): `Fear`
(default on), `HideInClosets`, `FleeHideBias`. The monster's side is in
[game-model.md §5](game-model.md#5-the-breathless).

---

## 1. What it does

| State | Entered at | Behaviour |
|---|---|---|
| `Calm` | stress below `FearAlertEnter` (from Alert: below `FearAlertExit`) | normal |
| `Alert` | stress at `FearAlertEnter` | watches the monster when idle, takes no step toward it (§4) |
| `Scared` | stress at `FearScaredEnter`, **or** the monster in a clear line within `FearPanicDist` (instant) | `BuddyMode.Flee`: hide (§6) or run (§5) |

Scared is left below `FearAlertEnter`, not `FearScaredEnter`, so a flee runs until the buddy is
properly calm. Then the buddy goes back to what it was doing, or to what it was ordered meanwhile.

Runs in `SlowUpdate` phase 2, after `BreathlessCheck`.

---

## 2. Seeing the monster

`NavProbe.CanSee` is the only sight test
([sight-stops-at-a-shut-door](invariants.md#sight-stops-at-a-shut-door)):

1. any gate that is not open, whose volume the line crosses, blocks it and is reported;
2. a straight raycast on `ProbeLayers` then fails on the first hit that is not a body, the target,
   or frame trim in a fully open doorway.

`ThinLos` would be wrong: it forgives door openings whether shut or not, and looks from 1 m over
low scenery.

The buddy looks from `FearEyeHeight` to two points - the monster's `raycastPoint` (where it looks
from) and its origin - and either being clear counts. Nothing beyond `FearSightRange` is cast.

**It has a front.** While Calm, only a monster within 65° of where the body faces counts
(`FearViewCos`). Behind it, it is logged as `clear line but behind it` and adds no stress. Two
exceptions:

- **above Calm, sight is all-round** - otherwise a fleeing buddy, facing away, loses sight at once
  and stops fleeing mid-run;
- **within `FearPanicDist`** it is felt from any side.

**The cloak does not hide it.** `Breathless.Data.visible` is ignored. Walls and shut doors still do.

---

## 3. Stress

Per second:

| Monster | Stress |
|---|---|
| in sight | `+FearStressRate × (1 + FearProximityGain × closeness) × (1 + FearApproachGain × approach)` - closeness 0 at `FearSightRange` → 1 at `FearPanicDist`; approach 0 → 1 as it closes at 0 → `FearApproachSpeed` |
| in sight within `FearPanicDist` | set to `FearStressCap` |
| otherwise | `−FearDecayRate` |
| in sight, but the flee is stalemated (§5) | `−FearDecayRate`, never below `FearStalemateStress` |

Timings from Calm (at the 0.24 s phase rate):

| In sight at | Standing / closing at 2 m/s | Alert after | Scared after |
|---|---|---|---|
| 18 m | 0.15 / 0.22 /s | 6.7 / 4.6 s | 20 / 13 s |
| 10 m | 0.34 / 0.52 /s | 3.1 / 2.2 s | 8.9 / 6.0 s |
| 6 m | 0.44 / 0.66 /s | 2.4 / 1.7 s | 7.0 / 4.6 s |
| 4 m | 0.49 / 0.73 /s | 2.2 / 1.4 s | 6.2 / 4.3 s |
| ≤ 2.5 m | - | instant | instant |

After losing sight, a panic stays Scared ~10 s, then Alert ~1.2 s. A glimpse that just reached Alert
is Calm ~2 s later. The exit threshold stops the state flickering.

---

## 4. Alert - holding back

`HoldBackFromMonster` runs in `Update` before `HandleDoors`. Above Calm, a heading within 60° of the
monster's **last known** position, while within `FearRestraintDist`, becomes `wantMove = false` - no
doors are opened that way, and stuck watchdogs reset instead of escalating. Logged every 5 s:

```
[fear] Alert: not walking toward the Breathless 7.4m away - holding (Follow)
```

Retreat plans are exempt (§5). Standing still, the buddy watches the monster instead of the player.
Wander skips targets within `FearRestraintDist` of it.

---

## 5. Scared - `BuddyMode.Flee`

A mode of its own, not a Route: `FinishRoute` forces Follow, which would end the flee on arrival.
`StartFlee` records `modeBeforeFlee`, clears any step-off and Follow wait, and plans.

| Phase | Does | Ends |
|---|---|---|
| `Retreat` | walks the retreat plan like Wander (dual advance, stair legs) at `FleeSpeedFactor` | arrived → `ToPlayer`; stuck, no progress, off the flight, or the monster now in the way → re-plan |
| `ToPlayer` | `UpdateFollow` at `FleeSpeedFactor`; holds while the player stands within `FleeClearance + FearPanicDist` of the monster | a panic re-plans a retreat |
| `Hold` | stands and watches; retries every `FleeHoldRetryDelay` | a retreat or back-away becomes possible |
| `Hide` | walks into a closet or locker and shuts it (§6) | it comes out |

### Where it runs

`TryPlanRetreat` scores every active node:

```
qualifies  if  dist(node, monster) ≥ dist(buddy, monster) + FleeMinGain
score      =   min(dist(node, monster), FleeSafeDist)
             − FleePlayerWeight × dist(node, player)
             − FleeTripWeight   × dist(buddy, node)
```

Past `FleeSafeDist` (15 m) nearness to the player decides: retreat, *then* run to the player. The
best `FleePickAttempts` are planned; the first plan that **keeps clear** wins - no leg may bring the
buddy nearer the monster than it starts, nor within `FleeClearance` once outside it. This re-runs
every 0.5 s; a failed target is skipped for 8 s.

**Nothing routes →** back away: the first of 0°, ±30°, ±60°, ±90° from straight away that is clear
(`BodyBlocked`, `StepsOffALedge`) becomes a `FleeBackAwayDist` step-off. **Nothing clear →** `Hold`.

### Hiding or running

`TryStartRetreat` draws which to try first, and falls back to the other:

```
hideFirst = PreferHide()
hideFirst and a spot found        -> hide
a retreat plan that keeps clear   -> run
not hideFirst, running failed     -> hide after all
neither                           -> back away, then hold
```

`PreferHide` is a weighted coin, starting from `FleeHideBias` (0.5):

| Term | Effect |
|---|---|
| the monster is in sight right now | × `HideSeenFactor` (0.35) - it would watch the buddy climb in |
| the monster is within `HideMonsterClose` (6 m) | × 0.5 - no time to shut the doors |
| the last retreat found nowhere to run | + `HideNoRetreatBias` (0.3) |

Clamped to 0..1, logged at level 2. `FleeHideBias` 0 never hides, 1 always tries. The rules in §6
still apply.

### Stalemate - nowhere left to run

If the monster stays in view and the buddy has run as far as the graph allows, stress cannot fall and
every retry fails: it would stare at the monster forever. So the flee records when it last ran out
of options (`fleeStuckSince`) and clears it when anything works. Stuck for `FleeStalemateSeconds`
(6 s) with the monster outside `FearPanicDist`, stress decays as if out of sight, down to
`FearStalemateStress` (0.75). The buddy drops to **Alert**: back to its task, still watching, still
not stepping toward it.

The stalemate breaks if the monster comes within `FearPanicDist`, closes `FleeStalemateClosed` (2 m)
on where it stood, or the flee ends. Logged (at most every 10 s), HUD shows `, stalemate`:

```
[fear] Nowhere left to run from the Breathless 9.4m away for 6s - easing off, staying Alert instead of staring at it
```

### Ending

`UpdateFear` ends the flee the tick fear drops below Scared (or at once if fear or the monster's AI
is switched off). `EndFlee` resumes `modeBeforeFlee`; a goto is re-planned, or dropped for Follow if
that fails ([fear-owns-the-buddy](invariants.md#fear-owns-the-buddy)).

---

## 6. Hiding in a closet or locker

`BuddyBehaviour.Hide.cs`. Config `HideInClosets` (default on), `FleeHideBias`; console `buddy_hide`;
dialog word **Hide**.

`HidingSpot` is the game's mountable closet, cabinet or locker. The game hides a *player* via
`Player.hidden`; the buddy is not a `Player`. What protects it is the mod: walls block sight, the
catch is skipped, and the game's Breathless never looks for the buddy anyway.

**A spot qualifies when it:**

- is within `HideSearchRadius` (14 m), free (never one you are in), in a built room, and has doors,
  **all shut** (an open one is yours);
- has no suit hanging in it (a `Locker`'s `EquipmentHolder.Item`). `Mountable.locked` itself is
  **not** tested: the game sets it whenever the doors are shut, which is how every spot is found;
- is at least `HideMonsterClearance` (3 m) from the monster;
- has a nav node with a clear walk to a stand point by its doors (stand points 0.55–0.85 m);
- has a plan that keeps clear of the monster, like a retreat;
- is at most `HidePreferWalk` (16 m) of walking - further, running is quicker;
- is on the buddy's own vessel (`OnMyVessel`).

The nearest qualifying spot wins.

**States** (`hideState`; `FleePhase.Hide` while a flee owns it):

1. **Walking** - plan to the node, then walk to the stand point, through the normal mode code
   (doors, stairs, detours). The flee's hold-back is waived. Gives up after `HideWalkSeconds` +
   1.2 s per metre, or if you get in first.
2. **Entering** - open the doors, wait 1.5 s, teleport to `MountPos` (adjusted for capsule height),
   switch the `CharacterController` **off**, face out, shut the doors.
3. **Hidden** - `BreathlessCheck` returns early and `UpdateFear` counts the monster as unseen, so
   stress decays.

   The doors must first be **seen shut**. `Door.Opened` stays true until the close animation ends
   (~2 s), so until then the buddy's own close looks like someone opening the door. Nothing counts
   as "found" until `hideDoorsShut`; a door still open after `HideDoorShutSeconds` (6 s) will not
   shut, and the buddy leaves.

   | Started by | Comes out when |
   |---|---|
   | a flee (`hideFromFear`) | at least `HideMinSeconds` (25 s) inside, Calm, and nothing seen for `HideCalmSeconds` (15 s) |
   | an order or `buddy_hide` (`hideOrdered`) | **only** on another order, on being found, or a forced leave - no timer |

   A frightened hide never ends while the monster is within `HideMonsterNearDist` (5 m) - it can hear
   it through the walls ([a-hidden-buddy-waits-out-a-monster-it-can-hear](invariants.md#a-hidden-buddy-waits-out-a-monster-it-can-hear)).
   `HideMaxSeconds` (240 s) caps that wait. `HideMonsterNearDist` is the number to tune if hiding
   feels too long or too short.

   An ordered hide has no timer or ceiling
   ([an-ordered-hide-ends-only-on-an-order](invariants.md#an-ordered-hide-ends-only-on-an-order)).
   Both kinds log their wait every 15 s.
4. **Found** - any door opened while inside (by the Breathless or you) ends the hide at once.
5. **Leaving** - open the doors, teleport back to the floor point it came from, shut the doors.
   Then ([a-hide-that-worked-ends-the-flee](invariants.md#a-hide-that-worked-ends-the-flee)):

   | Left because | Then |
   |---|---|
   | the fear passed | the flee **ends**, back to the previous mode |
   | found, doors would not shut, or never got in | the flee tries again (maybe another closet) |

**Guards**

- **You cannot climb into an occupied spot** - a prefix on `HidingSpot.Interact` shows the game's
  "Occupied" message.
- **Anything that removes the buddy** (death, parking, rebuild, despawn) calls
  `ForceLeaveHidingSpot`: controller back on, buddy outside, doors shut
  ([a-snack-closes-what-it-opened](invariants.md#a-snack-closes-what-it-opened)).
- **A save while hidden** records the outside point
  ([a-hidden-buddy-is-saved-outside-its-hiding-spot](invariants.md#a-hidden-buddy-is-saved-outside-its-hiding-spot)).
- **No dialog** while hiding; **the decider stands down**.
- **Orders end an ordered hide, not a frightened one.** `ApplyOrder`, `ApplyRouteOrder` and
  `RevokeOrder` call `LeaveAnOrderedHide`. A frightened hide defers the order
  ([fear-owns-the-buddy](invariants.md#fear-owns-the-buddy)).
- **`Fear` off or `ai_disable` ends only a frightened hide.** `buddy_hide` ignores `Fear` going in,
  so it must not pull the buddy out.
- **`buddy_hide` works while Alert** - that is when you would ask.
- **`buddy_hide` interrupts an errand** (logged `Stopped tidying up '…' - you asked me to hide`); a
  goto still refuses ([a-command-outranks-an-errand](invariants.md#a-command-outranks-an-errand)).

---

## 7. Guards

| Situation | What happens |
|---|---|
| being caught, or dead | `UpdateFear` does nothing; the catch and `Die` are untouched |
| the dialog is open | stress moves, but a flee starts only after it closes |
| `ai_disable monster`, or `Fear = false` | calm at once, stress 0, any flee ended |
| `ai_disable buddy` | fear is frozen with everything else |
| `ai_notarget` | no effect - it blinds the monster to the player, not the buddy to the monster |
| `MortalNPC = false` | fear still works; the monster just cannot kill the buddy |
| parked on an undocked station | no `Update`, no fear |
| the decider | stands down above Calm ([fear-owns-the-buddy](invariants.md#fear-owns-the-buddy)) |
| hiding | no catch, monster counts as unseen, removal puts the buddy outside first |

---

## 8. Reading a capture

Tag `[fear]`. Level 1: state changes and flee decisions. Level 2 adds a once-a-second stress trace,
retreat failures, back-aways and shut-gate refusals.

```
[fear] Calm -> Alert: stress 1.08, the Breathless in sight 9.8m away
[fear] stress 2.19 (+1.13/s) Alert: in sight 9.7m, mode Follow
[fear] Alert -> Scared: stress 3.23, the Breathless in sight 9.5m away
[fear] Fleeing the Breathless 9.5m away (watched it too long), then back to Follow
[fear] Retreating to (3.1, 1.0, -12.4): 16.2m from the Breathless (now 9.5m), 2.3m from the player, 6 waypoints
[fear] Scared -> Alert: stress 0.91, it is out of sight
[fear] The Breathless 6.2m away is behind shut gate 'Door02' - not in sight      <- level 2
[fear] Choosing to run (hide chance 0.17, it is watching me)                      <- level 2
[fear] Hiding in the closet 3.2m away (the Breathless is 9.5m away)
[fear] Hidden in the closet
[fear] Staying in the closet - the Breathless is 4.4m away (48s inside)           <- every 15s
[fear] Found in the closet - the door is open - getting out
[fear] Coming out of the closet - calm, and nothing seen for 15s
[fear] Out of the closet - you told me to Follow
[fear] Hide: not the closet at (…): it is within 3m of the Breathless             <- level 2
```

The HUD's `Fear:` line shows state, stress, sight (or `behind it`), distance and flee phase.

---

## 9. Known limitations

- **Only `GameManager.Breathless` is feared.** Scripted stand-ins (e.g. `BreathlessDecision`) are not.
- **Glass blocks sight.**
- **It avoids the last known position** until Calm, even if the monster has moved.
- **The dialog can be opened mid-flee**, and holds the buddy still while open.
- **Whisker detours are not restrained** and can angle toward the monster for a step.
- **Stress is not saved**; a loaded buddy is Calm.

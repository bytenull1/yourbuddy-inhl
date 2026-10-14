# Invariants - rules that must not be broken

Each rule is stated once, here. Code and other docs link to its anchor.
Every entry exists because breaking it caused a bug.

Format: **Rule** - what must hold. **Why** - what breaks without it. **Enforced in** - where it lives.

---

The rules for navigation, probes and path following, for the walking agent (doors, rooms, vessels,
carrying), for the game state behind them and for several NPC mods at once are NPC.Core's:
[its invariants](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md).

---

## Fear and orders

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

### survival-outranks-an-order

**Rule.** When the air aboard turns deadly (`LifeSupport.AirIsDangerous`), an order in force -
Follow, Wander or Stay - does not keep the decider out of its two survival urges: switch a unit on,
or when none can be tried, put a spare suit on. Once the air is killing the buddy
(`NpcAgent.LifeInDanger`), the suit comes first and outranks everything but fear, a catch and a
closet: the order, an errand, a walk to a terminal - checked every second. Everything else still
waits for the order. The task ends in the ordered mode again (`ModeAfterTask`). Autonomy off still
means it never acts on its own.

**Why.** Under a Follow order the buddy followed the player, suited, to its own death beside a free
suit - the decider was standing down for the order. Then, with no order, it chose the terminal at a
death counter of 5/5 and died on the way: a switched-on generator refills the air far slower than
the six ticks the counter leaves.

**Enforced in.** `BuddyBehaviour.TrySaveOwnLife`, `UpdateAutonomy` / `TrySurvival`,
`StandDownReason(ignoreOrder)`; the `Suit` urge's weight in `ScoreUrges`.

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

---

## The suit

### the-buddy-never-takes-your-last-suit

**Rule.** A suit the buddy takes is an enabled, ungrabbed, isolated `Suit` item - lying free in the
world, or displayed in a locker (`EquipmentHolder.Item`, released through its own `TryDropItem`) -
and it is taken only while you wear a suit yourself (then anything free is a spare), while two or
more are free, or while you explicitly ordered the run (`buddy_order outside`, `buddy_order suit on`): you are
right there and consented. The rule is checked when the suit is chosen and again at the suit, right
before it is worn.

**Why.** The buddy wearing your only suit leaves you stranded at the airlock: you cannot follow it
out, and the game's own `Equipment.Equip` refuses to double-equip. A suit left on the station is
always fetchable - carrying it home never strands you.

**Enforced in.** `BuddySuit.TakeableSuit`, re-checked in `EvaRun.SuitLeg.Approach`. The fetch skips
a suit within `PlayerNearSuitDist` (2 m) of you.

---

## Game state

### the-buddy-sells-only-trash-boxes

**Rule.** The buddy presses a sell button only when every `CanSell && Enabled` item in the zone is a
`Trash_Box`, the player is outside the catch zone, the gate is fully open, and every buddy stands
outside the zone. It presses with `Button.Interact(pilot)`, so the player is paid. Otherwise the
boxes stay loaded. A run presses once per load; the rule is checked right before each press.

**Why.** `SellStation.Sell` sells everything in the zone and kills a player standing inside. A body
under the gate trips its `AntiCrasher` and the sale silently fails.

**Enforced in.** `SellErrand.PressButton` (with `IErrandBody.AnotherBuddyWhere`) and `TryStart`,
`SellTask.StandAllowed`.

### a-selling-run-keeps-its-boxes

**Rule.** The search radius and the vessel filter choose a run's boxes; they never end it. A queued
box leaves the run only when it is gone, taken, loaded, or has no way to it, and each exit is logged.
A box in an unloaded room is still queued. A station whose room is off is still a candidate: its room
is loaded before a run plans at it, and again whenever the run finds it off.

**Why.** After a load the buddy stands at the station. A fresh search from there cannot see boxes
left on the ship: they are on another vessel, and their room may be unloaded, so `FindObjectsOfType`
skips them. The run then ended after one box of four. And the Shipyard's station unloads with its
hallway as soon as the buddy leaves for the next box; `FindObjectsOfType` without `true` skipped it
too ("no sell station within 80m" of four boxes).

**Enforced in.** `SellErrand.ConfirmSale`, `TryNextBox`, `FetchBlocker`, `StationBlocker`; `Items.InUnloadedRoom`.

### an-undock-ends-a-route-off-the-ship

**Rule.** When the station a route was planned under undocks, the route is checked once. If any
waypoint left, or the leg's stand point, is not on the floor of the vessel the buddy stands on, the
route ends. A route that stays aboard goes on.

**Why.** A plan is world points. After an undock, the waypoints through the station lead to the shut
outer airlock door. The buddy walked into it for good: bouncing off the door counted as progress, so
no stall recovery fired.

**Enforced in.** `BuddyBehaviour.RouteLostItsDock` (Modes.cs), called from `UpdateRoute`.

## Anomalies

### an-anomaly-puts-back-what-it-changed

**Rule.** Every way an anomaly ends - its own end, an order, a task, fear, deadly air, death,
parking, a rebuild - goes through `EndAnomaly`. That puts back exactly what the anomaly changed: the
renderers it switched off and no others, the controller's collisions, `Asleep`, the texture the body
wore, a flicker's skin, an ambush's closet, a stare's walk, the robot it talked to, a stalker's door codes,
a double it made and the capsule it shut. The doubles live outside the buddy, so its `OnDestroy` removes
them too.

**Why.** A vanish that ends any other way leaves an invisible buddy with no AI that nothing ever wakes.
Switching every renderer back on brings back the face mask `SpawnBuddy` hides. Restoring the original
texture instead of the one it wore strips a skin the player chose.

**Enforced in.** `EndAnomaly`, from `UpdateAnomaly`, `EndAnomalyForOrder`, `BusyForCommand`,
`AnomalyHoldsBody` (fear), `UpdateAutonomy` (air), `OnInterrupted` and `OnDied`; `Reappear`,
`BuddyGore.Remove`, `ShowFlicker`, `ReleaseBot`, `RemoveSleeper`; `BuddyBehaviour.OnDestroy`.

### an-anomaly-prop-never-enters-the-save

**Rule.** A prop an anomaly leaves (`AnomalyProps`) is a clone of a game object whose `SaveObject` id
is cleared and whose `Data` is set, with an id the game's counter never reaches, before its `Awake`
runs. That data is never registered with `SaveData`. A double of the buddy (`BuddyDouble`) has every
script stripped before it wakes, so it has no save data at all. Nothing either does reaches the vanilla
save.

**Why.** A clone keeps the original's serialized id and would share, and overwrite, its save data. A
fresh id makes `CreateData` register the prop, and a load then holds data for an object nothing makes
any more, or an item prefab the game cannot find once the mod is gone. Removing the mod must never break
a save.

**Enforced in.** `AnomalyProps.SpawnMeat`, `SpawnPipe`, `SpawnBlood` (set up under a switched-off holder);
`GameInternals.PropAccess`; `BuddyDouble.Make` (built under a switched-off root).

---

## Several buddies

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

### resource-duties-own-only-their-cell

**Rule.** At most one resource duty runs across all buddies. Its live leg claims its cell and
loader under [one-buddy-per-target](#one-buddy-per-target). A cell that is loading blocks refilling and is never touched. On arrival a spent cell is ejected;
an idle charged one is loaded if it is the due kind, else ejected and stored as a spare first.
Restocking leaves any inserted cell alone. A cell that was in the loader stays there on
interruption; one the duty brought is ejected. A refill that ends with charge left stores its cell as a spare. Saving leaves an active refill and its cell in place.
Purchases need an enabled resource rule, explicit purchase permission, enough shared remaining
allowance and enough cash. These conditions are checked
again immediately before spending. An uncertain purchase pauses the duties.
Spare-cell delivery targets reachable floor storage independently of the insertion slot.
Its footprint must have level support and clear space immediately before release; a failed
delivery must not substitute an unchecked forward drop.

**Why.** The ship has one cell slot. Independent per-buddy allowances would multiply permitted
spending. An idle cell left in the loader would block every refill, but one that is loading is
the player's work in progress.

**Enforced in.** `ResourceDuty`, `ResourceErrand`, `ResourceDutySettings.CanBuy`,
`ResourceErrand.Leg.Holds`.

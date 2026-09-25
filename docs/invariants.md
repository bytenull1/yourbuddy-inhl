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

### a-sell-station-room-stays-loaded

**Rule.** While a buddy exists and `SellTrash` is on, a room whose content holds a `SellStation` is
never switched off, by any NPC or by the game: YourBuddy keeps it through NPC.Core
([a-kept-room-stays-loaded](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#a-kept-room-stays-loaded)). The buddy also switches one back
on if it finds it off (a save restores it that way).

**Why.** `FindObjectsOfType` skips a switched-off station, so the sell run found "no sell station
within 80m" of four boxes. The game switches YardHallway off at its doorways, on docking
(`Docker.Dock`) and on leaving by the Shipyard airlock (`ShipyardStation.DisableHallway`).

**Enforced in.** `SellRoomsKeeper` (`ReasonToKeep`, `Rooms`), `SellPens.HoldsSellStation`, the brain's slow
phase 0 (`INpcBrain.SlowPhase`), and the agent's `ReasonToKeepLoaded`.

### a-selling-run-keeps-its-boxes

**Rule.** The search radius and the vessel filter choose a run's boxes; they never end it. A queued
box leaves the run only when it is gone, taken, loaded, or has no way to it, and each exit is logged.
A box in an unloaded room is still queued, and the run's station is reloaded if its room goes off.

**Why.** After a load the buddy stands at the station. A fresh search from there cannot see boxes
left on the ship: they are on another vessel, and their room may be unloaded, so `FindObjectsOfType`
skips them. The run then ended after one box of four. And the Shipyard's station unloads with its
hallway as soon as the buddy leaves for the next box.

**Enforced in.** `SellErrand.ConfirmSale`, `TryNextBox`, `FetchBlocker`, `StationBlocker`; `Items.InUnloadedRoom`.

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

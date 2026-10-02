# Anomalies - when the buddy is not quite itself

Now and then the buddy does something strange or frightening. The aim is doubt: is this still
the friend you woke up with? `AnomalyDirector.cs` decides when and how far; `BuddyBehaviour.Anomaly.cs`
acts it out. Config section `Anomalies`: `Anomalies` (on), `AnomalyDifficulty` (`Game`),
`AnomalyFrequency` (1).

---

## 1. How often and how far

The game has no single "danger level". Its own random events use two numbers, and so do these:

| Input | Read from | Values |
|---|---|---|
| events frequency | `SceneLoader.Instance.GameData.Settings.eventsFrequency` | Harmless 0, Normal 1, Expert 2, Custom its slider |
| event tier | `GameManager.Instance.EventSystem.Tier` | 0-3; rises as the story is reported, back to 0 for a quiet stretch |
| story progress | `GameManager.Instance.SequenceHandler.CompletedTasksCount` | tasks completed in this save; only rises |

`AnomalyDifficulty` = `Harmless` / `Normal` / `Expert` replaces the frequency with 0 / 1 / 2.

**What is allowed** (`AnomalyDirector.Allows`):

| Severity | Allowed when |
|---|---|
| strange | frequency above 0 |
| scary | frequency above 0, and `NormalScaryTasks` (1) story task done; on Expert `ExpertAllTasks` (1) |
| extreme | frequency above 0, and `NormalExtremeTasks` (3) story tasks done; on Expert `ExpertAllTasks` (1) |

So Harmless gets none. Normal escalates with the story: strange from the start,
scary after the first task, extreme after the third. Expert starts strange and allows scary and
extreme together after the first task. Progress, not the tier, gates them: the tier drops back to 0 for a quiet
stretch, which would make the buddy tamer halfway through the story. The tier still raises the chance
and leans the draw scarier.

**When.** Every `CheckSeconds` (60 s), after `FirstCheckSeconds` (300 s) and outside the cooldown, one
roll with chance `(0.05 + 0.07 f + 0.03 t max(0.5, f)) x AnomalyFrequency` (f frequency, t tier):
0.12-0.21 on Normal, 0.19-0.37 on Expert; Harmless never rolls. The cooldown after one is
`CooldownSeconds` (600 s) / (1 + 0.25 t) / (1.5 on Expert) / `AnomalyFrequency`. On Normal that is
roughly one every 15-20 minutes.

**Which.** A random buddy that is free (`AnomalyReady`: awake, inside, calm, not hiding, talked to, on
an errand or a goto), or one left on a station (`MoveReady`), which can only act out [`Move`](#move). Allowed kinds are drawn by their own weight times a severity weight that leans
scarier with the tier. A kind that happened in this save is never drawn again ([§2](#once-per-save)).
Once every kind allowed has happened, the buddy is itself for good. A kind that does not fit here (its
start conditions below) is dropped and the draw repeats. If none fits, the next roll comes in
`RetrySeconds` (20 s).

Never while the talk window is open, you are outside, the Breathless is within `MonsterClearance`
(25 m) of you, or another buddy is acting one out.

---

## 2. The anomalies

| Kind | Severity | Starts when | What happens | Ends |
|---|---|---|---|---|
| `Whisper` | strange | within 8 m, same vessel | says something unsettling | at once |
| `FakeCommand` | strange | the window is not open on it | its talk log gains an order you never typed | at once |
| `WrongName` | strange | - | the talk window opens under the wrong name, once | when next opened, or 15 min |
| `Vanish` | strange | out of your sight, 6 m+ | gone; comes back elsewhere | see below |
| `Noises` | strange | behind you, 1.2-7 m, unseen | clicks, odd noises (`Odd`), never the same twice, each after the last has ended and `NoisesGapMin`..`Max` (1.5-3 s) of silence; scary and worse add a `Creature` sound, cut short | 2-3 sounds, or you turn round |
| `WindowStare` | strange | a window within 20 m on its vessel | walks to it and stares out | 50-140 s |
| `WallStare` | strange | a wall within 4 m | walks up to it and faces it | 50-140 s |
| `BotTalk` | strange | docked at the Shipyard, you on the ship side, unseen | talks with the station's robot; looks round as you come, then runs off | see below |
| `Bloody` | scary | out of your sight, 3 m+, no suit | its suit is spattered with blood; it goes about its day but takes no orders | see below |
| `ClosetAmbush` | scary | out of your sight, 4 m+ | hides in a closet, jumps out with a shriek | see below |
| `ShutDoors` | scary | you and it aboard, 2+ doors open | walks a round of the open doors, nearest next, and shuts each behind itself | the round, at most 6 doors |
| `Meat` | scary | docked at the Shipyard, 8-60 m from its cryo room's door, unseen, no suit | bloody in the cryo room over raw meat and blood; stares when you open the door, runs when you step in, then vanishes | see below |
| `Move` | scary | left on a station that is switched off; you aboard another station | turns up near you, out of your sight, and waits facing you; found, it says you left it | until you see it, at most 300 s |
| `Pipe` | scary | out of your sight, 5-30 m, Follow or Wander, hands empty | comes up to you holding a bloody pipe, not answering; the moment you spot it, puts it down without a word | until you spot it, at most 120 s |
| `Smile` | scary | no suit | the next time you look it in the face, a bloody grin on its visor for a blink | see below |
| `Statue` | scary | 3-30 m, Follow or Wander | follows you, but only while you are not looking; freezes, staring, when you do | 50-90 s, once unseen |
| `Stalker` | extreme | out of your sight, no suit | `Bloody` and `Statue` together, with up to `StalkerSounds` (3) different sounds at your back; ignores you; opens pin-code doors without the code | 50-90 s, once unseen |
| `BehindYou` | extreme | out of your sight, 6 m+ | vanishes, then stands right behind you and speaks | when you turn round, or 15 s |
| `UnderTheSuit` | extreme | no suit | as `Smile`: raw flesh where the suit was, a face behind the glass | see below |
| `Sleeper` | extreme | you and it aboard your ship, it in Follow or Wander within 25 m, you 8 m+ from its capsule | its cryo capsule is shut again; walk up to it and it opens on the buddy, asleep inside, while it follows you all the same | see below |

An order, or a task you give it, ends most of them. It does not end:

- `Vanish`, `Stalker`, `BehindYou`, `Bloody`, `Meat` and `Pipe`: it is not listening. These, `Statue` and `BotTalk`
  refuse the talk window and every order and task, from the window or the console (`IgnoresYou`).
- `Smile`, `UnderTheSuit` and `Sleeper`: they play out elsewhere or on its looks. It answers
  and obeys as usual, and they go on.

Each row of the catalogue (`Anomalies.All`) carries these as `AnomalyTraits`: `IgnoresYou`, `Deaf` (not
listening), `Background` (those three and the blood), `Lasting` (counts unseen, [below](#once-per-save)) and
`OwnCue` (witnessed only at its own moment).

Fear ends any of them but a vanish, the blood and those three
([fear-owns-the-buddy](invariants.md#fear-owns-the-buddy)). So does deadly air
([survival-outranks-an-order](invariants.md#survival-outranks-an-order)). A running anomaly keeps the
decider standing down, except those that live in the background (`AnomalyInBackground`: the blood before
it runs off, and the three).

The `Stalker` sets NPC.Core's `NpcAgent.KnowsEveryCode` while it runs: a pin-code door you shut behind
you does not stop it. No code is learned, and `Locked` doors still stop it.

### Once per save

Every kind happens once in a save. Every spoken line, fake order and wrong name is used once per save
too; with a pool used up, `Move` and `BehindYou` stay silent.

`AnomalyMemory` holds both lists. They go into the `.buddy` sidecar with every save, and a load reads
them back; a new game, or a save without a sidecar, starts clean. A kind nobody saw, that left
nothing behind, is forgotten when it ends and may come again: a flicker you never looked at, a capsule you
never looked into. `Meat`, `Pipe` and `Move` always count, for the meat, the pipe and the buddy are
where they were left. `buddy_anomaly <kind>` plays a kind whatever the memory says; `buddy_anomaly forget`
clears it.

### Vanish

The renderers it had on are switched off, its controller stops colliding, and the agent is `Asleep`:
no AI, no catch, no air damage. `BuddyAgentSettings.ShowOnLifecare` turns false, so the scanner loses it
too. After 40-110 s it comes back at a ground node on your vessel, 6-16 m from you, that you cannot see.
If there is none, it waits, then comes back where it vanished once you look away. Its sidecar position
is where it vanished; a load brings it back there.

It will never walk through the doors it owes a close, so it leaves them to close behind it
(`NpcAgent.LeaveDoors`, npc-core:docs/doors.md §7), with the open door it stands in, whoever opened it.

### Bloody

`BuddyGore` copies the texture the body wears (through a render target, so it need not be readable) and
paints 16 dark red blots on its painted texels. The same buddy gets the same stains each time. The blood
lasts `BloodySeconds` (150 s), and every second you look at it uses up `BloodySeenRate` (15) seconds of
that: the more you stare, the sooner it is gone, as if it wanted to hide it from you. It never goes in
front of you: only once you have not seen the buddy for `BloodyUnseenSeconds` (5 s). A suit going on
ends it; the suit's skin wins. Blood that something else wiped off the materials is painted back.

Bloody, it takes no orders. When its time is up and you keep it in sight, it gets away from you: a run
at flee speed as `BotTalk`'s, to a node out of your sight if there is one, tried every
`BloodyRunRetrySeconds` (6 s). There it stands still until it is clean. The Breathless or deadly air
stop the run; the blood stays on.

### ClosetAmbush

The hide is [fear.md §6](fear.md#6-hiding-in-a-closet-or-locker)'s, with `hideAmbush` set. Inside, it
jumps out once you come within `AmbushTriggerDist` (1.5 m) of the spot on its deck, or open the door. The
doors open and it steps out at once, with a shriek. If you never come within `AmbushMaxSeconds` (240 s), it gives up: it leaves the closet without jumping, as nobody is there to scare. The Breathless
turns it into a real hide, and it stays in.

### FakeCommand

Through NPC.Core's `NpcInteraction.AddLine`, two lines go into its log: `$ let him in` drawn as yours,
and its reply. You find them the next time you open the window. The pool darkens with the severity
allowed ([§3](#3-what-it-says)).

### WrongName

The window's `Title` reads "Buddy 2", "B-UDDY 02" or similar, and it says nothing about it. The next
open is normal again.

### WindowStare and WallStare

A window is a renderer named `Glass*`: every ship and station window block has one. It stands on the
ground node nearest the pane, 0.8-4 m from it, and faces the pane's centre. A wall stare casts eight
rays at chest height and walks straight to the nearest wall that is not a body or an item. The walk
gives up after a time limit.

Standing still, the agent turns an idle body after the brain's `OverrideMovement` (`TryIdleFacing`).
So an anomaly sets where it looks there (`AnomalyFaces`), not in the override, or the turn to you in
Follow undoes it.

### BotTalk

It needs the player ship docked at the Shipyard, you aboard it or on the station `BotTalkMinDist`..`MaxDist`
(12-45 m) from the robot (`ShipyardStation.Bot`), and the buddy in Follow or Wander, unseen. The robot
is content of the hallway, which the game switches off while you are away: it is loaded first, as a sell
run loads its station ([items.md](items.md#4-selling-trash-boxes)). Only the
entry corridor and the ship are that far from the robot, so you come from there. It weighs 2.5: it
rarely fits, and when it does it should win the draw.

| Step | What happens | Next |
|---|---|---|
| placed | moved to the ground node `BotStandMin`..`Max` (1.2-2.2 m) from the robot on your side, out of your sight, facing it | at once |
| talking | turns: its blips, then the robot's own talk sound (`AssistanceBot.PlayTalkSound`) every `BotBlipGap` (0.035 s) for 0.8-2.4 s with its eye moving. The robot looks at it | you within `BotTalkCloseDist` (2.5 m), or seeing it within `BotTalkGlanceDist` (4 m) |
| looks round | faces you for `BotTalkGlanceSeconds` (1.2 s); the robot looks at you too | then |
| runs off | at flee speed to a node `RunOffMinDist`..`Max` (6-20 m) away, `RunOffGain` (4 m) further from you than it was and not past you, out of your sight if one is | there, or `RunOffLegSeconds` (20 s) |
| gone | waits until you have not seen it for `RunOffUnseenSeconds` (3 s) | the anomaly ends; it comes back to you as usual |

The run is the one `Bloody` uses too (`RunFrom`). If you never come, it ends after `BotTalkWaitSeconds` (240 s) and walks back to you. If you undock
first, it is put back where it stood aboard: you were leaving, not coming in. The end puts the robot
back as the game leaves it when you walk off: eye still, looking ahead.

### Meat

It needs your ship docked at the Shipyard, you `CaughtMinDist`..`MaxDist` (8-60 m) from the cryo room's
door, out of sight of it if it is open, and the buddy in Follow or Wander, unseen, without a suit. It
gets there by itself: out of your sight it is moved in, as `BotTalk` is. The cryo room (`YardCryo`) is
loaded first. Its doors come from the doorway detectors, as NPC.Core loads rooms, since the room's own
door list can leave one out; it faces the one nearest you, and shuts them all.

Where it stands: a ground node in clear view of a point `CaughtViewInset` (0.8 m) inside that door,
`CaughtStandMin`..`Max` (2-5.5 m) from it, nearest `CaughtStandDist` (3 m). The node must be on the
cryo room's side of the doorway, by the test the game uses for you. NPC.Core gives a room the nodes nearest
its furniture, and some of those lie in the hallway: a buddy put there is never seen when you open the door.

| Step | What happens | Next |
|---|---|---|
| placed | bloody (`Bloody`'s texture), back to the door. Raw meat lies `CaughtMeatAhead` (0.7 m) beyond it, blood under it, round it and at its feet | at once |
| eating | a feeding sound (`Gore`), 2-4 s of silence after each, facing the meat | you open a door, see it, or come within `CaughtNearDist` (3 m) |
| caught | a creature sound, your stress; it turns and stares at you, at least `CaughtTurnSeconds` (0.6 s) | you step in: on the room's side within `CaughtEnterDist` (3 m) of the door, or within 3 m of it; or you have watched it `CaughtStareSeconds` (3 s). After `CaughtCorneredSeconds` (120 s) it runs if you see it, else it is gone |
| runs | at flee speed, inside the room: a node `CaughtRunMin` (2 m) away and `CaughtRunGain` (1 m) further from you, else 1 m away and no nearer. None: cornered | there |
| cornered | faces you until you look away for `CaughtGoneUnseenSeconds` (1 s), at most 120 s. It never goes before you have seen it, unless it ran | gone |
| gone | a vanish ([§2](#vanish)); it comes back clean | back |

You never come: it ends after `CaughtWaitSeconds` (480 s) and walks back to you, clean. Undocked first,
it is put back aboard. Either way the meat and blood stay.

### The mess and the meat model

The meat and blood are props (`AnomalyProps`): the meat a copy of the game's `Skull` item with the meat
mesh at `MeatScale` (1.5; the Skull's own root is scaled down), the blood a copy of the game's `DirtBlood`
decal prefab, tinted dark red (`BloodTint`, a property block, since `Cleanable` swaps the material as
it is scrubbed). The decal is a one-sided plane: a mirrored (negative-scale) copy faces down and is not
drawn. Each stain and the meat sit on floor probed `MessProbeAbove` (5 cm) above its feet and level with
them: probed from higher, a crate top or a chair seat counts as floor, and a stain hangs in the air. You can pick the meat up and bin it, but
not store or sell it. The blood is scrubbed off as any dirt is, and goes once clean. Neither is ever
saved: a load finds the room clean
([an-anomaly-prop-never-enters-the-save](invariants.md#an-anomaly-prop-never-enters-the-save)). At most
`MaxProps` (16) at once; the oldest go first.

The meat is `YourBuddy/Resources/Meat.bbmodel`, a Blockbench model embedded in the dll and read as it is
(`BbModel`): cubes and meshes (triangles and quads), unrotated, one texture, 1 unit = 1 cm. Edit it in
Blockbench and rebuild. It is a slice of a human thigh: one mesh, skin and yellow fat round the whole
rim, and a 512 x 512 texture (muscle groups, femur, fat, skin), filtered smoothly. Mesh normals are smoothed over shared vertices, so a hard edge
needs split vertices. Its collider is not the mesh but its outline as a
low 16-sided prism: Unity's convex hull keeps at most 255 polygons. The material is matte (no highlight or
reflection), as flesh is not glossy.

### Pipe

Out of your sight a copy of the game's `Metal_Pipe` is made at its hands, its top end painted with
blood (`BuddyGore.BloodyEnd`), and the next frame it takes it up: the item's own `Start` puts it back where
its data says, so a same-frame pick-up would be undone. It holds it low, bloody end down and forward
(`PipeHold`).

| Step | What happens | Next |
|---|---|---|
| coming | walks up to you as a Follow does, taking no orders, and stands at your back | you spot it |
| the drop | puts the pipe down in front of it at once, without a word; within `PipeStartleDist` (4 m), your stress | the end |

Never spotted in `PipeGiveUpSeconds` (120 s), it puts the pipe down unseen.

Fear, bad air, or the pipe leaving its hands ends it early; the pipe goes down where it stands. The pipe
is a prop, as the meat is ([the mess](#the-mess-and-the-meat-model)): "Bloody pipe", binned but never
stored, sold or saved.

### Move

A buddy left on a station is parked: the game switches the station's interior off when you undock, and
the buddy rides it ([NPC.Core](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#an-npc-rides-its-own-floor)).
Nothing of it runs, so the director asks `MoveReady` of it instead: not on the ship, not dead or asleep,
and parked.

It needs you aboard another station: never your ship, which it would have needed to come with you, nor
world geometry or its own station. Getting there takes an undock, a flight and a dock, so it is never
left behind only a moment. It is put on a node `ReappearMinDist`..`Max` (6-16 m) from you, out of your
sight, as a vanish comes back. It is placed while still parked, then rides your station's frame, which
wakes it. Why not `MoveTo`: a buddy parked since its load never ran its agent's `Start`, so the agent has no
controller for `TeleportTo` and no `OriginToFeet` yet; the feet offset comes from its own
`CharacterController`. It forgets any order it had there, Stay included, and is in Follow.

| Step | What happens | Next |
|---|---|---|
| waiting | stands still, facing you | you see it, or `MoveWaitSeconds` (300 s) |
| found | says you left it (`AnomalyLines.LeftBehind`), a line not said before; within `MoveStartleDist` (4 m), your stress | the end: it follows you |

Orders end it, as most do.

### Smile and UnderTheSuit

Armed, it waits up to `FlickerWaitSeconds` (240 s) for you to look it in the face: its head within
`FlickerLookAngle` (25°) of the middle of your view, `FlickerMinDist`..`Max` (1.5-7 m) away, in sight,
and it facing you within `FlickerFacingAngle` (50°). Then its skin flickers for a blink and it is itself
again. `Smile`: on 0.2 s, off 0.12 s, on 0.12 s - a smile drawn in blood on the visor: arched eyes and
a curved mouth. `UnderTheSuit`: on 0.12 s - raw muscle where the suit was, a grey face with
hollow eyes pressed behind the glass. Your stress, a little.

The look is what it wears now (`BuddyGore.Overlaid`) with an embedded overlay painted over it, at the
overlay's resolution: `FlickerSmile.png` (512 px) and `FlickerFlesh.png` (256 px), in the 64 x 64 suit
atlas's layout. The visor's front is two texels wide and mirrored, so only an upscaled copy can hold a
face, and every face on it is symmetric. They are painted from the player mesh and its
UVs: each texel gets the colour of the 3D point it lands on. A suit going on ends it.

### Sleeper

The capsule it woke in on a new game ([game-model.md](game-model.md#the-cryo-room)) is shut again, its
monitor on with a pulse, and a copy of the buddy (`BuddyDouble`, in its own colours) stands asleep
inside, where it slept. The buddy itself keeps following you. It needs a capsule it woke in: a buddy
spawned from the console or at the Shipyard has none. The capsule's door is opaque, so you cannot see in:
it opens for you.

| Step | What happens | Next |
|---|---|---|
| asleep | the capsule waits, shut | you come within `SleeperOpenDist` (2.5 m) of the sleeper's head, it within `SleeperOpenAngle` (60°) of your view; or `SleeperWaitSeconds` (900 s) |
| opening | the door opens as the buddy's did on waking, with the pod's sound | open |
| found | your stress. The buddy stands still where it is, facing you | you see it, or `SleeperStareSeconds` (30 s) |
| empty | once you have not looked at the sleeper for `SleeperGoneUnseenSeconds` (1 s) (within `SleeperSeeDist` 6 m and `SleeperLookAngle` 45°), it is gone and the capsule open and dark again | the end, once the buddy is seen too |

The head is the top of the copy's drawn body. The capsule is the sight test's target, so its own walls never
block the look. An end any other way, a load included, leaves the capsule open and empty: the sidecar
keeps it open.

### ShutDoors

The open room doors aboard, airlocks, locked and password doors left out, at most `MaxDoors` (6),
nearest to the buddy next. For each door it plans to a ground node past the doorway, on the side
away from it, at least `DoorClearDist` (1.8 m) from the door, and hands the door to the agent with
NPC.Core's `CloseBehind`. The
agent shuts it once the buddy has walked through and is clear of the doorway, and waits while you stand
in it ([close-only-what-you-walked-through](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#close-only-what-you-walked-through)).
Past the door it stands facing it until it shuts, at most `DoorWaitSeconds` (4 s), then goes on. The
agent never closes a door while the buddy itself is within its 1.4 m of it, and the anomaly holds the
body still: so a stand nearer than `DoorClearDist` would wait for ever. Still that near, it steps once
straight further out. The doorway's axis is whichever of the gate's own two walks clear both ways from
its middle, `DoorPassDist` (1.6 m) out, else 1.1 m. A door it cannot plan through is skipped with a level-1 line naming it and why; a leg gives
up after `DoorLegSeconds` (25 s). Once through the list it goes round again for the doors still open:
up to `MaxDoorPasses` (3) passes, the third only if the second shut something. The end line names any
door left open. `CloseBehind` arms each door afresh from where the buddy stands, so a close owed from an earlier
walk cannot leave it waiting for a crossing that already happened the other way.

---

## 3. What it says

`AnomalyLines.cs` holds the pools: spoken lines by severity, fake orders and their replies, wrong names.
None is a joke, and each is used once per save ([§2](#once-per-save)).
A spoken line goes through NPC.Core's `NpcInteraction.Speak`: a speech panel in the talk window's
look, low in the middle of the screen, wrapped, for 3-8 s by length. The line also goes into the talk
log, and the station robot's talk blips play at the buddy as its voice: one per three letters, 4-16 of
them, `BlipGapMin`..`BlipGapMax` (0.07-0.12 s) apart, as its own typewriter plays them. Lines lean toward the worst
severity allowed.

---

## 4. Being seen

`PlayerView.Sees`: within `ViewHalfAngle` (55°) of the camera's forward, within `ViewRange` (40 m), and a
clear `NavProbe.CanSee` from the camera. Shut doors block it
([sight-stops-at-a-shut-door](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#sight-stops-at-a-shut-door)).
The buddy counts as seen when its chest or its head is. Behind you means more than `BehindAngle` (115°)
off the view. The running anomaly samples this every 0.1 s.

---

## 5. Sounds

FMOD events borrowed from the game, read off the first instance of their owner in the scene
(`GameInternals.ScareSoundAccess`) and kept until the world resets, and the mod's own clips:

| `ScareSound` | Events and clips |
|---|---|
| `Voice` | `AssistanceBot.talkSound` |
| `Click` | the robot's talk blips, `Gate.closeFailSound` |
| `Odd` | `Cleanable.cleanSound` (the brush), clips `odd_*`: a crack, a rip, knocks, a latch |
| `Gore` | clips `gore_*`: tearing, a splash, a snap, wet pops |
| `Creature` | `Breathless.movingSound`, `BreathlessActivity.sound`, `RandomSound.sound`, `BackgroundSound.scarySound` |
| `Shriek` | `Breathless.screechSound`, `UnsealScream.screamSound` |

A category found empty is looked for again after 60 s, and is silent until then. `Creature` and
`Shriek` can run for many seconds, so they are cut short with a fade after `SoundCapSeconds` (2.5 s).

A pick is never one of its category's last `RecentCount` (2) picks, so three sounds in a row from one
category all differ.

The clips are mono mp3s in `YourBuddy/Resources/Sounds`, embedded in the dll and found by their prefix.
`ModSounds` plays them through FMOD's core API on the game's sfx bus, so the sfx volume applies. They
fade out with distance to `MaxDistance` (20 m). Studio does not place core sounds, so while a clip
plays the core listener is moved to the game's each frame. A clip you could not see, by the test for
being seen ([§4](#4-being-seen)), is muffled: a low-pass at `MuffledCutoff` (600 Hz), at `MuffledVolume` (0.6).

`ScareSounds.Play` says how long the sound lasts: a clip's length, an event's from its description
(1 s when unknown), a capped one at most `SoundCapSeconds`. Each sound at level 2 logs
`[anomaly] Sound <category>: <event path or clip> (<seconds>)`.

---

## 6. The player's stress

`AnomalyDirector.Startle` adds stress through one `StressSource` of the mod's own, once per anomaly:
ambush 35, behind you 30, stalker 25 (close), shriek 25, statue 15 (close), creature sounds 8. It never sets threat, which is what can kill the player.

---

## 7. Testing

| Command | Does |
|---|---|
| `buddy_anomaly [@who]` | the director's state, what this save has had, the sounds found, the buddy's current one |
| `buddy_anomaly list` | every kind, and why one is not drawn now: it happened already, the difficulty, or the story tasks it waits for |
| `buddy_anomaly <kind> [@who]` | that one now, whatever the chance, cooldown and severity; still says why when it does not fit |
| `buddy_anomaly end [@who]` | ends the running one |
| `buddy_anomaly roll` | a draw now, as the director would, with a certain hit |
| `buddy_anomaly forget` | this save forgets which kinds and lines it had |

Log tag `[anomaly]`: starts, ends and lines at level 1; draws that did not fit, the chance and the sounds
found at level 2.

```
[anomaly] Buddy: vanishing (strange, 9.4m from you, out of sight)
[anomaly] Buddy is still gone - waiting for a spot out of your sight
[anomaly] Buddy is back, 11.2m from you
[anomaly] Buddy: vanishing over - back
```

---

## 8. Known limitations

- **The blood lands anywhere on the atlas**, face included: `Bloody`'s blots are random, not aimed.
- **A statue does not hurry.** The agent caps its speed factor at 1, so unseen it walks at its usual pace.

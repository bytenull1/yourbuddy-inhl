# The game's own model

How *Isolated Inhale* itself works, where only the buddy needs it, read from a decompile of its
assembly. Line references are to an ILSpy dump of **v0.8.9** and shift on other builds. `decompiled/`
is not in the repository - generate it locally ([`decompiled/README.md`](../decompiled/README.md)).

What every NPC needs - an NPC is not a `Player`, there are no room volumes, the world moves around the
ship, gates, the Breathless, and the player's own air and footstep rules an agent borrows - is NPC.Core's
[game-model.md](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/game-model.md). Read that first.

The buddy is the player prefab cloned by `YourBuddyPlugin.SpawnBuddy`, with every non-whitelisted
MonoBehaviour, `Player` included, destroyed
([an NPC is not a Player](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/game-model.md#1-an-npc-is-not-a-player)).

---

## The cryo room

A new game is `SaveData.worldTime == 0` at `GameManager.Start` (`GameManager.cs:157`), which calls
`CryoController.TrySpawnPlayer`: the player spawns at the first empty pod.

`ShipyardStationParts/Content/YardCryo/YardCryoContent` holds five capsules, `CrioCapsule1`–`5`
(x −10.24, z 25.55–28.80, yaw −90°). Only **`CrioCapsule2`** is a real pod (`CryoPod`,
`CryoPodAnimator`). The others are props with a door nothing drives; 3–5 have an always-lit monitor,
and `CrioCapsule1` is saved open. The real pod's animator slides the door to local z −0.061, then
lifts it to y 1.08, each phase 1/0.8 s (`CryoPodAnimator.cs:85-113`). Capsules stand on a 0.62 m deck
reached by stairs at its north end (bundled nodes #274–#276).

`CryoPod.Data.enabled` means "occupied, shut", so opening a real pod writes the player's save -
[mod-state-never-enters-the-vanilla-save](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/invariants.md#mod-state-never-enters-the-vanilla-save).
The pod asks for a name only while the nickname is empty or `Unknown`, and saves at once on confirm.

The station origin, `ShipyardStationParts` (0, 0, 26.87), is the `YardHallway` floor (bundled nodes
#228, #231).

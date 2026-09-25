# Doors

The buddy walks on NPC.Core's `NpcAgent`, which opens, waits for and closes doors, loads the rooms
around them and routes around what it cannot open:
[NPC.Core's doors.md](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/doors.md). This page is
what YourBuddy adds.

---

## 1. Settings

`AutoDoors` (General) is the agent's `CanOpenDoors` (`BuddyAgentSettings`). Off, the buddy opens no
door and owes no close; it still routes through doors that are open.

---

## 2. Password doors

The player gives a code through the [dialog](dialog.md) or `buddy_password`; it goes to NPC.Core
(`NpcDoors.LearnCode`), so every NPC knows it and it is saved with the game
([its password doors](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/doors.md#password-doors)).
The agent then opens that door through its own panel
([§6](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/doors.md#at-a-password-door)).

---

## 3. Rooms the buddy keeps loaded

Beyond the agent's door rule
([which rooms an NPC loads](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/doors.md#which-rooms-an-npc-loads)):

| Room | Loaded when |
|---|---|
| an errand's box or sell station room | the errand needs it (`IErrandBody.LoadRoomOf`) |
| a room holding a sell station | always, while `SellTrash` is on: loaded in the brain's slow phase 0, and kept on through `SellRoomsKeeper` ([a-sell-station-room-stays-loaded](invariants.md#a-sell-station-room-stays-loaded)) |

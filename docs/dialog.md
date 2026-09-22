# Dialog - giving the buddy orders

`BuddyDialog.cs` (the window), `DialogSkin.cs` (drawing), `BuddyDialogCommands.cs` (parsing),
`BuddyRooms.cs` (room names) and `BuddyCommands.cs` (the orders). Config: `Dialog` (General, default
on). There is no hotkey; the game's Interact binding is the only way in.

---

## 1. Opening it

Look at the buddy and press **Interact**. `BuddyDialog` listens to
`GameManager.Instance.InputHandler.OnInteract` and checks:

| Gate | Why |
|---|---|
| within `TalkRange` (2.4 m) | shorter than the player's reach, so using a keypad is not talking to the buddy |
| inside a `LookAngle` cone (30°) | measured to the nearest point of the buddy's capsule, not its chest |
| `!PlayerIsBusy` | the player is not aiming at or holding an `Interactable` - **the game's interaction wins**. Read from `PlayerController.focusedInteractable`; if a game update breaks that field, a ray along the view within reach decides instead, so a lost field never hands terminals' keypresses to the dialog |
| `!SightBlocked` | one ray on `ProbeLayers`; without it the window opens through walls |

While the window is open the buddy stands still and faces the player (`inDialog`). The mod frees the
cursor and calls `InputHandler.SwitchToUIInput()`, like `AssistanceBot.Talk`, and reverses both on
close (**Escape** or the close box).

Deliberately **not** used:

- **The game's `DialogMenu`** - it is typed to `AssistanceBot` and reads the bot's data and animator.
- **A game `Interactable` on the buddy** - it needs a serialized `outlines` object the mod cannot
  fill, and `ThrowInteractionRaycast` never picks the buddy's `CharacterController`. So selection is
  an angle test; the ray only checks for walls.

`InputHandler` clears its listeners on teardown and a scene load brings a new one, so the
subscription is re-checked every frame.

---

## 2. The panel

Styled after the game's terminal dialogs: a framed near-black panel on the right, a title bar with a
close box, a message log, and a bottom row of *commands toggle · text field · send*. `DialogSkin`
builds it from 1×1 fills and the game's own font and sprites, so the mod stays a single DLL.

**Font:** the game's `Pixellari` (the AssistantBot dialog font), found among loaded fonts by name and
retried every 5 s; Consolas until then. Logged once when found. Title `TitleSize` 32, text `TextSize`
24 - change `TextSize` to rescale. No synthesized bold/italic: it smears a pixel font.

**Sprites** (loaded from `Resources`, point-filtered):

| Sprite | Used for |
|---|---|
| `textures/ui/interfaces/ColorButton` | buttons and the text field frame, nine-sliced |
| `textures/ui/interfaces/ColorButtonPressed` | a held button |
| `textures/ui/icons/Arrow2` | send (tinted `AcceptTint`, green) |
| `textures/ui/icons/Cross` | close |
| `textures/ui/icons/Chat` | commands toggle |

Sizes are in UI pixels: `DialogSkin.UiScale` = screen height / 540, rounded. A sprite that fails to
load falls back to a flat frame and a text label. Every rect is rounded to whole pixels to avoid blur.

IMGUI notes: build styles inside `OnGUI` (they read `GUI.skin`), and give generated textures and fonts
`HideFlags.HideAndDontSave` or they are lost on scene load. This panel has controls, so it runs on
every event and must **not** be gated on `Repaint`
([read-only-panels-build-on-repaint](invariants.md#read-only-panels-build-on-repaint)).

---

## 3. The orders

`BuddyDialogCommands.Run` keyword-matches like the game's `AssistanceBot` ("follow" and "follow me"
both work). A bare number is a door code. Everything calls `BuddyCommands`, the same code the console
uses.

Matching order matters - it is a substring test ("trash box" contains "trash"). The one exception
is a goto that names a room, which is tried right after "decide" ("go to the workshop" contains
"work"):

| Word | Console | Effect |
|---|---|---|
| decide / yourself / your call | `buddy_auto on` | `RevokeOrder` ([behaviour.md](behaviour.md)); matched **first** |
| hide / closet / locker / conceal | `buddy_hide` | hide and stay until the next order ([fear.md §6](fear.md#6-hiding-in-a-closet-or-locker)) |
| follow / come | `buddy_follow` | `ApplyOrder(Follow)` |
| wander / job | `buddy_wander` | `ApplyOrder(Wander)` |
| stay / wait / stop | `buddy_stay` | `ApplyOrder(Stay)` |
| sell / trash box / money / cash | `buddy_sell` | sell nearby trash boxes ([items.md §4](items.md#4-selling-trash-boxes)); before tidy |
| tidy / clean / trash / rubbish / garbage / litter / bin | `buddy_tidy` | a tidying round ([items.md §3](items.md#3-tidying)) |
| play / toy | `buddy_play` | a play session ([items.md §5](items.md#5-idle-play)) |
| snack / eat / food / hungry | `buddy_snack` | eat or drink something nearby ([snacks.md](snacks.md)) |
| goto *room* | `buddy_goto <i>` (a node, not a room) | `FindPath` + `ApplyRouteOrder` ([below](#goto-by-room)) |
| password *nnnn* | `buddy_password <code>` | adds the code to `knownPinCodes` |

**Follow, Wander, Stay, Goto and "decide" are orders.** They are recorded as the order in force
([an-order-is-not-a-mode](invariants.md#an-order-is-not-a-mode)). Given during a flee, an order waits
until the flee ends, and the reply says so ([fear-owns-the-buddy](invariants.md#fear-owns-the-buddy)).

**Hide, Sell, Tidy, Play and Snack are tasks.** They start now, skipping schedule and config switch, and the
buddy returns to its order afterwards. They refuse while asleep, scared, busy or on a goto. Hide is
the exception: it works while Alert, and it interrupts an errand the buddy chose itself
([a-command-outranks-an-errand](invariants.md#a-command-outranks-an-errand)). An ordered hide ends
only on the next order ([an-ordered-hide-ends-only-on-an-order](invariants.md#an-ordered-hide-ends-only-on-an-order)).

`Stay` holds position, but still steps out of a doorway it blocks
([step-off-applies-in-every-mode](invariants.md#step-off-applies-in-every-mode)).

### Goto by room

The dialog sends the buddy to a **room**, not a node number; `buddy_goto` keeps node indices for
debugging. Rooms are those of the station the ship is docked to (`SpaceStation.rooms`), under the
names the debug HUD shows (`YardLibrary`, `OxygenKitchen`). Case, spaces and the station's common
prefix are ignored, and so is a partial name that is unique: "goto library", "go to the Yard Library".
A partial name that fits several rooms is answered with the candidates, and "goto" alone (the
**Goto** button too) lists the rooms.

A room has no volume ([game-model.md §2](game-model.md#2-there-are-no-room-volumes)) and a station's
floors are not under its rooms, so `BuddyRooms` gives each node to the room whose furniture (every
transform under the `Room`) is nearest to it. The room's nodes are tried nearest its middle first, up
to four, so one dead-end node does not strand it. A room with no node is not listed. The
`[nav] Rooms at <station>` line names the first node chosen for each room, by the index `buddy_goto`
takes, so a wrong pick can be tried from the console.

Orders and mode are not saved; a loaded buddy starts in Follow with no order. Door codes are saved.
The password reply says whether any door in the scene uses that code, so typos show at once.

# Logging - capturing and trimming evidence

Logs are the main evidence for bugs here. They are also mostly boilerplate, so capture deliberately
and trim before sharing.

---

## 1. Levels

`debug_level <0-3>` in the game console, or `[Debug] DebugLevel` in NPC.Core's config: one level for
every NPC mod ([NPC.Core logging](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/logging.md#1-levels)).

| Level | Adds |
|---|---|
| 0 | warnings and errors only |
| 1 | state changes, doors, deaths, blocked colliders |
| 2 | **reasoning**: every replan and why, seed lists, route chains, gate inventory and audits |
| 3 | per-frame obstacle dumps - very noisy, only for steering problems |

**Use level 2 for navigation bugs.** Level 1 shows the buddy failing, not what it decided.

`buddy_dev visuals on` toggles on-screen debug *visuals*; it does not change log verbosity.

---

## 2. Subsystem tags

| Tag | Source | Typically logs |
|---|---|---|
| `[ai]` | NPC.Core's `NpcAgent`; `BuddyBehaviour.*` | the agent: replans, doors, rooms loaded, stuck recovery, step-offs, blockers, riding and parking, death; the brain: routes finished or given up |
| `[fear]` | `BuddyBehaviour.Fear.cs` | fear state, stress trace, holding back, retreats |
| `[mind]` | `Autonomy.cs`, `Mind.cs` | decisions, orders revoked or expired, why the decider stood down |
| `[store]` | `StoreErrand` | storage searches, deliveries and reasons for refusing placement |
| `[store-detail]` | `StoreErrand.Diagnostics`, repacking | eligibility, ranked destinations, timed stage snapshots and repack refusal/cancellation reasons |
| `[resources]` | `ResourceDuty`, `ResourceErrand` | refill targets, blocked duties, purchases and remaining allowance |
| `[suit]` | `BuddySuit.cs`, `EvaRun.cs`, `SuitFetchErrand.cs` | suiting up and down, the airlock trips, waiting for your cycle, a lost EVA skin, the suit fetch |
| `[nav]` | NPC.Core `NavGraph` | seeding, route chains, path failures |
| `[probe]` | NPC.Core `NavProbe`, `SceneScan` | probe mask, gate inventory, gate-frame audits, full buffers, scene rescans |
| `[anomaly]` | `AnomalyDirector`, `BuddyBehaviour.Anomaly.cs`, `ScareSounds` | anomalies started and ended, lines said, a vanish waiting to come back; draws that did not fit and sounds found at level 2 |
| `[mgr]` | `BuddyManager`, `BuddyCryoSpawn` | spawns, restoring buddies from a save, the new-game wake-up |
| `[editor]` | NPC.Core `NodeEditor` | node and link placement |
| `[internals]` | `GameInternals` | missing game members at load, then one summary line |
| `[mod]` | plugin entry | startup, spawns |

NPC.Core logs the moments it owns under its own tags ([its logging](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/logging.md#2-sources-and-tags)):
`[save]` for loads and sidecars written or deleted, `[lifecare]` for scans, `[talk]` for the window,
`[world]` for rooms kept loaded. A line about one buddy still carries that buddy's source.

Tags are short because BepInEx already prefixes every line with its source. The plugin's own lines
come from `YourBuddy Mod`; everything a buddy does comes from that buddy's source, `YourBuddy:<name>`
(`[Info   :YourBuddy:Buddy 2] [ai] Opening door 'Door02'`), static code such as `FindPath` included.
`trimlog` names the buddy on each line when a capture holds more than one, and `--npc "Buddy 2"`
keeps one.

---

## 3. Capture workflow

NPC.Core's [capture steps](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/logging.md#3-capturing-a-log)
apply, with its `tools/trimlog.py` (from a sibling checkout: `python ../npc-core-inhl/tools/trimlog.py`).
It keeps NPC.Core's lines, the buddies' and `YourBuddy Mod`'s.

1. `debug_level 2` in the console.
2. Reproduce the problem - and nothing else.
3. Trim the log, `--stats` first: a dominant line shape is often the finding by itself.

   ```bash
   python ../npc-core-inhl/tools/trimlog.py BepInEx/LogOutput.log --stats
   python ../npc-core-inhl/tools/trimlog.py BepInEx/LogOutput.log --fold > capture.txt
   ```

4. **Add a one-line note where the behaviour changed** ("here it goes back down").

`--fold` keeps the first and last of each repeated shape: a position that never changes across
replans is a livelock; one that swings is a ping-pong.

---

## 4. Rules for adding logs

- **Any state machine that can stall must say why**, throttled, naming the blocker and the decision.
  A silent wait looks exactly like a forgotten one.
- Log the **inputs** to a decision, not only the outcome. `via #98 -> goal` says what was chosen;
  `2 visible entry seeds: #102(1,4m, +1,0y) #98(4,3m, +0,4y)` says why.
- Print values in the units the rule uses - e.g. deck difference, not raw Y.
- Throttle anything reachable every frame (2–5 s), and dedupe by subject, never with one global
  throttle.
- One line per decision. Multi-line dumps break `--stats` and `--fold`.
- **Buddy code runs inside `BuddyManager.Acting(buddy)`.** `YourBuddyPlugin.Log` writes to the acting
  buddy's source, so a new entry point - a Unity message, a game event, a patch, a command - opens
  that scope. A coroutine logs through `buddy.LogSource`: a scope must never outlive a `yield`. A line
  logged outside any scope carries the plugin's source - nameless, never misattributed.

---

## 5. Reading guides

- Routes and replans: [navigation.md §7](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/navigation.md#7-reading-a-findpath-capture)
- Doors: [NPC.Core's doors.md §9](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/doors.md#9-what-a-healthy-capture-looks-like)
- Lifecare: [lifecare.md §3](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/lifecare.md#reading-a-capture)
- Fear: [fear.md §8](fear.md#8-reading-a-capture)
- Decider: [behaviour.md §6](behaviour.md#6-reading-a-capture)
- Gate-frame audits: [probes.md](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/probes.md#the-gate-frame-rule)

## Storage diagnostics

`[store-detail]` is emitted at Info level, alongside ordinary storage outcomes. Startup records
its revision and module build ID so captures can be tied to the installed binary.
Each job has a per-buddy sequence number. State transitions and a five-second heartbeat record
item and buddy positions, source and destination, hand distance, probe/orientation, movement
phase, timeout and last swept blocker. Collision failures include collider bounds and sweep
endpoints; the last blocker is historical, not proof that it still obstructs the current phase.

Selection traces explain cooldowns, unsuitable items, home rankings and tidiness, at most once
per relevant item every 30 seconds. Ineligible supplies and items on other vessels are
counted in the scan summary instead of repeated individually. Repack refusals are throttled per target to five seconds. Queue
cancellation names the failed condition, and successful moves name the next queued move.
These traces inspect the existing decision inputs; they do not run additional scene scans or
per-frame physics sweeps. Keep the complete startup and reproduction log before another launch
replaces it. A completed job is distinct from an ended/interrupted leg.

Overflow placement refusals include the last probe reason and position: blocked space, missing ship floor, wrong room or area bounds. A last-probe reason describes that candidate, not every slot. Continuing Tidy, Sell and Play assignments use `[work]` lines for batch starts and retry delays.

`[store-probe]` records every rejected overflow candidate with job, room, orientation, slot, footprint and reason. Probing shares the four-per-frame budget; retries of unchanged failed placements wait for the cache.

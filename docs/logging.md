# Logging - capturing and trimming evidence

Logs are the main evidence for bugs here. They are also mostly boilerplate, so capture deliberately
and trim before sharing.

---

## 1. Levels

`debug_level <0-3>` in the game console:

| Level | Adds |
|---|---|
| 0 | warnings and errors only |
| 1 | state changes, doors, deaths, blocked colliders |
| 2 | **reasoning**: every replan and why, seed lists, route chains, gate inventory and audits |
| 3 | per-frame obstacle dumps - very noisy, only for steering problems |

**Use level 2 for navigation bugs.** Level 1 shows the buddy failing, not what it decided.

`buddy_debug on` toggles on-screen debug *visuals*; it does not change log verbosity.

---

## 2. Subsystem tags

| Tag | Source | Typically logs |
|---|---|---|
| `[ai]` | `BuddyBehaviour.*` | replans, doors, stuck recovery, step-offs, blockers |
| `[fear]` | `BuddyBehaviour.Fear.cs` | fear state, stress trace, holding back, retreats |
| `[mind]` | `Autonomy.cs`, `Mind.cs` | decisions, orders revoked or expired, why the decider stood down |
| `[nav]` | `BuddyNodeGraph` | seeding, route chains, path failures |
| `[probe]` | `NavProbe` | probe mask, gate inventory, gate-frame audits, full buffers |
| `[mgr]` | `BuddyManager` | spawn/save, lifecare scans |
| `[editor]` | `BuddyNodeEditor` | node and link placement |
| `[internals]` | `GameInternals` | missing game members at load, then one summary line |
| `[mod]` | plugin entry | startup, spawns, patch groups applied or failed |

Tags are short because BepInEx already prefixes every line with `[Info   :YourBuddy Mod] `.

---

## 3. Capture workflow

1. `debug_level 2` in the console.
2. Reproduce the problem - and nothing else.
3. Take `BepInEx/LogOutput.log` and trim it:

   ```bash
   python tools/trimlog.py BepInEx/LogOutput.log --stats
   python tools/trimlog.py BepInEx/LogOutput.log --fold > capture.txt
   ```

4. **Add a one-line note where the behaviour changed** ("here it goes back down"). `trimlog` keeps
   free-text lines, and these notes are often the fastest route to the answer.

Run `--stats` first. It shows what the log is mostly made of, and a dominant line shape is often the
finding by itself (e.g. dozens of `FindPath: no clear entry seed`).

| Mode | A 35 KB capture becomes |
|---|---|
| `--stats` | ~3.6 KB - a histogram of line shapes |
| `--fold` | ~11 KB - readable, first and last few of each shape |
| plain | ~30 KB - prefixes stripped, exact duplicates collapsed |

`--fold` keeps the **first and last** of each repeated shape because the drift between them matters:
a position that never changes across replans is a livelock; one that swings is a ping-pong.

Other flags: `--tag nav,ai` to filter by tag, `--all` to keep other BepInEx sources, `-` for stdin.

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

---

## 5. Reading guides

- Routes and replans: [navigation.md §7](navigation.md#7-reading-a-findpath-capture)
- Doors: [doors.md §7](doors.md#7-what-a-healthy-capture-looks-like)
- Lifecare: [lifecare.md §3](lifecare.md#reading-a-capture)
- Fear: [fear.md §8](fear.md#8-reading-a-capture)
- Decider: [behaviour.md §6](behaviour.md#6-reading-a-capture)
- Gate-frame audits: [probes.md](probes.md#the-gate-frame-rule)

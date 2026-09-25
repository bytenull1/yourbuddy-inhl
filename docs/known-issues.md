# Known issues and dead ends

Three lists:

1. **Open issues** - known defects or weak spots, not fixed yet.
2. **Dead ends** - explanations that turned out wrong. Don't chase them again.
3. **Abandoned approaches** - designs that were tried and dropped, and why.

When you fix an open issue, delete its entry and document the result in the topic doc.
Limitations accepted by design are listed in each topic doc, not here.

---

## Open issues

The walking agent's open issues are NPC.Core's
([its known issues](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/known-issues.md#open-issues)).
None are the buddy's own.

---

## Dead ends

Explanations that were checked and are wrong. The navigation ones are NPC.Core's
([its dead ends](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/known-issues.md#dead-ends)).

- **"The buddy's lifecare icon disappears."** It was the *player's* icon, a game bug -
  [lifecare.md §3](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/lifecare.md#3-the-missing-player-icon-game-bug-worked-around).

---

## Abandoned approaches

Navigation went through five designs before the current graph
([NPC.Core](https://github.com/bytenull1/npc-core-inhl/blob/main/docs/known-issues.md#abandoned-approaches)). Also dropped:

- **Walking to better air elsewhere aboard.** Environments are per vessel, so there is nowhere
  better to go. The buddy switches the unit on instead ([terminals.md](terminals.md)).
- **Throwing things at the Breathless.** Not done; nothing is thrown at the monster or the player.

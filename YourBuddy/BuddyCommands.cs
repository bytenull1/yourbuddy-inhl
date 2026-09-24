using System;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The orders the player can give a buddy, in one place. Both the console
    /// commands in Patches.cs and the dialog window call these, so the two surfaces
    /// cannot drift apart. Each returns the line to show the player; the caller picks the
    /// buddy and runs the order inside its BuddyManager.Acting scope.
    /// </summary>
    public static class BuddyCommands
    {
        private const int MaxRoomTries = 4;
        /// <summary>
        /// An order given mid-flee is kept for afterward. docs/invariants.md#fear-owns-the-buddy
        /// </summary>
        private const string OnceSafe = " once it gets away from the Breathless";

        /// <summary>
        /// The refusal for a dead buddy, or null for a living one.
        /// </summary>
        private static string? Dead(BuddyBehaviour buddy) => buddy.IsDead ? buddy.Name + " is dead" : null;

        public static string Follow(BuddyBehaviour buddy)
        {
            if (Dead(buddy) is { } dead) return dead;

            return buddy.ApplyOrder(BuddyMode.Follow) ? buddy.Name + " now follows you" : buddy.Name + " will follow you" + OnceSafe;
        }

        public static string Wander(BuddyBehaviour buddy)
        {
            if (Dead(buddy) is { } dead) return dead;

            return buddy.ApplyOrder(BuddyMode.Wander)
                ? buddy.Name + " now does its own thing (uses nav nodes as points of interest)"
                : buddy.Name + " will do its own thing" + OnceSafe;
        }

        public static string Stay(BuddyBehaviour buddy)
        {
            if (Dead(buddy) is { } dead) return dead;

            return buddy.ApplyOrder(BuddyMode.Stay) ? buddy.Name + " stays here" : buddy.Name + " will stay put" + OnceSafe;
        }

        /// <summary>
        /// The console's goto: one node, by index, for debugging.
        /// </summary>
        public static string GoToNode(BuddyBehaviour buddy, int nodeIndex)
        {
            if (Dead(buddy) is { } dead) return dead;
            if (nodeIndex < 0 || nodeIndex >= BuddyNodeGraph.NodeCount)
            {
                return "Node index out of range (0-" + (BuddyNodeGraph.NodeCount - 1) + ")";
            }

            return WalkTo(buddy, [nodeIndex], "node #" + nodeIndex);
        }

        /// <summary>
        /// The dialog's goto: a room by name, through the first of its nodes the buddy can reach.
        /// </summary>
        internal static string GoToRoom(BuddyBehaviour buddy, BuddyRooms.Entry room) =>
            Dead(buddy) ?? WalkTo(buddy, room.Nodes, room.Name);

        private static string WalkTo(BuddyBehaviour buddy, int[] nodes, string label)
        {
            // A dead-end node must not strand a whole room, and a failed search is not free.
            for (int i = 0; i < Math.Min(nodes.Length, MaxRoomTries); i++)
            {
                Vector3 target = BuddyNodeGraph.GetNodeWorld(nodes[i]);
                BuddyNodeGraph.NavPath? plan = BuddyNodeGraph.FindPath(buddy.FloorUnderBuddy(), target);
                if (plan is not { Count: > 0 }) continue;

                return buddy.ApplyRouteOrder(plan.Value, target)
                    ? buddy.Name + " walking to " + label + " (" + plan.Value.Count + " waypoints)"
                    : buddy.Name + " will walk to " + label + OnceSafe;
            }

            return BuddyNodeGraph.LastPathBlockedByDoor
                ? "No way to " + label + " that avoids a door I cannot open"
                : "No path to " + label + " - are nodes connected?";
        }

        /// <summary>
        /// Hands the choice back to the buddy: no order in force. docs/behaviour.md
        /// </summary>
        public static string DecideForYourself(BuddyBehaviour buddy)
        {
            if (Dead(buddy) is { } dead) return dead;
            if (!YourBuddyPlugin.ConfigAutonomy.Value)
            {
                return "Autonomy is switched off - 'buddy_auto on', or the Autonomy config setting";
            }
            buddy.RevokeOrder();
            return buddy.Name + " decides for itself now";
        }

        /// <summary>
        /// The Autonomy switch, for the console. Switching it on also revokes every order in
        /// force, or it would visibly do nothing until the next order.
        /// </summary>
        public static string SetAutonomy(bool on)
        {
            YourBuddyPlugin.ConfigAutonomy.Value = on;
            if (on)
            {
                foreach (BuddyBehaviour buddy in BuddyManager.Snapshot())
                {
                    if (buddy == null || buddy.IsDead) continue;

                    using BuddyManager.ActingScope _ = BuddyManager.Acting(buddy);
                    buddy.RevokeOrder();
                }
            }

            return on
                ? "Autonomy: ON - the buddies decide for themselves when no order holds"
                : "Autonomy: OFF - the buddies keep doing what they do until told otherwise";
        }

        /// <summary>
        /// A snack now, for testing: skips the schedule and the Snacks setting. docs/snacks.md
        /// </summary>
        public static string Snack(BuddyBehaviour buddy) => Dead(buddy) ?? buddy.StartSnackNow();

        /// <summary>
        /// Tidying now, for testing: skips the schedule and the Tidying setting. docs/items.md §3
        /// </summary>
        public static string Tidy(BuddyBehaviour buddy) => Dead(buddy) ?? buddy.StartTidyNow();

        /// <summary>
        /// Selling a trash box now, for testing: skips the schedule and the SellTrash setting. docs/items.md §4
        /// </summary>
        public static string Sell(BuddyBehaviour buddy) => Dead(buddy) ?? buddy.StartSellNow();

        /// <summary>
        /// Idle play with loose trash now, for testing: skips the schedule and the ItemPlay setting. docs/items.md §5
        /// </summary>
        public static string Play(BuddyBehaviour buddy) => Dead(buddy) ?? buddy.StartPlayNow();

        /// <summary>
        /// Hiding in a closet now, for testing: skips the fear and the HideInClosets setting. docs/fear.md §6
        /// </summary>
        public static string Hide(BuddyBehaviour buddy) => Dead(buddy) ?? buddy.StartHideNow();

        /// <summary>
        /// What the decider waits for and when it acts next: the HUD's Mind / Air / Snack lines. docs/behaviour.md §3
        /// </summary>
        public static string Mind(BuddyBehaviour buddy) => Dead(buddy) ?? buddy.Name + ": " + buddy.DescribeTimers();

        /// <summary>
        /// Ends the follow or wander bout now, for testing the switch. docs/behaviour.md §3
        /// </summary>
        public static string EndBout(BuddyBehaviour buddy) => Dead(buddy) ?? buddy.EndBoutNow();

        /// <summary>
        /// Switches the oxygen generator or climate control on now, whatever the air. docs/terminals.md
        /// </summary>
        public static string Terminal(BuddyBehaviour buddy, string which) => Dead(buddy) ?? buddy.StartTerminalNow(which);

        /// <summary>
        /// Teaches every buddy a door code. It is used only on a panel whose own code
        /// matches, so a wrong code changes nothing. docs/doors.md
        /// </summary>
        public static string GivePassword(string text)
        {
            if (!int.TryParse(text.Trim(), out int code)) return "That is not a door code";

            BuddyBehaviour.LearnPinCode(code);
            return BuddyBehaviour.AnyKnownDoorMatches()
                ? "Got it - that opens a door I know about"
                : "Noted, but no door I know of uses that code";
        }
    }
}

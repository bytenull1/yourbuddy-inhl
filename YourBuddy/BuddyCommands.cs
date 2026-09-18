using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The orders the player can give the buddy, in one place. Both the console
    /// commands in Patches.cs and the dialog window call these, so the two surfaces
    /// cannot drift apart. Each returns the line to show the player.
    /// </summary>
    public static class BuddyCommands
    {
        private const string NoBuddy = "No living buddy exists";
        /// <summary>
        /// An order given mid-flee is kept for afterward. docs/invariants.md#fear-owns-the-buddy
        /// </summary>
        private const string OnceSafe = " once it gets away from the Breathless";

        private static BuddyBehaviour? Living()
        {
            BuddyBehaviour? buddy = BuddyManager.CurrentBuddy;
            return buddy == null || buddy.IsDead ? null : buddy;
        }

        public static string Follow()
        {
            BuddyBehaviour? buddy = Living();
            if (buddy == null) return NoBuddy;

            return buddy.ApplyOrder(BuddyMode.Follow) ? "Buddy now follows you" : "Buddy will follow you" + OnceSafe;
        }

        public static string Wander()
        {
            BuddyBehaviour? buddy = Living();
            if (buddy == null) return NoBuddy;

            return buddy.ApplyOrder(BuddyMode.Wander)
                ? "Buddy now does its own thing (uses nav nodes as points of interest)"
                : "Buddy will do its own thing" + OnceSafe;
        }

        public static string Stay()
        {
            BuddyBehaviour? buddy = Living();
            if (buddy == null) return NoBuddy;

            return buddy.ApplyOrder(BuddyMode.Stay) ? "Buddy stays here" : "Buddy will stay put" + OnceSafe;
        }

        public static string GoToNode(int nodeIndex)
        {
            BuddyBehaviour? buddy = Living();
            if (buddy == null) return NoBuddy;
            if (nodeIndex < 0 || nodeIndex >= BuddyNodeGraph.NodeCount)
            {
                return "Node index out of range (0-" + (BuddyNodeGraph.NodeCount - 1) + ")";
            }

            Vector3 target = BuddyNodeGraph.GetNodeWorld(nodeIndex);
            BuddyNodeGraph.NavPath? plan = BuddyNodeGraph.FindPath(buddy.FloorUnderBuddy(), target);
            if (plan is not { Count: > 0 })
            {
                return BuddyNodeGraph.LastPathBlockedByDoor
                    ? "No way to node #" + nodeIndex + " that avoids a door I cannot open"
                    : "No path to node #" + nodeIndex + " - are nodes connected?";
            }

            return buddy.ApplyRouteOrder(plan.Value, target)
                ? "Buddy walking to node #" + nodeIndex + " (" + plan.Value.Count + " waypoints)"
                : "Buddy will walk to node #" + nodeIndex + OnceSafe;
        }

        /// <summary>
        /// Hands the choice back to the buddy: no order in force. docs/behaviour.md
        /// </summary>
        public static string DecideForYourself()
        {
            BuddyBehaviour? buddy = Living();
            if (buddy == null) return NoBuddy;
            if (!YourBuddyPlugin.ConfigAutonomy.Value)
            {
                return "Autonomy is switched off - 'buddy_auto on', or the Autonomy config setting";
            }
            buddy.RevokeOrder();
            return "Buddy decides for itself now";
        }

        /// <summary>
        /// The Autonomy switch, for the console. Switching it on also revokes the order in
        /// force, or it would visibly do nothing until the next order.
        /// </summary>
        public static string SetAutonomy(bool on)
        {
            YourBuddyPlugin.ConfigAutonomy.Value = on;
            BuddyBehaviour? buddy = Living();
            if (on && buddy != null) buddy.RevokeOrder();

            return on
                ? "Autonomy: ON - the buddy decides for itself when no order holds"
                : "Autonomy: OFF - the buddy keeps doing what it does until told otherwise";
        }

        /// <summary>
        /// A snack now, for testing: skips the schedule and the Snacks setting. docs/snacks.md
        /// </summary>
        public static string Snack()
        {
            BuddyBehaviour? buddy = Living();
            return buddy == null ? NoBuddy : buddy.StartSnackNow();
        }

        /// <summary>
        /// Tidying now, for testing: skips the schedule and the Tidying setting. docs/items.md §3
        /// </summary>
        public static string Tidy()
        {
            BuddyBehaviour? buddy = Living();
            return buddy == null ? NoBuddy : buddy.StartTidyNow();
        }

        /// <summary>
        /// Selling a trash box now, for testing: skips the schedule and the SellTrash setting. docs/items.md §4
        /// </summary>
        public static string Sell()
        {
            BuddyBehaviour? buddy = Living();
            return buddy == null ? NoBuddy : buddy.StartSellNow();
        }

        /// <summary>
        /// Idle play with loose trash now, for testing: skips the schedule and the ItemPlay setting. docs/items.md §5
        /// </summary>
        public static string Play()
        {
            BuddyBehaviour? buddy = Living();
            return buddy == null ? NoBuddy : buddy.StartPlayNow();
        }

        /// <summary>
        /// Hiding in a closet now, for testing: skips the fear and the HideInClosets setting. docs/fear.md §6
        /// </summary>
        public static string Hide()
        {
            BuddyBehaviour? buddy = Living();
            return buddy == null ? NoBuddy : buddy.StartHideNow();
        }

        /// <summary>
        /// What the decider waits for and when it acts next: the HUD's Mind / Air / Snack lines. docs/behaviour.md §3
        /// </summary>
        public static string Mind()
        {
            BuddyBehaviour? buddy = Living();
            return buddy == null ? NoBuddy : buddy.DescribeTimers();
        }

        /// <summary>
        /// Ends the follow or wander bout now, for testing the switch. docs/behaviour.md §3
        /// </summary>
        public static string EndBout()
        {
            BuddyBehaviour? buddy = Living();
            return buddy == null ? NoBuddy : buddy.EndBoutNow();
        }

        /// <summary>
        /// Switches the oxygen generator or climate control on now, whatever the air. docs/terminals.md
        /// </summary>
        public static string Terminal(string which)
        {
            BuddyBehaviour? buddy = Living();
            return buddy == null ? NoBuddy : buddy.StartTerminalNow(which);
        }

        /// <summary>
        /// Teaches the buddy a door code. It is used only on a panel whose own code
        /// matches, so a wrong code changes nothing. docs/doors.md
        /// </summary>
        public static string GivePassword(string text)
        {
            BuddyBehaviour? buddy = Living();
            if (buddy == null) return NoBuddy;

            if (!int.TryParse(text.Trim(), out int code)) return "That is not a door code";

            buddy.LearnPinCode(code);
            return buddy.AnyKnownDoorMatches()
                ? "Got it - that opens a door I know about"
                : "Noted, but no door I know of uses that code";
        }
    }
}

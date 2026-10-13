using System;
using NPC.Core;
using NPC.Core.Navigation;
using NPC.Core.World;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Every order the player can give a buddy. The console (BuddyConsole.cs) and the dialog both
    /// call these, so they cannot drift apart. Each returns the line to show the player; the caller
    /// picks the buddy and runs the order inside its BuddyManager.Acting scope.
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

        /// <summary>
        /// The refusal for a buddy not taking orders now (an anomaly), else Dead's.
        /// docs/anomalies.md#2-the-anomalies
        /// </summary>
        private static string? Deaf(BuddyBehaviour buddy) => Dead(buddy) ?? (buddy.IgnoresYou ? buddy.Name + " does not answer" : null);

        public static string Follow(BuddyBehaviour buddy)
        {
            if (Deaf(buddy) is { } dead) return dead;

            return buddy.ApplyOrder(BuddyMode.Follow) ? buddy.Name + " now follows you" : buddy.Name + " will follow you" + OnceSafe;
        }

        public static string Wander(BuddyBehaviour buddy)
        {
            if (Deaf(buddy) is { } dead) return dead;
            if (buddy.Floating) return buddy.Name + " has nothing to walk on out here - it can follow you or stay";

            return buddy.ApplyOrder(BuddyMode.Wander)
                ? buddy.Name + " will have a look around"
                : buddy.Name + " will do its own thing" + OnceSafe;
        }

        public static string Stay(BuddyBehaviour buddy)
        {
            if (Deaf(buddy) is { } dead) return dead;

            return buddy.ApplyOrder(BuddyMode.Stay) ? buddy.Name + " stays here" : buddy.Name + " will stay put" + OnceSafe;
        }

        /// <summary>
        /// The console's goto, to one node by index, for debugging.
        /// </summary>
        public static string GoToNode(BuddyBehaviour buddy, int nodeIndex)
        {
            if (Deaf(buddy) is { } dead) return dead;
            if (nodeIndex < 0 || nodeIndex >= NavGraph.NodeCount)
            {
                return "Node index out of range (0-" + (NavGraph.NodeCount - 1) + ")";
            }
            if (buddy.Floating) return buddy.Name + " is floating - there are no nodes out here";
            // A goto stays on the buddy's side of the airlocks. npc-core:docs/invariants.md#an-airlock-is-crossed-by-its-cycle
            bool outdoor = NavGraph.GetNodeType(nodeIndex) == NodeType.Outdoor;
            if (outdoor && !buddy.IsOutside)
            {
                return "Node #" + nodeIndex + " is outside - tell " + buddy.Name + " to go outside first";
            }
            if (!outdoor && buddy.IsOutside)
            {
                return "Node #" + nodeIndex + " is inside - tell " + buddy.Name + " to come inside first";
            }

            return WalkTo(buddy, [nodeIndex], "node #" + nodeIndex);
        }

        /// <summary>
        /// The dialog's goto, to a room by name, via the first of its nodes the buddy can reach.
        /// Rooms are inside.
        /// </summary>
        internal static string GoToRoom(BuddyBehaviour buddy, StationRooms.Entry room)
        {
            if (Deaf(buddy) is { } dead) return dead;

            return buddy.IsOutside
                ? buddy.Name + " is outside - tell it to come inside first"
                : WalkTo(buddy, room.Nodes, room.Name);
        }

        private static string WalkTo(BuddyBehaviour buddy, int[] nodes, string label)
        {
            // A dead-end node must not strand a whole room, and a failed search is not free.
            for (int i = 0; i < Math.Min(nodes.Length, MaxRoomTries); i++)
            {
                Vector3 target = NavGraph.GetNodeWorld(nodes[i]);
                NavPath? plan = NavGraph.FindPath(buddy.Agent.FloorUnderNpc(), target, mayGoOutside: buddy.IsOutside);
                if (plan is not { Count: > 0 }) continue;

                return buddy.ApplyRouteOrder(plan.Value, target)
                    ? buddy.Name + " is on the way to " + label
                    : buddy.Name + " will walk to " + label + OnceSafe;
            }

            return NavGraph.LastPathBlockedByDoor
                ? "No way to " + label + " that avoids a door I cannot open"
                : "I can't find a way to " + label + ". Try another room or lead me closer.";
        }

        /// <summary>
        /// Hands the choice back to the buddy, leaving no order in force. docs/behaviour.md
        /// </summary>
        public static string DecideForYourself(BuddyBehaviour buddy)
        {
            if (Deaf(buddy) is { } dead) return dead;
            if (!YourBuddyPlugin.ConfigAutonomy.Value)
            {
                return "Autonomy is switched off - 'buddy_order auto on', or the Autonomy config setting";
            }
            buddy.RevokeOrder();
            return buddy.Name + " decides for itself now";
        }

        /// <summary>
        /// The Autonomy switch, for the console. Turning it on also revokes every order in force,
        /// or it would visibly do nothing until the next order.
        /// </summary>
        public static string SetAutonomy(bool on)
        {
            YourBuddyPlugin.ConfigAutonomy.Value = on;
            if (on)
            {
                foreach (BuddyBehaviour buddy in BuddyManager.Snapshot())
                {
                    if (buddy == null || buddy.IsDead) continue;

                    using NpcRegistry.ActingScope _ = NpcRegistry.Acting(buddy.Agent);
                    buddy.RevokeOrder();
                }
            }

            return on
                ? "Autonomy: ON - the buddies decide for themselves when no order holds"
                : "Autonomy: OFF - the buddies keep doing what they do until told otherwise";
        }

        /// <summary>
        /// A snack now, for testing. Skips the schedule and the Snacks setting. docs/snacks.md
        /// </summary>
        private static string OtherOrder(BuddyBehaviour buddy, Func<string> run)
        {
            if (Dead(buddy) is { } dead) return dead;
            buddy.StopStoreOrder();
            return run();
        }

        public static string Snack(BuddyBehaviour buddy) => OtherOrder(buddy, buddy.StartSnackNow);

        /// <summary>
        /// Tidying now, for testing. Skips the schedule and the Tidying setting. docs/items.md §3
        /// </summary>
        public static string Store(BuddyBehaviour buddy) => Dead(buddy) ?? buddy.StartStoreNow();
        public static string Tidy(BuddyBehaviour buddy) => OtherOrder(buddy, buddy.StartTidyNow);

        /// <summary>
        /// Selling a trash box now, for testing. Skips the schedule and the SellTrash setting. docs/items.md §4
        /// </summary>
        public static string Sell(BuddyBehaviour buddy) => OtherOrder(buddy, buddy.StartSellNow);

        /// <summary>
        /// Idle play with loose trash now, for testing. Skips the schedule and the ItemPlay setting. docs/items.md §5
        /// </summary>
        public static string Play(BuddyBehaviour buddy) => OtherOrder(buddy, buddy.StartPlayNow);

        /// <summary>
        /// Fetching a suit left on the docked station now, for testing. Skips the schedule and the SuitFetch setting. docs/eva.md#6-bringing-a-forgotten-suit-home
        /// </summary>
        public static string FetchSuit(BuddyBehaviour buddy) => OtherOrder(buddy, buddy.StartSuitFetchNow);

        /// <summary>
        /// Hiding in a closet now, for testing. Skips the fear and the HideInClosets setting. docs/fear.md §6
        /// </summary>
        public static string Hide(BuddyBehaviour buddy) => OtherOrder(buddy, buddy.StartHideNow);

        /// <summary>
        /// An anomaly now, ignoring the chance, the difficulty and the quiet after the last one. docs/anomalies.md
        /// </summary>
        public static string Anomaly(BuddyBehaviour buddy, string kindName)
        {
            if (Dead(buddy) is { } dead) return dead;

            if (kindName.Equals("end", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!buddy.AnomalyRunning) return buddy.Name + " is not acting anything out";

                buddy.EndAnomaly("you ended it");
                return buddy.Name + " is back to normal";
            }
            if (Anomalies.Parse(kindName) is not { } kind) return "No anomaly '" + kindName + "' - 'buddy_anomaly list' names them";

            buddy.StopStoreOrder();
            string? blocker = buddy.TryStartAnomaly(kind);
            return blocker == null ? buddy.Name + ": " + Anomalies.Info(kind).Name : "Not now: " + blocker;
        }

        /// <summary>
        /// What the decider waits for and when it acts next (the HUD's Mind / Air / Snack lines). docs/behaviour.md §3
        /// </summary>
        public static string Mind(BuddyBehaviour buddy) => Dead(buddy) ?? buddy.Name + ": " + buddy.DescribeTimers();

        /// <summary>
        /// The EVA order. Suit up if a spare is free, then wait in the docked station's exit
        /// airlock for the player to cycle it. docs/eva.md
        /// </summary>
        public static string GoOutside(BuddyBehaviour buddy) => OtherOrder(buddy, buddy.StartOutsideNow);

        /// <summary>
        /// The way back in. From outside, into an airlock's chamber to wait for the player's cycle,
        /// then inside. docs/eva.md
        /// </summary>
        public static string GoInside(BuddyBehaviour buddy) => OtherOrder(buddy, buddy.StartInsideNow);

        /// <summary>
        /// `buddy_order suit on|off` wears a spare suit now or takes the worn one off. docs/eva.md
        /// </summary>
        public static string Suit(BuddyBehaviour buddy, string onOff) => OtherOrder(buddy, () => buddy.SuitNow(onOff));

        /// <summary>
        /// Takes the worn suit off, whatever the air and wherever the buddy stands. docs/eva.md
        /// </summary>
        public static string Unsuit(BuddyBehaviour buddy) => OtherOrder(buddy, buddy.UnsuitNow);

        /// <summary>
        /// Ends the follow or wander bout now, for testing the switch. docs/behaviour.md §3
        /// </summary>
        public static string EndBout(BuddyBehaviour buddy) => OtherOrder(buddy, buddy.EndBoutNow);

        /// <summary>
        /// Switches the oxygen generator or climate control on now, whatever the air. docs/terminals.md
        /// </summary>
        public static string Terminal(BuddyBehaviour buddy, string which) => OtherOrder(buddy, () => buddy.StartTerminalNow(which));

        /// <summary>
        /// Teaches every NPC a door code. It is used only on a panel whose own code
        /// matches, so a wrong code changes nothing. npc-core:docs/doors.md#password-doors
        /// </summary>
        public static string GivePassword(string text)
        {
            if (!int.TryParse(text.Trim(), out int code)) return "That is not a door code";

            NpcDoors.LearnCode(code);
            return NpcDoors.AnyKnownDoorMatches()
                ? "Got it - that opens a door I know about"
                : "Noted, but no door I know of uses that code";
        }

        internal const string ResourcesUsage = "Usage: buddy_order resources [oxygen|fuel|energy|buying [on|off]] | [limit <amount>]";

        /// <summary>
        /// The console's Resources page: shared duty settings, not per buddy. A change resumes paused
        /// duties, as the dialog's buttons do. docs/resources.md
        /// </summary>
        internal static string Resources(string[] args)
        {
            ResourceDutySettings s = ResourceDuty.Settings;
            if (args.Length == 0)
            {
                return $"Resource duties: oxygen {OnOff(s.Oxygen.Enabled)}, fuel {OnOff(s.Fuel.Enabled)}, " +
                       $"energy {OnOff(s.Energy.Enabled)}; buying {OnOff(s.Buying)}, limit {s.Budget}" +
                       (s.Paused ? "; paused - change a setting to resume" : "");
            }

            string what = args[0].ToLowerInvariant();
            if (what == "limit")
            {
                if (args.Length < 2 || !int.TryParse(args[1], out int budget) || budget < 0) return ResourcesUsage;

                s.Budget = budget;
                s.Paused = false;
                return $"Spending limit: {s.Budget}.";
            }
            if (what == "buying")
            {
                s.Buying = NpcConsole.Toggle(args, 1, s.Buying);
                s.Paused = false;
                return s.Buying ? "Buying allowed within the limit." : "Buying off.";
            }
            for (int kind = 0; kind < 3; kind++)
            {
                if (what != ResourceDutySettings.Label(kind)) continue;

                ResourceRule rule = s.Rule(kind);
                rule.Enabled = NpcConsole.Toggle(args, 1, rule.Enabled);
                s.Paused = false;
                return $"{ResourceDutySettings.Label(kind)} duty {OnOff(rule.Enabled)}.";
            }
            return ResourcesUsage;
        }

        private static string OnOff(bool on) => on ? "on" : "off";
    }
}

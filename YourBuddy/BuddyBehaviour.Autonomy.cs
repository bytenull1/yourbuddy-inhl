using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Orders and independent decisions: what the player told the buddy, kept apart from
    /// what it is doing, and the decider that acts when no order holds. docs/behaviour.md
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        // ReSharper disable RedundantDefaultMemberInitializer
        private float orderedAt = 0f;
        private float decideTraceAt = 0f;
        /// <summary>
        /// The mode the decider has watched uninterrupted since boutEnteredAt; null after standing down.
        /// </summary>
        private BuddyMode? boutMode = null;
        private const float DecideInterval = 3f;

        /// <summary>
        /// Following this long, counted from catching up with the player, and it goes off to wander.
        /// </summary>
        private const float FollowBoutMin = 20f;
        private const float FollowBoutMax = 45f;
        /// <summary>
        /// Wandering this long, however far it has gone, and it comes back to follow.
        /// </summary>
        private const float WanderBoutMin = 40f;
        private const float WanderBoutMax = 90f;
        // ReSharper restore RedundantDefaultMemberInitializer

        // ------------------------------------------------------------------
        // Orders - given only through BuddyCommands
        // ------------------------------------------------------------------

        /// <summary>
        /// Records an order and carries it out, or keeps it for when a flee is over.
        /// True when it took effect now. docs/invariants.md#fear-owns-the-buddy
        /// </summary>
        public bool ApplyOrder(BuddyMode order)
        {
            if (IsDead) return false;

            LeaveAnOrderedHide(OrderName(order));
            orderedMode = order;
            orderedAt = Time.time;
            if (order == BuddyMode.Wander) wanderOwner = null;

            if (mode == BuddyMode.Flee)
            {
                modeBeforeFlee = order;
                return false;
            }
            SetMode(order);
            return true;
        }

        /// <summary>
        /// A goto order: the plan to walk now, and the goal to plan for again after a flee.
        /// </summary>
        public bool ApplyRouteOrder(BuddyNodeGraph.NavPath plan, Vector3 goal)
        {
            if (IsDead) return false;

            LeaveAnOrderedHide("Goto");
            orderedMode = BuddyMode.Route;
            orderedAt = Time.time;
            routeGoal = goal;
            DropReachTask();
            if (mode == BuddyMode.Flee)
            {
                modeBeforeFlee = BuddyMode.Route;
                return false;
            }
            StartRoute(plan, goal);
            return true;
        }

        /// <summary>
        /// A hide the player asked for ends when they ask for something else. A hide a flee started
        /// does not: docs/invariants.md#fear-owns-the-buddy
        /// </summary>
        private void LeaveAnOrderedHide(string order)
        {
            if (Hiding && !hideFromFear) ForceLeaveHidingSpot("you told me to " + order);
        }

        /// <summary>
        /// "Decide for yourself": no order in force, and a decision at the next chance.
        /// </summary>
        public void RevokeOrder()
        {
            // An ordered hide waits for an order, and this is one: nothing else would ever end it.
            LeaveAnOrderedHide("decide for myself");
            if (orderedMode.HasValue)
            {
                YourBuddyPlugin.Log.LogInfo("[mind] Order '" + OrderName(orderedMode.Value) + "' revoked - deciding for myself");
            }
            orderedMode = null;
            decideAt = 0f;
        }

        /// <summary>
        /// True while an order holds, which is what keeps the decider out.
        /// </summary>
        private bool OrderInForce
        {
            get
            {
                if (!orderedMode.HasValue) return false;

                if (YourBuddyPlugin.ConfigOrderPersistence.Value == OrderPersistence.UntilRevoked) return true;

                return GotoUnderway || Time.time - orderedAt < OrderExpiry;
            }
        }

        /// <summary>
        /// A goto runs to completion under either policy, a flee in the middle of it included.
        /// </summary>
        private bool GotoUnderway =>
            orderedMode == BuddyMode.Route &&
            (mode == BuddyMode.Route || (mode == BuddyMode.Flee && modeBeforeFlee == BuddyMode.Route));

        private static float OrderExpiry => Mathf.Max(5f, YourBuddyPlugin.ConfigOrderExpirySeconds.Value);

        /// <summary>
        /// The player never said "route": they said goto.
        /// </summary>
        private static string OrderName(BuddyMode order) => order == BuddyMode.Route ? "Goto" : order.ToString();

        /// <summary>
        /// For the HUD.
        /// </summary>
        internal string DescribeOrders()
        {
            bool autonomy = YourBuddyPlugin.ConfigAutonomy.Value;
            if (!orderedMode.HasValue) return autonomy ? "none - deciding for itself" : "none (autonomy off)";

            string order = OrderName(orderedMode.Value);
            if (GotoUnderway) return order + ", until it arrives";

            if (!autonomy || YourBuddyPlugin.ConfigOrderPersistence.Value == OrderPersistence.UntilRevoked)
            {
                return order + ", until revoked";
            }
            return order + ", " + Mathf.Max(0f, OrderExpiry - (Time.time - orderedAt)).ToString("0") + "s left";
        }

        // ------------------------------------------------------------------
        // The decider
        // ------------------------------------------------------------------

        /// <summary>
        /// Phase 3 of SlowUpdate, throttled to DecideInterval. docs/behaviour.md
        /// </summary>
        private void UpdateAutonomy(Player player)
        {
            if (IsDead || player == null || player.Controller == null) return;

            if (!YourBuddyPlugin.ConfigAutonomy.Value) return;

            ExpireOrder();
            if (Time.time < decideAt) return;

            decideAt = Time.time + DecideInterval;

            string? standDown = StandDownReason(player);
            if (standDown != null)
            {
                boutMode = null;
                urgeReport = "standing down - " + standDown;
                TraceDecider("standing down: " + standDown);
                return;
            }
            // Everything it might want, weighed against everything else. docs/behaviour.md §3
            ChooseAndAct(player);
        }

        /// <summary>
        /// Starts a bout, with a fresh random length, whenever the decider finds a mode it was not watching.
        /// </summary>
        private void TrackBout()
        {
            if (boutMode == mode) return;

            boutMode = mode;
            boutEnteredAt = Time.time;
            bool wander = mode == BuddyMode.Wander;
            boutSince = wander ? Time.time : -1f;
            boutLength = wander ? Random.Range(WanderBoutMin, WanderBoutMax) : Random.Range(FollowBoutMin, FollowBoutMax);
        }

        /// <summary>
        /// Why the decider leaves the buddy alone this round, or null when it may decide.
        /// </summary>
        private string? StandDownReason(Player player)
        {
            if (OrderInForce) return "order '" + OrderName(orderedMode.GetValueOrDefault()) + "' in force";

            if (catchInProgress) return "being caught";

            if (inDialog) return "being talked to";

            if (Hiding) return hideState + " " + hideName;
            // docs/invariants.md#fear-owns-the-buddy
            if (fearState != FearState.Calm || mode == BuddyMode.Flee) return "fear is " + fearState;

            if (mode == BuddyMode.Route) return DescribeReachTask() is { } task ? task : "walking a route";
            // Follow already waits inside for a spacewalk; there is nothing to choose.
            if (IsPlayerInSpace(player)) return "the player is outside";

            return null;
        }

        /// <summary>
        /// `nodeOwner` is the graph owner an autonomous Wander stays on.
        /// </summary>
        private void Decide(BuddyMode next, string why, string? nodeOwner = null)
        {
            SetMode(next);
            TrackBout();
            if (next == BuddyMode.Wander) wanderOwner = nodeOwner;

            YourBuddyPlugin.Log.LogInfo("[mind] Decided: " + next + " - " + why +
                                        (nodeOwner != null ? ", on '" + nodeOwner + "'" : ""));
        }

        /// <summary>
        /// Expires policy only, and never a goto underway.
        /// </summary>
        private void ExpireOrder()
        {
            if (!orderedMode.HasValue || OrderInForce) return;

            YourBuddyPlugin.Log.LogInfo(
                $"[mind] Order '{OrderName(orderedMode.Value)}' expired after {OrderExpiry:0}s - deciding for myself again");
            orderedMode = null;
            decideAt = 0f;
        }

        /// <summary>
        /// For the HUD and buddy_mind: why the decider waits, or the bout it is timing and when it looks next.
        /// </summary>
        internal string DescribeMind()
        {
            if (!YourBuddyPlugin.ConfigAutonomy.Value) return "autonomy off";

            Player? player = PilotPlayer();
            if (player == null || player.Controller == null) return "no player";

            string? standDown = StandDownReason(player);
            if (standDown != null) return "standing down - " + standDown;

            float now = Time.time;
            string next = $"; next look in {Mathf.Max(0f, decideAt - now):0}s";
            if (boutMode != mode) return mode + ", no bout started yet" + next;

            if (mode == BuddyMode.Wander) return $"Wander {now - boutSince:0}s of {boutLength:0}, Follow weighed from {boutLength * BoutReadyFrom:0}s" + next;

            if (mode != BuddyMode.Follow) return mode + " left over from an order, then Follow" + next;

            return boutSince < 0f
                ? $"Follow, catching up {now - boutEnteredAt:0}s of {DecideCatchUpSeconds:0}, then {boutLength:0}s" + next
                : $"Follow {now - boutSince:0}s of {boutLength:0}, Wander weighed from {boutLength * BoutReadyFrom:0}s" + next;
        }

        /// <summary>
        /// The [mind] lines, shared by the HUD and buddy_mind. Test commands: buddy_bout, buddy_terminal, buddy_snack, buddy_tidy, buddy_sell, buddy_play.
        /// </summary>
        internal string DescribeTimers() =>
            "Mind: " + DescribeMind() + "\nWhy: " + DescribeUrges() + "\nAir: " + lifeSupport.Describe() +
            "\nSnack: " + snacks.Describe() + "\nTidy: " + tidying.Describe() + "\nSell: " + selling.Describe() +
            "\nPlay: " + play.Describe();

        /// <summary>
        /// The HUD's Mind and Why lines, without repeating what the Mode and Orders lines already say:
        /// a stand-down for the task in Mode's brackets or for the order in force is left out, and the
        /// Why line (the scored urges) is left out while the decider is standing down, since it would
        /// only copy the reason. Null when there is nothing to add.
        /// </summary>
        private string? DescribeHudMind()
        {
            string mind = DescribeMind();
            const string StandingDown = "standing down - ";
            if (mind.StartsWith(StandingDown))
            {
                string reason = mind.Substring(StandingDown.Length);
                bool shownElsewhere = reason == DescribeReachTask() || reason.StartsWith("order '");
                return shownElsewhere ? null : "Mind: " + mind;
            }
            return "Mind: " + mind + (urgeReport.Length > 0 && !urgeReport.StartsWith(StandingDown) ? "\nWhy: " + urgeReport : "");
        }

        /// <summary>
        /// buddy_bout: the bout being timed is over now, so the decider's next look switches Follow and Wander.
        /// </summary>
        internal string EndBoutNow()
        {
            if (!YourBuddyPlugin.ConfigAutonomy.Value) return "Autonomy is off - 'buddy_auto on' first";

            Player? player = PilotPlayer();
            if (player == null || player.Controller == null) return "No player";

            string? standDown = StandDownReason(player);
            if (standDown != null) return "The buddy is not deciding for itself now: " + standDown;

            TrackBout();
            // Catching up is skipped too.
            boutSince = Time.time - boutLength;
            decideAt = 0f;
            if (mode == BuddyMode.Wander) return "Wander over - coming back to you weighs full at its next look";

            if (mode != BuddyMode.Follow) return "It comes back to you at its next look";

            return BuddyNodeGraph.NearestActiveNodeOwner(transform.position, DecideNodeOwnerRadius) != null
                ? "Follow over - wandering off weighs full at its next look, unless an errand outweighs it"
                : $"Follow over, but no nav node is within {DecideNodeOwnerRadius:0}m to wander on - it keeps following";
        }

        /// <summary>
        /// The reason a decision was not made, throttled. docs/logging.md §4
        /// </summary>
        private void TraceDecider(string line)
        {
            if (YourBuddyPlugin.ConfigDebugLevel.Value < 2 || Time.time < decideTraceAt) return;

            decideTraceAt = Time.time + 15f;
            YourBuddyPlugin.Log.LogInfo("[mind] Decider " + line);
        }

        /// <summary>
        /// Why a command cannot start a task now, or null. `whileAlert` is for hiding, the one thing
        /// worth asking for with the Breathless about; a flee still owns the buddy either way.
        /// `preemptErrand` lets a command take the buddy off a job it is already doing -
        /// docs/invariants.md#a-command-outranks-an-errand
        /// </summary>
        private string? BusyForCommand(bool whileAlert = false, bool preemptErrand = false)
        {
            if (IsDead) return "The buddy is dead";

            if (Asleep || !gameObject.activeInHierarchy) return "Buddy is not awake here";

            if (catchInProgress || mode == BuddyMode.Flee) return "Buddy is already fleeing the Breathless";

            if (!whileAlert && fearState != FearState.Calm) return "Buddy is too scared for that";

            if (reachTask != null && !preemptErrand) return "Buddy is busy " + DescribeReachTask();

            // A goto is a standing order of yours, not something the buddy chose: it is not an errand.
            return GotoUnderway ? "Buddy is walking to a node you sent it to" : null;
        }

        private static Player? PilotPlayer()
        {
            GameManager gm = GameManager.Instance;
            return gm != null && gm.PlayerShip != null ? gm.PlayerShip.Pilot : null;
        }
    }
}

using NPC.Core.Agents;
using NPC.Core.Navigation;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The modes the brain steers with: Follow and Route here, Wander and Stay straight from the agent, Flee in
    /// BuddyBehaviour.Fear.cs. Mode switches and routes. docs/behaviour.md
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        // Follow hysteresis in XZ: start walking beyond the first, keep walking down to the second.
        private const float FollowStartDistance = 2.0f;
        private const float FollowStopDistance = 1.8f;

        /// <summary>
        /// Walks to the player, and stands once near them on their deck. The walk is the agent's Pursue.
        /// </summary>
        private Vector3 UpdateFollow(Player? player, out bool wantMove)
        {
            wantMove = false;
            if (player == null || player.Controller == null) return Vector3.zero;

            Transform playerTransform = player.Controller.CachedTransform;

            // If the player went EVA, the buddy politely waits inside.
            if (NpcAgent.IsPlayerInSpace(player)) return Vector3.zero;

            // Step-off stretch: a short unvalidated walk that gets him off chair seats
            // and ledges, or out of a doorway he is holding open.
            if (agent.TryStepOff(out Vector3 stepOff))
            {
                wantMove = true;
                return stepOff;
            }

            Vector3 toPlayer = playerTransform.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;

            // Being on another deck counts as far away no matter the XZ gap.
            if (!OnPlayersDeck(playerTransform) || distance > FollowStartDistance ||
                (agent.WasMoving && distance >= FollowStopDistance))
            {
                return agent.Pursue(playerTransform.position, out wantMove);
            }

            agent.ClearMoveTarget();
            agent.ReleasePlan();
            return Vector3.zero;
        }

        /// <summary>
        /// Floor to floor, not an XZ test: npc-core:docs/invariants.md#follow-arrival-is-level-aware
        /// </summary>
        private bool OnPlayersDeck(Transform playerTransform) =>
            Mathf.Abs(agent.FloorUnderPlayer(playerTransform) - agent.FloorUnderNpc().y) <= NpcAgent.FollowSameLevelDeltaY;

        /// <summary>
        /// A goto or an errand leg: the plan with the simple advance, then the leg's own approach.
        /// npc-core:docs/invariants.md#a-stair-leg-is-walked-not-improvised (Route keeps its own advance)
        /// </summary>
        private Vector3 UpdateRoute(out bool wantMove)
        {
            wantMove = false;

            if (agent.TryStepOff(out Vector3 stepOff))
            {
                wantMove = true;
                return stepOff;
            }

            if (!agent.HasPlanLeft)
            {
                if (reachTask != null) return reachTask.Approach(out wantMove);

                FinishRoute();
                return Vector3.zero;
            }

            return agent.SimpleAdvance(out wantMove);
        }

        private void FinishRoute()
        {
            // Every waypoint was skipped rather than reached: the buddy is not there.
            bool abandoned = agent.SkippedWaypoint && agent.PlanIndex > 0;
            Vector3 goal = routeGoal;
            agent.DropPlan();
            DropReachTask();
            // Arriving is what a goto order asked for, so it ends here. docs/behaviour.md
            bool ordered = orderedMode == BuddyMode.Route;
            if (ordered) orderedMode = null;
            mode = ModeAfterTask;

            decideAt = 0f;
            if (abandoned)
            {
                YourBuddyPlugin.Log.LogWarning(
                    $"[ai] Gave up on the route to {goal:0.0} - the last waypoint could not be reached" +
                    (ordered ? " (the goto order is not done)" : "") + ", switching to " + mode + " mode");
                return;
            }

            YourBuddyPlugin.Log.LogInfo("[ai] Route finished" + (ordered ? " (the goto order is done)" : "") +
                                        ", switching to " + mode + " mode");
        }

        /// <summary>
        /// Where a route ends up: the order in force (buddy_snack runs under one), else Follow.
        /// </summary>
        private BuddyMode ModeAfterTask => orderedMode is { } order && order != BuddyMode.Route && OrderInForce ? order : BuddyMode.Follow;

        /// <summary>
        /// Starts walking a NodeGraph-computed path. Orders come in through ApplyRouteOrder.
        /// `goal` is where it leads, which a plan ending at an approach node falls short of.
        /// </summary>
        private void StartRoute(NavPath plan, Vector3 goal)
        {
            if (plan.Count == 0)
            {
                YourBuddyPlugin.Log.LogWarning("[ai] Route has no points");
                return;
            }

            agent.CommitPlan(plan, true);
            routeGoal = goal;
            mode = BuddyMode.Route;
            YourBuddyPlugin.Log.LogInfo($"[ai] Walking path ({plan.Count} waypoints)");
        }

        /// <summary>
        /// Switches mode and forgets the plan. Leaves the agent's step-off, wander pause,
        /// barred waypoint and sidestep alone on purpose.
        /// Orders come in through ApplyOrder. docs/behaviour.md
        /// </summary>
        private void SetMode(BuddyMode newMode)
        {
            if (IsDead) return;

            mode = newMode;
            // A flee resumes a terminal route; anything else ends it, and a snack or an item task ends even then. docs/snacks.md §2
            bool endsOnFlee = reachTask is { EndsOnFlee: true };
            if (newMode == BuddyMode.Flee && endsOnFlee && modeBeforeFlee == BuddyMode.Route) modeBeforeFlee = ModeAfterTask;
            if (newMode != BuddyMode.Flee || endsOnFlee) DropReachTask();
            agent.DropPlan();
        }

        /// <summary>
        /// Ends the leg in hand, putting back anything it left half done.
        /// </summary>
        private void DropReachTask()
        {
            reachTask?.End();
            reachTask = null;
        }

        /// <summary>
        /// For the HUD's Mode line and the decider's stand-down reason.
        /// </summary>
        private string? DescribeReachTask() => reachTask?.Describe();
    }
}

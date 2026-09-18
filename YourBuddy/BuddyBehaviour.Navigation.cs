using System.Collections.Generic;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Mode logic and A* route following: follow, wander and goto route playback.
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        // ReSharper disable RedundantDefaultMemberInitializer
        private const float NavPathRecalcInterval = 0.22f;
        // A plan is kept while the goal stays within this drift and its entry stretch
        // is clear. Replanning every tick flips the first waypoint back and forth.
        private const float NavPathGoalDrift = 1.5f;
        private Vector3 navPathGoal = Vector3.zero;

        // How far off the previous waypoint still counts as walking the route; inside
        // it the commitment check is skipped.
        // docs/invariants.md#commitment-skips-on-route
        private const float NavPathOnRouteRadius = 1.2f;

        // Same-deck cap for the XZ advance branch, floor to floor. Kept in step with
        // BuddyNodeGraph.SameLevelDeltaY - both answer "same deck?".
        // docs/invariants.md#waypoint-advance-is-dual
        private const float WaypointAdvanceMaxDeltaY = 0.5f;
        // Reach radii for the same two branches: XZ on one deck, 3D for anything else.
        private const float WaypointReachedXZ = 0.55f;
        private const float WaypointReached3D = 0.65f;

        // A leg whose ends are on different decks is a flight of stairs, walked as drawn.
        // docs/invariants.md#a-stair-leg-is-walked-not-improvised
        /// <summary>
        /// How far along the leg, past the buddy's own projection, it aims: about one tread.
        /// </summary>
        private const float StairLegLookahead = 0.35f;
        /// <summary>
        /// How far beside a leg's line still counts as standing on that flight.
        /// </summary>
        private const float StairLegCorridor = 0.5f;

        // Follow hysteresis in XZ: start walking beyond the first, keep walking down to the second.
        private const float FollowStartDistance = 2.0f;
        private const float FollowStopDistance = 1.8f;

        // Anti-livelock for path commitment: bar the waypoint, then step off.
        // Counted over a sliding window, never a run:
        // docs/invariants.md#escalate-over-a-window
        private const float EntryBlockedWindow = 4f;
        private const int EntryBlockedAvoidAfter = 4;
        private const int EntryBlockedStepOffAfter = 8;
        private int entryBlockedCount = 0;
        private float entryBlockedWindowStart = 0f;
        private Vector3 lastBlockedEntry = Vector3.zero;

        // The last graph node the buddy walked away from. Lives outside the plan:
        // docs/invariants.md#backtrack-memory-outlives-the-plan
        private Vector3 lastDepartedWaypoint = Vector3.zero;
        private bool hasLastDepartedWaypoint = false;

        // Wander state. The idle windows are short on purpose: picking and planning
        // used to be separate frames with separate timers, which stacked into a buddy
        // standing still for ten seconds between destinations.
        private Vector3 wanderTarget = Vector3.zero;
        private float wanderTargetSince = 0f;
        private int wanderPickFailures = 0;
        private const float WanderIdleMin = 0.3f;
        private const float WanderIdleMax = 1.4f;
        private const float WanderRetryDelay = 0.5f;
        /// <summary>
        /// Destinations tried per pick. Each costs one A* over the active graph, so
        /// this stays small.
        /// </summary>
        private const int WanderPickAttempts = 4;

        // Idle-on-furniture watchdog. Every other recovery needs wantMove, so a buddy
        // that simply stands there had nothing watching it.
        private float idleNoRouteSeconds = 0f;
        private float idleStepOffCooldown = 0f;
        private const float IdleNoRouteGiveUp = 2.5f;
        /// <summary>
        /// How far above its own floor plane counts as "perched on something".
        /// </summary>
        private const float IdleAboveFloorDelta = 0.35f;

        private float doorBlockedLogAt = 0f;
        // ReSharper restore RedundantDefaultMemberInitializer

        // ------------------------------------------------------------------
        // Modes
        // ------------------------------------------------------------------

        private Vector3 UpdateFollow(Player? player, out bool wantMove)
        {
            wantMove = false;
            if (player == null || player.Controller == null) return Vector3.zero;

            Transform playerTransform = player.Controller.CachedTransform;

            // If the player went EVA, the buddy politely waits inside.
            if (IsPlayerInSpace(player)) return Vector3.zero;

            // Step-off stretch: a short unvalidated walk that gets him off chair seats
            // and ledges, or out of a doorway he is holding open.
            if (TryStepOff(out Vector3 stepOff))
            {
                wantMove = true;
                return stepOff;
            }

            // If the player is temporarily unreachable (real geometry in the way,
            // e.g. the docked ship's Docker collider), stop pushing and wait a bit.
            if (Time.time < followWaitUntil) return Vector3.zero;

            Vector3 toPlayer = playerTransform.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;

            // Being on another deck counts as far away no matter the XZ gap:
            // docs/invariants.md#follow-arrival-is-level-aware
            float levelGap = Mathf.Abs(FloorUnderPlayer(playerTransform) - FloorUnderBuddy().y);
            bool sameLevel = levelGap <= FollowSameLevelDeltaY;

            if (!sameLevel || distance > FollowStartDistance || (wasMoving && distance >= FollowStopDistance))
            {
                // Commit to the current plan while it still holds; replanning every tick
                // flips the first waypoint back and forth. docs/navigation.md §5
                if (Time.time >= navPathRecalcAt)
                {
                    navPathRecalcAt = Time.time + NavPathRecalcInterval;

                    Vector3 goal = playerTransform.position;
                    Vector3 start = FloorUnderBuddy();
                    bool debug = YourBuddyPlugin.ConfigDebugLevel.Value >= 2;
                    string? invalidReason;
                    // Captured where the condition is decided, so the replan below never
                    // has to re-dereference a plan it cannot prove is still there.
                    Vector3? blockedEntry = null;
                    bool leftStairLeg = false;
                    if (navPlan == null)
                    {
                        invalidReason = "no plan";
                    }
                    else if (navPathIndex >= navPlan.Value.Count)
                    {
                        invalidReason = "plan exhausted";
                    }
                    else if (!hasNavPathGoal)
                    {
                        invalidReason = "plan has no goal";
                    }
                    // A deck test, not a sight line, so forced legs get it too; ahead of "goal
                    // moved", which would keep the index. docs/invariants.md#off-the-flight-is-off-the-plan
                    else if (HasLeftStairLeg(out float legLow, out float legHigh))
                    {
                        invalidReason = "left the stair leg";
                        leftStairLeg = true;
                        if (YourBuddyPlugin.ConfigDebugLevel.Value >= 1)
                        {
                            YourBuddyPlugin.Log.LogWarning(
                                $"[ai] Left the stair leg to {navPlan.Value[navPathIndex]:0.0}: feet {GroundPos(0f).y:0.00} " +
                                $"(probe floor {start.y:0.00}) are outside {legLow:0.00}..{legHigh:0.00} - replanning from here");
                        }
                    }
                    else if ((navPathGoal - goal).sqrMagnitude > NavPathGoalDrift * NavPathGoalDrift)
                    {
                        invalidReason = "goal moved";
                    }
                    // docs/invariants.md#one-entry-predicate; skipped per
                    // #commitment-skips-on-route and #force-and-priority-have-no-los
                    else if (!navPlan.Value.IsForced(navPathIndex) &&
                             !IsStandingOnRoute(start) &&
                             !BuddyNodeGraph.CanReachEntry(start, navPlan.Value[navPathIndex]))
                    {
                        invalidReason = "entry stretch blocked";
                        blockedEntry = navPlan.Value[navPathIndex];
                    }
                    // No "next stretch blocked" check: docs/invariants.md#force-and-priority-have-no-los
                    else
                    {
                        invalidReason = null;
                    }

                    if (invalidReason != null)
                    {
                        // Where the buddy departed; entry nodes near it are penalized.
                        // docs/invariants.md#backtrack-memory-outlives-the-plan
                        // Not after leaving a stair leg: its head is where the route resumes.
                        Vector3? cameFrom = leftStairLeg ? null
                            : navPlan != null && navPathIndex >= 1 && navPathIndex < navPlan.Value.Count
                                ? navPlan.Value[navPathIndex - 1]
                                : hasLastDepartedWaypoint ? lastDepartedWaypoint : null;

                        // A blocked entry that keeps coming back gets barred, so the
                        // search must offer a different way out.
                        // docs/invariants.md#escalate-over-a-window
                        bool entryBlocked = blockedEntry.HasValue;
                        Vector3? avoidEntry = null;
                        if (Time.time - entryBlockedWindowStart > EntryBlockedWindow)
                        {
                            entryBlockedWindowStart = Time.time;
                            entryBlockedCount = 0;
                        }
                        if (entryBlocked)
                        {
                            // entryBlocked is blockedEntry.HasValue.
                            lastBlockedEntry = blockedEntry!.Value;
                            entryBlockedCount++;
                            if (entryBlockedCount >= EntryBlockedAvoidAfter) avoidEntry = lastBlockedEntry;
                        }

                        // A waypoint the stuck detector proved unwalkable outranks the
                        // blocked-entry heuristic: that one is measured from the buddy's
                        // actual failure to make progress, not from a probe.
                        if (Time.time < unreachableWaypointUntil) avoidEntry = unreachableWaypoint;

                        if (debug)
                        {
                            YourBuddyPlugin.Log.LogInfo(
                                $"[ai] Replanning ({invalidReason}): buddy {transform.position:0.0} -> goal {goal:0.0}, " +
                                $"start {start:0.0}, cameFrom={(cameFrom.HasValue ? cameFrom.Value.ToString("0.0") : "none")}" +
                                (entryBlocked ? $", blocked x{entryBlockedCount} in {EntryBlockedWindow:0}s" : "") +
                                (avoidEntry.HasValue ? $", avoiding {avoidEntry.Value:0.0}" : ""));
                        }

                        // Always call it, even with an empty graph: FindPath returns null
                        // straight away and clears LastPathBlockedByDoor, which the
                        // no-plan branch below reads.
                        BuddyNodeGraph.NavPath? plan =
                            BuddyNodeGraph.FindPath(start, goal, cameFrom, avoidEntry);

                        if (entryBlocked && entryBlockedCount >= EntryBlockedStepOffAfter)
                        {
                            // Even with the offending waypoint barred the graph keeps
                            // pointing through it. Walk off manually and re-plan from
                            // wherever that leaves the buddy.
                            YourBuddyPlugin.Log.LogWarning(
                                $"[ai] {entryBlockedCount} blocked entries in {EntryBlockedWindow:0}s " +
                                $"near {lastBlockedEntry:0.0} - abandoning the plan and stepping off");
                            plan = null;
                            navPlan = null;
                            navPathIndex = 0;
                            entryBlockedCount = 0;
                            entryBlockedWindowStart = Time.time;
                        }

                        if (plan is { Count: > 0 })
                        {
                            // Keep walk progress when the fresh plan continues the old route - never
                            // after a blocked entry or off the flight, never past the waypoint walked.
                            // docs/invariants.md#drop-the-index-on-a-blocked-entry
                            if (!entryBlocked && !leftStairLeg && navPlan != null &&
                                SameRemainingPath(navPlan.Value.Waypoints, navPathIndex, plan.Value.Waypoints,
                                    out int resumeAt))
                            {
                                navPathIndex = Mathf.Min(navPathIndex, resumeAt);
                                navPlan = plan;
                            }
                            else
                            {
                                navPlan = plan;
                                navPathIndex = 0;
                            }
                            navPathGoal = goal;
                            hasNavPathGoal = true;
                        }
                        else if (navPlan == null || navPathIndex >= navPlan.Value.Count)
                        {
                            navPlan = null;
                            hasNavPathGoal = false;

                            // A door the buddy cannot open is the whole reason there is
                            // no route: stepping off has nowhere to go.
                            // docs/invariants.md#stand-still-when-door-blocked
                            if (BuddyNodeGraph.LastPathBlockedByDoor)
                            {
                                LogDoorBlockedRoute();
                                hasMoveTarget = false;
                                return Vector3.zero;
                            }

                            // Cannot route from here (e.g. wedged on furniture under a
                            // low ceiling, where every probe fails): step off toward the
                            // player and re-plan from the new spot.
                            Vector3 toGoal = goal - transform.position;
                            toGoal.y = 0f;
                            if (toGoal.sqrMagnitude < 0.001f) toGoal = transform.forward;

                            toGoal.Normalize();
                            toGoal = Quaternion.Euler(0f, Random.Range(-30f, 30f), 0f) * toGoal;
                            followStepOffTarget = transform.position + toGoal * 1.5f;
                            followStepOffUntil = Time.time + 1.2f;
                            if (debug)
                            {
                                YourBuddyPlugin.Log.LogInfo(
                                "[ai] No routable plan - stepping off toward the player");
                            }
                        }
                        // Keeping the old path beats a blind direct walk - only while it is a
                        // path from here. docs/invariants.md#a-stale-plan-is-worse-than-none
                        else if (leftStairLeg || !BuddyNodeGraph.WaypointIsOnAReachableDeck(
                                     start, navPlan.Value[navPathIndex]))
                        {
                            YourBuddyPlugin.Log.LogWarning(
                                $"[ai] Dropping a stale plan: {navPlan.Value[navPathIndex]:0.0} is not on a deck " +
                                $"I can reach from {start:0.0}");
                            navPlan = null;
                            hasNavPathGoal = false;
                            navPathIndex = 0;
                        }
                        // else: keep walking the old path - it beats a blind direct walk.
                    }
                }

                // Follow path if available
                if (navPlan != null && navPathIndex < navPlan.Value.Count)
                {
                    AdvancePastReachedWaypoints();

                    if (navPathIndex < navPlan.Value.Count)
                    {
                        wantMove = true;
                        currentMoveTarget = navPlan.Value[navPathIndex];
                        hasMoveTarget = true;
                        return HeadingAlongPlan() * moveSpeed;
                    }
                }

                // Direct fallback if no node path available
                wantMove = true;
                currentMoveTarget = playerTransform.position;
                hasMoveTarget = true;
                return toPlayer.normalized * moveSpeed;
            }

            hasMoveTarget = false;
            navPlan = null;
            entryBlockedCount = 0;
            return Vector3.zero;
        }

        /// <summary>
        /// Motionless above its own floor plane means perched on something it cannot
        /// route from. Every other watchdog needs wantMove, so nothing else sees this.
        /// docs/invariants.md#idle-above-the-floor-plane-is-a-stall
        /// </summary>
        private void UpdateIdleRecovery(bool wantMove)
        {
            if (wantMove || catchInProgress || mode == BuddyMode.Stay || mode == BuddyMode.Dead)
            {
                idleNoRouteSeconds = 0f;
                return;
            }

            idleNoRouteSeconds += Time.deltaTime;
            if (idleNoRouteSeconds < IdleNoRouteGiveUp || Time.time < idleStepOffCooldown) return;

            idleNoRouteSeconds = 0f;

            // On its own deck this is legitimate waiting - arrived, or holding for a
            // door it cannot open. Only an elevated perch is a stall.
            if (!hasBaseFloor || FloorUnderBuddy().y - baseFloorY < IdleAboveFloorDelta) return;

            idleStepOffCooldown = Time.time + 6f;
            Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            followStepOffTarget = transform.position + dir * 2f;
            followStepOffUntil = Time.time + 1.2f;
            navPlan = null;
            YourBuddyPlugin.Log.LogWarning(
                "[ai] Idle " + IdleNoRouteGiveUp.ToString("0.0") + "s above my own floor with no route - stepping off");
        }

        /// <summary>
        /// Says why the buddy is standing still, throttled. A silent stall is
        /// indistinguishable from a forgotten one.
        /// </summary>
        private void LogDoorBlockedRoute()
        {
            if (Time.time < doorBlockedLogAt) return;

            doorBlockedLogAt = Time.time + 10f;
            YourBuddyPlugin.Log.LogInfo(
                "[ai] No route that avoids a door I cannot open - waiting here" +
                (impassableGates.Count > 0 ? " (" + impassableGates.Count + " closed to me)" : ""));
        }

        /// <summary>
        /// Stay mode: hold position. Only the step-off stretch may move the buddy, so a
        /// staying buddy still clears a doorway it is blocking.
        /// docs/invariants.md#step-off-applies-in-every-mode
        /// </summary>
        private Vector3 UpdateStay(out bool wantMove)
        {
            wantMove = false;
            if (TryStepOff(out Vector3 stepOff))
            {
                wantMove = true;
                return stepOff;
            }
            hasMoveTarget = false;
            return Vector3.zero;
        }

        /// <summary>
        /// Advances navPathIndex past every waypoint the buddy has reached, on a dual
        /// threshold - XZ on its own deck, 3D for anything above or below. Both
        /// branches must stay: docs/invariants.md#waypoint-advance-is-dual
        /// </summary>
        private void AdvancePastReachedWaypoints()
        {
            if (navPlan == null) return;

            float floorY = FloorUnderBuddy().y;
            // The stair band reads the body, not the probe: docs/invariants.md#off-the-flight-is-off-the-plan
            float feetY = GroundPos(0f).y;
            while (navPathIndex < navPlan.Value.Count)
            {
                Vector3 wp = navPlan.Value[navPathIndex];
                float distXZ = new Vector2(transform.position.x - wp.x, transform.position.z - wp.z).magnitude;
                float dist3DSquared = (transform.position - wp).sqrMagnitude;
                // Deck to deck: a waypoint's own Y is the marker height.
                // docs/invariants.md#floor-to-floor
                bool sameLevel = Mathf.Abs(floorY - NavProbe.FloorHeight(wp)) <= WaypointAdvanceMaxDeltaY;
                bool reached = (distXZ < WaypointReachedXZ && sameLevel) ||
                               dist3DSquared < WaypointReached3D * WaypointReached3D;

                // The leg that leaves wp, when it is a flight.
                if (TryStairLegBand(navPathIndex + 1, out float low, out float high))
                {
                    // Reached is not enough: never advance into a flight whose decks the buddy
                    // is off. docs/invariants.md#off-the-flight-is-off-the-plan
                    if (feetY < low || feetY > high) break;

                    // ...but already walking it counts as reached, as after a mid-flight
                    // replan. docs/invariants.md#a-stair-leg-is-walked-not-improvised
                    ProjectOntoLeg(wp, navPlan.Value[navPathIndex + 1], out _, out float length,
                        out float along, out float across);
                    reached |= along >= 0f && along <= length && across <= StairLegCorridor;
                }
                if (!reached) break;

                // Only real nodes: waypoint 0 is usually the plan's own start, and
                // penalizing entries near that pushes the buddy off its own spot.
                if (BuddyNodeGraph.IsNodePosition(wp))
                {
                    lastDepartedWaypoint = wp;
                    hasLastDepartedWaypoint = true;
                }
                navPathIndex++;
            }
        }

        /// <summary>
        /// Short unvalidated walk toward followStepOffTarget: gets the buddy off chair
        /// seats and ledges, and out of a doorway it is holding open. Every mode:
        /// docs/invariants.md#step-off-applies-in-every-mode
        /// </summary>
        private bool TryStepOff(out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (Time.time >= followStepOffUntil) return false;

            currentMoveTarget = followStepOffTarget;
            hasMoveTarget = true;
            Vector3 toStep = followStepOffTarget - transform.position;
            toStep.y = 0f;
            if (toStep.sqrMagnitude >= 0.001f) velocity = toStep.normalized * moveSpeed;

            return true;
        }

        /// <summary>
        /// True when the stretch the buddy is walking is the graph edge the cache
        /// already validated, not an improvised line from off the route.
        /// </summary>
        private bool IsStandingOnRoute(Vector3 start)
        {
            if (navPlan == null || navPathIndex < 1 || navPathIndex >= navPlan.Value.Count) return false;

            return (start - navPlan.Value[navPathIndex - 1]).sqrMagnitude <=
                   NavPathOnRouteRadius * NavPathOnRouteRadius;
        }

        /// <summary>
        /// True when the remaining old waypoints reappear at the tail of the new plan, which
        /// starts at `resumeAt` (same route; the final waypoint is the goal and may drift).
        /// </summary>
        private static bool SameRemainingPath(IReadOnlyList<Vector3> oldPath, int oldIndex, IReadOnlyList<Vector3> newPath,
            out int resumeAt)
        {
            int remaining = oldPath.Count - oldIndex;
            resumeAt = newPath.Count - remaining;
            if (remaining <= 0 || resumeAt < 0) return false;

            for (int i = 0; i < remaining; i++)
            {
                float tolerance = i == remaining - 1 ? 2.0f : 0.3f;
                if ((oldPath[oldIndex + i] - newPath[resumeAt + i]).sqrMagnitude > tolerance * tolerance) return false;
            }
            return true;
        }

        /// <summary>
        /// The decks stair leg i (waypoint i-1 to i) joins, widened by the same-deck
        /// tolerance; false when both ends are on one deck and it is no flight at all.
        /// </summary>
        private bool TryStairLegBand(int i, out float low, out float high)
        {
            low = high = 0f;
            if (navPlan == null || i < 1 || i >= navPlan.Value.Count) return false;

            float a = navPlan.Value.FloorY(i - 1);
            float b = navPlan.Value.FloorY(i);
            if (Mathf.Abs(a - b) <= WaypointAdvanceMaxDeltaY) return false;

            low = Mathf.Min(a, b) - WaypointAdvanceMaxDeltaY;
            high = Mathf.Max(a, b) + WaypointAdvanceMaxDeltaY;
            return true;
        }

        /// <summary>
        /// Walking a stair leg with the feet on neither of its decks nor between them: the
        /// waypoint is not reachable along it from here. Feet, not FloorUnderBuddy: an
        /// ungrounded frame on the treads can probe a deck below. docs/invariants.md#off-the-flight-is-off-the-plan
        /// </summary>
        private bool HasLeftStairLeg(out float low, out float high)
        {
            float feetY = GroundPos(0f).y;
            return TryStairLegBand(navPathIndex, out low, out high) && (feetY < low || feetY > high);
        }

        /// <summary>
        /// The buddy against the flat line a->b: its unit direction and length, how far
        /// along it the buddy stands, and how far beside it.
        /// </summary>
        private void ProjectOntoLeg(Vector3 a, Vector3 b, out Vector3 direction, out float length,
            out float along, out float across)
        {
            direction = new Vector3(b.x - a.x, 0f, b.z - a.z);
            length = direction.magnitude;
            Vector3 offset = new(transform.position.x - a.x, 0f, transform.position.z - a.z);
            direction = length > 0.01f ? direction / length : Vector3.zero;
            along = Vector3.Dot(offset, direction);
            across = length > 0.01f
                ? Mathf.Abs(direction.x * offset.z - direction.z * offset.x)
                : offset.magnitude;
        }

        /// <summary>
        /// Flat heading for the current waypoint. On a stair leg it aims a tread's length
        /// along the leg, not at its far end, so the buddy turns onto the flight instead of
        /// cutting the corner. docs/invariants.md#a-stair-leg-is-walked-not-improvised
        /// </summary>
        private Vector3 HeadingAlongPlan()
        {
            walkingStairLeg = false;
            if (navPlan is not { } plan)
            {
                return Vector3.zero;
            }

            Vector3 aim = plan[navPathIndex];
            walkingStairLeg = TryStairLegBand(navPathIndex, out _, out _);
            if (walkingStairLeg)
            {
                Vector3 from = plan[navPathIndex - 1];
                ProjectOntoLeg(from, aim, out Vector3 direction, out float length, out float along, out _);
                LogStairLeg(plan, from, aim);
                aim = from + direction * Mathf.Clamp(along + StairLegLookahead, 0f, length);
            }
            Vector3 heading = aim - transform.position;
            heading.y = 0f;
            return heading.normalized;
        }

        /// <summary>
        /// Once per leg, so a capture shows where the stair rule took over.
        /// </summary>
        private void LogStairLeg(BuddyNodeGraph.NavPath plan, Vector3 from, Vector3 to)
        {
            if (YourBuddyPlugin.ConfigDebugLevel.Value < 2 || (to - loggedStairLegEnd).sqrMagnitude < 0.01f) return;

            loggedStairLegEnd = to;
            YourBuddyPlugin.Log.LogInfo(
                $"[ai] Walking the stair leg {from:0.0} -> {to:0.0} " +
                $"(decks {plan.FloorY(navPathIndex - 1):0.00} -> {plan.FloorY(navPathIndex):0.00})");
        }

        private Vector3 UpdateWander(out bool wantMove)
        {
            wantMove = false;

            if (TryStepOff(out Vector3 stepOff))
            {
                wantMove = true;
                return stepOff;
            }

            // Follow path to wander target
            if (navPlan != null && navPathIndex < navPlan.Value.Count)
            {
                // Hard give-up timer
                float budget = 6f + Vector3.Distance(transform.position, wanderTarget) * 1.2f;
                if (Time.time - wanderTargetSince > budget)
                {
                    YourBuddyPlugin.Log.LogInfo("[ai] Wander target unreachable, picking a new one");
                    navPlan = null;
                    wanderIdleUntil = Time.time + WanderRetryDelay;
                    hasMoveTarget = false;
                    return Vector3.zero;
                }

                // Advance past waypoints (same level-aware threshold as Follow).
                AdvancePastReachedWaypoints();

                if (navPathIndex >= navPlan.Value.Count)
                {
                    // Reached the end of the path: a short breath, not a long stare.
                    navPlan = null;
                    wanderIdleUntil = Time.time + Random.Range(WanderIdleMin, WanderIdleMax);
                    hasMoveTarget = false;
                    return Vector3.zero;
                }

                // docs/invariants.md#off-the-flight-is-off-the-plan
                if (HasLeftStairLeg(out float legLow, out float legHigh))
                {
                    // Wander has no replan-and-keep-walking path, so standing still re-plans
                    // the same unwalkable leg from the same spot forever. Walk off it first:
                    // docs/invariants.md#step-off-applies-in-every-mode
                    bool stepping = TryStepOffTowardLegHead();
                    YourBuddyPlugin.Log.LogInfo(
                        $"[ai] Left the stair leg to the wander target (feet {GroundPos(0f).y:0.00}, probe floor " +
                        $"{FloorUnderBuddy().y:0.00}, outside {legLow:0.00}..{legHigh:0.00}), " +
                        (stepping ? $"stepping off toward {followStepOffTarget:0.0} and picking a new one"
                                  : "picking a new one"));
                    navPlan = null;
                    wanderIdleUntil = Time.time + WanderRetryDelay;
                    hasMoveTarget = false;
                    return Vector3.zero;
                }

                wantMove = true;
                currentMoveTarget = navPlan.Value[navPathIndex];
                hasMoveTarget = true;
                return HeadingAlongPlan() * (moveSpeed * 0.85f);
            }

            if (Time.time >= wanderIdleUntil && TryStartWanderRoute()) return Vector3.zero;

            hasMoveTarget = false;
            return Vector3.zero;
        }

        /// <summary>
        /// Picks a wander target and plans to it in one go, trying a few candidates.
        /// Picking and planning used to be separate frames with their own idle timers,
        /// which is how a failed pick turned into several seconds of standing still.
        /// </summary>
        private bool TryStartWanderRoute()
        {
            if (BuddyNodeGraph.NodeCount == 0)
            {
                StepOffAfterWanderFailure();
                return false;
            }

            int planned = 0;
            for (int attempt = 0; attempt < WanderPickAttempts; attempt++)
            {
                // Autonomous wandering stays on the owner it was chosen on. docs/behaviour.md
                Vector3 node = BuddyNodeGraph.RandomNode(wanderOwner);
                if (node == Vector3.zero) break;
                // Not over toward the Breathless while it has the buddy on edge. docs/fear.md
                if (fearState != FearState.Calm && (node - lastMonsterPos).sqrMagnitude <
                    FearRestraintDist * FearRestraintDist)
                {
                    continue;
                }

                planned++;

                // From the floor, like Follow: docs/invariants.md#floor-to-floor
                BuddyNodeGraph.NavPath? plan = BuddyNodeGraph.FindPath(FloorUnderBuddy(), node);
                if (plan is { Count: > 0 })
                {
                    wanderTarget = node;
                    wanderTargetSince = Time.time;
                    wanderPickFailures = 0;
                    navPlan = plan;
                    navPathIndex = 0;
                    return true;
                }

                // docs/invariants.md#stand-still-when-door-blocked
                if (BuddyNodeGraph.LastPathBlockedByDoor)
                {
                    LogDoorBlockedRoute();
                    wanderIdleUntil = Time.time + WanderRetryDelay;
                    return false;
                }
            }

            wanderIdleUntil = Time.time + WanderRetryDelay;
            // Every pick turned down for being by the monster is a choice, not a stranded buddy.
            if (planned > 0 || fearState == FearState.Calm) StepOffAfterWanderFailure();

            return false;
        }

        /// <summary>
        /// Walks back toward the head of the leg just abandoned - the one place the route is
        /// known to resume from. False when a step-off is already running.
        /// docs/invariants.md#step-off-applies-in-every-mode
        /// </summary>
        private bool TryStepOffTowardLegHead()
        {
            if (Time.time < followStepOffUntil) return false;

            Vector3 direction = navPlan is { } plan && navPathIndex >= 1 && navPathIndex < plan.Count
                ? plan[navPathIndex - 1] - transform.position
                : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = transform.forward;

            followStepOffTarget = transform.position + direction.normalized * 1.5f;
            followStepOffUntil = Time.time + 1.2f;
            return true;
        }

        /// <summary>
        /// Nothing in the graph is reachable from here - most often because the buddy
        /// climbed onto furniture. Walk somewhere, anywhere, and re-plan from there.
        /// docs/invariants.md#step-off-applies-in-every-mode
        /// </summary>
        private void StepOffAfterWanderFailure()
        {
            wanderPickFailures++;
            if (wanderPickFailures < 3 || Time.time < followStepOffUntil) return;

            wanderPickFailures = 0;
            Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            followStepOffTarget = transform.position + dir * Random.Range(1.5f, 2.5f);
            followStepOffUntil = Time.time + 1.2f;
            YourBuddyPlugin.Log.LogInfo("[ai] No reachable wander targets - stepping off to re-plan");
        }

        private Vector3 UpdateRoute(out bool wantMove)
        {
            wantMove = false;

            if (TryStepOff(out Vector3 stepOff))
            {
                wantMove = true;
                return stepOff;
            }

            if (navPlan == null || navPlan.Value.Count == 0 || navPathIndex >= navPlan.Value.Count)
            {
                if (reachTask != null) return reachTask.Approach(out wantMove);

                FinishRoute();
                return Vector3.zero;
            }

            Vector3 waypoint = navPlan.Value[navPathIndex];
            Vector3 toWaypoint = waypoint - transform.position;
            toWaypoint.y = 0f;

            if (toWaypoint.sqrMagnitude < 0.2025f)
            {
                navPathIndex++;
                return Vector3.zero;
            }

            wantMove = true;
            currentMoveTarget = waypoint;
            hasMoveTarget = true;
            return toWaypoint.normalized * moveSpeed;
        }

        private void FinishRoute()
        {
            DropPlan();
            DropReachTask();
            // Arriving is what a goto order asked for, so it ends here. docs/behaviour.md
            bool ordered = orderedMode == BuddyMode.Route;
            if (ordered) orderedMode = null;
            mode = ModeAfterTask;

            decideAt = 0f;
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
        private void StartRoute(BuddyNodeGraph.NavPath plan, Vector3 goal)
        {
            if (plan.Count == 0)
            {
                YourBuddyPlugin.Log.LogWarning("[ai] Route has no points");
                return;
            }

            navPlan = plan;
            navPathIndex = 0;
            routeGoal = goal;
            mode = BuddyMode.Route;
            YourBuddyPlugin.Log.LogInfo($"[ai] Walking path ({plan.Count} waypoints)");
        }

        /// <summary>
        /// Switches mode and forgets the plan. Leaves followStepOffUntil, wanderIdleUntil,
        /// unreachableWaypointUntil, followWaitUntil and sidestepUntil alone on purpose.
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
            DropPlan();
        }

        /// <summary>
        /// Forgets the plan and what was learned walking it.
        /// </summary>
        private void DropPlan()
        {
            navPlan = null;
            navPathIndex = 0;
            hasNavPathGoal = false;
            entryBlockedCount = 0;
            hasLastDepartedWaypoint = false;
        }

        private bool HasRoute()
        {
            return mode == BuddyMode.Route && navPlan != null;
        }


    }
}

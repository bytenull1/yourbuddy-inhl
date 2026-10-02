using NPC.Core;
using NPC.Core.Agents;
using NPC.Core.Navigation;
using NPC.Core.World;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// WindowStare and WallStare. It walks up to a window or a wall and stares. docs/anomalies.md#windowstare-and-wallstare
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        private static readonly RaycastHit[] WallHits = new RaycastHit[8];
        private const float StareMinSeconds = 50f;
        private const float StareMaxSeconds = 140f;
        private const float StareSearchRadius = 20f;
        private const float WallSearchDist = 4f;
        private const float WallStandOff = 0.45f;

        // Window and wall

        /// <summary>
        /// A window on its own vessel within StareSearchRadius, and a node in front of it to stand on.
        /// </summary>
        private string? StartWindowStare()
        {
            Vector3 here = transform.position;
            Renderer? best = null;
            float bestDist = float.MaxValue;
            foreach (MeshRenderer renderer in SceneScan.ThisFrame<MeshRenderer>())
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;

                // The pane itself. Every window block of ship and station has a child 'Glass'.
                if (!renderer.name.StartsWith("Glass")) continue;

                float d = Vector3.Distance(renderer.bounds.center, here);
                if (d > StareSearchRadius || d >= bestDist) continue;

                if (!agent.OnMyVessel(renderer.transform)) continue;

                best = renderer;
                bestDist = d;
            }
            if (best == null) return $"no window within {StareSearchRadius:0}m";

            Vector3 window = best.bounds.center;
            Vector3? stand = NodeNear(window, 0.8f, 4f);
            if (stand == null) return "no node in front of the window";

            NavPath? plan = NavGraph.FindPath(agent.FloorUnderNpc(), stand.Value);
            if (plan is not { Count: > 0 }) return "no way to the window";

            StartHold(AnomalyKind.WindowStare, StareMaxSeconds + 60f);
            agent.CommitPlan(plan.Value, false);
            anomalyWalking = true;
            anomalyStand = stand.Value;
            anomalyArrival = NpcAgent.ReachStandArrival;
            anomalyFace = window;
            anomalyGiveUpAt = Time.time + 10f + Vector3.Distance(here, stand.Value) * 1.5f;
            return null;
        }

        /// <summary>
        /// The nearest wall around it at chest height, walked to in a straight line.
        /// </summary>
        private string? StartWallStare()
        {
            Vector3 chest = agent.GroundPos(1.1f);
            Vector3 bestHit = Vector3.zero;
            Vector3 bestDir = Vector3.zero;
            float bestDist = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                int count = Physics.RaycastNonAlloc(chest, dir, WallHits, WallSearchDist, NavProbe.ProbeLayers,
                    QueryTriggerInteraction.Ignore);
                for (int h = 0; h < count; h++)
                {
                    RaycastHit hit = WallHits[h];
                    if (!IsWall(hit.collider)) continue;

                    if (hit.distance >= bestDist) continue;

                    bestDist = hit.distance;
                    bestHit = hit.point;
                    bestDir = dir;
                }
            }
            if (bestDist > WallSearchDist) return $"no wall within {WallSearchDist:0}m";

            Vector3 floor = agent.FloorUnderNpc();
            Vector3 stand = bestHit - bestDir * WallStandOff;
            stand.y = floor.y;
            if (Vector3.Distance(stand, floor) > 0.3f && !NavProbe.WalkLos(floor, stand, 0.5f)) return "the wall cannot be walked up to";

            StartHold(AnomalyKind.WallStare, StareMaxSeconds + 60f);
            anomalyWalking = true;
            anomalyStand = stand;
            anomalyArrival = NpcAgent.ReachStandArrival;
            anomalyFace = new Vector3(bestHit.x, chest.y + 0.4f, bestHit.z);
            anomalyGiveUpAt = Time.time + 8f;
            return null;
        }

        /// <summary>
        /// Building, not a body or a loose item, which is what a wall stare may face.
        /// </summary>
        private static bool IsWall(Collider? collider)
        {
            if (collider == null) return false;

            Transform t = collider.transform;
            if (NpcPlayer.Controller is { } you && t.IsChildOf(you.transform)) return false;

            return t.GetComponentInParent<NpcAgent>() == null && t.GetComponentInParent<Grabbable>() == null;
        }


        /// <summary>
        /// Steer while it walks to a window, a wall or through a door. The plan, then a straight stretch
        /// to the stand point.
        /// </summary>
        private Vector3 WalkAnomalyLeg(out bool wantMove)
        {
            wantMove = false;
            if (Time.time > anomalyGiveUpAt)
            {
                if (anomaly == AnomalyKind.ShutDoors) NextDoorOrEnd("that door took too long");
                else if (anomaly == AnomalyKind.Bloody) StopBloodyRun();
                else if (anomaly == AnomalyKind.Meat) StopCaughtRun();
                else EndAnomaly("the walk there took too long");

                return Vector3.zero;
            }
            // Running from you after the robot, a walk otherwise.
            float speed = anomaly is AnomalyKind.BotTalk or AnomalyKind.Bloody or AnomalyKind.Meat
                ? agent.WalkSpeed * FleeSpeedFactor
                : agent.WalkSpeed;
            if (agent.HasPlanLeft)
            {
                agent.AdvancePlan();
                if (agent.HasPlanLeft) return agent.HeadAlongPlan(speed, out wantMove);

                agent.DropPlan();
            }
            Vector3 toStand = anomalyStand - transform.position;
            toStand.y = 0f;
            if (toStand.sqrMagnitude <= anomalyArrival * anomalyArrival)
            {
                anomalyWalking = false;
                agent.ClearMoveTarget();
                if (anomaly == AnomalyKind.ShutDoors)
                {
                    anomalyStepAt = Time.time + DoorWaitSeconds;
                    return Vector3.zero;
                }
                if (anomaly == AnomalyKind.BotTalk)
                {
                    anomalyStep = 3;
                    anomalyUntil = Time.time + RunOffLegSeconds;
                    return Vector3.zero;
                }
                if (anomaly == AnomalyKind.Bloody)
                {
                    StopBloodyRun();
                    return Vector3.zero;
                }
                if (anomaly == AnomalyKind.Meat)
                {
                    StopCaughtRun();
                    return Vector3.zero;
                }
                anomalyUntil = Time.time + Random.Range(StareMinSeconds, StareMaxSeconds);
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} stops and stares at the " +
                                            (anomaly == AnomalyKind.WindowStare ? "window" : "wall") +
                                            $" for {anomalyUntil - Time.time:0}s");
                return Vector3.zero;
            }
            wantMove = true;
            agent.SetMoveTarget(anomalyStand);
            return toStand.normalized * speed;
        }
    }
}

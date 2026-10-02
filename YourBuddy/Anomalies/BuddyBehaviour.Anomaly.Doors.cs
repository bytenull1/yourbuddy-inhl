using System.Collections.Generic;
using NPC.Core;
using NPC.Core.Navigation;
using NPC.Core.World;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// ShutDoors. It goes round shutting the doors. docs/anomalies.md#shutdoors
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        private readonly List<Gate> doorsToShut = [];
        /// <summary>
        /// Passes over the doors still open, and how many were shut when the last one began.
        /// </summary>
        private int doorPasses = 0;
        private int doorsShutAtPass = 0;
        /// <summary>
        /// Past this door it has already stepped further out of the doorway once.
        /// </summary>
        private bool doorSteppedClear = false;
        private static readonly float[] DoorPassDists = [DoorPassDist, 1.1f];
        private const float DoorPassDist = 1.6f;
        private const float DoorClearDist = 1.8f;
        private const int MaxDoorPasses = 3;
        private const float DoorLegSeconds = 25f;
        private const float DoorWaitSeconds = 4f;
        private const int MaxDoors = 6;

        // Doors

        /// <summary>
        /// A round of the open room doors aboard, nearest next. It walks through each and the agent shuts
        /// it behind it (CloseBehind), clear of the doorway and of you. Airlocks, locked and password doors
        /// are left alone. npc-core:docs/invariants.md#close-only-what-you-walked-through
        /// </summary>
        private string? StartShutDoors(Transform you)
        {
            if (NpcVessels.FloorOwner(you.position) != FloorOwnership.PlayerShip) return "you are not aboard";

            if (!agent.IsAboardPlayerShip()) return "it is not aboard";

            if (!YourBuddyPlugin.ConfigAutoDoors.Value) return "it may not touch doors (AutoDoors)";

            doorsToShut.Clear();
            foreach (Gate gate in SceneScan.ThisFrame<Gate>())
            {
                if (gate == null || !gate.Opened || gate.Locked || !gate.gameObject.activeInHierarchy) continue;

                if (NpcVessels.OwnerOfTransform(gate.transform) != NavGraph.ShipOwner) continue;

                if (NpcDoors.IsAirlockGate(gate) || NpcDoors.IsPasswordGate(gate)) continue;

                doorsToShut.Add(gate);
            }
            if (doorsToShut.Count < 2) return $"only {doorsToShut.Count} door(s) open aboard";

            OrderDoorsNearestNext(transform.position);
            StartHold(AnomalyKind.ShutDoors, 2 * MaxDoors * DoorLegSeconds + 10f);
            anomalyStep = -1;
            doorPasses = 1;
            doorsShutAtPass = 0;
            if (NextDoor()) return null;

            anomaly = null;
            return "no open door it can walk through";
        }

        /// <summary>
        /// Nearest to the buddy first, then nearest to the last, at most MaxDoors.
        /// </summary>
        private void OrderDoorsNearestNext(Vector3 from)
        {
            for (int i = 0; i < doorsToShut.Count; i++)
            {
                int best = i;
                for (int j = i + 1; j < doorsToShut.Count; j++)
                {
                    if (FlatDistance(doorsToShut[j].transform.position, from) < FlatDistance(doorsToShut[best].transform.position, from)) best = j;
                }
                (doorsToShut[i], doorsToShut[best]) = (doorsToShut[best], doorsToShut[i]);
                from = doorsToShut[i].transform.position;
            }
            if (doorsToShut.Count > MaxDoors) doorsToShut.RemoveRange(MaxDoors, doorsToShut.Count - MaxDoors);
        }

        /// <summary>
        /// The walk through the next door it can plan through. False when none is left.
        /// </summary>
        private bool NextDoor()
        {
            while (++anomalyStep < doorsToShut.Count)
            {
                Gate gate = doorsToShut[anomalyStep];
                if (gate == null || !gate.Opened) continue;

                if (FarSideOf(gate, transform.position) is not { } beyond)
                {
                    SkipDoor(gate, "no clear walk through its doorway");
                    continue;
                }
                NavPath? plan = NavGraph.FindPath(agent.FloorUnderNpc(), beyond);
                if (plan is not { Count: > 0 })
                {
                    SkipDoor(gate, "no path to its far side");
                    continue;
                }
                if (!agent.CloseBehind(gate))
                {
                    SkipDoor(gate, "the agent will not close it");
                    continue;
                }

                agent.CommitPlan(plan.Value, false);
                anomalyWalking = true;
                anomalyStand = beyond;
                anomalyArrival = 0.6f;
                doorSteppedClear = false;
                anomalyFace = gate.transform.position;
                anomalyGiveUpAt = Time.time + DoorLegSeconds;
                TraceDoors($"through '{gate.name}' ({anomalyStep + 1} of {doorsToShut.Count})");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Still in reach of the door it waits to see shut, so a straight step further out, once.
        /// </summary>
        private void StepClearOf(Gate gate, float now)
        {
            doorSteppedClear = true;
            Vector3 away = transform.position - gate.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;

            Vector3 floor = agent.FloorUnderNpc();
            Vector3 clear = gate.transform.position + away.normalized * (DoorClearDist + 0.4f);
            clear.y = floor.y;
            if (!NavProbe.WalkLos(floor, clear, 0.5f))
            {
                TraceDoors($"in reach of '{gate.name}' and no room to step clear");
                return;
            }
            anomalyWalking = true;
            anomalyStand = clear;
            anomalyArrival = 0.3f;
            anomalyGiveUpAt = now + 4f;
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} steps clear of '{gate.name}' so it can shut");
        }

        private void SkipDoor(Gate gate, string why) =>
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} leaves '{gate.name}' open - {why}");

        private void NextDoorOrEnd(string why)
        {
            agent.DropPlan();
            anomalyWalking = false;
            if (NextDoor()) return;

            // Another pass over what is still open (a door the agent had not shut by the time it moved on,
            // or one planned around, from a different side now) while a pass still shuts something.
            int shut = DoorsShut();
            if (shut < doorsToShut.Count && doorPasses < MaxDoorPasses && (doorPasses == 1 || shut > doorsShutAtPass))
            {
                doorPasses++;
                doorsToShut.RemoveAll(g => g == null || !g.Opened);
                doorsShutAtPass = 0;
                OrderDoorsNearestNext(transform.position);
                anomalyStep = -1;
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} goes round again for {doorsToShut.Count} door(s) still open: {OpenDoorNames()}");
                if (NextDoor()) return;
            }
            string open = OpenDoorNames();
            EndAnomaly($"{DoorsShut()} of {doorsToShut.Count} doors shut ({why})" + (open.Length > 0 ? " - left open: " + open : ""));
        }

        private string OpenDoorNames()
        {
            string names = "";
            foreach (Gate gate in doorsToShut)
            {
                if (gate != null && gate.Opened) names += (names.Length > 0 ? ", " : "") + "'" + gate.name + "'";
            }
            return names;
        }

        private int DoorsShut()
        {
            int shut = 0;
            foreach (Gate gate in doorsToShut)
            {
                if (gate != null && !gate.Opened) shut++;
            }
            return shut;
        }

        /// <summary>
        /// A ground node DoorPassDist beyond the doorway, on the side away from `here`. The doorway's
        /// axis is whichever of the gate's own two that walks clear both ways from its middle.
        /// </summary>
        private static Vector3? FarSideOf(Gate gate, Vector3 here)
        {
            Vector3 middle = gate.transform.position;
            if (NavProbe.TryFloorHeight(middle + Vector3.up * 0.5f, out float floorY)) middle.y = floorY;

            // A narrow room behind the door may not leave 1.6 m clear, so a shorter step is tried next.
            foreach (float pass in DoorPassDists)
            {
                foreach (Vector3 axis in new[] { gate.transform.forward, gate.transform.right })
                {
                    Vector3 flat = new(axis.x, 0f, axis.z);
                    if (flat.sqrMagnitude < 0.01f) continue;

                    flat.Normalize();
                    Vector3 a = middle + flat * pass;
                    Vector3 b = middle - flat * pass;
                    if (!NavProbe.WalkLos(middle, a, 0.5f) || !NavProbe.WalkLos(middle, b, 0.5f)) continue;

                    Vector3 far = FlatDistance(a, here) > FlatDistance(b, here) ? a : b;
                    Vector3 dir = (far - middle).normalized;
                    // Clear of the doorway, since the agent never closes a door on itself, and the anomaly holds it
                    // still past the door. npc-core:docs/invariants.md#never-force-a-close-into-the-npc
                    Vector3? node = NodeNear(far, 0f, 1.5f);
                    if (node != null && Vector3.Dot(node.Value - middle, dir) > 0f && FlatDistance(node.Value, middle) >= DoorClearDist) return node;

                    Vector3 clear = middle + dir * DoorClearDist;
                    return NavProbe.WalkLos(middle, clear, 0.5f) ? clear : far;
                }
            }
            return null;
        }

        private void TraceDoors(string line)
        {
            if (NpcLog.Level >= 2) YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} shutting doors: {line}");
        }
    }
}

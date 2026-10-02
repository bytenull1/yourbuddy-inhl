using System.Collections.Generic;
using NPC.Core.Navigation;
using NPC.Core.World;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Node searches and station lookups the set pieces share to place the buddy and its doubles.
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        private static readonly List<Vector3> NodesFound = [];

        /// <summary>
        /// Active ground nodes on `owner` (any when null), `min`..`max` from `center` on the flat and within
        /// `rise` of its height. One shared list, so read it before the next call.
        /// </summary>
        private static List<Vector3> GroundNodes(Vector3 center, string? owner, float min, float max, float rise)
        {
            NodesFound.Clear();
            for (int i = 0; i < NavGraph.NodeCount; i++)
            {
                if (!NavGraph.IsNodeActive(i) || NavGraph.GetNodeType(i) != NodeType.Ground) continue;

                if (owner != null && NavGraph.GetNodeOwner(i) != owner) continue;

                Vector3 node = NavGraph.GetNodeWorld(i);
                if (Mathf.Abs(node.y - center.y) > rise) continue;

                float d = FlatDistance(node, center);
                if (d >= min && d <= max) NodesFound.Add(node);
            }
            return NodesFound;
        }

        /// <summary>
        /// The active node nearest `point` on the flat, between `min` and `max` from it.
        /// </summary>
        private static Vector3? NodeNear(Vector3 point, float min, float max)
        {
            Vector3? best = null;
            float bestDist = float.MaxValue;
            foreach (Vector3 node in GroundNodes(point, null, min, max, 2.5f))
            {
                float d = FlatDistance(node, point);
                if (d >= bestDist) continue;

                best = node;
                bestDist = d;
            }
            return best;
        }

        /// <summary>
        /// You see neither the chest nor the shins of someone standing on `node`.
        /// </summary>
        private static bool Unseen(Vector3 node) =>
            !PlayerView.Sees(node + Vector3.up * 1.1f, null) && !PlayerView.Sees(node + Vector3.up * 0.3f, null);

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>
        /// The station by its owner name (its GameObject's), or null for a ship or anything else.
        /// </summary>
        private static SpaceStation? StationNamed(string owner)
        {
            foreach (SpaceStation station in Object.FindObjectsOfType<SpaceStation>())
            {
                if (station != null && station.gameObject.name == owner) return station;
            }
            return null;
        }

        /// <summary>
        /// The station you stand aboard (never a ship), or null.
        /// </summary>
        private static SpaceStation? StationUnder(Vector3 you) =>
            NpcVessels.FloorOwner(you, out string? owner, out _) == FloorOwnership.Elsewhere && owner != null ? StationNamed(owner) : null;
    }
}

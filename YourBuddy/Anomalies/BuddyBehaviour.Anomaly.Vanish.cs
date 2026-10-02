using System.Collections.Generic;
using NPC.Core.Navigation;
using NPC.Core.World;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Vanish, Stalker and BehindYou. Gone from sight, back somewhere else. docs/anomalies.md#vanish
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        // Vanished means drawn nowhere, no collisions, no AI. docs/invariants.md#an-anomaly-puts-back-what-it-changed
        private bool vanished = false;
        private readonly List<Renderer> vanishedRenderers = [];

        private static readonly float[] BehindTurns = [0f, 30f, -30f];
        private const float VanishMinSeconds = 40f;
        private const float VanishMaxSeconds = 110f;
        private const float ReappearMinDist = 6f;
        private const float ReappearMaxDist = 16f;
        private const float BehindYouDist = 1.2f;

        // Vanishing

        private void Disappear()
        {
            vanishedRenderers.Clear();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled) continue;

                renderer.enabled = false;
                vanishedRenderers.Add(renderer);
            }
            cc.detectCollisions = false;
            // Asleep, it never walks through the doors it owes, so they close behind it now.
            agent.LeaveDoors();
            agent.ClearMoveTarget();
            agent.DropPlan();
            Asleep = true;
            vanished = true;
        }

        /// <summary>
        /// Back where it is now. Only the renderers it switched off return, so the mask SpawnBuddy hid stays hidden.
        /// </summary>
        private void Reappear()
        {
            if (!vanished) return;

            foreach (Renderer renderer in vanishedRenderers)
            {
                if (renderer != null) renderer.enabled = true;
            }
            vanishedRenderers.Clear();
            if (cc != null) cc.detectCollisions = true;

            Asleep = false;
            vanished = false;
        }

        /// <summary>
        /// A node on your vessel, out of your sight, ReappearMinDist..MaxDist from you; null when none.
        /// </summary>
        private Vector3? HiddenSpotNear(Transform you)
        {
            NpcVessels.FloorOwner(you.position, out string? owner, out _);
            List<Vector3> nodes = GroundNodes(you.position, owner, ReappearMinDist, ReappearMaxDist, 3f);
            for (int tries = 0; tries < 12 && nodes.Count > 0; tries++)
            {
                int index = Random.Range(0, nodes.Count);
                Vector3 node = nodes[index];
                nodes.RemoveAt(index);
                if (Unseen(node)) return node;
            }
            return null;
        }

        /// <summary>
        /// The floor point a little behind the player, where they will not see it until they turn.
        /// </summary>
        private static Vector3? SpotBehind(Transform you)
        {
            Transform? cam = PlayerView.Camera();
            if (cam == null || NpcPlayer.Controller is not { } controller) return null;

            Vector3 back = -cam.forward;
            back.y = 0f;
            if (back.sqrMagnitude < 0.01f) return null;

            float feetY = controller.transform.position.y + controller.center.y - controller.height * 0.5f;
            Vector3 yourFeet = new(you.position.x, feetY, you.position.z);
            foreach (float turn in BehindTurns)
            {
                Vector3 dir = Quaternion.Euler(0f, turn, 0f) * back.normalized;
                Vector3 spot = yourFeet + dir * BehindYouDist;
                if (!NavProbe.TryFloorHeight(spot + Vector3.up * 0.5f, out float floorY)) continue;

                spot.y = floorY;
                if (!NavProbe.WalkLos(yourFeet, spot, 0.4f)) continue;

                return spot;
            }
            return null;
        }

        private void MoveTo(Vector3 floor)
        {
            agent.TeleportTo(floor - Vector3.up * agent.OriginToFeet, true);
            NpcVessels.FloorOwner(floor, out string? owner, out Transform? anchor);
            if (owner != null && anchor != null) agent.RideOwner(owner, anchor);
        }
    }
}

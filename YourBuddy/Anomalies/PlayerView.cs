using NPC.Core.Navigation;
using NPC.Core.World;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// What the player can see, from their camera, a view cone and the sight line. Anomalies happen out of
    /// sight and stop when watched. docs/anomalies.md#4-being-seen
    /// </summary>
    internal static class PlayerView
    {
        /// <summary>
        /// Half the cone counted as "in view". Wider than the talk window's aim, since the corner of
        /// the screen still shows the buddy.
        /// </summary>
        internal const float ViewHalfAngle = 55f;
        /// <summary>
        /// Further than this nothing counts as seen, since a figure across a station is a few pixels.
        /// </summary>
        internal const float ViewRange = 40f;
        /// <summary>
        /// Off the view by more than this, a point is behind the player.
        /// </summary>
        internal const float BehindAngle = 115f;
        /// <summary>
        /// The points of the buddy the player could see, its chest and head above its feet.
        /// </summary>
        private const float ChestAboveFeet = 1.1f;
        private const float HeadAboveFeet = 1.6f;

        /// <summary>
        /// The player's eyes, or null without a player.
        /// </summary>
        internal static Transform? Camera()
        {
            PlayerController? controller = NpcPlayer.Pilot is { } pilot ? pilot.Controller : null;
            if (controller == null) return null;

            return controller.CameraAnimator != null ? controller.CameraAnimator.CachedTransform : controller.CachedTransform;
        }

        /// <summary>
        /// How far `point` is off the centre of the view, in degrees; 180 without a player.
        /// </summary>
        internal static float AngleTo(Vector3 point)
        {
            Transform? cam = Camera();
            if (cam == null) return 180f;

            Vector3 aim = point - cam.position;
            return aim.sqrMagnitude < 0.0001f ? 0f : Vector3.Angle(cam.forward, aim);
        }

        /// <summary>
        /// Within the view cone and range, with nothing solid or a shut door between. `target`'s own
        /// colliders never block. npc-core:docs/invariants.md#sight-stops-at-a-shut-door
        /// </summary>
        internal static bool Sees(Vector3 point, Transform? target)
        {
            Transform? cam = Camera();
            if (cam == null) return false;

            Vector3 aim = point - cam.position;
            if (aim.sqrMagnitude > ViewRange * ViewRange) return false;

            if (aim.sqrMagnitude > 0.0001f && Vector3.Angle(cam.forward, aim) > ViewHalfAngle) return false;

            return NavProbe.CanSee(cam.position, point, target, out _);
        }

        /// <summary>
        /// The player sees the buddy's chest or head.
        /// </summary>
        internal static bool SeesBody(Transform body, Vector3 feet) =>
            Sees(feet + Vector3.up * ChestAboveFeet, body) || Sees(feet + Vector3.up * HeadAboveFeet, body);

        /// <summary>
        /// Behind the player's back, well off the view, whatever lies between.
        /// </summary>
        internal static bool IsBehind(Vector3 point) => AngleTo(point) >= BehindAngle;

        /// <summary>
        /// The player's yaw, as a rotation about the vertical.
        /// </summary>
        internal static Quaternion? Yaw()
        {
            Transform? cam = Camera();
            if (cam == null) return null;

            Vector3 forward = cam.forward;
            forward.y = 0f;
            return forward.sqrMagnitude < 0.0001f ? null : Quaternion.LookRotation(forward.normalized);
        }
    }
}

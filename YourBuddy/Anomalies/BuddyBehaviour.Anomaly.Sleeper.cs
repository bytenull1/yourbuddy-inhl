using NPC.Core.World;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Sleeper. A second buddy asleep in the cryo capsule. docs/anomalies.md#sleeper
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        // Sleeper state, with the copy in its capsule, the capsule, and when you last saw it. docs/anomalies.md#sleeper
        private GameObject? sleeper = null;
        private SleeperBed? sleeperBed = null;
        private Vector3 sleeperHead;
        private bool sleeperOpened = false;
        private float sleeperSeenAt = 0f;
        private bool sleeperActorFound = false;
        private const float SleeperMinDist = 8f;
        private const float SleeperWithYouDist = 25f;
        private const float SleeperWaitSeconds = 900f;
        private const float SleeperOpenDist = 2.5f;
        private const float SleeperOpenAngle = 60f;
        private const float SleeperSeeDist = 6f;
        private const float SleeperLookAngle = 45f;
        private const float SleeperGoneUnseenSeconds = 1f;
        private const float SleeperStareSeconds = 30f;

        // The second sleeper

        /// <summary>
        /// While it follows you aboard, the capsule it woke in is shut again, its monitor showing a pulse, and a
        /// copy of it sleeps inside. The door is opaque and opens by itself when you come up to it.
        /// docs/anomalies.md#sleeper
        /// </summary>
        private string? StartSleeper(Transform you, float dist, bool sameVessel)
        {
            if (NpcVessels.FloorOwner(you.position) != FloorOwnership.PlayerShip) return "you are not aboard";

            if (!sameVessel || dist > SleeperWithYouDist) return "it is not with you";

            if (mode is not BuddyMode.Follow and not BuddyMode.Wander) return "it is staying put";

            if (suit.Suited) return "it wears a suit";

            if (Model == null) return "it has no body to copy";

            if (BuddyCryoSpawn.OpenedBed(out string why) is not { } bed) return why;

            float far = FlatDistance(bed.Position, you.position);
            if (far < SleeperMinDist) return $"you are {far:0.0}m from its capsule - it needs you {SleeperMinDist:0}m away";

            if (FlatDistance(bed.Position, transform.position) < SleeperMinDist / 2f) return "it is by its capsule itself";

            if (PlayerView.Sees(bed.Position + Vector3.up * 1.2f, bed.Capsule)) return "you can see its capsule";

            GameObject? copy = BuddyDouble.Make(Model, transform, bed.Capsule, bed.Position, bed.Rotation, "YB_Sleeper");
            if (copy == null) return "it has no body to copy";

            BuddyDouble.Walk(copy, 0f);
            BuddyCryoSpawn.ShutWithSleeper(bed);
            StartHold(AnomalyKind.Sleeper, SleeperWaitSeconds, keepPlan: true);
            sleeper = copy;
            sleeperBed = bed;
            sleeperOpened = false;
            sleeperActorFound = false;
            sleeperHead = bed.Capsule.InverseTransformPoint(HeadOf(copy, bed.Position));
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} is asleep in '{bed.Capsule.name}' again, {far:0.0}m from you - and it follows you " +
                                        $"(its head {HeadOf(copy, bed.Position).y - bed.Position.y:0.00}m over the bed point)");
            return null;
        }

        /// <summary>
        /// The top of the copy's drawn body, a little down, i.e. its head. The bed point plus 1.5 m when nothing draws.
        /// </summary>
        private static Vector3 HeadOf(GameObject copy, Vector3 fallback)
        {
            bool any = false;
            Bounds all = default;
            foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;

                if (any) all.Encapsulate(renderer.bounds);
                else all = renderer.bounds;
                any = true;
            }
            return any ? new Vector3(all.center.x, all.max.y - 0.2f, all.center.z) : fallback + Vector3.up * 1.5f;
        }

        /// <summary>
        /// You look at its head from this near. The capsule is the sight test's target, so its own walls never block.
        /// </summary>
        private bool LooksAtSleeper(float near, float angle)
        {
            if (sleeperBed is not { } bed || bed.Capsule == null) return false;

            Transform? cam = PlayerView.Camera();
            Vector3 head = bed.Capsule.TransformPoint(sleeperHead);
            return cam != null && FlatDistance(cam.position, head) <= near && PlayerView.AngleTo(head) <= angle && PlayerView.Sees(head, bed.Capsule);
        }

        /// <summary>
        /// Shut until you come up to it, then it opens and there it is. Once found it raises your stress, and the
        /// one following you stands still facing you until you turn to it. Once you look away from the capsule it is empty.
        /// </summary>
        private void UpdateSleeper(float now, float dist)
        {
            if (sleeperBed is not { } bed || bed.Capsule == null)
            {
                EndAnomaly("the capsule went away");
                return;
            }
            switch (anomalyStep)
            {
                case 0:
                    if (LooksAtSleeper(SleeperOpenDist, SleeperOpenAngle))
                    {
                        anomalyStep = 1;
                        BuddyCryoSpawn.OpenForSleeper(bed, () => sleeperOpened = true);
                        YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name}'s capsule opens in front of you");
                        return;
                    }
                    if (now >= anomalyUntil) EndAnomaly("you never came to its capsule");

                    break;
                case 1:
                    if (!sleeperOpened) return;

                    anomalyStep = 2;
                    anomalyWitnessed = true;
                    anomalyUntil = now + SleeperStareSeconds;
                    sleeperSeenAt = now;
                    Startle(30);
                    YourBuddyPlugin.Log.LogInfo($"[anomaly] You found {Name} asleep in its capsule - the other one is {dist:0.0}m from you");
                    break;
                case 2:
                    if (sleeper != null && LooksAtSleeper(SleeperSeeDist, SleeperLookAngle)) sleeperSeenAt = now;

                    if (sleeper != null && now - sleeperSeenAt >= SleeperGoneUnseenSeconds)
                    {
                        RemoveSleeper();
                        YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name}'s capsule is empty again");
                    }
                    if (anomalySeen && !sleeperActorFound)
                    {
                        sleeperActorFound = true;
                        YourBuddyPlugin.Log.LogInfo($"[anomaly] You turn to {Name} - it was standing there, facing you");
                    }
                    if (sleeper == null && (sleeperActorFound || now >= anomalyUntil)) EndAnomaly(sleeperActorFound ? "you saw both" : "it stopped waiting");

                    break;
            }
        }

        /// <summary>
        /// The copy gone and the capsule open and dark, as the buddy left it.
        /// </summary>
        private void RemoveSleeper()
        {
            if (sleeper != null) Destroy(sleeper);

            sleeper = null;
            if (sleeperBed is { } bed) BuddyCryoSpawn.OpenEmpty(bed);

            sleeperBed = null;
        }
    }
}

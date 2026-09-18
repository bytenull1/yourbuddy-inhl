using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// What an errand may ask of the buddy. BuddyBehaviour implements it explicitly, so none of it
    /// widens the component's own surface.
    /// </summary>
    internal interface IErrandBody
    {
        Transform Transform { get; }
        BuddyHands Hands { get; }
        /// <summary>
        /// The leg the current Route is walking to, or null.
        /// </summary>
        ErrandLeg? Leg { get; }
        bool OnRoute { get; }

        Vector3 FloorUnderBuddy();
        Vector3 GroundPos(float height);
        bool OnMyVessel(Transform what);
        void FacePoint(Vector3 point);
        /// <summary>
        /// Stands where it is, turning to `point`: the work at the end of a leg.
        /// </summary>
        void StandFacing(Vector3 point);

        bool InReach(ReachTask task);
        string? PlanReach(ReachTask task, out BuddyNodeGraph.NavPath plan);
        bool StepIntoReach(ReachTask task, out Vector3 move, out bool wantMove);
        /// <summary>
        /// Whether some node has a clear walk to a stand point for `task`. Fills its Node and StandPoint.
        /// </summary>
        bool HasReachNode(ReachTask task);
        /// <summary>
        /// Makes `leg` the Route's reason, walking `plan` to it; a null plan is a leg already in reach.
        /// The leg it replaces is not ended: that is how one leg hands over to the next.
        /// </summary>
        void Walk(ErrandLeg leg, BuddyNodeGraph.NavPath? plan);
        /// <summary>
        /// Ends the Route and its leg; the leg's End puts back what it left half done.
        /// </summary>
        void FinishRoute();

        string? BusyForCommand();
        Player? PilotPlayer();
        bool IsAboardPlayerShip();
        int FeltTemperature(Environment env);
    }

    /// <summary>
    /// One kind of fetch-and-carry job the decider weighs: selling, tidying, a snack, play. Keeps when it
    /// is next due, how the last one went, and what it leaves out for a while. docs/behaviour.md §3
    /// </summary>
    internal abstract class Errand(IErrandBody body)
    {
        protected readonly IErrandBody Body = body;
        protected readonly SkipList Skips = new();
        /// <summary>
        /// When the decider next weighs it; below zero until its first round schedules one.
        /// </summary>
        public float DueAt = -1f;
        /// <summary>
        /// How the last one went, or why there was none; for the HUD and buddy_mind.
        /// </summary>
        protected string? Last;

        public abstract bool Enabled { get; }
        /// <summary>
        /// The configured interval, which is also what the decider measures readiness against.
        /// </summary>
        public abstract float Interval { get; }
        /// <summary>
        /// The wait after a failure, and the least a Defer holds it off.
        /// </summary>
        public abstract float RetryDelay { get; }
        /// <summary>
        /// How long a target the buddy could not plan to or use is left out.
        /// </summary>
        protected abstract float SkipSeconds { get; }
        /// <summary>
        /// The same, when a walk into reach gave up.
        /// </summary>
        protected virtual float DeferSkipSeconds => SkipSeconds;
        /// <summary>
        /// The console command, for the "off" line.
        /// </summary>
        protected abstract string Command { get; }
        protected virtual string NextLabel => "in";

        /// <summary>
        /// Picks a target it can reach and sets off. `report` finishes a sentence either way.
        /// </summary>
        public abstract bool TryStart(out string report);

        /// <summary>
        /// How many candidates there are now, and how far the nearest is, flat. The collectors share
        /// static buffers, so exactly one runs per call. docs/behaviour.md §3
        /// </summary>
        public abstract int Count(out float nearest);

        /// <summary>
        /// The target is skipped for a while, and the next try waits at least RetryDelay.
        /// </summary>
        public void Defer(Transform target, float seconds)
        {
            Skips.Skip(target, DeferSkipSeconds);
            DueAt = Mathf.Max(DueAt, Time.time + Mathf.Max(seconds, RetryDelay));
        }

        /// <summary>
        /// A console command: now, whatever the schedule, the setting or an order in force.
        /// </summary>
        public string StartNow(string refused)
        {
            string? busy = Body.BusyForCommand();
            if (busy != null) return busy;

            return TryStart(out string report) ? "Buddy " + report : refused + report;
        }

        /// <summary>
        /// For the HUD and buddy_mind.
        /// </summary>
        public string Describe()
        {
            string last = Last != null ? " (last: " + Last + ")" : "";
            if (!Enabled) return $"off - {Command} still works" + last;

            return (DueAt < 0f ? "not scheduled yet" : $"{NextLabel} {Mathf.Max(0f, DueAt - Time.time):0}s") + last;
        }

        protected Vector3 Here => Body.Transform.position;

        protected float NextInterval() =>
            Interval * Random.Range(1f - Items.SnackIntervalJitter, 1f + Items.SnackIntervalJitter);

        protected bool Failed(string report)
        {
            Last = report;
            return false;
        }

        protected string? TakeBlocker(Grabbable? item) => Items.TakeBlocker(item, Body.Hands.Item);

        /// <summary>
        /// The nearest of `count` points, flat, from the buddy.
        /// </summary>
        protected float Nearest(int count, System.Func<int, Vector3> pointAt)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++) nearest = Mathf.Min(nearest, Items.FlatDistanceSq(pointAt(i), Here));

            return count > 0 ? Mathf.Sqrt(nearest) : 0f;
        }
    }
}

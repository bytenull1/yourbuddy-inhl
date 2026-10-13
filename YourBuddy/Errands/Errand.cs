using NPC.Core;
using NPC.Core.Agents;
using NPC.Core.Navigation;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// What an errand may ask of the buddy. BuddyBehaviour implements it explicitly, so none of it
    /// widens the component's own surface.
    /// </summary>
    internal interface IErrandBody
    {
        /// <summary>
        /// The name, e.g. "Buddy 2". Replies to a command say who answers.
        /// </summary>
        string Name { get; }
        Transform Transform { get; }
        NpcHands Hands { get; }
        /// <summary>
        /// The leg the current Route is walking to, or null.
        /// </summary>
        ErrandLeg? Leg { get; }
        bool OnRoute { get; }

        Vector3 FloorUnderBuddy();
        Vector3 GroundPos(float height);
        bool OnMyVessel(Transform what);
        /// <summary>
        /// Switches on the content of the room `what` lies in, as the game does for the player's own room.
        /// </summary>
        void LoadRoomOf(Transform what);
        void FacePoint(Vector3 point);
        /// <summary>
        /// Stands where it is, turning to `point`, as the work at the end of a leg.
        /// </summary>
        void StandFacing(Vector3 point);

        bool InReach(ReachTask task);
        string? PlanReach(ReachTask task, out NavPath plan);
        /// <summary>
        /// Plans a raw route to a world point, with Outdoor nodes when asked. It may end short, at the
        /// node nearest the point. Null with the plan, else why there is none.
        /// </summary>
        string? PlanRoute(Vector3 target, bool mayGoOutside, out NavPath plan);
        bool StepIntoReach(ReachTask task, out Vector3 move, out bool wantMove);
        /// <summary>
        /// Whether some node has a clear walk to a stand point for `task`. Fills its Node and StandPoint.
        /// </summary>
        bool HasReachNode(ReachTask task);
        /// <summary>
        /// Makes `leg` the Route's reason, walking `plan` to it; a null plan is a leg already in reach.
        /// The leg it replaces is not ended, which is how one leg hands over to the next.
        /// </summary>
        void Walk(ErrandLeg leg, NavPath? plan);
        /// <summary>
        /// Ends the Route and its leg; the leg's End puts back what it left half done.
        /// </summary>
        void FinishRoute();

        /// <summary>
        /// Why a job order cannot start now, or null. Outside counts as busy, since every job is inside.
        /// </summary>
        string? BusyForCommand();
        /// <summary>
        /// The same for the airlock orders (outside, inside, the suit), which also run outside.
        /// </summary>
        string? BusyForAirlockCommand();
        /// <summary>
        /// Outside the airlocks. npc-core:docs/invariants.md#an-airlock-is-crossed-by-its-cycle
        /// </summary>
        bool IsOutside { get; }
        /// <summary>
        /// The speed it walks at now, a worn suit's slowdown included.
        /// </summary>
        float WalkSpeed { get; }
        /// <summary>
        /// Outside with no gravity. It flies, and a leg steers with FlyTo. npc-core:docs/agent.md#8-floating
        /// </summary>
        bool Floating { get; }
        /// <summary>
        /// The agent's flight to a point, straight or along the player's trail. Zero once within `arrival`.
        /// </summary>
        Vector3 FlyTo(Vector3 point, float arrival, out bool wantMove);
        /// <summary>
        /// The buddy went through an airlock door and knows which side it came out on, and the airlock's
        /// gravity out there.
        /// </summary>
        void SetOutside(bool outside, string why, float gravity);
        /// <summary>
        /// Another buddy's leg or hide holds `what`: docs/invariants.md#one-buddy-per-target
        /// </summary>
        bool TakenByAnother(Transform what);
        /// <summary>
        /// Another living, loaded buddy stands where `test` says.
        /// </summary>
        bool AnotherBuddyWhere(System.Func<Vector3, bool> test);
        bool IsAboardPlayerShip();
        int FeltTemperature(Environment env);
        /// <summary>
        /// The air it breathes where it stands, null in space.
        /// </summary>
        Environment? Air { get; }
        /// <summary>
        /// Where an item at `pos` belongs (the room or station interior under it), or null. Anything
        /// it takes off goes there, as UnequipSuit does for the player's suit. docs/eva.md
        /// </summary>
        Transform? ItemParentAt(Vector3 pos);
    }

    /// <summary>
    /// One kind of fetch-and-carry job the decider weighs (selling, tidying, a snack, play). Keeps when it
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
        internal float DeferredUntil { get; private set; }
        /// <summary>
        /// How the last one went, or why there was none; for the HUD and buddy_dev mind.
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
        /// Once due, why nothing started. Shown instead of a timer stuck at 0 s. Null to show the timer.
        /// </summary>
        protected virtual string? WhyIdle => null;

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
        protected void Defer(Transform target, float seconds)
        {
            Skips.Skip(target, DeferSkipSeconds);
            DeferredUntil = Mathf.Max(DeferredUntil, Time.time + Mathf.Max(seconds, RetryDelay));
            DueAt = Mathf.Max(DueAt, DeferredUntil);
        }

        /// <summary>
        /// A console command. Runs now, whatever the schedule, the setting or an order in force.
        /// </summary>
        public string StartNow(string refused)
        {
            string? busy = Body.BusyForCommand();
            if (busy != null) return busy;

            // An order is not held back by what failed or was skipped earlier.
            Skips.Ignore = true;
            try
            {
                return TryStart(out string report) ? Body.Name + " " + report : refused + report;
            }
            finally
            {
                Skips.Ignore = false;
            }
        }

        /// <summary>
        /// For the HUD and buddy_dev mind.
        /// </summary>
        public virtual string Describe()
        {
            string last = Last != null ? " (last: " + Last + ")" : "";
            if (!Enabled) return $"off - {Command} still works" + last;

            if (DueAt >= 0f && DueAt <= Time.time && WhyIdle is { } idle) return "due - " + idle + last;

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

        protected string? TakeBlocker(Grabbable? item) =>
            Items.TakeBlocker(item, Body.Hands.Item) ??
            (item != null && Body.TakenByAnother(item.transform) ? "another buddy is after it" : null);

        protected string? NotInHands(Grabbable item) => Body.Hands.Item != item ? "it is no longer in my hands" : null;

        /// <summary>
        /// How <see cref="SetOff"/> went.
        /// </summary>
        protected enum SetOffResult { InReach, Walking, NoPlan, NoPlansLeft }

        /// <summary>
        /// Sets off for `task`, at once when it is in reach, else along a plan while fewer than `maxPlans` were
        /// tried. A target it cannot plan to is left out for SkipSeconds, and `failure` says why.
        /// </summary>
        protected SetOffResult SetOff<T>(T task, System.Action<T, NavPath?> begin, ref int plans, int maxPlans, ref string? failure)
            where T : ErrandLeg
        {
            if (Body.InReach(task))
            {
                task.Node = task.StandPoint = Here;
                begin(task, null);
                return SetOffResult.InReach;
            }
            if (plans++ >= maxPlans) return SetOffResult.NoPlansLeft;

            failure = Body.PlanReach(task, out NavPath plan);
            if (failure != null)
            {
                Skips.Skip(task.Own, SkipSeconds);
                Trace($"not {task.Name} at {task.TargetPoint:0.0}: {failure} - skipping it for {SkipSeconds:0}s");
                return SetOffResult.NoPlan;
            }
            begin(task, plan);
            return SetOffResult.Walking;
        }

        /// <summary>
        /// "right here", or how far off `task` is, for the report of a <see cref="SetOff"/>.
        /// </summary>
        protected string HowFar(SetOffResult set, ReachTask task) =>
            set == SetOffResult.InReach ? "right here" : $"{Vector3.Distance(Here, task.TargetPoint):0.0}m away";

        /// <summary>
        /// Walks `leg` (a null plan means already in reach). Until the job is done, a failed walk tries again after `retry`.
        /// </summary>
        protected void Begin(ErrandLeg leg, NavPath? plan, float retry, string last)
        {
            Body.Walk(leg, plan);
            DueAt = Time.time + retry;
            Last = last;
        }

        /// <summary>
        /// Gives up the leg underway; `what` ("'Can'", "the fridge alone") and `why` go to the HUD and the log.
        /// </summary>
        protected Vector3 Leave(string what, string why)
        {
            Last = $"left {what} - {why}";
            YourBuddyPlugin.Log.LogInfo($"[ai] Leaving {what} - {why}");
            Body.FinishRoute();
            return Vector3.zero;
        }

        /// <summary>
        /// Names this errand's trace lines, e.g. "Snack" or "Tidy".
        /// </summary>
        protected abstract string Topic { get; }

        protected void Trace(string line)
        {
            if (NpcLog.Level >= 2) YourBuddyPlugin.Log.LogInfo($"[mind] {Topic}: {line}");
        }

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

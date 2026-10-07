using System.Collections.Generic;
using NPC.Core.Navigation;
using NPC.Core.World;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// One trip through an airlock, out or back in. Put a spare suit on first when going out
    /// (unless already suited), walk into the chamber from this side, wait there for the player
    /// to cycle it, and walk out through the door the cycle opened. The airlock itself is never
    /// driven. docs/eva.md, npc-core:docs/invariants.md#an-airlock-is-crossed-by-its-cycle
    /// </summary>
    internal sealed class EvaRun(BuddySuit suit, IErrandBody body)
    {
        private enum Phase { Suit, ToChamber, Crossing }

        private Phase phase;
        private Airlock airlock = null!; // set by Begin and BeginInside
        /// <summary>
        /// Out onto the surface, or back inside.
        /// </summary>
        private bool outward;
        private bool stopAfterSuit;
        private BuddySuit.SuitReason suitReason;

        internal bool Active { get; private set; }

        // Starting

        /// <summary>
        /// The way out. Returns the reply line; a refusal leaves no run behind.
        /// </summary>
        internal string Begin(Airlock exitAirlock)
        {
            airlock = exitAirlock;
            outward = true;
            stopAfterSuit = false;
            suitReason = BuddySuit.SuitReason.Ordered;
            Active = true;

            if (suit.Suited) return StartChamberLeg() ?? body.Name + " is heading outside - it will wait for you in the airlock";

            Suit? spare = suit.TakeableSuit(out string? why, playerOrdered: true);
            if (spare == null)
            {
                Abort("no spare suit: " + why);
                return body.Name + " cannot go outside: no spare suit to take - " + why;
            }

            string? refusal = WalkToSuit(spare);
            if (refusal != null) return refusal;

            return suit.Suited
                ? StartChamberLeg() ?? body.Name + " is heading outside - it will wait for you in the airlock"
                : body.Name + " is putting on a suit, then heading outside - it will wait for you in the airlock";
        }

        /// <summary>
        /// The way back in, from outside. Returns the reply line; a refusal leaves no run behind.
        /// </summary>
        internal string BeginInside(Airlock chamberAirlock)
        {
            airlock = chamberAirlock;
            outward = false;
            stopAfterSuit = false;
            Active = true;
            return StartChamberLeg() ?? body.Name + " is heading for the airlock - it will wait in there for you to cycle it";
        }

        /// <summary>
        /// The survival suit-up. A suit on, then normal life aboard until the air is safe again.
        /// `ordered` marks an explicit player command (`buddy_order suit on`), which yields the
        /// only-suit count rule and keeps the suit on afterwards. False with a throttled trace
        /// when no spare is takeable.
        /// </summary>
        internal bool BeginSurvival(bool playerOrdered)
        {
            Suit? spare = suit.TakeableSuit(out string? why, playerOrdered);
            if (spare == null)
            {
                TraceThrottled("not suiting up: " + why);
                return false;
            }

            airlock = null!;
            stopAfterSuit = true;
            suitReason = playerOrdered ? BuddySuit.SuitReason.Ordered : BuddySuit.SuitReason.Survival;
            Active = true;
            return WalkToSuit(spare) == null;
        }

        /// <summary>
        /// Always walks to the suit, so putting it on is something you see. Null when the leg is
        /// on, else the refusal line.
        /// </summary>
        private string? WalkToSuit(Suit spare)
        {
            SuitLeg leg = new(this, body, suit, spare);
            string? failure = body.PlanReach(leg, out NavPath plan);
            if (failure != null)
            {
                suit.DeferUnreachableSuit(spare);
                Abort("cannot reach the suit - " + failure);
                return body.Name + " cannot reach the suit: " + failure;
            }

            body.Walk(leg, plan);
            phase = Phase.Suit;
            return null;
        }

        /// <summary>
        /// The walk into the chamber from this side, along the graph's own route toward the
        /// chamber's centre (the plan may end short of it, at the node nearest it), replanned from
        /// each arrival, the last metres straight in on sight. Null when the leg is on, else the
        /// refusal line.
        /// </summary>
        private string? StartChamberLeg()
        {
            phase = Phase.ToChamber;

            ChamberLeg leg = new(this, body, airlock, outward, StandPoint(airlock));
            // Floating, there is no graph, so the leg flies. docs/eva.md#7-floating
            if (body.Floating)
            {
                body.Walk(leg, null);
                return null;
            }
            if (body.PlanRoute(leg.TargetPoint, mayGoOutside: true, out NavPath plan) == null)
            {
                body.Walk(leg, plan);
                return null;
            }
            // Already beside it, where no node gets it nearer. The leg walks in, or waits at a shut door.
            if (leg.Beside())
            {
                body.Walk(leg, null);
                return null;
            }

            Abort("no way into the airlock - the graph does not reach it");
            return body.Name + " cannot reach the airlock: the graph does not reach it";
        }

        private static readonly List<Vector3> NodeBuffer = [];
        /// <summary>
        /// Without a node inside the chamber volume, one this near its centre stands for it.
        /// </summary>
        private const float StandNodeFallbackDist = 1.5f;

        /// <summary>
        /// Where the buddy waits. The graph node placed inside the chamber (the one inside its
        /// volume nearest the centre), else the volume's centre. Standing on the node you placed
        /// puts it where you expect it, clear of both doors.
        /// </summary>
        internal static Vector3 StandPoint(Airlock airlock)
        {
            Vector3 center = NpcDoors.ChamberCenter(airlock);
            NavGraph.CollectActiveNodes(NodeBuffer);
            Vector3 best = center;
            float bestSqr = float.MaxValue;
            bool bestInside = false;
            foreach (Vector3 node in NodeBuffer)
            {
                bool inside = NpcDoors.ChamberAt(node, withShip: true) == airlock;
                float sqr = Items.FlatDistanceSq(node, center);
                if (!inside && (sqr > StandNodeFallbackDist * StandNodeFallbackDist || Mathf.Abs(node.y - center.y) > 2f)) continue;
                if (bestInside && !inside) continue;
                if (inside == bestInside && sqr >= bestSqr) continue;

                best = node;
                bestSqr = sqr;
                bestInside = inside;
            }
            return best;
        }

        // Called by the legs

        private void WearNow(Suit spare)
        {
            string? failure = suit.Wear(spare, suitReason);
            if (failure != null)
            {
                Abort(failure);
                return;
            }
            SuitWorn();
        }

        /// <summary>
        /// The suit is on. A survival run ends here; an outside run walks on to the airlock.
        /// </summary>
        internal void SuitWorn()
        {
            if (stopAfterSuit)
            {
                YourBuddyPlugin.Log.LogInfo("[suit] " + body.Name + " is suited up and stays aboard");
                body.FinishRoute();
                Active = false;
                return;
            }
            StartChamberLeg();
        }

        /// <summary>
        /// From the chamber leg. The far door opened and the player's cycle is done, so walk through it.
        /// </summary>
        internal void Cross()
        {
            phase = Phase.Crossing;
            YourBuddyPlugin.Log.LogInfo("[suit] " + body.Name + "'s airlock cycled - " +
                                        (outward ? "stepping outside" : "stepping back inside"));
            body.Walk(new CrossLeg(this, body, airlock, outward), null);
        }

        /// <summary>
        /// From the crossing leg, once clear of the door on the far side. The run hands back to the
        /// mode in force, which follows the player on this side.
        /// </summary>
        internal void Crossed()
        {
            Active = false;
            body.SetOutside(outward, outward ? "went out through the airlock's outer door"
                                             : "walked in through the airlock's inner door", airlock.ExitGravity);
            body.FinishRoute();
            YourBuddyPlugin.Log.LogInfo("[suit] " + body.Name + (outward ? " is outside" : " is back inside"));
        }

        /// <summary>
        /// From the crossing leg, when the far door shut again before the buddy was through. The run
        /// goes on, back to the chamber's stand point to wait for the next cycle.
        /// </summary>
        internal void CrossingBlocked()
        {
            YourBuddyPlugin.Log.LogInfo("[suit] " + body.Name + " stays in the airlock - the door shut before it was through");
            StartChamberLeg();
        }

        /// <summary>
        /// A leg gave up or the suit was lost, so the run ends where it stands. Whatever was worn
        /// stays worn; taking it off is the watcher's or the player's call.
        /// </summary>
        internal void Abort(string why)
        {
            if (!Active) return;

            Active = false;
            body.FinishRoute();
            YourBuddyPlugin.Log.LogInfo("[suit] " + body.Name + " gave up on the run - " + why);
        }

        /// <summary>
        /// Cancelled from outside the run (a new command, death, despawn). No ceremony; the leg
        /// in flight is dropped by whoever ends the route.
        /// </summary>
        internal void End()
        {
            if (!Active) return;

            Active = false;
            YourBuddyPlugin.Log.LogInfo("[suit] " + body.Name + "'s run is over");
        }

        // Per frame, from BuddySuit.Update

        public void Update()
        {
            // Unsuited on the way out (the suit is gone), so nothing is left to walk out for.
            if (Active && outward && !suit.Suited && phase != Phase.Suit) Abort("no longer wearing a suit");
        }

        private float traceAt;

        private void TraceThrottled(string line)
        {
            if (Time.time < traceAt) return;

            traceAt = Time.time + 30f;
            YourBuddyPlugin.Log.LogInfo("[suit] " + body.Name + " " + line);
        }

        // The legs

        /// <summary>
        /// Walks to the spare suit and puts it on. The rule is checked again at the suit.
        /// docs/invariants.md#the-buddy-never-takes-your-last-suit
        /// </summary>
        private sealed class SuitLeg(EvaRun run, IErrandBody body, BuddySuit suit, Suit target)
            : ErrandLeg(target.transform.position, target.transform)
        {
            public override string Name => "the spare suit";
            public override string Describe() => "putting on a suit";
            public override void Defer(float seconds) => run.Abort("the suit could not be reached");
            public override bool Holds(Transform t) => t == target.transform;

            public override Vector3 Approach(out bool wantMove)
            {
                wantMove = false;
                if (suit.Suited)
                {
                    run.SuitWorn();
                    return Vector3.zero;
                }

                string? blocker = suit.TakeBlockerFor(target);
                if (blocker != null)
                {
                    run.Abort("the suit is no longer free - " + blocker);
                    return Vector3.zero;
                }

                if (!body.StepIntoReach(this, out Vector3 move, out wantMove)) return move;

                run.WearNow(target);
                return Vector3.zero;
            }
        }

        /// <summary>
        /// Gets into the chamber through this side's door and stands on its stand point (the node
        /// placed inside it) until the far door opens. It walks the graph's route toward it,
        /// replanning from each arrival, and the last metres straight in. Beside the airlock it
        /// never gives up. A shut door on this side is waited out, since only the player's cycle
        /// opens it, and anything else is walked straight at.
        /// </summary>
        private sealed class ChamberLeg(EvaRun run, IErrandBody body, Airlock airlock, bool outward, Vector3 stand)
            : ErrandLeg(stand, airlock.transform)
        {
            private const float WaitTraceSeconds = 15f;
            /// <summary>
            /// Arrived within this of the stand point, flat; once arrived, still there until
            /// InChamberLeave, since a nudge is not leaving.
            /// </summary>
            private const float InChamberArrival = 0.3f;
            private const float InChamberLeave = 0.8f;
            /// <summary>
            /// How close the straight walk in may start (and a shut door is waited beside), and
            /// how much closer each replanned route must end before the leg stops replanning.
            /// </summary>
            private const float StraightEnterMaxDist = 6f;
            private const float ChainProgressDist = 0.5f;

            private bool arrived;
            private bool straightStarted;
            private float lastEndDist = float.MaxValue;
            private float traceAt;

            // The run walks this only while suited, so markers of the Outdoor type are usable too.
            public override bool MayGoOutside => true;

            public override string Name => "the airlock";
            public override string Describe() => "waiting in the airlock";
            // Standing in the chamber (or by its shut door) is the job, not a stall.
            public override bool Waits => true;
            public override void Defer(float seconds) => run.Abort("no way into the airlock chamber");

            /// <summary>
            /// The door into the chamber from this side, and the one the cycle opens.
            /// </summary>
            private Gate? NearDoor => outward ? GameInternals.AirlockAccess.GetInnerDoor(airlock) : GameInternals.AirlockAccess.GetOuterDoor(airlock);
            private Gate? FarDoor => outward ? GameInternals.AirlockAccess.GetOuterDoor(airlock) : GameInternals.AirlockAccess.GetInnerDoor(airlock);
            private string NearName => outward ? "inner" : "outer";

            public override Vector3 Approach(out bool wantMove)
            {
                wantMove = false;
                if (body.Floating) return Float(out wantMove);

                float dist = FlatDistance();
                arrived = dist < (arrived ? InChamberLeave : InChamberArrival);
                if (arrived) return Arrived();

                bool beside = dist <= StraightEnterMaxDist;
                // The way in is shut, and only the player's cycle opens it.
                if (beside && NearDoor is not { Opened: true } && !InsideChamber())
                {
                    Trace("waits by the airlock for its " + NearName + " door to open");
                    return Vector3.zero;
                }

                if (!beside)
                {
                    Replan();
                    return Vector3.zero;
                }

                if (!straightStarted)
                {
                    straightStarted = true;
                    body.StandFacing(TargetPoint);
                }
                wantMove = true;
                Vector3 to = TargetPoint - body.Transform.position;
                to.y = 0f;
                return to.sqrMagnitude < 0.0001f ? Vector3.zero : to.normalized * body.WalkSpeed;
            }

            /// <summary>
            /// On the stand point. Through once the far door is open, else waiting for your cycle.
            /// </summary>
            private Vector3 Arrived()
            {
                if (FarDoor is { Opened: true })
                {
                    run.Cross();
                    return Vector3.zero;
                }

                // Standing still is the job; say so now and then. docs/logging.md §4
                Trace("waits in the airlock for you to cycle it (" + NearName + " door " +
                      (NearDoor is { Opened: true } ? "open" : "shut") + ")");
                return Vector3.zero;
            }

            /// <summary>
            /// Floating in from outside. Flown to the stand point, straight or along your trail, and held
            /// there; a shut door into the chamber is waited out beside it. docs/eva.md#7-floating
            /// </summary>
            private Vector3 Float(out bool wantMove)
            {
                wantMove = false;
                float dist = Vector3.Distance(body.Transform.position, TargetPoint);
                arrived = dist < (arrived ? InChamberLeave : InChamberArrival);
                if (arrived) return Arrived();

                if (dist <= StraightEnterMaxDist && NearDoor is not { Opened: true } && !InsideChamber())
                {
                    Trace("waits by the airlock for its " + NearName + " door to open");
                    return Vector3.zero;
                }
                return body.FlyTo(TargetPoint, InChamberArrival * 0.5f, out wantMove);
            }

            private void Trace(string line)
            {
                if (Time.time < traceAt) return;

                traceAt = Time.time + WaitTraceSeconds;
                YourBuddyPlugin.Log.LogInfo("[suit] " + body.Name + " " + line);
            }

            private float FlatDistance()
            {
                Vector3 to = TargetPoint - body.Transform.position;
                to.y = 0f;
                return to.magnitude;
            }

            /// <summary>
            /// Past the near door already, in the chamber's own volume.
            /// </summary>
            private bool InsideChamber() => NpcDoors.ChamberAt(body.Transform.position, withShip: true) == airlock;

            /// <summary>
            /// Near enough to walk straight in, or to wait at the door into the chamber.
            /// </summary>
            internal bool Beside() => FlatDistance() <= StraightEnterMaxDist;

            /// <summary>
            /// Still far off after the route ran out. Route again from here, and give up only when
            /// a replan cannot end meaningfully closer.
            /// </summary>
            private void Replan()
            {
                if (body.PlanRoute(TargetPoint, mayGoOutside: true, out NavPath plan) != null)
                {
                    run.Abort("the graph does not reach the airlock - place a node near it");
                    return;
                }

                float endDist = Vector3.Distance(plan[plan.Count - 1], TargetPoint);
                if (endDist > lastEndDist - ChainProgressDist)
                {
                    run.Abort("the graph does not reach the airlock - place a node near it");
                    return;
                }
                lastEndDist = endDist;
                body.Walk(this, plan);
            }
        }

        /// <summary>
        /// The straight steps through the door the cycle opened, ending a couple of metres clear.
        /// Never planned on the graph, since the door is a few metres and the far side may have no node.
        /// </summary>
        private sealed class CrossLeg(EvaRun run, IErrandBody body, Airlock airlock, bool outward)
            : ErrandLeg(ClearPoint(airlock, outward), airlock.transform)
        {
            private const float ClearArrival = 1.4f;
            private const float ClearDistance = 2.2f;

            public override string Name => outward ? "the airlock's outer door" : "the airlock's inner door";
            public override string Describe() => outward ? "stepping outside" : "stepping back inside";
            public override void Defer(float seconds)
            {
                // Not a planned leg; the reach machinery never defers it.
            }

            public override Vector3 Approach(out bool wantMove)
            {
                wantMove = false;

                if (FarDoor(airlock, outward) is not { Opened: true })
                {
                    run.CrossingBlocked();
                    return Vector3.zero;
                }

                if (body.Floating)
                {
                    // Out through the door at its own height, since there is nothing to stand on out there.
                    Vector3 clear = TargetPoint;
                    clear.y = body.Transform.position.y;
                    if ((clear - body.Transform.position).sqrMagnitude < ClearArrival * ClearArrival)
                    {
                        run.Crossed();
                        return Vector3.zero;
                    }
                    return body.FlyTo(clear, 0.1f, out wantMove);
                }

                Vector3 to = TargetPoint - body.Transform.position;
                to.y = 0f;
                if (to.sqrMagnitude < ClearArrival * ClearArrival)
                {
                    run.Crossed();
                    return Vector3.zero;
                }

                wantMove = true;
                return to.normalized * body.WalkSpeed;
            }

            private static Gate? FarDoor(Airlock airlock, bool outward) =>
                outward ? GameInternals.AirlockAccess.GetOuterDoor(airlock) : GameInternals.AirlockAccess.GetInnerDoor(airlock);

            /// <summary>
            /// ClearDistance beyond the far door, along the line from the near door through it.
            /// </summary>
            private static Vector3 ClearPoint(Airlock airlock, bool outward)
            {
                Gate? far = FarDoor(airlock, outward);
                Gate? near = FarDoor(airlock, !outward);
                if (far == null) return airlock.transform.position;

                Vector3 through = near != null ? far.transform.position - near.transform.position : Vector3.zero;
                through.y = 0f;
                through = through.sqrMagnitude > 0.0001f ? through.normalized : Vector3.forward;
                return far.transform.position + through * ClearDistance;
            }
        }
    }
}

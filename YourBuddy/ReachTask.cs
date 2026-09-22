using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Something the buddy walks up to and uses: route to the nearest node with a probed straight walk to
    /// a stand point, then into reach. BuddyBehaviour.Reach.cs does the walking. docs/terminals.md §2
    /// </summary>
    internal abstract class ReachTask(Vector3 targetPoint, Transform own)
    {
        protected const float ReachDist = 1.6f;
        /// <summary>
        /// How far above the buddy's floor a target may be and still be reached.
        /// </summary>
        public const float ReachHeight = 2f;
        /// <summary>
        /// Stand points tried in front of the target, nearest first, all within ReachDist.
        /// </summary>
        protected static readonly float[] ReachStandOffs = [0.8f, 1.1f, 1.4f];
        /// <summary>
        /// After a plan that failed: often the buddy stood somewhere transient, like on furniture.
        /// </summary>
        public const float ReachReplanDelay = 5f;

        /// <summary>
        /// The point that must be in reach and in sight; `Own` is the object whose colliders sight ignores.
        /// </summary>
        public readonly Vector3 TargetPoint = targetPoint;
        public readonly Transform Own = own;
        public float ApproachSince = -1f;
        /// <summary>
        /// The node the route ends at, and the floor point beyond it the object is used from.
        /// </summary>
        public Vector3 Node;
        public Vector3 StandPoint;
        public int Replans;

        /// <summary>
        /// Flat distance the target is used from, and the stand points tried, nearest first, within it.
        /// </summary>
        public virtual float Reach => ReachDist;
        public virtual float[] StandOffs => ReachStandOffs;
        /// <summary>
        /// How far below the buddy's floor the target point may sit: an item on the floor is barely above it.
        /// </summary>
        public virtual float ReachBelow => 0f;

        /// <summary>
        /// Whether the buddy may stand at `point` to use the target: a sell station is not used from
        /// under its gate, and nothing is done from inside one's fences.
        /// docs/invariants.md#a-fenced-sell-station-is-not-somewhere-to-stand
        /// </summary>
        public virtual bool StandAllowed(Vector3 point) => !SellPens.InAFencedPen(point, null, SellPens.SellStationClearance);

        /// <summary>
        /// "the oxygen generator", "the fridge": for log lines.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Holds this kind of task off for `seconds` after a failure.
        /// </summary>
        public abstract void Defer(float seconds);

        /// <summary>
        /// The walk into reach gave up. True when the task took the failure over itself - a selling
        /// run goes on to the next box, or sells what is already loaded - and the caller must then
        /// leave the route alone. docs/items.md §4
        /// </summary>
        public virtual bool Recover(string why) => false;
    }

    /// <summary>
    /// A reach task that is the current Route's reason: UpdateRoute hands over to it once the plan is walked.
    /// </summary>
    internal abstract class ErrandLeg(Vector3 targetPoint, Transform own) : ReachTask(targetPoint, own)
    {
        /// <summary>
        /// The last steps into reach and the work there. Returns the step to take, if any.
        /// </summary>
        public abstract Vector3 Approach(out bool wantMove);

        /// <summary>
        /// However the leg ended, when no next leg took over: put back what it left half done.
        /// </summary>
        public virtual void End()
        {
        }

        /// <summary>
        /// For the HUD's Mode line and the decider's stand-down reason.
        /// </summary>
        public abstract string Describe();

        /// <summary>
        /// A flee drops this leg instead of resuming it once calm. docs/snacks.md §2
        /// </summary>
        public virtual bool EndsOnFlee => true;
    }
}

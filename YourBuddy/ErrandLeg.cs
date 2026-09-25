using NPC.Core.Agents;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// A reach task that is the current Route's reason: UpdateRoute hands over to it once the plan is walked.
    /// The walk into reach is NPC.Core's (ReachTask, NpcAgent.Reach.cs). docs/terminals.md §2
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

        /// <summary>
        /// Whether this leg is about `t`, so no other buddy takes it: docs/invariants.md#one-buddy-per-target
        /// </summary>
        public virtual bool Holds(Transform t) => t == Own;
    }
}

using NPC.Core.Agents;
using NPC.Core.Navigation;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The errands the buddy runs. They reach it only through IErrandBody, implemented explicitly so
    /// it adds nothing to the component's own surface. docs/behaviour.md
    /// </summary>
    public sealed partial class BuddyBehaviour : IErrandBody
    {
        // Created in Awake, before the first frame.
        private ResourceErrand resources = null!; // Initialized in Awake before the decider runs.
        private LifeSupport lifeSupport = null!;
        private BuddySuit suit = null!; // Awake; the agent's settings read it only from the slow phases on
        private SnackErrand snacks = null!;
        private StoreErrand storing = null!; // Initialized in Awake before the decider runs.
        private TidyErrand tidying = null!;
        private SellErrand selling = null!;
        private PlayErrand play = null!;
        private SuitFetchErrand suitFetch = null!;

        private void Awake()
        {
            cc = GetComponent<CharacterController>();
            nearMonster = node => (node - lastMonsterPos).sqrMagnitude < FearRestraintDist * FearRestraintDist;
            inDockCorridor = InDockCorridor;
            nearMonsterOrCorridor = node => nearMonster(node) || InDockCorridor(node);
            resources = new ResourceErrand(this);
            lifeSupport = new LifeSupport(this);
            suit = new BuddySuit(this, lifeSupport);
            snacks = new SnackErrand(this);
            tidying = new TidyErrand(this);
            storing = new StoreErrand(this);
            selling = new SellErrand(this);
            play = new PlayErrand(this);
            suitFetch = new SuitFetchErrand(this, suit);
        }

        // Console and dialog commands. docs/behaviour.md
        internal string StartSnackNow() => snacks.StartNow("No snack: ");
        internal string StartTidyNow() => StartChoreOrder(tidying, "tidying");
        internal string StartSellNow() => StartChoreOrder(selling, "selling");
        internal string StartPlayNow() => StartChoreOrder(play, "playing");
        internal string StartSuitFetchNow() => suitFetch.StartNow("No suit fetch: ");
        internal string StartTerminalNow(string which) => lifeSupport.StartNow(which);
        internal string StartOutsideNow() => suit.StartOutsideNow();
        internal string StartInsideNow() => suit.StartInsideNow();

        /// <summary>
        /// Outside the airlocks. npc-core:docs/invariants.md#an-airlock-is-crossed-by-its-cycle
        /// </summary>
        internal bool IsOutside => agent.IsOutside;

        /// <summary>
        /// Outside with no gravity. npc-core:docs/agent.md#8-floating
        /// </summary>
        internal bool Floating => agent.Floating;
        internal string SuitNow(string onOff) => suit.SuitNow(onOff);
        internal string UnsuitNow() => suit.UnsuitNow();

        /// <summary>
        /// Whether the buddy wears a suit. The agent's settings pass it to NPC.Core's atmosphere and
        /// space rules. docs/eva.md
        /// </summary>
        internal bool SuitSuited => suit.Suited;

        /// <summary>
        /// The worn suit's MovementSpeedModifier, 1 without one. docs/eva.md
        /// </summary>
        internal float SuitSpeedFactor => suit.SpeedFactor;

        /// <summary>
        /// Item id of the worn suit, for the sidecar.
        /// </summary>
        internal uint WornSuitId => suit.WornSuitId;

        /// <summary>
        /// The suit item it wears, or null.
        /// </summary>
        internal Suit? WornSuit => suit.WornSuit;

        /// <summary>
        /// After a load, wears the suit the sidecar named. docs/eva.md
        /// </summary>
        internal void RestoreWornSuit(uint suitId, string? wornFor) => suit.RestoreWorn(suitId, wornFor);

        /// <summary>
        /// Why the worn suit is on (BuddySuit.SuitReason), for the sidecar. Null without one.
        /// </summary>
        internal string? WornSuitReason => suit.WornReason;

        /// <summary>
        /// On despawn the worn suit goes back into the world, or it would vanish with the buddy.
        /// </summary>
        internal void ReleaseWornSuit() => suit.OnDespawned();

        string IErrandBody.Name => Name;
        Transform IErrandBody.Transform => transform;
        NpcHands IErrandBody.Hands => agent.Hands;
        ErrandLeg? IErrandBody.Leg => reachTask;
        bool IErrandBody.OnRoute => mode == BuddyMode.Route;

        Vector3 IErrandBody.FloorUnderBuddy() => agent.FloorUnderNpc();
        Vector3 IErrandBody.GroundPos(float height) => agent.GroundPos(height);
        bool IErrandBody.OnMyVessel(Transform what) => agent.OnMyVessel(what);

        void IErrandBody.LoadRoomOf(Transform what)
        {
            Room? room = what.GetComponentInParent<Room>(true);
            if (room != null && what.IsChildOf(room.ContentParent)) agent.LoadRoom(room, "an errand needs it");
        }

        void IErrandBody.FacePoint(Vector3 point) => agent.FacePoint(point);

        void IErrandBody.StandFacing(Vector3 point)
        {
            agent.ClearMoveTarget();
            agent.FacePoint(point);
        }

        bool IErrandBody.InReach(ReachTask task) => agent.InReach(task);
        string? IErrandBody.PlanReach(ReachTask task, out NavPath plan) => agent.PlanReach(task, out plan);

        string? IErrandBody.PlanRoute(Vector3 target, bool mayGoOutside, out NavPath plan)
        {
            NavPath? found = NavGraph.FindPath(agent.FloorUnderNpc(), target, mayGoOutside: mayGoOutside);
            if (found is not { Count: > 0 })
            {
                plan = default;
                return NavGraph.LastPathBlockedByDoor ? "a door I cannot open is in the way" : "there is no path to it";
            }
            plan = found.Value;
            return null;
        }

        bool IErrandBody.StepIntoReach(ReachTask task, out Vector3 move, out bool wantMove) =>
            agent.StepIntoReach(task, out move, out wantMove);
        bool IErrandBody.HasReachNode(ReachTask task) => NpcAgent.FindReachNode(task);

        void IErrandBody.Walk(ErrandLeg leg, NavPath? plan)
        {
            if (plan.HasValue)
            {
                StartRoute(plan.Value, leg.Node);
            }
            else
            {
                agent.DropPlan();
                routeGoal = leg.Node;
                mode = BuddyMode.Route;
            }
            reachTask = leg;
        }

        void IErrandBody.FinishRoute() => FinishRoute();
        string? IErrandBody.BusyForCommand() => BusyForCommand();
        string? IErrandBody.BusyForAirlockCommand() => BusyForCommand(outsideOk: true);
        bool IErrandBody.IsOutside => agent.IsOutside;
        float IErrandBody.WalkSpeed => agent.WalkSpeed;
        bool IErrandBody.Floating => agent.Floating;
        Vector3 IErrandBody.FlyTo(Vector3 point, float arrival, out bool wantMove) => agent.FlyTo(point, arrival, out wantMove);
        void IErrandBody.SetOutside(bool outside, string why, float gravity) => agent.SetOutside(outside, why, gravity);
        bool IErrandBody.TakenByAnother(Transform what) => BuddyManager.TakenByAnother(what, this);
        bool IErrandBody.AnotherBuddyWhere(System.Func<Vector3, bool> test) => BuddyManager.AnotherBuddyWhere(test, this);
        bool IErrandBody.IsAboardPlayerShip() => agent.IsAboardPlayerShip();
        int IErrandBody.FeltTemperature(Environment env) => agent.FeltTemperature(env);
        Environment? IErrandBody.Air => agent.Air;
        Transform? IErrandBody.ItemParentAt(Vector3 pos) => agent.ItemParentAt(pos);
    }
}

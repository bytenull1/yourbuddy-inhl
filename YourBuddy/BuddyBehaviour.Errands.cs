using NPC.Core.Agents;
using NPC.Core.Navigation;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The errands the buddy runs, and the one door they have into it: IErrandBody, implemented
    /// explicitly so it adds nothing to the component's own surface. docs/behaviour.md
    /// </summary>
    public sealed partial class BuddyBehaviour : IErrandBody
    {
        // Created in Awake, before the first frame.
        private LifeSupport lifeSupport = null!;
        private SnackErrand snacks = null!;
        private TidyErrand tidying = null!;
        private SellErrand selling = null!;
        private PlayErrand play = null!;

        private void Awake()
        {
            cc = GetComponent<CharacterController>();
            nearMonster = node => (node - lastMonsterPos).sqrMagnitude < FearRestraintDist * FearRestraintDist;
            lifeSupport = new LifeSupport(this);
            snacks = new SnackErrand(this);
            tidying = new TidyErrand(this);
            selling = new SellErrand(this);
            play = new PlayErrand(this);
        }

        // Console and dialog commands. docs/behaviour.md
        internal string StartSnackNow() => snacks.StartNow("No snack: ");
        internal string StartTidyNow() => tidying.StartNow("No tidying: ");
        internal string StartSellNow() => selling.StartNow("No selling: ");
        internal string StartPlayNow() => play.StartNow("No play: ");
        internal string StartTerminalNow(string which) => lifeSupport.StartNow(which);

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
        Player? IErrandBody.PilotPlayer() => PilotPlayer();
        bool IErrandBody.TakenByAnother(Transform what) => BuddyManager.TakenByAnother(what, this);
        bool IErrandBody.AnotherBuddyWhere(System.Func<Vector3, bool> test) => BuddyManager.AnotherBuddyWhere(test, this);
        bool IErrandBody.IsAboardPlayerShip() => agent.IsAboardPlayerShip();
        int IErrandBody.FeltTemperature(Environment env) => agent.FeltTemperature(env);
    }
}

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

        Transform IErrandBody.Transform => transform;
        BuddyHands IErrandBody.Hands => hands;
        ErrandLeg? IErrandBody.Leg => reachTask;
        bool IErrandBody.OnRoute => mode == BuddyMode.Route;

        Vector3 IErrandBody.FloorUnderBuddy() => FloorUnderBuddy();
        Vector3 IErrandBody.GroundPos(float height) => GroundPos(height);
        bool IErrandBody.OnMyVessel(Transform what) => OnMyVessel(what);

        void IErrandBody.LoadRoomOf(Transform what)
        {
            Room? room = what.GetComponentInParent<Room>(true);
            if (room != null && what.IsChildOf(room.ContentParent)) EnableRoom(room);
        }

        void IErrandBody.FacePoint(Vector3 point) => FacePoint(point);

        void IErrandBody.StandFacing(Vector3 point)
        {
            hasMoveTarget = false;
            FacePoint(point);
        }

        bool IErrandBody.InReach(ReachTask task) => InReach(task);
        string? IErrandBody.PlanReach(ReachTask task, out BuddyNodeGraph.NavPath plan) => PlanReach(task, out plan);
        bool IErrandBody.StepIntoReach(ReachTask task, out Vector3 move, out bool wantMove) => StepIntoReach(task, out move, out wantMove);
        bool IErrandBody.HasReachNode(ReachTask task) => TryFindReachNode(task);

        void IErrandBody.Walk(ErrandLeg leg, BuddyNodeGraph.NavPath? plan)
        {
            if (plan.HasValue)
            {
                StartRoute(plan.Value, leg.Node);
            }
            else
            {
                DropPlan();
                routeGoal = leg.Node;
                mode = BuddyMode.Route;
            }
            reachTask = leg;
        }

        void IErrandBody.FinishRoute() => FinishRoute();
        string? IErrandBody.BusyForCommand() => BusyForCommand();
        Player? IErrandBody.PilotPlayer() => PilotPlayer();
        bool IErrandBody.IsAboardPlayerShip() => IsAboardPlayerShip();
        int IErrandBody.FeltTemperature(Environment env) => FeltTemperature(env);
    }
}

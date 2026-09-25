using System;
using System.Text;
using NPC.Core;
using NPC.Core.Agents;
using NPC.Core.World;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The buddy's mind: modes, orders, fear and hiding, errands, the decider, the HUD. NPC.Core's NpcAgent on
    /// the same GameObject walks, opens doors, rides vessels, breathes and dies; this is its brain.
    /// docs/architecture.md
    /// </summary>
    public sealed partial class BuddyBehaviour : MonoBehaviour, INpcBrain, INpcHider
    {
        private NpcAgent agent = null!; // Init sets it before the first frame
        private CharacterController cc = null!; // set in Awake

        /// <summary>
        /// The walking half, which NPC.Core's registry and patches see as this buddy.
        /// </summary>
        internal NpcAgent Agent => agent;

        /// <summary>
        /// Its console number ("@2") and name, unique among the buddies.
        /// </summary>
        internal int Number => agent.Number;
        internal string Name => agent.Name;
        internal bool IsDead => agent.IsDead;
        /// <summary>
        /// Shut in a cryo capsule on a new game: no AI at all until BuddyCryoSpawn wakes it.
        /// </summary>
        internal bool Asleep
        {
            get => agent.Asleep;
            set => agent.Asleep = value;
        }

        /// <summary>
        /// The mod name every buddy's log source starts with: "YourBuddy:Buddy 2".
        /// </summary>
        internal const string ModName = "YourBuddy";

        private BuddyMode mode = BuddyMode.Follow;
        /// <summary>
        /// Set by BuddyConversation while the talk window is open on it: hold still and face the player.
        /// </summary>
        internal bool InDialog = false;
        /// <summary>
        /// How it feels about the Breathless, and the stress behind that. docs/fear.md
        /// </summary>
        private FearState fearState = FearState.Calm;

        /// <summary>
        /// Alert or Scared: no step within 60 degrees of the monster's bearing while this near it.
        /// </summary>
        private const float FearRestraintDist = 10f;
        /// <summary>
        /// Where the monster was last seen or sensed; valid once fear has left Calm.
        /// </summary>
        private Vector3 lastMonsterPos = Vector3.zero;
        /// <summary>
        /// Distance to the monster at the last fear tick, whether or not it was in sight.
        /// </summary>
        private float monsterDist = float.MaxValue;
        /// <summary>
        /// Wander nodes near the monster, while it has the buddy on edge: docs/fear.md
        /// </summary>
        private Predicate<Vector3> nearMonster = null!; // set in Awake

        // Flee: retreat to a node away from the monster, then run to the player. docs/fear.md
        private enum FleePhase { Retreat, ToPlayer, Hold, Hide }

        // Hiding in a closet or locker. docs/fear.md §6
        private enum HideState { None, Walking, Entering, Hidden, Leaving }

        private HideState hideState = HideState.None;
        private string hideName = "the closet";
        private bool hideFromFear = false;
        /// <summary>
        /// The player asked for this one: it stays in until they say otherwise, or the monster goes away.
        /// docs/fear.md §6
        /// </summary>
        private bool hideOrdered = false;
        /// <summary>
        /// While hidden the monster is not watched for stress; this is the last time it was seen at all.
        /// </summary>
        private float hideMonsterSeenAt = 0f;
        private FleePhase fleePhase = FleePhase.Retreat;
        /// <summary>
        /// What to resume when the flee ends; an order given mid-flee replaces it.
        /// </summary>
        private BuddyMode modeBeforeFlee = BuddyMode.Follow;
        /// <summary>
        /// Where the current Route is going. A plan may end short of it, at an approach node.
        /// </summary>
        private Vector3 routeGoal = Vector3.zero;
        private float fleeRetryAt = 0f;
        private const float FleeSpeedFactor = 1.15f;

        // Orders and autonomy: what the player said, kept apart from what the buddy is
        // doing. docs/behaviour.md
        /// <summary>
        /// The order in force, or null when there is none. Written only through ApplyOrder,
        /// ApplyRouteOrder, RevokeOrder, and where an order ends: docs/invariants.md#an-order-is-not-a-mode
        /// </summary>
        private BuddyMode? orderedMode = null;
        /// <summary>
        /// Autonomous Wander stays on this owner's nodes; null means any active node.
        /// </summary>
        private string? wanderOwner = null;
        private float decideAt = 0f;
        private float boutEnteredAt = 0f;
        /// <summary>
        /// When the bout's clock started: a Follow's starts once it has caught up. Negative until then.
        /// </summary>
        private float boutSince = -1f;
        private float boutLength = 0f;

        private string urgeReport = "nothing weighed yet";
        /// <summary>
        /// A bout is still a bout: Follow and Wander are not weighed against each other until this much
        /// of the current one has run, and then the score ramps. So the switch lands somewhere in the last
        /// third rather than on the tick the clock runs out. docs/behaviour.md §3
        /// </summary>
        private const float BoutReadyFrom = 0.7f;
        private const float DecideCatchUpSeconds = 60f;
        /// <summary>
        /// Wander stays on the owner of the nearest node within this of the buddy.
        /// </summary>
        private const float DecideNodeOwnerRadius = 30f;

        // Walking into reach of an object. docs/terminals.md §2
        /// <summary>
        /// The leg the current Route leads to; ended when that Route ends.
        /// </summary>
        private ErrandLeg? reachTask = null;

        /// <summary>
        /// Called once by YourBuddyPlugin.SpawnBuddy, right after NpcAgent.Attach and before Start.
        /// </summary>
        internal void Init(NpcAgent walker) => agent = walker;

        // ------------------------------------------------------------------
        // What the agent asks its brain: npc-core:docs/agent.md#3-the-brain
        // ------------------------------------------------------------------

        void INpcBrain.SlowPhase(int phase, Player player)
        {
            switch (phase)
            {
                case 0:
                    // A save can restore one switched off.
                    if (YourBuddyPlugin.ConfigSellTrash.Value)
                    {
                        foreach (Room room in SellRoomsKeeper.Rooms) agent.LoadRoom(room, "it holds a sell station");
                    }
                    break;
                case 2:
                    UpdateFear();
                    break;
                case 3:
                    UpdateAutonomy(player);
                    lifeSupport.Update();
                    break;
            }
        }

        bool INpcBrain.OverrideMovement(Player player, out Vector3 move, out bool wantMove)
        {
            move = Vector3.zero;
            wantMove = false;
            // Being spoken to: hold still and face whoever is talking, whatever the
            // mode says. Walking off mid-sentence is not a conversation. docs/dialog.md
            if (InDialog)
            {
                agent.FacePlayer(player);
                return true;
            }

            // Inside the spot, or getting in and out of it, nothing else moves the buddy. The walk there
            // goes through Steer, so it sidesteps and opens doors as any walk does.
            if (!Hiding || hideState == HideState.Walking) return false;

            move = UpdateHide(out wantMove);
            return true;
        }

        Vector3 INpcBrain.Steer(Player player, out bool wantMove)
        {
            wantMove = false;
            // The walk to a hiding spot is a walk of its own, whatever the mode says. docs/fear.md §6
            if (hideState == HideState.Walking) return UpdateHide(out wantMove);

            switch (mode)
            {
                case BuddyMode.Follow:
                    return UpdateFollow(player, out wantMove);
                case BuddyMode.Wander:
                    // Autonomous wandering stays on the owner it was chosen on, and not over toward the
                    // Breathless while it has the buddy on edge. docs/behaviour.md, docs/fear.md
                    return agent.Wander(wanderOwner, fearState != FearState.Calm ? nearMonster : null, out wantMove);
                case BuddyMode.Route:
                    return UpdateRoute(out wantMove);
                case BuddyMode.Stay:
                    return agent.Stay(out wantMove);
                case BuddyMode.Flee:
                    return UpdateFlee(player, out wantMove);
            }
            return Vector3.zero;
        }

        /// <summary>
        /// Alert or Scared: no step toward the Breathless, whatever the mode wants, and
        /// so no door opened that way either. docs/fear.md
        /// </summary>
        Vector3 INpcBrain.Constrain(Vector3 desired, ref bool wantMove) => HoldBackFromMonster(desired, ref wantMove);

        /// <summary>
        /// The kind of walk each mode is, for the agent's recovery: docs/behaviour.md#4-every-place-that-reads-mode-outside-the-dispatch-switch
        /// </summary>
        NpcActivity INpcBrain.Activity
        {
            get
            {
                bool free = hideState == HideState.None;
                switch (mode)
                {
                    case BuddyMode.Follow:
                        return new NpcActivity(NpcRecovery.Pursuit, true, free, true);
                    case BuddyMode.Wander:
                        return new NpcActivity(NpcRecovery.DropPlanAndPause, true, free, true);
                    case BuddyMode.Route:
                        // A mid-route buddy clears a doorway on its own.
                        return new NpcActivity(NpcRecovery.SkipWaypoint, true, false, false);
                    case BuddyMode.Flee:
                        // The run to the player is Follow's own planning, so it gets Follow's recovery.
                        NpcRecovery recovery = fleePhase switch
                        {
                            FleePhase.Retreat => NpcRecovery.EndWalk,
                            FleePhase.ToPlayer => NpcRecovery.Pursuit,
                            _ => NpcRecovery.None
                        };
                        return new NpcActivity(recovery, true, false, true);
                    default:
                        // Stay, Dead: waiting is legitimate, and nothing is recovered.
                        return new NpcActivity(NpcRecovery.None, false, false, true);
                }
            }
        }

        string INpcBrain.ActivityName => mode.ToString();

        void INpcBrain.TryIdleFacing(Player player)
        {
            // Standing still with the Breathless about: watch it - unless being talked
            // to, where the override already faces the player. docs/fear.md
            if (fearState != FearState.Calm && !InDialog) agent.FacePoint(lastMonsterPos);
            else if (mode == BuddyMode.Follow) agent.FacePlayer(player);
        }

        /// <summary>
        /// A retreat that stalled, or a walk into reach that gave up without a task taking it over.
        /// </summary>
        void INpcBrain.OnWalkAbandoned(string why)
        {
            if (mode == BuddyMode.Flee) AbandonRetreat(why);
            else FinishRoute();
        }

        /// <summary>
        /// Its current errand leg or hide: docs/invariants.md#one-buddy-per-target
        /// </summary>
        bool INpcBrain.Holds(Transform t)
        {
            if (hideSpot != null && hideState != HideState.None && hideSpot.transform == t) return true;

            return reachTask != null && reachTask.Holds(t);
        }

        /// <summary>
        /// Shut in a closet, it is out of reach: the walls block the monster. docs/fear.md §6
        /// </summary>
        bool INpcBrain.Sheltered => hideState == HideState.Hidden;

        void INpcBrain.OnInterrupted(string why) => ForceLeaveHidingSpot(why);

        void INpcBrain.OnDied() => mode = BuddyMode.Dead;

        /// <summary>
        /// A goto's goal is a world point of the old layout, so it ends rather than resumes.
        /// npc-core:docs/invariants.md#an-npc-rides-its-own-floor
        /// </summary>
        bool INpcBrain.OnShipRebuilt()
        {
            bool gotoEnded = mode == BuddyMode.Route || (mode == BuddyMode.Flee && modeBeforeFlee == BuddyMode.Route);
            if (orderedMode == BuddyMode.Route) orderedMode = null;
            if (mode == BuddyMode.Flee && modeBeforeFlee == BuddyMode.Route) modeBeforeFlee = BuddyMode.Follow;
            if (mode == BuddyMode.Route) SetMode(BuddyMode.Follow);
            if (gotoEnded) decideAt = 0f;

            return gotoEnded;
        }

        bool INpcHider.Occupies(HidingSpot spot) => IsHiddenIn(spot);

        // ------------------------------------------------------------------
        // HUD
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            // Draw-only panel: npc-core:docs/invariants.md#read-only-panels-build-on-repaint
            if (UnityEngine.Event.current.type != EventType.Repaint) return;

            if (!YourBuddyPlugin.ConfigShowHud.Value) return;

            // One panel, for the buddy commands go to.
            if (BuddyManager.Focus != this) return;

            if (GameManager.Instance == null) return;

            // The console is a uGUI menu, which IMGUI always draws over: the panel steps aside instead.
            if (NpcConsole.IsOpen) return;

            string text = StatusText();
            // Measured only when the cached text changes: npc-core:docs/invariants.md#read-only-panels-build-on-repaint
            if (!ReferenceEquals(text, hudMeasuredText))
            {
                hudMeasuredText = text;
                hudTextHeight = GUI.skin.label.CalcHeight(new GUIContent(text), HudWidth - 25f);
            }
            GUI.Box(new Rect(10f, 10f, HudWidth, hudTextHeight + 32f),
                BuddyManager.All.Count > 1 ? "YourBuddy - " + Name : "YourBuddy");
            GUI.Label(new Rect(20f, 32f, HudWidth - 15f, hudTextHeight + 4f), text);
        }

        private const float HudWidth = 460f;
        private string? hudMeasuredText = null;
        private float hudTextHeight = 0f;
        private const float StatusTextInterval = 0.2f;
        private string? statusText = null;
        private float statusTextAt = 0f;
        // One builder for every rebuild: appending to a string copied the whole panel per line.
        private static readonly StringBuilder HudText = new();

        /// <summary>
        /// The HUD panel text, rebuilt five times a second rather than per GUI event.
        /// npc-core:docs/invariants.md#read-only-panels-build-on-repaint
        /// </summary>
        private string StatusText()
        {
            if (statusText != null && Time.time < statusTextAt) return statusText;

            statusTextAt = Time.time + StatusTextInterval;
            bool parked = !gameObject.activeInHierarchy;
            StringBuilder text = HudText.Clear();
            text.Append("Mode: ").Append(parked ? "parked" : Asleep ? "asleep" : mode.ToString());
            if (DescribeReachTask() is { } task) text.Append(" (").Append(task).Append(')');
            if (IsDead) text.Append(" [DEAD]");
            // "none" is the resting state, not news: the Mind line already says it is deciding.
            if (orderedMode.HasValue) text.Append("\nOrders: ").Append(DescribeOrders());

            text.Append("\nPos: ").Append(transform.position.ToString("0.00"));
            if (agent.CurrentOwner != null) text.Append(", on ").Append(agent.CurrentOwner).Append(parked ? " (unloaded)" : "");
            text.Append('\n').Append(agent.DescribeSurroundings());
            // The timers are deadlines on Time.time: printed for a dead buddy they count down with nothing behind them.
            if (IsDead)
            {
                text.Append("\nFear, mind, air, snack: nothing runs while dead");
            }
            else
            {
                text.Append("\nFear: ").Append(DescribeFear());
                if (DescribeHudMind() is { } mind) text.Append('\n').Append(mind);
                text.Append("\nAir: ").Append(lifeSupport.Describe()).Append("\nSnack: ").Append(snacks.Describe())
                    .Append("\nTidy: ").Append(tidying.Describe()).Append("\nSell: ").Append(selling.Describe())
                    .Append("\nPlay: ").Append(play.Describe());
            }
            if (agent.MoveTarget is { } target)
            {
                text.Append("\nTarget: ").Append(target.ToString("0.0")).Append(" (")
                    .Append(Vector3.Distance(transform.position, target).ToString("0.0")).Append("m)");
            }

            statusText = text.ToString();
            return statusText;
        }

        /// <summary>
        /// buddy_list's line: "#2 Buddy 2 * - Follow (tidying up), on ShipyardStation, 4.1 m away".
        /// </summary>
        internal string ListLine(bool focused)
        {
            string state = IsDead ? "dead" : !gameObject.activeInHierarchy ? "parked" : Asleep ? "asleep" : mode.ToString();
            if (!IsDead && DescribeReachTask() is { } task) state += " (" + task + ")";

            Player? player = PilotPlayer();
            string away = player != null && player.Controller != null
                ? ", " + Vector3.Distance(transform.position, player.Controller.CachedTransform.position).ToString("0.0") + " m away"
                : "";
            return "#" + Number + " " + Name + (focused ? " *" : "") + " - " + state +
                   (agent.CurrentOwner != null ? ", on " + agent.CurrentOwner : "") + away;
        }

        private void OnDestroy()
        {
            BuddyManager.Unregister(this);
        }
    }

}

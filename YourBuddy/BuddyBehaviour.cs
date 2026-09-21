using System.Collections.Generic;
using FMOD.Studio;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// AI behaviour component for the Buddy NPC: mode logic, NodeGraph-based navigation,
    /// doors, room loading, mortality and space protection.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed partial class BuddyBehaviour : MonoBehaviour
    {
        // Handed over by YourBuddyPlugin.SpawnBuddy through Init.
        private GameObject? ragdollObject;
        private Rigidbody? ragdollRigidbody;
        private GameObject? animatedModel;
        private Collider itemBlocker = null!; // SpawnBuddy calls Init before the first frame
        private CharacterController? playerCharacterController;

        // ReSharper disable RedundantDefaultMemberInitializer
        private BuddyMode mode = BuddyMode.Follow;
        internal bool IsDead { get; private set; }
        /// <summary>
        /// Shut in a cryo capsule on a new game: no AI at all until BuddyCryoSpawn wakes it.
        /// </summary>
        internal bool Asleep;
        /// <summary>
        /// Set by BuddyDialog while the player has it open: hold still and face them.
        /// </summary>
        internal bool inDialog = false;
        internal float moveSpeed = 3.5f;
        /// <summary>
        /// Which vessel's frame the buddy is riding ("ship", "world" or a station name).
        /// docs/invariants.md#the-buddy-rides-its-own-floor
        /// </summary>
        internal string? CurrentOwner { get; private set; }
        private Room? currentRoomRef = null;
        private Vector3 currentMoveTarget = Vector3.zero;
        private bool hasMoveTarget = false;
        /// <summary>
        /// How it feels about the Breathless, and the stress behind that. docs/fear.md
        /// </summary>
        private FearState fearState = FearState.Calm;

        // Movement / animation
        private CharacterController cc;
        private Animator[] anims;
        private float verticalVelocity = 0f;
        private bool wasMoving = false;
        private float jumpCommitUntil = 0f;
        private float obstacleReportAt = 0f;

        // The prefab root sits at CharacterController center height (~0.66m above the
        // floor), so probe heights must be computed from the ground, not the transform.
        private float originToFeet = 0f;

        // Footstep sound system (copied from player's CameraAnimator)
        private EventInstance footstepInstance;

        // Node-based navigation. navPlan holds the route plus which segments arrive via
        // a Force/Priority link; those skip the LOS commitment check by design.
        // docs/invariants.md#force-and-priority-have-no-los
        private BuddyNodeGraph.NavPath? navPlan = null;
        private int navPathIndex = 0;
        /// <summary>
        /// A waypoint on this plan was abandoned rather than reached, so running out of
        /// waypoints is a failure: docs/invariants.md#a-skipped-waypoint-is-not-an-arrival
        /// </summary>
        private bool routeSkippedWaypoint = false;
        private float navPathRecalcAt = 0f;
        private bool hasNavPathGoal = false;

        /// <summary>
        /// Set each frame by the mode that is walking a plan; read by the local steering.
        /// </summary>
        private bool walkingStairLeg = false;
        private Vector3 loggedStairLegEnd = Vector3.zero;

        // Follow's hysteresis is an XZ test, so a player one deck up reads as 0m away.
        // This deck gap, measured floor to floor, is what stops the buddy parking
        // underneath them. docs/invariants.md#follow-arrival-is-level-aware
        internal const float FollowSameLevelDeltaY = 0.5f;

        // A waypoint the buddy demonstrably failed to reach - it kept moving without
        // closing on it. Fed to FindPath as `avoidEntry`.
        // docs/invariants.md#avoid-entry-bars-the-whole-search
        private Vector3 unreachableWaypoint = Vector3.zero;
        private float unreachableWaypointUntil = 0f;

        private float wanderIdleUntil = 0f;

        // Obstacle avoidance (simplified - NodeGraph handles most routing)
        /// <summary>
        /// Whisker detours tried in order, widest last.
        /// </summary>
        private static readonly float[] DetourAngles = [30f, -30f, 60f, -60f, 90f, -90f];
        private Vector3 lastAvoidDirection = Vector3.forward;
        private Vector3 sidestepDirection = Vector3.zero;
        private float sidestepUntil = 0f;
        private Vector3 stuckCheckPosition = Vector3.zero;

        // Doors
        private Gate? waitingGate = null;
        private float followWaitUntil = 0f;
        private Vector3 followStepOffTarget = Vector3.zero;
        private float followStepOffUntil = 0f;
        // Doors the buddy opened and still owes a close to.
        // docs/invariants.md#pending-closes-are-a-list
        private sealed class PendingDoorClose
        {
            public Gate Gate = null!; // always set by its object initializer
            public float CloseAt;
            public float ArmedAt;
            public int Attempts;
            /// <summary>
            /// When the current run of deferrals began, 0 when last seen clear.
            /// docs/invariants.md#no-permanent-deferral
            /// </summary>
            public float DeferredSince;
            /// <summary>
            /// Where the buddy stood when it opened this gate, and whether it has since
            /// come out the other side.
            /// docs/invariants.md#close-only-what-you-walked-through
            /// </summary>
            public Vector3 OpenedFrom;
            public bool Crossed;
        }
        private readonly List<PendingDoorClose> pendingDoorCloses = [];

        // Gates the buddy cannot get through right now, fed to the path search so it
        // routes around them. docs/invariants.md#locked-doors-block-edges
        private readonly List<Gate> impassableGates = [];

        // Monster catch sequence
        private bool catchInProgress = false;

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
        private const float ReachStandArrival = 0.3f;
        /// <summary>
        /// Within this of its node the route counts as walked; further, it is planned again, at most ReachMaxReplans times.
        /// </summary>
        private const float ReachNodeArrival = 1.2f;
        private static readonly List<Vector3> ReachNodeBuffer = [];

        // Room tracking / environment / safety
        private Room? forcedRoom = null;
        private float slowTimer = 0f;
        private int slowPhase = 0;

        // Last confirmed floor plane. The buddy's own feet are not a good reference:
        // after hopping onto a counter every real floor looks "elevated". A new plane is
        // adopted only after it has stood at that level for a while.
        private float baseFloorY = 0f;
        private bool hasBaseFloor = false;

        // Scene caches
        private List<EntryDetector>? cachedDetectors = null;
        private List<Airlock>? cachedAirlocks = null;
        // ReSharper restore RedundantDefaultMemberInitializer

        // Pre-allocated physics buffers: the whisker/diagnostic probes run every frame,
        // so they must not allocate (use the non-allocating NonAlloc methods).
        // All physics queries happen on the main thread, so shared buffers are safe.
        private static readonly RaycastHit[] CastBuffer = new RaycastHit[32];
        private static readonly Collider[] OverlapBuffer = new Collider[16];
        /// <summary>
        /// Called once by YourBuddyPlugin.SpawnBuddy, right after AddComponent and before Start.
        /// </summary>
        internal void Init(GameObject? ragdoll, Rigidbody? ragdollBody, GameObject? model, Collider blocker,
            CharacterController? playerController, float speed)
        {
            ragdollObject = ragdoll;
            ragdollRigidbody = ragdollBody;
            animatedModel = model;
            itemBlocker = blocker;
            playerCharacterController = playerController;
            moveSpeed = speed;
            hands = new BuddyHands(this, blocker, GroundPos, () => currentRoomRef);
        }

        /// <summary>
        /// Whether a collider belongs to the player's body.
        /// </summary>
        internal bool IsPlayerBody(Transform t) =>
            playerCharacterController != null && t.IsChildOf(playerCharacterController.transform);

        private void Start()
        {

            cc = GetComponent<CharacterController>();
            anims = GetComponentsInChildren<Animator>();
            SaveParser.OnFileSaveInitiated.AddListener(OnGameSaving);

            foreach (Animator a in anims)
            {
                a.enabled = true;
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                a.applyRootMotion = false;
                a.speed = 1f;
                a.updateMode = AnimatorUpdateMode.Normal;
                if (a.gameObject.activeInHierarchy) a.Rebind();
            }

            if (GameManager.Instance != null && GameManager.Instance.PlayerShip != null && GameManager.Instance.PlayerShip.Pilot != null)
            {
                CopyFootstepEventsFromPlayer();
            }

            InitializeFootsteps();

            if (YourBuddyPlugin.ConfigDebugVisuals.Value) EnsureDebugVisuals(true);

            stuckCheckPosition = transform.position;
            ComputeOriginToFeet();

            // The graph holds no gameplay rules, so it asks us which doors are shut to
            // the buddy. docs/invariants.md#locked-doors-block-edges
            BuddyNodeGraph.SegmentBlockedByDoor = SegmentBlockedByDoor;
        }

        /// <summary>
        /// Works out how far the transform origin floats above the ground, so probe
        /// heights (shin / body / head) can be expressed relative to the actual floor.
        /// </summary>
        private void ComputeOriginToFeet()
        {
            if (cc != null)
            {
                originToFeet = cc.center.y - cc.height * 0.5f;
                return;
            }

            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 5f, ProbeLayers, QueryTriggerInteraction.Ignore))
            {
                originToFeet = hit.point.y - transform.position.y;
            }
        }

        /// <summary>
        /// Floor point under the buddy: plans live on the walkable plane, not on the
        /// transform origin. Goes through NavProbe so every deck is measured the same way.
        /// docs/invariants.md#one-probe-basis
        /// </summary>
        public Vector3 FloorUnderBuddy()
        {
            Vector3 p = transform.position;
            float feetY = p.y + originToFeet;

            // Standing on something is the answer, and a better one than any probe.
            // docs/invariants.md#a-grounded-buddy-stands-on-its-own-feet
            if (cc != null && cc.isGrounded) return new Vector3(p.x, feetY, p.z);

            if (NavProbe.TryFloorHeight(p, out float floorY)) return new Vector3(p.x, floorY, p.z);
            // Nothing underneath at all (mid-jump over a gap): the feet are the honest
            // answer, never the transform origin - that floats ~0.66m up.
            return new Vector3(p.x, feetY, p.z);
        }

        /// <summary>
        /// The floor the player is standing on, by the same rule as the buddy's.
        /// Follow's "are we on the same deck" test runs on this, and the doorway that
        /// answers two storeys down is one the player walks through too.
        /// docs/invariants.md#a-grounded-buddy-stands-on-its-own-feet
        /// </summary>
        private float FloorUnderPlayer(Transform playerTransform)
        {
            CharacterController? playerCc = playerCharacterController;
            if (playerCc != null && playerCc.isGrounded)
            {
                return playerCc.transform.position.y + playerCc.center.y - playerCc.height * 0.5f;
            }
            return NavProbe.FloorHeight(playerTransform.position);
        }

        /// <summary>
        /// A point at the given height above the buddy's feet.
        /// </summary>
        private Vector3 GroundPos(float height)
        {
            return transform.position + Vector3.up * (originToFeet + height);
        }

        // ------------------------------------------------------------------
        // Main update
        // ------------------------------------------------------------------

        private void Update()
        {
            if (IsDead || cc == null) return;

            GameManager gm = GameManager.Instance;
            if (gm == null || gm.PlayerShip == null) return;

            Player player = gm.PlayerShip.Pilot;

            // ai_disable, or asleep in its capsule: everything off, gravity kept so it does not float.
            if (AiDebug.BuddyDisabled || Asleep)
            {
                ApplyMovement(Vector3.zero, false);
                UpdateAnimation(Vector3.zero, false, player);
                return;
            }

            SlowUpdate(player);

            // While the monster is grabbing the buddy, it can't move.
            if (catchInProgress)
            {
                ApplyMovement(Vector3.zero, false);
                UpdateAnimation(Vector3.zero, false, player);
                return;
            }

            // Being spoken to: hold still and face whoever is talking, whatever the
            // mode says. Walking off mid-sentence is not a conversation. docs/dialog.md
            if (inDialog)
            {
                ApplyMovement(Vector3.zero, false);
                FacePlayer(player);
                UpdateAnimation(Vector3.zero, false, player);
                return;
            }

            // Inside the spot, or getting in and out of it, nothing else moves the buddy. The walk there
            // goes through the mode switch below, so it sidesteps and opens doors as any walk does.
            if (Hiding && hideState != HideState.Walking)
            {
                Vector3 hideMove = UpdateHide(out bool hideWants);
                if (cc.enabled) ApplyMovement(hideMove, hideWants);

                UpdateAnimation(hideMove, hideWants, player);
                return;
            }

            // Mode logic - decide where we want to go this frame.
            Vector3 desired = Vector3.zero;
            bool wantMove = false;
            walkingStairLeg = false;

            // The walk to a hiding spot is a walk of its own, whatever the mode says. docs/fear.md §6
            if (hideState == HideState.Walking) desired = UpdateHide(out wantMove);
            else switch (mode)
            {
                case BuddyMode.Follow:
                    desired = UpdateFollow(player, out wantMove);
                    break;
                case BuddyMode.Wander:
                    desired = UpdateWander(out wantMove);
                    break;
                case BuddyMode.Route:
                    desired = UpdateRoute(out wantMove);
                    break;
                case BuddyMode.Stay:
                    desired = UpdateStay(out wantMove);
                    break;
                case BuddyMode.Flee:
                    desired = UpdateFlee(player, out wantMove);
                    break;
            }

            // On a stair leg there is no sidestep, hop or detour: each of those reasons on
            // the flat and led off the flight. docs/invariants.md#a-stair-leg-is-walked-not-improvised
            if (walkingStairLeg) sidestepUntil = 0f;
            else loggedStairLegEnd = Vector3.zero;

            // Sidestep maneuver when stuck against an obstacle.
            if (wantMove && Time.time < sidestepUntil) desired = sidestepDirection * (moveSpeed * 0.8f);

            // Alert or Scared: no step toward the Breathless, whatever the mode wants, and
            // so no door opened that way either. docs/fear.md
            desired = HoldBackFromMonster(desired, ref wantMove);

            // Door handling: open closed room gates in front of us, wait for them,
            // and walk through the center of the doorway.
            desired = HandleDoors(desired, ref wantMove);

            // Auto-jump is evaluated on the raw target direction, before steering:
            // otherwise the whiskers deflect the direction first and the jump ray misses
            // the very obstacle the buddy should hop over.
            if (wantMove && !walkingStairLeg) TryAutoJump(desired);

            // While a jump is committed (and during the ascent), steer straight at the
            // obstacle instead of around it - avoidance must not cancel the hop.
            bool steeringSuspended = Time.time < jumpCommitUntil || verticalVelocity > 0.1f;
            if (wantMove && !steeringSuspended && !walkingStairLeg) desired = SteerAroundObstacles(desired);

            // Obstacle diagnostics (level 3): report everything in the buddy's path.
            if (wantMove && YourBuddyPlugin.ConfigDebugVisuals.Value &&
                YourBuddyPlugin.ConfigDebugLevel.Value >= 3 && Time.time >= obstacleReportAt)
            {
                obstacleReportAt = Time.time + 0.25f;
                ReportObstacles(desired);
            }

            if (!wantMove) desired = Vector3.zero;

            UpdateIdleRecovery(wantMove);
            UpdateStuckDetection(wantMove);
            ApplyMovement(desired, wantMove);
            UpdateAnimation(desired, wantMove, player);
            UpdateDebugVisuals();
        }

        /// <summary>
        /// Periodic (non per-frame) logic: room tracking, environment, space safety,
        /// monster proximity and fear, and the decider. One phase runs
        /// per call; never add a fifth, it stretches every phase (docs/architecture.md §3).
        /// </summary>
        private void SlowUpdate(Player player)
        {
            slowTimer += Time.deltaTime;
            if (slowTimer < 0.06f) return;

            slowTimer = 0f;
            slowPhase = (slowPhase + 1) % 4;

            switch (slowPhase)
            {
                case 0:
                    UpdateBaseFloor();
                    UpdateOwnerAnchor();
                    UpdateRoomTracking();
                    UpdateEnvironment();
                    break;
                case 1:
                    UpdateSpaceState(player);
                    break;
                case 2:
                    BreathlessCheck();
                    UpdateFear();
                    break;
                case 3:
                    UpdateDoorCloseBehind();
                    UpdateAutonomy(player);
                    lifeSupport.Update();
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Cleanup
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            // Draw-only panel: docs/invariants.md#read-only-panels-build-on-repaint
            if (UnityEngine.Event.current.type != EventType.Repaint) return;

            if (!YourBuddyPlugin.ConfigShowHud.Value) return;

            if (BuddyManager.CurrentBuddy != this) return;

            if (GameManager.Instance == null) return;

            // The console is a uGUI menu, which IMGUI always draws over: the panel steps aside instead.
            if (BuddyNodeEditor.ConsoleOpen()) return;

            string text = StatusText();
            // Measured only when the cached text changes: docs/invariants.md#read-only-panels-build-on-repaint
            if (!ReferenceEquals(text, hudMeasuredText))
            {
                hudMeasuredText = text;
                hudTextHeight = GUI.skin.label.CalcHeight(new GUIContent(text), HudWidth - 25f);
            }
            GUI.Box(new Rect(10f, 10f, HudWidth, hudTextHeight + 32f), "YourBuddy");
            GUI.Label(new Rect(20f, 32f, HudWidth - 15f, hudTextHeight + 4f), text);
        }

        private const float HudWidth = 460f;
        private string? hudMeasuredText = null;
        private float hudTextHeight = 0f;
        private const float StatusTextInterval = 0.2f;
        private string? statusText = null;
        private float statusTextAt = 0f;

        /// <summary>
        /// The HUD panel text, rebuilt five times a second rather than per GUI event.
        /// docs/invariants.md#read-only-panels-build-on-repaint
        /// </summary>
        private string StatusText()
        {
            if (statusText != null && Time.time < statusTextAt) return statusText;

            statusTextAt = Time.time + StatusTextInterval;
            bool parked = !gameObject.activeInHierarchy;
            string text = "Mode: " + (parked ? "parked" : Asleep ? "asleep" : mode.ToString()) +
                          (DescribeReachTask() is { } task ? " (" + task + ")" : "") +
                          (IsDead ? " [DEAD]" : "");
            // "none" is the resting state, not news: the Mind line already says it is deciding.
            if (orderedMode.HasValue) text += "\nOrders: " + DescribeOrders();

            text += "\nPos: " + transform.position.ToString("0.00") +
                    (CurrentOwner != null ? ", on " + CurrentOwner + (parked ? " (unloaded)" : "") : "");
            text += "\n" + DescribeSurroundings();
            // The timers are deadlines on Time.time: printed for a dead buddy they count down with nothing behind them.
            if (IsDead)
            {
                text += "\nFear, mind, air, snack: nothing runs while dead";
            }
            else
            {
                text += "\nFear: " + DescribeFear();
                if (DescribeHudMind() is { } mind) text += "\n" + mind;
                text += "\nAir: " + lifeSupport.Describe() + "\nSnack: " + snacks.Describe() +
                        "\nTidy: " + tidying.Describe() + "\nSell: " + selling.Describe() + "\nPlay: " + play.Describe();
            }
            if (hasMoveTarget)
            {
                text += "\nTarget: " + currentMoveTarget.ToString("0.0") + " (" +
                        Vector3.Distance(transform.position, currentMoveTarget).ToString("0.0") + "m)";
            }

            statusText = text;
            return text;
        }

        private void OnDestroy()
        {
            SaveParser.OnFileSaveInitiated.RemoveListener(OnGameSaving);
            hands.Drop("the buddy is gone");
            ForceLeaveHidingSpot("the buddy is gone");
            if (footstepInstance.isValid())
            {
                footstepInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                footstepInstance.release();
            }

            if (forcedRoom != null) forcedRoom.OnContentStateChanged.RemoveListener(OnForcedRoomContentChanged);

            // The ragdoll gets unparented on death - clean it up with the buddy.
            if (ragdollObject != null) Destroy(ragdollObject);

            if (BuddyManager.CurrentBuddy == this) BuddyManager.CurrentBuddy = null;

            if (BuddyNodeGraph.SegmentBlockedByDoor == SegmentBlockedByDoor) BuddyNodeGraph.SegmentBlockedByDoor = null;
        }
    }

}

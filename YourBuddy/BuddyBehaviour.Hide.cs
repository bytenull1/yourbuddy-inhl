using System.Collections.Generic;
using UnityEngine;
using static YourBuddy.Items;

namespace YourBuddy
{
    /// <summary>
    /// Hiding from the Breathless in a closet or locker: a flee that ends behind shut doors instead of
    /// across the ship. The mod's own catch and its sight both have to honour it. docs/fear.md §6
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        private HidingSpot? hideSpot = null;
        private readonly List<Door> hideOpened = [];
        private static readonly List<HidingSpot> HideCandidates = [];
        /// <summary>
        /// The floor point outside it walked in from and steps back out to, the point in front of the
        /// doors, and what to call the spot in a log line.
        /// </summary>
        private Vector3 hideStand;
        private Vector3 hideNode;
        private Vector3 hideFace;
        private float hidePhaseUntil = 0f;
        private float hiddenSince = 0f;
        private float hideWaitLogAt = 0f;
        /// <summary>
        /// The doors have been seen shut at least once since it climbed in; until then a door still
        /// reading open is its own close animation, not someone opening it. docs/fear.md §6
        /// </summary>
        private bool hideDoorsShut = false;
        private float hideDoorsShutBy = 0f;
        /// <summary>
        /// Whether the reason it is leaving still counts as being frightened. False only when the
        /// hide ended because the fear passed, and that ends the flee instead of retrying it.
        /// docs/invariants.md#a-hide-that-worked-ends-the-flee
        /// </summary>
        private bool hideStillAfraid = true;
        private string? hideLast = null;
        private readonly SkipList hideSkips = new();
        private const float HideSearchRadius = 14f;
        private const int HideMaxPlans = 3;
        private const float HideSkipSeconds = 600f;
        /// <summary>
        /// A flee hides only when the walk there is this short; further away, running is quicker.
        /// </summary>
        private const float HidePreferWalk = 16f;
        /// <summary>
        /// Never hidden in this near the Breathless: it would be shut in with it.
        /// </summary>
        private const float HideMonsterClearance = 3f;
        /// <summary>
        /// The walk there gives up after this plus its length; a doorway can be blocked for a while.
        /// </summary>
        private const float HideWalkSeconds = 8f;
        private const float HideWalkSecondsPerMetre = 1.2f;
        /// <summary>
        /// Inside for at least this long, then out once Calm and nothing has been seen for HideCalmSeconds.
        /// Neither ends it while the monster is within HideMonsterNearDist: docs/fear.md §6
        /// </summary>
        private const float HideMinSeconds = 25f;
        private const float HideCalmSeconds = 15f;
        /// <summary>
        /// How long a wait inside goes between log lines. docs/logging.md §4
        /// </summary>
        private const float HideWaitLogSeconds = 15f;
        /// <summary>
        /// Door.Opened only turns false when the close animation ends (Door.CloseRoutine), which takes
        /// 1/openSpeed seconds. Longer than that with a door still open is a door that will not shut.
        /// </summary>
        private const float HideDoorShutSeconds = 6f;
        /// <summary>
        /// It does not come out at all with the monster this near - measured without sight, through the
        /// closet walls, because hidden it cannot see but can hear.
        /// docs/invariants.md#a-hidden-buddy-waits-out-a-monster-it-can-hear
        /// </summary>
        private const float HideMonsterNearDist = 5f;
        /// <summary>
        /// A monster parked outside forever must not freeze the buddy in a cupboard.
        /// </summary>
        private const float HideMaxSeconds = 240f;

        /// <summary>
        /// Only used to reuse the reach walk's node and stand-point search; the walk itself is the flee's.
        /// </summary>
        private sealed class HideTask(HidingSpot spot, Vector3 doorFace, SkipList skips) : ReachTask(doorFace, spot.transform)
        {
            public readonly HidingSpot Spot = spot;

            public override string Name => "the " + ContainerKind(Spot.transform);
            public override float Reach => SnackReachDist;
            public override float[] StandOffs => SnackStandOffs;
            public override float ReachBelow => SnackReachBelow;

            public override void Defer(float seconds) => skips.Skip(Own, HideSkipSeconds);
        }

        /// <summary>
        /// True while the buddy owns its own movement to hide: the mode logic is skipped.
        /// </summary>
        internal bool Hiding => hideState != HideState.None;

        /// <summary>
        /// Where a save records a hidden buddy: outside, where it will step back out.
        /// docs/invariants.md#a-hidden-buddy-is-saved-outside-its-hiding-spot
        /// </summary>
        internal Vector3? HiddenSavePoint =>
            hideState == HideState.Hidden ? hideStand - Vector3.up * originToFeet : null;

        /// <summary>
        /// Whether the buddy is inside this spot now: the game's Interact asks before mounting you.
        /// </summary>
        internal bool IsHiddenIn(HidingSpot spot) => hideSpot == spot && hideState is HideState.Hidden or HideState.Leaving;

        /// <summary>
        /// A hiding spot near enough, free, reachable and not toward the monster. Starts the walk.
        /// `fromFear` is a flee; buddy_hide passes false. docs/fear.md §6
        /// </summary>
        private bool TryStartHide(bool fromFear, out string report)
        {
            if (!YourBuddyPlugin.ConfigHideInClosets.Value)
            {
                report = "hiding is switched off (HideInClosets)";
                return false;
            }
            hideSkips.Prune();
            Vector3 here = transform.position;
            string? failure = null;
            int plans = 0;
            HideCandidates.Clear();
            foreach (HidingSpot spot in FindObjectsOfType<HidingSpot>())
            {
                if (spot == null || !spot.gameObject.activeInHierarchy) continue;

                if (FlatDistanceSq(spot.transform.position, here) > HideSearchRadius * HideSearchRadius) continue;

                if (hideSkips.Has(spot.transform)) continue;

                HideCandidates.Add(spot);
            }
            HideCandidates.Sort((a, b) =>
                FlatDistanceSq(a.transform.position, here).CompareTo(FlatDistanceSq(b.transform.position, here)));

            foreach (HidingSpot spot in HideCandidates)
            {
                string? blocker = HideSpotBlocker(spot, fromFear);
                if (blocker != null)
                {
                    failure = blocker;
                    TraceHide($"not the {ContainerKind(spot.transform)} at {spot.transform.position:0.0}: {blocker}");
                    continue;
                }
                Door[] doors = spot.GetComponentsInChildren<Door>();
                HideTask task = new(spot, DoorFacePoint(doors), hideSkips);
                if (plans++ >= HideMaxPlans) break;

                failure = PlanReach(task, out BuddyNodeGraph.NavPath plan);
                if (failure != null)
                {
                    hideSkips.Skip(spot.transform, HideSkipSeconds);
                    TraceHide($"not {task.Name} at {task.TargetPoint:0.0}: {failure} - skipping it for {HideSkipSeconds:0}s");
                    continue;
                }
                float walk = PlanLength(plan);
                if (fromFear && !PlanKeepsClear(plan, 0))
                {
                    failure = "the way there passes the Breathless";
                    TraceHide($"not {task.Name}: {failure}");
                    continue;
                }
                if (fromFear && walk > HidePreferWalk)
                {
                    failure = $"the walk to it is {walk:0.0}m, more than {HidePreferWalk:0}m - running is quicker";
                    TraceHide($"not {task.Name}: {failure}");
                    continue;
                }

                BeginHide(task, plan, fromFear);
                report = $"is hiding in {task.Name}, {walk:0.0}m away";
                YourBuddyPlugin.Log.LogInfo($"[fear] Hiding in {task.Name} {walk:0.0}m away" +
                                            (fromFear ? $" (the Breathless is {Vector3.Distance(lastMonsterPos, here):0.0}m away)" : ""));
                return true;
            }
            report = HideCandidates.Count == 0
                ? $"there is no closet or locker within {HideSearchRadius:0}m"
                : $"none of the {HideCandidates.Count} hiding spot(s) nearby can be used ({failure})";
            return false;
        }

        /// <summary>
        /// Why this spot may not be hidden in now, or null.
        /// </summary>
        private string? HideSpotBlocker(HidingSpot spot, bool fromFear)
        {
            if (spot.Mounted) return "you are hiding in it";

            // `Mountable.locked` is not a lock. Closet.UpdateMountAvailability sets it to
            // `!leftDoor.Opened && !rightDoor.Opened` and Locker.CheckLockState to
            // `holder.Item != null || !door.Opened`: shut reads as locked. Shut is the state the buddy
            // finds every spot in and opens for itself, so testing it rejected every cabinet outright -
            // the doors test below is the real one. docs/fear.md §6
            EquipmentHolder? holder = spot.GetComponentInChildren<EquipmentHolder>(true);
            if (holder != null && holder.Item != null) return "a suit is hanging in it";

            if (!OnMyVessel(spot.transform)) return "it is not on the deck I am riding";

            CustomRoom room = spot.GetComponentInParent<CustomRoom>(true);
            if (room != null && !room.EnabledStructure) return "its room is not built";

            Door[] doors = spot.GetComponentsInChildren<Door>();
            if (doors.Length == 0) return "it has no doors";

            // A door standing open is one it would not close again, and it would be found through it at once.
            foreach (Door door in doors)
            {
                if (door != null && door.Opened) return "its door is already open";
            }

            if (fromFear && Vector3.Distance(spot.transform.position, lastMonsterPos) < HideMonsterClearance)
            {
                return $"it is within {HideMonsterClearance:0}m of the Breathless";
            }
            return null;
        }

        private static float PlanLength(BuddyNodeGraph.NavPath plan)
        {
            float length = 0f;
            for (int i = 1; i < plan.Count; i++) length += Vector3.Distance(plan[i - 1], plan[i]);

            return length;
        }

        /// <summary>
        /// Takes the plan over from whatever was walking it: hiding owns the buddy until it is out again.
        /// </summary>
        private void BeginHide(HideTask task, BuddyNodeGraph.NavPath plan, bool fromFear)
        {
            DropReachTask();
            // An errand this pre-empted owned the Route mode, and nothing walks it now.
            if (mode == BuddyMode.Route && !GotoUnderway) mode = ModeAfterTask;

            navPlan = plan;
            navPathIndex = 0;
            hideSpot = task.Spot;
            hideStand = task.StandPoint;
            hideNode = task.Node;
            hideFace = task.TargetPoint;
            hideName = task.Name;
            hideFromFear = fromFear;
            hideOrdered = !fromFear;
            hideWaitLogAt = 0f;
            hideOpened.Clear();
            hideState = HideState.Walking;
            hideMonsterSeenAt = Time.time;
            hidePhaseUntil = Time.time + HideWalkSeconds + PlanLength(plan) * HideWalkSecondsPerMetre;
            hideLast = "on the way to " + task.Name;
            if (fromFear) fleePhase = FleePhase.Hide;
        }

        /// <summary>
        /// Update, ahead of the mode logic, while Hiding. Returns the step to take, if any.
        /// </summary>
        private Vector3 UpdateHide(out bool wantMove)
        {
            wantMove = false;
            if (hideSpot == null || !hideSpot.gameObject.activeInHierarchy)
            {
                AbandonHide("it is gone");
                return Vector3.zero;
            }

            switch (hideState)
            {
                case HideState.Walking:
                    return WalkToHide(out wantMove);
                case HideState.Entering:
                    if (Time.time < hidePhaseUntil) break;

                    EnterHidingSpot();
                    break;
                case HideState.Hidden:
                    StayHidden();
                    break;
                case HideState.Leaving:
                    if (Time.time < hidePhaseUntil) break;

                    StepOutOfHidingSpot();
                    break;
            }
            hasMoveTarget = false;
            return Vector3.zero;
        }

        /// <summary>
        /// The plan to the node, then the straight walk to the stand point in front of the doors.
        /// </summary>
        private Vector3 WalkToHide(out bool wantMove)
        {
            wantMove = false;
            if (hideSpot != null && hideSpot.Mounted)
            {
                AbandonHide("you got in first");
                return Vector3.zero;
            }
            if (Time.time > hidePhaseUntil)
            {
                AbandonHide("the walk there is taking too long");
                return Vector3.zero;
            }

            if (navPlan != null && navPathIndex < navPlan.Value.Count)
            {
                AdvancePastReachedWaypoints();
                if (navPathIndex < navPlan.Value.Count)
                {
                    wantMove = true;
                    currentMoveTarget = navPlan.Value[navPathIndex];
                    hasMoveTarget = true;
                    return HeadingAlongPlan() * (MoveSpeed * (hideFromFear ? FleeSpeedFactor : 1f));
                }
                DropPlan();
            }
            // The straight stretch starts at the node: a plan that ran out short of it (stuck recovery
            // skips waypoints) is planned again, or the walk cuts through a wall.
            Vector3 toNode = hideNode - transform.position;
            toNode.y = 0f;
            if (toNode.sqrMagnitude > ReachNodeArrival * ReachNodeArrival)
            {
                BuddyNodeGraph.NavPath? again = BuddyNodeGraph.FindPath(FloorUnderBuddy(), hideNode);
                if (again is not { Count: > 0 })
                {
                    AbandonHide($"the walk ended {toNode.magnitude:0.0}m short of its node and cannot be planned again");
                    return Vector3.zero;
                }
                navPlan = again;
                navPathIndex = 0;
                return Vector3.zero;
            }

            Vector3 toFace = hideFace - transform.position;
            toFace.y = 0f;
            if (toFace.sqrMagnitude <= SnackReachDist * SnackReachDist)
            {
                OpenContainerDoors(hideSpot!.GetComponentsInChildren<Door>(), hideOpened, hideName);
                hideState = HideState.Entering;
                hidePhaseUntil = Time.time + SnackOpenSeconds;
                return Vector3.zero;
            }

            Vector3 toStand = hideStand - transform.position;
            toStand.y = 0f;
            Vector3 target = toStand.sqrMagnitude > ReachStandArrival * ReachStandArrival ? hideStand : hideFace;
            Vector3 step = target - transform.position;
            step.y = 0f;
            if (step.sqrMagnitude < 0.0001f) return Vector3.zero;

            wantMove = true;
            currentMoveTarget = target;
            hasMoveTarget = true;
            return step.normalized * (MoveSpeed * (hideFromFear ? FleeSpeedFactor : 1f));
        }

        /// <summary>
        /// Into the spot as the game mounts a player - a teleport, because the buddy cannot walk into
        /// furniture - then the doors it opened are shut behind it.
        /// </summary>
        private void EnterHidingSpot()
        {
            HidingSpot spot = hideSpot!;
            if (spot.Mounted)
            {
                AbandonHide("you got in first");
                return;
            }
            hideStand = FloorUnderBuddy();
            TeleportTo(MountOriginFor(spot));
            // It cannot be walked out of; the doors and the floor are not its own any more.
            cc.enabled = false;
            hasMoveTarget = false;
            verticalVelocity = 0f;
            Vector3 @out = hideStand - transform.position;
            @out.y = 0f;
            if (@out.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(@out.normalized);

            CloseOpenedDoors(hideOpened, null, hideName);
            hideOpened.Clear();
            hideState = HideState.Hidden;
            hiddenSince = Time.time;
            hideDoorsShut = false;
            hideDoorsShutBy = Time.time + HideDoorShutSeconds;
            hideWaitLogAt = 0f;
            hideMonsterSeenAt = Time.time;
            hideLast = "hidden in " + hideName;
            YourBuddyPlugin.Log.LogInfo($"[fear] Hidden in {hideName}");
        }

        /// <summary>
        /// The game teleports a player's controller origin onto MountPos; the buddy's capsule sits at a
        /// different height, so the feet are matched instead.
        /// </summary>
        private Vector3 MountOriginFor(HidingSpot spot)
        {
            CharacterController? playerCc = playerCharacterController;
            float playerOriginToFeet = playerCc != null ? playerCc.center.y - playerCc.height * 0.5f : originToFeet;
            return spot.MountPos + Vector3.up * (playerOriginToFeet - originToFeet);
        }

        /// <summary>
        /// Waits behind shut doors: a frightened hide until the Breathless has gone, an ordered one
        /// until you say otherwise. A door opened from outside is the game's only way of finding it.
        /// docs/fear.md §6
        /// </summary>
        private void StayHidden()
        {
            if (!hideDoorsShut && !ConfirmHideDoorsShut()) return;

            if (AnyHideDoorOpen())
            {
                YourBuddyPlugin.Log.LogInfo($"[fear] Found in {hideName} - the door is open - getting out");
                // Whoever opened it is standing there; going straight back in is not hiding.
                // UpdateHide checked the spot is still there before it called this.
                hideSkips.Skip(hideSpot!.transform, HideSkipSeconds);
                LeaveHidingSpot("the door was opened", true);
                return;
            }
            float inside = Time.time - hiddenSince;
            // Hidden it cannot see out, but it can hear: the distance needs no sight line.
            // docs/invariants.md#a-hidden-buddy-waits-out-a-monster-it-can-hear
            bool monsterNear = monsterDist <= HideMonsterNearDist;

            // docs/invariants.md#an-ordered-hide-ends-only-on-an-order
            if (hideOrdered)
            {
                if (Time.time < hideWaitLogAt) return;

                hideWaitLogAt = Time.time + HideWaitLogSeconds;
                YourBuddyPlugin.Log.LogInfo($"[fear] Staying in {hideName} - you asked me to ({inside:0}s inside" +
                                            (monsterNear ? $", the Breathless is {monsterDist:0.0}m away" : "") + ")");
                return;
            }
            if (inside < HideMinSeconds) return;

            if (monsterNear && inside < HideMaxSeconds)
            {
                // docs/logging.md §4: a wait that can last must say so.
                if (Time.time < hideWaitLogAt) return;

                hideWaitLogAt = Time.time + HideWaitLogSeconds;
                YourBuddyPlugin.Log.LogInfo(
                    $"[fear] Staying in {hideName} - the Breathless is {monsterDist:0.0}m away ({inside:0}s inside)");
                return;
            }
            if (monsterNear)
            {
                YourBuddyPlugin.Log.LogInfo($"[fear] Coming out of {hideName} after {HideMaxSeconds:0}s - " +
                                            $"it is still {monsterDist:0.0}m away, but I cannot stay in here for ever");
            }
            else
            {
                if (fearState != FearState.Calm || Time.time - hideMonsterSeenAt < HideCalmSeconds) return;

                YourBuddyPlugin.Log.LogInfo($"[fear] Coming out of {hideName} - calm, and nothing seen for {HideCalmSeconds:0}s");
            }
            LeaveHidingSpot("it is over", false);
        }

        /// <summary>
        /// Door.Opened only turns false when the close animation ends, so for a second or two the
        /// buddy's own close reads exactly like a door opened from outside. Nothing counts as being
        /// found until the doors have been seen shut once. docs/fear.md §6
        /// </summary>
        private bool ConfirmHideDoorsShut()
        {
            if (!AnyHideDoorOpen())
            {
                hideDoorsShut = true;
                return true;
            }
            if (Time.time < hideDoorsShutBy) return false;

            // UpdateHide checked the spot is still there before it called this.
            YourBuddyPlugin.Log.LogInfo($"[fear] {hideName}'s doors are still open {HideDoorShutSeconds:0}s after " +
                                        "shutting them - getting out");
            hideSkips.Skip(hideSpot!.transform, HideSkipSeconds);
            LeaveHidingSpot("its doors would not shut");
            return false;
        }

        private bool AnyHideDoorOpen()
        {
            foreach (Door door in hideSpot!.GetComponentsInChildren<Door>())
            {
                if (door != null && door.Opened) return true;
            }
            return false;
        }

        /// <summary>
        /// Opens the doors, then steps out at the next tick (Leaving).
        /// </summary>
        private void LeaveHidingSpot(string why, bool stillAfraid = true)
        {
            hideStillAfraid = stillAfraid;
            hideLast = "left " + hideName + " - " + why;
            OpenContainerDoors(hideSpot!.GetComponentsInChildren<Door>(), hideOpened, hideName);
            hideState = HideState.Leaving;
            hidePhaseUntil = Time.time + SnackOpenSeconds;
        }

        /// <summary>
        /// Back to the floor point it came from, the doors it opened shut behind it, and the flee - or the
        /// decider - takes over again.
        /// </summary>
        private void StepOutOfHidingSpot()
        {
            if (!cc.enabled) cc.enabled = true;

            TeleportTo(hideStand - Vector3.up * originToFeet);
            verticalVelocity = 0f;
            CloseOpenedDoors(hideOpened, null, hideName);
            hideOpened.Clear();
            // Before EndHide, which may end the flee and log that it has.
            YourBuddyPlugin.Log.LogInfo($"[fear] Out of {hideName} again");
            EndHide();
        }

        /// <summary>
        /// Gives the buddy back: a flee retries its retreat at once, anything else decides again.
        /// </summary>
        private void EndHide()
        {
            bool retry = hideFromFear && hideStillAfraid;
            hideState = HideState.None;
            hideSpot = null;
            hideFromFear = false;
            hideOrdered = false;
            hideStillAfraid = true;
            hasMoveTarget = false;
            DropPlan();
            if (mode == BuddyMode.Flee)
            {
                if (retry)
                {
                    // Cut short - found, or it never got in. The flee still needs somewhere to go.
                    fleePhase = FleePhase.Retreat;
                    fleeRetryAt = 0f;
                }
                else
                {
                    // The hide did its job. UpdateFear would reach the same conclusion on its next
                    // tick, but UpdateFlee runs every frame and would have retreated into the next
                    // closet first. docs/invariants.md#a-hide-that-worked-ends-the-flee
                    EndFlee();
                }
            }
            decideAt = 0f;
        }

        /// <summary>
        /// Gave up before it was inside, or the spot went away: nothing to step out of.
        /// </summary>
        private void AbandonHide(string why)
        {
            YourBuddyPlugin.Log.LogInfo($"[fear] Not hiding in {hideName} - {why}");
            hideLast = "gave up on " + hideName + " - " + why;
            if (hideSpot != null) hideSkips.Skip(hideSpot.transform, HideSkipSeconds);

            CloseOpenedDoors(hideOpened, null, hideName);
            hideOpened.Clear();
            if (!cc.enabled) cc.enabled = true;

            EndHide();
        }

        /// <summary>
        /// Death, parking, a despawn, a ship rebuild, fear switched off: out at once, wherever it was.
        /// docs/invariants.md#a-snack-closes-what-it-opened
        /// </summary>
        internal void ForceLeaveHidingSpot(string why)
        {
            if (!Hiding) return;

            bool inside = hideState is HideState.Hidden or HideState.Leaving;
            if (cc != null && !cc.enabled) cc.enabled = true;

            if (inside && !IsDead && cc != null) TeleportTo(hideStand - Vector3.up * originToFeet);

            CloseOpenedDoors(hideOpened, null, hideName);
            hideOpened.Clear();
            YourBuddyPlugin.Log.LogInfo($"[fear] Out of {hideName} - {why}");
            EndHide();
        }

        /// <summary>
        /// buddy_hide, and the dialog's "hide": in now, whatever the fear, and it stays until the
        /// Breathless is not about or you give it another order. docs/fear.md §6
        /// </summary>
        internal string StartHideNow()
        {
            if (Hiding) return "Buddy is already hiding in " + hideName;

            // Hiding is the one thing you ask for under pressure: it takes the buddy off a chore.
            // docs/invariants.md#a-command-outranks-an-errand
            string? busy = BusyForCommand(true, true);
            if (busy != null) return busy;

            string? doing = reachTask != null ? DescribeReachTask() : null;

            if (!TryStartHide(false, out string report)) return "No hiding: " + report;

            // TryStartHide -> BeginHide dropped it, so only say so once the hide really started.
            if (doing != null) YourBuddyPlugin.Log.LogInfo($"[ai] Stopped {doing} - you asked me to hide");

            return "Buddy " + report;
        }

        /// <summary>
        /// For the HUD's Fear line and buddy_mind.
        /// </summary>
        internal string DescribeHide()
        {
            string last = hideLast != null ? " (last: " + hideLast + ")" : "";
            if (!YourBuddyPlugin.ConfigHideInClosets.Value) return "off - buddy_hide still works" + last;

            if (hideState == HideState.Hidden)
            {
                string why = hideOrdered ? ", you asked - and I stay until you say otherwise"
                    : monsterDist <= HideMonsterNearDist ? $", it is {monsterDist:0.0}m away" : "";
                return $"hidden in {hideName} {Time.time - hiddenSince:0}s{why}";
            }

            return Hiding ? hideState + " " + hideName + last : "not hiding" + last;
        }

        private static void TraceHide(string line)
        {
            if (YourBuddyPlugin.ConfigDebugLevel.Value >= 2) YourBuddyPlugin.Log.LogInfo("[fear] Hide: " + line);
        }
    }
}

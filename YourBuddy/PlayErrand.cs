using System.Collections.Generic;
using Space;
using UnityEngine;
using static YourBuddy.Items;

namespace YourBuddy
{
    /// <summary>
    /// Idle play with something loose: carry it a few metres and put it down, or throw it across the room.
    /// docs/items.md §5
    /// </summary>
    internal sealed class PlayErrand(IErrandBody body) : Errand(body)
    {
        private const float PlaySearchRadius = 20f;
        private const int PlayMaxPlans = 3;
        private const float PlayMinInterval = 60f;
        private const float PlayRetryDelay = 120f;
        private const float PlaySkipSeconds = 600f;
        /// <summary>
        /// A near carry puts the item down this far from where it lay, flat, at a node's floor in sight
        /// of it; a far one takes it out of the room, so no sight line is asked for. docs/items.md §5
        /// </summary>
        private const float PlayCarryMin = 3f;
        private const float PlayCarryMax = 6f;
        private const float PlayFarMin = 8f;
        private const float PlayFarMax = 25f;
        /// <summary>
        /// How the three games are drawn against one another: taking something right away is the rarest.
        /// </summary>
        private const int PlayCarryNearWeight = 3;
        private const int PlayCarryFarWeight = 1;
        private const int PlayThrowWeight = 3;
        /// <summary>
        /// Games played back to back before the buddy loses interest and the interval starts again.
        /// </summary>
        private const int PlayGamesMin = 1;
        private const int PlayGamesMax = 3;
        /// <summary>
        /// The spot is PlayDropOut in front of the node, and knee height must be clear to PlayDropClear.
        /// </summary>
        private const float PlayDropOut = 0.6f;
        private const float PlayDropClear = 0.9f;
        /// <summary>
        /// The spot's floor within this of the node's: not onto a step, bed or table.
        /// </summary>
        private const float PlayDropFlat = 0.1f;
        private const float PlayDropAimHeight = 0.3f;
        private const float PlayDropLift = 0.05f;
        private const float PlaySightHeight = 0.3f;
        /// <summary>
        /// Nothing is put down or tossed within this of an item zone or a trash can slot.
        /// </summary>
        private const float PlayMachineClearance = 1f;
        /// <summary>
        /// Holding it before the throw, then turning to face the way it goes: at most this long, or until
        /// the buddy is within PlayAimAngle of it.
        /// </summary>
        private const float PlayLiftSeconds = 0.5f;
        private const float PlayAimSeconds = 1f;
        private const float PlayAimAngle = 20f;
        /// <summary>
        /// The throw: the item's sphere must clear this far along the way, and it leaves the hands at this
        /// speed (the player's own throw is 10 m/s on a 1 kg item), tilted up.
        /// </summary>
        private const float PlayThrowClear = 2.5f;
        private const float PlayThrowSpeedMin = 5f;
        private const float PlayThrowSpeedMax = 9f;
        private const float PlayThrowUpSpeed = 1.6f;
        private const float PlayThrowLift = 0.1f;
        /// <summary>
        /// Throws per game, and how far the buddy will go to fetch what it threw. Nothing is left where it
        /// landed: the last throw is fetched and put down. docs/items.md §5
        /// </summary>
        private const int PlayThrowsMin = 1;
        private const int PlayThrowsMax = 3;
        private const float PlayFetchRadius = 12f;
        /// <summary>
        /// Flat directions tried for room to throw in, from where the buddy faces.
        /// </summary>
        private const int PlayThrowDirections = 8;
        /// <summary>
        /// Never thrown within this angle of the player when they are this near.
        /// </summary>
        private const float PlayThrowPlayerAngle = 35f;
        private const float PlayThrowPlayerDist = 8f;
        /// <summary>
        /// Setting an item down - put back or at a carry's spot - is lowered at this pace, not dropped.
        /// </summary>
        private const float PlayLowerSpeed = 0.8f;
        private const float PlayLowerArrival = 0.03f;
        private const float PlayLowerSeconds = 2f;
        private const float PlayWatchSeconds = 2f;

        private static readonly List<Grabbable> PlayItems = [];
        private static readonly List<Bounds> PlayMachineZones = [];
        private static readonly List<Vector3> NodeBuffer = [];
        private static readonly RaycastHit[] ThrowHits = new RaycastHit[32];

        public override bool Enabled => YourBuddyPlugin.ConfigItemPlay.Value;
        public override float Interval => Mathf.Max(PlayMinInterval, YourBuddyPlugin.ConfigItemPlayIntervalMinutes.Value * 60f);
        public override float RetryDelay => PlayRetryDelay;
        protected override float SkipSeconds => PlaySkipSeconds;
        protected override string Command => "buddy_play";

        private enum PlayKind { CarryNear, CarryFar, Throw }

        private enum PlayPhase { Walk, Lift, Aim, Lower, Watch }

        /// <summary>
        /// One bout of playing: a few games in a row, with a fresh item and a fresh game each time, so
        /// the buddy messes about for a while instead of moving one object every few minutes.
        /// docs/items.md §5
        /// </summary>
        private sealed class PlaySession
        {
            public readonly int MaxGames = Random.Range(PlayGamesMin, PlayGamesMax + 1);
            public int Games;

            public bool WantsMore => Games < MaxGames;
        }

        /// <summary>
        /// The item's leg, then for a carry a second task: the floor spot it is put down at.
        /// </summary>
        private sealed class PlayTask : ErrandLeg
        {
            private readonly PlayErrand errand;
            public readonly PlaySession Session;
            public readonly Grabbable Item;
            public readonly PlayKind Kind;
            public readonly string ItemLabel;
            /// <summary>
            /// Where the item lay: its collider centre and rotation, to put it back.
            /// </summary>
            public readonly Vector3 HomeCentre;
            private readonly Quaternion homeRotation;
            /// <summary>
            /// A carry's floor spot and the node it is put down from; the item's leg carries them to the spot's leg.
            /// </summary>
            public readonly Vector3 DropAt;
            private readonly Vector3 dropNode;
            public readonly bool ToSpot;
            public PlayPhase Phase = PlayPhase.Walk;
            public float PhaseUntil;
            /// <summary>
            /// Lower: the centre the held item is set down at, and the line logged once it is there.
            /// </summary>
            public Vector3 LowerTo;
            public string LowerDone = "";
            /// <summary>
            /// Aim: the flat direction the item is thrown, chosen when it was picked up, and the moment the
            /// throw may happen at the earliest (PhaseUntil is the latest, aimed or not).
            /// </summary>
            public Vector3 ThrowDir;
            public float ThrowAfter;
            /// <summary>
            /// Throws already made and how many this game has: the buddy fetches what it threw until the
            /// last one, which it picks up and puts down. docs/items.md §5
            /// </summary>
            public int Throws;
            public int MaxThrows = 1;

            public PlayTask(PlayErrand errand, PlaySession session, Grabbable item, PlayKind kind, Vector3 dropAt, Vector3 dropNode)
                : base(ItemTop(item), item.transform)
            {
                this.errand = errand;
                Session = session;
                Item = item;
                Kind = kind;
                ItemLabel = ItemLabelOf(item);
                HomeCentre = ColliderBounds(item.gameObject, out Bounds bounds) ? bounds.center : item.transform.position;
                homeRotation = item.transform.rotation;
                DropAt = dropAt;
                this.dropNode = dropNode;
            }

            /// <summary>
            /// The spot's leg, aimed a little above the floor so the eye's sight line does not end in it.
            /// </summary>
            public PlayTask(PlayTask from)
                : base(from.DropAt + Vector3.up * PlayDropAimHeight, from.Item.transform)
            {
                errand = from.errand;
                Session = from.Session;
                Item = from.Item;
                Kind = from.Kind;
                ItemLabel = from.ItemLabel;
                HomeCentre = from.HomeCentre;
                homeRotation = from.homeRotation;
                DropAt = from.DropAt;
                dropNode = from.dropNode;
                ToSpot = true;
                Node = from.dropNode;
                StandPoint = new Vector3(from.dropNode.x, NavProbe.FloorHeight(from.dropNode), from.dropNode.z);
            }

            public override string Name => ToSpot ? "the spot to put it down" : $"'{ItemLabel}'";
            public override float Reach => SnackReachDist;
            public override float[] StandOffs => SnackStandOffs;
            public override float ReachBelow => SnackReachBelow;

            public override void Defer(float seconds) => errand.Defer(Own, seconds);

            public override Vector3 Approach(out bool wantMove) => errand.Approach(this, out wantMove);

            public override void End() => errand.Body.Hands.Drop("stopped playing");

            public override string Describe() => Kind switch
            {
                PlayKind.Throw => $"throwing '{ItemLabel}' about",
                PlayKind.CarryFar when ToSpot => $"carrying '{ItemLabel}' to another room",
                _ when ToSpot => $"carrying '{ItemLabel}' somewhere else",
                _ => $"fetching '{ItemLabel}' to carry it about",
            };
        }

        public override bool TryStart(out string report) => TryStart(out report, null);

        /// <summary>
        /// The nearest loose trash it can walk to, with a game picked at random. `report` finishes a sentence.
        /// </summary>
        private bool TryStart(out string report, PlaySession? bout)
        {
            PlaySession session = bout ?? new PlaySession();
            CollectPlayItems(out int skipped);
            if (PlayItems.Count == 0)
            {
                report = $"there is nothing loose to play with within {PlaySearchRadius:0}m" +
                         (skipped > 0 ? $" ({skipped} I could not get to lately)" : "");
                return Failed(report);
            }

            string? failure = null;
            int plans = 0;
            foreach (Grabbable item in PlayItems)
            {
                PlayKind kind = PickPlayKind();
                Vector3 dropAt = Vector3.zero, dropNode = Vector3.zero;
                if (kind != PlayKind.Throw)
                {
                    bool far = kind == PlayKind.CarryFar;
                    float min = far ? PlayFarMin : PlayCarryMin;
                    float max = far ? PlayFarMax : PlayCarryMax;
                    if (!FindPlayDrop(item, min, max, !far, out dropAt, out dropNode))
                    {
                        Trace($"no spot {min:0}-{max:0}m from '{ItemLabelOf(item)}' " +
                              $"{(far ? "it could carry it to" : "in the same room to put it down")} - throwing it instead");
                        kind = PlayKind.Throw;
                    }
                }
                PlayTask task = new(this, session, item, kind, dropAt, dropNode)
                    { MaxThrows = Random.Range(PlayThrowsMin, PlayThrowsMax + 1) };
                if (Body.InReach(task))
                {
                    task.Node = task.StandPoint = Here;
                    Begin(task, null);
                    report = $"is {task.Describe()}, right here";
                    YourBuddyPlugin.Log.LogInfo($"[mind] Decided: play with '{task.ItemLabel}' - {PlayKindName(kind)}, right here");
                    return true;
                }
                if (plans++ >= PlayMaxPlans) break;

                failure = Body.PlanReach(task, out BuddyNodeGraph.NavPath plan);
                if (failure != null)
                {
                    Skips.Skip(item.transform, PlaySkipSeconds);
                    Trace($"not '{task.ItemLabel}' at {task.TargetPoint:0.0}: {failure} - skipping it for {PlaySkipSeconds:0}s");
                    continue;
                }

                Begin(task, plan);
                float distance = Vector3.Distance(Here, task.TargetPoint);
                report = $"is {task.Describe()}, {distance:0.0}m away";
                YourBuddyPlugin.Log.LogInfo($"[mind] Decided: play with '{task.ItemLabel}' - {PlayKindName(kind)}, {distance:0.0}m away, " +
                                            $"via node {task.Node:0.0}" + (kind != PlayKind.Throw ? $", to put it down at {dropAt:0.0}" : ""));
                return true;
            }
            report = $"none of the {PlayItems.Count} loose item(s) nearby can be walked to ({failure})";
            return Failed(report);
        }

        public override int Count(out float nearest)
        {
            CollectPlayItems(out _);
            nearest = Nearest(PlayItems.Count, i => PlayItems[i].transform.position);
            return PlayItems.Count;
        }

        private static string PlayKindName(PlayKind kind) => kind switch
        {
            PlayKind.CarryNear => "carry it somewhere else",
            PlayKind.CarryFar => "carry it to another room",
            _ => "throw it across the room",
        };

        /// <summary>
        /// Which game this time, drawn against the weights: taking something right out of the room is
        /// the rare one, because it is the long walk. docs/items.md §5
        /// </summary>
        private static PlayKind PickPlayKind()
        {
            int roll = Random.Range(0, PlayCarryNearWeight + PlayCarryFarWeight + PlayThrowWeight);
            if (roll < PlayCarryNearWeight) return PlayKind.CarryNear;

            return roll < PlayCarryNearWeight + PlayCarryFarWeight ? PlayKind.CarryFar : PlayKind.Throw;
        }

        /// <summary>
        /// A throw is never the end: the buddy walks to where the item landed and picks it up again, until
        /// the last throw of the game, which it sets down there. docs/items.md §5
        /// </summary>
        private void FetchThrown(PlayTask task)
        {
            string? blocker = TakeBlocker(task.Item);
            if (blocker == null && FlatDistanceSq(task.Item.transform.position, Here) > PlayFetchRadius * PlayFetchRadius)
            {
                blocker = $"it landed more than {PlayFetchRadius:0}m away";
            }
            PlayTask? fetch = blocker == null
                ? new PlayTask(this, task.Session, task.Item, PlayKind.Throw, Vector3.zero, Vector3.zero)
                    { Throws = task.Throws, MaxThrows = task.MaxThrows }
                : null;
            if (fetch != null && Body.InReach(fetch))
            {
                fetch.Node = fetch.StandPoint = Here;
                Body.Walk(fetch, null);
                Last = $"fetching '{task.ItemLabel}' back";
                return;
            }
            string? failure = blocker;
            if (fetch != null)
            {
                failure = Body.PlanReach(fetch, out BuddyNodeGraph.NavPath plan);
                if (failure == null)
                {
                    Body.Walk(fetch, plan);
                    Last = $"fetching '{task.ItemLabel}' back";
                    return;
                }
            }
            Skips.Skip(task.Item.transform, PlaySkipSeconds);
            Done(task, $"threw '{task.ItemLabel}' and could not fetch it back - {failure}");
        }

        /// <summary>
        /// A null plan is an item already in reach. Only a game played to the end schedules the full interval.
        /// </summary>
        private void Begin(PlayTask task, BuddyNodeGraph.NavPath? plan)
        {
            Body.Walk(task, plan);
            DueAt = Time.time + PlayRetryDelay;
            Last = "on the way to '" + task.ItemLabel + "'";
        }

        /// <summary>
        /// What the buddy may play with, nearest first: never in a container, in hands, loaded into a machine,
        /// or beside one. Trash only, unless ItemPlayAnything is on. docs/items.md §5
        /// </summary>
        private void CollectPlayItems(out int skipped)
        {
            PlayItems.Clear();
            PutAwayItems.Clear();
            Skips.Prune();
            skipped = 0;
            Vector3 here = Here;
            float floorY = Body.FloorUnderBuddy().y;

            CollectContainerContents(here, PlaySearchRadius + SnackContainerMargin);
            foreach (ItemDetector detector in SceneScan.ThisFrame<ItemDetector>()) PutAwayItems.UnionWith(detector.Items);
            CollectMachineZones();

            foreach (Grabbable item in SceneScan.ThisFrame<Grabbable>())
            {
                if (PutAwayItems.Contains(item) || TakeBlocker(item) != null || !IsPlaything(item)) continue;

                Vector3 point = ItemTop(item);
                if (FlatDistanceSq(point, here) > PlaySearchRadius * PlaySearchRadius ||
                    point.y < floorY - SnackReachBelow || point.y > floorY + ReachTask.ReachHeight ||
                    NearMachine(point) || !Body.OnMyVessel(item.transform)) continue;

                if (Skips.Has(item.transform)) skipped++;
                else PlayItems.Add(item);
            }
            PutAwayItems.Clear();

            PlayItems.Sort((a, b) => FlatDistanceSq(a.transform.position, here).CompareTo(FlatDistanceSq(b.transform.position, here)));
            ShuffleNearest(PlayItems);
        }

        /// <summary>
        /// Every item zone and trash can slot, grown by PlayMachineClearance: nothing is picked up, put down
        /// or thrown into one. docs/items.md §5
        /// </summary>
        private static void CollectMachineZones()
        {
            PlayMachineZones.Clear();
            foreach (ItemDetector detector in SceneScan.ThisFrame<ItemDetector>()) AddMachineZone(detector.gameObject);
            foreach (ItemDestroyer slot in SceneScan.ThisFrame<ItemDestroyer>()) AddMachineZone(slot.gameObject);
        }

        private static void AddMachineZone(GameObject zone)
        {
            if (!zone.TryGetComponent(out Collider collider) || !collider.enabled) return;

            Bounds bounds = collider.bounds;
            bounds.Expand(PlayMachineClearance * 2f);
            PlayMachineZones.Add(bounds);
        }

        private static bool NearMachine(Vector3 point)
        {
            foreach (Bounds zone in PlayMachineZones)
            {
                if (zone.Contains(point)) return true;
            }
            return false;
        }

        /// <summary>
        /// A floor spot `minDist`-`maxDist` from the item, in front of an active node on the buddy's level
        /// and clear at knee height. `requireSight` adds a clear line from where the item lies - "the same
        /// room"; a far carry drops it and lets the graph route. docs/items.md §5
        /// </summary>
        private bool FindPlayDrop(Grabbable item, float minDist, float maxDist, bool requireSight,
                                  out Vector3 dropAt, out Vector3 dropNode)
        {
            dropAt = dropNode = Vector3.zero;
            Vector3 itemTop = ItemTop(item);
            Vector3 itemEye = itemTop + Vector3.up * PlaySightHeight;
            float floorY = Body.FloorUnderBuddy().y;
            BuddyNodeGraph.CollectActiveNodes(NodeBuffer);
            int count = NodeBuffer.Count;
            if (count == 0) return false;

            // Random start, so the same item is not always carried to the same node.
            int start = Random.Range(0, count);
            for (int i = 0; i < count; i++)
            {
                Vector3 node = NodeBuffer[(start + i) % count];
                float flatSq = FlatDistanceSq(node, itemTop);
                if (flatSq < minDist * minDist || flatSq > maxDist * maxDist) continue;

                float nodeFloor = NavProbe.FloorHeight(node);
                if (Mathf.Abs(nodeFloor - floorY) > BuddyBehaviour.FollowSameLevelDeltaY) continue;

                float angle = Random.Range(0f, 360f);
                for (int turn = 0; turn < 4; turn++, angle += 90f)
                {
                    Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                    Vector3 clear = new(node.x + dir.x * PlayDropClear, nodeFloor, node.z + dir.z * PlayDropClear);
                    if (!NavProbe.WalkLos(node, clear, BuddyBehaviour.FollowSameLevelDeltaY)) continue;

                    Vector3 spot = new(node.x + dir.x * PlayDropOut, node.y, node.z + dir.z * PlayDropOut);
                    spot.y = NavProbe.FloorHeight(spot);
                    // Not onto a step, a bed or a table.
                    if (Mathf.Abs(spot.y - nodeFloor) > PlayDropFlat || NearMachine(spot)) continue;

                    if (requireSight &&
                        !NavProbe.CanSee(itemEye, spot + Vector3.up * PlaySightHeight, item.transform, out _)) continue;

                    dropAt = spot;
                    dropNode = node;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// UpdateRoute, once a play leg's plan is walked.
        /// </summary>
        private Vector3 Approach(PlayTask task, out bool wantMove)
        {
            wantMove = false;
            string? blocker = LegBlocker(task);
            if (blocker != null)
            {
                Last = $"left '{task.ItemLabel}' - {blocker}";
                YourBuddyPlugin.Log.LogInfo($"[ai] Leaving '{task.ItemLabel}' - {blocker}");
                Body.FinishRoute();
                return Vector3.zero;
            }

            switch (task.Phase)
            {
                case PlayPhase.Walk:
                    if (!Body.StepIntoReach(task, out Vector3 move, out wantMove)) return move;

                    task.Phase = PlayPhase.Lift;
                    task.PhaseUntil = Time.time + (task.ToSpot ? TidyAimSeconds : TidyReachSeconds);
                    break;
                case PlayPhase.Lift:
                    if (Time.time < task.PhaseUntil) break;

                    if (task.ToSpot)
                    {
                        float carried = Vector3.Distance(task.HomeCentre, task.DropAt);
                        Lower(task, task.DropAt + Vector3.up * (Body.Hands.Extents.y + PlayDropLift), null,
                              $"carried '{task.ItemLabel}' {carried:0.0}m and put it down");
                        break;
                    }
                    return PickUp(task);
                case PlayPhase.Aim:
                    Body.StandFacing(Here + task.ThrowDir);
                    if (Time.time < task.ThrowAfter) return Vector3.zero;

                    if (Time.time < task.PhaseUntil &&
                        Vector3.Angle(Body.Transform.forward, task.ThrowDir) > PlayAimAngle) return Vector3.zero;

                    Throw(task);
                    return Vector3.zero;
                case PlayPhase.Lower:
                    if (Body.Hands.DistanceTo(task.LowerTo) > PlayLowerArrival && Time.time < task.PhaseUntil) break;

                    Body.Hands.PutDown(task.LowerTo, Vector3.zero);
                    Done(task, task.LowerDone);
                    return Vector3.zero;
                case PlayPhase.Watch:
                    if (Time.time < task.PhaseUntil)
                    {
                        Body.StandFacing(task.Item != null ? task.Item.transform.position : Here);
                        return Vector3.zero;
                    }
                    if (task.Throws < task.MaxThrows) FetchThrown(task);
                    else Done(task, $"threw '{task.ItemLabel}' across the room {task.Throws} time(s)");
                    return Vector3.zero;
            }
            Body.StandFacing(task.TargetPoint);
            return Vector3.zero;
        }

        /// <summary>
        /// Checked at every step of a leg. Watching a thrown item needs nothing: it is not the buddy's any more.
        /// </summary>
        private string? LegBlocker(PlayTask task)
        {
            if (task.Phase == PlayPhase.Watch) return null;

            if (task.ToSpot || task.Phase is PlayPhase.Aim or PlayPhase.Lower) return Body.Hands.Item != task.Item ? "it is no longer in my hands" : null;

            string? item = TakeBlocker(task.Item);
            if (item != null) return item;

            return FlatDistanceSq(ItemTop(task.Item), task.TargetPoint) > SnackItemMovedDist * SnackItemMovedDist ? "it has been moved" : null;
        }

        /// <summary>
        /// Picks the item up; a carry sets off for its spot in the same Route, a throw turns to face its way.
        /// </summary>
        private Vector3 PickUp(PlayTask task)
        {
            if (Body.Hands.Item != task.Item && !Body.Hands.PickUp(task.Item))
            {
                Skips.Skip(task.Item.transform, PlaySkipSeconds);
                Last = $"left '{task.ItemLabel}' - I could not pick it up";
                YourBuddyPlugin.Log.LogInfo($"[ai] Leaving '{task.ItemLabel}' - I could not pick it up");
                Body.FinishRoute();
                return Vector3.zero;
            }
            YourBuddyPlugin.Log.LogInfo($"[ai] Picked up '{task.ItemLabel}'" + (task.Throws > 0 ? " again" : " to play with it"));
            if (task.Kind == PlayKind.Throw)
            {
                // A throw needs no spot at all.
                // The last throw of the game is not thrown: it is put down where it landed, so nothing is left lost.
                if (task.Throws >= task.MaxThrows)
                {
                    Lower(task, Body.GroundPos(Body.Hands.Extents.y + PlayDropLift) + Body.Transform.forward * Body.Hands.Forward, null,
                          $"threw '{task.ItemLabel}' {task.Throws} time(s), then put it down");
                    return Vector3.zero;
                }
                AimThrow(task, PlayLiftSeconds);
                return Vector3.zero;
            }

            PlayTask toSpot = new(task);
            if (Body.InReach(toSpot))
            {
                toSpot.Node = toSpot.StandPoint = Here;
                toSpot.Phase = PlayPhase.Lift;
                toSpot.PhaseUntil = Time.time + TidyLiftSeconds;
                Body.Walk(toSpot, null);
                return Vector3.zero;
            }
            BuddyNodeGraph.NavPath? plan = BuddyNodeGraph.FindPath(Body.FloorUnderBuddy(), toSpot.Node);
            if (plan is not { Count: > 0 })
            {
                YourBuddyPlugin.Log.LogInfo($"[ai] Cannot carry '{task.ItemLabel}' to {task.DropAt:0.0} - there is no path to it");
                AimThrow(task, 0f);
                return Vector3.zero;
            }
            // Straight from leg to leg: ending this one would put the item down.
            Body.Walk(toSpot, plan.Value);
            Last = $"carrying '{task.ItemLabel}' somewhere else";
            return Vector3.zero;
        }

        /// <summary>
        /// Holds the item `lift` seconds, turning to face where it is going, then throws.
        /// </summary>
        private void AimThrow(PlayTask task, float lift)
        {
            task.ThrowDir = ThrowDirection();
            task.Phase = PlayPhase.Aim;
            task.ThrowAfter = Time.time + lift;
            task.PhaseUntil = task.ThrowAfter + PlayAimSeconds;
        }

        /// <summary>
        /// Let go at the hands, flat out and tilted up, as the player's own throw does. Short of room the
        /// item still leaves the hands - just not far.
        /// </summary>
        private void Throw(PlayTask task)
        {
            Vector3 dir = task.ThrowDir.sqrMagnitude > 0.01f ? task.ThrowDir.normalized : Body.Transform.forward;
            float room = ThrowRoom(dir);
            float speed = Mathf.Lerp(PlayThrowSpeedMin, Random.Range(PlayThrowSpeedMin, PlayThrowSpeedMax),
                                     Mathf.Clamp01(room / PlayThrowClear));
            Body.Hands.PutDown(Body.Hands.Point + Vector3.up * PlayThrowLift, dir * speed + Vector3.up * PlayThrowUpSpeed);
            task.Throws++;
            YourBuddyPlugin.Log.LogInfo($"[ai] Threw '{task.ItemLabel}' at {speed:0.0}m/s ({room:0.0}m clear ahead), " +
                                        $"throw {task.Throws} of {task.MaxThrows}");
            task.Phase = PlayPhase.Watch;
            task.PhaseUntil = Time.time + PlayWatchSeconds;
        }

        /// <summary>
        /// The clearest flat direction to throw in, tried at random from where the buddy faces: never at the
        /// player nearby, never into a machine. Its own facing when nothing is clear.
        /// </summary>
        private Vector3 ThrowDirection()
        {
            Vector3 toPlayer = Vector3.zero;
            Player? player = Body.PilotPlayer();
            if (player != null && player.Controller != null)
            {
                toPlayer = player.Controller.CachedTransform.position - Here;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > PlayThrowPlayerDist * PlayThrowPlayerDist) toPlayer = Vector3.zero;
            }
            Vector3 forward = Body.Transform.forward;
            Vector3 best = forward;
            float bestRoom = -1f;
            float turn = Random.Range(0f, 360f);
            for (int i = 0; i < PlayThrowDirections; i++, turn += 360f / PlayThrowDirections)
            {
                Vector3 dir = Quaternion.Euler(0f, turn, 0f) * forward;
                if (toPlayer != Vector3.zero && Vector3.Angle(dir, toPlayer) < PlayThrowPlayerAngle) continue;

                float room = ThrowRoom(dir);
                if (room <= bestRoom) continue;

                best = dir;
                bestRoom = room;
                if (room >= PlayThrowClear) break;
            }
            return best;
        }

        /// <summary>
        /// How far the item's sphere gets along `dir` before it meets anything but the buddy or itself,
        /// capped at PlayThrowClear; a machine ahead counts as no room at all.
        /// </summary>
        private float ThrowRoom(Vector3 dir)
        {
            Vector3 extents = Body.Hands.Extents;
            float radius = Mathf.Max(0.02f, Mathf.Min(extents.x, Mathf.Min(extents.y, extents.z)));
            Vector3 from = Body.Hands.Point;
            if (NearMachine(from + dir * (PlayThrowClear * 0.5f)) || NearMachine(from + dir * PlayThrowClear)) return 0f;

            float room = PlayThrowClear;
            int count = Physics.SphereCastNonAlloc(from, radius, dir, ThrowHits, PlayThrowClear, NavProbe.ProbeLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = ThrowHits[i].collider;
                if (hit == null || hit.transform.IsChildOf(Body.Transform)) continue;
                if (Body.Hands.Item != null && hit.transform.IsChildOf(Body.Hands.Item.transform)) continue;

                room = Mathf.Min(room, ThrowHits[i].distance);
            }
            return room;
        }

        /// <summary>
        /// Sets the held item down at `centre` at a slow pace, turned to `rotation` if given; put down once it is there.
        /// </summary>
        private void Lower(PlayTask task, Vector3 centre, Quaternion? rotation, string done)
        {
            if (rotation.HasValue) Body.Hands.Turn(rotation.Value);
            Body.Hands.ReachTo(centre, PlayLowerSpeed);
            task.LowerTo = centre;
            task.LowerDone = done;
            task.Phase = PlayPhase.Lower;
            task.PhaseUntil = Time.time + PlayLowerSeconds;
        }

        /// <summary>
        /// One game over. The session may want another, with a fresh item and a fresh game - straight on,
        /// because FinishRoute would end the whole bout. The hands are empty at every call site.
        /// </summary>
        private void Done(PlayTask task, string what)
        {
            PlaySession session = task.Session;
            session.Games++;
            Last = what;
            YourBuddyPlugin.Log.LogInfo($"[ai] Playing: {what} (game {session.Games} of {session.MaxGames})");
            if (session.WantsMore && TryStart(out _, session)) return;

            DueAt = Time.time + NextInterval();
            Last = session.Games > 1 ? $"{what} - {session.Games} games" : what;
            Body.FinishRoute();
        }

        private static void Trace(string line)
        {
            if (YourBuddyPlugin.ConfigDebugLevel.Value >= 2) YourBuddyPlugin.Log.LogInfo("[mind] Play: " + line);
        }
    }
}

using System.Collections.Generic;
using Space;
using UnityEngine;
using static YourBuddy.Items;

namespace YourBuddy
{
    /// <summary>
    /// A trash box a full trash can dropped: carried to a sell station, loaded, and sold with the station's
    /// own button, the money going to the player. docs/items.md §4
    /// </summary>
    internal sealed class SellErrand(IErrandBody body) : Errand(body)
    {
        /// <summary>
        /// Grabbable.signature of what a full TrashCan drops: canSell, not canTrash.
        /// </summary>
        private const string TrashBoxSignature = "TRASH_BOX";
        /// <summary>
        /// Between looks for a trash box, and after a sale; trash boxes are rare, so no config interval.
        /// </summary>
        private const float SellCheckInterval = 60f;
        private const float SellSkipSeconds = 600f;
        /// <summary>
        /// A station or box that merely did not work out this time, as against one there is structurally
        /// no way to: a walk worth trying again must not blind the buddy to the station for SellSkipSeconds.
        /// </summary>
        private const float SellRetrySeconds = 90f;
        private const float SellSearchRadius = 30f;
        /// <summary>
        /// A sell station further than this from the box is not carried to.
        /// </summary>
        private const float SellStationRadius = 80f;
        private const int SellMaxPlans = 3;
        /// <summary>
        /// Boxes loaded before each press - the game's Sell pays for everything in the zone at once - and
        /// how many items the zone may end up holding. A run with more boxes loads again. docs/items.md
        /// </summary>
        private const int SellMaxBoxes = 4;
        private const int SellZoneMaxItems = 4;
        /// <summary>
        /// The transit stations' sales area (Oxygen, Solar, Fuel) is smaller than the Shipyard's: fewer boxes at once.
        /// </summary>
        private const int SellMaxBoxesTransit = 2;
        private static readonly float[] SellLoadStandOffs = [1.35f, 1.6f];
        private const float SellLoadReach = 1.8f;
        /// <summary>
        /// Slots for boxes in the zone: clear of its walls, apart from each other, and let go this high above the floor or the box below.
        /// </summary>
        private const float SellSlotMargin = 0.02f;
        private const float SellSlotGap = 0.02f;
        private const float SellDropHeight = 0.03f;
        /// <summary>
        /// Loading: held in the zone until the ItemDetector lists it, at most this long; then let go and left to settle.
        /// </summary>
        private const float SellLoadSeconds = 1.5f;
        /// <summary>
        /// The held box's centre this near its drop point counts as there.
        /// </summary>
        private const float SellDropArrival = 0.05f;
        private const float SellSettleSeconds = 1f;
        /// <summary>
        /// Waiting for the gate to open, or for the player to step out of the station, before giving up.
        /// </summary>
        private const float SellWaitSeconds = 20f;
        /// <summary>
        /// Sell runs on the gate's OnClosed. Open again this long after the press with the box still there: the close failed.
        /// </summary>
        private const float SellReopenedAfter = 1.5f;
        private const float SellConfirmSeconds = 10f;

        private static readonly List<SellCandidate> SellCandidates = [];
        private static readonly List<SellStationParts> SellStations = [];

        public override bool Enabled => YourBuddyPlugin.ConfigSellTrash.Value;
        public override float Interval => SellCheckInterval;
        public override float RetryDelay => SellCheckInterval;
        protected override float SkipSeconds => SellSkipSeconds;
        // A walk that did not work out is worth trying again; only a structural failure (no node
        // with a clear walk) earns the long skip that TryStart and StartLeg hand out.
        protected override float DeferSkipSeconds => SellRetrySeconds;
        protected override string Command => "buddy_sell";
        protected override string NextLabel => "next look in";

        private enum SellLeg { Box, Load, Button }

        private enum SellPhase { Walk, Handle, Hold, Settle, Pressed }

        /// <summary>
        /// A sell station, read once: where items go, who would be caught inside, its gate and button.
        /// </summary>
        private sealed class SellStationParts(SellStation station, ItemDetector detector, BoxCollider zone,
                                              InstantItemDetector? catchZone, Gate gate, Space.Button button)
        {
            public readonly SellStation Station = station;
            public readonly ItemDetector Detector = detector;
            public readonly BoxCollider Zone = zone;
            /// <summary>
            /// A player inside when the gate closes dies (SellStation.Sell). Null on a SellTerminal.
            /// </summary>
            public readonly InstantItemDetector? CatchZone = catchZone;
            public readonly Gate Gate = gate;
            public readonly Space.Button Button = button;

            public bool Usable => Station != null && Station.isActiveAndEnabled && Zone != null && Button != null && Gate != null;

            public Vector3 LoadPoint => Zone.bounds.center;

            public Vector3 ButtonPoint => ColliderBounds(Button.gameObject, out Bounds bounds) ? bounds.center : Button.transform.position;

            /// <summary>
            /// Flat, with SellStationClearance around the item zone: under the gate, or where a player would be caught.
            /// </summary>
            public bool Covers(Vector3 point)
            {
                Bounds bounds = Zone.bounds;
                const float clearance = SellPens.SellStationClearance;
                return point.x > bounds.min.x - clearance && point.x < bounds.max.x + clearance &&
                       point.z > bounds.min.z - clearance && point.z < bounds.max.z + clearance &&
                       point.y > bounds.min.y - ReachTask.ReachHeight && point.y < bounds.max.y;
            }

            public bool Holds(Grabbable item) => Detector.Items.Contains(item);

            /// <summary>
            /// Boxes per press: SellMaxBoxes at the Shipyard, fewer at the transit stations, whose stations live
            /// under a root named for them (StaticObjects/OxygenStationParts, ...); the Shipyard's is ShipyardStationParts.
            /// </summary>
            public int MaxBoxes => InShipyard() ? SellMaxBoxes : SellMaxBoxesTransit;

            /// <summary>
            /// Boxes still to fetch before the next press: MaxBoxes, less what the zone already holds.
            /// </summary>
            public int Room() => Mathf.Min(MaxBoxes, SellZoneMaxItems - Detector.Items.Count);

            private bool InShipyard()
            {
                for (Transform? t = Station.transform; t != null; t = t.parent)
                {
                    if (t.name.StartsWith("ShipyardStationParts")) return true;
                }
                return false;
            }

            /// <summary>
            /// The box stands in the cage whether or not the ItemDetector noticed it: its list only
            /// grows on a trigger crossing, and SellStation.Sell clears it outright. docs/items.md §4
            /// </summary>
            private bool Encloses(Grabbable item)
            {
                if (item == null) return false;

                Vector3 point = ColliderBounds(item.gameObject, out Bounds bounds) ? bounds.center : item.transform.position;
                return Zone.bounds.Contains(point);
            }

            /// <summary>
            /// Already the station's business, so never something to fetch. docs/items.md §4
            /// </summary>
            public bool HasBox(Grabbable item) => Holds(item) || Encloses(item);

            /// <summary>
            /// Something SellStation.Sell would sell that is not a trash box: the player's own things.
            /// </summary>
            public Grabbable? OtherSellable()
            {
                foreach (Grabbable item in Detector.Items)
                {
                    if (item != null && item is { CanSell: true, Enabled: true } && item.Signature != TrashBoxSignature) return item;
                }
                return null;
            }

            public static SellStationParts? Read(SellStation station)
            {
                ItemDetector? detector = GameInternals.SellStationAccess.GetItemDetector(station);
                Gate? gate = GameInternals.SellStationAccess.GetGate(station);
                Space.Button? button = GameInternals.SellStationAccess.GetSellButton(station);
                if (detector == null || gate == null || button == null || !detector.TryGetComponent(out BoxCollider zone)) return null;

                return new SellStationParts(station, detector, zone, GameInternals.SellStationAccess.GetInstantDetector(station), gate, button);
            }
        }

        private readonly struct SellCandidate(Grabbable box, SellStationParts? loadedIn)
        {
            public readonly Grabbable Box = box;
            /// <summary>
            /// The station the box is already loaded into, or null for a box lying about.
            /// </summary>
            public readonly SellStationParts? LoadedIn = loadedIn;
        }

        /// <summary>
        /// One selling run: every trash box the buddy means to put into this station, loaded one after
        /// another, a press whenever the zone is full, and again for the rest. docs/items.md §4
        /// </summary>
        private sealed class SellRun(SellStationParts parts)
        {
            public readonly SellStationParts Parts = parts;
            /// <summary>
            /// Boxes still to fetch, nearest first, wherever they lie now; and the ones in the zone for this press.
            /// docs/invariants.md#a-selling-run-keeps-its-boxes
            /// </summary>
            public readonly List<Grabbable> Queue = [];
            public readonly List<Grabbable> Loaded = [];
            /// <summary>
            /// How many Loaded may hold before the press: what the zone had room for, plus what was in it.
            /// </summary>
            public int Capacity;
            /// <summary>
            /// The box being fetched or carried; null once every one of them is loaded.
            /// </summary>
            public Grabbable? Current;
            public int CashBefore;
            public float PressedAt;
            /// <summary>
            /// Boxes already picked up and put down again once, because they lay across the zone's edge
            /// or the station never listed them. One retry each, so the run always ends.
            /// </summary>
            public readonly HashSet<Grabbable> Reseated = [];

            public bool Full => Loaded.Count >= Capacity;

            public int Left => Queue.Count + (Current != null ? 1 : 0);

            /// <summary>
            /// After a sale: Sell emptied the zone and its list, so the next load starts from nothing.
            /// </summary>
            public void NextLoad()
            {
                Loaded.Clear();
                Reseated.Clear();
                Capacity = Parts.Room();
            }

            /// <summary>
            /// How many of the loaded boxes the zone still lists: what a press would actually sell.
            /// </summary>
            public int InZone()
            {
                int count = 0;
                foreach (Grabbable box in Loaded)
                {
                    if (box != null && box.gameObject.activeInHierarchy && Parts.Holds(box)) count++;
                }
                return count;
            }

            /// <summary>
            /// Loaded boxes still standing in the cage at all, listed or not: what the run can still
            /// act on, because an unlisted one is re-seated rather than given up. docs/items.md §4
            /// </summary>
            public int InCage()
            {
                int count = 0;
                foreach (Grabbable box in Loaded)
                {
                    if (box != null && box.gameObject.activeInHierarchy && Parts.HasBox(box)) count++;
                }
                return count;
            }

            public static string Count(int boxes) => boxes == 1 ? "the trash box" : boxes + " trash boxes";
        }

        /// <summary>
        /// One leg of a run: to a box, to the station's item zone holding it, or to its button.
        /// </summary>
        private sealed class SellTask(SellErrand errand, SellRun run, SellLeg leg)
            : ErrandLeg(PointOf(run, leg), OwnOf(run, leg))
        {
            public readonly SellRun Run = run;
            public readonly SellLeg Leg = leg;
            public SellPhase Phase = SellPhase.Walk;
            public float PhaseUntil;
            /// <summary>
            /// Since when it waits for the gate to open or the player to step out; below zero when not waiting.
            /// </summary>
            public float WaitingSince = -1f;
            /// <summary>
            /// Where the held box is going in the item zone; it is let go only once there.
            /// </summary>
            public Vector3 DropPoint;
            /// <summary>
            /// How the box lies when let go: square to the zone, whichever way the buddy turns meanwhile.
            /// </summary>
            public Quaternion DropRotation = Quaternion.identity;

            public SellStationParts Parts => Run.Parts;
            /// <summary>
            /// The box this leg is about. Only a Box or Load leg has one - Run.Current is set before either.
            /// </summary>
            public Grabbable Box => Run.Current!;

            private static Vector3 PointOf(SellRun run, SellLeg leg) => leg switch
            {
                SellLeg.Box => ItemTop(run.Current!),
                SellLeg.Load => run.Parts.LoadPoint,
                _ => run.Parts.ButtonPoint,
            };

            // The button sits in the station's own console (SellStation/BottomPart/Button), whose mesh
            // collider stands between an eye at ReachEyeHeight and the button itself: a station must
            // not block the sight of its own panel. docs/items.md §4
            private static Transform OwnOf(SellRun run, SellLeg leg) => leg switch
            {
                SellLeg.Box => run.Current!.transform,
                _ => run.Parts.Station.transform,
            };

            public override string Name => Leg switch
            {
                SellLeg.Box => "the trash box",
                SellLeg.Load => "the sell station",
                _ => "the sell station's button",
            };

            public override float Reach => Leg switch
            {
                SellLeg.Box => SnackReachDist,
                SellLeg.Load => SellLoadReach,
                _ => ReachDist,
            };

            public override float[] StandOffs => Leg switch
            {
                SellLeg.Box => SnackStandOffs,
                SellLeg.Load => SellLoadStandOffs,
                _ => ReachStandOffs,
            };

            public override float ReachBelow => Leg == SellLeg.Box ? SnackReachBelow : 0f;

            // docs/invariants.md#the-buddy-sells-only-trash-boxes. Its own pen is exempt - these legs
            // are what the station is for - but another station's still bars a stand point.
            public override bool StandAllowed(Vector3 point) =>
                (Leg == SellLeg.Box || !Parts.Covers(point)) &&
                !SellPens.InAFencedPen(point, Parts.Station, SellPens.SellStationClearance);

            public override void Defer(float seconds) => errand.Defer(Own, seconds);

            public override bool Recover(string why) => errand.RecoverRun(this, why);

            // The whole run: its station and every box it means to sell. docs/invariants.md#one-buddy-per-target
            public override bool Holds(Transform t)
            {
                if (t == Own || (Parts.Station != null && t == Parts.Station.transform)) return true;
                if (Run.Current != null && Run.Current.transform == t) return true;

                foreach (Grabbable box in Run.Queue)
                {
                    if (box != null && box.transform == t) return true;
                }
                foreach (Grabbable box in Run.Loaded)
                {
                    if (box != null && box.transform == t) return true;
                }
                return false;
            }

            public override Vector3 Approach(out bool wantMove) => errand.Approach(this, out wantMove);

            // A box in its hands is put down however the run ended.
            public override void End() => errand.Body.Hands.Drop("stopped selling");

            public override string Describe() => Leg switch
            {
                SellLeg.Box => "fetching a trash box to sell",
                SellLeg.Load => "carrying a trash box to the sell station",
                _ => "selling " + SellRun.Count(Run.Loaded.Count),
            };
        }

        /// <summary>
        /// The nearest trash box with a sell station it can take it to. `report` finishes a sentence.
        /// </summary>
        public override bool TryStart(out string report)
        {
            if (Body.PilotPlayer() == null)
            {
                report = "there is nobody to pay";
                return Failed(report);
            }
            CollectSellCandidates(out int skipped);
            if (SellCandidates.Count == 0)
            {
                report = $"there is no trash box within {SellSearchRadius:0}m" + (skipped > 0 ? $" ({skipped} I could not deal with lately)" : "");
                return Failed(report);
            }

            string? failure = null;
            int plans = 0;
            foreach (SellCandidate candidate in SellCandidates)
            {
                SellStationParts? parts = candidate.LoadedIn ?? NearestSellStation(candidate.Box.transform.position, out failure);
                if (parts == null) continue;

                if (Body.TakenByAnother(parts.Station.transform))
                {
                    failure = "another buddy is selling there";
                    continue;
                }

                Grabbable? other = parts.OtherSellable();
                if (other != null)
                {
                    failure = $"your '{ItemLabelOf(other)}' is in the sell station - I would sell it too";
                    continue;
                }
                // Every box this station can take in one trip: one press sells the lot. docs/items.md §4
                SellRun run = BuildSellRun(parts, candidate.Box);
                // Its zone is already as full as the buddy will make it, and nothing of ours is in it.
                if (run.Current == null && run.Loaded.Count == 0)
                {
                    failure = "the sell station is already full";
                    continue;
                }
                // Every leg's end is checked before setting off; only the first is walked now.
                if (!Body.HasReachNode(new SellTask(this, run, SellLeg.Button)) ||
                    (run.Current != null && !Body.HasReachNode(new SellTask(this, run, SellLeg.Load))))
                {
                    Skips.Skip(parts.Station.transform, SellSkipSeconds);
                    failure = "no nav node near the sell station has a clear walk to it from outside its gate";
                    Trace($"not the sell station at {parts.LoadPoint:0.0}: {failure} - skipping it for {SellSkipSeconds:0}s");
                    continue;
                }

                SellTask task = new(this, run, run.Current != null ? SellLeg.Box : SellLeg.Button);
                string what = DescribeSellRun(run);
                if (Body.InReach(task))
                {
                    task.Node = task.StandPoint = Here;
                    Begin(task, null);
                    report = $"is selling {what}, right here";
                    YourBuddyPlugin.Log.LogInfo($"[mind] Decided: sell {what} - right here");
                    return true;
                }
                if (plans++ >= SellMaxPlans) break;

                failure = Body.PlanReach(task, out BuddyNodeGraph.NavPath plan);
                if (failure != null)
                {
                    Skips.Skip(task.Own, SellSkipSeconds);
                    Trace($"not {task.Name} at {task.TargetPoint:0.0}: {failure} - skipping it for {SellSkipSeconds:0}s");
                    continue;
                }

                Begin(task, plan);
                float distance = Vector3.Distance(Here, task.TargetPoint);
                report = $"is selling {what}, {distance:0.0}m away";
                YourBuddyPlugin.Log.LogInfo($"[mind] Decided: sell {what} - {distance:0.0}m away, via node " +
                                            $"{task.Node:0.0}, at the sell station at {parts.LoadPoint:0.0}");
                return true;
            }
            report = $"none of the {SellCandidates.Count} trash box(es) nearby can be sold ({failure})";
            return Failed(report);
        }

        public override int Count(out float nearest)
        {
            CollectSellCandidates(out _);
            nearest = Nearest(SellCandidates.Count, i => SellCandidates[i].Box.transform.position);
            return SellCandidates.Count;
        }

        /// <summary>
        /// `first` and every other candidate box for this station. The first load is capped by the room
        /// its zone has left; the rest wait for the next. Already-loaded boxes need no trip. docs/items.md §4
        /// </summary>
        private static SellRun BuildSellRun(SellStationParts parts, Grabbable first)
        {
            SellRun run = new(parts);
            foreach (SellCandidate other in SellCandidates)
            {
                if (other.Box == null) continue;

                if (parts.HasBox(other.Box))
                {
                    run.Loaded.Add(other.Box);
                    continue;
                }
                // Another station's box, or one too far from this one to be worth carrying here.
                if (other.LoadedIn != null ||
                    (other.Box != first &&
                     (other.Box.transform.position - parts.LoadPoint).sqrMagnitude > SellStationRadius * SellStationRadius))
                {
                    continue;
                }
                run.Queue.Add(other.Box);
            }
            // The box the decider chose leads, whatever order the collector left them in.
            if (run.Queue.Remove(first)) run.Queue.Insert(0, first);

            run.Capacity = run.Loaded.Count + parts.Room();
            if (run.Queue.Count > 0 && !run.Full)
            {
                run.Current = run.Queue[0];
                run.Queue.RemoveAt(0);
            }
            return run;
        }

        private static string DescribeSellRun(SellRun run)
        {
            int total = run.Left + run.Loaded.Count;
            string what = SellRun.Count(total);
            what = run.Left == 0 ? what + ", already loaded"
                : run.Loaded.Count == 0 ? what + ", lying about"
                : $"{what}, {run.Loaded.Count} already loaded";
            return total > run.Capacity ? $"{what}, {Mathf.Max(1, run.Capacity)} at a time" : what;
        }

        private void Begin(SellTask task, BuddyNodeGraph.NavPath? plan)
        {
            Body.Walk(task, plan);
            DueAt = Time.time + SellCheckInterval;
            Last = "on the way to " + task.Name;
        }

        /// <summary>
        /// Trash boxes near the buddy, nearest first: lying about, or already loaded into a sell station.
        /// Never one in a container, in hands, or loaded into another machine.
        /// </summary>
        private void CollectSellCandidates(out int skipped)
        {
            SellCandidates.Clear();
            SellStations.Clear();
            PutAwayItems.Clear();
            Skips.Prune();
            skipped = 0;
            Vector3 here = Here;
            float floorY = Body.FloorUnderBuddy().y;

            foreach (SellStation station in SceneScan.ThisFrame<SellStation>())
            {
                SellStationParts? parts = SellStationParts.Read(station);
                if (parts is { Usable: true }) SellStations.Add(parts);
            }
            CollectContainerContents(here, SellSearchRadius + SnackContainerMargin);
            foreach (ItemDetector detector in SceneScan.ThisFrame<ItemDetector>())
            {
                if (SellStationOf(detector) == null) PutAwayItems.UnionWith(detector.Items);
            }

            foreach (Grabbable item in SceneScan.ThisFrame<Grabbable>())
            {
                if (item.Signature != TrashBoxSignature || !item.CanSell || PutAwayItems.Contains(item) || TakeBlocker(item) != null) continue;

                Vector3 point = ItemTop(item);
                if (FlatDistanceSq(point, here) > SellSearchRadius * SellSearchRadius ||
                    point.y < floorY - SnackReachBelow || point.y > floorY + ReachTask.ReachHeight ||
                    !Body.OnMyVessel(item.transform)) continue;

                if (Skips.Has(item.transform))
                {
                    skipped++;
                    continue;
                }
                SellStationParts? loadedIn = null;
                foreach (SellStationParts parts in SellStations)
                {
                    if (parts.HasBox(item)) loadedIn = parts;
                }
                SellCandidates.Add(new SellCandidate(item, loadedIn));
            }
            PutAwayItems.Clear();

            SellCandidates.Sort((a, b) => FlatDistanceSq(a.Box.transform.position, here).CompareTo(FlatDistanceSq(b.Box.transform.position, here)));
            ShuffleNearest(SellCandidates);
        }

        private static SellStationParts? SellStationOf(ItemDetector detector)
        {
            foreach (SellStationParts parts in SellStations)
            {
                if (parts.Detector == detector) return parts;
            }
            return null;
        }

        private SellStationParts? NearestSellStation(Vector3 from, out string? failure)
        {
            SellStationParts? best = null;
            SellStationParts? left = null;
            float bestSq = SellStationRadius * SellStationRadius;
            float leftSq = bestSq;
            bool taken = false;
            foreach (SellStationParts parts in SellStations)
            {
                float distSq = (parts.LoadPoint - from).sqrMagnitude;
                // One run per station: another buddy's press would sell this one's boxes too.
                if (distSq < bestSq && Body.TakenByAnother(parts.Station.transform))
                {
                    taken = true;
                    continue;
                }
                if (Skips.Has(parts.Station.transform))
                {
                    if (distSq >= leftSq) continue;

                    left = parts;
                    leftSq = distSq;
                    continue;
                }
                if (distSq >= bestSq) continue;

                best = parts;
                bestSq = distSq;
            }
            // docs/logging.md §4: "there is no sell station" and "I am not using that one yet" are
            // different answers, and the second one is the one that looks like a bug from outside.
            failure = best != null ? null
                : taken ? "another buddy is selling there"
                : left != null
                    ? $"the sell station {Mathf.Sqrt(leftSq):0.0}m away is being left out for another " +
                      $"{Skips.Remaining(left.Station.transform):0}s"
                    : $"no sell station within {SellStationRadius:0}m of it";
            return best;
        }

        /// <summary>
        /// UpdateRoute, once a sell leg's plan is walked.
        /// </summary>
        private Vector3 Approach(SellTask task, out bool wantMove)
        {
            wantMove = false;
            if (task.Phase == SellPhase.Pressed)
            {
                ConfirmSale(task);
                return Face(task);
            }
            string? blocker = LegBlocker(task);
            if (blocker != null)
            {
                // Only the station going away ends the run; one box's trouble is one box's.
                if (task.Leg == SellLeg.Button || !task.Parts.Usable) End(task, blocker, 0f);
                else AbandonBox(task, $"leaving the trash box - {blocker}", 0f);

                return Vector3.zero;
            }

            switch (task.Phase)
            {
                case SellPhase.Walk:
                    if (!Body.StepIntoReach(task, out Vector3 move, out wantMove)) return move;

                    task.Phase = SellPhase.Handle;
                    task.PhaseUntil = Time.time + (task.Leg == SellLeg.Box ? TidyReachSeconds : TidyAimSeconds);
                    break;
                case SellPhase.Handle:
                    // In reach of a box whose room the player walked out of: docs/invariants.md#a-selling-run-keeps-its-boxes
                    if (task.Leg == SellLeg.Box && InUnloadedRoom(task.Box))
                    {
                        Body.LoadRoomOf(task.Box.transform);
                        if (InUnloadedRoom(task.Box))
                        {
                            AbandonBox(task, "leaving the trash box - its room will not load", SellRetrySeconds);
                            return Vector3.zero;
                        }
                        YourBuddyPlugin.Log.LogInfo("[ai] Selling: loaded the trash box's room, which was switched off");
                    }
                    if (Time.time < task.PhaseUntil) break;

                    if (task.Leg == SellLeg.Box)
                    {
                        if (!Body.Hands.PickUp(task.Box))
                        {
                            AbandonBox(task, "leaving the trash box - I could not pick it up", SellSkipSeconds);
                            return Vector3.zero;
                        }
                        YourBuddyPlugin.Log.LogInfo("[ai] Picked up the trash box" +
                                                    (task.Run.Queue.Count > 0 ? $" ({task.Run.Queue.Count} more to fetch)" : ""));
                        NextLeg(task, SellLeg.Load);
                        return Vector3.zero;
                    }
                    if (task.Leg == SellLeg.Load)
                    {
                        if (!WaitForStation(task, task.Parts.Gate.FullyOpened ? null : "its gate to open")) return Face(task);

                        if (!LoadPlacement(task, out Vector3 dropPoint, out Quaternion dropRotation))
                        {
                            AbandonBox(task, "leaving the trash box - there is no room for it in the sell station", SellRetrySeconds);
                            return Vector3.zero;
                        }
                        task.DropPoint = dropPoint;
                        task.DropRotation = dropRotation;
                        Body.Hands.Turn(dropRotation);
                        Body.Hands.ReachTo(task.DropPoint);
                        task.Phase = SellPhase.Hold;
                        task.PhaseUntil = Time.time + SellLoadSeconds;
                        break;
                    }
                    PressButton(task);
                    break;
                case SellPhase.Hold:
                    Body.Hands.Turn(task.DropRotation);
                    // Listed from the first touch of the zone's edge: let go only at the drop point, or
                    // the box can rest across the edge, under the closing gate's AntiCrasher.
                    if (task.Parts.Holds(task.Box) && Body.Hands.DistanceTo(task.DropPoint) < SellDropArrival)
                    {
                        // Its colliders are already on, so the zone keeps it.
                        Body.Hands.Release();
                        YourBuddyPlugin.Log.LogInfo("[ai] Loaded the trash box into the sell station");
                        task.Phase = SellPhase.Settle;
                        task.PhaseUntil = Time.time + SellSettleSeconds;
                        break;
                    }
                    if (Time.time < task.PhaseUntil) break;

                    Body.Hands.ReachTo(null);
                    AbandonBox(task, "could not get the trash box into the sell station", SellSkipSeconds);
                    return Vector3.zero;
                case SellPhase.Settle:
                    if (Time.time < task.PhaseUntil) break;

                    // Loaded. Fetch the next box, or press for the lot once the zone is full. docs/items.md §4
                    if (task.Run.Current != null) task.Run.Loaded.Add(task.Run.Current);

                    task.Run.Current = null;
                    if (!TryNextBox(task)) NextLeg(task, SellLeg.Button);

                    return Vector3.zero;
            }
            return Face(task);
        }

        private Vector3 Face(SellTask task)
        {
            Body.StandFacing(task.TargetPoint);
            return Vector3.zero;
        }

        /// <summary>
        /// Checked at every step of a leg until the button is pressed.
        /// </summary>
        private string? LegBlocker(SellTask task)
        {
            string? station = StationBlocker(task.Parts);
            if (station != null) return station;

            switch (task.Leg)
            {
                case SellLeg.Box:
                    return FetchBlocker(task.Box, task.Phase is SellPhase.Walk or SellPhase.Handle) ??
                           (FlatDistanceSq(ItemTop(task.Box), task.TargetPoint) > SnackItemMovedDist * SnackItemMovedDist ? "it has been moved" : null);
                case SellLeg.Load:
                    // Settling, it has been let go on purpose.
                    if (task.Phase == SellPhase.Settle) return task.Parts.Holds(task.Box) ? null : "it fell out of the sell station";

                    return Body.Hands.Item != task.Box ? "it is no longer in my hands" : null;
                default:
                    // Any box still in the cage is worth the walk - PressButton re-seats one the
                    // station has not listed. All of them gone is not.
                    return task.Phase != SellPhase.Walk && task.Run.InCage() == 0
                        ? "there is nothing left in the sell station"
                        : null;
            }
        }

        /// <summary>
        /// Where the box is let go and how it lies: upright and square to the zone, in the first free slot
        /// of a grid of box footprints (stacked when the zone is tall enough). A box across the zone's edge or
        /// at an angle is not sold - the transit stations' zone is a 0.5 m cube, a box 0.375 x 0.25 m. docs/items.md §4
        /// </summary>
        private bool LoadPlacement(SellTask task, out Vector3 point, out Quaternion rotation)
        {
            Bounds zone = task.Parts.Zone.bounds;
            Vector3 size = BoxSize(task.Box);
            // Long side along x (yaw 0) or along z (yaw 90): whichever the zone takes more boxes in.
            int cellsX = Mathf.FloorToInt((zone.size.x - 2f * SellSlotMargin + SellSlotGap) / (size.x + SellSlotGap));
            int cellsZ = Mathf.FloorToInt((zone.size.z - 2f * SellSlotMargin + SellSlotGap) / (size.z + SellSlotGap));
            int turnedX = Mathf.FloorToInt((zone.size.x - 2f * SellSlotMargin + SellSlotGap) / (size.z + SellSlotGap));
            int turnedZ = Mathf.FloorToInt((zone.size.z - 2f * SellSlotMargin + SellSlotGap) / (size.x + SellSlotGap));
            bool turned = turnedX * turnedZ > cellsX * cellsZ;
            rotation = Quaternion.Euler(0f, turned ? 90f : 0f, 0f);
            Vector3 footprint = turned ? new Vector3(size.z, size.y, size.x) : size;
            int columns = turned ? turnedX : cellsX;
            int rows = turned ? turnedZ : cellsZ;
            int layers = Mathf.FloorToInt((zone.size.y + SellSlotGap) / (size.y + SellSlotGap));
            point = default;
            if (columns < 1 || rows < 1 || layers < 1) return false;

            // Free of the boxes already in the zone, the nearest to the buddy first: lowest layer, then the short reach.
            List<Bounds> taken = [];
            foreach (Grabbable other in task.Run.Loaded) AddTaken(taken, other, task.Box);
            foreach (Grabbable other in task.Parts.Detector.Items) AddTaken(taken, other, task.Box);

            float bestReach = float.MaxValue;
            bool found = false;
            for (int layer = 0; layer < layers && !found; layer++)
            {
                for (int column = 0; column < columns; column++)
                {
                    for (int row = 0; row < rows; row++)
                    {
                        Vector3 centre = new(
                            zone.center.x + (column - (columns - 1) * 0.5f) * (footprint.x + SellSlotGap),
                            zone.min.y + footprint.y * 0.5f + layer * (footprint.y + SellSlotGap) + SellDropHeight,
                            zone.center.z + (row - (rows - 1) * 0.5f) * (footprint.z + SellSlotGap));
                        Bounds slot = new(centre, footprint - Vector3.one * 0.02f);
                        if (taken.Exists(other => other.Intersects(slot))) continue;

                        float reach = FlatDistanceSq(centre, Here);
                        if (reach >= bestReach) continue;

                        bestReach = reach;
                        point = centre;
                        found = true;
                    }
                }
            }
            return found;
        }

        private static void AddTaken(List<Bounds> taken, Grabbable other, Grabbable self)
        {
            if (other == null || other == self || !ColliderBounds(other.gameObject, out Bounds bounds)) return;

            taken.Add(bounds);
        }

        /// <summary>
        /// The box's own size, turned or not: its collider's, scaled.
        /// </summary>
        private static Vector3 BoxSize(Grabbable box)
        {
            BoxCollider? collider = box.GetComponentInChildren<BoxCollider>();
            if (collider != null)
            {
                Vector3 scale = collider.transform.lossyScale;
                return new Vector3(collider.size.x * Mathf.Abs(scale.x), collider.size.y * Mathf.Abs(scale.y), collider.size.z * Mathf.Abs(scale.z));
            }
            return ColliderBounds(box.gameObject, out Bounds bounds) ? bounds.size : new Vector3(0.375f, 0.1875f, 0.25f);
        }

        /// <summary>
        /// True when nothing is in the way. Otherwise waits up to SellWaitSeconds, saying once what for, then gives up.
        /// </summary>
        private bool WaitForStation(SellTask task, string? waitingFor)
        {
            if (waitingFor == null)
            {
                task.WaitingSince = -1f;
                return true;
            }
            if (task.WaitingSince < 0f)
            {
                task.WaitingSince = Time.time;
                YourBuddyPlugin.Log.LogInfo($"[ai] Waiting at the sell station for {waitingFor}");
                return false;
            }
            if (Time.time - task.WaitingSince > SellWaitSeconds)
            {
                string left = task.Leg == SellLeg.Load ? "" : " - the trash box stays loaded";
                End(task, $"gave up waiting {SellWaitSeconds:0}s for {waitingFor}{left}", SellRetrySeconds);
            }
            return false;
        }

        /// <summary>
        /// The station's own button, as the player presses it: SellStation.Close, then Sell on the gate's OnClosed
        /// pays the player. docs/invariants.md#the-buddy-sells-only-trash-boxes
        /// </summary>
        private void PressButton(SellTask task)
        {
            SellStationParts parts = task.Parts;
            Player? player = Body.PilotPlayer();
            if (player == null)
            {
                End(task, "there is nobody to pay - the trash box stays loaded", 0f);
                return;
            }
            Grabbable? other = parts.OtherSellable();
            if (other != null)
            {
                End(task, $"your '{ItemLabelOf(other)}' is in the sell station too - the trash box stays loaded, you sell", SellSkipSeconds);
                return;
            }
            SellRun run = task.Run;
            Grabbable? strayBox = BoxAstray(run, out string astray);
            if (strayBox != null)
            {
                // It would be under the closing gate: this one must be moved before any press.
                if (run.Reseated.Add(strayBox))
                {
                    YourBuddyPlugin.Log.LogInfo($"[ai] A trash box lies {astray} - loading it again");
                    run.Loaded.Remove(strayBox);
                    run.Current = strayBox;
                    NextLeg(task, SellLeg.Box);
                    return;
                }
                End(task, $"a trash box still lies {astray} after loading it again - " +
                          $"leaving {SellRun.Count(run.Loaded.Count)} loaded", SellRetrySeconds);
                return;
            }
            // In the cage, but the detector never listed it: SellStation.Sell pays for its own list only.
            // Each box is loaded again once; the loop drops the ones that will not list, so it ends.
            while (BoxUnlisted(run) is { } unlisted)
            {
                if (run.Reseated.Add(unlisted))
                {
                    YourBuddyPlugin.Log.LogInfo("[ai] The sell station has not noticed a trash box in it - loading it again");
                    run.Loaded.Remove(unlisted);
                    run.Current = unlisted;
                    NextLeg(task, SellLeg.Box);
                    return;
                }
                run.Loaded.Remove(unlisted);
                YourBuddyPlugin.Log.LogInfo("[ai] The sell station still has not noticed a trash box in it - " +
                                            "leaving that one out of this sale");
            }
            if (run.Loaded.Count == 0)
            {
                End(task, "the sell station has not noticed any of the trash boxes in it", SellRetrySeconds);
                return;
            }
            string? waitingFor = !parts.Gate.FullyOpened ? "its gate to open"
                : parts.CatchZone != null && parts.CatchZone.GetPlayerInZone() != null ? "you to step out of it"
                : Body.AnotherBuddyWhere(parts.Covers) ? "the other buddy to step out of it"
                : null;
            if (!WaitForStation(task, waitingFor) || Body.Leg != task) return;

            run.CashBefore = player.CashSystem.Cash;
            run.PressedAt = Time.time;
            task.Phase = SellPhase.Pressed;
            task.PhaseUntil = Time.time + SellConfirmSeconds;
            parts.Button.Interact(player);
            YourBuddyPlugin.Log.LogInfo($"[ai] Pressed the sell station's button for {SellRun.Count(run.InZone())}");
        }

        private void ConfirmSale(SellTask task)
        {
            SellRun run = task.Run;
            // Sell destroys what it sold, and the press sells everything in the zone at once.
            int sold = 0;
            foreach (Grabbable box in run.Loaded)
            {
                if (box == null || !box.gameObject.activeInHierarchy) sold++;
            }
            if (sold > 0 && sold == run.Loaded.Count)
            {
                Player? player = Body.PilotPlayer();
                int earned = player != null ? player.CashSystem.Cash - run.CashBefore : 0;
                DueAt = Time.time + SellCheckInterval;
                string result = $"sold {SellRun.Count(sold)} for {earned}";
                Last = result;
                YourBuddyPlugin.Log.LogInfo($"[ai] Sold {SellRun.Count(sold)} for {earned}" +
                                            (run.Queue.Count > 0 ? $" - {run.Queue.Count} more of this run to fetch" : ""));
                // The run's own boxes first, however far or on whichever vessel they lie from here:
                // docs/invariants.md#a-selling-run-keeps-its-boxes. Walk replaces this leg, as tidying
                // chains its pieces; FinishRoute would end the errand.
                if (run.Queue.Count > 0)
                {
                    run.NextLoad();
                    if (TryNextBox(task))
                    {
                        YourBuddyPlugin.Log.LogInfo($"[ai] Selling: another load - {SellRun.Count(run.Left)} left, " +
                                                    $"{run.Capacity} at a time");
                        return;
                    }
                }
                // Boxes the run did not know of: a new run from here.
                if (TryStart(out string next))
                {
                    YourBuddyPlugin.Log.LogInfo("[ai] Selling: more trash boxes about - another trip");
                    return;
                }
                Trace($"no further trip: {next}");
                Last = result;
                Body.FinishRoute();
                return;
            }
            if (Time.time - run.PressedAt > SellReopenedAfter && task.Parts.Gate.FullyOpened)
            {
                End(task, "the sell station opened again without selling - something is under its gate (" +
                          GateSurroundings(task) + ")", SellRetrySeconds);
                return;
            }
            if (Time.time >= task.PhaseUntil)
            {
                End(task, "the sell station did not sell " + SellRun.Count(run.Loaded.Count), SellRetrySeconds);
            }
        }

        /// <summary>
        /// The first loaded box not wholly inside the item zone, flat, and how far out it lies: the closing
        /// gate comes down on that one. Null when they all sit properly inside.
        /// </summary>
        private static Grabbable? BoxAstray(SellRun run, out string astray)
        {
            astray = "";
            Bounds zone = run.Parts.Zone.bounds;
            foreach (Grabbable candidate in run.Loaded)
            {
                if (candidate == null || !ColliderBounds(candidate.gameObject, out Bounds box)) continue;

                float overX = Mathf.Max(zone.min.x - box.min.x, box.max.x - zone.max.x);
                float overZ = Mathf.Max(zone.min.z - box.min.z, box.max.z - zone.max.z);
                float over = Mathf.Max(overX, overZ);
                if (over <= 0f) continue;

                astray = $"{over:0.00}m across the sell station's edge";
                return candidate;
            }
            return null;
        }

        /// <summary>
        /// The first loaded box standing in the zone that the ItemDetector does not list, so a press
        /// would walk past it. Its list only grows on a trigger crossing and Sell clears it outright.
        /// </summary>
        private static Grabbable? BoxUnlisted(SellRun run)
        {
            foreach (Grabbable candidate in run.Loaded)
            {
                if (candidate == null || !candidate.gameObject.activeInHierarchy) continue;

                if (!run.Parts.Holds(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>
        /// For a gate that opened again: what may have been under it.
        /// </summary>
        private string GateSurroundings(SellTask task)
        {
            Bounds zone = task.Parts.Zone.bounds;
            // UNT0007: a Unity object comparison, not a null-coalescing one.
            Grabbable? worst = BoxAstray(task.Run, out _);
            if (worst == null && task.Run.Loaded.Count > 0) worst = task.Run.Loaded[0];
            string box = worst != null && ColliderBounds(worst.gameObject, out Bounds bounds)
                ? $"box {bounds.center - zone.center:0.00} half {bounds.extents:0.00}"
                : "box unmeasured";
            Player? player = Body.PilotPlayer();
            string you = player != null && task.Parts.CatchZone != null && task.Parts.CatchZone.GetPlayerInZone() == player
                ? ", you inside"
                : "";
            return $"zone half {zone.extents:0.00}, {box}, buddy {Here - zone.center:0.00}{you}";
        }

        /// <summary>
        /// One box of a run could not be fetched or loaded. The run goes on to the next box, or presses
        /// for whatever is already in the zone; only a run with nothing loaded ends here. Boxes already
        /// put away must not be forgotten because a later one went wrong. docs/items.md §4
        /// </summary>
        private void AbandonBox(SellTask task, string why, float skipSeconds)
        {
            SellRun run = task.Run;
            Grabbable? box = run.Current;
            if (box != null && skipSeconds > 0f) Skips.Skip(box.transform, skipSeconds);

            // The next leg replaces this one without ending it: empty the hands here.
            if (Body.Hands.Item != null && Body.Hands.Item == box) Body.Hands.Drop(why);

            run.Current = null;
            int loaded = run.InCage();
            if (TryNextBox(task))
            {
                Last = why;
                YourBuddyPlugin.Log.LogInfo($"[ai] Selling: {why} - fetching the next one instead");
                return;
            }
            if (loaded > 0)
            {
                Last = why;
                YourBuddyPlugin.Log.LogInfo($"[ai] Selling: {why} - selling {SellRun.Count(loaded)} already loaded anyway");
                NextLeg(task, SellLeg.Button);
                return;
            }
            End(task, why, 0f);
        }

        /// <summary>
        /// ReachTask.Recover: the walk to a box or to the item zone gave up. True means selling took the
        /// failure over and the caller must not end the route as well.
        /// </summary>
        private bool RecoverRun(SellTask task, string why)
        {
            if (task.Leg == SellLeg.Button) return false;

            // StepIntoReach already called Defer, which skipped whatever the leg was about.
            AbandonBox(task, why, 0f);
            return true;
        }

        /// <summary>
        /// A new task for the next leg, replacing this one directly: ending it would put the box down.
        /// </summary>
        private void NextLeg(SellTask task, SellLeg leg)
        {
            // Its zone and button are measured now: a collider switched off with its room has no bounds.
            string? station = leg == SellLeg.Box ? null : StationBlocker(task.Parts);
            if (station != null)
            {
                End(task, station, 0f);
                return;
            }
            SellTask next = new(this, task.Run, leg);
            if (StartLeg(next)) return;

            End(task, $"cannot get to {next.Name} - there is no way to it", SellSkipSeconds);
        }

        /// <summary>
        /// The next box of the run, saying why any it leaves out cannot be fetched. False when the zone is
        /// full or none is left, and the run should go and press the button. docs/items.md §4
        /// </summary>
        private bool TryNextBox(SellTask task)
        {
            SellRun run = task.Run;
            run.Current = null;
            while (run.Queue.Count > 0)
            {
                if (run.Full)
                {
                    YourBuddyPlugin.Log.LogInfo($"[ai] Selling: the sell station is full with {SellRun.Count(run.Loaded.Count)} - " +
                                                $"selling those, then {run.Queue.Count} more");
                    return false;
                }
                Grabbable box = run.Queue[0];
                run.Queue.RemoveAt(0);
                if (box == null)
                {
                    YourBuddyPlugin.Log.LogInfo("[ai] Selling: leaving a trash box out of this run - it is gone");
                    continue;
                }
                if (run.Parts.HasBox(box))
                {
                    // Someone loaded it meanwhile: this press sells it too.
                    if (!run.Loaded.Contains(box)) run.Loaded.Add(box);
                    continue;
                }
                string? blocker = FetchBlocker(box, true);
                if (blocker != null)
                {
                    YourBuddyPlugin.Log.LogInfo($"[ai] Selling: leaving the trash box at {box.transform.position:0.0} out of this run - {blocker}");
                    continue;
                }
                run.Current = box;
                if (StartLeg(new SellTask(this, run, SellLeg.Box))) return true;

                YourBuddyPlugin.Log.LogInfo($"[ai] Selling: leaving the trash box at {ItemTop(box):0.0} out of this run - " +
                                            $"there is no way to it{(InUnloadedRoom(box) ? " (its room is not loaded)" : "")}");
            }
            run.Current = null;
            return false;
        }

        /// <summary>
        /// Null when the station can be used. The game switches its room off once nobody is in it; the run
        /// holds the station, so that room is loaded again. Undocked, the room cannot load: gone.
        /// docs/invariants.md#a-selling-run-keeps-its-boxes
        /// </summary>
        private string? StationBlocker(SellStationParts parts)
        {
            if (parts.Usable) return null;

            if (parts.Station == null || !parts.Station.gameObject.activeSelf || parts.Station.gameObject.activeInHierarchy)
            {
                return "the sell station is gone";
            }
            Body.LoadRoomOf(parts.Station.transform);
            if (!parts.Usable) return "the sell station is gone - its room will not load";

            YourBuddyPlugin.Log.LogInfo("[ai] Selling: loaded the sell station's room, which was switched off");
            return null;
        }

        /// <summary>
        /// TakeBlocker, except that until it is picked up a box whose room is merely unloaded is still worth
        /// the walk: in reach, the buddy loads it. docs/items.md#4-selling-trash-boxes
        /// </summary>
        private string? FetchBlocker(Grabbable box, bool beforePickUp) =>
            beforePickUp && InUnloadedRoom(box) ? null : TakeBlocker(box);

        /// <summary>
        /// Puts `next` in hand as the current leg, walking to it when it is not already in reach.
        /// False when it cannot be planned to; nothing is changed then.
        /// </summary>
        private bool StartLeg(SellTask next)
        {
            if (Body.InReach(next))
            {
                next.Node = next.StandPoint = Here;
                next.Phase = SellPhase.Handle;
                next.PhaseUntil = Time.time + TidyAimSeconds;
                Body.Walk(next, null);
                Last = next.Describe();
                return true;
            }
            if (Body.PlanReach(next, out BuddyNodeGraph.NavPath plan) != null) return false;

            Body.Walk(next, plan);
            Last = next.Describe();
            return true;
        }

        /// <summary>
        /// Ends selling with a reason; a box in its hands is put down by the leg's End. `skipSeconds` above zero leaves the station out that long.
        /// </summary>
        private void End(SellTask task, string why, float skipSeconds)
        {
            if (skipSeconds > 0f) Skips.Skip(task.Parts.Station.transform, skipSeconds);
            Last = why;
            YourBuddyPlugin.Log.LogInfo("[ai] Selling: " + why);
            Body.FinishRoute();
        }

        private static void Trace(string line)
        {
            if (YourBuddyPlugin.ConfigDebugLevel.Value >= 2) YourBuddyPlugin.Log.LogInfo("[mind] Sell: " + line);
        }
    }
}

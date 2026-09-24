using System.Collections.Generic;
using UnityEngine;
using static YourBuddy.Items;

namespace YourBuddy
{
    /// <summary>
    /// Now and then, with nothing else to do: pick up a piece of trash - lying about, or from a fridge,
    /// cabinet, chest or locker, opened and closed again - carry it to a trash can and put it in through
    /// the slot. docs/items.md §3
    /// </summary>
    internal sealed class TidyErrand(IErrandBody body) : Errand(body)
    {
        private const float TidySearchRadius = 30f;
        /// <summary>
        /// A trash can further than this from the item is not carried to.
        /// </summary>
        private const float TidyBinRadius = 40f;
        private const int TidyMaxPlans = 3;
        private const float TidyMinInterval = 60f;
        private const float TidyRetryDelay = 120f;
        /// <summary>
        /// One round clears several pieces, back to back, instead of one every interval. It stops on the
        /// budget, on running out of trash, or on the clock. docs/items.md §3
        /// </summary>
        private const int TidyRoundMin = 2;
        private const int TidyRoundMax = 5;
        private const float TidyRoundSeconds = 180f;
        private const float TidySkipSeconds = 600f;
        /// <summary>
        /// ItemDestroyer hides what it takes the physics step it touches the slot; still active this
        /// long after reaching out, it did not go in.
        /// </summary>
        private const float TidyInsertSeconds = 1.5f;
        private const int TidyMaxInserts = 2;

        private static readonly List<TidySpot> TidySpots = [];
        private static readonly List<TrashCan> TidyBins = [];
        private static readonly HashSet<Grabbable> ContainedItems = [];

        public override bool Enabled => YourBuddyPlugin.ConfigTidying.Value;
        public override float Interval => Mathf.Max(TidyMinInterval, YourBuddyPlugin.ConfigTidyIntervalMinutes.Value * 60f);
        public override float RetryDelay => TidyRetryDelay;
        protected override float SkipSeconds => TidySkipSeconds;
        protected override string Command => "buddy_tidy";

        private enum TidyPhase { Walk, Handle, Insert }

        /// <summary>
        /// A container with trash in it: the doors to open and what it holds (Furniture.itemMover). docs/snacks.md §1
        /// </summary>
        private sealed class TrashContainer(Transform root, Door[] doors, InstantItemDetector contents, HidingSpot? hideout)
        {
            public readonly Transform Root = root;
            public readonly Door[] Doors = doors;
            public readonly InstantItemDetector Contents = contents;
            public readonly HidingSpot? Hideout = hideout;
            public readonly string Name = "the " + ContainerKind(root);
        }

        /// <summary>
        /// One tidying round: several pieces carried one after another, so the buddy clears a room
        /// instead of binning one wrapper every few minutes. docs/items.md §3
        /// </summary>
        private sealed class TidyRun
        {
            public readonly int Budget = Random.Range(TidyRoundMin, TidyRoundMax + 1);
            private readonly float startedAt = Time.time;
            public int Done;

            /// <summary>
            /// Whether another piece may be fetched: the budget and the clock, nothing about the world.
            /// </summary>
            public bool WantsMore => Done < Budget && Time.time - startedAt < TidyRoundSeconds;

            public string Describe() => Done == 1 ? "a piece of trash" : Done + " pieces of trash";
        }

        /// <summary>
        /// Found trash: where it is walked to (its top, or its container's doors).
        /// </summary>
        private readonly struct TidySpot(Grabbable item, TrashContainer? from, Vector3 point)
        {
            public readonly Grabbable Item = item;
            public readonly TrashContainer? From = from;
            public readonly Vector3 Point = point;
        }

        /// <summary>
        /// Two legs, two tasks: to the item (or its container), then - holding it - to the trash can.
        /// </summary>
        private sealed class TidyTask : ErrandLeg
        {
            private readonly TidyErrand errand;
            public readonly Grabbable Item;
            public readonly TrashContainer? From;
            public readonly TrashCan Bin;
            public readonly ItemDestroyer Slot;
            public readonly bool ToBin;
            public readonly string ItemLabel;
            public readonly TidyRun Run;
            public TidyPhase Phase = TidyPhase.Walk;
            public float PhaseUntil;
            public int Inserts;
            /// <summary>
            /// Only these are closed again: a door found open stays open.
            /// </summary>
            public readonly List<Door> OpenedDoors = [];

            public TidyTask(TidyErrand errand, TidyRun run, Grabbable item, TrashContainer? from, TrashCan bin,
                            ItemDestroyer slot, Vector3 point, bool toBin)
                : base(point, toBin ? bin.transform : from != null ? from.Root : item.transform)
            {
                this.errand = errand;
                Run = run;
                Item = item;
                From = toBin ? null : from;
                Bin = bin;
                Slot = slot;
                ToBin = toBin;
                ItemLabel = ItemLabelOf(item);
            }

            public override string Name => ToBin ? "the trash can" : From != null ? From.Name : $"'{ItemLabel}'";
            public override float Reach => SnackReachDist;
            public override float[] StandOffs => SnackStandOffs;
            public override float ReachBelow => SnackReachBelow;

            public override void Defer(float seconds) => errand.Defer(Own, seconds);

            public override Vector3 Approach(out bool wantMove) => errand.Approach(this, out wantMove);

            /// <summary>
            /// However tidying ended: the doors it opened, then the item in its hands.
            /// </summary>
            public override void End()
            {
                if (From != null) CloseOpenedDoors(OpenedDoors, From.Hideout, Name);
                errand.Body.Hands.Drop("stopped tidying");
            }

            public override string Describe() => ToBin ? $"carrying '{ItemLabel}' to the trash can" : $"tidying up '{ItemLabel}'";
        }

        public override bool TryStart(out string report) => TryStart(out report, null);

        /// <summary>
        /// The nearest trash it can reach, and a trash can it can carry that to. `report` finishes a sentence.
        /// </summary>
        private bool TryStart(out string report, TidyRun? round)
        {
            TidyRun run = round ?? new TidyRun();
            CollectTidySpots(out int skipped);
            if (TidySpots.Count == 0)
            {
                report = $"there is no trash within {TidySearchRadius:0}m, lying about or in a container" +
                         (skipped > 0 ? $" ({skipped} I could not deal with lately)" : "");
                return Failed(report);
            }

            string? failure = null;
            int plans = 0;
            foreach (TidySpot spot in TidySpots)
            {
                Grabbable item = spot.Item;
                if (!NearestBin(item.transform.position, out TrashCan? bin, out ItemDestroyer? slot))
                {
                    failure = $"no trash can within {TidyBinRadius:0}m of '{ItemLabelOf(item)}'";
                    continue;
                }
                TidyTask toBin = new(this, run, item, null, bin!, slot!, SlotPoint(slot!), true);
                // Only the item's leg is walked now, but a trash can with no way to it is not set off for.
                if (!Body.HasReachNode(toBin))
                {
                    Skips.Skip(bin!.transform, TidySkipSeconds);
                    failure = "no nav node near the trash can has a clear walk to it";
                    Trace($"not the trash can at {toBin.TargetPoint:0.0}: {failure} - skipping it for {TidySkipSeconds:0}s");
                    continue;
                }

                TidyTask task = new(this, run, item, spot.From, bin!, slot!, spot.Point, false);
                string where = task.From != null ? "in " + task.From.Name : "lying about";
                if (Body.InReach(task))
                {
                    task.Node = task.StandPoint = Here;
                    Begin(task, null);
                    report = $"is tidying up '{task.ItemLabel}', {where}, right here";
                    YourBuddyPlugin.Log.LogInfo($"[mind] Decided: tidy up '{task.ItemLabel}' - {where}, right here, then to the trash can");
                    return true;
                }
                if (plans++ >= TidyMaxPlans) break;

                failure = Body.PlanReach(task, out BuddyNodeGraph.NavPath plan);
                if (failure != null)
                {
                    Skips.Skip(task.Own, TidySkipSeconds);
                    Trace($"not {task.Name} at {task.TargetPoint:0.0}: {failure} - skipping it for {TidySkipSeconds:0}s");
                    continue;
                }

                Begin(task, plan);
                float distance = Vector3.Distance(Here, task.TargetPoint);
                report = $"is tidying up '{task.ItemLabel}', {where}, {distance:0.0}m away";
                YourBuddyPlugin.Log.LogInfo($"[mind] Decided: tidy up '{task.ItemLabel}' - {where}, {distance:0.0}m away, via node " +
                                            $"{task.Node:0.0}, then to the trash can at {toBin.TargetPoint:0.0}");
                return true;
            }
            report = $"none of the {TidySpots.Count} piece(s) of trash nearby can be taken to a trash can ({failure})";
            return Failed(report);
        }

        public override int Count(out float nearest)
        {
            CollectTidySpots(out _);
            nearest = Nearest(TidySpots.Count, i => TidySpots[i].Point);
            return TidySpots.Count;
        }

        /// <summary>
        /// A null plan is a target already in reach. Only trash that went in schedules the full interval.
        /// </summary>
        private void Begin(TidyTask task, BuddyNodeGraph.NavPath? plan)
        {
            Body.Walk(task, plan);
            DueAt = Time.time + TidyRetryDelay;
            Last = (task.Run.Done > 0 ? $"{task.Run.Describe()} so far, now " : "") + "on the way to " + task.Name;
        }

        /// <summary>
        /// Trash near the buddy, nearest first: one piece per container that holds some, and loose trash.
        /// Never what is in someone's hands, or loaded into a machine.
        /// </summary>
        private void CollectTidySpots(out int skipped)
        {
            TidySpots.Clear();
            ContainedItems.Clear();
            Skips.Prune();
            skipped = 0;
            Vector3 here = Here;
            float floorY = Body.FloorUnderBuddy().y;
            float margin = TidySearchRadius + SnackContainerMargin;

            foreach (Door door in SceneScan.ThisFrame<Door>())
            {
                Transform? root = ContainerOf(door, out InstantItemDetector? contents);
                // A closet has two doors; the first found brings both.
                if (root == null || !CheckedContainers.Add(root)) continue;

                Door[] doors = root.GetComponentsInChildren<Door>();
                Vector3 point = DoorFacePoint(doors);
                if (FlatDistanceSq(point, here) > margin * margin) continue;

                // Both were found together. What it holds is never loose, even out of bounds.
                List<Grabbable> inside = contents!.GetItemsInZone();
                ContainedItems.UnionWith(inside);
                if (!InBounds(point, here, floorY, root)) continue;

                Grabbable? trash = inside.Find(item => IsTrash(item) && TakeBlocker(item) == null);
                if (trash == null) continue;

                CustomRoom room = root.GetComponentInParent<CustomRoom>(true);
                if (room != null && !room.EnabledStructure) continue;

                TrashContainer container = new(root, doors, contents, root.GetComponent<HidingSpot>());
                if (ContainerBlocker(container) != null) continue;

                if (Skips.Has(root)) skipped++;
                else TidySpots.Add(new TidySpot(trash, container, point));
            }
            CheckedContainers.Clear();
            // Loaded into a sell station, furnace or airlock: the player put it there. docs/items.md §3
            foreach (ItemDetector detector in SceneScan.ThisFrame<ItemDetector>()) ContainedItems.UnionWith(detector.Items);

            foreach (Grabbable item in SceneScan.ThisFrame<Grabbable>())
            {
                if (ContainedItems.Contains(item) || TakeBlocker(item) != null || !IsTrash(item)) continue;

                Vector3 point = ItemTop(item);
                if (!InBounds(point, here, floorY, item.transform)) continue;

                if (Skips.Has(item.transform)) skipped++;
                else TidySpots.Add(new TidySpot(item, null, point));
            }
            ContainedItems.Clear();

            TidySpots.Sort((a, b) => FlatDistanceSq(a.Point, here).CompareTo(FlatDistanceSq(b.Point, here)));
            ShuffleNearest(TidySpots);
        }

        private bool InBounds(Vector3 point, Vector3 here, float floorY, Transform what) =>
            FlatDistanceSq(point, here) <= TidySearchRadius * TidySearchRadius &&
            point.y >= floorY - SnackReachBelow && point.y <= floorY + ReachTask.ReachHeight && Body.OnMyVessel(what);

        /// <summary>
        /// The trash can nearest the item, within TidyBinRadius, with its slot.
        /// </summary>
        private bool NearestBin(Vector3 from, out TrashCan? best, out ItemDestroyer? bestSlot)
        {
            best = null;
            bestSlot = null;
            float bestSq = TidyBinRadius * TidyBinRadius;
            TidyBins.Clear();
            TidyBins.AddRange(SceneScan.ThisFrame<TrashCan>());
            foreach (TrashCan bin in TidyBins)
            {
                float distSq = (bin.transform.position - from).sqrMagnitude;
                if (distSq > bestSq || Skips.Has(bin.transform)) continue;

                ItemDestroyer? slot = GameInternals.TrashCanAccess.GetItemDestroyer(bin);
                if (slot == null || !slot.isActiveAndEnabled) continue;

                best = bin;
                bestSlot = slot;
                bestSq = distSq;
            }
            TidyBins.Clear();
            return best != null;
        }

        private static Vector3 SlotPoint(ItemDestroyer slot) =>
            slot.TryGetComponent(out BoxCollider box) ? box.bounds.center : slot.transform.position;

        /// <summary>
        /// Why a container may not be opened now, or null. docs/invariants.md#a-snack-closes-what-it-opened
        /// </summary>
        private static string? ContainerBlocker(TrashContainer container)
        {
            if (container.Root == null || !container.Root.gameObject.activeInHierarchy) return "it is gone";

            return container.Hideout != null && container.Hideout.CurrentInteractor != null ? "you are hiding in it" : null;
        }

        /// <summary>
        /// UpdateRoute, once a tidy leg's plan is walked.
        /// </summary>
        private Vector3 Approach(TidyTask task, out bool wantMove)
        {
            wantMove = false;
            // ItemDestroyer hides what it takes, in the physics step the item touches the slot.
            if (task.Phase == TidyPhase.Insert && (task.Item == null || !task.Item.gameObject.activeInHierarchy))
            {
                // Release, never Drop: the can already took it, and the hands must be empty
                // before the round chains into the next piece.
                if (Body.Hands.Item == task.Item) Body.Hands.Release();
                task.Run.Done++;
                YourBuddyPlugin.Log.LogInfo($"[ai] Put '{task.ItemLabel}' into the trash can " +
                                            $"({task.Run.Done} of {task.Run.Budget} this round)");
                // Straight on to the next piece: FinishRoute would end the round and put nothing down.
                if (task.Run.WantsMore && TryStart(out _, task.Run)) return Vector3.zero;

                EndRound(task.Run);
                return Vector3.zero;
            }
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
                case TidyPhase.Walk:
                    if (!Body.StepIntoReach(task, out Vector3 move, out wantMove)) return move;

                    if (task.From != null) OpenContainerDoors(task.From.Doors, task.OpenedDoors, task.Name);
                    task.Phase = TidyPhase.Handle;
                    task.PhaseUntil = Time.time + (task.ToBin ? TidyAimSeconds : task.From != null ? SnackOpenSeconds : TidyReachSeconds);
                    break;
                case TidyPhase.Handle:
                    if (Time.time < task.PhaseUntil) break;

                    if (!task.ToBin) return PickUp(task);

                    task.Inserts++;
                    Body.Hands.ReachTo(SlotPoint(task.Slot));
                    task.Phase = TidyPhase.Insert;
                    task.PhaseUntil = Time.time + TidyInsertSeconds;
                    break;
                case TidyPhase.Insert:
                    if (Time.time < task.PhaseUntil) break;

                    InsertFailed(task);
                    return Vector3.zero;
            }
            Body.StandFacing(task.TargetPoint);
            return Vector3.zero;
        }

        /// <summary>
        /// Checked at every step of a leg.
        /// </summary>
        private string? LegBlocker(TidyTask task)
        {
            if (task.ToBin)
            {
                if (task.Bin == null || !task.Bin.gameObject.activeInHierarchy) return "the trash can is gone";

                return Body.Hands.Item != task.Item ? "it is no longer in my hands" : null;
            }
            string? item = TakeBlocker(task.Item);
            if (item != null) return item;

            if (task.From != null) return ContainerBlocker(task.From);

            return FlatDistanceSq(ItemTop(task.Item), task.TargetPoint) > SnackItemMovedDist * SnackItemMovedDist ? "it has been moved" : null;
        }

        /// <summary>
        /// Picks the item up, closes its container, and sets off on the second leg, the trash can, in the same Route.
        /// </summary>
        private Vector3 PickUp(TidyTask task)
        {
            string? failure = null;
            if (task.From != null && !task.From.Contents.GetItemsInZone().Contains(task.Item)) failure = $"it is no longer in {task.Name}";
            else if (Body.Hands.Item != task.Item && !Body.Hands.PickUp(task.Item)) failure = "I could not pick it up";
            if (failure != null)
            {
                Skips.Skip(task.Own, TidySkipSeconds);
                Last = $"left '{task.ItemLabel}' - {failure}";
                YourBuddyPlugin.Log.LogInfo($"[ai] Leaving '{task.ItemLabel}' - {failure}");
                Body.FinishRoute();
                return Vector3.zero;
            }
            string from = task.From != null ? " from " + task.Name : "";
            if (task.Inserts == 0) YourBuddyPlugin.Log.LogInfo($"[ai] Picked up '{task.ItemLabel}'{from}");
            // The next leg replaces this one without ending it, so the doors close here.
            if (task.From != null) CloseOpenedDoors(task.OpenedDoors, task.From.Hideout, task.Name);

            TidyTask toBin = new(this, task.Run, task.Item, null, task.Bin, task.Slot, SlotPoint(task.Slot), true) { Inserts = task.Inserts };
            if (Body.InReach(toBin))
            {
                toBin.Node = toBin.StandPoint = Here;
                toBin.Phase = TidyPhase.Handle;
                toBin.PhaseUntil = Time.time + TidyLiftSeconds;
                Body.Walk(toBin, null);
                return Vector3.zero;
            }
            failure = Body.PlanReach(toBin, out BuddyNodeGraph.NavPath plan);
            if (failure != null)
            {
                Skips.Skip(task.Bin.transform, TidySkipSeconds);
                Last = $"could not carry '{task.ItemLabel}' to the trash can - {failure}";
                YourBuddyPlugin.Log.LogInfo($"[ai] Cannot carry '{task.ItemLabel}' to the trash can - {failure}");
                // FinishRoute ends the task, and that puts the item down.
                Body.FinishRoute();
                return Vector3.zero;
            }
            // Straight from leg to leg: ending this one would put the item down.
            Body.Walk(toBin, plan);
            Last = $"carrying '{task.ItemLabel}' to the trash can";
            return Vector3.zero;
        }

        /// <summary>
        /// Still active after reaching into the slot: once more, then leave it. The line says why it may have failed.
        /// </summary>
        private void InsertFailed(TidyTask task)
        {
            Vector3 slot = SlotPoint(task.Slot);
            float gap = Body.Hands.DistanceTo(slot);
            bool slotOn = task.Slot.TryGetComponent(out BoxCollider box) && box.enabled && task.Slot.isActiveAndEnabled;
            string why = $"{gap:0.00}m from the slot, slot trigger {(slotOn ? "on" : "off")}, CanTrash {task.Item.CanTrash}";
            Body.Hands.ReachTo(null);
            if (task.Inserts < TidyMaxInserts)
            {
                YourBuddyPlugin.Log.LogInfo($"[ai] '{task.ItemLabel}' did not go into the trash can ({why}) - trying again");
                task.Phase = TidyPhase.Handle;
                task.PhaseUntil = Time.time + TidyAimSeconds;
                return;
            }
            Skips.Skip(task.Item.transform, TidySkipSeconds);
            Last = $"could not get '{task.ItemLabel}' into the trash can";
            YourBuddyPlugin.Log.LogInfo($"[ai] Could not get '{task.ItemLabel}' into the trash can ({why}, " +
                                        $"{task.Inserts} tries) - leaving it");
            Body.FinishRoute();
        }

        /// <summary>
        /// The round is over: only now does the next one get the full interval.
        /// </summary>
        private void EndRound(TidyRun run)
        {
            DueAt = Time.time + NextInterval();
            Last = "put " + run.Describe() + " in the trash can";
            YourBuddyPlugin.Log.LogInfo($"[ai] Tidied up: {run.Describe()} in the trash can" +
                                        (run.Done >= run.Budget ? "" : " - nothing else to clear"));
            Body.FinishRoute();
        }

        private static void Trace(string line)
        {
            if (YourBuddyPlugin.ConfigDebugLevel.Value >= 2) YourBuddyPlugin.Log.LogInfo("[mind] Tidy: " + line);
        }
    }
}

using System.Collections.Generic;
using NPC.Core;
using NPC.Core.Agents;
using NPC.Core.Navigation;
using UnityEngine;
using static YourBuddy.Items;

namespace YourBuddy
{
    /// <summary>
    /// Now and then, with nothing else to do, carry a piece of trash to a trash can and put it in
    /// through the slot. The trash lies about or sits in a fridge, cabinet, chest or locker, which is
    /// opened and closed again. docs/items.md §3
    /// </summary>
    internal sealed class TidyErrand(IErrandBody body) : Errand(body)
    {
        private const float TidySearchRadius = 30f;
        /// <summary>
        /// A trash can further than this from the item is not carried to.
        /// </summary>
        private const float TidyBinRadius = 40f;
        private const int TidyMaxPlans = 3;
        private const int TidyMaxBinPlans = 3;
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
        /// ItemDestroyer hides what it takes in the physics step it touches the slot. Still active this
        /// long after reaching out means it did not go in.
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
        protected override string Command => "buddy_order tidy";
        protected override string Topic => "Tidy";

        private enum TidyPhase { Walk, Handle, Insert }

        /// <summary>
        /// A container with trash in it, with the doors to open and what it holds (Furniture.itemMover). docs/snacks.md §1
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
        /// One tidying round of several pieces carried one after another, so the buddy clears a room
        /// instead of binning one wrapper every few minutes. docs/items.md §3
        /// </summary>
        private sealed class TidyRun
        {
            public readonly int Budget = Random.Range(TidyRoundMin, TidyRoundMax + 1);
            private readonly float startedAt = Time.time;
            public int Done;

            /// <summary>
            /// Whether another piece may be fetched, by the budget and the clock only.
            /// </summary>
            public bool WantsMore => Done < Budget && Time.time - startedAt < TidyRoundSeconds;

            public string Describe() => Done == 1 ? "a piece of trash" : Done + " pieces of trash";
        }

        /// <summary>
        /// Found trash and where it is walked to (its top, or its container's doors).
        /// </summary>
        private readonly struct TidySpot(Grabbable item, TrashContainer? from, Vector3 point)
        {
            public readonly Grabbable Item = item;
            public readonly TrashContainer? From = from;
            public readonly Vector3 Point = point;
        }

        /// <summary>
        /// Two legs, two tasks. To the item (or its container), then, holding it, to the trash can.
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
            /// Only these are closed again. A door found open stays open.
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
            /// However tidying ended, closes the doors it opened, then puts down the item in its hands.
            /// </summary>
            public override void End()
            {
                if (From != null) CloseOpenedDoors(OpenedDoors, From.Hideout, Name);
                errand.Body.Hands.Drop("stopped tidying");
            }

            public override string Describe() => ToBin ? $"carrying '{ItemLabel}' to the trash can" : $"tidying up '{ItemLabel}'";
            public override bool Holds(Transform t) => base.Holds(t) ||
                (Item != null && t == Item.transform) || (Bin != null && t == Bin.transform);
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
            int binPlans = 0;
            foreach (TidySpot spot in TidySpots)
            {
                Grabbable item = spot.Item;
                TidyTask? toBin = FindBin(run, item, ref binPlans, ref failure);
                if (toBin == null) continue;

                TidyTask task = new(this, run, item, spot.From, toBin.Bin, toBin.Slot, spot.Point, false);
                SetOffResult set = SetOff(task, Begin, ref plans, TidyMaxPlans, ref failure);
                if (set == SetOffResult.NoPlansLeft) break;
                if (set == SetOffResult.NoPlan) continue;

                string where = task.From != null ? "in " + task.From.Name : "lying about";
                string far = HowFar(set, task);
                report = $"is tidying up '{task.ItemLabel}', {where}, {far}";
                YourBuddyPlugin.Log.LogInfo($"[mind] Decided: tidy up '{task.ItemLabel}' - {where}, {far}, " +
                                            (set == SetOffResult.InReach ? "then to the trash can"
                                                : $"via node {task.Node:0.0}, then to the trash can at {toBin.TargetPoint:0.0}"));
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
        private void Begin(TidyTask task, NavPath? plan) =>
            Begin(task, plan, TidyRetryDelay, (task.Run.Done > 0 ? $"{task.Run.Describe()} so far, now " : "") + "on the way to " + task.Name);

        /// <summary>
        /// Trash near the buddy, nearest first. One piece per container that holds some, and loose trash.
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
            // Loaded into a sell station, furnace or airlock, so the player put it there. docs/items.md §3
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
        /// Nearest reachable bin first, with a bounded number of complete route searches.
        /// </summary>
        private TidyTask? FindBin(TidyRun run, Grabbable item, ref int plans, ref string? failure)
        {
            Vector3 from = item.transform.position;
            TidyBins.Clear();
            TidyBins.AddRange(SceneScan.ThisFrame<TrashCan>());
            TidyBins.RemoveAll(bin => bin == null || !bin.isActiveAndEnabled ||
                (bin.transform.position - from).sqrMagnitude > TidyBinRadius * TidyBinRadius ||
                Skips.Has(bin.transform) || Body.TakenByAnother(bin.transform) || !Body.OnMyVessel(bin.transform));
            TidyBins.Sort((a, b) => (a.transform.position - from).sqrMagnitude.CompareTo((b.transform.position - from).sqrMagnitude));
            foreach (TrashCan bin in TidyBins)
            {
                ItemDestroyer? slot = GameInternals.TrashCanAccess.GetItemDestroyer(bin);
                if (slot == null || !slot.isActiveAndEnabled) continue;
                TidyTask task = new(this, run, item, null, bin, slot, SlotPoint(slot), true);
                if (Body.InReach(task)) return task;
                if (plans >= TidyMaxBinPlans) break;
                plans++;
                string? why = Body.PlanReach(task, out _);
                if (why == null) return task;
                failure = why;
                Skips.Skip(bin.transform, TidySkipSeconds);
                Trace($"not the trash can at {task.TargetPoint:0.0}: {why} - trying another bin");
            }
            failure ??= "no available reachable trash can nearby";
            return null;
        }

        private static Vector3 SlotPoint(ItemDestroyer slot) =>
            slot.TryGetComponent(out BoxCollider box) ? box.bounds.center : slot.transform.position;

        /// <summary>
        /// Why a container may not be opened now, or null. docs/invariants.md#a-snack-closes-what-it-opened
        /// </summary>
        private string? ContainerBlocker(TrashContainer container)
        {
            if (container.Root == null || !container.Root.gameObject.activeInHierarchy) return "it is gone";

            if (container.Hideout != null && container.Hideout.CurrentInteractor != null) return "you are hiding in it";

            // Another buddy snacking or hiding there. docs/invariants.md#one-buddy-per-target
            return Body.TakenByAnother(container.Root) ? "another buddy is using it" : null;
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
                // Release, never Drop. The can already took it, and the hands must be empty
                // before the round chains into the next piece.
                if (Body.Hands.Item == task.Item) Body.Hands.Release();
                task.Run.Done++;
                YourBuddyPlugin.Log.LogInfo($"[ai] Put '{task.ItemLabel}' into the trash can " +
                                            $"({task.Run.Done} of {task.Run.Budget} this round)");
                // Straight on to the next piece, since FinishRoute would end the round and put nothing down.
                if (task.Run.WantsMore && TryStart(out _, task.Run)) return Vector3.zero;

                EndRound(task.Run);
                return Vector3.zero;
            }
            string? blocker = LegBlocker(task);
            if (blocker != null) return Leave($"'{task.ItemLabel}'", blocker);

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
                if (task.Bin == null || !task.Bin.isActiveAndEnabled || task.Slot == null || !task.Slot.isActiveAndEnabled)
                    return "the trash can is unavailable";

                return NotInHands(task.Item);
            }
            string? item = TakeBlocker(task.Item);
            if (item != null) return item;

            if (task.From != null) return ContainerBlocker(task.From);

            return MovedBlocker(ItemTop(task.Item), task.TargetPoint);
        }

        /// <summary>
        /// Picks the item up, closes its container, and sets off on the second leg, the trash can, in the same Route.
        /// </summary>
        private Vector3 PickUp(TidyTask task)
        {
            if (task.Bin == null || task.Slot == null || !task.Bin.isActiveAndEnabled || !task.Slot.isActiveAndEnabled)
                return Leave($"'{task.ItemLabel}'", "the trash can is unavailable");
            TidyTask toBin = new(this, task.Run, task.Item, null, task.Bin, task.Slot, SlotPoint(task.Slot), true) { Inserts = task.Inserts };
            bool inReach = Body.InReach(toBin);
            NavPath? route = null;
            string? failure = null;
            if (task.From != null && !task.From.Contents.GetItemsInZone().Contains(task.Item)) failure = $"it is no longer in {task.Name}";
            else if (!inReach)
            {
                failure = Body.PlanReach(toBin, out NavPath plan);
                if (failure == null) route = plan;
                else Skips.Skip(task.Bin.transform, TidySkipSeconds);
            }
            if (failure == null && Body.Hands.Item != task.Item && !Body.Hands.PickUp(task.Item)) failure = "I could not pick it up";
            if (failure != null)
            {
                Skips.Skip(task.Own, TidySkipSeconds);
                return Leave($"'{task.ItemLabel}'", failure);
            }
            string from = task.From != null ? " from " + task.Name : "";
            if (task.Inserts == 0) YourBuddyPlugin.Log.LogInfo($"[ai] Picked up '{task.ItemLabel}'{from}");
            // The next leg replaces this one without ending it, so the doors close here.
            if (task.From != null) CloseOpenedDoors(task.OpenedDoors, task.From.Hideout, task.Name);

            if (inReach)
            {
                toBin.Node = toBin.StandPoint = Here;
                toBin.Phase = TidyPhase.Handle;
                toBin.PhaseUntil = Time.time + TidyLiftSeconds;
                Body.Walk(toBin, null);
                return Vector3.zero;
            }
            // Straight from leg to leg, since ending this one would put the item down.
            Body.Walk(toBin, route);
            Last = $"carrying '{task.ItemLabel}' to the trash can";
            return Vector3.zero;
        }

        /// <summary>
        /// Still active after reaching into the slot. Try once more, then leave it. The line says why it may have failed.
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
        /// The round is over. Only now does the next one get the full interval.
        /// </summary>
        private void EndRound(TidyRun run)
        {
            DueAt = Time.time + NextInterval();
            Last = "put " + run.Describe() + " in the trash can";
            YourBuddyPlugin.Log.LogInfo($"[ai] Tidied up: {run.Describe()} in the trash can" +
                                        (run.Done >= run.Budget ? "" : " - nothing else to clear"));
            Body.FinishRoute();
        }
    }
}

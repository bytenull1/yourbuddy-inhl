using System.Collections.Generic;
using NPC.Core;
using NPC.Core.Agents;
using NPC.Core.Navigation;
using UnityEngine;

namespace YourBuddy
{
    // Separate from trash disposal and resource buying. docs/storing.md
    internal sealed partial class StoreErrand(IErrandBody body) : Errand(body)
    {
        private readonly List<FurnitureStorage> containers = [];
        private readonly List<Grabbable> items = [];
        private readonly HashSet<Grabbable> putAway = [];
        private readonly Dictionary<FurnitureStorage, int> ranks = [];
        private readonly Dictionary<FurnitureStorage, int> insertionFailures = [];
        private readonly Dictionary<Grabbable, FurnitureStorage> sources = [];
        private readonly HashSet<Grabbable> candidates = [];
        private FurnitureStorage? source;
        private readonly SkipList recentlyStored = new();
        private static readonly ResourceProbeBudget Probes = new(4);
        private int containerIndex, probeIndex, orientationIndex;
        private Vector3 shapeHalf, originalHalf, itemCenter;
        private Quaternion placementRotation;
        private bool rearranging;
        private Vector3 half;
        private Quaternion rotation;
        private float deadline;
        private const float HandSpeed = .75f;
        private static readonly float[] ApproachOffsets = [.55f, .7f, .85f];
        private static readonly float[] PickupOffsets = [1.3f, 1.15f, 1.05f, .95f, .85f, .7f, .55f];
        public override bool Enabled => YourBuddyPlugin.ConfigStoring.Value && Body.IsAboardPlayerShip();
        public override float Interval => 3f;
        internal bool Running => Body.Leg is Leg;
        public override float RetryDelay => 60f;
        protected override float SkipSeconds => 120f;
        protected override string Command => "buddy_order store";
        protected override string Topic => "Store";

        private int BestHome(Grabbable item)
        {
            bool food = item.GetComponent<Food>() != null, resource = item.GetComponent<ResourceContainer>() != null;
            int best = 100;
            foreach (FurnitureStorage container in containers)
            {
                if (container.Overflow != null) continue;
                best = System.Math.Min(best, StoragePolicy.Home(food, resource, container.Name));
            }
            return best;
        }

        private void Collect()
        {
            items.Clear(); containers.Clear(); putAway.Clear(); sources.Clear(); candidates.Clear(); Skips.Prune(); recentlyStored.Prune();
            SpaceShip? ship = GameManager.Instance.PlayerShip;
            if (ship == null || !Body.IsAboardPlayerShip() || Body.Hands.Item != null) return;
            FurnitureStorage.Collect(ship, containers);
            StorageOverflow.Collect(ship, containers);
            foreach (FurnitureStorage container in containers)
            {
                Body.LoadRoomOf(container.Root);
                putAway.Clear();
                if (!container.ReadContents(putAway))
                { RepackRefused(container, "contents scan unavailable; storage selection aborted"); containers.Clear(); return; }
                foreach (Grabbable stored in putAway)
                {
                    if (!sources.ContainsKey(stored)) sources.Add(stored, container);
                    candidates.Add(stored);
                }
            }
            putAway.Clear();
            foreach (ItemDetector detector in SceneScan.ThisFrame<ItemDetector>()) putAway.UnionWith(detector.Items);
            candidates.UnionWith(SceneScan.ThisFrame<Grabbable>());
            RefreshPlacementScene();
            int irrelevant = 0;
            foreach (Grabbable item in candidates)
            {
                if (item == null) continue;
                string? skip = TakeBlocker(item);
                if (skip == null && putAway.Contains(item)) skip = "in a machine";
                if (skip == null && PlacementDeferred(item)) skip = "placement unchanged; retry pending";
                if (skip == null && Skips.Has(item.transform)) skip = "failed-item cooldown";
                if (skip == null && !Body.OnMyVessel(item.transform)) skip = "not on my vessel";
                if (skip == null && !StorageItems.CanStore(item)) skip = "not a usable storage supply";
                if (skip == null && item.GetComponent<Suit>() != null) skip = "suit excluded";
                if (skip == null && Items.FlatDistanceSq(item.transform.position, Here) > 900f) skip = "over 30m away";
                if (skip != null)
                {
                    if (skip == "not on my vessel" || skip == "not a usable storage supply") irrelevant++;
                    else SelectionLog(item, skip);
                    continue;
                }
                if (sources.TryGetValue(item, out FurnitureStorage? current))
                {
                    int home = StoragePolicy.Home(item.GetComponent<Food>() != null, item.GetComponent<ResourceContainer>() != null, current.Name);
                    bool needsArrange = current.NeedsArrange(item);
                    if (repack?.Next?.Item != item && current.Overflow == null && !needsArrange && recentlyStored.Has(item.transform))
                    { SelectionLog(item, "recent successful placement; working on other supplies first"); continue; }
                    int best = BestHome(item);
                    bool excluded = !current.Available || Body.TakenByAnother(current.Root) ||
                        (repack?.Next?.Item != item && !StoragePolicy.ShouldMove(home, best) && !needsArrange);
                    SelectionLog(item, $"{(excluded ? "skip" : "eligible")} source={StorageLabel(current)} available={current.Available} reserved={Body.TakenByAnother(current.Root)} home={home} best={best} untidy={needsArrange} repackNext={repack?.Next?.Item == item}");
                    if (excluded) continue;
                }
                else SelectionLog(item, "eligible loose supply");
                items.Add(item);
            }
            if (Time.time >= scanLogAt)
            {
                scanLogAt = Time.time + 30f;
                YourBuddyPlugin.Log.LogInfo($"[store-detail] scan candidates={candidates.Count} eligible={items.Count} irrelevant={irrelevant} containers={containers.Count} repack={repack != null}");
            }
            items.Sort((a,b) => Items.FlatDistanceSq(a.transform.position, Here).CompareTo(Items.FlatDistanceSq(b.transform.position, Here)));
        }

        public override int Count(out float nearest)
        {
            Collect(); nearest = Nearest(items.Count, i => items[i].transform.position);
            return containers.Count > 0 ? items.Count : 0;
        }

        public override bool TryStart(out string report)
        {
            Collect();
            SelectRepackItem();
            if (items.Count == 0 || containers.Count == 0)
            {
                CloseStorageDoors();
                report = containers.Count == 0 ? "no storage area is available aboard" : "nothing needs storing or sorting";
                return Failed(report);
            }
            jobNumber++; jobLogAt = 0; jobState = "";
            Grabbable item = items[0];
            source = sources.TryGetValue(item, out FurnitureStorage? current) ? current : null;
            if (!NpcHands.ColliderBounds(item.gameObject, out Bounds bounds))
            {
                Skips.Skip(item.transform, SkipSeconds);
                report = "the item's size is unavailable";
                return Failed(report);
            }
            Leg pickupCheck = new(this, item, source != null && source.Overflow == null ? source.Approach : Items.ItemTop(item), Stage.Fetch);
            if (Body.Hands.Item != item && !Body.InReach(pickupCheck))
            {
                string? unreachable = Body.PlanReach(pickupCheck, out _);
                if (unreachable != null)
                {
                    Skips.Skip(item.transform, SkipSeconds);
                    report = "cannot reach " + Items.ItemLabelOf(item) + ": " + unreachable;
                    Trace(report);
                    return Failed(report);
                }
            }
            half = originalHalf = bounds.extents + Vector3.one * .01f;
            shapeHalf = StorageShape.Measure(item, bounds);
            itemCenter = item.transform.InverseTransformPoint(bounds.center);
            rearranging = source != null && source.NeedsArrange(item);
            rotation = item.transform.rotation;
            containerIndex = probeIndex = orientationIndex = 0;
            deadline = Time.time + 120f;
            bool food = item.GetComponent<Food>() != null;
            int sourceHome = source == null ? int.MaxValue : StoragePolicy.Home(food, item.GetComponent<ResourceContainer>() != null, source.Name);
            ranks.Clear(); insertionFailures.Clear();
            string group = Group(item);
            bool resource = item.GetComponent<ResourceContainer>() != null;
            foreach (FurnitureStorage container in containers)
            {
                putAway.Clear();
                bool known = container.ReadContents(putAway);
                bool matching = known && putAway.Count > 0;
                foreach (Grabbable stored in putAway)
                {
                    if (Group(stored) != group) { matching = false; break; }
                }
                int home = StoragePolicy.Home(food, resource, container.Name);
                if (RepackEvacuating(item))
                {
                    ranks[container] = container.Overflow != null && container.Overflow.CandidateCount > 0 ? container.Overflow.Priority : int.MaxValue;
                    continue;
                }
                ranks[container] = container.Overflow != null ? (container.Overflow.CandidateCount == 0 || (source != null && sourceHome != int.MaxValue) ? int.MaxValue : 10000 + container.Overflow.Priority * 100) : (home < sourceHome || (container == source && rearranging)) && home < 100 ?
                    home * 100 + StoragePolicy.Rank(food, resource, container.Name, matching, known && putAway.Count == 0) : int.MaxValue;
            }
            containers.RemoveAll(c => (c == source && !rearranging) || !c.Available || Body.TakenByAnother(c.Root) || Skips.Has(c.Root) || ranks[c] == int.MaxValue);
            containers.Sort((a,b) => ranks[a] != ranks[b] ? ranks[a].CompareTo(ranks[b]) :
                Items.FlatDistanceSq(a.Approach, Here).CompareTo(Items.FlatDistanceSq(b.Approach, Here)));
            if (containers.Count == 0)
            {
                report = "no available storage for this item";
                Skips.Skip(item.transform, SkipSeconds);
                return Failed(report);
            }
            foreach (FurnitureStorage container in containers)
                YourBuddyPlugin.Log.LogInfo($"[store-detail] job={jobNumber} candidate={StorageLabel(container)} rank={ranks[container]} approach={container.Approach}");
            Leg search = new(this, item, Here, Stage.Search);
            search.Node = search.StandPoint = Here;
            Begin(search, null, RetryDelay, "checking storage space");
            report = "is finding storage for " + Items.ItemLabelOf(item);
            YourBuddyPlugin.Log.LogInfo($"[store] {report}; measured size {bounds.size}");
            return true;
        }

        private static string Group(Grabbable item)
        {
            if (item.GetComponent<Food>() != null) return "food";
            ResourceContainer? cell = item.GetComponent<ResourceContainer>();
            if (cell != null) return "cell:" + cell.Type;
            return string.IsNullOrEmpty(item.Signature) ? Items.ItemLabelOf(item) : item.Signature;
        }

        private bool Plan(Leg leg)
        {
            int plans = 0; string? why = null;
            SetOffResult result = SetOff(leg, (task, path) => Begin(task, path, RetryDelay, task.Describe()), ref plans, 1, ref why);
            if (why != null) Trace(why);
            return result == SetOffResult.InReach || result == SetOffResult.Walking;
        }

        private Vector3 Search(Leg task)
        {
            while (containerIndex < containers.Count)
            {
                FurnitureStorage destination = containers[containerIndex];
                if (!destination.Available || Body.TakenByAnother(destination.Root))
                { containerIndex++; probeIndex = orientationIndex = 0; continue; }
                Body.LoadRoomOf(destination.Root);
                placementRotation = StorageShape.Rotation(destination.Root, orientationIndex);
                half = StorageShape.Half(shapeHalf, placementRotation);
                int capacity = destination.CandidateCount(half);
                if (probeIndex >= capacity)
                {
                    if (++orientationIndex < StorageShape.Orientations) { probeIndex = 0; continue; }
                    YourBuddyPlugin.Log.LogInfo($"[store] {destination.Name} in {destination.Room.name}: " +
                        (capacity == 0 ? "item is too large" : "no clear supported slot; last probe: " + destination.LastBlocker) + "; trying another container");
                    if (TryBeginRepack(task, destination)) return Vector3.zero;
                    containerIndex++; probeIndex = orientationIndex = 0;
                    continue;
                }
                if (!Probes.Take(Time.frameCount)) return Vector3.zero;
                if (!destination.Probe(probeIndex++, half, task.Item.transform, out Vector3 point))
                {
                    if (destination.Overflow != null)
                        YourBuddyPlugin.Log.LogInfo($"[store-probe] job={jobNumber} room={destination.Room.name} orientation={orientationIndex} slot={probeIndex - 1} half={half}: {destination.LastBlocker}");
                    continue;
                }
                if (destination == source && Items.ColliderBounds(task.Item.gameObject, out Bounds currentBounds) &&
                    (currentBounds.center - point).sqrMagnitude < .0025f &&
                    Quaternion.Angle(rotation, placementRotation) < 5f) continue;
                YourBuddyPlugin.Log.LogInfo($"[store] Fit in {destination.Name}: orientation {orientationIndex}, footprint {half * 2f}, slot {probeIndex - 1}");
                Leg delivery = new(this, task.Item, destination.Approach, Stage.Deliver, destination);
                NavPath? route = null;
                if (!Body.InReach(delivery))
                {
                    string? why = Body.PlanReach(delivery, out NavPath path);
                    if (why != null)
                    {
                        Trace("cannot reach " + destination.Name + ": " + why);
                        containerIndex++; probeIndex = orientationIndex = 0;
                        return Vector3.zero;
                    }
                    route = path;
                }
                else delivery.Node = delivery.StandPoint = Here;
                if (Body.Hands.Item == task.Item)
                {
                    delivery.LocalPoint = destination.Root.InverseTransformPoint(point);
                    Body.Walk(delivery, route);
                    YourBuddyPlugin.Log.LogInfo($"[store] Carrying {Items.ItemLabelOf(task.Item)} to {destination.Name} in {destination.Room.name}");
                    return Vector3.zero;
                }
                Leg fetch = new(this, task.Item, source != null && source.Overflow == null ? source.Approach : Items.ItemTop(task.Item), Stage.Fetch, destination)
                { LocalPoint = destination.Root.InverseTransformPoint(point) };
                if (!Plan(fetch)) return Stop(task, "cannot reach the item");
                return Vector3.zero;
            }
            return Stop(task, Body.Hands.Item == task.Item ? "all suitable storage is blocked; ending the carry" : "all suitable storage is full, too small or unreachable; leaving the item untouched", placementFailure: true);
        }

        private Vector3 Approach(Leg task, out bool wantMove)
        {
            wantMove = false;
            if (Time.time > deadline) return Stop(task, "storage job timed out");
            if (task.Item == null) return Stop(task, "item unavailable");
            JobLog(task);
            if (task.Stage == Stage.Search)
            {
                if (Body.Hands.Item != task.Item && TakeBlocker(task.Item) is { } busy) return Stop(task, busy);
                return Search(task);
            }
            FurnitureStorage? destination = task.Destination;
            if (destination == null || !destination.Available) return Retry(task, "storage became unavailable");
            Body.LoadRoomOf(destination.Root);
            if (task.Released)
            {
                if (Time.time < task.Until) return Vector3.zero;
                Vector3 plannedPoint = destination.Root.TransformPoint(task.LocalPoint);
                bool settled = destination.Settled(task.Item);
                Vector3 displacement = task.Item.transform.TransformPoint(itemCenter) - plannedPoint;
                float drift = displacement.magnitude;
                float tilt = Quaternion.Angle(task.Item.transform.rotation, placementRotation);
                if (!settled || !StoragePolicy.PoseFits(displacement.x, displacement.y, displacement.z, tilt, destination.Overflow != null))
                    return Stop(task, $"placement verification failed: settled={settled}, drift={drift:0.000}m, displacement={displacement}, rotation={tilt:0.0}deg; not counted as stored");
                task.Item.SavePosition();
                YourBuddyPlugin.Log.LogInfo($"[store] Stored {Items.ItemLabelOf(task.Item)} in {destination.Name} ({destination.Room.name})");
                DueAt = Time.time + Interval;
                Last = "stored " + Items.ItemLabelOf(task.Item);
                task.Completed = true;
                if (destination.Overflow == null) recentlyStored.Skip(task.Item.transform, 120f);
                RepackStored(task.Item, destination);
                Body.FinishRoute();
                return Vector3.zero;
            }
            if (task.Stage == Stage.Fetch)
            {
                if (task.Extracting) return Extract(task);
                if (TakeBlocker(task.Item) is { } why) return Stop(task, why);
                if (!StorageItems.CanStore(task.Item)) return Stop(task, "item is no longer a usable supply");
                if (source != null && !source.Available) return Stop(task, "source storage is unavailable");
                if (source != null && source.Overflow == null && Items.FlatDistanceSq(source.Approach, task.TargetPoint) > .25f)
                    return Stop(task, "source storage moved; checking again later");
                if (source == null || source.Overflow != null)
                {
                    if (Items.MovedBlocker(Items.ItemTop(task.Item), task.TargetPoint) is { } moved) return Stop(task, moved);
                }
            }
            else
            {
                if (Body.Hands.Item != task.Item) return Stop(task, "item left my hands");
                if (Items.FlatDistanceSq(destination.Approach, task.TargetPoint) > .25f)
                    return Stop(task, "storage moved; checking again later");
            }
            if (!Body.StepIntoReach(task, out Vector3 move, out wantMove)) return move;
            Vector3 point = destination.Root.TransformPoint(task.LocalPoint);
            if (task.Stage == Stage.Fetch)
            {
                if (source != null && source.Overflow == null)
                {
                    if (task.Until == 0)
                    {
                        OpenStorageDoors(source);
                        task.Until = Time.time + Items.SnackOpenSeconds;
                        return Vector3.zero;
                    }
                    if (Time.time < task.ClearanceCheckAt) return Vector3.zero;
                    if (!source.DoorsReady)
                    {
                        if (Time.time < task.Until || WaitForClearance(task)) return Vector3.zero;
                        return Stop(task, "source door has not finished opening");
                    }
                    task.BlockedUntil = task.ClearanceCheckAt = 0;
                    putAway.Clear();
                    if (!source.ReadContents(putAway) || !putAway.Contains(task.Item)) return Stop(task, "item left its source storage");
                }
                // Recheck container and staged-item ownership before taking anything.
                putAway.Clear();
                foreach (FurnitureStorage container in containers)
                {
                    if (!container.ReadContents(putAway)) return Stop(task, "cannot check stored items");
                }
                if (source == null && putAway.Contains(task.Item)) return Stop(task, "item is already stored");
                foreach (ItemDetector detector in SceneScan.ThisFrame<ItemDetector>())
                {
                    if (detector.Items.Contains(task.Item)) return Stop(task, "item was placed in a machine");
                }
                if (!destination.Clear(point, half, task.Item.transform, true)) return Retry(task, "storage space became occupied", false);
                Leg delivery = new(this, task.Item, destination.Approach, Stage.Deliver, destination) { LocalPoint = task.LocalPoint };
                bool close = Body.InReach(delivery);
                NavPath? route = null;
                if (!close)
                {
                    string? failure = Body.PlanReach(delivery, out NavPath path);
                    if (failure != null) return Retry(task, failure);
                    route = path;
                }
                if (source != null && source.Overflow == null)
                {
                    if (!Items.ColliderBounds(task.Item.gameObject, out Bounds itemBounds)) return Stop(task, "item bounds unavailable");
                    // Clear shelf contact before the padded horizontal sweep. docs/storing.md
                    Vector3 lifted = itemBounds.center + new Vector3(0f, .03f, 0f);
                    task.LiftHalf = new Vector3(originalHalf.x, StorageAccess.LiftHalfHeight(itemBounds.extents.y), originalHalf.z);
                    Vector3[] insertion = source.InsertionPath(Body.Hands.Point, lifted, originalHalf);
                    task.ReachPoints = new Vector3[insertion.Length + 1];
                    task.ReachPoints[0] = lifted;
                    for (int i = 0; i < insertion.Length - 1; i++) task.ReachPoints[i + 1] = insertion[insertion.Length - 2 - i];
                    task.ReachPoints[insertion.Length] = Body.Hands.Point;
                    Vector3 from = itemBounds.center;
                    for (int i = 0; i < task.ReachPoints.Length; i++)
                    {
                        Vector3 next = task.ReachPoints[i];
                        if (!source.ClearReach(from, next, i == 0 ? task.LiftHalf : originalHalf, task.Item.transform, Body.Transform))
                            return Stop(task, "cannot safely take the item out of its storage: " + source.LastBlocker);
                        from = next;
                    }
                    if (!Body.Hands.PickUp(task.Item)) return Stop(task, "could not pick up the item");
                    Body.Hands.TurnWorld(rotation);
                    task.Extracting = true;
                    task.Until = Time.time + 4f;
                    Body.Hands.ReachTo(task.ReachPoints[0], HandSpeed);
                    return Vector3.zero;
                }
                if (Time.time < task.ClearanceCheckAt) return Vector3.zero;
                if (!Items.ColliderBounds(task.Item.gameObject, out Bounds looseBounds)) return Stop(task, "item bounds unavailable");
                Vector3 liftedLoose = looseBounds.center + new Vector3(0, .03f, 0);
                Vector3 liftHalf = new(originalHalf.x, StorageAccess.LiftHalfHeight(looseBounds.extents.y), originalHalf.z);
                if (!destination.ClearReach(looseBounds.center, liftedLoose, liftHalf, task.Item.transform, Body.Transform) ||
                    !destination.ClearReach(liftedLoose, Body.Hands.Point, originalHalf, task.Item.transform, Body.Transform))
                {
                    if (WaitForClearance(task)) return Vector3.zero;
                    return Stop(task, "pickup path blocked: " + destination.LastBlocker);
                }
                if (!Body.Hands.PickUp(task.Item)) return Stop(task, "could not pick up the item");
                Body.Hands.TurnWorld(rotation);
                if (close) delivery.Node = delivery.StandPoint = Here;
                Body.Walk(delivery, route);
                YourBuddyPlugin.Log.LogInfo($"[store] Carrying {Items.ItemLabelOf(task.Item)} to {destination.Name} in {destination.Room.name}");
                return Vector3.zero;
            }
            Body.StandFacing(task.TargetPoint);
            if (!task.Oriented && Quaternion.Angle(task.Item.transform.rotation, placementRotation) > 2f)
            {
                if (Time.time < task.ClearanceCheckAt) return Vector3.zero;
                if (!task.Turning)
                {
                    if (Body.Hands.DistanceTo(Body.Hands.Point) > .05f)
                    {
                        if (task.CarryReadyUntil == 0) task.CarryReadyUntil = Time.time + 4f;
                        if (Time.time >= task.CarryReadyUntil) return Retry(task, "item did not reach carrying height");
                        return Vector3.zero;
                    }
                    Body.Hands.ReachTo(task.Item.transform.TransformPoint(itemCenter), HandSpeed);
                    task.Turning = true;
                    return Vector3.zero;
                }
                if (!Items.ColliderBounds(task.Item.gameObject, out Bounds held)) return Stop(task, "cannot measure held item before rotation");
                if (Quaternion.Angle(task.Item.transform.rotation, placementRotation) > 2f &&
                    !destination.ClearTurn(held.center, shapeHalf.magnitude + .02f, task.Item.transform, Body.Transform))
                {
                    if (WaitForClearance(task)) return Vector3.zero;
                    return Retry(task, "no clear space to rotate the item safely");
                }
                Body.Hands.TurnWorld(placementRotation);
                task.Oriented = true;
                task.BlockedUntil = task.ClearanceCheckAt = 0;
                return Vector3.zero;
            }
            if (task.Until == 0)
            {
                OpenStorageDoors(destination);
                task.Until = Time.time + Items.SnackOpenSeconds;
                return Vector3.zero;
            }
            if (!task.Reaching && !destination.DoorsReady)
            {
                if (Time.time < task.Until || Time.time < task.ClearanceCheckAt || WaitForClearance(task)) return Vector3.zero;
                return Retry(task, "storage door has not finished opening");
            }
            if (task.Reaching)
            {
                foreach (Door door in destination.Doors)
                {
                    if (door == null || !door.Opened) return Stop(task, "storage door closed during placement");
                }
            }
            if (Time.time < task.ClearanceCheckAt) return Vector3.zero;
            if (!destination.Clear(point, half, task.Item.transform, false))
            {
                if (!task.Reaching && WaitForClearance(task)) return Vector3.zero;
                return Retry(task, "storage opening or slot is blocked", false);
            }
            if (!task.Reaching)
            {
                Vector3 heldCenter = task.Item.transform.TransformPoint(itemCenter);
                task.ReachPoints = destination.InsertionPath(heldCenter, point, half);
                Vector3 from = heldCenter;
                bool clear = true;
                foreach (Vector3 next in task.ReachPoints)
                {
                    if (!destination.ClearReach(from, next, half, task.Item.transform, Body.Transform)) { clear = false; break; }
                    from = next;
                }
                // A straight sweep can fit where lowering beside the furniture cannot.
                if (!clear && destination.ClearReach(heldCenter, point, half, task.Item.transform, Body.Transform))
                { task.ReachPoints = [point]; clear = true; }
                if (!clear)
                {
                    if (WaitForClearance(task)) return Vector3.zero;
                    return Retry(task, "cannot move the item through the storage opening: " + destination.LastBlocker, false);
                }
                task.BlockedUntil = task.ClearanceCheckAt = 0;
                Body.Hands.ReachTo(task.ReachPoints[0], HandSpeed);
                task.Reaching = true;
                task.Until = Time.time + 4f;
                return Vector3.zero;
            }
            int ready = ReachReady(task, destination);
            if (ready == 0) return Vector3.zero;
            if (ready < 0) return Stop(task, "item could not safely reach the storage slot");
            if (++task.ReachIndex < task.ReachPoints.Length)
            {
                Vector3 next = task.ReachPoints[task.ReachIndex];
                if (!destination.ClearReach(task.ReachPoints[task.ReachIndex - 1], next, half, task.Item.transform, Body.Transform))
                    return Stop(task, "storage opening became blocked");
                Body.Hands.ReachTo(next, HandSpeed);
                task.Until = Time.time + 4f;
                return Vector3.zero;
            }
            Body.Hands.PutDown(point, Vector3.zero);
            task.Item.SetParent(destination.Room.ContentParent);
            task.Item.SavePosition();
            task.Released = true;
            task.Until = Time.time + 1.5f;
            return Vector3.zero;
        }

        private Vector3 Extract(Leg task)
        {
            if (source == null || !source.Available || Body.Hands.Item != task.Item) return Stop(task, "source changed during extraction");
            foreach (Door door in source.Doors)
            {
                if (door == null || !door.Opened) return Stop(task, "source door closed during extraction");
            }
            int ready = ReachReady(task, source);
            if (ready == 0) return Vector3.zero;
            if (ready < 0) return Stop(task, "item could not safely leave its source storage");
            if (++task.ReachIndex < task.ReachPoints.Length)
            {
                Vector3 next = task.ReachPoints[task.ReachIndex];
                if (!source.ClearReach(task.ReachPoints[task.ReachIndex - 1], next, originalHalf, task.Item.transform, Body.Transform))
                    return Stop(task, "extraction path became blocked");
                Body.Hands.ReachTo(next, HandSpeed);
                task.Until = Time.time + 4f;
                return Vector3.zero;
            }
            Body.Hands.ReachTo(null);
            // Recheck the destination after extraction, retaining the held item across this search.
            task.Extracting = false;
            probeIndex = System.Math.Max(0, probeIndex - 1);
            return Retry(task, "item removed; rechecking destination", false);
        }

        private bool WaitForClearance(Leg task)
        {
            if (task.BlockedUntil == 0)
            {
                task.BlockedUntil = Time.time + 3f;
                YourBuddyPlugin.Log.LogInfo("[store] Storage access blocked; waiting briefly for clear space");
            }
            task.ClearanceCheckAt = Time.time + .25f;
            return Time.time < task.BlockedUntil;
        }

        // Finish promptly when the hand arrives; four seconds is a timeout, not a fixed delay.
        private int ReachReady(Leg task, FurnitureStorage storage)
        {
            if (Time.time < task.ClearanceCheckAt) return 0;
            Vector3 target = task.ReachPoints[task.ReachIndex];
            if (!Items.ColliderBounds(task.Item.gameObject, out Bounds bounds)) return -1;
            if (!storage.ClearReach(bounds.center, target, task.Extracting ? (task.ReachIndex == 0 ? task.LiftHalf : originalHalf) : half, task.Item.transform, Body.Transform))
            {
                Body.Hands.ReachTo(bounds.center, HandSpeed);
                task.PausedReach = true;
                return WaitForClearance(task) ? 0 : -1;
            }
            if (task.PausedReach)
            {
                task.PausedReach = false;
                Body.Hands.ReachTo(target, HandSpeed);
                task.Until = Time.time + 4f;
            }
            task.BlockedUntil = task.ClearanceCheckAt = 0;
            if (Body.Hands.DistanceTo(target) <= .01f) return 1;
            return Time.time < task.Until ? 0 : -1;
        }

        private Vector3 Retry(Leg task, string why, bool nextContainer = true)
        {
            // Never reroute with an item partway through a cupboard opening.
            if (task.Reaching || task.Released || task.Extracting) return Stop(task, why);
            // Rotation can pin the item in world space; a new walking leg must carry it again.
            Body.Hands.ReachTo(null);
            if (!nextContainer && task.Stage == Stage.Deliver && task.Destination != null)
            {
                insertionFailures.TryGetValue(task.Destination, out int failures);
                insertionFailures[task.Destination] = ++failures;
                nextContainer = failures >= 3;
            }
            if (nextContainer && (task.Destination == null || !TryBeginRepack(task, task.Destination)))
            { containerIndex++; probeIndex = orientationIndex = 0; }
            YourBuddyPlugin.Log.LogInfo("[store] " + why + "; checking alternative storage");
            Leg search = new(this, task.Item, Here, Stage.Search);
            search.Node = search.StandPoint = Here;
            Body.Walk(search, null);
            return Vector3.zero;
        }

        private Vector3 Stop(Leg task, string why, bool placementFailure = false)
        {
            JobLog(task, "stop: " + why);
            CancelRepack(why);
            if (task.Item != null)
            {
                if (placementFailure) DeferPlacement(task.Item);
                else Skips.Skip(task.Item.transform, SkipSeconds);
            }
            // Skip the failed item, not all other storage work. docs/storing.md
            DueAt = Time.time + Interval;
            YourBuddyPlugin.Log.LogInfo("[store] " + why);
            return Leave("storing", why);
        }

        private enum Stage { Search, Fetch, Deliver }
        private sealed class Leg(StoreErrand errand, Grabbable item, Vector3 point, Stage stage, FurnitureStorage? destination = null)
            : ErrandLeg(point, destination != null && stage == Stage.Deliver ? destination.Root :
                stage == Stage.Fetch && errand.source != null && errand.source.Overflow == null ? errand.source.Root : item.transform)
        {
            internal readonly Grabbable Item = item;
            internal readonly Stage Stage = stage;
            internal readonly FurnitureStorage? Destination = destination;
            internal Vector3 LocalPoint, LiftHalf;
            internal float Until, BlockedUntil, ClearanceCheckAt, CarryReadyUntil;
            internal bool Reaching, Released, Extracting, PausedReach, Turning, Oriented, Completed;
            internal Vector3[] ReachPoints = [];
            internal int ReachIndex;
            public override string Name => Destination != null && Stage == Stage.Deliver ? Destination.Name : "loose item";
            private bool LoosePickup => Stage == Stage.Fetch && (errand.source == null || errand.source.Overflow != null);
            public override float Reach => LoosePickup ? 1.35f : Items.SnackReachDist;
            public override float ReachBelow => Items.SnackReachBelow;
            public override float[] StandOffs => LoosePickup ? PickupOffsets : ApproachOffsets;
            public override bool Waits => Stage == Stage.Search || Until > 0;
            public override Vector3 Approach(out bool wantMove) => errand.Approach(this, out wantMove);
            public override string Describe() => "storing " + Items.ItemLabelOf(Item);
            public override void Defer(float seconds) => errand.Defer(Own, seconds);
            public override bool Holds(Transform t) => base.Holds(t) || (Item != null && t == Item.transform) ||
                (Destination != null && t == Destination.Root) || (errand.source != null && t == errand.source.Root) ||
                (errand.repack != null && t == errand.repack.Root);
            public override void End()
            {
                if (Item != null) errand.JobLog(this, Completed ? "completed" : "leg ended/interrupted");
                if (!Completed) errand.CancelRepack("leg ended before verified placement");
                if (errand.Body.Hands.Item == Item && Item != null &&
                    !FurnitureStorage.PutDownSafely(errand.Body.Hands, errand.Body.Transform,
                        StorageShape.Half(errand.shapeHalf, Item.transform.rotation)))
                {
                    errand.Body.Hands.Release();
                    YourBuddyPlugin.Log.LogInfo("[store] No clear floor destination; released at current position with physics restored, not stored");
                }
                if (Completed) errand.doorsIdleUntil = Time.time + 8f;
                else errand.CloseStorageDoors();
            }
        }
    }
}

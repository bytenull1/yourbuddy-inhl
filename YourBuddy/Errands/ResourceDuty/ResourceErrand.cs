using System;
using System.Collections.Generic;
using NPC.Core;
using NPC.Core.Navigation;
using NPC.Core.Agents;
using NPC.Core.World;
using Space;
using UnityEngine;

namespace YourBuddy
{
    // One slot, one owner; each handoff remains a normal reach/carry leg. docs/resources.md
    internal sealed partial class ResourceErrand(IErrandBody body) : Errand(body)
    {
        // Tuning. docs/reference.md#resource-duties---resourceerrandcs
        private const float CheckSeconds = 30f;
        private const float ResourceSkipSeconds = 60f;
        private const float RunSeconds = 240f;
        private const float StagedRescanSeconds = 0.5f;
        private const int MaxCellTries = 8;
        private const int MaxStorageWalks = 3;
        private const float StorageRescanSeconds = 30f;
        private const int StorageChoices = 4;
        private const float SettleSeconds = 1.5f;
        private const float InsertSeconds = 5f;
        private const float SlotOffSeconds = 2f;
        private const float ReachMemoSeconds = 10f;
        private const float ResumeSeconds = 1f;
        private const float LegReachBelow = 0.5f;
        private const float ShopFrontCos = 0.7f;
        // Squared distances.
        private const float MovedSqr = 0.5f;
        private const float OutletSqr = 4f;
        private const float RejectedStorageSqr = 0.36f;
        private const float FloorMovedSqr = 0.04f;

        private StorageSearch? storage;
        private StorageSearch Storage => storage ??= new StorageSearch(this);
        private Grabbable? awaitingSafeDrop;
        private bool endReported;

        // One reach plan a frame (docs/behaviour.md §3): set when this frame's plan is spent.
        private bool planLater;
        private float parkedAt = -1f;
        private readonly List<ReachMemo> reachMemo = [];

        private readonly bool[] needed = new bool[ResourceKinds.Count];
        private readonly HashSet<int> beforePurchase = [];
        private readonly HashSet<Grabbable> stagedItems = [];
        private readonly List<ResourceContainer> candidates = [];
        private Comparison<ResourceContainer>? candidateOrder;
        private float stagedRefresh;
        private float deadline;
        private int kind;
        private int nextKind;
        private SpaceShip? ship;
        private ResourceController? loader;
        private ResourceController? gatesOf;
        private Airlock? loaderAirlock;
        private readonly List<Gate> loaderGates = [];
        private ResourceContainer? cell;
        private ItemDetector? slot;
        private bool startedLoading;
        private bool finishing;
        private bool restocking;
        // A refill is due: the fetched or bought cell goes into the loader, not to storage.
        private bool insertAfter;
        // A refill ended with charge left: the ejected cell is stored as a spare, not left in the airlock.
        private bool puttingAway;
        // The cell was already in the loader: an interruption leaves it there.
        private bool borrowed;
        private bool NeedsStorage => (restocking && !insertAfter) || puttingAway;
        private int stockGoal;
        private int purchasesMade;
        private int purchaseFrame = -1;
        private int purchasePrice;
        private int cheapestOverLimit;
        private float ejectedAt = -10f;
        private long purchaseSpent;
        private bool purchaseUncertain;
        private Vector3 purchaseOutlet;
        private int PurchasesLeft => Math.Max(0, ResourceRule.BuyQuantity - purchasesMade);
        private ResourceRule Rule => ResourceDuty.Settings.Rule(kind);
        private string Label => ResourceKinds.Label(kind);

        private bool Fits(ResourceContainer candidate) =>
            ship != null && candidate.LoadingSpeed > 0 && ResourceKinds.Levels(ship, kind, out int capacity, out int value) &&
            capacity - value >= candidate.LoadingSpeed;

        private float Percent(int index) =>
            ship != null && ResourceKinds.Levels(ship, index, out int capacity, out int value) && capacity > 0
                ? 100f * value / capacity
                : 100f;

        public override bool Enabled => ResourceDuty.Enabled && ResourceDuty.Owner == null && !Body.IsOutside;
        public override float Interval => CheckSeconds;
        public override float RetryDelay => CheckSeconds;
        protected override float SkipSeconds => ResourceSkipSeconds;
        protected override string Command => "Resources";
        protected override string Topic => "Resources";

        /// <summary>
        /// TryStart spent this frame's plan and goes on at the decider's next slow tick.
        /// </summary>
        public override bool Resuming => parkedAt >= 0f && Time.time - parkedAt <= ResumeSeconds;

        public override string Describe()
        {
            if (ResourceDuty.Owner == this) return "active - " + ResourceDuty.Status;
            if (ResourceDuty.Owner != null) return "waiting for another buddy's resource run";
            if (!ResourceDuty.Enabled) return "off - enable duties on the Resources page";
            if (Body.IsOutside) return "waiting to come inside";
            return base.Describe();
        }

        private bool PrepareLoader()
        {
            if (!GameInternals.ResourceAccess.Ready) return Waiting("loader support unavailable");
            loader = ship != null ? ship.CellController : null;
            if (loader == null || !loader.Initialized) return Waiting("ship loader is not initialized");
            slot = GameInternals.ResourceAccess.Slot(loader);
            if (slot == null || !Items.Loadable(slot)) return Waiting("ship loader room is unavailable");
            if (gatesOf != loader)
            {
                gatesOf = loader;
                loaderGates.Clear();
                loaderAirlock = loader.GetComponentInParent<Airlock>();
                if (loaderAirlock != null) loaderAirlock.GetComponentsInChildren(true, loaderGates);
            }
            return true;
        }

        // The loader is used from its own side of the airlock's floor-level doors; a stand point
        // beyond one is in the docking collar. docs/resources.md#errands-and-interruptions
        internal bool InLoaderRoom(Vector3 point, Vector3 target)
        {
            foreach (Gate gate in loaderGates)
            {
                if (gate == null || gate.transform.position.y >= target.y) continue;
                Vector3 forward = gate.transform.forward;
                Vector3 origin = gate.transform.position;
                if (Vector3.Dot(target - origin, forward) * Vector3.Dot(point - origin, forward) < 0f) return false;
            }
            return true;
        }

        // The loader's airlock lists what lies on its floor; that is not a slot.
        private bool IsChamber(ItemDetector detector) =>
            detector != slot && loaderAirlock != null && detector.GetComponentInParent<Airlock>() == loaderAirlock;

        // Whether `item` is placed in another slot. The loader deactivates a cell it takes, so no trigger exit
        // follows and its slot keeps listing every cell it ever ejected: only one lying in its box counts.
        private bool Staged(ItemDetector detector, Grabbable item) =>
            !IsChamber(detector) && detector.Items.Contains(item) &&
            (detector != slot || (slot.TryGetComponent(out BoxCollider box) && box.bounds.Contains(item.transform.position)));

        private Vector3 SlotPoint() =>
            slot == null ? Here : slot.TryGetComponent(out BoxCollider box) ? box.bounds.center : slot.transform.position;

        // Only with the loader prepared.
        private Leg LoaderLeg(Phase stage) => new(this, SlotPoint(), loader!.transform, stage);

        private string? RefillBlocked()
        {
            // Only a cell that is loading blocks; a spent or idle one is dealt with at the loader.
            // docs/invariants.md#resource-duties-own-only-their-cell
            ResourceContainer? inserted = loader != null ? GameInternals.ResourceAccess.Current(loader) : null;
            if (inserted == null || inserted.Value <= 0 || !GameInternals.ResourceAccess.IsLoading(loader!)) return null;

            // An inserted cell is inactive; an active one lies in the world while the loader still holds it.
            return inserted.gameObject.activeInHierarchy
                ? $"loader occupied: it is loading '{inserted.name}', which also lies at {inserted.transform.position:0.0}"
                : $"loader occupied: it is loading '{inserted.name}'";
        }

        // For the HUD while due and idle: what blocks a refill, else each enabled level.
        protected override string? WhyIdle
        {
            get
            {
                if (ship == null || loader == null) return null;
                string levels = "";
                for (int i = 0; i < ResourceKinds.Count; i++)
                {
                    if (ResourceDuty.Settings.Rule(i).Enabled) levels += $"{(levels.Length > 0 ? ", " : "")}{ResourceKinds.Label(i)} {Percent(i):0}%";
                }
                if (levels.Length == 0) return "no duty enabled";
                return RefillBlocked() is { } blocked ? $"waiting: {blocked} ({levels})"
                    : $"nothing to refill: {levels}, refills at {ResourceRule.Start}%";
            }
        }

        // Whether a duty is due now; the decider's readiness does the scheduling. Waiting() keeps the
        // Resources page's status current even when nothing can start.
        public override int Count(out float nearest)
        {
            nearest = 0f;
            if (!Enabled) return 0;
            if (awaitingSafeDrop != null && Body.Hands.Item == awaitingSafeDrop) return 1;
            if (Body.Hands.Item != null) return 0;
            ship = GameManager.Instance.PlayerShip;
            if (ship == null || (!Body.IsAboardPlayerShip() && string.IsNullOrEmpty(ship.Autopilot.DockedStation))) return 0;
            if (!PrepareLoader() || loader == null) return 0;
            string? blocked = RefillBlocked();
            nearest = Mathf.Sqrt(Items.FlatDistanceSq(loader.transform.position, Here));
            for (int i = 0; i < ResourceKinds.Count; i++)
            {
                ResourceRule rule = ResourceDuty.Settings.Rule(i);
                if (!rule.Enabled) continue;
                if (rule.NeedsRefill(Percent(i), needed[i]))
                {
                    if (blocked == null) return 1;
                    Waiting(blocked);
                }
                if (ResourceDuty.Settings.Buying && ShipStock(i) < ResourceRule.MinCells) return 1;
            }
            return 0;
        }

        public override bool TryStart(out string report)
        {
            bool started = TryStartDuty();
            report = started ? "started resource duties" : Resuming ? "still looking - one plan a frame" : ResourceDuty.Status;
            Last = report;
            return started;
        }

        private bool TryStartDuty()
        {
            planLater = false;
            parkedAt = -1f;
            if (awaitingSafeDrop != null && Body.Hands.Item == awaitingSafeDrop)
            {
                PutDownSafely();
                awaitingSafeDrop = Body.Hands.Item;
                return false;
            }
            awaitingSafeDrop = null;
            if (ResourceDuty.Owner != null || !ResourceDuty.Enabled || Body.IsOutside || Body.Hands.Item != null) return false;
            DueAt = Time.time + CheckSeconds;
            Skips.Prune();
            ship = GameManager.Instance.PlayerShip;
            if (ship == null || !GameInternals.ResourceAccess.Ready) return Waiting("ship or loader support unavailable");
            if (!Body.IsAboardPlayerShip() && string.IsNullOrEmpty(ship.Autopilot.DockedStation))
                return Waiting("Buddy must be aboard or at the docked station");
            if (!PrepareLoader() || loader == null || slot == null) return false;
            string? blocked = RefillBlocked();
            stagedRefresh = 0f;
            Body.LoadRoomOf(slot.transform);
            for (int n = 0; n < ResourceKinds.Count; n++)
            {
                kind = (nextKind + n) % ResourceKinds.Count;
                if (!Rule.Enabled) continue;
                needed[kind] = Rule.NeedsRefill(Percent(kind), needed[kind]);
                ResetRun();
                if (TryKind(blocked, out int stock)) return true;
                // Plan, FindCell and FindShop return at once while the frame's plan is spent.
                if (planLater) return Park();
                if (needed[kind] && blocked != null) Waiting(blocked);
                else if (needed[kind] || (ResourceDuty.Settings.Buying && stock < ResourceRule.MinCells))
                    ResourceDuty.Report(Storage.Failure ?? OverLimit() ?? $"Waiting: no reachable {Label} supply or affordable shop; {stock} usable cells aboard{(ResourceDuty.Settings.Buying ? "" : "; Buying is off")}.", Label);
            }
            return false;
        }

        // Starts a run for `kind`: a refill, else a restock.
        private bool TryKind(string? blocked, out int stock)
        {
            stock = 0;
            bool insert = needed[kind] && blocked == null && Plan(LoaderLeg(Phase.Insert), false);
            if (insert && UseInserted()) return true;
            if (insert && FindCell())
            {
                Start();
                return true;
            }
            stock = ShipStock(kind);
            if (!ResourceDuty.Settings.Buying || stock >= ResourceRule.MinCells) return false;

            restocking = true;
            insertAfter = insert;
            stockGoal = stock + ResourceRule.BuyQuantity;
            // A spent plan parks the try rather than waiting on a Search leg.
            if (!FindCell() && !FindShop() && (planLater || !BeginSearch())) return false;

            Start();
            return true;
        }

        // This frame's plan is spent. The decider resumes at its next slow tick; reaches already
        // planned are remembered and failed targets skipped, so the try moves on.
        private bool Park()
        {
            parkedAt = Time.time;
            DueAt = Time.time;
            ResourceDuty.Trace("Still looking - one plan a frame.", "scheduler");
            return false;
        }

        // Per kind tried, and so per run: what the last one left must not carry over.
        private void ResetRun()
        {
            restocking = false;
            insertAfter = false;
            puttingAway = false;
            borrowed = false;
            cheapestOverLimit = 0;
            purchasesMade = 0;
            Storage.Reset();
        }

        // An idle charged cell in the loader: loaded if it is the due kind, else taken out and stored first.
        private bool UseInserted()
        {
            ResourceContainer? inserted = loader != null ? GameInternals.ResourceAccess.Current(loader) : null;
            if (loader == null || slot == null || inserted == null || inserted.Value <= 0) return false;
            bool same = inserted.Type == ResourceKinds.Type(kind) && Fits(inserted);
            if (!Plan(LoaderLeg(same ? Phase.Insert : Phase.Clear), true)) return false;
            cell = inserted;
            borrowed = true;
            Start();
            if (!same)
            {
                nextKind = kind;
                ResourceDuty.Report($"Clearing the loader for {Label}: storing its {ResourceKinds.LabelOf(inserted.Type)} cell as a spare.", Label);
            }
            return true;
        }

        private string? OverLimit()
        {
            if (cheapestOverLimit <= 0) return null;
            int cash = NpcPlayer.Pilot != null ? NpcPlayer.Pilot.CashSystem.Cash : 0;
            string limit = cheapestOverLimit > ResourceDuty.Settings.Budget
                ? $"spending allowance is {ResourceDuty.Settings.Budget} (Limit on the Resources page)"
                : $"you have {cash}";
            return $"Waiting: a {Label} cell costs {cheapestOverLimit}; {limit}.";
        }

        private bool Waiting(string reason)
        {
            ResourceDuty.Report("Waiting: " + reason + ".", "scheduler");
            return false;
        }

        private void Start()
        {
            ResourceDuty.Owner = this;
            endReported = false;
            deadline = Time.time + RunSeconds;
            startedLoading = false;
            nextKind = (kind + 1) % ResourceKinds.Count;
            if (insertAfter) ResourceDuty.Report($"Getting {ResourceKinds.Article(kind)} {Label} cell for the loader at {Percent(kind):0.0}%.", Label);
            else if (restocking) ResourceDuty.Report($"Restocking {Label}: up to {PurchasesLeft} cells.", Label);
            else ResourceDuty.Report($"Refilling {Label} at {Percent(kind):0.0}% towards {ResourceRule.Target}%.", Label);
        }

        private int ShipStock(int ofKind)
        {
            int count = 0;
            foreach (ResourceContainer candidate in ResourceScan.Cells())
            {
                if (candidate == null || candidate.Data == null || candidate.Type != ResourceKinds.Type(ofKind) || candidate.Value <= 0) continue;
                if (NpcVessels.OwnerOfTransform(candidate.transform) != NavGraph.ShipOwner) continue;
                // A cell Buddy failed to reach lately is no stock: it must not stop a purchase.
                if (Skips.Has(candidate.transform)) continue;
                if (Items.Loadable(candidate) || (loader != null && GameInternals.ResourceAccess.Current(loader) == candidate)) count++;
            }
            return count;
        }

        private bool FindCell()
        {
            if (planLater) return false;
            if (Time.time >= stagedRefresh)
            {
                stagedItems.Clear();
                foreach (ItemDetector detector in SceneScan.ThisFrame<ItemDetector>())
                {
                    if (detector == null || IsChamber(detector)) continue;
                    foreach (Grabbable listed in detector.Items)
                    {
                        if (listed != null && Staged(detector, listed)) stagedItems.Add(listed);
                    }
                }
                stagedRefresh = Time.time + StagedRescanSeconds;
            }
            candidates.Clear();
            foreach (ResourceContainer candidate in ResourceScan.Cells())
            {
                if (candidate == null || candidate.Data == null || candidate.Type != ResourceKinds.Type(kind) || candidate.Value <= 0 || !Items.Loadable(candidate)) continue;
                if (restocking && NpcVessels.OwnerOfTransform(candidate.transform) == NavGraph.ShipOwner) continue;
                if (Skips.Has(candidate.transform)) continue;
                if (!restocking && !Fits(candidate)) continue;
                Grabbable item = candidate.GetComponent<Grabbable>();
                if (item == null || stagedItems.Contains(item) || item.IsGrabbed || item.restrictGrab || Body.TakenByAnother(item.transform)) continue;
                candidates.Add(candidate);
            }
            // Use partially spent cells first, preserving full cells when possible.
            candidates.Sort(candidateOrder ??= CompareCells);
            int attempts = 0;
            foreach (ResourceContainer candidate in candidates)
            {
                if (attempts++ >= MaxCellTries) break;
                Body.LoadRoomOf(candidate.transform);
                Grabbable item = candidate.GetComponent<Grabbable>();
                if (Items.TakeBlocker(item, Body.Hands.Item) != null) continue;
                if (NeedsStorage)
                {
                    StorageResult room = Storage.Check(item);
                    // The storage scan goes on next frame; another cell would not get further.
                    if (room == StorageResult.Pending) return false;
                    if (room == StorageResult.None) continue;
                }
                if (Plan(new Leg(this, Items.ItemTop(item), candidate.transform, Phase.Fetch), true))
                {
                    cell = candidate;
                    return true;
                }
                if (planLater) return false;
            }
            return false;
        }

        private int CompareCells(ResourceContainer a, ResourceContainer b) =>
            a.Value != b.Value ? a.Value.CompareTo(b.Value) :
                (a.transform.position - Here).sqrMagnitude.CompareTo((b.transform.position - Here).sqrMagnitude);

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        private bool FindShop()
        {
            if (planLater) return false;
            Player? player = NpcPlayer.Pilot;
            if (player == null || !GameInternals.ShopAccess.Ready || !ResourceDuty.Settings.Buying) return false;
            foreach (Shop shop in ResourceScan.Shops())
            {
                if (shop == null || !Items.Loadable(shop) || Skips.Has(shop.transform)) continue;
                Grabbable[]? stock = GameInternals.ShopAccess.Stock(shop);
                Transform? outlet = GameInternals.ShopAccess.Outlet(shop);
                if (stock == null) continue;
                for (int i = 0; i < stock.Length; i++)
                {
                    Grabbable product = stock[i];
                    if (product == null || !product.TryGetComponent(out ResourceContainer resource) ||
                        resource.Type != ResourceKinds.Type(kind) || (!restocking && !Fits(resource))) continue;
                    if (!ResourceDuty.Settings.CanBuy(Rule, product.BuyPrice, player.CashSystem.Cash))
                    {
                        if (!ResourceDuty.Settings.Paused && product.BuyPrice > 0 && (cheapestOverLimit == 0 || product.BuyPrice < cheapestOverLimit))
                            cheapestOverLimit = product.BuyPrice;
                        continue;
                    }
                    if (outlet == null) break;
                    Body.LoadRoomOf(shop.transform);
                    if (NeedsStorage)
                    {
                        StorageResult room = Storage.Check(product);
                        if (room == StorageResult.Pending) return false;
                        if (room == StorageResult.None) continue;
                    }
                    // The prefab faces its local -z: the buttons and the outlet sit there.
                    Leg buy = new(this, outlet.position, shop.transform, Phase.Buy)
                        { Shop = shop, Product = i, Front = Flat(shop.transform.forward * -1f) };
                    if (Plan(buy, true)) return true;
                    if (planLater) return false;
                }
            }
            return false;
        }

        /// <summary>
        /// One reach plan for `leg`, walked when `walk`. Budgeted to one a frame, except a single step from
        /// leg to leg. False when unreachable, or with planLater set when the plan waits for the next frame.
        /// Reachability is remembered for ReachMemoSeconds, so a resumed try does not plan the same leg again.
        /// </summary>
        private bool Plan(Leg leg, bool walk, bool budgeted = true)
        {
            if (planLater) return false;
            bool? known = Remembered(leg);
            if (known == false || (known == true && !walk)) return known.Value;

            int plans = 0;
            string? failure = null;
            SetOffResult result = SetOff(leg, (step, route) =>
            {
                if (walk) Begin(step, route, RetryDelay, step.Describe());
            }, ref plans, 1, ref failure, onePlanPerFrame: budgeted);
            if (result == SetOffResult.Later)
            {
                planLater = true;
                return false;
            }
            bool reached = result is SetOffResult.InReach or SetOffResult.Walking;
            if (result != SetOffResult.InReach) Remember(leg, reached);
            if (failure != null) ResourceDuty.Report($"Cannot reach {leg.Name}: {failure}.", leg.Name);
            return reached;
        }

        private bool? Remembered(Leg leg)
        {
            reachMemo.RemoveAll(memo => Time.time >= memo.Until);
            foreach (ReachMemo memo in reachMemo)
            {
                if (memo.Own == leg.Own && memo.Stage == leg.Stage && (memo.Point - leg.TargetPoint).sqrMagnitude < 0.01f) return memo.Reachable;
            }
            return null;
        }

        private void Remember(Leg leg, bool reachable) =>
            reachMemo.Add(new ReachMemo(leg.Own, leg.Stage, leg.TargetPoint, reachable, Time.time + ReachMemoSeconds));

        private bool PlanDelivery() => Storage.Walk() == StorageResult.Found || BeginSearch();

        // Waits where it stands while a search spans frames: the storage probes, or one plan a frame.
        private bool BeginSearch()
        {
            if ((!Storage.Pending && !planLater) || loader == null) return false;
            Leg wait = new(this, Here, loader.transform, Phase.Search);
            wait.Node = wait.StandPoint = Here;
            Body.Walk(wait, null);
            return true;
        }

        private void PutDownSafely()
        {
            if (Body.Hands.Item == null) return;
            Vector3 half = ResourceStorage.Clearance(Body.Hands.Extents);
            Vector3 origin = Body.Transform.position;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6f;
                Vector3 sample = origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .9f;
                if (!ResourceStorage.FindFloor(sample, out RaycastHit floor, shipOnly: false)) continue;
                Vector3 point = floor.point + Vector3.up * (half.y + .03f);
                if (!ResourceStorage.Clear(point, half, Body.Hands.Item.transform, shipOnly: false) ||
                    !NavProbe.WalkLos(origin, sample, .3f)) continue;
                Body.Hands.PutDown(point, Vector3.zero);
                return;
            }
            ResourceDuty.Settings.Paused = true;
            awaitingSafeDrop = Body.Hands.Item;
            ResourceDuty.Report("Duties paused: no clear floor nearby. Move Buddy to open floor, then toggle a resource to retry.", "storage");
        }

        internal void Tick()
        {
            if (ResourceDuty.Owner != this) return;
            if (Body.Transform == null)
            {
                End();
                return;
            }
            if (ResourceDuty.Settings.Paused || !Rule.Enabled || !YourBuddyPlugin.ConfigAutonomy.Value ||
                ship == null || loader == null || Body.Leg is not Leg || !Body.OnRoute || Time.time > deadline)
            {
                Cancel("paused, unavailable, or timed out");
                return;
            }
            if (restocking && !ResourceDuty.Settings.Buying)
            {
                Cancel("purchase permission disabled");
                return;
            }
            if (!restocking && !puttingAway && Percent(kind) >= ResourceRule.Target)
            {
                needed[kind] = false;
                PutAway("target reached");
            }
        }

        internal void Cancel(string why)
        {
            if (ResourceDuty.Owner != this || finishing) return;
            using NpcRegistry.ActingScope scope = NpcRegistry.Acting(
                Body.Transform != null ? Body.Transform.GetComponent<NpcAgent>() : null);
            endReported = true;
            ResourceDuty.Report(Label + ": " + why, Label);
            if (Body.Transform != null && Body.Leg is Leg) Body.FinishRoute();
            else End();
        }

        private void End()
        {
            if (finishing) return;
            finishing = true;
            if (!endReported && ResourceDuty.Owner == this)
                ResourceDuty.Report("Resource run interrupted; checking again shortly.", "scheduler");
            try
            {
                // Eject only our own cell when the job ends; saving does not end the job.
                if (!borrowed && loader != null && cell != null && GameInternals.ResourceAccess.Current(loader) == cell)
                    loader.TryTakeOut();
                if (cell != null && Body.Hands.Item == cell.GetComponent<Grabbable>()) PutDownSafely();
            }
            finally
            {
                cell = null;
                if (purchaseFrame >= 0)
                {
                    ResourceDuty.Settings.Buying = false;
                    ResourceDuty.Report("Buying off: check the interrupted purchase.", "purchase");
                }
                purchaseFrame = -1;
                beforePurchase.Clear();
                startedLoading = false;
                puttingAway = false;
                borrowed = false;
                Storage.Reset();
                if (ResourceDuty.Owner == this) ResourceDuty.Owner = null;
                DueAt = Time.time + CheckSeconds;
                finishing = false;
            }
        }

        private Vector3 Approach(Leg leg, out bool wantMove)
        {
            wantMove = false;
            planLater = false;
            Tick();
            // Tick may have ended the run or moved it to a new leg.
            if (ResourceDuty.Owner != this || Body.Leg != leg) return Vector3.zero;
            if (loader == null || slot == null) return Stop("loader unavailable");
            if (leg.Stage == Phase.Search)
            {
                bool ready = Body.Hands.Item != null ? Storage.Walk() == StorageResult.Found : FindCell() || FindShop();
                if (ready || Storage.Pending || planLater) return Vector3.zero;
                return Stop("no reachable supply or clear storage");
            }
            if (leg.Stage == Phase.Settle)
            {
                if (Time.time < ejectedAt + SettleSeconds) return Vector3.zero;
                if (cell == null || !cell.gameObject.activeInHierarchy) return Stop("ejected cell unavailable");
                if (!Plan(new Leg(this, Items.ItemTop(cell.GetComponent<Grabbable>()), cell.transform, Phase.Fetch), true, budgeted: false))
                    return Stop("cannot reach the ejected cell; it stays by the loader");
                return Vector3.zero;
            }
            ResourceContainer? current = GameInternals.ResourceAccess.Current(loader);
            if (leg.Stage == Phase.Clear)
            {
                if (!Body.StepIntoReach(leg, out Vector3 reach, out wantMove)) return reach;
                if (cell == null || current != cell) return Stop("the loader's cell changed");
                if (GameInternals.ResourceAccess.IsLoading(loader)) return Stop("the loader started loading");
                PutAway("clearing the loader");
                return Vector3.zero;
            }
            if (!restocking && current != null && current != cell && current.Value > 0) return Stop("the player is using the loader");
            if (leg.Stage == Phase.Insert && cell != null && current == cell)
            {
                if (Body.Hands.Item == cell.GetComponent<Grabbable>()) Body.Hands.Release();
                if (cell.Value <= 0) return Stop("cell empty; checking again shortly");
                if (!startedLoading)
                {
                    if (!Fits(cell))
                    {
                        PutAway("not enough room for another loading increment");
                        return Vector3.zero;
                    }
                    if (!GameInternals.ResourceAccess.IsLoading(loader)) loader.SwitchLoading();
                    startedLoading = true;
                }
                else if (!GameInternals.ResourceAccess.IsLoading(loader))
                {
                    PutAway("loader stopped");
                    return Vector3.zero;
                }
                Body.StandFacing(leg.TargetPoint);
                return Vector3.zero;
            }
            if (leg.Stage != Phase.Buy && (cell == null || !cell.gameObject.activeInHierarchy)) return Stop("cell unavailable");
            if (leg.Stage == Phase.Deliver && (leg.Floor == null ||
                (leg.Floor.TransformPoint(leg.FloorPoint) - leg.TargetPoint).sqrMagnitude > FloorMovedSqr))
            {
                if (!PlanDelivery()) return Stop("ship storage moved or became unavailable");
                return Vector3.zero;
            }
            if (!Body.StepIntoReach(leg, out Vector3 move, out wantMove)) return move;
            if (leg.Stage == Phase.Buy) return purchaseFrame >= 0 ? VerifyPurchase() : Buy(leg);
            // The guard above establishes the cell for both remaining phases.
            Grabbable item = cell!.GetComponent<Grabbable>();
            if (leg.Stage == Phase.Fetch)
            {
                foreach (ItemDetector detector in SceneScan.ThisFrame<ItemDetector>())
                {
                    if (detector != null && Staged(detector, item)) return Stop("cell was placed in another slot");
                }
                if (Items.TakeBlocker(item, Body.Hands.Item) != null || Body.TakenByAnother(item.transform) ||
                    (Items.ItemTop(item) - leg.TargetPoint).sqrMagnitude > MovedSqr) return Stop("cell moved or was taken");
                if (!Body.Hands.PickUp(item)) return Stop("could not pick up the cell");
                Storage.Reset();
                if ((restocking || puttingAway) && !InsertBought())
                {
                    if (!PlanDelivery()) return Stop("no reachable clear ship storage space");
                    return Vector3.zero;
                }
                Body.LoadRoomOf(slot.transform);
                if (!Plan(LoaderLeg(Phase.Insert), true, budgeted: false)) return Stop("cannot reach the loader");
            }
            else if (leg.Stage == Phase.Deliver)
            {
                if (Body.Hands.Item != item || !Body.IsAboardPlayerShip()) return Stop("cannot deliver the cell aboard");
                if (!ResourceStorage.Clear(leg.TargetPoint, ResourceStorage.Clearance(Body.Hands.Extents), item.transform))
                {
                    Storage.Rejected.Add(leg.TargetPoint);
                    if (!PlanDelivery()) return Stop("ship storage became blocked");
                    return Vector3.zero;
                }
                Body.Hands.PutDown(leg.TargetPoint, Vector3.zero);
                Room? destination = leg.Floor != null ? leg.Floor.GetComponentInParent<Room>() : null;
                if (destination != null)
                {
                    item.SetParent(destination.ContentParent);
                    item.SavePosition();
                }
                YourBuddyPlugin.Log.LogInfo($"[resources] Stored {Label} cell at {leg.TargetPoint}");
                if (NpcVessels.OwnerOfTransform(item.transform) != NavGraph.ShipOwner)
                {
                    ResourceDuty.Settings.Paused = true;
                    return Stop("delivered cell was not registered aboard; duties paused for inspection");
                }
                cell = null;
                int stock = ShipStock(kind);
                if (puttingAway) return Stop($"spare stored; {stock} usable cells aboard");
                if (stock >= stockGoal || PurchasesLeft <= 0) return Stop($"restock complete; {stock} usable cells aboard");
                deadline = Time.time + RunSeconds;
                if (FindCell() || FindShop() || BeginSearch()) return Vector3.zero;
                return Stop($"restock stopped; {stock} usable cells aboard, no reachable supply or affordable purchase");
            }
            else
            {
                // The loader registers a released cell a few frames later; the branch above takes it from there.
                if (Body.Hands.Item != item)
                {
                    if (leg.InsertUntil > 0 && Time.time <= leg.InsertUntil) return Vector3.zero;
                    return Stop(leg.InsertUntil > 0 ? "cell did not enter the loader" : "cell left Buddy's hands");
                }
                if (current != null && current != cell)
                {
                    loader.TryTakeOut();
                    ejectedAt = Time.time;
                    YourBuddyPlugin.Log.LogInfo("[resources] Took a spent cell out of the loader");
                    return Vector3.zero;
                }
                // Taking a cell out switches the slot off for a second.
                if (!slot.isActiveAndEnabled) return Time.time < ejectedAt + SlotOffSeconds ? Vector3.zero : Stop("loader slot is unavailable");
                if (leg.InsertUntil == 0)
                {
                    Body.Hands.ReachTo(SlotPoint());
                    leg.InsertUntil = Time.time + InsertSeconds;
                }
                else if (Time.time > leg.InsertUntil)
                {
                    return Stop("cell did not enter the loader");
                }
                Body.StandFacing(leg.TargetPoint);
            }
            return Vector3.zero;
        }

        // A cell got for a due refill goes straight into the loader, if it still may.
        private bool InsertBought()
        {
            needed[kind] = Rule.NeedsRefill(Percent(kind), needed[kind]);
            if (!insertAfter || !needed[kind] || RefillBlocked() != null || cell == null || !Fits(cell)) return false;
            restocking = false;
            ResourceDuty.Report($"Refilling {Label} at {Percent(kind):0.0}% with the new cell.", Label);
            return true;
        }

        // Ejects our charged cell and goes on to store it. docs/invariants.md#resource-duties-own-only-their-cell
        private void PutAway(string why)
        {
            if (loader == null || cell == null || GameInternals.ResourceAccess.Current(loader) != cell || cell.Value <= 0)
            {
                Cancel(why);
                return;
            }
            loader.TryTakeOut();
            ejectedAt = Time.time;
            startedLoading = false;
            puttingAway = true;
            borrowed = false;
            insertAfter = false;
            deadline = Time.time + RunSeconds;
            Storage.Reset();
            ResourceDuty.Report($"{Label}: {why}; storing the cell as a spare.", Label);
            // The eject pushes the cell out; it is fetched once it has come to rest.
            Leg settle = new(this, Here, loader.transform, Phase.Settle);
            settle.Node = settle.StandPoint = Here;
            Body.Walk(settle, null);
        }

        private Vector3 Buy(Leg leg)
        {
            Player? player = NpcPlayer.Pilot;
            Shop? shop = leg.Shop;
            Transform? outlet = shop != null ? GameInternals.ShopAccess.Outlet(shop) : null;
            if (player == null || shop == null || outlet == null || !shop.isActiveAndEnabled ||
                (outlet.position - leg.TargetPoint).sqrMagnitude > MovedSqr || player.Controller.IsControlling)
                return Stop("shop or player is busy");
            Grabbable[]? stock = GameInternals.ShopAccess.Stock(shop);
            if (stock == null || leg.Product < 0 || leg.Product >= stock.Length) return Stop("shop stock changed");
            Grabbable product = stock[leg.Product];
            if (product == null || !product.TryGetComponent(out ResourceContainer prefab) || prefab.Type != ResourceKinds.Type(kind) || (!restocking && !Fits(prefab)) ||
                !ResourceDuty.Settings.CanBuy(Rule, product.BuyPrice, player.CashSystem.Cash)) return Stop("purchase permission or funds changed");
            if (restocking && (ShipStock(kind) >= stockGoal || PurchasesLeft <= 0)) return Stop("restock quantity reached");
            // A cell may have appeared while Buddy walked to the shop; recheck before spending.
            stagedRefresh = 0f;
            if (FindCell()) return Vector3.zero;
            // The recheck waits for next frame's plan; nothing is spent meanwhile.
            if (planLater) return Vector3.zero;
            if (!ResourceDuty.Settings.CanBuy(Rule, product.BuyPrice, player.CashSystem.Cash))
                return Stop("no more cells affordable within your limits");
            if (NeedsStorage && Storage.Check(product) != StorageResult.Found)
            {
                if (BeginSearch()) return Vector3.zero;
                return Stop("no clear storage route; purchase cancelled before spending");
            }
            beforePurchase.Clear();
            foreach (ResourceContainer existing in SceneScan.ThisFrame<ResourceContainer>())
                beforePurchase.Add(existing.GetInstanceID());
            int cash = player.CashSystem.Cash;
            bool uncertain = false;
            try
            {
                GameInternals.ShopAccess.Buy(shop, leg.Product, player);
            }
            catch (Exception ex)
            {
                uncertain = true;
                YourBuddyPlugin.Log.LogWarning("[resources] Purchase failed: " + ex.Message);
            }
            long spent = Math.Max(0L, (long)cash - player.CashSystem.Cash);
            ResourceDuty.Settings.Budget = (int)Math.Max(0L, ResourceDuty.Settings.Budget - spent);
            purchaseFrame = Time.frameCount;
            purchasePrice = product.BuyPrice;
            purchaseSpent = spent;
            purchaseUncertain = uncertain;
            purchaseOutlet = outlet.position;
            return Vector3.zero;
        }

        private Vector3 VerifyPurchase()
        {
            if (Time.frameCount <= purchaseFrame) return Vector3.zero;
            purchaseFrame = -1;
            ResourceContainer? bought = null;
            int count = 0;
            foreach (ResourceContainer candidate in SceneScan.ThisFrame<ResourceContainer>())
            {
                if (beforePurchase.Contains(candidate.GetInstanceID()) || candidate.Type != ResourceKinds.Type(kind) ||
                    (candidate.transform.position - purchaseOutlet).sqrMagnitude > OutletSqr) continue;
                bought = candidate;
                count++;
            }
            beforePurchase.Clear();
            if (purchaseUncertain || purchaseSpent != purchasePrice || count != 1 || bought == null)
            {
                ResourceDuty.Settings.Paused = true;
                return Stop("purchase outcome unclear; duties paused. Check your cash and shop before resuming");
            }
            cell = bought;
            purchasesMade++;
            YourBuddyPlugin.Log.LogInfo($"[resources] Bought {Label} cell for {purchaseSpent}; allowance {ResourceDuty.Settings.Budget}");
            if (!Plan(new Leg(this, Items.ItemTop(bought.GetComponent<Grabbable>()), bought.transform, Phase.Fetch), true, budgeted: false))
            {
                ResourceDuty.Settings.Paused = true;
                return Stop("purchased cell is at the shop; duties paused because Buddy cannot reach it");
            }
            return Vector3.zero;
        }

        private Vector3 Stop(string why)
        {
            Cancel(why);
            return Vector3.zero;
        }

        private enum Phase { Fetch, Insert, Buy, Deliver, Search, Settle, Clear }

        // Whether a leg could be planned to, lately.
        private readonly struct ReachMemo(Transform own, Phase stage, Vector3 point, bool reachable, float until)
        {
            public readonly Transform Own = own;
            public readonly Phase Stage = stage;
            public readonly Vector3 Point = point;
            public readonly bool Reachable = reachable;
            public readonly float Until = until;
        }

        private sealed class Leg(ResourceErrand job, Vector3 point, Transform own, Phase stage) : ErrandLeg(point, own)
        {
            internal readonly Phase Stage = stage;
            internal Shop? Shop;
            internal int Product;
            internal float InsertUntil;
            internal Transform? Floor;
            internal Vector3 FloorPoint;
            // The shop's front, flat: a purchase is made from in front of it.
            internal Vector3 Front;
            public override string Name => Stage == Phase.Buy ? "resource shop" : Stage == Phase.Fetch ? "resource cell" : Stage == Phase.Deliver ? "ship storage" : "ship loader";
            public override float ReachBelow => LegReachBelow;
            public override bool StandAllowed(Vector3 point) =>
                base.StandAllowed(point) && (Stage is not (Phase.Insert or Phase.Clear) || job.InLoaderRoom(point, TargetPoint)) &&
                (Stage != Phase.Buy || InFront(point));
            // Within 45 degrees of the shop's front, so the walk up does not run into its side.
            internal bool InFront(Vector3 point)
            {
                Vector3 off = Flat(point - TargetPoint);
                float along = Vector3.Dot(off, Front);
                return along > 0f && along * along >= ShopFrontCos * ShopFrontCos * off.sqrMagnitude * Front.sqrMagnitude;
            }
            public override bool Waits => Stage == Phase.Search || Stage == Phase.Settle || InsertUntil > 0 || job.startedLoading || job.purchaseFrame >= 0;
            public override Vector3 Approach(out bool wantMove) => job.Approach(this, out wantMove);
            public override string Describe() =>
                (Stage == Phase.Clear ? "clearing the loader for " : job.puttingAway ? "putting away " : job.restocking ? "restocking " : "refilling ") + job.Label;
            public override void Defer(float seconds) => job.Defer(Own, seconds);
            public override bool Recover(string why)
            {
                if (Stage == Phase.Deliver && job.cell != null)
                {
                    job.Storage.Rejected.Add(TargetPoint);
                    if (job.PlanDelivery()) return true;
                }
                if (job.purchasesMade > 0 && job.cell != null)
                {
                    ResourceDuty.Settings.Paused = true;
                    why += "; purchased cell needs recovering, duties paused";
                }
                job.Cancel(why);
                return true;
            }
            public override void End() => job.End();
            public override bool Holds(Transform t) => base.Holds(t) ||
                (job.cell != null && t == job.cell.transform) || (job.loader != null && t == job.loader.transform);
        }
    }
}

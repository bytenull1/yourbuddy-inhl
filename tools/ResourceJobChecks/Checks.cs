using System;
using NPC.Core;
using NPC.Core.World;
using Space.Enums;
using UnityEngine;
using Xunit;
using Xunit.Abstractions;
using YourBuddy;

// One fact: the checks share static state and run in order.
public sealed class Checks(ITestOutputHelper output)
{
    [Fact]
    public void All()
    {
        int checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
        ItemDetector slot = new() { transform = new() };
        slot.Components[typeof(BoxCollider)] = new BoxCollider { bounds = new Bounds { extents = Vector3.one * .5f } };
        ResourceController loader = new() { transform = new(), Slot = slot };
        SpaceShip ship = new() { CellController = loader };
        GameManager.Instance.PlayerShip = ship;
        ship.OxygenController.Value = 20;
        ResourceDuty.Settings.Oxygen.Enabled = true;
        IErrandBody body = new();
        ResourceErrand job = new(body);
        Check(job.Count(out _) == 1, "empty initialized loader offers refill");
        // The stand point must be on the loader's side of the airlock's floor-level doors.
        Airlock airlock = new() { transform = new() };
        Transform outer = new() { position = new(0, 0, 4.6f), forward = new(0, 0, 1) };
        Transform hatch = new() { position = new(0, 1.53f, 3.75f), forward = new(0, 0, 1) };
        airlock.Children.Add(new Gate { transform = outer });
        airlock.Children.Add(new Gate { transform = hatch });
        loader.Components[typeof(Airlock)] = airlock;
        ResourceErrand roomJob = new(body);
        Check(roomJob.Count(out _) == 1, "loader with an airlock still offers refill");
        Vector3 slotCenter = new(1.12f, 0.85f, 3.9f);
        Check(!roomJob.InLoaderRoom(new(1.02f, 0, 4.69f), slotCenter), "stand point beyond the outer door is refused");
        Check(roomJob.InLoaderRoom(new(0.32f, 0, 3.9f), slotCenter) && roomJob.InLoaderRoom(new(-0.28f, 0, 3.2f), slotCenter),
            "stand points in the chamber are allowed, whichever side of the ceiling hatch");
        foreach (int value in new[] { 0, -1 })
        {
            loader.Current = new ResourceContainer { Value = value };
            Check(job.Count(out _) == 1, "a spent cell does not block refilling");
        }
        loader.Current = new ResourceContainer { Value = 10 };
        loader.Loading = true;
        Check(job.Count(out _) == 0, "a loader that is loading is not a refill candidate");
        Check(ResourceDuty.Status.Contains("loader occupied"), "occupied diagnostic is precise");
        job.DueAt = 0;
        Check(!job.TryStart(out string report) && report.Contains("loader occupied"), "start agrees with count and reports loader");
        Check(loader.Ejections == 0, "player cell left untouched");
        job.DueAt = 0;
        ResourceDuty.Settings.Buying = true;
        Check(job.Count(out _) == 1, "occupied loader does not block independent restocking");
        ResourceDuty.Settings.Buying = false;
        loader.Current = null;
        loader.Loading = false;
        loader.Initialized = false;
        Check(job.Count(out _) == 0 && !job.TryStart(out _), "uninitialized loader rejected by both entry points");
        loader.Initialized = true;
        job.DueAt = 0;
        slot.isActiveAndEnabled = false;
        Check(job.Count(out _) == 0 && !job.TryStart(out _), "unavailable slot rejected by both entry points");
        slot.isActiveAndEnabled = true;
        job.DueAt = 0;
        ResourceDuty.Owner = job;
        ResourceDuty.Status = "Refilling oxygen";
        Check(job.Describe().StartsWith("active - Refilling oxygen"), "active owner HUD is active");
        Check(job.Count(out _) == 0, "active owner cannot be scheduled twice");
        Check(new ResourceErrand(new()).Describe().Contains("another buddy"), "other buddy HUD waits for owner");
        ResourceDuty.Owner = null;
        ResourceDuty.Settings.Oxygen.Enabled = false;
        Check(job.Describe().Contains("Resources page") && !job.Describe().Contains("still works"), "disabled HUD points to actual controls");
        ResourceDuty.Settings.Oxygen.Enabled = true;
        body.IsOutside = true;
        Check(job.Describe().Contains("come inside") && job.Count(out _) == 0, "outside buddy waits indoors");
        body.IsOutside = false;
        // A cell staged after the cached scan must be rejected when Buddy arrives.
        Grabbable item = new() { transform = new() };
        ResourceContainer cell = new() { transform = item.transform };
        cell.Components[typeof(Grabbable)] = item;
        SceneScan.Snapshot<ResourceContainer>.Items = [cell];
        SceneScan.Snapshot<ItemDetector>.Items = [slot];
        Check(job.TryStart(out _), "available cell starts production job");
        Check(body.Leg != null, "fetch leg started");
        slot.Items.Add(item);
        body.Leg!.Approach(out _); // The preceding check established a leg.
        Check(body.Hands.Item == null && ResourceDuty.Owner == null, "newly staged cell is not picked up from another slot");
        foreach (string resource in new[] { "oxygen", "fuel", "energy" })
        {
            Reserve reserve = resource == "oxygen" ? ship.OxygenController : resource == "fuel" ? ship.FuelController : ship.ElectricityController;
            reserve.Value = 100;
            Check(BuddyResourceDebug.Drain([resource]).Contains("drained to 20%") && reserve.Value == 20 && reserve.LastAmount == 80, "drain uses selected game API");
            int calls = reserve.Calls;
            Check(BuddyResourceDebug.Drain([resource]).Contains("already") && reserve.Calls == calls, "already low reserves are unchanged");
            reserve.Value = 10;
            BuddyResourceDebug.Drain([resource]);
            Check(reserve.Value == 10 && reserve.Calls == calls, "drain never refills a lower reserve");
            reserve.Value = 100;
            reserve.Accept = false;
            Check(BuddyResourceDebug.Drain([resource]).Contains("Could not fully drain"), "failed game operation reported");
            reserve.Accept = true;
            reserve.Capacity = 0;
            Check(BuddyResourceDebug.Drain([resource]).Contains("No capacity"), "zero capacity rejected");
            reserve.Capacity = 100;
        }
        Check(BuddyResourceDebug.Drain([]).StartsWith("Usage"), "missing argument rejected");
        Check(BuddyResourceDebug.Drain(["food"]).StartsWith("Usage"), "unknown resource rejected");
        Check(BuddyResourceDebug.Drain(["fuel", "extra"]).StartsWith("Usage"), "extra argument rejected");
        ship.FuelController.Value = 100;
        Check(BuddyResourceDebug.Drain(["FUEL"]).Contains("drained"), "resource argument is case insensitive");
        // A spent cell is taken out once Buddy is at the loader with a charged one.
        ship.OxygenController.Value = 20;
        slot.Items.Clear();
        Grabbable freshItem = new() { transform = new() };
        ResourceContainer fresh = new() { transform = freshItem.transform };
        fresh.Components[typeof(Grabbable)] = freshItem;
        SceneScan.Snapshot<ResourceContainer>.Items = [fresh];
        loader.Current = new ResourceContainer { Value = 0 };
        int ejections = loader.Ejections;
        IErrandBody ejectBody = new();
        ResourceErrand ejectJob = new(ejectBody);
        Check(ejectJob.TryStart(out _) && ejectBody.Leg != null, "refill starts with a spent cell in the loader");
        ejectBody.Leg!.Approach(out _); // The preceding check established the fetch leg.
        Check(ejectBody.Hands.Item == freshItem && ejectBody.Leg != null, "charged cell picked up for the loader");
        ejectBody.Leg!.Approach(out _); // Picking up planned the insert leg.
        Check(loader.Ejections == ejections + 1 && loader.Current == null && ResourceDuty.Owner == ejectJob, "spent cell ejected, run continues");
        ejectJob.Cancel("check done");
        ResourceDuty.Settings.Paused = false; // The stub has no floor to put the cell down on.
        // A refill that reaches its target ejects the charged cell and stores it as a spare.
        Grabbable spareItem = new() { transform = new() };
        ResourceContainer spare = new() { transform = spareItem.transform };
        spare.Components[typeof(Grabbable)] = spareItem;
        SceneScan.Snapshot<ResourceContainer>.Items = [spare];
        loader.Current = null;
        ship.OxygenController.Value = 20;
        IErrandBody spareBody = new();
        ResourceErrand spareJob = new(spareBody);
        Check(spareJob.TryStart(out _) && spareBody.Leg != null, "refill starts for the spare check");
        spareBody.Leg!.Approach(out _); // Fetch leg: picks up and plans the insert leg.
        loader.Current = spare;
        spareBody.Leg!.Approach(out _); // Insert leg: the loader holds the cell; loading starts.
        ship.OxygenController.Value = 90;
        ejections = loader.Ejections;
        spareBody.Leg!.Approach(out _); // Target reached.
        Check(loader.Ejections == ejections + 1 && ResourceDuty.Owner == spareJob && spareBody.Leg!.Describe().StartsWith("putting away"),
            "charged cell ejected at the target and the run goes on to store it");
        Time.time += 2f;
        spareBody.Leg!.Approach(out _); // Settled: plans the fetch.
        Check(spareBody.Leg!.Name == "resource cell", "settled cell is fetched for storage");
        spareJob.Cancel("check done");
        ship.OxygenController.Value = 20;
        ResourceDuty.Settings.Paused = false;
        // An idle charged cell of the due kind is loaded where it is, not ejected.
        ResourceContainer idle = new() { transform = new(), Type = ResourceType.Oxygen };
        loader.Current = idle;
        ship.OxygenController.Value = 20;
        ejections = loader.Ejections;
        IErrandBody idleBody = new();
        ResourceErrand idleJob = new(idleBody);
        Check(idleJob.Count(out _) == 1 && idleJob.TryStart(out _) && idleBody.Leg!.Describe().StartsWith("refilling"),
            "an idle cell of the due kind is used");
        idleBody.Leg!.Approach(out _); // At the loader: loading starts.
        Check(loader.Loading && loader.Ejections == ejections && loader.Current == idle, "the idle cell is loaded in place");
        idleJob.Cancel("check done");
        Check(loader.Current == idle, "an interruption leaves a cell that was already in the loader");
        loader.Loading = false;
        // An idle charged cell of another kind is taken out and stored first.
        ResourceContainer other = new() { transform = new(), Type = ResourceType.Fuel };
        other.Components[typeof(Grabbable)] = new Grabbable { transform = other.transform };
        loader.Current = other;
        IErrandBody otherBody = new();
        ResourceErrand otherJob = new(otherBody);
        Check(otherJob.TryStart(out _) && otherBody.Leg!.Describe().StartsWith("clearing the loader"), "an idle cell of another kind is cleared");
        otherBody.Leg!.Approach(out _); // At the loader: eject.
        Check(loader.Ejections == ejections + 1 && otherBody.Leg!.Describe().StartsWith("putting away"), "the cleared cell is stored as a spare");
        otherJob.Cancel("check done");
        ResourceDuty.Settings.Paused = false;
        loader.Current = null;
        // With a refill due and no cell anywhere, a bought cell goes straight to the loader, needing no storage.
        Grabbable boughtItem = new() { transform = new() };
        ResourceContainer boughtCell = new() { transform = boughtItem.transform };
        boughtCell.Components[typeof(Grabbable)] = boughtItem;
        NpcVessels.Owners[boughtItem.transform] = "station";
        Grabbable product = new() { BuyPrice = 750 };
        product.Components[typeof(ResourceContainer)] = new ResourceContainer();
        SceneScan.Snapshot<Shop>.Items = [new Shop { transform = new(), Outlet = new(), Items = [product], Sells = boughtCell }];
        SceneScan.Snapshot<ResourceContainer>.Items = [];
        loader.Current = null;
        NpcPlayer.Pilot = new Player();
        ResourceDuty.Settings.Buying = true;
        ResourceDuty.Settings.Budget = 1500;
        IErrandBody refillBody = new();
        ResourceErrand refillJob = new(refillBody);
        Check(refillJob.TryStart(out _) && refillBody.Leg != null, "due refill with no cell starts a purchase without ship storage");
        refillBody.Leg!.Approach(out _); // The preceding check established the shop leg.
        Time.frameCount++;
        refillBody.Leg!.Approach(out _); // Buying kept the shop leg; this verifies the purchase.
        refillBody.Leg!.Approach(out _); // Verifying planned the fetch leg.
        Check(refillBody.Hands.Item == boughtItem && refillBody.Leg != null && refillBody.Leg.Describe().StartsWith("refilling"),
            "bought cell is taken to the loader, not to storage");
        refillJob.Cancel("check done");
        ResourceDuty.Settings.Paused = false;
        ResourceDuty.Settings.Buying = false;
        // A ship cell Buddy could not reach does not count as stock, so he still buys one.
        Grabbable stuckItem = new() { transform = new() };
        ResourceContainer stuck = new() { transform = stuckItem.transform };
        stuck.Components[typeof(Grabbable)] = stuckItem;
        SkipList.Skipped.Add(stuckItem.transform);
        SceneScan.Snapshot<ResourceContainer>.Items = [stuck];
        ship.OxygenController.Value = 20;
        ResourceDuty.Settings.Buying = true;
        NpcPlayer.Pilot = new Player();
        ResourceDuty.Settings.Budget = 1500;
        IErrandBody stuckBody = new();
        ResourceErrand stuckJob = new(stuckBody);
        Check(stuckJob.TryStart(out _) && stuckBody.Leg != null && stuckBody.Leg.Name == "resource shop",
            "an unreachable ship cell does not block buying");
        stuckJob.Cancel("check done");
        ResourceDuty.Settings.Paused = false;
        ResourceDuty.Settings.Buying = false;
        SkipList.Skipped.Clear();
        SceneScan.Snapshot<Shop>.Items = [];
        NpcPlayer.Pilot = null;
        // A shop cell priced over the allowance is named in the report, not hidden behind "no supply".
        NpcPlayer.Pilot = new Player();
        Grabbable cylinder = new() { BuyPrice = 1050 };
        cylinder.Components[typeof(ResourceContainer)] = new ResourceContainer { Type = ResourceType.Oxygen };
        SceneScan.Snapshot<Shop>.Items = [new Shop { transform = new(), Items = [cylinder] }];
        SceneScan.Snapshot<ResourceContainer>.Items = [];
        ship.OxygenController.Value = 20;
        ResourceDuty.Settings.Buying = true;
        ResourceDuty.Settings.Budget = 100;
        ResourceErrand shopJob = new(new IErrandBody());
        Check(!shopJob.TryStart(out string priceReport) && priceReport.Contains("costs 1050") && priceReport.Contains("allowance is 100"),
            "price over the allowance is reported");
        ResourceDuty.Settings.Buying = false;
        SceneScan.Snapshot<Shop>.Items = [];
        // One reach plan a frame: the loader check spends it, and the cell is planned on the next try.
        Grabbable farItem = new() { transform = new() };
        ResourceContainer far = new() { transform = farItem.transform };
        far.Components[typeof(Grabbable)] = farItem;
        SceneScan.Snapshot<ResourceContainer>.Items = [far];
        loader.Current = null;
        ship.OxygenController.Value = 20;
        IErrandBody farBody = new() { Near = false };
        ResourceErrand farJob = new(farBody);
        Check(!farJob.TryStart(out string parked) && farJob.Resuming && farBody.Plans == 1 && farBody.Leg == null && parked.Contains("one plan a frame"),
            "a second plan in the same frame waits");
        Time.frameCount++;
        Time.time += 0.3f;
        Check(farJob.TryStart(out _) && farBody.Plans == 2 && farBody.Leg != null && farBody.Leg.Name == "resource cell",
            "the resumed try reuses the loader's reach and plans the cell");
        farJob.Cancel("check done");
        // One plan a frame through the storage search: a restock parks, resumes, waits a frame for the spot's walk,
        // and re-plans once the route's switched-off room is loaded.
        Grabbable stationItem = new() { transform = new() };
        ResourceContainer stationCell = new() { transform = stationItem.transform };
        stationCell.Components[typeof(Grabbable)] = stationItem;
        NpcVessels.Owners[stationItem.transform] = "station";
        SceneScan.Snapshot<ResourceContainer>.Items = [stationCell];
        BoxCollider storageFloor = new() { transform = new() };
        BoxCollider routeFloor = new() { transform = new() };
        Room unloaded = new() { transform = new(), ContentEnabled = false };
        routeFloor.Components[typeof(Room)] = unloaded;
        ResourceStorage.Spots = [new(3, 0, 0)];
        ResourceStorage.Floor = p => p.x < 2.5f ? routeFloor : storageFloor;
        ResourceStorage.IsClear = true;
        loader.Current = null;
        ship.OxygenController.Value = 100;
        ResourceDuty.Settings.Buying = true;
        ResourceDuty.Settings.Budget = 1500;
        NpcPlayer.Pilot = new Player();
        IErrandBody storeBody = new() { Near = false, Rooms = [unloaded] };
        storeBody.Hands.Extents = Vector3.one * .15f;
        ResourceErrand storeJob = new(storeBody);
        Check(!storeJob.TryStart(out _) && storeJob.Resuming && storeBody.Plans == 1 && storeBody.Leg == null,
            "the storage spot spends the frame's plan and the restock parks");
        Time.frameCount++;
        Time.time += 0.3f;
        Check(storeJob.TryStart(out _) && storeBody.Plans == 2 && storeBody.Leg?.Name == "resource cell",
            "the resumed restock reuses the storage spot's reach and plans the cell");
        storeBody.PlannedFrame = Time.frameCount;
        storeBody.Leg!.Approach(out _); // Picks up; this frame's plan is already spent.
        Check(storeBody.Hands.Item == stationItem && storeBody.Plans == 2 && storeBody.Leg?.Name == "ship loader",
            "a spent plan sends the carried cell to wait, not to fail");
        Time.frameCount++;
        storeBody.Leg!.Approach(out _); // Search: plans the spot; its route loads the room.
        Check(unloaded.ContentEnabled && storeBody.Plans == 3 && storeBody.Leg?.Name == "ship loader",
            "the route's room loads and the re-check waits for the next frame");
        Time.frameCount++;
        storeBody.Leg!.Approach(out _); // Search: the re-check plans and walks.
        Check(storeBody.Plans == 4 && storeBody.Leg?.Name == "ship storage", "the re-checked spot is walked to");
        storeJob.Cancel("check done");
        ResourceDuty.Settings.Paused = false;
        ResourceDuty.Settings.Buying = false;
        ResourceStorage.Spots = [];
        ResourceStorage.Floor = p => null;
        ResourceStorage.IsClear = false;
        // The slot keeps listing a cell it ejected (inserting deactivates it): only a cell in its box is queued there.
        Grabbable ejectedItem = new() { transform = new() { position = new(5, 0, 0) } };
        ResourceContainer ejectedCell = new() { transform = ejectedItem.transform };
        ejectedCell.Components[typeof(Grabbable)] = ejectedItem;
        slot.Items.Add(ejectedItem);
        SceneScan.Snapshot<ResourceContainer>.Items = [ejectedCell];
        SceneScan.Snapshot<ItemDetector>.Items = [slot];
        loader.Current = null;
        ship.OxygenController.Value = 20;
        IErrandBody ejectedBody = new();
        ResourceErrand ejectedJob = new(ejectedBody);
        Check(ejectedJob.TryStart(out _) && ejectedBody.Leg?.Name == "resource cell", "a cell the slot still lists after ejecting it is a candidate");
        ejectedBody.Leg!.Approach(out _); // The preceding check established the fetch leg.
        Check(ejectedBody.Hands.Item == ejectedItem, "a cell the slot still lists after ejecting it is picked up");
        ejectedJob.Cancel("check done");
        ResourceDuty.Settings.Paused = false;
        slot.Items.Clear();
        // How a run ends. Every exit leaves the player's cell alone and takes back only Buddy's own.
        void Step(IErrandBody walker)
        {
            if (walker.Leg == null) throw new InvalidOperationException("no leg to step");
            walker.Leg.Approach(out _);
        }
        void Calm()
        {
            ResourceDuty.Owner = null;
            ResourceDuty.Settings.Paused = false;
            ResourceDuty.Settings.Buying = false;
            ResourceStorage.Floor = p => null;
            ResourceStorage.IsClear = false;
            ship.OxygenController.Value = 20;
            loader.Current = null;
            loader.Loading = false;
            slot.isActiveAndEnabled = true;
            slot.Items.Clear();
            SkipList.Skipped.Clear();
            SceneScan.Snapshot<Shop>.Items = [];
            NpcPlayer.Pilot = null;
        }
        // A refill run with a fresh cell at x 2; with `carry`, Buddy has picked it up and walks to the loader.
        (ResourceErrand Job, IErrandBody Body, Grabbable Item, ResourceContainer Cell) Refill(bool carry = true)
        {
            Calm();
            Grabbable item = new() { transform = new() { position = new(2, 0, 0) } };
            ResourceContainer fresh = new() { transform = item.transform, Type = ResourceType.Oxygen };
            fresh.Components[typeof(Grabbable)] = item;
            SceneScan.Snapshot<ResourceContainer>.Items = [fresh];
            SceneScan.Snapshot<ItemDetector>.Items = [slot];
            IErrandBody walker = new();
            ResourceErrand run = new(walker);
            Check(run.TryStart(out _) && walker.Leg?.Name == "resource cell", "setup: the run fetches the cell");
            if (carry)
            {
                Step(walker);
                Check(walker.Hands.Item == item && walker.Leg?.Name == "ship loader", "setup: the cell is carried to the loader");
            }
            return (run, walker, item, fresh);
        }
        // Buddy's cell in the loader and loading.
        (ResourceErrand Job, IErrandBody Body, Grabbable Item, ResourceContainer Cell) Loading()
        {
            var run = Refill();
            Step(run.Body); // Reaches into the slot.
            loader.Current = run.Cell;
            Step(run.Body); // The loader registered the cell: loading starts.
            Check(loader.Loading && run.Body.Hands.Item == null && ResourceDuty.Owner == run.Job, "setup: the loader loads Buddy's cell");
            return run;
        }

        var player = Refill();
        ResourceContainer theirs = new() { transform = new(), Type = ResourceType.Oxygen, Value = 50 };
        loader.Current = theirs;
        int ejectedBefore = loader.Ejections;
        ResourceStorage.Floor = p => new BoxCollider { transform = new() };
        ResourceStorage.IsClear = true;
        Step(player.Body);
        Check(ResourceDuty.Owner == null && loader.Current == theirs && loader.Ejections == ejectedBefore && player.Body.Hands.Item == null &&
            ResourceDuty.Status.Contains("the player is using the loader"), "a cell the player loaded is left in; Buddy sets his own down");

        var paused = Loading();
        ejectedBefore = loader.Ejections;
        ResourceDuty.Settings.Paused = true;
        Step(paused.Body);
        Check(ResourceDuty.Owner == null && loader.Ejections == ejectedBefore + 1 && loader.Current == null &&
            ResourceDuty.Status.Contains("paused, unavailable, or timed out"), "pausing ends the run and takes Buddy's own cell back out");

        var late = Loading();
        ejectedBefore = loader.Ejections;
        Time.time += 239f;
        Step(late.Body);
        Check(ResourceDuty.Owner == late.Job, "a run inside its four minutes goes on");
        Time.time += 2f;
        Step(late.Body);
        Check(ResourceDuty.Owner == null && loader.Ejections == ejectedBefore + 1, "a run past its four minutes ends and takes its cell out");

        var stopped = Loading();
        ejectedBefore = loader.Ejections;
        loader.Loading = false;
        Step(stopped.Body);
        Check(loader.Ejections == ejectedBefore + 1 && ResourceDuty.Owner == stopped.Job && stopped.Body.Leg?.Describe() == "putting away oxygen" &&
            ResourceDuty.Status.Contains("loader stopped; storing the cell as a spare"), "a loader switched off mid-load: Buddy takes his cell to store it");
        stopped.Job.Cancel("check done");

        var target = Loading();
        ejectedBefore = loader.Ejections;
        ship.OxygenController.Value = 79;
        Step(target.Body);
        Check(ResourceDuty.Owner == target.Job && loader.Ejections == ejectedBefore, "below the target the loading goes on");
        ship.OxygenController.Value = 80;
        Step(target.Body);
        Check(loader.Ejections == ejectedBefore + 1 && ResourceDuty.Status.Contains("target reached"), "at the target the cell comes out as a spare");
        target.Job.Cancel("check done");

        var empty = Refill();
        Step(empty.Body); // Reaches into the slot.
        empty.Cell.Value = 0;
        loader.Current = empty.Cell;
        ejectedBefore = loader.Ejections;
        Step(empty.Body);
        Check(ResourceDuty.Owner == null && loader.Ejections == ejectedBefore + 1 && ResourceDuty.Status.Contains("cell empty"),
            "an empty cell ends the run and comes back out");

        var full = Refill();
        Step(full.Body); // Reaches into the slot.
        full.Cell.LoadingSpeed = 50;
        ship.OxygenController.Value = 60;
        loader.Current = full.Cell;
        ejectedBefore = loader.Ejections;
        Step(full.Body);
        Check(loader.Ejections == ejectedBefore + 1 && ResourceDuty.Status.Contains("not enough room for another loading increment"),
            "a reserve with no room for one more increment does not start loading");
        full.Job.Cancel("check done");

        var slow = Refill();
        Step(slow.Body); // Reaches into the slot; the loader has five seconds to register the cell.
        slow.Body.Hands.Release();
        Time.time += 4.9f;
        Step(slow.Body);
        Check(ResourceDuty.Owner == slow.Job, "a released cell is given five seconds to register");
        Time.time += .2f;
        Step(slow.Body);
        Check(ResourceDuty.Owner == null && ResourceDuty.Status.Contains("cell did not enter the loader"), "a cell the loader never registers ends the run");

        // Only the loader legs keep their stand points on the loader's side of the airlock's doors.
        BoxCollider slotBox = (BoxCollider)slot.Components[typeof(BoxCollider)];
        Bounds onFloor = slotBox.bounds;
        slotBox.bounds = new Bounds { center = new(1.12f, .85f, 3.9f), extents = onFloor.extents };
        var room = Refill(carry: false);
        Vector3 beyond = new(1.02f, 0, 4.69f), chamber = new(.32f, 0, 3.9f);
        Check(room.Body.Leg?.StandAllowed(beyond) == true, "a fetch may stand anywhere");
        Step(room.Body);
        Check(room.Body.Leg?.StandAllowed(beyond) == false && room.Body.Leg?.StandAllowed(chamber) == true,
            "the insert leg stands only on the loader's side of the outer door");
        room.Job.Cancel("check done");
        slotBox.bounds = onFloor;

        var moved = Refill(carry: false);
        moved.Item.transform.position = new(3, 0, 0);
        Step(moved.Body);
        Check(ResourceDuty.Owner == null && moved.Body.Hands.Item == null && ResourceDuty.Status.Contains("cell moved or was taken"),
            "a cell moved a metre since planning is not picked up");

        // Purchases: a due refill with no cell anywhere buys one at x 0's shop for 750.
        (ResourceErrand Job, IErrandBody Body, Shop Shop) Shopping()
        {
            Calm();
            Grabbable sold = new() { transform = new() };
            ResourceContainer soldCell = new() { transform = sold.transform, Type = ResourceType.Oxygen };
            soldCell.Components[typeof(Grabbable)] = sold;
            NpcVessels.Owners[sold.transform] = "station";
            Grabbable product = new() { BuyPrice = 750 };
            product.Components[typeof(ResourceContainer)] = new ResourceContainer { Type = ResourceType.Oxygen };
            Shop shop = new() { transform = new(), Outlet = new(), Items = [product], Sells = soldCell };
            SceneScan.Snapshot<Shop>.Items = [shop];
            SceneScan.Snapshot<ResourceContainer>.Items = [];
            NpcPlayer.Pilot = new Player();
            ResourceDuty.Settings.Buying = true;
            ResourceDuty.Settings.Budget = 1500;
            IErrandBody walker = new();
            ResourceErrand run = new(walker);
            Check(run.TryStart(out _) && walker.Leg?.Name == "resource shop", "setup: the run walks to the shop");
            return (run, walker, shop);
        }
        Player Pilot()
        {
            if (NpcPlayer.Pilot == null) throw new InvalidOperationException("no pilot");
            return NpcPlayer.Pilot;
        }

        var busy = Shopping();
        Pilot().Controller.IsControlling = true;
        Step(busy.Body);
        Check(ResourceDuty.Owner == null && Pilot().CashSystem.Cash == 1000 && ResourceDuty.Status.Contains("shop or player is busy"),
            "nothing is bought while the player is at the controls");

        var poor = Shopping();
        Pilot().CashSystem.Cash = 700;
        Step(poor.Body);
        Check(ResourceDuty.Owner == null && Pilot().CashSystem.Cash == 700 && ResourceDuty.Status.Contains("purchase permission or funds changed"),
            "nothing is bought once the cell costs more than the player has");

        var denied = Shopping();
        ResourceDuty.Settings.Buying = false;
        Step(denied.Body);
        Check(ResourceDuty.Owner == null && Pilot().CashSystem.Cash == 1000 && ResourceDuty.Status.Contains("purchase permission disabled"),
            "switching Buying off ends a restock before it spends");

        var unclear = Shopping();
        unclear.Shop.Sells = null; // The shop takes no money and makes no cell.
        Step(unclear.Body);
        Time.frameCount++;
        Step(unclear.Body);
        Check(ResourceDuty.Owner == null && ResourceDuty.Settings.Paused && ResourceDuty.Settings.Buying &&
            ResourceDuty.Status.Contains("purchase outcome unclear"), "a purchase that spent nothing or made no cell pauses the duties");

        var stranded = Shopping();
        Step(stranded.Body);
        Time.frameCount++;
        Step(stranded.Body);
        Check(stranded.Body.Leg?.Name == "resource cell" && stranded.Body.Leg.Recover("stuck") && ResourceDuty.Owner == null &&
            ResourceDuty.Settings.Paused && ResourceDuty.Status.Contains("purchased cell needs recovering"),
            "a bought cell Buddy cannot walk to pauses the duties");

        var interrupted = Shopping();
        Step(interrupted.Body);
        Check(Pilot().CashSystem.Cash == 250 && ResourceDuty.Settings.Budget == 750, "a purchase is paid for and taken off the allowance");
        interrupted.Job.Cancel("check done");
        Check(!ResourceDuty.Settings.Buying && ResourceDuty.Status.Contains("Buying off: check the interrupted purchase"),
            "a run ended between paying and finding the cell switches Buying off");
        Calm();
        NpcVessels.Owners.Clear();
        GameManager.Instance.PlayerShip = null;
        Check(BuddyResourceDebug.Drain(["fuel"]).Contains("No player ship"), "no ship handled");
        output.WriteLine($"{checks} resource job/debug checks passed.");
    }
}

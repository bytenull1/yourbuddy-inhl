using System;
using NPC.Core;
using NPC.Core.Agents;
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
            ResourceStorage.ClearAt = null;
            ResourceStorage.Spots = [];
            ResourceStorage.Probes = int.MaxValue;
            Items.Blocker = item => null;
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
        // Storage: a restock carries a station cell at x -2 to the shortest of the first four reachable spots.
        BoxCollider shelf = new() { transform = new() };
        Room hold = new() { transform = new() };
        shelf.Components[typeof(Room)] = hold;
        shelf.transform.Components[typeof(Room)] = hold; // The stub's transform has its own components.
        // Every clearance check must ignore the carried or prospective cell.
        void Store(params Vector3[] spots)
        {
            Calm();
            ResourceStorage.Spots = [.. spots];
            ResourceStorage.Floor = p => shelf;
            ResourceStorage.ClearAt = (p, item) => item != null;
            ResourceStorage.Scans = 0;
        }
        // Steps frame by frame until Buddy walks to storage or the run ends; the frames it took.
        int ToStorage(IErrandBody walker)
        {
            for (int frames = 0; frames < 12; frames++)
            {
                if (walker.Leg == null || ResourceDuty.Owner == null || walker.Leg.Name == "ship storage") return frames;
                Time.frameCount++;
                Step(walker);
            }
            throw new InvalidOperationException("storage search did not settle");
        }
        // A restock for a station cell at x -2 measured as `size`, tried on up to `tries` frames.
        ResourceErrand Restocking(IErrandBody walker, out Grabbable item, Bounds? size = null, int tries = 4, ResourceErrand? job = null)
        {
            item = new() { transform = new() { position = new(-2, 0, 0) } };
            if (size.HasValue) NpcHands.Measured[item.gameObject] = size.Value;
            ResourceContainer stationCell = new() { transform = item.transform, Type = ResourceType.Oxygen };
            stationCell.Components[typeof(Grabbable)] = item;
            NpcVessels.Owners[item.transform] = "station";
            SceneScan.Snapshot<ResourceContainer>.Items = [stationCell];
            ship.OxygenController.Value = 100;
            ResourceDuty.Settings.Buying = true;
            ResourceDuty.Settings.Budget = 1500;
            NpcPlayer.Pilot = new Player();
            if (walker.Hands.Extents.x == 0) walker.Hands.Extents = Vector3.one * .15f;
            ResourceErrand run = job ?? new(walker);
            for (int n = 0; n < tries && !run.TryStart(out _); n++)
            {
                Time.frameCount++;
                Time.time += .3f;
            }
            return run;
        }
        // The restock has started and Buddy carries the cell aboard.
        (ResourceErrand Job, IErrandBody Body, Grabbable Item) Restock(IErrandBody walker, Bounds? size = null)
        {
            ResourceErrand run = Restocking(walker, out Grabbable item, size);
            // A storage check spanning frames waits on a search leg first.
            for (int n = 0; n < 4 && walker.Leg?.Name == "ship loader"; n++)
            {
                Time.frameCount++;
                Step(walker);
            }
            Check(walker.Leg?.Name == "resource cell", "setup: the restock fetches the station cell");
            Time.frameCount++;
            Step(walker);
            Check(walker.Hands.Item == item, "setup: Buddy carries the station cell");
            NpcVessels.Owners.Remove(item.transform); // Aboard from here on.
            return (run, walker, item);
        }
        float Spot(IErrandBody walker) => walker.Leg?.TargetPoint.x ?? float.NaN;

        Store(new(5, 0, 0), new(3, 0, 0), new(4, 0, 0), new(6, 0, 0), new(1, 0, 0));
        var shortest = Restock(new IErrandBody { Near = false });
        int plansBefore = shortest.Body.Plans;
        ToStorage(shortest.Body);
        Check(Spot(shortest.Body) == 3 && shortest.Body.Plans == plansBefore + 3 && ResourceStorage.Scans == 1,
            "the cell goes to the shortest of the first four reachable spots, one plan a frame, from one scan");
        Check(MathF.Abs(shortest.Body.Leg!.TargetPoint.y - .18f) < .001f && ResourceStorage.LastHalf.x == .15f &&
            ResourceStorage.LastIgnored == shortest.Item.transform, "the spot sits the held cell's half-height above the floor, clear of the cell itself");
        Step(shortest.Body);
        Check(shortest.Body.Hands.Placed?.x == 3 && shortest.Item.Parent == hold.ContentParent && shortest.Item.Saves == 1 &&
            ResourceDuty.Owner == null && ResourceDuty.Status.Contains("restock complete; 1 usable cells aboard"),
            "the stored cell is put down, parented to its room and saved, and the restock completes");

        Store(new Vector3(3, 0, 0));
        var stray = Restock(new IErrandBody());
        ToStorage(stray.Body);
        NpcVessels.Owners[stray.Item.transform] = "station";
        Step(stray.Body);
        Check(stray.Body.Hands.Placed != null && ResourceDuty.Owner == null && ResourceDuty.Settings.Paused &&
            ResourceDuty.Status.Contains("not registered aboard"), "a stored cell the ship does not own pauses the duties");
        NpcVessels.Owners.Remove(stray.Item.transform);

        Store(new Vector3(3, 0, 0));
        var blocked = Restock(new IErrandBody());
        ToStorage(blocked.Body);
        ResourceStorage.ClearAt = (p, item) => p.x < 2; // Only beside Buddy, where the run sets the cell down.
        Step(blocked.Body);
        Check(blocked.Body.Hands.Placed?.x < 2 && ResourceDuty.Owner == null && ResourceDuty.Status.Contains("ship storage became blocked"),
            "a spot blocked by the time Buddy arrives is not used; with no other the cell is set down beside him");

        Store(new Vector3(3, 0, 0));
        var shifted = Restock(new IErrandBody());
        ToStorage(shifted.Body);
        shelf.transform.position = new(0, 0, .5f);
        ResourceStorage.ClearAt = (p, item) => p.x < 2;
        Step(shifted.Body);
        shelf.transform.position = Vector3.zero;
        Check(shifted.Body.Hands.Placed?.x < 2 && ResourceDuty.Owner == null && ResourceDuty.Status.Contains("ship storage moved"),
            "a floor that moved since planning is searched again, not put down on");

        // A spot Buddy cannot get to is dropped and the next best is walked to, up to three walks a run.
        Store(new(5, 0, 0), new(3, 0, 0), new(4, 0, 0), new(6, 0, 0));
        var detour = Restock(new IErrandBody());
        ToStorage(detour.Body);
        Check(Spot(detour.Body) == 5 && detour.Body.Leg!.Recover("stuck") && ResourceDuty.Owner == detour.Job, "setup: Buddy walks to the first spot in reach");
        ToStorage(detour.Body);
        Check(detour.Body.Leg?.Name == "ship storage" && Spot(detour.Body) == 3, "a spot Buddy got stuck on is dropped for the next");
        Check(detour.Body.Leg!.Recover("stuck") && ResourceDuty.Owner == detour.Job && Spot(detour.Body) == 4, "a second stuck spot is dropped too");
        Check(detour.Body.Leg!.Recover("stuck") && ResourceDuty.Owner == null && ResourceDuty.Status.Contains("stuck"),
            "after three storage walks the run ends");

        Store(new(5, 0, 0), new(3, 0, 0));
        var back = Restock(new IErrandBody { Near = false });
        ToStorage(back.Body);
        Check(Spot(back.Body) == 3 && back.Body.Leg!.Recover("stuck"), "setup: Buddy walks to the shorter, second spot");
        ToStorage(back.Body);
        Check(Spot(back.Body) == 5 && ResourceDuty.Owner == back.Job, "a dropped spot falls back to a longer one found before it");
        back.Job.Cancel("check done");

        // Without a spot the restock does not start, and says whether storage is missing or out of reach.
        Store(new Vector3(3, 0, 0));
        ResourceStorage.ClearAt = (p, item) => false;
        Restocking(new IErrandBody(), out _);
        Check(ResourceDuty.Owner == null && ResourceDuty.Status.Contains("no clear floor storage found"), "with no clear floor the restock waits for room");
        Store(new Vector3(3, 0, 0));
        IErrandBody shut = new() { Near = false, FailAt = leg => leg.Name == "ship storage" ? "door shut" : null };
        Restocking(shut, out _, tries: 1);
        Check(ResourceDuty.Owner == null && shut.Plans == 1 && ResourceDuty.Status.Contains("Buddy cannot reach it"),
            "clear storage out of reach is reported as such");

        // A spot with no floor is skipped; a spot's switched-off room is loaded.
        Store(new(3, 0, 0), new(4, 0, 0));
        Room closet = new() { transform = new(), ContentEnabled = false };
        BoxCollider closetFloor = new() { transform = new() };
        closetFloor.Components[typeof(Room)] = closet;
        ResourceStorage.Floor = p => p.x < 3.5f ? null : closetFloor;
        var floorless = Restock(new IErrandBody { Rooms = [closet], Transform = new() { position = new(1, 0, 1) } });
        ToStorage(floorless.Body);
        Check(Spot(floorless.Body) == 4 && closet.ContentEnabled, "a spot with no floor is skipped and the chosen spot's room is loaded");
        Check(floorless.Body.Leg!.StandPoint.x == 1 && floorless.Body.Leg.StandPoint.z == 1, "a spot in reach is put down from where Buddy stands");

        // A spot in reach ends the scan: nothing beyond it is planned.
        Store(new(3, 0, 0), new(4, 0, 0));
        IErrandBody reacher = new() { Near = false, NearAt = leg => leg.Name == "ship storage" && leg.TargetPoint.x == 3 };
        Restock(reacher);
        Check(Spot(reacher) == 3 && reacher.Plans == 1, "a spot in reach is taken without planning the others");

        // A spot found unreachable lately is passed over without planning it again.
        Store(new(3, 0, 0), new(4, 0, 0));
        IErrandBody recall = new() { Near = false, FailAt = leg => leg.Name == "ship storage" && leg.TargetPoint.x == 3 ? "door shut" : null };
        var recalled = Restock(recall, new Bounds { size = Vector3.one * .5f, extents = Vector3.one * .25f });
        Check(Spot(recall) == 4 && ResourceStorage.Scans == 2, "after a rescan the spot remembered unreachable is skipped, and the next walked to at once");
        NpcHands.Measured.Clear();

        // The walk is measured along the route, not in a straight line.
        Store(new(3, 0, 0), new(0, 0, -4));
        var winding = Restock(new IErrandBody
        {
            Near = false,
            RouteAt = leg => leg.Name == "ship storage" && leg.TargetPoint.x == 3 ? [new(3, 0, 3)] : []
        });
        ToStorage(winding.Body);
        Check(Spot(winding.Body) == 0, "a spot near in a straight line but far by its route loses to a shorter walk");
        Store(new(0, 0, -4), new(3, 0, 0));
        var tail = Restock(new IErrandBody
        {
            Near = false,
            RouteAt = leg => leg.Name != "ship storage" ? [] : leg.TargetPoint.x == 3 ? [new(3, 0, .5f)] : [new(0, 0, -.5f)]
        });
        ToStorage(tail.Body);
        Check(Spot(tail.Body) == 3, "the route's last stretch to the spot counts");

        // Switched-off rooms under the route are loaded and the spot is planned again before walking.
        Room hallA = new() { transform = new(), ContentEnabled = false }, hallB = new() { transform = new(), ContentEnabled = false };
        BoxCollider hallFloorA = new() { transform = new() }, hallFloorB = new() { transform = new() };
        hallFloorA.Components[typeof(Room)] = hallA;
        hallFloorB.Components[typeof(Room)] = hallB;
        BoxCollider HallFloor(Vector3 p) =>
            p.x == 2 && MathF.Abs(p.z) < .01f ? hallFloorA : p.x == 2 && MathF.Abs(p.z - 2.25f) < .1f ? hallFloorB : shelf;
        Func<ErrandLeg, Vector3[]> viaHall = leg => leg.Name == "ship storage" && leg.TargetPoint.x == 2 ? [new(2, 0, 0)] : [];
        Store(new Vector3(2, 0, 3));
        ResourceStorage.Floor = HallFloor;
        IErrandBody hallWalker = new() { Near = false, Rooms = [hallA, hallB], RouteAt = viaHall };
        Restock(hallWalker);
        int hallPlans = hallWalker.Plans;
        int hallFrames = ToStorage(hallWalker);
        Check(hallA.ContentEnabled && hallB.ContentEnabled && Spot(hallWalker) == 2 && hallFrames == 1 && hallWalker.Plans == hallPlans + 1,
            "both rooms under the route load, and the spot is planned again on the next frame");
        Store(new Vector3(2, 0, 3));
        ResourceStorage.Floor = HallFloor; // Both rooms are loaded now.
        IErrandBody litWalker = new() { Near = false, RouteAt = viaHall };
        Restock(litWalker);
        int litPlans = litWalker.Plans;
        Check(ToStorage(litWalker) == 0 && litWalker.Plans == litPlans && Spot(litWalker) == 2, "a route over loaded rooms is walked at once");

        // A spot blocked once its route's rooms load is dropped, and the search goes on without it.
        hallA.ContentEnabled = hallB.ContentEnabled = false;
        Store(new(2, 0, 3), new(0, 0, -6));
        ResourceStorage.Floor = HallFloor;
        ResourceStorage.ClearAt = (p, item) => item != null && !(hallA.ContentEnabled && p.x == 2);
        var dropped = Restock(new IErrandBody { Near = false, Rooms = [hallA, hallB], RouteAt = viaHall });
        int droppedFrames = ToStorage(dropped.Body);
        Check(Spot(dropped.Body) == 0 && ResourceDuty.Owner == dropped.Job && droppedFrames == 2,
            "a spot blocked after its rooms load gives way to the next, walked to on the re-check's frame");
        hallA.ContentEnabled = hallB.ContentEnabled = false;
        Store(new(2, 0, 3), new(0, 0, -6));
        ResourceStorage.Floor = HallFloor;
        var unplanned = Restock(new IErrandBody
        {
            Near = false,
            Rooms = [hallA, hallB],
            RouteAt = viaHall,
            FailAt = leg => leg.Name == "ship storage" && leg.TargetPoint.x == 2 && hallA.ContentEnabled ? "furniture" : null
        });
        ToStorage(unplanned.Body);
        Check(Spot(unplanned.Body) == 0, "a spot that cannot be planned once its rooms load is dropped too");

        // The footprint is the cell's: a prospective cell's collider, else the held cell's extents.
        Store(new Vector3(3, 0, 0));
        IErrandBody measured = new();
        measured.Hands.Extents = Vector3.one * .2f;
        Restocking(measured, out Grabbable bigCell, new Bounds { size = Vector3.one * .5f, extents = Vector3.one * .25f });
        Check(ResourceStorage.LastHalf.x == .25f && ResourceStorage.Scans == 1, "a shop or station cell is checked with its own footprint");
        Time.frameCount++;
        Step(measured);
        Check(measured.Hands.Item == bigCell && ResourceStorage.LastHalf.x == .2f && ResourceStorage.Scans == 2,
            "a held cell's different footprint scans the spots again");
        Store(new Vector3(3, 0, 0));
        IErrandBody unmeasured = new();
        unmeasured.Hands.Extents = Vector3.one * .2f;
        Restocking(unmeasured, out _, new Bounds());
        Check(ResourceStorage.LastHalf.x == .15f, "a cell with no measurable collider gets the default footprint");
        NpcHands.Measured.Clear();

        // A search spanning frames scans again once its spots are half a minute old.
        Store(new(3, 0, 0), new(4, 0, 0));
        var stale = Restock(new IErrandBody { Near = false });
        Check(stale.Body.Leg?.Name == "ship loader" && ResourceStorage.Scans == 1, "setup: the walk's search waits for the next frame's plan");
        Time.time += 30f;
        ResourceStorage.ClearAt = (p, item) => item != null && p.x != 3;
        ToStorage(stale.Body);
        Check(ResourceStorage.Scans == 2 && Spot(stale.Body) == 4, "spots older than half a minute are scanned again, forgetting the old choice");

        // An interrupted search leaves nothing pending: the next try with nothing to fetch does not wait on it.
        Store(new(3, 0, 0), new(4, 0, 0));
        var pending = Restock(new IErrandBody { Near = false });
        Check(pending.Body.Leg?.Name == "ship loader", "setup: the walk's search waits for the next frame's plan");
        pending.Job.Cancel("interrupted");
        ResourceDuty.Settings.Paused = false;
        SceneScan.Snapshot<ResourceContainer>.Items = [];
        Time.frameCount++;
        Check(!pending.Job.TryStart(out _) && ResourceDuty.Owner == null, "an interrupted search does not hold the next try");


        // Which cells a refill takes: every cell below sorts before the good one and has one reason to be passed over.
        ResourceContainer MakeCell(Vector3 at, int value = 5, ResourceType type = ResourceType.Oxygen, bool grabbable = true)
        {
            Grabbable handle = new() { transform = new() { position = at } };
            ResourceContainer made = new() { transform = handle.transform, Type = type, Value = value };
            if (grabbable) made.Components[typeof(Grabbable)] = handle;
            return made;
        }
        Grabbable HandleOf(ResourceContainer c) => c.GetComponent<Grabbable>();
        Calm();
        ResourceContainer good = MakeCell(new(9, 0, 0), 10);
        ResourceContainer dataless = MakeCell(new(1, 0, 0)), fuelCell = MakeCell(new(1, 0, 0), type: ResourceType.Fuel), drained = MakeCell(new(1, 0, 0), 0);
        ResourceContainer off = MakeCell(new(1, 0, 0)), skipped = MakeCell(new(1, 0, 0)), grabbed = MakeCell(new(1, 0, 0));
        ResourceContainer restricted = MakeCell(new(1, 0, 0)), queued = MakeCell(new(1, 0, 0)), bare = MakeCell(new(1, 0, 0), grabbable: false);
        ResourceContainer oversized = MakeCell(new(1, 0, 0));
        dataless.Data = null;
        off.isActiveAndEnabled = false;
        SkipList.Skipped.Add(skipped.transform);
        HandleOf(grabbed).IsGrabbed = true;
        HandleOf(restricted).restrictGrab = true;
        oversized.LoadingSpeed = 90;
        ItemDetector elsewhere = new() { transform = new() };
        elsewhere.Items.Add(HandleOf(queued));
        SceneScan.Snapshot<ItemDetector>.Items = [null!, slot, elsewhere];
        SceneScan.Snapshot<ResourceContainer>.Items = [null!, dataless, fuelCell, drained, off, skipped, grabbed, restricted, queued, bare, oversized, good];
        IErrandBody picker = new();
        Check(new ResourceErrand(picker).TryStart(out _) && picker.Leg?.Own == good.transform,
            "a refill passes over dataless, other-kind, empty, inactive, skipped, held, restricted, queued, handle-less and oversized cells");
        Calm();

        ResourceContainer lowCharge = MakeCell(new(9, 0, 0), 5), fullFar = MakeCell(new(1, 0, 0), 10);
        SceneScan.Snapshot<ResourceContainer>.Items = [fullFar, lowCharge];
        IErrandBody saver = new();
        Check(new ResourceErrand(saver).TryStart(out _) && saver.Leg?.Own == lowCharge.transform, "a partly spent cell is used before a full one");
        Calm();
        ResourceContainer nearer = MakeCell(new(9, 0, 0), 10), farther = MakeCell(new(2, 0, 0), 10);
        SceneScan.Snapshot<ResourceContainer>.Items = [farther, nearer];
        IErrandBody chooser = new() { Transform = new() { position = new(10, 0, 0) } };
        Check(new ResourceErrand(chooser).TryStart(out _) && chooser.Leg?.Own == nearer.transform, "of equal cells the nearer to Buddy is taken");
        Calm();
        ResourceContainer opposite = MakeCell(new(-10, 0, 0), 10);
        SceneScan.Snapshot<ResourceContainer>.Items = [opposite, nearer];
        IErrandBody facing = new() { Transform = new() { position = new(10, 0, 0) } };
        Check(new ResourceErrand(facing).TryStart(out _) && facing.Leg?.Own == nearer.transform, "distance is measured from Buddy, whichever side a cell lies");
        Calm();

        // At most eight cells are tried a run, one plan a frame.
        ResourceContainer[] row = new ResourceContainer[9];
        for (int i = 0; i < row.Length; i++) row[i] = MakeCell(new(i + 1, 0, 0), 10);
        SceneScan.Snapshot<ResourceContainer>.Items = row;
        IErrandBody tryer = new() { Near = false, FailAt = leg => leg.Name == "resource cell" && leg.Own.position.x < 8.5f ? "blocked" : null };
        ResourceErrand tryJob = new(tryer);
        for (int n = 0; n < 12 && !tryJob.TryStart(out _); n++)
        {
            Time.frameCount++;
            Time.time += .3f;
        }
        Check(tryer.Leg == null && tryer.Plans == 9, "after eight unreachable cells the ninth is not tried");
        Calm();

        // Queued cells are looked up afresh each try.
        ResourceContainer queuedAgain = MakeCell(new(1, 0, 0), 10);
        elsewhere.Items.Clear();
        elsewhere.Items.Add(HandleOf(queuedAgain));
        SceneScan.Snapshot<ItemDetector>.Items = [slot, elsewhere];
        SceneScan.Snapshot<ResourceContainer>.Items = [queuedAgain];
        IErrandBody waiter = new();
        ResourceErrand waitJob = new(waiter);
        Check(!waitJob.TryStart(out _) && waitJob.DueAt > Time.time + 29f, "a cell queued in another slot is not taken; the next check is in half a minute");
        elsewhere.Items.Clear();
        Time.frameCount++;
        Check(waitJob.TryStart(out _) && waiter.Leg?.Own == queuedAgain.transform, "once it leaves that slot it is taken");
        Calm();

        // Ship stock: only usable cells of the kind aboard count against a restock.
        ship.OxygenController.Value = 100;
        ResourceDuty.Settings.Buying = true;
        ResourceContainer stockDataless = MakeCell(new(1, 0, 0)), stockFuel = MakeCell(new(1, 0, 0), type: ResourceType.Fuel);
        ResourceContainer stockEmpty = MakeCell(new(1, 0, 0), 0), stockOff = MakeCell(new(1, 0, 0)), stockStation = MakeCell(new(1, 0, 0));
        stockDataless.Data = null;
        stockOff.isActiveAndEnabled = false;
        NpcVessels.Owners[stockStation.transform] = "station";
        SceneScan.Snapshot<ResourceContainer>.Items = [null!, stockDataless, stockFuel, stockEmpty, stockOff, stockStation];
        ResourceErrand stockJob = new(new IErrandBody());
        Check(stockJob.Count(out _) == 1, "dataless, other-kind, empty, inactive and station cells are no stock");
        SceneScan.Snapshot<ResourceContainer>.Items = [stockOff, MakeCell(new(1, 0, 0))];
        Check(stockJob.Count(out _) == 0, "a usable cell aboard is stock");
        loader.Current = stockOff;
        SceneScan.Snapshot<ResourceContainer>.Items = [stockOff];
        Check(stockJob.Count(out _) == 0, "a cell inside the loader is stock");
        NpcVessels.Owners.Remove(stockStation.transform);
        Calm();

        // A try starts only for a free, enabled buddy indoors with empty hands, aboard or at the docked station.
        Room loaderRoom = new() { transform = new(), ContentEnabled = false, ContentParent = slot.transform };
        IErrandBody StartBody() => new() { Rooms = [loaderRoom] };
        void OneCell() => SceneScan.Snapshot<ResourceContainer>.Items = [MakeCell(new(1, 0, 0), 10)];
        OneCell();
        ResourceDuty.Owner = new ResourceErrand(new IErrandBody());
        Check(!new ResourceErrand(StartBody()).TryStart(out _), "no start while another buddy runs the duties");
        Calm();
        OneCell();
        ResourceDuty.Settings.Paused = true;
        Check(!new ResourceErrand(StartBody()).TryStart(out _), "no start while the duties are paused");
        Calm();
        OneCell();
        Check(!new ResourceErrand(new IErrandBody { IsOutside = true }).TryStart(out _), "no start outside");
        Calm();
        OneCell();
        IErrandBody busyHands = StartBody();
        busyHands.Hands.Item = new Grabbable { transform = new() };
        Check(!new ResourceErrand(busyHands).TryStart(out _), "no start with something in hand");
        Calm();
        OneCell();
        ship.Autopilot.DockedStation = "";
        Check(!new ResourceErrand(new IErrandBody { Aboard = false }).TryStart(out _) && ResourceDuty.Status.Contains("must be aboard"),
            "no start off the ship while undocked");
        IErrandBody onBoard = StartBody();
        Check(new ResourceErrand(onBoard).TryStart(out _) && loaderRoom.ContentEnabled, "aboard while undocked a try starts, with the loader's room loaded");
        Calm();
        OneCell();
        ship.Autopilot.DockedStation = "station";
        Check(new ResourceErrand(new IErrandBody { Aboard = false }).TryStart(out _), "at the docked station a try starts");
        Calm();

        // Kinds take turns: after an oxygen run the next try goes on from fuel, then energy.
        ResourceDuty.Settings.Fuel.Enabled = ResourceDuty.Settings.Energy.Enabled = true;
        ship.FuelController.Value = 20;
        ship.ElectricityController.Value = 20;
        SceneScan.Snapshot<ResourceContainer>.Items =
            [MakeCell(new(1, 0, 0), 10), MakeCell(new(2, 0, 0), 10, ResourceType.Fuel), MakeCell(new(3, 0, 0), 10, ResourceType.Energy)];
        IErrandBody turns = new();
        ResourceErrand turnJob = new(turns);
        Check(turnJob.TryStart(out _) && turns.Leg?.Describe() == "refilling oxygen", "setup: oxygen first");
        turnJob.Cancel("check done");
        Time.frameCount++;
        Check(turnJob.TryStart(out _) && turns.Leg?.Describe() == "refilling fuel", "the next try starts with fuel");
        turnJob.Cancel("check done");
        ship.FuelController.Value = 100;
        Time.frameCount++;
        Check(turnJob.TryStart(out _) && turns.Leg?.Describe() == "refilling energy", "with fuel full, the next after fuel is energy, not oxygen");
        turnJob.Cancel("check done");
        ResourceDuty.Settings.Fuel.Enabled = ResourceDuty.Settings.Energy.Enabled = false;
        ship.ElectricityController.Value = 100;
        Calm();

        // A cell Buddy could not set down is held, duties paused, until a later try finds floor for it.
        var held = Refill();
        held.Job.Cancel("interrupted");
        Check(held.Body.Hands.Item == held.Item && ResourceDuty.Settings.Paused && ResourceDuty.Status.Contains("no clear floor nearby"),
            "with no floor nearby Buddy keeps the cell and pauses the duties");
        ResourceDuty.Settings.Paused = false;
        Check(held.Job.Count(out _) == 1 && !held.Job.TryStart(out _) && held.Body.Hands.Item == held.Item && ResourceDuty.Settings.Paused,
            "the held cell is due first, and stays held while there is still no floor");
        ResourceDuty.Settings.Paused = false;
        ResourceStorage.Floor = p => shelf;
        ResourceStorage.IsClear = true;
        Check(!held.Job.TryStart(out _) && held.Body.Hands.Item == null && held.Body.Hands.Placed != null && ResourceDuty.Owner == null,
            "once there is floor the held cell is set down, and that try starts nothing");

        // More of the delivery: not aboard, a restock short of its goal, and Recover off other legs.
        Store(new Vector3(3, 0, 0));
        var ashore = Restock(new IErrandBody());
        ToStorage(ashore.Body);
        ashore.Body.Aboard = false;
        Step(ashore.Body);
        Check(ResourceDuty.Owner == null && ashore.Body.Hands.Placed?.x != 3 && ResourceDuty.Status.Contains("cannot deliver the cell aboard"),
            "off the ship the cell is not stored");
        Store(new Vector3(3, 0, 0));
        var shortOne = Restock(new IErrandBody());
        SkipList.Skipped.Add(shortOne.Item.transform); // The stored cell will not count as stock.
        ToStorage(shortOne.Body);
        Step(shortOne.Body);
        Check(shortOne.Body.Hands.Placed?.x == 3 && ResourceDuty.Owner == null && ResourceDuty.Status.Contains("restock stopped; 0 usable cells aboard"),
            "a restock short of its goal with nothing more to get stops and says so");
        Store(new Vector3(3, 0, 0));
        var goOn = Restock(new IErrandBody());
        SkipList.Skipped.Add(goOn.Item.transform);
        ResourceContainer second = MakeCell(new(-3, 0, 0), 10);
        NpcVessels.Owners[second.transform] = "station";
        SceneScan.Snapshot<ResourceContainer>.Items = [.. SceneScan.Snapshot<ResourceContainer>.Items, second];
        ToStorage(goOn.Body);
        Step(goOn.Body);
        Check(ResourceDuty.Owner == goOn.Job && goOn.Body.Leg?.Own == second.transform, "a restock short of its goal goes on to the next cell");
        goOn.Job.Cancel("check done");
        NpcVessels.Owners.Remove(second.transform);
        var lost = Refill(carry: false);
        ResourceStorage.Spots = [new(3, 0, 0)];
        ResourceStorage.Floor = p => shelf;
        ResourceStorage.ClearAt = (p, item) => true;
        Check(lost.Body.Leg!.Recover("stuck") && ResourceDuty.Owner == null && !ResourceDuty.Settings.Paused && ResourceDuty.Status.Contains("stuck"),
            "a fetch Buddy got stuck on ends the run unpaused; it is no storage walk");

        // A spot dropped in one run is tried again in the next.
        Store(new Vector3(3, 0, 0));
        var retry = Restock(new IErrandBody());
        ToStorage(retry.Body);
        Check(retry.Body.Leg!.Recover("stuck") && ResourceDuty.Owner == null, "setup: the only spot is dropped and the run ends");
        ResourceDuty.Settings.Paused = false;
        Time.time += 31f;
        Restocking(retry.Body, out _, job: retry.Job);
        Check(ResourceDuty.Owner == retry.Job, "a spot dropped in one run is tried again in a later one");
        retry.Job.Cancel("check done");

        // A storage check spanning frames holds the restock on a search leg; nothing is fetched yet.
        Store(new(3, 0, 0), new(4, 0, 0));
        IErrandBody slowStore = new() { Near = false, FailAt = leg => leg.Name == "ship storage" && leg.TargetPoint.x == 3 ? "door shut" : null };
        ResourceErrand slowJob = Restocking(slowStore, out _, tries: 1);
        Check(ResourceDuty.Owner == slowJob && slowStore.Leg?.Name == "ship loader" && slowStore.Plans == 1,
            "a storage check spanning frames holds the restock on a search leg, with no fetch planned");
        slowJob.Cancel("check done");

        // More of FindCell: a blocked cell is passed over; a cell's switched-off room is loaded.
        Calm();
        ResourceContainer pinned = MakeCell(new(1, 0, 0), 5), loose = MakeCell(new(2, 0, 0), 10);
        Items.Blocker = item => item == HandleOf(pinned) ? "under a box" : null;
        SceneScan.Snapshot<ResourceContainer>.Items = [pinned, loose];
        IErrandBody unpinner = new();
        Check(new ResourceErrand(unpinner).TryStart(out _) && unpinner.Leg?.Own == loose.transform, "a cell something else blocks is passed over");
        Calm();
        ResourceContainer roomed = MakeCell(new(1, 0, 0), 10);
        Room cellRoom = new() { transform = new(), ContentEnabled = false, ContentParent = roomed.transform };
        SceneScan.Snapshot<ResourceContainer>.Items = [roomed];
        Check(new ResourceErrand(new IErrandBody { Rooms = [cellRoom] }).TryStart(out _) && cellRoom.ContentEnabled,
            "a cell's switched-off room is loaded before planning to it");

        // More of TryStartDuty: each kind starts clean, reports why nothing started, and restocks only when it may.
        Calm();
        ship.OxygenController.Value = 100;
        ResourceDuty.Settings.Fuel.Enabled = true;
        ship.FuelController.Value = 20;
        ResourceDuty.Settings.Buying = true;
        ResourceDuty.Settings.Budget = 1500;
        NpcPlayer.Pilot = new Player();
        SceneScan.Snapshot<ResourceContainer>.Items = [MakeCell(new(1, 0, 0), 10, ResourceType.Fuel)];
        IErrandBody fueler = new();
        Check(new ResourceErrand(fueler).TryStart(out _) && fueler.Leg?.Describe() == "refilling fuel",
            "a failed oxygen restock leaves nothing behind for the fuel refill");
        ResourceDuty.Settings.Fuel.Enabled = false;
        ship.FuelController.Value = 100;
        Calm();
        ship.OxygenController.Value = 100;
        ResourceDuty.Settings.Buying = true;
        NpcPlayer.Pilot = new Player();
        SceneScan.Snapshot<ResourceContainer>.Items = [];
        Check(!new ResourceErrand(new IErrandBody()).TryStart(out _) && ResourceDuty.Status.Contains("no reachable oxygen supply") &&
            !ResourceDuty.Status.Contains("Buying is off"), "a restock with nothing to fetch or buy says so");
        Calm();
        SceneScan.Snapshot<ResourceContainer>.Items = [];
        Check(!new ResourceErrand(new IErrandBody()).TryStart(out _) && ResourceDuty.Status.Contains("Buying is off"),
            "a due refill with no cell says when Buying is off");
        Calm();
        loader.Current = MakeCell(new(0, 0, 0), 10);
        loader.Loading = true;
        ResourceDuty.Status = "";
        Check(!new ResourceErrand(new IErrandBody()).TryStart(out _) && ResourceDuty.Status.Contains("loader occupied"),
            "a try for a due refill names the busy loader");
        Store(new Vector3(3, 0, 0));
        ship.OxygenController.Value = 100;
        ResourceDuty.Settings.Buying = true;
        ResourceDuty.Settings.Budget = 1500;
        NpcPlayer.Pilot = new Player();
        ResourceContainer ashoreCell = MakeCell(new(2, 0, 0), 10);
        NpcVessels.Owners[ashoreCell.transform] = "station";
        SceneScan.Snapshot<ResourceContainer>.Items = [MakeCell(new(1, 0, 0), 10), ashoreCell];
        Check(!new ResourceErrand(new IErrandBody()).TryStart(out _), "one usable cell aboard is stock enough: no restock");
        ResourceDuty.Settings.Buying = false;
        SceneScan.Snapshot<ResourceContainer>.Items = [ashoreCell];
        Check(!new ResourceErrand(new IErrandBody()).TryStart(out _), "with Buying off there is no restock, even with a station cell at hand");
        NpcVessels.Owners.Remove(ashoreCell.transform);
        Calm();
        NpcVessels.Owners.Clear();
        GameManager.Instance.PlayerShip = null;
        Check(BuddyResourceDebug.Drain(["fuel"]).Contains("No player ship"), "no ship handled");
        output.WriteLine($"{checks} resource job/debug checks passed.");
    }
}

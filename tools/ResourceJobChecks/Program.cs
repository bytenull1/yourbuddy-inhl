using System;
using YourBuddy;
using UnityEngine;
using NPC.Core;
using NPC.Core.World;
using Space.Enums;

int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
ItemDetector slot = new() { transform = new() };
slot.Components[typeof(BoxCollider)] = new BoxCollider();
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
Check(job.Count(out _) == 0, "usable inserted cell counts as onboard stock");
Check(!job.TryStart(out _), "inserted stock prevents an unnecessary restock attempt");
job.DueAt = 0;
loader.Current.Type = ResourceType.Fuel;
Check(job.Count(out _) == 1, "a different resource in the loader does not count as oxygen stock");
loader.Current.Type = ResourceType.Oxygen;

loader.Current = new ResourceContainer { Value = 0 };
Check(job.Count(out _) == 1, "spent occupied loader does not block independent restocking");
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
GameManager.Instance.PlayerShip = null;
Check(BuddyResourceDebug.Drain(["fuel"]).Contains("No player ship"), "no ship handled");
Console.WriteLine($"{checks} resource job/debug checks passed.");

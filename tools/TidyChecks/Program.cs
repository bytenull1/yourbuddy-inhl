using System;
using System.Collections.Generic;
using NPC.Core;
using NPC.Core.Agents;
using NPC.Core.Navigation;
using UnityEngine;
using YourBuddy;

int checks = 0;
void Check(bool ok, string why) { if (!ok) throw new Exception(why); checks++; }
(Body Body, TidyErrand Errand, Grabbable Trash, TrashCan[] Bins) Setup(int n = 2)
{
    SceneScan.Data.Clear(); Time.time = 0;
    Grabbable trash = new();
    TrashCan[] bins = new TrashCan[n];
    for (int i = 0; i < n; i++) { bins[i] = new(); bins[i].transform.position = new(i + 2, 0, 0); }
    SceneScan.Data[typeof(Grabbable)] = new[] { trash };
    SceneScan.Data[typeof(TrashCan)] = bins;
    Body body = new();
    return (body, new TidyErrand(body), trash, bins);
}
void Arrive(Body body)
{
    var leg = body.Leg ?? throw new Exception("No leg");
    leg.Approach(out _); Time.time += 10; leg.Approach(out _);
}
var t = Setup();
t.Body.Blocked.Add(t.Bins[0].transform);
Check(t.Errand.TryStart(out _), "alternative bin starts a tidy round");
Check(t.Body.Leg!.Holds(t.Bins[1].transform) && !t.Body.Leg.Holds(t.Bins[0].transform), "unreachable nearest bin replaced");
Check(t.Body.Leg.Holds(t.Trash.transform), "fetch leg claims trash too");
Check(t.Body.Hands.Pickups == 0, "planning never picks up trash");
Arrive(t.Body);
Check(t.Body.Hands.Pickups == 1 && t.Body.Leg!.Own == t.Bins[1].transform, "pickup continues to reachable alternative");
Check(t.Body.Leg!.Holds(t.Trash.transform), "delivery retains item claim");

t = Setup(4);
foreach (var bin in t.Bins) t.Body.Blocked.Add(bin.transform);
Check(!t.Errand.TryStart(out _), "all blocked bins refuse job");
Check(t.Body.Plans == 3 && t.Body.Walks == 0 && t.Body.Hands.Pickups == 0, "blocked search is bounded without side effects");

t = Setup();
t.Body.Taken.Add(t.Bins[0].transform);
Check(t.Errand.TryStart(out _) && t.Body.Leg!.Holds(t.Bins[1].transform), "another buddy's bin excluded");

t = Setup();
t.Body.OtherVessel.Add(t.Bins[0].transform);
Check(t.Errand.TryStart(out _) && t.Body.Leg!.Holds(t.Bins[1].transform), "bin on other vessel excluded");

t = Setup(1);
Check(t.Errand.TryStart(out _), "initially reachable job starts");
t.Body.Blocked.Add(t.Bins[0].transform);
Arrive(t.Body);
Check(t.Body.Hands.Pickups == 0 && t.Body.Finished, "route blocked at pickup leaves trash untouched");

t = Setup(1);
t.Errand.TryStart(out _); t.Bins[0].Slot.isActiveAndEnabled = false; Arrive(t.Body);
Check(t.Body.Hands.Pickups == 0 && t.Body.Finished, "disabled slot never receives a pickup");
t = Setup(1);
t.Errand.TryStart(out _); Arrive(t.Body);
t.Bins[0].Slot.isActiveAndEnabled = false;
t.Body.Leg!.Approach(out _);
Check(t.Body.Finished && t.Body.Hands.Item == null, "slot disabled during delivery ends safely");
// Exercise the production storing state machine with a controllable furniture boundary.
(Body Body, StoreErrand Errand, Grabbable Item, FurnitureStorage Storage) StorageSetup()
{
    StorageShape.Orientations = 1; FurnitureStorage.SafeDropAvailable = true; Items.ClosedDoors = 0;
    SceneScan.Data.Clear(); FurnitureStorage.All.Clear(); StorageOverflow.Area = null; Time.time = 1; Time.frameCount++;
    Grabbable item = new() { CanTrash = false };
    item.Extras[typeof(AidKit)] = new AidKit();
    SceneScan.Data[typeof(Grabbable)] = new[] { item };
    FurnitureStorage storage = new(); FurnitureStorage.All.Add(storage);
    Body body = new();
    return (body,new StoreErrand(body),item,storage);
}
void Step(Body body) { Time.frameCount++; Time.time += 5; body.Leg?.Approach(out _); }
var s = StorageSetup();
Check(s.Errand.TryStart(out _), "store order starts without buying or low resource levels");
Step(s.Body);
Check(s.Body.Hands.Pickups == 0 && s.Body.Leg!.Holds(s.Storage.Root), "destination reserved before pickup");
Step(s.Body); Step(s.Body); Step(s.Body); Step(s.Body); Step(s.Body);
Check(s.Body.Hands.Stored == 1 && s.Item.Saved && s.Body.Finished, "completed storage releases and saves item");

s = StorageSetup(); s.Storage.Inside.Add(s.Item);
Check(s.Errand.Count(out _) == 0, "already stored item excluded");
s = StorageSetup(); SceneScan.Data[typeof(ItemDetector)] = new[] { new ItemDetector { Items = [s.Item] } };
Check(s.Errand.Count(out _) == 0, "machine contents excluded");
s = StorageSetup(); s.Item.Extras[typeof(Suit)] = new Suit();
Check(s.Errand.Count(out _) == 0, "suit left to equipment handling");
s = StorageSetup(); s.Item.CanTrash = true;
Check(s.Errand.Count(out _) == 0, "trash left for tidying");
s = StorageSetup(); s.Item.Signature = "TRASH_BOX";
Check(s.Errand.Count(out _) == 0, "sellable trash boxes left for selling");
s = StorageSetup(); s.Body.Aboard = false;
Check(!s.Errand.TryStart(out _) && s.Body.Hands.Pickups == 0, "station items not collected for ship storage");

s = StorageSetup(); s.Storage.Fits = false; s.Errand.TryStart(out _); Step(s.Body);
Check(s.Storage.Probes == 4 && s.Body.Hands.Pickups == 0, "storage search yields after four probes");
for (int i=0;i<10;i++) Step(s.Body);
Check(s.Body.Finished && s.Body.Hands.Pickups == 0, "full storage leaves item untouched");

s = StorageSetup(); s.Errand.TryStart(out _); Step(s.Body); s.Body.Blocked.Add(s.Storage.Root); Step(s.Body);
Check(s.Body.Hands.Pickups == 0, "route blocked before pickup leaves item untouched");
s = StorageSetup(); s.Errand.TryStart(out _); Step(s.Body); s.Storage.Inside.Add(s.Item); Step(s.Body);
Check(s.Body.Hands.Pickups == 0, "player putting item away cancels fetch");
s = StorageSetup(); s.Body.Taken.Add(s.Storage.Root);
Check(!s.Errand.TryStart(out _), "claimed storage not selected");
s = StorageSetup(); s.Storage.Name = "fridge";
Check(!s.Errand.TryStart(out _), "nonfood does not fill fridge");
s = StorageSetup(); s.Storage.Name = "fridge"; s.Item.Extras[typeof(Food)] = new Food();
Check(s.Errand.TryStart(out _), "food can use fridge");
s = StorageSetup(); s.Errand.TryStart(out _); Step(s.Body); Step(s.Body); s.Storage.ReachClear = false; Step(s.Body); Step(s.Body);
for (int i=0;i<22 && !s.Body.Finished;i++) Step(s.Body);
Check(s.Body.Hands.Stored == 0 && s.Body.Finished, "blocked opening never receives item");
s = StorageSetup(); s.Errand.TryStart(out _); Step(s.Body); s.Storage.IsClear = false; Step(s.Body);
Check(s.Body.Hands.Pickups == 0, "occupied destination detected before pickup");
s = StorageSetup(); s.Errand.TryStart(out _); Step(s.Body); Step(s.Body); s.Storage.Available = false; Step(s.Body);
Step(s.Body);
Check(s.Body.Finished && s.Body.Hands.Stored == 0, "removed room aborts delivery without storing");
// Full or blocked preferred destinations must not strand an otherwise possible job.
s = StorageSetup(); s.Storage.Fits = false;
FurnitureStorage spare = new(); FurnitureStorage.All.Add(spare);
s.Errand.TryStart(out _);
for (int i=0;i<11;i++) Step(s.Body);
Check(s.Body.Leg != null && s.Body.Leg.Holds(spare.Root), "full container falls back to another container");
s = StorageSetup(); spare = new(); FurnitureStorage.All.Add(spare);
s.Errand.TryStart(out _); Step(s.Body); Step(s.Body);
s.Storage.ReachClear = false;
for (int i=0;i<20 && s.Body.Leg != null && !s.Body.Leg.Holds(spare.Root);i++) Step(s.Body);
Check(s.Body.Hands.Pickups == 1 && s.Body.Hands.Item == s.Item && s.Body.Leg!.Holds(spare.Root),
    "blocked opening reroutes held item without dropping or repicking it");
s = StorageSetup(); s.Storage.IsSettled = false; s.Errand.TryStart(out _);
for (int i=0;i<6;i++) Step(s.Body);
Check(s.Body.Finished && s.Errand.DueAt == Time.time + s.Errand.Interval, "failed settling pauses briefly while its item is skipped");
Check(StoragePolicy.Rank(false,true,"fridge",true,false) == int.MaxValue, "resource cells cannot fill fridge");
Check(StoragePolicy.Rank(true,false,"fridge",false,false) < StoragePolicy.Rank(true,false,"cabinet",true,false), "food prefers fridge");
Check(StoragePolicy.Rank(false,true,"cabinet",true,false) < StoragePolicy.Rank(false,true,"chest",false,true), "matching supplies beat empty fallback");
Check(StoragePolicy.Rank(false,true,"chest",false,true) < StoragePolicy.Rank(false,true,"cabinet",false,true), "resource fallback prefers chest");
Check(StoragePolicy.Rank(false,false,"cabinet",false,true) < StoragePolicy.Rank(false,false,"cabinet",false,false), "empty container precedes mixed contents");
Check(StoragePolicy.Columns(.3f,.16f) == 0, "oversized footprint rejected");
Check(StoragePolicy.Columns(.3f,.15f) == 1, "exact fit yields one centred column");
Check(StoragePolicy.Columns(2f,.01f) == 5, "tiny items cannot create unbounded searches");
Check(StoragePolicy.Columns(.5f,.08f) > StoragePolicy.Columns(.5f,.2f), "slot density adapts to item size");
// Sorting promotes items to better homes; overflow never shuffles between rooms.
s = StorageSetup(); s.Item.Extras[typeof(Food)] = new Food(); s.Storage.Inside.Add(s.Item);
FurnitureStorage fridge = new() { Name = "fridge" }; FurnitureStorage.All.Add(fridge);
Check(s.Errand.Count(out _) == 1, "food in chest becomes sortable when a fridge exists");
s.Errand.TryStart(out _); Step(s.Body);
Check(s.Body.Leg!.Holds(s.Storage.Root) && s.Body.Leg.Holds(fridge.Root), "sorting claims source and destination");
for (int i=0;i<12 && !s.Body.Finished;i++) Step(s.Body);
Check(s.Body.Hands.Pickups == 1 && s.Body.Hands.Stored == 1 && s.Body.Finished, "sorting extracts and delivers one item");
s = StorageSetup(); s.Item.Extras[typeof(Food)] = new Food(); s.Storage.Inside.Add(s.Item); s.Storage.ReachClear = false;
FurnitureStorage.All.Add(new FurnitureStorage { Name = "fridge" });
s.Errand.TryStart(out _); for (int i=0;i<4;i++) Step(s.Body);
Check(s.Body.Hands.Pickups == 0, "blocked extraction leaves stored item untouched");
s = StorageSetup(); s.Item.Extras[typeof(Food)] = new Food(); s.Storage.Inside.Add(s.Item);
fridge = new() { Name = "fridge", Fits = false }; FurnitureStorage.All.Add(fridge);
FurnitureStorage overflow = new() { Name = "overflow area", Overflow = new StorageOverflow() }; StorageOverflow.Area = overflow;
s.Errand.TryStart(out _); for (int i=0;i<11;i++) Step(s.Body);
Check(s.Body.Finished && s.Body.Hands.Pickups == 0, "usable fallback storage stays put while preferred fridge is full");
s = StorageSetup(); s.Storage.Fits = false;
overflow = new() { Name = "overflow area", Overflow = new StorageOverflow() }; StorageOverflow.Area = overflow; overflow.Inside.Add(s.Item);
s.Errand.TryStart(out _); for (int i=0;i<11;i++) Step(s.Body);
Check(s.Body.Hands.Pickups == 0, "overflow item stays put when proper storage is full");
s = StorageSetup();
overflow = new() { Name = "overflow area", Overflow = new StorageOverflow() }; StorageOverflow.Area = overflow; overflow.Inside.Add(s.Item);
Check(s.Errand.TryStart(out _), "overflow item can return to proper storage");
for (int i=0;i<7 && !s.Body.Finished;i++) Step(s.Body);
Check(s.Body.Hands.Stored == 1, "overflow promotion completes");
Check(!StoragePolicy.ShouldMove(10,10), "equal category homes never shuffle");
Check(!StoragePolicy.ShouldMove(100,100), "overflow without a proper home is stable");
Check(StoragePolicy.ShouldMove(int.MaxValue,100), "nonfood in a fridge may move to overflow");
Check(StoragePolicy.OnOverflowGrid(.65f,-.65f), "grid centres are recognised across reloads");
Check(!StoragePolicy.OnOverflowGrid(.3f,.3f), "untidy floor clutter is not counted as placed overflow");
Check(!StoragePolicy.OnOverflowGrid(1.3f,0), "overflow never expands past its designated grid");
s = StorageSetup(); s.Item.Extras[typeof(Food)] = new Food(); s.Storage.Inside.Add(s.Item);
FurnitureStorage.All.Add(new FurnitureStorage { Name = "fridge" });
s.Errand.TryStart(out _); Step(s.Body); s.Storage.Root.position = new(2, 0, 0); Step(s.Body);
Check(s.Body.Finished && s.Body.Hands.Pickups == 0, "moved source cancels fetch before pickup");
s = StorageSetup(); s.Item.Extras[typeof(Food)] = new Food(); s.Storage.Inside.Add(s.Item);
FurnitureStorage.All.Add(new FurnitureStorage { Name = "fridge", Fits = false });
StorageOverflow.Area = new FurnitureStorage { Name = "overflow area", Overflow = new StorageOverflow(), Fits = false };
s.Errand.TryStart(out _); for (int i = 0; i < 22 && !s.Body.Finished; i++) Step(s.Body);
Check(s.Body.Finished && s.Body.Hands.Pickups == 0, "full home and overflow leave misplaced item untouched");
s = StorageSetup(); s.Storage.Inside.Add(s.Item);
Check(!s.Errand.TryStart(out string sortedReply) && sortedReply == "nothing needs storing or sorting", "already sorted reply describes both jobs");
s = StorageSetup(); s.Item.Extras.Clear();
Check(s.Errand.Count(out _) == 0 && !s.Errand.TryStart(out _), "unknown objects are neither autonomous nor ordered storage candidates");
s = StorageSetup(); s.Item.Extras.Clear(); s.Item.Signature = "TABLET_BROKEN";
Check(s.Errand.Count(out _) == 0, "broken tablet is not treated as useful storage");
s = StorageSetup(); s.Item.Extras.Clear(); s.Item.Extras[typeof(ResourceContainer)] = new ResourceContainer { Data = new SupplyData { value = 0 } };
Check(!StorageItems.CanStore(s.Item), "empty resource cell is not stored");
s.Item.Extras[typeof(ResourceContainer)] = new ResourceContainer { Data = new SupplyData { value = 1 } };
Check(StorageItems.CanStore(s.Item), "partly used resource cell remains useful");
s = StorageSetup(); s.Item.Extras.Clear(); s.Item.Extras[typeof(Food)] = new Food { Data = new SupplyData { usages = 0 } };
Check(!StorageItems.CanStore(s.Item), "used up food excluded independently of trash flag");
s = StorageSetup(); s.Item.Extras.Clear(); s.Item.Extras[typeof(SeedPack)] = new SeedPack();
Check(StorageItems.CanStore(s.Item), "seeds are useful supplies");
s = StorageSetup(); s.Item.Extras.Clear(); s.Item.Extras[typeof(RepairKit)] = new RepairKit();
Check(StorageItems.CanStore(s.Item), "stocked repair kits are useful supplies");
s.Item.Extras[typeof(RepairKit)] = new RepairKit { Data = null };
Check(!StorageItems.CanStore(s.Item), "uninitialised kit left alone");
s = StorageSetup(); s.Errand.TryStart(out _); Step(s.Body);
s.Item.Extras[typeof(AidKit)] = new AidKit { Data = new SupplyData { value = 0 } }; Step(s.Body);
Check(s.Body.Finished && s.Body.Hands.Pickups == 0, "supply consumed during approach is not picked up");
s = StorageSetup(); s.Errand.TryStart(out _);
Step(s.Body); Step(s.Body); Step(s.Body); Step(s.Body);
Time.time += .1f; Time.frameCount++; s.Body.Leg!.Approach(out _);
Check(s.Body.Hands.Stored == 1, "arrived hand releases without waiting four seconds");
s = StorageSetup(); s.Errand.TryStart(out _);
Step(s.Body); Step(s.Body); Step(s.Body); s.Storage.ReachClear = false; Step(s.Body);
Check(!s.Body.Finished && s.Body.Hands.Item == s.Item && s.Body.Hands.Stored == 0, "temporary obstruction waits while keeping item");
s.Storage.ReachClear = true; Time.time += .3f; Time.frameCount++; s.Body.Leg!.Approach(out _);
Time.time += .1f; Time.frameCount++; s.Body.Leg!.Approach(out _);
Check(s.Body.Hands.Stored == 1, "cleared obstruction resumes same placement");
s = StorageSetup(); s.Errand.TryStart(out _);
Step(s.Body); Step(s.Body); Step(s.Body); Step(s.Body); s.Storage.ReachClear = false;
Time.time += .1f; Time.frameCount++; s.Body.Leg!.Approach(out _);
Check(s.Body.Hands.Stored == 0 && !s.Body.Finished, "new obstruction during reach pauses before release");
s.Storage.ReachClear = true; Time.time += .3f; Time.frameCount++; s.Body.Leg!.Approach(out _);
Check(s.Body.Hands.Stored == 1, "paused reach resumes once its sweep is clear");
s = StorageSetup(); s.Item.Extras[typeof(Food)] = new Food();
FurnitureStorage.All.Add(new FurnitureStorage { Name = "fridge", Fits = false });
s.Errand.TryStart(out _); for (int i=0;i<11;i++) Step(s.Body);
Check(s.Body.Leg != null && s.Body.Leg.Holds(s.Storage.Root), "food tries usable chest after fridge candidates fail");
s = StorageSetup(); s.Storage.Fits = false;
overflow = new() { Name = "overflow area", Overflow = new StorageOverflow() }; overflow.Inside.Add(s.Item);
StorageOverflow.Area = overflow;
FurnitureStorage otherOverflow = new() { Name = "overflow area", Overflow = new StorageOverflow() };
FurnitureStorage.All.Add(otherOverflow);
s.Errand.TryStart(out _); for (int i=0;i<12 && !s.Body.Finished;i++) Step(s.Body);
Check(s.Body.Finished && s.Body.Hands.Pickups == 0, "full normal storage never shuffles between overflow rooms");
Check(StoragePolicy.ProbeColumns(.55f,.1f) == 3, "two-item-wide shelf also samples its centre");
Check(StoragePolicy.PackingOffset(2,.55f,.1f) == 0, "centre remains an alternative when side packing is blocked");
Check(System.Math.Abs(StoragePolicy.PackingOffset(0,.55f,.1f) + .165f) < .0001f, "edge probes leave wall clearance");
Check(StoragePolicy.ProbeColumns(.1f,.1f) == 0, "probe search still rejects oversized items");
s = StorageSetup(); s.Errand.TryStart(out _); Step(s.Body); Step(s.Body);
Check(s.Body.Hands.WorldOrientation, "storage carry locks the orientation used for footprint planning");
s = StorageSetup(); s.Storage.Inside.Add(s.Item); s.Storage.Untidy = true;
s.Storage.Root.position = new(.2f,0,0);
Check(s.Errand.Count(out _) == 1 && s.Errand.TryStart(out _), "untidy useful item can be rearranged within its current container");
for (int i=0;i<18 && !s.Body.Finished;i++) Step(s.Body);
Check(s.Body.Hands.Stored == 1, "same-container rearrangement extracts and replaces item");
s = StorageSetup(); s.Storage.Inside.Add(s.Item); s.Storage.Untidy = true; s.Storage.Fits = false;
s.Errand.TryStart(out _); for (int i=0;i<12 && !s.Body.Finished;i++) Step(s.Body);
Check(s.Body.Hands.Pickups == 0, "rearranging requires a clear destination before extraction");
s = StorageSetup(); s.Storage.Inside.Add(s.Item); s.Storage.Untidy = true; s.Item.Extras.Clear();
Check(s.Errand.Count(out _) == 0, "rearrangement leaves unknown and story items alone");
s = StorageSetup(); StorageShape.Orientations = 2; s.Storage.RequiredOrientation = 1;
s.Errand.TryStart(out _); for (int i=0;i<22 && !s.Body.Finished;i++) Step(s.Body);
Check(s.Body.Hands.Stored == 1 && s.Item.transform.rotation.Degrees == 90, "alternate orientation is searched and applied before insertion");
s = StorageSetup(); s.Item.transform.rotation = new Quaternion { Degrees = 45 }; s.Storage.TurnClear = false;
s.Errand.TryStart(out _); for (int i=0;i<14 && !s.Body.Finished;i++) Step(s.Body);
Check(s.Body.Hands.Stored == 0, "blocked rotation volume prevents placement");
Check(StoragePolicy.PackingOffset(0,.55f,.1f) < 0 && StoragePolicy.PackingOffset(1,.55f,.1f) > 0,
    "two fitting items pack on opposite sides instead of occupying the centre first");
foreach (float itemHalf in new[] { .025f, .07f, .12f })
{
    var front = StorageAccess.ThroughFace(0, 1, .6f, .5f, itemHalf, itemHalf, .1f, -.1f);
    Check(front.OuterZ > .25f + itemHalf && front.OuterX == .1f && front.InnerX == .1f && front.InnerZ == -.1f,
        "front opening stays in the slot lane, avoiding sideways sweeps through neighbours");
    var side = StorageAccess.ThroughFace(-1, 0, .6f, .5f, itemHalf, itemHalf, .1f, -.1f);
    Check(side.OuterX < -.3f - itemHalf && side.OuterZ == -.1f && side.InnerX == .1f && side.InnerZ == -.1f,
        "side opening route accounts for footprint and reversed face");
}
s = StorageSetup(); s.Storage.DoorsReady = false; s.Errand.TryStart(out _);
for (int i=0;i<5;i++) Step(s.Body);
Check(s.Body.Hands.Stored == 0, "an opening animation is not treated as an open aperture");
s = StorageSetup(); s.Errand.TryStart(out _);
for (int i=0;i<8 && s.Body.Hands.Stored == 0;i++) Step(s.Body);
s.Item.transform.position = new(1,0,0); Step(s.Body);
Check(s.Body.Finished && s.Errand.DueAt == Time.time + s.Errand.Interval, "drift failure lets other storage work continue after the short interval");
s = StorageSetup(); s.Item.transform.rotation = new Quaternion { Degrees = 45 };
s.Storage.Doors = [new Door { Opened = false }];
s.Errand.TryStart(out _); Step(s.Body); Step(s.Body); s.Storage.ReachClear = false;
for (int i=0;i<5;i++) Step(s.Body);
Check(s.Body.Hands.Item == s.Item && s.Body.Hands.ReachTarget == null && !s.Body.Finished,
    "placement retry restores carrying instead of leaving item pinned in world space");
Check(s.Storage.Doors[0].Opened && Items.ClosedDoors == 0, "slot retry does not start a conflicting closing animation");
s.Body.FinishRoute();
Check(Items.ClosedDoors == 1 && !s.Storage.Doors[0].Opened, "job ending closes the door it opened even after retry handover");
s = StorageSetup(); s.Item.transform.rotation = new Quaternion { Degrees = 45 };
s.Errand.TryStart(out _); Step(s.Body); Step(s.Body); Step(s.Body);
Check(s.Body.Hands.ReachTarget != null, "fixture reaches rotation hold before cancellation");
FurnitureStorage.SafeDropAvailable = false; s.Body.FinishRoute();
Check(s.Body.Hands.Item == null && s.Body.Hands.ReachTarget == null,
    "cancellation with no safe floor releases item and clears pinned reach state");
// Compact rows must keep their spacing and remain inside the usable width.
foreach (float width in new[] { .42f, .55f, .64f, 1.1f })
foreach (float radius in new[] { .03f, .07f, .1f })
{
    int count = StoragePolicy.Columns(width, radius);
    float previous = StoragePolicy.PackingOffset(0, width, radius);
    for (int i = 1; i < count; i++)
    {
        float offset = StoragePolicy.PackingOffset(i, width, radius);
        Check(Math.Abs(offset - previous - (radius * 2 + .02f)) < .0001f,
            "packed neighbours leave only the safety gap");
        Check(offset + radius <= width * .5f - .0099f,
            "compact row fits within furniture wall clearance");
        previous = offset;
    }
}
s = StorageSetup();
Grabbable untidy = new() { CanTrash = false };
untidy.Extras[typeof(AidKit)] = new AidKit(); untidy.transform.position = new(3,0,0);
s.Storage.Inside.Add(untidy); s.Storage.Untidy = true;
s.Errand.TryStart(out _);
Check(s.Body.Leg!.Holds(s.Item.transform) && !s.Body.Leg.Holds(untidy.transform),
    "distant stored contents cannot monopolise work ahead of nearby loose supplies");
Check(s.Errand.Interval == 3f && s.Errand.RetryDelay == 60f,
    "successful jobs resume sooner without accelerating failure retries");
s = StorageSetup(); s.Storage.Inside.Add(s.Item); s.Storage.Untidy = true;
s.Storage.ReachClear = false;
s.Errand.TryStart(out _);
for (int i=0; i<12 && !s.Body.Finished; i++) Step(s.Body);
Check(s.Body.Finished && s.Body.Hands.Pickups == 0,
    "blocked extraction leaves original item inside furniture");
Grabbable other = new() { CanTrash = false }; other.Extras[typeof(AidKit)] = new AidKit();
SceneScan.Data[typeof(Grabbable)] = new[] { s.Item, other };
s.Body.Finished = false; s.Storage.ReachClear = true;
Check(s.Errand.TryStart(out _),
    "changed scene reconsiders failed placement without waiting for expiry");
Check(StorageAccess.OnPackingGrid(0, 1, .55f, .55f, .1f, .1f, 0, 0),
    "centre fallback is accepted as tidy instead of picked up again");
Check(StorageAccess.OnPackingGrid(0, 1, .55f, .55f, .1f, .1f, -.12f, 0),
    "accepted settling drift does not trigger rearrangement");
Check(!StorageAccess.OnPackingGrid(0, 1, .55f, .55f, .1f, .1f, .16f, 0),
    "genuinely off-row placement remains eligible for rearrangement");
Check(!StorageAccess.OnPackingGrid(0, 1, .1f, .1f, .1f, .1f, 0, 0),
    "oversized footprint has no acceptable packing line");
foreach (float faceSign in new[] { -1f, 1f })
{
    var first = StorageAccess.PackingSlot(0, faceSign, .55f, .55f, .07f, .07f, 0);
    var next = StorageAccess.PackingSlot(0, faceSign, .55f, .55f, .07f, .07f, 1);
    Check(first.Z * faceSign < 0 && first.Z == next.Z && next.X > first.X,
        "front and rear openings fill one back row before approaching the door");
    first = StorageAccess.PackingSlot(faceSign, 0, .55f, .55f, .07f, .07f, 0);
    next = StorageAccess.PackingSlot(faceSign, 0, .55f, .55f, .07f, .07f, 1);
    Check(StorageAccess.OnPackingGrid(faceSign, 0, .55f, .55f, .07f, .07f, first.X, first.Z),
        "mirrored placement is recognised by the real tidiness policy");
    Check(first.X * faceSign < 0 && first.X == next.X && next.Z > first.Z,
        "left and right openings fill across the back row first");
}
s = StorageSetup(); s.Item.Extras[typeof(Food)] = new Food();
FurnitureStorage preferred = new() { Name = "fridge" }; preferred.Root.position = new(5,0,0);
FurnitureStorage.All.Add(preferred); s.Errand.TryStart(out _); Step(s.Body);
Check(s.Body.Leg!.Holds(preferred.Root), "food chooses a distant usable fridge before a nearby fallback chest");
s = StorageSetup(); s.Errand.TryStart(out _);
Step(s.Body); Step(s.Body); Step(s.Body);
Time.time += .05f; Time.frameCount++; s.Body.Leg!.Approach(out _);
Check(s.Body.Hands.ReachTarget != null, "fully open destination starts insertion without full door timer");
Check(s.Body.Hands.ReachSpeed == .75f, "storage uses moderate hand movement speed");
s = StorageSetup(); s.Storage.DoorsReady = false; s.Errand.TryStart(out _);
Step(s.Body); Step(s.Body); Step(s.Body);
Time.time += .05f; Time.frameCount++; s.Body.Leg!.Approach(out _);
Check(s.Body.Hands.ReachTarget == null && s.Body.Hands.Stored == 0,
    "unfinished door animation still blocks early insertion");
s = StorageSetup(); s.Storage.Inside.Add(s.Item); s.Storage.Untidy = true;
s.Storage.Root.position = new(.2f,0,0); s.Errand.TryStart(out _);
Step(s.Body); Step(s.Body);
Time.time += .05f; Time.frameCount++; s.Body.Leg!.Approach(out _);
Check(s.Body.Hands.Pickups == 1, "fully open source starts extraction without full door timer");
// Bounded plan sequence: stage incoming, evacuate contents, refill contents, then incoming.
Grabbable incoming = new(), packA = new(), packB = new(), packC = new(), packD = new();
StorageRepackPlan packing = new(new Transform(), incoming, [packA, packA, incoming, packB, packC, packD], Time.time);
Check(packing.Next is { ToOverflow: true } step && step.Item == incoming, "repack stages incoming first");
Check(!packing.Advance(packA, true) && !packing.Advance(incoming, false), "wrong item or wrong destination cannot advance repacking");
Check(packing.Advance(incoming, true), "incoming checked overflow advances repacking");
foreach (Grabbable item in new[] { packA, packB, packC }) Check(packing.Advance(item, true), "bounded contents stage before refill");
foreach (Grabbable item in new[] { packA, packB, packC }) Check(packing.Advance(item, false), "staged contents return to normal storage");
Check(packing.Advance(incoming, false) && packing.Next == null, "original item is retried last and sequence ends");
Check(packing.ExpiresAt == Time.time + 300, "repacking has a finite lifetime");

void FinishStorage(Body body)
{
    for (int i = 0; i < 250 && !body.Finished; i++)
    { Time.frameCount++; Time.time += .2f; body.Leg?.Approach(out _); }
    Check(body.Finished, "storage fixture reaches an end within its budget");
}
s = StorageSetup(); s.Storage.Name = "fridge"; s.Storage.Fits = false; s.Storage.Untidy = true;
s.Storage.Root.position = new(.2f,0,0); s.Item.Extras[typeof(Food)] = new Food();
Grabbable messy = new() { CanTrash = false }; messy.Extras[typeof(Food)] = new Food();
messy.transform.position = new(.001f,0,0); s.Storage.Inside.Add(messy);
overflow = new() { Name = "overflow area", Overflow = new StorageOverflow() }; StorageOverflow.Area = overflow;
s.Errand.TryStart(out _); FinishStorage(s.Body);
Check(s.Body.Hands.Stored == 1 && !s.Storage.Inside.Contains(s.Item), "blocked preferred home stages incoming in overflow");
overflow.Inside.Add(s.Item); s.Body.Finished = false;
Check(s.Errand.TryStart(out _) && s.Body.Leg!.Holds(messy.transform), "repack selects cupboard contents ahead of staged incoming");
FinishStorage(s.Body);
Check(s.Body.Hands.Stored == 2, "eligible messy contents can leave a compatible full container for checked overflow");
s.Storage.Inside.Remove(messy); overflow.Inside.Add(messy); s.Storage.Fits = true; s.Body.Finished = false;
Check(s.Errand.TryStart(out _) && s.Body.Leg!.Holds(messy.transform), "staged contents are refilled before incoming");
FinishStorage(s.Body); overflow.Inside.Remove(messy); s.Storage.Inside.Add(messy); s.Storage.Untidy = false; s.Body.Finished = false;
Check(s.Errand.TryStart(out _) && s.Body.Leg!.Holds(s.Item.transform), "incoming is retried after making room");
FinishStorage(s.Body);
Check(s.Body.Hands.Stored == 4, "bounded production repack completes all four checked moves");
s = StorageSetup(); s.Storage.Name = "fridge"; s.Storage.Fits = false; s.Storage.Untidy = true;
s.Item.Extras[typeof(Food)] = new Food();
messy = new() { CanTrash = false }; messy.Extras[typeof(Food)] = new Food(); messy.transform.position = new(.001f,0,0);
s.Storage.Inside.Add(messy);
overflow = new() { Name = "overflow area", Overflow = new StorageOverflow(), Fits = false }; StorageOverflow.Area = overflow;
s.Errand.TryStart(out _); FinishStorage(s.Body);
Check(s.Body.Hands.Pickups == 0, "full overflow prevents evacuation or pickup of the incoming item");

s = StorageSetup(); s.Storage.Name = "fridge"; s.Storage.Fits = false; s.Storage.Untidy = true;
s.Item.Extras[typeof(Food)] = new Food();
Grabbable protectedItem = new() { CanTrash = false }; s.Storage.Inside.Add(protectedItem);
overflow = new() { Name = "overflow area", Overflow = new StorageOverflow() }; StorageOverflow.Area = overflow;
s.Errand.TryStart(out _); FinishStorage(s.Body); overflow.Inside.Add(s.Item); s.Body.Finished = false;
Check(!s.Errand.TryStart(out _) || !s.Body.Leg!.Holds(protectedItem.transform), "unknown or protected contents never become evacuation targets");

s = StorageSetup(); s.Storage.Name = "fridge"; s.Storage.Fits = false; s.Storage.Untidy = true;
s.Item.Extras[typeof(Food)] = new Food();
messy = new() { CanTrash = false }; messy.Extras[typeof(Food)] = new Food(); messy.transform.position = new(.001f,0,0);
s.Storage.Inside.Add(messy);
overflow = new() { Name = "overflow area", Overflow = new StorageOverflow() }; StorageOverflow.Area = overflow;
s.Errand.TryStart(out _); FinishStorage(s.Body); overflow.Inside.Add(s.Item); s.Body.Finished = false;
s.Errand.CancelRepack();
Check(s.Errand.TryStart(out _) && s.Body.Leg!.Holds(s.Item.transform), "cancellation discards pending evacuation priority");
s.Body.FinishRoute();
s = StorageSetup(); s.Storage.Name = "fridge"; s.Storage.Untidy = true;
s.Item.Extras[typeof(Food)] = new Food();
messy = new() { CanTrash = false }; messy.Extras[typeof(Food)] = new Food(); messy.transform.position = new(.001f,0,0);
s.Storage.Inside.Add(messy);
overflow = new() { Name = "overflow area", Overflow = new StorageOverflow() }; StorageOverflow.Area = overflow;
s.Errand.TryStart(out _); Step(s.Body); Step(s.Body); s.Storage.ReachClear = false; FinishStorage(s.Body);
Check(s.Body.Hands.Pickups == 1 && s.Body.Hands.Stored == 1,
    "already carried incoming item is staged after repeated blocked insertion without being picked up twice");
overflow.Inside.Add(s.Item); s.Storage.Inside.Remove(messy); s.Body.Finished = false;
Check(s.Errand.TryStart(out _) && s.Body.Leg!.Holds(s.Item.transform),
    "player removing planned contents invalidates evacuation rather than chasing that item");
s.Body.FinishRoute();
s = StorageSetup(); s.Storage.Name = "fridge";
s.Item.Extras[typeof(Food)] = new Food();
messy = new() { CanTrash = false }; messy.Extras[typeof(Food)] = new Food();
s.Storage.Inside.Add(messy); s.Storage.LastBlockingItem = messy;
overflow = new() { Name = "overflow area", Overflow = new StorageOverflow() }; StorageOverflow.Area = overflow;
s.Errand.TryStart(out _); Step(s.Body); Step(s.Body); s.Storage.ReachClear = false;
FinishStorage(s.Body); overflow.Inside.Add(s.Item); s.Body.Finished = false;
Check(s.Errand.TryStart(out _) && s.Body.Leg!.Holds(messy.transform),
    "aligned useful item blocking the opening is selected for evacuation");
s.Body.FinishRoute();

s = StorageSetup(); s.Storage.Name = "fridge"; s.Storage.Fits = false; s.Storage.Untidy = true;
s.Item.Extras[typeof(Food)] = new Food(); s.Storage.Inside.Add(s.Item);
messy = new() { CanTrash = false }; messy.Extras[typeof(Food)] = new Food(); messy.transform.position = new(.001f,0,0);
s.Storage.Inside.Add(messy);
overflow = new() { Name = "overflow area", Overflow = new StorageOverflow() }; StorageOverflow.Area = overflow;
s.Errand.TryStart(out _); FinishStorage(s.Body);
Check(s.Body.Hands.Stored == 1, "same-container rearrangement can stage incoming even though normal overflow fallback was filtered out");
s.Storage.Inside.Remove(s.Item); overflow.Inside.Add(s.Item); s.Body.Finished = false;
Check(s.Errand.TryStart(out _) && s.Body.Leg!.Holds(messy.transform), "same-container repack proceeds to other contents");
s.Body.FinishRoute();
Check(StoragePolicy.PoseFits(.03f, -.04f, -.03f, 0, true),
    "logged stable overflow settling does not abort repacking");
Check(StoragePolicy.PoseFits(-.04f, -.02f, .02f, 0, false),
    "logged cupboard settling stays within the tidiness grid and release clearance");
Check(StoragePolicy.PoseFits(.05f, -.02f, .01f, 0, false),
    "second logged stable cupboard placement is accepted");
Check(!StoragePolicy.PoseFits(.051f, 0, 0, 0, false), "off-grid horizontal drift remains rejected");
Check(!StoragePolicy.PoseFits(0, -.041f, 0, 0, false), "excessive cupboard vertical drift remains rejected");
Check(!StoragePolicy.PoseFits(0, 0, 0, 9, false), "tipped cupboard placement remains rejected");
var extractionLane = StorageAccess.ThroughFace(0, 1, .55f, .45f, .06f, .11f, .18f, .04f);
Check(extractionLane.OuterX == .18f && extractionLane.InnerX == .18f,
    "logged cereal extraction keeps x offset instead of crossing the neighbouring cereal at centre");
Check(!StoragePolicy.PoseFits(.081f, 0, 0, 0, true), "overflow leaving its grid tolerance is rejected");
Check(!StoragePolicy.PoseFits(0, -.081f, 0, 0, true), "excessive vertical drop in overflow is rejected");
Check(!StoragePolicy.PoseFits(0, 0, 0, 9, true), "tipped overflow placement is still rejected");
s = StorageSetup(); s.Storage.Fits = false;
YourBuddyPlugin.Log.Lines.Clear();
s.Errand.TryStart(out _);
for (int i = 0; i < 20; i++) s.Body.Leg!.Approach(out _);
Check(YourBuddyPlugin.Log.Lines.FindAll(line => line.Contains("progress state=")).Count == 1,
    "unchanged storage progress is not logged every frame");
Time.time += 5f;
s.Body.Leg!.Approach(out _);
Check(YourBuddyPlugin.Log.Lines.FindAll(line => line.Contains("progress state=")).Count == 2,
    "stalled storage emits a five-second heartbeat");
Check(YourBuddyPlugin.Log.Lines.Exists(line => line.Contains("candidate=") && line.Contains("rank=")),
    "job diagnostics include ranked destination inputs");
Check(YourBuddyPlugin.Log.Lines.Exists(line => line.Contains("selection=eligible loose supply")),
    "selection diagnostics explain loose-item eligibility");
s.Body.FinishRoute();
Check(YourBuddyPlugin.Log.Lines.Exists(line => line.Contains("leg ended/interrupted")),
    "interrupted jobs produce a final state snapshot");
s = StorageSetup(); s.Errand.TryStart(out _);
s.Body.Hands.ReachDistance = .04f;
for (int i = 0; i < 15 && !s.Body.Finished; i++)
{ Time.time += .05f; Time.frameCount++; s.Body.Leg!.Approach(out _); }
Check(s.Body.Hands.Stored == 0 && !s.Body.Finished,
    "four-centimetre waypoint error waits instead of cutting into next segment");
s.Body.Hands.ReachDistance = .009f;
FinishStorage(s.Body);
Check(s.Body.Hands.Stored == 1, "accurate hand arrival resumes normal storage");
s.Storage.Inside.Add(s.Item); s.Item.Extras[typeof(Food)] = new Food();
FurnitureStorage.All.Add(new FurnitureStorage { Name = "fridge" });
s.Body.Finished = false;
Check(!s.Errand.TryStart(out _), "successful fallback is not immediately retrieved for promotion");
Time.time += 121f;
Check(s.Errand.TryStart(out _), "preferred-home promotion is reconsidered after success cooldown");
s.Body.FinishRoute();
s = StorageSetup(); s.Errand.TryStart(out _); FinishStorage(s.Body);
s.Storage.Inside.Add(s.Item); s.Storage.Untidy = true; s.Body.Finished = false;
Check(s.Errand.TryStart(out _), "player-disturbed placement can be repaired during success cooldown");
s.Body.FinishRoute();
SkipList timedSkip = new(); Transform skippedTarget = new(); timedSkip.Skip(skippedTarget, 2f);
timedSkip.Ignore = true; Check(!timedSkip.Has(skippedTarget), "explicit skip override works");
timedSkip.Ignore = false; Check(timedSkip.Has(skippedTarget), "skip override restores timed exclusion");
s = StorageSetup(); s.Storage.Doors = [new Door { Opened = false }];
s.Errand.TryStart(out _); FinishStorage(s.Body);
Check(s.Storage.Doors[0].Opened && Items.ClosedDoors == 0, "completed item retains owned door for next nearby job");
Grabbable nextSupply = new() { CanTrash = false }; nextSupply.Extras[typeof(AidKit)] = new AidKit();
SceneScan.Data[typeof(Grabbable)] = new[] { nextSupply }; s.Body.Finished = false;
Check(s.Errand.TryStart(out _), "next nearby storage job starts with retained door");
s.Errand.MaintainStorageDoors(); FinishStorage(s.Body);
Check(s.Storage.Doors[0].Opened && Items.ClosedDoors == 0, "two items reuse open door without closing between them");
s.Errand.CloseStorageDoors();
Check(!s.Storage.Doors[0].Opened && Items.ClosedDoors == 1, "cancellation closes retained door exactly once");
s = StorageSetup(); s.Storage.Doors = [new Door { Opened = true }];
s.Errand.TryStart(out _); FinishStorage(s.Body); s.Errand.CloseStorageDoors();
Check(s.Storage.Doors[0].Opened && Items.ClosedDoors == 0, "player-opened door remains open after storage");
s = StorageSetup(); s.Storage.Doors = [new Door { Opened = false }];
s.Errand.TryStart(out _); FinishStorage(s.Body); Time.time += 9; s.Errand.MaintainStorageDoors();
Check(!s.Storage.Doors[0].Opened, "idle timeout closes retained door when no job resumes");
s = StorageSetup(); s.Storage.Doors = [new Door { Opened = false }];
s.Errand.TryStart(out _); FinishStorage(s.Body); s.Body.Transform.position = new(4,0,0); s.Errand.MaintainStorageDoors();
Check(!s.Storage.Doors[0].Opened, "moving away closes owned storage door");
s = StorageSetup(); s.Storage.Doors = [new Door { Opened = false }];
s.Errand.TryStart(out _); FinishStorage(s.Body); SceneScan.Data[typeof(Grabbable)] = new Grabbable[0];
s.Errand.TryStart(out _);
Check(!s.Storage.Doors[0].Opened, "no remaining storage work closes retained doors immediately");
// Enabled room sets from the installed StorymodeShipBuilder stages.
foreach (string[] built in new[] {
    new[] { "Front_M00" }, new[] { "Front_M00", "Front_R03", "Front_L06" },
    new[] { "Front_M00", "Front_R03", "Front_L06", "Core_R04", "Core_L07" },
    new[] { "Front_M00", "Front_R03", "Front_L06", "Core_R04", "Core_L07", "Core_M01" },
    new[] { "Front_M00", "Front_R03", "Front_L06", "Core_R04", "Core_L07", "Core_M01", "Core_R10E", "Core_L13E" },
    new[] { "Front_M00", "Front_R03", "Front_L06", "Core_R04", "Core_L07", "Core_M01", "Core_R10E", "Core_L13E", "Back_R05", "Back_L08" } })
{
    var available = Array.FindAll(StorageOverflowLayout.Areas, area => !area.Legacy && Array.IndexOf(built, area.Room) >= 0);
    Check(available.Length > 0, "every stock room layout has a configured overflow row");
    Check(available[0].Room == (built.Length == 1 ? "Front_M00" : "Front_L06"), "fuel room precedes cockpit whenever built");
}
foreach (var area in StorageOverflowLayout.Areas)
{
    if (area.Legacy) continue;
    Check(Math.Abs(area.ApproachX - area.X) >= .8f, "approach lane stays outside item row");
    for (int i = 1; i < area.Slots; i++)
        Check(Math.Abs(StorageOverflowLayout.RowOffset(i, area.Slots) - StorageOverflowLayout.RowOffset(i-1, area.Slots) - .65f) < .001f,
            "overflow row keeps consistent spacing");
    if (area.Room == "Front_L06")
    {
        Check(area.X + .3f < 4.3125f && area.Z - .65f - .3f > -5.625f && area.Z + .65f + .3f < -1.875f,
            "fuel row footprint stays within serialized floor boundaries");
        Check(area.X - .3f - 1.565f > 1f, "fuel row leaves more than one metre from stock doorway");
    }
}
s = StorageSetup(); s.Errand.TryStart(out _); Step(s.Body);
Check(s.Body.Leg!.Reach == 1.35f && s.Body.Leg.StandOffs[0] == 1.3f,
    "loose pickup plans a farther standing point and reaches logged chair-side item");
Step(s.Body);
Check(s.Body.Leg!.Reach == Items.SnackReachDist,
    "longer pickup reach does not change storage delivery reach");
s.Body.FinishRoute();
s = StorageSetup(); s.Storage.Inside.Add(s.Item); s.Storage.Untidy = true;
s.Errand.TryStart(out _); Step(s.Body);
Check(s.Body.Leg!.Reach == Items.SnackReachDist,
    "furniture extraction retains existing approach distance");
s.Body.FinishRoute();
s = StorageSetup(); s.Item.transform.rotation = new Quaternion { Degrees = 45 };
s.Errand.TryStart(out _); Step(s.Body); Step(s.Body);
s.Body.Hands.ReachDistance = .9f;
Time.time += .1f; Time.frameCount++; s.Body.Leg!.Approach(out _);
Check(s.Body.Hands.ReachTarget == null, "floor pickup is not pinned before reaching carrying height");
s.Body.Hands.ReachDistance = 0;
FinishStorage(s.Body);
Check(s.Body.Hands.Stored == 1, "lifted pickup rotates and stores normally");
s = StorageSetup(); s.Item.transform.rotation = new Quaternion { Degrees = 45 };
s.Errand.TryStart(out _); Step(s.Body); Step(s.Body); s.Body.Hands.ReachDistance = .9f;
FinishStorage(s.Body);
Check(s.Body.Hands.Stored == 0 && s.Body.Hands.Item == null, "failed lift times out and releases item safely");
// More overflow candidates stay within the same side strip and retain old centres first.
foreach (int slots in new[] { 2, 3 })
{
    var offsets = new System.Collections.Generic.HashSet<(float, float)>();
    for (int i = 0; i < StorageOverflowLayout.ProbeCount(slots); i++)
    {
        var offset = StorageOverflowLayout.ProbeOffset(i, slots);
        Check(offsets.Add(offset), "overflow candidates are unique");
        Check(Math.Abs(offset.X) <= .15f && Math.Abs(offset.Z) <= (slots-1)*.325f,
            "additional candidates stay inside existing strip");
        if (i < slots) Check(offset.X == 0 && offset.Z == StorageOverflowLayout.RowOffset(i,slots),
            "original positions are attempted first");
    }
    Check(offsets.Contains((0f, StorageOverflowLayout.RowOffset(0,slots)+.325f)),
        "small supplies can use gaps between original centres");
}
s = StorageSetup(); s.Storage.Fits = false; s.Errand.TryStart(out _);
for (int i=0; i<12 && !s.Body.Finished; i++) Step(s.Body);
int probesBefore = s.Storage.Probes;
Check(!s.Errand.TryStart(out _) && s.Storage.Probes == probesBefore,
    "unchanged failed placement is cached without another physics search");
s.Item.transform.position = new(.02f,0,0);
Check(s.Errand.Count(out _) == 0, "small settling noise does not invalidate placement cache");
s.Item.transform.position = new(.07f,0,0);
Check(s.Errand.Count(out _) == 1, "accumulated movement invalidates placement cache");
s = StorageSetup(); s.Storage.Fits = false; s.Errand.TryStart(out _);
for (int i=0; i<12 && !s.Body.Finished; i++) Step(s.Body);
Time.time += 31;
Check(s.Errand.Count(out _) == 1, "placement cache expires even without observed item changes");
Check(.68f - StorageAccess.LiftHalfHeight(.05f) > .63f,
    "logged cereal lift sweep clears flush shelf contact");
Check(StorageAccess.LiftHalfHeight(.0005f) > 0, "thin items retain a positive lift sweep");
s = StorageSetup(); s.Item.Extras[typeof(Food)] = new Food(); s.Storage.Name = "cabinet"; s.Storage.Inside.Add(s.Item);
fridge = new() { Name = "fridge" }; FurnitureStorage.All.Add(fridge);
bool sawLift = false;
s.Storage.ReachPolicy = (from,to,h) => {
    if (to.y > from.y && to.x == from.x && to.z == from.z) sawLift = true;
    return true;
};
Check(s.Errand.TryStart(out _), "cupboard food is selected for fridge promotion");
FinishStorage(s.Body);
Check(sawLift && s.Body.Hands.Stored == 1, "food lifts from its shelf before completing fridge promotion");
s = StorageSetup(); s.Item.Extras[typeof(Food)] = new Food(); s.Storage.Inside.Add(s.Item);
fridge = new() { Name = "fridge" }; FurnitureStorage.All.Add(fridge);
s.Storage.ReachPolicy = (from,to,h) => to.y <= from.y;
s.Errand.TryStart(out _); FinishStorage(s.Body);
Check(s.Body.Hands.Pickups == 0, "blocked lift refuses extraction before picking up item");
Check(StoragePolicy.Home(false,false,"cabinet") < StoragePolicy.Home(false,false,"chest"),
    "kits and seeds prefer cupboards over general chests");
Check(StoragePolicy.Home(false,true,"chest") < StoragePolicy.Home(false,true,"cabinet"),
    "resource cells prefer chests over cupboards");
Check(StoragePolicy.Home(false,false,"fridge") == int.MaxValue,
    "nonfood supplies cannot occupy food-priority fridges");
// Logged open-door exit: clear the leaf before moving towards the carrier.
Check(Math.Abs(StorageAccess.OutsideDoor(.44f, .39f, .18f, .14f) - .79f) < .0001f,
    "exit plane includes open door and carried footprint");
Check(StorageAccess.OutsideDoor(1f, .1f, .1f, .1f) == 1f,
    "door behind the portal does not shorten the exit");
s = StorageSetup(); s.Storage.Name = "fridge"; s.Storage.Fits = false; s.Storage.Untidy = true;
s.Item.Extras[typeof(Food)] = new Food();
messy = new() { CanTrash = false }; messy.Extras[typeof(Food)] = new Food();
messy.transform.position = new(.01f,0,0); s.Storage.Inside.Add(messy);
overflow = new() { Name = "overflow area", Overflow = new StorageOverflow() };
StorageOverflow.Area = overflow; overflow.Inside.Add(s.Item);
s.Errand.TryStart(out _); FinishStorage(s.Body);
Check(s.Body.Hands.Pickups == 0, "already staged incoming item is not shuffled to another overflow slot");
s.Body.Finished = false;
Check(s.Errand.TryStart(out _) && s.Body.Leg!.Holds(messy.transform),
    "overflow incoming triggers preferred-container evacuation");
FinishStorage(s.Body);
Check(s.Body.Hands.Stored == 1, "preferred container can be cleared for an already staged item");
s = StorageSetup(); s.Storage.ReachClear = false;
s.Errand.TryStart(out _); FinishStorage(s.Body);
Check(s.Body.Hands.Pickups == 0, "longer pickup reach never pulls an item through furniture");
s = StorageSetup(); s.Item.Extras[typeof(Food)] = new Food(); s.Storage.Name = "fridge";
var sourceCabinet = new FurnitureStorage { Name = "cabinet" };
sourceCabinet.Inside.Add(s.Item); FurnitureStorage.All.Add(sourceCabinet);
s.Body.Blocked.Add(sourceCabinet.Root);
Check(!s.Errand.TryStart(out var blockedPickup) && blockedPickup.Contains("a door I cannot open"),
    "pickup preflight retains the actual route failure reason");
Check(s.Storage.Probes == 0 && s.Body.Hands.Pickups == 0,
    "unreachable source is rejected before scanning destination slots");
s = StorageSetup(); s.Item.Extras[typeof(Food)] = new Food(); s.Storage.Name = "fridge";
s.Storage.Fits = false; s.Storage.Untidy = true;
sourceCabinet = new FurnitureStorage { Name = "cabinet" };
sourceCabinet.Inside.Add(s.Item); FurnitureStorage.All.Add(sourceCabinet);
messy = new() { CanTrash = false }; messy.Extras[typeof(Food)] = new Food();
messy.transform.position = new(.01f,0,0); s.Storage.Inside.Add(messy);
overflow = new() { Name = "overflow area", Overflow = new StorageOverflow() }; StorageOverflow.Area = overflow;
s.Errand.TryStart(out _); FinishStorage(s.Body);
Check(s.Body.Hands.Stored == 1, "item in a lower-priority cupboard may stage while its preferred home is repacked");
Console.WriteLine($"{checks} tidy/storage state and policy checks passed.");

sealed class Body : IErrandBody
{
    public bool Aboard = true;
    public bool IsAboardPlayerShip() => Aboard;
    public void LoadRoomOf(Transform what) { }
    public Transform Transform { get; } = new();
    public NpcHands Hands { get; } = new();
    public ErrandLeg? Leg { get; set; }
    public readonly HashSet<Transform> Blocked = [], Taken = [], OtherVessel = [];
    public int Plans, Walks;
    public bool Finished;
    public Vector3 FloorUnderBuddy() => Transform.position;
    public bool OnMyVessel(Transform t) => !OtherVessel.Contains(t);
    public bool TakenByAnother(Transform t) => Taken.Contains(t);
    public bool InReach(ReachTask t) => t.Own.Component is Grabbable;
    public string? PlanReach(ReachTask t, out NavPath path) { Plans++; path = new(); return Blocked.Contains(t.Own) ? "a door I cannot open is in the way" : null; }
    public void Walk(ErrandLeg leg, NavPath? path) { Walks++; Leg = leg; }
    public bool StepIntoReach(ReachTask t, out Vector3 move, out bool wantMove) { move = default; wantMove = false; return true; }
    public void StandFacing(Vector3 point) { }
    public void FinishRoute() { Finished = true; Leg?.End(); Leg = null; }
}

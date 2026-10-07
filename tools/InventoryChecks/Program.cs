using System;
using System.Linq;
using YourBuddy;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
// Enabled room names from StorymodeShipBuilder stages 0..5.
string[][] stages = [
    ["Front_M00"],
    ["Front_M00", "Front_R03", "Front_L06"],
    ["Front_M00", "Front_R03", "Front_L06", "Core_R04", "Core_L07"],
    ["Front_M00", "Front_R03", "Front_L06", "Core_R04", "Core_L07", "Core_M01"],
    ["Front_M00", "Front_R03", "Front_L06", "Core_R04", "Core_L07", "Core_M01", "Core_R10E", "Core_L13E"],
    ["Front_M00", "Front_R03", "Front_L06", "Core_R04", "Core_L07", "Core_M01", "Core_R10E", "Core_L13E", "Back_R05", "Back_L08"]
];
string[][] preferred = [
    ["Front_M00", "Front_M00", "Front_M00"],
    ["Front_R03", "Front_L06", "Front_M00"],
    ["Core_L07", "Front_L06", "Core_R04"],
    ["Core_L07", "Front_L06", "Core_R04"],
    ["Core_L13E", "Front_L06", "Core_R10E"],
    ["Core_L13E", "Front_L06", "Core_R10E"]
];
for (int stage = 0; stage < stages.Length; stage++)
{
    for (int kind = 0; kind < 3; kind++)
    {
        var areas = StoragePlaces.For(kind).Where(a => stages[stage].Contains(a.Room)).ToArray();
        Check(areas[0].Room == preferred[stage][kind], $"stage {stage}, resource {kind}: preferred enabled room");
        Check(areas.Last().Room == "Front_M00", $"stage {stage}, resource {kind}: starter fallback");
    }
}
var cockpitSlots = new System.Collections.Generic.HashSet<(float, float)>();
for (int kind = 0; kind < 3; kind++)
{
    Check(StoragePlaces.For(kind).Select(a => a.Room).Distinct().Count() == StoragePlaces.For(kind).Count,
        "each resource visits a room only once");
    for (int slot = 0; slot < 3; slot++)
        Check(cockpitSlots.Add(StoragePlaces.Offset(kind, slot)), "resources do not request the same cockpit position");
    Check(!StoragePlaces.For(kind).Any(a => a.Room.Contains("Airlock")), "airlock never designated for storage");
}
Console.WriteLine($"{checks} storage-layout checks passed.");

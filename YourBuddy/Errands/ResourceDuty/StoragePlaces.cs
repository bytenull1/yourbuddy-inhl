using System.Collections.Generic;

namespace YourBuddy
{
    // Room-local approach centres from the bundled ship graph; offsets are storage candidates,
    // never permission to place without a floor, clearance and route check. docs/resources.md
    internal static class StoragePlaces
    {
        internal readonly record struct Area(string Room, float X, float Y, float Z);
        private static readonly Area Cockpit = new("Front_M00", -.063f, 1f, -3.764f);
        private static readonly Area[] Oxygen =
        [new("Core_L13E", 5.061f, 1f, .143f), new("Core_L07", 2.839f, 1f, .042f),
            new("Front_R03", -2.742f, 1f, -3.855f), Cockpit];
        private static readonly Area[] Fuel = [new("Front_L06", 2.696f, 1f, -3.671f), Cockpit];
        private static readonly Area[] Energy =
        [new("Core_R10E", -5.515f, 1f, -.050f), new("Core_R04", -2.715f, 1f, -.116f), Cockpit];

        internal static IReadOnlyList<Area> For(int kind) => kind switch
        {
            0 => Oxygen, 1 => Fuel, _ => Energy
        };

        // Disjoint sides in the shared cockpit; alternative spots stay within the same area.
        internal static (float X, float Z) Offset(int kind, int slot)
        {
            float along = (slot - 1) * .7f;
            return kind switch { 0 => (.85f, along), 1 => (-.85f, along), _ => (along, .85f) };
        }
    }
}

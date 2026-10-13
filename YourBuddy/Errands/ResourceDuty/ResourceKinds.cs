using System;
using Space.Enums;

namespace YourBuddy
{
    // The three duties by index (0 oxygen, 1 fuel, 2 energy), one table for every caller. docs/resources.md
    internal static class ResourceKinds
    {
        internal const int Count = 3;
        private static readonly string[] Labels = ["oxygen", "fuel", "energy"];
        private static readonly ResourceType[] Types = [ResourceType.Oxygen, ResourceType.Fuel, ResourceType.Energy];

        internal static string Label(int kind) => Labels[kind];
        internal static ResourceType Type(int kind) => Types[kind];
        internal static string Article(int kind) => kind == 1 ? "a" : "an";

        // The kind a typed label names, or -1.
        internal static int Of(string label) => Array.IndexOf(Labels, label.ToLowerInvariant());

        internal static string LabelOf(ResourceType type)
        {
            int kind = Array.IndexOf(Types, type);
            return kind >= 0 ? Labels[kind] : type.ToString().ToLowerInvariant();
        }

        // The ship's capacity and charge of `kind`; false when its controller is missing.
        internal static bool Levels(SpaceShip ship, int kind, out int capacity, out int value)
        {
            capacity = value = 0;
            switch (kind)
            {
                case 0:
                    if (ship.OxygenController == null) return false;

                    capacity = ship.OxygenController.Capacity;
                    value = ship.OxygenController.RemainingOxygen;
                    return true;
                case 1:
                    if (ship.FuelController == null) return false;

                    capacity = ship.FuelController.Capacity;
                    value = ship.FuelController.Fuel;
                    return true;
                default:
                    if (ship.ElectricityController == null) return false;

                    capacity = ship.ElectricityController.Capacity;
                    value = ship.ElectricityController.Energy;
                    return true;
            }
        }

        // The game's own drain, for buddy_dev drain.
        internal static bool Reduce(SpaceShip ship, int kind, int amount) => kind switch
        {
            0 => ship.OxygenController.TryReduceOxygen(amount),
            1 => ship.FuelController.TryConsumeFuel(amount),
            _ => ship.ElectricityController.TryReduceEnergy(amount),
        };
    }
}

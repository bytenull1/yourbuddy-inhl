using System;
namespace UnityEngine { internal static class Time { internal static float time = 1; } }
namespace Space.Enums { internal enum ResourceType { Oxygen, Fuel, Energy } }
internal sealed class OxygenController { internal int Capacity = 0, RemainingOxygen = 0; internal bool TryReduceOxygen(int n) => true; }
internal sealed class FuelController { internal int Capacity = 0, Fuel = 0; internal bool TryConsumeFuel(int n) => true; }
internal sealed class ElectricityController { internal int Capacity = 0, Energy = 0; internal bool TryReduceEnergy(int n) => true; }
internal sealed class SpaceShip
{
    internal OxygenController OxygenController = new();
    internal FuelController FuelController = new();
    internal ElectricityController ElectricityController = new();
}
namespace NPC.Core
{
    internal static class NpcEvents
    {
        internal static event Action? Tick, WorldReset;
        internal static event Action<bool>? GameStarting;
        internal static void ResetWorld() => WorldReset?.Invoke();
        internal static void Start(bool newGame) => GameStarting?.Invoke(newGame);
        internal static void Frame() => Tick?.Invoke();
    }
}
namespace YourBuddy
{
    internal sealed class ResourceErrand
    {
        internal bool Cancelled;
        internal void Cancel(string why) { Cancelled = true; ResourceDuty.Owner = null; }
        internal void Tick() { }
    }
    internal static class YourBuddyPlugin
    {
        internal static readonly Logger Log = new();
        internal sealed class Logger { internal void LogInfo(string text) { } }
    }
}

using System;
using System.Collections.Generic;

// Test boundaries only: production parsing and status code are linked into this project.
namespace NPC.Core
{
    internal static class NpcRegistry
    {
        internal readonly struct ActingScope : IDisposable { public void Dispose() { } }
        internal static ActingScope Acting(object agent) => new();
    }
}
namespace NPC.Core.Navigation
{
    internal static class StationRooms { internal sealed class Entry { } }
}
namespace YourBuddy
{
    public sealed partial class BuddyBehaviour
    {
        internal bool IsOutside, Floating, SuitSuited, IsDead, Asleep, Hiding;
        internal bool storeOrder = false;
        internal object? choreOrder = null;
        internal string ChoreOrderStatus => "Continuing assigned work.";
        internal string StoreOrderStatus => "Still on storage duty.";
        internal string Name = "Buddy";
        internal object Agent = new();
        internal string LastOrder = "unchanged";
        internal string? ReachDescription;
        private string? DescribeReachTask() => ReachDescription;
        private BuddyMode mode = BuddyMode.Follow;
        private FearState fearState = FearState.Calm;
        internal void SetState(BuddyMode value, FearState fear = FearState.Calm)
        {
            mode = value;
            fearState = fear;
        }
    }
    internal static class BuddyManager
    {
        internal static readonly List<BuddyBehaviour> Buddies = [];
        internal static IEnumerable<BuddyBehaviour> Snapshot() => Buddies;
    }
    internal static class BuddyRooms
    {
        internal static bool TryResolve(string text, out NPC.Core.Navigation.StationRooms.Entry? room,
            out List<NPC.Core.Navigation.StationRooms.Entry>? several)
        {
            room = text.Contains("workshop", StringComparison.Ordinal) ? new() : null;
            several = null;
            return room != null;
        }
        internal static string Join(IEnumerable<NPC.Core.Navigation.StationRooms.Entry> entries) => "rooms";
        internal static string Prompt() => "Where to?";
    }
    internal static class BuddyCommands
    {
        private static string Set(BuddyBehaviour buddy, string order) => buddy.LastOrder = order;
        internal static string Follow(BuddyBehaviour b) => Set(b, "follow");
        internal static string Stay(BuddyBehaviour b) => Set(b, "stay");
        internal static string Wander(BuddyBehaviour b) => Set(b, "wander");
        internal static string Hide(BuddyBehaviour b) => Set(b, "hide");
        internal static string GoOutside(BuddyBehaviour b) => Set(b, "outside");
        internal static string GoInside(BuddyBehaviour b) => Set(b, "inside");
        internal static string Unsuit(BuddyBehaviour b) => Set(b, "unsuit");
        internal static string Sell(BuddyBehaviour b) => Set(b, "sell");
        internal static string Store(BuddyBehaviour b) => Set(b, "store");
        internal static string Tidy(BuddyBehaviour b) => Set(b, "tidy");
        internal static string Play(BuddyBehaviour b) => Set(b, "play");
        internal static string FetchSuit(BuddyBehaviour b) => Set(b, "fetch suit");
        internal static string Terminal(BuddyBehaviour b, string which) => Set(b, which);
        internal static string Snack(BuddyBehaviour b) => Set(b, "snack");
        internal static string DecideForYourself(BuddyBehaviour b) => Set(b, "decide");
        internal static string GoToRoom(BuddyBehaviour b, NPC.Core.Navigation.StationRooms.Entry room) => Set(b, "goto");
        internal static string GivePassword(string code) => "code:" + code;
    }
}

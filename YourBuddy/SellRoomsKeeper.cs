using System.Collections.Generic;
using NPC.Core.World;

namespace YourBuddy
{
    /// <summary>
    /// Keeps every room holding a sell station loaded while a buddy may sell, through NPC.Core's one
    /// Room.SetContentEnabled patch. docs/invariants.md#a-sell-station-room-stays-loaded
    /// </summary>
    internal sealed class SellRoomsKeeper : IRoomKeeper
    {
        private static readonly List<Room> SellRooms = [];
        private static int _builtFor = -1;

        internal static void Register() => NpcRooms.AddKeeper(new SellRoomsKeeper());

        public string? ReasonToKeep(Room room) =>
            BuddyManager.All.Count > 0 && YourBuddyPlugin.ConfigSellTrash.Value && SellPens.HoldsSellStation(room)
                ? "it holds a sell station"
                : null;

        /// <summary>
        /// The rooms with a sell station among those the doorways join, rebuilt when the doorways were swept
        /// again. A buddy switches one back on when it finds it off: a save can restore it that way.
        /// </summary>
        internal static IReadOnlyList<Room> Rooms
        {
            get
            {
                IReadOnlyList<EntryDetector> detectors = NpcDoors.Detectors;
                if (_builtFor == NpcDoors.DetectorsVersion) return SellRooms;

                _builtFor = NpcDoors.DetectorsVersion;
                SellRooms.Clear();
                foreach (EntryDetector detector in detectors)
                {
                    NpcDoors.RoomsOf(detector, out Room? inner, out Room? outer);
                    Add(inner);
                    Add(outer);
                }
                return SellRooms;
            }
        }

        private static void Add(Room? room)
        {
            if (room != null && !SellRooms.Contains(room) && SellPens.HoldsSellStation(room)) SellRooms.Add(room);
        }
    }
}

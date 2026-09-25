using System.Collections.Generic;
using NPC.Core.Navigation;

namespace YourBuddy
{
    /// <summary>
    /// The buddy's words for NPC.Core's station rooms: "goto library" in the dialog. The console keeps
    /// node numbers. See docs/dialog.md.
    /// </summary>
    internal static class BuddyRooms
    {
        /// <summary>
        /// What the player calls it, which names no room.
        /// </summary>
        private static readonly string[] OwnWords = ["buddy"];

        internal static bool TryResolve(string lower, out StationRooms.Entry? room, out List<StationRooms.Entry>? several) =>
            StationRooms.TryResolve(lower, out room, out several, OwnWords);

        internal static string Join(IEnumerable<StationRooms.Entry> entries) => StationRooms.Join(entries);

        /// <summary>
        /// What the buddy answers to "goto" with no room in it.
        /// </summary>
        internal static string Prompt()
        {
            StationRooms.Entry[] entries = StationRooms.Current(out string station);
            if (station.Length == 0) return "No station is docked, so there is nowhere to send me.";

            if (entries.Length == 0) return "I know no route through " + station + " yet.";

            return "Where to? Try 'goto " + entries[0].Name + "'.\nRooms at " + station + ": " + Join(entries) + ".";
        }
    }
}

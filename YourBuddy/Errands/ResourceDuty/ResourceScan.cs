using System.Collections.Generic;
using NPC.Core;

namespace YourBuddy
{
    // Shops and cells in switched-off rooms too; Items.Loadable decides, LoadRoomOf switches them on.
    // docs/resources.md#scans-and-storage
    internal static class ResourceScan
    {
        private const float Rescan = 5f;
        private static Shop[] shops = [];
        private static float shopsAt;
        private static ResourceContainer[] cells = [];
        private static float cellsAt;
        private static readonly List<ResourceContainer> Merged = [];

        internal static Shop[] Shops() => SceneSweeps.Cached(ref shops, ref shopsAt, Rescan);

        // Active cells from this frame's sweep, so a cell just bought counts; switched-off ones from the cache.
        internal static List<ResourceContainer> Cells()
        {
            Merged.Clear();
            Merged.AddRange(SceneScan.ThisFrame<ResourceContainer>());
            foreach (ResourceContainer cell in SceneSweeps.Cached(ref cells, ref cellsAt, Rescan))
            {
                if (cell != null && !cell.gameObject.activeInHierarchy) Merged.Add(cell);
            }
            return Merged;
        }
    }
}

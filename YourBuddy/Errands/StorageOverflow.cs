using System.Collections.Generic;
using NPC.Core;
using UnityEngine;
namespace YourBuddy
{
    // A bounded room-local grid, never arbitrary floor placement. docs/storing.md
    internal sealed class StorageOverflow(CustomRoom room, StorageOverflowLayout.Area area)
    {
        private readonly Vector3 localCenter = new(area.X, 1f, area.Z);
        internal int CandidateCount => area.Legacy ? 0 : StorageOverflowLayout.ProbeCount(area.Slots);
        internal int Priority => area.Priority;
        internal string LastBlocker { get; private set; } = "not probed";
        private bool Blocked(string why) { LastBlocker = why; return false; }
        internal Vector3 Center => room.transform.TransformPoint(new Vector3(area.ApproachX, 1f, area.Z));
        internal bool Contains(Vector3 point, Vector3 half)
        {
            Vector3 local = room.transform.InverseTransformPoint(point) - localCenter;
            return Mathf.Abs(local.x) + half.x <= (area.Legacy ? 1.05f : .3f) &&
                Mathf.Abs(local.z) + half.z <= (area.Legacy ? 1.05f : (area.Slots - 1) * StoragePolicy.OverflowSpacing * .5f + .3f) &&
                local.y >= -1.2f && local.y + half.y <= .2f;
        }
        internal bool Probe(int index, Vector3 half, Transform item, out Vector3 point)
        {
            point = default;
            if (index < 0 || index >= CandidateCount) return Blocked("no configured overflow slot");
            var offset = StorageOverflowLayout.ProbeOffset(index, area.Slots);
            Vector3 candidate = room.transform.TransformPoint(new Vector3(localCenter.x + offset.X, .1f, localCenter.z + offset.Z));
            if (!ResourceStorage.FindFloor(candidate, out RaycastHit floor)) return Blocked($"no supported ship floor at {candidate}: {ResourceStorage.LastBlocker}");
            if (floor.collider.GetComponentInParent<CustomRoom>() != room) return Blocked($"floor belongs to another room at {candidate}");
            point = floor.point + Vector3.up * (half.y + .03f);
            return Clear(point, half, item);
        }
        internal bool Clear(Vector3 point, Vector3 half, Transform item)
        {
            if (!Contains(point, half)) return Blocked($"outside overflow bounds at {point}, half={half}");
            if (!ResourceStorage.Clear(point, half, item)) return Blocked(ResourceStorage.LastBlocker + $" at {point}");
            LastBlocker = "clear";
            return true;
        }
        internal bool ReadContents(HashSet<Grabbable> into)
        {
            foreach (Grabbable item in SceneScan.ThisFrame<Grabbable>())
            {
                if (item == null || item.GetComponentInParent<CustomRoom>() != room ||
                    !Items.ColliderBounds(item.gameObject, out Bounds bounds) || !Contains(bounds.center, bounds.extents)) continue;
                Vector3 local = room.transform.InverseTransformPoint(bounds.center) - localCenter;
                if (area.Legacy)
                {
                    if (StoragePolicy.OnOverflowGrid(local.x, local.z)) into.Add(item);
                }
                else
                {
                    into.Add(item);
                }
            }
            return true;
        }
        internal static void Collect(SpaceShip ship, List<FurnitureStorage> into)
        {
            foreach (StorageOverflowLayout.Area area in StorageOverflowLayout.Areas)
            {
                foreach (CustomRoom room in ship.Rooms)
                {
                    if (room == null || !room.EnabledStructure || room.name != area.Room) continue;
                    into.Add(new FurnitureStorage(room.transform, null, room, []) { Overflow = new StorageOverflow(room, area) });
                    break;
                }
            }
        }
    }
}

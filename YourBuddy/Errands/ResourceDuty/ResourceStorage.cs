using System.Collections.Generic;
using NPC.Core.Navigation;
using NPC.Core.World;
using UnityEngine;

namespace YourBuddy
{
    // Designated places move with enabled ship rooms. docs/resources.md
    internal static class ResourceStorage
    {
        private static readonly Collider[] Overlaps = new Collider[96];
        private static readonly RaycastHit[] Hits = new RaycastHit[32];
        private static readonly ResourceProbeBudget ProbeBudget = new(4);
        internal static bool MayProbe() => ProbeBudget.Take(Time.frameCount);

        internal static string LastBlocker { get; private set; } = "no supported floor";

        internal static Vector3 Clearance(Vector3 size)
        {
            float radius = Mathf.Max(.2f, new Vector2(size.x, size.z).magnitude);
            return new Vector3(radius + .04f, Mathf.Max(.2f, size.y) + .02f, radius + .04f);
        }

        internal readonly record struct Place(CustomRoom Room, Vector3 LocalPoint)
        {
            internal Vector3 WorldPoint => Room.transform.TransformPoint(LocalPoint);
        }

        internal static int Layout(SpaceShip ship)
        {
            int mask = 0;
            for (int i = 0; i < ship.Rooms.Length && i < 31; i++)
            {
                if (ship.Rooms[i] != null && ship.Rooms[i].EnabledStructure) mask |= 1 << i;
            }
            return mask;
        }

        internal static void Candidates(SpaceShip ship, int kind, List<Place> points)
        {
            points.Clear();
            foreach (StoragePlaces.Area area in StoragePlaces.For(kind))
            {
                foreach (CustomRoom room in ship.Rooms)
                {
                    if (room == null || !room.EnabledStructure || room.name != area.Room) continue;
                    for (int slot = 0; slot < 3; slot++)
                    {
                        var offset = StoragePlaces.Offset(kind, slot);
                        points.Add(new Place(room, new Vector3(area.X + offset.X, area.Y, area.Z + offset.Z)));
                    }
                    break;
                }
            }
        }

        internal static bool FindFloor(Vector3 point, out RaycastHit floor, bool shipOnly = true)
        {
            floor = default;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * .3f, Vector3.down, Hits, 1.8f,
                NavProbe.ProbeLayers, QueryTriggerInteraction.Ignore);
            if (count == Hits.Length) return Blocked("floor ray buffer full");
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].distance >= nearest) continue;
                nearest = Hits[i].distance;
                floor = Hits[i];
            }
            if (floor.collider == null) return Blocked($"floor ray missed from {point}");
            if (floor.normal.y <= .98f) return Blocked($"sloped hit {floor.collider.name}, normal={floor.normal}");
            if (floor.collider.GetComponentInParent<Grabbable>() != null) return Blocked($"item covers floor: {floor.collider.name}");
            if (shipOnly && NpcVessels.OwnerOfTransform(floor.collider.transform) != NavGraph.ShipOwner)
                return Blocked($"floor is not aboard ship: {floor.collider.name}");
            return true;
        }

        internal static bool Clear(Vector3 center, Vector3 half, Transform? item, bool shipOnly = true)
        {
            if (NpcDoors.ChamberAt(center, withShip: true) != null) return Blocked("airlock chamber");
            if (shipOnly && FindFloor(center, out RaycastHit support) &&
                support.collider.GetComponentInParent<CustomRoom>() is { } room)
            {
                float clearance = Mathf.Max(half.x, half.z) + .8f;
                foreach (Gate door in room.Doors)
                {
                    if (door == null) continue;
                    if (Items.FlatDistanceSq(center, door.transform.position) < clearance * clearance)
                        return Blocked("doorway clearance");
                }
            }
            // Require support beneath the whole footprint, on the same deck.
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    Vector3 sample = center + new Vector3(x * half.x, 0, z * half.z);
                    if (!FindFloor(sample, out RaycastHit floor, shipOnly) ||
                        Mathf.Abs(floor.point.y - (center.y - half.y - .03f)) > .04f) return Blocked("uneven or missing floor support");
                }
            }
            int count = Physics.OverlapBoxNonAlloc(center, half, Overlaps, Quaternion.identity,
                ~0, QueryTriggerInteraction.Collide);
            if (count == Overlaps.Length) return Blocked("overlap buffer full");
            int restricted = LayerMask.NameToLayer("PlaceRestriction");
            int solidMask = item != null ? NavProbe.CollisionMaskFor(item.gameObject.layer) : NavProbe.ProbeLayers;
            for (int i = 0; i < count; i++)
            {
                Collider hit = Overlaps[i];
                if (item != null && hit.transform.IsChildOf(item)) continue;
                if ((!hit.isTrigger && (solidMask & (1 << hit.gameObject.layer)) != 0) || hit.gameObject.layer == restricted ||
                    hit.GetComponentInParent<ItemDetector>() != null ||
                    hit.GetComponentInParent<ItemDestroyer>() != null)
                    return Blocked("blocked by " + hit.name + " (layer " + hit.gameObject.layer + ")");
            }
            return true;
        }

        private static bool Blocked(string why) { LastBlocker = why; return false; }
    }
}

using System.Collections.Generic;
using NPC.Core.Navigation;
using NPC.Core.Agents;
using UnityEngine;

namespace YourBuddy
{
    // Physical storage zones, not inventory slots. docs/storing.md
    internal sealed class FurnitureStorage(Transform root, InstantItemDetector? zone, CustomRoom room, Door[] doors)
    {
        internal readonly Transform Root = root;
        internal readonly InstantItemDetector? Zone = zone;
        internal readonly CustomRoom Room = room;
        internal readonly Door[] Doors = doors;
        internal readonly HidingSpot? Hideout = root.GetComponent<HidingSpot>();
        internal StorageOverflow? Overflow;
        internal string LastBlocker = "unknown obstruction";
        internal Grabbable? LastBlockingItem;
        internal string Name => Overflow != null ? "overflow area" : Items.ContainerKind(Root);
        internal bool Available => Root != null && Room != null && Room.EnabledStructure && Items.Loadable(Root) &&
            (Hideout == null || Hideout.CurrentInteractor == null);
        private readonly Vector3 approach = root.InverseTransformPoint(doors.Length > 0 ? ClosedFace(doors) : root.position);
        internal Vector3 Approach => Overflow != null ? Overflow.Center : Root.TransformPoint(approach);

        internal Vector3[] InsertionPath(Vector3 from, Vector3 point, Vector3 half)
        {
            if (Zone == null || !GameInternals.StorageZoneAccess.TryRead(Zone, out Vector3 size, out Vector3 center))
                return [new Vector3(from.x, point.y, from.z), point];
            if (Name != "chest")
            {
                Quaternion inverse = Quaternion.Inverse(Zone.transform.rotation);
                Vector3 face = inverse * (Approach - center);
                Vector3 target = inverse * (point - center);
                Vector3 margin = ProjectedHalf(half, inverse);
                StorageAccess.Portal portal = StorageAccess.ThroughFace(face.x, face.z, size.x, size.z,
                    margin.x, margin.z, target.x, target.z);
                Vector3 outside = new(portal.OuterX, target.y, portal.OuterZ);
                Vector3 opening = center + Zone.transform.rotation * outside;
                // Clear the open door leaf before moving sideways. docs/storing.md#opening-geometry
                bool alongX = Mathf.Abs(face.x) / size.x > Mathf.Abs(face.z) / size.z;
                Vector3 normal = Zone.transform.rotation * (alongX
                    ? new Vector3(Mathf.Sign(face.x), 0, 0) : new Vector3(0, 0, face.z < 0 ? -1 : 1));
                float depth = Vector3.Dot(opening - center, normal);
                float padding = ProjectedHalf(half, inverse)[alongX ? 0 : 2];
                foreach (Door door in Doors)
                {
                    if (door == null) continue;
                    foreach (Collider collider in door.GetComponentsInChildren<Collider>())
                    {
                        if (!collider.enabled || collider.isTrigger) continue;
                        Bounds bounds = collider.bounds;
                        float radius = Mathf.Abs(normal.x) * bounds.extents.x + Mathf.Abs(normal.z) * bounds.extents.z;
                        depth = StorageAccess.OutsideDoor(depth, Vector3.Dot(bounds.center - center, normal), radius, padding);
                    }
                }
                opening += normal * (depth - Vector3.Dot(opening - center, normal));
                return [new Vector3(opening.x, from.y, opening.z), opening, point];
            }
            float above = Mathf.Max(from.y, center.y + size.y * .5f + half.y + .08f);
            return [new Vector3(from.x, above, from.z), new Vector3(point.x, above, point.z), point];
        }
        private static Vector3 ClosedFace(Door[] doors)
        {
            Vector3 center = Vector3.zero;
            foreach (Door door in doors) center += GameInternals.StorageDoorAccess.ClosedCenter(door);
            return center / doors.Length;
        }
        internal bool DoorsReady
        {
            get
            {
                foreach (Door door in Doors)
                    if (door == null || !GameInternals.StorageDoorAccess.Ready(door)) return false;
                return true;
            }
        }

        private static readonly RaycastHit[] Hits = new RaycastHit[64];
        private static readonly Collider[] Overlaps = new Collider[128];

        internal static void Collect(SpaceShip ship, List<FurnitureStorage> into)
        {
            into.Clear();
            HashSet<Transform> seen = [];
            foreach (CustomRoom room in ship.Rooms)
            {
                if (room == null || !room.EnabledStructure) continue;
                foreach (Door door in room.ContentParent.GetComponentsInChildren<Door>(true))
                {
                    Transform? root = Items.ContainerOf(door, out InstantItemDetector? zone);
                    if (root == null || zone == null || !seen.Add(root) || !Items.Loadable(root)) continue;
                    into.Add(new FurnitureStorage(root, zone, room, root.GetComponentsInChildren<Door>(true)));
                }
            }
        }

        internal bool ReadContents(HashSet<Grabbable> into)
        {
            if (Overflow != null) return Overflow.ReadContents(into);
            if (Zone == null || !GameInternals.StorageZoneAccess.TryRead(Zone, out Vector3 size, out Vector3 center)) return false;
            int count = Physics.OverlapBoxNonAlloc(center, size * .5f, Overlaps, Zone.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            if (count == Overlaps.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Grabbable? item = Overlaps[i].GetComponentInParent<Grabbable>();
                if (item != null) into.Add(item);
            }
            return true;
        }

        internal bool Contains(Vector3 point, Vector3 half)
        {
            if (Overflow != null) return Overflow.Contains(point, half);
            if (Zone == null || !GameInternals.StorageZoneAccess.TryRead(Zone, out Vector3 size, out Vector3 center)) return false;
            Quaternion inverse = Quaternion.Inverse(Zone.transform.rotation);
            Vector3 local = inverse * (point - center);
            Vector3 margin = ProjectedHalf(half, inverse);
            return Mathf.Abs(local.x) + margin.x <= size.x * .5f &&
                Mathf.Abs(local.y) + margin.y <= size.y * .5f && Mathf.Abs(local.z) + margin.z <= size.z * .5f;
        }

        private static Vector3 ProjectedHalf(Vector3 half, Quaternion rotation)
        {
            Vector3 x = rotation * new Vector3(half.x, 0, 0);
            Vector3 y = rotation * new Vector3(0, half.y, 0);
            Vector3 z = rotation * new Vector3(0, 0, half.z);
            return new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        }

        internal bool NeedsArrange(Grabbable item)
        {
            if (Overflow != null || !Items.ColliderBounds(item.gameObject, out Bounds bounds)) return false;
            bool aligned = false;
            for (int i = 0; i < StorageShape.Orientations; i++)
                if (Quaternion.Angle(item.transform.rotation, StorageShape.Rotation(Root, i)) <= 8f) aligned = true;
            if (!aligned || !Contains(bounds.center, bounds.extents)) return true;
            if (Zone == null || !GameInternals.StorageZoneAccess.TryRead(Zone, out Vector3 size, out Vector3 center)) return false;
            Quaternion inverse = Quaternion.Inverse(Zone.transform.rotation);
            Vector3 half = ProjectedHalf(bounds.extents + Vector3.one * .01f, inverse);
            Vector3 local = inverse * (bounds.center - center);
            Vector3 face = inverse * (Approach - center);
            return !StorageAccess.OnPackingGrid(face.x, face.z, size.x, size.z, half.x, half.z, local.x, local.z);
        }

        internal bool ClearTurn(Vector3 center, float radius, Transform item, Transform buddy)
        {
            int count = Physics.OverlapSphereNonAlloc(center, radius, Overlaps,
                NavProbe.CollisionMaskFor(item.gameObject.layer), QueryTriggerInteraction.Ignore);
            if (count == Overlaps.Length) return false;
            for (int i = 0; i < count; i++)
                if (!Overlaps[i].transform.IsChildOf(item) && !Overlaps[i].transform.IsChildOf(buddy)) return false;
            return true;
        }

        internal int CandidateCount(Vector3 half)
        {
            if (Overflow != null) return Overflow.CandidateCount;
            if (Zone == null || !GameInternals.StorageZoneAccess.TryRead(Zone, out Vector3 size, out _))
            { LastBlocker = "storage dimensions unavailable"; return 0; }
            Vector3 margin = ProjectedHalf(half, Quaternion.Inverse(Zone.transform.rotation));
            return margin.y * 2 > size.y ? 0 : StoragePolicy.ProbeColumns(size.x, margin.x) * StoragePolicy.ProbeColumns(size.z, margin.z) * 4;
        }

        // Item-sized columns with a fixed vertical budget. docs/storing.md
        internal bool Probe(int index, Vector3 half, Transform item, out Vector3 point)
        {
            point = default;
            if (Overflow != null)
            {
                bool clear = Overflow.Probe(index, half, item, out point);
                LastBlocker = Overflow.LastBlocker;
                return clear;
            }
            if (!Available || Zone == null || !GameInternals.StorageZoneAccess.TryRead(Zone, out Vector3 size, out Vector3 center)) return false;
            Vector3 margin = ProjectedHalf(half, Quaternion.Inverse(Zone.transform.rotation));
            int columns = StoragePolicy.ProbeColumns(size.x, margin.x), rows = StoragePolicy.ProbeColumns(size.z, margin.z);
            if (columns == 0 || rows == 0 || margin.y * 2 > size.y) return false;
            Vector3 face = Quaternion.Inverse(Zone.transform.rotation) * (Approach - center);
            StorageAccess.Slot slot = StorageAccess.PackingSlot(face.x, face.z, size.x, size.z, margin.x, margin.z, index);
            Vector3 local = new(slot.X, size.y * .5f - (index / (columns * rows)) * size.y / 4f, slot.Z);
            Vector3 ray = center + Zone.transform.rotation * local;
            if (!Support(ray + Vector3.up * .02f, size.y / 4f + .04f, item, out RaycastHit support))
            { LastBlocker = $"no shelf support at {ray}"; return false; }
            point = support.point + Vector3.up * (half.y + .015f);
            return Clear(point, half, item, true);
        }

        internal bool Settled(Grabbable item)
        {
            if (item == null || !item.gameObject.activeInHierarchy || item.IsGrabbed ||
                !Items.ColliderBounds(item.gameObject, out Bounds bounds) || !Contains(bounds.center, bounds.extents)) return false;
            return item.CachedRigidbody == null || item.CachedRigidbody.IsSleeping() ||
                item.CachedRigidbody.velocity.sqrMagnitude < .0025f;
        }

        private bool Support(Vector3 from, float distance, Transform item, out RaycastHit support)
        {
            support = default;
            int count = Physics.RaycastNonAlloc(from, Vector3.down, Hits, distance, NavProbe.ProbeLayers, QueryTriggerInteraction.Ignore);
            if (count == Hits.Length) return false;
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].transform.IsChildOf(item) || Hits[i].distance >= nearest) continue;
                nearest = Hits[i].distance;
                support = Hits[i];
            }
            return support.collider != null && support.normal.y > .98f && support.transform.IsChildOf(Root) &&
                support.collider.GetComponentInParent<Grabbable>() == null;
        }

        internal bool Clear(Vector3 point, Vector3 half, Transform item, bool closedDoors)
        {
            LastBlockingItem = null;
            if (!Available) { LastBlocker = "storage unavailable"; return false; }
            if (!Contains(point, half)) { LastBlocker = $"outside storage bounds at {point}, half={half}"; return false; }
            if (Overflow != null)
            {
                bool clear = Overflow.Clear(point, half, item);
                LastBlocker = Overflow.LastBlocker;
                return clear;
            }
            // Centre and corners must rest on furniture, not an item or the room floor below it.
            for (int i = 0; i < 5; i++)
            {
                Vector3 offset = i == 0 ? Vector3.zero : new Vector3((i % 2 == 0 ? 1 : -1) * half.x, 0, (i <= 2 ? -1 : 1) * half.z);
                Vector3 bottom = point + offset - Vector3.up * half.y;
                if (!Support(bottom + Vector3.up * .04f, .09f, item, out RaycastHit support) || Mathf.Abs(support.point.y - (bottom.y - .015f)) > .025f)
                { LastBlocker = $"missing or uneven shelf support at {bottom}"; return false; }
            }
            int count = Physics.OverlapBoxNonAlloc(point, half, Overlaps, Quaternion.identity, ~0, QueryTriggerInteraction.Collide);
            if (count == Overlaps.Length) { LastBlocker = "slot overlap buffer full"; return false; }
            int mask = NavProbe.CollisionMaskFor(item.gameObject.layer);
            int restricted = LayerMask.NameToLayer("PlaceRestriction");
            for (int i = 0; i < count; i++)
            {
                Collider hit = Overlaps[i];
                if (hit.transform.IsChildOf(item)) continue;
                if (closedDoors && IsDoor(hit.transform)) continue;
                if ((!hit.isTrigger && (mask & (1 << hit.gameObject.layer)) != 0) || hit.gameObject.layer == restricted ||
                    hit.GetComponentInParent<ItemDetector>() != null || hit.GetComponentInParent<ItemDestroyer>() != null)
                {
                    LastBlockingItem = hit.GetComponentInParent<Grabbable>();
                    LastBlocker = $"slot blocked by {hit.name} at {point}";
                    return false;
                }
            }
            return true;
        }

        private bool IsDoor(Transform t)
        {
            foreach (Door door in Doors)
            {
                if (door != null && t.IsChildOf(door.transform)) return true;
            }
            return false;
        }

        internal static bool PutDownSafely(NpcHands hands, Transform buddy, Vector3 currentHalf)
        {
            if (hands.Item == null) return true;
            Vector3 half = ResourceStorage.Clearance(currentHalf);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                Vector3 sample = buddy.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .9f;
                if (!ResourceStorage.FindFloor(sample, out RaycastHit floor, shipOnly: false)) continue;
                Vector3 point = floor.point + Vector3.up * (half.y + .03f);
                if (!ResourceStorage.Clear(point, half, hands.Item.transform, shipOnly: false) ||
                    !NavProbe.WalkLos(buddy.position, sample, .3f)) continue;
                hands.PutDown(point, Vector3.zero);
                YourBuddyPlugin.Log.LogInfo("[store] Carry ended: item set on checked clear floor; not stored");
                return true;
            }
            return false;
        }

        internal bool ClearReach(Vector3 from, Vector3 to, Vector3 half, Transform item, Transform buddy)
        {
            LastBlockingItem = null;
            Vector3 delta = to - from;
            int count = Physics.BoxCastNonAlloc(from, half, delta.normalized, Hits, Quaternion.identity,
                delta.magnitude, NavProbe.CollisionMaskFor(item.gameObject.layer), QueryTriggerInteraction.Ignore);
            if (count == Hits.Length) { LastBlocker = "sweep hit buffer full"; return false; }
            for (int i = 0; i < count; i++)
            {
                if (!Hits[i].transform.IsChildOf(item) && !Hits[i].transform.IsChildOf(buddy))
                {
                    LastBlockingItem = Hits[i].collider.GetComponentInParent<Grabbable>();
                    LastBlocker = $"{Hits[i].transform.name}#{Hits[i].collider.GetInstanceID()} (layer {Hits[i].collider.gameObject.layer}) bounds={Hits[i].collider.bounds} sweepFrom={from} sweepTo={to} half={half} hitDistance={Hits[i].distance:0.000}";
                    return false;
                }
            }
            return true;
        }
    }
}

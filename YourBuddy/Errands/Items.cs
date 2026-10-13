using System.Collections.Generic;
using NPC.Core;
using NPC.Core.Agents;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// What every item errand shares. How close an item is reached from, which items count as trash,
    /// what is put away in a container, and opening and closing one. docs/items.md, docs/snacks.md
    /// </summary>
    internal static class Items
    {
        /// <summary>
        /// Up close, unlike a terminal. The flat distance from the buddy to a door's face or an item's top.
        /// </summary>
        public const float ItemReachDist = 0.9f;
        public static readonly float[] ItemStandOffs = [0.55f, 0.7f, 0.85f];
        public const float ItemReachBelow = 0.3f;
        /// <summary>
        /// Loose food further than this from where it was chosen has been moved; the walk ends.
        /// </summary>
        public const float ItemMovedDist = 0.5f;
        /// <summary>
        /// The door opens over about 2 s (Door.openSpeed 0.5); the buddy looks in meanwhile.
        /// </summary>
        public const float DoorOpenSeconds = 1.5f;
        /// <summary>
        /// A container this far beyond a search radius still has its contents kept out of the loose items.
        /// </summary>
        public const float ContainerMargin = 2f;
        /// <summary>
        /// Each interval is the configured one, this much either way at random.
        /// </summary>
        public const float IntervalJitter = 0.25f;
        public const float ItemReachSeconds = 0.5f;
        public const float ItemLiftSeconds = 0.5f;
        public const float ItemAimSeconds = 0.4f;
        /// <summary>
        /// How many of the nearest candidates a task picks between at random, instead of always the
        /// first. docs/items.md
        /// </summary>
        private const int PickNearest = 3;
        /// <summary>
        /// How many parents up from a Door its container's root may be (a closet door: GameObject, then Closet).
        /// </summary>
        private const int ContainerSearchDepth = 2;

        /// <summary>
        /// Borrowed by one collector at a time, and left empty.
        /// </summary>
        private static readonly HashSet<Transform> CheckedContainers = [];
        public static readonly HashSet<Grabbable> PutAwayItems = [];
        private static readonly List<ContainerSpot> NearbyContainers = [];

        public static float FlatDistanceSq(Vector3 a, Vector3 b)
        {
            Vector3 flat = a - b;
            flat.y = 0f;
            return flat.sqrMagnitude;
        }

        /// <summary>
        /// Why an item at `now` is no longer the one planned for at `planned`, or null.
        /// </summary>
        public static string? MovedBlocker(Vector3 now, Vector3 planned) =>
            FlatDistanceSq(now, planned) > ItemMovedDist * ItemMovedDist ? "it has been moved" : null;

        public static bool ColliderBounds(GameObject root, out Bounds bounds) => NpcHands.ColliderBounds(root, out bounds);

        /// <summary>
        /// Within `radius` of `here`, flat, and at a height reached from `floorY`: an item's top at most
        /// ItemReachBelow under the floor, at most ReachTask.ReachHeight over it. Whose vessel is the caller's question.
        /// </summary>
        public static bool InReachBand(Vector3 point, Vector3 here, float floorY, float radius) =>
            FlatDistanceSq(point, here) <= radius * radius &&
            point.y >= floorY - ItemReachBelow && point.y <= floorY + ReachTask.ReachHeight;

        /// <summary>
        /// The top of the item's colliders, the part visible on a floor or a shelf.
        /// </summary>
        public static Vector3 ItemTop(Component item)
        {
            if (!ColliderBounds(item.gameObject, out Bounds bounds)) return item.transform.position;

            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }

        /// <summary>
        /// "Trash (2)" is "Trash".
        /// </summary>
        public static string ItemLabelOf(Grabbable item) => BaseName(item.gameObject.name);

        /// <summary>
        /// A scene copy's name without its " (n)" suffix.
        /// </summary>
        public static string BaseName(string name)
        {
            int copy = name.IndexOf(" (", System.StringComparison.Ordinal);
            return copy > 0 ? name[..copy] : name;
        }

        /// <summary>
        /// Why an item may not be picked up now, or null. `held` is what the buddy already has in its hands.
        /// </summary>
        public static string? TakeBlocker(Grabbable? item, Grabbable? held)
        {
            if (item == null || !item.gameObject.activeSelf) return "it is gone";

            if (!item.gameObject.activeInHierarchy) return "its room is not loaded";

            if (item.IsGrabbed) return "someone took it";

            return item.restrictGrab && item != held ? "it is held down" : null;
        }

        /// <summary>
        /// Switched off with its room's content, not gone. A sale or a trash can clears the item's own flag
        /// (Grabbable.Destroy); an unloaded room leaves it alone. docs/items.md#4-selling-trash-boxes
        /// </summary>
        public static bool InUnloadedRoom(Grabbable? item) =>
            item != null && item.gameObject.activeSelf && !item.gameObject.activeInHierarchy;

        /// <summary>
        /// Active, or off only with its room's content, which LoadRoomOf switches back on. Not on a station
        /// you are not docked at, since its interior lies in the docked one's place and its rooms cannot load.
        /// </summary>
        public static bool Loadable(Component thing)
        {
            if (thing.gameObject.activeInHierarchy) return true;

            Room? room = thing.GetComponentInParent<Room>(true);
            if (room == null || !room.gameObject.activeInHierarchy || room.ContentEnabled) return false;

            for (Transform? t = thing.transform; t != null && t != room.ContentParent; t = t.parent)
            {
                if (!t.gameObject.activeSelf) return false;
            }
            return thing.transform.IsChildOf(room.ContentParent);
        }

        /// <summary>
        /// A wrapper, can, empty seed pack or broken loot box the game turned to trash, or a trash item.
        /// Never something still useful. CanTrash alone is not trash, since first-aid kits have it. docs/items.md §1
        /// </summary>
        public static bool IsTrash(Grabbable item)
        {
            if (!item.CanTrash) return false;

            if (item.Signature == "TRASH") return true;

            if (item.TryGetComponent(out Food food) && food.Data is { usages: 0 }) return true;

            if (item.TryGetComponent(out SeedPack pack) && pack.Seeds == 0) return true;

            return item.TryGetComponent(out LootBox box) && box.Broken;
        }

        /// <summary>
        /// What idle play may pick up. Trash, or any loose item with ItemPlayAnything. docs/items.md §5
        /// </summary>
        public static bool IsPlaything(Grabbable item) =>
            YourBuddyPlugin.ConfigItemPlayAnything.Value || IsTrash(item);

        /// <summary>
        /// Nearest first is the right order; always taking the very nearest is what makes the buddy
        /// look mechanical. Shuffles the head of an already sorted candidate list. docs/items.md
        /// </summary>
        public static void ShuffleNearest<T>(List<T> sorted)
        {
            for (int i = Mathf.Min(PickNearest, sorted.Count) - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (sorted[i], sorted[j]) = (sorted[j], sorted[i]);
            }
        }

        /// <summary>
        /// Collects into PutAwayItems what sits in a fridge, cabinet, closet, chest or locker within
        /// `radius` of `here`, flat. Those items are never loose. docs/items.md §1
        /// </summary>
        public static void CollectContainerContents(Vector3 here, float radius)
        {
            CheckedContainers.Clear();
            foreach (Door door in SceneScan.ThisFrame<Door>())
            {
                Transform? root = ContainerOf(door, out InstantItemDetector? contents);
                if (root == null || !CheckedContainers.Add(root) || FlatDistanceSq(root.position, here) > radius * radius) continue;

                // Both were found together.
                PutAwayItems.UnionWith(contents!.GetItemsInZone());
            }
            CheckedContainers.Clear();
        }

        /// <summary>
        /// A fridge, cabinet, closet, chest or locker near the buddy: what holds its contents, its doors,
        /// and the face between them it is walked up to.
        /// </summary>
        public readonly struct ContainerSpot(Transform root, InstantItemDetector contents, Door[] doors, Vector3 face)
        {
            public readonly Transform Root = root;
            public readonly InstantItemDetector Contents = contents;
            public readonly Door[] Doors = doors;
            public readonly Vector3 Face = face;

            /// <summary>
            /// In a custom room whose structure is switched off.
            /// </summary>
            public bool StructureOff()
            {
                CustomRoom room = Root.GetComponentInParent<CustomRoom>(true);
                return room != null && !room.EnabledStructure;
            }
        }

        /// <summary>
        /// Every container whose doors' face is within `radius` of `here`, flat, once each (a closet has two
        /// doors). The list is shared, so one collector at a time. docs/snacks.md §1
        /// </summary>
        public static List<ContainerSpot> ContainersNear(Vector3 here, float radius)
        {
            NearbyContainers.Clear();
            CheckedContainers.Clear();
            foreach (Door door in SceneScan.ThisFrame<Door>())
            {
                Transform? root = ContainerOf(door, out InstantItemDetector? contents);
                if (root == null || !CheckedContainers.Add(root)) continue;

                Door[] doors = root.GetComponentsInChildren<Door>();
                Vector3 face = DoorFacePoint(doors);
                // Both were found together.
                if (FlatDistanceSq(face, here) <= radius * radius) NearbyContainers.Add(new ContainerSpot(root, contents!, doors, face));
            }
            CheckedContainers.Clear();
            return NearbyContainers;
        }

        /// <summary>
        /// The object a furniture door belongs to, the nearest ancestor with a Furniture child that
        /// has an itemMover. In the scene that is every fridge, closet, chest and locker. docs/snacks.md §1
        /// </summary>
        public static Transform? ContainerOf(Door door, out InstantItemDetector? contents)
        {
            contents = null;
            Transform parent = door.transform.parent;
            for (int depth = 0; parent != null && depth < ContainerSearchDepth; depth++, parent = parent.parent)
            {
                foreach (Transform child in parent)
                {
                    if (!child.TryGetComponent(out Furniture furniture)) continue;

                    contents = GameInternals.FurnitureAccess.GetItemMover(furniture);
                    if (contents != null) return parent;
                }
            }
            return null;
        }

        /// <summary>
        /// Between the closed doors' colliders, the face the buddy walks up to.
        /// </summary>
        public static Vector3 DoorFacePoint(Door[] doors)
        {
            Vector3 sum = Vector3.zero;
            foreach (Door door in doors)
            {
                sum += ColliderBounds(door.gameObject, out Bounds bounds) ? bounds.center : door.transform.position;
            }
            return sum / doors.Length;
        }

        public static string ContainerKind(Transform container)
        {
            string name = container.name;
            if (name.Contains("Fridge")) return "fridge";

            if (name.Contains("Closet") || name.Contains("Cabinet")) return "cabinet";

            if (name.Contains("Chest")) return "chest";

            return name.Contains("Locker") ? "locker" : "container";
        }

        /// <summary>
        /// Door.Open, which the player's Door.Switch calls. Plays the sound and animation and fires the
        /// scene's OnOpen wiring (fridge light and freezer, hiding spot). docs/snacks.md §1
        /// </summary>
        public static void OpenContainerDoors(Door[] doors, List<Door> opened, string name)
        {
            foreach (Door door in doors)
            {
                if (door == null || door.Opened) continue;

                door.Open();
                opened.Add(door);
            }
            YourBuddyPlugin.Log.LogInfo(opened.Count > 0
                ? $"[ai] Opened {name}"
                : $"[ai] Looking into {name} - it was already open");
        }

        /// <summary>
        /// However a snack or tidying ended. Not while the player hides inside, since that is their door.
        /// docs/invariants.md#a-snack-closes-what-it-opened
        /// </summary>
        public static void CloseOpenedDoors(List<Door> opened, HidingSpot? hideout, string name)
        {
            if (opened.Count == 0) return;

            bool hiding = hideout != null && hideout.CurrentInteractor != null;
            int closed = 0;
            foreach (Door door in opened)
            {
                if (hiding || door == null || !door.Opened) continue;

                door.Close();
                closed++;
            }
            opened.Clear();
            if (closed > 0) YourBuddyPlugin.Log.LogInfo($"[ai] Closed {name}");
        }
    }
}

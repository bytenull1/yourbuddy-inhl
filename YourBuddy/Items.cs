using System.Collections.Generic;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// What every item errand shares: how close an item is reached from, which items count as trash,
    /// what is put away in a container, and opening and closing one. docs/items.md, docs/snacks.md
    /// </summary>
    internal static class Items
    {
        /// <summary>
        /// Up close, unlike a terminal: the flat distance from the buddy to a door's face or an item's top.
        /// </summary>
        public const float SnackReachDist = 0.9f;
        public static readonly float[] SnackStandOffs = [0.55f, 0.7f, 0.85f];
        public const float SnackReachBelow = 0.3f;
        /// <summary>
        /// Loose food further than this from where it was chosen has been moved; the walk ends.
        /// </summary>
        public const float SnackItemMovedDist = 0.5f;
        /// <summary>
        /// The door opens over about 2 s (Door.openSpeed 0.5); the buddy looks in meanwhile.
        /// </summary>
        public const float SnackOpenSeconds = 1.5f;
        /// <summary>
        /// A container this far beyond a search radius still has its contents kept out of the loose items.
        /// </summary>
        public const float SnackContainerMargin = 2f;
        /// <summary>
        /// Each interval is the configured one, this much either way at random.
        /// </summary>
        public const float SnackIntervalJitter = 0.25f;
        public const float TidyReachSeconds = 0.5f;
        public const float TidyLiftSeconds = 0.5f;
        public const float TidyAimSeconds = 0.4f;
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
        public static readonly HashSet<Transform> CheckedContainers = [];
        public static readonly HashSet<Grabbable> PutAwayItems = [];

        public static float FlatDistanceSq(Vector3 a, Vector3 b)
        {
            Vector3 flat = a - b;
            flat.y = 0f;
            return flat.sqrMagnitude;
        }

        public static bool ColliderBounds(GameObject root, out Bounds bounds)
        {
            bool found = false;
            bounds = default;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger) continue;

                if (found) bounds.Encapsulate(collider.bounds);
                else bounds = collider.bounds;
                found = true;
            }
            return found;
        }

        public static Vector3 ItemTop(Grabbable item)
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
            if (item == null || !item.gameObject.activeInHierarchy) return "it is gone";

            if (item.IsGrabbed) return "someone took it";

            return item.restrictGrab && item != held ? "it is held down" : null;
        }

        /// <summary>
        /// A wrapper, can, empty seed pack or broken loot box the game turned to trash, or a trash item:
        /// never something still useful. CanTrash alone is not trash - first-aid kits have it. docs/items.md §1
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
        /// What idle play may pick up: trash, or with ItemPlayAnything any loose item. docs/items.md §5
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
        /// What sits in a fridge, cabinet, closet, chest or locker within `radius` of `here`, flat, into
        /// PutAwayItems: never loose. docs/items.md §1
        /// </summary>
        public static void CollectContainerContents(Vector3 here, float radius)
        {
            CheckedContainers.Clear();
            foreach (Door door in Object.FindObjectsOfType<Door>())
            {
                Transform? root = ContainerOf(door, out InstantItemDetector? contents);
                if (root == null || !CheckedContainers.Add(root) || FlatDistanceSq(root.position, here) > radius * radius) continue;

                // Both were found together.
                PutAwayItems.UnionWith(contents!.GetItemsInZone());
            }
            CheckedContainers.Clear();
        }

        /// <summary>
        /// The object a furniture door belongs to: the nearest ancestor with a Furniture child that
        /// has an itemMover. Scene: every fridge, closet, chest and locker. docs/snacks.md §1
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
        /// Between the closed doors' colliders: the face the buddy walks up to.
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
        /// Door.Open, which is what the player's Door.Switch calls: sound, animation and the scene's
        /// OnOpen wiring (fridge light and freezer, hiding spot). docs/snacks.md §1
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
        /// However a snack or tidying ended. Not while the player hides inside: that is their door.
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

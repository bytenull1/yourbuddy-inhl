using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using FMODUnity;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Reflection into the game's private members that YourBuddy alone needs. Each member resolves
    /// once and warns once, naming what its loss degrades. Misses return null/default. NPC.Core
    /// keeps its own. npc-core:docs/invariants.md#reflection-lives-in-gameinternals
    /// </summary>
    internal static class GameInternals
    {
        private static readonly HashSet<string> WarnedMembers = [];
        private static int _resolvedCount;

        /// <summary>
        /// Runs every accessor group's initializer now, so a game update reports all its
        /// missing members at plugin load instead of whenever each feature is first used.
        /// </summary>
        internal static void ResolveAll()
        {
            foreach (Type group in typeof(GameInternals).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
            {
                try
                {
                    RuntimeHelpers.RunClassConstructor(group.TypeHandle);
                }
                catch (TypeInitializationException ex)
                {
                    YourBuddyPlugin.Log.LogError($"[internals] {group.Name} failed to resolve: {ex.InnerException}");
                }
            }

            if (WarnedMembers.Count == 0)
            {
                YourBuddyPlugin.Log.LogInfo($"[internals] All {_resolvedCount} game members resolved.");
            }
            else
            {
                YourBuddyPlugin.Log.LogWarning(
                    $"[internals] {WarnedMembers.Count} of {_resolvedCount} game members missing - see warnings above.");
            }
        }

        internal static class ResourceAccess
        {
            private const string Feature = "resource duties";
            private static readonly FieldInfo? Cell = Field<ResourceContainer>(typeof(ResourceController), "currentCell", Feature);
            private static readonly FieldInfo? Detector = Field<ItemDetector>(typeof(ResourceController), "itemDetector", Feature);
            private static readonly FieldInfo? Loading = Field<bool>(typeof(ResourceController), "loading", Feature);
            internal static bool Ready => Cell != null && Detector != null && Loading != null;
            internal static ResourceContainer? Current(ResourceController controller) => Get<ResourceContainer>(Cell, controller);
            internal static ItemDetector? Slot(ResourceController controller) => Get<ItemDetector>(Detector, controller);
            internal static bool IsLoading(ResourceController controller) => GetValue<bool>(Loading, controller) == true;
        }

        internal static class ShopAccess
        {
            private const string Feature = "resource purchases";
            private static readonly FieldInfo? Items = Field<Grabbable[]>(typeof(Shop), "items", Feature);
            private static readonly FieldInfo? Selection = Field<int>(typeof(Shop), "currentItem", Feature);
            private static readonly FieldInfo? Spawn = Field<Transform>(typeof(Shop), "spawnPoint", Feature);
            internal static Grabbable[]? Stock(Shop shop) => Get<Grabbable[]>(Items, shop);
            internal static Transform? Outlet(Shop shop) => Get<Transform>(Spawn, shop);
            internal static bool Ready => Items != null && Selection != null && Spawn != null;
            internal static void Buy(Shop shop, int index, Player player)
            {
                if (Selection == null) return;
                object old = Selection.GetValue(shop);
                try
                {
                    Selection.SetValue(shop, index);
                    shop.TryBuyChosenItem(player);
                }
                finally { Selection.SetValue(shop, old); }
            }
        }

        private const BindingFlags MemberFlags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;

        /// <summary>
        /// A field whose type no longer holds a T counts as missing, so no accessor ever
        /// casts a changed type or silently reads null.
        /// </summary>
        /// <remarks>
        /// A `critical` member (the buddy's wake-up, the dialog's keypress) logs its miss as an
        /// error, since a player notices that loss at once.
        /// </remarks>
        private static FieldInfo? Field<T>(Type type, string name, string feature, bool critical = false)
        {
            _resolvedCount++;
            FieldInfo? field = type.GetField(name, MemberFlags);
            if (field == null)
            {
                WarnOnce(type, name, "not found", feature, critical);
                return null;
            }
            if (!typeof(T).IsAssignableFrom(field.FieldType))
            {
                WarnOnce(type, name, "changed type from " + typeof(T).Name + " to " + field.FieldType.Name, feature, critical);
                return null;
            }
            return field;
        }

        private static void WarnOnce(Type type, string name, string problem, string feature, bool critical = false)
        {
            if (!WarnedMembers.Add(type.Name + "." + name)) return;

            string message = "[internals] Game internals changed: member '" + name + "' on '" + type.Name + "' " + problem +
                             ". A game update probably changed it - " + feature + " may be broken.";
            if (critical) YourBuddyPlugin.Log.LogError(message);
            else YourBuddyPlugin.Log.LogWarning(message);
        }

        private static T? Get<T>(FieldInfo? field, object? instance) where T : class
        {
            return field == null || instance == null ? null : field.GetValue(instance) as T;
        }

        /// <summary>
        /// Ragdoll parts and model of the PlayerController prefab a buddy is cloned from.
        /// </summary>
        internal static class PlayerControllerAccess
        {
            private static readonly FieldInfo? RagdollObject = Field<GameObject>(typeof(PlayerController), "ragdollObject", "buddy ragdoll death");
            private static readonly FieldInfo? Ragdoll = Field<Rigidbody>(typeof(PlayerController), "ragdoll", "buddy ragdoll death");
            private static readonly FieldInfo? ModelAnimator = Field<Animator>(typeof(PlayerController), "animator", "buddy model setup");

            internal static GameObject? GetRagdollObject(PlayerController? controller) => Get<GameObject>(RagdollObject, controller);
            internal static Rigidbody? GetRagdollRigidbody(PlayerController? controller) => Get<Rigidbody>(Ragdoll, controller);
            internal static Animator? GetModelAnimator(PlayerController? controller) => Get<Animator>(ModelAnimator, controller);
        }

        /// <summary>
        /// The player prefab on GameManager that the buddy is cloned from.
        /// </summary>
        internal static class GameManagerAccess
        {
            private static readonly FieldInfo? PlayerPrefab = Field<Player>(typeof(GameManager), "playerPrefab", "buddy spawning");

            internal static Player? GetPlayerPrefab(GameManager? manager) => Get<Player>(PlayerPrefab, manager);
        }

        /// <summary>
        /// How the player's pod opens, replayed on the prop capsule the buddy wakes in.
        /// docs/game-model.md#the-cryo-room
        /// </summary>
        internal static class CryoPodAnimatorAccess
        {
            private const string Feature = "the buddy waking in a cryo capsule on a new game";
            private static readonly FieldInfo? Door = Field<Transform>(typeof(CryoPodAnimator), "door", Feature, critical: true);
            private static readonly FieldInfo? Curve = Field<AnimationCurve>(typeof(CryoPodAnimator), "openAnimationCurve", Feature, critical: true);
            private static readonly FieldInfo? Speed = Field<float>(typeof(CryoPodAnimator), "animationSpeed", Feature, critical: true);
            private static readonly FieldInfo? SlideRange = Field<float>(typeof(CryoPodAnimator), "slideRange", Feature, critical: true);
            private static readonly FieldInfo? OpenRange = Field<float>(typeof(CryoPodAnimator), "openRange", Feature, critical: true);
            private static readonly FieldInfo? OpenEvent = Field<EventReference>(typeof(CryoPodAnimator), "openEvent", Feature, critical: true);

            internal static Transform? GetDoor(CryoPodAnimator? animator) => Get<Transform>(Door, animator);
            internal static AnimationCurve? GetCurve(CryoPodAnimator? animator) => Get<AnimationCurve>(Curve, animator);
            internal static float? GetSpeed(CryoPodAnimator? animator) => GetValue<float>(Speed, animator);
            internal static float? GetSlideRange(CryoPodAnimator? animator) => GetValue<float>(SlideRange, animator);
            internal static float? GetOpenRange(CryoPodAnimator? animator) => GetValue<float>(OpenRange, animator);
            internal static EventReference? GetOpenEvent(CryoPodAnimator? animator) => GetValue<EventReference>(OpenEvent, animator);
        }

        private static T? GetValue<T>(FieldInfo? field, object? instance) where T : struct
        {
            return field != null && instance != null && field.GetValue(instance) is T value ? value : null;
        }

        /// <summary>
        /// The point the monster casts its sight line from. The buddy looks there to see it.
        /// docs/fear.md
        /// </summary>
        internal static class BreathlessControllerAccess
        {
            private static readonly FieldInfo? RaycastPoint = Field<Transform>(typeof(BreathlessController), "raycastPoint",
                "the buddy seeing the Breathless, which falls back to the monster's origin");

            internal static Transform? GetRaycastPoint(BreathlessController? controller) =>
                Get<Transform>(RaycastPoint, controller);
        }

        /// <summary>
        /// Physical item-zone dimensions for furniture placement. docs/storing.md
        /// </summary>
        internal static class StorageZoneAccess
        {
            private const string Feature = "storing loose items";
            private static readonly FieldInfo? Size = Field<Vector3>(typeof(InstantItemDetector), "boxSize", Feature);
            private static readonly FieldInfo? Offset = Field<Vector3>(typeof(InstantItemDetector), "boxCenterOffset", Feature);
            internal static bool TryRead(InstantItemDetector zone, out Vector3 size, out Vector3 center)
            {
                size = GetValue<Vector3>(Size, zone) ?? Vector3.zero;
                center = zone.transform.position + zone.transform.TransformDirection(GetValue<Vector3>(Offset, zone) ?? Vector3.zero);
                return Size != null && Offset != null && size.x > 0 && size.y > 0 && size.z > 0;
            }
        }

        internal static class StorageDoorAccess
        {
            private const string Feature = "storage door clearance";
            private static readonly FieldInfo? Anchor = Field<Transform>(typeof(Door), "doorAnchor", Feature);
            private static readonly FieldInfo? Elapsed = Field<float>(typeof(Door), "elapsed", Feature);
            internal static bool Ready(Door door) => door.Opened && (GetValue<float>(Elapsed, door) ?? 0f) >= .999f;
            internal static Vector3 ClosedCenter(Door door)
            {
                Vector3 center = Items.ColliderBounds(door.gameObject, out Bounds bounds) ? bounds.center : door.transform.position;
                Transform? anchor = Get<Transform>(Anchor, door);
                if (anchor == null) return center;
                Vector3 closed = anchor.localPosition + Vector3.Scale(anchor.InverseTransformPoint(center), anchor.localScale);
                return anchor.parent == null ? closed : anchor.parent.TransformPoint(closed);
            }
        }

        internal static class FurnitureAccess
        {
            private static readonly FieldInfo? ItemMover = Field<InstantItemDetector>(typeof(Furniture), "itemMover",
                "the buddy's snacks (finding containers and what is in them)");

            internal static InstantItemDetector? GetItemMover(Furniture? furniture) => Get<InstantItemDetector>(ItemMover, furniture);
        }

        /// <summary>
        /// The TrashCan trigger that takes an item in. docs/items.md §1
        /// </summary>
        internal static class TrashCanAccess
        {
            private static readonly FieldInfo? ItemDestroyer = Field<ItemDestroyer>(typeof(TrashCan), "itemDestroyer",
                "the buddy tidying trash into trash cans");

            internal static ItemDestroyer? GetItemDestroyer(TrashCan? bin) => Get<ItemDestroyer>(ItemDestroyer, bin);
        }

        /// <summary>
        /// SellStation detectors (items loaded, anyone caught inside), gate and button. docs/items.md §1
        /// </summary>
        internal static class SellStationAccess
        {
            private const string Feature = "the buddy selling trash boxes";
            private static readonly FieldInfo? ItemDetector = Field<ItemDetector>(typeof(SellStation), "itemDetector", Feature);
            private static readonly FieldInfo? InstantDetector = Field<InstantItemDetector>(typeof(SellStation), "instantDetector", Feature);
            private static readonly FieldInfo? StationGate = Field<Gate>(typeof(SellStation), "gate", Feature);
            private static readonly FieldInfo? SellButton = Field<Space.Button>(typeof(SellStation), "sellButton", Feature);

            internal static ItemDetector? GetItemDetector(SellStation? station) => Get<ItemDetector>(ItemDetector, station);
            internal static InstantItemDetector? GetInstantDetector(SellStation? station) => Get<InstantItemDetector>(InstantDetector, station);
            internal static Gate? GetGate(SellStation? station) => Get<Gate>(StationGate, station);
            internal static Space.Button? GetSellButton(SellStation? station) => Get<Space.Button>(SellButton, station);
        }

        /// <summary>
        /// The player's suit equip sound, replayed when the buddy suits up. docs/eva.md
        /// </summary>
        internal static class EquipmentSystemAccess
        {
            private static readonly FieldInfo? SuitEquipEvent = Field<EventReference>(typeof(EquipmentSystem), "suitEquipEvent",
                "the EVA suit's equip sound");

            internal static EventReference? GetSuitEquipEvent(EquipmentSystem? system) => GetValue<EventReference>(SuitEquipEvent, system);
        }

        /// <summary>
        /// The game's own sounds the buddy's anomalies borrow, read off whichever instance is in the
        /// scene. docs/anomalies.md#5-sounds
        /// </summary>
        internal static class ScareSoundAccess
        {
            private const string Feature = "the buddy's anomaly sounds (that one stays silent)";
            private static readonly FieldInfo? Screech = Field<EventReference>(typeof(Breathless), "screechSound", Feature);
            private static readonly FieldInfo? Talk = Field<EventReference>(typeof(AssistanceBot), "talkSound", Feature);
            private static readonly FieldInfo? Scream = Field<EventReference>(typeof(UnsealScream), "screamSound", Feature);
            private static readonly FieldInfo? RandomEvent = Field<EventReference>(typeof(RandomSound), "sound", Feature);
            private static readonly FieldInfo? Background = Field<EventReference>(typeof(BackgroundSound), "scarySound", Feature);

            internal static EventReference? GetScreech(Breathless? b) => GetValue<EventReference>(Screech, b);
            internal static EventReference? GetTalk(AssistanceBot? bot) => GetValue<EventReference>(Talk, bot);
            internal static EventReference? GetScream(UnsealScream? e) => GetValue<EventReference>(Scream, e);
            internal static EventReference? GetRandom(RandomSound? e) => GetValue<EventReference>(RandomEvent, e);
            internal static EventReference? GetBackground(BackgroundSound? e) => GetValue<EventReference>(Background, e);
        }

        /// <summary>
        /// Save id and item flags for anomaly props. They clone the game's own item and blood decal,
        /// and the save never sees them. docs/anomalies.md#meat
        /// </summary>
        internal static class PropAccess
        {
            private const string Feature = "the props anomalies leave: meat, blood, the bloody pipe (none are made)";
            private static readonly FieldInfo? GrabbableId = Field<uint>(typeof(SaveObject<Space.Data.GrabbableData>), "id", Feature);
            private static readonly FieldInfo? CleanableId = Field<uint>(typeof(SaveObject<Space.Data.ByteData>), "id", Feature);
            private static readonly FieldInfo? Signature = Field<string>(typeof(Grabbable), "signature", Feature);
            private static readonly FieldInfo? CanStore = Field<bool>(typeof(Grabbable), "canStore", Feature);
            private static readonly FieldInfo? CanSell = Field<bool>(typeof(Grabbable), "canSell", Feature);
            private static readonly FieldInfo? CanTrash = Field<bool>(typeof(Grabbable), "canTrash", Feature);
            private static readonly FieldInfo? Price = Field<int>(typeof(Grabbable), "price", Feature);

            /// <summary>
            /// All members resolved. A clone that kept the original's save id would share its data.
            /// </summary>
            internal static bool Ready => GrabbableId != null && CleanableId != null && Signature != null &&
                                          CanStore != null && CanSell != null && CanTrash != null && Price != null;

            // Ready is checked by every caller first.
            internal static void ClearId(Grabbable item) => GrabbableId!.SetValue(item, 0u);
            internal static void ClearId(Cleanable dirt) => CleanableId!.SetValue(dirt, 0u);

            internal static void SetItem(Grabbable item, string signature, bool canStore, bool canSell, bool canTrash, int price)
            {
                Signature!.SetValue(item, signature);
                CanStore!.SetValue(item, canStore);
                CanSell!.SetValue(item, canSell);
                CanTrash!.SetValue(item, canTrash);
                Price!.SetValue(item, price);
            }

            private static readonly FieldInfo? LastImpact = Field<float>(typeof(Grabbable), "lastImpactTime",
                "the meat's own landing sound (it clonks as the skull it is cloned from)");

            /// <summary>
            /// The item's landing sound never plays: its last impact is put out of reach of Time.time.
            /// </summary>
            internal static void MuteImpacts(Grabbable item)
            {
                if (LastImpact == null) return;

                LastImpact.SetValue(item, float.MaxValue);
            }
        }

        /// <summary>
        /// An airlock's two doors. NPC.Core reflects the same fields for gate detection; these
        /// let the EVA run wait out the cycle in the chamber.
        /// </summary>
        internal static class AirlockAccess
        {
            private const string Feature = "the buddy's EVA run (waiting out the airlock cycle)";
            private static readonly FieldInfo? OuterDoor = Field<Gate>(typeof(Airlock), "outerDoor", Feature);
            private static readonly FieldInfo? InnerDoor = Field<Gate>(typeof(Airlock), "innerDoor", Feature);

            internal static Gate? GetOuterDoor(Airlock? airlock) => Get<Gate>(OuterDoor, airlock);
            internal static Gate? GetInnerDoor(Airlock? airlock) => Get<Gate>(InnerDoor, airlock);
        }

        /// <summary>
        /// The room a station's Docker opens onto: its docking corridor. docs/behaviour.md#where-a-wander-ends
        /// </summary>
        internal static class DockerAccess
        {
            private static readonly FieldInfo? EntryRoom = Field<Room>(typeof(Docker), "entryRoom",
                "wander goals kept out of the docking corridor");

            internal static Room? GetEntryRoom(Docker? docker) => Get<Room>(EntryRoom, docker);
        }
    }
}

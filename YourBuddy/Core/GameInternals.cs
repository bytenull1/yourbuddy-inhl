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
        /// The Furniture detector for what moves with it, which is what a container holds. docs/snacks.md §1
        /// </summary>
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
            private static readonly FieldInfo? Moving = Field<EventReference>(typeof(Breathless), "movingSound", Feature);
            private static readonly FieldInfo? Talk = Field<EventReference>(typeof(AssistanceBot), "talkSound", Feature);
            private static readonly FieldInfo? Clean = Field<EventReference>(typeof(Cleanable), "cleanSound", Feature);
            private static readonly FieldInfo? CloseFail = Field<EventReference>(typeof(Gate), "closeFailSound", Feature);
            private static readonly FieldInfo? Scream = Field<EventReference>(typeof(UnsealScream), "screamSound", Feature);
            private static readonly FieldInfo? Activity = Field<EventReference>(typeof(BreathlessActivity), "sound", Feature);
            private static readonly FieldInfo? RandomEvent = Field<EventReference>(typeof(RandomSound), "sound", Feature);
            private static readonly FieldInfo? Background = Field<EventReference>(typeof(BackgroundSound), "scarySound", Feature);

            internal static EventReference? GetScreech(Breathless? b) => GetValue<EventReference>(Screech, b);
            internal static EventReference? GetMoving(Breathless? b) => GetValue<EventReference>(Moving, b);
            internal static EventReference? GetTalk(AssistanceBot? bot) => GetValue<EventReference>(Talk, bot);
            internal static EventReference? GetClean(Cleanable? dirt) => GetValue<EventReference>(Clean, dirt);
            internal static EventReference? GetCloseFail(Gate? gate) => GetValue<EventReference>(CloseFail, gate);
            internal static EventReference? GetScream(UnsealScream? e) => GetValue<EventReference>(Scream, e);
            internal static EventReference? GetActivity(BreathlessActivity? e) => GetValue<EventReference>(Activity, e);
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
    }
}

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
    /// Every reflection accessor YourBuddy alone needs into the game's private members, resolved once,
    /// with a one-time warning naming what a missing member degrades. Misses return null/default. What
    /// NPC.Core reads is in its own. npc-core:docs/invariants.md#reflection-lives-in-gameinternals
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
        /// `critical` marks a member whose loss a player would notice at once (the buddy's
        /// wake-up, the dialog's keypress); its miss is logged as an error so it stands out.
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
        /// PlayerController: the ragdoll parts and model of the prefab a buddy is cloned from.
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
        /// GameManager: the player prefab the buddy is cloned from.
        /// </summary>
        internal static class GameManagerAccess
        {
            private static readonly FieldInfo? PlayerPrefab = Field<Player>(typeof(GameManager), "playerPrefab", "buddy spawning");

            internal static Player? GetPlayerPrefab(GameManager? manager) => Get<Player>(PlayerPrefab, manager);
        }

        /// <summary>
        /// CryoPodAnimator: how the player's pod opens, replayed on the prop capsule the
        /// buddy wakes in. docs/game-model.md#the-cryo-room
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
        /// BreathlessController: the point the monster casts its own sight line from, which
        /// is where the buddy looks to see it. docs/fear.md
        /// </summary>
        internal static class BreathlessControllerAccess
        {
            private static readonly FieldInfo? RaycastPoint = Field<Transform>(typeof(BreathlessController), "raycastPoint",
                "the buddy seeing the Breathless, which falls back to the monster's origin");

            internal static Transform? GetRaycastPoint(BreathlessController? controller) =>
                Get<Transform>(RaycastPoint, controller);
        }

        /// <summary>
        /// Furniture: the detector for what moves with it, which is what a container holds. docs/snacks.md §1
        /// </summary>
        internal static class FurnitureAccess
        {
            private static readonly FieldInfo? ItemMover = Field<InstantItemDetector>(typeof(Furniture), "itemMover",
                "the buddy's snacks (finding containers and what is in them)");

            internal static InstantItemDetector? GetItemMover(Furniture? furniture) => Get<InstantItemDetector>(ItemMover, furniture);
        }

        /// <summary>
        /// TrashCan: the trigger that takes an item in. docs/items.md §1
        /// </summary>
        internal static class TrashCanAccess
        {
            private static readonly FieldInfo? ItemDestroyer = Field<ItemDestroyer>(typeof(TrashCan), "itemDestroyer",
                "the buddy tidying trash into trash cans");

            internal static ItemDestroyer? GetItemDestroyer(TrashCan? bin) => Get<ItemDestroyer>(ItemDestroyer, bin);
        }

        /// <summary>
        /// SellStation: where items are loaded, who would be caught inside, its gate and button. docs/items.md §1
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
    }
}

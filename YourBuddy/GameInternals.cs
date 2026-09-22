using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using FMODUnity;
using Space;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YourBuddy
{
    /// <summary>
    /// Every reflection accessor into the game's private members, resolved once, with
    /// a one-time warning naming what a missing member degrades. Misses return
    /// null/default. docs/invariants.md#reflection-lives-in-gameinternals
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

        private static MethodInfo? Method(Type type, string name, Type[] parameters, string feature)
        {
            _resolvedCount++;
            MethodInfo? method = type.GetMethod(name, MemberFlags, null, parameters, null);
            if (method == null) WarnOnce(type, name, "not found with its expected parameters", feature);

            return method;
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
        /// EntryDetector: rooms on both sides of a doorway. Buddy room tracking
        /// replicates the game's inner/outer side test with these members.
        /// </summary>
        internal static class EntryDetectorAccess
        {
            private static readonly FieldInfo? InnerRoom = Field<Room>(typeof(EntryDetector), "innerRoom", "buddy room tracking");
            private static readonly FieldInfo? OuterRoom = Field<Room>(typeof(EntryDetector), "outerRoom", "buddy room tracking");
            private static readonly FieldInfo? RotationReference = Field<Transform>(typeof(EntryDetector), "rotationReference", "buddy room tracking");
            private static readonly FieldInfo? ReverseSide = Field<bool>(typeof(EntryDetector), "reverseSide", "buddy room tracking");
            private static readonly FieldInfo? Door = Field<Gate>(typeof(EntryDetector), "door", "room content loading around doors");
            // The detector's remembered player, which the game sets but never clears.
            // docs/invariants.md#buddy-closes-must-not-move-the-player
            private static readonly FieldInfo? CurrentPlayer = Field<Player>(typeof(EntryDetector), "player", "keeping the buddy's door closes from moving the player between rooms");

            internal static Room? GetInnerRoom(EntryDetector? detector) => Get<Room>(InnerRoom, detector);
            internal static Room? GetOuterRoom(EntryDetector? detector) => Get<Room>(OuterRoom, detector);
            internal static Transform? GetRotationReference(EntryDetector? detector) => Get<Transform>(RotationReference, detector);
            internal static bool GetReverseSide(EntryDetector? detector) => detector != null && ReverseSide != null && (bool)ReverseSide.GetValue(detector);
            internal static Gate? GetDoor(EntryDetector? detector) => Get<Gate>(Door, detector);
            internal static Player? GetCurrentPlayer(EntryDetector? detector) => Get<Player>(CurrentPlayer, detector);

            internal static bool SetCurrentPlayer(EntryDetector? detector, Player? player)
            {
                if (CurrentPlayer == null || detector == null) return false;

                CurrentPlayer.SetValue(detector, player);
                return true;
            }
        }

        /// <summary>
        /// Gate: the AntiCrasher sensors whose OnTriggerEnter calls Gate.FailClose, and
        /// the sliding mechanism the doorway carve-out is measured from - the anchors
        /// move along the gate's local x, which is what makes local x the passage width.
        /// </summary>
        internal static class GateAccess
        {
            private static readonly FieldInfo? AntiCrashers = Field<AntiCrasher[]>(typeof(Gate), "antiCrashers", "doorway occupancy test before closing a door");
            private static readonly FieldInfo? OpenedWidth = Field<float>(typeof(Gate), "openedWidth", "the doorway carve-out width, which falls back to a fixed 0.85m");
            private static readonly FieldInfo? GateAnchors = Field<Transform[]>(typeof(Gate), "gateAnchors", "the doorway carve-out shape, which falls back to a cylinder");

            internal static AntiCrasher[]? GetAntiCrashers(Gate? gate) => Get<AntiCrasher[]>(AntiCrashers, gate);
            internal static Transform[]? GetGateAnchors(Gate? gate) => Get<Transform[]>(GateAnchors, gate);

            /// <summary>
            /// Whether the anchors can be read at all. A gate with no anchors is not a
            /// door - `Gate.ApplyData` warns and refuses to work. A gate whose anchors
            /// cannot be read is a game update, and every gate must keep its doorway.
            /// </summary>
            internal static bool AnchorsReadable => GateAnchors != null;

            /// <summary>
            /// How far each leaf slides. 0 when unreadable, which leaves the carve-out at
            /// the leaf's current inner edge - the conservative direction.
            /// </summary>
            internal static float GetOpenedWidth(Gate? gate)
            {
                return OpenedWidth == null || gate == null ? 0f : (float)OpenedWidth.GetValue(gate);
            }
        }

        /// <summary>
        /// Airlock: outer/inner doors, hatch and connected room. Used for airlock gate
        /// detection (never auto-opened) and airlock-chamber safe-position checks.
        /// </summary>
        internal static class AirlockAccess
        {
            private static readonly FieldInfo? OuterDoor = Field<Gate>(typeof(Airlock), "outerDoor", "airlock gate detection / space protection");
            private static readonly FieldInfo? InnerDoor = Field<Gate>(typeof(Airlock), "innerDoor", "airlock gate detection / space protection");
            private static readonly FieldInfo? Hatch = Field<Gate>(typeof(Airlock), "hatch", "airlock gate detection / space protection");
            private static readonly FieldInfo? ConnectedRoom = Field<Room>(typeof(Airlock), "connectedRoom", "airlock chamber detection");

            internal static Gate? GetOuterDoor(Airlock? airlock) => Get<Gate>(OuterDoor, airlock);
            internal static Gate? GetInnerDoor(Airlock? airlock) => Get<Gate>(InnerDoor, airlock);
            internal static Gate? GetHatch(Airlock? airlock) => Get<Gate>(Hatch, airlock);
            internal static Room? GetConnectedRoom(Airlock? airlock) => Get<Room>(ConnectedRoom, airlock);
        }

        /// <summary>
        /// LifecareDisplay: the scanning terminal UI the buddy integrates with (map
        /// icon + lifeforms counter). Known open issue: the buddy icon can disappear
        /// from the terminal for no apparent reason - see BuddyManager.TryHookLifecare.
        /// </summary>
        internal static class LifecareDisplayAccess
        {
            // Icon mapping fallback used when the game's scale field is missing.
            private const float DefaultScale = 0.05f;

            private static readonly FieldInfo? LifeformsLabel = Field<TMP_Text>(typeof(LifecareDisplay), "lifeformsLabel", "lifecare terminal integration");
            private static readonly FieldInfo? BreathlessIcon = Field<Image>(typeof(LifecareDisplay), "breathlessIcon", "lifecare terminal integration");
            private static readonly FieldInfo? PlayerIcon = Field<Image>(typeof(LifecareDisplay), "playerIcon", "lifecare terminal integration");
            private static readonly FieldInfo? TempObjects = Field<GameObject[]>(typeof(LifecareDisplay), "tempObjects", "lifecare terminal integration");
            private static readonly FieldInfo? Scale = Field<float>(typeof(LifecareDisplay), "scale", "lifecare terminal integration");
            private static readonly FieldInfo? BootLoading = Field<LoadingAnimator>(typeof(LifecareDisplay), "bootLoading", "lifecare terminal integration");
            private static readonly FieldInfo? ScanLoading = Field<LoadingAnimator>(typeof(LifecareDisplay), "scanLoading", "lifecare terminal integration");

            internal static TMP_Text? GetLifeformsLabel(LifecareDisplay? display) => Get<TMP_Text>(LifeformsLabel, display);
            internal static Image? GetBreathlessIcon(LifecareDisplay? display) => Get<Image>(BreathlessIcon, display);
            internal static Image? GetPlayerIcon(LifecareDisplay? display) => Get<Image>(PlayerIcon, display);
            internal static GameObject[]? GetTempObjects(LifecareDisplay? display) => Get<GameObject[]>(TempObjects, display);
            internal static LoadingAnimator? GetBootLoading(LifecareDisplay? display) => Get<LoadingAnimator>(BootLoading, display);
            internal static LoadingAnimator? GetScanLoading(LifecareDisplay? display) => Get<LoadingAnimator>(ScanLoading, display);

            internal static float GetScale(LifecareDisplay? display)
            {
                float? scale = GetValue<float>(Scale, display);
                return scale ?? DefaultScale;
            }
        }

        /// <summary>
        /// ConsoleMenu: the private command dictionary and console print method.
        /// </summary>
        internal static class ConsoleMenuAccess
        {
            private static readonly MethodInfo? PrintMethod = Method(typeof(ConsoleMenu), "Print", [typeof(string)], "console command output");
            private static readonly FieldInfo? Commands = Field<Dictionary<string, Action<string[]>>>(typeof(ConsoleMenu), "commands", "console command registration");

            internal static void Print(ConsoleMenu? console, string text)
            {
                try
                {
                    if (PrintMethod != null && console != null) PrintMethod.Invoke(console, [text]);
                }
                catch (Exception ex)
                {
                    // Console might be closed mid-print; keep the output visible in
                    // the BepInEx log so command feedback is never lost entirely.
                    YourBuddyPlugin.Log.LogDebug("[internals] Console print failed: " + ex.Message);
                }
            }

            internal static Dictionary<string, Action<string[]>>? GetCommands(ConsoleMenu? console)
            {
                return Get<Dictionary<string, Action<string[]>>>(Commands, console);
            }
        }

        /// <summary>
        /// SpaceObject: the container the game deactivates when a station optimizes.
        /// Anything left aboard must live under it, as Grabbables do via
        /// SpaceObject.SetItemOwnership. docs/invariants.md#the-buddy-rides-its-own-floor
        /// </summary>
        internal static class SpaceObjectAccess
        {
            private static readonly FieldInfo? ContentParent =
                Field<GameObject>(typeof(SpaceObject), "contentParent", "telling which station a floor or room belongs to, and parking the buddy there");

            internal static Transform? GetContentParent(SpaceObject? spaceObject)
            {
                GameObject? content = Get<GameObject>(ContentParent, spaceObject);
                return content != null ? content.transform : null;
            }
        }

        /// <summary>
        /// SpaceStation: room list. Read from the backing array to avoid referencing
        /// Unity.InputSystem just to iterate the public ReadOnlyArray.
        /// </summary>
        internal static class SpaceStationAccess
        {
            private static readonly FieldInfo? Rooms = Field<Room[]>(typeof(SpaceStation), "rooms", "station owner detection for nav nodes");

            internal static Room[]? GetRooms(SpaceStation? station) => Get<Room[]>(Rooms, station);
        }

        /// <summary>
        /// PlayerController: ragdoll parts and the player's head/foot triggers.
        /// </summary>
        internal static class PlayerControllerAccess
        {
            private static readonly FieldInfo? RagdollObject = Field<GameObject>(typeof(PlayerController), "ragdollObject", "buddy ragdoll death");
            private static readonly FieldInfo? Ragdoll = Field<Rigidbody>(typeof(PlayerController), "ragdoll", "buddy ragdoll death");
            private static readonly FieldInfo? ModelAnimator = Field<Animator>(typeof(PlayerController), "animator", "buddy model setup");
            private static readonly FieldInfo? HeadTriggerField = Field<HeadTrigger>(typeof(PlayerController), "headTrigger", "player/buddy trigger collision setup");
            private static readonly FieldInfo? FootTriggerField = Field<HeadTrigger>(typeof(PlayerController), "footTrigger", "player/buddy trigger collision setup");
            private static readonly FieldInfo? FocusedInteractable = Field<Interactable>(typeof(PlayerController), "focusedInteractable", "keeping the buddy dialog out of the way of terminals and held items", critical: true);

            /// <summary>
            /// False after a game update broke the focus field; the dialog then falls
            /// back to its own aim test rather than reading null as "not busy".
            /// </summary>
            internal static bool CanReadFocus => FocusedInteractable != null;

            internal static Interactable? GetFocusedInteractable(PlayerController? controller) =>
                Get<Interactable>(FocusedInteractable, controller);

            internal static GameObject? GetRagdollObject(PlayerController? controller) => Get<GameObject>(RagdollObject, controller);
            internal static Rigidbody? GetRagdollRigidbody(PlayerController? controller) => Get<Rigidbody>(Ragdoll, controller);
            internal static Animator? GetModelAnimator(PlayerController? controller) => Get<Animator>(ModelAnimator, controller);
            internal static HeadTrigger? GetHeadTrigger(PlayerController? controller) => Get<HeadTrigger>(HeadTriggerField, controller);
            internal static HeadTrigger? GetFootTrigger(PlayerController? controller) => Get<HeadTrigger>(FootTriggerField, controller);
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
        /// PlayerDetector: whether a doorway only opens for a suited player.
        /// docs/invariants.md#the-buddy-opens-only-what-the-player-could
        /// </summary>
        internal static class PlayerDetectorAccess
        {
            private static readonly FieldInfo? HelmetRequired = Field<bool>(typeof(PlayerDetector), "helmetRequired",
                "keeping the buddy from opening a door you need a suit for (the tutorial's first door)");

            internal static bool? GetHelmetRequired(PlayerDetector? detector) => GetValue<bool>(HelmetRequired, detector);
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
        /// CameraAnimator: the player's footstep FMOD events the buddy reuses.
        /// </summary>
        internal static class CameraAnimatorAccess
        {
            private static readonly FieldInfo? FootstepEvents = Field<EventReference[]>(typeof(CameraAnimator), "footstepEvents", "buddy footstep sounds");

            internal static EventReference[]? GetFootstepEvents(CameraAnimator? animator) => Get<EventReference[]>(FootstepEvents, animator);
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
        /// HealthSystem: the player's death counter, traced beside the buddy's. docs/game-model.md#atmosphere-kills-by-the-players-rule
        /// </summary>
        internal static class HealthSystemAccess
        {
            private static readonly FieldInfo? DeathCounter = Field<byte>(typeof(HealthSystem), "deathCounter",
                "the player's death counter in the level-2 atmosphere trace");

            internal static byte? GetDeathCounter(HealthSystem? health) => GetValue<byte>(DeathCounter, health);
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

        /// <summary>
        /// DoorPinCode: the gate a pin-code panel unlocks, and the code that opens it.
        /// The buddy only uses a code the player gave it - see docs/doors.md.
        /// </summary>
        internal static class DoorPinCodeAccess
        {
            private static readonly FieldInfo? Door = Field<Gate>(typeof(DoorPinCode), "door", "password-locked gate detection");
            private static readonly FieldInfo? InitialPinCode = Field<int>(typeof(PinCode), "initialPinCode", "giving the buddy a door password");

            internal static Gate? GetWiredGate(DoorPinCode? pin) => Get<Gate>(Door, pin);

            /// <summary>
            /// The panel's code, or null when reflection failed - in which case no code
            /// the player types can ever match, and the door simply stays impassable.
            /// </summary>
            internal static int? GetPinCode(DoorPinCode? pin)
            {
                return GetValue<int>(InitialPinCode, pin);
            }
        }
    }
}

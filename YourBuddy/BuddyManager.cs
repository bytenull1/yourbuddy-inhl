using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using Newtonsoft.Json;
using Space;
using TMPro;
using UnityEngine.UI;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Persistent manager: status HUD, node-graph IO, save sidecar and delayed spawn
    /// after a load. Tick() is driven by a postfix on GameManager.FixedUpdate, so it
    /// runs exactly while a game scene is loaded.
    /// </summary>
    public sealed class BuddyManager : MonoBehaviour
    {
        // Every buddy in the scene, alive or dead, in spawn order.
        private static readonly List<BuddyBehaviour> Buddies = [];
        private static BuddyBehaviour? _focus;
        // The buddy whose code is running, for YourBuddyPlugin.Log. docs/logging.md#4-rules-for-adding-logs
        private static BuddyBehaviour? _acting;
        private static readonly Dictionary<string, ManualLogSource> LogSources = [];
        private static bool _tickLogged = false;

        // Pending spawn after loading a save
        private static BuddySaveFile? _pendingSpawn = null;

        // Lifecare scan integration (game internals access lives in GameInternals): one map icon per buddy.
        private sealed class LifeIcon
        {
            public Image? Icon;
            /// <summary>
            /// Ship-local position at the last scan's end, or null when not drawn.
            /// </summary>
            public Vector3? LocalPos;
            /// <summary>
            /// Cloned on the current display, so a missing clone was lost rather than never made.
            /// </summary>
            public bool Created;
        }
        private static readonly Dictionary<BuddyBehaviour, LifeIcon> LifeIcons = [];
        private static LifecareDisplay? _lifeDisplay = null;
        private static float _lifeHookAt = 0f;
        private static bool _scanWasActive = false;
        private static bool _terminalWasEnabled = false;

        // Gates a buddy closed, when, and who closed it. Gate.OnClosed fires at the end of the
        // animation, so this must outlive the Close() call itself.
        private readonly struct BuddyClose(Gate gate, float at, BuddyBehaviour by)
        {
            public readonly Gate Gate = gate;
            public readonly float At = at;
            public readonly BuddyBehaviour By = by;
        }
        private static readonly List<BuddyClose> BuddyClosedGates = [];
        private const float BuddyCloseWindow = 6f;

        // ------------------------------------------------------------------
        // The buddies: registry, focus, names. docs/architecture.md#6-buddy-lifecycle
        // ------------------------------------------------------------------

        /// <summary>
        /// Every buddy in the scene, alive or dead. Handlers for game events iterate Snapshot(),
        /// because a callback may despawn.
        /// </summary>
        internal static IReadOnlyList<BuddyBehaviour> All => Buddies;

        internal static BuddyBehaviour[] Snapshot() => [.. Buddies];

        internal static int LivingCount
        {
            get
            {
                int count = 0;
                foreach (BuddyBehaviour buddy in Buddies)
                {
                    if (buddy != null && !buddy.IsDead) count++;
                }
                return count;
            }
        }

        internal static void Register(BuddyBehaviour buddy)
        {
            if (!Buddies.Contains(buddy)) Buddies.Add(buddy);
        }

        /// <summary>
        /// Idempotent: Despawn calls it at once, OnDestroy again at the end of the frame.
        /// </summary>
        internal static void Unregister(BuddyBehaviour buddy)
        {
            Buddies.Remove(buddy);
            if (_focus == buddy) _focus = null;
            RemoveLifeIcon(buddy);
        }

        /// <summary>
        /// Out of the registry now, destroyed at the end of the frame: a respawn in the same frame
        /// gets the numbers and names back.
        /// </summary>
        public static void Despawn(BuddyBehaviour buddy)
        {
            Unregister(buddy);
            if (buddy != null) Destroy(buddy.gameObject);
        }

        public static void DespawnAll()
        {
            for (int i = Buddies.Count - 1; i >= 0; i--) Despawn(Buddies[i]);
        }

        /// <summary>
        /// Who a command without a target means: the buddy last talked to or named while it lives,
        /// else the nearest living one, else any - so a corpse can still be despawned.
        /// </summary>
        internal static BuddyBehaviour? Focus
        {
            get
            {
                if (_focus != null && !_focus.IsDead) return _focus;

                BuddyBehaviour? nearest = NearestLiving();
                if (nearest != null) return nearest;

                if (_focus != null) return _focus;

                return Buddies.Count > 0 ? Buddies[0] : null;
            }
        }

        internal static void SetFocus(BuddyBehaviour buddy) => _focus = buddy;

        private static BuddyBehaviour? NearestLiving()
        {
            GameManager gm = GameManager.Instance;
            Player? pilot = gm != null && gm.PlayerShip != null ? gm.PlayerShip.Pilot : null;
            bool hasPlayer = pilot != null && pilot.Controller != null;
            Vector3 from = Vector3.zero;
            if (hasPlayer) from = pilot!.Controller!.CachedTransform.position; // hasPlayer checked both

            BuddyBehaviour? best = null;
            float bestSqr = float.MaxValue;
            foreach (BuddyBehaviour buddy in Buddies)
            {
                if (buddy == null || buddy.IsDead) continue;
                if (!hasPlayer) return buddy;

                float sqr = (buddy.transform.position - from).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                best = buddy;
                bestSqr = sqr;
            }
            return best;
        }

        /// <summary>
        /// `wanted` when no buddy has it, else the lowest number free.
        /// </summary>
        internal static int FreeNumber(int wanted = 0)
        {
            if (wanted > 0 && ByNumber(wanted) == null) return wanted;

            int number = 1;
            while (ByNumber(number) != null) number++;

            return number;
        }

        internal static BuddyBehaviour? ByNumber(int number)
        {
            foreach (BuddyBehaviour buddy in Buddies)
            {
                if (buddy != null && buddy.Number == number) return buddy;
            }
            return null;
        }

        /// <summary>
        /// "Buddy" / "Buddy N": a name is also a console target and a log source.
        /// </summary>
        internal static string NameFor(int number) => number == 1 ? "Buddy" : "Buddy " + number;

        /// <summary>
        /// A console target: case and spaces ignored, so "@buddy2" is "Buddy 2".
        /// </summary>
        internal static BuddyBehaviour? ByName(string name)
        {
            string key = TargetKey(name);
            foreach (BuddyBehaviour buddy in Buddies)
            {
                if (buddy != null && TargetKey(buddy.Name) == key) return buddy;
            }
            return null;
        }

        private static string TargetKey(string name) => name.Replace(" ", "").ToLowerInvariant();

        // ------------------------------------------------------------------
        // Log attribution: docs/logging.md#4-rules-for-adding-logs
        // ------------------------------------------------------------------

        /// <summary>
        /// One BepInEx source per buddy name, kept for the session: names repeat across loads.
        /// </summary>
        internal static ManualLogSource LogSourceFor(string name)
        {
            if (!LogSources.TryGetValue(name, out ManualLogSource source))
            {
                source = BepInEx.Logging.Logger.CreateLogSource("YourBuddy:" + name);
                LogSources[name] = source;
            }
            return source;
        }

        /// <summary>
        /// The source YourBuddyPlugin.Log writes to while a buddy's code runs, or null.
        /// </summary>
        internal static ManualLogSource? ActingLog => _acting != null ? _acting.LogSource : null;

        /// <summary>
        /// Marks `buddy` as the one running until the scope is disposed. Never held across a yield.
        /// </summary>
        internal static ActingScope Acting(BuddyBehaviour buddy)
        {
            ActingScope scope = new(_acting);
            _acting = buddy;
            return scope;
        }

        internal readonly struct ActingScope(BuddyBehaviour? previous) : IDisposable
        {
            public void Dispose() => _acting = previous;
        }

        // ------------------------------------------------------------------
        // What one buddy asks about the others. docs/invariants.md#one-buddy-per-target
        // ------------------------------------------------------------------

        /// <summary>
        /// Any buddy's own body, a corpse included: docs/invariants.md#buddies-never-block-each-other
        /// </summary>
        internal static bool IsBuddyBody(Transform t)
        {
            foreach (BuddyBehaviour buddy in Buddies)
            {
                if (buddy != null && buddy.IsOwnBody(t)) return true;
            }
            return false;
        }

        /// <summary>
        /// Another buddy's errand leg or hide holds this.
        /// </summary>
        internal static bool TakenByAnother(Transform what, BuddyBehaviour me)
        {
            foreach (BuddyBehaviour buddy in Buddies)
            {
                if (buddy != null && buddy != me && buddy.Holds(what)) return true;
            }
            return false;
        }

        /// <summary>
        /// Another living, loaded buddy stands where `test` says.
        /// </summary>
        internal static bool AnotherBuddyWhere(Func<Vector3, bool> test, BuddyBehaviour me)
        {
            foreach (BuddyBehaviour buddy in Buddies)
            {
                if (buddy == null || buddy == me || buddy.IsDead || !buddy.isActiveAndEnabled) continue;
                if (test(buddy.transform.position)) return true;
            }
            return false;
        }

        /// <summary>
        /// Another buddy tracked in this room, which keeps it loaded.
        /// docs/invariants.md#the-buddy-loads-rooms-by-the-door-rule
        /// </summary>
        internal static BuddyBehaviour? OtherTrackedIn(Room room, BuddyBehaviour me)
        {
            foreach (BuddyBehaviour buddy in Buddies)
            {
                if (buddy != null && buddy != me && buddy.TracksRoom(room)) return buddy;
            }
            return null;
        }

        /// <summary>
        /// Body colliders of two buddies ignore each other, a dead one's ragdoll included. Both must be
        /// enabled and active for IgnoreCollision, so OnEnable repeats it.
        /// docs/invariants.md#buddies-never-block-each-other
        /// </summary>
        internal static void IgnoreBodies(BuddyBehaviour a, BuddyBehaviour b)
        {
            if (a == null || b == null || a == b) return;

            CharacterController? aCc = a.Controller;
            CharacterController? bCc = b.Controller;
            Ignore(aCc, bCc);
            foreach (Collider part in b.SolidParts()) Ignore(aCc, part);
            foreach (Collider part in a.SolidParts()) Ignore(bCc, part);
        }

        internal static void IgnoreAllBodies(BuddyBehaviour buddy)
        {
            foreach (BuddyBehaviour other in Buddies) IgnoreBodies(buddy, other);
        }

        private static void Ignore(Collider? a, Collider? b)
        {
            if (a == null || b == null || !a.enabled || !b.enabled) return;
            if (!a.gameObject.activeInHierarchy || !b.gameObject.activeInHierarchy) return;

            Physics.IgnoreCollision(a, b);
        }

        /// <summary>
        /// Which vessel the floor under a world position belongs to. Tri-state on
        /// purpose: docs/invariants.md#aboard-is-answered-by-the-floor
        /// </summary>
        internal enum FloorOwnership
        {
            Unknown = 0,
            PlayerShip = 1,
            Elsewhere = 2
        }

        /// <summary>
        /// Which vessel owns whatever is underfoot (BuddyNodeGraph.TryVesselOf).
        /// Far more reliable than the game's tracked room. docs/game-model.md
        /// </summary>
        internal static FloorOwnership FloorOwner(Vector3 worldPos)
        {
            return FloorOwner(worldPos, out _, out _);
        }

        /// <summary>
        /// The same answer, plus which vessel it is and the transform anything standing
        /// on that floor has to ride. `owner` is null only when no floor was found at
        /// all; an `anchor` of null means the player ship, which never moves.
        /// The probe goes through NavProbe so this cannot answer differently from the
        /// height probe: docs/invariants.md#one-probe-basis
        /// </summary>
        internal static FloorOwnership FloorOwner(Vector3 worldPos, out string? owner, out Transform? anchor)
        {
            owner = null;
            anchor = null;
            GameManager gm = GameManager.Instance;
            SpaceShip? ship = gm != null ? gm.PlayerShip : null;
            if (ship == null) return FloorOwnership.Unknown;

            if (!NavProbe.TryFloorCollider(worldPos, out Collider? floor) || floor == null) return FloorOwnership.Unknown;

            if (BuddyNodeGraph.TryVesselOf(floor.transform, out SpaceShip? vessel, out SpaceObject? spaceObject))
            {
                if (vessel != null)
                {
                    bool mine = vessel == ship;
                    owner = mine ? BuddyNodeGraph.ShipOwner : vessel.gameObject.name;
                    anchor = mine ? null : vessel.transform;
                    return mine ? FloorOwnership.PlayerShip : FloorOwnership.Elsewhere;
                }
                // TryVesselOf returned true without a ship, so it found a SpaceObject.
                owner = spaceObject!.gameObject.name;
                // The container the game deactivates when the station optimizes, so a buddy
                // parked there is frozen and restored by the game's own lifecycle.
                // docs/invariants.md#the-buddy-rides-its-own-floor
                Transform? contentParent = GameInternals.SpaceObjectAccess.GetContentParent(spaceObject);
                anchor = contentParent != null ? contentParent : spaceObject.transform;
                return FloorOwnership.Elsewhere;
            }

            // Real floor, no vessel above it: world geometry. Not a vessel we can name,
            // so ownership stays Unknown - but it still rides the world container.
            owner = BuddyNodeGraph.WorldOwner;
            // ship is only set when gm is.
            anchor = gm!.WorldObjects;
            return FloorOwnership.Unknown;
        }

        /// <summary>
        /// Which vessel a scene object belongs to, by the same rule the floor probe uses.
        /// Null when it belongs to no vessel at all.
        /// </summary>
        internal static string? OwnerOfTransform(Transform start)
        {
            if (!BuddyNodeGraph.TryVesselOf(start, out SpaceShip? vessel, out SpaceObject? spaceObject)) return null;

            // TryVesselOf returned true without a ship, so it found a SpaceObject.
            if (vessel == null) return spaceObject!.gameObject.name;

            SpaceShip? ship = GameManager.Instance != null ? GameManager.Instance.PlayerShip : null;
            return ship != null && vessel == ship ? BuddyNodeGraph.ShipOwner : vessel.gameObject.name;
        }

        /// <summary>
        /// The transform an owner's occupants ride, or null for the player ship.
        /// </summary>
        internal static Transform? AnchorForOwner(string? owner)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || string.IsNullOrEmpty(owner) || owner == BuddyNodeGraph.ShipOwner) return null;

            if (owner == BuddyNodeGraph.WorldOwner) return gm.WorldObjects;

            foreach (SpaceObject spaceObject in FindObjectsOfType<SpaceObject>())
            {
                if (spaceObject == null || spaceObject.gameObject.name != owner) continue;

                Transform? contentParent = GameInternals.SpaceObjectAccess.GetContentParent(spaceObject);
                return contentParent != null ? contentParent : spaceObject.transform;
            }
            return null;
        }

        /// <summary>
        /// Records that a buddy - not the player, not the game - issued a close on
        /// this gate, so the EntryDetector patch can tell the two apart.
        /// </summary>
        internal static void NoteBuddyClosedGate(Gate gate, BuddyBehaviour by)
        {
            if (gate == null) return;

            for (int i = BuddyClosedGates.Count - 1; i >= 0; i--)
            {
                if (BuddyClosedGates[i].Gate == gate || BuddyClosedGates[i].Gate == null ||
                    Time.time - BuddyClosedGates[i].At > BuddyCloseWindow)
                {
                    BuddyClosedGates.RemoveAt(i);
                }
            }
            BuddyClosedGates.Add(new BuddyClose(gate, Time.time, by));
        }

        /// <summary>
        /// True when a buddy closed this gate within the last BuddyCloseWindow; `by` is that buddy,
        /// or null once it is gone.
        /// </summary>
        internal static bool GateWasClosedByBuddy(Gate gate, out BuddyBehaviour? by)
        {
            by = null;
            if (gate == null) return false;

            bool found = false;
            for (int i = BuddyClosedGates.Count - 1; i >= 0; i--)
            {
                if (BuddyClosedGates[i].Gate == null ||
                    Time.time - BuddyClosedGates[i].At > BuddyCloseWindow)
                {
                    BuddyClosedGates.RemoveAt(i);
                    continue;
                }
                if (BuddyClosedGates[i].Gate != gate) continue;

                found = true;
                BuddyBehaviour closer = BuddyClosedGates[i].By;
                by = closer != null ? closer : null;
            }
            return found;
        }

        private static Transform? ShipTransform
        {
            get
            {
                GameManager gm = GameManager.Instance;
                return gm != null && gm.PlayerShip != null ? gm.PlayerShip.transform : null;
            }
        }

        /// <summary>
        /// Called every game FixedUpdate from Patches.Tick.GameManager_FixedUpdate_Postfix.
        /// </summary>
        public static void Tick()
        {
            if (!_tickLogged)
            {
                _tickLogged = true;
                YourBuddyPlugin.Log.LogInfo("[mgr] Game tick hook active");
            }

            GameManager gm = GameManager.Instance;
            bool sceneReady = gm != null && gm.SceneFullyLoaded && gm.PlayerShip != null && gm.PlayerShip.Pilot != null;

            // A buddy destroyed without Despawn (scene unload) leaves its slot in OnDestroy; this is the backstop.
            for (int i = Buddies.Count - 1; i >= 0; i--)
            {
                if (Buddies[i] == null) Buddies.RemoveAt(i);
            }

            // Spawn the buddies saved with this save file once the game scene is fully loaded.
            if (_pendingSpawn != null && sceneReady)
            {
                BuddySaveFile data = _pendingSpawn;
                _pendingSpawn = null;
                RestoreBuddies(data);
            }

            // After the save's own buddy, so a restored one is never doubled by the new-game wake-up.
            // sceneReady implies gm != null.
            if (sceneReady) BuddyCryoSpawn.Tick(gm!);

            // Periodic node-graph save
            BuddyNodeGraph.TickSave();

            // The game re-enables the monster on its own schedule, so a debug override
            // has to be re-asserted rather than set once.
            if (AiDebug.Any) AiDebug.Apply();

            if (AnyActive() && Time.time >= _lifeHookAt)
            {
                _lifeHookAt = Time.time + 0.1f; // Call more frequently to catch scan state changes
                TryHookLifecare();
                UpdateBuddyLifeIcons();
            }
        }

        private static bool AnyActive()
        {
            foreach (BuddyBehaviour buddy in Buddies)
            {
                if (buddy != null && buddy.gameObject.activeInHierarchy) return true;
            }
            return false;
        }

        /// <summary>
        /// Every living buddy the save holds, then the door codes - learned even when none of them lives.
        /// </summary>
        private static void RestoreBuddies(BuddySaveFile data)
        {
            foreach (BuddyState state in BuddySaveFile.StatesOf(data))
            {
                string who = state.Name ?? "Buddy";
                if (!state.Alive)
                {
                    YourBuddyPlugin.Log.LogInfo($"[mgr] {who} was dead in this save file - not spawning. Use 'spawn_buddy'.");
                    continue;
                }
                Vector3 position = RestorePosition(state);
                Quaternion rotation = RestoreRotation(state);
                YourBuddyPlugin.Log.LogInfo($"[mgr] Restoring {who} from save at {position}");
                BuddyBehaviour? buddy = YourBuddyPlugin.SpawnBuddy(position, rotation, state.Number);
                if (buddy == null) continue;

                using ActingScope _ = Acting(buddy);
                AttachToOwner(buddy, state.Owner);
                BuddyCryoSpawn.ResumeSleep(buddy, state.SleepingCapsule);
            }
            if (data.KnownPinCodes != null) foreach (int code in data.KnownPinCodes) BuddyBehaviour.LearnPinCode(code);
        }

        private static Vector3 RestorePosition(BuddyState data)
        {
            // The frame it was standing in wins: a station's world position changes
            // while the ship flies. docs/invariants.md#the-buddy-rides-its-own-floor
            if (data.OwnerLocalPosition is { Length: 3 } && !string.IsNullOrEmpty(data.Owner))
            {
                Transform? anchor = AnchorForOwner(data.Owner);
                if (anchor != null)
                {
                    return anchor.TransformPoint(new Vector3(
                        data.OwnerLocalPosition[0], data.OwnerLocalPosition[1], data.OwnerLocalPosition[2]));
                }
                YourBuddyPlugin.Log.LogWarning(
                    $"[mgr] Save puts the buddy on '{data.Owner}', which is not in this scene - using the ship instead");
            }

            Transform? ship = ShipTransform;
            if (data.ShipLocalPosition is { Length: 3 } && ship != null)
            {
                return ship.TransformPoint(new Vector3(data.ShipLocalPosition[0], data.ShipLocalPosition[1], data.ShipLocalPosition[2]));
            }
            if (data.WorldPosition is { Length: 3 })
            {
                return new Vector3(data.WorldPosition[0], data.WorldPosition[1], data.WorldPosition[2]);
            }
            return Vector3.zero;
        }

        /// <summary>
        /// Puts a new buddy in the frame of the vessel it stands on, before its first
        /// SlowUpdate has to probe a floor that may not be loaded. Parenting into a
        /// station whose content is deactivated parks it, which is the point.
        /// </summary>
        internal static void AttachToOwner(BuddyBehaviour buddy, string? owner)
        {
            if (string.IsNullOrEmpty(owner) || owner == BuddyNodeGraph.ShipOwner) return;

            Transform? anchor = AnchorForOwner(owner);
            if (anchor == null) return;

            buddy.RideOwner(owner, anchor);
            YourBuddyPlugin.Log.LogInfo($"[mgr] Buddy placed onto '{owner}'");
        }

        private static Quaternion RestoreRotation(BuddyState data)
        {
            if (data.Rotation is { Length: 4 })
            {
                return new Quaternion(data.Rotation[0], data.Rotation[1], data.Rotation[2], data.Rotation[3]);
            }
            return Quaternion.identity;
        }

        // ------------------------------------------------------------------
        // Lifecare scan integration: the terminal sees the buddy as a lifeform.
        // ------------------------------------------------------------------

        // "Aboard" is answered by the floor, never by currentRoomRef, which is null
        // until the buddy first crosses a doorway.
        // docs/invariants.md#aboard-is-answered-by-the-floor
        private static void TryHookLifecare()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.PlayerShip == null || gm.PlayerShip.LifecareController == null) return;

            LifecareDisplay display = gm.PlayerShip.LifecareController.Display;
            if (display == null) return;

            Image? breathless = GameInternals.LifecareDisplayAccess.GetBreathlessIcon(display);
            if (breathless == null) return;

            // A new terminal: the old clones and the last scan's snapshot belong to the old one.
            bool newDisplay = _lifeDisplay != display;
            if (newDisplay)
            {
                foreach (LifeIcon old in LifeIcons.Values)
                {
                    if (old.Icon != null) Destroy(old.Icon.gameObject);
                    old.Icon = null;
                    old.LocalPos = null;
                    old.Created = false;
                }
                _lifeDisplay = display;
            }

            // Re-hook when our clone went missing: the game may rebuild the terminal UI while the
            // display object itself survives. docs/lifecare.md
            foreach (BuddyBehaviour buddy in Buddies)
            {
                if (buddy == null) continue;

                if (!LifeIcons.TryGetValue(buddy, out LifeIcon? entry))
                {
                    entry = new LifeIcon();
                    LifeIcons[buddy] = entry;
                }
                if (entry.Icon != null && entry.Icon.transform.parent != null) continue;

                if (entry.Created)
                {
                    using ActingScope _ = Acting(buddy);
                    YourBuddyPlugin.Log.LogInfo("[mgr] Lifecare buddy icon was lost - re-creating it");
                }

                // Clone the breathless icon as the buddy's own map icon. The last scan's snapshot is
                // kept: it is still valid, and dropping it would blank the icon until the next scan.
                GameObject clone = Instantiate(breathless.gameObject, breathless.transform.parent);
                clone.name = "BuddyLifeIcon";
                entry.Icon = clone.GetComponent<Image>();
                if (entry.Icon == null)
                {
                    Destroy(clone);
                    continue;
                }
                entry.Icon.enabled = false;
                entry.Created = true;
            }
        }

        /// <summary>
        /// A buddy that is gone takes its icon and its count with it.
        /// </summary>
        private static void RemoveLifeIcon(BuddyBehaviour buddy)
        {
            if (!LifeIcons.TryGetValue(buddy, out LifeIcon? entry)) return;

            LifeIcons.Remove(buddy);
            if (entry.Icon != null) Destroy(entry.Icon.gameObject);
            RecountLifeforms(_lifeDisplay);
        }

        private static void UpdateBuddyLifeIcons()
        {
            if (_lifeDisplay == null) return;

            // Check if terminal is enabled (not in boot/loading state)
            LoadingAnimator? bootLoading = GameInternals.LifecareDisplayAccess.GetBootLoading(_lifeDisplay);
            bool terminalEnabled = bootLoading == null || !bootLoading.gameObject.activeSelf;

            // If terminal just turned off, hide the icons and reset
            if (!terminalEnabled && _terminalWasEnabled)
            {
                _terminalWasEnabled = false;
                _scanWasActive = false;
                foreach (LifeIcon entry in LifeIcons.Values)
                {
                    if (entry.Icon != null && entry.Icon.enabled) entry.Icon.enabled = false;
                }
                RecountLifeforms(_lifeDisplay);
                return;
            }

            // Turned on: the cached positions from a previous scan are drawn below.
            if (terminalEnabled) _terminalWasEnabled = true;

            // Only update when a scan is actively running.
            // The game uses scanLoading.gameObject.activeSelf to indicate an active scan.
            LoadingAnimator? scanLoading = GameInternals.LifecareDisplayAccess.GetScanLoading(_lifeDisplay);
            bool scanIsActive = scanLoading != null && scanLoading.gameObject.activeSelf;

            // Track scan state transitions - detect when scan ends
            if (scanIsActive && !_scanWasActive)
            {
                // Scan just started - mark as active but don't update position yet
                _scanWasActive = true;
            }
            else if (!scanIsActive && _scanWasActive)
            {
                // Scan just ended - capture every buddy's position now
                _scanWasActive = false;
                foreach (KeyValuePair<BuddyBehaviour, LifeIcon> pair in LifeIcons)
                {
                    using ActingScope _ = Acting(pair.Key);
                    CaptureScan(pair.Key, pair.Value);
                }
                UpdateIconVisibility();
                LogIconState();
            }

            // Re-assert every poll, not only on scan edges: whatever the game does to
            // this UI in between, the icons and count are restored within 0.1 s instead
            // of staying wrong until the next scan.
            if (terminalEnabled) UpdateIconVisibility();
        }

        /// <summary>
        /// A snapshot taken when the scan finishes, like the game's own player and Breathless icons:
        /// a buddy not aboard at that instant is not drawn until the next scan. docs/lifecare.md
        /// </summary>
        private static void CaptureScan(BuddyBehaviour buddy, LifeIcon entry)
        {
            entry.LocalPos = null;
            Transform? ship = ShipTransform;
            if (buddy != null && !buddy.IsDead && ship != null)
            {
                bool aboard = buddy.IsAboardPlayerShip();
                if (aboard) entry.LocalPos = ship.InverseTransformPoint(buddy.transform.position);

                if (YourBuddyPlugin.ConfigDebugLevel.Value >= 1)
                {
                    YourBuddyPlugin.Log.LogInfo(
                        "[mgr] Lifecare scan finished: buddy aboard=" + aboard +
                        ", pos=" + buddy.transform.position.ToString("0.0") +
                        ", trackedRoom=" + buddy.TrackedRoomName +
                        " -> icon " + (entry.LocalPos.HasValue ? "shown" : "hidden"));
                }
            }
            else if (YourBuddyPlugin.ConfigDebugLevel.Value >= 1)
            {
                YourBuddyPlugin.Log.LogInfo(
                    "[mgr] Lifecare scan finished with no live buddy to draw (buddy=" +
                    (buddy == null ? "null" : buddy.IsDead ? "dead" : "ok") + ", ship=" + (ship == null ? "null" : "ok") + ")");
            }
        }

        /// <summary>
        /// The rendered state, not our bookkeeping: is each clone still in a live UI hierarchy, and did
        /// our count survive to the label? Our own flags have reported "shown" while the player saw nothing.
        /// </summary>
        private static void LogIconState()
        {
            if (YourBuddyPlugin.ConfigDebugLevel.Value < 1 || _lifeDisplay == null) return;

            TMP_Text? label = GameInternals.LifecareDisplayAccess.GetLifeformsLabel(_lifeDisplay);
            Image? playerIcon = GameInternals.LifecareDisplayAccess.GetPlayerIcon(_lifeDisplay);
            foreach (KeyValuePair<BuddyBehaviour, LifeIcon> pair in LifeIcons)
            {
                Image? icon = pair.Value.Icon;
                if (icon == null) continue;

                using ActingScope _ = Acting(pair.Key);
                YourBuddyPlugin.Log.LogInfo(
                    "[mgr] Lifecare icon state: enabled=" + icon.enabled +
                    ", activeInHierarchy=" + icon.gameObject.activeInHierarchy +
                    ", parent=" + (icon.transform.parent != null ? icon.transform.parent.name : "none") +
                    ", localPos=" + icon.transform.localPosition.ToString("0.0") +
                    ", playerIcon=" + (playerIcon != null && playerIcon.enabled) +
                    ", label=" + (label != null ? label.text : "<no label>"));
            }
        }

        private static void UpdateIconVisibility()
        {
            if (_lifeDisplay == null) return;

            float scale = GameInternals.LifecareDisplayAccess.GetScale(_lifeDisplay);
            foreach (LifeIcon entry in LifeIcons.Values)
            {
                if (entry.Icon == null) continue;

                if (entry.LocalPos == null)
                {
                    if (entry.Icon.enabled) entry.Icon.enabled = false;
                    continue;
                }

                // Display the captured position
                Vector3 wanted = -new Vector3(entry.LocalPos.Value.x * scale, entry.LocalPos.Value.z * scale, 0f);
                if (!entry.Icon.gameObject.activeSelf) entry.Icon.gameObject.SetActive(true);

                entry.Icon.enabled = true;
                entry.Icon.transform.localPosition = wanted;
            }
            RecountLifeforms(_lifeDisplay);
        }

        /// <summary>
        /// Recomputes the lifeforms label, adding the buddy to the game's own count.
        /// </summary>
        internal static void RecountLifeforms(LifecareDisplay? display)
        {
            if (display == null) return;

            TMP_Text? label = GameInternals.LifecareDisplayAccess.GetLifeformsLabel(display);
            if (label == null) return;

            int count = 0;

            // The game clamps breathless + temp blips to one between them:
            // docs/invariants.md#mirror-the-vanilla-lifeform-clamp
            bool otherLifeform = false;
            Image? breathless = GameInternals.LifecareDisplayAccess.GetBreathlessIcon(display);
            if (breathless != null && breathless.enabled) otherLifeform = true;

            GameObject[]? temps = GameInternals.LifecareDisplayAccess.GetTempObjects(display);
            if (!otherLifeform && temps != null)
            {
                foreach (GameObject temp in temps)
                {
                    if (temp != null && temp.activeSelf)
                    {
                        otherLifeform = true;
                        break;
                    }
                }
            }
            if (otherLifeform) count++;

            Image? playerIcon = GameInternals.LifecareDisplayAccess.GetPlayerIcon(display);
            if (playerIcon != null && playerIcon.enabled) count++;
            // Only the icons belonging to this display: the postfix fires for whichever
            // LifecareDisplay the game touched.
            if (display == _lifeDisplay)
            {
                foreach (LifeIcon entry in LifeIcons.Values)
                {
                    if (entry.Icon != null && entry.Icon.enabled) count++;
                }
            }

            // Assigning TMP text forces a mesh rebuild, and this now runs on every poll.
            string text = count.ToString();
            if (label.text != text) label.text = text;
        }

        // ------------------------------------------------------------------
        // Save sidecar
        // ------------------------------------------------------------------

        public static void WriteSidecar(string saveFileName)
        {
            if (string.IsNullOrEmpty(saveFileName)) return;

            if (!YourBuddyPlugin.ConfigSaveSupport.Value)
            {
                // Leaving an old sidecar next to a save it no longer describes is how a
                // buddy comes back from a save that no longer has one.
                DeleteSidecar(saveFileName);
                return;
            }

            try
            {
                List<BuddyState> states = [];
                foreach (BuddyBehaviour buddy in Buddies)
                {
                    if (buddy != null) states.Add(CaptureState(buddy));
                }
                BuddySaveFile data = BuddySaveFile.Of(states, [.. BuddyBehaviour.KnownPinCodes], BuddyCryoSpawn.OpenedCapsule);

                string path = Path.Combine(SaveParser.SaveFilesPath, saveFileName + ".buddy");
                WriteAtomically(path, JsonConvert.SerializeObject(data, Formatting.Indented));
                YourBuddyPlugin.Log.LogInfo($"[mgr] Buddy sidecar written: {path}");
            }
            catch (Exception ex)
            {
                YourBuddyPlugin.Log.LogError($"[mgr] Failed to write buddy sidecar: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes beside the target and swaps it in, so a crash mid-write leaves the old
        /// sidecar rather than a truncated one.
        /// </summary>
        private static void WriteAtomically(string path, string contents)
        {
            string temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, contents);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        /// <summary>
        /// Whether a living buddy is inside this hiding spot, for the game's own Interact.
        /// </summary>
        public static bool BuddyIsHidingIn(HidingSpot spot)
        {
            if (spot == null) return false;

            foreach (BuddyBehaviour buddy in Buddies)
            {
                if (buddy != null && !buddy.IsDead && buddy.IsHiddenIn(spot)) return true;
            }
            return false;
        }

        /// <summary>
        /// One buddy as the sidecar stores it. The ship-local position is what a load prefers,
        /// so it is written whenever there is a ship to be local to.
        /// </summary>
        private static BuddyState CaptureState(BuddyBehaviour buddy)
        {
            Transform? ship = ShipTransform;
            // A hidden buddy is saved outside its spot: the load knows nothing of hiding.
            // docs/invariants.md#a-hidden-buddy-is-saved-outside-its-hiding-spot
            Vector3 pos = buddy.HiddenSavePoint ?? buddy.transform.position;
            float[]? shipLocal = null;
            if (ship != null)
            {
                Vector3 local = ship.InverseTransformPoint(pos);
                shipLocal = [local.x, local.y, local.z];
            }
            Quaternion rot = buddy.transform.rotation;

            // The frame the buddy is actually riding, which is the only one that
            // survives the ship flying away.
            float[]? ownerLocal = null;
            Transform anchor = buddy.transform.parent;
            if (anchor != null)
            {
                Vector3 ownerPoint = anchor.InverseTransformPoint(pos);
                ownerLocal = [ownerPoint.x, ownerPoint.y, ownerPoint.z];
            }

            return new BuddyState
            {
                Number = buddy.Number,
                Name = buddy.Name,
                Alive = !buddy.IsDead,
                Owner = buddy.CurrentOwner,
                OwnerLocalPosition = ownerLocal,
                ShipLocalPosition = shipLocal,
                WorldPosition = [pos.x, pos.y, pos.z],
                Rotation = [rot.x, rot.y, rot.z, rot.w],
                SleepingCapsule = BuddyCryoSpawn.SleepingCapsuleOf(buddy)
            };
        }

        /// <summary>
        /// A sidecar belongs to its save file and goes with it. Unconditional on
        /// purpose: a stale .buddy outlives the save that made it, and a new game with
        /// the recycled name then resurrects the old buddy.
        /// </summary>
        public static void DeleteSidecar(string saveFileName)
        {
            if (string.IsNullOrEmpty(saveFileName)) return;

            TryDelete(Path.Combine(SaveParser.SaveFilesPath, saveFileName + ".buddy"));
        }

        public static void DeleteAllSidecars()
        {
            try
            {
                if (!Directory.Exists(SaveParser.SaveFilesPath)) return;
                foreach (string path in Directory.GetFiles(SaveParser.SaveFilesPath, "*.buddy"))
                {
                    TryDelete(path);
                }
            }
            catch (Exception ex)
            {
                YourBuddyPlugin.Log.LogWarning($"[mgr] Failed to clear buddy sidecars: {ex.Message}");
            }
        }

        /// <summary>
        /// Sidecars whose save is gone - deleted outside the game, or by a path we do
        /// not patch. Cheap enough to run wherever the game re-reads its save list.
        /// </summary>
        public static void PruneOrphanSidecars()
        {
            try
            {
                if (!Directory.Exists(SaveParser.SaveFilesPath)) return;

                foreach (string path in Directory.GetFiles(SaveParser.SaveFilesPath, "*.buddy"))
                {
                    // The game's own existence test, so a changed save format never orphans every sidecar.
                    if (SaveParser.SaveFileExists(Path.GetFileNameWithoutExtension(path))) continue;

                    TryDelete(path);
                }
            }
            catch (Exception ex)
            {
                YourBuddyPlugin.Log.LogWarning($"[mgr] Failed to prune buddy sidecars: {ex.Message}");
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!File.Exists(path)) return;

                File.Delete(path);
                YourBuddyPlugin.Log.LogInfo($"[mgr] Buddy sidecar deleted: {path}");
            }
            catch (Exception ex)
            {
                YourBuddyPlugin.Log.LogWarning($"[mgr] Failed to delete buddy sidecar '{path}': {ex.Message}");
            }
        }

        private static BuddySaveFile? ReadSidecar(string saveFileName)
        {
            try
            {
                string path = Path.Combine(SaveParser.SaveFilesPath, saveFileName + ".buddy");
                if (!File.Exists(path))
                {
                    YourBuddyPlugin.Log.LogInfo($"[mgr] No buddy sidecar at {path}");
                    return null;
                }
                return JsonConvert.DeserializeObject<BuddySaveFile>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                YourBuddyPlugin.Log.LogWarning($"[mgr] Failed to read buddy sidecar: {ex.Message}");
                return null;
            }
        }

        public static void ArmPendingSpawn(string saveFileName)
        {
            _pendingSpawn = null;
            BuddyCryoSpawn.ArmReopen(null);
            if (!YourBuddyPlugin.ConfigSaveSupport.Value) return;

            if (string.IsNullOrEmpty(saveFileName)) return;

            BuddySaveFile? data = ReadSidecar(saveFileName);
            BuddyCryoSpawn.ArmReopen(data?.OpenedCapsule);
            if (data is { Exists: true })
            {
                _pendingSpawn = data;
                YourBuddyPlugin.Log.LogInfo($"[mgr] Buddy sidecar found for save '{saveFileName}' (alive={data.Alive}) - will spawn after the scene loads");
            }
        }
    }

}

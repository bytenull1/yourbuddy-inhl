using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NPC.Core;
using NPC.Core.Navigation;
using NPC.Core.Saves;
using NPC.Core.World;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The buddies: registry, focus and names, the '.buddy' sidecar's contents and the delayed spawn
    /// after a load. Tick() runs on NpcEvents.Tick, exactly while a game scene is loaded.
    /// </summary>
    public sealed class BuddyManager : MonoBehaviour
    {
        // Every buddy in the scene, alive or dead, in spawn order.
        private static readonly List<BuddyBehaviour> Buddies = [];
        private static BuddyBehaviour? _focus;
        private static bool _tickLogged = false;

        // Pending spawn after loading a save
        private static BuddySaveFile? _pendingSpawn = null;

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

        /// <summary>
        /// Into this list and NPC.Core's registry, which is what NPCs of other mods see.
        /// </summary>
        internal static void Register(BuddyBehaviour buddy)
        {
            if (!Buddies.Contains(buddy)) Buddies.Add(buddy);
            NpcRegistry.Register(buddy.Agent);
        }

        /// <summary>
        /// Idempotent: Despawn calls it at once, OnDestroy again at the end of the frame.
        /// </summary>
        internal static void Unregister(BuddyBehaviour buddy)
        {
            Buddies.Remove(buddy);
            NpcRegistry.Unregister(buddy.Agent);
            if (_focus == buddy) _focus = null;
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
        // What one buddy asks about the others. docs/invariants.md#one-buddy-per-target
        // ------------------------------------------------------------------

        /// <summary>
        /// Another NPC's errand leg or hide - of any mod - holds this. docs/invariants.md#one-buddy-per-target
        /// </summary>
        internal static bool TakenByAnother(Transform what, BuddyBehaviour me) =>
            NpcRegistry.TakenByAnother(what, me.Agent);

        /// <summary>
        /// Another living, loaded NPC stands where `test` says.
        /// </summary>
        internal static bool AnotherBuddyWhere(Func<Vector3, bool> test, BuddyBehaviour me) =>
            NpcRegistry.AnotherNpcWhere(test, me.Agent);

        private static Transform? ShipTransform
        {
            get
            {
                GameManager gm = GameManager.Instance;
                return gm != null && gm.PlayerShip != null ? gm.PlayerShip.transform : null;
            }
        }

        /// <summary>
        /// Every game FixedUpdate, from NpcEvents.Tick.
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
        }

        /// <summary>
        /// Every living buddy the save holds, then the door codes a sidecar from before NPC.Core kept them
        /// holds. NPC.Core restores its own from '.npccore'.
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

                using NpcRegistry.ActingScope _ = NpcRegistry.Acting(buddy.Agent);
                AttachToOwner(buddy, state.Owner);
                BuddyCryoSpawn.ResumeSleep(buddy, state.SleepingCapsule);
            }
            if (data.KnownPinCodes != null) foreach (int code in data.KnownPinCodes) NpcDoors.LearnCode(code);
        }

        private static Vector3 RestorePosition(BuddyState data)
        {
            // The frame it was standing in wins: a station's world position changes
            // while the ship flies. npc-core:docs/invariants.md#an-npc-rides-its-own-floor
            if (data.OwnerLocalPosition is { Length: 3 } && !string.IsNullOrEmpty(data.Owner))
            {
                Transform? anchor = NpcVessels.AnchorForOwner(data.Owner);
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
            if (string.IsNullOrEmpty(owner) || owner == NavGraph.ShipOwner) return;

            Transform? anchor = NpcVessels.AnchorForOwner(owner);
            if (anchor == null) return;

            buddy.Agent.RideOwner(owner, anchor);
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
        // Save sidecar
        // ------------------------------------------------------------------

        /// <summary>
        /// Registered with NPC.Core, which writes "&lt;save&gt;.buddy" with every save and deletes and prunes it
        /// with its save. docs/architecture.md#5-persistence
        /// </summary>
        internal const string SidecarExtension = "buddy";

        /// <summary>
        /// The sidecar for a save being written, or null for none: NPC.Core then deletes an old one, since a
        /// sidecar left next to a save it no longer describes is how a buddy comes back from it.
        /// </summary>
        internal static string? SidecarContents(string _)
        {
            if (!YourBuddyPlugin.ConfigSaveSupport.Value) return null;

            List<BuddyState> states = [];
            foreach (BuddyBehaviour buddy in Buddies)
            {
                if (buddy != null) states.Add(CaptureState(buddy));
            }
            BuddySaveFile data = BuddySaveFile.Of(states, [.. NpcDoors.KnownCodes], BuddyCryoSpawn.OpenedCapsule);
            return JsonConvert.SerializeObject(data, Formatting.Indented);
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
                Owner = buddy.Agent.CurrentOwner,
                OwnerLocalPosition = ownerLocal,
                ShipLocalPosition = shipLocal,
                WorldPosition = [pos.x, pos.y, pos.z],
                Rotation = [rot.x, rot.y, rot.z, rot.w],
                SleepingCapsule = BuddyCryoSpawn.SleepingCapsuleOf(buddy)
            };
        }

        private static BuddySaveFile? ReadSidecar(string saveFileName)
        {
            string? contents = NpcSaves.Read(saveFileName, SidecarExtension);
            if (contents == null)
            {
                YourBuddyPlugin.Log.LogInfo($"[mgr] No buddy sidecar at {NpcSaves.PathOf(saveFileName, SidecarExtension)}");
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<BuddySaveFile>(contents);
            }
            catch (Exception ex)
            {
                YourBuddyPlugin.Log.LogWarning($"[mgr] Failed to read buddy sidecar: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// From NpcEvents.SaveLoaded: the buddies of that save spawn once its scene is ready.
        /// </summary>
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

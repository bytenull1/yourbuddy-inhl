using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
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
        // ReSharper disable RedundantDefaultMemberInitializer
        public static BuddyBehaviour? CurrentBuddy;
        private static bool _tickLogged = false;

        // Pending spawn after loading a save
        private static BuddySaveFile? _pendingSpawn = null;

        // Lifecare scan integration (game internals access lives in GameInternals)
        private static Image? _buddyLifeIcon = null;
        private static LifecareDisplay? _lifeDisplay = null;
        private static float _lifeHookAt = 0f;
        private static Vector3? _lastBuddyLocalPos = null;
        private static bool _scanWasActive = false;
        private static bool _terminalWasEnabled = false;

        // Gates the buddy closed, with the moment of each close. Gate.OnClosed fires at
        // the end of the animation, so this must outlive the Close() call itself.
        private static readonly List<KeyValuePair<Gate, float>> BuddyClosedGates = [];
        private const float BuddyCloseWindow = 6f;
        // ReSharper restore RedundantDefaultMemberInitializer

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
        /// Records that the buddy - not the player, not the game - issued a close on
        /// this gate, so the EntryDetector patch can tell the two apart.
        /// </summary>
        internal static void NoteBuddyClosedGate(Gate gate)
        {
            if (gate == null) return;

            for (int i = BuddyClosedGates.Count - 1; i >= 0; i--)
            {
                if (BuddyClosedGates[i].Key == gate || BuddyClosedGates[i].Key == null ||
                    Time.time - BuddyClosedGates[i].Value > BuddyCloseWindow)
                {
                    BuddyClosedGates.RemoveAt(i);
                }
            }
            BuddyClosedGates.Add(new KeyValuePair<Gate, float>(gate, Time.time));
        }

        /// <summary>
        /// True when the buddy closed this gate within the last BuddyCloseWindow.
        /// </summary>
        internal static bool GateWasClosedByBuddy(Gate gate)
        {
            if (gate == null) return false;

            bool found = false;
            for (int i = BuddyClosedGates.Count - 1; i >= 0; i--)
            {
                if (BuddyClosedGates[i].Key == null ||
                    Time.time - BuddyClosedGates[i].Value > BuddyCloseWindow)
                {
                    BuddyClosedGates.RemoveAt(i);
                    continue;
                }
                if (BuddyClosedGates[i].Key == gate) found = true;
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

            // Spawn a buddy saved with this save file once the game scene is fully loaded.
            if (_pendingSpawn != null)
            {
                if (sceneReady)
                {
                    BuddySaveFile data = _pendingSpawn;
                    _pendingSpawn = null;
                    if (data.Alive)
                    {
                        Vector3 position = RestorePosition(data);
                        Quaternion rotation = RestoreRotation(data);
                        YourBuddyPlugin.Log.LogInfo($"[mgr] Restoring buddy from save at {position}");
                        YourBuddyPlugin.SpawnBuddy(position, rotation);
                        if (CurrentBuddy != null)
                        {
                            AttachToOwner(CurrentBuddy, data.Owner);
                            BuddyCryoSpawn.ResumeSleep(CurrentBuddy, data.SleepingCapsule);
                            if (data.KnownPinCodes != null) foreach (int code in data.KnownPinCodes) CurrentBuddy.LearnPinCode(code);
                        }
                    }
                    else
                    {
                        YourBuddyPlugin.Log.LogInfo("[mgr] Buddy was dead in this save file - not spawning. Use 'spawn_buddy'.");
                    }
                }
            }

            // After the save's own buddy, so a restored one is never doubled by the new-game wake-up.
            // sceneReady implies gm != null.
            if (sceneReady) BuddyCryoSpawn.Tick(gm!);

            // Periodic node-graph save
            BuddyNodeGraph.TickSave();

            // The game re-enables the monster on its own schedule, so a debug override
            // has to be re-asserted rather than set once.
            if (AiDebug.Any) AiDebug.Apply();

            if (CurrentBuddy != null && CurrentBuddy.gameObject.activeInHierarchy && Time.time >= _lifeHookAt)
            {
                _lifeHookAt = Time.time + 0.1f; // Call more frequently to catch scan state changes
                TryHookLifecare();
                UpdateBuddyLifeIcon();
            }
        }

        private static Vector3 RestorePosition(BuddySaveFile data)
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

        private static Quaternion RestoreRotation(BuddySaveFile data)
        {
            if (data.Rotation is { Length: 4 })
            {
                return new Quaternion(data.Rotation[0], data.Rotation[1], data.Rotation[2], data.Rotation[3]);
            }
            return Quaternion.identity;
        }

        public static void DespawnBuddy()
        {
            if (CurrentBuddy != null)
            {
                Destroy(CurrentBuddy.gameObject);
                CurrentBuddy = null;
            }
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

            // Re-hook when the display changed or our clone went missing: the game may
            // rebuild the terminal UI while the display object itself survives.
            // docs/lifecare.md
            bool iconLost = _buddyLifeIcon == null || _buddyLifeIcon.transform.parent == null;
            if (_lifeDisplay == display && !iconLost) return;

            Image? breathless = GameInternals.LifecareDisplayAccess.GetBreathlessIcon(display);
            if (breathless == null) return;

            if (iconLost && _lifeDisplay == display)
            {
                YourBuddyPlugin.Log.LogInfo("[mgr] Lifecare buddy icon was lost - re-creating it");
            }

            // Clone the breathless icon as the buddy's own map icon.
            GameObject clone = Instantiate(breathless.gameObject, breathless.transform.parent);
            clone.name = "BuddyLifeIcon";
            _buddyLifeIcon = clone.GetComponent<Image>();
            if (_buddyLifeIcon == null)
            {
                Destroy(clone);
                return;
            }
            _buddyLifeIcon.enabled = false;
            // Keep the last scan's snapshot when we are only replacing a lost clone on
            // the same terminal - the scan result is still valid, and dropping it would
            // blank the icon until the player scanned again.
            if (_lifeDisplay != display) _lastBuddyLocalPos = null;
            _lifeDisplay = display;
        }

        private static void UpdateBuddyLifeIcon()
        {
            if (_lifeDisplay == null || _buddyLifeIcon == null) return;

            // Check if terminal is enabled (not in boot/loading state)
            LoadingAnimator? bootLoading = GameInternals.LifecareDisplayAccess.GetBootLoading(_lifeDisplay);
            bool terminalEnabled = bootLoading == null || !bootLoading.gameObject.activeSelf;

            // If terminal just turned off, hide the icon and reset
            if (!terminalEnabled && _terminalWasEnabled)
            {
                _terminalWasEnabled = false;
                _scanWasActive = false;
                if (_buddyLifeIcon.enabled)
                {
                    _buddyLifeIcon.enabled = false;
                    RecountLifeforms(_lifeDisplay);
                }
                return;
            }

            // Track when terminal turns on
            if (terminalEnabled && !_terminalWasEnabled)
            {
                _terminalWasEnabled = true;
                // Restore the icon if we have a cached position from a previous scan
                if (_lastBuddyLocalPos.HasValue)
                {
                    float scale = GameInternals.LifecareDisplayAccess.GetScale(_lifeDisplay);
                    _buddyLifeIcon.enabled = true;
                    _buddyLifeIcon.transform.localPosition = -new Vector3(_lastBuddyLocalPos.Value.x * scale, _lastBuddyLocalPos.Value.z * scale, 0f);
                    RecountLifeforms(_lifeDisplay);
                }
            }

            // Only update when a scan is actively running.
            // The game uses scanLoading.gameObject.activeSelf to indicate an active scan.
            LoadingAnimator? scanLoading = GameInternals.LifecareDisplayAccess.GetScanLoading(_lifeDisplay);
            bool scanIsActive = scanLoading != null && scanLoading.gameObject.activeSelf;

            BuddyBehaviour? buddy = CurrentBuddy;
            Transform? ship = ShipTransform;

            // Track scan state transitions - detect when scan ends
            if (scanIsActive && !_scanWasActive)
            {
                // Scan just started - mark as active but don't update position yet
                _scanWasActive = true;
            }
            else if (!scanIsActive && _scanWasActive)
            {
                // Scan just ended - capture the buddy's current position now
                _scanWasActive = false;
                _lastBuddyLocalPos = null; // Clear any previous cached position

                // A snapshot taken when the scan finishes, like the game's own player
                // and Breathless icons: a buddy not aboard at that instant is not drawn
                // until the next scan. docs/lifecare.md
                if (buddy != null && !buddy.IsDead && ship != null)
                {
                    bool aboard = buddy.IsAboardPlayerShip();
                    if (aboard)
                    {
                        _lastBuddyLocalPos = ship.InverseTransformPoint(buddy.transform.position);
                    }
                    if (YourBuddyPlugin.ConfigDebugLevel.Value >= 1)
                    {
                        YourBuddyPlugin.Log.LogInfo(
                            "[mgr] Lifecare scan finished: buddy aboard=" + aboard +
                            ", pos=" + buddy.transform.position.ToString("0.0") +
                            ", trackedRoom=" + buddy.TrackedRoomName +
                            " -> icon " + (_lastBuddyLocalPos.HasValue ? "shown" : "hidden"));
                    }
                }
                else if (YourBuddyPlugin.ConfigDebugLevel.Value >= 1)
                {
                    YourBuddyPlugin.Log.LogInfo(
                        "[mgr] Lifecare scan finished with no live buddy to draw (buddy=" +
                        (buddy == null ? "null" : buddy.IsDead ? "dead" : "ok") + ", ship=" + (ship == null ? "null" : "ok") + ")");
                }

                UpdateIconVisibility();

                if (YourBuddyPlugin.ConfigDebugLevel.Value >= 1)
                {
                    // The rendered state, not our bookkeeping: is the clone still in a
                    // live UI hierarchy, and did our count survive to the label?
                    // Our own flags have reported "shown" while the player saw nothing.
                    TMP_Text? label = GameInternals.LifecareDisplayAccess.GetLifeformsLabel(_lifeDisplay);
                    Image? playerIcon = GameInternals.LifecareDisplayAccess.GetPlayerIcon(_lifeDisplay);
                    YourBuddyPlugin.Log.LogInfo(
                        "[mgr] Lifecare icon state: enabled=" + _buddyLifeIcon.enabled +
                        ", activeInHierarchy=" + _buddyLifeIcon.gameObject.activeInHierarchy +
                        ", parent=" + (_buddyLifeIcon.transform.parent != null ? _buddyLifeIcon.transform.parent.name : "none") +
                        ", localPos=" + _buddyLifeIcon.transform.localPosition.ToString("0.0") +
                        ", playerIcon=" + (playerIcon != null && playerIcon.enabled) +
                        ", label=" + (label != null ? label.text : "<no label>"));
                }
            }

            // Re-assert every poll, not only on scan edges: whatever the game does to
            // this UI in between, the icon and count are restored within 0.1 s instead
            // of staying wrong until the next scan.
            if (terminalEnabled) UpdateIconVisibility();

            static void UpdateIconVisibility()
            {
                // Only called after UpdateBuddyLifeIcon checked _lifeDisplay and _buddyLifeIcon.
                if (_lastBuddyLocalPos == null)
                {
                    if (_buddyLifeIcon!.enabled)
                    {
                        _buddyLifeIcon.enabled = false;
                        RecountLifeforms(_lifeDisplay);
                    }
                    return;
                }

                // Display the captured position
                float scale = GameInternals.LifecareDisplayAccess.GetScale(_lifeDisplay);
                Vector3 wanted = -new Vector3(_lastBuddyLocalPos.Value.x * scale, _lastBuddyLocalPos.Value.z * scale, 0f);
                if (!_buddyLifeIcon!.gameObject.activeSelf) _buddyLifeIcon.gameObject.SetActive(true);

                _buddyLifeIcon.enabled = true;
                _buddyLifeIcon.transform.localPosition = wanted;
                RecountLifeforms(_lifeDisplay);
            }
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
            // Only the icon belonging to this display: the postfix fires for whichever
            // LifecareDisplay the game touched.
            if (_buddyLifeIcon != null && _buddyLifeIcon.enabled && display == _lifeDisplay) count++;

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
                BuddyBehaviour? buddy = CurrentBuddy;
                BuddySaveFile data = buddy == null ? new BuddySaveFile { Exists = false } : CaptureSaveFile(buddy);
                data = data with { OpenedCapsule = BuddyCryoSpawn.OpenedCapsule, SleepingCapsule = BuddyCryoSpawn.SleepingCapsule };

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
        /// Whether the living buddy is inside this hiding spot, for the game's own Interact.
        /// </summary>
        public static bool BuddyIsHidingIn(HidingSpot spot)
        {
            BuddyBehaviour? buddy = CurrentBuddy;
            return spot != null && buddy != null && !buddy.IsDead && buddy.IsHiddenIn(spot);
        }

        /// <summary>
        /// The buddy as the sidecar stores it. The ship-local position is what a load prefers,
        /// so it is written whenever there is a ship to be local to.
        /// </summary>
        private static BuddySaveFile CaptureSaveFile(BuddyBehaviour buddy)
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

            return new BuddySaveFile
            {
                Exists = true,
                Alive = !buddy.IsDead,
                Owner = buddy.CurrentOwner,
                OwnerLocalPosition = ownerLocal,
                ShipLocalPosition = shipLocal,
                WorldPosition = [pos.x, pos.y, pos.z],
                Rotation = [rot.x, rot.y, rot.z, rot.w],
                KnownPinCodes = [.. buddy.KnownPinCodes]
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

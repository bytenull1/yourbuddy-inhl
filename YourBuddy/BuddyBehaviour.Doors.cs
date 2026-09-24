using System;
using System.Collections.Generic;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Door interaction: open gates in the path, wait for them, close behind; password and airlock gates; room loading around doors.
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        private float waitingGateSince = 0f;
        private float lastDoorRayTime = 0f;
        private float closedToPlayerLogAt = 0f;
        /// <summary>
        /// Fallback radius around a gate's transform searched for bodies before issuing
        /// a close, used only when the gate's own AntiCrasher volumes are unreachable.
        /// </summary>
        private const float DoorwayClearRadius = 1.2f;
        /// <summary>
        /// How near the gate the buddy has to be to count as blocking it itself.
        /// </summary>
        private const float DoorwaySelfRadius = 1.4f;
        /// <summary>
        /// After this long unable to close, close anyway and let the AntiCrasher
        /// arbitrate. A wait with no end is indistinguishable from a forgotten door.
        /// </summary>
        private const float DoorDeferGiveUp = 20f;
        private float doorwayStepOutCooldown = 0f;
        private float doorwayBlockLogAt = 0f;

        // Gates unlocked by pin codes, mapped to the panel that opens them (refreshed
        // periodically). The panel carries both the code and the position to walk to.
        private static readonly Dictionary<Gate, DoorPinCode> PasswordGates = [];
        private static float _passwordGatesRefreshAt = 0f;

        // Door codes the player told any buddy, through the dialog; every buddy knows them all.
        // Persisted in the .buddy sidecar; the vanilla save is never touched. docs/doors.md
        private static readonly HashSet<int> KnownCodes = [];
        // The game the codes were told in, so a save written from the menu cannot take the last game's.
        private static GameManager? _codesIn;

        private static float _impassableGatesRefreshAt = 0f;
        private const float ImpassableGatesTtl = 1f;
        /// <summary>
        /// Cheap first pass before the gate's volume is tested at all.
        /// </summary>
        private const float DoorBroadPhaseRadius = 3f;
        /// <summary>
        /// Body clearance added to a gate's volume, so a route cannot thread the buddy
        /// through a door leaf's edge.
        /// </summary>
        private const float DoorBlockPadding = 0.3f;
        /// <summary>
        /// How near the buddy must get to a pin panel before it may enter the code.
        /// </summary>
        private const float PinPanelReachDist = 1.6f;
        private float pinPanelTryAt = 0f;

        /// <summary>
        /// Simple door handling: open closed room gates in front of the buddy, wait for
        /// Our door to finish opening, walk through everything already open.
        /// </summary>
        private Vector3 HandleDoors(Vector3 desired, ref bool wantMove)
        {
            if (waitingGate != null)
            {
                if (waitingGate.FullyOpened)
                {
                    waitingGate = null;
                }
                else if (Time.time - waitingGateSince > 5f)
                {
                    YourBuddyPlugin.Log.LogWarning("[ai] Timed out waiting for a door, moving on");
                    waitingGate = null;
                }
                else
                {
                    wantMove = false;
                    return Vector3.zero;
                }
            }

            if (YourBuddyPlugin.ConfigAutoDoors.Value && wantMove && Time.time >= lastDoorRayTime + 0.2f)
            {
                lastDoorRayTime = Time.time;

                Vector3 dir = new(desired.x, 0f, desired.z);
                if (dir.sqrMagnitude > 0.0001f)
                {
                    dir.Normalize();
                    Vector3 origin = GroundPos(0.9f);
                    if (Physics.Raycast(origin, dir, out RaycastHit hit, 1.2f, ProbeLayers, QueryTriggerInteraction.Ignore))
                    {
                        Gate gate = hit.collider.GetComponentInParent<Gate>();
                        // ReSharper disable once MergeIntoPattern
                        if (gate != null && gate.gameObject.activeInHierarchy && !gate.Locked && !gate.Opened)
                        {
                            // A gate the buddy may not open itself is left alone; the
                            // path search already routes around it.
                            if (!GateIsPassable(gate))
                            {
                                LogClosedToPlayerRefusal(gate);
                                return desired;
                            }

                            // A password door the buddy does know the code for is opened
                            // through its own panel, not by Gate.Open.
                            if (TryUnlockPasswordGate(gate)) return desired;

                            LoadRoomsAroundGate(gate);
                            gate.Open();
                            waitingGate = gate;
                            waitingGateSince = Time.time;
                            ArmDoorClose(gate);
                            YourBuddyPlugin.Log.LogInfo("[ai] Opening door '" + gate.gameObject.name + "'");
                        }
                    }
                }
            }

            return desired;
        }


        /// <summary>
        /// May the buddy open this gate itself? Door handling only.
        /// This is not the routing question: docs/invariants.md#opening-is-not-passing
        /// </summary>
        private static bool GateIsPassable(Gate gate)
        {
            if (gate == null || !gate.gameObject.activeInHierarchy) return false;

            if (gate.Opened) return true;

            if (gate.Locked || WhyClosedToPlayer(gate) != null) return false;
            // Airlocks cycle themselves; opening one risks venting.
            if (IsAirlockGate(gate)) return false;
            // docs/invariants.md#keep-electricitypanelgate-excluded
            if (gate.gameObject.name.IndexOf("ElectricityPanelGate", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
            DoorPinCode panel = PinPanelFor(gate);
            if (panel == null) return true;

            return CodeKnown(GameInternals.DoorPinCodeAccess.GetPinCode(panel));
        }

        /// <summary>
        /// Should the path search route around this gate? Only one that will still be
        /// shut when the buddy arrives: locked, closed to the player, or a pin door it has no code for.
        /// docs/invariants.md#opening-is-not-passing
        /// </summary>
        private static bool GateBlocksRouting(Gate gate)
        {
            if (gate == null || !gate.gameObject.activeInHierarchy) return false;

            if (gate.Opened) return false;

            if (gate.Locked) return true;
            // An airlock or docking gate is the only way between ship and station and
            // the player opens it. Refusing to open one is not a reason to refuse to
            // walk through it. docs/invariants.md#opening-is-not-passing
            if (IsAirlockGate(gate)) return false;
            if (WhyClosedToPlayer(gate) != null) return true;

            DoorPinCode panel = PinPanelFor(gate);
            if (panel == null) return false;

            return !CodeKnown(GameInternals.DoorPinCodeAccess.GetPinCode(panel));
        }

        /// <summary>
        /// Why the player could not walk this gate open, or null when they could. A gate its doorway
        /// detectors drive opens only through one that is switched on, and needs a suit if that one does.
        /// docs/invariants.md#the-buddy-opens-only-what-the-player-could
        /// </summary>
        private static string? WhyClosedToPlayer(Gate gate)
        {
            RefreshDetectors();
            if (!DetectorsByGate.TryGetValue(gate, out List<EntryDetector>? detectors)) return null;

            bool driven = false;
            bool suitOnly = false;
            foreach (EntryDetector detector in detectors)
            {
                if (detector == null) continue;

                driven = true;
                if (!detector.gameObject.activeInHierarchy) continue;
                if (GameInternals.PlayerDetectorAccess.GetHelmetRequired(detector) != true || !PlayerLacksSuit()) return null;

                suitOnly = true;
            }
            if (!driven) return null;

            return suitOnly
                ? "it opens only for someone in a suit, and you have none"
                : "its doorway sensor is switched off, so you cannot walk it open either";
        }

        private static bool PlayerLacksSuit()
        {
            GameManager gm = GameManager.Instance;
            Space.Player? player = gm != null && gm.PlayerShip != null ? gm.PlayerShip.Pilot : null;
            return player != null && player.EquipmentSystem != null && !player.EquipmentSystem.SuitEquipped;
        }

        private void LogClosedToPlayerRefusal(Gate gate)
        {
            if (Time.time < closedToPlayerLogAt) return;

            string? why = WhyClosedToPlayer(gate);
            if (why == null) return;

            closedToPlayerLogAt = Time.time + 5f;
            YourBuddyPlugin.Log.LogInfo("[ai] Not opening '" + gate.gameObject.name + "' - " + why);
        }

        /// <summary>
        /// The pin panel wired to this gate, or null when it is not a password door.
        /// The whole map is rebuilt every 5 s - the set of panels only changes with the
        /// scene, and a wholesale refresh cannot go stale per entry.
        /// </summary>
        private static DoorPinCode PinPanelFor(Gate gate)
        {
            RefreshPasswordGates();
            return PasswordGates.GetValueOrDefault(gate);
        }

        private static void RefreshPasswordGates()
        {
            if (Time.time < _passwordGatesRefreshAt || !SceneScan.MayRescan(_passwordGatesRefreshAt <= 0f)) return;

            PasswordGates.Clear();
            foreach (DoorPinCode pin in FindObjectsOfType<DoorPinCode>(true))
            {
                Gate? wired = GameInternals.DoorPinCodeAccess.GetWiredGate(pin);
                if (wired != null) PasswordGates[wired] = pin;
            }
            _passwordGatesRefreshAt = Time.time + 5f;
        }

        /// <summary>
        /// True for gates unlocked through a pin code, whatever the buddy knows.
        /// </summary>
        private static bool IsPasswordGate(Gate gate) => PinPanelFor(gate) != null;

        /// <summary>
        /// Enters a code the player gave the buddy on the gate's own panel. Returns
        /// true when this gate is a password door, so the caller stops handling it -
        /// including while the buddy is still walking to the panel.
        /// </summary>
        private bool TryUnlockPasswordGate(Gate gate)
        {
            DoorPinCode panel = PinPanelFor(gate);
            if (panel == null) return false;

            if (Time.time < pinPanelTryAt) return true;

            // Out of arm's reach: keep walking, the route already leads past the panel.
            if ((transform.position - panel.transform.position).sqrMagnitude > PinPanelReachDist * PinPanelReachDist)
            {
                return true;
            }

            pinPanelTryAt = Time.time + 2f;
            LoadRoomsAroundGate(gate);
            // The panel's own OnValidated wiring opens the gate, so the door goes
            // through the game's path and the close-behind bookkeeping still applies.
            // Never PinCode.Interact(null) - that is the monster's random-guess branch.
            panel.ForceValidate();
            ArmDoorClose(gate);
            YourBuddyPlugin.Log.LogInfo("[ai] Entered the password for '" + gate.gameObject.name + "'");
            return true;
        }

        /// <summary>
        /// Gates no buddy can currently get through, for the path search.
        /// Short TTL: lock and open state both change during play.
        /// </summary>
        private static void RefreshImpassableGates()
        {
            if (Time.time < _impassableGatesRefreshAt) return;

            _impassableGatesRefreshAt = Time.time + ImpassableGatesTtl;
            ImpassableGates.Clear();
            foreach (Gate gate in NavProbe.Gates)
            {
                if (GateBlocksRouting(gate)) ImpassableGates.Add(gate);
            }
        }

        /// <summary>
        /// True when a->b passes through a gate the buddies cannot open; a corridor
        /// running past a shut airlock door has to stay routable. Bound once as
        /// BuddyNodeGraph.SegmentBlockedByDoor. docs/invariants.md#locked-doors-block-edges
        /// </summary>
        internal static bool SegmentBlockedByDoor(Vector3 a, Vector3 b)
        {
            RefreshImpassableGates();
            if (ImpassableGates.Count == 0) return false;

            foreach (Gate gate in ImpassableGates)
            {
                if (gate == null) continue;

                if (FlatDistanceToSegment(gate.transform.position, a, b) > DoorBroadPhaseRadius) continue;

                if (NavProbe.SegmentCrossesGate(gate, a, b, DoorBlockPadding)) return true;
            }
            return false;
        }

        /// <summary>
        /// Horizontal distance from a point to a segment.
        /// </summary>
        private static float FlatDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 pf = new(p.x, p.z);
            Vector2 af = new(a.x, a.z);
            Vector2 bf = new(b.x, b.z);
            Vector2 ab = bf - af;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f) return (pf - af).magnitude;

            float t = Mathf.Clamp01(Vector2.Dot(pf - af, ab) / lenSq);
            return (pf - (af + ab * t)).magnitude;
        }

        /// <summary>
        /// Records a door code the player gave a buddy. True when it is new.
        /// </summary>
        public static bool LearnPinCode(int code) => Codes().Add(code);

        /// <summary>
        /// The codes the buddies have been told, for the save sidecar.
        /// </summary>
        public static IReadOnlyCollection<int> KnownPinCodes => Codes();

        /// <summary>
        /// The code set of the running game: a new GameManager starts it empty.
        /// </summary>
        private static HashSet<int> Codes()
        {
            if (_codesIn == GameManager.Instance) return KnownCodes;

            KnownCodes.Clear();
            _codesIn = GameManager.Instance;
            return KnownCodes;
        }

        private static bool CodeKnown(int? code) => code.HasValue && Codes().Contains(code.Value);

        /// <summary>
        /// True when a known code opens at least one password door in the scene.
        /// </summary>
        public static bool AnyKnownDoorMatches()
        {
            RefreshPasswordGates();
            foreach (KeyValuePair<Gate, DoorPinCode> entry in PasswordGates)
            {
                if (CodeKnown(GameInternals.DoorPinCodeAccess.GetPinCode(entry.Value))) return true;
            }
            return false;
        }

        private static bool IsAirlockGate(Gate gate)
        {
            RefreshAirlocks();
            if (_cachedAirlocks == null) return false;

            foreach (Airlock airlock in _cachedAirlocks)
            {
                if (GameInternals.AirlockAccess.GetOuterDoor(airlock) == gate) return true;

                if (GameInternals.AirlockAccess.GetInnerDoor(airlock) == gate) return true;

                if (GameInternals.AirlockAccess.GetHatch(airlock) == gate) return true;
            }
            return false;
        }


        /// <summary>
        /// Registers a door the buddy just opened; re-arming only refreshes the timer.
        /// `alreadyCrossed` is for a door it blocked rather than opened. docs/doors.md
        /// </summary>
        private void ArmDoorClose(Gate gate, bool alreadyCrossed = false)
        {
            foreach (PendingDoorClose existing in pendingDoorCloses)
            {
                if (existing.Gate == gate)
                {
                    existing.CloseAt = Time.time + 3f;
                    existing.ArmedAt = Time.time;
                    existing.Attempts = 0;
                    return;
                }
            }
            pendingDoorCloses.Add(new PendingDoorClose
            {
                Gate = gate,
                CloseAt = Time.time + 3f,
                ArmedAt = Time.time,
                Attempts = 0,
                DeferredSince = 0f,
                OpenedFrom = transform.position,
                Crossed = alreadyCrossed
            });
        }

        /// <summary>
        /// A close undone by the gate's own AntiCrasher, from the Gate.FailClose patch.
        /// The game never retries, so a door the buddy blocked is the buddy's to
        /// finish. docs/doors.md#5-closes-the-buddy-blocked
        /// </summary>
        public void NoteCloseFailed(Gate gate)
        {
            if (IsDead || gate == null || !YourBuddyPlugin.ConfigAutoDoors.Value) return;
            // Somebody else was in the way; not ours to finish.
            if ((transform.position - gate.transform.position).sqrMagnitude >= DoorwaySelfRadius * DoorwaySelfRadius)
            {
                return;
            }
            // Airlocks cycle themselves and password doors are not ours to touch.
            if (IsAirlockGate(gate) || IsPasswordGate(gate)) return;

            foreach (PendingDoorClose existing in pendingDoorCloses)
            {
                if (existing.Gate != gate) continue;
                // Already owed, and deliberately not re-armed:
                // docs/invariants.md#fail-close-must-not-rearm
                existing.CloseAt = Mathf.Max(existing.CloseAt, Time.time + 1.5f);
                StepOutOfDoorway(gate);
                return;
            }

            ArmDoorClose(gate, alreadyCrossed: true);
            YourBuddyPlugin.Log.LogInfo("[ai] Blocked '" + gate.gameObject.name +
                                        "' from closing - taking the close over");
            StepOutOfDoorway(gate);
        }

        /// <summary>
        /// "Behind itself" means it actually went through. A door opened ahead and shut
        /// again three seconds later is a door slammed in its own face.
        /// docs/invariants.md#close-only-what-you-walked-through
        /// </summary>
        private bool HasCrossed(PendingDoorClose pending, Gate gate)
        {
            if (pending.Crossed) return true;

            Vector3 gatePos = gate.transform.position;
            Vector3 now = transform.position - gatePos;
            Vector3 then = pending.OpenedFrom - gatePos;
            now.y = 0f;
            then.y = 0f;
            if (now.sqrMagnitude > 0.25f && Vector3.Dot(now, then) < 0f) pending.Crossed = true;

            return pending.Crossed;
        }

        /// <summary>
        /// Closes the doors the buddy opened, once it is clear of each doorway.
        /// Retries matter because a close issued into a blocker is undone by the door
        /// itself. Doors the buddy did not open are not tracked - see docs/doors.md.
        /// </summary>
        private void UpdateDoorCloseBehind()
        {
            if (IsDead || pendingDoorCloses.Count == 0) return;

            for (int i = pendingDoorCloses.Count - 1; i >= 0; i--)
            {
                PendingDoorClose pending = pendingDoorCloses[i];
                Gate gate = pending.Gate;

                // Destroyed with its room, already shut, or no longer ours to close.
                if (gate == null || !gate.Opened || !YourBuddyPlugin.ConfigAutoDoors.Value ||
                    gate.Locked || Time.time - pending.ArmedAt > 90f)
                {
                    pendingDoorCloses.RemoveAt(i);
                    continue;
                }

                if (Time.time < pending.CloseAt) continue;

                if (!HasCrossed(pending, gate))
                {
                    pending.CloseAt = Time.time + 1f;
                    continue;
                }

                // Closing into a blocker just feeds the AntiCrasher, which re-opens the
                // door and burns an attempt. docs/doors.md
                bool selfBlocking = (transform.position - gate.transform.position).sqrMagnitude <
                                    DoorwaySelfRadius * DoorwaySelfRadius;
                Collider? blocker = selfBlocking ? null : DoorwayBlocker(gate);

                if (selfBlocking || blocker != null)
                {
                    if (pending.DeferredSince <= 0f) pending.DeferredSince = Time.time;

                    if (selfBlocking)
                    {
                        // Waiting alone is not enough: while the player stays close the
                        // buddy has no reason to move, so it can loiter in the doorway
                        // indefinitely. Push it clear instead.
                        StepOutOfDoorway(gate);
                    }
                    else
                    {
                        // Not self-blocking, so the check above found a blocker.
                        LogDoorwayBlocked(gate, blocker!);
                    }

                    // docs/invariants.md#no-permanent-deferral, except when the buddy is
                    // itself the blocker:
                    // docs/invariants.md#never-force-a-close-into-the-buddy
                    if (selfBlocking || Time.time - pending.DeferredSince < DoorDeferGiveUp)
                    {
                        pending.CloseAt = Time.time + (selfBlocking ? 1f : 1.5f);
                        continue;
                    }
                }

                pending.DeferredSince = 0f;
                gate.Close();
                // Tell the EntryDetector patch this close was ours, so it does not let
                // the game re-file the player into whatever room this doorway leads to.
                BuddyManager.NoteBuddyClosedGate(gate, this);
                pending.Attempts++;
                YourBuddyPlugin.Log.LogInfo("[ai] Closing door '" + gate.gameObject.name +
                                            "' behind itself (attempt " + pending.Attempts + ")");

                if (pending.Attempts >= 10)
                {
                    YourBuddyPlugin.Log.LogWarning("[ai] Giving up on closing '" +
                                                   gate.gameObject.name + "' after " + pending.Attempts + " attempts");
                    pendingDoorCloses.RemoveAt(i);
                }
                else
                {
                    pending.CloseAt = Time.time + 3f;
                }
            }
        }

        /// <summary>
        /// Walks the buddy out of a doorway it is holding open. Only when it is not
        /// already walking somewhere - a mid-route buddy clears the doorway on its own.
        /// </summary>
        private void StepOutOfDoorway(Gate gate)
        {
            if (Time.time < followStepOffUntil || Time.time < doorwayStepOutCooldown) return;

            if (hasMoveTarget || mode == BuddyMode.Route) return;

            Vector3 away = transform.position - gate.transform.position;
            away.y = 0f;
            // Standing exactly in the threshold: any horizontal direction will do, so
            // use the way the buddy is facing.
            if (away.sqrMagnitude < 0.04f) away = transform.forward;

            away.y = 0f;
            if (away.sqrMagnitude < 0.001f) away = Vector3.forward;

            away.Normalize();

            followStepOffTarget = transform.position + away * 1.8f;
            followStepOffUntil = Time.time + 1.5f;
            doorwayStepOutCooldown = Time.time + 4f;
            YourBuddyPlugin.Log.LogInfo("[ai] Standing in '" + gate.gameObject.name +
                                        "' - stepping clear so it can close");
        }

        /// <summary>
        /// The collider standing in the doorway that would make a close fail, or null.
        /// Asks the gate's own AntiCrasher sensors rather than guessing at a shape:
        /// docs/invariants.md#occupancy-asks-the-anticrasher
        /// </summary>
        private Collider? DoorwayBlocker(Gate gate)
        {
            AntiCrasher[]? sensors = GameInternals.GateAccess.GetAntiCrashers(gate);
            bool probedASensor = false;
            if (sensors != null)
            {
                foreach (AntiCrasher sensor in sensors)
                {
                    if (sensor == null || !sensor.gameObject.activeInHierarchy) continue;
                    // Normally on the sensor itself; tolerate a prefab that parks it on
                    // a child rather than falling back to the crude radius.
                    if (!sensor.TryGetComponent(out Collider volume))
                    {
                        volume = sensor.GetComponentInChildren<Collider>();
                    }
                    if (volume == null) continue;

                    probedASensor = true;

                    Bounds bounds = volume.bounds;
                    // The sensor's own layer row, not the buddy's: an AntiCrasher stops
                    // for a dropped item, which a body walks straight through.
                    int hits = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, OverlapBuffer,
                        Quaternion.identity, NavProbe.CollisionMaskFor(volume.gameObject.layer),
                        QueryTriggerInteraction.Ignore);
                    Collider? blocker = FirstDoorwayBlocker(gate, hits);
                    if (blocker != null) return blocker;
                }
            }
            if (probedASensor) return null;

            // No sensors reachable (reflection failed, or this prefab has none): fall
            // back to the old radius so the check degrades rather than disappearing.
            int count = Physics.OverlapSphereNonAlloc(gate.transform.position, DoorwayClearRadius,
                OverlapBuffer, ProbeLayers, QueryTriggerInteraction.Ignore);
            return FirstDoorwayBlocker(gate, count);
        }

        /// <summary>
        /// First entry in OverlapBuffer that would trip the gate's AntiCrasher.
        /// Must be called before the buffer is reused.
        /// </summary>
        private Collider? FirstDoorwayBlocker(Gate gate, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Collider blocker = OverlapBuffer[i];
                if (blocker == null) continue;

                if (blocker.gameObject.layer == 0)
                {
                    continue;               // static geometry
                }

                if (blocker.transform.IsChildOf(gate.transform))
                {
                    continue; // the door leaves
                }

                if (blocker.transform.IsChildOf(transform))
                {
                    continue;      // us; the distance check owns that
                }

                return blocker;
            }
            return null;
        }

        /// <summary>
        /// Names whatever is holding a door open, throttled. Without it a silently
        /// deferred door cannot be diagnosed from a capture. See docs/logging.md.
        /// </summary>
        private void LogDoorwayBlocked(Gate gate, Collider blocker)
        {
            if (YourBuddyPlugin.ConfigDebugLevel.Value < 1) return;

            if (Time.time < doorwayBlockLogAt) return;

            doorwayBlockLogAt = Time.time + 5f;
            YourBuddyPlugin.Log.LogInfo("[ai] Waiting to close '" + gate.gameObject.name +
                                        "': '" + blocker.gameObject.name + "' (layer '" +
                                        LayerMask.LayerToName(blocker.gameObject.layer) +
                                        "') is in the doorway");
        }

        private static bool RoomIsAirlockChamber(Room? room)
        {
            if (room == null) return false;

            RefreshAirlocks();
            if (_cachedAirlocks == null) return false;

            foreach (Airlock airlock in _cachedAirlocks)
            {
                Room? connected = GameInternals.AirlockAccess.GetConnectedRoom(airlock);
                if (connected == room) return true;
            }
            return false;
        }

        /// <summary>
        /// When the buddy opens a gate, immediately load the rooms on both sides.
        /// </summary>
        private void LoadRoomsAroundGate(Gate gate)
        {
            RefreshDetectors();
            if (_cachedDetectors == null) return;

            foreach (EntryDetector detector in _cachedDetectors)
            {
                if (detector == null) continue;

                Gate? detectorDoor = GameInternals.EntryDetectorAccess.GetDoor(detector);
                if (detectorDoor != gate) continue;

                Room? innerRoom = GameInternals.EntryDetectorAccess.GetInnerRoom(detector);
                Room? outerRoom = GameInternals.EntryDetectorAccess.GetOuterRoom(detector);
                LoadRoom(innerRoom, "opening the door to", outerRoom);
                LoadRoom(outerRoom, "opening the door to", innerRoom);
            }
        }


    }
}

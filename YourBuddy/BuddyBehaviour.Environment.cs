using Space;
using Space.Data;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Room tracking and content loading, floor plane tracking, environment and space protection, atmosphere damage, Breathless catch and death.
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        /// <summary>
        /// The player's HealthSystem.deathCounter and threat, for the buddy. docs/game-model.md#atmosphere-kills-by-the-players-rule
        /// </summary>
        private int deathCounter = 0;
        private int lifeThreatLevel = 0;
        private bool lifeThreat = false;
        private bool currentlyInSpace = false;
        private Environment? currentEnvironmentRef = null;

        // Life threat: HealthSystem.Tick's numbers. docs/game-model.md#atmosphere-kills-by-the-players-rule
        private const int LifeThreatLethal = 10;
        private const int DeathCounterArmed = 5;
        private const int DeathCounterCap = 10;
        private const int VacuumThreat = 100;
        /// <summary>
        /// PilotSuit.temperatureResistance, read from level1: +1 C. docs/game-model.md#atmosphere-kills-by-the-players-rule
        /// </summary>
        private const int BuddySuitTemperatureResistance = 100;
        /// <summary>
        /// The GameManager whose OnTick AtmosphereTick is subscribed to, or null.
        /// </summary>
        private GameManager? tickSource = null;
        /// <summary>
        /// This tick's threat, counted on the next: docs/game-model.md#atmosphere-kills-by-the-players-rule
        /// </summary>
        private int pendingThreat = 0;

        // Set only while ParkWithShip holds the buddy inactive for a spacewalk.
        private bool parkedWithShip = false;
        private bool hasKnownRoom = false;
        // Stored in the space of the anchor it was measured against: a world coordinate
        // recorded on a station expires the moment the world moves around the ship.
        // docs/invariants.md#the-buddy-rides-its-own-floor
        private Vector3 lastValidInsideLocal = Vector3.zero;
        private string? safePositionOwner = null;
        private bool hasSafePosition = false;

        private float baseFloorMismatchTimer = 0f;

        private float detectorsRefreshAt = 0f;
        private float airlocksRefreshAt = 0f;
        private List<Environment>? cachedEnvironments = null;
        private float environmentsRefreshAt = 0f;

        private static readonly WaitForSeconds CatchReleaseDelay = new(1.5f);

        // ------------------------------------------------------------------
        // Room tracking & content loading (so rooms "load" for the NPC too)
        // ------------------------------------------------------------------

        private void UpdateRoomTracking()
        {
            RefreshDetectors();
            if (cachedDetectors == null) return;

            EntryDetector? best = null;
            // Until a room has ever been resolved (spawn, save restore), the nearest detector
            // at any range; after that, within 3.5 m. docs/lifecare.md
            bool bootstrap = currentRoomRef == null;
            float bestDistanceSquared = bootstrap ? float.MaxValue : 3.5f * 3.5f;
            Vector3 pos = transform.position;

            foreach (EntryDetector detector in cachedDetectors)
            {
                if (detector == null || !detector.gameObject.activeInHierarchy) continue;
                // An unbounded search would otherwise hand a buddy on the ship a docked
                // station's room, and the environment that comes with it.
                if (bootstrap && CurrentOwner != null &&
                    BuddyManager.OwnerOfTransform(detector.transform) != CurrentOwner)
                {
                    continue;
                }
                float distanceSquared = (detector.transform.position - pos).sqrMagnitude;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    best = detector;
                }
            }

            if (best == null) return;

            Transform? rotationRef = GameInternals.EntryDetectorAccess.GetRotationReference(best);
            bool reverseSide = GameInternals.EntryDetectorAccess.GetReverseSide(best);
            if (rotationRef == null) return;

            // Same side test EntryDetector.TriggerCheckForEnter uses for the player.
            Vector3 local = rotationRef.InverseTransformPoint(pos);
            bool innerSide = reverseSide ? (local.z < 0f) : (local.z > 0f);

            Room? innerRoom = GameInternals.EntryDetectorAccess.GetInnerRoom(best);
            Room? outerRoom = GameInternals.EntryDetectorAccess.GetOuterRoom(best);
            if (innerRoom == null && outerRoom == null) return;

            EnableRoom(innerRoom);
            EnableRoom(outerRoom);

            Room? target = innerSide ? innerRoom : outerRoom;
            if (target != null)
            {
                if (target != currentRoomRef)
                {
                    currentRoomRef = target;
                    hasKnownRoom = true;
                    SetForcedRoom(target);
                }

                // Never treat the inside of an airlock as a safe position.
                if (!RoomIsAirlockChamber(target)) RememberSafePosition(pos);
            }
        }


        /// <summary>
        /// Records a spot known to be inside, in the frame the buddy is riding.
        /// </summary>
        private void RememberSafePosition(Vector3 worldPos)
        {
            Transform anchor = transform.parent;
            lastValidInsideLocal = anchor != null ? anchor.InverseTransformPoint(worldPos) : worldPos;
            safePositionOwner = CurrentOwner;
            hasSafePosition = true;
        }

        /// <summary>
        /// False once the buddy rides a different frame: the stored point then names a
        /// spot in the old frame, which after an undock is an arbitrary point in vacuum.
        /// </summary>
        private bool TryRecallSafePosition(out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (!hasSafePosition || safePositionOwner != CurrentOwner) return false;

            Transform anchor = transform.parent;
            worldPos = anchor != null ? anchor.TransformPoint(lastValidInsideLocal) : lastValidInsideLocal;
            return true;
        }

        /// <summary>
        /// Tracks the buddy's stable floor plane.
        /// </summary>
        private void UpdateBaseFloor()
        {
            if (!Physics.Raycast(GroundPos(0.3f), Vector3.down, out RaycastHit hit, 3f, ProbeLayers, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            if (!hasBaseFloor)
            {
                baseFloorY = hit.point.y;
                hasBaseFloor = true;
                baseFloorMismatchTimer = 0f;
                return;
            }

            if (Mathf.Abs(hit.point.y - baseFloorY) <= 0.12f)
            {
                baseFloorY = hit.point.y;
                baseFloorMismatchTimer = 0f;
            }
            else
            {
                baseFloorMismatchTimer += 0.25f;
                if (baseFloorMismatchTimer > 4f)
                {
                    baseFloorY = hit.point.y;
                    baseFloorMismatchTimer = 0f;
                }
            }
        }

        /// <summary>
        /// Keeps the buddy's current room content enabled.
        /// </summary>
        private void SetForcedRoom(Room room)
        {
            if (forcedRoom == room) return;

            if (forcedRoom != null) forcedRoom.OnContentStateChanged.RemoveListener(OnForcedRoomContentChanged);

            forcedRoom = room;

            if (forcedRoom != null)
            {
                forcedRoom.OnContentStateChanged.AddListener(OnForcedRoomContentChanged);
                if (!forcedRoom.ContentEnabled) EnableRoom(forcedRoom);
            }
        }

        private void OnForcedRoomContentChanged(bool contentEnabled)
        {
            // A parked buddy holds nothing loaded: docs/invariants.md#an-unloaded-ship-parks-the-buddy
            if (!contentEnabled && !IsDead && isActiveAndEnabled && forcedRoom != null) EnableRoom(forcedRoom);
        }

        /// <summary>
        /// Keeps the buddy in the frame of the floor it is standing on. The game never
        /// moves the ship, it moves the world around it, so a buddy left on a station
        /// must ride that station's container - exactly as a dropped item does via
        /// SpaceObject.SetItemOwnership. The container is also what the game deactivates
        /// on undock, which is what parks the buddy in place.
        /// docs/invariants.md#the-buddy-rides-its-own-floor
        /// </summary>
        private void UpdateOwnerAnchor()
        {
            BuddyManager.FloorOwner(transform.position, out string? owner, out Transform? anchor);
            // No floor to judge by: keep the frame we have rather than guess a new one.
            if (owner == null || owner == CurrentOwner) return;

            CurrentOwner = owner;
            if (transform.parent == anchor) return;

            transform.SetParent(anchor, true);
            YourBuddyPlugin.Log.LogInfo("[ai] Riding '" + owner + "' now");
        }

        /// <summary>
        /// The last room crossed into, for log lines.
        /// </summary>
        internal string TrackedRoomName => currentRoomRef != null ? currentRoomRef.gameObject.name : "none";

        /// <summary>
        /// Room and environment, and the life threat only while there is one, for the HUD.
        /// </summary>
        private string DescribeSurroundings()
        {
            // The room is the last EntryDetector the buddy crossed, not a volume test -
            // the game has no room volumes, so this label is sticky by design.
            string text = "Room: " + (currentRoomRef != null ? currentRoomRef.gameObject.name : "unknown");
            if (currentEnvironmentRef != null)
            {
                text += "\nEnv: " + currentEnvironmentRef.gameObject.name +
                        " (O2 " + currentEnvironmentRef.Data.Oxygen +
                        ", temp " + currentEnvironmentRef.Data.Temperature + ")";
            }
            else
            {
                text += "\nEnv: none" + (currentlyInSpace ? " [EXPOSED TO SPACE]" : "");
            }
            if (lifeThreatLevel > 0 || deathCounter > 0 || lifeThreat)
            {
                text += "\nLife threat: " + lifeThreatLevel + "/10, death " + deathCounter + "/5" + (lifeThreat ? " [!]" : "");
            }
            return text;
        }

        /// <summary>
        /// A restored buddy starts in its saved vessel's frame, before any floor is probed.
        /// </summary>
        internal void RideOwner(string owner, Transform anchor)
        {
            CurrentOwner = owner;
            transform.SetParent(anchor, true);
        }

        /// <summary>
        /// Enables room content like the game does when the player enters a room.
        /// </summary>
        private static void EnableRoom(Room? room)
        {
            if (room == null || !room.gameObject.activeInHierarchy) return;

            CustomRoom? customRoom = room as CustomRoom;
            if (customRoom != null && !customRoom.EnabledStructure) return;

            if (!room.ContentEnabled) room.SetContentEnabled(true);
        }


        // ------------------------------------------------------------------
        // Environment / space protection / mortality
        // ------------------------------------------------------------------

        private void UpdateEnvironment()
        {
            Environment? env = null;

            if (currentRoomRef != null &&
                BuddyNodeGraph.TryVesselOf(currentRoomRef.transform.parent, out SpaceShip? ship, out SpaceObject? spaceObject))
            {
                // TryVesselOf returned true without a ship, so it found a SpaceObject.
                env = ship != null ? GameManager.Instance.PlayerShip.Environment : spaceObject!.GetComponentInChildren<Environment>();
            }

            if (env == null && (hasKnownRoom || IsEnclosed())) env = FindNearestEnvironment();

            if (!currentlyInSpace) currentEnvironmentRef = env;
        }

        /// <summary>
        /// True when the buddy is aboard the player's ship. The floor decides;
        /// `currentRoomRef` looks more authoritative but is only the last doorway
        /// crossed. docs/invariants.md#aboard-is-answered-by-the-floor
        /// </summary>
        public bool IsAboardPlayerShip()
        {
            SpaceShip? ship = GameManager.Instance != null ? GameManager.Instance.PlayerShip : null;
            if (ship == null) return false;

            switch (BuddyManager.FloorOwner(transform.position))
            {
                case BuddyManager.FloorOwnership.PlayerShip: return true;
                case BuddyManager.FloorOwnership.Elsewhere: return false;
            }

            // No floor underneath (mid-jump, mid-teleport, in a doorway over a gap):
            // fall back to the last room the buddy was tracked into.
            if (currentRoomRef != null && ship.Rooms != null)
            {
                foreach (CustomRoom room in ship.Rooms)
                {
                    if (room != null && room == currentRoomRef) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Fires 10 horizontal rays; inside any structure most of them hit geometry
        /// within 20m, in open space almost none do.
        /// </summary>
        private bool IsEnclosed()
        {
            Vector3 origin = GroundPos(0.6f);
            int hits = 0;
            for (int i = 0; i < 10; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 36f, 0f) * Vector3.forward;
                if (Physics.Raycast(origin, dir, 20f, ProbeLayers, QueryTriggerInteraction.Ignore)) hits++;
            }
            return hits >= 4;
        }

        private Environment? FindNearestEnvironment()
        {
            RefreshEnvironments();
            if (cachedEnvironments == null || GameManager.Instance == null) return null;

            Environment spaceEnv = GameManager.Instance.SpaceEnvironment;
            Environment? best = null;
            float bestDistanceSquared = 45f * 45f;
            Vector3 pos = transform.position;

            foreach (Environment env in cachedEnvironments)
            {
                if (env == null || env == spaceEnv) continue;

                if (!env.gameObject.activeInHierarchy) continue;

                float distanceSquared = (env.transform.position - pos).sqrMagnitude;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    best = env;
                }
            }
            return best;
        }

        private void UpdateSpaceState(Player? player)
        {
            GameManager gm = GameManager.Instance;
            bool inSpace =
                (currentRoomRef != null && gm.SpaceRoom != null && currentRoomRef == gm.SpaceRoom) ||
                (hasKnownRoom && currentEnvironmentRef == null) ||
                (player != null && player.Controller != null &&
                 transform.position.y < player.Controller.CachedTransform.position.y - 25f);

            currentlyInSpace = inSpace;

            if (inSpace && YourBuddyPlugin.ConfigPreventSpace.Value) ReturnFromSpace(player);
        }

        private void ReturnFromSpace(Player? player)
        {
            Vector3 target;
            if (hasSafePosition && TryRecallSafePosition(out Vector3 safe))
            {
                target = safe;
            }
            else if (player != null && player.Controller != null && !IsPlayerInSpace(player))
            {
                target = player.Controller.CachedTransform.position +
                         player.Controller.CachedTransform.forward * 1.5f;
            }
            else
            {
                return;
            }

            if ((target - transform.position).sqrMagnitude < 0.25f) return;

            TeleportTo(target + Vector3.up * 0.1f);
            verticalVelocity = 0f;
            navPlan = null;
            YourBuddyPlugin.Log.LogWarning("[ai] Buddy reached open space, teleported back inside");
        }

        private static bool IsPlayerInSpace(Player player)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.SpaceRoom == null) return false;

            return player.CurrentRoom == gm.SpaceRoom;
        }

        /// <summary>
        /// Moves the buddy aboard when it has no floor of its own to ride - the game
        /// does exactly this to its own free-roaming NPC on undock (Docker.Undock).
        /// </summary>
        public void PullAboard(Vector3 position)
        {
            if (IsDead) return;
            // Back to the ship's frame, which is the scene root - the next owner probe
            // re-parents it if it turns out to be standing on something else.
            transform.SetParent(null, true);
            TeleportTo(position);
            verticalVelocity = 0f;
            navPlan = null;
            CurrentOwner = null;
            hasSafePosition = false;
            YourBuddyPlugin.Log.LogWarning("[ai] Undocked with nothing underfoot - moved to the ship airlock");
        }

        /// <summary>
        /// Freezes a buddy aboard while the game unloads the player's ship for a spacewalk,
        /// before any room goes dark. docs/invariants.md#an-unloaded-ship-parks-the-buddy
        /// </summary>
        public void ParkWithShip()
        {
            if (parkedWithShip || IsDead || !gameObject.activeInHierarchy || !IsAboardPlayerShip()) return;

            parkedWithShip = true;
            hands.Drop("the ship is unloading");
            ForceLeaveHidingSpot("the ship is unloading");
            gameObject.SetActive(false);
            YourBuddyPlugin.Log.LogInfo("[ai] Player ship unloaded for a spacewalk - parked aboard");
        }

        public void UnparkFromShip()
        {
            if (!parkedWithShip) return;

            parkedWithShip = false;
            gameObject.SetActive(true);
            YourBuddyPlugin.Log.LogInfo("[ai] Player ship loaded again - unparked");
        }

        /// <summary>
        /// After the player ship was rebuilt around a buddy aboard: moves it by `worldShift`, and
        /// forgets every world position taken in the old layout. docs/invariants.md#the-buddy-rides-its-own-floor
        /// </summary>
        public void RideShipRebuild(byte stage, Vector3 worldShift, bool floorStands, string what)
        {
            if (IsDead) return;

            // The spot it hid in belongs to a room that is being rebuilt around it.
            ForceLeaveHidingSpot("the ship is being rebuilt");

            if (worldShift.sqrMagnitude > 0.0001f)
            {
                TeleportTo(transform.position + worldShift);
                verticalVelocity = 0f;
            }

            // A goto's goal is a world point of the old layout too, so it ends rather than resumes.
            bool gotoEnded = mode == BuddyMode.Route || (mode == BuddyMode.Flee && modeBeforeFlee == BuddyMode.Route);
            if (orderedMode == BuddyMode.Route) orderedMode = null;
            if (mode == BuddyMode.Flee && modeBeforeFlee == BuddyMode.Route) modeBeforeFlee = BuddyMode.Follow;
            if (mode == BuddyMode.Route) SetMode(BuddyMode.Follow);
            DropPlan();
            hasMoveTarget = false;
            followStepOffUntil = 0f;
            if (gotoEnded) decideAt = 0f;

            bool safeSpot = floorStands && !RoomIsAirlockChamber(currentRoomRef);
            if (safeSpot) RememberSafePosition(transform.position);
            else hasSafePosition = false;

            if (YourBuddyPlugin.ConfigDebugLevel.Value < 1) return;

            YourBuddyPlugin.Log.LogInfo(
                $"[ai] Ship rebuilt (stage {stage}): {what}; plan dropped, safe spot " +
                (safeSpot ? "set here" : "forgotten") + (gotoEnded ? ", goto ended" : ""));
        }

        private void TeleportTo(Vector3 position)
        {
            cc.enabled = false;
            transform.position = position;
            cc.enabled = true;
            hasBaseFloor = false;
            baseFloorMismatchTimer = 0f;
        }

        /// <summary>
        /// The game's own tick, the one HealthSystem.Tick runs on. Subscribed while enabled, so a
        /// parked buddy takes no damage. docs/game-model.md#atmosphere-kills-by-the-players-rule
        /// </summary>
        private void OnEnable()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || tickSource == gm) return;

            tickSource = gm;
            gm.OnTick.AddListener(AtmosphereTick);
        }

        private void OnDisable()
        {
            if (tickSource != null) tickSource.OnTick.RemoveListener(AtmosphereTick);

            tickSource = null;
        }

        /// <summary>
        /// The player's own death rule (HealthSystem.Tick), once per game tick: a threat of
        /// LifeThreatLethal or more counts up, anything less counts down.
        /// </summary>
        private void AtmosphereTick()
        {
            if (!YourBuddyPlugin.ConfigMortal.Value || IsDead || Asleep || AiDebug.BuddyDisabled) return;

            // Counted one tick late, as the player's is: the buff reaches HealthSystem through BuffBar.
            lifeThreatLevel = pendingThreat;
            pendingThreat = AtmosphereThreat(currentlyInSpace ? null : currentEnvironmentRef);
            lifeThreat = lifeThreatLevel >= LifeThreatLethal;
            if (lifeThreat) deathCounter = Mathf.Min(deathCounter + 1, DeathCounterCap);
            else if (deathCounter > 0) deathCounter--;

            TraceAtmosphere();

            if (deathCounter > DeathCounterArmed && UnityEngine.Random.Range(0, 5) == 0)
            {
                YourBuddyPlugin.Log.LogWarning($"[ai] Buddy died from a deadly environment (threat {lifeThreatLevel})");
                Die(transform.forward * 2f + Vector3.up);
            }
        }

        private static readonly string[] BuffNames = ["Hunger", "Suffocation", "Hyperoxia", "Cold", "Heat", "Sleepiness", "Energy"];

        /// <summary>
        /// Level 2, once per game tick while either side is threatened: the buddy's numbers
        /// beside the player's, with what feeds the player's. docs/game-model.md#atmosphere-kills-by-the-players-rule
        /// </summary>
        private void TraceAtmosphere()
        {
            if (YourBuddyPlugin.ConfigDebugLevel.Value < 2) return;

            GameManager gm = GameManager.Instance;
            Player? player = gm != null && gm.PlayerShip != null ? gm.PlayerShip.Pilot : null;
            HealthSystem? health = player != null ? player.HealthSystem : null;
            int playerThreat = health != null ? health.CurrentTotalThreat : 0;
            byte? playerCounter = GameInternals.HealthSystemAccess.GetDeathCounter(health);
            if (lifeThreatLevel == 0 && deathCounter == 0 && playerThreat == 0 && playerCounter.GetValueOrDefault() == 0) return;

            Environment? env = currentlyInSpace ? null : currentEnvironmentRef;
            string line = $"[ai] Air tick: buddy threat {lifeThreatLevel}, death {deathCounter}/{DeathCounterArmed}" +
                          (env != null ? $" (O2 {env.Data.Oxygen / 100f:0.0}%, {env.Data.Temperature / 100f:0.0}C)" : " (space)");
            if (player != null && health != null)
            {
                Environment? playerEnv = player.CurrentEnvironment;
                line += $" | player threat {playerThreat}, death {(playerCounter.HasValue ? playerCounter.Value.ToString() : "?")}/{DeathCounterArmed}" +
                        (playerEnv != null ? $" (O2 {playerEnv.Data.Oxygen / 100f:0.0}%, {playerEnv.Data.Temperature / 100f:0.0}C)" : " (no air)") +
                        (playerEnv == env ? ", same air" : ", OTHER air") + ", buffs:";
                bool any = false;
                if (player.BuffSystem != null && player.BuffSystem.Data != null)
                {
                    foreach (Space.Data.BuffData buff in player.BuffSystem.Data.Buffs)
                    {
                        if (buff.time == 0) continue;

                        line += " " + (buff.id < BuffNames.Length ? BuffNames[buff.id] : "#" + buff.id) + buff.Strength;
                        any = true;
                    }
                }
                if (!any) line += " none";
                Suit? suit = player.EquipmentSystem != null ? player.EquipmentSystem.CachedSuitItem : null;
                if (suit != null) line += $", suit {(suit.Isolated ? "isolated" : "resist " + suit.TemperatureResistance)}";
            }
            YourBuddyPlugin.Log.LogInfo(line);
        }

        /// <summary>
        /// The room as the buddy feels it: the player's default PilotSuit, which Player.Temperature
        /// adds to the room. docs/game-model.md#atmosphere-kills-by-the-players-rule
        /// </summary>
        private static int FeltTemperature(Environment env) => env.Data.Temperature + BuddySuitTemperatureResistance;

        /// <summary>
        /// The threat the player's atmosphere buffs set (Suffocation, Hyperoxia, Cold, Heat in
        /// Space/Player.cs), in a PilotSuit. No environment is space, and lethal.
        /// </summary>
        private static int AtmosphereThreat(Environment? env)
        {
            if (env == null) return VacuumThreat;

            int oxygen = env.Data.Oxygen;
            int temperature = FeltTemperature(env);
            int threat = 0;

            if (oxygen < 600) threat += 3 * 7;
            else if (oxygen < 1950) threat += 7;
            else if (oxygen > 5000) threat += 2 * 3;
            else if (oxygen >= 3200) threat += 3;

            if (temperature < -2000) threat += 3 * 6;
            else if (temperature < 0) threat += 2 * 6;
            else if (temperature < 1400) threat += 6;
            else if (temperature > 4000) threat += 2 * 8;
            else if (temperature > 3200) threat += 8;

            return threat;
        }

        /// <summary>
        /// The Breathless ignores objects without a Player component, so we detect the
        /// catch ourselves and then reuse the monster's own grab choreography.
        /// </summary>
        private void BreathlessCheck()
        {
            if (!YourBuddyPlugin.ConfigMortal.Value || IsDead || catchInProgress) return;
            // Shut in a closet, it is out of reach: the walls block the monster. docs/fear.md §6
            if (hideState == HideState.Hidden) return;
            // The buddy's catch is the mod's own - the game's detectors never see it - so
            // ai_disable has to stop it here. ai_notarget deliberately does not:
            // it blinds the monster to the player only. docs/reference.md
            if (AiDebug.MonsterDisabled) return;

            Breathless breathless = GameManager.Instance.Breathless;
            if (breathless == null || !breathless.gameObject.activeInHierarchy) return;

            if ((breathless.transform.position - transform.position).sqrMagnitude < 2.89f)
            {
                StartCoroutine(CatchRoutine(breathless));
            }
        }

        private System.Collections.IEnumerator CatchRoutine(Breathless breathless)
        {
            catchInProgress = true;
            YourBuddyPlugin.Log.LogWarning("[ai] The Breathless grabbed the buddy");
            hasMoveTarget = false;

            // Remember the monster's cloaked/visible state so it can be restored after the kill.
            bool visibilityWas = breathless.Visible;

            // Stop the monster from homing in on the player while it is busy grabbing the buddy.
            BreathlessAggressor aggressor = breathless.Aggressor;
            bool aggressorWasEnabled = aggressor != null && aggressor.enabled;
            // aggressorWasEnabled implies aggressor != null.
            if (aggressorWasEnabled) aggressor!.enabled = false;

            bool killed = false;
            void OnContact()
            {
                if (killed || IsDead) return;

                killed = true;
                KillFromBreathless(breathless);
            }

            try
            {
                breathless.Animator.SetVisibility(true);
                breathless.Animator.Follow(transform);
                breathless.MakeAngrySound();
                breathless.PlayMovementSound();

                for (int i = 0; i < 4 && !killed; i++) breathless.Animator.Touch(transform, OnContact);
            }
            catch (Exception ex)
            {
                YourBuddyPlugin.Log.LogWarning($"[ai] Catch sequence failed: {ex.Message}");
            }

            float waited = 0f;
            while (waited < 4f && !killed)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            if (!killed && !IsDead) KillFromBreathless(breathless);

            yield return CatchReleaseDelay;

            try
            {
                breathless.Animator.Unfollow();
                breathless.Animator.SetVisibility(visibilityWas);
            }
            catch (Exception ex)
            {
                // The Breathless may be destroyed mid-catch; the cleanup is best
                // effort, but it must never kill the coroutine (catchInProgress
                // is reset below and the buddy would freeze otherwise).
                YourBuddyPlugin.Log.LogWarning("[ai] Breathless release cleanup failed: " + ex.Message);
            }

            // Restore to what the debug override says, not to what the flag happened to
            // be when the grab started - otherwise a catch silently re-arms the monster.
            if (aggressorWasEnabled && aggressor != null && !AiDebug.MonsterHuntSuppressed) aggressor.enabled = true;

            catchInProgress = false;
        }

        private void KillFromBreathless(Breathless breathless)
        {
            Vector3 away = (transform.position - breathless.transform.position).normalized;
            if (away.sqrMagnitude < 0.001f) away = Vector3.up;

            YourBuddyPlugin.Log.LogWarning("[ai] The Breathless killed the buddy");
            Die(away * 3f + Vector3.up * 2f);
        }

        /// <summary>
        /// Kills the buddy and swaps the animated model for the player prefab's ragdoll,
        /// which BuddyCorpse then makes carryable.
        /// </summary>
        public void Die(Vector3 impulse)
        {
            if (IsDead) return;

            hands.Drop("dying");
            ForceLeaveHidingSpot("dying");
            IsDead = true;
            mode = BuddyMode.Dead;
            hasMoveTarget = false;
            waitingGate = null;
            catchInProgress = false;
            // UpdateDoorCloseBehind returns early once IsDead, so these would sit here
            // holding stale Gate references for the rest of the scene. The doors stay
            // open either way - a corpse cannot close them.
            pendingDoorCloses.Clear();

            if (ragdollObject != null)
            {
                ragdollObject.transform.parent = null;
                ragdollObject.SetActive(true);

                GameSettingsData? settings = SceneLoader.Instance != null ? SceneLoader.Instance.GameData?.Settings : null;
                float gravityScale = 9.81f * (settings?.gravityMultiplier ?? 1f);

                foreach (Rigidbody rb in ragdollObject.GetComponentsInChildren<Rigidbody>(true))
                {
                    rb.isKinematic = false;
                    rb.detectCollisions = true;
                    rb.useGravity = gravityScale > 0.01f;
                }

                foreach (Collider col in ragdollObject.GetComponentsInChildren<Collider>(true))
                {
                    col.enabled = true;
                    if (playerCharacterController != null) Physics.IgnoreCollision(playerCharacterController, col);
                }

                if (ragdollRigidbody != null) ragdollRigidbody.AddForce(impulse, ForceMode.Impulse);
                // The body can be picked up and carried, like the helper robot's.
                ragdollObject.AddComponent<BuddyCorpse>().Init(ragdollRigidbody);
            }

            if (animatedModel != null) animatedModel.SetActive(false);

            if (cc != null) cc.enabled = false;
            if (itemBlocker != null) itemBlocker.enabled = false;

            StopFootsteps();

            HideDebugVisuals();
            YourBuddyPlugin.Log.LogWarning("[ai] Buddy died");
        }

        // ------------------------------------------------------------------
        // Scene caches
        // ------------------------------------------------------------------

        private void RefreshDetectors()
        {
            if (cachedDetectors != null && Time.time < detectorsRefreshAt) return;

            cachedDetectors = [.. FindObjectsOfType<EntryDetector>()];
            detectorsRefreshAt = Time.time + 5f;
        }

        private void RefreshAirlocks()
        {
            if (cachedAirlocks != null && Time.time < airlocksRefreshAt) return;

            cachedAirlocks = [.. FindObjectsOfType<Airlock>()];
            airlocksRefreshAt = Time.time + 5f;
        }

        private void RefreshEnvironments()
        {
            if (cachedEnvironments != null && Time.time < environmentsRefreshAt) return;

            cachedEnvironments = [.. FindObjectsOfType<Environment>()];
            environmentsRefreshAt = Time.time + 5f;
        }


    }
}

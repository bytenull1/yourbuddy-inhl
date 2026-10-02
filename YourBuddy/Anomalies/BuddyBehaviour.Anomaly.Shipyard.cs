using System.Collections.Generic;
using NPC.Core.Agents;
using NPC.Core.Navigation;
using NPC.Core.World;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The Shipyard set pieces: Meat in the cryo room and BotTalk with its robot. docs/anomalies.md#meat
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        // Talking with the Shipyard's robot, with whose turn it is and the robot's line. docs/anomalies.md#bottalk
        private AssistanceBot? talkBot = null;
        /// <summary>
        /// The Shipyard a set piece put it on, and where it stood aboard before, so an undock can put it back.
        /// </summary>
        private ShipyardStation? shipyard = null;
        private Vector3? fromAboard = null;
        // Meat state, with the cryo room, its doorways, the one it faces, and whether it ran. docs/anomalies.md#meat
        private Room? cryoRoom = null;
        private readonly List<EntryDetector> cryoDoorways = [];
        private EntryDetector? caughtDoorway = null;
        private bool caughtRan = false;
        private bool botTalksNext = false;
        private float botBurstUntil = 0f;
        private float botBlipAt = 0f;
        private const float BotTalkMinDist = 12f;
        private const float BotTalkMaxDist = 45f;
        private const float BotStandMin = 1.2f;
        private const float BotStandMax = 2.2f;
        private const float BotTalkGlanceDist = 4f;
        private const float BotTalkCloseDist = 2.5f;
        private const float BotTalkWaitSeconds = 240f;
        private const float BotTalkGlanceSeconds = 1.2f;
        private const float BotBlipGap = 0.035f;
        private const string CryoRoomName = "YardCryo";
        private const float CaughtMinDist = 8f;
        private const float CaughtMaxDist = 60f;
        private const float CaughtStandMin = 2f;
        private const float CaughtStandMax = 5.5f;
        private const float CaughtStandDist = 3f;
        private const float CaughtViewInset = 0.8f;
        private const float CaughtEnterDist = 3f;
        private const float CaughtMeatAhead = 0.7f;
        private const float MessProbeAbove = 0.05f;
        private const float MessLevelTolerance = 0.05f;
        private const float CaughtWaitSeconds = 480f;
        private const float CaughtNearDist = 3f;
        private const float CaughtTurnSeconds = 0.6f;
        private const float CaughtStareSeconds = 3f;
        private const float CaughtCorneredSeconds = 120f;
        private const float CaughtGoneUnseenSeconds = 1f;
        private const float CaughtRunMin = 2f;
        private const float CaughtRunGain = 1f;

        // The Shipyard

        /// <summary>
        /// The Shipyard, with your ship docked at it, else null and why not. Each set piece then checks
        /// how far you are from where it plays.
        /// </summary>
        private static ShipyardStation? DockedShipyard(out string why)
        {
            why = "";
            ShipyardStation? station = Object.FindObjectOfType<ShipyardStation>();
            GameManager gm = GameManager.Instance;
            if (station != null && gm != null && station.Docker != null && station.Docker.DockedShip == gm.PlayerShip) return station;

            why = "it plays only at the Shipyard - dock there first";
            return null;
        }

        /// <summary>
        /// Undocked while it is on the Shipyard for a set piece. It goes back where it stood aboard, never
        /// left behind, and the set piece ends. True when it did.
        /// </summary>
        private bool EndIfUndocked()
        {
            GameManager gm = GameManager.Instance;
            if (shipyard != null && gm != null && shipyard.Docker.DockedShip == gm.PlayerShip) return false;

            if (fromAboard != null && !agent.IsAboardPlayerShip()) MoveTo(fromAboard.Value);

            EndAnomaly("you undocked");
            return true;
        }

        // Caught in the cryo room

        /// <summary>
        /// Out of your sight, it is put in the Shipyard's cryo room, bloody, back to the shut door and in view
        /// of it, over raw meat and blood it leaves there. docs/anomalies.md#meat
        /// </summary>
        private string? StartCaught(Transform you, bool seen)
        {
            if (seen) return "you are watching it";

            if (mode is not BuddyMode.Follow and not BuddyMode.Wander) return "it is staying put";

            if (suit.Suited) return "it wears a suit";

            ShipyardStation? station = DockedShipyard(out string why);
            if (station == null) return why;

            Room? cryo = CryoRoom(cryoDoorways);
            if (cryo == null) return "the Shipyard's cryo room (YardCryo) was not found";

            if (cryoDoorways.Count == 0) return "the cryo room's door was not found";

            if (!Items.Loadable(cryo.ContentParent)) return "the cryo room cannot be loaded";

            // The doorway you will come through is the one nearest you.
            EntryDetector doorway = cryoDoorways[0];
            foreach (EntryDetector d in cryoDoorways)
            {
                if (FlatDistance(you.position, d.transform.position) < FlatDistance(you.position, doorway.transform.position)) doorway = d;
            }
            // CryoRoom keeps only doorways with a door.
            Gate door = NpcDoors.DoorOf(doorway)!;
            float far = FlatDistance(you.position, door.transform.position);
            if (far < CaughtMinDist || far > CaughtMaxDist)
            {
                return $"you are {far:0.0}m from the cryo room's door - it needs you {CaughtMinDist:0}-{CaughtMaxDist:0}m away, outside that room";
            }
            foreach (EntryDetector d in cryoDoorways)
            {
                if (NpcDoors.DoorOf(d) is { Opened: true } open && PlayerView.Sees(open.transform.position + Vector3.up * 1.2f, null))
                {
                    return "you can see the cryo room's open door - look away or step out of sight of it";
                }
            }

            agent.LoadRoom(cryo, "it hides in the cryo room");
            if (!cryo.ContentEnabled) return "the cryo room will not load";

            Vector3? view = DoorwayView(doorway, cryo, door);
            if (view == null) return $"cannot tell which side of '{door.name}' the cryo room is";

            Vector3? stand = CaughtStand(doorway, cryo, view.Value);
            if (stand == null) return $"no spot in the cryo room in view of '{door.name}' and out of yours";

            string? gore = PutGoreOn();
            if (gore != null) return gore;

            // Shut, so that you are the one to open it.
            foreach (EntryDetector d in cryoDoorways)
            {
                if (NpcDoors.DoorOf(d) is not { Opened: true } shut) continue;

                shut.Close();
                NpcDoors.NoteClosedBy(shut, agent);
            }
            StartHold(AnomalyKind.Meat, CaughtWaitSeconds);
            fromAboard = agent.IsAboardPlayerShip() ? agent.FloorUnderNpc() : null;
            MoveTo(stand.Value);
            shipyard = station;
            cryoRoom = cryo;
            caughtDoorway = doorway;
            caughtRan = false;

            Vector3 away = stand.Value - view.Value;
            away.y = 0f;
            away.Normalize();
            Vector3 feet = stand.Value;
            if (NavProbe.TryFloorHeight(feet + Vector3.up * MessProbeAbove, out float feetY)) feet.y = feetY;
            Vector3 meat = MeatSpot(feet, away);
            anomalyFace = meat;
            anomalyStepAt = Time.time + 1f;
            LeaveTheMess(cryo.ContentParent, feet, meat, away);
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} crouches over something in the cryo room, " +
                                        $"{FlatDistance(stand.Value, view.Value):0.0}m in from '{door.name}', {FlatDistance(stand.Value, you.position):0.0}m from you");
            return null;
        }

        /// <summary>
        /// The Shipyard's cryo room, and into `doorways` the doorways with a door that join it. They come from
        /// the doorway detectors, as NPC.Core loads rooms by them, since the room's own door list can leave one out.
        /// </summary>
        private static Room? CryoRoom(List<EntryDetector> doorways)
        {
            doorways.Clear();
            Room? cryo = null;
            foreach (Room room in Object.FindObjectsOfType<Room>(true))
            {
                if (room != null && room.name == CryoRoomName) cryo = room;
            }
            if (cryo == null) return null;

            foreach (EntryDetector detector in NpcDoors.Detectors)
            {
                if (detector == null || NpcDoors.DoorOf(detector) == null) continue;

                NpcDoors.RoomsOf(detector, out Room? inner, out Room? outer);
                if (inner == cryo || outer == cryo) doorways.Add(detector);
            }
            return cryo;
        }

        /// <summary>
        /// On the cryo room's side of this doorway, by the test the game uses for you.
        /// </summary>
        private static bool InCryo(EntryDetector doorway, Room cryo, Vector3 point)
        {
            NpcDoors.RoomsOf(doorway, out Room? inner, out _);
            return NpcDoors.TryInnerSide(doorway, point, out bool isInner) && isInner == (inner == cryo);
        }

        /// <summary>
        /// The floor CaughtViewInset inside the doorway, on the cryo room's side, where you first see in.
        /// </summary>
        private static Vector3? DoorwayView(EntryDetector doorway, Room cryo, Gate door)
        {
            Vector3 middle = door.transform.position;
            foreach (Vector3 axis in new[] { door.transform.forward, -door.transform.forward, door.transform.right, -door.transform.right })
            {
                Vector3 flat = new(axis.x, 0f, axis.z);
                if (flat.sqrMagnitude < 0.01f) continue;

                Vector3 at = middle + flat.normalized * CaughtViewInset;
                if (!InCryo(doorway, cryo, at) || !NavProbe.TryFloorHeight(at + Vector3.up * 0.5f, out float floorY)) continue;

                at.y = floorY;
                return at;
            }
            return null;
        }

        /// <summary>
        /// A ground node in clear view of the doorway, on the cryo room's side, CaughtStandMin..Max in and
        /// nearest CaughtStandDist, out of your sight. NPC.Core gives a room the nodes nearest its furniture,
        /// which can lie in the hallway, so the side and the clear line keep it in the room you open.
        /// </summary>
        private static Vector3? CaughtStand(EntryDetector doorway, Room cryo, Vector3 view)
        {
            NpcVessels.FloorOwner(view, out string? owner, out _);
            Vector3? best = null;
            float bestOff = float.MaxValue;
            foreach (Vector3 at in GroundNodes(view, owner, CaughtStandMin, CaughtStandMax, 1f))
            {
                float off = Mathf.Abs(FlatDistance(at, view) - CaughtStandDist);
                if (off >= bestOff) continue;

                if (!InCryo(doorway, cryo, at) || !NavProbe.WalkLos(view, at, 0.5f)) continue;

                if (PlayerView.Sees(at + Vector3.up * 1.1f, null)) continue;

                best = at;
                bestOff = off;
            }
            return best;
        }

        /// <summary>
        /// The meat in front of it, blood under and around the meat and at its feet.
        /// </summary>
        private void LeaveTheMess(Transform room, Vector3 feet, Vector3 meat, Vector3 away)
        {
            int stains = 0;
            float yaw = Quaternion.LookRotation(away).eulerAngles.y;
            if (Stain(meat, 1.6f)) stains++;
            if (Stain(feet, 1f)) stains++;
            for (int i = 0; i < 3; i++)
            {
                Vector3 at = meat + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(0.4f, 1f);
                if (Stain(at, Random.Range(0.8f, 1.3f))) stains++;
            }
            bool meatLeft = AnomalyProps.SpawnMeat(room, meat, yaw + Random.Range(-40f, 40f)) != null;
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} leaves {(meatLeft ? "raw meat and " : "")}{stains} blood stain(s) in the cryo room");

            bool Stain(Vector3 at, float scale) =>
                FloorNear(at, feet.y, out at) && AnomalyProps.SpawnBlood(room, at, Random.Range(0f, 360f), scale) != null;
        }

        /// <summary>
        /// The meat CaughtMeatAhead beyond its feet, or nearer, on bare floor it could walk to; at its feet if
        /// furniture fills both.
        /// </summary>
        private static Vector3 MeatSpot(Vector3 feet, Vector3 away)
        {
            foreach (float ahead in new[] { CaughtMeatAhead, CaughtMeatAhead * 0.6f })
            {
                if (FloorNear(feet + away * ahead, feet.y, out Vector3 at) && NavProbe.WalkLos(feet, at, 0.1f)) return at;
            }
            return feet + away * 0.3f;
        }

        /// <summary>
        /// The floor at `at`, level with `floorY`. Probed from just above it, so a crate top or a chair seat
        /// is never taken for the floor. docs/anomalies.md#the-mess-and-the-meat-model
        /// </summary>
        private static bool FloorNear(Vector3 at, float floorY, out Vector3 floor)
        {
            floor = new Vector3(at.x, floorY, at.z);
            if (!NavProbe.TryFloorHeight(floor + Vector3.up * MessProbeAbove, out float y) || Mathf.Abs(y - floorY) > MessLevelTolerance) return false;

            floor.y = y;
            return true;
        }

        /// <summary>
        /// Feeding sounds until you open a door or come near. Then it stares at you until you step in, and runs
        /// deeper into the room. Gone once you have seen it and look away; it comes back clean.
        /// </summary>
        private void UpdateCaught(float now, float dist, Transform you)
        {
            if (EndIfUndocked()) return;

            if (cryoRoom == null || caughtDoorway == null)
            {
                EndAnomaly("the cryo room went away");
                return;
            }
            switch (anomalyStep)
            {
                case 0:
                    bool doorOpen = cryoDoorways.Exists(d => d != null && NpcDoors.DoorOf(d) is { Opened: true });
                    if (doorOpen || anomalySeen || dist < CaughtNearDist)
                    {
                        anomalyStep = 1;
                        anomalyStepAt = now + CaughtTurnSeconds;
                        anomalyUntil = now + CaughtCorneredSeconds;
                        anomalySeenFor = 0f;
                        ScareSounds.Play(ScareSound.Creature, agent.GroundPos(1.2f));
                        Startle(30);
                        YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} is caught ({(doorOpen ? "you opened the door" : anomalySeen ? "you saw it" : "you came near")}, " +
                                                    $"{dist:0.0}m, {(anomalySeen ? "in your view" : "not in your view yet")})");
                        return;
                    }
                    if (now >= anomalyUntil)
                    {
                        EndAnomaly("you never came");
                        return;
                    }
                    if (now < anomalyStepAt) return;

                    ScareSounds.Play(ScareSound.Gore, agent.GroundPos(0.6f), transform, out float seconds);
                    anomalyStepAt = now + seconds + Random.Range(2f, 4f);
                    break;
                case 1:
                    // It stares at you until you step in, or have watched it CaughtStareSeconds.
                    if (now < anomalyStepAt) return;

                    bool youIn = dist < CaughtNearDist || (InCryo(caughtDoorway, cryoRoom, you.position) &&
                                                           FlatDistance(you.position, caughtDoorway.transform.position) < CaughtEnterDist);
                    bool staredDown = anomalySeenFor >= CaughtStareSeconds;
                    if (!youIn && !staredDown && now < anomalyUntil) return;

                    if (!youIn && !anomalySeen)
                    {
                        CaughtGone(now, "you never came in");
                        return;
                    }
                    Room cryo = cryoRoom;
                    EntryDetector doorway = caughtDoorway;
                    bool InRoom(Vector3 node) => InCryo(doorway, cryo, node);
                    string? stuck = RunFrom(now, you.position, CaughtRunMin, CaughtRunGain, InRoom);
                    if (stuck != null) stuck = RunFrom(now, you.position, 1f, 0f, InRoom);

                    anomalyUntil = now + CaughtCorneredSeconds;
                    if (stuck == null)
                    {
                        anomalyStep = 2;
                        caughtRan = true;
                        YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} runs deeper into the cryo room - " +
                                                    (youIn ? "you stepped in" : staredDown ? $"you watched it {anomalySeenFor:0.0}s" : "out of time"));
                        return;
                    }
                    anomalyStep = 3;
                    YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} is cornered in the cryo room - {stuck}");
                    break;
                case 3:
                    // Never gone before you have seen it, unless it ran where you could not.
                    bool lookedAway = !anomalySeen && now - anomalyUnseenSince >= CaughtGoneUnseenSeconds;
                    if (lookedAway && (anomalySeenFor > 0f || caughtRan))
                    {
                        CaughtGone(now, anomalySeenFor > 0f ? "you looked away" : "it ran out of your sight");
                        return;
                    }
                    if (now < anomalyUntil) return;

                    if (anomalySeen) EndAnomaly("you never looked away");
                    else CaughtGone(now, "out of time");
                    break;
                case 4:
                    UpdateVanished(now, you);
                    break;
            }
        }

        private void CaughtGone(float now, string why)
        {
            anomalyStep = 4;
            anomalyUntil = now + Random.Range(VanishMinSeconds, VanishMaxSeconds);
            anomalyGiveUpAt = anomalyUntil + 60f;
            Disappear();
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} is gone from the cryo room - {why} (seen {anomalySeenFor:0.0}s)");
        }

        /// <summary>
        /// There, or the run gave up. It waits, cornered, for you to look away.
        /// </summary>
        private void StopCaughtRun()
        {
            StopAnomalyWalk();
            anomalyStep = 3;
        }

        // The Shipyard's robot

        /// <summary>
        /// Out of your sight, it is put at the Shipyard robot's desk, on the side you will come from, while
        /// you are on the ship side of the docked station. docs/anomalies.md#bottalk
        /// </summary>
        private string? StartBotTalk(Transform you, bool seen)
        {
            if (seen) return "you are watching it";

            if (mode is not BuddyMode.Follow and not BuddyMode.Wander) return "it is staying put";

            ShipyardStation? station = DockedShipyard(out string why);
            if (station == null) return why;

            AssistanceBot? bot = station.Bot;
            // Off with its hallway while you are aboard, so it is loaded below, as a sell run loads its station.
            // docs/items.md#4-selling-trash-boxes
            if (bot == null || !Items.Loadable(bot)) return "the Shipyard's robot is not here";

            string? botOwner = NpcVessels.OwnerOfTransform(bot.transform);

            Vector3 botPos = bot.transform.position;
            float far = FlatDistance(you.position, botPos);
            if (far < BotTalkMinDist || far > BotTalkMaxDist)
            {
                return $"you are {far:0.0}m from the Shipyard's robot - it needs you {BotTalkMinDist:0}-{BotTalkMaxDist:0}m away, on the ship side";
            }

            Vector3? stand = BotStand(botPos, botOwner, you.position);
            if (stand == null) return "no node by the robot out of your sight";

            if (!LoadBotRoom(bot)) return "the robot's room will not load";

            StartHold(AnomalyKind.BotTalk, BotTalkWaitSeconds);
            fromAboard = agent.IsAboardPlayerShip() ? agent.FloorUnderNpc() : null;
            MoveTo(stand.Value);
            talkBot = bot;
            shipyard = station;
            botTalksNext = Random.value < 0.5f;
            botBurstUntil = 0f;
            anomalyFace = botPos;
            anomalyStepAt = Time.time + 0.5f;
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} stands at the Shipyard's robot, {FlatDistance(stand.Value, you.position):0.0}m from you");
            return null;
        }

        /// <summary>
        /// The robot's room switched on if the game has it off. False when it cannot be.
        /// </summary>
        private bool LoadBotRoom(AssistanceBot bot)
        {
            if (bot.gameObject.activeInHierarchy) return true;

            if (!Items.Loadable(bot)) return false;

            agent.LoadRoom(bot.GetComponentInParent<Room>(true), "it talks with the robot");
            return bot.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// The node BotStandMin..Max from the robot that faces where you are, out of your sight.
        /// </summary>
        private static Vector3? BotStand(Vector3 botPos, string? owner, Vector3 yourPos)
        {
            Vector3 toYou = yourPos - botPos;
            toYou.y = 0f;
            toYou.Normalize();
            Vector3? best = null;
            float bestDot = -2f;
            foreach (Vector3 node in GroundNodes(botPos, owner, BotStandMin, BotStandMax, 1.5f))
            {
                Vector3 dir = node - botPos;
                dir.y = 0f;
                float dot = Vector3.Dot(dir.normalized, toYou);
                if (dot <= bestDot || !Unseen(node)) continue;

                best = node;
                bestDot = dot;
            }
            return best;
        }

        /// <summary>
        /// Turns of a line of its blips, then the robot's answer in its own talk sound, its eye moving.
        /// The robot's sound plays as fast as its typewriter would, about once a frame.
        /// </summary>
        private void Converse(float now, AssistanceBot bot)
        {
            bot.Animator.TargetLookPosition = agent.GroundPos(1.5f);
            if (now < botBurstUntil)
            {
                if (now < botBlipAt) return;

                bot.PlayTalkSound(0);
                botBlipAt = now + BotBlipGap;
                return;
            }
            bot.Animator.Eye.scaleAnimation = false;
            if (now < anomalyStepAt) return;

            botTalksNext = !botTalksNext;
            if (botTalksNext)
            {
                botBurstUntil = now + Random.Range(0.8f, 2.4f);
                botBlipAt = now;
                bot.Animator.Eye.scaleAnimation = true;
                anomalyStepAt = botBurstUntil + Random.Range(0.4f, 1.2f);
                return;
            }
            int blips = Random.Range(5, 15);
            ScareSounds.Babble(transform, blips);
            anomalyStepAt = now + blips * 0.1f + Random.Range(0.4f, 1.2f);
        }

        /// <summary>
        /// Talking until you come near, then a look round at you, then a run away from you. Done once it
        /// is out of your sight. The run itself is WalkAnomalyLeg's.
        /// </summary>
        private void UpdateBotTalk(float now, float dist, Transform you)
        {
            if (EndIfUndocked()) return;

            AssistanceBot? bot = talkBot;
            if (bot == null || !LoadBotRoom(bot))
            {
                EndAnomaly("the robot is gone");
                return;
            }
            switch (anomalyStep)
            {
                case 0:
                    if (dist < BotTalkCloseDist || (anomalySeen && dist < BotTalkGlanceDist))
                    {
                        anomalyStep = 1;
                        anomalyStepAt = now + BotTalkGlanceSeconds;
                        botBurstUntil = 0f;
                        bot.Animator.Eye.scaleAnimation = false;
                        YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} sees you coming ({dist:0.0}m) and looks round");
                        return;
                    }
                    if (now >= anomalyUntil)
                    {
                        EndAnomaly("you never came");
                        return;
                    }
                    Converse(now, bot);
                    break;
                case 1:
                    // The robot looks at you too.
                    bot.Animator.TargetLookPosition = you.position;
                    if (now < anomalyStepAt) break;

                    string? stuck = RunFrom(now, you.position);
                    if (stuck != null) EndAnomaly(stuck);
                    else anomalyStep = 2;
                    break;
                case 3:
                    if ((!anomalySeen && now - anomalyUnseenSince >= RunOffUnseenSeconds) || now >= anomalyUntil) EndAnomaly("ran off");
                    break;
            }
        }

        /// <summary>
        /// A run to a node further from you, away from you, out of your sight if it can; WalkAnomalyLeg
        /// runs it. Null when it is on its way, else why not. `within` keeps the run in one place (Meat).
        /// </summary>
        private string? RunFrom(float now, Vector3 yourPos, float minDist = RunOffMinDist, float gain = RunOffGain,
            System.Predicate<Vector3>? within = null)
        {
            Vector3 here = transform.position;
            Vector3 toYou = yourPos - here;
            toYou.y = 0f;
            float yours = toYou.magnitude;
            RunOffNodes.Clear();
            foreach (Vector3 node in GroundNodes(here, agent.CurrentOwner, minDist, RunOffMaxDist, 3f))
            {
                if (FlatDistance(node, yourPos) < yours + gain) continue;

                Vector3 away = node - here;
                away.y = 0f;
                if (Vector3.Dot(away, toYou) >= 0f || (within != null && !within(node))) continue;

                RunOffNodes.Add(node);
            }
            Vector3 from = agent.FloorUnderNpc();
            // Out of your sight first, so it is gone once there, else anywhere away from you.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < RunOffNodes.Count; i++)
                {
                    int pick = Random.Range(i, RunOffNodes.Count);
                    (RunOffNodes[i], RunOffNodes[pick]) = (RunOffNodes[pick], RunOffNodes[i]);
                    Vector3 node = RunOffNodes[i];
                    if (pass == 0 && PlayerView.Sees(node + Vector3.up * 1.1f, null)) continue;

                    NavPath? plan = NavGraph.FindPath(from, node);
                    if (plan is not { Count: > 0 }) continue;

                    agent.CommitPlan(plan.Value, false);
                    anomalyWalking = true;
                    anomalyStand = node;
                    anomalyArrival = NpcAgent.ReachStandArrival;
                    anomalyGiveUpAt = now + RunOffLegSeconds;
                    YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} runs off {FlatDistance(node, here):0.0}m, " +
                                                $"to {FlatDistance(node, yourPos):0.0}m from you" + (pass == 0 ? ", out of your sight" : ""));
                    return null;
                }
            }
            return $"nowhere to run ({RunOffNodes.Count} node(s) away from you)";
        }

        /// <summary>
        /// There, or the run gave up. It stands still and waits to be out of your sight.
        /// </summary>
        private void StopBloodyRun()
        {
            StopAnomalyWalk();
            anomalyStep = 2;
        }

        private void StopAnomalyWalk()
        {
            anomalyWalking = false;
            agent.DropPlan();
            agent.ClearMoveTarget();
        }

        /// <summary>
        /// The robot as the game leaves it when you walk away, eye still and looking ahead.
        /// </summary>
        private void ReleaseBot()
        {
            if (talkBot != null)
            {
                talkBot.Animator.Eye.scaleAnimation = false;
                talkBot.Animator.TargetLookPosition = talkBot.Animator.Forward;
            }
            talkBot = null;
        }
    }
}

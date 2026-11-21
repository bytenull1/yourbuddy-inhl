using System.Collections.Generic;
using NPC.Core;
using NPC.Core.Agents;
using NPC.Core.Interaction;
using NPC.Core.Navigation;
using NPC.Core.World;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The anomalies one buddy acts out, strange or frightening moments that make you doubt it is
    /// the friend you woke up with. AnomalyDirector decides when; this does them. docs/anomalies.md
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        private AnomalyKind? anomaly = null;
        private int anomalyStep = 0;
        private float anomalyUntil = 0f;
        private float anomalyStepAt = 0f;
        private float anomalyGiveUpAt = 0f;
        private string anomalyLast = "none yet";

        // Being watched, sampled at SightSampleSeconds.
        private bool anomalySeen = false;
        private float anomalySeenFor = 0f;
        private float anomalyUnseenSince = 0f;
        private float anomalySightAt = 0f;
        private bool anomalyStartled = false;
        /// <summary>
        /// You saw what it acted out. One nobody saw may come again. docs/anomalies.md#once-per-save
        /// </summary>
        private bool anomalyWitnessed = false;
        private const float SightSampleSeconds = 0.1f;
        private const int StalkerSounds = 3;
        private const float NoisesGapMin = 3f;
        private const float NoisesGapMax = 5f;
        private const float NoisesMinDist = 1.2f;
        private const float NoisesMaxDist = 7f;
        private const float ComeMaxDist = 30f;
        private const float ComeSeconds = 60f;
        private const float WhisperDist = 4f;
        /// <summary>Noises' sounds over their own volume. docs/anomalies.md#noises</summary>
        private const float NoisesVolume = 1.25f;
        /// <summary>Whisper or Noises: still walking up to you. docs/anomalies.md#coming-up-to-you</summary>
        private bool anomalyComing = false;

        // Bloody keeps the texture it wore before.
        private Texture? goreWearing = null;
        private Texture2D? goreTexture = null;

        // Walking to a window or a wall, then staring at it.
        private bool anomalyWalking = false;
        private Vector3 anomalyStand;
        private Vector3 anomalyFace;
        private float anomalyArrival = NpcAgent.ReachStandArrival;

        // The wrong name in the talk window's title, until it is next opened.
        private string? wrongTitle = null;
        private float wrongTitleUntil = 0f;
        private static readonly List<Vector3> RunOffNodes = [];

        /// <summary>
        /// The talk window's conversation with this buddy, set by SpawnBuddy.
        /// </summary>
        internal BuddyConversation? Conversation { get; set; }

        /// <summary>
        /// The animated body a double copies, set by SpawnBuddy. docs/anomalies.md#sleeper
        /// </summary>
        internal GameObject? Model { get; set; }

        // Tuning. docs/reference.md#1-tuning-constants
        private const float BloodySeconds = 150f;
        private const float BloodySeenRate = 15f;
        private const float BloodyUnseenSeconds = 5f;
        private const float BloodyRunRetrySeconds = 6f;
        private const float WrongNameSeconds = 900f;
        private const float RunOffMinDist = 6f;
        private const float RunOffMaxDist = 20f;
        private const float RunOffGain = 4f;
        private const float RunOffLegSeconds = 20f;
        private const float RunOffUnseenSeconds = 3f;

        /// <summary>
        /// One is running and holds the buddy, its looks or its schedule.
        /// </summary>
        internal bool AnomalyRunning => anomaly.HasValue;

        /// <summary>
        /// Gone from sight, the scanner and the monster's reach. docs/anomalies.md#vanish
        /// </summary>
        internal bool Vanished => vanished;

        /// <summary>
        /// It does not answer while vanished, frozen in a stare at you, or stalking you.
        /// </summary>
        internal bool IgnoresYou => vanished || Anomalies.Has(anomaly, AnomalyTraits.IgnoresYou);

        /// <summary>
        /// It changes only its looks, or runs a set piece elsewhere, so the decider, orders and fear go on around it.
        /// docs/anomalies.md#2-the-anomalies
        /// </summary>
        private bool AnomalyInBackground => Anomalies.Has(anomaly, AnomalyTraits.Background);

        /// <summary>
        /// The title the talk window shows instead of its name, once. docs/anomalies.md#wrongname
        /// </summary>
        internal string? TitleOverride =>
            wrongTitle != null && Time.time < wrongTitleUntil && !(NpcPlayer.Pilot is { } player && AirStopsAnomaly(player)) ? wrongTitle : null;

        // Starting

        /// <summary>
        /// Why this buddy cannot act out anything now, or null.
        /// </summary>
        internal string? AnomalyReady()
        {
            if (IsDead) return "dead";

            if (Asleep || !gameObject.activeInHierarchy) return "not awake here";

            if (anomaly.HasValue) return "already acting one out";

            if (agent.IsOutside) return "outside";

            if (agent.IsBeingCaught) return "being caught";

            if (InDialog) return "being talked to";

            if (Hiding) return "hiding";
            // docs/invariants.md#fear-owns-the-buddy
            if (fearState != FearState.Calm || mode == BuddyMode.Flee) return "afraid";

            if (reachTask != null) return "busy " + DescribeReachTask();

            if (mode == BuddyMode.Route) return "walking to a node you sent it to";

            return suit.RunActive ? "on an airlock run" : null;
        }

        /// <summary>
        /// Starts one now, or says why not. The director has already judged the severity; buddy_anomaly
        /// skips that and the chance. docs/anomalies.md
        /// </summary>
        internal string? TryStartAnomaly(AnomalyKind kind)
        {
            // A buddy left on a station is parked, and only Move wakes it. docs/anomalies.md#move
            string? blocker = kind == AnomalyKind.Move ? MoveReady() : AnomalyReady();
            if (blocker != null) return blocker;

            Player? player = NpcPlayer.Pilot;
            if (player == null || player.Controller == null) return "no player";

            if (NpcAgent.IsPlayerInSpace(player)) return "you are outside";

            if (AirStopsAnomaly(player, kind)) return "the air is bad";

            Transform you = player.Controller.CachedTransform;
            Vector3 feet = agent.FloorUnderNpc();
            Vector3 toYou = you.position - transform.position;
            toYou.y = 0f;
            float dist = toYou.magnitude;
            bool seen = PlayerView.SeesBody(transform, feet);
            NpcVessels.FloorOwner(you.position, out string? yourOwner, out _);
            bool sameVessel = yourOwner == null || agent.CurrentOwner == null || yourOwner == agent.CurrentOwner;
            bool withYou = sameVessel || DockedTogether(yourOwner, agent.CurrentOwner);

            blocker = kind switch
            {
                AnomalyKind.ClosetAmbush => !seen && dist >= 4f ? StartAmbush() : "you would see it climb in",
                AnomalyKind.Whisper => StartWhisper(dist, withYou),
                AnomalyKind.FakeCommand => StartFakeCommand(),
                AnomalyKind.WrongName => StartWrongName(),
                AnomalyKind.Vanish => !seen && dist >= 6f ? StartVanish(kind, Random.Range(VanishMinSeconds, VanishMaxSeconds)) : "you could see it go",
                AnomalyKind.Noises => StartNoises(seen, dist, withYou),
                AnomalyKind.WindowStare => StartWindowStare(),
                AnomalyKind.WallStare => StartWallStare(),
                AnomalyKind.BotTalk => StartBotTalk(you, seen),
                AnomalyKind.Bloody => StartBloody(kind, seen, dist),
                AnomalyKind.ShutDoors => StartShutDoors(you),
                AnomalyKind.Statue => StartStatue(kind, dist, withYou),
                AnomalyKind.Meat => StartCaught(you, seen),
                AnomalyKind.Pipe => !seen ? StartPipe(dist, withYou) : "you are watching it",
                AnomalyKind.Move => StartMove(you),
                AnomalyKind.Smile or AnomalyKind.UnderTheSuit => StartFlicker(kind, withYou),
                AnomalyKind.Sleeper => StartSleeper(you, dist, sameVessel),
                AnomalyKind.Stalker => !seen ? StartStalker(dist, withYou) : "you are watching it",
                AnomalyKind.BehindYou => !seen && dist >= 6f ? StartVanish(kind, Random.Range(20f, 45f)) : "you could see it go",
                _ => "unknown",
            };
            if (blocker != null) return blocker;

            // It is somewhere else now.
            if (kind == AnomalyKind.Move) dist = FlatDistance(you.position, transform.position);

            AnomalyDirector.Began(this, kind);
            AnomalyInfo info = Anomalies.Info(kind);
            anomalyLast = info.Name;
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name}: {info.Name} ({info.Severity.ToString().ToLowerInvariant()}, " +
                                        $"{dist:0.0}m from you{(seen ? ", in your view" : ", out of sight")})");
            return null;
        }

        /// <summary>
        /// Bad air where you or it stands. Move leaves its old room's air behind. docs/invariants.md#no-anomaly-in-bad-air
        /// </summary>
        private bool AirStopsAnomaly(Player player, AnomalyKind? kind = null)
        {
            if (!NpcAgent.IsPlayerInSpace(player) && lifeSupport.Bad(player.CurrentEnvironment)) return true;

            return kind != AnomalyKind.Move && lifeSupport.Bad(agent.Air);
        }

        /// <summary>
        /// Your ship docked at the station the other one stands on: the buddy can walk to you through the hatch.
        /// docs/anomalies.md#2-the-anomalies
        /// </summary>
        private static bool DockedTogether(string? yours, string? its)
        {
            GameManager gm = GameManager.Instance;
            string? docked = gm != null && gm.PlayerShip != null ? gm.PlayerShip.Autopilot.DockedStation : null;
            if (string.IsNullOrEmpty(docked)) return false;

            return (yours == NavGraph.ShipOwner && its == docked) || (yours == docked && its == NavGraph.ShipOwner);
        }

        /// <summary>
        /// The shared bookkeeping for starting one. `keepPlan` leaves a walk already committed alone.
        /// </summary>
        private string? StartHold(AnomalyKind kind, float seconds, bool keepPlan = false)
        {
            anomaly = kind;
            anomalyStep = 0;
            anomalyUntil = Time.time + seconds;
            anomalyStepAt = Time.time;
            anomalySeen = false;
            anomalySeenFor = 0f;
            anomalyUnseenSince = Time.time;
            anomalyStartled = false;
            anomalyWitnessed = false;
            if (keepPlan) return null;

            agent.ClearMoveTarget();
            agent.ReleasePlan();
            return null;
        }

        private string? StartAmbush() => StartAmbushHide() ?? StartHold(AnomalyKind.ClosetAmbush, AmbushMaxSeconds + 30f, keepPlan: true);

        private string? StartFakeCommand()
        {
            if (Conversation == null) return "it has no talk window";

            if (NpcInteraction.IsOpenOn(Conversation)) return "the talk window is open on it";

            if (AnomalyLines.Order(AnomalyDirector.Ceiling) is not { } order) return "every order it could be told was told already";

            (string said, string reply) = order;
            NpcInteraction.AddLine(Conversation, said, true);
            NpcInteraction.AddLine(Conversation, reply, false);
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name}'s talk log now reads '$ {said}' / '> {reply}'");
            return null;
        }

        private string? StartWrongName()
        {
            if (TitleOverride != null) return "the wrong name is already waiting";

            if (Conversation != null && NpcInteraction.IsOpenOn(Conversation)) return "the talk window is open on it";

            wrongTitle = AnomalyLines.WrongName(Name);
            if (wrongTitle == null) return "every wrong name was used already";

            wrongTitleUntil = Time.time + WrongNameSeconds;
            return null;
        }

        private string? StartVanish(AnomalyKind kind, float seconds)
        {
            StartHold(kind, seconds);
            anomalyGiveUpAt = anomalyUntil + 60f;
            Disappear();
            return null;
        }

        private string? StartNoises(bool seen, float dist, bool withYou)
        {
            if (!withYou) return "you are on another vessel, not docked to its";

            if (seen) return "you are watching it";

            if (dist >= NoisesMinDist && dist <= NoisesMaxDist && PlayerView.IsBehind(agent.GroundPos(1.1f)))
            {
                StartHold(AnomalyKind.Noises, 12f);
                anomalyStepAt = Time.time + 0.4f;
                return null;
            }
            return StartComing(AnomalyKind.Noises, dist);
        }

        private string? StartWhisper(float dist, bool withYou)
        {
            if (!withYou) return "you are on another vessel, not docked to its";

            if (dist > WhisperDist) return StartComing(AnomalyKind.Whisper, dist);

            return SpeakNew(AnomalyLines.Spoken(AnomalyDirector.Ceiling));
        }

        /// <summary>
        /// Too far, or not where it needs to be: it comes up to you as a Follow does first.
        /// docs/anomalies.md#coming-up-to-you
        /// </summary>
        private string? StartComing(AnomalyKind kind, float dist)
        {
            if (dist > ComeMaxDist) return $"it is {dist:0.0}m from you";

            if (mode is not BuddyMode.Follow and not BuddyMode.Wander) return "it is staying put";

            if (mode == BuddyMode.Wander) SetMode(BuddyMode.Follow);

            StartHold(kind, ComeSeconds, keepPlan: true);
            anomalyComing = true;
            return null;
        }

        /// <summary>
        /// While it comes: true when it gave up or `wait` says why it is not there yet (logged, throttled).
        /// </summary>
        private bool StillComing(float now, string? wait)
        {
            if (now >= anomalyUntil)
            {
                EndAnomaly("it never got to you");
                return true;
            }
            if (wait == null) return false;

            if (NpcLog.Level >= 2 && now >= anomalyTraceAt)
            {
                anomalyTraceAt = now + 15f;
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} is coming up to you - {wait}");
            }
            return true;
        }

        /// <summary>
        /// Close enough, it says its line, whether or not you see it.
        /// </summary>
        private void UpdateWhisper(float now, float dist)
        {
            if (StillComing(now, dist > WhisperDist ? $"it is {dist:0.0}m from you" : null)) return;

            // Heard is as good as seen. docs/anomalies.md#once-per-save
            anomalyWitnessed = true;
            EndAnomaly(SpeakNew(AnomalyLines.Spoken(AnomalyDirector.Ceiling)) ?? "said");
        }

        private string? StartBloody(AnomalyKind kind, bool seen, float dist)
        {
            if (seen) return "you are watching it";

            if (dist < 3f) return "you are too close";

            string? gore = PutGoreOn();
            if (gore != null) return gore;

            StartHold(kind, BloodySeconds);
            return null;
        }

        private string? PutGoreOn()
        {
            if (suit.Suited) return "it wears a suit";

            goreWearing = BuddyGore.Apply(transform, Number, out goreTexture);
            return goreWearing == null ? "its body takes no skin" : null;
        }

        private void TakeGoreOff()
        {
            if (goreTexture == null) return;

            // A suit put on since drew over it, and taking it off restored the original.
            if (!suit.Suited && BuddySkin.IsApplied(transform, goreTexture)) BuddyGore.Remove(transform, goreWearing);

            goreTexture = null;
            goreWearing = null;
        }

        private string? StartStatue(AnomalyKind kind, float dist, bool withYou)
        {
            if (!withYou) return "you are on another vessel, not docked to its";

            if (dist < 3f || dist > 30f) return $"it is {dist:0.0}m from you";

            if (mode is not BuddyMode.Follow and not BuddyMode.Wander) return "it is staying put";

            if (mode == BuddyMode.Wander) SetMode(BuddyMode.Follow);

            StartHold(kind, Random.Range(50f, 90f));
            return null;
        }

        private string? StartStalker(float dist, bool withYou)
        {
            string? statue = StartStatue(AnomalyKind.Stalker, dist, withYou);
            if (statue != null) return statue;

            string? gore = PutGoreOn();
            if (gore == null)
            {
                // Your door codes do not stop it. docs/anomalies.md#2-the-anomalies
                agent.KnowsEveryCode = true;
                return null;
            }
            anomaly = null;
            return gore;
        }

        // Every frame

        private void Update()
        {
            TickLaterLine();
            if (!anomaly.HasValue) return;

            using NpcRegistry.ActingScope _ = NpcRegistry.Acting(agent);
            UpdateAnomaly();
        }

        private void UpdateAnomaly()
        {
            if (IsDead)
            {
                EndAnomaly("it died");
                return;
            }
            Player? player = NpcPlayer.Pilot;
            if (player == null || player.Controller == null) return;

            Transform you = player.Controller.CachedTransform;
            if (AirStopsAnomaly(player, anomaly))
            {
                // Gone, it comes back out of your sight if it can.
                if (vanished && HiddenSpotNear(you) is { } spot) MoveTo(spot);
                EndAnomaly("the air is bad");
                return;
            }
            float now = Time.time;
            if (now >= anomalySightAt)
            {
                anomalySightAt = now + SightSampleSeconds;
                bool seen = !vanished && PlayerView.SeesBody(transform, agent.FloorUnderNpc());
                if (seen) anomalySeenFor += SightSampleSeconds;
                // A flicker is witnessed when it flashes, a sleeper when it is found.
                if (seen && !Anomalies.Has(anomaly, AnomalyTraits.OwnCue) && !anomalyComing) anomalyWitnessed = true;
                // Watched, the blood's time runs out faster, as if it wanted it gone before you looked
                // too closely. docs/anomalies.md#bloody
                if (seen && anomaly == AnomalyKind.Bloody) anomalyUntil -= SightSampleSeconds * (BloodySeenRate - 1f);
                else if (anomalySeen) anomalyUnseenSince = now;

                anomalySeen = seen;
            }
            float dist = FlatDistance(you.position, transform.position);

            switch (anomaly)
            {
                case AnomalyKind.ShutDoors:
                    UpdateShutDoors(now);
                    break;
                case AnomalyKind.ClosetAmbush:
                    // Fear took the closet over, or the hide ended some other way. docs/invariants.md#fear-owns-the-buddy
                    if (!hideAmbush) EndAnomaly(Hiding ? "it is hiding from the Breathless now" : "out of the closet");
                    break;
                case AnomalyKind.Whisper:
                    UpdateWhisper(now, dist);
                    break;
                case AnomalyKind.Noises:
                    UpdateNoises(now, dist);
                    break;
                case AnomalyKind.WindowStare:
                case AnomalyKind.WallStare:
                    if (!anomalyWalking && now >= anomalyUntil) EndAnomaly("done staring");
                    break;
                case AnomalyKind.BotTalk:
                    UpdateBotTalk(now, dist, you);
                    break;
                case AnomalyKind.Meat:
                    UpdateCaught(now, dist, you);
                    break;
                case AnomalyKind.Bloody:
                    UpdateBloody(now, you.position);
                    break;
                case AnomalyKind.Statue:
                    if (anomalySeen && dist < 2.5f) Startle(15);
                    if (now >= anomalyUntil && !anomalySeen) EndAnomaly("done");
                    break;
                case AnomalyKind.Stalker:
                    UpdateStalker(now, dist);
                    break;
                case AnomalyKind.Pipe:
                    UpdatePipe(now, dist, you);
                    break;
                case AnomalyKind.Move:
                    UpdateMove(now, dist);
                    break;
                case AnomalyKind.Smile:
                case AnomalyKind.UnderTheSuit:
                    UpdateFlicker(now, dist);
                    break;
                case AnomalyKind.Sleeper:
                    UpdateSleeper(now, dist);
                    break;
                case AnomalyKind.Vanish:
                case AnomalyKind.BehindYou:
                    UpdateVanished(now, you);
                    break;
            }
        }

        /// <summary>
        /// Through a door, straight on to the next; at the end of a pass, a moment for the closes still owed.
        /// Then the closet. The round as a whole ends at anomalyUntil.
        /// </summary>
        private void UpdateShutDoors(float now)
        {
            if (doorsHiding)
            {
                // Fear took the closet over, or it came out. docs/invariants.md#fear-owns-the-buddy
                if (!hideAmbush) EndAnomaly(Hiding ? "it is hiding from the Breathless now" : "out of the closet");

                return;
            }
            if (now >= anomalyUntil)
            {
                EndAnomaly($"{DoorsShut()} of {doorsToShut.Count} doors shut (out of time)");
                return;
            }
            if (anomalyWalking) return;

            if (doorsSettling && now < doorsSettleUntil && DoorsShut() < doorsToShut.Count) return;

            NextDoorOrEnd("the round is done");
        }

        private void UpdateNoises(float now, float dist)
        {
            if (anomalyComing)
            {
                string? wait = anomalySeen ? "you can see it"
                    : dist < NoisesMinDist || dist > NoisesMaxDist ? $"it is {dist:0.0}m from you"
                    : !PlayerView.IsBehind(agent.GroundPos(1.1f)) ? "it is not behind you" : null;
                if (StillComing(now, wait)) return;

                // Behind you now: it stops and the noises begin.
                anomalyComing = false;
                agent.ClearMoveTarget();
                agent.ReleasePlan();
                anomalyUntil = now + 12f;
                anomalyStepAt = now + 0.4f;
                if (NpcLog.Level >= 2) YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} is behind you, {dist:0.0}m - the noises begin");
                return;
            }
            // Seen, or not at your back any more: what still plays dies away at once.
            if (anomalySeen || !PlayerView.IsBehind(agent.GroundPos(1.1f)))
            {
                ScareSounds.FadeOut(transform);
                EndAnomaly("you turned round");
                return;
            }
            if (now < anomalyStepAt) return;

            bool scary = AnomalyDirector.Ceiling >= AnomalySeverity.Scary;
            int sounds = scary ? 4 : 2;
            // Scary and worse: the last is the creature, the second maybe too.
            ScareSound sound = anomalyStep switch
            {
                0 => ScareSound.Odd,
                _ when scary && anomalyStep == sounds - 1 => ScareSound.Creature,
                1 => scary && Random.value < 0.5f ? ScareSound.Creature : ScareSound.Odd,
                _ => ScareSound.Odd,
            };
            if (ScareSounds.Play(sound, agent.GroundPos(1.2f), transform, out float seconds, NoisesVolume) && sound >= ScareSound.Creature) Startle(8);

            anomalyStep++;
            // The next one only after this one is over, and a silence.
            anomalyStepAt = now + seconds + Random.Range(NoisesGapMin, NoisesGapMax);
            if (anomalyStep >= sounds || dist > 9f) EndAnomaly("silent again");
        }

        private void UpdateBloody(float now, Vector3 yourPos)
        {
            if (goreTexture == null || suit.Suited)
            {
                EndAnomaly("its look changed");
                return;
            }
            // Something in the world can rewrite the body's materials, as BuddySuit's watcher finds, so put it back.
            if (!BuddySkin.IsApplied(transform, goreTexture))
            {
                BuddySkin.ApplyTexture(transform, goreTexture);
                YourBuddyPlugin.Log.LogWarning($"[anomaly] {Name}'s blood was lost (its materials changed) - applying it again");
            }
            // The run gives way to the Breathless. docs/invariants.md#fear-owns-the-buddy
            if (anomalyWalking)
            {
                if (fearState == FearState.Calm) return;

                StopAnomalyWalk();
                anomalyStep = 0;
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} stops running off - it has worse to worry about");
                return;
            }
            // Never while you look at it, only once its time is up and you have not seen it for a while.
            if (now < anomalyUntil) return;

            if (!anomalySeen && now - anomalyUnseenSince >= BloodyUnseenSeconds)
            {
                EndAnomaly(anomalySeenFor > 0f ? "out of your sight, clean again" : "nobody saw");
                return;
            }
            // You keep it in sight, so it gets away from you to get clean.
            if (now < anomalyStepAt || fearState != FearState.Calm) return;

            anomalyStepAt = now + BloodyRunRetrySeconds;
            if (RunFrom(now, yourPos) is { } stuck) TraceBloody(stuck);
            else anomalyStep = 1;
        }

        private void TraceBloody(string why)
        {
            if (NpcLog.Level >= 2) YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} cannot get out of your sight yet - {why}");
        }

        private void UpdateStalker(float now, float dist)
        {
            if (goreTexture == null || suit.Suited)
            {
                EndAnomaly("its look changed");
                return;
            }
            if (anomalySeen && dist < 3f) Startle(25);

            // anomalyStep counts its sounds at your back, at most StalkerSounds.
            if (!anomalySeen && anomalyStep < StalkerSounds && now >= anomalyStepAt && dist < 6f && PlayerView.IsBehind(agent.GroundPos(1.1f)))
            {
                anomalyStep++;
                ScareSounds.Play(Random.value < 0.6f ? ScareSound.Odd : ScareSound.Creature, agent.GroundPos(1.2f), transform, out float seconds);
                anomalyStepAt = now + seconds + Random.Range(6f, 10f);
            }
            if (now >= anomalyUntil && !anomalySeen) EndAnomaly("gone quiet");
        }

        /// <summary>
        /// Waits out its time unseen, then comes back where you will not see it arrive (for BehindYou,
        /// right at your back). Logs why while it cannot.
        /// </summary>
        private void UpdateVanished(float now, Transform you)
        {
            if (vanished)
            {
                if (now < anomalyUntil || now < anomalyStepAt) return;

                anomalyStepAt = now + 2f;
                bool behindYou = anomaly == AnomalyKind.BehindYou;
                Vector3? spot = behindYou ? SpotBehind(you) : HiddenSpotNear(you);
                if (spot == null && now < anomalyGiveUpAt)
                {
                    TraceAnomaly(behindYou ? "waiting for room behind you" : "waiting for a spot out of your sight");
                    return;
                }
                if (spot == null && PlayerView.SeesBody(transform, agent.FloorUnderNpc()) && now < anomalyGiveUpAt + 60f)
                {
                    TraceAnomaly("waiting for you to look away from where it vanished");
                    return;
                }
                if (spot != null) MoveTo(spot.Value);

                Reappear();
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} is back, {FlatDistance(transform.position, you.position):0.0}m from you" +
                                            (spot == null ? ", where it vanished" : ""));
                if (!behindYou || spot == null)
                {
                    EndAnomaly(spot == null && behindYou ? "no room behind you" : "back");
                    return;
                }
                anomalyStep = 1;
                anomalyUntil = now + 15f;
                agent.FacePoint(you.position);
                ScareSounds.Play(ScareSound.Creature, agent.GroundPos(1.2f));
                // Silent at your back; the line comes later. docs/anomalies.md#3-what-it-says
                SayLater(AnomalyLines.Spoken(AnomalySeverity.Extreme));
                return;
            }
            // BehindYou, standing at your back. Once you turn round, it holds a moment and lets go.
            if (anomalyStep == 1 && anomalySeen)
            {
                anomalyStep = 2;
                anomalyUntil = now + 1.5f;
                Startle(30);
            }
            if (now >= anomalyUntil) EndAnomaly(anomalyStep == 2 ? "you saw it" : "you never turned round");
        }

        // The body, from OverrideMovement and Steer

        private enum AnomalyFacing
        {
            Usual,
            You,
            Point
        }

        /// <summary>
        /// Whether the anomaly holds the body still, and where it looks standing still. One table for
        /// OverrideMovement and TryIdleFacing.
        /// </summary>
        private (bool Holds, AnomalyFacing Facing) AnomalyPose()
        {
            switch (anomaly)
            {
                case AnomalyKind.Noises:
                    // Walking up as a Follow does, then still at your back.
                    return anomalyComing ? (false, AnomalyFacing.Usual) : (true, AnomalyFacing.You);
                case AnomalyKind.Pipe:
                    // Put the pipe down: still, facing you, until you look away and it is gone.
                    return anomalyStep >= 2 ? (true, vanished ? AnomalyFacing.Usual : AnomalyFacing.You) : (false, AnomalyFacing.Usual);
                case AnomalyKind.BehindYou:
                case AnomalyKind.Move:
                    return (true, AnomalyFacing.You);
                case AnomalyKind.Statue:
                case AnomalyKind.Stalker:
                    return anomalySeen ? (true, AnomalyFacing.You) : (false, AnomalyFacing.Usual);
                case AnomalyKind.Sleeper:
                    // You found the one in the capsule, so it stands where it is, facing you, until you see it.
                    return anomalyStep == 2 && !sleeperActorFound ? (true, AnomalyFacing.You) : (false, AnomalyFacing.Usual);
                case AnomalyKind.Bloody:
                    // Got away from you, so it stays there to get clean.
                    return (anomalyStep == 2, AnomalyFacing.Usual);
                case AnomalyKind.ShutDoors:
                    // In the closet the hide steers it.
                    if (doorsHiding) return (false, AnomalyFacing.Usual);

                    return anomalyWalking ? (false, AnomalyFacing.Usual) : (true, AnomalyFacing.Point);
                case AnomalyKind.WindowStare:
                case AnomalyKind.WallStare:
                    return anomalyWalking ? (false, AnomalyFacing.Usual) : (true, AnomalyFacing.Point);
                case AnomalyKind.Meat:
                    // Over the meat, back to the door; then at you, caught, and cornered.
                    if (anomalyWalking) return (false, AnomalyFacing.Usual);

                    return (true, vanished ? AnomalyFacing.Usual : anomalyStep == 0 ? AnomalyFacing.Point : AnomalyFacing.You);
                case AnomalyKind.BotTalk:
                    // The robot while they talk, you for the look round; once away, the usual facing.
                    return (!anomalyWalking, anomalyStep switch { 0 => AnomalyFacing.Point, 1 => AnomalyFacing.You, _ => AnomalyFacing.Usual });
                default:
                    return (false, AnomalyFacing.Usual);
            }
        }

        /// <summary>
        /// OverrideMovement's turn. True while the anomaly holds the buddy still. Fear ends any but a vanish.
        /// docs/invariants.md#fear-owns-the-buddy
        /// </summary>
        private bool AnomalyHoldsBody()
        {
            if (!anomaly.HasValue) return false;

            if (fearState != FearState.Calm && !vanished && !AnomalyInBackground)
            {
                EndAnomaly("the Breathless");
                return false;
            }
            // Fear frees a bloody buddy that got away to be clean.
            if (anomaly == AnomalyKind.Bloody && anomalyStep == 2 && fearState != FearState.Calm) anomalyStep = 0;

            bool holds = AnomalyPose().Holds;
            if (holds && anomaly is AnomalyKind.Statue or AnomalyKind.Stalker or AnomalyKind.Sleeper) agent.ClearMoveTarget();

            return holds;
        }

        /// <summary>
        /// TryIdleFacing's turn, standing still. Where the anomaly looks. False leaves the usual facing.
        /// The agent turns an idle body after the brain's override, so facing set there would be undone.
        /// </summary>
        private bool AnomalyFaces(Player player)
        {
            switch (AnomalyPose().Facing)
            {
                case AnomalyFacing.You:
                    agent.FacePlayer(player);
                    return true;
                case AnomalyFacing.Point:
                    agent.FacePoint(anomalyFace);
                    return true;
                default:
                    return false;
            }
        }

        // Ending

        /// <summary>
        /// Puts back everything the anomaly changed (looks, collisions, AI, the closet) on every way out.
        /// docs/invariants.md#an-anomaly-puts-back-what-it-changed
        /// </summary>
        internal void EndAnomaly(string why)
        {
            if (!anomaly.HasValue) return;

            AnomalyKind kind = anomaly.Value;
            anomaly = null;
            Reappear();
            TakeGoreOff();
            ShowFlicker(false);
            flickerTexture = null;
            flickerWearing = null;
            RemoveSleeper();
            ReleaseBot();
            agent.KnowsEveryCode = false;
            anomalyComing = false;
            shipyard = null;
            fromAboard = null;
            cryoRoom = null;
            caughtDoorway = null;
            // Whatever ended it, the pipe goes down where it stands.
            if (pipe != null && agent.Hands.Item == pipe) agent.Hands.Drop("the bloody pipe is over - " + why);
            if (pipe != null) AnomalyProps.GoneWhenUnseen(pipe.gameObject);
            pipe = null;
            if (anomalyWalking)
            {
                anomalyWalking = false;
                agent.DropPlan();
                agent.ClearMoveTarget();
            }
            doorsToShut.Clear();
            doorsHiding = false;
            if (hideAmbush)
            {
                // With the Breathless about it stays in, hiding for real now. docs/invariants.md#fear-owns-the-buddy
                if (fearState != FearState.Calm)
                {
                    hideAmbush = false;
                    hideFromFear = true;
                }
                else
                {
                    ForceLeaveHidingSpot("the ambush is over - " + why);
                }
            }
            decideAt = 0f;
            anomalyLast = Anomalies.Info(kind).Name + " - " + why;
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name}: {Anomalies.Info(kind).Name} over - {why}");
            // Nobody saw it and nothing of it is left to find, so it may come again. docs/anomalies.md#once-per-save
            if (!anomalyWitnessed && !Anomalies.Info(kind).Has(AnomalyTraits.Lasting))
            {
                AnomalyMemory.Forget(kind);
                YourBuddyPlugin.Log.LogInfo($"[anomaly] Nobody saw {Anomalies.Info(kind).Name} - it may happen again");
            }
        }

        /// <summary>
        /// An order reaching it ends what it was acting out, unless it is not listening.
        /// </summary>
        private void EndAnomalyForOrder(string order)
        {
            if (anomaly.HasValue && !Anomalies.Has(anomaly, AnomalyTraits.Deaf | AnomalyTraits.Background)) EndAnomaly("you told me to " + order);
        }

        // Saying things, and scaring you

        /// <summary>
        /// A line said aloud in NPC.Core's speech panel and its talk log, with the robot's blips as its
        /// voice. Null when it was said.
        /// </summary>
        private string? Speak(string text)
        {
            if (Conversation != null) NpcInteraction.Speak(Conversation, text);

            ScareSounds.Babble(transform, Mathf.Clamp(text.Length / 3, 4, 16));
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} says: \"{text}\"");
            return null;
        }

        /// <summary>
        /// A line not said before in this save; null, as Speak, when it was said. With every line said it stays
        /// silent and says why. docs/anomalies.md#once-per-save
        /// </summary>
        private string? SpeakNew(string? text)
        {
            if (text != null) return Speak(text);

            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} has nothing left to say that it has not said");
            return "it has said every line it has";
        }

        /// <summary>
        /// Once per anomaly, your stress through the director's own source.
        /// </summary>
        private void Startle(int stress)
        {
            if (anomalyStartled) return;

            anomalyStartled = true;
            AnomalyDirector.Startle(stress);
        }

        /// <summary>
        /// From BuddyConversation.SetOpen. The window opened under the wrong name, once, and it says nothing about it.
        /// </summary>
        internal void OnTalkOpened()
        {
            if (TitleOverride == null) return;

            wrongTitle = null;
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name}'s talk window opened under the wrong name");
        }

        private float anomalyTraceAt = 0f;

        private void TraceAnomaly(string line)
        {
            if (Time.time < anomalyTraceAt) return;

            anomalyTraceAt = Time.time + 15f;
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} is still gone - {line}");
        }

        /// <summary>
        /// For the HUD and buddy_anomaly.
        /// </summary>
        internal string DescribeAnomaly()
        {
            string now = anomaly.HasValue
                ? Anomalies.Info(anomaly.Value).Name + (vanished ? ", gone" : anomalyWalking || anomalyComing ? ", on the way" : "") +
                  (anomalyWitnessed ? ", seen" : "") +
                  $", {Mathf.Max(0f, anomalyUntil - Time.time):0}s"
                : "none";
            return now + (TitleOverride != null ? "; wrong name waiting" : "") + " (last: " + anomalyLast + ")";
        }
    }
}

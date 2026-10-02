using System;
using System.Collections.Generic;
using System.IO;
using FMODUnity;
using NPC.Core.Navigation;
using NPC.Core.World;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The buddy's EVA suit. Wearing one mirrors the game's own equip. The spare suit item is
    /// switched off (as Equipment.Equip does to the player's) and the body is drawn with the
    /// embedded skin. Taking it off re-enables the item at the buddy's feet, as
    /// EquipmentSystem.UnequipSuit does. The walks live in EvaRun and SuitFetchErrand. docs/eva.md
    /// </summary>
    internal sealed class BuddySuit(IErrandBody body, LifeSupport lifeSupport)
    {
        /// <summary>
        /// Within this of a stranded suit the player may be about to pick it up, so the fetch
        /// leaves it. docs/invariants.md#the-buddy-never-takes-your-last-suit
        /// </summary>
        internal const float PlayerNearSuitDist = 2f;

        /// <summary>Whether the buddy wears a suit right now.</summary>
        internal bool Suited { get; private set; }

        /// <summary>
        /// How much the worn suit slows the buddy. Its MovementSpeedModifier (Space_Suit 0.4), as
        /// PlayerController.MovementSpeed applies it to the player, times SuitedPace. 1 without a suit.
        /// </summary>
        internal float SpeedFactor => Suited && wornSuit != null ? wornSuit.MovementSpeedModifier * SuitedPace : 1f;

        /// <summary>
        /// Keeps up with a sprinting suited player (3 m/s x 1.5 sprint x 0.4 = 1.8 m/s). At the
        /// default MoveSpeed 3.5 the buddy walks 3.5 x 0.4 x 1.3 = 1.82 m/s.
        /// </summary>
        internal const float SuitedPace = 1.3f;

        /// <summary>
        /// Why the suit is on. Only a <see cref="SuitReason.Survival"/> suit comes off by itself
        /// (back aboard, air safe again); an ordered one waits for the player's "unsuit".
        /// docs/eva.md
        /// </summary>
        internal enum SuitReason { Ordered, Survival }

        private SuitReason reason;

        private Suit? wornSuit;
        private EvaRun? run;

        // Finding and wearing a suit

        private static readonly List<Suit> SuitBuffer = [];

        /// <summary>
        /// Every suit item that could be taken right now. Suits lying free in the world, and the
        /// ones a locker displays (inactive, held by an EquipmentHolder, released through its own
        /// TryDropItem). A worn suit (the player's CachedSuitItem) is in neither and never shows.
        /// </summary>
        private static int CollectSuits()
        {
            SuitBuffer.Clear();
            foreach (Suit suit in UnityEngine.Object.FindObjectsOfType<Suit>())
            {
                if (suit != null && suit.Isolated) SuitBuffer.Add(suit);
            }
            foreach (EquipmentHolder holder in UnityEngine.Object.FindObjectsOfType<EquipmentHolder>(true))
            {
                // Not a locker on a station you are not docked at. docs/eva.md
                if (holder != null && holder.Item is Suit { Isolated: true } displayed && Items.Loadable(holder)) SuitBuffer.Add(displayed);
            }
            return SuitBuffer.Count;
        }

        /// <summary>
        /// Whether the player is wearing a suit, which makes anything on the floor a spare.
        /// </summary>
        private static bool PlayerIsSuited()
        {
            Player? pilot = NpcPlayer.Pilot;
            return pilot != null && pilot.EquipmentSystem != null && pilot.EquipmentSystem.SuitEquipped;
        }

        /// <summary>
        /// Why `suit` may not be taken right now, or null. A locker-displayed suit is released
        /// through its holder on the way, so it counts as free here and is released at the leg.
        /// </summary>
        internal string? TakeBlockerFor(Suit target)
        {
            if (!target.gameObject.activeSelf && !ReleaseFromDisplay(target)) return "the suit is gone";

            string? blocker = Items.TakeBlocker(target, null);
            if (blocker == null && body.TakenByAnother(target.transform)) blocker = "another buddy is on it";
            return blocker;
        }

        /// <summary>
        /// Gives a locker-displayed suit back to the world, the holder's own way. False when the
        /// suit is not in a holder or would not come out.
        /// </summary>
        private bool ReleaseFromDisplay(Suit suit)
        {
            foreach (EquipmentHolder holder in UnityEngine.Object.FindObjectsOfType<EquipmentHolder>(true))
            {
                if (holder == null || holder.Item != suit) continue;

                // The parameter is unused; TryDropItem drops at the holder's own dropPoint.
                holder.TryDropItem(null!);
                if (suit.gameObject.activeSelf)
                {
                    YourBuddyPlugin.Log.LogInfo("[suit] " + body.Name + " took the suit out of its locker");
                    return true;
                }
                return false;
            }
            return false;
        }

        /// <summary>
        /// The nearest suit the buddy may take, or null with why. While you wear a suit, one free
        /// suit is a spare; while you do not, two must be free, so taking one leaves you the other.
        /// A suit you ordered the buddy to take is the exception, since you are right there and asked.
        /// docs/invariants.md#the-buddy-never-takes-your-last-suit
        /// </summary>
        internal Suit? TakeableSuit(out string? why, bool playerOrdered = false)
        {
            why = null;
            if (!YourBuddyPlugin.ConfigEvaSuit.Value)
            {
                why = "the EvaSuit setting is off";
                return null;
            }

            CollectSuits();
            bool playerSuited = PlayerIsSuited();
            int free = 0;
            Suit? nearest = null;
            float nearestSqr = float.MaxValue;
            Vector3 here = body.Transform.position;
            foreach (Suit suit in SuitBuffer)
            {
                // A displayed suit is free; the release happens at the leg, in reach of the locker.
                if (suit.gameObject.activeSelf)
                {
                    string? blocker = Items.TakeBlocker(suit, null);
                    if (blocker == null && body.TakenByAnother(suit.transform)) blocker = "another buddy is on it";
                    if (blocker != null) continue;
                }

                free++;
                float sqr = Items.FlatDistanceSq(suit.transform.position, here);
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = suit;
                }
            }

            if (nearest == null)
            {
                why = free == 0 ? "no isolated suit is free to take" : "every free suit is spoken for";
                return null;
            }
            if (!playerSuited && free < 2 && !playerOrdered)
            {
                why = "it is your only suit";
                return null;
            }
            if (!playerSuited && free < 2)
            {
                YourBuddyPlugin.Log.LogInfo("[suit] " + body.Name + " takes your only free suit - you ordered it outside");
            }
            return nearest;
        }

        /// <summary>
        /// Whether any spare suit is takeable at all. The decider's cheap test before it scores
        /// the suit-up urge.
        /// </summary>
        internal bool SpareSuitAvailable() => TakeableSuit(out _) != null;

        /// <summary>
        /// A run is under way (walking to a suit, to or through an airlock).
        /// </summary>
        internal bool RunActive => run is { Active: true };

        /// <summary>
        /// Wears `suit`, switching the item off and the skin on with the equip sound. Null when worn,
        /// else why not. A locker-displayed suit is released first.
        /// </summary>
        internal string? Wear(Suit suit, SuitReason wornFor)
        {
            if (!suit.gameObject.activeSelf && !ReleaseFromDisplay(suit)) return "the suit is gone";
            if (Suited) return "already wearing one";

            // npc-core:docs/invariants.md#an-item-zone-lists-only-items-in-it
            NpcItemZones.Leave(suit);
            suit.Enabled = false;
            wornSuit = suit;
            Suited = true;
            reason = wornFor;
            ApplySkin();
            PlayEquipSound();
            YourBuddyPlugin.Log.LogInfo("[suit] " + body.Name + " put on the EVA suit ('" + suit.gameObject.name + "')");
            return null;
        }

        /// <summary>
        /// The sound the player's own equip plays (EquipmentSystem.ApplyEquipment).
        /// </summary>
        private void PlayEquipSound()
        {
            Player? pilot = NpcPlayer.Pilot;
            if (pilot == null) return;

            if (GameInternals.EquipmentSystemAccess.GetSuitEquipEvent(pilot.EquipmentSystem) is { } equipEvent)
            {
                RuntimeManager.PlayOneShot(equipEvent, body.Transform.position);
            }
        }

        /// <summary>
        /// Takes the suit off, restoring the skin and re-enabling the item where the buddy stands.
        /// Like EquipmentSystem.UnequipSuit, it re-parents the item into the room it lands in, judged
        /// by the floor under it since the buddy's tracked room is stale aboard the ship. A suit filed
        /// under a room it is not in vanishes with that room.
        /// npc-core:docs/invariants.md#a-carried-item-belongs-to-the-room-its-carrier-is-in
        /// </summary>
        internal void TakeOff(string why)
        {
            Suit? suit = wornSuit;
            Suited = false;
            wornSuit = null;
            BuddySkin.RestoreAll(body.Transform);

            if (suit != null)
            {
                suit.Enabled = true;
                Vector3 landing = body.Transform.position + body.Transform.forward * 0.8f + Vector3.up * 0.5f;
                Transform? room = body.ItemParentAt(landing);
                if (room != null) suit.SetParent(room);
                suit.Teleport(landing);
                suit.UseGravity = true;
                suit.AwakePhysics();
            }
            YourBuddyPlugin.Log.LogInfo("[suit] " + body.Name + " put the suit down - " + why);
        }

        // The skin, embedded in the dll

        private const string SkinResource = "YourBuddy.Resources.PilotSuit_eva.png";

        private static Texture2D? embeddedSkin;
        private static bool skinFailed;

        private static Texture2D? LoadEmbeddedSkin()
        {
            if (embeddedSkin != null || skinFailed) return embeddedSkin;

            try
            {
                using Stream? stream = typeof(YourBuddyPlugin).Assembly.GetManifestResourceStream(SkinResource);
                if (stream == null)
                {
                    skinFailed = true;
                    YourBuddyPlugin.Log.LogWarning("[suit] Embedded EVA skin " + SkinResource +
                                                   " is missing from the dll - the buddy suits up without a visual change");
                    return null;
                }
                using MemoryStream memory = new();
                stream.CopyTo(memory);
                Texture2D texture = new(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                if (!ImageConversion.LoadImage(texture, memory.ToArray()))
                {
                    UnityEngine.Object.Destroy(texture);
                    skinFailed = true;
                    YourBuddyPlugin.Log.LogWarning("[suit] Embedded EVA skin could not be read as a PNG - suiting up without a visual change");
                    return null;
                }
                embeddedSkin = texture;
                return texture;
            }
            catch (Exception ex)
            {
                skinFailed = true;
                YourBuddyPlugin.Log.LogWarning("[suit] Embedded EVA skin failed to load: " + ex.Message);
                return null;
            }
        }

        private void ApplySkin()
        {
            Texture2D? skin = LoadEmbeddedSkin();
            if (skin == null) return;

            if (BuddySkin.ApplyTexture(body.Transform, skin) == 0)
            {
                YourBuddyPlugin.Log.LogWarning("[suit] No material on " + body.Name + " takes the EVA skin");
            }
        }

        // Entry points and the watcher

        /// <summary>
        /// The EVA order. Suit up if a spare is free, then wait in an airlock (a station's, or the
        /// ship's own while undocked) for the player to cycle it. Returns the reply line.
        /// </summary>
        internal string StartOutsideNow()
        {
            string? busy = body.BusyForAirlockCommand();
            if (busy != null) return busy;

            if (body.IsOutside) return body.Name + " is already outside";

            if (WhyNoWayOut() is { } why) return body.Name + " cannot go outside: " + why;

            Airlock? airlock = PickAirlock();
            if (airlock == null) return body.Name + " cannot go outside: no airlock here opens to the outside";

            EndRun();
            run = new EvaRun(this, body);
            return run.Begin(airlock);
        }

        /// <summary>
        /// The way back in. Walk into an airlock's chamber from outside and wait for the player to
        /// cycle it. The airlock is the one the player stands in, else the nearest. Returns the reply.
        /// </summary>
        internal string StartInsideNow()
        {
            string? busy = body.BusyForAirlockCommand();
            if (busy != null) return busy;

            if (!body.IsOutside) return body.Name + " is already inside";

            Airlock? airlock = PickAirlock();
            if (airlock == null) return body.Name + " cannot find an airlock to come in through";

            EndRun();
            run = new EvaRun(this, body);
            return run.BeginInside(airlock);
        }

        /// <summary>
        /// A route toward an airlock that ends this near it counts as reaching it; the run walks the rest.
        /// </summary>
        private const float AirlockReachedWithin = 6f;

        private static readonly List<Airlock> AirlockBuffer = [];

        /// <summary>
        /// The airlock for a trip. The one the player stands in; when floating, the nearest chamber;
        /// when walking, the nearest the buddy can walk to, since a station's interior reaches only some
        /// of its airlocks (the FuelStation refinery opens onto the surface alone). Else the nearest,
        /// or null.
        /// </summary>
        private Airlock? PickAirlock()
        {
            Player? pilot = NpcPlayer.Pilot;
            if (pilot != null && pilot.Controller != null &&
                NpcDoors.ChamberAt(pilot.Controller.CachedTransform.position, withShip: true) is { } withPlayer &&
                OpensOutside(withPlayer))
            {
                return withPlayer;
            }

            Vector3 here = body.Transform.position;
            AirlockBuffer.Clear();
            foreach (Airlock airlock in UnityEngine.Object.FindObjectsOfType<Airlock>())
            {
                if (airlock != null && OpensOutside(airlock)) AirlockBuffer.Add(airlock);
            }

            // Nothing to walk on out there, so the nearest chamber. docs/eva.md#7-floating
            if (body.Floating)
            {
                AirlockBuffer.Sort((a, b) => (NpcDoors.ChamberCenter(a) - here).sqrMagnitude
                    .CompareTo((NpcDoors.ChamberCenter(b) - here).sqrMagnitude));
                return AirlockBuffer.Count > 0 ? AirlockBuffer[0] : null;
            }

            AirlockBuffer.Sort((a, b) => Items.FlatDistanceSq(a.transform.position, here)
                .CompareTo(Items.FlatDistanceSq(b.transform.position, here)));

            foreach (Airlock airlock in AirlockBuffer)
            {
                Vector3 at = airlock.transform.position;
                if (Items.FlatDistanceSq(at, here) <= AirlockReachedWithin * AirlockReachedWithin) return airlock;

                if (body.PlanRoute(at, mayGoOutside: true, out NavPath plan) == null &&
                    Items.FlatDistanceSq(plan[plan.Count - 1], at) <= AirlockReachedWithin * AirlockReachedWithin)
                {
                    return airlock;
                }
            }
            return AirlockBuffer.Count > 0 ? AirlockBuffer[0] : null;
        }

        /// <summary>
        /// Every station airlock opens to the outside, the ship's own only while undocked. Docked, it
        /// is the way into the station and both its doors stand open.
        /// </summary>
        private static bool OpensOutside(Airlock airlock)
        {
            SpaceShip? ship = GameManager.Instance != null ? GameManager.Instance.PlayerShip : null;
            return ship == null || airlock != ship.Airlock || string.IsNullOrEmpty(ship.Autopilot.DockedStation);
        }

        /// <summary>
        /// Why the suit must stay on where the buddy stands, or null. Outside, or in an airlock
        /// chamber the next cycle may open to space, the ship's included. docs/eva.md
        /// </summary>
        private string? WhyKeepSuitOn()
        {
            if (body.IsOutside) return "there is no air out here";

            return NpcDoors.ChamberAt(body.Transform.position, withShip: true) != null ? "it is standing in an airlock" : null;
        }

        /// <summary>
        /// buddy_order suit on|off. Wear a spare now or take the worn one off, whatever the air says.
        /// </summary>
        internal string SuitNow(string onOff)
        {
            string? busy = body.BusyForAirlockCommand();
            if (busy != null) return busy;

            bool on = !onOff.StartsWith("off", StringComparison.OrdinalIgnoreCase) &&
                      !onOff.StartsWith("0", StringComparison.Ordinal);
            if (on)
            {
                if (Suited) return body.Name + " is already wearing a suit";

                EndRun();
                run = new EvaRun(this, body);
                return run.BeginSurvival(playerOrdered: true)
                    ? body.Name + " is putting a suit on"
                    : body.Name + " cannot suit up: no free spare";
            }

            if (!Suited) return body.Name + " is not wearing a suit";
            return UnsuitNow();
        }

        /// <summary>
        /// Takes the worn suit off now, whatever the air, but never outside or in an airlock.
        /// </summary>
        internal string UnsuitNow()
        {
            string? busy = body.BusyForAirlockCommand();
            if (busy != null) return busy;

            if (!Suited) return body.Name + " is not wearing a suit";

            if (WhyKeepSuitOn() is { } keep) return body.Name + " keeps the suit on - " + keep;

            TakeOff("you asked");
            return body.Name + " took the suit off";
        }

        /// <summary>
        /// From the decider, once the suit-up urge won. Put a spare on and shelter aboard until
        /// the air is safe again. True when the buddy set off for a suit.
        /// </summary>
        internal bool TryStartSurvivalSuitUp()
        {
            if (Suited) return false;

            EndRun();
            run = new EvaRun(this, body);
            return run.BeginSurvival(playerOrdered: false);
        }

        /// <summary>
        /// Why no airlock will let the buddy out now, or null. An airlock locks while the ship is
        /// moving (Airlock.Tick), so nobody cycles out then.
        /// </summary>
        private static string? WhyNoWayOut()
        {
            GameManager? gm = GameManager.Instance;
            if (gm == null || gm.PlayerShip == null) return "there is no ship";

            return gm.IsStaticWorldPosition && gm.IsStaticWorldRotation ? null : "the ship is moving, and the airlock stays locked";
        }

        /// <summary>
        /// Slow phase 3, from the brain. Keeps the run moving and the worn suit real, and takes
        /// the suit off again once the buddy is back aboard in breathable air.
        /// </summary>
        public void Update()
        {
            run?.Update();

            if (!Suited) return;

            // The worn suit was taken or destroyed, and the skin would stay on with nothing behind it.
            // Outside, the buddy stays suited until it is back in the air.
            if (wornSuit == null)
            {
                if (WhyKeepSuitOn() == null) TakeOff("the suit is gone");
                return;
            }

            // Something in the world can rewrite the body's materials (seen between the docking
            // corridor and the ship), so put the skin back and log when.
            Texture2D? skin = LoadEmbeddedSkin();
            if (skin != null && Time.time >= nextSkinCheckAt)
            {
                nextSkinCheckAt = Time.time + 1f;
                if (!BuddySkin.IsApplied(body.Transform, skin))
                {
                    YourBuddyPlugin.Log.LogWarning("[suit] " + body.Name + "'s EVA skin was lost (its materials changed) - applying it again");
                    ApplySkin();
                }
            }

            // Back in from outside by any way (a cycle, "inside", pulled aboard, a rescue), the walk out
            // is over. The suit comes off like a survival suit once the air is safe. docs/eva.md#2-the-suit-on-the-buddy
            bool outside = body.IsOutside;
            bool cameIn = wasOutside && !outside;
            if (cameIn && reason == SuitReason.Ordered)
            {
                reason = SuitReason.Survival;
                YourBuddyPlugin.Log.LogInfo("[suit] " + body.Name + " is back inside - the suit comes off once the air is safe");
            }
            wasOutside = outside;

            // Only the survival suit comes off by itself; one you ordered on inside stays on until you
            // say "unsuit". Not in an airlock chamber, which the next cycle may vent, and not on the check
            // it came in on, since its air is read afresh only at the agent's next slow phase 0. docs/eva.md
            if (reason == SuitReason.Survival && !outside && !cameIn && WhyKeepSuitOn() == null && AirIsSafeHere())
            {
                TakeOff("the air is safe again");
            }
        }

        /// <summary>
        /// Updated by the watcher. Coming back in is the edge that ends a walk outside.
        /// </summary>
        private bool wasOutside;

        /// <summary>
        /// The air where the buddy stands, aboard or on a station, is one it can breathe unsuited.
        /// </summary>
        private bool AirIsSafeHere()
        {
            Environment? air = body.Air;
            return air != null && air.Data != null && !lifeSupport.Dangerous(air);
        }

        private float nextSkinCheckAt;

        /// <summary>
        /// The buddy died. The suit it wears goes down beside it, or a load would lose it.
        /// </summary>
        internal void OnDied()
        {
            EndRun();
            if (Suited) TakeOff("it died");
        }

        /// <summary>
        /// On despawn the worn suit goes back into the world, or it would vanish with the buddy.
        /// </summary>
        internal void OnDespawned()
        {
            EndRun();
            if (Suited) TakeOff("it was despawned");
        }

        private void EndRun()
        {
            run?.End();
            run = null;
        }

        internal void RunEnded()
        {
            run = null;
        }

        /// <summary>
        /// The suit the buddy wears, for the sidecar. The item is inactive while worn, so a save
        /// and load needs its id to give it back.
        /// </summary>
        internal uint WornSuitId => wornSuit != null ? wornSuit.ID : 0;

        /// <summary>
        /// The suit item this buddy wears, or null. The fetch counts it as one at home.
        /// </summary>
        internal Suit? WornSuit => Suited ? wornSuit : null;

        /// <summary>
        /// Why the worn suit is on, for the sidecar; null without one.
        /// </summary>
        internal string? WornReason => Suited ? reason.ToString() : null;

        /// <summary>
        /// After a load, wears the suit the sidecar says this buddy was wearing, for the reason it
        /// was worn, or a survival suit would never come off by itself again. A sidecar without a
        /// reason (older builds) restores survival, which the watcher only takes off aboard in
        /// safe air. docs/eva.md
        /// </summary>
        internal void RestoreWorn(uint suitId, string? wornFor)
        {
            reason = Enum.TryParse(wornFor, out SuitReason saved) ? saved : SuitReason.Survival;
            if (Suited || suitId == 0) return;

            foreach (Suit suit in UnityEngine.Object.FindObjectsOfType<Suit>(true))
            {
                if (suit == null || suit.ID != suitId) continue;

                wornSuit = suit;
                Suited = true;
                ApplySkin();
                YourBuddyPlugin.Log.LogInfo("[suit] " + body.Name + " is wearing its suit again after the load (" + reason + ")");
                return;
            }
            YourBuddyPlugin.Log.LogWarning("[suit] The save says " + body.Name +
                                           " was wearing suit #" + suitId + ", which is not in this scene");
        }
    }
}

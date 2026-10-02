using System.Collections.Generic;
using NPC.Core;
using NPC.Core.Navigation;
using NPC.Core.World;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// A spare suit left on the docked station is carried back aboard and set down by the ship's
    /// airlock. A suit outside stays there, since only your cycle takes the buddy through an airlock.
    /// docs/eva.md, npc-core:docs/invariants.md#an-airlock-is-crossed-by-its-cycle
    /// </summary>
    internal sealed class SuitFetchErrand(IErrandBody body, BuddySuit suit) : Errand(body)
    {
        private const float FetchInterval = 120f;
        private const float FetchRetryDelay = 180f;
        private const float FetchSkipSeconds = 45f;

        private Suit? target;

        public override bool Enabled => YourBuddyPlugin.ConfigEvaSuit.Value && YourBuddyPlugin.ConfigSuitFetch.Value;
        public override float Interval => FetchInterval;
        public override float RetryDelay => FetchRetryDelay;
        protected override float SkipSeconds => FetchSkipSeconds;
        protected override string Command => "buddy_order fetchsuit";
        protected override string Topic => "Suit";

        /// <summary>
        /// The stranded suits right now. Isolated, free, filed on the docked station (by its room, else
        /// the floor under it, switched-off rooms included), not outside, and not within PlayerNearSuitDist
        /// of the player, who may be about to pick it up. Only while the buddy is aboard. Fills `scanNote`
        /// with what was seen and why each was left.
        /// </summary>
        private static readonly List<Suit> Stranded = [];

        private int CollectStranded(out float nearest)
        {
            Stranded.Clear();
            Skips.Prune();
            nearest = float.MaxValue;
            int seen = 0, aboard = 0, outside = 0, taken = 0, nearYou = 0, noFloor = 0, elsewhere = 0, skipped = 0;
            if (!Body.IsAboardPlayerShip())
            {
                NoteScan("I only fetch from aboard your ship");
                return 0;
            }
            int atHome = CountSuitsAtHome();
            if (atHome >= EnoughSuitsAtHome)
            {
                NoteScan($"{atHome} suits are already yours or aboard - enough");
                return 0;
            }

            Player? pilot = NpcPlayer.Pilot;
            Vector3 playerPos = pilot != null && pilot.Controller != null
                ? pilot.Controller.CachedTransform.position
                : Vector3.zero;
            Vector3 here = Body.Transform.position;
            // What the cycle carries out (Airlock.Exit) and a suit taken off outside land here.
            Room? space = GameManager.Instance != null ? GameManager.Instance.SpaceRoom : null;

            // Switched-off ones too. While you are aboard, the station rooms you are not in have their
            // content off, and a suit lying there with it. Worn and locker-held suits are off themselves.
            foreach (Suit suit in UnityEngine.Object.FindObjectsOfType<Suit>(true))
            {
                if (suit == null || !suit.Isolated || !suit.gameObject.activeSelf) continue;

                seen++;
                if (suit.IsGrabbed || suit.restrictGrab) { taken++; continue; }
                if (space != null && suit.transform.IsChildOf(space.ContentParent)) { outside++; continue; }
                // Another station's rooms cannot load, and its interior lies where the docked one is.
                if (!Items.Loadable(suit)) { elsewhere++; continue; }

                // The room the game filed it under says whose it is, since a floor probe finds no floor
                // in a switched-off room. The probe answers when the filing does not.
                string? owner = NpcVessels.OwnerOfTransform(suit.transform);
                FloorOwnership floor = owner == NavGraph.ShipOwner ? FloorOwnership.PlayerShip
                    : owner != null ? FloorOwnership.Elsewhere
                    : NpcVessels.FloorOwner(suit.transform.position);
                if (floor == FloorOwnership.PlayerShip) { aboard++; continue; }
                if (floor == FloorOwnership.Unknown) { noFloor++; continue; }

                if (pilot != null && Items.FlatDistanceSq(suit.transform.position, playerPos) <
                    BuddySuit.PlayerNearSuitDist * BuddySuit.PlayerNearSuitDist) { nearYou++; continue; }
                if (Skips.Has(suit.transform)) { skipped++; continue; }

                Stranded.Add(suit);
                nearest = Mathf.Min(nearest, Mathf.Sqrt(Items.FlatDistanceSq(suit.transform.position, here)));
            }

            NoteScan(seen == 0 ? "no isolated suit lying anywhere"
                : $"{seen} isolated suit(s): {Stranded.Count} to fetch" + Part(aboard, "already aboard") +
                  Part(outside, "outside") + Part(taken, "held or taken") + Part(nearYou, "near you") +
                  Part(noFloor, "on no floor I can tell") + Part(elsewhere, "on a station you are not docked at") +
                  Part(skipped, "left out for now after a failed try"));
            return Stranded.Count;
        }

        /// <summary>
        /// With this many isolated suits yours or aboard, a third is not worth a trip. One to wear, one
        /// spare (the spare rule's own count). docs/eva.md
        /// </summary>
        private const int EnoughSuitsAtHome = 2;

        private static readonly HashSet<Suit> HomeSuits = [];

        /// <summary>
        /// Isolated suits that are yours or aboard. The one you wear or carry in hands, belt or backpack;
        /// any lying aboard or displayed in a locker aboard; any a buddy aboard wears.
        /// </summary>
        private static int CountSuitsAtHome()
        {
            HomeSuits.Clear();
            Player? pilot = NpcPlayer.Pilot;
            EquipmentSystem? gear = pilot != null ? pilot.EquipmentSystem : null;
            if (gear != null)
            {
                AddHome(gear.CachedSuitItem);
                AddHome(gear.CachedLeftHandItem);
                AddHome(gear.CachedRightHandItem);
                if (gear.CachedBeltItem != null && gear.CachedBeltItem.Inventory is { } belt)
                {
                    for (byte i = 0; i < belt.SlotsCount; i++) AddHome(belt.GetSlotItem(i));
                }
                if (gear.CachedBackpackItem != null && gear.CachedBackpackItem.Inventory is { } pack)
                {
                    for (byte i = 0; i < pack.SlotsOccupied; i++) AddHome(pack.GetItemByIndex(i));
                }
            }

            foreach (Suit suit in UnityEngine.Object.FindObjectsOfType<Suit>(true))
            {
                if (suit != null && suit.gameObject.activeSelf && IsAboard(suit.transform)) AddHome(suit);
            }
            foreach (EquipmentHolder holder in UnityEngine.Object.FindObjectsOfType<EquipmentHolder>(true))
            {
                if (holder != null && holder.Item is Suit shown && IsAboard(holder.transform)) AddHome(shown);
            }
            foreach (BuddyBehaviour buddy in BuddyManager.Snapshot())
            {
                if (buddy != null && !buddy.IsDead && buddy.Agent.IsAboardPlayerShip()) AddHome(buddy.WornSuit);
            }
            int count = HomeSuits.Count;
            HomeSuits.Clear();
            return count;
        }

        private static void AddHome(Grabbable? item)
        {
            if (item is Suit { Isolated: true } suit) HomeSuits.Add(suit);
        }

        private static bool IsAboard(Transform t)
        {
            string? owner = NpcVessels.OwnerOfTransform(t);
            return owner != null ? owner == NavGraph.ShipOwner : NpcVessels.FloorOwner(t.position) == FloorOwnership.PlayerShip;
        }

        private static string Part(int count, string what) => count > 0 ? $", {count} {what}" : "";

        /// <summary>
        /// What the last scan saw, for the HUD; logged at level 2 when it changes. docs/logging.md §4
        /// </summary>
        private string scanNote = "not looked yet";

        private void NoteScan(string note)
        {
            if (note == scanNote) return;

            scanNote = note;
            if (NpcLog.Level >= 2) YourBuddyPlugin.Log.LogInfo("[suit] " + Body.Name + " suit fetch: " + note);
        }

        protected override string? WhyIdle => scanNote;

        public override bool TryStart(out string report)
        {
            if (CollectStranded(out _) == 0)
            {
                report = "no suit is stranded away from the ship";
                return false;
            }

            target = PickTarget();
            if (target == null)
            {
                report = "every stranded suit is taken or too near you";
                return false;
            }

            return SetOff(new FetchLeg(this, Body, suit, target), out report);
        }

        private bool SetOff(ErrandLeg leg, out string report)
        {
            if (!Body.InReach(leg))
            {
                string? failure = Body.PlanReach(leg, out NavPath plan);
                if (failure != null)
                {
                    if (target != null) Defer(target.transform, FetchRetryDelay);
                    report = failure;
                    return false;
                }
                Body.Walk(leg, plan);
            }
            else
            {
                Body.Walk(leg, null);
            }

            report = "is fetching the suit '" + Items.BaseName(target!.gameObject.name) + "'"; // target set by the callers
            return true;
        }

        /// <summary>
        /// The nearest stranded suit this buddy may work on. docs/invariants.md#one-buddy-per-target
        /// </summary>
        private Suit? PickTarget()
        {
            Suit? best = null;
            float bestSqr = float.MaxValue;
            Vector3 here = Body.Transform.position;
            foreach (Suit suit in Stranded)
            {
                if (Body.TakenByAnother(suit.transform)) continue;

                float sqr = Items.FlatDistanceSq(suit.transform.position, here);
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = suit;
                }
            }
            return best;
        }

        public override int Count(out float nearest) => CollectStranded(out nearest);

        // The legs

        /// <summary>
        /// Walks to the stranded suit and picks it up, then walks home.
        /// </summary>
        private sealed class FetchLeg(SuitFetchErrand errand, IErrandBody body, BuddySuit suit, Suit target)
            : ErrandLeg(target.transform.position, target.transform)
        {
            public override string Name => "the forgotten suit";
            public override string Describe() => "fetching " + Items.BaseName(target.gameObject.name);
            public override void Defer(float seconds) => errand.LegDeferred(target);
            public override bool Holds(Transform t) => t == target.transform;

            public override Vector3 Approach(out bool wantMove)
            {
                wantMove = false;
                // It may still lie in a room whose content is off, and the buddy is in there now.
                if (Items.InUnloadedRoom(target)) body.LoadRoomOf(target.transform);

                string? blocker = suit.TakeBlockerFor(target);
                if (blocker != null)
                {
                    errand.GiveUp("the suit is no longer there - " + blocker, target);
                    return Vector3.zero;
                }

                if (!body.StepIntoReach(this, out Vector3 move, out wantMove)) return move;

                if (!body.Hands.PickUp(target))
                {
                    errand.GiveUp("the suit could not be picked up", target);
                    return Vector3.zero;
                }

                YourBuddyPlugin.Log.LogInfo("[suit] " + body.Name + " picked up the forgotten suit ('" +
                                            target.gameObject.name + "')");
                errand.BeginHome();
                return Vector3.zero;
            }

            public override void End() => body.Hands.Drop("stopped fetching");
        }

        /// <summary>
        /// Carries the suit to the ship's airlock and puts it down inside.
        /// </summary>
        private sealed class HomeLeg(SuitFetchErrand errand, IErrandBody body, Grabbable carried)
            // HomeTransform is null only without a player ship, and this leg is only built while fetching, aboard.
            : ErrandLeg(HomePoint(), HomeTransform()!)
        {
            /// <summary>
            /// How far inside the ship's inner airlock door the suit is set down.
            /// </summary>
            private const float HomeInsideInnerDoor = 1.5f;
            /// <summary>
            /// The target point's height over the floor, where reach and sight are measured to.
            /// </summary>
            private const float HomeAboveFloor = 0.5f;

            /// <summary>
            /// Just inside the ship's inner airlock door, on the floor of the room beside it. Not the
            /// airlock's own transform, which is no place anyone can see or stand at, and not in the
            /// chamber, whose next cycle would carry the suit out with the player (Airlock.Exit).
            /// </summary>
            private static Vector3 HomePoint()
            {
                SpaceShip? ship = GameManager.Instance != null ? GameManager.Instance.PlayerShip : null;
                if (ship == null || ship.Airlock == null) return Vector3.zero;

                Gate? inner = GameInternals.AirlockAccess.GetInnerDoor(ship.Airlock);
                Gate? outer = GameInternals.AirlockAccess.GetOuterDoor(ship.Airlock);
                if (inner == null || outer == null) return ship.Airlock.transform.position;

                Vector3 inward = inner.transform.position - outer.transform.position;
                inward.y = 0f;
                Vector3 point = inner.transform.position +
                                (inward.sqrMagnitude > 0.0001f ? inward.normalized : Vector3.zero) * HomeInsideInnerDoor;
                if (NavProbe.TryFloorHeight(point, out float floorY)) point.y = floorY + HomeAboveFloor;
                return point;
            }

            private static Transform? HomeTransform()
            {
                SpaceShip? ship = GameManager.Instance != null ? GameManager.Instance.PlayerShip : null;
                return ship != null && ship.Airlock != null ? ship.Airlock.transform : null;
            }

            public override string Name => "your ship's airlock";
            public override string Describe() => "bringing the suit home";
            public override void Defer(float seconds) => errand.LegDeferred(null);

            public override Vector3 Approach(out bool wantMove)
            {
                wantMove = false;
                if (body.Hands.Item != carried)
                {
                    errand.GiveUp("the suit is no longer in my hands", null);
                    return Vector3.zero;
                }

                if (!body.StepIntoReach(this, out Vector3 move, out wantMove)) return move;

                body.Hands.Drop("your suit is home");
                errand.Delivered();
                return Vector3.zero;
            }

            public override void End() => body.Hands.Drop("stopped fetching");
        }

        // Leg callbacks

        private void BeginHome()
        {
            if (target == null) return;

            HomeLeg leg = new(this, Body, target);
            if (Body.InReach(leg))
            {
                Body.Walk(leg, null);
                return;
            }
            string? failure = Body.PlanReach(leg, out NavPath plan);
            if (failure == null)
            {
                Body.Walk(leg, plan);
                return;
            }
            Body.Hands.Drop("no way home - putting the suit down here");
            GiveUp("no way back to the ship - " + failure, target);
        }

        private void Delivered()
        {
            YourBuddyPlugin.Log.LogInfo("[suit] " + Body.Name + " brought the forgotten suit home");
            Last = "brought the suit home";
            Body.FinishRoute();
        }

        /// <summary>
        /// The walk into reach gave up. The target sits out, and the route's own end drops the leg.
        /// </summary>
        private void LegDeferred(Suit? what)
        {
            if (what != null) Defer(what.transform, FetchRetryDelay);
            Last = "could not get there";
        }

        private void GiveUp(string why, Suit? what)
        {
            if (what != null) Defer(what.transform, FetchRetryDelay);
            Last = why;
            YourBuddyPlugin.Log.LogInfo("[suit] " + Body.Name + " gave up on fetching the suit - " + why);
            Body.FinishRoute();
        }
    }
}

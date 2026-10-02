using NPC.Core.Navigation;
using NPC.Core.World;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Move. Left on a station, it turns up where you are. docs/anomalies.md#move
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        private const float MoveWaitSeconds = 300f;
        private const float MoveStartleDist = 4f;

        // Turning up far from where you left it

        /// <summary>
        /// Why it cannot turn up near you, or null. It must be parked on a station with its interior switched
        /// off. The only kind a parked buddy acts out. docs/anomalies.md#move
        /// </summary>
        internal string? MoveReady()
        {
            if (IsDead) return "dead";

            if (gameObject.activeInHierarchy) return "it is not left behind anywhere";

            if (anomaly.HasValue) return "already acting one out";

            if (Asleep) return "asleep";

            string? owner = agent.CurrentOwner;
            return owner == null || owner == NavGraph.ShipOwner ? "it is parked with the ship, not left on a station" : null;
        }

        /// <summary>
        /// Out of the switched-off interior and onto the station you are on, out of your sight. Any order it had
        /// is gone. Never onto a ship, since it came without one.
        /// </summary>
        private string? StartMove(Transform you)
        {
            if (StationUnder(you.position) is not { } station) return "you are not aboard a station - it joins you only on another station";

            string yours = station.gameObject.name;
            string from = agent.CurrentOwner ?? "?";
            if (yours == from) return "you are on its station";

            Transform? anchor = NpcVessels.AnchorForOwner(yours);
            if (anchor == null) return $"'{yours}' has no frame to ride";

            Vector3? spot = HiddenSpotNear(you);
            if (spot == null) return "no spot near you out of your sight";

            // Placed while still parked, then woken by riding your station. A buddy parked since its load never ran
            // its agent's Start, which TeleportTo's controller and OriginToFeet come from. docs/anomalies.md#move
            CharacterController body = GetComponent<CharacterController>();
            float toFeet = body != null ? body.center.y - body.height * 0.5f : agent.OriginToFeet;
            transform.position = spot.Value - Vector3.up * toFeet;
            agent.RideOwner(yours, anchor);
            if (!gameObject.activeInHierarchy) return $"it did not wake on '{yours}'";

            agent.ClearMoveTarget();
            if (orderedMode.HasValue)
            {
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} forgets the order '{OrderName(orderedMode.Value)}' it had on '{from}'");
                orderedMode = null;
            }
            SetMode(BuddyMode.Follow);
            StartHold(AnomalyKind.Move, MoveWaitSeconds);
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name}, left on '{from}', turns up on '{yours}' {FlatDistance(spot.Value, you.position):0.0}m from you");
            return null;
        }

        /// <summary>
        /// Stands where it turned up until you find it, says a line, and follows you again.
        /// </summary>
        private void UpdateMove(float now, float dist)
        {
            // Its first frame awake, with the agent started. Where it stands against the floor under it.
            if (anomalyStep == 0)
            {
                anomalyStep = 1;
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} stands with its feet at {transform.position.y + agent.OriginToFeet:0.00}, " +
                                            $"the floor under it at {agent.FloorUnderNpc().y:0.00}");
            }
            if (anomalySeen)
            {
                if (dist < MoveStartleDist) Startle(20);

                SpeakNew(AnomalyLines.LeftBehind());
                EndAnomaly($"you found it {dist:0.0}m away");
                return;
            }
            if (now >= anomalyUntil) EndAnomaly("you never looked its way");
        }
    }
}

using NPC.Core.Interaction;
using NPC.Core.World;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// A line kept back from a silent reveal and said later, out of your sight. docs/anomalies.md#3-what-it-says
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        private string? laterLine = null;
        private float laterLineAt = 0f;
        private float laterLineCheckAt = 0f;
        private const float LaterLineSeconds = 10f;
        /// <summary>Not said this long after it was due, it is only written into the talk log.</summary>
        private const float LaterLineGiveUpSeconds = 120f;
        private const float LaterLineDist = 8f;

        /// <summary>
        /// Keeps `text` back for LaterLineSeconds. Nothing when the pool is used up.
        /// </summary>
        private void SayLater(string? text)
        {
            if (text == null) return;

            laterLine = text;
            laterLineAt = Time.time + LaterLineSeconds;
            YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} keeps a line back for {laterLineAt - Time.time:0}s: \"{text}\"");
        }

        /// <summary>
        /// From Update. Says the kept line once it is due, near you and out of your sight, with nothing else running.
        /// </summary>
        private void TickLaterLine()
        {
            if (laterLine == null) return;

            float now = Time.time;
            if (now < laterLineAt || now < laterLineCheckAt) return;

            laterLineCheckAt = now + 0.5f;
            if (now >= laterLineAt + LaterLineGiveUpSeconds)
            {
                if (Conversation != null) NpcInteraction.AddLine(Conversation, laterLine, false);
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} never got the moment - the line is only in its talk log");
                laterLine = null;
                return;
            }
            if (anomaly.HasValue || vanished || IsDead || Hiding) return;

            Player? player = NpcPlayer.Pilot;
            if (player == null || player.Controller == null) return;

            if (AirStopsAnomaly(player)) return;

            if (FlatDistance(player.Controller.CachedTransform.position, transform.position) > LaterLineDist) return;

            if (PlayerView.SeesBody(transform, agent.FloorUnderNpc())) return;

            string text = laterLine;
            laterLine = null;
            Speak(text);
        }
    }
}

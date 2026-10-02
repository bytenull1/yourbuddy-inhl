using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Smile and UnderTheSuit. Its skin flickers when you look it in the face. docs/anomalies.md#smile-and-underthesuit
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        // Smile and UnderTheSuit state, with the flicker's texture, what it wore, and the beat it is on. docs/anomalies.md#smile-and-underthesuit
        private Texture2D? flickerTexture = null;
        private Texture? flickerWearing = null;
        private bool flickerOn = false;
        private int flickerBeat = 0;
        private const float FlickerWaitSeconds = 240f;
        private const float FlickerMinDist = 1.5f;
        private const float FlickerMaxDist = 7f;
        private const float FlickerLookAngle = 25f;
        private const float FlickerFacingAngle = 50f;
        /// <summary>
        /// The flickers' beats, on and off in turn, in seconds.
        /// </summary>
        private static readonly float[] SmileBeats = [0.2f, 0.12f, 0.12f];
        private static readonly float[] FleshBeats = [0.12f];

        // Skin flickers

        /// <summary>
        /// Armed. The next time you look it in the face up close, the skin flickers for a blink, a bloody
        /// grin on the visor or flesh with a face behind the glass. docs/anomalies.md#smile-and-underthesuit
        /// </summary>
        private string? StartFlicker(AnomalyKind kind, bool sameVessel)
        {
            if (!sameVessel) return "you are on another vessel";

            if (suit.Suited) return "it wears a suit";

            Texture2D? texture = BuddyGore.Overlaid(transform, kind == AnomalyKind.Smile ? "FlickerSmile.png" : "FlickerFlesh.png", out Texture? wearing);
            if (texture == null) return "its body takes no skin";

            StartHold(kind, FlickerWaitSeconds, keepPlan: true);
            flickerTexture = texture;
            flickerWearing = wearing;
            flickerBeat = -1;
            return null;
        }

        private void UpdateFlicker(float now, float dist)
        {
            if (flickerTexture == null || suit.Suited)
            {
                EndAnomaly("its look changed");
                return;
            }
            if (flickerBeat < 0)
            {
                if (now >= anomalyUntil)
                {
                    EndAnomaly("you never looked it in the face");
                    return;
                }
                if (!FacesYouClose(dist)) return;

                flickerBeat = 0;
                anomalyStepAt = now;
                anomalyWitnessed = true;
                Startle(anomaly == AnomalyKind.Smile ? 10 : 15);
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name}'s skin flickers, {dist:0.0}m from you");
            }
            if (now < anomalyStepAt) return;

            float[] beats = anomaly == AnomalyKind.Smile ? SmileBeats : FleshBeats;
            if (flickerBeat >= beats.Length)
            {
                EndAnomaly("a blink");
                return;
            }
            ShowFlicker(flickerBeat % 2 == 0);
            anomalyStepAt = now + beats[flickerBeat];
            flickerBeat++;
        }

        /// <summary>
        /// You look straight at its head, FlickerMinDist..MaxDist away, and it faces you.
        /// </summary>
        private bool FacesYouClose(float dist)
        {
            if (dist < FlickerMinDist || dist > FlickerMaxDist) return false;

            Transform? cam = PlayerView.Camera();
            Vector3 head = agent.GroundPos(1.6f);
            if (cam == null || PlayerView.AngleTo(head) > FlickerLookAngle || !PlayerView.Sees(head, transform)) return false;

            Vector3 toYou = cam.position - transform.position;
            toYou.y = 0f;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            return Vector3.Angle(forward, toYou) <= FlickerFacingAngle;
        }

        private void ShowFlicker(bool on)
        {
            if (on && flickerTexture != null)
            {
                BuddySkin.ApplyTexture(transform, flickerTexture);
                flickerOn = true;
                return;
            }
            if (!flickerOn) return;

            BuddyGore.Remove(transform, flickerWearing);
            flickerOn = false;
        }
    }
}

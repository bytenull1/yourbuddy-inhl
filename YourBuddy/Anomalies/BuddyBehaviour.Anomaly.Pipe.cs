using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Pipe. It brings you a bloody pipe and drops it when you see it. docs/anomalies.md#pipe
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        // Pipe state, the bloody pipe in its hands and the frame it was made. docs/anomalies.md#pipe
        private Grabbable? pipe = null;
        private int pipeFrame = 0;
        private const float PipeMinDist = 5f;
        private const float PipeMaxDist = 30f;
        private const float PipeGiveUpSeconds = 120f;
        private const float PipeStartleDist = 4f;
        /// <summary>
        /// The pipe's hold relative to the body, bloody top end down and forward, a little to one side.
        /// </summary>
        private static readonly Quaternion PipeHold = Quaternion.Euler(150f, 0f, 20f);

        // The bloody pipe

        /// <summary>
        /// Out of your sight a bloody pipe is put in its hands. It comes up to you with it, not answering,
        /// and the moment you spot it, puts it down without a word. docs/anomalies.md#pipe
        /// </summary>
        private string? StartPipe(float dist, bool sameVessel)
        {
            if (!sameVessel) return "you are on another vessel";

            if (dist < PipeMinDist || dist > PipeMaxDist) return $"it is {dist:0.0}m from you - it needs {PipeMinDist:0}-{PipeMaxDist:0}m";

            if (mode is not BuddyMode.Follow and not BuddyMode.Wander) return "it is staying put";

            if (agent.Hands.Item != null) return "its hands are full";

            Vector3 hold = agent.Hands.Point;
            Transform? parent = agent.ItemParentAt(hold);
            if (parent == null) return "no room here to keep a pipe in";

            Grabbable? made = AnomalyProps.SpawnPipe(parent, hold, transform.rotation * PipeHold);
            if (made == null) return "no pipe to hold - see the warning above";

            if (mode == BuddyMode.Wander) SetMode(BuddyMode.Follow);

            StartHold(AnomalyKind.Pipe, PipeGiveUpSeconds);
            pipe = made;
            // Picked up next frame, since the item's Start puts it back where its data says.
            pipeFrame = Time.frameCount;
            return null;
        }

        /// <summary>
        /// Takes the pipe up and comes to you as a Follow does; the moment you see it, it puts the pipe down.
        /// If never seen in PipeGiveUpSeconds, it puts it down unseen.
        /// </summary>
        private void UpdatePipe(float now, float dist)
        {
            if (anomalyStep == 0)
            {
                if (Time.frameCount <= pipeFrame) return;

                if (pipe == null || !agent.Hands.PickUp(pipe))
                {
                    EndAnomaly("it could not take the pipe up");
                    return;
                }
                agent.Hands.Turn(transform.rotation * PipeHold);
                anomalyStep = 1;
                YourBuddyPlugin.Log.LogInfo($"[anomaly] {Name} comes to you with a bloody pipe, {dist:0.0}m away");
                return;
            }
            if (pipe == null || agent.Hands.Item != pipe)
            {
                EndAnomaly("the pipe is out of its hands");
                return;
            }
            if (!anomalySeen && now < anomalyUntil) return;

            if (anomalySeen && dist < PipeStartleDist) Startle(15);

            agent.Hands.Drop(anomalySeen ? "you saw it" : "nobody saw");
            pipe = null;

            EndAnomaly(anomalySeen ? $"you spotted it {dist:0.0}m away - it put the pipe down" : "put the pipe down unseen");
        }
    }
}

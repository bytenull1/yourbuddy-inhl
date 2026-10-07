using Space;
using UnityEngine;

namespace YourBuddy
{
    public sealed partial class BuddyBehaviour
    {
        private Errand? choreOrder;
        private string choreName = "";
        private float choreCheckAt, choreTraceAt;
        private string? choreTrace;
        private string ChoreOrderStatus => "I'm on " + choreName + " duty. I'll keep checking for work.";

        // A standing task survives the individual batches it starts. docs/behaviour.md#2-persistence
        private string StartChoreOrder(Errand errand, string name)
        {
            if (BusyForCommand() is { } busy) return busy;
            ApplyOrder(BuddyMode.Stay);
            choreOrder = errand;
            choreName = name;
            choreTrace = null;
            TryOrderedChore();
            return Name + " will keep " + name + " until given another order";
        }

        private void StopChoreOrder()
        {
            if (choreOrder == null) return;
            choreOrder = null;
            orderedMode = null;
            YourBuddyPlugin.Log.LogInfo("[work] Continuing " + choreName + " order cancelled");
        }

        private void ContinueChoreOrder(Player player)
        {
            if (choreOrder == null) return;
            string? blocked = StandDownReason(player, ignoreOrder: true);
            if (blocked != null || TrySurvival() || Time.time < Mathf.Max(choreCheckAt, choreOrder.DeferredUntil)) return;
            TryOrderedChore();
        }

        // End non-storage legs too when the world interrupts the body. docs/behaviour.md#2-persistence
        private void InterruptCurrentErrand()
        {
            if (reachTask != null) FinishRoute();
        }

        private void TryOrderedChore()
        {
            if (choreOrder == null) return;
            bool started = choreOrder.TryStart(out string report);
            choreCheckAt = Time.time + (started ? 3f : Mathf.Max(3f, choreOrder.RetryDelay));
            if (started || report != choreTrace || Time.time >= choreTraceAt)
            {
                YourBuddyPlugin.Log.LogInfo("[work] Continuing " + choreName + ": " + report +
                    (started ? "" : $"; checking again in {choreCheckAt - Time.time:0}s"));
                choreTrace = report;
                choreTraceAt = Time.time + 30f;
            }
        }
    }
}

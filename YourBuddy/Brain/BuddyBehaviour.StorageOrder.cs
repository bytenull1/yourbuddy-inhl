using Space;
using UnityEngine;

namespace YourBuddy
{
    public sealed partial class BuddyBehaviour
    {
        private bool storeOrder;
        private string storeWait = "I'll check storage again shortly.";
        private string? storeTrace;
        private float storeTraceAt;
        internal string StoreOrderStatus => "I'm still on storage duty. " + storeWait;

        private void ReportStoreWait(string reply, string detail)
        {
            storeWait = reply;
            if (detail == storeTrace && Time.time < storeTraceAt) return;
            storeTrace = detail;
            storeTraceAt = Time.time + 30f;
            YourBuddyPlugin.Log.LogInfo("[store] Continuing order: " + detail);
        }

        internal string StartStoreNow()
        {
            if (BusyForCommand() is { } busy) return busy;
            ApplyOrder(BuddyMode.Stay);
            storeOrder = true;
            storeReturnAt = 0f;
            if (agent.IsAboardPlayerShip()) TryOrderedStore();
            else ReturnForStorage();
            return Name + " will keep storing ship supplies until given another order";
        }

        internal void StopStoreOrder()
        {
            StopChoreOrder();
            CancelStoreReturn();
            storing.CancelRepack();
            if (!storeOrder) { if (!storing.Running) storing.CloseStorageDoors(); return; }
            storeOrder = false;
            orderedMode = null;
            InterruptStoreJob();
            YourBuddyPlugin.Log.LogInfo("[store] Continuing store order cancelled");
        }

        private void InterruptStoreJob()
        {
            CancelStoreReturn();
            storing.CancelRepack();
            if (storing.Running) FinishRoute();
            storing.CloseStorageDoors();
        }

        private void TryOrderedStore()
        {
            // Unlike StartNow, continuing work respects skipped items. docs/storing.md
            if (storing.TryStart(out string report))
            {
                storeTrace = null;
                storeWait = "I'll check for the next item afterward.";
                return;
            }
            storing.DueAt = Time.time + storing.Interval;
            ReportStoreWait("Nothing I can store right now; I'll keep checking.", report);
        }
        private void ContinueStoreOrder(Player player)
        {
            CheckStoreReturn();
            string? blocked = StandDownReason(player, ignoreOrder: true);
            if (blocked != null)
            {
                ReportStoreWait("I'll resume when I'm free.", blocked);
                return;
            }
            if (TrySurvival()) return;
            if (!agent.IsAboardPlayerShip())
            {
                ReturnForStorage();
                return;
            }
            if (Time.time < storing.DueAt)
            {
                ReportStoreWait("I'll check for another item shortly.", "waiting for the next storage check");
                return;
            }
            TryOrderedStore();
        }
    }
}

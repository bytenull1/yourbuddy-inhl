using System.Collections.Generic;
using UnityEngine;

namespace YourBuddy
{
    internal sealed partial class StoreErrand
    {
        private readonly Dictionary<Transform, float> selectionLogAt = new();
        private readonly Dictionary<Transform, float> repackLogAt = new();
        private float scanLogAt, jobLogAt;
        private int jobNumber;
        private string jobState = "";

        private static string StorageLabel(FurnitureStorage? storage) => storage == null ? "loose/unknown" :
            $"{storage.Name}@{storage.Room.name} pos={storage.Root.position}";

        private void SelectionLog(Grabbable item, string reason)
        {
            if (selectionLogAt.TryGetValue(item.transform, out float until) && Time.time < until) return;
            if (selectionLogAt.Count >= 512) selectionLogAt.Clear();
            selectionLogAt[item.transform] = Time.time + 30f;
            YourBuddyPlugin.Log.LogInfo($"[store-detail] t={Time.time:0.0} item={Items.ItemLabelOf(item)} pos={item.transform.position} selection={reason}");
        }

        private bool RepackRefused(FurnitureStorage target, string reason)
        {
            if (!repackLogAt.TryGetValue(target.Root, out float until) || Time.time >= until)
            {
                if (repackLogAt.Count >= 128) repackLogAt.Clear();
                repackLogAt[target.Root] = Time.time + 5f;
                YourBuddyPlugin.Log.LogInfo($"[store-detail] job={jobNumber} repack refused: {reason}; target={StorageLabel(target)} source={StorageLabel(source)}");
            }
            return false;
        }

        private void JobLog(Leg task, string? outcome = null)
        {
            string state = $"{task.Stage}/extract={task.Extracting}/insert={task.Reaching}/released={task.Released}/turn={task.Turning}/segment={task.ReachIndex}";
            if (outcome == null && state == jobState && Time.time < jobLogAt) return;
            jobState = state;
            jobLogAt = Time.time + 5f;
            Vector3 reach = task.ReachIndex < task.ReachPoints.Length ? task.ReachPoints[task.ReachIndex] : task.TargetPoint;
            YourBuddyPlugin.Log.LogInfo($"[store-detail] t={Time.time:0.0} job={jobNumber} {outcome ?? "progress"} state={state} " +
                $"item={Items.ItemLabelOf(task.Item)} itemPos={task.Item.transform.position} buddy={Here} held={Body.Hands.Item == task.Item} " +
                $"source={StorageLabel(source)} target={StorageLabel(task.Destination)} plannedLocal={task.LocalPoint} reach={reach} " +
                $"handDistance={Body.Hands.DistanceTo(reach):0.000} orientation={orientationIndex} probe={probeIndex} " +
                $"half={half} remaining={deadline - Time.time:0.0}s doorWait={task.Until - Time.time:0.0}s " +
                $"blockedWait={task.BlockedUntil - Time.time:0.0}s lastBlocker={task.Destination?.LastBlocker ?? source?.LastBlocker ?? "none"}");
        }
    }
}

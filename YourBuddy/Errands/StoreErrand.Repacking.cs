using System.Collections.Generic;
using UnityEngine;

namespace YourBuddy
{
    internal sealed partial class StoreErrand
    {
        private StorageRepackPlan? repack;
        private readonly SkipList repackSkips = new();

        internal void CancelRepack(string reason = "order cancelled or interrupted")
        {
            if (repack == null) return;
            YourBuddyPlugin.Log.LogInfo($"[store] Repacking ended: {reason}; staged supplies remain available in overflow");
            repack = null;
        }

        private bool TryBeginRepack(Leg task, FurnitureStorage target)
        {
            Grabbable incoming = task.Item;
            if (repack != null) return RepackRefused(target, "sequence already active");
            if (target.Overflow != null) return RepackRefused(target, "target is overflow");
            if (!target.Available) return RepackRefused(target, "target unavailable");
            if (repackSkips.Has(target.Root)) return RepackRefused(target, "target repack cooldown");
            if (Body.TakenByAnother(target.Root)) return RepackRefused(target, "target reserved by another buddy");
            bool food = incoming.GetComponent<Food>() != null;
            bool resource = incoming.GetComponent<ResourceContainer>() != null;
            if (StoragePolicy.Home(food, resource, target.Name) != BestHome(incoming)) return RepackRefused(target, "not the best home category");
            SpaceShip? ship = GameManager.Instance.PlayerShip;
            if (ship == null) return false;
            List<FurnitureStorage> staging = [];
            StorageOverflow.Collect(ship, staging);
            staging.RemoveAll(c => !c.Available || c.Overflow == null || c.Overflow.CandidateCount == 0 || Body.TakenByAnother(c.Root));
            staging.Sort((a, b) => (a.Overflow?.Priority ?? 0).CompareTo(b.Overflow?.Priority ?? 0));
            if (staging.Count == 0)
            {
                YourBuddyPlugin.Log.LogInfo("[store] Cannot make room: no available overflow area");
                return false;
            }
            putAway.Clear();
            if (!target.ReadContents(putAway)) return RepackRefused(target, "cannot read target contents");
            List<Grabbable> contents = [];
            foreach (Grabbable item in putAway)
            {
                if (item == null) continue;
                YourBuddyPlugin.Log.LogInfo($"[store-detail] repack content={Items.ItemLabelOf(item)} pos={item.transform.position} incoming={item == incoming} usable={StorageItems.CanStore(item)} blocker={TakeBlocker(item) ?? "none"} cooldown={Skips.Has(item.transform)} untidy={target.NeedsArrange(item)} blocksOpening={item == target.LastBlockingItem}");
                if (item == incoming || !StorageItems.CanStore(item) || TakeBlocker(item) != null ||
                    Skips.Has(item.transform)) continue;
                int home = StoragePolicy.Home(item.GetComponent<Food>() != null,
                    item.GetComponent<ResourceContainer>() != null, target.Name);
                if (item != target.LastBlockingItem && !target.NeedsArrange(item) &&
                    !StoragePolicy.ShouldMove(home, BestHome(item))) continue;
                contents.Add(item);
                if (contents.Count == StorageRepackPlan.MaxContents) break;
            }
            if (contents.Count == 0)
            {
                YourBuddyPlugin.Log.LogInfo("[store] Cannot make room: no eligible untidy, misplaced or blocking supplies");
                return false;
            }
            repack = new StorageRepackPlan(target.Root, incoming, contents, Time.time);
            repackSkips.Skip(target.Root, 300f);
            if (source?.Overflow != null && Body.Hands.Item != incoming)
            {
                repack.Advance(incoming, true);
                task.Completed = true;
                DueAt = Time.time + Interval;
                YourBuddyPlugin.Log.LogInfo("[store] Making room: incoming item already in overflow; clearing preferred storage first");
                Body.FinishRoute();
                return true;
            }
            containers.Clear();
            containers.AddRange(staging);
            containerIndex = probeIndex = orientationIndex = 0;
            YourBuddyPlugin.Log.LogInfo($"[store] Making room in {target.Name}: stage incoming item, move {contents.Count} supplies, then refill");
            return true;
        }

        private bool RepackEvacuating(Grabbable item) => repack?.Next is { ToOverflow: true } next && next.Item == item;

        private void SelectRepackItem()
        {
            repackSkips.Prune();
            if (repack == null) return;
            if (Time.time >= repack.ExpiresAt) { CancelRepack("sequence deadline expired"); return; }
            if (repack.Root == null) { CancelRepack("target removed"); return; }
            if (repack.Next is not { } next) { CancelRepack("no queued move"); return; }
            if (!items.Contains(next.Item)) { CancelRepack("next item missing or excluded; see selection trace"); return; }
            bool targetAvailable = false;
            foreach (FurnitureStorage container in containers)
                if (container.Root == repack.Root && container.Available && !Body.TakenByAnother(container.Root)) targetAvailable = true;
            if (!targetAvailable) { CancelRepack("target unavailable or reserved"); return; }
            if (!sources.TryGetValue(next.Item, out FurnitureStorage? current) ||
                (next.ToOverflow ? current.Root != repack.Root : current.Overflow == null))
            { CancelRepack("next item is not in its expected source area"); return; }
            items.Remove(next.Item);
            items.Insert(0, next.Item);
        }

        private void RepackStored(Grabbable item, FurnitureStorage destination)
        {
            if (repack == null) return;
            if (!repack.Advance(item, destination.Overflow != null)) { CancelRepack("completed move does not match queued item/destination"); return; }
            YourBuddyPlugin.Log.LogInfo($"[store-detail] repack advanced; next={(repack.Next is { } next ? Items.ItemLabelOf(next.Item) + (next.ToOverflow ? " -> overflow" : " -> normal storage") : "complete")}");
            if (repack.Next != null) return;
            repack = null;
            YourBuddyPlugin.Log.LogInfo("[store] Repacking complete; original item stored");
        }
    }
}

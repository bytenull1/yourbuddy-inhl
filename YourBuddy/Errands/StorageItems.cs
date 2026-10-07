namespace YourBuddy
{
    // Only recognised usable supplies enter storage. docs/storing.md
    internal static class StorageItems
    {
        internal static bool CanStore(Grabbable item)
        {
            if (Items.IsTrash(item) || item.Signature == "TRASH_BOX") return false;
            if (item.GetComponent<Food>() is { } food) return food.Data is { usages: > 0 };
            if (item.GetComponent<ResourceContainer>() is { } cell) return cell.Data != null && cell.Value > 0;
            if (item.GetComponent<SeedPack>() is { } seeds) return seeds.Data != null && seeds.Seeds > 0;
            if (item.GetComponent<AidKit>() is { } aid) return aid.Data is { value: > 0 };
            if (item.GetComponent<RepairKit>() is { } repair) return repair.Data is { value: > 0 };
            return false;
        }
    }
}

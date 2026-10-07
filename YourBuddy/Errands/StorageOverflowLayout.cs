namespace YourBuddy
{
    // Room-local side rows. Scene geometry and runtime clearance own the final fit.
    internal static class StorageOverflowLayout
    {
        internal readonly record struct Area(string Room, float X, float Z, float ApproachX, int Slots, int Priority, bool Legacy = false);
        internal static readonly Area[] Areas =
        [
            new("Front_L06", 3.8f, -4f, 2.9f, 3, 0),
            new("Core_R04", -3.85f, .025f, -2.95f, 2, 1),
            new("Front_M00", 1f, -5f, 0f, 2, 2),
            // Recognise old staged items for collection without placing more in the centre.
            new("Core_R04", -2.715f, -.116f, -2.715f, 9, 3, true),
            new("Front_L06", 2.696f, -3.671f, 2.696f, 9, 3, true),
            new("Front_M00", -.063f, -3.764f, -.063f, 9, 3, true)
        ];
        internal static int ProbeCount(int slots) => (slots * 2 - 1) * 3;
        internal static (float X, float Z) ProbeOffset(int index, int slots)
        {
            int rows = slots * 2 - 1;
            int lane = index / rows, row = index % rows;
            float z = row < slots ? RowOffset(row, slots) :
                (RowOffset(row - slots, slots) + RowOffset(row - slots + 1, slots)) * .5f;
            return (lane == 0 ? 0f : lane == 1 ? -.15f : .15f, z);
        }
        internal static float RowOffset(int slot, int count) => (slot - (count - 1) * .5f) * StoragePolicy.OverflowSpacing;
    }
}

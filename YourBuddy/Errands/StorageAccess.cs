using System;
namespace YourBuddy
{
    // Keep the item in its slot lane while crossing the opening. docs/storing.md
    internal static class StorageAccess
    {
        internal static float LiftHalfHeight(float measuredHalf) => System.Math.Max(.001f, measuredHalf - .002f);

        internal static float OutsideDoor(float current, float doorCenter, float doorHalf, float itemHalf) =>
            Math.Max(current, doorCenter + doorHalf + itemHalf + .08f);

        internal readonly record struct Portal(float OuterX, float OuterZ, float InnerX, float InnerZ);
        internal readonly record struct Slot(float X, float Z);

        internal static Slot PackingSlot(float faceX, float faceZ, float width, float depth,
            float halfX, float halfZ, int index)
        {
            int columns = StoragePolicy.ProbeColumns(width, halfX);
            int rows = StoragePolicy.ProbeColumns(depth, halfZ);
            if (columns == 0 || rows == 0) return default;
            bool alongX = Math.Abs(faceX) / width > Math.Abs(faceZ) / depth;
            int xIndex = alongX ? (index / rows) % columns : index % columns;
            int zIndex = alongX ? index % rows : (index / columns) % rows;
            float x = StoragePolicy.PackingOffset(xIndex, width, halfX);
            float z = StoragePolicy.PackingOffset(zIndex, depth, halfZ);
            // The row furthest from the opening comes first. docs/storing.md
            if (alongX && faceX < 0) x = -x;
            if (!alongX && faceZ < 0) z = -z;
            return new Slot(x, z);
        }

        internal static bool OnPackingGrid(float faceX, float faceZ, float width, float depth,
            float halfX, float halfZ, float x, float z)
        {
            int count = StoragePolicy.ProbeColumns(width, halfX) * StoragePolicy.ProbeColumns(depth, halfZ);
            for (int i = 0; i < count; i++)
            {
                Slot slot = PackingSlot(faceX, faceZ, width, depth, halfX, halfZ, i);
                if (Math.Abs(x - slot.X) <= .05f && Math.Abs(z - slot.Z) <= .05f) return true;
            }
            return false;
        }

        internal static Portal ThroughFace(float faceX, float faceZ, float width, float depth,
            float halfX, float halfZ, float targetX, float targetZ)
        {
            if (width <= 0 || depth <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            bool alongX = Math.Abs(faceX) / width > Math.Abs(faceZ) / depth;
            return alongX
                ? new Portal(Math.Sign(faceX) * (width * .5f + halfX + .08f), targetZ, targetX, targetZ)
                : new Portal(targetX, (faceZ < 0 ? -1 : 1) * (depth * .5f + halfZ + .08f), targetX, targetZ);
        }
    }
}

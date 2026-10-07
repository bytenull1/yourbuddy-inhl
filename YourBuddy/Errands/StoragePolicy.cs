using System;
namespace YourBuddy
{
    // Preference is separate from physical capacity. docs/storing.md
    internal static class StoragePolicy
    {
        internal static int Rank(bool food, bool resource, string container, bool sameKind, bool empty)
        {
            if (container == "fridge") return food ? 0 : int.MaxValue;
            if (sameKind) return 10;
            int type = resource ? (container == "chest" ? 0 : 1) : (container == "cabinet" ? 0 : 1);
            return (empty ? 20 : 30) + type;
        }

        internal static bool PoseFits(float dx, float dy, float dz, float angle, bool overflow)
        {
            if (angle > 8f) return false;
            // Use grid tolerances horizontally and allow release clearance to settle vertically.
            return overflow
                ? Math.Abs(dx) <= .08f && Math.Abs(dy) <= .08f && Math.Abs(dz) <= .08f
                : Math.Abs(dx) <= .05f && Math.Abs(dy) <= .04f && Math.Abs(dz) <= .05f;
        }

        internal const float OverflowSpacing = .65f;
        internal static bool OnOverflowGrid(float x, float z)
        {
            float column = (float)Math.Round(x / OverflowSpacing), row = (float)Math.Round(z / OverflowSpacing);
            return Math.Abs(column) <= 1 && Math.Abs(row) <= 1 &&
                Math.Abs(x - column * OverflowSpacing) <= .08f && Math.Abs(z - row * OverflowSpacing) <= .08f;
        }

        internal static int Home(bool food, bool resource, string container) => container switch
        {
            "overflow area" => 100,
            "fridge" => food ? 0 : int.MaxValue,
            "chest" => resource ? 0 : food ? 20 : 10,
            "cabinet" => food ? 10 : resource ? 10 : 0,
            "locker" => 20,
            _ => int.MaxValue
        };

        internal static bool ShouldMove(int currentHome, int bestHome) => currentHome > bestHome;

        internal static int ProbeColumns(float size, float half)
        {
            int columns = Columns(size, half);
            return columns == 0 ? 0 : columns == 1 ? 1 : columns <= 3 ? 3 : 5;
        }

        internal static float PackingOffset(int index, float size, float half)
        {
            int fitting = Columns(size, half);
            if (fitting <= 1 || index >= fitting) return 0;
            float span = Math.Max(0, size * .5f - half - .01f);
            return -span + index * (half * 2 + .02f);
        }

        internal static int Columns(float size, float half)
        {
            if (half <= 0 || size < half * 2) return 0;
            return Math.Min(5, Math.Max(1, (int)Math.Floor(size / (half * 2 + .02f))));
        }
    }
}

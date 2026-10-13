using System;

namespace YourBuddy
{
    public sealed class ResourceRule
    {
        public bool Enabled { get; set; }
        internal const int Start = 30, Target = 80, MinCells = 1, BuyQuantity = 1;
        internal bool NeedsRefill(float percent, bool pending) =>
            Enabled && !float.IsNaN(percent) && percent >= 0 && percent < Target && (pending || percent <= Start);
    }

    // Shared by the save, not multiplied by the number of buddies. docs/resources.md
    public sealed class ResourceDutySettings
    {
        public bool Buying { get; set; }
        public int Budget { get; set; }
        public ResourceRule Oxygen { get; set; } = new();
        public ResourceRule Fuel { get; set; } = new();
        public ResourceRule Energy { get; set; } = new();
        internal bool Paused;
        // One cell costs 750-1050 at the default price multiplier.
        private static readonly int[] Limits = [0, 1500, 3000, 6000, 12000];

        internal ResourceRule Rule(int kind) => kind switch { 0 => Oxygen, 1 => Fuel, _ => Energy };
        internal bool CanBuy(ResourceRule rule, int price, int cash) =>
            !Paused && rule.Enabled && Buying && price > 0 && price <= Budget && price <= cash;

        internal void CycleBudget()
        {
            foreach (int limit in Limits)
            {
                if (limit <= Budget) continue;
                Budget = limit;
                return;
            }
            Budget = 0;
        }

        internal void Normalize()
        {
            Oxygen ??= new();
            Fuel ??= new();
            Energy ??= new();
            Budget = Math.Max(0, Budget);
        }
    }
}

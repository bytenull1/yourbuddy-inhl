namespace YourBuddy
{
    internal static class BuddyResourceDebug
    {
        private const string Usage = "Usage: buddy_dev drain <oxygen|fuel|energy>";

        internal static string Drain(string[] args)
        {
            if (args.Length != 1) return Usage;

            int kind = ResourceKinds.Of(args[0]);
            if (kind < 0) return Usage;

            string resource = ResourceKinds.Label(kind);
            SpaceShip? ship = GameManager.Instance != null ? GameManager.Instance.PlayerShip : null;
            if (ship == null) return "No player ship is available.";

            if (!ResourceKinds.Levels(ship, kind, out int capacity, out int current)) return resource + " controller unavailable.";

            if (capacity <= 0) return "No capacity available for " + resource + ".";

            int amount = current - capacity / 5;
            if (amount <= 0) return resource + " is already at or below 20%.";

            return ResourceKinds.Reduce(ship, kind, amount) ? resource + " drained to 20%; resource settings unchanged."
                : "Could not fully drain " + resource + "; check functional tanks or batteries.";
        }
    }
}

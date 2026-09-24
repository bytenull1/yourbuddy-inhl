using System.Collections.Generic;

namespace YourBuddy
{
    /// <summary>
    /// Serializable state for the '.buddy' sidecar written next to game saves.
    /// The vanilla game never reads it, so uninstalling the mod is safe. Every buddy
    /// resumes in Follow. docs/architecture.md §5
    /// </summary>
    public sealed record BuddySaveFile
    {
        /// <summary>
        /// Every buddy. Null in a sidecar written before there could be several: the single-buddy
        /// fields below are then the one buddy.
        /// </summary>
        public BuddyState[]? Buddies { get; init; }

        // The first buddy again, so a mod version from before Buddies still restores one.
        public bool Exists { get; init; }
        public bool Alive { get; init; }
        public string? Owner { get; init; }
        public float[]? OwnerLocalPosition { get; init; }
        public float[]? ShipLocalPosition { get; init; }
        public float[]? WorldPosition { get; init; }
        public float[]? Rotation { get; init; }
        public string? SleepingCapsule { get; init; }

        /// <summary>
        /// Door codes the player told the buddies; every buddy knows all of them.
        /// docs/invariants.md#door-knowledge-is-shared
        /// </summary>
        public int[]? KnownPinCodes { get; init; }
        /// <summary>
        /// The prop cryo capsule opened for the buddy on a new game, kept open on every load.
        /// Written even when no buddy exists. docs/game-model.md#the-cryo-room
        /// </summary>
        public string? OpenedCapsule { get; init; }

        internal static BuddySaveFile Of(IReadOnlyList<BuddyState> buddies, int[] codes, string? openedCapsule)
        {
            BuddySaveFile data = buddies.Count == 0
                ? new BuddySaveFile { Exists = false }
                : new BuddySaveFile
                {
                    Exists = true,
                    Alive = buddies[0].Alive,
                    Owner = buddies[0].Owner,
                    OwnerLocalPosition = buddies[0].OwnerLocalPosition,
                    ShipLocalPosition = buddies[0].ShipLocalPosition,
                    WorldPosition = buddies[0].WorldPosition,
                    Rotation = buddies[0].Rotation,
                    SleepingCapsule = buddies[0].SleepingCapsule
                };
            return data with { Buddies = [.. buddies], KnownPinCodes = codes, OpenedCapsule = openedCapsule };
        }

        /// <summary>
        /// The buddies this file holds, from either layout.
        /// </summary>
        internal static BuddyState[] StatesOf(BuddySaveFile data)
        {
            if (data.Buddies != null) return data.Buddies;

            if (!data.Exists) return [];

            return
            [
                new BuddyState
                {
                    Number = 1,
                    Alive = data.Alive,
                    Owner = data.Owner,
                    OwnerLocalPosition = data.OwnerLocalPosition,
                    ShipLocalPosition = data.ShipLocalPosition,
                    WorldPosition = data.WorldPosition,
                    Rotation = data.Rotation,
                    SleepingCapsule = data.SleepingCapsule
                }
            ];
        }
    }

    /// <summary>
    /// One buddy in the sidecar.
    /// </summary>
    public sealed record BuddyState
    {
        /// <summary>
        /// Its console number and name, kept so "@2" and "Buddy 2" survive a load.
        /// </summary>
        public int Number { get; init; }
        public string? Name { get; init; }
        public bool Alive { get; init; }
        /// <summary>
        /// The vessel the buddy was standing on - "ship", "world" or a station's
        /// GameObject name, the same key the game itself uses for SaveData.dockedStation
        /// - and its position in that vessel's own space. A ship-local point is a world
        /// point in disguise, so without these a buddy left on a station reloads into
        /// vacuum. docs/invariants.md#the-buddy-rides-its-own-floor
        /// </summary>
        public string? Owner { get; init; }
        public float[]? OwnerLocalPosition { get; init; }
        public float[]? ShipLocalPosition { get; init; }
        public float[]? WorldPosition { get; init; }
        public float[]? Rotation { get; init; }
        /// <summary>
        /// The prop capsule the buddy was still asleep in; it sleeps on after a load.
        /// </summary>
        public string? SleepingCapsule { get; init; }
    }

}

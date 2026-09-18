namespace YourBuddy
{
    /// <summary>
    /// Serializable buddy state for the '.buddy' sidecar written next to game saves.
    /// The vanilla game never reads it, so uninstalling the mod is safe. The buddy
    /// always resumes in Follow. docs/architecture.md §5
    /// </summary>
    public sealed record BuddySaveFile
    {
        public bool Exists { get; init; }
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
        /// Door codes the player told the buddy.
        /// </summary>
        public int[]? KnownPinCodes { get; init; }
        /// <summary>
        /// The prop cryo capsule opened for the buddy on a new game, kept open on every load.
        /// Written even when no buddy exists. docs/game-model.md#the-cryo-room
        /// </summary>
        public string? OpenedCapsule { get; init; }
        /// <summary>
        /// The prop capsule the buddy was still asleep in; it sleeps on after a load.
        /// </summary>
        public string? SleepingCapsule { get; init; }
    }

}

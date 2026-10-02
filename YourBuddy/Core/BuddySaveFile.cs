using System.Collections.Generic;

namespace YourBuddy
{
    /// <summary>
    /// Serializable state for the '.buddy' sidecar NPC.Core writes next to game saves.
    /// The vanilla game never reads it, so uninstalling the mod is safe. Every buddy
    /// resumes in Follow. docs/architecture.md §5
    /// </summary>
    public sealed record BuddySaveFile
    {
        /// <summary>
        /// Every buddy. Null in an older single-buddy sidecar, where the fields below hold the
        /// one buddy.
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
        public uint SuitId { get; init; }

        /// <summary>
        /// The door codes, which NPC.Core also saves in its own '.npccore'. A sidecar from before NPC.Core
        /// kept them has only these, so a load still merges them. npc-core:docs/invariants.md#door-knowledge-is-shared
        /// </summary>
        public int[]? KnownPinCodes { get; init; }
        /// <summary>
        /// The prop cryo capsule opened for the buddy on a new game, kept open on every load.
        /// Written even when no buddy exists. docs/game-model.md#the-cryo-room
        /// </summary>
        public string? OpenedCapsule { get; init; }
        /// <summary>
        /// The anomalies this save has had, by kind name, and every line the buddies said in
        /// them. Written even when no buddy exists. docs/anomalies.md#once-per-save
        /// </summary>
        public string[]? AnomaliesHappened { get; init; }
        public string[]? LinesSaid { get; init; }

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
                    SleepingCapsule = buddies[0].SleepingCapsule,
                    SuitId = buddies[0].SuitId
                };
            return data with
            {
                Buddies = [.. buddies],
                KnownPinCodes = codes,
                OpenedCapsule = openedCapsule,
                AnomaliesHappened = AnomalyMemory.HappenedNow,
                LinesSaid = AnomalyMemory.SaidNow
            };
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
                    SleepingCapsule = data.SleepingCapsule,
                    SuitId = data.SuitId
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
        /// The vessel the buddy stood on ("ship", "world" or a station's GameObject name, the key
        /// SaveData.dockedStation uses) and its position in that vessel's space. Without these a
        /// buddy left on a station reloads into vacuum. npc-core:docs/invariants.md#an-npc-rides-its-own-floor
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
        /// <summary>
        /// Item id of the worn suit. The worn item is inactive, so a load needs the id to give it
        /// back. docs/eva.md
        /// </summary>
        public uint SuitId { get; init; }
        /// <summary>
        /// Why that suit was on (`Ordered`, `Survival`). Only a survival suit comes off by itself,
        /// so a load must keep it. Missing restores as survival. docs/eva.md
        /// </summary>
        public string? SuitReason { get; init; }
        /// <summary>
        /// Outside the airlocks. After a load only a cycle would tell this again.
        /// npc-core:docs/invariants.md#an-airlock-is-crossed-by-its-cycle
        /// </summary>
        public bool Outside { get; init; }
        /// <summary>
        /// ExitGravity of the airlock it went out through; 0 loads it floating. Missing (an older
        /// sidecar) loads it on a walkable surface. npc-core:docs/agent.md#8-floating
        /// </summary>
        public float? Gravity { get; init; }
    }

}

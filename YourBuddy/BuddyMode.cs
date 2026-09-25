namespace YourBuddy
{
    public enum BuddyMode
    {
        Follow,
        Wander,
        Route,
        /// <summary>
        /// Holds position until told otherwise. Still steps out of a doorway it blocks:
        /// npc-core:docs/invariants.md#step-off-applies-in-every-mode
        /// </summary>
        Stay,
        /// <summary>
        /// Owned by the fear system, never ordered: docs/invariants.md#fear-owns-the-buddy
        /// </summary>
        Flee,
        Dead
    }

    /// <summary>
    /// How the buddy feels about the Breathless. docs/fear.md
    /// </summary>
    public enum FearState
    {
        Calm,
        /// <summary>
        /// Faces it and will not walk toward it; the mode is otherwise untouched.
        /// </summary>
        Alert,
        /// <summary>
        /// Flees. docs/fear.md
        /// </summary>
        Scared
    }

    /// <summary>
    /// How long a player's order holds. docs/behaviour.md
    /// </summary>
    public enum OrderPersistence
    {
        UntilRevoked,
        Expires
    }

}

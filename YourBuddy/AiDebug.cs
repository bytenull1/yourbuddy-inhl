namespace YourBuddy
{
    /// <summary>
    /// Debug overrides for the buddy's and the monster's AI, owned in one place so a
    /// coroutine that saves and restores a flag cannot silently undo a command.
    /// Everything here is runtime component state: never Breathless.Enabled or
    /// BreathlessController.SetAggressive, both of which write into the game's own save
    /// file. docs/invariants.md#mod-state-never-enters-the-vanilla-save
    /// </summary>
    internal static class AiDebug
    {
        internal static bool BuddyDisabled;
        internal static bool MonsterDisabled;
        internal static bool NoTarget;

        /// <summary>
        /// Something we switched off is still off, so it has to be switched back on
        /// exactly once when the override is lifted.
        /// </summary>
        private static bool _huntSuppressed;
        private static bool _idleSuppressed;

        internal static bool Any => BuddyDisabled || MonsterDisabled || NoTarget;

        /// <summary>
        /// The monster may not target the player. notarget means exactly this, and it is
        /// what the two DetectItem prefixes and the aggressor flag key off.
        /// The buddy is deliberately still fair game: the game's own detectors never see
        /// it (no Player component), so its catch runs from BuddyBehaviour.BreathlessCheck
        /// and only a full ai_disable stops that. docs/reference.md
        /// </summary>
        internal static bool PlayerIgnored => MonsterDisabled || NoTarget;

        /// <summary>
        /// Kept under its old name for CatchRoutine, which asks before restoring the
        /// aggressor flag it borrowed.
        /// </summary>
        internal static bool MonsterHuntSuppressed => PlayerIgnored;

        /// <summary>
        /// Re-asserted periodically, because the game turns these back on by itself.
        /// Only the suppressed state is written continuously - re-enabling every tick
        /// would fight whatever the game does with its own monster.
        /// </summary>
        internal static void Apply()
        {
            Breathless? breathless = GameManager.Instance != null ? GameManager.Instance.Breathless : null;
            if (breathless == null) return;

            // The aggressor homes on the player every FixedUpdate with no perception gate
            // of its own, so blinding it means switching it off. Its catch trigger is a
            // UnityEvent that fires regardless; that is filtered in the DetectItem prefix.
            bool hunt = PlayerIgnored;
            BreathlessAggressor aggressor = breathless.Aggressor;
            if (aggressor != null && (hunt || _huntSuppressed)) aggressor.enabled = !hunt;

            BreathlessController controller = breathless.Controller;
            if (controller != null && (MonsterDisabled || _idleSuppressed)) controller.enabled = !MonsterDisabled;

            _huntSuppressed = hunt;
            _idleSuppressed = MonsterDisabled;
        }

        /// <summary>
        /// A loaded save is a new scene with a new monster, so an override set for the
        /// last one is dropped rather than carried over. The current monster is restored
        /// first, since a load can still be refused and leave this scene running; the
        /// suppressed marks are then dropped so nothing is ever forced on in the new one.
        /// </summary>
        internal static void Reset()
        {
            if (!Any && !_huntSuppressed && !_idleSuppressed) return;

            YourBuddyPlugin.Log.LogInfo("[debug] AI overrides cleared by save load (were: " + Describe() + ")");
            BuddyDisabled = false;
            MonsterDisabled = false;
            NoTarget = false;
            Apply();
            _huntSuppressed = false;
            _idleSuppressed = false;
        }

        internal static string Describe()
        {
            return "buddy AI " + (BuddyDisabled ? "OFF" : "on") +
                   ", monster AI " + (MonsterDisabled ? "OFF" : "on") +
                   ", notarget " + (NoTarget ? "ON (player only - the buddy is still hunted)" : "off");
        }
    }
}

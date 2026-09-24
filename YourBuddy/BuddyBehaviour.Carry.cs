using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The buddy's side of items: its hands, putting down before a save, and which vessel a candidate
    /// is on. Holding one is BuddyHands; the item rules are Items. docs/items.md
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        private BuddyHands hands = null!; // created in Init, before the first frame

        private void LateUpdate()
        {
            using BuddyManager.ActingScope _ = BuddyManager.Acting(this);
            hands.Follow(MoveSpeed);
        }

        /// <summary>
        /// Before the game writes the save: docs/invariants.md#a-carried-item-is-put-down-before-a-save
        /// </summary>
        private void OnGameSaving()
        {
            using BuddyManager.ActingScope _ = BuddyManager.Acting(this);
            hands.Drop("the game is saving");
        }

        /// <summary>
        /// Whether a candidate belongs to the vessel the buddy is riding. The enlarged search radii
        /// (docs/reference.md) only stay safe with this: at 30 m a docked station is well in range.
        /// A non-answer allows it - docs/invariants.md#a-missing-floor-is-a-last-resort-not-an-answer.
        /// </summary>
        private bool OnMyVessel(Transform what)
        {
            if (CurrentOwner == null || what == null) return true;

            // The object's own ancestry first; only a thing the game has not re-owned needs a probe.
            string? owner = BuddyManager.OwnerOfTransform(what);
            if (owner == null) BuddyManager.FloorOwner(what.position, out owner, out _);

            return owner == null || owner == CurrentOwner;
        }

    }
}

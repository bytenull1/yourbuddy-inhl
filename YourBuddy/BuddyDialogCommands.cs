using System;
using System.Collections.Generic;
using System.Globalization;

namespace YourBuddy
{
    /// <summary>
    /// Turns what the player typed (or clicked) into one of the orders in
    /// BuddyCommands. Keyword matching, like the game's own AssistanceBot, so
    /// "follow me" and "follow" both work. See docs/dialog.md.
    /// </summary>
    internal static class BuddyDialogCommands
    {
        /// <summary>
        /// The list shown on the commands page, in the order it is drawn.
        /// </summary>
        internal static readonly string[] Names =
            ["Follow", "Wander", "Stay", "Hide", "Tidy", "Sell", "Play", "Snack", "Goto", "Decide", "Password"];

        private static readonly string[] GotoWords = ["goto", "go to", "walk", "move to"];
        /// <summary>
        /// Words that give an order to every buddy at once, not only the one being talked to.
        /// </summary>
        private static readonly string[] GroupWords = ["everyone", "everybody", "all of you", "both of you"];

        /// <summary>
        /// `buddy` is the one being talked to; a group word hands each order to every living, awake buddy.
        /// </summary>
        internal static string Run(BuddyBehaviour buddy, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "...";

            string lower = text.Trim().ToLowerInvariant();
            List<BuddyBehaviour> targets = Targets(buddy, ref lower);

            // First: "decide for yourself whether to follow" is not a follow order.
            if (Has(lower, "decide", "yourself", "autonom", "your call", "own mind")) return ForAll(targets, BuddyCommands.DecideForYourself);

            // Before the rest, so "go to the workshop" is not a wander order ("work"). A goto that
            // names no room falls through, so "stay, do not walk" is still a stay order.
            if (Has(lower, GotoWords) && BuddyRooms.TryResolve(lower, out BuddyRooms.Entry? room, out List<BuddyRooms.Entry>? several))
            {
                return room != null
                    ? ForAll(targets, b => BuddyCommands.GoToRoom(b, room))
                    : "Which one? " + BuddyRooms.Join(several!); // several is set when room is not
            }

            // Before Follow, so "come and hide" is not a follow order.
            if (Has(lower, "hide", "closet", "locker", "conceal")) return ForAll(targets, BuddyCommands.Hide);

            if (Has(lower, "follow", "come", "heel")) return ForAll(targets, BuddyCommands.Follow);

            if (Has(lower, "job", "wander", "own thing", "work", "busy")) return ForAll(targets, BuddyCommands.Wander);

            if (Has(lower, "stay", "wait", "hold", "stop", "halt")) return ForAll(targets, BuddyCommands.Stay);

            // Sell before tidy: "trash box" contains "trash".
            if (Has(lower, "sell", "trash box", "money", "cash")) return ForAll(targets, BuddyCommands.Sell);

            if (Has(lower, "tidy", "clean", "trash", "rubbish", "garbage", "litter", "bin")) return ForAll(targets, BuddyCommands.Tidy);

            if (Has(lower, "play", "toy")) return ForAll(targets, BuddyCommands.Play);

            if (Has(lower, "snack", "eat", "food", "hungry")) return ForAll(targets, BuddyCommands.Snack);

            if (Has(lower, GotoWords)) return BuddyRooms.Prompt();

            // Codes are shared, so a password is told once, whoever hears it. docs/invariants.md#door-knowledge-is-shared
            if (Has(lower, "password", "code", "pin", "key"))
            {
                return TryNumber(lower, out int code)
                    ? BuddyCommands.GivePassword(code.ToString(CultureInfo.InvariantCulture))
                    : "Give me the digits too, like 'password 1423'.";
            }

            // A bare number is almost always a door code being handed over.
            if (int.TryParse(lower, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bare))
            {
                return BuddyCommands.GivePassword(bare.ToString(CultureInfo.InvariantCulture));
            }

            return "I don't know that one. Try: " + string.Join(", ", Names);
        }

        /// <summary>
        /// The buddy talked to, or with a group word (removed from `lower`) every living, awake buddy.
        /// </summary>
        private static List<BuddyBehaviour> Targets(BuddyBehaviour buddy, ref string lower)
        {
            bool group = false;
            foreach (string word in GroupWords)
            {
                if (lower.IndexOf(word, StringComparison.Ordinal) < 0) continue;

                lower = lower.Replace(word, " ");
                group = true;
            }
            if (!group) return [buddy];

            List<BuddyBehaviour> all = [];
            foreach (BuddyBehaviour other in BuddyManager.Snapshot())
            {
                if (other != null && !other.IsDead && !other.Asleep) all.Add(other);
            }
            return all;
        }

        /// <summary>
        /// One reply line per buddy, each order run as that buddy.
        /// </summary>
        private static string ForAll(List<BuddyBehaviour> targets, Func<BuddyBehaviour, string> order)
        {
            if (targets.Count == 0) return "Nobody is awake to hear that";

            List<string> replies = [];
            foreach (BuddyBehaviour target in targets)
            {
                using BuddyManager.ActingScope _ = BuddyManager.Acting(target);
                replies.Add(order(target));
            }
            return string.Join("\n", replies);
        }

        private static bool Has(string text, params string[] keywords)
        {
            foreach (string k in keywords)
            {
                if (text.IndexOf(k, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        /// <summary>
        /// First run of digits in the text.
        /// </summary>
        private static bool TryNumber(string text, out int value)
        {
            value = 0;
            int start = -1;
            for (int i = 0; i <= text.Length; i++)
            {
                bool digit = i < text.Length && char.IsDigit(text[i]);
                if (digit && start < 0)
                {
                    start = i;
                }
                else if (!digit && start >= 0)
                {
                    return int.TryParse(text[start..i], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out value);
                }
            }
            return false;
        }
    }
}

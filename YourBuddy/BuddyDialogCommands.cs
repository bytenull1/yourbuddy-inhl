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

        internal static string Run(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "...";

            string lower = text.Trim().ToLowerInvariant();

            // First: "decide for yourself whether to follow" is not a follow order.
            if (Has(lower, "decide", "yourself", "autonom", "your call", "own mind")) return BuddyCommands.DecideForYourself();

            // Before the rest, so "go to the workshop" is not a wander order ("work"). A goto that
            // names no room falls through, so "stay, do not walk" is still a stay order.
            if (Has(lower, GotoWords) && BuddyRooms.TryResolve(lower, out BuddyRooms.Entry? room, out List<BuddyRooms.Entry>? several))
            {
                return room != null
                    ? BuddyCommands.GoToRoom(room)
                    : "Which one? " + BuddyRooms.Join(several!); // several is set when room is not
            }

            // Before Follow, so "come and hide" is not a follow order.
            if (Has(lower, "hide", "closet", "locker", "conceal")) return BuddyCommands.Hide();

            if (Has(lower, "follow", "come", "heel")) return BuddyCommands.Follow();

            if (Has(lower, "job", "wander", "own thing", "work", "busy")) return BuddyCommands.Wander();

            if (Has(lower, "stay", "wait", "hold", "stop", "halt")) return BuddyCommands.Stay();

            // Sell before tidy: "trash box" contains "trash".
            if (Has(lower, "sell", "trash box", "money", "cash")) return BuddyCommands.Sell();

            if (Has(lower, "tidy", "clean", "trash", "rubbish", "garbage", "litter", "bin")) return BuddyCommands.Tidy();

            if (Has(lower, "play", "toy")) return BuddyCommands.Play();

            if (Has(lower, "snack", "eat", "food", "hungry")) return BuddyCommands.Snack();

            if (Has(lower, GotoWords)) return BuddyRooms.Prompt();

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

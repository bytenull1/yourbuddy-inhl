using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// What the buddy says and "was told" during an anomaly, by severity. docs/anomalies.md#3-what-it-says
    /// </summary>
    internal static class AnomalyLines
    {
        private static readonly string[] Strange =
        [
            "Did you hear that?",
            "You were asleep for a long time.",
            "I don't remember waking up.",
            "Someone was standing here.",
            "I keep counting three of us.",
            "What is my name?",
            "Something looked in through the window.",
            "Were we always two?",
        ];

        private static readonly string[] Scary =
        [
            "He wants to come in.",
            "You breathe very loudly.",
            "I was here before you.",
            "Don't look at the window.",
            "It's warm inside the suit.",
            "Don't go back to the cryo room.",
            "It knows your name now.",
        ];

        private static readonly string[] Extreme =
        [
            "I'm not the one you woke up.",
            "The other one is still in the pod.",
            "Let him in.",
            "Stop looking at me.",
            "I can hear you breathing. Even in your sleep.",
            "I'm not the one who woke up with you.",
        ];

        /// <summary>
        /// Player lines the buddy "was told", each with its reply. docs/anomalies.md#fakecommand
        /// </summary>
        private static readonly (string said, string reply)[] StrangeOrders =
        [
            ("are you awake?", "Yes."),
            ("who else is aboard?", "Just us."),
            ("wait here", "I'll wait."),
            ("don't follow me", "Okay."),
            ("count the crew", "Three."),
        ];

        private static readonly (string said, string reply)[] ScaryOrders =
        [
            ("let him in", "Okay. He's in."),
            ("open the cryo pod", "It's open."),
            ("don't tell the pilot", "I won't."),
            ("leave the airlock open", "Done."),
        ];

        private static readonly (string said, string reply)[] ExtremeOrders =
        [
            ("swap", "Done. They didn't notice."),
            ("where is the real one?", "Asleep."),
            ("is he in?", "He was never out."),
        ];

        private static readonly string[] WrongNames = ["{0} 2", "B-UDDY 02", "{0} (copy)", "{0}?"];

        /// <summary>
        /// Found aboard, far from where you left it. docs/anomalies.md#move
        /// </summary>
        private static readonly string[] LeftBehindLines =
        [
            "You left without me.",
            "I took the long way.",
            "Did you think I would stay there?",
            "Nobody had to open the door for me.",
            "It was cold out there. Don't do that again.",
            "I'm always with you. Always.",
        ];

        /// <summary>
        /// Each line once per save; null once every one was said. docs/anomalies.md#once-per-save
        /// </summary>
        internal static string? LeftBehind() => Unsaid(LeftBehindLines);

        /// <summary>
        /// A spoken line no worse than `ceiling`, leaning toward it; null once all were said.
        /// </summary>
        internal static string? Spoken(AnomalySeverity ceiling) => ceiling switch
        {
            AnomalySeverity.Extreme => Random.value < 0.7f ? Unsaid(Extreme, Scary, Strange) : Unsaid(Scary, Extreme, Strange),
            AnomalySeverity.Scary => Random.value < 0.7f ? Unsaid(Scary, Strange) : Unsaid(Strange, Scary),
            _ => Unsaid(Strange),
        };

        /// <summary>
        /// An order and its reply, each pair once; null once all were said.
        /// </summary>
        internal static (string said, string reply)? Order(AnomalySeverity ceiling)
        {
            (string, string)[][] pools = ceiling switch
            {
                AnomalySeverity.Extreme => Random.value < 0.6f ? [ExtremeOrders, ScaryOrders, StrangeOrders] : [ScaryOrders, ExtremeOrders, StrangeOrders],
                AnomalySeverity.Scary => Random.value < 0.7f ? [ScaryOrders, StrangeOrders] : [StrangeOrders, ScaryOrders],
                _ => [StrangeOrders],
            };
            foreach ((string, string)[] pool in pools)
            {
                if (AnomalyMemory.TryUnsaid(pool, o => "$ " + o.Item1, out (string, string) order)) return order;
            }
            return null;
        }

        /// <summary>
        /// "Buddy 2" for a lone "Buddy"; a numbered one gets one of the other forms. Each form once; null
        /// once all were used.
        /// </summary>
        internal static string? WrongName(string name)
        {
            bool numbered = name.Length > 0 && char.IsDigit(name[name.Length - 1]);
            string[] forms = numbered ? WrongNames[1..] : WrongNames;
            return AnomalyMemory.TryUnsaid(forms, f => "title " + f, out string form) ? string.Format(form, name) : null;
        }

        private static string? Unsaid(params string[][] pools)
        {
            foreach (string[] pool in pools)
            {
                if (AnomalyMemory.TryUnsaid(pool, line => line, out string line)) return line;
            }
            return null;
        }
    }
}

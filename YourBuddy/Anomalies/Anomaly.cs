using System;

namespace YourBuddy
{
    /// <summary>
    /// How far an anomaly goes. The difficulty and the game's event tier decide which are allowed.
    /// docs/anomalies.md#1-how-often-and-how-far
    /// </summary>
    public enum AnomalySeverity
    {
        Strange,
        Scary,
        Extreme
    }

    /// <summary>
    /// The AnomalyDifficulty setting. The game's own difficulty, or one fixed for the buddy alone.
    /// </summary>
    public enum AnomalyLevel
    {
        Game,
        Harmless,
        Normal,
        Expert
    }

    /// <summary>
    /// Every anomaly the buddy can act out, each once per save. docs/anomalies.md#2-the-anomalies
    /// </summary>
    internal enum AnomalyKind
    {
        Whisper,
        FakeCommand,
        WrongName,
        Vanish,
        Noises,
        WindowStare,
        WallStare,
        BotTalk,
        Bloody,
        ClosetAmbush,
        ShutDoors,
        Statue,
        Meat,
        Pipe,
        Move,
        Smile,
        Stalker,
        BehindYou,
        UnderTheSuit,
        Sleeper
    }

    /// <summary>
    /// How a kind treats you and its orders while it runs. docs/anomalies.md#2-the-anomalies
    /// </summary>
    [Flags]
    internal enum AnomalyTraits
    {
        None = 0,
        /// <summary>Refuses the talk window and every order and task (`IgnoresYou`).</summary>
        IgnoresYou = 1,
        /// <summary>An order does not end it, since it is not listening.</summary>
        Deaf = 2,
        /// <summary>Only its looks, or a set piece elsewhere, so the decider, orders and fear go on around it.</summary>
        Background = 4,
        /// <summary>It leaves something behind, so it counts as had even unseen. docs/anomalies.md#once-per-save</summary>
        Lasting = 8,
        /// <summary>Witnessed only at its own moment (the flash, the open capsule), not by seeing the buddy.</summary>
        OwnCue = 16,
    }

    /// <summary>
    /// One row of the catalogue with its severity, draw weight among its peers, name and traits.
    /// </summary>
    internal readonly struct AnomalyInfo(AnomalyKind kind, AnomalySeverity severity, float weight, string name,
        AnomalyTraits traits = AnomalyTraits.None)
    {
        public readonly AnomalyKind Kind = kind;
        public readonly AnomalySeverity Severity = severity;
        public readonly float Weight = weight;
        public readonly string Name = name;
        public readonly AnomalyTraits Traits = traits;

        public bool Has(AnomalyTraits trait) => (Traits & trait) != 0;
    }

    internal static class Anomalies
    {
        private const AnomalyTraits IgnoresYou = AnomalyTraits.IgnoresYou;
        private const AnomalyTraits Deaf = AnomalyTraits.Deaf;
        private const AnomalyTraits Background = AnomalyTraits.Background;
        private const AnomalyTraits Lasting = AnomalyTraits.Lasting;
        private const AnomalyTraits OwnCue = AnomalyTraits.OwnCue;

        /// <summary>
        /// The catalogue, in AnomalyKind order. docs/anomalies.md#2-the-anomalies
        /// </summary>
        internal static readonly AnomalyInfo[] All =
        [
            new(AnomalyKind.Whisper, AnomalySeverity.Strange, 1.2f, "saying something odd"),
            new(AnomalyKind.FakeCommand, AnomalySeverity.Strange, 0.8f, "an order you never gave"),
            new(AnomalyKind.WrongName, AnomalySeverity.Strange, 0.6f, "the wrong name"),
            new(AnomalyKind.Vanish, AnomalySeverity.Strange, 1f, "vanishing", Deaf),
            new(AnomalyKind.Noises, AnomalySeverity.Strange, 1f, "noises behind you"),
            new(AnomalyKind.WindowStare, AnomalySeverity.Strange, 0.9f, "staring out of a window"),
            new(AnomalyKind.WallStare, AnomalySeverity.Strange, 0.6f, "facing the wall"),
            // Fits only while you are on the ship side of the docked Shipyard, and should win when it does.
            new(AnomalyKind.BotTalk, AnomalySeverity.Strange, 2.5f, "talking with the Shipyard's robot", IgnoresYou),
            new(AnomalyKind.Bloody, AnomalySeverity.Scary, 1f, "covered in blood", IgnoresYou | Deaf | Background),
            new(AnomalyKind.ClosetAmbush, AnomalySeverity.Scary, 1f, "an ambush from a closet"),
            new(AnomalyKind.ShutDoors, AnomalySeverity.Scary, 0.7f, "shutting every door"),
            new(AnomalyKind.Statue, AnomalySeverity.Scary, 0.9f, "moving only while unseen", IgnoresYou),
            // Fits only at the docked Shipyard, away from its cryo room, and should win when it does.
            new(AnomalyKind.Meat, AnomalySeverity.Scary, 1.5f, "caught in the cryo room", IgnoresYou | Deaf | Lasting),
            new(AnomalyKind.Pipe, AnomalySeverity.Scary, 1f, "carrying a bloody pipe", IgnoresYou | Deaf | Lasting),
            // Fits only while it is left on another station than yours, and should win when it does.
            new(AnomalyKind.Move, AnomalySeverity.Scary, 2f, "turning up far from where you left it", Lasting),
            new(AnomalyKind.Smile, AnomalySeverity.Scary, 0.9f, "a bloody smile, for a blink", Background | OwnCue),
            new(AnomalyKind.Stalker, AnomalySeverity.Extreme, 1f, "the bloody stalker", IgnoresYou | Deaf),
            new(AnomalyKind.BehindYou, AnomalySeverity.Extreme, 1f, "right behind you", IgnoresYou | Deaf),
            new(AnomalyKind.UnderTheSuit, AnomalySeverity.Extreme, 0.9f, "something under the suit, for a blink", Background | OwnCue),
            new(AnomalyKind.Sleeper, AnomalySeverity.Extreme, 1.2f, "still asleep in its cryo capsule", Background | OwnCue),
        ];

        internal static AnomalyInfo Info(AnomalyKind kind) => All[(int)kind];

        internal static bool Has(AnomalyKind? kind, AnomalyTraits trait) => kind.HasValue && Info(kind.Value).Has(trait);

        /// <summary>
        /// A kind by its name, case-insensitive, for buddy_anomaly.
        /// </summary>
        internal static AnomalyKind? Parse(string text)
        {
            foreach (AnomalyInfo info in All)
            {
                if (info.Kind.ToString().Equals(text, StringComparison.OrdinalIgnoreCase)) return info.Kind;
            }
            return null;
        }
    }
}

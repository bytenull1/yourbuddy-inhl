using System.Collections.Generic;
using NPC.Core;
using NPC.Core.Interaction;
using NPC.Core.World;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// When the buddies act out an anomaly, and how far one may go, by the game's difficulty and event
    /// tier as the game's own events use them. One at a time across every buddy. docs/anomalies.md
    /// </summary>
    internal static class AnomalyDirector
    {
        /// <summary>
        /// How often the dice are rolled, how long the first roll waits after a load, and how soon a
        /// roll that found nothing possible tries again.
        /// </summary>
        internal const float CheckSeconds = 60f;
        internal const float FirstCheckSeconds = 300f;
        internal const float RetrySeconds = 20f;
        /// <summary>
        /// The quiet after an anomaly, at the game's Normal frequency and tier 0. Expert and higher
        /// tiers shorten it. docs/anomalies.md#1-how-often-and-how-far
        /// </summary>
        internal const float CooldownSeconds = 600f;
        /// <summary>
        /// Never with the Breathless this near the player, whose own scare owns that moment.
        /// </summary>
        internal const float MonsterClearance = 25f;
        /// <summary>
        /// Story tasks done before Normal turns scary, then extreme; and before Expert allows both.
        /// docs/anomalies.md#1-how-often-and-how-far
        /// </summary>
        internal const int NormalScaryTasks = 1;
        internal const int NormalExtremeTasks = 3;
        internal const int ExpertAllTasks = 1;

        private static float _nextCheckAt = -1f;
        private static float _cooldownUntil;
        private static string _last = "none yet";
        private static float _traceAt;
        private static readonly List<AnomalyInfo> Candidates = [];
        private static readonly List<BuddyBehaviour> Actors = [];

        private static Player? _stressPlayer;
        private static byte _stressSource;

        static AnomalyDirector()
        {
            NpcEvents.WorldReset += () =>
            {
                _nextCheckAt = -1f;
                _cooldownUntil = 0f;
                _stressPlayer = null;
            };
        }

        // Danger, from the difficulty and the game's tier

        /// <summary>
        /// The game's events frequency (Harmless 0, Normal 1, Expert 2, a custom difficulty its slider),
        /// or the preset AnomalyDifficulty names.
        /// </summary>
        internal static float Frequency
        {
            get
            {
                switch (YourBuddyPlugin.ConfigAnomalyDifficulty.Value)
                {
                    case AnomalyLevel.Harmless: return 0f;
                    case AnomalyLevel.Normal: return 1f;
                    case AnomalyLevel.Expert: return 2f;
                }
                SceneLoader loader = SceneLoader.Instance;
                return loader != null && loader.GameData != null && loader.GameData.Settings != null
                    ? loader.GameData.Settings.eventsFrequency
                    : 1f;
            }
        }

        /// <summary>
        /// The game's event tier, 0-3. It rises with the story and drops to 0 for a quiet stretch.
        /// </summary>
        internal static int Tier
        {
            get
            {
                GameManager gm = GameManager.Instance;
                return gm != null && gm.EventSystem != null ? Mathf.Clamp(gm.EventSystem.Tier, 0, 3) : 0;
            }
        }

        /// <summary>
        /// Story tasks completed in this save. Unlike the tier, it never drops back to 0.
        /// </summary>
        internal static int Progress
        {
            get
            {
                GameManager gm = GameManager.Instance;
                return gm != null && gm.SequenceHandler != null ? gm.SequenceHandler.CompletedTasksCount : 0;
            }
        }

        /// <summary>
        /// Harmless allows none. Normal escalates with the story, strange first, then scary after
        /// NormalScaryTasks and extreme after NormalExtremeTasks. Expert allows strange, then scary and
        /// extreme together after ExpertAllTasks. docs/anomalies.md#1-how-often-and-how-far
        /// </summary>
        internal static bool Allows(AnomalySeverity severity)
        {
            float frequency = Frequency;
            if (frequency <= 0f) return false;

            if (severity == AnomalySeverity.Strange) return true;

            if (frequency >= 1.5f) return Progress >= ExpertAllTasks;

            return Progress >= (severity == AnomalySeverity.Scary ? NormalScaryTasks : NormalExtremeTasks);
        }

        /// <summary>
        /// This kind may be drawn now, by its severity and because this save has not had it yet.
        /// </summary>
        internal static bool Allows(AnomalyInfo info) => WhyNot(info) == null;

        /// <summary>
        /// Why this kind is not drawn now, or null. It happened already, the difficulty, or the story
        /// progress it waits for. For buddy_anomaly list.
        /// </summary>
        internal static string? WhyNot(AnomalyInfo info)
        {
            // docs/anomalies.md#once-per-save
            if (AnomalyMemory.HasHappened(info.Kind)) return "happened already in this save";

            if (Allows(info.Severity)) return null;

            float frequency = Frequency;
            if (frequency <= 0f) return "not on Harmless";

            int needed = frequency >= 1.5f ? ExpertAllTasks
                : info.Severity == AnomalySeverity.Scary ? NormalScaryTasks : NormalExtremeTasks;
            return $"after {needed} story task(s), {Progress} done";
        }

        /// <summary>
        /// The worst severity allowed now, strange at the least. The line pools lean toward it.
        /// </summary>
        internal static AnomalySeverity Ceiling
        {
            get
            {
                for (AnomalySeverity s = AnomalySeverity.Extreme; s > AnomalySeverity.Strange; s--)
                {
                    if (Allows(s)) return s;
                }
                return AnomalySeverity.Strange;
            }
        }

        /// <summary>
        /// The chance a roll starts one, scaled by AnomalyFrequency.
        /// </summary>
        internal static float Chance
        {
            get
            {
                float frequency = Frequency;
                float chance = 0.05f + 0.07f * frequency + 0.03f * Tier * Mathf.Max(0.5f, frequency);
                return Mathf.Clamp01(chance * Mathf.Max(0f, YourBuddyPlugin.ConfigAnomalyFrequency.Value));
            }
        }

        private static float Cooldown
        {
            get
            {
                float scale = Mathf.Max(0.05f, YourBuddyPlugin.ConfigAnomalyFrequency.Value);
                float faster = (1f + 0.25f * Tier) * (Frequency >= 1.5f ? 1.5f : 1f);
                return CooldownSeconds / faster / scale;
            }
        }

        /// <summary>
        /// How often a severity is drawn among those allowed. The scarier it is, the more the tier pushes it.
        /// </summary>
        private static float SeverityWeight(AnomalySeverity severity)
        {
            int tier = Tier;
            bool expert = Frequency >= 1.5f;
            return severity switch
            {
                AnomalySeverity.Strange => expert ? 0.8f : 1f,
                AnomalySeverity.Scary => 0.7f + 0.2f * tier,
                _ => 0.5f + 0.3f * tier,
            };
        }

        // The roll

        /// <summary>
        /// From BuddyManager.Tick; acts at most every CheckSeconds.
        /// </summary>
        internal static void Tick()
        {
            ScareSounds.Tick();
            if (!YourBuddyPlugin.ConfigAnomalies.Value) return;

            float now = Time.time;
            if (_nextCheckAt < 0f)
            {
                _nextCheckAt = now + FirstCheckSeconds;
                return;
            }
            if (now < _nextCheckAt) return;

            _nextCheckAt = now + CheckSeconds;
            if (now < _cooldownUntil) return;

            string? quiet = WhyQuiet();
            if (quiet != null)
            {
                Trace("no anomaly - " + quiet);
                return;
            }
            float chance = Chance;
            if (Random.value >= chance)
            {
                Trace($"no anomaly this time (chance {chance:0.00})");
                return;
            }
            if (!TryStartAny(out string report))
            {
                _nextCheckAt = now + RetrySeconds;
                Trace("rolled one, but " + report + $" - trying again in {RetrySeconds:0}s");
            }
        }

        /// <summary>
        /// What keeps every anomaly off now, or null.
        /// </summary>
        private static string? WhyQuiet()
        {
            Player? player = NpcPlayer.Pilot;
            if (player == null || player.Controller == null) return "no player";

            if (Frequency <= 0f) return "none on Harmless";

            if (NpcInteraction.IsOpen) return "the talk window is open";

            Breathless? monster = GameManager.Instance != null ? GameManager.Instance.Breathless : null;
            if (monster != null && monster.gameObject.activeInHierarchy &&
                Vector3.Distance(monster.transform.position, player.Controller.CachedTransform.position) < MonsterClearance)
            {
                return "the Breathless is near you";
            }
            foreach (BuddyBehaviour buddy in BuddyManager.All)
            {
                if (buddy != null && buddy.AnomalyRunning) return buddy.Name + " is already acting one out";
            }
            return null;
        }

        /// <summary>
        /// Draws allowed kinds by weight, for a random buddy at a time, until one starts.
        /// </summary>
        private static bool TryStartAny(out string report)
        {
            report = "no buddy can act one out";
            Actors.Clear();
            foreach (BuddyBehaviour buddy in BuddyManager.All)
            {
                // Left on a station it is parked, but it may still turn up near you.
                if (buddy != null && (buddy.AnomalyReady() == null || buddy.MoveReady() == null)) Actors.Add(buddy);
            }
            if (Actors.Count == 0) return false;

            BuddyBehaviour actor = Actors[Random.Range(0, Actors.Count)];
            Candidates.Clear();
            foreach (AnomalyInfo info in Anomalies.All)
            {
                if (Allows(info)) Candidates.Add(info);
            }
            string? lastBlocker = null;
            while (Candidates.Count > 0)
            {
                int index = Draw();
                AnomalyInfo pick = Candidates[index];
                Candidates.RemoveAt(index);
                using NpcRegistry.ActingScope _ = NpcRegistry.Acting(actor.Agent);
                lastBlocker = actor.TryStartAnomaly(pick.Kind);
                if (lastBlocker == null) return true;

                if (NpcLog.Level >= 2) YourBuddyPlugin.Log.LogInfo($"[anomaly] Not {pick.Name}: {lastBlocker}");
            }
            report = lastBlocker != null ? "none fits here (" + lastBlocker + ")" : "every kind allowed has happened in this save";
            return false;
        }

        private static int Draw()
        {
            float total = 0f;
            foreach (AnomalyInfo info in Candidates) total += info.Weight * SeverityWeight(info.Severity);

            float roll = Random.value * total;
            for (int i = 0; i < Candidates.Count; i++)
            {
                roll -= Candidates[i].Weight * SeverityWeight(Candidates[i].Severity);
                if (roll <= 0f) return i;
            }
            return Candidates.Count - 1;
        }

        /// <summary>
        /// A buddy began one, drawn or forced. The cooldown starts and the save remembers it.
        /// </summary>
        internal static void Began(BuddyBehaviour buddy, AnomalyKind kind)
        {
            _cooldownUntil = Time.time + Cooldown;
            AnomalyMemory.Remember(kind);

            _last = Anomalies.Info(kind).Name + " (" + buddy.Name + ")";
        }

        /// <summary>
        /// buddy_anomaly roll. The next roll happens now, with no cooldown and a certain hit.
        /// </summary>
        internal static string RollNow()
        {
            string? quiet = WhyQuiet();
            if (quiet != null) return "No anomaly: " + quiet;

            return TryStartAny(out string report) ? "Started: " + _last : "No anomaly: " + report;
        }

        // The player's side

        /// <summary>
        /// A fright for the player. Stress through a source of the mod's own, never the threat that can
        /// kill. docs/anomalies.md#6-the-players-stress
        /// </summary>
        internal static void Startle(int stress)
        {
            Player? player = NpcPlayer.Pilot;
            if (player == null || player.HealthSystem == null || stress <= 0) return;

            if (_stressPlayer != player)
            {
                _stressPlayer = player;
                _stressSource = player.HealthSystem.AddStressSource();
            }
            player.HealthSystem.AddStress(_stressSource, stress);
        }

        /// <summary>
        /// For the HUD and buddy_anomaly.
        /// </summary>
        internal static string Describe()
        {
            if (!YourBuddyPlugin.ConfigAnomalies.Value) return "off (Anomalies)";

            float now = Time.time;
            string level = Frequency <= 0f ? "harmless" : Frequency >= 1.5f ? "expert" : "normal";
            string when = _nextCheckAt < 0f ? "after the first tick"
                : now < _cooldownUntil ? $"quiet for {_cooldownUntil - now:0}s"
                : $"next roll in {Mathf.Max(0f, _nextCheckAt - now):0}s, chance {Chance:0.00}";
            return $"{level}, tier {Tier}, {Progress} task(s) done, up to {Ceiling.ToString().ToLowerInvariant()}; {when}; last: {_last}";
        }

        private static void Trace(string line)
        {
            if (NpcLog.Level < 2 || Time.time < _traceAt) return;

            _traceAt = Time.time + 15f;
            YourBuddyPlugin.Log.LogInfo("[anomaly] " + line);
        }
    }
}

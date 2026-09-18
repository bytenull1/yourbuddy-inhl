using System.Collections.Generic;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The utility decider: every urge scores itself, and one of the best few is drawn at random rather
    /// than the first that fits a fixed order. The tasks themselves are untouched - they are what an urge
    /// acts through. docs/behaviour.md §3
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        // ReSharper disable RedundantDefaultMemberInitializer
        // The utility decider: what the buddy might want, and how much. docs/behaviour.md §3
        private enum Urge { None, Terminal, Snack, Sell, Tidy, Play, Wander, Follow }

        private const int UrgeCount = 8;

        /// <summary>
        /// What a collector last found for one urge: how many candidates, and how far the nearest was.
        /// Kept for UrgeOpportunityTtl so the scan budget is not spent re-answering the same question.
        /// </summary>
        private readonly struct Chance(int count, float nearest)
        {
            public readonly int Count = count;
            public readonly float Nearest = nearest;
            public readonly float MeasuredAt = Time.time;

            public bool Fresh => MeasuredAt > 0f && Time.time - MeasuredAt < UrgeOpportunityTtl;
        }

        /// <summary>
        /// One urge with its score and the sentence that explains it.
        /// </summary>
        private readonly struct Weighed(Urge urge, float score, string why)
        {
            public readonly Urge Urge = urge;
            public readonly float Score = score;
            public readonly string Why = why;
        }

        private readonly List<Weighed> urgeScores = [];
        private readonly Chance[] urgeChance = new Chance[UrgeCount];
        /// <summary>
        /// The urge acted on last: it is worth a little less than the others this round.
        /// </summary>
        private Urge lastUrge = Urge.None;
        /// <summary>
        /// Where the scan budget starts looking, so no urge is starved of a fresh count.
        /// </summary>
        private int urgeScanCursor = 0;
        /// <summary>
        /// Distance at which an urge's opportunity has halved. Distance costs an urge points now
        /// instead of deleting its candidates: docs/behaviour.md §3
        /// </summary>
        private const float UrgeRangeSoftness = 8f;
        /// <summary>
        /// At or above this the best urge is taken outright; below it, one of the best few is drawn at
        /// random. Bad air scores 1 and so is never left to a die roll.
        /// </summary>
        private const float UrgeDecisive = 0.75f;
        private const int UrgePickTop = 3;
        private const float UrgeSharpness = 2f;
        /// <summary>
        /// The urge acted on last is worth this much less, and every score is jittered this far, so two
        /// identical situations do not produce identical behaviour.
        /// </summary>
        private const float UrgeRepeatPenalty = 0.35f;
        private const float UrgeNoise = 0.15f;
        /// <summary>
        /// The scan budget. An urge less ready than the floor is not looked for at all, at most this many
        /// collectors run per decision, and what one found stands for this long. docs/behaviour.md §3
        /// </summary>
        private const float UrgeScanFloor = 0.5f;
        /// <summary>
        /// Below this an urge is not worth acting on at all. Without it, a score near zero still wins
        /// whenever it is the only one on the table.
        /// </summary>
        private const float UrgeFloor = 0.05f;
        private const int UrgeScansPerDecision = 2;
        private const float UrgeOpportunityTtl = 10f;
        /// <summary>
        /// Caught up: this near on the player's deck. A Follow that cannot get there
        /// starts its clock anyway after DecideCatchUpSeconds.
        /// </summary>
        private const float DecideCaughtUpDist = 4f;
        // ReSharper restore RedundantDefaultMemberInitializer

        /// <summary>
        /// The fetch-and-carry urges, in the order the scan budget rotates through them.
        /// </summary>
        private static readonly Urge[] Errands = [Urge.Sell, Urge.Tidy, Urge.Snack, Urge.Play];

        /// <summary>
        /// Weighs everything the buddy might want and acts on one of them. True when something started.
        /// Called from UpdateAutonomy once the stand-down reasons have passed.
        /// </summary>
        private bool ChooseAndAct(Player player)
        {
            ScoreUrges(player);
            if (urgeScores.Count == 0)
            {
                urgeReport = "nothing worth doing";
                return false;
            }
            urgeScores.Sort((a, b) => b.Score.CompareTo(a.Score));
            BuildUrgeReport();
            TraceDecider("weighing: " + urgeReport);

            // Three goes: an urge that turns out to be impossible should not cost the whole round.
            for (int attempt = 0; attempt < 3 && urgeScores.Count > 0; attempt++)
            {
                int index = urgeScores[0].Score >= UrgeDecisive ? 0 : SampleUrge();
                Weighed pick = urgeScores[index];
                urgeScores.RemoveAt(index);
                string beaten = Runners();
                if (YourBuddyPlugin.ConfigDebugLevel.Value >= 1)
                {
                    YourBuddyPlugin.Log.LogInfo($"[mind] Chose {UrgeName(pick.Urge)} ({pick.Score:0.00}) - {pick.Why}" +
                                                (beaten.Length > 0 ? " - over " + beaten : ""));
                }
                if (!ActOn(pick.Urge)) continue;

                lastUrge = pick.Urge;
                return true;
            }
            return false;
        }

        /// <summary>
        /// One of the best UrgePickTop, drawn with weight score^UrgeSharpness. This is where two identical
        /// situations stop producing identical behaviour. docs/behaviour.md §3
        /// </summary>
        private int SampleUrge()
        {
            int top = Mathf.Min(UrgePickTop, urgeScores.Count);
            float total = 0f;
            for (int i = 0; i < top; i++) total += Mathf.Pow(urgeScores[i].Score, UrgeSharpness);

            if (total <= 0f) return 0;

            float roll = Random.value * total;
            for (int i = 0; i < top; i++)
            {
                roll -= Mathf.Pow(urgeScores[i].Score, UrgeSharpness);
                if (roll <= 0f) return i;
            }
            return top - 1;
        }

        /// <summary>
        /// Starts the task an urge stands for. False when it turned out to be impossible, in which case
        /// the same bookkeeping the old fixed chain did - a retry delay and a level-2 line - happens here.
        /// </summary>
        private bool ActOn(Urge urge)
        {
            switch (urge)
            {
                case Urge.Terminal:
                    return lifeSupport.TryStart();
                case Urge.Snack:
                    return TryErrand(snacks, "Fancied a snack, but ");
                case Urge.Sell:
                    return TryErrand(selling, "Looked for a trash box to sell, but ");
                case Urge.Tidy:
                    return TryErrand(tidying, "Thought of tidying up, but ");
                case Urge.Play:
                    return TryErrand(play, "Thought of playing with something, but ");
                case Urge.Wander:
                    string? owner = BuddyNodeGraph.NearestActiveNodeOwner(transform.position, DecideNodeOwnerRadius);
                    if (owner == null) return false;

                    Decide(BuddyMode.Wander, $"followed you {Time.time - boutSince:0}s", owner);
                    return true;
                case Urge.Follow:
                    Decide(BuddyMode.Follow, mode == BuddyMode.Wander
                        ? $"wandered {Time.time - boutSince:0}s, coming back to you"
                        : "no order, coming to you");
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// False when the errand turned out to be impossible: it waits its retry delay, with a level-2 line.
        /// </summary>
        private static bool TryErrand(Errand errand, string lead)
        {
            if (errand.TryStart(out string report)) return true;

            errand.DueAt = Time.time + errand.RetryDelay;
            if (YourBuddyPlugin.ConfigDebugLevel.Value >= 2)
            {
                YourBuddyPlugin.Log.LogInfo($"[mind] {lead}{report} - trying again in {errand.RetryDelay:0}s");
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Scoring
        // ------------------------------------------------------------------

        /// <summary>
        /// Score = Weight x Need x Opportunity x Readiness x Novelty x jitter, and any factor at zero is
        /// a veto. Only urges above zero go in the list. docs/behaviour.md §3
        /// </summary>
        private void ScoreUrges(Player player)
        {
            urgeScores.Clear();
            TrackBout();
            int scans = 0;

            // Bad air is the one thing never left to a die roll: it scores 1 and wins outright.
            if (lifeSupport.AirIsDangerous()) Add(Urge.Terminal, 1f, 1f, 1f, 1f, "the air aboard is dangerous");

            // Rotated, so the scan budget cannot keep the same urge waiting for a fresh count.
            urgeScanCursor = (urgeScanCursor + 1) % Errands.Length;
            for (int i = 0; i < Errands.Length; i++)
            {
                Urge urge = Errands[(urgeScanCursor + i) % Errands.Length];
                float weight = urge switch
                {
                    Urge.Sell => 0.75f,
                    Urge.Tidy => 0.65f,
                    Urge.Snack => 0.55f,
                    _ => 0.35f,
                };
                ScoreErrand(urge, ErrandOf(urge), weight, ref scans);
            }
            ScoreCompany(player);
        }

        /// <summary>
        /// The four fetch-and-carry urges. They differ only in which collector answers "is there anything,
        /// and how far", which the scan budget decides whether to ask at all.
        /// </summary>
        private void ScoreErrand(Urge urge, Errand errand, float weight, ref int scans)
        {
            if (!errand.Enabled) return;

            // The first round only schedules; nothing is due the moment a buddy spawns.
            if (errand.DueAt < 0f)
            {
                errand.DueAt = Time.time + errand.Interval;
                return;
            }
            float readiness = Readiness(errand.DueAt, errand.Interval);
            if (readiness < UrgeScanFloor) return;

            Chance chance = urgeChance[(int)urge];
            if (!chance.Fresh)
            {
                // Out of budget: nothing is known about this one, so it sits out until its turn comes.
                if (scans >= UrgeScansPerDecision) return;

                scans++;
                int count = errand.Count(out float nearest);
                urgeChance[(int)urge] = chance = new Chance(count, nearest);
            }
            if (chance.Count == 0) return;

            float need = urge switch
            {
                Urge.Sell => Mathf.Clamp01(0.4f + 0.2f * chance.Count),
                Urge.Tidy => Mathf.Clamp01(0.5f + 0.25f * (chance.Count - 1)),
                _ => 1f,
            };
            Add(urge, weight, need, Opportunity(chance.Nearest), readiness,
                $"{chance.Count} within {chance.Nearest:0.0}m");
        }

        /// <summary>
        /// Follow and Wander: the two things the buddy does when no errand wins. Their readiness is the
        /// bout the other one has run, so they still take turns - just no longer only with each other.
        /// </summary>
        private void ScoreCompany(Player player)
        {
            Transform? playerTransform = player.Controller != null ? player.Controller.CachedTransform : null;
            if (playerTransform == null) return;

            if (mode == BuddyMode.Wander)
            {
                Vector3 toPlayer = playerTransform.position - transform.position;
                toPlayer.y = 0f;
                // Floor to floor: docs/invariants.md#follow-arrival-is-level-aware
                bool sameLevel = Mathf.Abs(FloorUnderPlayer(playerTransform) - FloorUnderBuddy().y) <= FollowSameLevelDeltaY;
                float wandered = Time.time - boutSince;
                Add(Urge.Follow, 0.3f, sameLevel ? Mathf.Clamp01(0.5f + toPlayer.magnitude / 40f) : 1f, 1f,
                    BoutReadiness(wandered, boutLength),
                    $"wandering {wandered:0}s of {boutLength:0}, you are {toPlayer.magnitude:0.0}m away");
                return;
            }
            if (mode != BuddyMode.Follow)
            {
                // A Stay left behind by an order that ended.
                Add(Urge.Follow, 0.3f, 1f, 1f, 1f, "no order left, and not following");
                return;
            }
            // The Follow clock starts once it has caught up: a wander can end on the far side of a station.
            if (boutSince < 0f)
            {
                Vector3 toPlayer = playerTransform.position - transform.position;
                toPlayer.y = 0f;
                bool caughtUp = toPlayer.sqrMagnitude <= DecideCaughtUpDist * DecideCaughtUpDist &&
                    Mathf.Abs(FloorUnderPlayer(playerTransform) - FloorUnderBuddy().y) <= FollowSameLevelDeltaY;
                float chasing = Time.time - boutEnteredAt;
                if (!caughtUp && chasing < DecideCatchUpSeconds)
                {
                    TraceDecider($"Wander not weighed: catching up, {toPlayer.magnitude:0.0}m away, {chasing:0}s of {DecideCatchUpSeconds:0}");
                    return;
                }
                boutSince = Time.time;
            }
            string? owner = BuddyNodeGraph.NearestActiveNodeOwner(transform.position, DecideNodeOwnerRadius);
            float followed = Time.time - boutSince;
            Add(Urge.Wander, 0.3f, 1f, owner != null ? 1f : 0f, BoutReadiness(followed, boutLength),
                owner != null ? $"followed you {followed:0}s of {boutLength:0}, nodes of '{owner}' here"
                              : $"no node within {DecideNodeOwnerRadius:0}m");
        }

        private void Add(Urge urge, float weight, float need, float opportunity, float readiness, string why)
        {
            float score = weight * need * opportunity * readiness;
            if (urge == lastUrge) score *= 1f - UrgeRepeatPenalty;

            score *= Random.Range(1f - UrgeNoise, 1f + UrgeNoise);
            if (score < UrgeFloor) return;

            urgeScores.Add(new Weighed(urge, score, why));
        }

        /// <summary>
        /// Distance as a cost rather than a wall: 1 at the buddy's feet, a half at UrgeRangeSoftness,
        /// a quarter at three times that. It never reaches zero, so a far candidate still wins when
        /// nothing nearer is worth doing. docs/behaviour.md §3
        /// </summary>
        private static float Opportunity(float distance) => 1f / (1f + Mathf.Max(0f, distance) / UrgeRangeSoftness);

        /// <summary>
        /// A ramp, not a gate: 0 when the task was just done, 1 when its interval is up, and it keeps
        /// climbing past that only in the sense that it stays at 1 while better things keep winning.
        /// </summary>
        private static float Readiness(float dueAt, float interval) =>
            Mathf.Clamp01(1f - (dueAt - Time.time) / Mathf.Max(1f, interval));

        /// <summary>
        /// Follow and Wander take turns, but not to the second: nothing is offered until BoutReadyFrom of
        /// the bout has run, and the score then ramps to 1 at its end and stays there.
        /// docs/behaviour.md §3
        /// </summary>
        private static float BoutReadiness(float elapsed, float length)
        {
            float fraction = Mathf.Clamp01(elapsed / Mathf.Max(1f, length));
            return fraction < BoutReadyFrom ? 0f : (fraction - BoutReadyFrom) / (1f - BoutReadyFrom);
        }

        private Errand ErrandOf(Urge urge) => urge switch
        {
            Urge.Sell => selling,
            Urge.Tidy => tidying,
            Urge.Snack => snacks,
            _ => play,
        };

        // ------------------------------------------------------------------
        // Saying why
        // ------------------------------------------------------------------

        private static string UrgeName(Urge urge) => urge switch
        {
            Urge.Terminal => "life support",
            Urge.Snack => "a snack",
            Urge.Sell => "selling",
            Urge.Tidy => "tidying up",
            Urge.Play => "playing",
            Urge.Wander => "wandering off",
            _ => "following you",
        };

        /// <summary>
        /// The scored table, for the HUD's Why line, buddy_mind and the level-2 trace. Built from the
        /// already sorted list, so the order is the ranking.
        /// </summary>
        private void BuildUrgeReport()
        {
            urgeReport = "";
            for (int i = 0; i < urgeScores.Count; i++)
            {
                urgeReport += (i > 0 ? ", " : "") + $"{UrgeName(urgeScores[i].Urge)} {urgeScores[i].Score:0.00}";
            }
        }

        /// <summary>
        /// What the chosen urge beat, for the one-line [mind] entry.
        /// </summary>
        private string Runners()
        {
            string text = "";
            for (int i = 0; i < Mathf.Min(UrgePickTop, urgeScores.Count); i++)
            {
                text += (i > 0 ? ", " : "") + $"{UrgeName(urgeScores[i].Urge)} {urgeScores[i].Score:0.00}";
            }
            return text;
        }

        /// <summary>
        /// For the HUD and buddy_mind.
        /// </summary>
        internal string DescribeUrges() => urgeReport;
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// What this save has already seen, the anomalies that happened and every line said. Kept in
    /// the '.buddy' sidecar. docs/anomalies.md#once-per-save
    /// </summary>
    internal static class AnomalyMemory
    {
        private static readonly HashSet<string> Happened = [];
        private static readonly HashSet<string> Said = [];

        internal static bool HasHappened(AnomalyKind kind) => Happened.Contains(kind.ToString());

        internal static void Remember(AnomalyKind kind) => Happened.Add(kind.ToString());

        /// <summary>
        /// Nobody saw it and it left nothing behind, so it may come again.
        /// </summary>
        internal static void Forget(AnomalyKind kind) => Happened.Remove(kind.ToString());

        internal static bool WasSaid(string line) => Said.Contains(line);

        internal static void MarkSaid(string line) => Said.Add(line);

        /// <summary>
        /// A random entry of `pool` whose key was never said, now marked said. False when all were.
        /// </summary>
        internal static bool TryUnsaid<T>(T[] pool, System.Func<T, string> key, out T picked)
        {
            int start = Random.Range(0, pool.Length);
            for (int i = 0; i < pool.Length; i++)
            {
                picked = pool[(start + i) % pool.Length];
                if (Said.Add(key(picked))) return true;
            }
            picked = default!; // only read when this returns true
            return false;
        }

        /// <summary>
        /// For the sidecar.
        /// </summary>
        internal static string[] HappenedNow => [.. Happened];
        internal static string[] SaidNow => [.. Said];

        /// <summary>
        /// A save's memory, or a clean slate for none (a new game, a save without a sidecar).
        /// </summary>
        internal static void Load(BuddySaveFile? data)
        {
            Happened.Clear();
            Said.Clear();
            if (data?.AnomaliesHappened != null) Happened.UnionWith(data.AnomaliesHappened);
            if (data?.LinesSaid != null) Said.UnionWith(data.LinesSaid);
        }

        internal static void OnGameStarting(bool newGame)
        {
            if (newGame) Load(null);
        }

        /// <summary>
        /// For buddy_anomaly.
        /// </summary>
        internal static string Describe() =>
            Happened.Count == 0 ? $"none happened yet, {Said.Count} line(s) said"
                : $"happened: {string.Join(", ", Happened)}; {Said.Count} line(s) said";
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Objects a task could not plan to, reach or use, each left out until its own time runs out.
    /// </summary>
    internal sealed class SkipList
    {
        private readonly Dictionary<Transform, float> until = [];
        private readonly List<Transform> stale = [];

        public void Skip(Transform what, float seconds) => until[what] = Time.time + seconds;

        /// <summary>
        /// While set, nothing counts as skipped: an order from the player looks at everything again.
        /// </summary>
        public bool Ignore;

        public bool Has(Transform what) => !Ignore && until.TryGetValue(what, out float end) && Time.time < end;

        /// <summary>
        /// Seconds left before `what` is tried again; zero when it is not skipped.
        /// </summary>
        public float Remaining(Transform what) => until.TryGetValue(what, out float end) ? Mathf.Max(0f, end - Time.time) : 0f;

        /// <summary>
        /// Forgets entries that ran out or whose object is destroyed.
        /// </summary>
        public void Prune()
        {
            stale.Clear();
            foreach (KeyValuePair<Transform, float> entry in until)
            {
                if (entry.Key == null || Time.time >= entry.Value) stale.Add(entry.Key!); // Unity null: destroyed, still a valid key
            }
            foreach (Transform key in stale) until.Remove(key);
            stale.Clear();
        }
    }
}

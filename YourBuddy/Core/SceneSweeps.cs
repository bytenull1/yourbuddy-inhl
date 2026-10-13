using NPC.Core;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The scene sweeps NPC.Core's SceneScan leaves out: inactive objects included.
    /// npc-core:docs/invariants.md#scene-sweeps-are-budgeted
    /// </summary>
    internal static class SceneSweeps
    {
        /// <summary>
        /// FindObjectsOfType(true), swept once per frame for every caller. Read only.
        /// </summary>
        internal static T[] WithInactive<T>() where T : Object => FrameArray<T>.Get();

        /// <summary>
        /// FindObjectsOfType(true) kept `rescan` seconds, then swept again once SceneScan.MayRescan allows.
        /// </summary>
        internal static T[] Cached<T>(ref T[] items, ref float at, float rescan) where T : Object
        {
            if ((at > 0f && Time.time < at) || !SceneScan.MayRescan(at <= 0f)) return items;

            at = Time.time + rescan;
            items = Object.FindObjectsOfType<T>(true);
            return items;
        }

        private static class FrameArray<T> where T : Object
        {
            private static T[] _items = [];
            private static int _frame = -1;

            internal static T[] Get()
            {
                if (_frame == Time.frameCount) return _items;

                _items = Object.FindObjectsOfType<T>(true);
                _frame = Time.frameCount;
                return _items;
            }
        }
    }
}

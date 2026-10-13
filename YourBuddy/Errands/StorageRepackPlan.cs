using System.Collections.Generic;
using UnityEngine;

namespace YourBuddy
{
    // Bounded staging followed by refill, with no recursive evacuation. docs/storing.md
    internal sealed class StorageRepackPlan
    {
        internal const int MaxContents = 3;
        internal readonly record struct Move(Grabbable Item, bool ToOverflow);
        internal readonly Transform Root;
        internal readonly float ExpiresAt;
        private readonly Queue<Move> moves = new();
        internal Move? Next => moves.Count > 0 ? moves.Peek() : null;

        internal StorageRepackPlan(Transform root, Grabbable incoming, List<Grabbable> contents, float now)
        {
            Root = root;
            ExpiresAt = now + 300f;
            List<Grabbable> selected = [];
            foreach (Grabbable item in contents)
            {
                if (item == incoming || selected.Contains(item)) continue;
                selected.Add(item);
                if (selected.Count == MaxContents) break;
            }
            moves.Enqueue(new(incoming, true));
            foreach (Grabbable item in selected) moves.Enqueue(new(item, true));
            foreach (Grabbable item in selected) moves.Enqueue(new(item, false));
            moves.Enqueue(new(incoming, false));
        }

        internal bool Advance(Grabbable item, bool inOverflow)
        {
            if (Next is not { } next || next.Item != item || next.ToOverflow != inOverflow) return false;
            moves.Dequeue();
            return true;
        }
    }
}

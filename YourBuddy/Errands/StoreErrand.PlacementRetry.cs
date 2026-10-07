using System.Collections.Generic;
using UnityEngine;

namespace YourBuddy
{
    internal sealed partial class StoreErrand
    {
        private readonly Dictionary<Grabbable, float> placementFailures = new();
        private Dictionary<Grabbable, (Vector3 Position, Quaternion Rotation, Transform? Parent)> placementScene = new();
        private Dictionary<Grabbable, (Vector3 Position, Quaternion Rotation, Transform? Parent)> nextPlacementScene = new();

        // Local poses ignore ship travel. Only placement failures are invalidated. docs/storing.md
        private void RefreshPlacementScene()
        {
            nextPlacementScene.Clear();
            bool changed = false;
            foreach (Grabbable item in candidates)
            {
                if (item == null || !item.gameObject.activeInHierarchy || !Body.OnMyVessel(item.transform)) continue;
                Transform t = item.transform;
                var pose = (Position: t.localPosition, Rotation: t.localRotation, Parent: t.parent);
                if (!placementScene.TryGetValue(item, out var old) || old.Parent != pose.Parent ||
                    (old.Position - pose.Position).sqrMagnitude > .0025f || Quaternion.Angle(old.Rotation, pose.Rotation) > 5f)
                    changed = true;
                nextPlacementScene[item] = pose;
            }
            if (placementScene.Count != nextPlacementScene.Count) changed = true;
            if (changed)
            {
                placementFailures.Clear();
                (placementScene, nextPlacementScene) = (nextPlacementScene, placementScene);
            }
        }

        private bool PlacementDeferred(Grabbable item)
        {
            if (!placementFailures.TryGetValue(item, out float until)) return false;
            if (Time.time < until) return true;
            placementFailures.Remove(item);
            return false;
        }

        private void DeferPlacement(Grabbable item)
        {
            if (placementFailures.Count >= 128) placementFailures.Clear();
            placementFailures[item] = Time.time + 30f;
        }
    }
}

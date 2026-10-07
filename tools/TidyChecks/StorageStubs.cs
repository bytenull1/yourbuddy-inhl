using System.Collections.Generic;
using UnityEngine;
namespace YourBuddy
{
    internal static class StorageShape
    {
        internal static int Orientations = 1, CurrentOrientation;
        internal static Vector3 Measure(Grabbable item, Bounds bounds) => bounds.extents;
        internal static Quaternion Rotation(Transform root, int index) { CurrentOrientation = index; return new Quaternion { Degrees = root.rotation.Degrees + index * 90 }; }
        internal static Vector3 Half(Vector3 half, Quaternion rotation) => half;
    }
    internal sealed class StorageOverflow
    {
        internal int CandidateCount => 9;
        internal int Priority => 0;
        internal static FurnitureStorage? Area;
        internal static void Collect(SpaceShip ship, List<FurnitureStorage> into) { if (Area != null) into.Add(Area); }
    }
    // Engine boundary for StoreErrand state tests; does not simulate collision geometry.
    internal sealed class FurnitureStorage
    {
        internal static readonly List<FurnitureStorage> All = [];
        internal readonly HashSet<Grabbable> Inside = [];
        internal readonly Transform Root = new();
        internal readonly CustomRoom Room = new();
        internal Door[] Doors = [];
        internal HidingSpot? Hideout => null;
        internal StorageOverflow? Overflow;
        internal string Name = "chest";
        internal string LastBlocker = "test obstacle";
        internal Grabbable? LastBlockingItem = null;
        internal bool Available = true, Fits = true, IsClear = true, ReachClear = true;
        internal bool IsSettled = true;
        internal bool DoorsReady = true;
        internal bool Untidy = false, TurnClear = true;
        internal bool NeedsArrange(Grabbable item) => Untidy;
        internal bool ClearTurn(Vector3 center,float radius,Transform item,Transform buddy) => TurnClear;
        internal bool Settled(Grabbable item) => IsSettled;
        internal int CandidateCount(Vector3 half) => 36;
        internal static bool SafeDropAvailable = true;
        internal static bool PutDownSafely(NPC.Core.Agents.NpcHands hands, Transform buddy, Vector3 half) { if (!SafeDropAvailable) return false; hands.Drop("safe test drop"); return true; }
        internal int Probes;
        internal int RequiredOrientation = -1;
        internal Vector3 Approach => Root.position;
        internal static void Collect(SpaceShip ship,List<FurnitureStorage> into) { into.Clear(); into.AddRange(All); }
        internal bool ReadContents(HashSet<Grabbable> into) { into.UnionWith(Inside); return true; }
        internal bool Probe(int index,Vector3 half,Transform item,out Vector3 point) { Probes++; point = Root.position; return Fits && (RequiredOrientation < 0 || RequiredOrientation == StorageShape.CurrentOrientation); }
        internal bool Clear(Vector3 point,Vector3 half,Transform item,bool closed) => IsClear;
        internal System.Func<Vector3,Vector3,Vector3,bool>? ReachPolicy;
        internal bool ClearReach(Vector3 from,Vector3 to,Vector3 half,Transform item,Transform buddy) => ReachClear && (ReachPolicy?.Invoke(from,to,half) ?? true);
        internal Vector3[] InsertionPath(Vector3 from,Vector3 point,Vector3 half) => [point];
    }
}

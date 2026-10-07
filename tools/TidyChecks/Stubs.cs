using System;
using System.Collections.Generic;
using NPC.Core.Agents;
using NPC.Core.Navigation;
using UnityEngine;

namespace UnityEngine
{
    public readonly record struct Vector3(float x, float y, float z)
    {
        public static Vector3 zero => default;
        public static Vector3 one => new(1,1,1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x*b,a.y*b,a.z*b);
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public float sqrMagnitude => x*x + y*y + z*z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public static class Time { public static float time; public static int frameCount; }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a,b); }
    public static class Random { public static int Range(int a, int b) => a; }
    public class GameObject { public bool activeInHierarchy = true; }
    public class Transform
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 localPosition { get => position; set => position = value; }
        public Quaternion localRotation => rotation;
        public Transform? parent;
        public Vector3 InverseTransformPoint(Vector3 point) => point-position;
        public Vector3 TransformPoint(Vector3 point) => point+position;
        public GameObject gameObject = new();
        public Component? Component;
        public T[] GetComponentsInChildren<T>() => [];
        public T GetComponent<T>() where T : class => null!;
        public T GetComponentInParent<T>(bool includeInactive = false) where T : class => null!;
    }
    public class Component
    {
        public readonly Transform transform;
        public readonly Dictionary<Type,object> Extras = [];
        public T GetComponent<T>() where T : class => this is T self ? self : Extras.TryGetValue(typeof(T),out var value) ? (T)value : null!;
        public GameObject gameObject => transform.gameObject;
        public bool isActiveAndEnabled = true;
        public Component() { transform = new() { Component = this }; }
        public bool TryGetComponent<T>(out T value) { value = default!; return false; }
    }
    public sealed class BoxCollider : Component { public Bounds bounds; public bool enabled = true; }
    public struct Bounds { public Vector3 center, extents; public Vector3 size => extents * 2; }
    public struct Quaternion { public float Degrees; public static float Angle(Quaternion a,Quaternion b) => Math.Abs(a.Degrees-b.Degrees); }
}
public sealed class Grabbable : Component
{
    public bool CanTrash = true;
    public string Signature = "";
    public void SetParent(Transform parent) { }
    public bool Saved;
    public void SavePosition() => Saved = true;
}
public sealed class Door : Component { public bool Opened = true; }
public sealed class InstantItemDetector { public List<Grabbable> GetItemsInZone() => []; }
public sealed class ItemDetector { public List<Grabbable> Items = []; }
public sealed class CustomRoom { public bool EnabledStructure = true; public string name = "room"; public Transform ContentParent = new(); }
public sealed class SpaceShip { }
public sealed class GameManager { public static readonly GameManager Instance = new(); public SpaceShip PlayerShip = new(); }
public sealed class ResourceContainer { public int Type { get; set; } public SupplyData? Data = new(); public int Value => Data!.value; }
public sealed class SupplyData { public int value = 1, usages = 1; }
public sealed class Food { public SupplyData? Data = new(); }
public sealed class SeedPack { public SupplyData? Data = new(); public int Seeds => Data!.value; }
public sealed class AidKit { public SupplyData? Data = new(); }
public sealed class RepairKit { public SupplyData? Data = new(); }
public sealed class Suit { }
public sealed class HidingSpot { public object? CurrentInteractor { get; set; } }
public sealed class TrashCan : Component { public readonly ItemDestroyer Slot = new(); }
public sealed class ItemDestroyer : Component { }
namespace NPC.Core
{
    public static class SceneScan
    {
        public static readonly Dictionary<Type, object> Data = [];
        public static T[] ThisFrame<T>() => Data.TryGetValue(typeof(T),out var value) ? (T[])value : [];
    }
}
namespace NPC.Core.Navigation { public sealed class NavPath { } }
namespace NPC.Core.Agents
{
    public abstract class ReachTask(Vector3 point, Transform own)
    {
        public readonly Vector3 TargetPoint = point;
        public readonly Transform Own = own;
        public Vector3 Node, StandPoint;
        public const float ReachHeight = 2f;
        public virtual float Reach => 1;
        public virtual float ReachBelow => 1;
        public virtual float[] StandOffs => [];
        public abstract string Name { get; }
    }
    public sealed class NpcHands
    {
        public Grabbable? Item;
        public Vector3 Point => Vector3.zero;
        public bool WorldOrientation;
        public void TurnWorld(Quaternion rotation) { WorldOrientation = true; if (Item != null) Item.transform.rotation = rotation; }
        public int Stored;
        public void PutDown(Vector3 point, Vector3 velocity) { Stored++; if (Item != null) Item.transform.position = point; Item = null; }
        public static bool ColliderBounds(GameObject item, out Bounds bounds) { bounds = new() { extents = new(.05f,.05f,.05f) }; return true; }
        public int Pickups;
        public bool PickUp(Grabbable item) { Pickups++; Item = item; return true; }
        public void Release() { Item = null; ReachTarget = null; }
        public void Drop(string why) => Release();
        public Vector3? ReachTarget;
        public float ReachSpeed;
        public void ReachTo(Vector3? point, float speed = 0) { ReachTarget = point; ReachSpeed = speed; }
        public float ReachDistance;
        public float DistanceTo(Vector3 point) => ReachDistance;
    }
}
namespace YourBuddy
{
    internal interface IErrandBody
    {
        ErrandLeg? Leg { get; }
        bool IsAboardPlayerShip();
        void LoadRoomOf(Transform what);
        Transform Transform { get; }
        NpcHands Hands { get; }
        Vector3 FloorUnderBuddy();
        bool OnMyVessel(Transform what);
        bool TakenByAnother(Transform what);
        bool InReach(ReachTask task);
        string? PlanReach(ReachTask task, out NavPath path);
        void Walk(ErrandLeg task, NavPath? path);
        bool StepIntoReach(ReachTask task, out Vector3 move, out bool wantMove);
        void StandFacing(Vector3 point);
        void FinishRoute();
    }
    internal abstract class ErrandLeg(Vector3 point, Transform own) : ReachTask(point, own)
    {
        public abstract void Defer(float seconds);
        public abstract Vector3 Approach(out bool wantMove);
        public abstract void End();
        public abstract string Describe();
        public virtual bool Waits => false;
        public virtual bool Holds(Transform t) => Own == t;
    }
    internal abstract class Errand(IErrandBody body)
    {
        protected readonly IErrandBody Body = body;
        protected readonly SkipList Skips = new();
        protected Vector3 Here => Body.Transform.position;
        protected string? Last;
        public float DueAt;
        public abstract bool Enabled { get; }
        public abstract float Interval { get; }
        public abstract float RetryDelay { get; }
        protected abstract float SkipSeconds { get; }
        protected abstract string Command { get; }
        protected abstract string Topic { get; }
        public abstract bool TryStart(out string report);
        public abstract int Count(out float nearest);
        protected void Defer(Transform t, float seconds) => Skips.Skip(t,seconds);
        protected float Nearest(int n, Func<int, Vector3> at) => 0;
        protected bool Failed(string why) { Last = why; return false; }
        protected string? TakeBlocker(Grabbable item) => Body.TakenByAnother(item.transform) ? "taken" : null;
        protected string? NotInHands(Grabbable item) => Body.Hands.Item == item ? null : "not held";
        protected void Trace(string line) { }
        protected enum SetOffResult { InReach, Walking, NoPlan, NoPlansLeft }
        protected SetOffResult SetOff<T>(T task, Action<T,NavPath?> begin, ref int plans, int max, ref string? failure) where T : ErrandLeg
        {
            if (Body.InReach(task)) { begin(task,null); return SetOffResult.InReach; }
            if (plans++ >= max) return SetOffResult.NoPlansLeft;
            failure = Body.PlanReach(task,out var route);
            if (failure != null) return SetOffResult.NoPlan;
            begin(task,route); return SetOffResult.Walking;
        }
        protected string HowFar(SetOffResult result, ReachTask task) => "nearby";
        protected void Begin(ErrandLeg task, NavPath? path, float retry, string last) => Body.Walk(task,path);
        protected Vector3 Leave(string item, string why) { Body.FinishRoute(); return default; }
        protected float NextInterval() => Interval;
    }
    internal static class Items
    {
        public const float SnackReachDist = .9f, SnackReachBelow = .5f, SnackContainerMargin = 1f,
            TidyAimSeconds = .4f, TidyReachSeconds = .5f, TidyLiftSeconds = .5f, SnackOpenSeconds = 1.5f;
        public static readonly float[] SnackStandOffs = [.55f,.85f];
        public static readonly HashSet<Transform> CheckedContainers = [];
        public static string ContainerKind(Transform t) => "container";
        public static string ItemLabelOf(Grabbable t) => "trash";
        public static bool ColliderBounds(GameObject item,out Bounds bounds) => NpcHands.ColliderBounds(item,out bounds);
        public static Vector3 ItemTop(Grabbable t) => t.transform.position;
        public static float FlatDistanceSq(Vector3 a, Vector3 b) => (a-b).sqrMagnitude;
        public static Transform? ContainerOf(Door d,out InstantItemDetector? contents) { contents = null; return null; }
        public static Vector3 DoorFacePoint(Door[] doors) => default;
        public static bool IsTrash(Grabbable item) => item.CanTrash;
        public static void ShuffleNearest<T>(List<T> items) { }
        public static void OpenContainerDoors(Door[] doors,List<Door> opened,string name) { foreach (Door door in doors) if (!door.Opened) { door.Opened = true; opened.Add(door); } }
        public static int ClosedDoors;
        public static void CloseOpenedDoors(List<Door> doors,HidingSpot? hide,string name) { ClosedDoors += doors.Count; foreach (Door door in doors) door.Opened = false; doors.Clear(); }
        public static string? MovedBlocker(Vector3 a,Vector3 b) => null;
    }
    internal static class GameInternals
    {
        internal static class TrashCanAccess { public static ItemDestroyer GetItemDestroyer(TrashCan bin) => bin.Slot; }
    }
    internal static class YourBuddyPlugin
    {
        public sealed class Setting<T>(T value) { public T Value = value; }
        public static readonly Setting<bool> ConfigStoring = new(true);
        public static readonly Setting<bool> ConfigTidying = new(true);
        public static readonly Setting<float> ConfigTidyIntervalMinutes = new(5);
        public sealed class Logger { public readonly List<string> Lines = []; public void LogInfo(string text) => Lines.Add(text); }
        public static readonly Logger Log = new();
    }
}

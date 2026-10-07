using System;
using System.Collections.Generic;
using UnityEngine;
using NPC.Core.Agents;
using NPC.Core.Navigation;
using Space.Enums;

// Engine boundaries for executing the production resource job without Unity.
namespace UnityEngine
{
    public class Component
    {
        public Transform transform = null!;
        public GameObject gameObject = new();
        public bool isActiveAndEnabled = true;
        public Dictionary<Type, object> Components = new();
        public T GetComponent<T>() => Components.TryGetValue(typeof(T), out var value) ? (T)value : default!;
        public T GetComponentInParent<T>() => GetComponent<T>();
        public bool TryGetComponent<T>(out T value) { value = GetComponent<T>(); return value is not null; }
        public int GetInstanceID() => GetHashCode();
        public List<Component> Children = [];
        public void GetComponentsInChildren<T>(bool inactive, List<T> into) { foreach (Component c in Children) if (c is T t) into.Add(t); }
    }
    public class Transform : Component
    {
        public Vector3 position, forward;
        public Transform() { transform = this; }
        public Vector3 TransformPoint(Vector3 v) => v + position;
        public Vector3 InverseTransformPoint(Vector3 v) => v - position;
    }
    public class GameObject { public bool activeInHierarchy = true; }
    public struct Vector3(float x, float y, float z)
    {
        public float x=x, y=y, z=z;
        public float sqrMagnitude => x*x+y*y+z*z;
        public static float Dot(Vector3 a, Vector3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
        public static Vector3 zero => new();
        public static Vector3 one => new(1,1,1);
        public static Vector3 up => new(0,1,0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x*b,a.y*b,a.z*b);
    }
    public static class Time { public static float time; public static int frameCount; }
    public static class Mathf
    {
        public const float PI = MathF.PI;
        public static float Sqrt(float x) => MathF.Sqrt(x);
        public static int CeilToInt(float x) => (int)MathF.Ceiling(x);
        public static float Cos(float x) => MathF.Cos(x);
        public static float Sin(float x) => MathF.Sin(x);
    }
    public struct Bounds { public Vector3 center, size, extents; }
    public class BoxCollider : Component { public Bounds bounds; }
    public struct RaycastHit { public BoxCollider collider; public Vector3 point; }
}
namespace Space { }
namespace Space.Enums { public enum ResourceType { Oxygen, Fuel, Energy } }
public class Grabbable : Component
{
    public bool IsGrabbed, restrictGrab;
    public int BuyPrice = 50;
    public void SetParent(Transform t) { }
    public void SavePosition() { }
}
public class ResourceContainer : Component
{
    public object? Data = new();
    public ResourceType Type;
    public int Value = 10, LoadingSpeed = 1;
}
public class ResourceController : Component
{
    public bool Initialized = true, Loading;
    public ResourceContainer? Current;
    public ItemDetector? Slot;
    public int Ejections;
    public void TryTakeOut() { Ejections++; Current = null; Loading = false; }
    public void SwitchLoading() { Loading = !Loading; }
}
public class Gate : Component { }
public class Airlock : Component { }
public class ItemDetector : Component { public HashSet<Grabbable> Items = []; }
public class Room : Component { public Transform ContentParent = new(); public bool ContentEnabled = true; }
public class CustomRoom : Room { public bool EnabledStructure = true; public string name = "room"; }
public class Shop : Component { public Grabbable[]? Items; public Transform? Outlet; public ResourceContainer? Sells; }
public class Player { public Wallet CashSystem = new(); public Control Controller = new(); }
public class Wallet { public int Cash = 1000; }
public class Control { public bool IsControlling; }
public class Reserve
{
    public int Capacity = 100, Value = 100, Calls, LastAmount;
    public bool Accept = true;
    public bool Reduce(int amount) { Calls++; LastAmount = amount; if (Accept) Value -= amount; return Accept; }
}
public class OxygenController : Reserve { public int RemainingOxygen => Value; public bool TryReduceOxygen(int n) => Reduce(n); }
public class FuelController : Reserve { public int Fuel => Value; public bool TryConsumeFuel(int n) => Reduce(n); }
public class ElectricityController : Reserve { public int Energy => Value; public bool TryReduceEnergy(int n) => Reduce(n); }
public class SpaceShip
{
    public ResourceController? CellController;
    public OxygenController OxygenController = new();
    public FuelController FuelController = new();
    public ElectricityController ElectricityController = new();
    public Pilot Autopilot = new();
}
public class Pilot { public string DockedStation = "station"; }
public class GameManager { public static GameManager Instance = new(); public SpaceShip? PlayerShip; }
namespace NPC.Core
{
    public static class SceneScan
    {
        public static class Snapshot<T> { public static T[] Items = []; }
        public static T[] ThisFrame<T>() => Snapshot<T>.Items;
    }
    public static class NpcRegistry
    {
        public struct ActingScope : IDisposable { public void Dispose() { } }
        public static ActingScope Acting(NpcAgent? a) => new();
    }
}
namespace NPC.Core.World
{
    public static class NpcVessels
    {
        public static Dictionary<Transform, string> Owners = new();
        public static string OwnerOfTransform(Transform t) => Owners.TryGetValue(t, out string? o) ? o : NavGraph.ShipOwner;
    }
    public static class NpcPlayer { public static Player? Pilot; }
}
namespace NPC.Core.Navigation
{
    public struct NavPath { public int Count => 0; public Vector3 this[int i] => default; }
    public static class NavGraph { public const string ShipOwner = "ship"; }
    public static class NavProbe { public static bool WalkLos(Vector3 a, Vector3 b, float c) => true; }
}
namespace NPC.Core.Agents
{
    public class NpcAgent { }
    public class NpcHands
    {
        public Grabbable? Item;
        public Vector3 Extents;
        public void PutDown(Vector3 p, Vector3 r) { Item = null; }
        public void Release() { Item = null; }
        public bool PickUp(Grabbable item) { Item = item; return true; }
        public void ReachTo(Vector3 p) { }
        public static bool ColliderBounds(GameObject o, out Bounds b) { b = new(); return false; }
    }
}
namespace YourBuddy
{
    internal class IErrandBody
    {
        public Transform Transform = new();
        public NpcHands Hands = new();
        public bool IsOutside;
        public ErrandLeg? Leg;
        public bool OnRoute => Leg != null;
        public bool IsAboardPlayerShip() => true;
        public bool TakenByAnother(Transform t) => false;
        public void LoadRoomOf(Transform t) { }
        public void StandFacing(Vector3 p) { }
        public bool InReach(ErrandLeg leg) => true;
        public string? PlanReach(ErrandLeg leg, out NavPath p) { p = new(); return null; }
        public void Walk(ErrandLeg leg, NavPath? p) { Leg = leg; }
        public void FinishRoute() { var old = Leg; Leg = null; old?.End(); }
        public bool StepIntoReach(ErrandLeg leg, out Vector3 move, out bool wantMove) { move = new(); wantMove = false; return true; }
    }
    internal abstract class ErrandLeg(Vector3 point, Transform own)
    {
        public Vector3 TargetPoint = point, Node, StandPoint;
        public Transform Own = own;
        public abstract string Name { get; }
        public virtual float ReachBelow => 0;
        public virtual bool StandAllowed(Vector3 point) => true;
        public virtual bool Waits => false;
        public abstract Vector3 Approach(out bool move);
        public abstract string Describe();
        public virtual void Defer(float seconds) { }
        public virtual bool Recover(string why) => false;
        public virtual void End() { }
        public virtual bool Holds(Transform t) => t == Own;
    }
    internal class SkipList { public static HashSet<Transform> Skipped = []; public void Prune() { } public bool Has(Transform t) => Skipped.Contains(t); }
    internal abstract class Errand(IErrandBody body)
    {
        protected IErrandBody Body = body;
        protected SkipList Skips = new();
        protected Vector3 Here => Body.Transform.position;
        public float DueAt;
        protected string? Last;
        public abstract bool Enabled { get; }
        public abstract float Interval { get; }
        public abstract float RetryDelay { get; }
        protected abstract float SkipSeconds { get; }
        protected abstract string Command { get; }
        protected abstract string Topic { get; }
        public abstract int Count(out float nearest);
        public abstract bool TryStart(out string report);
        public virtual string Describe() => "scheduled";
        protected enum SetOffResult { InReach, Walking }
        protected SetOffResult SetOff<T>(T leg, Action<T, NavPath?> begin, ref int plans, int max, ref string? failure) where T: ErrandLeg { begin(leg, null); return SetOffResult.InReach; }
        protected void Begin(ErrandLeg leg, NavPath? p, float delay, string report) => Body.Walk(leg, p);
        public void Defer(Transform t, float s) { }
    }
    internal static class ResourceDuty
    {
        internal static ResourceDutySettings Settings = new();
        internal static ResourceErrand? Owner;
        internal static string Status = "waiting";
        internal static bool Enabled => !Settings.Paused && (Settings.Oxygen.Enabled || Settings.Fuel.Enabled || Settings.Energy.Enabled);
        internal static void Report(string t, string subject = "") { Status = t; }
        internal static void Trace(string t, string subject) { }
    }
    internal static class Items
    {
        internal static bool Loadable(Component c) => c.isActiveAndEnabled;
        internal static float FlatDistanceSq(Vector3 a, Vector3 b) => (a-b).sqrMagnitude;
        internal static string? TakeBlocker(Grabbable item, Grabbable? held) => null;
        internal static Vector3 ItemTop(Grabbable item) => item.transform.position;
    }
    internal static class GameInternals
    {
        internal static class ResourceAccess
        {
            internal static bool Ready = true;
            internal static ItemDetector? Slot(ResourceController c) => c.Slot;
            internal static ResourceContainer? Current(ResourceController c) => c.Current;
            internal static bool IsLoading(ResourceController c) => c.Loading;
        }
        internal static class ShopAccess
        {
            internal static bool Ready = true;
            internal static Grabbable[]? Stock(Shop s) => s.Items;
            internal static Transform? Outlet(Shop s) => s.Outlet;
            internal static void Buy(Shop s, int n, Player p)
            {
                if (s.Sells == null || s.Items == null) return;
                p.CashSystem.Cash -= s.Items[n].BuyPrice;
                NPC.Core.SceneScan.Snapshot<ResourceContainer>.Items = [.. NPC.Core.SceneScan.Snapshot<ResourceContainer>.Items, s.Sells];
            }
        }
    }
    internal static class ResourceScan
    {
        internal static Shop[] Shops() => NPC.Core.SceneScan.ThisFrame<Shop>();
        internal static List<ResourceContainer> Cells() => [.. NPC.Core.SceneScan.ThisFrame<ResourceContainer>()];
    }
    internal static class ResourceStorage
    {
        internal static string LastBlocker = "blocked";
        internal static Vector3 Clearance(Vector3 size) => size;
        internal readonly record struct Place(CustomRoom Room, Vector3 WorldPoint);
        internal static int Layout(SpaceShip ship) => 0;
        internal static void Candidates(SpaceShip ship, int kind, List<Place> result) { }
        internal static bool MayProbe() => true;
        internal static bool FindFloor(Vector3 p, out RaycastHit hit, bool shipOnly = true) { hit = new(); return false; }
        internal static bool Clear(Vector3 p, Vector3 half, Transform? item, bool shipOnly = true) => false;
    }
    internal static class YourBuddyPlugin { internal static Logger Log = new(); internal static Setting ConfigAutonomy = new(); }
    internal class Logger { internal void LogInfo(string t) { } internal void LogWarning(string t) { } }
    internal class Setting { internal bool Value = true; }
}

using System;
using YourBuddy;
using Space;
using UnityEngine;
int checks = 0;
void Check(bool ok, string why) { if (!ok) throw new Exception(why); checks++; }
var b = new BuddyBehaviour();
b.StartStoreNow();
Check(b.Active && b.storing.Attempts == 1, "store order stays active with no work");
b.Tick(); Check(b.storing.Attempts == 1, "empty search waits before polling");
Time.time = 8; b.Tick(); Check(b.storing.Attempts == 2, "empty store order rechecks without another command");
b.storing.Found = true; Time.time = 16; b.Tick();
Check(b.Active && b.storing.Running, "later work starts under original order");
b.Blocked = "route"; Time.time = 100; b.Tick();
Check(b.storing.Attempts == 3, "active route is not replaced");
b.StopStoreOrder(); Check(!b.Active && b.Finished == 1, "new order releases current storage leg");
int beforeStop = b.storing.Attempts; Time.time += 100; b.Tick();
Check(b.storing.Attempts == beforeStop, "cancelled order cannot restart on a later tick");
b = new BuddyBehaviour { Busy = "afraid" }; b.StartStoreNow();
Check(!b.Active && b.storing.Attempts == 0, "refused initial command does not create a loop");
b = new BuddyBehaviour(); b.StartStoreNow(); b.Survival = true; Time.time += 8; b.Tick();
Check(b.storing.Attempts == 1, "survival takes precedence over repeat storage");
b.storing.Running = false; b.StopStoreOrder();
Check(b.Finished == 0, "cancelling repeat does not abort another task");
b = new BuddyBehaviour(); b.StartStoreNow(); b.agent.Aboard = false; Time.time += 8; b.Tick();
Check(b.storing.Attempts == 1, "repeat waits when away from ship");
b.agent.Aboard = true; b.Blocked = "dialog"; b.Tick();
Check(b.storing.Attempts == 1, "dialog and other stand-down reasons prevent restart");
b = new BuddyBehaviour(); b.storing.Found = true; b.StartStoreNow(); b.Interrupt();
Check(b.Active && !b.storing.Running && b.Finished == 1,
    "world interruption cleans up storage leg but retains continuing order");
b.Interrupt(); Check(b.Finished == 1, "repeated interruption does not end an unrelated route");
b = new BuddyBehaviour(); b.StartStoreNow();
Check(b.StoreOrderStatus.Contains("Nothing"), "empty workload is explained rather than silently waiting");
b.Blocked = "being talked to"; b.Tick();
Check(b.StoreOrderStatus.Contains("resume"), "deferred work explains that storage will resume");
b = new BuddyBehaviour(); b.agent.Aboard = false; b.StartStoreNow();
Check(b.Active && b.Returns == 1 && b.storing.Attempts == 0, "station store order returns aboard before scanning supplies");
b.agent.Aboard = true; b.Interrupt(); b.Tick();
Check(b.storing.Attempts == 1, "arrival aboard starts storage without a second command");
b = new BuddyBehaviour(); b.agent.Aboard = false; b.StartStoreNow(); b.StopStoreOrder();
Check(!b.Active && b.Finished == 1, "new order cancels actual return route");
b = new BuddyBehaviour(); b.agent.Aboard = false; b.StartStoreNow();
GameManager.Instance.PlayerShip.Autopilot.DockedStation = ""; b.Tick();
Check(b.Finished == 1 && b.storing.Attempts == 0, "undocking cancels return without scanning station items");
b = new BuddyBehaviour(); b.agent.Aboard = false; b.StartStoreNow();
Check(b.Returns == 0 && b.Active, "disconnected ship keeps order pending without walking");
GameManager.Instance.PlayerShip.Autopilot.DockedStation = "station";
NPC.Core.Navigation.NavGraph.Partial = true;
b = new BuddyBehaviour(); b.agent.Aboard = false; b.StartStoreNow();
Check(b.Returns == 0 && b.storing.Attempts == 0, "partial ship route is refused");
NPC.Core.Navigation.NavGraph.Partial = false;
Time.time += 16; b.Tick();
Check(b.Returns == 1, "pending return retries after backoff when route becomes usable");
// Production chore scheduler: batches finish, orders survive, failures back off.
b = new BuddyBehaviour(); b.Chore.Found = true; b.StartChore();
Check(b.ChoreActive && b.Chore.Attempts == 1, "ordered chore starts and remains active");
b.Blocked = "route"; Time.time += 100; b.TickChore();
Check(b.Chore.Attempts == 1, "running batch cannot be replaced");
b.Blocked = null; b.TickChore();
Check(b.Chore.Attempts == 2, "finished batch restarts without another player command");
b.Chore.Found = false; Time.time += 4; b.TickChore();
int attempts = b.Chore.Attempts; Time.time += 4; b.TickChore();
Check(b.Chore.Attempts == attempts && b.ChoreActive, "empty work retains duty and respects retry delay");
b.Chore.Found = true; Time.time += 61; b.Survival = true; b.TickChore();
Check(b.Chore.Attempts == attempts, "survival takes precedence over chore retry");
b.Survival = false; b.TickChore();
Check(b.Chore.Attempts == attempts + 1, "new work resumes when safe after failed batch");
b.StopStoreOrder(); Time.time += 100; b.TickChore();
Check(!b.ChoreActive && b.Chore.Attempts == attempts + 1, "replacement or Decide cancels continuing chores");
b = new BuddyBehaviour { Busy = "afraid" }; b.StartChore();
Check(!b.ChoreActive && b.Chore.Attempts == 0, "refused chore does not create continuing duty");
b = new BuddyBehaviour(); b.StartChore(); b.StartStoreNow();
Check(!b.ChoreActive && b.Active, "storage replaces the previous continuing chore");
b.StartChore();
Check(b.ChoreActive && !b.Active, "chore replaces continuing storage");
b = new BuddyBehaviour(); b.Chore.Found = true; b.StartChore();
b.Chore.DeferredUntil = Time.time + 60; Time.time += 4; b.TickChore();
Check(b.Chore.Attempts == 1, "continuing chore respects navigation deferral after the batch stops");
Time.time += 61; b.TickChore();
Check(b.Chore.Attempts == 2, "deferred chore resumes after its retry window");
b.SetActiveLeg(); b.InterruptAnyLeg();
Check(b.Finished == 1 && b.ChoreActive, "world interruption ends the active errand and preserves assigned duty");
b.InterruptAnyLeg(); Check(b.Finished == 1, "repeated interruption does not end an absent leg twice");
Console.WriteLine($"{checks} continuing-task lifecycle checks passed.");
namespace UnityEngine {
 public static class Mathf { public static float Max(float a,float b) => Math.Max(a,b); }
 public static class Time { public static float time; }
 public readonly record struct Vector3(float x, float y, float z) {
  public float sqrMagnitude => x*x+y*y+z*z;
  public static Vector3 operator -(Vector3 a,Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
 }
 public sealed class Transform { public Vector3 TransformPoint(Vector3 v) => v; }
}
public sealed class GameManager { public static readonly GameManager Instance = new(); public SpaceShip PlayerShip = new(); }
public sealed class SpaceShip { public Autopilot Autopilot = new(); public CustomRoom[] Rooms = [new()]; }
public sealed class Autopilot { public string DockedStation = "station"; }
public sealed class CustomRoom { public bool EnabledStructure = true; public string name = "Front_M00"; public UnityEngine.Transform transform = new(); }
namespace NPC.Core.Navigation {
 public readonly struct NavPath {
  public int Count => 1;
  public UnityEngine.Vector3 this[int i] => new(0,1,NavGraph.Partial ? -2 : -5);
 }
 public static class NavGraph {
  public static bool Partial;
  public static bool LastPathBlockedByDoor => false;
  public static NavPath? FindPath(UnityEngine.Vector3 from,UnityEngine.Vector3 to,bool mayGoOutside) => new NavPath();
 }
}
namespace Space { public class Player { } }
namespace YourBuddy
{
    public enum BuddyMode { Stay, Route }
    public sealed partial class BuddyBehaviour
    {
        internal readonly Errand Chore = new();
        public bool ChoreActive => choreOrder != null;
        public void StartChore() => StartChoreOrder(Chore, "tidying");
        public void TickChore() => ContinueChoreOrder(new Player());
        internal readonly StoreErrand storing = new();
        internal readonly Agent agent = new();
        internal BuddyMode? orderedMode;
        public string Name => "Buddy";
        public string? Busy, Blocked;
        public bool Survival;
        public int Finished;
        public bool Active => storeOrder;
        public void Interrupt() => InterruptStoreJob();
        public void Tick() { if (storeOrder) ContinueStoreOrder(new Player()); }
        public int Returns;
        private BuddyMode mode;
        private object? reachTask;
        public void SetActiveLeg() => reachTask = new object();
        public void InterruptAnyLeg() => InterruptCurrentErrand();
        private void DropReachTask() { }
        private void StartRoute(NPC.Core.Navigation.NavPath path, Vector3 goal) { Returns++; mode = BuddyMode.Route; }
        private string? BusyForCommand() => Busy;
        private string? StandDownReason(Player player, bool ignoreOrder) => Blocked ?? (mode == BuddyMode.Route ? "walking a route" : null);
        private bool TrySurvival() => Survival;
        private void ApplyOrder(BuddyMode order) { StopStoreOrder(); orderedMode = order; }
        private void FinishRoute() { Finished++; reachTask = null; storing.Running = false; mode = BuddyMode.Stay; }
        private void TraceDecider(string why) { }
    }
    internal sealed class Agent { internal bool Aboard = true; internal bool IsOutside => false; internal string CurrentOwner => "station"; internal Vector3 FloorUnderNpc() => new(); internal bool IsAboardPlayerShip() => Aboard; }
    internal class Errand
    {
        internal void CancelRepack() { }
        internal void CloseStorageDoors() { }
        internal bool Running, Found;
        internal int Attempts;
        internal float DueAt, DeferredUntil;
        internal float Interval => 8;
        internal float RetryDelay => 60;
        internal bool TryStart(out string report) { Attempts++; report = "none"; Running = Found; DueAt = Time.time + 60; return Found; }
    }
    internal sealed class StoreErrand : Errand { }
    internal static class YourBuddyPlugin { internal static readonly Logger Log = new(); }
    internal sealed class Logger { internal void LogInfo(string text) { } }
}

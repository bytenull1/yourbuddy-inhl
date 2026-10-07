using NPC.Core.Navigation;
using UnityEngine;

namespace YourBuddy
{
    public sealed partial class BuddyBehaviour
    {
        private bool storeReturning;
        private float storeReturnAt;

        private bool StoreShipConnected()
        {
            SpaceShip? ship = GameManager.Instance != null ? GameManager.Instance.PlayerShip : null;
            return ship != null && !agent.IsOutside && !string.IsNullOrEmpty(ship.Autopilot.DockedStation) &&
                agent.CurrentOwner == ship.Autopilot.DockedStation;
        }

        private void CheckStoreReturn()
        {
            if (!storeReturning) return;
            if (mode != BuddyMode.Route) { storeReturning = false; return; }
            if (!agent.IsAboardPlayerShip() && !StoreShipConnected())
            {
                storeReturning = false;
                FinishRoute();
                ReportStoreWait("I need the ship docked here to return.", "return interrupted: ship disconnected");
            }
        }

        private void ReturnForStorage()
        {
            if (Time.time < storeReturnAt) return;
            storeReturnAt = Time.time + 15f;
            if (!StoreShipConnected())
            {
                ReportStoreWait("I need the ship docked here to return.", "storage return: no connected ship");
                return;
            }
            SpaceShip ship = GameManager.Instance.PlayerShip;
            CustomRoom? cockpit = null;
            foreach (CustomRoom room in ship.Rooms)
                if (room != null && room.EnabledStructure && room.name == "Front_M00") { cockpit = room; break; }
            if (cockpit == null)
            {
                ReportStoreWait("I cannot find the ship entrance.", "storage return: no enabled cockpit");
                return;
            }
            Vector3 goal = cockpit.transform.TransformPoint(new Vector3(0f, 1f, -5f));
            NavPath? route = NavGraph.FindPath(agent.FloorUnderNpc(), goal, mayGoOutside: false);
            if (route is not { Count: > 0 } path || (path[path.Count - 1] - goal).sqrMagnitude > 1f)
            {
                ReportStoreWait("I cannot reach the ship yet.", NavGraph.LastPathBlockedByDoor ?
                    "storage return: a door I cannot open is in the way" : "storage return: no complete route");
                return;
            }
            DropReachTask();
            storeReturning = true;
            StartRoute(path, goal);
            ReportStoreWait("I'm heading to the ship to store its supplies.", "returning aboard for storage");
        }

        private void CancelStoreReturn()
        {
            if (storeReturning && mode == BuddyMode.Route) FinishRoute();
            storeReturning = false;
        }
    }
}

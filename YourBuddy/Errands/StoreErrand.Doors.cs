using System.Collections.Generic;
using UnityEngine;

namespace YourBuddy
{
    internal sealed partial class StoreErrand
    {
        private readonly Dictionary<Transform, (FurnitureStorage Storage, List<Door> Opened)> doorSessions = new();
        private readonly List<Transform> doorsToClose = [];
        private float doorsIdleUntil;

        private void OpenStorageDoors(FurnitureStorage storage)
        {
            if (!doorSessions.TryGetValue(storage.Root, out var session))
            {
                session = (storage, new List<Door>());
                doorSessions.Add(storage.Root, session);
            }
            // Retain ownership across jobs, but never claim doors already opened by the player.
            session.Opened.RemoveAll(door => door == null || !door.Opened);
            bool closed = false;
            foreach (Door door in storage.Doors)
                if (door != null && !door.Opened) closed = true;
            if (closed) Items.OpenContainerDoors(storage.Doors, session.Opened, storage.Name);
        }

        internal void MaintainStorageDoors()
        {
            doorsToClose.Clear();
            foreach (var pair in doorSessions)
            {
                FurnitureStorage storage = pair.Value.Storage;
                bool manipulating = pair.Key != null && Body.Leg is Leg leg && (leg.Reaching || leg.Extracting) && leg.Holds(pair.Key);
                if (manipulating) continue;
                if (pair.Key == null || Items.FlatDistanceSq(storage.Approach, Here) > 6.25f ||
                    (!Running && Time.time >= doorsIdleUntil)) doorsToClose.Add(pair.Key!); // Unity destroyed objects retain their dictionary key.
            }
            foreach (Transform root in doorsToClose)
            {
                var session = doorSessions[root];
                Items.CloseOpenedDoors(session.Opened, session.Storage.Hideout, session.Storage.Name);
                doorSessions.Remove(root);
            }
        }

        internal void CloseStorageDoors()
        {
            foreach (var session in doorSessions.Values)
                Items.CloseOpenedDoors(session.Opened, session.Storage.Hideout, session.Storage.Name);
            doorSessions.Clear();
        }
    }
}

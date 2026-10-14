using System.Collections.Generic;
using NPC.Core.Agents;
using NPC.Core.Navigation;
using UnityEngine;

namespace YourBuddy
{
    internal sealed partial class ResourceErrand
    {
        private enum StorageResult { None, Found, Pending }

        // A clear spot aboard for a carried or prospective cell. Probes and reach plans are budgeted, so a
        // search can span frames: Pending says it goes on next frame. docs/resources.md#scans-and-storage
        private sealed class StorageSearch(ResourceErrand job)
        {
            // Scanning collects up to StorageChoices reachable spots; Chosen walks the shortest; Recheck
            // waits for a plan after the chosen route's switched-off rooms loaded.
            private enum Stage { Scanning, Chosen, Recheck }

            private readonly List<Vector3> points = [];
            // The next spot to settle; one waiting for a probe or a plan is tried again. A walk sets it back
            // to the first spot, so a later search in the run can fall back to a longer one.
            private int cursor;
            private float refreshAt;
            private Vector3 half;
            private Stage stage;
            private Leg? best;
            private NavPath? bestRoute;
            private float bestLength;
            private int found;
            private int walks;

            // Spots dropped this run, also by the delivery leg.
            internal readonly List<Vector3> Rejected = [];
            internal string? Failure { get; private set; }
            internal bool Pending { get; private set; }

            internal void Reset()
            {
                walks = 0;
                Rejected.Clear();
                Failure = null;
                Pending = false;
                ResetChoice();
            }

            private void ResetChoice()
            {
                best = null;
                bestRoute = null;
                found = 0;
                stage = Stage.Scanning;
            }

            // Whether `prospective` would have a reachable spot; nothing is walked.
            internal StorageResult Check(Grabbable prospective) => Step(false, prospective);

            // Walks the carried cell to the shortest of the first few reachable spots.
            internal StorageResult Walk() => Step(true, null);

            private StorageResult Step(bool walk, Grabbable? prospective)
            {
                StorageResult result = Search(walk, prospective);
                Pending = result == StorageResult.Pending;
                return result;
            }

            private StorageResult Search(bool walk, Grabbable? prospective)
            {
                ResourceController? loader = job.loader;
                if (loader == null || walks >= MaxStorageWalks) return StorageResult.None;
                NpcHands hands = job.Body.Hands;
                Vector3 size = hands.Item != null ? hands.Extents : Vector3.one * .15f;
                if (prospective != null && NpcHands.ColliderBounds(prospective.gameObject, out Bounds bounds) && bounds.size.sqrMagnitude > .001f)
                    size = bounds.extents;
                Vector3 want = ResourceStorage.Clearance(size);
                bool resized = (want - half).sqrMagnitude > .0001f;
                // Finish a pending footprint before considering a differently sized shop product.
                if (Pending && resized && Time.time < refreshAt) return StorageResult.Pending;
                // A walk's choice waiting for its re-check is no answer to a prospective check.
                if (!walk && stage != Stage.Scanning) ResetChoice();
                if (points.Count == 0 || resized || Time.time >= refreshAt)
                {
                    ResourceStorage.Candidates(loader.transform.position, points);
                    cursor = 0;
                    ResetChoice();
                    half = want;
                    refreshAt = Time.time + StorageRescanSeconds;
                }
                int plans = 0, floors = 0, clear = 0;
                string blocker = "no supported ship floor near active nodes";
                for (; stage == Stage.Scanning && cursor < points.Count; cursor++)
                {
                    if (!ResourceStorage.MayProbe()) return StorageResult.Pending;
                    if (!ResourceStorage.FindFloor(points[cursor], out RaycastHit floor)) continue;
                    floors++;
                    Room? room = floor.collider.GetComponentInParent<Room>();
                    if (room != null) job.Body.LoadRoomOf(room.ContentParent);
                    Vector3 point = floor.point + Vector3.up * (want.y + .03f);
                    if (Rejected.Exists(p => (p - point).sqrMagnitude < RejectedStorageSqr)) continue;
                    Transform? carried = hands.Item != null ? hands.Item.transform : prospective != null ? prospective.transform : null;
                    if (!ResourceStorage.Clear(point, want, carried))
                    {
                        blocker = ResourceStorage.LastBlocker;
                        continue;
                    }
                    clear++;
                    Leg leg = new(job, point, loader.transform, Phase.Deliver)
                    {
                        Floor = floor.collider.transform,
                        FloorPoint = floor.collider.transform.InverseTransformPoint(point)
                    };
                    NavPath? route = null;
                    float length = 0f;
                    if (job.Body.InReach(leg))
                    {
                        leg.Node = leg.StandPoint = job.Body.Transform.position;
                    }
                    else
                    {
                        bool? known = job.Remembered(leg);
                        if (known == false)
                        {
                            blocker = "no way to it, found lately";
                            continue;
                        }
                        if (known == true && !walk) return StorageResult.Found;
                        // One plan a frame: this spot is planned next frame.
                        if (!job.MayPlanNow) return StorageResult.Pending;

                        plans++;
                        string? failure = job.Body.PlanReach(leg, out NavPath planned);
                        job.Remember(leg, failure == null);
                        if (failure != null)
                        {
                            blocker = failure;
                            continue;
                        }
                        route = planned;
                        length = RouteLength(planned, point);
                    }
                    // A prospective check needs only one reachable spot.
                    if (!walk) return StorageResult.Found;

                    if (best == null || length < bestLength)
                    {
                        best = leg;
                        bestRoute = route;
                        bestLength = length;
                    }
                    if (++found >= StorageChoices || length <= 0f) stage = Stage.Chosen;
                }
                if (walk && best != null)
                {
                    if (stage == Stage.Scanning) stage = Stage.Chosen;
                    StorageResult walked = WalkToBest(want);
                    // The spot was dropped once its route's rooms loaded: search again without it.
                    return walked == StorageResult.None ? Step(true, null) : walked;
                }
                Failure = clear > 0 ? "Waiting: clear storage exists, but Buddy cannot reach it. Check the ship's doors and approach." :
                    "Waiting: no clear floor storage found aboard. Make room for a spare cell.";
                ResourceDuty.Trace($"Storage scan: {floors} supported, {clear} clear, {plans} route checks; half-size {want}; {blocker}.", "storage scan");
                ResourceDuty.Report(Failure, "storage");
                return StorageResult.None;
            }

            // The route's switched-off rooms are loaded first, so their furniture is in the checks; then the
            // spot is checked and planned again. None after dropping it.
            private StorageResult WalkToBest(Vector3 want)
            {
                // Only called with a chosen spot.
                Leg leg = best!;
                NavPath? route = bestRoute;
                if (route.HasValue && LoadRouteRooms(route.Value, leg.TargetPoint)) stage = Stage.Recheck;
                if (stage == Stage.Recheck)
                {
                    if (!job.MayPlanNow) return StorageResult.Pending;

                    stage = Stage.Chosen;
                    Transform? carried = job.Body.Hands.Item != null ? job.Body.Hands.Item.transform : null;
                    NavPath replanned = default;
                    string? failure = ResourceStorage.Clear(leg.TargetPoint, want, carried)
                        ? job.Body.PlanReach(leg, out replanned) : ResourceStorage.LastBlocker;
                    if (failure != null)
                    {
                        ResourceDuty.Trace($"Storage at {leg.TargetPoint} dropped once its route's rooms loaded: {failure}.", "storage scan");
                        Rejected.Add(leg.TargetPoint);
                        ResetChoice();
                        cursor = 0;
                        return StorageResult.None;
                    }
                    route = replanned;
                }
                cursor = 0;
                ResetChoice();
                job.Body.Walk(leg, route);
                walks++;
                YourBuddyPlugin.Log.LogInfo($"[resources] Carrying cell to clear ship storage at {leg.TargetPoint}");
                return StorageResult.Found;
            }

            private float RouteLength(NavPath route, Vector3 end)
            {
                float length = 0f;
                Vector3 from = job.Here;
                for (int i = 0; i <= route.Count; i++)
                {
                    Vector3 to = i < route.Count ? route[i] : end;
                    length += Mathf.Sqrt((to - from).sqrMagnitude);
                    from = to;
                }
                return length;
            }

            // Ship rooms under the route, sampled every metre; true when one was switched on.
            private bool LoadRouteRooms(NavPath route, Vector3 end)
            {
                bool loaded = false;
                Vector3 from = job.Here;
                for (int i = 0; i <= route.Count; i++)
                {
                    Vector3 to = i < route.Count ? route[i] : end;
                    int steps = Mathf.CeilToInt(Mathf.Sqrt((to - from).sqrMagnitude));
                    for (int k = 1; k <= steps; k++)
                    {
                        if (!ResourceStorage.FindFloor(from + (to - from) * ((float)k / steps), out RaycastHit floor)) continue;
                        Room? room = floor.collider.GetComponentInParent<Room>();
                        if (room == null || room.ContentEnabled) continue;
                        job.Body.LoadRoomOf(room.ContentParent);
                        loaded |= room.ContentEnabled;
                    }
                    from = to;
                }
                return loaded;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Hand-placed navigation nodes, stored per owner ("ship", "world", or a station
    /// name) in that owner's local space, which is what keeps them valid across
    /// flight, docking and saves. See docs/navigation.md.
    /// </summary>
    public static class BuddyNodeGraph
    {
        public enum NodeType
        {
            Ground = 0,
            Stair = 1
        }

        public enum LinkMode
        {
            Force = 0,
            Block = 1,
            // Directional rule: whenever a path arrives at the first node of the link
            // from anywhere else, the only allowed exit is the second node. Arriving
            // Via the second node leaves all other exits open, so return trips work.
            Priority = 2
        }

        public enum EdgeKind
        {
            Auto = 0,
            Forced = 1,
            Blocked = 2,
            Priority = 3
        }

        /// <summary>
        /// A planned route in world space. WaypointIsForced[i]: the edge arriving at i is Force or
        /// Priority (docs/invariants.md#force-and-priority-have-no-los). WaypointFloorY[i]: the
        /// deck under i as the search measured it, never the marker's Y (#floor-to-floor).
        /// </summary>
        public readonly record struct NavPath(IReadOnlyList<Vector3> Waypoints, IReadOnlyList<bool> WaypointIsForced,
            IReadOnlyList<float> WaypointFloorY)
        {
            public int Count => Waypoints?.Count ?? 0;
            public Vector3 this[int i] => Waypoints[i];
            public bool IsForced(int i) => WaypointIsForced != null && i < WaypointIsForced.Count && WaypointIsForced[i];
            public float FloorY(int i) => WaypointFloorY[i];
        }

        /// <summary>
        /// "Does this straight stretch pass through a door the buddy cannot open?"
        /// Supplied by BuddyBehaviour so the graph stays free of gameplay rules; null
        /// means no filter. docs/invariants.md#locked-doors-block-edges
        /// </summary>
        public static Func<Vector3, Vector3, bool>? SegmentBlockedByDoor;

        /// <summary>
        /// True when the last FindPath rejected something for a door it cannot open.
        /// Follow reads it to stand still instead of wandering off:
        /// docs/invariants.md#stand-still-when-door-blocked
        /// </summary>
        public static bool LastPathBlockedByDoor { get; private set; }

        private static bool DoorBlocks(Vector3 a, Vector3 b)
        {
            if (SegmentBlockedByDoor == null || !SegmentBlockedByDoor(a, b)) return false;

            LastPathBlockedByDoor = true;
            return true;
        }

        public const string ShipOwner = "ship";
        public const string WorldOwner = "world";

        // Deck-to-deck span an auto edge may cross; steeper needs a Stair endpoint or a
        // Force link. Node-to-node only - the finish leg uses SameLevelDeltaY:
        // docs/invariants.md#finish-may-not-change-deck
        private const float MaxDirectDeltaY = 0.8f;
        // With a Stair endpoint, auto edges may climb up to this height.
        private const float MaxStairDeltaY = 2.0f;

        private sealed class Node
        {
            public int Id;
            public string Owner = null!; // always set by its object initializer
            public Vector3 LocalPos;
            public NodeType Type;
            public bool AutoLink = true; // false = manual links only for this node
            /// <summary>
            /// Came from the graph shipped inside the DLL, so it is not written to the
            /// user's file and a mod update may replace it. docs/navigation.md
            /// </summary>
            public bool Bundled;
            /// <summary>
            /// Ship nodes only: the room it stands on, HullAnchor, or null while no floor has been
            /// found under it. AnchorAt is that room's ship-local position when the node was placed.
            /// docs/invariants.md#a-ship-node-rides-its-room
            /// </summary>
            public string? Anchor;
            public Vector3 AnchorAt;
        }

        private sealed class ManualLink(int a, int b, LinkMode mode, Vector3 offset)
        {
            public readonly int A = a;
            public readonly int B = b;
            public LinkMode Mode = mode; // toggled between modes in place
            /// <summary>
            /// How far A's room had moved relative to B's when the link was drawn; the link holds in
            /// that ship layout only. docs/invariants.md#a-ship-node-rides-its-room
            /// </summary>
            public readonly Vector3 Offset = offset;
        }

        private readonly struct Edge(int to, float cost, EdgeKind kind)
        {
            public readonly int To = to;
            public readonly float Cost = cost;
            public readonly EdgeKind Kind = kind;
        }

        // ------------------------------------------------------------------
        // Save file
        // ------------------------------------------------------------------

        private sealed class NodeFile
        {
            public int Id;
            public float[]? P;
            public int T;
            public bool A = true; // AutoLink
            // Node.Anchor / AnchorAt; absent on other owners and on ship nodes not yet anchored.
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string? R;
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public float[]? S;
        }

        private sealed class GraphFile
        {
            public string? Owner;
            public uint OwnerID;

            [JsonProperty("Nodes")]
            public List<NodeFile?>? Entries;
        }

        private sealed class LinkFile
        {
            public int A;
            public int B;
            public int M;
            // ManualLink.Offset; absent when zero, which is every link drawn in the layout its nodes were placed in.
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public float[]? O;
        }

        private sealed class NodeGraphFile
        {
            // ReSharper disable FieldCanBeMadeReadOnly.Local
            public int Version = 4;
            /// <summary>
            /// Bundled file only: bumped by hand when the shipped graph changes, which
            /// is what makes an update land without touching the user's own file.
            /// </summary>
            public int BundleVersion = 0;
            /// <summary>
            /// User file only: owners the player has taken over. Kept explicitly so an
            /// owner can be forked and then emptied - "I want no nodes here" is not the
            /// same answer as "I never touched this".
            /// </summary>
            [JsonProperty("ForkedOwners")]
            public List<string>? Forked = [];
            public List<GraphFile?>? Graphs = [];
            public List<LinkFile?>? Links = [];
            // ReSharper restore FieldCanBeMadeReadOnly.Local
        }

        // ReSharper disable RedundantDefaultMemberInitializer
        private static readonly List<Node> Nodes = [];
        private static readonly List<ManualLink> ManualLinks = [];
        private static readonly Dictionary<string, uint> OwnerIds = [];
        /// <summary>
        /// Owners the player has taken over from the bundled graph.
        /// </summary>
        private static readonly HashSet<string> ForkedOwners = [];
        /// <summary>
        /// Every node id in the graph, so the two files cannot end up sharing one.
        /// </summary>
        private static readonly HashSet<int> UsedIds = [];
        private static bool _hasBundledNodes;
        /// <summary>
        /// New user nodes are numbered from here, so a later bundle update can add ids
        /// of its own without ever colliding with something the player placed.
        /// </summary>
        private const int UserIdBase = 100000;
        private static int _nextId = 1;
        private static bool _loaded = false;
        private static bool _dirty = false;
        private static float _lastSaveAt = 0f;

        // Cached adjacency lists over active nodes (node id -> outgoing edges).
        private static Dictionary<int, List<Edge>>? _edges = null;
        // Node id -> set of priority-exit targets: when a path arrives at such a node
        // from anywhere else, A* may only leave through one of these targets.
        private static Dictionary<int, HashSet<int>>? _priorityExits = null;
        // Node id -> node, rebuilt with the edge cache. A duplicated id resolves to the first
        // node, as the linear scan this replaced did.
        private static readonly Dictionary<int, Node> NodesById = [];
        private static bool _edgesDirty = true;
        private static string? _lastDockSig = null;
        private static float _lastMaxEdgeDist = -1f;

        // Node id -> how far the marker floats above the floor underneath it.
        // See NodeFloorY for why the hover is cached rather than the floor height.
        private static readonly Dictionary<int, float> NodeHover = [];
        /// <summary>
        /// Survives the TTL and every edge rebuild: the last hover actually measured for
        /// a node, used when the floor under it cannot be probed at all.
        /// </summary>
        private static readonly Dictionary<int, float> LastKnownHover = [];
        private static float _typicalHover;
        private static int _typicalHoverAt = -1;
        private static float _nodeHoverRefreshAt = 0f;
        private const float NodeHoverTtl = 3f;

        // Scene cache of every SpaceObject (stations, derelicts) for owner resolution.
        private static readonly List<SpaceObject> SpaceObjects = [];
        private static readonly List<string?> SpaceObjectNames = [];
        private static float _spaceObjectsRefreshAt = 0f;
        private static bool _spaceObjectsValid = false;
        // ReSharper restore RedundantDefaultMemberInitializer

        // SpaceStation.Rooms reflection access lives in GameInternals.SpaceStationAccess.

        private static string RoutesDir => Path.Combine(BepInEx.Paths.ConfigPath, "YourBuddyRoutes");
        /// <summary>
        /// The player's own graph. Only F6 and the autosave ever write it.
        /// </summary>
        private static string GraphPath => Path.Combine(RoutesDir, "nodegraph.json");
        /// <summary>
        /// A cache of the graph inside the DLL, rewritten whenever the mod ships a newer
        /// one. Never user data - editing it is pointless, it is overwritten.
        /// </summary>
        private static string BundledGraphPath => Path.Combine(RoutesDir, "nodegraph.bundled.json");
        private const string BundledResourceName = "YourBuddy.Resources.nodegraph.bundled.json";

        public static int NodeCount
        {
            get { EnsureLoaded(); return Nodes.Count; }
        }

        private static bool ValidIndex(int index)
        {
            return index >= 0 && index < Nodes.Count;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;

            _loaded = true;

            NodeGraphFile? user = ReadGraphFile(GraphPath, "nodegraph.json");
            // An owner the user has nodes for counts as forked even without the marker,
            // so a graph written before the bundle existed is never merged into.
            if (user != null)
            {
                if (user.Forked != null) ForkedOwners.UnionWith(user.Forked);

                if (user.Graphs != null)
                {
                    foreach (GraphFile? g in user.Graphs)
                    {
                        if (g?.Entries is { Count: > 0 })
                        {
                            ForkedOwners.Add(OwnerNameOf(g));
                        }
                    }
                }
            }

            NodeGraphFile? bundled = LoadBundled();
            if (bundled != null) Ingest(bundled, true);

            if (user != null) Ingest(user, false);

            _edgesDirty = true;
        }

        /// <summary>
        /// Owners come out of the file as names; an empty one is the world container.
        /// </summary>
        private static string OwnerNameOf(GraphFile g) =>
            string.IsNullOrEmpty(g.Owner) ? WorldOwner : g.Owner;

        private static NodeGraphFile? ReadGraphFile(string path, string label)
        {
            try
            {
                if (!File.Exists(path)) return null;

                NodeGraphFile? data = JsonConvert.DeserializeObject<NodeGraphFile>(File.ReadAllText(path));
                if (data == null || data.Version < 4 || data.Graphs == null)
                {
                    YourBuddyPlugin.Log.LogWarning(
                        "[nav] " + label + " is corrupt or a legacy (v3) file - ignored. " +
                        "Delete the file to start a fresh graph.");
                    return null;
                }
                return data;
            }
            catch (Exception ex)
            {
                YourBuddyPlugin.Log.LogWarning("[nav] Load of " + label + " failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Adds one file's graphs to the in-memory set. A bundled owner the user has
        /// taken over is skipped wholesale: ownership is answered per owner, so a fork
        /// is never a half-merge nobody can reason about. docs/navigation.md
        /// </summary>
        private static void Ingest(NodeGraphFile data, bool bundled)
        {
            HashSet<int> added = [];
            Dictionary<int, int>? renumbered = null;
            // ReadGraphFile and LoadBundled reject a file without Graphs.
            foreach (GraphFile? g in data.Graphs!)
            {
                if (g?.Entries == null) continue;

                string owner = OwnerNameOf(g);
                if (bundled && ForkedOwners.Contains(owner)) continue;

                if (g.OwnerID != 0) OwnerIds[owner] = g.OwnerID;

                foreach (NodeFile? nf in g.Entries)
                {
                    if (nf?.P is not { Length: >= 3 })
                    {
                        continue;
                    }

                    int id = nf.Id;
                    // A graph written before the bundle existed can reuse one of its ids.
                    // Two nodes sharing an id makes every link between them ambiguous, so
                    // the player's copy is moved into the range reserved for it.
                    if (!bundled && UsedIds.Contains(id))
                    {
                        if (_nextId < UserIdBase) _nextId = UserIdBase;

                        int fresh = _nextId++;
                        (renumbered ??= [])[id] = fresh;
                        id = fresh;
                    }
                    Nodes.Add(new Node
                    {
                        Id = id,
                        Owner = owner,
                        LocalPos = new Vector3(nf.P[0], nf.P[1], nf.P[2]),
                        Type = (NodeType)nf.T,
                        AutoLink = nf.A,
                        Bundled = bundled,
                        Anchor = owner == ShipOwner && !string.IsNullOrEmpty(nf.R) ? nf.R : null,
                        AnchorAt = nf.S is { Length: >= 3 } ? new Vector3(nf.S[0], nf.S[1], nf.S[2]) : Vector3.zero
                    });
                    UsedIds.Add(id);
                    added.Add(id);
                    if (bundled) _hasBundledNodes = true;

                    if (id >= _nextId) _nextId = id + 1;
                }
            }

            if (renumbered != null)
            {
                _dirty = true;
                YourBuddyPlugin.Log.LogWarning(
                    "[nav] " + renumbered.Count + " of your nodes had ids the bundled graph also uses; " +
                    "they were renumbered and will be written back on the next save.");
            }

            if (data.Links == null) return;

            foreach (LinkFile? lf in data.Links)
            {
                if (lf == null) continue;

                int a = lf.A;
                int b = lf.B;
                if (renumbered != null)
                {
                    if (renumbered.TryGetValue(a, out int movedA)) a = movedA;

                    if (renumbered.TryGetValue(b, out int movedB)) b = movedB;
                }
                // A bundled link whose ends did not survive the owner filter is not a
                // relation anymore - the user owns that side of it now.
                if (bundled && (!added.Contains(a) || !added.Contains(b))) continue;

                Vector3 offset = lf.O is { Length: >= 3 } ? new Vector3(lf.O[0], lf.O[1], lf.O[2]) : Vector3.zero;
                ManualLinks.RemoveAll(l => SamePair(l, a, b) && SameOffset(l, a, offset));
                ManualLinks.Add(new ManualLink(a, b, (LinkMode)lf.M, offset));
            }
        }

        /// <summary>
        /// Writes the graph shipped inside the DLL out to disk whenever the version on
        /// disk is not the one this build carries, then loads it. That file is a cache,
        /// so an update lands on its own without ever touching the user's own graph.
        /// </summary>
        private static NodeGraphFile? LoadBundled()
        {
            if (!YourBuddyPlugin.ConfigBundledGraph.Value)
            {
                return null;
            }

            try
            {
                string? embedded = ReadEmbeddedBundle();
                if (embedded == null) return null;

                NodeGraphFile? shipped = JsonConvert.DeserializeObject<NodeGraphFile>(embedded);
                if (shipped?.Graphs == null) return null;

                NodeGraphFile? onDisk = ReadGraphFile(BundledGraphPath, "nodegraph.bundled.json");
                if (onDisk == null || onDisk.BundleVersion != shipped.BundleVersion)
                {
                    Directory.CreateDirectory(RoutesDir);
                    File.WriteAllText(BundledGraphPath, embedded);
                    YourBuddyPlugin.Log.LogInfo(
                        "[nav] Wrote the bundled node graph (version " + shipped.BundleVersion +
                        ") to " + BundledGraphPath);
                }
                return shipped;
            }
            catch (Exception ex)
            {
                YourBuddyPlugin.Log.LogWarning("[nav] Bundled graph unavailable: " + ex.Message);
                return null;
            }
        }

        private static string? ReadEmbeddedBundle()
        {
            using Stream? stream = typeof(BuddyNodeGraph).Assembly
                .GetManifestResourceStream(BundledResourceName);
            if (stream == null)
            {
                YourBuddyPlugin.Log.LogWarning(
                    "[nav] This build carries no bundled node graph - only your own nodes are used.");
                return null;
            }
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }

        public static void Save()
        {
            try
            {
                EnsureLoaded();
                string? dir = Path.GetDirectoryName(GraphPath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                NodeGraphFile data = new();
                // A new NodeGraphFile starts with empty lists.
                data.Forked!.AddRange(ForkedOwners);

                // Only the player's own nodes are written here; bundled ones live in the
                // DLL and are rewritten by updates. docs/navigation.md
                List<string> order = [];
                Dictionary<string, List<NodeFile?>> byOwner = [];
                HashSet<int> ownIds = [];
                foreach (Node n in Nodes)
                {
                    if (n.Bundled) continue;

                    ownIds.Add(n.Id);
                    if (!byOwner.TryGetValue(n.Owner, out List<NodeFile?>? list))
                    {
                        list = [];
                        byOwner[n.Owner] = list;
                        order.Add(n.Owner);
                    }
                    list.Add(new NodeFile
                    {
                        Id = n.Id, P = [n.LocalPos.x, n.LocalPos.y, n.LocalPos.z], T = (int)n.Type, A = n.AutoLink,
                        R = n.Anchor,
                        S = n.Anchor != null && n.AnchorAt != Vector3.zero
                            ? [n.AnchorAt.x, n.AnchorAt.y, n.AnchorAt.z]
                            : null
                    });
                }
                foreach (string owner in order)
                {
                    OwnerIds.TryGetValue(owner, out uint ownerId);
                    data.Graphs!.Add(new GraphFile { Owner = owner, OwnerID = ownerId, Entries = byOwner[owner] });
                }
                foreach (ManualLink l in ManualLinks)
                {
                    // A link with one end on a forked owner is the player's to keep or
                    // delete; one wholly inside the bundle is the bundle's.
                    if (!ownIds.Contains(l.A) && !ownIds.Contains(l.B)) continue;

                    data.Links!.Add(new LinkFile
                    {
                        A = l.A, B = l.B, M = (int)l.Mode,
                        O = l.Offset != Vector3.zero ? [l.Offset.x, l.Offset.y, l.Offset.z] : null
                    });
                }

                File.WriteAllText(GraphPath, JsonConvert.SerializeObject(data, Formatting.Indented));
                _dirty = false;
                _lastSaveAt = Time.time;
            }
            catch (Exception ex)
            {
                YourBuddyPlugin.Log.LogError("[nav] Save failed: " + ex.Message);
            }
        }

        public static void TickSave()
        {
            if (_dirty && Time.time - _lastSaveAt > 20f) Save();
        }

        // ----------------------------------------------------------------
        // Bundled graph ownership
        // ----------------------------------------------------------------

        /// <summary>
        /// Takes an owner over from the bundled graph. Editing one bundled node makes
        /// the whole owner the player's, because ownership is per owner - anything finer
        /// leaves a graph nobody can predict after an update. docs/navigation.md
        /// </summary>
        private static void ForkOwner(string owner)
        {
            if (string.IsNullOrEmpty(owner) || !ForkedOwners.Add(owner)) return;

            int taken = 0;
            foreach (Node n in Nodes)
            {
                if (n.Owner != owner || !n.Bundled) continue;

                n.Bundled = false;
                taken++;
            }
            // Anything the player places from here on goes in the player's file, so its
            // ids must be out of reach of a later bundle.
            if (_nextId < UserIdBase) _nextId = UserIdBase;

            _dirty = true;
            if (taken > 0)
            {
                YourBuddyPlugin.Log.LogInfo(
                    "[nav] '" + owner + "' is yours now (" + taken + " bundled nodes copied); " +
                    "mod updates will no longer change it.");
            }
        }

        private static void ForkOwnerOf(int index)
        {
            if (ValidIndex(index) && Nodes[index].Bundled) ForkOwner(Nodes[index].Owner);
        }

        /// <summary>
        /// Gives an owner back to the bundled graph, discarding the player's version of
        /// it. Takes effect on the next load, like every other graph read.
        /// </summary>
        public static bool UnforkOwner(string owner)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(owner) || !ForkedOwners.Remove(owner)) return false;

            int removedNodes = Nodes.RemoveAll(n => n.Owner == owner);
            // Against the node list, not NodesById: that index belongs to the edge cache
            // and is a rebuild behind this mutation.
            HashSet<int> live = [];
            foreach (Node n in Nodes) live.Add(n.Id);

            UsedIds.IntersectWith(live);
            ManualLinks.RemoveAll(l => !live.Contains(l.A) || !live.Contains(l.B));
            _dirty = true;
            _edgesDirty = true;
            Save();
            YourBuddyPlugin.Log.LogInfo(
                "[nav] '" + owner + "' handed back to the bundled graph (" + removedNodes +
                " of your nodes dropped); restart to load it.");
            return true;
        }

        /// <summary>
        /// Which owners come from the mod and which the player has taken over.
        /// </summary>
        public static string DescribeBundle()
        {
            EnsureLoaded();
            if (!_hasBundledNodes && ForkedOwners.Count == 0) return "no bundled graph in this build";

            Dictionary<string, int> bundled = [];
            Dictionary<string, int> own = [];
            foreach (Node n in Nodes)
            {
                Dictionary<string, int> target = n.Bundled ? bundled : own;
                target.TryGetValue(n.Owner, out int count);
                target[n.Owner] = count + 1;
            }
            List<string> parts = [];
            foreach (KeyValuePair<string, int> kv in bundled) parts.Add(kv.Key + " " + kv.Value + " (mod)");

            foreach (KeyValuePair<string, int> kv in own) parts.Add(kv.Key + " " + kv.Value + " (yours)");

            foreach (string owner in ForkedOwners)
            {
                if (!own.ContainsKey(owner)) parts.Add(owner + " 0 (yours, emptied)");
            }
            return string.Join(", ", parts);
        }

        // ----------------------------------------------------------------
        // Node management
        // ----------------------------------------------------------------

        public static void AddNode(Vector3 worldPos)
        {
            EnsureLoaded();
            DetectOwner(worldPos, out string owner, out Transform? ownerT);
            Vector3 floor = FloorUnder(worldPos, ownerT);
            Vector3 local = ownerT != null ? ownerT.InverseTransformPoint(floor) : floor;

            // Against where live nodes are now: a ship node's stored point is where its room was.
            OwnerSnapshot owners = new();
            foreach (Node n in Nodes)
            {
                if (n.Owner == owner && owners.IsLive(n) && (owners.WorldOf(n) - floor).sqrMagnitude < 0.09f)
                {
                    return; // duplicate
                }
            }

            ForkOwner(owner);
            int id = _nextId++;
            UsedIds.Add(id);
            Node added = new() { Id = id, Owner = owner, LocalPos = local, Type = NodeType.Ground };
            if (owner == ShipOwner && !TryAnchorShipNode(added, worldPos)) added.Anchor = HullAnchor;

            Nodes.Add(added);
            _dirty = true;
            _edgesDirty = true;
        }

        /// <summary>
        /// Removes the node at the given index. Returns true if removed.
        /// </summary>
        public static bool RemoveNode(int index)
        {
            EnsureLoaded();
            if (!ValidIndex(index)) return false;

            ForkOwnerOf(index);
            int removedId = Nodes[index].Id;
            Nodes.RemoveAt(index);
            UsedIds.Remove(removedId);
            // Manual links pointing at a deleted node would dangle forever.
            ManualLinks.RemoveAll(l => l.A == removedId || l.B == removedId);
            _dirty = true;
            _edgesDirty = true;
            return true;
        }

        public static void Clear()
        {
            EnsureLoaded();
            // Emptying means "I want no nodes here", which only survives a restart if
            // every owner is marked as taken over first.
            foreach (Node n in Nodes)
            {
                if (n.Bundled) ForkedOwners.Add(n.Owner);
            }
            if (_nextId < UserIdBase) _nextId = UserIdBase;

            UsedIds.Clear();
            Nodes.Clear();
            ManualLinks.Clear();
            _dirty = true;
            _edgesDirty = true;
        }

        /// <summary>
        /// Returns a random node in world space, or Vector3.zero if no nodes exist.
        /// Only active nodes are considered, so the buddy never targets a station it
        /// is not docked to. `owner` narrows it to one ship or station; null means any.
        /// </summary>
        public static Vector3 RandomNode(string? owner = null)
        {
            EnsureLoaded();
            OwnerSnapshot owners = new();
            List<int> active = [];
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (owner != null && Nodes[i].Owner != owner) continue;

                if (owners.IsLive(Nodes[i])) active.Add(i);
            }
            if (active.Count == 0) return Vector3.zero;

            return owners.WorldOf(Nodes[active[UnityEngine.Random.Range(0, active.Count)]]);
        }

        /// <summary>
        /// The owner of the nearest active node within maxDist, or null. Node owners come from
        /// room lists, floor owners from collider ancestry, and the two can name one place
        /// differently: docs/behaviour.md §3
        /// </summary>
        public static string? NearestActiveNodeOwner(Vector3 worldPos, float maxDist)
        {
            EnsureLoaded();
            OwnerSnapshot owners = new();
            string? best = null;
            float bestDistanceSquared = maxDist * maxDist;
            foreach (Node n in Nodes)
            {
                if (!owners.IsLive(n)) continue;

                float distanceSquared = (owners.WorldOf(n) - worldPos).sqrMagnitude;
                if (distanceSquared >= bestDistanceSquared) continue;

                bestDistanceSquared = distanceSquared;
                best = n.Owner;
            }
            return best;
        }

        /// <summary>
        /// Every active node in world space, into a list the caller owns and reuses.
        /// </summary>
        public static void CollectActiveNodes(List<Vector3> into)
        {
            EnsureLoaded();
            into.Clear();
            OwnerSnapshot owners = new();
            foreach (Node n in Nodes)
            {
                if (owners.IsLive(n)) into.Add(owners.WorldOf(n));
            }
        }

        /// <summary>
        /// Returns all nodes in world space (every owner, active or not). The node
        /// editor distance-culls the markers it draws near the player, so large
        /// graphs do not paint cross-hairs across the whole sector.
        /// </summary>
        public static List<Vector3> GetAllNodesWorld()
        {
            EnsureLoaded();
            OwnerSnapshot owners = new();
            List<Vector3> result = new(Nodes.Count);
            foreach (Node n in Nodes) result.Add(owners.WorldOf(n));
            return result;
        }

        /// <summary>
        /// Returns a single node in world space, or zero when the index is out of range.
        /// </summary>
        public static Vector3 GetNodeWorld(int index)
        {
            EnsureLoaded();
            return ValidIndex(index) ? new OwnerSnapshot().WorldOf(Nodes[index]) : Vector3.zero;
        }

        // ----------------------------------------------------------------
        // Node metadata (type / owner) for editor and console
        // ----------------------------------------------------------------

        public static NodeType GetNodeType(int index)
        {
            EnsureLoaded();
            return ValidIndex(index) ? Nodes[index].Type : NodeType.Ground;
        }

        public static NodeType SetNodeType(int index, NodeType type)
        {
            EnsureLoaded();
            if (ValidIndex(index))
            {
                ForkOwnerOf(index);
                Nodes[index].Type = type;
                _dirty = true;
                _edgesDirty = true;
            }
            return type;
        }

        public static bool GetNodeAutoLink(int index)
        {
            EnsureLoaded();
            return !ValidIndex(index) || Nodes[index].AutoLink;
        }

        public static bool SetNodeAutoLink(int index, bool autoLink)
        {
            EnsureLoaded();
            if (ValidIndex(index))
            {
                ForkOwnerOf(index);
                Nodes[index].AutoLink = autoLink;
                _dirty = true;
                _edgesDirty = true;
            }
            return autoLink;
        }

        public static string? GetNodeOwner(int index)
        {
            EnsureLoaded();
            return ValidIndex(index) ? Nodes[index].Owner : null;
        }

        /// <summary>
        /// Which part of the ship a ship node rides ("Core_M01", "hull"), "unanchored" while it has
        /// none yet, or null for any other owner.
        /// </summary>
        public static string? GetNodeAnchor(int index)
        {
            EnsureLoaded();
            if (!ValidIndex(index) || Nodes[index].Owner != ShipOwner) return null;

            return Nodes[index].Anchor ?? "unanchored";
        }

        /// <summary>
        /// True when the node can currently be walked to: its owner is reachable (ship/world always,
        /// stations only while docked) and, on the ship, the room under it is built.
        /// </summary>
        public static bool IsNodeActive(int index)
        {
            EnsureLoaded();
            return ValidIndex(index) && new OwnerSnapshot().IsLive(Nodes[index]);
        }

        private const float NodeSummaryTtl = 0.25f;
        private static string? _nodeSummary;
        private static float _nodeSummaryAt;

        /// <summary>
        /// "12/42 active (ship 30, FuelStation 12)" style summary for HUD/console.
        /// </summary>
        public static string DescribeNodes()
        {
            EnsureLoaded();
            // A readout, not a decision input, and stale by at most NodeSummaryTtl.
            // docs/invariants.md#read-only-panels-build-on-repaint
            if (_nodeSummary != null && Time.time < _nodeSummaryAt) return _nodeSummary;

            _nodeSummaryAt = Time.time + NodeSummaryTtl;
            Dictionary<string, int> byOwner = [];
            // One snapshot, not a lookup per node.
            OwnerSnapshot owners = new();
            int active = 0;
            foreach (Node n in Nodes)
            {
                byOwner.TryGetValue(n.Owner, out int count);
                byOwner[n.Owner] = count + 1;
                if (owners.IsLive(n)) active++;
            }
            List<string> parts = [];
            foreach (KeyValuePair<string, int> kv in byOwner) parts.Add(kv.Key + " " + kv.Value);

            _nodeSummary = active + "/" + Nodes.Count + " active (" + string.Join(", ", parts) + ")";
            return _nodeSummary;
        }

        // ----------------------------------------------------------------
        // Manual link control (Force / Block)
        // ----------------------------------------------------------------

        /// <summary>
        /// Toggles a manual link between two nodes (by list index). Returns the
        /// resulting mode, or null when an existing link with the same mode was
        /// removed. Callers must validate indices first.
        /// </summary>
        public static LinkMode? ToggleLink(int indexA, int indexB, LinkMode mode)
        {
            EnsureLoaded();
            if (!ValidIndex(indexA) || !ValidIndex(indexB) || indexA == indexB) return null;

            ForkOwnerOf(indexA);
            ForkOwnerOf(indexB);
            int idA = Nodes[indexA].Id;
            int idB = Nodes[indexB].Id;
            // A link drawn in another ship layout is a different link, and stays.
            Vector3 offset = new OwnerSnapshot().OffsetOf(Nodes[indexA], Nodes[indexB]);

            for (int i = 0; i < ManualLinks.Count; i++)
            {
                ManualLink l = ManualLinks[i];
                if (SamePair(l, idA, idB) && SameOffset(l, idA, offset))
                {
                    if (l.Mode == mode)
                    {
                        ManualLinks.RemoveAt(i);
                        _edgesDirty = true;
                        _dirty = true;
                        return null;
                    }
                    l.Mode = mode;
                    _edgesDirty = true;
                    _dirty = true;
                    return l.Mode;
                }
            }

            ManualLinks.Add(new ManualLink(idA, idB, mode, offset));
            _edgesDirty = true;
            _dirty = true;
            return mode;
        }

        /// <summary>
        /// True when a link between the two nodes (by list index), drawn now, would hold only at ship
        /// stages that place their rooms as they are now.
        /// </summary>
        public static bool LinkIsStageBound(int indexA, int indexB)
        {
            EnsureLoaded();
            return ValidIndex(indexA) && ValidIndex(indexB) &&
                   new OwnerSnapshot().OffsetOf(Nodes[indexA], Nodes[indexB]).sqrMagnitude >=
                   LinkShiftTolerance * LinkShiftTolerance;
        }

        /// <summary>
        /// Removes the manual links between the two nodes (by list index), in every ship layout.
        /// Returns true when one existed.
        /// </summary>
        public static bool ClearLink(int indexA, int indexB)
        {
            EnsureLoaded();
            if (!ValidIndex(indexA) || !ValidIndex(indexB)) return false;

            ForkOwnerOf(indexA);
            ForkOwnerOf(indexB);
            int idA = Nodes[indexA].Id;
            int idB = Nodes[indexB].Id;
            if (ManualLinks.RemoveAll(l => SamePair(l, idA, idB)) == 0) return false;

            _edgesDirty = true;
            _dirty = true;
            return true;
        }

        /// <summary>
        /// Removes every manual link touching the node (by list index); auto edges are left
        /// to its AutoLink flag. Returns how many links were removed.
        /// </summary>
        public static int ClearLinks(int index)
        {
            EnsureLoaded();
            if (!ValidIndex(index)) return 0;

            int id = Nodes[index].Id;
            int removed = ManualLinks.RemoveAll(l => l.A == id || l.B == id);
            if (removed == 0) return 0;
            // This node's owner is enough: a bundled link loads only with both ends in the bundle.
            ForkOwnerOf(index);
            _edgesDirty = true;
            _dirty = true;
            return removed;
        }

        /// <summary>
        /// Order-independent key for a node pair: a manual link counts in either direction.
        /// </summary>
        private static (int, int) PairKey(int idA, int idB) => idA < idB ? (idA, idB) : (idB, idA);

        private static bool SamePair(ManualLink l, int idA, int idB) =>
            (l.A == idA && l.B == idB) || (l.A == idB && l.B == idA);

        /// <summary>
        /// Whether `offset`, measured from node `fromId` to the other end, is the layout `l` was drawn in.
        /// </summary>
        private static bool SameOffset(ManualLink l, int fromId, Vector3 offset)
        {
            Vector3 drawn = fromId == l.A ? l.Offset : -l.Offset;
            return (offset - drawn).sqrMagnitude < LinkShiftTolerance * LinkShiftTolerance;
        }

        // ----------------------------------------------------------------
        // Owners
        // ----------------------------------------------------------------

        /// <summary>
        /// Which object owns a world position, and its SaveObject ID for lookup after
        /// a load. The player's current room is the primary signal - collider ancestry
        /// is not reliable here, and the downward raycast is only a fallback.
        /// </summary>
        private static void DetectOwner(Vector3 worldPos, out string owner, out Transform? ownerT)
        {
            owner = WorldOwner;
            ownerT = null;

            GameManager gm = GameManager.Instance;
            if (gm == null) return;

            Transform? playerShip = gm.PlayerShip != null ? gm.PlayerShip.transform : null;

            // 1) The game's own room tracking (authoritative).
            Room? currentRoom = gm.PlayerShip != null && gm.PlayerShip.Pilot != null ? gm.PlayerShip.Pilot.CurrentRoom : null;
            if (currentRoom != null)
            {
                // 1a) Ship rooms are listed on the SpaceShip itself.
                // playerShip is only set when gm.PlayerShip is.
                if (playerShip != null && gm.PlayerShip!.Rooms != null)
                {
                    foreach (CustomRoom shipRoom in gm.PlayerShip.Rooms)
                    {
                        if (shipRoom == currentRoom)
                        {
                            owner = ShipOwner;
                            ownerT = playerShip;
                            return;
                        }
                    }
                }

                // 1b) Station rooms are listed on their SpaceStation.
                RefreshSpaceObjects();
                foreach (SpaceObject so in SpaceObjects)
                {
                    if (so == null || so is not SpaceStation station) continue;

                    bool roomIsStations = false;
                    if (GameInternals.SpaceStationAccess.GetRooms(station) is { } stationRooms)
                    {
                        foreach (Room stationRoom in stationRooms)
                        {
                            if (stationRoom == currentRoom)
                            {
                                roomIsStations = true;
                                break;
                            }
                        }
                    }
                    if (roomIsStations)
                    {
                        owner = so.gameObject.name;
                        ownerT = so.transform;
                        if (so.ID != 0) OwnerIds[owner] = so.ID;

                        return;
                    }
                }

                // 1c) Any other structure: walk up from the room's transform.
                // owner is null only on a miss; the fallbacks below reassign it.
                if (TryOwnerFromTransform(currentRoom.transform, playerShip, out owner!, out ownerT))
                {
                    if (owner == WorldOwner) ownerT = gm.WorldObjects;

                    return;
                }
            }

            // 2) Fallback: whatever is directly under the position.
            if (Physics.Raycast(worldPos + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 4f,
                    NavProbe.ProbeLayers, QueryTriggerInteraction.Ignore) &&
                TryOwnerFromTransform(hit.collider.transform, playerShip, out owner!, out ownerT))
            {
                if (owner == WorldOwner) ownerT = gm.WorldObjects;

                return;
            }

            // 3) Unowned geometry - keep the node glued to the world container so at
            //    least it does not drift relative to the stations while flying.
            owner = WorldOwner;
            ownerT = gm.WorldObjects;
        }

        /// <summary>
        /// The owner name and node frame of whatever vessel `start` belongs to.
        /// </summary>
        private static bool TryOwnerFromTransform(Transform start, Transform? playerShip, [NotNullWhen(true)] out string? owner, [NotNullWhen(true)] out Transform? ownerT)
        {
            owner = null;
            ownerT = null;
            if (!TryVesselOf(start, out SpaceShip? shipOwner, out SpaceObject? spaceOwner)) return false;

            if (shipOwner != null)
            {
                if (playerShip != null && shipOwner.transform == playerShip)
                {
                    owner = ShipOwner;
                    ownerT = playerShip;
                }
                else
                {
                    owner = shipOwner.gameObject.name;
                    ownerT = shipOwner.transform;
                    if (shipOwner.ID != 0) OwnerIds[owner] = shipOwner.ID;
                }
                return true;
            }
            // TryVesselOf returned true without a ship, so it found a SpaceObject.
            owner = spaceOwner!.gameObject.name;
            ownerT = spaceOwner.transform;
            if (spaceOwner.ID != 0) OwnerIds[owner] = spaceOwner.ID;

            return true;
        }

        /// <summary>
        /// The vessel a scene object belongs to: its nearest SpaceShip or SpaceObject ancestor,
        /// else the SpaceObject whose content container holds it - no station interior is
        /// under its station. docs/invariants.md#a-station-owns-its-interior-by-reference
        /// </summary>
        internal static bool TryVesselOf(Transform? start, out SpaceShip? ship, out SpaceObject? spaceObject)
        {
            ship = null;
            spaceObject = null;
            for (Transform? t = start; t != null; t = t.parent)
            {
                ship = t.GetComponent<SpaceShip>();
                if (ship != null) return true;

                spaceObject = t.GetComponent<SpaceObject>();
                if (spaceObject != null) return true;
            }
            if (start == null) return false;

            RefreshSpaceObjects();
            foreach (SpaceObject so in SpaceObjects)
            {
                if (so == null) continue;

                Transform? content = GameInternals.SpaceObjectAccess.GetContentParent(so);
                if (content == null || !start.IsChildOf(content)) continue;

                spaceObject = so;
                return true;
            }
            return false;
        }

        private static Transform? OwnerTransform(string owner)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return null;

            if (owner == ShipOwner) return gm.PlayerShip != null ? gm.PlayerShip.transform : null;

            if (owner == WorldOwner) return gm.WorldObjects;

            SpaceObject? so = FindOwnerObject(owner);
            return so != null ? so.transform : null;
        }

        private static void RefreshSpaceObjects()
        {
            // A destroyed entry means a new scene, which the TTL alone would hide for 5 s.
            if (_spaceObjectsValid && Time.time < _spaceObjectsRefreshAt && !HasDestroyedSpaceObject()) return;

            SpaceObjects.Clear();
            SpaceObjectNames.Clear();
            SpaceObjects.AddRange(UnityEngine.Object.FindObjectsOfType<SpaceObject>());
            // Read once, so the owner scan never touches `.name`:
            // docs/invariants.md#unity-name-reads-allocate
            foreach (SpaceObject so in SpaceObjects) SpaceObjectNames.Add(so == null ? null : so.gameObject.name);
            _spaceObjectsRefreshAt = Time.time + 5f;
            _spaceObjectsValid = true;
        }

        private static bool HasDestroyedSpaceObject()
        {
            foreach (SpaceObject so in SpaceObjects)
            {
                if (so == null) return true;
            }
            return false;
        }

        private static SpaceObject? FindOwnerObject(string owner)
        {
            RefreshSpaceObjects();
            OwnerIds.TryGetValue(owner, out uint wantId);
            SpaceObject? nameMatch = null;
            for (int i = 0; i < SpaceObjects.Count; i++)
            {
                SpaceObject so = SpaceObjects[i];
                if (so == null || SpaceObjectNames[i] != owner) continue;

                if (wantId != 0 && so.ID == wantId) return so;

                if (nameMatch == null) nameMatch = so;
            }
            return nameMatch;
        }

        private static bool OwnerIsActive(string owner)
        {
            if (owner == ShipOwner || owner == WorldOwner) return true;

            GameManager gm = GameManager.Instance;
            if (gm == null || gm.PlayerShip == null) return false;

            if (FindOwnerObject(owner) is SpaceStation station)
            {
                Docker docker = station.Docker;
                return docker != null && docker.DockedShip == gm.PlayerShip;
            }
            return false;
        }

        /// <summary>
        /// Each owner's transform and docked state, resolved once per graph query instead of once
        /// per node. Lives for one call only: flight moves owners and docking changes the active
        /// set. docs/invariants.md#never-cache-node-world-positions
        /// </summary>
        private sealed class OwnerSnapshot
        {
            private readonly Dictionary<string, Transform?> transforms = [];
            private readonly Dictionary<string, bool> active = [];
            private readonly Dictionary<string, CustomRoom?> rooms = [];

            private Transform? TransformOf(string owner)
            {
                if (!transforms.TryGetValue(owner, out Transform? ownerT))
                {
                    ownerT = OwnerTransform(owner);
                    transforms[owner] = ownerT;
                }
                return ownerT;
            }

            private bool IsActive(string owner)
            {
                if (!this.active.TryGetValue(owner, out bool ownerIsActive))
                {
                    ownerIsActive = OwnerIsActive(owner);
                    this.active[owner] = ownerIsActive;
                }
                return ownerIsActive;
            }

            private CustomRoom? RoomOf(Node n)
            {
                if (n.Owner != ShipOwner || n.Anchor == null || n.Anchor == HullAnchor) return null;

                if (!rooms.TryGetValue(n.Anchor, out CustomRoom? room))
                {
                    room = ShipRoom(n.Anchor);
                    rooms[n.Anchor] = room;
                }
                return room;
            }

            /// <summary>
            /// Walkable now: the owner is reachable and, on the ship, the node has a floor it rides
            /// and that room is built. docs/invariants.md#a-ship-node-rides-its-room
            /// </summary>
            public bool IsLive(Node n)
            {
                if (!IsActive(n.Owner)) return false;

                if (n.Owner != ShipOwner || n.Anchor == HullAnchor) return true;

                if (n.Anchor == null) return false;

                CustomRoom? room = RoomOf(n);
                return room == null || room.EnabledStructure;
            }

            /// <summary>
            /// How far, ship-local, the node's room has moved since the node was placed.
            /// </summary>
            private Vector3 ShiftOf(Node n)
            {
                CustomRoom? room = RoomOf(n);
                Transform? shipT = room != null ? TransformOf(ShipOwner) : null;
                // shipT is only set when room is.
                return shipT != null ? shipT.InverseTransformPoint(room!.transform.position) - n.AnchorAt : Vector3.zero;
            }

            public Vector3 WorldOf(Node n) => ToWorld(n.LocalPos + ShiftOf(n), TransformOf(n.Owner));

            /// <summary>
            /// A manual link holds only in the ship layout it was drawn in.
            /// </summary>
            public bool LinkIsLive(ManualLink l, Node a, Node b) => SameOffset(l, a.Id, OffsetOf(a, b));

            /// <summary>
            /// How far `a`'s room has moved relative to `b`'s, in the layout the ship has now.
            /// </summary>
            public Vector3 OffsetOf(Node a, Node b) => ShiftOf(a) - ShiftOf(b);
        }

        // ----------------------------------------------------------------
        // Ship rooms - the player ship is rebuilt by its upgrade stage
        // ----------------------------------------------------------------

        /// <summary>
        /// Anchor of a ship node on a floor that belongs to no room: the airlock, the docking collar.
        /// </summary>
        public const string HullAnchor = "hull";
        // Room moves are whole cells (3.75 m); anything under this is float noise.
        private const float LinkShiftTolerance = 0.1f;
        private const float AnchorRetryInterval = 2f;
        private static float _anchorRetryAt;
        private static int _lastShipLayoutSig;
        private static bool _warnedUnknownRoom;

        private static readonly Dictionary<string, CustomRoom> RoomsByName = new(16);
        private static CustomRoom[]? _roomsByNameOf;

        private static CustomRoom? ShipRoom(string name)
        {
            CustomRoom[]? shipRooms = GameManager.Instance != null && GameManager.Instance.PlayerShip != null
                ? GameManager.Instance.PlayerShip.Rooms
                : null;
            if (shipRooms == null) return null;

            // A stage only moves and toggles this fixed array, so the map is keyed on the
            // array itself: docs/invariants.md#unity-name-reads-allocate
            if (!ReferenceEquals(shipRooms, _roomsByNameOf))
            {
                RoomsByName.Clear();
                foreach (CustomRoom room in shipRooms)
                {
                    if (room == null) continue;

                    string roomName = room.gameObject.name;
                    RoomsByName.TryAdd(roomName, room);
                }
                _roomsByNameOf = shipRooms;
            }

            // A destroyed room reads as null through Unity's operator and is no room at all.
            if (RoomsByName.TryGetValue(name, out CustomRoom? found) && found != null) return found;

            if (!_warnedUnknownRoom)
            {
                _warnedUnknownRoom = true;
                YourBuddyPlugin.Log.LogWarning(
                    "[nav] The player ship has no room '" + name + "' - its nodes stay where they were placed " +
                    "and no longer follow ship upgrades.");
            }
            return null;
        }

        /// <summary>
        /// Anchors a ship node to the room whose floor is under `worldPos`, or to the hull when the
        /// floor is part of no room. False when there is no floor there at all.
        /// </summary>
        private static bool TryAnchorShipNode(Node n, Vector3 worldPos)
        {
            SpaceShip? ship = GameManager.Instance != null ? GameManager.Instance.PlayerShip : null;
            if (ship == null || !NavProbe.TryFloorCollider(worldPos, out Collider? floor) || floor == null) return false;

            CustomRoom? room = floor.GetComponentInParent<CustomRoom>();
            if (room != null && Array.IndexOf(ship.Rooms, room) >= 0)
            {
                n.Anchor = room.gameObject.name;
                n.AnchorAt = ship.transform.InverseTransformPoint(room.transform.position);
            }
            else
            {
                n.Anchor = HullAnchor;
                n.AnchorAt = Vector3.zero;
            }
            return true;
        }

        /// <summary>
        /// Ship nodes from a graph written before anchoring are anchored by the floor under them in
        /// the layout the ship has now. One with no floor waits, dormant, for one to be built.
        /// </summary>
        private static void AnchorPendingShipNodes()
        {
            if (Time.time < _anchorRetryAt) return;

            _anchorRetryAt = Time.time + AnchorRetryInterval;
            Transform? shipT = OwnerTransform(ShipOwner);
            if (shipT == null) return;

            int anchored = 0;
            List<int>? waiting = null;
            foreach (Node n in Nodes)
            {
                if (n.Owner != ShipOwner || n.Anchor != null) continue;

                if (TryAnchorShipNode(n, shipT.TransformPoint(n.LocalPos)))
                {
                    anchored++;
                    if (!n.Bundled) _dirty = true;
                }
                else
                {
                    (waiting ??= []).Add(n.Id);
                }
            }
            if (anchored == 0) return;

            _edgesDirty = true;
            YourBuddyPlugin.Log.LogInfo(
                "[nav] Anchored " + anchored + " ship node(s) to the rooms under them at ship stage " +
                ShipStage() + (waiting == null ? "" : "; no floor yet under #" + string.Join(", #", waiting)));
        }

        private static string ShipStage()
        {
            SpaceShip? ship = GameManager.Instance != null ? GameManager.Instance.PlayerShip : null;
            return ship != null && ship.StorymodeShipBuilder != null
                ? ship.StorymodeShipBuilder.CurrentLevel.ToString()
                : "?";
        }

        /// <summary>
        /// Which rooms are built and where they stand: an upgrade changes both, and with them
        /// which ship nodes are live and where they are.
        /// </summary>
        internal static int ShipLayoutSignature()
        {
            CustomRoom[]? shipRooms = GameManager.Instance != null && GameManager.Instance.PlayerShip != null
                ? GameManager.Instance.PlayerShip.Rooms
                : null;
            if (shipRooms == null) return 0;

            int sig = 17;
            foreach (CustomRoom room in shipRooms)
            {
                if (room == null) continue;

                Vector3 p = room.transform.localPosition;
                sig = sig * 31 + (room.EnabledStructure ? 1 : 2);
                sig = sig * 31 + Mathf.RoundToInt(p.x * 20f);
                sig = sig * 31 + Mathf.RoundToInt(p.z * 20f);
            }
            return sig;
        }

        /// <summary>
        /// Signature of "which stations are we docked to" - when it changes, the edge
        /// cache must be rebuilt because the active node set changed.
        /// </summary>
        private static string DockSignature()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.PlayerShip == null) return "nogame";

            RefreshSpaceObjects();
            List<string>? docked = null;
            foreach (SpaceObject so in SpaceObjects)
            {
                if (so == null) continue;

                if (so is SpaceStation station && station.Docker != null &&
                    station.Docker.DockedShip == gm.PlayerShip)
                {
                    (docked ??= []).Add(station.gameObject.name);
                }
            }
            return docked == null ? "none" : string.Join("|", docked);
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static Vector3 FloorUnder(Vector3 pos, Transform? owner)
        {
            Vector3 up = owner != null ? owner.up : Vector3.up;
            if (Physics.Raycast(pos + up * 0.5f, -up, out RaycastHit hit, 4f,
                    NavProbe.ProbeLayers, QueryTriggerInteraction.Ignore))
            {
                // Ensure we don't pick a point on the ceiling if we are following.
                if (hit.point.y > pos.y + 0.5f) return pos;

                return hit.point;
            }
            return pos;
        }

        private static Vector3 ToWorld(Vector3 local, Transform? ownerT)
        {
            return ownerT != null ? ownerT.TransformPoint(local) : local;
        }

        // ----------------------------------------------------------------
        // Node floor heights - which deck a node belongs to
        // ----------------------------------------------------------------

        /// <summary>
        /// The deck a node stands on, which is not its own Y.
        /// docs/invariants.md#floor-to-floor, docs/invariants.md#node-hover-not-height
        /// </summary>
        private static float NodeFloorY(Node? n, Vector3 worldPos)
        {
            if (n == null) return NavProbe.FloorHeight(worldPos);

            if (!NodeHover.TryGetValue(n.Id, out float hover))
            {
                if (NavProbe.TryFloorHeight(worldPos, out float probed))
                {
                    hover = worldPos.y - probed;
                    LastKnownHover[n.Id] = hover;
                }
                else if (!LastKnownHover.TryGetValue(n.Id, out hover))
                {
                    hover = TypicalHover();
                }

                NodeHover[n.Id] = hover;
            }
            return worldPos.y - hover;
        }

        /// <summary>
        /// How far the user's markers usually float above their deck, from the ones we
        /// have measured - the honest estimate when a probe finds no floor.
        /// docs/invariants.md#unprobeable-floors-keep-the-last-hover
        /// </summary>
        private static float TypicalHover()
        {
            if (LastKnownHover.Count == 0) return 0f;

            if (_typicalHoverAt == LastKnownHover.Count) return _typicalHover;

            List<float> sorted = [.. LastKnownHover.Values];
            sorted.Sort();
            _typicalHover = sorted[sorted.Count / 2];
            _typicalHoverAt = LastKnownHover.Count;
            return _typicalHover;
        }

        /// <summary>
        /// Floor under a route waypoint: the cached deck height when the waypoint is
        /// a graph node, a fresh probe when it is the start or the goal.
        /// </summary>
        private static float FloorYOf(Node? node, Vector3 worldPos)
        {
            return node != null ? NodeFloorY(node, worldPos) : NavProbe.FloorHeight(worldPos);
        }

        private static void RefreshNodeFloors()
        {
            if (Time.time < _nodeHoverRefreshAt) return;

            _nodeHoverRefreshAt = Time.time + NodeHoverTtl;
            NodeHover.Clear();
        }

        // ----------------------------------------------------------------
        // Edge cache
        // ----------------------------------------------------------------

        private static void EnsureEdgesFresh()
        {
            EnsureLoaded();
            RefreshNodeFloors();
            AnchorPendingShipNodes();
            string sig = DockSignature();
            int layoutSig = ShipLayoutSignature();
            float maxDist = YourBuddyPlugin.ConfigMaxEdgeDist.Value;
            if (_edgesDirty || sig != _lastDockSig || layoutSig != _lastShipLayoutSig ||
                !Mathf.Approximately(maxDist, _lastMaxEdgeDist))
            {
                // The dock context or the ship's rooms changed, so the scene's gate set may
                // have too - the edge rebuild below probes geometry and must not consult a
                // stale list.
                NavProbe.InvalidateGates();
                bool layoutChanged = _lastShipLayoutSig != 0 && layoutSig != _lastShipLayoutSig;
                RebuildEdges(sig, maxDist);
                _lastShipLayoutSig = layoutSig;
                if (layoutChanged) LogShipLayoutChange();
            }
        }

        private static void LogShipLayoutChange()
        {
            OwnerSnapshot owners = new();
            int live = 0;
            int dormant = 0;
            foreach (Node n in Nodes)
            {
                if (n.Owner != ShipOwner) continue;

                if (owners.IsLive(n)) live++;
                else dormant++;
            }
            YourBuddyPlugin.Log.LogInfo(
                "[nav] Ship rebuilt (stage " + ShipStage() + "): " + live + " ship node(s) live, " +
                dormant + " dormant in rooms not built; edges rebuilt");
        }

        private static void RebuildEdges(string dockSig, float maxDist)
        {
            // Geometry is about to be re-probed; the deck each node stands on may have
            // changed with it (a station appearing, a ship module unfolding).
            NodeHover.Clear();
            _edges = [];
            _priorityExits = [];

            NodesById.Clear();
            foreach (Node n in Nodes) NodesById.TryAdd(n.Id, n);

            OwnerSnapshot owners = new();
            List<int> activeIndices = [];
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (owners.IsLive(Nodes[i]))
                {
                    activeIndices.Add(i);
                    _edges[Nodes[i].Id] = [];
                }
            }

            // Once per node rather than once per pair; still fresh for this rebuild.
            Vector3[] activeWorld = new Vector3[activeIndices.Count];
            for (int k = 0; k < activeIndices.Count; k++) activeWorld[k] = owners.WorldOf(Nodes[activeIndices[k]]);

            // A lookup instead of scanning every manual link per pair; a pair described twice
            // keeps the first link, as the scan this replaced did. A link whose ends a ship
            // upgrade moved apart is left out: docs/invariants.md#a-ship-node-rides-its-room
            Dictionary<(int, int), LinkMode> manualModes = new(ManualLinks.Count);
            foreach (ManualLink l in ManualLinks)
            {
                Node la = NodesById.GetValueOrDefault(l.A);
                Node lb = NodesById.GetValueOrDefault(l.B);
                if (la == null || lb == null || !owners.LinkIsLive(l, la, lb)) continue;

                manualModes.TryAdd(PairKey(l.A, l.B), l.Mode);
            }

            for (int a = 0; a < activeIndices.Count; a++)
            {
                for (int b = a + 1; b < activeIndices.Count; b++)
                {
                    Node na = Nodes[activeIndices[a]];
                    Node nb = Nodes[activeIndices[b]];
                    Vector3 wa = activeWorld[a];
                    Vector3 wb = activeWorld[b];
                    float dist = Vector3.Distance(wa, wb);
                    if (dist > maxDist) continue;

                    LinkMode? manual = manualModes.TryGetValue(PairKey(na.Id, nb.Id), out LinkMode linkMode)
                        ? linkMode
                        : null;
                    if (manual == LinkMode.Block) continue;

                    bool forced = manual == LinkMode.Force || manual == LinkMode.Priority;
                    // AutoLink=false nodes accept manual links only.
                    if (!forced)
                    {
                        if (!na.AutoLink || !nb.AutoLink) continue;

                        if (!AutoEdgeAllowed(na, nb, NodeFloorY(na, wa), NodeFloorY(nb, wb))) continue;
                        // Thin, not a body sweep: the strict sweep rejected exactly the
                        // connections the user drew. docs/navigation.md
                        if (!NavProbe.ThinLos(wa, wb, MaxStairDeltaY)) continue;
                    }

                    EdgeKind kind = manual == LinkMode.Priority ? EdgeKind.Priority
                        : forced ? EdgeKind.Forced
                        : EdgeKind.Auto;
                    _edges[na.Id].Add(new Edge(nb.Id, dist, kind));
                    _edges[nb.Id].Add(new Edge(na.Id, dist, kind));
                }
            }

            // Directional on purpose: the reverse trip stays free, or stair routes
            // could not be walked both ways.
            foreach (ManualLink l in ManualLinks)
            {
                if (l.Mode != LinkMode.Priority) continue;

                Node? na = NodeById(l.A);
                Node? nb = NodeById(l.B);
                if (na == null || nb == null) continue;

                if (!owners.IsLive(na) || !owners.IsLive(nb) || !owners.LinkIsLive(l, na, nb)) continue;

                if (!_priorityExits.TryGetValue(l.A, out HashSet<int> exits))
                {
                    exits = [];
                    _priorityExits[l.A] = exits;
                }
                exits.Add(l.B);
            }

            _edgesDirty = false;
            _lastDockSig = dockSig;
            _lastMaxEdgeDist = maxDist;
        }

        /// <summary>
        /// Vertical traversal rule: near-flat edges are automatic; a climb is only
        /// automatic toward a Stair node; anything steeper needs a Force link.
        /// Measured deck-to-deck, not marker-to-marker (see NodeFloorY).
        /// </summary>
        private static bool AutoEdgeAllowed(Node a, Node b, float floorA, float floorB)
        {
            float deltaY = Mathf.Abs(floorB - floorA);
            if (deltaY <= MaxDirectDeltaY) return true;

            if (deltaY <= MaxStairDeltaY) return a.Type == NodeType.Stair || b.Type == NodeType.Stair;

            return false;
        }

        /// <summary>
        /// World-space lines for editor visualization: cached auto/forced edges plus
        /// manual Block links (which are not part of the traversable graph).
        /// </summary>
        public static void GetEdgeVisuals(List<(Vector3 a, Vector3 b, EdgeKind kind)> lines)
        {
            EnsureLoaded();
            EnsureEdgesFresh();
            if (_edges == null) return;

            OwnerSnapshot owners = new();
            Dictionary<int, int> idToIndex = [];
            for (int i = 0; i < Nodes.Count; i++) idToIndex[Nodes[i].Id] = i;

            foreach (KeyValuePair<int, List<Edge>> kv in _edges)
            {
                if (!idToIndex.TryGetValue(kv.Key, out int ia)) continue;

                foreach (Edge e in kv.Value)
                {
                    if (!idToIndex.TryGetValue(e.To, out int ib) || ib <= ia)
                    {
                        continue; // each edge once
                    }

                    lines.Add((owners.WorldOf(Nodes[ia]), owners.WorldOf(Nodes[ib]), e.Kind));
                }
            }

            foreach (ManualLink l in ManualLinks)
            {
                if (l.Mode != LinkMode.Block) continue;

                if (idToIndex.TryGetValue(l.A, out int ia2) && idToIndex.TryGetValue(l.B, out int ib2))
                {
                    lines.Add((owners.WorldOf(Nodes[ia2]), owners.WorldOf(Nodes[ib2]), EdgeKind.Blocked));
                }
            }
        }

        // ----------------------------------------------------------------
        // A* Pathfinding over the cached edges
        // ----------------------------------------------------------------

        // How far from the buddy a node may sit and still be tried as a route entry.
        private const float MaxSeedDist = 60f;

        // How long the final straight walk to the goal may be - roughly one room wide.
        // Never the same number as MaxSeedDist:
        // docs/invariants.md#seed-radius-is-not-finish-length
        private const float MaxGoalFinishDist = 6f;

        // Anti-backtrack: a node the buddy just departed loses near-ties as the entry.
        private const float BacktrackRadius = 1.5f;
        // How near a position must be to count as that node, for avoidEntry - not
        // BacktrackRadius: docs/invariants.md#avoid-entry-bars-the-whole-search
        private const float AvoidEntryRadius = 0.5f;
        private const float BacktrackPenalty = 3f;
        // Same deck when the floors under two points are this close. The rule most often
        // deleted by accident: docs/invariants.md#entry-seeds-on-own-deck
        // Ceiling on the value: docs/invariants.md#same-level-tolerance-ceiling
        private const float SameLevelDeltaY = 0.5f;
        // The one exception: a Stair node may be entered off-level, but only from
        // right at its foot - not "I can see a tread from across the room".
        private const float StairSeedRadius = 2.5f;
        // ...and only over ground that does not break by more than this on the way.
        // docs/invariants.md#an-entry-must-be-walkable-not-merely-visible
        private const float StairEntryGroundStep = 0.8f;

        // Arm's-length entry with no line of sight: the chord to a node the buddy is
        // practically standing on is often broken by its own body. Measured flat,
        // because the marker can be a metre overhead and still be at its feet.
        private const float SeedCloseDist = 1.25f;
        private const float SeedCloseDeltaY = 0.5f;

        // Height is charged this many times its length when scoring how near a node is
        // to the goal. A node one deck below the player is not "close" just because it
        // sits under their feet - getting there still needs a staircase.
        private const float VerticalCostFactor = 4f;
        // Tie-break weight for route length when no node has a clear finish: pick the
        // node nearest the goal, preferring cheap routes among equally near ones.
        private const float ApproachRouteWeight = 0.1f;
        // How much nearer the goal such a node must get the buddy before the detour to
        // it is worth planning at all.
        private const float ApproachMinGain = 1.5f;

        /// <summary>
        /// "How near is this node to the goal", for the finish and the approach.
        /// Elevation-aware (VerticalCostFactor) and deck-to-deck, because raw Ys here
        /// come from three different bases. docs/invariants.md#floor-to-floor
        /// </summary>
        private static float GoalCost(Vector3 from, float fromFloorY, Vector3 to, float toFloorY)
        {
            float flat = new Vector2(to.x - from.x, to.z - from.z).magnitude;
            return flat + Mathf.Abs(toFloorY - fromFloorY) * VerticalCostFactor;
        }

        /// <summary>
        /// The rule for whether the buddy may walk straight at a route entry.
        /// Seeding and path commitment must both come through here:
        /// docs/invariants.md#one-entry-predicate
        /// </summary>
        public static bool CanReachEntry(Vector3 start, Vector3 waypoint)
        {
            EnsureLoaded();
            EnsureEdgesFresh();
            Node? node = NodeAt(waypoint);
            // start is a floor point - re-probing it is how the caller's answer got
            // thrown away. docs/invariants.md#the-start-point-is-already-a-floor
            return CanReachEntry(start, start.y, waypoint, FloorYOf(node, waypoint), node);
        }

        private static bool CanReachEntry(Vector3 start, float startFloorY, Vector3 waypoint,
            float waypointFloorY, Node? node)
        {
            if (IsWithinArmsReach(start, startFloorY, waypoint, waypointFloorY)) return true;

            float allowedDeltaY = EntryDeltaYAllowance(start, startFloorY, waypoint, waypointFloorY, node);
            if (allowedDeltaY <= 0f || !NavProbe.ThinLos(start, waypoint, allowedDeltaY)) return false;

            // An off-level (stair) entry needs more than a sight line:
            // docs/invariants.md#an-entry-must-be-walkable-not-merely-visible
            if (Mathf.Abs(waypointFloorY - startFloorY) <= SameLevelDeltaY) return true;
            // Two ways a stairwell defeats an eye-height sight line, and one test each:
            // an open well the line dips through, and a wall or railing it looks over.
            return NavProbe.GroundIsContinuous(start, waypoint, StairEntryGroundStep) &&
                   NavProbe.WalkLos(start, waypoint, allowedDeltaY);
        }

        /// <summary>
        /// How much height change this entry may span, or 0 when the node is not on
        /// the buddy's deck and may not be entered directly.
        /// </summary>
        private static float EntryDeltaYAllowance(Vector3 start, float startFloorY, Vector3 waypoint,
            float waypointFloorY, Node? node)
        {
            float deltaY = Mathf.Abs(waypointFloorY - startFloorY);
            if (deltaY <= SameLevelDeltaY) return SameLevelDeltaY;

            // Standing at the foot (or head) of a flight of steps.
            if (node?.Type == NodeType.Stair && deltaY <= MaxStairDeltaY)
            {
                float flat = new Vector2(waypoint.x - start.x, waypoint.z - start.z).magnitude;
                if (flat <= StairSeedRadius) return MaxStairDeltaY;
            }
            return 0f;
        }

        /// <summary>
        /// The no-line-of-sight half of CanReachEntry, split out so the seeding loop can
        /// reuse the ThinLos result it already has instead of probing every candidate twice.
        /// </summary>
        private static bool IsWithinArmsReach(Vector3 start, float startFloorY, Vector3 waypoint,
            float waypointFloorY)
        {
            float flat = new Vector2(waypoint.x - start.x, waypoint.z - start.z).magnitude;
            return flat <= SeedCloseDist && Mathf.Abs(waypointFloorY - startFloorY) <= SeedCloseDeltaY;
        }

        /// <summary>
        /// Could the buddy step onto this waypoint's deck from where it is standing at
        /// all? Not "is the route clear" - just whether the two are within one flight of
        /// each other. A plan whose next waypoint fails this was made somewhere else.
        /// docs/invariants.md#a-stale-plan-is-worse-than-none
        /// </summary>
        public static bool WaypointIsOnAReachableDeck(Vector3 start, Vector3 waypoint)
        {
            EnsureLoaded();
            return Mathf.Abs(FloorYOf(NodeAt(waypoint), waypoint) - start.y) <= MaxStairDeltaY;
        }

        /// <summary>
        /// The active node standing at a world position (within half a metre), or null.
        /// Route waypoints are node positions, so this recovers a waypoint's node type.
        /// </summary>
        private static Node? NodeAt(Vector3 worldPos)
        {
            OwnerSnapshot owners = new();
            Node? best = null;
            float bestDistanceSquared = 0.25f;
            foreach (Node n in Nodes)
            {
                if (!owners.IsLive(n)) continue;

                float distanceSquared = (owners.WorldOf(n) - worldPos).sqrMagnitude;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    best = n;
                }
            }
            return best;
        }

        /// <summary>
        /// True when a world position is one of the active graph's nodes, i.e. a route
        /// waypoint the buddy can meaningfully remember having left (as opposed to the
        /// plan's own inserted start point).
        /// </summary>
        public static bool IsNodePosition(Vector3 worldPos)
        {
            EnsureLoaded();
            return NodeAt(worldPos) != null;
        }

        /// <summary>
        /// Plans a route. cameFromPos penalizes entry near the node just departed;
        /// avoidEntry bars a waypoint from the whole search. docs/navigation.md §4,
        /// docs/invariants.md#avoid-entry-bars-the-whole-search
        /// </summary>
        public static NavPath? FindPath(Vector3 start, Vector3 end, Vector3? cameFromPos = null,
            Vector3? avoidEntry = null)
        {
            NavPath? plan = FindPathCore(start, end, cameFromPos, avoidEntry);
            if (plan != null || !avoidEntry.HasValue) return plan;

            // No route around the barred waypoint: go through it rather than have no plan.
            // docs/invariants.md#a-barred-waypoint-is-a-preference-not-a-wall
            plan = FindPathCore(start, end, cameFromPos, null);
            if (plan != null && YourBuddyPlugin.ConfigDebugLevel.Value >= 2)
            {
                YourBuddyPlugin.Log.LogInfo(
                    $"[nav] FindPath: no route avoids {avoidEntry.Value:0.0} - going through it instead");
            }
            return plan;
        }

        private static NavPath? FindPathCore(Vector3 start, Vector3 end, Vector3? cameFromPos,
            Vector3? avoidEntry)
        {
            EnsureLoaded();
            LastPathBlockedByDoor = false;
            if (Nodes.Count == 0) return null;

            EnsureEdgesFresh();
            if (_edges == null || _edges.Count == 0) return null;

            bool debug = YourBuddyPlugin.ConfigDebugLevel.Value >= 2;

            // Fresh world positions each call
            // (docs/invariants.md#never-cache-node-world-positions), plus the deck each
            // node stands on - every height test below is made between floors.
            OwnerSnapshot owners = new();
            Dictionary<int, Vector3> world = new(_edges.Count);
            Dictionary<int, float> floors = new(_edges.Count);
            foreach (int id in _edges.Keys)
            {
                Node? n = NodeById(id);
                if (n == null) continue;

                Vector3 w = owners.WorldOf(n);
                world[id] = w;
                floors[id] = NodeFloorY(n, w);
            }

            // `start` is already a floor point; never re-probe it.
            // docs/invariants.md#the-start-point-is-already-a-floor
            float startFloorY = start.y;
            // `end` is a body point - the player, or a node - so it still needs probing.
            float endFloorY = NavProbe.FloorHeight(end);

            // A short, validated walk: take it. The finish leg's own predicate, anchored
            // at the buddy. docs/invariants.md#a-clear-short-goal-beats-the-graph
            if ((start - end).sqrMagnitude <= MaxGoalFinishDist * MaxGoalFinishDist &&
                Mathf.Abs(endFloorY - startFloorY) <= SameLevelDeltaY &&
                !DoorBlocks(start, end) &&
                HasLineOfSight(start, end, SameLevelDeltaY))
            {
                if (debug)
                {
                    YourBuddyPlugin.Log.LogInfo(
                    $"[nav] FindPath: goal is {Vector3.Distance(start, end):0.0}m and clear - walking straight there");
                }

                return new NavPath([end], [false], [endFloorY]);
            }

            // Seeding is all-candidate; avoidEntry is barred from the whole search.
            // docs/invariants.md#avoid-entry-bars-the-whole-search
            HashSet<int>? excluded = null;
            if (avoidEntry.HasValue)
            {
                foreach (int id in world.Keys)
                {
                    if ((world[id] - avoidEntry.Value).sqrMagnitude < AvoidEntryRadius * AvoidEntryRadius)
                    {
                        (excluded ??= []).Add(id);
                    }
                }
            }

            List<int> startIds = [];
            List<KeyValuePair<int, float>> candidates = [];
            foreach (int id in world.Keys)
            {
                if (excluded != null && excluded.Contains(id)) continue;

                float d = Vector3.Distance(start, world[id]);
                if (d <= MaxSeedDist) candidates.Add(new KeyValuePair<int, float>(id, d));
            }
            candidates.Sort((x, y) => x.Value.CompareTo(y.Value));
            string seedTrace = "";
            // Why the nearest candidates were turned down. Without it a seed list that
            // skips the node at the buddy's feet is undiagnosable.
            string rejectTrace = "";
            int rejectsShown = 0;
            foreach (KeyValuePair<int, float> candidate in candidates)
            {
                Vector3 candidateWorld = world[candidate.Key];
                float candidateFloorY = floors[candidate.Key];
                string? reject = null;
                if (DoorBlocks(start, candidateWorld))
                {
                    reject = "door";
                }
                else if (!CanReachEntry(start, startFloorY, candidateWorld, candidateFloorY,
                             NodeById(candidate.Key)))
                {
                    reject = Mathf.Abs(candidateFloorY - startFloorY) > SameLevelDeltaY ? "deck" : "los";
                }

                if (reject != null)
                {
                    if (debug && rejectsShown < 5)
                    {
                        rejectsShown++;
                        rejectTrace += $" #{candidate.Key}({candidate.Value:0.0}m," +
                                       $"{candidateFloorY - startFloorY:+0.0;-0.0;0.0}y,{reject})";
                    }
                    continue;
                }
                startIds.Add(candidate.Key);
                if (debug)
                {
                    seedTrace += $" #{candidate.Key}({candidate.Value:0.0}m, {candidateFloorY - startFloorY:+0.0;-0.0;0.0}y)";
                }
            }
            if (debug)
            {
                YourBuddyPlugin.Log.LogInfo(
                $"[nav] FindPath: {candidates.Count} candidates in range, {startIds.Count} visible entry seeds " +
                $"(start floor {startFloorY:0.00}):{seedTrace}" +
                (rejectTrace.Length > 0 ? $" | nearest rejected:{rejectTrace}" : ""));
            }

            if (startIds.Count == 0)
            {
                // Nothing reachable around: fall back to the nearest active node so
                // buddy_goto and long-distance Follow still work. Ranked by GoalCost so
                // the fallback cannot undo the level restriction seeding just applied.
                int nearest = -1;
                float nearestCost = float.MaxValue;
                for (int i = 0; i < Nodes.Count; i++)
                {
                    if (!owners.IsLive(Nodes[i]) || !world.ContainsKey(Nodes[i].Id)) continue;

                    if (excluded != null && excluded.Contains(Nodes[i].Id)) continue;

                    Vector3 nodeWorld = world[Nodes[i].Id];
                    if (DoorBlocks(start, nodeWorld)) continue;

                    float cost = GoalCost(start, startFloorY, nodeWorld, floors[Nodes[i].Id]);
                    // docs/invariants.md#fallback-respects-backtrack
                    if (cameFromPos.HasValue &&
                        (nodeWorld - cameFromPos.Value).sqrMagnitude < BacktrackRadius * BacktrackRadius)
                    {
                        cost += BacktrackPenalty;
                    }
                    if (cost < nearestCost)
                    {
                        nearestCost = cost;
                        nearest = i;
                    }
                }
                if (nearest >= 0)
                {
                    startIds.Add(Nodes[nearest].Id);
                    if (debug)
                    {
                        YourBuddyPlugin.Log.LogInfo(
                        "[nav] FindPath: no clear entry seed, falling back to nearest active node " +
                        "#" + Nodes[nearest].Id);
                    }
                }
            }
            if (startIds.Count == 0) return null;

            // A* over the cached adjacency lists.
            List<int> openSet = [];
            HashSet<int> closedSet = [];
            Dictionary<int, int> cameFrom = [];
            Dictionary<int, float> gScore = [];
            Dictionary<int, float> fScore = [];

            foreach (int sn in startIds)
            {
                // Elevation-aware: charging the climb makes the buddy enter at the
                // Foot of the stairs instead of scrabbling at the tread beside it.
                float g = GoalCost(start, startFloorY, world[sn], floors[sn]);
                if (cameFromPos.HasValue && (world[sn] - cameFromPos.Value).sqrMagnitude <
                    BacktrackRadius * BacktrackRadius)
                {
                    g += BacktrackPenalty;
                }
                openSet.Add(sn);
                gScore[sn] = g;
                fScore[sn] = g + Vector3.Distance(world[sn], end);
            }

            // The search does not stop at the first node that can see the goal - that
            // let a backward node with a lucky view steal the route from the forward one.
            int goalId = -1;
            float bestTotal = float.MaxValue;
            // Best-effort finish: the expanded node that gets nearest the goal without
            // a clear walk to it. The route ends there and the buddy replans on arrival.
            int approachId = -1;
            float bestApproach = float.MaxValue;
            // An approach node must get the buddy meaningfully nearer, or a buddy
            // closing the last metres on its own is planned back onto the node behind it.
            float startGoalCost = GoalCost(start, startFloorY, end, endFloorY) - ApproachMinGain;
            while (openSet.Count > 0)
            {
                int current = openSet[0];
                for (int i = 1; i < openSet.Count; i++)
                {
                    if (fScore[openSet[i]] < fScore[current]) current = openSet[i];
                }

                // Can we walk the last stretch directly from here? Probed and length
                // capped, and SameLevelDeltaY rather than MaxDirectDeltaY:
                // docs/invariants.md#finish-may-not-change-deck
                if ((world[current] - end).sqrMagnitude <= MaxGoalFinishDist * MaxGoalFinishDist)
                {
                    // Cost first: the walkable-line probe is the expensive part, and only a better finish needs it.
                    float total = gScore[current] + GoalCost(world[current], floors[current], end, endFloorY);
                    if (total < bestTotal && !DoorBlocks(world[current], end) &&
                        HasLineOfSight(world[current], end, SameLevelDeltaY))
                    {
                        bestTotal = total;
                        goalId = current;
                    }
                }

                float currentGoalCost = GoalCost(world[current], floors[current], end, endFloorY);
                float approach = currentGoalCost + gScore[current] * ApproachRouteWeight;
                if (approach < bestApproach && currentGoalCost < startGoalCost)
                {
                    bestApproach = approach;
                    approachId = current;
                }

                openSet.Remove(current);
                closedSet.Add(current);

                // Optimal stop: f is a lower bound for any route through an unexpanded
                // node, so once bestTotal is at or below every remaining f, nothing wins.
                if (bestTotal < float.MaxValue)
                {
                    float minF = float.MaxValue;
                    foreach (int t in openSet)
                    {
                        if (fScore[t] < minF) minF = fScore[t];
                    }
                    if (minF >= bestTotal) break;
                }

                if (!_edges.TryGetValue(current, out List<Edge> edges)) continue;

                // Priority links: arriving from anywhere other than the priority target
                // forces the exit through it. Arriving via it leaves all exits open.
                if (_priorityExits != null && _priorityExits.TryGetValue(current, out HashSet<int> exits))
                {
                    // No cameFrom entry means `current` is a seed, and a start is not an
                    // arrival: docs/invariants.md#no-priority-constraint-on-seeds
                    if (cameFrom.TryGetValue(current, out int predecessor) && !exits.Contains(predecessor))
                    {
                        List<Edge>? forcedExits = null;
                        foreach (Edge e in edges)
                        {
                            if (exits.Contains(e.To)) (forcedExits ??= []).Add(e);
                        }
                        if (forcedExits != null) edges = forcedExits;
                    }
                }

                foreach (Edge e in edges)
                {
                    if (closedSet.Contains(e.To) || !world.ContainsKey(e.To)) continue;

                    if (excluded != null && excluded.Contains(e.To)) continue;
                    // Force and Priority edges are filtered too: their LOS exemption is
                    // about sight lines, and a locked door is not one.
                    // docs/invariants.md#locked-doors-block-edges
                    if (DoorBlocks(world[current], world[e.To])) continue;

                    float tentative = gScore[current] + e.Cost;
                    if (gScore.TryGetValue(e.To, out float oldG) && tentative >= oldG) continue;

                    cameFrom[e.To] = current;
                    gScore[e.To] = tentative;
                    fScore[e.To] = tentative + Vector3.Distance(world[e.To], end);
                    if (!openSet.Contains(e.To)) openSet.Add(e.To);
                }
            }

            // Only a validated finish may append the goal as a waypoint; an approach
            // must not, or it recreates the beeline the finish cap exists to prevent.
            bool finishesAtGoal = goalId >= 0;
            if (!finishesAtGoal) goalId = approachId;

            if (goalId < 0)
            {
                if (debug)
                {
                    YourBuddyPlugin.Log.LogInfo(
                    "[nav] FindPath failed: no node reachable from the buddy");
                }

                return null; // no path found
            }

            // Reconstruct path as a node-id chain so we can look up edge kinds.
            List<int> nodeChain = [];
            int cur = goalId;
            nodeChain.Add(cur);
            while (cameFrom.TryGetValue(cur, out int prev))
            {
                cur = prev;
                nodeChain.Add(cur);
            }
            nodeChain.Reverse();

            if (debug)
            {
                // The whole chain, not just the destination: a one-hop chain means A*
                // never traversed an edge. docs/navigation.md
                string chain = "";
                foreach (int id in nodeChain)
                {
                    chain += (chain.Length > 0 ? " -> " : "") + "#" + id +
                             (NodeById(id)?.Type == NodeType.Stair ? "(stair)" : "");
                }
                YourBuddyPlugin.Log.LogInfo(
                    finishesAtGoal
                        ? $"[nav] FindPath: total {bestTotal:0.0}m via {chain} -> goal, " +
                          $"cameFrom={(cameFromPos.HasValue ? cameFromPos.Value.ToString("0.0") : "none")}" +
                          $"{(avoidEntry.HasValue ? $", avoiding {avoidEntry.Value:0.0}" : "")}"
                        : $"[nav] FindPath: no clear finish - approaching via {chain} " +
                          $"({GoalCost(world[goalId], floors[goalId], end, endFloorY):0.0}m of goal cost left), " +
                          $"cameFrom={(cameFromPos.HasValue ? cameFromPos.Value.ToString("0.0") : "none")}" +
                          $"{(avoidEntry.HasValue ? $", avoiding {avoidEntry.Value:0.0}" : "")}");
            }

            // Build waypoints and the parallel forced[] and deck arrays.
            // forced[i] = true when the edge arriving at waypoint i is Force/Priority.
            List<Vector3> waypoints = [];
            List<bool> forced = [];
            List<float> decks = [];

            // Insert the actual start if it is not at the first node.
            if ((start - world[nodeChain[0]]).sqrMagnitude > 0.25f)
            {
                waypoints.Add(start);
                forced.Add(false);
                decks.Add(startFloorY);
            }

            for (int ni = 0; ni < nodeChain.Count; ni++)
            {
                int nodeId = nodeChain[ni];
                waypoints.Add(world[nodeId]);
                decks.Add(floors[nodeId]);

                // Forced: a Force/Priority link arrives here, or this is an off-level Stair
                // entry the seeder already accepted. docs/invariants.md#a-stair-entry-is-committed-once-taken
                bool isForced = ni == 0 &&
                                NodeById(nodeId)?.Type == NodeType.Stair &&
                                Mathf.Abs(floors[nodeId] - startFloorY) > SameLevelDeltaY;
                if (ni > 0)
                {
                    int prevId = nodeChain[ni - 1];
                    if (_edges.TryGetValue(prevId, out List<Edge> prevEdges))
                    {
                        foreach (Edge e in prevEdges)
                        {
                            if (e.To == nodeId)
                            {
                                isForced = e.Kind == EdgeKind.Forced || e.Kind == EdgeKind.Priority;
                                break;
                            }
                        }
                    }
                }
                forced.Add(isForced);
            }

            if (finishesAtGoal && (end - world[goalId]).sqrMagnitude > 0.25f)
            {
                waypoints.Add(end);
                forced.Add(false);
                decks.Add(endFloorY);
            }

            return new NavPath(waypoints, forced, decks);
        }

        /// <summary>
        /// Valid after EnsureEdgesFresh: the index is rebuilt with the edge cache, and every
        /// change to the node set marks that cache dirty.
        /// </summary>
        private static Node? NodeById(int id) => NodesById.GetValueOrDefault(id);

        /// <summary>
        /// Endpoint check, for the short clear goal and the finish leg: a climb cap, a thin line of sight,
        /// and a walkable line at knee height - an eye-height line flies over a railing.
        /// docs/invariants.md#an-endpoint-must-be-walkable-not-merely-visible
        /// </summary>
        private static bool HasLineOfSight(Vector3 a, Vector3 b, float maxDeltaY)
        {
            return NavProbe.ThinLos(a, b, maxDeltaY) && NavProbe.WalkLos(a, b, maxDeltaY);
        }
    }
}

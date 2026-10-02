using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// A Blockbench model (.bbmodel, embedded in the dll) as a Unity mesh and its texture, built once.
    /// Cubes and meshes, unrotated, one texture; units are centimetres. docs/anomalies.md#the-mess-and-the-meat-model
    /// </summary>
    internal static class BbModel
    {
        internal sealed class Model(Mesh mesh, Mesh hull, Texture2D texture)
        {
            public readonly Mesh Mesh = mesh;
            /// <summary>
            /// For a convex collider, the outline as a low prism. Unity's hull keeps at most 255 polygons.
            /// </summary>
            public readonly Mesh Hull = hull;
            public readonly Texture2D Texture = texture;
        }

        private const float UnitsPerMetre = 100f;
        private const int HullSides = 16;
        private static readonly Dictionary<string, Model?> Cache = [];

        private static readonly string[] FaceNames = ["north", "south", "east", "west", "up", "down"];

        /// <summary>
        /// The model in an embedded resource, or null (logged once) when it is missing or unreadable.
        /// </summary>
        internal static Model? Load(string resource)
        {
            if (Cache.TryGetValue(resource, out Model? cached) && (cached == null || cached.Mesh != null)) return cached;

            Model? model = null;
            try
            {
                using Stream? stream = typeof(BbModel).Assembly.GetManifestResourceStream(resource);
                if (stream == null) throw new InvalidDataException("not in the dll");

                using StreamReader reader = new(stream);
                model = Build(JObject.Parse(reader.ReadToEnd()), resource);
            }
            catch (Exception ex)
            {
                YourBuddyPlugin.Log.LogWarning($"[anomaly] Model {resource} could not be built: {ex.Message}");
            }
            Cache[resource] = model;
            return model;
        }

        private static Model Build(JObject json, string resource)
        {
            // A texture's own UV size wins over the project's (per_texture_uv_size in the free format).
            float texW = (float?)json["textures"]?[0]?["uv_width"] ?? (float?)json["resolution"]?["width"] ?? 16f;
            float texH = (float?)json["textures"]?[0]?["uv_height"] ?? (float?)json["resolution"]?["height"] ?? 16f;
            List<Vector3> vertices = [];
            List<Vector3> normals = [];
            List<Vector2> uvs = [];
            List<int> triangles = [];
            foreach (JToken element in json["elements"] ?? new JArray())
            {
                string type = (string?)element["type"] ?? "cube";
                if (type == "mesh")
                {
                    AddMesh(element, resource, texW, texH, vertices, normals, uvs, triangles);
                    continue;
                }
                if (type != "cube") continue;

                Vector3 from = Vec(element["from"]);
                Vector3 to = Vec(element["to"]);
                if (Vec(element["rotation"]) != Vector3.zero)
                {
                    YourBuddyPlugin.Log.LogWarning($"[anomaly] {resource}: cube '{element["name"]}' is rotated - drawn unrotated");
                }
                foreach (string face in FaceNames)
                {
                    JToken? data = element["faces"]?[face];
                    if (data == null || data["texture"] == null || data["texture"]!.Type == JTokenType.Null) continue;

                    AddFace(face, from, to, data["uv"], texW, texH, vertices, normals, uvs, triangles);
                }
            }
            Mesh mesh = new() { name = resource };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            string source = (string?)json["textures"]?[0]?["source"] ?? "";
            int comma = source.IndexOf(',');
            Texture2D texture = new(2, 2, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
            if (comma < 0 || !texture.LoadImage(Convert.FromBase64String(source[(comma + 1)..])))
            {
                throw new InvalidDataException("its texture is not an embedded PNG");
            }
            // Compressed to DXT on the GPU and the CPU copy freed, since nothing reads its pixels back.
            texture.Compress(true);
            texture.Apply(false, true);
            texture.name = resource;
            return new Model(mesh, Prism(vertices, resource), texture);
        }

        /// <summary>
        /// One face as Blockbench draws it, with corners top-left, top-right, bottom-right, bottom-left seen
        /// from outside, matching the face's UV rectangle. Blockbench's z is mirrored into Unity's.
        /// </summary>
        private static void AddFace(string face, Vector3 a, Vector3 b, JToken? uv, float texW, float texH,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            (Vector3[] corners, Vector3 normal) = face switch
            {
                "north" => (new[] { V(b.x, b.y, a.z), V(a.x, b.y, a.z), V(a.x, a.y, a.z), V(b.x, a.y, a.z) }, Vector3.back),
                "south" => (new[] { V(a.x, b.y, b.z), V(b.x, b.y, b.z), V(b.x, a.y, b.z), V(a.x, a.y, b.z) }, Vector3.forward),
                "east" => (new[] { V(b.x, b.y, b.z), V(b.x, b.y, a.z), V(b.x, a.y, a.z), V(b.x, a.y, b.z) }, Vector3.right),
                "west" => (new[] { V(a.x, b.y, a.z), V(a.x, b.y, b.z), V(a.x, a.y, b.z), V(a.x, a.y, a.z) }, Vector3.left),
                "up" => (new[] { V(a.x, b.y, a.z), V(b.x, b.y, a.z), V(b.x, b.y, b.z), V(a.x, b.y, b.z) }, Vector3.up),
                _ => (new[] { V(a.x, a.y, b.z), V(b.x, a.y, b.z), V(b.x, a.y, a.z), V(a.x, a.y, a.z) }, Vector3.down),
            };
            Vector3 unityNormal = new(normal.x, normal.y, -normal.z);
            float u0 = (float?)uv?[0] ?? 0f, v0 = (float?)uv?[1] ?? 0f, u1 = (float?)uv?[2] ?? 0f, v1 = (float?)uv?[3] ?? 0f;
            Vector2[] faceUvs = [new(u0, v0), new(u1, v0), new(u1, v1), new(u0, v1)];
            int first = vertices.Count;
            for (int i = 0; i < 4; i++)
            {
                vertices.Add(new Vector3(corners[i].x, corners[i].y, -corners[i].z) / UnitsPerMetre);
                normals.Add(unityNormal);
                uvs.Add(new Vector2(faceUvs[i].x / texW, 1f - faceUvs[i].y / texH));
            }
            // The mirror flips the winding, so put it back to make the face point out.
            Vector3 drawn = Vector3.Cross(vertices[first + 1] - vertices[first], vertices[first + 2] - vertices[first]);
            if (Vector3.Dot(drawn, unityNormal) >= 0f) triangles.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
            else triangles.AddRange([first, first + 2, first + 1, first, first + 3, first + 2]);
        }

        /// <summary>
        /// A mesh element of triangles and quads, with normals smoothed over shared vertices. Blockbench
        /// winds faces counter-clockwise from outside; the z mirror reverses that for Unity.
        /// </summary>
        private static void AddMesh(JToken element, string resource, float texW, float texH,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            if (Vec(element["rotation"]) != Vector3.zero)
            {
                YourBuddyPlugin.Log.LogWarning($"[anomaly] {resource}: mesh '{element["name"]}' is rotated - drawn unrotated");
            }
            Vector3 origin = Vec(element["origin"]);
            Dictionary<string, Vector3> points = [];
            foreach (JProperty vertex in (element["vertices"] as JObject)?.Properties() ?? [])
            {
                points[vertex.Name] = origin + Vec(vertex.Value);
            }

            // Corners of every triangle, as (vertex key, uv), and each key's area-weighted normal.
            List<(string Key, Vector2 Uv)> corners = [];
            Dictionary<string, Vector3> smooth = [];
            foreach (JToken face in (element["faces"] as JObject)?.PropertyValues() ?? [])
            {
                if (face["texture"] == null || face["texture"]!.Type == JTokenType.Null) continue;

                List<string> keys = face["vertices"]?.Values<string>().OfType<string>().Where(points.ContainsKey).ToList() ?? [];
                if (keys.Count == 4) keys = SortQuad(keys, points);
                else if (keys.Count != 3) continue;

                for (int i = 1; i + 1 < keys.Count; i++)
                {
                    string[] tri = [keys[0], keys[i], keys[i + 1]];
                    Vector3 n = Vector3.Cross(points[tri[1]] - points[tri[0]], points[tri[2]] - points[tri[0]]);
                    foreach (string key in tri)
                    {
                        smooth[key] = smooth.TryGetValue(key, out Vector3 sum) ? sum + n : n;
                        corners.Add((key, Vec2(face["uv"]?[key])));
                    }
                }
            }

            Dictionary<(string, Vector2), int> made = [];
            for (int c = 0; c < corners.Count; c += 3)
            {
                // Mirrored into Unity's z, so the triangle is laid down reversed.
                for (int k = 2; k >= 0; k--)
                {
                    (string key, Vector2 uv) = corners[c + k];
                    if (!made.TryGetValue((key, uv), out int index))
                    {
                        index = vertices.Count;
                        made[(key, uv)] = index;
                        Vector3 p = points[key], n = smooth[key].normalized;
                        vertices.Add(new Vector3(p.x, p.y, -p.z) / UnitsPerMetre);
                        normals.Add(new Vector3(n.x, n.y, -n.z));
                        uvs.Add(new Vector2(uv.x / texW, 1f - uv.y / texH));
                    }
                    triangles.Add(index);
                }
            }
        }

        /// <summary>
        /// The model's convex outline seen from above, cut to HullSides corners (each time dropping the
        /// corner that loses least area), standing from its lowest point to its highest.
        /// </summary>
        private static Mesh Prism(List<Vector3> points, string resource)
        {
            float low = float.MaxValue, high = float.MinValue;
            List<Vector2> flat = new(points.Count);
            foreach (Vector3 p in points)
            {
                low = Mathf.Min(low, p.y);
                high = Mathf.Max(high, p.y);
                flat.Add(new Vector2(p.x, p.z));
            }
            List<Vector2> ring = Outline(flat);
            while (ring.Count > HullSides)
            {
                int cheapest = 0;
                float least = float.MaxValue;
                for (int i = 0; i < ring.Count; i++)
                {
                    float lost = Mathf.Abs(Cross(ring[(i + ring.Count - 1) % ring.Count], ring[i], ring[(i + 1) % ring.Count]));
                    if (lost < least) (least, cheapest) = (lost, i);
                }
                ring.RemoveAt(cheapest);
            }

            List<Vector3> corners = [];
            List<int> triangles = [];
            int n = ring.Count;
            foreach (Vector2 c in ring) corners.Add(new Vector3(c.x, low, c.y));
            foreach (Vector2 c in ring) corners.Add(new Vector3(c.x, high, c.y));
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                triangles.AddRange([i, j, n + j, i, n + j, n + i]);
                if (i >= 1 && i + 1 < n) triangles.AddRange([0, i + 1, i, n, n + i, n + i + 1]);
            }
            Mesh hull = new() { name = resource + " hull" };
            hull.SetVertices(corners);
            hull.SetTriangles(triangles, 0);
            hull.RecalculateBounds();
            return hull;
        }

        /// <summary>
        /// Convex hull of 2D points, counter-clockwise (Andrew's monotone chain).
        /// </summary>
        private static List<Vector2> Outline(List<Vector2> points)
        {
            List<Vector2> sorted = points.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            if (sorted.Count < 3) return sorted;

            List<Vector2> hull = [];
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                foreach (Vector2 p in sorted)
                {
                    while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], p) <= 0f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
                sorted.Reverse();
            }
            return hull;
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        /// <summary>
        /// A quad's corners in perimeter order, as Blockbench's MeshFace.getSortedVertices puts them.
        /// </summary>
        private static List<string> SortQuad(List<string> k, Dictionary<string, Vector3> p)
        {
            if (Beyond(p[k[1]], p[k[2]], p[k[0]], p[k[3]])) return [k[2], k[0], k[1], k[3]];
            if (Beyond(p[k[0]], p[k[1]], p[k[2]], p[k[3]])) return [k[0], k[2], k[1], k[3]];
            return k;
        }

        /// <summary>
        /// True when `check` lies past the line base1-base2 on the side away from `top`.
        /// </summary>
        private static bool Beyond(Vector3 base1, Vector3 base2, Vector3 top, Vector3 check)
        {
            Vector3 line = base2 - base1;
            Vector3 foot = base1 + Vector3.Project(top - base1, line);
            return Vector3.Dot(foot - top, check - base2) > 0f;
        }

        private static Vector3 V(float x, float y, float z) => new(x, y, z);

        private static Vector2 Vec2(JToken? token) =>
            token is JArray { Count: >= 2 } a ? new Vector2((float)a[0], (float)a[1]) : Vector2.zero;

        private static Vector3 Vec(JToken? token) =>
            token is JArray { Count: >= 3 } a ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : Vector3.zero;
    }
}

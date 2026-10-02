using System.Collections.Generic;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The buddy's suit spattered with blood. A copy of the texture it wears now with dark red blots
    /// painted over it, put on and taken off through BuddySkin. docs/anomalies.md#bloody
    /// </summary>
    internal static class BuddyGore
    {
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly Dictionary<(Texture, int), Texture2D> Made = [];
        private static readonly Dictionary<(Texture, string), Texture2D> MadeOverlays = [];
        private static readonly Dictionary<string, Texture2D?> Overlays = [];

        /// <summary>
        /// Blots per 64x64 texels, and their radius in texels at that size.
        /// </summary>
        private const int Blots = 16;
        private const float BlotMinRadius = 1f;
        private const float BlotMaxRadius = 4f;
        private static readonly Color Blood = new(0.4f, 0.02f, 0.03f, 1f);

        /// <summary>
        /// Puts the bloody copy of what the body wears on it. `seed` keeps one buddy's stains the same
        /// each time. Returns the texture it was wearing, for Remove, or null when nothing took it.
        /// </summary>
        internal static Texture? Apply(Component body, int seed, out Texture2D? bloody)
        {
            bloody = null;
            Texture? wearing = Current(body);
            if (wearing == null) return null;

            bloody = Make(wearing, seed);
            if (bloody == null || BuddySkin.ApplyTexture(body, bloody) == 0) return null;

            return wearing;
        }

        /// <summary>
        /// Restores the look it had before Apply, its original or the skin it wore then.
        /// </summary>
        internal static void Remove(Component body, Texture? wearing)
        {
            BuddySkin.RestoreAll(body);
            if (wearing is Texture2D before && Current(body) != before) BuddySkin.ApplyTexture(body, before);
        }

        /// <summary>
        /// The texture on the body's first skinned material that takes one.
        /// </summary>
        private static Texture? Current(Component body)
        {
            foreach (SkinnedMeshRenderer renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;

                    if (material.HasProperty(BaseMap) && material.GetTexture(BaseMap) is { } baseMap) return baseMap;

                    if (material.HasProperty(MainTex) && material.GetTexture(MainTex) is { } mainTex) return mainTex;
                }
            }
            return null;
        }

        private static Texture2D? Make(Texture source, int seed) => Painted(source, seed, (pixels, w, h) =>
        {
            float scale = w / 64f;
            System.Random random = new(seed * 7919 + 17);
            int placed = 0;
            for (int tries = 0; placed < Blots && tries < Blots * 20; tries++)
            {
                int cx = random.Next(w);
                int cy = random.Next(h);
                // Only on the painted parts of the atlas, since a blot on its empty margin shows nowhere.
                if (pixels[cy * w + cx].a < 0.5f) continue;

                placed++;
                float radius = (BlotMinRadius + (float)random.NextDouble() * (BlotMaxRadius - BlotMinRadius)) * scale;
                Blot(pixels, w, h, cx, cy, radius, random);
            }
        });

        /// <summary>
        /// A readable copy of `source` with `paint` applied to its pixels (and the width and height), made once per key.
        /// </summary>
        private static Texture2D? Painted(Texture source, int key, System.Action<Color[], int, int> paint)
        {
            if (Made.TryGetValue((source, key), out Texture2D? cached) && cached != null) return cached;

            Texture2D? copy = Readable(source);
            if (copy == null) return null;

            Color[] pixels = copy.GetPixels();
            paint(pixels, copy.width, copy.height);
            copy.SetPixels(pixels);
            copy.Apply(false);
            Made[(source, key)] = copy;
            return copy;
        }

        /// <summary>
        /// `was` tinted toward `paint` by `amount`, keeping its alpha so the atlas's empty margin stays empty.
        /// </summary>
        private static Color Over(Color was, Color paint, float amount)
        {
            Color mixed = Color.Lerp(was, paint, amount);
            mixed.a = was.a;
            return mixed;
        }

        private static void Blot(Color[] pixels, int w, int h, int cx, int cy, float radius, System.Random random)
        {
            int r = Mathf.CeilToInt(radius);
            float shade = 0.7f + (float)random.NextDouble() * 0.3f;
            for (int y = Mathf.Max(0, cy - r); y <= Mathf.Min(h - 1, cy + r); y++)
            {
                for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(w - 1, cx + r); x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / Mathf.Max(0.5f, radius);
                    if (d > 1f) continue;

                    // Ragged edge, the rim is hit or miss.
                    if (d > 0.6f && random.NextDouble() < d - 0.3f) continue;

                    pixels[y * w + x] = Over(pixels[y * w + x], Blood * shade, d < 0.6f ? 0.92f : 0.7f);
                }
            }
        }

        /// <summary>
        /// The Metal_Pipe atlas with its top end bloody. The sides lie on v 0.25..1 (v 1 at the top end), the
        /// top cap at u 0.375..0.56, v 0.06..0.25. Blood thins down the shaft, with a few drips.
        /// </summary>
        internal static Texture2D? BloodyEnd(Texture source) => Painted(source, -1, (pixels, w, h) =>
        {
            System.Random random = new(4111);
            float[] drips = new float[w];
            for (int x = 0; x < w; x++) drips[x] = random.NextDouble() < 0.4 ? 0.25f + (float)random.NextDouble() * 0.3f : 1f;

            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    Color was = pixels[y * w + x];
                    if (was.a < 0.5f) continue;

                    float u = (x + 0.5f) / w;
                    float t = (v - 0.25f) / 0.75f;
                    float chance = v >= 0.25f
                        ? t > 0.8f ? 0.95f : t > 0.6f ? 0.6f : t > drips[x] ? 0.9f : t > 0.45f ? 0.2f : 0f
                        : u is > 0.375f and < 0.5625f && v > 0.0625f ? 0.9f : 0f;
                    if (random.NextDouble() >= chance) continue;

                    pixels[y * w + x] = Over(was, Blood * (0.7f + (float)random.NextDouble() * 0.3f), 0.85f);
                }
            }
        });

        /// <summary>
        /// What the body wears now with an embedded overlay painted over it, at the larger of the two sizes,
        /// point sampled so the skin flickers. The overlays follow the suit's atlas layout.
        /// Null when either cannot be read. docs/anomalies.md#smile-and-underthesuit
        /// </summary>
        internal static Texture2D? Overlaid(Component body, string resource, out Texture? wearing)
        {
            wearing = Current(body);
            if (wearing == null) return null;

            Texture2D? overlay = Embedded(resource);
            if (overlay == null) return null;

            if (MadeOverlays.TryGetValue((wearing, resource), out Texture2D? cached) && cached != null) return cached;

            Texture2D? under = Readable(wearing);
            if (under == null) return null;

            int size = Mathf.Max(under.width, overlay.width);
            Color[] below = under.GetPixels();
            Color[] above = overlay.GetPixels();
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color was = below[y * under.height / size * under.width + x * under.width / size];
                    Color paint = above[y * overlay.height / size * overlay.width + x * overlay.width / size];
                    pixels[y * size + x] = Over(was, paint, paint.a);
                }
            }
            Texture2D made = new(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = wearing.name + "_" + resource,
            };
            made.SetPixels(pixels);
            made.Apply(false);
            Object.Destroy(under);
            MadeOverlays[(wearing, resource)] = made;
            return made;
        }

        /// <summary>
        /// A PNG embedded in the dll as YourBuddy.Resources.`name`, read once.
        /// </summary>
        private static Texture2D? Embedded(string name)
        {
            if (Overlays.TryGetValue(name, out Texture2D? loaded)) return loaded;

            Texture2D? texture = null;
            using (System.IO.Stream? stream = typeof(BuddyGore).Assembly.GetManifestResourceStream("YourBuddy.Resources." + name))
            {
                if (stream != null)
                {
                    using System.IO.MemoryStream bytes = new();
                    stream.CopyTo(bytes);
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(texture, bytes.ToArray()))
                    {
                        Object.Destroy(texture);
                        texture = null;
                    }
                }
            }
            if (texture == null) YourBuddyPlugin.Log.LogWarning($"[anomaly] The overlay {name} is not in the dll or is not a PNG - that flicker is off");

            Overlays[name] = texture;
            return texture;
        }

        /// <summary>
        /// A CPU copy of any texture, readable or not, through a render target.
        /// </summary>
        private static Texture2D? Readable(Texture source)
        {
            int w = source.width;
            int h = source.height;
            if (w <= 0 || h <= 0) return null;

            RenderTexture target = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                Texture2D copy = new(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = source.filterMode,
                    wrapMode = TextureWrapMode.Clamp,
                    name = source.name + "_bloody",
                };
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                copy.Apply(false);
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}

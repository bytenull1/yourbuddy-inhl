using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Swaps the texture on a buddy's body with a PNG from the "skins" folder next to the plugin dll,
    /// and puts the original back. Debug tool behind buddy_manage skin. docs/reference.md#2-debug-commands
    /// </summary>
    internal static class BuddySkin
    {
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Color_ = Shader.PropertyToID("_Color");

        private sealed class Original(Texture? baseMap, Texture? mainTex, Color? baseTint, Color? tint)
        {
            internal readonly Texture? BaseMap = baseMap;
            internal readonly Texture? MainTex = mainTex;
            internal readonly Color? BaseTint = baseTint;
            internal readonly Color? Tint = tint;
        }

        /// <summary>
        /// A material's first look, kept so "default" can restore it. Materials are per buddy instances.
        /// </summary>
        private static readonly ConditionalWeakTable<Material, Original> Originals = new();
        private static readonly Dictionary<string, Texture2D> Loaded = [];

        internal static string Folder => Path.Combine(Path.GetDirectoryName(typeof(YourBuddyPlugin).Assembly.Location) ?? ".", "skins");

        /// <summary>
        /// The names (no extension) of the PNGs in the skins folder.
        /// </summary>
        internal static List<string> Available()
        {
            List<string> names = [];
            if (Directory.Exists(Folder))
            {
                foreach (string file in Directory.GetFiles(Folder, "*.png")) names.Add(Path.GetFileNameWithoutExtension(file));
            }
            return names;
        }

        /// <summary>
        /// Puts skin `name` on the buddy, or the original look for "default". Returns the reply line.
        /// </summary>
        internal static string Apply(BuddyBehaviour buddy, string name)
        {
            bool restore = name.Equals("default", StringComparison.OrdinalIgnoreCase);
            Texture2D? skin = null;
            if (!restore)
            {
                string? failure = Load(name, out skin);
                if (failure != null) return failure;
            }

            int changed = restore ? RestoreAll(buddy) : ApplyTexture(buddy, skin!); // skin is set whenever not restoring
            if (changed == 0) return buddy.Name + " has no material that takes a skin";

            return buddy.Name + (restore ? " skin restored" : " skin: " + name);
        }

        /// <summary>
        /// Puts a ready texture on every skinned material of the body and returns how many changed.
        /// This is how the buddy wears its EVA suit. docs/eva.md
        /// </summary>
        internal static int ApplyTexture(Component body, Texture2D skin) => Change(body, material => Set(material, skin));

        /// <summary>
        /// Restores every skinned material's original look and returns how many were restored.
        /// </summary>
        internal static int RestoreAll(Component body) => Change(body, Restore);

        /// <summary>
        /// Runs `change` on every skinned material of the body and returns how many it changed.
        /// </summary>
        private static int Change(Component body, System.Func<Material, bool> change)
        {
            int changed = 0;
            foreach (SkinnedMeshRenderer renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (Material material in renderer.materials)
                {
                    if (change(material)) changed++;
                }
            }
            return changed;
        }

        private static string? Load(string name, out Texture2D? skin)
        {
            skin = null;
            if (name.IndexOfAny(['/', '\\', ':']) >= 0 || name.Contains("..")) return "A skin name is a file name without a folder";

            if (Loaded.TryGetValue(name, out skin) && skin != null) return null;

            string path = Path.Combine(Folder, name + ".png");
            if (!File.Exists(path)) return "No skin '" + name + "' - put " + name + ".png in " + Folder;

            Texture2D texture = new(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
            {
                UnityEngine.Object.Destroy(texture);
                return "Could not read " + path + " as a PNG";
            }
            texture.filterMode = FilterMode.Point;
            Loaded[name] = texture;
            skin = texture;
            return null;
        }

        /// <summary>
        /// Whether every skinned material that takes a skin now shows `skin`. False when something
        /// rewrote the body's materials, so the wearer can put the skin back.
        /// </summary>
        internal static bool IsApplied(Component body, Texture2D skin)
        {
            foreach (SkinnedMeshRenderer renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (Material material in renderer.materials)
                {
                    bool hasBase = material.HasProperty(BaseMap);
                    bool hasMain = material.HasProperty(MainTex);
                    if (!hasBase && !hasMain) continue; // never took a skin at all

                    if (hasBase && material.GetTexture(BaseMap) == skin) continue;
                    if (hasMain && material.GetTexture(MainTex) == skin) continue;
                    return false;
                }
            }
            return true;
        }

        private static bool Set(Material material, Texture2D skin)
        {
            bool hasBase = material.HasProperty(BaseMap);
            bool hasMain = material.HasProperty(MainTex);
            if (!hasBase && !hasMain) return false;

            Originals.GetValue(material, m => new Original(
                m.HasProperty(BaseMap) ? m.GetTexture(BaseMap) : null,
                m.HasProperty(MainTex) ? m.GetTexture(MainTex) : null,
                m.HasProperty(BaseColor) ? m.GetColor(BaseColor) : null,
                m.HasProperty(Color_) ? m.GetColor(Color_) : null));

            // A tint would multiply into the skin, so show it as painted.
            if (hasBase) material.SetTexture(BaseMap, skin);
            if (hasMain) material.SetTexture(MainTex, skin);
            if (material.HasProperty(BaseColor)) material.SetColor(BaseColor, Color.white);
            if (material.HasProperty(Color_)) material.SetColor(Color_, Color.white);
            return true;
        }

        private static bool Restore(Material material)
        {
            if (!Originals.TryGetValue(material, out Original original)) return false;

            if (material.HasProperty(BaseMap)) material.SetTexture(BaseMap, original.BaseMap);
            if (material.HasProperty(MainTex)) material.SetTexture(MainTex, original.MainTex);
            if (original.BaseTint is Color baseColor) material.SetColor(BaseColor, baseColor);
            if (original.Tint is Color color) material.SetColor(Color_, color);
            return true;
        }
    }
}

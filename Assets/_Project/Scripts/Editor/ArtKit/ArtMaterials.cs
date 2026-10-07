using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScrapYardKing.EditorTools.Art
{
    /// <summary>
    /// The Scrap Yard King palette and stylized PBR materials. Every material is URP Lit with a saturated base colour
    /// multiplied by a subtle tileable detail texture (paint grain, brushed metal, cloth, rubber), so surfaces read as
    /// "clean with a little wear" (target: 70% clean base, 20% wear, 10% detail) instead of flat plastic.
    /// Procedural textures are generated once into <c>Art/Textures/Toy</c>.
    /// </summary>
    public static class ArtMaterials
    {
        // ---------- palette ----------
        public static readonly Color Yellow = new(1f, 0.76f, 0.1f);
        public static readonly Color YellowDeep = new(0.93f, 0.6f, 0.05f);
        public static readonly Color Orange = new(1f, 0.46f, 0.1f);
        public static readonly Color Red = new(0.86f, 0.18f, 0.13f);
        public static readonly Color RedDeep = new(0.62f, 0.12f, 0.1f);
        public static readonly Color Blue = new(0.17f, 0.45f, 0.86f);
        public static readonly Color BlueDeep = new(0.11f, 0.22f, 0.5f);
        public static readonly Color Teal = new(0.12f, 0.6f, 0.62f);
        public static readonly Color Green = new(0.32f, 0.74f, 0.26f);
        public static readonly Color GreenCash = new(0.42f, 0.86f, 0.22f);
        public static readonly Color Charcoal = new(0.15f, 0.16f, 0.18f);
        public static readonly Color Gray = new(0.46f, 0.48f, 0.52f);
        public static readonly Color Silver = new(0.74f, 0.76f, 0.8f);
        public static readonly Color Cream = new(0.97f, 0.93f, 0.81f);
        public static readonly Color Brown = new(0.47f, 0.31f, 0.2f);
        public static readonly Color Rust = new(0.66f, 0.33f, 0.15f);

        public enum Detail
        {
            Paint,
            Metal,
            Cloth,
            Rubber,
            Plain,
            Concrete,
            Wood,
            RustPatch
        }

        static readonly Dictionary<string, Material> Cache = new();

        /// <summary>Stylized PBR material (created or updated in place).</summary>
        public static Material Mat(string name, Color color, float smoothness = 0.35f, float metallic = 0f, Detail detail = Detail.Paint,
            float tiling = 0.6f, Color? emission = null)
        {
            Directory.CreateDirectory(ArtAssets.MatDir);
            string path = $"{ArtAssets.MatDir}/M_{name}.mat";
            if (!Cache.TryGetValue(path, out var m) || m == null)
            {
                m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_" + name };
                    AssetDatabase.CreateAsset(m, path);
                }

                Cache[path] = m;
            }

            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            var tex = detail == Detail.Plain ? null : DetailTexture(detail);
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", Vector2.one * tiling);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", emission.Value);
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }

            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Unlit transparent material (decals, glows, shadows).</summary>
        public static Material Transparent(string name, Color color, Texture2D texture = null, bool additive = false, bool unlit = true)
        {
            Directory.CreateDirectory(ArtAssets.MatDir);
            string path = $"{ArtAssets.MatDir}/M_{name}.mat";
            var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = "M_" + name };
                AssetDatabase.CreateAsset(m, path);
            }

            m.shader = shader;
            m.SetColor("_BaseColor", color);
            m.SetTexture("_BaseMap", texture);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            if (!unlit)
            {
                m.SetFloat("_Smoothness", 0.1f);
                m.SetFloat("_ReceiveShadows", 1f);
            }

            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------- procedural detail textures ----------

        public static Texture2D DetailTexture(Detail detail)
        {
            string name = "T_Detail_" + detail;
            string path = $"{ArtAssets.TexDir}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            const int size = 256;
            var px = new Color[size * size];
            var rng = new System.Random(detail.GetHashCode() * 31 + 7);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                px[y * size + x] = detail switch
                {
                    Detail.Paint => Gray01(0.95f + (Fbm(u, v, 6, 4, 11) - 0.5f) * 0.09f - Spots(u, v, 14, 0.82f, 21) * 0.08f),
                    Detail.Metal => Gray01(0.9f + (Fbm(u * 0.25f, v, 4, 3, 5) - 0.5f) * 0.1f + (Fbm(u, v, 24, 2, 9) - 0.5f) * 0.06f),
                    Detail.Cloth => Gray01(0.93f + Mathf.Sin(u * Mathf.PI * 2f * 48f) * Mathf.Sin(v * Mathf.PI * 2f * 48f) * 0.03f +
                                           (Fbm(u, v, 8, 3, 3) - 0.5f) * 0.08f),
                    Detail.Rubber => Gray01(0.9f + (Fbm(u, v, 20, 3, 17) - 0.5f) * 0.12f),
                    Detail.Concrete => Gray01(0.88f + (Fbm(u, v, 5, 5, 23) - 0.5f) * 0.18f - Spots(u, v, 30, 0.9f, 4) * 0.12f),
                    Detail.Wood => Gray01(0.86f + Mathf.Sin((v * 9f + Fbm(u, v, 3, 3, 8) * 1.6f) * Mathf.PI * 2f) * 0.06f + (Fbm(u * 4f, v, 6, 2, 2) - 0.5f) * 0.08f),
                    Detail.RustPatch => RustColor(u, v),
                    _ => Color.white
                };
            }

            // Fine scratches on metal and paint: thin brighter strokes, sparse.
            if (detail is Detail.Metal or Detail.Paint)
            {
                int strokes = detail == Detail.Metal ? 70 : 18;
                for (int s = 0; s < strokes; s++)
                {
                    float x0 = (float)rng.NextDouble() * size, y0 = (float)rng.NextDouble() * size;
                    float ang = (float)(rng.NextDouble() * Math.PI);
                    int len = 8 + rng.Next(30);
                    float k = detail == Detail.Metal ? 0.08f : 0.06f;
                    for (int i = 0; i < len; i++)
                    {
                        int x = ((int)(x0 + Mathf.Cos(ang) * i) % size + size) % size;
                        int y = ((int)(y0 + Mathf.Sin(ang) * i) % size + size) % size;
                        var c = px[y * size + x];
                        px[y * size + x] = new Color(Mathf.Min(1f, c.r + k), Mathf.Min(1f, c.g + k), Mathf.Min(1f, c.b + k), 1f);
                    }
                }
            }

            return SaveTexture(path, size, px, true);
        }

        static Color RustColor(float u, float v)
        {
            float n = Fbm(u, v, 5, 4, 41);
            float spots = Spots(u, v, 10, 0.6f, 13);
            float t = Mathf.Clamp01(n * 1.2f - 0.1f + spots * 0.3f);
            return Color.Lerp(new Color(0.82f, 0.47f, 0.22f), new Color(0.42f, 0.2f, 0.09f), t);
        }

        static Color Gray01(float g)
        {
            g = Mathf.Clamp01(g);
            return new Color(g, g, g, 1f);
        }

        public static Texture2D SaveTexture(string path, int size, Color[] pixels, bool tile, bool alpha = false, bool mips = true, int width = -1)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            int w = width > 0 ? width : size;
            int h = pixels.Length / w;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.mipmapEnabled = mips;
            importer.alphaIsTransparency = alpha;
            importer.anisoLevel = 4;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------- tileable noise ----------

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7fffffff) / (float)int.MaxValue;
            }
        }

        /// <summary>Periodic value noise on [0,1)² with <paramref name="period"/> cells per tile.</summary>
        public static float Noise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int Wrap(int i) => ((i % period) + period) % period;
            float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Tileable fractal noise in [0,1].</summary>
        public static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Noise(u, v, period << o, seed + o * 17) * amp;
                norm += amp;
                amp *= 0.5f;
            }

            return sum / norm;
        }

        /// <summary>Soft round spots where noise exceeds <paramref name="threshold"/> (0..1 strength).</summary>
        public static float Spots(float u, float v, int period, float threshold, int seed)
        {
            float n = Fbm(u, v, period, 2, seed);
            return Mathf.Clamp01((n - threshold) / (1f - threshold));
        }
    }
}

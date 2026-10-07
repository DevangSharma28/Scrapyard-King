using UnityEditor;
using UnityEngine;

namespace ScrapYardKing.EditorTools.Art
{
    /// <summary>Palette colours (also the slot numbers of a palette-mode <see cref="MeshKit"/>).</summary>
    public enum PC
    {
        Yellow, YellowDeep, Orange, Red, RedDeep, Blue, BlueDeep, Teal, Green, GreenDark, GreenCash, Charcoal, Gray, GrayLight, Silver, Cream,
        White, Brown, BrownDark, Rust, RustDark, Wood, WoodDark, Rubber, Glass, Pink, Purple, Concrete, Sand, Leaf, LeafDark, Bark, Mint, SteelBlue,
        Navy, Lime, Count
    }

    /// <summary>
    /// One small texture of flat colour cells shared by many props and items. Meshes built in palette mode point each
    /// vertex at its colour cell, so a whole cluster of props draws with one material and batches (mobile budget).
    /// </summary>
    public static class ArtPalette
    {
        const int Columns = 8;
        const int Cell = 16;
        const string TexPath = ArtAssets.TexDir + "/T_ToyPalette.png";

        static readonly Color[] Colors =
        {
            new(1f, 0.76f, 0.1f), new(0.93f, 0.6f, 0.05f), new(1f, 0.46f, 0.1f), new(0.86f, 0.18f, 0.13f), new(0.6f, 0.12f, 0.1f),
            new(0.17f, 0.45f, 0.86f), new(0.11f, 0.22f, 0.5f), new(0.12f, 0.6f, 0.62f), new(0.32f, 0.72f, 0.26f), new(0.15f, 0.42f, 0.2f),
            new(0.42f, 0.86f, 0.22f), new(0.15f, 0.16f, 0.18f), new(0.45f, 0.47f, 0.51f), new(0.66f, 0.68f, 0.72f), new(0.8f, 0.82f, 0.86f),
            new(0.97f, 0.93f, 0.81f), new(0.97f, 0.97f, 0.96f), new(0.5f, 0.33f, 0.2f), new(0.3f, 0.2f, 0.13f), new(0.68f, 0.34f, 0.15f),
            new(0.45f, 0.22f, 0.12f), new(0.78f, 0.58f, 0.36f), new(0.55f, 0.38f, 0.22f), new(0.12f, 0.12f, 0.13f), new(0.55f, 0.72f, 0.82f),
            new(0.97f, 0.58f, 0.65f), new(0.52f, 0.32f, 0.78f), new(0.72f, 0.7f, 0.66f), new(0.86f, 0.76f, 0.56f), new(0.36f, 0.7f, 0.25f),
            new(0.2f, 0.5f, 0.2f), new(0.45f, 0.3f, 0.18f), new(0.6f, 0.88f, 0.76f), new(0.4f, 0.55f, 0.72f), new(0.14f, 0.2f, 0.38f),
            new(0.7f, 0.9f, 0.18f),
        };

        public static Vector2 Uv(int slot)
        {
            int i = Mathf.Clamp(slot, 0, Colors.Length - 1);
            int rows = (Colors.Length + Columns - 1) / Columns;
            float u = (i % Columns + 0.5f) / Columns;
            float v = 1f - (i / Columns + 0.5f) / Mathf.NextPowerOfTwo(rows);
            return new Vector2(u, v);
        }

        public static Color Color(PC c) => Colors[(int)c];

        static bool generated;

        public static Texture2D Texture()
        {
            if (generated)
            {
                var cached = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
                if (cached != null) return cached;
            }

            generated = true;
            int rows = Mathf.NextPowerOfTwo((Colors.Length + Columns - 1) / Columns);
            int w = Columns * Cell, h = rows * Cell;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int row = rows - 1 - y / Cell;
                int i = row * Columns + x / Cell;
                px[y * w + x] = i < Colors.Length ? Colors[i] : UnityEngine.Color.magenta;
            }

            var tex = ArtMaterials.SaveTexture(TexPath, w, px, false, false, false, w);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexPath);
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
        }

        /// <summary>The shared palette material (matte paint).</summary>
        public static Material Material => Make("ToyPalette", 0.32f, 0.05f);

        /// <summary>Palette material for metal props (glossier).</summary>
        public static Material MetalMaterial => Make("ToyPaletteMetal", 0.55f, 0.3f);

        static Material Make(string name, float smoothness, float metallic)
        {
            var m = ArtMaterials.Mat(name, UnityEngine.Color.white, smoothness, metallic, ArtMaterials.Detail.Plain);
            m.SetTexture("_BaseMap", Texture());
            m.SetTextureScale("_BaseMap", Vector2.one);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}

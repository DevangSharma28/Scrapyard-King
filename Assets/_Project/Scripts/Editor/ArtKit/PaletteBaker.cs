using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ScrapYardKing.EditorTools.Art
{
    /// <summary>
    /// Mobile draw-call pass for multi-material models. Every flat-colour material on a renderer is merged into a single
    /// submesh that samples one shared palette texture (colour in RGB, smoothness in alpha, glow in an emission map), so
    /// a renderer goes from up to a dozen submeshes to one or two. Materials that carry a real texture (hazard stripes,
    /// bricks, belts) or are swapped per instance stay as their own submeshes.
    /// </summary>
    public sealed class PaletteBaker
    {
        const int Columns = 8, Rows = 8, Cell = 8;

        readonly string name;
        readonly HashSet<string> keep;
        readonly Dictionary<Material, int> cells = new();
        readonly Dictionary<string, Color> colorOverrides;
        Material material;

        public PaletteBaker(string name, IEnumerable<string> keepMaterials, Dictionary<string, Color> colorOverrides = null)
        {
            this.name = name;
            keep = new HashSet<string>(keepMaterials ?? Enumerable.Empty<string>());
            this.colorOverrides = colorOverrides ?? new Dictionary<string, Color>();
        }

        string TexPath => $"{ArtAssets.TexDir}/T_{name}.png";
        string EmiPath => $"{ArtAssets.TexDir}/T_{name}_E.png";

        public Material Material
        {
            get
            {
                if (material != null) return material;
                string path = $"{ArtAssets.MatDir}/M_{name}.mat";
                material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_" + name };
                    AssetDatabase.CreateAsset(material, path);
                }

                return material;
            }
        }

        Vector2 Uv(int cell) => new((cell % Columns + 0.5f) / Columns, 1f - (cell / Columns + 0.5f) / Rows);

        int CellOf(Material m)
        {
            if (cells.TryGetValue(m, out int c)) return c;
            c = cells.Count;
            if (c >= Columns * Rows)
            {
                Debug.LogWarning($"[PaletteBaker] {name}: palette full, reusing the last cell for {m.name}");
                c = Columns * Rows - 1;
            }

            cells[m] = c;
            return c;
        }

        /// <summary>Converts every MeshRenderer under <paramref name="root"/>; meshes are re-saved in <paramref name="container"/>.</summary>
        public void Convert(GameObject root, string container)
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                if (!AssetDatabase.GetAssetPath(mesh).Contains("/Meshes/Toy/")) continue;
                var mats = r.sharedMaterials;
                if (mats.Length != mesh.subMeshCount) continue;
                if (mats.All(m => m == null || keep.Contains(m.name) || m == Material)) continue;

                var uvs = new List<Vector2>();
                mesh.GetUVs(0, uvs);
                var merged = new List<int>();
                var keptSubs = new List<(Material mat, int[] tris)>();
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var m = mats[s];
                    var tris = mesh.GetTriangles(s);
                    if (m == null || keep.Contains(m.name))
                    {
                        keptSubs.Add((m, tris));
                        continue;
                    }

                    var uv = Uv(CellOf(m));
                    foreach (int idx in tris) uvs[idx] = uv;
                    merged.AddRange(tris);
                }

                var outMesh = new Mesh { name = mesh.name, indexFormat = mesh.indexFormat };
                outMesh.SetVertices(mesh.vertices);
                outMesh.SetNormals(mesh.normals);
                outMesh.SetUVs(0, uvs);
                var newMats = new List<Material>();
                var subs = new List<int[]>();
                if (merged.Count > 0)
                {
                    subs.Add(merged.ToArray());
                    newMats.Add(Material);
                }

                foreach (var (mat, tris) in keptSubs)
                {
                    subs.Add(tris);
                    newMats.Add(mat);
                }

                outMesh.subMeshCount = subs.Count;
                for (int i = 0; i < subs.Count; i++) outMesh.SetTriangles(subs[i], i, false);
                outMesh.RecalculateBounds();
                var saved = ArtAssets.SaveMesh(container, mesh.name, outMesh);
                mf.sharedMesh = saved;
                r.sharedMaterials = newMats.ToArray();
            }
        }

        /// <summary>Writes the palette textures and finishes the material. Call once after every Convert.</summary>
        public void Save()
        {
            int w = Columns * Cell, h = Rows * Cell;
            var albedo = new Color[w * h];
            var glow = new Color[w * h];
            for (int i = 0; i < albedo.Length; i++)
            {
                albedo[i] = new Color(1f, 0f, 1f, 0.3f);
                glow[i] = Color.black;
            }

            foreach (var (m, cell) in cells)
            {
                var c = colorOverrides.TryGetValue(m.name, out var o) ? o : m.GetColor("_BaseColor");
                float smooth = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : 0.3f;
                var e = m.IsKeywordEnabled("_EMISSION") ? m.GetColor("_EmissionColor") : Color.black;
                int cx = cell % Columns, cy = Rows - 1 - cell / Columns;
                for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    int i = (cy * Cell + y) * w + cx * Cell + x;
                    albedo[i] = new Color(c.r, c.g, c.b, smooth);
                    glow[i] = new Color(Mathf.Clamp01(e.r), Mathf.Clamp01(e.g), Mathf.Clamp01(e.b), 1f);
                }
            }

            var tex = Write(TexPath, w, albedo);
            var emi = Write(EmiPath, w, glow);
            var mat = Material;
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_Metallic", 0.05f);
            mat.SetFloat("_SmoothnessTextureChannel", 1f);
            mat.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            mat.SetTexture("_EmissionMap", emi);
            mat.SetColor("_EmissionColor", Color.white);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
        }

        static Texture2D Write(string path, int width, Color[] pixels)
        {
            ArtMaterials.SaveTexture(path, width, pixels, false, false, false, width);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}

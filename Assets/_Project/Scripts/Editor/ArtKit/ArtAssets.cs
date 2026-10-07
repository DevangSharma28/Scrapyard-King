using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ScrapYardKing.EditorTools.Art
{
    /// <summary>
    /// Asset IO for the art builders. Meshes live as named sub-assets of one container per model
    /// (<c>Art/Meshes/Toy/&lt;Model&gt;.asset</c>) and are updated in place, so prefabs keep their references and GUIDs.
    /// </summary>
    public static class ArtAssets
    {
        public const string Root = "Assets/_Project";
        public const string MeshDir = Root + "/Art/Meshes/Toy";
        public const string MatDir = Root + "/Art/Materials/Toy";
        public const string TexDir = Root + "/Art/Textures/Toy";

        /// <summary>Stores <paramref name="kit"/> as sub-asset <paramref name="part"/> of container <paramref name="model"/>.</summary>
        public static Mesh SaveMesh(string model, string part, MeshKit kit, int slotCount = -1) =>
            SaveMesh(model, part, kit.ToMesh(part, slotCount));

        public static Mesh SaveMesh(string model, string part, Mesh mesh)
        {
            Directory.CreateDirectory(MeshDir);
            string path = $"{MeshDir}/{model}.asset";
            mesh.name = part;
            var container = AssetDatabase.LoadMainAssetAtPath(path);
            if (container == null)
            {
                // The first mesh becomes the main asset; later parts are added next to it.
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            var existing = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault(m => m.name == part);
            if (existing == null)
            {
                AssetDatabase.AddObjectToAsset(mesh, path);
                EditorUtility.SetDirty(container);
                return mesh;
            }

            Copy(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>Copies buffers explicitly (CopySerialized can leave stale GPU data on screen until a reimport).</summary>
        public static void Copy(Mesh from, Mesh to)
        {
            to.Clear();
            to.indexFormat = from.indexFormat;
            to.SetVertices(from.vertices);
            to.SetNormals(from.normals);
            to.SetUVs(0, from.uv);
            to.subMeshCount = from.subMeshCount;
            for (int i = 0; i < from.subMeshCount; i++) to.SetTriangles(from.GetTriangles(i), i, false);
            if (from.bindposes != null && from.bindposes.Length > 0)
            {
                to.boneWeights = from.boneWeights;
                to.bindposes = from.bindposes;
            }

            to.RecalculateBounds();
        }

        /// <summary>
        /// Bakes <paramref name="kit"/> (slots index <paramref name="table"/>) as sub-asset <paramref name="part"/> of
        /// <paramref name="model"/> and adds it as a child renderer with only the materials it uses.
        /// </summary>
        public static GameObject Part(string model, string part, MeshKit kit, Material[] table, Transform parent, Vector3 localPosition = default,
            Vector3 localEuler = default, bool castShadows = true, string objectName = null)
        {
            var mesh = SaveMesh(model, part, kit.ToMeshCompact(part, out var used));
            var mats = new Material[Mathf.Max(1, used.Count)];
            for (int i = 0; i < used.Count; i++) mats[i] = table[used[i]];
            return MeshObject(objectName ?? part, parent, mesh, mats, localPosition, localEuler, castShadows);
        }

        /// <summary>Creates a child with a mesh renderer showing <paramref name="mesh"/>.</summary>
        public static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material[] materials, Vector3 localPosition = default,
            Vector3 localEuler = default, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = Fit(materials, mesh.subMeshCount);
            r.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Pads or trims a material array to a submesh count.</summary>
        public static Material[] Fit(Material[] materials, int count)
        {
            var result = new Material[Mathf.Max(1, count)];
            for (int i = 0; i < result.Length; i++) result[i] = materials.Length == 0 ? null : materials[Mathf.Min(i, materials.Length - 1)];
            return result;
        }

        public static GameObject SavePrefab(GameObject go, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
            Object.DestroyImmediate(go);
            if (!ok) Debug.LogError("[ArtAssets] failed to save " + path);
            return prefab;
        }

        public static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }

            return null;
        }

        public static Bounds RendererBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Sets serialized fields by name (private [SerializeField]s included).</summary>
        public static void Set(Object target, params (string field, object value)[] values)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in values)
            {
                var p = so.FindProperty(field);
                if (p == null)
                {
                    Debug.LogWarning($"[ArtAssets] {target.GetType().Name}.{field} not found");
                    continue;
                }

                switch (value)
                {
                    case Object o: p.objectReferenceValue = o; break;
                    case float f: p.floatValue = f; break;
                    case int i when p.propertyType == SerializedPropertyType.Enum: p.enumValueIndex = i; break;
                    case int i: p.intValue = i; break;
                    case bool b: p.boolValue = b; break;
                    case string s: p.stringValue = s; break;
                    case Color c: p.colorValue = c; break;
                    case Vector2 v2: p.vector2Value = v2; break;
                    case Vector3 v3: p.vector3Value = v3; break;
                    case null: p.objectReferenceValue = null; break;
                    default: Debug.LogWarning($"[ArtAssets] unsupported value for {field}"); break;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        public static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogWarning($"[ArtAssets] {target.GetType().Name}.{field} not found");
                return;
            }

            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }
}

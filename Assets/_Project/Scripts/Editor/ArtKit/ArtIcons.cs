using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScrapYardKing.EditorTools.Art
{
    /// <summary>Renders a model into a transparent 256 px sprite (upgrade cards, tiles, bubbles) in an isolated preview scene.</summary>
    public static class ArtIcons
    {
        public const string IconDir = ArtAssets.Root + "/Art/Icons";

        /// <param name="pose">Optional: called on the instance before rendering (sample an animation clip, hide parts).</param>
        public static Sprite Render(string name, GameObject source, Vector3 viewEuler, Action<GameObject> pose = null, float zoom = 0.92f)
        {
            const int size = 256;
            var preview = EditorSceneManager.NewPreviewScene();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source) ?? UnityEngine.Object.Instantiate(source);
            if (PrefabUtility.IsPartOfPrefabInstance(instance)) PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            SceneManager.MoveGameObjectToScene(instance, preview);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.SetActive(true);
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
            foreach (var c in instance.GetComponentsInChildren<Canvas>(true)) c.gameObject.SetActive(false);
            pose?.Invoke(instance);

            var renderers = instance.GetComponentsInChildren<Renderer>()
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && r is MeshRenderer or SkinnedMeshRenderer).ToArray();
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            var camGo = new GameObject("IconCamera");
            SceneManager.MoveGameObjectToScene(camGo, preview);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = preview;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.orthographic = true;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 200f;
            var rot = Quaternion.Euler(viewEuler);
            float radius = bounds.extents.magnitude;
            camGo.transform.SetPositionAndRotation(bounds.center - rot * Vector3.forward * (radius * 3f), rot);
            cam.orthographicSize = radius * zoom;

            AddLight(preview, Quaternion.Euler(48f, viewEuler.y - 40f, 0f), 1.35f, new Color(1f, 0.96f, 0.88f));
            AddLight(preview, Quaternion.Euler(18f, viewEuler.y + 140f, 0f), 0.65f, new Color(0.75f, 0.85f, 1f));

            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            EditorSceneManager.ClosePreviewScene(preview);

            Directory.CreateDirectory(IconDir);
            string path = $"{IconDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static void AddLight(Scene scene, Quaternion rotation, float intensity, Color color)
        {
            var go = new GameObject("IconLight");
            SceneManager.MoveGameObjectToScene(go, scene);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            go.transform.rotation = rotation;
        }

        /// <summary>Poses a toy rig on its first Animator with <paramref name="clip"/> at <paramref name="time01"/>.</summary>
        public static Action<GameObject> Pose(AnimationClip clip, float time01 = 0.3f) => go =>
        {
            var animator = go.GetComponentInChildren<Animator>();
            if (animator != null && clip != null) clip.SampleAnimation(animator.gameObject, clip.length * time01);
        };
    }
}

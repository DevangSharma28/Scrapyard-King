using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ScrapYardKing.EditorTools.Art
{
    /// <summary>Material slots of a toy character (index into <see cref="ToySpec.Table"/>).</summary>
    public enum CS
    {
        Skin, Shirt, Pants, Boots, Gloves, Eye, White, Mouth, Hair, Hat, HatAccent, Vest, Stripe, Pack, PackDark, Steel, Charcoal, Blush, Apron,
        Lens, Belt, Count
    }

    public enum HatStyle { HardHat, Cap, Beanie, None }
    public enum HairStyle { Short, Long, Bun, Bald }
    public enum BackStyle { None, UtilityPack, CarryRack }

    /// <summary>Look of one toy character: materials per slot plus a few shape and accessory switches.</summary>
    public sealed class ToySpec
    {
        public string Name;
        public readonly Material[] Table = new Material[(int)CS.Count];
        public HatStyle Hat = HatStyle.HardHat;
        public HairStyle Hair = HairStyle.Short;
        public BackStyle Back = BackStyle.None;
        public bool Vest = true, Apron, EarMuffs, Goggles, Mustache, Beard, Blush = true, Pouch, Scarf, Sleeves = true;
        /// <summary>Torso width multiplier (broad shoulders &gt; 1).</summary>
        public float Bulk = 1f;
        /// <summary>Extra belly depth in metres.</summary>
        public float Belly;
        public float HeadScale = 1f;

        public Material this[CS slot]
        {
            get => Table[(int)slot];
            set => Table[(int)slot] = value;
        }
    }

    /// <summary>Joint transforms of a built rig (all names are fixed: the shared animation clips address them by path).</summary>
    public sealed class ToyRigParts
    {
        public GameObject Model;
        public Transform Hips, Torso, Head, ArmL, ArmR, ForearmL, ForearmR, HandL, HandR, LegL, LegR, ShinL, ShinR, Back;
    }

    /// <summary>
    /// Segmented "toy" characters: chunky rigid body parts on a small joint hierarchy, animated by generic clips generated
    /// here (idle / walk / run / tool hold / cut / cheer). Proportions: oversized head and hard hat, broad shoulders, big
    /// gloves, short legs, chunky boots. Joint positions are identical for every character (only part sizes change), so
    /// one clip set serves the player, workers and customers.
    /// </summary>
    public static class ToyRig
    {
        public const string Hips = "Hips";
        public const string Torso = "Hips/Torso";
        public const string Head = Torso + "/Head";
        public const string ArmL = Torso + "/ArmL";
        public const string ArmR = Torso + "/ArmR";
        public const string ForearmL = ArmL + "/ForearmL";
        public const string ForearmR = ArmR + "/ForearmR";
        public const string LegL = Hips + "/LegL";
        public const string LegR = Hips + "/LegR";
        public const string ShinL = LegL + "/ShinL";
        public const string ShinR = LegR + "/ShinR";

        public const float LegSegment = 0.26f;
        public const float FootDrop = 0.105f;
        public static readonly float HipJoint = LegSegment * 2f + FootDrop;
        public static readonly float HipsY = HipJoint + 0.02f;
        const float TorsoY = 0.12f;
        const float NeckY = 0.52f;
        const float ShoulderY = 0.44f;
        const float UpperArm = 0.24f;

        public const string AnimDir = ArtAssets.Root + "/Art/Animation/Toy";

        // =====================================================================================
        // Build
        // =====================================================================================

        /// <summary>Builds the rig under <paramref name="parent"/> as "Model" (with an Animator using <paramref name="controller"/>).</summary>
        public static ToyRigParts Build(ToySpec spec, Transform parent, RuntimeAnimatorController controller)
        {
            // Characters are hero assets: allow rounder (2-segment) boxes; props and machines stay on cheap chamfers.
            int previousSegments = MeshKit.MaxSegments;
            float previousDetail = MeshKit.RoundDetail;
            MeshKit.MaxSegments = 2;
            MeshKit.RoundDetail = 0.7f;
            try
            {
                return BuildRig(spec, parent, controller);
            }
            finally
            {
                MeshKit.MaxSegments = previousSegments;
                MeshKit.RoundDetail = previousDetail;
            }
        }

        static ToyRigParts BuildRig(ToySpec spec, Transform parent, RuntimeAnimatorController controller)
        {
            var t = spec.Table;
            string model = "Char_" + spec.Name;
            float bw = spec.Bulk;
            var parts = new ToyRigParts();

            var root = new GameObject("Model");
            root.transform.SetParent(parent, false);
            parts.Model = root;
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            parts.Hips = Joint("Hips", root.transform, new Vector3(0f, HipsY, 0f));
            ArtAssets.Part(model, "Pelvis", Pelvis(spec), t, parts.Hips);

            parts.Torso = Joint("Torso", parts.Hips, new Vector3(0f, TorsoY, 0f));
            ArtAssets.Part(model, "Torso", TorsoKit(spec), t, parts.Torso);

            parts.Head = Joint("Head", parts.Torso, new Vector3(0f, NeckY, 0.02f));
            ArtAssets.Part(model, "Head", HeadKit(spec), t, parts.Head);

            float shoulderX = 0.33f * bw;
            parts.ArmL = Joint("ArmL", parts.Torso, new Vector3(-shoulderX, ShoulderY, 0f));
            parts.ArmR = Joint("ArmR", parts.Torso, new Vector3(shoulderX, ShoulderY, 0f));
            ArtAssets.Part(model, "UpperArm", UpperArmKit(spec), t, parts.ArmL, objectName: "UpperArm");
            ArtAssets.Part(model, "UpperArm", UpperArmKit(spec), t, parts.ArmR, objectName: "UpperArm");
            parts.ForearmL = Joint("ForearmL", parts.ArmL, new Vector3(0f, -UpperArm, 0f));
            parts.ForearmR = Joint("ForearmR", parts.ArmR, new Vector3(0f, -UpperArm, 0f));
            ArtAssets.Part(model, "ForearmL", ForearmKit(spec, -1), t, parts.ForearmL, objectName: "Forearm");
            ArtAssets.Part(model, "ForearmR", ForearmKit(spec, 1), t, parts.ForearmR, objectName: "Forearm");
            parts.HandL = Joint("HandL", parts.ForearmL, new Vector3(0f, -0.23f, 0.03f));
            parts.HandR = Joint("HandR", parts.ForearmR, new Vector3(0f, -0.23f, 0.03f));

            parts.LegL = Joint("LegL", parts.Hips, new Vector3(-0.115f * Mathf.Lerp(1f, bw, 0.5f), -0.02f, 0f));
            parts.LegR = Joint("LegR", parts.Hips, new Vector3(0.115f * Mathf.Lerp(1f, bw, 0.5f), -0.02f, 0f));
            ArtAssets.Part(model, "Thigh", ThighKit(spec), t, parts.LegL, objectName: "Thigh");
            ArtAssets.Part(model, "Thigh", ThighKit(spec), t, parts.LegR, objectName: "Thigh");
            parts.ShinL = Joint("ShinL", parts.LegL, new Vector3(0f, -LegSegment, 0f));
            parts.ShinR = Joint("ShinR", parts.LegR, new Vector3(0f, -LegSegment, 0f));
            ArtAssets.Part(model, "Shin", ShinKit(spec), t, parts.ShinL, objectName: "Shin");
            ArtAssets.Part(model, "Shin", ShinKit(spec), t, parts.ShinR, objectName: "Shin");

            parts.Back = Joint("Back", parts.Torso, new Vector3(0f, 0.28f, -0.2f));
            if (spec.Back != BackStyle.None) ArtAssets.Part(model, "Back", BackKit(spec), t, parts.Back);

            Skin(spec, parts, model);
            return parts;
        }

        /// <summary>
        /// Mobile budget: bakes every rigid body part into one skinned mesh (each vertex weighted fully to the joint it
        /// hangs from) drawn with one per-character palette material: colour cells in RGB, smoothness in alpha, glow in an
        /// emission map. The joint hierarchy and the generated clips are unchanged; a character costs one draw call.
        /// </summary>
        static void Skin(ToySpec spec, ToyRigParts parts, string model)
        {
            var root = parts.Model.transform;
            var bones = new List<Transform> { parts.Hips, parts.Torso, parts.Head, parts.ArmL, parts.ForearmL, parts.ArmR, parts.ForearmR, parts.LegL, parts.ShinL,
                parts.LegR, parts.ShinR, parts.Back };
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var weights = new List<BoneWeight>();
            var tris = new List<int>();
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf.sharedMesh;
                int bone = bones.IndexOf(r.transform.parent);
                if (bone < 0) bone = 0;
                var toRoot = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var nrm = toRoot.inverse.transpose;
                var mv = mesh.vertices;
                var mn = mesh.normals;
                int offset = verts.Count;
                for (int i = 0; i < mv.Length; i++)
                {
                    verts.Add(toRoot.MultiplyPoint3x4(mv[i]));
                    normals.Add(nrm.MultiplyVector(mn[i]).normalized);
                    uvs.Add(Vector2.zero);
                    weights.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1f });
                }

                var mats = r.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int slot = Array.IndexOf(spec.Table, mats[Mathf.Min(sub, mats.Length - 1)]);
                    if (slot < 0) slot = (int)CS.Charcoal;
                    var cell = PaletteUv(slot);
                    var subTris = mesh.GetTriangles(sub);
                    foreach (int idx in subTris) uvs[offset + idx] = cell;
                    foreach (int idx in subTris) tris.Add(offset + idx);
                }

                Object.DestroyImmediate(r.gameObject);
            }

            var skinMesh = new Mesh { name = "Skin" };
            skinMesh.SetVertices(verts);
            skinMesh.SetNormals(normals);
            skinMesh.SetUVs(0, uvs);
            skinMesh.SetTriangles(tris, 0);
            skinMesh.boneWeights = weights.ToArray();
            var skinGo = new GameObject("Skin");
            skinGo.transform.SetParent(root, false);
            skinMesh.bindposes = bones.Select(b => b.worldToLocalMatrix * skinGo.transform.localToWorldMatrix).ToArray();
            skinMesh.RecalculateBounds();
            var saved = ArtAssets.SaveMesh(model, "Skin", skinMesh);
            var smr = skinGo.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = saved;
            smr.bones = bones.ToArray();
            smr.rootBone = parts.Hips;
            smr.sharedMaterial = PaletteMaterial(spec, model);
            smr.localBounds = new Bounds(new Vector3(0f, 0.3f, 0f), new Vector3(1.6f, 2.6f, 1.4f));
            smr.quality = SkinQuality.Bone1;
            smr.updateWhenOffscreen = false;
        }

        const int PaletteColumns = 8, PaletteRows = 4, PaletteCell = 8;

        static Vector2 PaletteUv(int slot) =>
            new((slot % PaletteColumns + 0.5f) / PaletteColumns, 1f - (slot / PaletteColumns + 0.5f) / PaletteRows);

        static Material PaletteMaterial(ToySpec spec, string model)
        {
            int w = PaletteColumns * PaletteCell, h = PaletteRows * PaletteCell;
            var albedo = new Color[w * h];
            var glow = new Color[w * h];
            for (int slot = 0; slot < PaletteColumns * PaletteRows; slot++)
            {
                var m = slot < spec.Table.Length ? spec.Table[slot] : null;
                var c = m != null ? m.GetColor("_BaseColor") : Color.magenta;
                float smooth = m != null ? m.GetFloat("_Smoothness") : 0.3f;
                var e = m != null && m.IsKeywordEnabled("_EMISSION") ? m.GetColor("_EmissionColor") : Color.black;
                int cx = slot % PaletteColumns, cy = PaletteRows - 1 - slot / PaletteColumns;
                for (int y = 0; y < PaletteCell; y++)
                for (int x = 0; x < PaletteCell; x++)
                {
                    int i = (cy * PaletteCell + y) * w + cx * PaletteCell + x;
                    albedo[i] = new Color(c.r, c.g, c.b, smooth);
                    glow[i] = new Color(Mathf.Clamp01(e.r), Mathf.Clamp01(e.g), Mathf.Clamp01(e.b), 1f);
                }
            }

            var tex = SaveRaw($"{ArtAssets.TexDir}/T_{model}.png", w, albedo);
            var emi = SaveRaw($"{ArtAssets.TexDir}/T_{model}_E.png", w, glow);
            string path = $"{ArtAssets.MatDir}/M_{model}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_" + model };
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_SmoothnessTextureChannel", 1f);
            mat.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            mat.SetTexture("_EmissionMap", emi);
            mat.SetColor("_EmissionColor", Color.white);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Texture2D SaveRaw(string path, int width, Color[] pixels)
        {
            var tex = ArtMaterials.SaveTexture(path, width, pixels, false, false, false, width);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Transform Joint(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        // ---------- body parts ----------

        static MeshKit Pelvis(ToySpec s)
        {
            float bw = Mathf.Lerp(1f, s.Bulk, 0.6f);
            var k = new MeshKit();
            k.Box((int)CS.Pants, new Vector3(0f, 0.03f, 0f), new Vector3(0.42f * bw, 0.2f, 0.28f + s.Belly * 0.5f), 0.08f, default, 2);
            k.Box((int)CS.Belt, new Vector3(0f, 0.12f, 0f), new Vector3(0.44f * bw, 0.06f, 0.3f + s.Belly * 0.5f), 0.03f);
            k.Box((int)CS.Steel, new Vector3(0f, 0.12f, 0.155f + s.Belly * 0.25f), new Vector3(0.08f, 0.055f, 0.02f), 0.01f);
            if (s.Apron)
            {
                k.Box((int)CS.Apron, new Vector3(0f, -0.06f, 0.155f + s.Belly * 0.5f), new Vector3(0.36f * bw, 0.26f, 0.03f), 0.012f, new Vector3(-6f, 0f, 0f));
                k.Box((int)CS.Apron, new Vector3(0.07f, -0.04f, 0.175f + s.Belly * 0.5f), new Vector3(0.12f, 0.08f, 0.02f), 0.008f, new Vector3(-6f, 0f, 0f));
            }

            if (s.Pouch)
            {
                k.Box((int)CS.PackDark, new Vector3(0.2f * bw, 0.04f, 0.06f), new Vector3(0.08f, 0.13f, 0.14f), 0.03f);
                k.Box((int)CS.Pack, new Vector3(0.205f * bw, 0.09f, 0.06f), new Vector3(0.085f, 0.04f, 0.145f), 0.015f);
            }

            return k;
        }

        static MeshKit TorsoKit(ToySpec s)
        {
            float bw = s.Bulk;
            var k = new MeshKit();
            k.Box((int)CS.Shirt, new Vector3(0f, 0.3f, 0f), new Vector3(0.56f * bw, 0.42f, 0.34f + s.Belly * 0.4f), 0.13f, default, 2);
            k.Box((int)CS.Shirt, new Vector3(0f, 0.11f, 0.01f + s.Belly * 0.25f), new Vector3(0.48f * bw, 0.24f, 0.31f + s.Belly), 0.1f, default, 2);
            for (int side = -1; side <= 1; side += 2)
                k.Sphere((int)CS.Shirt, new Vector3(side * 0.27f * bw, 0.43f, 0f), new Vector3(0.12f, 0.11f, 0.13f), 12, 8);
            k.Cylinder((int)CS.Skin, new Vector3(0f, 0.53f, 0.01f), 0.085f, 0.09f, 12, default, 0.01f);
            k.Box((int)CS.Shirt, new Vector3(0f, 0.5f, 0f), new Vector3(0.24f, 0.05f, 0.21f), 0.025f);

            float front = 0.17f + s.Belly * 0.2f;
            if (s.Vest)
            {
                // Two front panels (shirt shows in the gap), back panel, side panels, shoulder straps, reflective bands.
                for (int side = -1; side <= 1; side += 2)
                {
                    k.Box((int)CS.Vest, new Vector3(side * 0.13f * bw, 0.25f, front), new Vector3(0.2f * bw, 0.44f, 0.055f), 0.025f);
                    k.Box((int)CS.Vest, new Vector3(side * 0.285f * bw, 0.25f, 0f), new Vector3(0.05f, 0.38f, 0.3f), 0.02f);
                    k.Box((int)CS.Vest, new Vector3(side * 0.17f * bw, 0.49f, 0f), new Vector3(0.11f, 0.05f, 0.37f), 0.02f);
                    foreach (float y in new[] { 0.14f, 0.3f })
                    {
                        k.Box((int)CS.Stripe, new Vector3(side * 0.13f * bw, y, front + 0.03f), new Vector3(0.2f * bw, 0.045f, 0.012f), 0.005f);
                        k.Box((int)CS.Stripe, new Vector3(side * 0.31f * bw, y, 0f), new Vector3(0.012f, 0.045f, 0.3f), 0.005f);
                    }
                }

                k.Box((int)CS.Vest, new Vector3(0f, 0.27f, -0.17f), new Vector3(0.53f * bw, 0.46f, 0.055f), 0.025f);
                foreach (float y in new[] { 0.14f, 0.3f })
                    k.Box((int)CS.Stripe, new Vector3(0f, y, -0.2f), new Vector3(0.53f * bw, 0.045f, 0.012f), 0.005f);
                // Chest pocket with a pen.
                k.Box((int)CS.Vest, new Vector3(-0.13f * bw, 0.38f, front + 0.035f), new Vector3(0.1f, 0.07f, 0.02f), 0.01f);
                k.Box((int)CS.Steel, new Vector3(-0.15f * bw, 0.42f, front + 0.045f), new Vector3(0.015f, 0.05f, 0.015f), 0.005f);
            }

            if (s.Apron)
            {
                // Bib and neck straps on the torso; the skirt hangs from the pelvis so it moves with the hips.
                k.Box((int)CS.Apron, new Vector3(0f, 0.22f, front + 0.012f), new Vector3(0.3f * bw, 0.34f, 0.03f), 0.014f);
                for (int side = -1; side <= 1; side += 2)
                    k.Box((int)CS.Apron, new Vector3(side * 0.1f, 0.47f, 0.02f), new Vector3(0.035f, 0.03f, 0.34f), 0.01f, new Vector3(0f, side * -8f, 0f));
                k.Box((int)CS.Belt, new Vector3(0f, 0.06f, 0f), new Vector3(0.5f * bw, 0.04f, 0.33f + s.Belly), 0.015f);
            }

            if (s.Scarf) k.Torus((int)CS.HatAccent, new Vector3(0f, 0.52f, 0.01f), 0.11f, 0.045f, 14, 8);
            return k;
        }

        static MeshKit HeadKit(ToySpec s)
        {
            float hs = s.HeadScale;
            var k = new MeshKit();
            k.Push(Vector3.zero, default, Vector3.one * hs);
            k.Sphere((int)CS.Skin, new Vector3(0f, 0.22f, 0.01f), new Vector3(0.25f, 0.24f, 0.23f), 18, 12);
            for (int side = -1; side <= 1; side += 2)
            {
                k.Sphere((int)CS.Skin, new Vector3(side * 0.245f, 0.2f, 0f), new Vector3(0.045f, 0.065f, 0.04f), 10, 6);
                // Cartoon eyes: tall black ovals with a white glint, brows above.
                k.Sphere((int)CS.Eye, new Vector3(side * 0.085f, 0.235f, 0.218f), new Vector3(0.032f, 0.047f, 0.02f), 10, 8);
                k.Sphere((int)CS.White, new Vector3(side * 0.085f - 0.01f, 0.252f, 0.234f), new Vector3(0.011f, 0.013f, 0.008f), 6, 4);
                k.Box((int)CS.Hair, new Vector3(side * 0.088f, 0.302f, 0.212f), new Vector3(0.075f, 0.022f, 0.025f), 0.01f, new Vector3(0f, 0f, side * -8f));
                if (s.Blush) k.Sphere((int)CS.Blush, new Vector3(side * 0.15f, 0.16f, 0.19f), new Vector3(0.045f, 0.028f, 0.02f), 8, 5);
            }

            k.Sphere((int)CS.Skin, new Vector3(0f, 0.18f, 0.235f), new Vector3(0.048f, 0.042f, 0.04f), 10, 6);
            k.Torus((int)CS.Mouth, new Vector3(0f, 0.16f, 0.214f), 0.05f, 0.011f, 10, 6, new Vector3(-90f, 0f, -8f), null, 110f, 215f);
            if (s.Mustache)
                for (int side = -1; side <= 1; side += 2)
                    k.Sphere((int)CS.Hair, new Vector3(side * 0.04f, 0.128f, 0.226f), new Vector3(0.05f, 0.022f, 0.022f), 8, 5, new Vector3(0f, 0f, side * 12f));
            if (s.Beard) k.Dome((int)CS.Hair, new Vector3(0f, 0.12f, 0.03f), new Vector3(0.235f, -0.13f, 0.22f), 16, 5, default, false);

            Hair(k, s);
            Hat(k, s);
            k.Pop();
            return k;
        }

        static void Hair(MeshKit k, ToySpec s)
        {
            if (s.Hair == HairStyle.Bald) return;
            // Hair: a crown cap whose front edge is the hairline (above the brows), plus a band round the back and sides
            // that leaves the face open.
            k.Dome((int)CS.Hair, new Vector3(0f, 0.215f, -0.01f), new Vector3(0.262f, 0.262f, 0.25f), 18, 4, new Vector3(-22f, 0f, 0f), false, 30f);
            k.Dome((int)CS.Hair, new Vector3(0f, 0.215f, -0.01f), new Vector3(0.262f, 0.262f, 0.25f), 18, 4, default, false, -28f, 40f, 250f, 145f);
            if (s.Hair == HairStyle.Long)
                k.Box((int)CS.Hair, new Vector3(0f, 0.08f, -0.13f), new Vector3(0.46f, 0.3f, 0.14f), 0.07f, default, 2);
            if (s.Hair == HairStyle.Bun)
                k.Sphere((int)CS.Hair, new Vector3(0f, 0.43f, -0.14f), new Vector3(0.1f, 0.1f, 0.1f), 12, 8);
        }

        static void Hat(MeshKit k, ToySpec s)
        {
            switch (s.Hat)
            {
                case HatStyle.HardHat:
                    // Oversized: the helmet is the character's silhouette from the top-down camera.
                    k.Dome((int)CS.Hat, new Vector3(0f, 0.28f, 0f), new Vector3(0.305f, 0.27f, 0.315f), 20, 7);
                    k.Push(new Vector3(0f, 0.285f, 0.035f), default, new Vector3(1f, 1f, 1.17f));
                    k.Cylinder((int)CS.Hat, Vector3.zero, 0.335f, 0.04f, 22, default, 0.016f);
                    k.Pop();
                    k.Box((int)CS.Hat, new Vector3(0f, 0.545f, -0.01f), new Vector3(0.085f, 0.06f, 0.4f), 0.03f, default, 2);
                    k.Box((int)CS.HatAccent, new Vector3(0f, 0.43f, 0.27f), new Vector3(0.11f, 0.07f, 0.02f), 0.01f, new Vector3(-28f, 0f, 0f));
                    if (s.EarMuffs)
                        for (int side = -1; side <= 1; side += 2)
                        {
                            k.Cylinder((int)CS.Charcoal, new Vector3(side * 0.27f, 0.2f, 0f), 0.08f, 0.07f, 14, new Vector3(0f, 0f, 90f), 0.02f);
                            k.Cylinder((int)CS.HatAccent, new Vector3(side * 0.31f, 0.2f, 0f), 0.05f, 0.02f, 12, new Vector3(0f, 0f, 90f), 0.005f);
                        }

                    if (s.Goggles)
                    {
                        k.Torus((int)CS.Charcoal, new Vector3(0f, 0.34f, 0.0f), 0.3f, 0.018f, 24, 6, default, new Vector2(1f, 1.4f));
                        for (int side = -1; side <= 1; side += 2)
                        {
                            k.Cylinder((int)CS.Charcoal, new Vector3(side * 0.085f, 0.36f, 0.275f), 0.065f, 0.05f, 14, new Vector3(-62f, 0f, 0f), 0.012f);
                            k.Cylinder((int)CS.Lens, new Vector3(side * 0.085f, 0.375f, 0.295f), 0.05f, 0.02f, 14, new Vector3(-62f, 0f, 0f), 0.005f);
                        }
                    }

                    break;
                case HatStyle.Cap:
                    k.Dome((int)CS.Hat, new Vector3(0f, 0.27f, -0.005f), new Vector3(0.268f, 0.2f, 0.262f), 18, 6);
                    k.Box((int)CS.HatAccent, new Vector3(0f, 0.285f, 0.27f), new Vector3(0.3f, 0.03f, 0.2f), 0.014f, new Vector3(-10f, 0f, 0f));
                    k.Sphere((int)CS.HatAccent, new Vector3(0f, 0.465f, 0f), new Vector3(0.03f, 0.02f, 0.03f), 8, 4);
                    break;
                case HatStyle.Beanie:
                    k.Dome((int)CS.Hat, new Vector3(0f, 0.27f, -0.01f), new Vector3(0.27f, 0.25f, 0.265f), 18, 6);
                    k.Cylinder((int)CS.HatAccent, new Vector3(0f, 0.3f, -0.01f), 0.278f, 0.08f, 18, default, 0.025f);
                    k.Sphere((int)CS.HatAccent, new Vector3(0f, 0.53f, -0.02f), new Vector3(0.07f, 0.07f, 0.07f), 10, 6);
                    break;
            }
        }

        static MeshKit UpperArmKit(ToySpec s)
        {
            var k = new MeshKit();
            var cloth = s.Sleeves ? CS.Shirt : CS.Skin;
            k.Box((int)cloth, new Vector3(0f, -0.11f, 0f), new Vector3(0.165f, 0.25f, 0.165f), 0.075f, default, 2);
            if (s.Sleeves) k.Cylinder((int)CS.Shirt, new Vector3(0f, -0.205f, 0f), 0.088f, 0.05f, 12, default, 0.015f);
            return k;
        }

        static MeshKit ForearmKit(ToySpec s, int side)
        {
            var k = new MeshKit();
            k.Box((int)CS.Skin, new Vector3(0f, -0.075f, 0f), new Vector3(0.13f, 0.17f, 0.13f), 0.06f, default, 2);
            k.Cylinder((int)CS.Gloves, new Vector3(0f, -0.15f, 0.005f), 0.088f, 0.06f, 12, default, 0.02f);
            // Big work gloves: a fist block and a thumb on the inside.
            k.Box((int)CS.Gloves, new Vector3(0f, -0.225f, 0.015f), new Vector3(0.17f, 0.16f, 0.18f), 0.075f, default, 2);
            k.Sphere((int)CS.Gloves, new Vector3(-side * 0.075f, -0.205f, 0.075f), new Vector3(0.045f, 0.06f, 0.045f), 10, 6, new Vector3(20f, 0f, side * 25f));
            return k;
        }

        static MeshKit ThighKit(ToySpec s)
        {
            var k = new MeshKit();
            k.Box((int)CS.Pants, new Vector3(0f, -0.13f, 0f), new Vector3(0.2f, 0.29f, 0.21f), 0.085f, default, 2);
            return k;
        }

        static MeshKit ShinKit(ToySpec s)
        {
            var k = new MeshKit();
            k.Box((int)CS.Pants, new Vector3(0f, -0.105f, 0f), new Vector3(0.18f, 0.23f, 0.19f), 0.075f, default, 2);
            // Chunky boots with a cuff, toe cap and thick sole.
            k.Box((int)CS.Boots, new Vector3(0f, -0.265f, 0.035f), new Vector3(0.205f, 0.15f, 0.31f), 0.065f, default, 2);
            k.Cylinder((int)CS.Boots, new Vector3(0f, -0.2f, 0f), 0.103f, 0.06f, 14, default, 0.02f);
            k.Box((int)CS.Charcoal, new Vector3(0f, -LegSegment - FootDrop + 0.022f, 0.035f), new Vector3(0.2f, 0.044f, 0.305f), 0.018f);
            k.Box((int)CS.Belt, new Vector3(0f, -0.24f, 0.165f), new Vector3(0.12f, 0.03f, 0.02f), 0.008f);
            return k;
        }

        static MeshKit BackKit(ToySpec s)
        {
            var k = new MeshKit();
            if (s.Back == BackStyle.UtilityPack)
            {
                k.Box((int)CS.Pack, new Vector3(0f, 0f, -0.06f), new Vector3(0.36f, 0.36f, 0.15f), 0.06f, default, 2);
                k.Box((int)CS.PackDark, new Vector3(0f, 0.12f, -0.07f), new Vector3(0.37f, 0.12f, 0.16f), 0.04f);
                k.Box((int)CS.PackDark, new Vector3(0f, -0.06f, -0.145f), new Vector3(0.22f, 0.14f, 0.05f), 0.025f);
                for (int side = -1; side <= 1; side += 2)
                {
                    k.Box((int)CS.PackDark, new Vector3(side * 0.2f, -0.04f, -0.06f), new Vector3(0.06f, 0.2f, 0.11f), 0.025f);
                    k.Box((int)CS.PackDark, new Vector3(side * 0.13f, 0.15f, 0.12f), new Vector3(0.05f, 0.04f, 0.3f), 0.015f);
                }

                // Tools sticking out: a wrench handle and a coiled cable.
                k.Cylinder((int)CS.Steel, new Vector3(0.11f, 0.23f, -0.08f), 0.022f, 0.2f, 8, new Vector3(0f, 0f, -12f), 0.006f);
                k.Box((int)CS.Steel, new Vector3(0.135f, 0.34f, -0.08f), new Vector3(0.07f, 0.05f, 0.025f), 0.01f, new Vector3(0f, 0f, -12f));
                k.Torus((int)CS.HatAccent, new Vector3(-0.2f, 0.02f, -0.07f), 0.06f, 0.02f, 14, 6, new Vector3(0f, 0f, 90f));
            }
            else
            {
                // Carry rack: steel back frame with rails that the stack rides on.
                k.Box((int)CS.Steel, new Vector3(0f, 0f, -0.05f), new Vector3(0.36f, 0.42f, 0.04f), 0.015f);
                for (int side = -1; side <= 1; side += 2)
                {
                    k.Cylinder((int)CS.Charcoal, new Vector3(side * 0.19f, 0.02f, -0.06f), 0.025f, 0.5f, 8, default, 0.008f);
                    k.Box((int)CS.PackDark, new Vector3(side * 0.13f, 0.17f, 0.11f), new Vector3(0.05f, 0.04f, 0.3f), 0.015f);
                }

                k.Box((int)CS.Charcoal, new Vector3(0f, -0.21f, -0.14f), new Vector3(0.4f, 0.04f, 0.2f), 0.012f);
                k.Box((int)CS.HatAccent, new Vector3(0f, 0.1f, -0.075f), new Vector3(0.26f, 0.06f, 0.02f), 0.008f);
            }

            return k;
        }

        // =====================================================================================
        // Animation clips and controllers
        // =====================================================================================

        sealed class ClipWriter
        {
            public readonly AnimationClip Clip;
            readonly int samples;
            readonly bool loop;

            public ClipWriter(AnimationClip clip, int samples, bool loop)
            {
                Clip = clip;
                this.samples = samples;
                this.loop = loop;
            }

            /// <summary>Local euler rotation of <paramref name="path"/> as a function of normalized time.</summary>
            public void Rot(string path, Func<float, Vector3> euler)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    int a = axis;
                    Curve(path, "localEulerAnglesRaw." + "xyz"[axis], t => euler(t)[a]);
                }
            }

            public void Pos(string path, Func<float, Vector3> pos)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    int a = axis;
                    Curve(path, "m_LocalPosition." + "xyz"[axis], t => pos(t)[a]);
                }
            }

            /// <summary>Linear 0→360° spin around one local axis, one turn per clip.</summary>
            public void Spin(string path, int axis, float turns = 1f)
            {
                float len = Clip.length > 0f ? Clip.length : 1f;
                for (int i = 0; i < 3; i++)
                {
                    var c = i == axis
                        ? AnimationCurve.Linear(0f, 0f, len, 360f * turns)
                        : AnimationCurve.Constant(0f, len, 0f);
                    AnimationUtility.SetEditorCurve(Clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "localEulerAnglesRaw." + "xyz"[i]), c);
                }
            }

            void Curve(string path, string property, Func<float, float> f)
            {
                float length = Length;
                var keys = new Keyframe[samples + 1];
                const float eps = 0.002f;
                for (int i = 0; i <= samples; i++)
                {
                    float t = i / (float)samples;
                    float v = f(loop && i == samples ? 0f : t);
                    float slope = (f(Mathf.Repeat(t + eps, 1f)) - f(Mathf.Repeat(t - eps + 1f, 1f))) / (2f * eps * length);
                    if (!loop && (i == 0 || i == samples)) slope = 0f;
                    keys[i] = new Keyframe(t * length, v, slope, slope);
                }

                AnimationUtility.SetEditorCurve(Clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), new AnimationCurve(keys));
            }

            public float Length { get; set; } = 1f;
        }

        static AnimationClip MakeClip(string name, float length, bool loop, int samples, Action<ClipWriter> write)
        {
            Directory.CreateDirectory(AnimDir);
            string path = $"{AnimDir}/{name}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = name };
                AssetDatabase.CreateAsset(clip, path);
            }

            clip.ClearCurves();
            var w = new ClipWriter(clip, samples, loop) { Length = length };
            write(w);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        static float S(float t, float phase = 0f) => Mathf.Sin((t + phase) * Mathf.PI * 2f);
        static float C(float t, float phase = 0f) => Mathf.Cos((t + phase) * Mathf.PI * 2f);

        /// <summary>Locomotion cycle. <paramref name="legs"/>/<paramref name="arms"/> swing in degrees.</summary>
        static void Gait(ClipWriter w, float legs, float knee, float arms, float forearm, float lean, float bob, float twist, float armOut)
        {
            w.Pos(Hips, t => new Vector3(0f, HipsY - bob * 0.5f + bob * 0.5f * C(t * 2f), 0f));
            w.Rot(Hips, t => new Vector3(0f, -twist * 0.6f * S(t), 0f));
            w.Rot(Torso, t => new Vector3(lean + bob * 40f * C(t * 2f), twist * S(t), 0f));
            w.Rot(Head, t => new Vector3(-lean * 0.6f - bob * 30f * C(t * 2f), -twist * 0.6f * S(t), 0f));
            w.Rot(LegL, t => new Vector3(legs * S(t), 0f, 0f));
            w.Rot(LegR, t => new Vector3(-legs * S(t), 0f, 0f));
            w.Rot(ShinL, t => new Vector3(4f + knee * Mathf.Max(0f, -C(t)), 0f, 0f));
            w.Rot(ShinR, t => new Vector3(4f + knee * Mathf.Max(0f, C(t)), 0f, 0f));
            w.Rot(ArmL, t => new Vector3(-arms * S(t), 0f, -armOut));
            w.Rot(ArmR, t => new Vector3(arms * S(t), 0f, armOut));
            w.Rot(ForearmL, t => new Vector3(forearm - Mathf.Abs(S(t)) * forearm * 0.3f, 0f, 0f));
            w.Rot(ForearmR, t => new Vector3(forearm - Mathf.Abs(S(t)) * forearm * 0.3f, 0f, 0f));
        }

        public sealed class ClipSet
        {
            public AnimationClip Idle, Walk, Run, Hold, Cut, Happy, Work;
        }

        /// <summary>Generates (or updates) the shared toy clips. <paramref name="bladePaths"/> spin while cutting.</summary>
        public static ClipSet BuildClips(string[] bladePaths)
        {
            var set = new ClipSet();
            set.Idle = MakeClip("Toy_Idle", 2.4f, true, 24, w =>
            {
                w.Pos(Hips, t => new Vector3(0f, HipsY - 0.004f + 0.004f * C(t), 0f));
                w.Rot(Hips, t => new Vector3(0f, 0f, 0.8f * S(t, 0.25f)));
                w.Rot(Torso, t => new Vector3(2f + 1.4f * S(t), 0f, -0.8f * S(t, 0.25f)));
                w.Rot(Head, t => new Vector3(-1f + 2.2f * S(t, 0.15f), 3f * S(t * 0.5f), 0f));
                w.Rot(LegL, _ => new Vector3(0f, 0f, -1f));
                w.Rot(LegR, _ => new Vector3(0f, 0f, 1f));
                w.Rot(ShinL, _ => new Vector3(2f, 0f, 0f));
                w.Rot(ShinR, _ => new Vector3(2f, 0f, 0f));
                w.Rot(ArmL, t => new Vector3(2f, 0f, -8f - 1.6f * S(t)));
                w.Rot(ArmR, t => new Vector3(2f, 0f, 8f + 1.6f * S(t)));
                w.Rot(ForearmL, _ => new Vector3(-14f, 0f, 0f));
                w.Rot(ForearmR, _ => new Vector3(-14f, 0f, 0f));
            });
            set.Walk = MakeClip("Toy_Walk", 0.62f, true, 24, w => Gait(w, 28f, 34f, 26f, -22f, 4f, 0.03f, 6f, 8f));
            set.Run = MakeClip("Toy_Run", 0.4f, true, 24, w => Gait(w, 50f, 72f, 46f, -68f, 13f, 0.055f, 9f, 10f));

            // Tool layer (masked to the arms): two-handed grip on the cutter in front of the chest.
            set.Hold = MakeClip("Toy_Hold", 1.2f, true, 12, w =>
            {
                w.Rot(ArmR, t => new Vector3(-48f + 1.5f * S(t), -6f, 14f));
                w.Rot(ForearmR, _ => new Vector3(-42f, 0f, 0f));
                w.Rot(ArmL, t => new Vector3(-56f + 1.5f * S(t), 18f, 24f));
                w.Rot(ForearmL, _ => new Vector3(-48f, 0f, 0f));
                foreach (var p in bladePaths) w.Spin(p, 0, 0f);
            });
            // A full sawing stroke (push out, pull back), big enough to read from the game camera: the first version was
            // a 7 degree tremble that looked like the arms were not moving at all.
            set.Cut = MakeClip("Toy_Cut", 0.26f, true, 14, w =>
            {
                w.Rot(ArmR, t => new Vector3(-58f + 26f * S(t), -6f + 8f * C(t), 14f));
                w.Rot(ForearmR, t => new Vector3(-34f - 18f * S(t), 0f, 0f));
                w.Rot(ArmL, t => new Vector3(-66f + 26f * S(t), 18f + 6f * C(t), 24f));
                w.Rot(ForearmL, t => new Vector3(-40f - 18f * S(t), 0f, 0f));
                foreach (var p in bladePaths) w.Spin(p, 0, 3f);
            });
            set.Happy = MakeClip("Toy_Happy", 0.75f, false, 24, w =>
            {
                float Bump(float t) => Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                float Jump(float t) => Mathf.Sin(Mathf.Clamp01((t - 0.1f) / 0.6f) * Mathf.PI);
                w.Pos(Hips, t => new Vector3(0f, HipsY + 0.22f * Jump(t), 0f));
                w.Rot(Hips, _ => Vector3.zero);
                w.Rot(Torso, t => new Vector3(-6f * Bump(t), 0f, 0f));
                w.Rot(Head, t => new Vector3(-10f * Bump(t), 0f, 0f));
                w.Rot(ArmL, t => new Vector3(-10f * Bump(t), 0f, -8f - 140f * Bump(t)));
                w.Rot(ArmR, t => new Vector3(-10f * Bump(t), 0f, 8f + 140f * Bump(t)));
                w.Rot(ForearmL, t => new Vector3(-14f - 20f * Bump(t), 0f, 0f));
                w.Rot(ForearmR, t => new Vector3(-14f - 20f * Bump(t), 0f, 0f));
                w.Rot(LegL, t => new Vector3(-18f * Jump(t), 0f, -1f));
                w.Rot(LegR, t => new Vector3(-10f * Jump(t), 0f, 1f));
                w.Rot(ShinL, t => new Vector3(2f + 40f * Jump(t), 0f, 0f));
                w.Rot(ShinR, t => new Vector3(2f + 30f * Jump(t), 0f, 0f));
            });
            // Specialists at a post: both hands busy at chest height (console buttons, goods over the counter).
            set.Work = MakeClip("Toy_Work", 0.9f, true, 24, w =>
            {
                w.Pos(Hips, t => new Vector3(0f, HipsY - 0.006f + 0.006f * C(t * 2f), 0f));
                w.Rot(Hips, _ => Vector3.zero);
                w.Rot(Torso, t => new Vector3(9f + 2f * S(t * 2f), 5f * S(t), 0f));
                w.Rot(Head, t => new Vector3(6f + 3f * S(t * 2f, 0.1f), -4f * S(t), 0f));
                w.Rot(LegL, _ => new Vector3(0f, 0f, -2f));
                w.Rot(LegR, _ => new Vector3(0f, 0f, 2f));
                w.Rot(ShinL, _ => new Vector3(3f, 0f, 0f));
                w.Rot(ShinR, _ => new Vector3(3f, 0f, 0f));
                w.Rot(ArmR, t => new Vector3(-52f + 16f * S(t), -4f, 10f));
                w.Rot(ForearmR, t => new Vector3(-46f - 14f * S(t), 0f, 0f));
                w.Rot(ArmL, t => new Vector3(-52f + 16f * S(t, 0.5f), 4f, -10f));
                w.Rot(ForearmL, t => new Vector3(-46f - 14f * S(t, 0.5f), 0f, 0f));
            });
            return set;
        }

        static AnimatorController NewController(string name)
        {
            Directory.CreateDirectory(AnimDir);
            string path = $"{AnimDir}/{name}.controller";
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }

        static AnimatorState Locomotion(AnimatorController ac, ClipSet clips, float walkAt)
        {
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var state = ac.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(clips.Idle, 0f);
            tree.AddChild(clips.Walk, walkAt);
            tree.AddChild(clips.Run, 1f);
            ac.layers[0].stateMachine.defaultState = state;
            return state;
        }

        /// <summary>Player: locomotion + an arm layer holding the cutter (Cutting bool → saw stroke + blade spin).</summary>
        public static AnimatorController PlayerController(ClipSet clips, GameObject rigForMask, string[] maskPaths)
        {
            var ac = NewController("AC_ToyPlayer");
            Locomotion(ac, clips, 0.35f);
            ac.AddParameter("Cutting", AnimatorControllerParameterType.Bool);

            var mask = new AvatarMask { name = "Mask_ToolArms" };
            // Paths relative to the Animator's own transform. AvatarMask.AddTransformPath prefixes every path with the
            // root's name ("Model/Hips/..."): nothing matched, every bone was masked out and the layer never moved the
            // arms (the cutter hung at the hip, and cutting showed no arm motion).
            var bones = rigForMask.GetComponentsInChildren<Transform>(true);
            mask.transformCount = bones.Length;
            for (int i = 0; i < bones.Length; i++)
            {
                string p = AnimationUtility.CalculateTransformPath(bones[i], rigForMask.transform);
                mask.SetTransformPath(i, p);
                mask.SetTransformActive(i, maskPaths.Any(m => p == m || p.StartsWith(m + "/")));
            }

            AssetDatabase.AddObjectToAsset(mask, ac);
            ac.AddLayer("Tool");
            var layers = ac.layers;
            layers[1].defaultWeight = 1f;
            layers[1].avatarMask = mask;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            ac.layers = layers;

            var sm = ac.layers[1].stateMachine;
            var hold = sm.AddState("Hold");
            hold.motion = clips.Hold;
            var cut = sm.AddState("Cut");
            cut.motion = clips.Cut;
            sm.defaultState = hold;
            var toCut = hold.AddTransition(cut);
            toCut.hasExitTime = false;
            toCut.duration = 0.05f;
            toCut.AddCondition(AnimatorConditionMode.If, 0f, "Cutting");
            var toHold = cut.AddTransition(hold);
            toHold.hasExitTime = false;
            toHold.duration = 0.1f;
            toHold.AddCondition(AnimatorConditionMode.IfNot, 0f, "Cutting");
            EditorUtility.SetDirty(ac);
            return ac;
        }

        public static AnimatorController WorkerController(ClipSet clips)
        {
            var ac = NewController("AC_ToyWorker");
            var loco = Locomotion(ac, clips, 0.5f);
            // Working bool: operators at a console and sellers at a counter (Worker.SetWorking).
            ac.AddParameter("Working", AnimatorControllerParameterType.Bool);
            var sm = ac.layers[0].stateMachine;
            var work = sm.AddState("Work");
            work.motion = clips.Work;
            var toWork = loco.AddTransition(work);
            toWork.hasExitTime = false;
            toWork.duration = 0.15f;
            toWork.AddCondition(AnimatorConditionMode.If, 0f, "Working");
            var toLoco = work.AddTransition(loco);
            toLoco.hasExitTime = false;
            toLoco.duration = 0.15f;
            toLoco.AddCondition(AnimatorConditionMode.IfNot, 0f, "Working");
            EditorUtility.SetDirty(ac);
            return ac;
        }

        /// <summary>Customers: locomotion plus a cheer jump on the Happy trigger.</summary>
        public static AnimatorController CustomerController(ClipSet clips)
        {
            var ac = NewController("AC_ToyCustomer");
            var loco = Locomotion(ac, clips, 1f);
            ac.AddParameter("Happy", AnimatorControllerParameterType.Trigger);
            var sm = ac.layers[0].stateMachine;
            var happy = sm.AddState("Happy");
            happy.motion = clips.Happy;
            var toHappy = sm.AddAnyStateTransition(happy);
            toHappy.hasExitTime = false;
            toHappy.duration = 0.05f;
            toHappy.canTransitionToSelf = false;
            toHappy.AddCondition(AnimatorConditionMode.If, 0f, "Happy");
            var back = happy.AddTransition(loco);
            back.hasExitTime = true;
            back.exitTime = 0.95f;
            back.duration = 0.1f;
            EditorUtility.SetDirty(ac);
            return ac;
        }
    }
}

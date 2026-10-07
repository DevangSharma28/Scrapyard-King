using System.IO;
using System.Linq;
using System.Text;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Factory;
using ScrapYardKing.Harvest;
using ScrapYardKing.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Art pass 5 — VFX. Rebuilds the shared effects in place (same prefab GUIDs, so every existing reference upgrades) and
// adds a few new ones wired into existing VFX slots (no new code): punchy hit sparks with a flash and metal flecks, a
// break burst with flying scrap shards, dust and a shock ring, a heavier break for big vehicles, construction dust for
// area reveals, a crush puff per crusher output, sorter flashes and metallic upgrade glints.
public static class Art_VFX
{
    const string P = "Assets/_Project";
    const string VfxDir = P + "/Prefabs/VFX";
    const string MatDir = P + "/Art/Materials";
    static readonly StringBuilder Log = new();

    static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");

    static Material Glow => ParticleMat("FX_Glow", "Assets/_Project/Art/Textures/T_SoftCircle.png", new Color(1f, 0.85f, 0.55f), true);
    static Material RingAdd => ParticleMat("FX_RingAdd", "Assets/_Project/Art/Textures/T_Ring.png", new Color(1f, 0.95f, 0.8f), true);
    static Material DustSoft => ParticleMat("FX_DustSoft", "Assets/_Project/Art/Textures/T_SoftCircle.png", new Color(0.86f, 0.78f, 0.66f, 0.7f), false);

    static Material ParticleMat(string name, string texture, Color color, bool additive)
    {
        string path = $"{MatDir}/M_{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "M_" + name };
            AssetDatabase.CreateAsset(m, path);
        }

        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texture));
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Burst particle layer with common settings.</summary>
    static ParticleSystem Layer(GameObject go, Material mat, int count, Vector2 life, Vector2 speed, Vector2 size, Color a, Color b, float gravity, float coneAngle,
        float radius, ParticleSystemRenderMode mode = ParticleSystemRenderMode.Billboard, float delay = 0f, bool loop = false, float rate = 0f)
    {
        if (!go.TryGetComponent(out ParticleSystem ps)) ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = loop;
        main.playOnAwake = loop;
        main.duration = loop ? 1f : 0.6f;
        main.startDelay = delay;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = Mathf.Max(count * 2, 16);
        var e = ps.emission;
        e.rateOverTime = rate;
        e.SetBursts(loop ? new ParticleSystem.Burst[0] : new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = coneAngle >= 90f ? ParticleSystemShapeType.Sphere : ParticleSystemShapeType.Cone;
        shape.angle = Mathf.Min(coneAngle, 89f);
        shape.radius = radius;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = mode;
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (mode == ParticleSystemRenderMode.Stretch)
        {
            r.velocityScale = 0.05f;
            r.lengthScale = 1.8f;
        }

        return ps;
    }

    static GameObject Child(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    static void FadeOut(ParticleSystem ps, bool growth = false)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        if (!growth) return;
        var sol = ps.sizeOverLifetime;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.6f));
    }

    static void Spin(ParticleSystem ps, float degrees)
    {
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-degrees * Mathf.Deg2Rad, degrees * Mathf.Deg2Rad);
    }

    /// <summary>Flying mesh debris using the loose-scrap piece meshes (palette material).</summary>
    static void Shards(GameObject go, int count, Vector2 speed, float scale, float gravity = 2.2f)
    {
        var ps = Layer(go, ArtPalette.Material, count, new Vector2(0.9f, 1.4f), speed, new Vector2(0.6f * scale, 1.1f * scale), Color.white, Color.white, gravity, 55f, 0.3f,
            ParticleSystemRenderMode.Mesh);
        var r = go.GetComponent<ParticleSystemRenderer>();
        var meshes = AssetDatabase.LoadAllAssetsAtPath($"{ArtAssets.MeshDir}/ScrapPieces.asset").OfType<Mesh>().ToArray();
        if (meshes.Length > 0) r.SetMeshes(meshes);
        r.alignment = ParticleSystemRenderSpace.Local;
        var main = ps.main;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.separateAxes = true;
        rot.x = new ParticleSystem.MinMaxCurve(-8f, 8f);
        rot.y = new ParticleSystem.MinMaxCurve(-8f, 8f);
        rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
        var sol = ps.sizeOverLifetime;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f)));
    }

    static GameObject Save(GameObject go, string name)
    {
        var path = $"{VfxDir}/{name}.prefab";
        var saved = ArtAssets.SavePrefab(go, path);
        Log.AppendLine("vfx " + name);
        return saved;
    }

    /// <summary>Procedural particle sprites (replace the last third-party textures): a lumpy smoke puff and a 4-point sparkle.</summary>
    static void Sprites()
    {
        const int size = 128;
        var smoke = new Color[size * size];
        var star = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = x / (float)size, v = y / (float)size;
            float du = u - 0.5f, dv = v - 0.5f;
            float r = Mathf.Sqrt(du * du + dv * dv) * 2f;
            float lumps = ArtMaterials.Fbm(u, v, 4, 4, 13);
            float a = Mathf.Clamp01((1f - r * (0.85f + (1f - lumps) * 0.45f)) * 2.2f);
            float shade = 0.82f + lumps * 0.18f;
            smoke[y * size + x] = new Color(shade, shade, shade, a * a);
            float arms = Mathf.Max(Mathf.Clamp01(1f - Mathf.Abs(du) * 14f) * Mathf.Clamp01(1f - Mathf.Abs(dv) * 2.4f),
                Mathf.Clamp01(1f - Mathf.Abs(dv) * 14f) * Mathf.Clamp01(1f - Mathf.Abs(du) * 2.4f));
            float core = Mathf.Clamp01(1f - r * 3f);
            float s = Mathf.Clamp01(arms + core * core);
            star[y * size + x] = new Color(1f, 1f, 1f, s);
        }

        var smokeTex = ArtMaterials.SaveTexture($"{ArtAssets.TexDir}/T_Smoke.png", size, smoke, false, true);
        var starTex = ArtMaterials.SaveTexture($"{ArtAssets.TexDir}/T_Star.png", size, star, false, true);
        foreach (var (mat, tex) in new[] { ("M_FX_Smoke", smokeTex), ("M_FX_Star", starTex) })
        {
            var m = Mat(mat);
            if (m == null) continue;
            m.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(m);
        }
    }

    public static string Build()
    {
        Log.Clear();
        Sprites();
        var sparks = Mat("M_FX_Sparks");
        var smoke = Mat("M_FX_Smoke");
        var confetti = Mat("M_FX_Confetti");
        var star = Mat("M_FX_Star");

        // ---------- hit: spark spray + flash + metal flecks + a puff ----------
        {
            var root = new GameObject("VFX_HitSparks");
            Layer(root, sparks, 18, new Vector2(0.18f, 0.42f), new Vector2(6f, 12f), new Vector2(0.06f, 0.12f), new Color(1f, 0.9f, 0.45f), new Color(1f, 0.55f, 0.12f), 2f, 38f,
                0.05f, ParticleSystemRenderMode.Stretch);
            var flash = Layer(Child(root, "Flash"), Glow, 1, new Vector2(0.07f, 0.09f), Vector2.zero, new Vector2(0.9f, 1.1f), new Color(1f, 0.9f, 0.6f), Color.white, 0f, 0f, 0f);
            _ = flash;
            var flecks = Layer(Child(root, "Flecks"), ArtPalette.MetalMaterial, 5, new Vector2(0.4f, 0.7f), new Vector2(3f, 6f), new Vector2(0.06f, 0.1f), Color.white, Color.white,
                2.5f, 45f, 0.05f, ParticleSystemRenderMode.Mesh);
            var fr = flecks.GetComponent<ParticleSystemRenderer>();
            var cube = AssetDatabase.LoadAllAssetsAtPath($"{ArtAssets.MeshDir}/ScrapPieces.asset").OfType<Mesh>().FirstOrDefault(m => m.name == "Bolt");
            if (cube != null) fr.mesh = cube;
            var puff = Layer(Child(root, "Puff"), DustSoft, 3, new Vector2(0.3f, 0.5f), new Vector2(0.4f, 1f), new Vector2(0.35f, 0.6f), new Color(0.85f, 0.8f, 0.72f, 0.5f),
                new Color(0.7f, 0.66f, 0.6f, 0.4f), -0.05f, 40f, 0.1f);
            FadeOut(puff, true);
            Save(root, "VFX_HitSparks");
        }

        // ---------- break: dust cloud + sparks + flying scrap shards + shock ring ----------
        GameObject Break(string name, float s)
        {
            var root = new GameObject(name);
            var dust = Layer(root, DustSoft, Mathf.RoundToInt(12 * s), new Vector2(0.7f, 1.3f), new Vector2(1.5f * s, 4f * s), new Vector2(0.8f * s, 1.6f * s),
                new Color(0.88f, 0.82f, 0.72f, 0.75f), new Color(0.72f, 0.68f, 0.62f, 0.6f), -0.02f, 70f, 0.6f * s);
            FadeOut(dust, true);
            Spin(dust, 60f);
            Layer(Child(root, "Sparks"), sparks, Mathf.RoundToInt(28 * s), new Vector2(0.3f, 0.6f), new Vector2(7f, 14f * s), new Vector2(0.07f, 0.15f),
                new Color(1f, 0.9f, 0.45f), new Color(1f, 0.5f, 0.12f), 2.2f, 60f, 0.3f, ParticleSystemRenderMode.Stretch);
            Shards(Child(root, "Shards"), Mathf.RoundToInt(14 * s), new Vector2(5f, 10f * Mathf.Sqrt(s)), 1f + (s - 1f) * 0.4f);
            var ring = Layer(Child(root, "Ring"), RingAdd, 1, new Vector2(0.25f, 0.3f), Vector2.zero, new Vector2(1.5f * s, 1.5f * s), new Color(1f, 0.92f, 0.7f, 0.9f),
                new Color(1f, 0.85f, 0.6f, 0.9f), 0f, 0f, 0f);
            var rs = ring.sizeOverLifetime;
            rs.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.3f, 1f, 3.2f));
            FadeOut(ring);
            ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            var flash = Layer(Child(root, "Flash"), Glow, 1, new Vector2(0.1f, 0.12f), Vector2.zero, new Vector2(2.2f * s, 2.6f * s), new Color(1f, 0.92f, 0.7f), Color.white, 0f, 0f,
                0f);
            _ = flash;
            return Save(root, name);
        }

        Break("VFX_BreakBurst", 1f);
        var heavy = Break("VFX_BreakBurstHeavy", 1.8f);
        foreach (var n in new[] { "Scrap_Tractor", "Scrap_Truck", "Scrap_GarbageTruck" })
        {
            var def = AssetDatabase.LoadAssetAtPath<ScrapDefinition>($"{P}/Data/Scrap/{n}.asset");
            if (def != null) ArtAssets.Set(def, ("breakVfx", heavy.GetComponent<ParticleSystem>()), ("breakVfxScale", 1.15f));
        }

        // ---------- spawn dust (scrap popping in) ----------
        {
            var root = new GameObject("VFX_SpawnDust");
            var dust = Layer(root, DustSoft, 10, new Vector2(0.5f, 0.9f), new Vector2(1.2f, 2.6f), new Vector2(0.5f, 0.9f), new Color(0.9f, 0.84f, 0.74f, 0.7f),
                new Color(0.78f, 0.72f, 0.64f, 0.6f), 0f, 85f, 0.5f);
            var shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            FadeOut(dust, true);
            Layer(Child(root, "Stars"), star, 4, new Vector2(0.3f, 0.5f), new Vector2(1f, 2f), new Vector2(0.25f, 0.4f), new Color(1f, 0.95f, 0.7f), Color.white, -0.2f, 40f, 0.3f);
            Save(root, "VFX_SpawnDust");
        }

        // ---------- crusher: working dust (loop) and a crush puff of chips per output ----------
        {
            var root = new GameObject("VFX_CrusherDust");
            var dust = Layer(root, DustSoft, 0, new Vector2(0.8f, 1.4f), new Vector2(0.6f, 1.4f), new Vector2(0.5f, 0.9f), new Color(0.82f, 0.76f, 0.68f, 0.55f),
                new Color(0.66f, 0.62f, 0.58f, 0.45f), -0.05f, 30f, 0.5f, ParticleSystemRenderMode.Billboard, 0f, true, 9f);
            dust.GetComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = dust.main;
            main.playOnAwake = false;
            FadeOut(dust, true);
            var chips = Layer(Child(root, "Chips"), ArtPalette.MetalMaterial, 0, new Vector2(0.4f, 0.7f), new Vector2(2f, 4f), new Vector2(0.12f, 0.2f), Color.white, Color.white,
                1.8f, 25f, 0.4f, ParticleSystemRenderMode.Mesh, 0f, true, 6f);
            var cm = chips.main;
            cm.playOnAwake = false;
            var meshes = AssetDatabase.LoadAllAssetsAtPath($"{ArtAssets.MeshDir}/ScrapPieces.asset").OfType<Mesh>().ToArray();
            if (meshes.Length > 0) chips.GetComponent<ParticleSystemRenderer>().SetMeshes(meshes);
            Save(root, "VFX_CrusherDust");
        }

        {
            var root = new GameObject("VFX_CrushPuff");
            var puff = Layer(root, DustSoft, 6, new Vector2(0.4f, 0.7f), new Vector2(1.5f, 3f), new Vector2(0.4f, 0.7f), new Color(0.85f, 0.8f, 0.72f, 0.7f),
                new Color(0.7f, 0.66f, 0.6f, 0.6f), 0f, 50f, 0.3f);
            FadeOut(puff, true);
            Layer(Child(root, "Sparks"), sparks, 8, new Vector2(0.2f, 0.4f), new Vector2(3f, 6f), new Vector2(0.05f, 0.1f), new Color(1f, 0.85f, 0.4f), new Color(1f, 0.55f, 0.15f),
                1.5f, 50f, 0.2f, ParticleSystemRenderMode.Stretch);
            Save(root, "VFX_CrushPuff");
        }

        {
            var root = new GameObject("VFX_SorterFlash");
            Layer(root, Glow, 1, new Vector2(0.12f, 0.15f), Vector2.zero, new Vector2(1.2f, 1.4f), new Color(0.6f, 0.95f, 1f), new Color(1f, 0.95f, 0.7f), 0f, 0f, 0f);
            Layer(Child(root, "Glints"), star, 5, new Vector2(0.3f, 0.5f), new Vector2(1.5f, 3f), new Vector2(0.2f, 0.35f), new Color(0.75f, 0.95f, 1f), Color.white, -0.3f, 50f,
                0.3f);
            Save(root, "VFX_SorterFlash");
        }

        // ---------- upgrades / hires: confetti + metallic glints + ring ----------
        {
            var root = new GameObject("VFX_UpgradeBurst");
            var conf = Layer(root, confetti, 36, new Vector2(0.8f, 1.4f), new Vector2(4f, 9f), new Vector2(0.12f, 0.24f), new Color(1f, 0.85f, 0.2f), new Color(0.3f, 0.85f, 1f),
                1.4f, 40f, 0.3f);
            var main = conf.main;
            var grad = new Gradient();
            grad.SetKeys(new[]
            {
                new GradientColorKey(new Color(1f, 0.85f, 0.2f), 0f), new GradientColorKey(new Color(0.3f, 0.85f, 1f), 0.33f),
                new GradientColorKey(new Color(1f, 0.4f, 0.35f), 0.66f), new GradientColorKey(new Color(0.5f, 1f, 0.4f), 1f)
            }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            main.startColor = new ParticleSystem.MinMaxGradient(grad) { mode = ParticleSystemGradientMode.RandomColor };
            Spin(conf, 360f);
            Layer(Child(root, "Glints"), sparks, 18, new Vector2(0.3f, 0.6f), new Vector2(5f, 10f), new Vector2(0.08f, 0.14f), new Color(1f, 0.95f, 0.7f), new Color(1f, 0.8f, 0.3f),
                0.8f, 70f, 0.2f, ParticleSystemRenderMode.Stretch);
            Layer(Child(root, "Stars"), star, 10, new Vector2(0.4f, 0.7f), new Vector2(1.5f, 4f), new Vector2(0.3f, 0.6f), new Color(1f, 0.95f, 0.6f), Color.white, 0f, 90f, 0.6f);
            var ring = Layer(Child(root, "Ring"), RingAdd, 1, new Vector2(0.35f, 0.4f), Vector2.zero, new Vector2(2f, 2f), new Color(1f, 0.9f, 0.5f), new Color(1f, 0.9f, 0.5f), 0f,
                0f, 0f, ParticleSystemRenderMode.HorizontalBillboard);
            var rs = ring.sizeOverLifetime;
            rs.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.3f, 1f, 2.6f));
            FadeOut(ring);
            Save(root, "VFX_UpgradeBurst");
        }

        // ---------- expansion reveal: construction dust ring + debris + confetti ----------
        GameObject construction;
        {
            var root = new GameObject("VFX_ConstructionBurst");
            var dust = Layer(root, DustSoft, 40, new Vector2(1.2f, 2.2f), new Vector2(4f, 9f), new Vector2(1.6f, 3f), new Color(0.9f, 0.84f, 0.74f, 0.75f),
                new Color(0.75f, 0.7f, 0.64f, 0.6f), -0.02f, 90f, 3f);
            var shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 3f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var vel = dust.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.y = new ParticleSystem.MinMaxCurve(1f, 2.5f);
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            FadeOut(dust, true);
            Spin(dust, 45f);
            Shards(Child(root, "Debris"), 24, new Vector2(6f, 12f), 1.4f, 2.4f);
            var conf = Layer(Child(root, "Confetti"), confetti, 50, new Vector2(1.2f, 2f), new Vector2(6f, 12f), new Vector2(0.18f, 0.3f), new Color(1f, 0.85f, 0.2f),
                new Color(0.3f, 0.85f, 1f), 1f, 35f, 1f);
            Spin(conf, 360f);
            construction = Save(root, "VFX_ConstructionBurst");
        }

        // ---------- wire the new effects into existing slots ----------
        var crushPuff = AssetDatabase.LoadAssetAtPath<GameObject>($"{VfxDir}/VFX_CrushPuff.prefab");
        var sorterFlash = AssetDatabase.LoadAssetAtPath<GameObject>($"{VfxDir}/VFX_SorterFlash.prefab");
        EditPrefab($"{P}/Prefabs/Stations/Crusher.prefab", root => AttachBurst(root, crushPuff, new Vector3(0f, 0.9f, -1.9f)));
        EditPrefab($"{P}/Prefabs/Stations/Sorter.prefab", root => AttachBurst(root, sorterFlash, new Vector3(0f, 1.6f, -1.4f)));

        var active = SceneManager.GetActiveScene();
        if (active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = EditorSceneManager.OpenScene($"{P}/Scenes/Area1_OldScrapYard.unity", OpenSceneMode.Single);
        foreach (var e in Object.FindObjectsByType<Expansion>(FindObjectsInactive.Include))
            ArtAssets.Set(e, ("revealVfx", construction.GetComponent<ParticleSystem>()));
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    static void AttachBurst(GameObject root, GameObject prefab, Vector3 localPos)
    {
        var old = root.transform.Find(prefab.name);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
        go.transform.localPosition = localPos;
        var visuals = root.GetComponent<MachineVisuals>();
        ArtAssets.Set(visuals, ("outputBurst", go.GetComponent<ParticleSystem>()));
    }

    static void EditPrefab(string path, System.Action<GameObject> edit)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            edit(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}

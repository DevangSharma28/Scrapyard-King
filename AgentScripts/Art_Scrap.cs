using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Harvest;
using ScrapYardKing.Items;
using ScrapYardKing.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using static ScrapYardKing.EditorTools.Art.ArtMaterials;

// Art pass 2 — scrap. Replaces every Kenney scrap object with custom toy-industrial models built from ArtKit: cars
// (sedan, hatchback, pickup), oil drums, tire stacks, fridges, washer, stove, go-karts, tractor, mini excavator, box
// truck, flatbed, garbage truck and cement mixer. Each is a wreck you can take apart: panels, doors and wheels are
// ScrapParts that fly off as health drops, ScrapDamageStages add smoke / cracked glass / a sagging body, and the
// break bursts into shards. Also the loose scrap pieces (palette-atlas meshes) and the damage VFX.
// Entry points: Preview (lineup PNG), Build (prefabs in place, definitions, icons).
public static class Art_Scrap
{
    const string P = "Assets/_Project";
    const string PrefabDir = P + "/Prefabs";
    const string DataDir = P + "/Data";
    static readonly StringBuilder Log = new();
    static PaletteBaker baker;

    enum VS { Paint, Paint2, Trim, Chrome, Glass, Rubber, Rim, Light, Tail, Rust, Interior, White, Wood, Hazard, Steel, Count }

    static Material[] Table(Material paint, Material paint2 = null)
    {
        var t = new Material[(int)VS.Count];
        t[(int)VS.Paint] = paint;
        t[(int)VS.Paint2] = paint2 ?? paint;
        t[(int)VS.Trim] = Mat("ScrapTrim", new Color(0.17f, 0.18f, 0.2f), 0.3f, 0f, Detail.Rubber, 1.5f);
        t[(int)VS.Chrome] = Mat("ScrapChrome", new Color(0.8f, 0.82f, 0.86f), 0.78f, 0.35f, Detail.Metal, 1.5f);
        t[(int)VS.Glass] = Mat("ScrapGlass", new Color(0.4f, 0.58f, 0.7f), 0.92f, 0.1f, Detail.Plain);
        t[(int)VS.Rubber] = Mat("ScrapRubber", new Color(0.13f, 0.13f, 0.14f), 0.22f, 0f, Detail.Rubber, 2f);
        t[(int)VS.Rim] = Mat("ScrapRim", new Color(0.68f, 0.7f, 0.74f), 0.6f, 0.3f, Detail.Metal, 2f);
        t[(int)VS.Light] = Mat("ScrapHeadlight", new Color(1f, 0.96f, 0.82f), 0.9f, 0f, Detail.Plain, 1f, new Color(0.5f, 0.45f, 0.3f));
        t[(int)VS.Tail] = Mat("ScrapTaillight", new Color(0.95f, 0.15f, 0.12f), 0.85f, 0f, Detail.Plain, 1f, new Color(0.5f, 0.05f, 0.03f));
        t[(int)VS.Rust] = Mat("ScrapRust", Color.white, 0.15f, 0f, Detail.RustPatch, 1.2f);
        t[(int)VS.Interior] = Mat("ScrapInterior", new Color(0.26f, 0.27f, 0.3f), 0.25f, 0f, Detail.Cloth, 2f);
        t[(int)VS.White] = Mat("ScrapAppliance", new Color(0.95f, 0.95f, 0.92f), 0.6f, 0f, Detail.Paint, 1f);
        t[(int)VS.Wood] = Mat("ScrapWood", new Color(0.78f, 0.56f, 0.33f), 0.2f, 0f, Detail.Wood, 0.8f);
        t[(int)VS.Hazard] = Mat("ScrapHazard", new Color(1f, 0.76f, 0.1f), 0.45f, 0f, Detail.Paint, 1f);
        t[(int)VS.Steel] = Mat("ScrapSteel", new Color(0.52f, 0.55f, 0.6f), 0.5f, 0.3f, Detail.Metal, 1.5f);
        return t;
    }

    static Material Paint(string name, Color c) => Mat("Paint" + name, c, 0.55f, 0.08f, Detail.Paint, 0.6f);

    // =====================================================================================
    // Scaffolding
    // =====================================================================================

    sealed class Wreck
    {
        public GameObject Root;
        public Transform Visual, Body;
        public string Model;
        public Material[] Table;
        public readonly List<GameObject> Smoke = new(), Cracks = new(), Heavy = new();
        public float Sag1 = 2f, Sag2 = 5f;
        public Vector3 SagAxis = Vector3.forward;
    }

    static Wreck NewWreck(string model, ScrapDefinition def, Material[] table)
    {
        var w = new Wreck { Model = model, Table = table };
        w.Root = new GameObject(model);
        w.Root.layer = LayerMask.NameToLayer("Scrap");
        w.Visual = new GameObject("Visual").transform;
        w.Visual.SetParent(w.Root.transform, false);
        w.Body = new GameObject("Body").transform;
        w.Body.SetParent(w.Visual, false);
        var scrap = w.Root.AddComponent<ScrapObject>();
        var box = w.Root.AddComponent<BoxCollider>();
        ArtAssets.Set(scrap, ("definition", def), ("visualRoot", w.Visual), ("hitCollider", box));
        return w;
    }

    static GameObject Static(Wreck w, string part, MeshKit k, Vector3 pos = default, Vector3 euler = default) =>
        ArtAssets.Part(w.Model, part, k, w.Table, w.Body, pos, euler);

    static GameObject Part(Wreck w, string part, MeshKit k, Vector3 pos, int order, Vector3 euler = default)
    {
        var go = ArtAssets.Part(w.Model, part, k, w.Table, w.Body, pos, euler);
        var sp = go.AddComponent<ScrapPart>();
        ArtAssets.Set(sp, ("detachOrder", order), ("lifetime", 0.75f));
        return go;
    }

    static GameObject Effect(Wreck w, string prefab, Vector3 pos, float scale = 1f)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/VFX/{prefab}.prefab");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, w.Body);
        go.transform.localPosition = pos;
        go.transform.localScale = Vector3.one * scale;
        go.SetActive(false);
        return go;
    }

    /// <summary>Collider from the model, health bar, NavMesh carving, damage stages; saves the prefab.</summary>
    static GameObject Finish(Wreck w, string path, float shrink = 0.94f)
    {
        var b = ArtAssets.RendererBounds(w.Visual.gameObject);
        var box = w.Root.GetComponent<BoxCollider>();
        box.center = w.Root.transform.InverseTransformPoint(b.center);
        box.size = new Vector3(b.size.x * shrink, b.size.y, b.size.z * shrink);
        HealthBar(w.Root.transform, b.max.y + 0.6f, 1.4f);

        var obstacle = w.Root.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = box.center;
        obstacle.size = box.size;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;

        var stages = w.Root.AddComponent<ScrapDamageStages>();
        var so = new SerializedObject(stages);
        so.FindProperty("scrap").objectReferenceValue = w.Root.GetComponent<ScrapObject>();
        so.FindProperty("sagPivot").objectReferenceValue = w.Body;
        so.FindProperty("sagAxis").vector3Value = w.SagAxis;
        var sp = so.FindProperty("stages");
        var defs = new[] { (0.8f, w.Smoke, 0f), (0.55f, w.Cracks, w.Sag1), (0.3f, w.Heavy, w.Sag2) };
        sp.arraySize = defs.Length;
        for (int i = 0; i < defs.Length; i++)
        {
            var e = sp.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("belowHealth").floatValue = defs[i].Item1;
            e.FindPropertyRelative("sagDegrees").floatValue = defs[i].Item3;
            var show = e.FindPropertyRelative("show");
            show.arraySize = defs[i].Item2.Count;
            for (int j = 0; j < defs[i].Item2.Count; j++) show.GetArrayElementAtIndex(j).objectReferenceValue = defs[i].Item2[j];
            e.FindPropertyRelative("hide").arraySize = 0;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        foreach (var go in w.Smoke.Concat(w.Cracks).Concat(w.Heavy)) go.SetActive(false);
        baker?.Convert(w.Root, w.Model);
        return ArtAssets.SavePrefab(w.Root, path);
    }

    static void HealthBar(Transform parent, float height, float width)
    {
        var bg = AssetDatabase.LoadAssetAtPath<Material>(P + "/Art/Materials/M_BarBg.mat");
        var fill = AssetDatabase.LoadAssetAtPath<Material>(P + "/Art/Materials/M_BarFill.mat");
        var bar = new GameObject("HealthBar");
        bar.transform.SetParent(parent, false);
        bar.transform.localPosition = new Vector3(0f, height, 0f);
        bar.transform.localScale = new Vector3(width, 0.16f, 1f);
        var bgGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bgGo.name = "Bg";
        Object.DestroyImmediate(bgGo.GetComponent<Collider>());
        bgGo.transform.SetParent(bar.transform, false);
        bgGo.GetComponent<Renderer>().sharedMaterial = bg;
        var fillRoot = new GameObject("FillRoot").transform;
        fillRoot.SetParent(bar.transform, false);
        fillRoot.localPosition = new Vector3(0f, 0f, -0.01f);
        var fillGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fillGo.name = "Fill";
        Object.DestroyImmediate(fillGo.GetComponent<Collider>());
        fillGo.transform.SetParent(fillRoot, false);
        fillGo.transform.localScale = new Vector3(0.96f, 0.7f, 1f);
        fillGo.GetComponent<Renderer>().sharedMaterial = fill;
        foreach (var r in bar.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        var hb = bar.AddComponent<WorldHealthBar>();
        ArtAssets.Set(hb, ("fill", fillRoot), ("fillRenderer", fillGo.GetComponent<Renderer>()));
        bar.SetActive(false);
        ArtAssets.Set(parent.GetComponent<ScrapObject>(), ("healthBar", hb));
    }

    // =====================================================================================
    // Shared pieces
    // =====================================================================================

    /// <summary>Chunky tyre with tread blocks, steel rim and hub cap; axle along X.</summary>
    static void Wheel(MeshKit k, Vector3 pos, float r, float w, bool treads = true, VS rim = VS.Rim)
    {
        float ri = r * 0.6f;
        k.Push(pos, new Vector3(0f, 0f, 90f));
        k.Lathe((int)VS.Rubber, Vector3.zero, new[]
        {
            new Vector2(ri, -w * 0.5f), new Vector2(r - 0.05f, -w * 0.5f), new Vector2(r, -w * 0.5f + 0.06f), new Vector2(r, w * 0.5f - 0.06f),
            new Vector2(r - 0.05f, w * 0.5f), new Vector2(ri, w * 0.5f),
        }, 22, default, false, 40f, true);
        if (treads)
        {
            int n = Mathf.Max(10, Mathf.RoundToInt(r * 30f));
            for (int i = 0; i < n; i++)
            {
                float a = 360f * i / n;
                var d = Quaternion.Euler(0f, a, 0f) * Vector3.right;
                k.Box((int)VS.Rubber, d * (r + 0.01f), new Vector3(0.05f, w * 0.82f, r * 0.2f), 0.012f, new Vector3(0f, -a, 0f));
            }
        }

        k.Cylinder((int)rim, Vector3.zero, ri * 1.02f, w * 0.86f, 16, default, 0.02f);
        k.Cylinder((int)VS.Chrome, new Vector3(0f, w * 0.44f, 0f), ri * 0.42f, 0.04f, 12, default, 0.012f);
        k.Cylinder((int)VS.Chrome, new Vector3(0f, -w * 0.44f, 0f), ri * 0.42f, 0.04f, 12, default, 0.012f);
        for (int i = 0; i < 5; i++)
        {
            var d = Quaternion.Euler(0f, 72f * i, 0f) * Vector3.right * ri * 0.66f;
            k.Cylinder((int)VS.Trim, d + new Vector3(0f, w * 0.43f, 0f), 0.025f, 0.03f, 6, default, 0.005f);
        }

        k.Pop();
    }

    static MeshKit WheelKit(float r, float w)
    {
        var k = new MeshKit();
        Wheel(k, Vector3.zero, r, w);
        return k;
    }

    /// <summary>Dark half-disc wheel well on a body side (axle along X, opening downward).</summary>
    static void Arch(MeshKit k, Vector3 pos, float r)
    {
        k.Push(pos, new Vector3(0f, 0f, 90f));
        k.Lathe((int)VS.Trim, Vector3.zero, new[] { new Vector2(0.001f, -0.03f), new Vector2(r, -0.03f), new Vector2(r, 0.03f), new Vector2(0.001f, 0.03f) },
            14, default, false, 50f, true, 180f, 270f);
        k.Pop();
    }

    static void Crack(MeshKit k, Vector3 center, Vector3 euler, float size)
    {
        k.Push(center, euler);
        for (int i = 0; i < 5; i++)
        {
            float a = i * 72f + 15f;
            var d = Quaternion.Euler(0f, 0f, a) * Vector3.up * size * 0.32f;
            k.Box((int)VS.White, d, new Vector3(0.018f, size * 0.62f, 0.012f), 0.004f, new Vector3(0f, 0f, a));
        }

        k.Pop();
    }

    static MeshKit Rust(params (Vector3 pos, Vector3 size)[] patches)
    {
        var k = new MeshKit();
        foreach (var (pos, size) in patches) k.Box((int)VS.Rust, pos, size, Mathf.Min(size.x, size.y, size.z) * 0.4f);
        return k;
    }

    // =====================================================================================
    // Cars
    // =====================================================================================

    enum CarStyle { Sedan, Hatch, Pickup }

    /// <summary>Which panel of this car is mismatched primer grey (wrecks are patched together from other cars).</summary>
    enum Primer { None, Hood, DoorFL, Lid, DoorRR }

    static GameObject BuildCar(string path, string model, ScrapDefinition def, CarStyle style, Material paint, Primer primer)
    {
        var w = NewWreck(model, def, Table(paint, Mat("PaintCarDark", new Color(0.2f, 0.21f, 0.24f), 0.5f, 0.1f)));
        const float halfW = 1.0f, wr = 0.46f, ww = 0.34f, axleF = 1.38f, axleR = -1.38f;
        float len = style == CarStyle.Pickup ? 2.25f : 2.15f;

        var k = new MeshKit();
        k.Prism((int)VS.Paint, Vector3.zero, new[]
        {
            new Vector2(-len + 0.05f, 0.34f), new Vector2(-len, 0.95f), new Vector2(-len + 0.6f, 1.08f), new Vector2(1.35f, 1.06f), new Vector2(len - 0.03f, 0.86f),
            new Vector2(len, 0.34f),
        }, halfW * 2f, 0.15f);
        k.Box((int)VS.Trim, new Vector3(0f, 0.28f, 0f), new Vector3(halfW * 1.9f, 0.18f, len * 1.8f), 0.06f);
        // Cabin with glass; pillars in body colour.
        Vector2[] cabin = style switch
        {
            CarStyle.Hatch => new[] { new Vector2(-len + 0.15f, 1.02f), new Vector2(-len + 0.22f, 1.68f), new Vector2(0.42f, 1.75f), new Vector2(1.05f, 1.02f) },
            CarStyle.Pickup => new[] { new Vector2(-0.55f, 1.02f), new Vector2(-0.5f, 1.78f), new Vector2(0.48f, 1.8f), new Vector2(1.08f, 1.02f) },
            _ => new[] { new Vector2(-1.45f, 1.02f), new Vector2(-1.15f, 1.72f), new Vector2(0.45f, 1.75f), new Vector2(1.05f, 1.02f) },
        };
        k.Prism((int)VS.Paint, Vector3.zero, cabin, 1.78f, 0.13f);
        var glass = cabin.Select((p, i) => i switch
        {
            0 => p + new Vector2(0.17f, 0.1f),
            1 => p + new Vector2(0.14f, -0.1f),
            2 => p + new Vector2(-0.1f, -0.1f),
            _ => p + new Vector2(-0.2f, 0.1f),
        }).ToArray();
        k.Prism((int)VS.Glass, Vector3.zero, glass, 1.81f, 0.02f);
        float pillarZ = (cabin[1].x + cabin[2].x) * 0.5f;
        k.Box((int)VS.Paint, new Vector3(0f, 1.38f, pillarZ), new Vector3(1.83f, 0.6f, 0.1f), 0.03f);
        // Windscreen and rear window on the slopes.
        Vector2 f0 = cabin[2], f1 = cabin[3];
        float fa = Mathf.Atan2(f0.y - f1.y, f1.x - f0.x) * Mathf.Rad2Deg;
        k.Box((int)VS.Glass, new Vector3(0f, (f0.y + f1.y) * 0.5f + 0.02f, (f0.x + f1.x) * 0.5f + 0.02f), new Vector3(1.5f, 0.03f, Vector2.Distance(f0, f1) * 0.8f),
            0.01f, new Vector3(fa, 0f, 0f));
        Vector2 r0 = cabin[0], r1 = cabin[1];
        float ra = -Mathf.Atan2(r1.y - r0.y, r1.x - r0.x) * Mathf.Rad2Deg;
        k.Box((int)VS.Glass, new Vector3(0f, (r0.y + r1.y) * 0.5f + 0.01f, (r0.x + r1.x) * 0.5f - 0.02f), new Vector3(1.45f, 0.03f, Vector2.Distance(r0, r1) * 0.78f),
            0.01f, new Vector3(ra, 0f, 0f));
        if (style == CarStyle.Pickup)
        {
            // Open bed behind the cab.
            k.Box((int)VS.Paint, new Vector3(-halfW + 0.06f, 1.22f, -1.38f), new Vector3(0.12f, 0.36f, 1.65f), 0.04f);
            k.Box((int)VS.Paint, new Vector3(halfW - 0.06f, 1.22f, -1.38f), new Vector3(0.12f, 0.36f, 1.65f), 0.04f);
            k.Box((int)VS.Paint, new Vector3(0f, 1.22f, -0.6f), new Vector3(halfW * 2f, 0.36f, 0.1f), 0.04f);
            k.Box((int)VS.Interior, new Vector3(0f, 1.09f, -1.38f), new Vector3(halfW * 1.8f, 0.04f, 1.6f), 0.01f);
            k.Box((int)VS.Wood, new Vector3(0.2f, 1.25f, -1.5f), new Vector3(0.7f, 0.3f, 0.5f), 0.04f, new Vector3(0f, 15f, 0f));
        }

        // Face: grille, headlights, tail lights, plates, mirrors.
        k.Box((int)VS.Trim, new Vector3(0f, 0.66f, len + 0.01f), new Vector3(0.95f, 0.24f, 0.05f), 0.02f);
        for (int i = 0; i < 4; i++) k.Box((int)VS.Chrome, new Vector3(0f, 0.58f + i * 0.055f, len + 0.035f), new Vector3(0.9f, 0.018f, 0.02f), 0.005f);
        for (int side = -1; side <= 1; side += 2)
        {
            k.Box((int)VS.Light, new Vector3(side * 0.68f, 0.76f, len - 0.02f), new Vector3(0.36f, 0.16f, 0.08f), 0.04f);
            k.Box((int)VS.Tail, new Vector3(side * 0.7f, 0.82f, -len + 0.02f), new Vector3(0.32f, 0.14f, 0.08f), 0.03f);
            k.Box((int)VS.Paint, new Vector3(side * 1.07f, 1.13f, 0.95f), new Vector3(0.16f, 0.12f, 0.2f), 0.04f);
            Arch(k, new Vector3(side * (halfW + 0.005f), wr - 0.02f, axleF), wr + 0.08f);
            Arch(k, new Vector3(side * (halfW + 0.005f), wr - 0.02f, axleR), wr + 0.08f);
        }

        k.Box((int)VS.White, new Vector3(0f, 0.55f, -len - 0.01f), new Vector3(0.5f, 0.17f, 0.03f), 0.01f);
        k.Box((int)VS.Trim, new Vector3(0f, 0.55f, -len - 0.025f), new Vector3(0.38f, 0.05f, 0.01f), 0.002f);
        // Right-side doors stay on (with seams and handles); the brake drum shows where a wheel is missing.
        k.Box((int)VS.Paint, new Vector3(halfW + 0.015f, 0.74f, 0.42f), new Vector3(0.04f, 0.56f, 1.02f), 0.02f);
        k.Box((int)VS.Chrome, new Vector3(halfW + 0.045f, 0.9f, 0.12f), new Vector3(0.03f, 0.04f, 0.16f), 0.01f);
        if (style != CarStyle.Pickup) k.Box((int)VS.Chrome, new Vector3(-halfW - 0.045f, 0.9f, -0.95f), new Vector3(0.03f, 0.04f, 0.16f), 0.01f);
        k.Cylinder((int)VS.Steel, new Vector3(-halfW + 0.12f, wr - 0.02f, axleR), 0.2f, 0.16f, 12, new Vector3(0f, 0f, 90f), 0.03f);
        Static(w, "Body", k);
        // Wear that reads from the top-down camera: roof rust, a scraped side, a dented front-left fender.
        float roofY = cabin[1].y + 0.005f;
        Static(w, "Rust", Rust((new Vector3(-halfW - 0.01f, 0.48f, -1.65f), new Vector3(0.04f, 0.2f, 0.55f)),
            (new Vector3(halfW + 0.01f, 0.5f, 1.9f), new Vector3(0.04f, 0.16f, 0.3f)),
            (new Vector3(0.35f, roofY, (cabin[1].x + cabin[2].x) * 0.5f - 0.2f), new Vector3(0.45f, 0.025f, 0.38f)),
            (new Vector3(-0.3f, roofY, (cabin[1].x + cabin[2].x) * 0.5f + 0.35f), new Vector3(0.2f, 0.025f, 0.16f))));
        var dent = new MeshKit();
        dent.Box((int)VS.Paint2, Vector3.zero, new Vector3(0.05f, 0.22f, 0.5f), 0.04f);
        for (int i = 0; i < 3; i++) dent.Box((int)VS.Chrome, new Vector3(0.02f, -0.06f + i * 0.06f, 0.1f * i - 0.1f), new Vector3(0.02f, 0.012f, 0.3f), 0.004f, new Vector3(8f * i, 0f, 0f));
        Static(w, "Dent", dent, new Vector3(-halfW - 0.005f, 0.82f, 1.65f), new Vector3(0f, -4f, 0f));

        // Detachable parts in dismantling order.
        int P(Primer which) => primer == which ? (int)VS.Steel : (int)VS.Paint;
        var hood = new MeshKit();
        hood.Box(P(Primer.Hood), Vector3.zero, new Vector3(1.62f, 0.05f, 1.02f), 0.025f);
        hood.Box((int)VS.Rust, new Vector3(-0.45f, 0.02f, 0.25f), new Vector3(0.32f, 0.02f, 0.26f), 0.01f);
        Part(w, "Hood", hood, new Vector3(0f, 1.0f, 1.56f), 0, new Vector3(11f, 0f, 0f));

        var door = new MeshKit();
        door.Box(P(Primer.DoorFL), Vector3.zero, new Vector3(0.06f, 0.58f, 1.04f), 0.025f);
        door.Box((int)VS.Chrome, new Vector3(-0.04f, 0.16f, -0.3f), new Vector3(0.03f, 0.04f, 0.16f), 0.01f);
        Part(w, "DoorFL", door, new Vector3(-halfW - 0.02f, 0.74f, 0.42f), 1);
        Part(w, "WheelFR", WheelKit(wr, ww), new Vector3(halfW - 0.08f, wr, axleF), 2);
        var bumper = new MeshKit();
        bumper.Box((int)VS.Chrome, Vector3.zero, new Vector3(2.04f, 0.2f, 0.2f), 0.07f, default, 2);
        bumper.Box((int)VS.Trim, new Vector3(0f, -0.05f, 0.06f), new Vector3(2f, 0.08f, 0.12f), 0.03f);
        Part(w, "BumperF", bumper, new Vector3(0f, 0.42f, len + 0.06f), 3);

        var lid = new MeshKit();
        if (style == CarStyle.Sedan) lid.Box(P(Primer.Lid), Vector3.zero, new Vector3(1.6f, 0.05f, 0.58f), 0.022f);
        else if (style == CarStyle.Hatch)
        {
            lid.Box(P(Primer.Lid), Vector3.zero, new Vector3(1.62f, 0.6f, 0.06f), 0.025f);
            lid.Box((int)VS.Glass, new Vector3(0f, 0.1f, -0.03f), new Vector3(1.36f, 0.3f, 0.03f), 0.01f);
        }
        else
        {
            lid.Box(P(Primer.Lid), Vector3.zero, new Vector3(1.9f, 0.36f, 0.07f), 0.025f);
            lid.Box((int)VS.Rust, new Vector3(0.5f, -0.08f, -0.04f), new Vector3(0.4f, 0.14f, 0.02f), 0.01f);
        }

        Vector3 lidPos = style switch
        {
            CarStyle.Sedan => new Vector3(0f, 1.04f, -len + 0.35f),
            CarStyle.Hatch => new Vector3(0f, 1.3f, -len - 0.01f),
            _ => new Vector3(0f, 1.2f, -len - 0.02f),
        };
        Part(w, "Lid", lid, lidPos, 4, style == CarStyle.Sedan ? new Vector3(-12f, 0f, 0f) : style == CarStyle.Hatch ? new Vector3(-6f, 0f, 0f) : default);
        Part(w, "WheelFL", WheelKit(wr, ww), new Vector3(-halfW + 0.08f, wr, axleF), 5);
        var rearDoor = new MeshKit();
        rearDoor.Box(P(Primer.DoorRR), Vector3.zero, new Vector3(0.06f, 0.56f, 0.92f), 0.025f);
        rearDoor.Box((int)VS.Chrome, new Vector3(0.04f, 0.16f, -0.25f), new Vector3(0.03f, 0.04f, 0.16f), 0.01f);
        if (style == CarStyle.Pickup) rearDoor.Box((int)VS.Rust, new Vector3(0.04f, -0.12f, 0.2f), new Vector3(0.02f, 0.18f, 0.3f), 0.01f);
        Part(w, "DoorRR", rearDoor, new Vector3(halfW + 0.02f, 0.72f, -0.68f), 6);
        var rearBumper = new MeshKit();
        rearBumper.Box((int)VS.Chrome, Vector3.zero, new Vector3(2.02f, 0.18f, 0.18f), 0.06f, default, 2);
        Part(w, "BumperR", rearBumper, new Vector3(0f, 0.42f, -len - 0.05f), 7);
        Part(w, "WheelRR", WheelKit(wr, ww), new Vector3(halfW - 0.08f, wr, axleR), 8);

        // Damage stages: engine smoke, cracked windscreen, heavy smoke + sparks as it falls apart.
        w.Smoke.Add(Effect(w, "VFX_ScrapSmoke", new Vector3(0.2f, 1.2f, 1.6f)));
        var cracks = new MeshKit();
        Crack(cracks, Vector3.zero, new Vector3(fa - 90f, 0f, 0f), 0.75f);
        var crackGo = ArtAssets.Part(w.Model, "Cracks", cracks, w.Table, w.Body,
            new Vector3(0.25f, (f0.y + f1.y) * 0.5f + 0.045f, (f0.x + f1.x) * 0.5f + 0.045f));
        w.Cracks.Add(crackGo);
        w.Heavy.Add(Effect(w, "VFX_ScrapSmoke", new Vector3(-0.4f, 1.3f, 0.2f), 1.5f));
        w.Heavy.Add(Effect(w, "VFX_ScrapSparksLoop", new Vector3(-0.8f, 0.6f, -1.2f)));
        // Sitting low on the missing rear-left wheel.
        w.Body.localRotation = Quaternion.Euler(-1.5f, 0f, 2f);
        w.SagAxis = new Vector3(0.5f, 0f, 1f).normalized;
        w.Sag1 = 2.5f;
        w.Sag2 = 5f;
        return Finish(w, path);
    }

    // =====================================================================================
    // Small scrap
    // =====================================================================================

    static GameObject BuildBarrel(string path, string model, ScrapDefinition def, Material paint, bool hazard)
    {
        var w = NewWreck(model, def, Table(paint));
        var k = new MeshKit();
        Vector2[] profile =
        {
            new(0.36f, 0f), new(0.4f, 0.035f), new(0.4f, 0.34f), new(0.425f, 0.36f), new(0.425f, 0.41f), new(0.4f, 0.43f), new(0.4f, 0.71f),
            new(0.425f, 0.73f), new(0.425f, 0.78f), new(0.4f, 0.8f), new(0.4f, 1.1f), new(0.37f, 1.13f),
        };
        k.Lathe((int)VS.Paint, Vector3.zero, profile, 22, default, true, 35f);
        if (hazard)
        {
            k.Cylinder((int)VS.Hazard, new Vector3(0f, 0.57f, 0f), 0.405f, 0.22f, 22, default, 0.005f);
            for (int i = 0; i < 6; i++)
                k.Box((int)VS.Trim, Quaternion.Euler(0f, i * 60f, 0f) * new Vector3(0f, 0.57f, 0.405f), new Vector3(0.1f, 0.24f, 0.012f), 0.004f,
                    new Vector3(0f, i * 60f, 30f));
        }
        else
        {
            k.Box((int)VS.White, new Vector3(0f, 0.57f, 0.4f), new Vector3(0.36f, 0.24f, 0.02f), 0.01f);
            k.Box((int)VS.Trim, new Vector3(0f, 0.6f, 0.412f), new Vector3(0.24f, 0.04f, 0.01f), 0.003f);
        }

        k.Box((int)VS.Rust, new Vector3(0.25f, 0.22f, -0.32f), new Vector3(0.18f, 0.3f, 0.04f), 0.02f, new Vector3(0f, 38f, 0f));
        k.Box((int)VS.Rust, new Vector3(-0.38f, 0.95f, 0.05f), new Vector3(0.04f, 0.18f, 0.16f), 0.015f);
        Static(w, "Drum", k);
        var lid = new MeshKit();
        lid.Cylinder((int)VS.Paint, Vector3.zero, 0.375f, 0.04f, 22, default, 0.01f);
        lid.Torus((int)VS.Paint, new Vector3(0f, 0.02f, 0f), 0.36f, 0.025f, 22, 6);
        lid.Cylinder((int)VS.Chrome, new Vector3(0.18f, 0.03f, 0.05f), 0.05f, 0.04f, 10, default, 0.01f);
        Part(w, "Lid", lid, new Vector3(0f, 1.15f, 0f), 0, new Vector3(4f, 0f, -3f));
        w.Cracks.Add(Effect(w, "VFX_ScrapSmoke", new Vector3(0f, 1.2f, 0f), 0.6f));
        w.SagAxis = Vector3.right;
        w.Sag1 = 4f;
        w.Sag2 = 9f;
        return Finish(w, path, 1f);
    }

    static void Tire(MeshKit k, Vector3 pos, float yaw, float r = 0.68f, float width = 0.42f)
    {
        float ri = r * 0.48f;
        k.Push(pos, new Vector3(0f, yaw, 0f));
        k.Lathe((int)VS.Rubber, Vector3.zero, new[]
        {
            new Vector2(ri, -width * 0.5f + 0.04f), new Vector2(ri + 0.05f, -width * 0.5f), new Vector2(r - 0.08f, -width * 0.5f), new Vector2(r, -width * 0.5f + 0.08f),
            new Vector2(r, width * 0.5f - 0.08f), new Vector2(r - 0.08f, width * 0.5f), new Vector2(ri + 0.05f, width * 0.5f), new Vector2(ri, width * 0.5f - 0.04f),
        }, 24, default, false, 40f, true);
        int n = 22;
        for (int i = 0; i < n; i++)
        {
            float a = 360f * i / n;
            var d = Quaternion.Euler(0f, a, 0f) * Vector3.right;
            k.Box((int)VS.Rubber, d * (r + 0.012f), new Vector3(0.05f, width * 0.78f, 0.12f), 0.015f, new Vector3(0f, -a + 25f, 0f));
        }

        k.Pop();
    }

    static GameObject BuildTires(string path, string model, ScrapDefinition def)
    {
        var w = NewWreck(model, def, Table(Paint("TireRim", new Color(0.75f, 0.2f, 0.15f))));
        var k = new MeshKit();
        Tire(k, new Vector3(0f, 0.21f, 0f), 0f);
        k.Cylinder((int)VS.Paint, new Vector3(0f, 0.21f, 0f), 0.33f, 0.3f, 16, default, 0.03f);
        k.Cylinder((int)VS.Chrome, new Vector3(0f, 0.37f, 0f), 0.12f, 0.03f, 12, default, 0.01f);
        Static(w, "Base", k);
        var t2 = new MeshKit();
        Tire(t2, Vector3.zero, 0f);
        Part(w, "Tire2", t2, new Vector3(0.05f, 0.64f, 0.03f), 1, new Vector3(3f, 37f, 0f));
        var t3 = new MeshKit();
        Tire(t3, Vector3.zero, 0f, 0.62f, 0.38f);
        Part(w, "Tire3", t3, new Vector3(-0.06f, 1.05f, -0.02f), 0, new Vector3(-6f, 12f, 5f));
        w.SagAxis = Vector3.right;
        w.Sag1 = 3f;
        w.Sag2 = 6f;
        return Finish(w, path, 0.95f);
    }

    static GameObject BuildFridge(string path, string model, ScrapDefinition def, Material paint)
    {
        var w = NewWreck(model, def, Table(paint));
        const float W = 1.3f, H = 2.35f, D = 0.95f;
        var k = new MeshKit();
        k.Box((int)VS.Paint, new Vector3(0f, H * 0.5f + 0.08f, 0f), new Vector3(W, H, D), 0.16f, default, 2);
        k.Box((int)VS.White, new Vector3(0f, H * 0.5f + 0.08f, D * 0.5f - 0.03f), new Vector3(W - 0.18f, H - 0.18f, 0.06f), 0.03f);
        for (int i = 0; i < 4; i++) k.Box((int)VS.Glass, new Vector3(0f, 0.55f + i * 0.42f, D * 0.5f), new Vector3(W - 0.26f, 0.03f, 0.05f), 0.01f);
        k.Box((int)VS.Wood, new Vector3(-0.25f, 0.95f, D * 0.5f + 0.02f), new Vector3(0.22f, 0.2f, 0.12f), 0.04f);
        k.Box((int)VS.Tail, new Vector3(0.25f, 1.38f, D * 0.5f + 0.02f), new Vector3(0.16f, 0.22f, 0.12f), 0.05f);
        for (int x = -1; x <= 1; x += 2)
        for (int z = -1; z <= 1; z += 2)
            k.Cylinder((int)VS.Trim, new Vector3(x * (W * 0.5f - 0.15f), 0.04f, z * (D * 0.5f - 0.15f)), 0.06f, 0.08f, 8, default, 0.01f);
        k.Box((int)VS.Rust, new Vector3(W * 0.5f + 0.005f, 0.3f, 0.1f), new Vector3(0.02f, 0.3f, 0.4f), 0.01f);
        k.Box((int)VS.Rust, new Vector3(-0.4f, H + 0.07f, -0.2f), new Vector3(0.3f, 0.02f, 0.25f), 0.01f);
        Static(w, "Body", k);
        var freezer = new MeshKit();
        freezer.Box((int)VS.Paint, Vector3.zero, new Vector3(W - 0.04f, 0.62f, 0.1f), 0.05f, default, 2);
        freezer.Box((int)VS.Chrome, new Vector3(W * 0.36f, -0.12f, 0.08f), new Vector3(0.06f, 0.26f, 0.06f), 0.02f);
        Part(w, "Freezer", freezer, new Vector3(0f, H - 0.27f + 0.08f, D * 0.5f + 0.04f), 0);
        var main = new MeshKit();
        main.Box((int)VS.Paint, Vector3.zero, new Vector3(W - 0.04f, H - 0.72f, 0.1f), 0.05f, default, 2);
        main.Box((int)VS.Chrome, new Vector3(W * 0.36f, 0.4f, 0.08f), new Vector3(0.06f, 0.42f, 0.06f), 0.02f);
        main.Box((int)VS.Chrome, new Vector3(0f, -0.25f, 0.06f), new Vector3(0.32f, 0.07f, 0.02f), 0.01f);
        Part(w, "Door", main, new Vector3(0f, (H - 0.72f) * 0.5f + 0.1f, D * 0.5f + 0.04f), 1);
        w.Cracks.Add(Effect(w, "VFX_ScrapSmoke", new Vector3(0f, 0.6f, -0.3f), 0.7f));
        w.SagAxis = Vector3.forward;
        w.Sag1 = 3f;
        w.Sag2 = 7f;
        return Finish(w, path, 0.97f);
    }

    static GameObject BuildWasher(string path, string model, ScrapDefinition def)
    {
        var w = NewWreck(model, def, Table(Mat("ScrapApplianceBlue", new Color(0.62f, 0.78f, 0.92f), 0.55f, 0f, Detail.Paint)));
        const float W = 1.15f, H = 1.4f, D = 1.05f;
        var k = new MeshKit();
        k.Box((int)VS.White, new Vector3(0f, H * 0.5f + 0.05f, 0f), new Vector3(W, H, D), 0.1f, default, 2);
        k.Box((int)VS.Paint, new Vector3(0f, H - 0.08f, D * 0.5f - 0.01f), new Vector3(W - 0.06f, 0.24f, 0.06f), 0.04f);
        k.Box((int)VS.Light, new Vector3(0.18f, H - 0.08f, D * 0.5f + 0.025f), new Vector3(0.26f, 0.1f, 0.03f), 0.015f);
        for (int i = 0; i < 2; i++) k.Cylinder((int)VS.Chrome, new Vector3(-0.38f + i * 0.17f, H - 0.08f, D * 0.5f + 0.03f), 0.05f, 0.05f, 12, new Vector3(90f, 0f, 0f), 0.012f);
        k.Cylinder((int)VS.Steel, new Vector3(0f, 0.68f, D * 0.5f - 0.02f), 0.36f, 0.06f, 22, new Vector3(90f, 0f, 0f), 0.01f);
        k.Cylinder((int)VS.Interior, new Vector3(0f, 0.68f, D * 0.5f + 0.005f), 0.28f, 0.02f, 18, new Vector3(90f, 0f, 0f), 0.003f);
        k.Box((int)VS.Rust, new Vector3(-W * 0.5f - 0.005f, 0.25f, 0.2f), new Vector3(0.02f, 0.22f, 0.3f), 0.01f);
        Static(w, "Body", k);
        var door = new MeshKit();
        door.Torus((int)VS.Chrome, Vector3.zero, 0.33f, 0.06f, 22, 8, new Vector3(90f, 0f, 0f));
        door.Cylinder((int)VS.Glass, Vector3.zero, 0.3f, 0.04f, 22, new Vector3(90f, 0f, 0f), 0.01f);
        door.Box((int)VS.Chrome, new Vector3(0.36f, 0f, 0.03f), new Vector3(0.06f, 0.16f, 0.06f), 0.02f);
        Part(w, "Door", door, new Vector3(0f, 0.68f, D * 0.5f + 0.06f), 0);
        var top = new MeshKit();
        top.Box((int)VS.White, Vector3.zero, new Vector3(W + 0.02f, 0.06f, D + 0.02f), 0.03f);
        Part(w, "Top", top, new Vector3(0f, H + 0.07f, 0f), 1, new Vector3(0f, 6f, 2f));
        w.Cracks.Add(Effect(w, "VFX_ScrapSmoke", new Vector3(0f, H, 0f), 0.6f));
        w.Sag1 = 3f;
        w.Sag2 = 6f;
        return Finish(w, path, 0.97f);
    }

    static GameObject BuildStove(string path, string model, ScrapDefinition def)
    {
        var w = NewWreck(model, def, Table(Mat("ScrapApplianceCream", new Color(0.97f, 0.9f, 0.74f), 0.55f, 0f, Detail.Paint)));
        const float W = 1.3f, H = 1.05f, D = 1.05f;
        var k = new MeshKit();
        k.Box((int)VS.Paint, new Vector3(0f, H * 0.5f + 0.05f, 0f), new Vector3(W, H, D), 0.08f, default, 2);
        k.Box((int)VS.Trim, new Vector3(0f, H + 0.07f, 0f), new Vector3(W - 0.04f, 0.04f, D - 0.04f), 0.015f);
        for (int x = -1; x <= 1; x += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            k.Cylinder((int)VS.Steel, new Vector3(x * 0.3f, H + 0.1f, z * 0.24f), 0.17f, 0.03f, 16, default, 0.008f);
            k.Torus((int)VS.Trim, new Vector3(x * 0.3f, H + 0.12f, z * 0.24f), 0.11f, 0.018f, 14, 6);
        }

        k.Box((int)VS.Paint, new Vector3(0f, H + 0.32f, -D * 0.5f + 0.06f), new Vector3(W, 0.5f, 0.12f), 0.05f);
        k.Box((int)VS.Light, new Vector3(0f, H + 0.4f, -D * 0.5f + 0.125f), new Vector3(0.3f, 0.14f, 0.02f), 0.01f);
        for (int i = 0; i < 4; i++)
            k.Cylinder((int)VS.Chrome, new Vector3(-0.45f + i * 0.3f, H - 0.08f, D * 0.5f + 0.02f), 0.05f, 0.05f, 10, new Vector3(90f, 0f, 0f), 0.01f);
        k.Box((int)VS.Rust, new Vector3(W * 0.5f + 0.004f, 0.3f, -0.1f), new Vector3(0.02f, 0.25f, 0.35f), 0.01f);
        Static(w, "Body", k);
        var door = new MeshKit();
        door.Box((int)VS.Paint, Vector3.zero, new Vector3(W - 0.12f, 0.62f, 0.08f), 0.04f);
        door.Box((int)VS.Glass, new Vector3(0f, -0.04f, 0.045f), new Vector3(W - 0.5f, 0.3f, 0.02f), 0.02f);
        door.Box((int)VS.Chrome, new Vector3(0f, 0.24f, 0.07f), new Vector3(W - 0.4f, 0.05f, 0.05f), 0.02f);
        Part(w, "OvenDoor", door, new Vector3(0f, 0.43f, D * 0.5f + 0.03f), 0);
        w.Cracks.Add(Effect(w, "VFX_ScrapSmoke", new Vector3(0f, H + 0.2f, 0f), 0.6f));
        w.Sag1 = 2f;
        w.Sag2 = 5f;
        return Finish(w, path, 0.97f);
    }

    static GameObject BuildKart(string path, string model, ScrapDefinition def, Material paint)
    {
        var w = NewWreck(model, def, Table(paint));
        const float wr = 0.26f;
        var k = new MeshKit();
        // Tube chassis, side pods, seat, engine and exhaust at the back.
        k.Box((int)VS.Trim, new Vector3(0f, 0.18f, 0f), new Vector3(1.1f, 0.08f, 2.1f), 0.03f);
        for (int side = -1; side <= 1; side += 2)
        {
            k.Box((int)VS.Paint, new Vector3(side * 0.62f, 0.32f, 0f), new Vector3(0.26f, 0.22f, 1.0f), 0.08f, default, 2);
            k.Cylinder((int)VS.Steel, new Vector3(side * 0.5f, 0.24f, 0f), 0.03f, 2.0f, 8, new Vector3(90f, 0f, 0f), 0.008f);
        }

        k.Box((int)VS.Interior, new Vector3(0f, 0.45f, -0.35f), new Vector3(0.52f, 0.12f, 0.5f), 0.05f);
        k.Box((int)VS.Interior, new Vector3(0f, 0.75f, -0.62f), new Vector3(0.52f, 0.55f, 0.12f), 0.05f, new Vector3(-12f, 0f, 0f));
        k.Box((int)VS.Steel, new Vector3(0.28f, 0.45f, -0.85f), new Vector3(0.4f, 0.32f, 0.36f), 0.06f);
        k.Cylinder((int)VS.Chrome, new Vector3(0.45f, 0.55f, -1.05f), 0.05f, 0.3f, 8, new Vector3(70f, 0f, 0f), 0.01f);
        k.Cylinder((int)VS.Trim, new Vector3(0f, 0.6f, 0.45f), 0.03f, 0.5f, 8, new Vector3(-55f, 0f, 0f), 0.008f);
        k.Torus((int)VS.Trim, new Vector3(0f, 0.8f, 0.3f), 0.17f, 0.03f, 14, 6, new Vector3(-55f, 0f, 0f));
        k.Box((int)VS.White, new Vector3(0f, 0.42f, 0.9f), new Vector3(0.6f, 0.06f, 0.4f), 0.03f);
        k.Box((int)VS.Rust, new Vector3(-0.75f, 0.33f, 0.3f), new Vector3(0.02f, 0.12f, 0.3f), 0.01f);
        k.Cylinder((int)VS.Steel, new Vector3(-0.72f, wr, 0.82f), 0.11f, 0.12f, 10, new Vector3(0f, 0f, 90f), 0.02f);
        Static(w, "Body", k);
        var nose = new MeshKit();
        nose.Prism((int)VS.Paint, Vector3.zero, new[] { new Vector2(-0.3f, 0f), new Vector2(-0.3f, 0.2f), new Vector2(0.25f, 0.12f), new Vector2(0.3f, 0f) }, 1.0f, 0.06f);
        nose.Box((int)VS.Hazard, new Vector3(0f, 0.12f, 0f), new Vector3(0.3f, 0.12f, 0.4f), 0.03f, new Vector3(-8f, 0f, 0f));
        Part(w, "Nose", nose, new Vector3(0f, 0.2f, 1.08f), 0);
        Part(w, "WheelFR", WheelKit(wr, 0.24f), new Vector3(0.72f, wr, 0.82f), 1);
        Part(w, "WheelRL", WheelKit(wr + 0.04f, 0.32f), new Vector3(-0.74f, wr + 0.04f, -0.78f), 2);
        Part(w, "WheelRR", WheelKit(wr + 0.04f, 0.32f), new Vector3(0.74f, wr + 0.04f, -0.78f), 3);
        w.Cracks.Add(Effect(w, "VFX_ScrapSmoke", new Vector3(0.28f, 0.7f, -0.85f), 0.7f));
        w.Body.localRotation = Quaternion.Euler(0f, 0f, 3f);
        w.Sag1 = 3f;
        w.Sag2 = 6f;
        return Finish(w, path, 0.95f);
    }

    // =====================================================================================
    // Heavy vehicles
    // =====================================================================================

    static GameObject BuildTractor(string path, string model, ScrapDefinition def)
    {
        var paint = Paint("TractorGreen", new Color(0.24f, 0.62f, 0.26f));
        var w = NewWreck(model, def, Table(paint, Paint("TractorYellow", new Color(1f, 0.78f, 0.12f))));
        var k = new MeshKit();
        // Hood and engine block forward, cab over the rear axle, big rear wheels with fenders.
        k.Box((int)VS.Paint, new Vector3(0f, 1.15f, 0.85f), new Vector3(1.05f, 0.85f, 2.1f), 0.18f, default, 2);
        k.Box((int)VS.Trim, new Vector3(0f, 0.72f, 0.7f), new Vector3(0.8f, 0.4f, 2.2f), 0.06f);
        k.Box((int)VS.Trim, new Vector3(0f, 1.1f, 1.92f), new Vector3(0.9f, 0.6f, 0.06f), 0.03f);
        for (int i = 0; i < 5; i++) k.Box((int)VS.Chrome, new Vector3(0f, 0.88f + i * 0.11f, 1.955f), new Vector3(0.82f, 0.03f, 0.02f), 0.006f);
        for (int side = -1; side <= 1; side += 2)
        {
            k.Box((int)VS.Light, new Vector3(side * 0.42f, 1.42f, 1.9f), new Vector3(0.18f, 0.14f, 0.08f), 0.04f);
            k.Box((int)VS.Paint, new Vector3(side * 0.98f, 1.55f, -0.95f), new Vector3(0.5f, 0.12f, 1.45f), 0.05f, new Vector3(0f, 0f, side * 6f));
        }

        k.Box((int)VS.Paint2, new Vector3(0f, 1.28f, -0.95f), new Vector3(1.4f, 0.24f, 1.5f), 0.08f);
        k.Box((int)VS.Interior, new Vector3(0f, 1.6f, -1.1f), new Vector3(0.6f, 0.3f, 0.6f), 0.08f);
        // Cab frame (glass + posts) and roof.
        k.Box((int)VS.Glass, new Vector3(0f, 2.35f, -0.95f), new Vector3(1.32f, 1.2f, 1.3f), 0.04f);
        for (int x = -1; x <= 1; x += 2)
        for (int z = -1; z <= 1; z += 2)
            k.Box((int)VS.Trim, new Vector3(x * 0.67f, 2.35f, -0.95f + z * 0.66f), new Vector3(0.08f, 1.24f, 0.08f), 0.02f);
        k.Cylinder((int)VS.Chrome, new Vector3(0.3f, 2.25f, 1.25f), 0.07f, 1.4f, 10, default, 0.015f);
        k.Cylinder((int)VS.Trim, new Vector3(0.3f, 2.97f, 1.25f), 0.09f, 0.08f, 10, default, 0.015f);
        Wheel(k, new Vector3(-0.95f, 0.85f, -0.95f), 0.85f, 0.55f);
        k.Box((int)VS.Rust, new Vector3(0.53f, 1.0f, 1.3f), new Vector3(0.02f, 0.3f, 0.4f), 0.01f);
        k.Box((int)VS.Rust, new Vector3(-0.3f, 1.58f, 0.5f), new Vector3(0.35f, 0.02f, 0.3f), 0.01f);
        Static(w, "Body", k);
        var roof = new MeshKit();
        roof.Box((int)VS.Paint2, Vector3.zero, new Vector3(1.55f, 0.12f, 1.55f), 0.05f, default, 2);
        roof.Box((int)VS.Light, new Vector3(0f, 0.08f, 0.6f), new Vector3(0.5f, 0.06f, 0.1f), 0.02f);
        Part(w, "Roof", roof, new Vector3(0f, 3.02f, -0.95f), 0);
        Part(w, "WheelFL", WheelKit(0.48f, 0.34f), new Vector3(-0.7f, 0.48f, 1.25f), 1);
        Part(w, "WheelRR", WheelKit(0.85f, 0.55f), new Vector3(0.95f, 0.85f, -0.95f), 2);
        var hood = new MeshKit();
        hood.Box((int)VS.Paint, Vector3.zero, new Vector3(1.08f, 0.1f, 1.5f), 0.05f);
        Part(w, "HoodTop", hood, new Vector3(0f, 1.6f, 1.0f), 3);
        Part(w, "WheelFR", WheelKit(0.48f, 0.34f), new Vector3(0.7f, 0.48f, 1.25f), 4);
        w.Smoke.Add(Effect(w, "VFX_ScrapSmoke", new Vector3(0.3f, 3.0f, 1.25f), 1.2f));
        w.Heavy.Add(Effect(w, "VFX_ScrapSparksLoop", new Vector3(0f, 1.2f, 1.6f)));
        w.Sag1 = 2.5f;
        w.Sag2 = 5f;
        return Finish(w, path);
    }

    static GameObject BuildExcavator(string path, string model, ScrapDefinition def)
    {
        var w = NewWreck(model, def, Table(Paint("ExcavatorYellow", new Color(1f, 0.74f, 0.08f)), Paint("ExcavatorBlack", new Color(0.2f, 0.2f, 0.22f))));
        var k = new MeshKit();
        // Tracks.
        for (int side = -1; side <= 1; side += 2)
        {
            k.Box((int)VS.Rubber, new Vector3(side * 0.95f, 0.38f, 0f), new Vector3(0.6f, 0.72f, 3.4f), 0.3f, default, 2);
            for (int i = 0; i < 4; i++) k.Cylinder((int)VS.Steel, new Vector3(side * 0.95f, 0.36f, -1.15f + i * 0.77f), 0.2f, 0.64f, 12, new Vector3(0f, 0f, 90f), 0.03f);
            for (int i = 0; i < 14; i++) k.Box((int)VS.Rubber, new Vector3(side * 0.95f, 0.74f, -1.6f + i * 0.245f), new Vector3(0.62f, 0.05f, 0.12f), 0.015f);
        }

        k.Box((int)VS.Paint2, new Vector3(0f, 0.78f, 0f), new Vector3(1.4f, 0.3f, 1.8f), 0.06f);
        k.Cylinder((int)VS.Paint2, new Vector3(0f, 0.97f, 0f), 0.8f, 0.12f, 20, default, 0.02f);
        // Upper house: cab on the left, engine cover and counterweight behind.
        k.Push(new Vector3(0f, 1.05f, 0f), new Vector3(0f, -18f, 0f));
        k.Box((int)VS.Paint, new Vector3(0.1f, 0.42f, -0.35f), new Vector3(2.1f, 0.62f, 2.2f), 0.14f, default, 2);
        k.Box((int)VS.Paint, new Vector3(0.1f, 0.42f, -1.45f), new Vector3(2.1f, 0.62f, 0.4f), 0.16f, default, 2);
        k.Box((int)VS.Hazard, new Vector3(0.1f, 0.42f, -1.66f), new Vector3(1.9f, 0.22f, 0.04f), 0.01f);
        k.Box((int)VS.Paint, new Vector3(-0.5f, 1.25f, 0.25f), new Vector3(0.9f, 1.15f, 1.0f), 0.1f, default, 2);
        k.Box((int)VS.Glass, new Vector3(-0.5f, 1.32f, 0.27f), new Vector3(0.92f, 0.8f, 0.92f), 0.06f);
        k.Box((int)VS.Paint2, new Vector3(-0.5f, 1.85f, 0.25f), new Vector3(0.96f, 0.08f, 1.05f), 0.03f);
        k.Cylinder((int)VS.Chrome, new Vector3(0.7f, 1.05f, -0.9f), 0.06f, 0.6f, 8, default, 0.015f);
        k.Box((int)VS.Trim, new Vector3(0.55f, 0.75f, -0.5f), new Vector3(0.8f, 0.06f, 0.9f), 0.02f);
        // Boom base.
        k.Box((int)VS.Paint, new Vector3(0.35f, 0.55f, 0.85f), new Vector3(0.5f, 0.5f, 0.5f), 0.1f);
        k.Pop();
        k.Box((int)VS.Rust, new Vector3(1.26f, 0.5f, 0.8f), new Vector3(0.02f, 0.25f, 0.5f), 0.01f);
        Static(w, "Body", k);
        // Boom + stick + bucket (detach in pieces).
        var boom = new MeshKit();
        boom.Box((int)VS.Paint, new Vector3(0f, 0f, 0.8f), new Vector3(0.36f, 0.42f, 1.8f), 0.1f, new Vector3(-28f, 0f, 0f), 2);
        boom.Cylinder((int)VS.Chrome, new Vector3(0.24f, -0.1f, 0.65f), 0.07f, 1.2f, 10, new Vector3(62f, 0f, 0f), 0.015f);
        Part(w, "Boom", boom, new Vector3(0.6f, 1.6f, 0.95f), 2, new Vector3(0f, -18f, 0f));
        var stick = new MeshKit();
        stick.Box((int)VS.Paint, new Vector3(0f, -0.5f, 0f), new Vector3(0.3f, 1.3f, 0.34f), 0.08f, default, 2);
        Part(w, "Stick", stick, new Vector3(0.95f, 2.0f, 2.2f), 1, new Vector3(-25f, -18f, 0f));
        var bucket = new MeshKit();
        bucket.Prism((int)VS.Paint2, Vector3.zero, new[] { new Vector2(-0.3f, 0.3f), new Vector2(0.25f, 0.35f), new Vector2(0.4f, -0.1f), new Vector2(0.05f, -0.35f),
            new Vector2(-0.3f, -0.2f) }, 0.8f, 0.05f);
        for (int i = 0; i < 4; i++) bucket.Box((int)VS.Steel, new Vector3(-0.3f + i * 0.2f, -0.38f, 0.1f), new Vector3(0.07f, 0.1f, 0.12f), 0.02f);
        Part(w, "Bucket", bucket, new Vector3(1.15f, 0.55f, 2.6f), 0, new Vector3(0f, -18f, 0f));
        var door = new MeshKit();
        door.Box((int)VS.Paint, Vector3.zero, new Vector3(0.06f, 0.9f, 0.6f), 0.03f);
        door.Box((int)VS.Glass, new Vector3(-0.035f, 0.18f, 0f), new Vector3(0.02f, 0.45f, 0.45f), 0.01f);
        Part(w, "CabDoor", door, new Vector3(-1.02f, 2.35f, 0.55f), 3, new Vector3(0f, -18f, 0f));
        w.Smoke.Add(Effect(w, "VFX_ScrapSmoke", new Vector3(0.6f, 2.2f, -1.1f), 1.1f));
        w.Heavy.Add(Effect(w, "VFX_ScrapSparksLoop", new Vector3(0.9f, 1.2f, 1.6f)));
        w.Sag1 = 2f;
        w.Sag2 = 4f;
        return Finish(w, path);
    }

    static void TruckChassis(MeshKit k, float len, float wheelR, bool dual)
    {
        k.Box((int)VS.Trim, new Vector3(0f, 0.62f, -0.1f), new Vector3(1.6f, 0.3f, len * 2f - 0.6f), 0.06f);
        float[] axles = { len - 1.0f, -len + 1.15f };
        foreach (float z in axles)
            for (int side = -1; side <= 1; side += 2)
            {
                Arch(k, new Vector3(side * 1.26f, wheelR, z), wheelR + 0.1f);
                if (dual && z < 0f) Wheel(k, new Vector3(side * 0.82f, wheelR, z), wheelR, 0.34f, false);
            }
    }

    static void Cab(MeshKit k, float frontZ, Material _, bool flatNose = true)
    {
        k.Box((int)VS.Paint, new Vector3(0f, 1.5f, frontZ - 0.75f), new Vector3(2.4f, 1.9f, 1.5f), 0.2f, default, 2);
        k.Box((int)VS.Glass, new Vector3(0f, 1.95f, frontZ - 0.05f), new Vector3(2.0f, 0.75f, 0.12f), 0.05f);
        for (int side = -1; side <= 1; side += 2)
        {
            k.Box((int)VS.Glass, new Vector3(side * 1.2f, 1.95f, frontZ - 0.65f), new Vector3(0.06f, 0.6f, 0.8f), 0.03f);
            k.Box((int)VS.Light, new Vector3(side * 0.85f, 0.95f, frontZ + 0.02f), new Vector3(0.36f, 0.2f, 0.08f), 0.04f);
            k.Box((int)VS.Trim, new Vector3(side * 1.32f, 1.95f, frontZ - 0.15f), new Vector3(0.08f, 0.35f, 0.18f), 0.03f);
        }

        k.Box((int)VS.Trim, new Vector3(0f, 0.95f, frontZ + 0.03f), new Vector3(1.1f, 0.35f, 0.06f), 0.03f);
        for (int i = 0; i < 4; i++) k.Box((int)VS.Chrome, new Vector3(0f, 0.83f + i * 0.08f, frontZ + 0.065f), new Vector3(1.0f, 0.025f, 0.02f), 0.005f);
        k.Box((int)VS.Hazard, new Vector3(0f, 2.48f, frontZ - 0.6f), new Vector3(1.2f, 0.12f, 0.25f), 0.04f);
    }

    static GameObject BuildBoxTruck(string path, string model, ScrapDefinition def)
    {
        var w = NewWreck(model, def, Table(Paint("TruckBlue", new Color(0.17f, 0.45f, 0.86f)), Mat("TruckBox", new Color(0.95f, 0.93f, 0.88f), 0.45f, 0f, Detail.Paint)));
        const float len = 2.7f, wr = 0.5f;
        var k = new MeshKit();
        TruckChassis(k, len, wr, true);
        Cab(k, len, null);
        // Cargo box with a colour band and roll-up door frame.
        k.Box((int)VS.Paint2, new Vector3(0f, 1.85f, -0.95f), new Vector3(2.45f, 2.3f, 3.4f), 0.12f, default, 2);
        k.Box((int)VS.Paint, new Vector3(0f, 1.5f, -0.95f), new Vector3(2.48f, 0.35f, 3.3f), 0.04f);
        k.Box((int)VS.Hazard, new Vector3(0f, 1.15f, -0.95f), new Vector3(2.47f, 0.08f, 3.3f), 0.02f);
        k.Box((int)VS.Trim, new Vector3(0f, 1.85f, -2.66f), new Vector3(2.3f, 2.2f, 0.04f), 0.02f);
        for (int side = -1; side <= 1; side += 2) k.Box((int)VS.Tail, new Vector3(side * 1.0f, 0.75f, -2.68f), new Vector3(0.24f, 0.14f, 0.06f), 0.03f);
        k.Box((int)VS.Rust, new Vector3(1.235f, 2.5f, -1.8f), new Vector3(0.02f, 0.35f, 0.6f), 0.01f);
        k.Box((int)VS.Rust, new Vector3(-1.21f, 1.2f, 1.9f), new Vector3(0.02f, 0.3f, 0.4f), 0.01f);
        Wheel(k, new Vector3(-1.1f, wr, -len + 1.15f), wr, 0.38f);
        Static(w, "Body", k);
        var rollUp = new MeshKit();
        for (int i = 0; i < 8; i++) rollUp.Box((int)VS.Paint2, new Vector3(0f, -0.95f + i * 0.27f, 0f), new Vector3(2.15f, 0.25f, 0.06f), 0.03f);
        rollUp.Box((int)VS.Chrome, new Vector3(0f, -0.92f, 0.05f), new Vector3(0.4f, 0.06f, 0.05f), 0.02f);
        Part(w, "CargoDoor", rollUp, new Vector3(0f, 1.95f, -2.71f), 0);
        var door = new MeshKit();
        door.Box((int)VS.Paint, Vector3.zero, new Vector3(0.07f, 1.1f, 0.95f), 0.04f);
        door.Box((int)VS.Glass, new Vector3(-0.04f, 0.28f, 0f), new Vector3(0.02f, 0.45f, 0.75f), 0.02f);
        Part(w, "CabDoor", door, new Vector3(-1.23f, 1.65f, len - 0.7f), 1);
        Part(w, "WheelFR", WheelKit(wr, 0.38f), new Vector3(1.1f, wr, len - 1.0f), 2);
        var bumper = new MeshKit();
        bumper.Box((int)VS.Chrome, Vector3.zero, new Vector3(2.5f, 0.3f, 0.25f), 0.08f, default, 2);
        Part(w, "Bumper", bumper, new Vector3(0f, 0.55f, len + 0.1f), 3);
        Part(w, "WheelFL", WheelKit(wr, 0.38f), new Vector3(-1.1f, wr, len - 1.0f), 4);
        w.Smoke.Add(Effect(w, "VFX_ScrapSmoke", new Vector3(0.3f, 2.6f, len - 0.4f), 1.2f));
        w.Heavy.Add(Effect(w, "VFX_ScrapSparksLoop", new Vector3(-1f, 1.2f, 0.5f)));
        w.Sag1 = 1.5f;
        w.Sag2 = 3.5f;
        return Finish(w, path);
    }

    static GameObject BuildFlatbed(string path, string model, ScrapDefinition def)
    {
        var w = NewWreck(model, def, Table(Paint("TruckOrange", new Color(1f, 0.46f, 0.1f))));
        const float len = 2.7f, wr = 0.5f;
        var k = new MeshKit();
        TruckChassis(k, len, wr, true);
        Cab(k, len, null);
        k.Box((int)VS.Wood, new Vector3(0f, 1.0f, -0.95f), new Vector3(2.45f, 0.14f, 3.5f), 0.03f);
        for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 4; i++)
                k.Box((int)VS.Steel, new Vector3(side * 1.18f, 1.3f, -2.4f + i * 0.95f), new Vector3(0.08f, 0.5f, 0.08f), 0.02f);
        // Load: pipe bundle (stays) and crates (fly off).
        for (int i = 0; i < 5; i++)
            k.Cylinder((int)VS.Steel, new Vector3(-0.6f + (i % 3) * 0.28f, 1.23f + (i / 3) * 0.24f, -1.4f), 0.13f, 1.9f, 12, new Vector3(90f, 0f, 0f), 0.02f);
        k.Box((int)VS.Rust, new Vector3(-1.21f, 1.2f, 1.9f), new Vector3(0.02f, 0.3f, 0.4f), 0.01f);
        Wheel(k, new Vector3(-1.1f, wr, -len + 1.15f), wr, 0.38f);
        Static(w, "Body", k);
        var crate = new MeshKit();
        crate.Box((int)VS.Wood, Vector3.zero, new Vector3(0.8f, 0.7f, 0.8f), 0.05f, default, 2);
        crate.Box((int)VS.Steel, new Vector3(0f, 0f, 0.41f), new Vector3(0.82f, 0.1f, 0.02f), 0.01f);
        crate.Box((int)VS.Steel, new Vector3(0f, 0f, -0.41f), new Vector3(0.82f, 0.1f, 0.02f), 0.01f);
        Part(w, "Crate1", crate, new Vector3(0.65f, 1.43f, -0.45f), 0, new Vector3(0f, 12f, 0f));
        Part(w, "Crate2", crate, new Vector3(0.6f, 1.43f, -1.6f), 2, new Vector3(0f, -8f, 0f));
        var door = new MeshKit();
        door.Box((int)VS.Paint, Vector3.zero, new Vector3(0.07f, 1.1f, 0.95f), 0.04f);
        door.Box((int)VS.Glass, new Vector3(-0.04f, 0.28f, 0f), new Vector3(0.02f, 0.45f, 0.75f), 0.02f);
        Part(w, "CabDoor", door, new Vector3(-1.23f, 1.65f, len - 0.7f), 1);
        Part(w, "WheelFR", WheelKit(wr, 0.38f), new Vector3(1.1f, wr, len - 1.0f), 3);
        Part(w, "WheelFL", WheelKit(wr, 0.38f), new Vector3(-1.1f, wr, len - 1.0f), 4);
        w.Smoke.Add(Effect(w, "VFX_ScrapSmoke", new Vector3(0.3f, 2.6f, len - 0.4f), 1.2f));
        w.Heavy.Add(Effect(w, "VFX_ScrapSparksLoop", new Vector3(1f, 1.2f, 0.5f)));
        w.Sag1 = 1.5f;
        w.Sag2 = 3.5f;
        return Finish(w, path);
    }

    static GameObject BuildGarbageTruck(string path, string model, ScrapDefinition def)
    {
        var w = NewWreck(model, def, Table(Paint("GarbageGreen", new Color(0.26f, 0.66f, 0.3f)), Mat("GarbageWhite", new Color(0.95f, 0.95f, 0.93f), 0.5f, 0f, Detail.Paint)));
        const float len = 3.0f, wr = 0.55f;
        var k = new MeshKit();
        TruckChassis(k, len, wr, true);
        Cab(k, len, null);
        // Compactor body: rounded box with ribs, rear hopper with a lifter arm.
        k.Box((int)VS.Paint, new Vector3(0f, 2.0f, -0.75f), new Vector3(2.55f, 2.4f, 3.4f), 0.35f, default, 2);
        for (int i = 0; i < 5; i++) k.Box((int)VS.Paint, new Vector3(0f, 2.0f, -2.2f + i * 0.7f), new Vector3(2.62f, 2.3f, 0.12f), 0.06f);
        k.Box((int)VS.Paint2, new Vector3(0f, 1.25f, -0.75f), new Vector3(2.58f, 0.3f, 3.3f), 0.05f);
        k.Box((int)VS.Hazard, new Vector3(0f, 1.06f, -0.75f), new Vector3(2.57f, 0.1f, 3.3f), 0.02f);
        k.Box((int)VS.Steel, new Vector3(0f, 1.5f, -2.9f), new Vector3(2.2f, 1.6f, 0.9f), 0.15f, default, 2);
        for (int side = -1; side <= 1; side += 2)
        {
            k.Box((int)VS.Tail, new Vector3(side * 1.0f, 0.95f, -3.36f), new Vector3(0.24f, 0.14f, 0.06f), 0.03f);
            k.Cylinder((int)VS.Chrome, new Vector3(side * 0.9f, 1.6f, -2.5f), 0.07f, 1.4f, 10, new Vector3(-40f, 0f, 0f), 0.015f);
        }

        k.Box((int)VS.Rust, new Vector3(1.29f, 2.6f, -1.5f), new Vector3(0.02f, 0.4f, 0.6f), 0.01f);
        Wheel(k, new Vector3(-1.1f, wr, -len + 1.15f), wr, 0.4f);
        Static(w, "Body", k);
        var lid = new MeshKit();
        lid.Box((int)VS.Hazard, Vector3.zero, new Vector3(2.25f, 0.12f, 1.0f), 0.05f);
        for (int i = 0; i < 3; i++) lid.Box((int)VS.Trim, new Vector3(-0.7f + i * 0.7f, 0.07f, 0f), new Vector3(0.3f, 0.03f, 0.9f), 0.01f);
        Part(w, "HopperLid", lid, new Vector3(0f, 2.36f, -2.95f), 0, new Vector3(-8f, 0f, 0f));
        var bin = new MeshKit();
        bin.Box((int)VS.Paint, Vector3.zero, new Vector3(0.7f, 0.95f, 0.7f), 0.08f, default, 2);
        bin.Box((int)VS.Paint2, new Vector3(0f, 0.5f, 0f), new Vector3(0.75f, 0.08f, 0.75f), 0.03f);
        Part(w, "Bin", bin, new Vector3(0.75f, 0.8f, -3.6f), 1);
        var door = new MeshKit();
        door.Box((int)VS.Paint, Vector3.zero, new Vector3(0.07f, 1.1f, 0.95f), 0.04f);
        door.Box((int)VS.Glass, new Vector3(-0.04f, 0.28f, 0f), new Vector3(0.02f, 0.45f, 0.75f), 0.02f);
        Part(w, "CabDoor", door, new Vector3(-1.23f, 1.65f, len - 0.7f), 2);
        Part(w, "WheelFR", WheelKit(wr, 0.4f), new Vector3(1.1f, wr, len - 1.0f), 3);
        Part(w, "WheelFL", WheelKit(wr, 0.4f), new Vector3(-1.1f, wr, len - 1.0f), 4);
        w.Smoke.Add(Effect(w, "VFX_ScrapSmoke", new Vector3(0.6f, 3.2f, -0.5f), 1.3f));
        w.Heavy.Add(Effect(w, "VFX_ScrapSparksLoop", new Vector3(-1.2f, 1.4f, -1f)));
        w.Sag1 = 1.5f;
        w.Sag2 = 3f;
        return Finish(w, path);
    }

    static GameObject BuildMixer(string path, string model, ScrapDefinition def)
    {
        var w = NewWreck(model, def, Table(Paint("MixerRed", new Color(0.86f, 0.2f, 0.14f)), Paint("MixerDrum", new Color(1f, 0.5f, 0.1f))));
        const float len = 3.0f, wr = 0.55f;
        var k = new MeshKit();
        TruckChassis(k, len, wr, true);
        Cab(k, len, null);
        // Tilted mixing drum with stripes.
        k.Push(new Vector3(0f, 2.05f, -0.8f), new Vector3(-80f, 0f, 0f));
        k.Lathe((int)VS.Paint2, Vector3.zero, new[] { new Vector2(0.45f, -1.9f), new Vector2(1.1f, -1.1f), new Vector2(1.18f, -0.2f), new Vector2(1.05f, 0.8f),
            new Vector2(0.55f, 1.65f) }, 22, default, true, 30f);
        for (int i = 0; i < 3; i++)
            k.Torus((int)VS.White, new Vector3(0f, -0.9f + i * 0.75f, 0f), 1.12f - Mathf.Abs(i - 0.6f) * 0.08f, 0.05f, 22, 6);
        k.Pop();
        k.Box((int)VS.Steel, new Vector3(0f, 1.0f, -0.8f), new Vector3(1.4f, 0.4f, 3.2f), 0.05f);
        for (int side = -1; side <= 1; side += 2) k.Box((int)VS.Tail, new Vector3(side * 1.0f, 0.95f, -3.0f), new Vector3(0.24f, 0.14f, 0.06f), 0.03f);
        k.Box((int)VS.Rust, new Vector3(0.9f, 2.6f, -1.2f), new Vector3(0.3f, 0.4f, 0.02f), 0.01f, new Vector3(0f, 70f, 0f));
        Wheel(k, new Vector3(-1.1f, wr, -len + 1.15f), wr, 0.4f);
        Static(w, "Body", k);
        var chute = new MeshKit();
        chute.Box((int)VS.Steel, Vector3.zero, new Vector3(0.5f, 0.14f, 1.3f), 0.05f, new Vector3(25f, 0f, 0f));
        Part(w, "Chute", chute, new Vector3(0f, 1.7f, -3.2f), 0);
        var ladder = new MeshKit();
        for (int side = -1; side <= 1; side += 2) ladder.Box((int)VS.Chrome, new Vector3(side * 0.18f, 0f, 0f), new Vector3(0.05f, 1.8f, 0.05f), 0.01f);
        for (int i = 0; i < 6; i++) ladder.Box((int)VS.Chrome, new Vector3(0f, -0.75f + i * 0.3f, 0f), new Vector3(0.36f, 0.04f, 0.04f), 0.01f);
        Part(w, "Ladder", ladder, new Vector3(1.0f, 1.9f, -2.75f), 1, new Vector3(-12f, 0f, 0f));
        var door = new MeshKit();
        door.Box((int)VS.Paint, Vector3.zero, new Vector3(0.07f, 1.1f, 0.95f), 0.04f);
        door.Box((int)VS.Glass, new Vector3(-0.04f, 0.28f, 0f), new Vector3(0.02f, 0.45f, 0.75f), 0.02f);
        Part(w, "CabDoor", door, new Vector3(-1.23f, 1.65f, len - 0.7f), 2);
        Part(w, "WheelFR", WheelKit(wr, 0.4f), new Vector3(1.1f, wr, len - 1.0f), 3);
        Part(w, "WheelFL", WheelKit(wr, 0.4f), new Vector3(-1.1f, wr, len - 1.0f), 4);
        w.Smoke.Add(Effect(w, "VFX_ScrapSmoke", new Vector3(0.3f, 2.6f, len - 0.4f), 1.2f));
        w.Heavy.Add(Effect(w, "VFX_ScrapSparksLoop", new Vector3(1.1f, 1.4f, -1f)));
        w.Sag1 = 1.5f;
        w.Sag2 = 3f;
        return Finish(w, path);
    }

    // =====================================================================================
    // VFX and loose pieces
    // =====================================================================================

    static void BuildEffects()
    {
        var smokeMat = AssetDatabase.LoadAssetAtPath<Material>(P + "/Art/Materials/M_FX_Smoke.mat");
        var sparkMat = AssetDatabase.LoadAssetAtPath<Material>(P + "/Art/Materials/M_FX_Sparks.mat");

        var smoke = new GameObject("VFX_ScrapSmoke");
        var ps = smoke.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.duration = 2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.35f, 0.34f, 0.34f, 0.5f), new Color(0.55f, 0.53f, 0.52f, 0.45f));
        main.gravityModifier = -0.04f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 30;
        var emission = ps.emission;
        emission.rateOverTime = 5f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 15f;
        shape.radius = 0.15f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        col.color = fade;
        var r = smoke.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = smokeMat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        ArtAssets.SavePrefab(smoke, $"{PrefabDir}/VFX/VFX_ScrapSmoke.prefab");

        var sparks = new GameObject("VFX_ScrapSparksLoop");
        var sp = sparks.AddComponent<ParticleSystem>();
        sp.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var sm = sp.main;
        sm.loop = true;
        sm.playOnAwake = true;
        sm.duration = 1.1f;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
        sm.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
        sm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.5f, 0.15f));
        sm.gravityModifier = 1.5f;
        sm.simulationSpace = ParticleSystemSimulationSpace.World;
        sm.scalingMode = ParticleSystemScalingMode.Hierarchy;
        sm.maxParticles = 40;
        var se = sp.emission;
        se.rateOverTime = 0f;
        se.SetBursts(new[] { new ParticleSystem.Burst(0.1f, 6, 10), new ParticleSystem.Burst(0.65f, 3, 6) });
        var ss = sp.shape;
        ss.shapeType = ParticleSystemShapeType.Cone;
        ss.angle = 40f;
        ss.radius = 0.05f;
        ss.rotation = new Vector3(-60f, 0f, 0f);
        var sr = sparks.GetComponent<ParticleSystemRenderer>();
        sr.renderMode = ParticleSystemRenderMode.Stretch;
        sr.velocityScale = 0.05f;
        sr.lengthScale = 1.6f;
        sr.sharedMaterial = sparkMat;
        sr.shadowCastingMode = ShadowCastingMode.Off;
        ArtAssets.SavePrefab(sparks, $"{PrefabDir}/VFX/VFX_ScrapSparksLoop.prefab");
    }

    /// <summary>Loose scrap pieces: six chunky palette meshes (bent panel, pipe, gear, bolt and nut, spring, crushed can).</summary>
    static Mesh[] BuildPieces()
    {
        var meshes = new List<Mesh>();
        Mesh Save(string name, MeshKit k)
        {
            var b = k.Bounds;
            var centered = new MeshKit();
            centered.Push(-b.center).Append(k).Pop();
            return ArtAssets.SaveMesh("ScrapPieces", name, centered.ToPaletteMesh(name));
        }

        var panel = new MeshKit();
        panel.Box((int)PC.Red, new Vector3(-0.1f, 0f, 0f), new Vector3(0.24f, 0.05f, 0.34f), 0.015f, new Vector3(0f, 0f, 12f));
        panel.Box((int)PC.Red, new Vector3(0.12f, 0.02f, 0f), new Vector3(0.22f, 0.05f, 0.34f), 0.015f, new Vector3(0f, 0f, -18f));
        panel.Box((int)PC.Rust, new Vector3(0.05f, 0.05f, 0.08f), new Vector3(0.1f, 0.02f, 0.1f), 0.008f, new Vector3(0f, 0f, -18f));
        meshes.Add(Save("PanelRed", panel));

        var panelBlue = new MeshKit();
        panelBlue.Box((int)PC.Blue, new Vector3(0f, 0f, 0f), new Vector3(0.4f, 0.05f, 0.28f), 0.015f, new Vector3(6f, 0f, 8f));
        panelBlue.Box((int)PC.GrayLight, new Vector3(0f, 0.035f, 0f), new Vector3(0.3f, 0.02f, 0.05f), 0.006f, new Vector3(6f, 0f, 8f));
        meshes.Add(Save("PanelBlue", panelBlue));

        var pipe = new MeshKit();
        pipe.Lathe((int)PC.SteelBlue, Vector3.zero, new[] { new Vector2(0.08f, -0.2f), new Vector2(0.095f, -0.19f), new Vector2(0.095f, 0.19f), new Vector2(0.08f, 0.2f) },
            10, new Vector3(90f, 0f, 0f), false);
        pipe.Lathe((int)PC.Charcoal, Vector3.zero, new[] { new Vector2(0.05f, 0.2f), new Vector2(0.05f, -0.2f) }, 10, new Vector3(90f, 0f, 0f), false);
        pipe.Torus((int)PC.Orange, new Vector3(0f, 0f, 0.17f), 0.075f, 0.018f, 12, 6, new Vector3(90f, 0f, 0f));
        meshes.Add(Save("Pipe", pipe));

        var gear = new MeshKit();
        gear.Cylinder((int)PC.Gray, Vector3.zero, 0.15f, 0.08f, 16, default, 0.015f);
        for (int i = 0; i < 10; i++) gear.Box((int)PC.Gray, Quaternion.Euler(0f, i * 36f, 0f) * new Vector3(0.17f, 0f, 0f), new Vector3(0.06f, 0.08f, 0.05f), 0.01f, new Vector3(0f, -i * 36f, 0f));
        gear.Cylinder((int)PC.Charcoal, Vector3.zero, 0.05f, 0.1f, 10, default, 0.01f);
        meshes.Add(Save("Gear", gear));

        var bolt = new MeshKit();
        bolt.Cylinder((int)PC.YellowDeep, new Vector3(0f, 0.05f, 0f), 0.1f, 0.08f, 6, default, 0.012f);
        bolt.Cylinder((int)PC.Gray, new Vector3(0f, -0.08f, 0f), 0.05f, 0.22f, 8, default, 0.01f);
        bolt.Cylinder((int)PC.Gray, new Vector3(0.12f, -0.03f, 0.05f), 0.07f, 0.06f, 6, new Vector3(0f, 0f, 70f), 0.01f);
        meshes.Add(Save("Bolt", bolt));

        var can = new MeshKit();
        can.Lathe((int)PC.Green, Vector3.zero, new[] { new Vector2(0.12f, -0.12f), new Vector2(0.15f, -0.04f), new Vector2(0.1f, 0.02f), new Vector2(0.15f, 0.08f),
            new Vector2(0.12f, 0.13f) }, 10, new Vector3(0f, 0f, 75f));
        can.Box((int)PC.White, new Vector3(0f, 0f, 0.13f), new Vector3(0.12f, 0.08f, 0.02f), 0.01f, new Vector3(0f, 0f, 75f));
        meshes.Add(Save("Can", can));
        return meshes.ToArray();
    }

    // =====================================================================================
    // Entry points
    // =====================================================================================

    static ScrapDefinition Def(string name) => AssetDatabase.LoadAssetAtPath<ScrapDefinition>($"{DataDir}/Scrap/Scrap_{name}.asset");
    static string ScrapPath(string name) => $"{PrefabDir}/Scrap/{name}.prefab";

    public static string Build()
    {
        Log.Clear();
        // One palette material for all scrap (mobile draw calls); the rust patch texture becomes a flat rust colour.
        baker = new PaletteBaker("ScrapPalette", null, new Dictionary<string, Color> { ["M_ScrapRust"] = new Color(0.62f, 0.31f, 0.14f) });
        BuildEffects();

        // Cars: four variants on the car definition (file names kept so pools and GUIDs stay valid).
        var car = Def("CarWreck");
        var cars = new[]
        {
            BuildCar(ScrapPath("Scrap_CarWreck"), "Car_SedanYellow", car, CarStyle.Sedan, Paint("CarYellow", new Color(1f, 0.74f, 0.12f)), Primer.DoorFL),
            BuildCar(ScrapPath("Scrap_Car_sedan"), "Car_SedanRed", car, CarStyle.Sedan, Paint("CarRed", new Color(0.86f, 0.2f, 0.15f)), Primer.Hood),
            BuildCar(ScrapPath("Scrap_Car_hatchback-sports"), "Car_HatchBlue", car, CarStyle.Hatch, Paint("CarBlue", new Color(0.18f, 0.48f, 0.88f)), Primer.Lid),
            BuildCar(ScrapPath("Scrap_Car_suv"), "Car_PickupTeal", car, CarStyle.Pickup, Paint("CarTeal", new Color(0.12f, 0.62f, 0.62f)), Primer.DoorRR),
        };
        SetVariants(car, cars);

        var barrel = Def("Barrel");
        SetVariants(barrel, new[]
        {
            BuildBarrel(ScrapPath("Scrap_Barrel"), "Barrel_Blue", barrel, Paint("DrumBlue", new Color(0.18f, 0.46f, 0.86f)), false),
            BuildBarrel(ScrapPath("Scrap_Barrel_Red"), "Barrel_Red", barrel, Paint("DrumRed", new Color(0.86f, 0.2f, 0.14f)), true),
            BuildBarrel(ScrapPath("Scrap_Barrel_Green"), "Barrel_Green", barrel, Paint("DrumGreen", new Color(0.3f, 0.66f, 0.28f)), false),
        });
        var tires = Def("TireStack");
        SetVariants(tires, new[] { BuildTires(ScrapPath("Scrap_TireStack"), "TireStack", tires) });
        var fridge = Def("Fridge");
        SetVariants(fridge, new[]
        {
            BuildFridge(ScrapPath("Scrap_Fridge"), "Fridge_Cream", fridge, Mat("FridgeCream", new Color(0.97f, 0.93f, 0.82f), 0.6f, 0f, Detail.Paint)),
            BuildFridge(ScrapPath("Scrap_Fridge_Mint"), "Fridge_Mint", fridge, Mat("FridgeMint", new Color(0.56f, 0.86f, 0.76f), 0.6f, 0f, Detail.Paint)),
        });
        var washer = Def("Washer");
        SetVariants(washer, new[] { BuildWasher(ScrapPath("Scrap_Washer"), "Washer", washer) });
        var stove = Def("Stove");
        SetVariants(stove, new[] { BuildStove(ScrapPath("Scrap_Stove"), "Stove", stove) });
        var kart = Def("Kart");
        SetVariants(kart, new[]
        {
            BuildKart(ScrapPath("Scrap_Kart_oobi"), "Kart_Red", kart, Paint("KartRed", new Color(0.88f, 0.2f, 0.15f))),
            BuildKart(ScrapPath("Scrap_Kart_oodi"), "Kart_Blue", kart, Paint("KartBlue", new Color(0.2f, 0.5f, 0.9f))),
            BuildKart(ScrapPath("Scrap_Kart_ooli"), "Kart_Green", kart, Paint("KartGreen", new Color(0.3f, 0.75f, 0.3f))),
            BuildKart(ScrapPath("Scrap_Kart_oopi"), "Kart_Purple", kart, Paint("KartPurple", new Color(0.55f, 0.35f, 0.82f))),
        });
        var tractor = Def("Tractor");
        SetVariants(tractor, new[]
        {
            BuildTractor(ScrapPath("Scrap_Tractor_tractor"), "Tractor", tractor),
        });
        // The excavator is its own, heavier scrap type since Revamp 4 (data: R4_Build.Assets); the prefab keeps its old path.
        var excavator = Def("Excavator") != null ? Def("Excavator") : tractor;
        var excavatorPrefab = BuildExcavator(ScrapPath("Scrap_Tractor_tractor-shovel"), "Excavator", excavator);
        if (excavator != tractor) SetVariants(excavator, new[] { excavatorPrefab });
        var truck = Def("Truck");
        SetVariants(truck, new[]
        {
            BuildBoxTruck(ScrapPath("Scrap_Truck_delivery"), "BoxTruck", truck), BuildFlatbed(ScrapPath("Scrap_Truck_delivery-flat"), "Flatbed", truck),
        });
        var garbage = Def("GarbageTruck");
        SetVariants(garbage, new[]
        {
            BuildGarbageTruck(ScrapPath("Scrap_GarbageTruck_garbage-truck"), "GarbageTruck", garbage),
            BuildMixer(ScrapPath("Scrap_GarbageTruck_firetruck"), "Mixer", garbage),
        });

        baker.Save();
        baker = null;

        // Loose pieces.
        var pieces = BuildPieces();
        var piecePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Items/Item_ScrapPiece.prefab");
        var root = PrefabUtility.LoadPrefabContents($"{PrefabDir}/Items/Item_ScrapPiece.prefab");
        try
        {
            var wi = root.GetComponent<WorldItem>();
            var mf = root.GetComponentInChildren<MeshFilter>();
            var mr = root.GetComponentInChildren<MeshRenderer>();
            mf.sharedMesh = pieces[0];
            mr.sharedMaterials = new[] { ArtPalette.Material };
            mf.transform.localScale = Vector3.one * 1.15f;
            var so = new SerializedObject(wi);
            var mv = so.FindProperty("meshVariants");
            mv.arraySize = pieces.Length;
            for (int i = 0; i < pieces.Length; i++) mv.GetArrayElementAtIndex(i).objectReferenceValue = pieces[i];
            var mats = so.FindProperty("materialVariants");
            mats.arraySize = 2;
            mats.GetArrayElementAtIndex(0).objectReferenceValue = ArtPalette.Material;
            mats.GetArrayElementAtIndex(1).objectReferenceValue = ArtPalette.MetalMaterial;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/Items/Item_ScrapPiece.prefab");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        _ = piecePrefab;

        // Icons that show scrap.
        var scrapItem = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{DataDir}/Items/Item_Scrap.asset");
        if (scrapItem != null)
            ArtAssets.Set(scrapItem, ("icon", ArtIcons.Render("Icon_Scrap", AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Items/Item_ScrapPiece.prefab"),
                new Vector3(40f, 30f, 0f), null, 0.85f)));
        var backLot = AssetDatabase.LoadAssetAtPath<ExpansionDefinition>($"{DataDir}/World/Expansion_BackLot.asset");
        if (backLot != null)
            ArtAssets.Set(backLot, ("icon", ArtIcons.Render("Icon_BackLot", AssetDatabase.LoadAssetAtPath<GameObject>(ScrapPath("Scrap_Fridge_Mint")),
                new Vector3(18f, 150f, 0f), HideBar, 0.8f)));
        var heavy = AssetDatabase.LoadAssetAtPath<ExpansionDefinition>($"{DataDir}/World/Expansion_HeavyYard.asset");
        if (heavy != null)
            ArtAssets.Set(heavy, ("icon", ArtIcons.Render("Icon_HeavyYard", AssetDatabase.LoadAssetAtPath<GameObject>(ScrapPath("Scrap_Tractor_tractor-shovel")),
                new Vector3(22f, 150f, 0f), HideBar, 0.72f)));

        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    static void HideBar(GameObject go)
    {
        var bar = go.transform.Find("HealthBar");
        if (bar != null) bar.gameObject.SetActive(false);
    }

    static void SetVariants(ScrapDefinition def, GameObject[] prefabs)
    {
        ArtAssets.Set(def, ("prefab", prefabs[0].GetComponent<ScrapObject>()));
        ArtAssets.SetArray(def, "prefabVariants", prefabs.Select(p => (Object)p.GetComponent<ScrapObject>()).ToArray());
        Log.AppendLine($"{def.name}: {prefabs.Length} variants");
    }

    /// <summary>Lineup of every scrap prefab rendered from the game camera angle (run after Build).</summary>
    public static string Preview(string outDir)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var names = new[]
        {
            "Scrap_CarWreck", "Scrap_Car_sedan", "Scrap_Car_hatchback-sports", "Scrap_Car_suv", "Scrap_Barrel", "Scrap_Barrel_Red", "Scrap_Barrel_Green",
            "Scrap_TireStack", "Scrap_Fridge", "Scrap_Fridge_Mint", "Scrap_Washer", "Scrap_Stove", "Scrap_Kart_oobi", "Scrap_Kart_oodi", "Scrap_Tractor_tractor",
            "Scrap_Tractor_tractor-shovel", "Scrap_Truck_delivery", "Scrap_Truck_delivery-flat", "Scrap_GarbageTruck_garbage-truck", "Scrap_GarbageTruck_firetruck",
        };
        float x = 0f;
        var positions = new List<Vector3>();
        int row = 0;
        float z = 0f;
        foreach (var n in names)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(ScrapPath(n));
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            SceneManager.MoveGameObjectToScene(go, scene);
            float width = go.GetComponent<BoxCollider>().size.x + 0.8f;
            if (x + width > 16f)
            {
                x = 0f;
                z -= 6.5f;
                row++;
            }

            go.transform.position = new Vector3(x + width * 0.5f, 0f, z);
            go.transform.rotation = Quaternion.Euler(0f, 160f, 0f);
            x += width;
        }

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        SceneManager.MoveGameObjectToScene(ground, scene);
        ground.transform.position = new Vector3(8f, 0f, -8f);
        ground.transform.localScale = new Vector3(3f, 1f, 3f);
        ground.GetComponent<Renderer>().sharedMaterial = Mat("PreviewGround", new Color(0.8f, 0.74f, 0.62f), 0.1f, 0f, Detail.Concrete);
        Directory.CreateDirectory(outDir);
        Render(scene, Path.Combine(outDir, "scrap_game.png"), new Vector3(50f, 0f, 0f), new Vector3(8f, 0f, -7f), 18f, 1600, 1200);
        Render(scene, Path.Combine(outDir, "scrap_close.png"), new Vector3(30f, 200f, 0f), new Vector3(4f, 1f, 0f), 9f, 1600, 900);
        EditorSceneManager.ClosePreviewScene(scene);
        return "ok";
    }

    static void Render(Scene scene, string path, Vector3 euler, Vector3 target, float width, int w, int h)
    {
        var camGo = new GameObject("Cam");
        SceneManager.MoveGameObjectToScene(camGo, scene);
        var cam = camGo.AddComponent<Camera>();
        cam.scene = scene;
        cam.orthographic = true;
        cam.orthographicSize = width * h / w * 0.5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
        var rot = Quaternion.Euler(euler);
        camGo.transform.SetPositionAndRotation(target - rot * Vector3.forward * 40f, rot);
        cam.farClipPlane = 100f;
        var sun = new GameObject("Sun");
        SceneManager.MoveGameObjectToScene(sun, scene);
        var l = sun.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.4f;
        l.color = new Color(1f, 0.95f, 0.85f);
        l.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
        var rt = new RenderTexture(w, h, 24) { antiAliasing = 1 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(sun);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Factory;
using ScrapYardKing.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using static ScrapYardKing.EditorTools.Art.ArtMaterials;

// Art pass 3 — machines. Rebuilds the look of every station as a chunky landmark (custom ArtKit geometry, no Kenney):
// the red Crusher (tall hollow hopper with toothed rollers, flywheel, press plate, beacon, ladder), the yellow Sorter
// (screen deck, magnet drum, sorting paddles, colour-coded chutes), the brick Furnace (glowing mouth, bellows, fan),
// ribbed storage bins, striped market stalls and custom conveyor belts. Functional transforms (pads, hoppers, intake,
// output points, labels, VFX) are kept exactly where they were, so gameplay and the scene layout do not change.
// Entry points: Prefabs (stations), Scene (conveyors, per-instance colours), Icons; All runs the three.
public static class Art_Machines
{
    const string P = "Assets/_Project";
    const string StationDir = P + "/Prefabs/Stations";
    const string DataDir = P + "/Data";
    const string ScenePath = P + "/Scenes/Area1_OldScrapYard.unity";
    static readonly StringBuilder Log = new();

    enum MS { Main, MainDark, Accent, AccentDark, Steel, Dark, Chrome, Rubber, Concrete, Wood, White, Glass, Light, Hazard, Copper, Count }

    static Texture2D hazardTex, beltTex;
    static PaletteBaker baker;

    static readonly string[] KeepTextured =
    {
        "M_MachHazard", "M_FurnaceBrick", "M_ConveyorBelt", "M_MachConcrete", "M_MachWood", "M_BinBlue", "M_BinBlueDeep", "M_StallRed", "M_StallRedDeep",
        "M_StatusLight", "M_FurnaceMouth", "M_FurnaceMouthIron", "M_FurnaceMouthCopper", "M_FurnaceMouthAluminum", "M_FurnaceMouthSteel", "M_PressGlow",
    };

    static Material[] Table(Material main, Material mainDark, Material accent = null)
    {
        var t = new Material[(int)MS.Count];
        t[(int)MS.Main] = main;
        t[(int)MS.MainDark] = mainDark;
        t[(int)MS.Accent] = accent ?? Mat("MachYellow", new Color(1f, 0.76f, 0.1f), 0.5f, 0.05f, Detail.Paint, 0.7f);
        t[(int)MS.AccentDark] = Mat("MachYellowDeep", new Color(0.92f, 0.58f, 0.05f), 0.45f, 0.05f, Detail.Paint, 0.7f);
        t[(int)MS.Steel] = Mat("MachSteel", new Color(0.55f, 0.58f, 0.63f), 0.5f, 0.3f, Detail.Metal, 0.8f);
        t[(int)MS.Dark] = Mat("MachDark", new Color(0.17f, 0.18f, 0.2f), 0.35f, 0.1f, Detail.Metal, 0.8f);
        t[(int)MS.Chrome] = Mat("MachChrome", new Color(0.82f, 0.84f, 0.88f), 0.8f, 0.4f, Detail.Metal, 1f);
        t[(int)MS.Rubber] = Mat("MachRubber", new Color(0.12f, 0.12f, 0.13f), 0.25f, 0f, Detail.Rubber, 1.5f);
        t[(int)MS.Concrete] = Mat("MachConcrete", new Color(0.72f, 0.7f, 0.67f), 0.15f, 0f, Detail.Concrete, 0.35f);
        t[(int)MS.Wood] = Mat("MachWood", new Color(0.8f, 0.58f, 0.34f), 0.22f, 0f, Detail.Wood, 0.6f);
        t[(int)MS.White] = Mat("MachWhite", new Color(0.97f, 0.96f, 0.93f), 0.4f, 0f, Detail.Paint, 0.7f);
        t[(int)MS.Glass] = Mat("MachGlass", new Color(0.45f, 0.65f, 0.8f), 0.92f, 0.1f, Detail.Plain);
        t[(int)MS.Light] = Mat("MachLamp", new Color(1f, 0.93f, 0.7f), 0.9f, 0f, Detail.Plain, 1f, new Color(1.6f, 1.35f, 0.8f));
        t[(int)MS.Hazard] = HazardMat();
        t[(int)MS.Copper] = Mat("MachCopper", new Color(0.95f, 0.52f, 0.22f), 0.6f, 0.25f, Detail.Metal, 1f);
        return t;
    }

    static Material HazardMat()
    {
        hazardTex ??= StripeTexture("T_Hazard", new Color(1f, 0.78f, 0.1f), new Color(0.13f, 0.13f, 0.14f), true);
        var m = Mat("MachHazard", Color.white, 0.4f, 0f, Detail.Plain);
        m.SetTexture("_BaseMap", hazardTex);
        m.SetTextureScale("_BaseMap", Vector2.one * 1.6f);
        return m;
    }

    static Texture2D StripeTexture(string name, Color a, Color b, bool diagonal)
    {
        const int size = 128;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float t = diagonal ? (x + y) / (float)size : y / (float)size;
            bool on = Mathf.Repeat(t * 2f, 1f) < 0.5f;
            float grain = 0.94f + Fbm(x / (float)size, y / (float)size, 8, 3, 3) * 0.08f;
            var c = on ? a : b;
            px[y * size + x] = new Color(c.r * grain, c.g * grain, c.b * grain, 1f);
        }

        return SaveTexture($"{ArtAssets.TexDir}/{name}.png", size, px, true);
    }

    /// <summary>Stylized running-bond bricks: warm reds with cream mortar and a little soot toward the bottom of each brick.</summary>
    static Texture2D BrickTexture()
    {
        const int size = 256;
        var px = new Color[size * size];
        const int rows = 8, cols = 4;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float v = y / (float)size * rows;
            int row = Mathf.FloorToInt(v);
            float u = x / (float)size * cols + (row % 2) * 0.5f;
            int col = Mathf.FloorToInt(u);
            float fu = u - col, fv = v - row;
            bool mortar = fu < 0.05f || fv < 0.1f;
            float tone = Noise((Mathf.Repeat(col, cols) + 0.5f) / cols, (row + 0.5f) / rows, rows, 7);
            var b = Color.Lerp(new Color(0.78f, 0.3f, 0.18f), new Color(0.62f, 0.2f, 0.13f), tone);
            float grain = 0.92f + Fbm(x / (float)size, y / (float)size, 16, 2, 4) * 0.12f;
            b *= grain * Mathf.Lerp(0.85f, 1f, fv);
            px[y * size + x] = mortar ? new Color(0.86f, 0.78f, 0.66f) : new Color(b.r, b.g, b.b, 1f);
        }

        return SaveTexture($"{ArtAssets.TexDir}/T_Brick.png", size, px, true);
    }

    static Texture2D BeltTexture()
    {
        if (beltTex != null) return beltTex;
        const int size = 128;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float v = y / (float)size, u = x / (float)size;
            // Rubber with a raised chevron cleat every half metre (two per texture tile = 1 m).
            float chevron = Mathf.Repeat(v * 2f + Mathf.Abs(u - 0.5f) * 0.6f, 1f);
            bool cleat = chevron < 0.14f;
            float g = cleat ? 0.42f : 0.17f + Fbm(u, v, 16, 2, 5) * 0.05f;
            bool edge = u < 0.05f || u > 0.95f;
            px[y * size + x] = edge ? new Color(0.9f, 0.68f, 0.1f) : new Color(g, g, g * 1.05f);
        }

        beltTex = SaveTexture($"{ArtAssets.TexDir}/T_Belt.png", size, px, true);
        return beltTex;
    }

    static Material BeltMat()
    {
        var m = Mat("ConveyorBelt", Color.white, 0.3f, 0f, Detail.Plain);
        m.SetTexture("_BaseMap", BeltTexture());
        m.SetTextureScale("_BaseMap", Vector2.one);
        return m;
    }

    // =====================================================================================
    // Prefab editing helpers
    // =====================================================================================

    static void EditPrefab(string path, Action<GameObject> edit)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            edit(root);
            baker?.Convert(root, "Mach_" + Path.GetFileNameWithoutExtension(path));
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Log.AppendLine("edited " + path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Removes purely visual children (renderers, Kenney models) of <paramref name="parent"/>, keeping named functional ones.</summary>
    static void StripVisuals(Transform parent, params string[] keep)
    {
        foreach (Transform c in parent.Cast<Transform>().ToArray())
        {
            if (keep.Contains(c.name)) continue;
            bool functional = c.GetComponentsInChildren<Component>(true).Any(comp => comp is not (Transform or MeshFilter or MeshRenderer or SpriteRenderer)
                && comp.GetType().Name != "ProBuilderMesh");
            if (!functional) Object.DestroyImmediate(c.gameObject);
        }
    }

    static Transform Child(Transform parent, string name, Vector3 localPosition = default, Vector3 euler = default)
    {
        var existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPosition;
        t.localRotation = Quaternion.Euler(euler);
        return t;
    }

    static GameObject Part(string model, string part, MeshKit k, Material[] table, Transform parent, Vector3 pos = default, Vector3 euler = default) =>
        ArtAssets.Part(model, part, k, table, parent, pos, euler);

    /// <summary>Status beacon: dark pole + a light renderer using the shared status material (MachineVisuals tints it).</summary>
    static Renderer Beacon(string model, Material[] table, Transform parent, Vector3 pos, float pole = 0.6f)
    {
        var k = new MeshKit();
        k.Cylinder((int)MS.Dark, new Vector3(0f, pole * 0.5f, 0f), 0.06f, pole, 10, default, 0.01f);
        k.Cylinder((int)MS.Dark, new Vector3(0f, pole + 0.04f, 0f), 0.17f, 0.08f, 14, default, 0.02f);
        k.Torus((int)MS.Dark, new Vector3(0f, pole + 0.22f, 0f), 0.16f, 0.015f, 14, 5);
        for (int i = 0; i < 4; i++)
            k.Box((int)MS.Dark, Quaternion.Euler(0f, i * 90f + 45f, 0f) * new Vector3(0.155f, pole + 0.2f, 0f), new Vector3(0.025f, 0.32f, 0.025f), 0.006f);
        Part(model, "BeaconBase", k, table, parent, pos);
        var lightK = new MeshKit();
        lightK.Cylinder(0, new Vector3(0f, 0.11f, 0f), 0.13f, 0.22f, 14, default, 0.04f);
        lightK.Dome(0, new Vector3(0f, 0.22f, 0f), new Vector3(0.13f, 0.1f, 0.13f), 14, 4);
        var status = AssetDatabase.LoadAssetAtPath<Material>(P + "/Art/Materials/M_StatusLight.mat");
        var go = Part(model, "StatusLight", lightK, new[] { status }, parent, pos + new Vector3(0f, pole + 0.08f, 0f));
        go.name = "StatusLight";
        return go.GetComponent<Renderer>();
    }

    static void Bolts(MeshKit k, Vector3 from, Vector3 to, int count, float r = 0.045f)
    {
        for (int i = 0; i < count; i++) k.Sphere((int)MS.Chrome, Vector3.Lerp(from, to, count == 1 ? 0.5f : i / (float)(count - 1)), Vector3.one * r, 8, 5);
    }

    static void Ladder(MeshKit k, Vector3 bottom, float height, Vector3 euler)
    {
        k.Push(bottom, euler);
        for (int side = -1; side <= 1; side += 2) k.Cylinder((int)MS.Accent, new Vector3(side * 0.22f, height * 0.5f, 0f), 0.035f, height, 8, default, 0.01f);
        for (float y = 0.3f; y < height; y += 0.32f) k.Cylinder((int)MS.Accent, new Vector3(0f, y, 0f), 0.025f, 0.44f, 6, new Vector3(0f, 0f, 90f), 0.005f);
        k.Pop();
    }

    static void Rail(MeshKit k, Vector3 a, Vector3 b, float height = 0.9f)
    {
        float len = Vector3.Distance(a, b);
        var mid = (a + b) * 0.5f;
        float yaw = Mathf.Atan2(b.x - a.x, b.z - a.z) * Mathf.Rad2Deg;
        k.Cylinder((int)MS.Accent, mid + Vector3.up * height, 0.035f, len, 8, new Vector3(90f, yaw, 0f), 0.01f);
        k.Cylinder((int)MS.Accent, mid + Vector3.up * height * 0.5f, 0.03f, len, 8, new Vector3(90f, yaw, 0f), 0.01f);
        int posts = Mathf.Max(2, Mathf.RoundToInt(len / 1.1f) + 1);
        for (int i = 0; i < posts; i++) k.Cylinder((int)MS.Accent, Vector3.Lerp(a, b, i / (float)(posts - 1)) + Vector3.up * height * 0.5f, 0.035f, height, 8, default, 0.01f);
    }

    static LevelVisuals Tiers(GameObject root, Transform holder, params (int level, GameObject go)[] tiers)
    {
        if (!root.TryGetComponent(out LevelVisuals lv)) lv = root.AddComponent<LevelVisuals>();
        var so = new SerializedObject(lv);
        var p = so.FindProperty("tiers");
        p.arraySize = tiers.Length;
        for (int i = 0; i < tiers.Length; i++)
        {
            var e = p.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("minLevel").intValue = tiers[i].level;
            var objs = e.FindPropertyRelative("objects");
            objs.arraySize = 1;
            objs.GetArrayElementAtIndex(0).objectReferenceValue = tiers[i].go;
            tiers[i].go.SetActive(false);
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        _ = holder;
        return lv;
    }

    static GameObject Tier(Transform holder, string name, string model, MeshKit k, Material[] table)
    {
        var t = Child(holder, name);
        Part(model, name, k, table, t);
        return t.gameObject;
    }

    // =====================================================================================
    // Crusher
    // =====================================================================================

    static void Crusher()
    {
        var table = Table(Mat("CrusherRed", new Color(0.88f, 0.17f, 0.13f), 0.5f, 0.08f, Detail.Paint, 0.7f),
            Mat("CrusherRedDeep", new Color(0.6f, 0.11f, 0.09f), 0.45f, 0.08f, Detail.Paint, 0.7f));
        const string model = "Mach_Crusher";
        EditPrefab($"{StationDir}/Crusher.prefab", root =>
        {
            var body = root.transform.Find("Body");
            StripVisuals(root.transform, "Body", "OutputPoint", "StationLabel", "InputPad");
            StripVisuals(body, "Intake", "HopperPile", "VFX_CrusherDust", "VFX_OverdriveSparks");
            var lvOld = body.Find("LevelVisuals");
            if (lvOld != null) Object.DestroyImmediate(lvOld.gameObject);

            // Static base (does not rumble).
            var baseK = new MeshKit();
            baseK.Box((int)MS.Concrete, new Vector3(0f, 0.12f, 0f), new Vector3(3.7f, 0.24f, 3.7f), 0.06f);
            baseK.Box((int)MS.Hazard, new Vector3(0f, 0.245f, -1.82f), new Vector3(3.6f, 0.02f, 0.12f), 0.005f);
            baseK.Box((int)MS.Hazard, new Vector3(0f, 0.245f, 1.82f), new Vector3(3.6f, 0.02f, 0.12f), 0.005f);
            // Output chute to the belt.
            baseK.Box((int)MS.Steel, new Vector3(0f, 0.72f, -1.68f), new Vector3(1.0f, 0.08f, 0.85f), 0.02f, new Vector3(-22f, 0f, 0f));
            for (int side = -1; side <= 1; side += 2)
                baseK.Box((int)MS.MainDark, new Vector3(side * 0.53f, 0.82f, -1.68f), new Vector3(0.06f, 0.26f, 0.85f), 0.02f, new Vector3(-22f, 0f, 0f));
            Part(model, "Base", baseK, table, Child(root.transform, "ArtBase"));

            var art = Child(body, "Art");
            var k = new MeshKit();
            k.Box((int)MS.Main, new Vector3(0f, 0.95f, 0f), new Vector3(2.8f, 1.7f, 2.6f), 0.2f, default, 2);
            k.Box((int)MS.Dark, new Vector3(0f, 0.3f, 0f), new Vector3(2.88f, 0.2f, 2.68f), 0.06f);
            k.Box((int)MS.MainDark, new Vector3(0f, 1.83f, 0f), new Vector3(2.94f, 0.14f, 2.74f), 0.05f);
            foreach (var x in new[] { -1.25f, 1.25f })
            foreach (var z in new[] { -1.15f, 1.15f })
                k.Box((int)MS.Dark, new Vector3(x, 0.12f, z), new Vector3(0.45f, 0.26f, 0.45f), 0.06f);
            Bolts(k, new Vector3(-1.3f, 1.83f, -1.38f), new Vector3(1.3f, 1.83f, -1.38f), 9);
            Bolts(k, new Vector3(-1.3f, 1.83f, 1.38f), new Vector3(1.3f, 1.83f, 1.38f), 9);
            // Front: hazard band, access hatch, warning plate.
            k.Box((int)MS.Hazard, new Vector3(0f, 0.6f, -1.31f), new Vector3(2.5f, 0.3f, 0.04f), 0.01f);
            k.Box((int)MS.MainDark, new Vector3(-0.72f, 1.2f, -1.32f), new Vector3(0.85f, 0.7f, 0.05f), 0.03f);
            k.Box((int)MS.Chrome, new Vector3(-0.42f, 1.2f, -1.36f), new Vector3(0.06f, 0.24f, 0.05f), 0.02f);
            Bolts(k, new Vector3(-1.08f, 1.5f, -1.35f), new Vector3(-0.36f, 1.5f, -1.35f), 4, 0.03f);
            k.Box((int)MS.Accent, new Vector3(0.62f, 1.22f, -1.33f), new Vector3(0.6f, 0.5f, 0.04f), 0.03f, new Vector3(0f, 0f, 45f));
            k.Box((int)MS.Dark, new Vector3(0.62f, 1.2f, -1.36f), new Vector3(0.07f, 0.24f, 0.02f), 0.01f);
            // Motor block at the back with fins and hoses up to the press.
            k.Box((int)MS.Steel, new Vector3(0.55f, 0.75f, 1.62f), new Vector3(1.1f, 0.85f, 0.65f), 0.12f, default, 2);
            for (int i = 0; i < 6; i++) k.Box((int)MS.Dark, new Vector3(0.12f + i * 0.17f, 0.75f, 1.96f), new Vector3(0.05f, 0.7f, 0.05f), 0.01f);
            k.Box((int)MS.Dark, new Vector3(-0.55f, 0.55f, 1.55f), new Vector3(0.7f, 0.5f, 0.5f), 0.08f);
            k.Torus((int)MS.Rubber, new Vector3(0.9f, 1.6f, 1.45f), 0.45f, 0.05f, 14, 6, new Vector3(0f, 90f, 90f), null, 160f, 190f);
            k.Torus((int)MS.Rubber, new Vector3(-0.9f, 1.6f, 1.45f), 0.45f, 0.05f, 14, 6, new Vector3(0f, 90f, 90f), null, 160f, 190f);
            // Hollow square hopper (items fill it), hazard rim, inner floor.
            k.Lathe((int)MS.MainDark, new Vector3(0f, 1.9f, 0f), new[] { new Vector2(1.05f, 0f), new Vector2(2.05f, 1.55f), new Vector2(2.1f, 1.62f), new Vector2(1.96f, 1.62f),
                new Vector2(0.95f, 0.08f) }, 4, new Vector3(0f, 45f, 0f), false, 30f);
            k.Lathe((int)MS.Hazard, new Vector3(0f, 1.9f, 0f), new[] { new Vector2(2.08f, 1.5f), new Vector2(2.14f, 1.56f), new Vector2(2.14f, 1.7f), new Vector2(1.94f, 1.7f) },
                4, new Vector3(0f, 45f, 0f), false, 30f);
            k.Box((int)MS.Dark, new Vector3(0f, 1.96f, 0f), new Vector3(1.4f, 0.04f, 1.4f), 0.01f);
            // Ladder up to a railed service ledge on the east side of the hopper.
            Ladder(k, new Vector3(1.5f, 0f, 0.55f), 1.95f, new Vector3(0f, 90f, 0f));
            Rail(k, new Vector3(1.38f, 1.9f, -1.2f), new Vector3(1.38f, 1.9f, 1.2f), 0.75f);
            Rail(k, new Vector3(-1.38f, 1.9f, -0.6f), new Vector3(-1.38f, 1.9f, 1.2f), 0.75f);
            Part(model, "Body", k, table, art);

            // Spinners: side flywheel (on the input side) and the two toothed crushing rolls inside the hopper.
            var spinners = new List<Object>();
            var fly = new MeshKit();
            fly.Cylinder((int)MS.Accent, Vector3.zero, 0.72f, 0.14f, 24, new Vector3(90f, 0f, 0f), 0.03f);
            fly.Torus((int)MS.AccentDark, Vector3.zero, 0.62f, 0.07f, 24, 6, new Vector3(90f, 0f, 0f));
            for (int i = 0; i < 5; i++) fly.Box((int)MS.Dark, Quaternion.Euler(0f, 0f, i * 72f) * new Vector3(0f, 0.34f, 0f), new Vector3(0.1f, 0.56f, 0.18f), 0.02f, new Vector3(0f, 0f, i * 72f));
            fly.Cylinder((int)MS.Chrome, Vector3.zero, 0.14f, 0.22f, 12, new Vector3(90f, 0f, 0f), 0.02f);
            var flyGo = Part(model, "Flywheel", fly, table, art, new Vector3(-1.52f, 1.0f, 0.35f), new Vector3(0f, 90f, 0f));
            spinners.Add(flyGo.transform);
            for (int side = -1; side <= 1; side += 2)
            {
                var roll = new MeshKit();
                roll.Cylinder((int)MS.Steel, Vector3.zero, 0.2f, 1.25f, 14, new Vector3(0f, 90f, 90f), 0.03f);
                for (int i = 0; i < 8; i++)
                for (int j = -2; j <= 2; j++)
                    roll.Box((int)MS.Dark, Quaternion.Euler(0f, 0f, i * 45f + j * 22f) * new Vector3(0f, 0.22f, 0f) + new Vector3(0f, 0f, j * 0.24f),
                        new Vector3(0.08f, 0.08f, 0.1f), 0.015f, new Vector3(0f, 0f, i * 45f + j * 22f));
                var rollGo = Part(model, "Roll", roll, table, art, new Vector3(0f, 2.06f, side * 0.3f), new Vector3(0f, 90f, 0f));
                rollGo.name = side < 0 ? "RollFront" : "RollBack";
                spinners.Add(rollGo.transform);
            }

            // Press plate on two hydraulic rams (the piston that slams once per cycle).
            var ramK = new MeshKit();
            for (int side = -1; side <= 1; side += 2)
            {
                ramK.Box((int)MS.Dark, new Vector3(side * 1.25f, 2.85f, -1.2f), new Vector3(0.22f, 2.1f, 0.22f), 0.04f);
                ramK.Cylinder((int)MS.Accent, new Vector3(side * 1.25f, 4.05f, -1.2f), 0.2f, 0.55f, 14, default, 0.04f);
            }

            ramK.Box((int)MS.Dark, new Vector3(0f, 4.38f, -1.2f), new Vector3(2.75f, 0.18f, 0.3f), 0.05f);
            Part(model, "RamFrame", ramK, table, art);
            var press = new MeshKit();
            press.Box((int)MS.Steel, Vector3.zero, new Vector3(2.3f, 0.16f, 0.45f), 0.04f);
            press.Box((int)MS.Hazard, new Vector3(0f, -0.06f, -0.23f), new Vector3(2.3f, 0.06f, 0.02f), 0.005f);
            for (int side = -1; side <= 1; side += 2) press.Cylinder((int)MS.Chrome, new Vector3(side * 0.85f, 0.32f, 0f), 0.07f, 0.6f, 10, default, 0.015f);
            var pressGo = Part(model, "Press", press, table, art, new Vector3(0f, 3.75f, -1.2f));

            var light = Beacon(model, table, art, new Vector3(-1.3f, 1.9f, -1.2f), 0.5f);

            // Level tiers: exhaust, second guard + gauges, floodlights, gold trim.
            var lv = Child(body, "LevelVisuals");
            var l2 = new MeshKit();
            l2.Cylinder((int)MS.Chrome, new Vector3(0.95f, 1.6f, 1.75f), 0.1f, 1.3f, 12, default, 0.02f);
            l2.Cylinder((int)MS.Dark, new Vector3(0.95f, 2.3f, 1.75f), 0.13f, 0.1f, 12, default, 0.02f);
            var l3 = new MeshKit();
            l3.Box((int)MS.AccentDark, new Vector3(-1.52f, 1.0f, 0.35f), new Vector3(0.08f, 1.0f, 1.6f), 0.04f);
            l3.Cylinder((int)MS.White, new Vector3(-0.4f, 1.55f, -1.36f), 0.14f, 0.05f, 16, new Vector3(90f, 0f, 0f), 0.01f);
            l3.Box((int)MS.Main, new Vector3(-0.4f, 1.58f, -1.39f), new Vector3(0.02f, 0.1f, 0.01f), 0.003f, new Vector3(0f, 0f, -30f));
            var l4 = new MeshKit();
            for (int side = -1; side <= 1; side += 2)
            {
                l4.Cylinder((int)MS.Dark, new Vector3(side * 1.6f, 1.6f, -1.6f), 0.05f, 3.2f, 8, default, 0.01f);
                l4.Box((int)MS.Dark, new Vector3(side * 1.45f, 3.2f, -1.6f), new Vector3(0.45f, 0.22f, 0.25f), 0.05f, new Vector3(30f, 0f, 0f));
                l4.Box((int)MS.Light, new Vector3(side * 1.45f, 3.16f, -1.72f), new Vector3(0.38f, 0.16f, 0.03f), 0.02f, new Vector3(30f, 0f, 0f));
            }

            var l5 = new MeshKit();
            l5.Lathe((int)MS.Copper, new Vector3(0f, 1.9f, 0f), new[] { new Vector2(2.15f, 1.72f), new Vector2(2.18f, 1.76f), new Vector2(2.18f, 1.84f),
                new Vector2(2.0f, 1.84f) }, 4, new Vector3(0f, 45f, 0f), false, 30f);
            l5.Box((int)MS.Copper, new Vector3(0f, 0.3f, 0f), new Vector3(2.9f, 0.08f, 2.7f), 0.03f);
            var gold = (Material[])table.Clone();
            gold[(int)MS.Copper] = Mat("MachGold", new Color(1f, 0.8f, 0.25f), 0.75f, 0.45f, Detail.Metal, 1f);
            Tiers(root, lv, (2, Tier(lv, "Lv2_Exhaust", model, l2, table)), (3, Tier(lv, "Lv3_Guard", model, l3, table)), (4, Tier(lv, "Lv4_Floodlights", model, l4, table)),
                (5, Tier(lv, "Lv5_GoldTrim", model, l5, gold)));

            var visuals = root.GetComponent<MachineVisuals>();
            ArtAssets.Set(visuals, ("body", body), ("spinAxis", Vector3.forward), ("spinSpeed", 300f), ("statusLight", light), ("pistonTravel", 0.32f));
            ArtAssets.SetArray(visuals, "spinners", spinners.ToArray());
            ArtAssets.SetArray(visuals, "pistons", new Object[] { pressGo.transform });
            var dust = body.Find("VFX_CrusherDust");
            if (dust != null) dust.localPosition = new Vector3(0f, 3.4f, 0f);
            ArtAssets.Set(root.GetComponent<Machine>(), ("levelVisuals", root.GetComponent<LevelVisuals>()));
            var box = root.GetComponent<BoxCollider>();
            box.center = new Vector3(0f, 1.9f, 0.1f);
            box.size = new Vector3(3.1f, 3.8f, 3.2f);
            var label = root.transform.Find("StationLabel");
            if (label != null) label.localPosition = new Vector3(0f, 5.6f, 1.2f);
        });
    }

    // =====================================================================================
    // Sorter
    // =====================================================================================

    // The Metal Splitter (Revamp 1): the Sorter grown to four chutes. Chute order west → east and the colours match the
    // bins and belts R1_Build places in the plant.
    static readonly (string point, float x, Color color)[] SplitterChutes =
    {
        ("IronOut", -2.1f, new Color(0.55f, 0.6f, 0.68f)),
        ("AluminumOut", -0.7f, new Color(0.72f, 0.84f, 0.95f)),
        ("CopperOut", 0.7f, new Color(0.95f, 0.52f, 0.22f)),
        ("SteelOut", 2.1f, new Color(0.3f, 0.36f, 0.5f)),
    };

    static void Sorter()
    {
        var table = Table(Mat("SorterYellow", new Color(1f, 0.76f, 0.1f), 0.5f, 0.05f, Detail.Paint, 0.7f),
            Mat("SorterYellowDeep", new Color(0.9f, 0.56f, 0.05f), 0.45f, 0.05f, Detail.Paint, 0.7f), Mat("SorterRed", new Color(0.86f, 0.18f, 0.13f), 0.5f, 0.1f, Detail.Paint, 0.7f));
        const string model = "Mach_Sorter";
        const float W = 5.6f, HW = W * 0.5f;   // body width; four chutes 1.4 m apart need it
        EditPrefab($"{StationDir}/Sorter.prefab", root =>
        {
            var body = root.transform.Find("Body");
            StripVisuals(root.transform, "Body", "Markers", "IronOut", "AluminumOut", "CopperOut", "SteelOut", "StationLabel", "InputPad");
            StripVisuals(body, "Intake", "HopperPile", "VFX_CrusherDust", "VFX_OverdriveSparks");
            var markers = root.transform.Find("Markers");
            if (markers != null)
                foreach (Transform m in markers.Cast<Transform>().ToArray())
                    if (m.GetComponent<SpriteRenderer>() == null) Object.DestroyImmediate(m.gameObject);

            // Output points: kept when they exist (the machine's ports and the scene belts reference them), only moved.
            foreach (var (point, x, _) in SplitterChutes)
            {
                var t = root.transform.Find(point);
                if (t == null)
                {
                    t = new GameObject(point).transform;
                    t.SetParent(root.transform, false);
                }

                t.localPosition = new Vector3(x, 0.72f, -1.95f);
            }

            if (root.TryGetComponent(out BoxCollider box))
            {
                box.center = new Vector3(0f, 1.3f, 0.1f);
                box.size = new Vector3(W + 0.3f, 2.6f, 3.0f);
            }

            var pad = root.transform.Find("InputPad");
            if (pad != null) pad.localPosition = new Vector3(-(HW + 1.6f), 0f, 0.1f);
            // The hopper sits a little further back than the old Sorter's, clear of the four gate hoods.
            const float HopperZ = -0.35f;
            foreach (string n in new[] { "Intake", "HopperPile" })
            {
                var t = body.Find(n);
                if (t != null) t.localPosition = new Vector3(0f, t.localPosition.y, HopperZ);
            }


            var baseT = (Material[])table.Clone();
            var baseK = new MeshKit();
            baseK.Box((int)MS.Concrete, new Vector3(0f, 0.12f, 0.05f), new Vector3(W + 0.8f, 0.24f, 3.8f), 0.06f);
            baseK.Box((int)MS.Hazard, new Vector3(0f, 0.245f, -1.82f), new Vector3(W + 0.7f, 0.02f, 0.12f), 0.005f);
            Part(model, "Base", baseK, baseT, Child(root.transform, "ArtBase"));

            // Colour-coded chutes with a gate hood each: the hood and the chute floor carry the material colour, so the
            // four streams read from the game camera before a single item has left.
            for (int i = 0; i < SplitterChutes.Length; i++)
            {
                var (point, x, color) = SplitterChutes[i];
                var chuteT = (Material[])table.Clone();
                chuteT[(int)MS.Copper] = Mat("Chute" + point.Replace("Out", ""), color, 0.55f, 0.25f, Detail.Metal);
                var chute = new MeshKit();
                chute.Box((int)MS.Copper, new Vector3(x, 0.74f, -1.66f), new Vector3(0.95f, 0.08f, 0.9f), 0.02f, new Vector3(-22f, 0f, 0f));
                for (int side = -1; side <= 1; side += 2)
                    chute.Box((int)MS.MainDark, new Vector3(x + side * 0.5f, 0.84f, -1.66f), new Vector3(0.06f, 0.24f, 0.9f), 0.02f, new Vector3(-22f, 0f, 0f));
                chute.Box((int)MS.Copper, new Vector3(x, 1.0f, -1.27f), new Vector3(0.9f, 0.5f, 0.06f), 0.03f);
                // Gate hood over the chute mouth.
                chute.Box((int)MS.Copper, new Vector3(x, 1.72f, -1.02f), new Vector3(1.08f, 0.5f, 0.7f), 0.1f);
                chute.Box((int)MS.Dark, new Vector3(x, 1.5f, -1.38f), new Vector3(0.8f, 0.3f, 0.06f), 0.02f);
                chute.Box((int)MS.Light, new Vector3(x, 1.86f, -1.38f), new Vector3(0.3f, 0.1f, 0.04f), 0.01f);
                Part(model, "Chute_" + point, chute, chuteT, i == 0 ? Child(root.transform, "ArtChutes") : root.transform.Find("ArtChutes"));
            }

            var art = Child(body, "Art");
            var k = new MeshKit();
            k.Box((int)MS.Main, new Vector3(0f, 0.7f, 0.1f), new Vector3(W, 1.4f, 2.7f), 0.18f, default, 2);
            k.Box((int)MS.Dark, new Vector3(0f, 0.22f, 0.1f), new Vector3(W + 0.08f, 0.2f, 2.78f), 0.05f);
            k.Box((int)MS.MainDark, new Vector3(0f, 1.42f, 0.1f), new Vector3(W + 0.14f, 0.12f, 2.84f), 0.04f);
            Bolts(k, new Vector3(-HW + 0.15f, 1.42f, -1.31f), new Vector3(HW - 0.15f, 1.42f, -1.31f), 17);
            k.Box((int)MS.Hazard, new Vector3(0f, 0.55f, -1.26f), new Vector3(W - 0.4f, 0.26f, 0.04f), 0.01f);
            // Screen deck with side walls; the separator drum on its gantry at the back; the input hopper at the west end
            // (above the pad side), so material visibly travels east across the deck past the four gates.
            k.Box((int)MS.Steel, new Vector3(0f, 1.55f, 0.35f), new Vector3(W - 0.4f, 0.08f, 1.7f), 0.02f, new Vector3(-5f, 0f, 0f));
            for (int side = -1; side <= 1; side += 2)
            {
                k.Box((int)MS.MainDark, new Vector3(side * (HW - 0.15f), 1.68f, 0.05f), new Vector3(0.1f, 0.35f, 2.4f), 0.03f, new Vector3(-5f, 0f, 0f));
                k.Box((int)MS.Dark, new Vector3(side * (HW - 0.13f), 2.05f, 0.78f), new Vector3(0.2f, 1.1f, 0.2f), 0.03f);
                k.Cylinder((int)MS.Accent, new Vector3(side * (HW - 0.13f), 2.35f, 0.78f), 0.2f, 0.24f, 12, new Vector3(0f, 0f, 90f), 0.03f);
            }

            k.Lathe((int)MS.MainDark, new Vector3(0f, 1.55f, HopperZ), new[] { new Vector2(0.45f, 0f), new Vector2(0.8f, 0.55f), new Vector2(0.84f, 0.6f), new Vector2(0.76f, 0.6f),
                new Vector2(0.42f, 0.05f) }, 4, new Vector3(0f, 45f, 0f), false, 30f);
            k.Box((int)MS.Dark, new Vector3(0f, 1.6f, HopperZ), new Vector3(0.6f, 0.03f, 0.6f), 0.01f);
            // Feed pipes from the deck down into each gate hood, a motor block on the west end, a control box on the east.
            foreach (var (_, x, _) in SplitterChutes)
            {
                if (Mathf.Abs(x) < 1f) continue;   // the two middle gates sit under the hopper
                k.Cylinder((int)MS.Chrome, new Vector3(x, 1.86f, -0.45f), 0.09f, 0.6f, 10, new Vector3(60f, 0f, 0f), 0.01f);
            }

            k.Box((int)MS.AccentDark, new Vector3(-HW - 0.22f, 0.8f, 0.7f), new Vector3(0.45f, 0.7f, 0.9f), 0.08f);
            k.Cylinder((int)MS.Dark, new Vector3(-HW - 0.3f, 1.3f, 0.7f), 0.2f, 0.3f, 12, default, 0.03f);
            k.Box((int)MS.White, new Vector3(HW + 0.2f, 0.95f, 0.6f), new Vector3(0.4f, 0.9f, 0.7f), 0.06f);
            k.Box((int)MS.Dark, new Vector3(HW + 0.41f, 1.1f, 0.6f), new Vector3(0.03f, 0.35f, 0.5f), 0.01f);
            k.Box((int)MS.Light, new Vector3(HW + 0.43f, 1.15f, 0.6f), new Vector3(0.02f, 0.2f, 0.36f), 0.005f);
            // Hose loops along the back.
            for (int i = -1; i <= 1; i++)
                k.Torus((int)MS.Rubber, new Vector3(i * 1.6f, 1.2f, 1.47f), 0.28f, 0.05f, 14, 6, new Vector3(0f, 0f, 0f));
            Part(model, "Body", k, table, art);

            var spinners = new List<Object>();
            for (int i = 0; i < 4; i++)
            {
                var roller = new MeshKit();
                roller.Cylinder((int)MS.Dark, Vector3.zero, 0.11f, W - 0.6f, 10, new Vector3(0f, 90f, 90f), 0.02f);
                for (int j = 0; j < 9; j++) roller.Torus((int)MS.Steel, new Vector3(0f, 0f, -2.2f + j * 0.55f), 0.11f, 0.025f, 10, 5, new Vector3(90f, 0f, 0f));
                var go = Part(model, "Roller", roller, table, art, new Vector3(0f, 1.7f - i * 0.03f, -0.1f + i * 0.34f), new Vector3(0f, 90f, 0f));
                spinners.Add(go.transform);
            }

            // Separator drum: one coloured band per material, so the spin reads as "sorting".
            var drum = new MeshKit();
            drum.Cylinder((int)MS.Copper, Vector3.zero, 0.42f, W - 0.5f, 18, new Vector3(0f, 90f, 90f), 0.05f);
            for (int j = 0; j < 5; j++) drum.Torus((int)MS.Dark, new Vector3(0f, 0f, -2.4f + j * 1.2f), 0.42f, 0.04f, 18, 5, new Vector3(90f, 0f, 0f));
            for (int j = 0; j < 8; j++) drum.Box((int)MS.Dark, new Vector3(0f, 0.42f, -2.1f + j * 0.6f), new Vector3(0.1f, 0.08f, 0.3f), 0.02f);
            var magnet = (Material[])table.Clone();
            magnet[(int)MS.Copper] = Mat("SorterRed", new Color(0.86f, 0.18f, 0.13f), 0.5f, 0.1f, Detail.Paint, 0.7f);
            var drumGo = Part(model, "MagnetDrum", drum, magnet, art, new Vector3(0f, 2.35f, 0.78f), new Vector3(0f, 90f, 0f));
            spinners.Add(drumGo.transform);

            // A sorting paddle per gate knocks material into its chute each cycle.
            var pistons = new List<Object>();
            foreach (var (point, x, _) in SplitterChutes)
            {
                var frame = new MeshKit();
                frame.Box((int)MS.Dark, new Vector3(0f, 0.3f, 0.3f), new Vector3(0.1f, 0.6f, 0.1f), 0.02f);
                frame.Box((int)MS.Dark, new Vector3(0f, 0.6f, 0.05f), new Vector3(0.1f, 0.1f, 0.6f), 0.02f);
                Part(model, "PaddleFrame_" + point, frame, table, art, new Vector3(x, 1.95f, -0.75f));
                var paddle = new MeshKit();
                paddle.Cylinder((int)MS.Chrome, new Vector3(0f, 0.25f, 0f), 0.045f, 0.5f, 8, default, 0.01f);
                paddle.Box((int)MS.Accent, Vector3.zero, new Vector3(0.5f, 0.08f, 0.26f), 0.03f);
                var pg = Part(model, "Paddle_" + point, paddle, table, art, new Vector3(x, 2.32f, -1.0f));
                pistons.Add(pg.transform);
            }

            var light = Beacon(model, table, art, new Vector3(-HW + 0.2f, 1.48f, 1.2f), 0.6f);
            var visuals = root.GetComponent<MachineVisuals>();
            ArtAssets.Set(visuals, ("body", body), ("spinAxis", Vector3.forward), ("spinSpeed", 360f), ("statusLight", light), ("pistonTravel", 0.25f));
            ArtAssets.SetArray(visuals, "spinners", spinners.ToArray());
            ArtAssets.SetArray(visuals, "pistons", pistons.ToArray());
        });
    }

    // =====================================================================================
    // Furnace
    // =====================================================================================

    // The Industrial Press (Revamp 3): ingots in, bars out. A hydraulic press on four chrome columns: a heavy blue crown
    // with the cylinder on top, a ram that slams onto the anvil every cycle (the machine's piston), a hot billet glowing on
    // the anvil while it works, a flywheel on the east side, feed tray west (toward the pad), output slide south. Kept
    // under 5 m and open between the columns so it hides little of the floor north of it.
    static void Press()
    {
        string path = $"{StationDir}/Press.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;   // created by R3_Build.Prefabs
        var table = Table(Mat("PressBlue", new Color(0.13f, 0.4f, 0.64f), 0.45f, 0.15f, Detail.Paint, 0.7f), Mat("PressBlueDeep", new Color(0.08f, 0.23f, 0.4f), 0.4f, 0.15f, Detail.Paint, 0.7f));
        const string model = "Mach_Press";
        var glowMat = Mat("PressGlow", new Color(1f, 0.55f, 0.15f), 0.6f, 0f, Detail.Plain, 1f, new Color(2.6f, 1.1f, 0.2f));
        EditPrefab(path, root =>
        {
            var body = root.transform.Find("Body");
            StripVisuals(root.transform, "Body", "OutputPoint", "StationLabel", "InputPad", "VFX_IngotSparks");
            StripVisuals(body, "Intake", "HopperPile", "VFX_FurnaceSmoke", "VFX_OverdriveSparks");
            foreach (var n in new[] { "Lv3_Chimney", "Lv5_Crown" })
            {
                var t = body.Find(n);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }

            var baseK = new MeshKit();
            baseK.Box((int)MS.Concrete, new Vector3(0f, 0.12f, 0f), new Vector3(5f, 0.24f, 3.9f), 0.06f);
            foreach (int sz in new[] { -1, 1 }) baseK.Box((int)MS.Hazard, new Vector3(0f, 0.245f, sz * 1.84f), new Vector3(4.9f, 0.02f, 0.12f), 0.005f);
            baseK.Box((int)MS.Steel, new Vector3(0f, 0.68f, -1.95f), new Vector3(1.3f, 0.07f, 0.9f), 0.02f, new Vector3(-22f, 0f, 0f));
            for (int side = -1; side <= 1; side += 2)
                baseK.Box((int)MS.Dark, new Vector3(side * 0.68f, 0.78f, -1.95f), new Vector3(0.06f, 0.22f, 0.9f), 0.02f, new Vector3(-22f, 0f, 0f));
            Part(model, "Base", baseK, table, Child(root.transform, "ArtBase"));

            var art = Child(body, "Art");
            var k = new MeshKit();
            // Bed and anvil.
            k.Box((int)MS.Dark, new Vector3(0f, 0.5f, 0f), new Vector3(4.4f, 1f, 3.4f), 0.1f, default, 2);
            k.Box((int)MS.Hazard, new Vector3(0f, 1.03f, 0f), new Vector3(4.46f, 0.1f, 3.46f), 0.03f);
            k.Box((int)MS.Steel, new Vector3(0f, 1.19f, 0f), new Vector3(2.4f, 0.22f, 2.1f), 0.04f);
            // Columns and crown.
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                {
                    k.Cylinder((int)MS.Chrome, new Vector3(sx * 1.6f, 2.08f, sz * 1.2f), 0.17f, 2f, 14, default, 0.02f);
                    k.Cylinder((int)MS.Dark, new Vector3(sx * 1.6f, 1.14f, sz * 1.2f), 0.26f, 0.14f, 14, default, 0.03f);
                }

            k.Box((int)MS.Main, new Vector3(0f, 3.4f, 0f), new Vector3(4.1f, 0.78f, 3.15f), 0.14f, default, 2);
            k.Box((int)MS.MainDark, new Vector3(0f, 3.83f, 0f), new Vector3(4.2f, 0.12f, 3.25f), 0.04f);
            k.Box((int)MS.Hazard, new Vector3(0f, 3.05f, -1.585f), new Vector3(3.6f, 0.14f, 0.03f), 0.01f);
            Bolts(k, new Vector3(-1.8f, 3.62f, -1.58f), new Vector3(1.8f, 3.62f, -1.58f), 11);
            // Hydraulic cylinder, hoses and gauges.
            k.Cylinder((int)MS.MainDark, new Vector3(0f, 4.3f, 0f), 0.58f, 0.86f, 18, default, 0.06f);
            k.Cylinder((int)MS.Accent, new Vector3(0f, 4.78f, 0f), 0.64f, 0.14f, 18, default, 0.04f);
            foreach (int sx in new[] { -1, 1 })
            {
                k.Cylinder((int)MS.Rubber, new Vector3(sx * 0.95f, 4.25f, 0.5f), 0.07f, 0.9f, 8, default, 0.01f);
                k.Torus((int)MS.Rubber, new Vector3(sx * 0.72f, 4.7f, 0.5f), 0.23f, 0.07f, 12, 6, new Vector3(0f, 0f, 0f), null, 180f, 0f);
                k.Cylinder((int)MS.White, new Vector3(sx * 1.25f, 3.4f, -1.6f), 0.2f, 0.05f, 16, new Vector3(90f, 0f, 0f), 0.01f);
                k.Box((int)MS.Dark, new Vector3(sx * 1.25f, 3.44f, -1.63f), new Vector3(0.025f, 0.14f, 0.01f), 0.003f, new Vector3(0f, 0f, sx * 35f));
            }

            // Feed tray toward the pad (west) and a control box.
            k.Box((int)MS.Steel, new Vector3(-2.62f, 1.05f, 0f), new Vector3(0.95f, 0.1f, 1.3f), 0.02f);
            foreach (int sz in new[] { -1, 1 }) k.Box((int)MS.Dark, new Vector3(-2.62f, 1.18f, sz * 0.67f), new Vector3(0.95f, 0.24f, 0.06f), 0.02f);
            k.Box((int)MS.White, new Vector3(2.0f, 1.6f, -1.45f), new Vector3(0.5f, 0.9f, 0.4f), 0.06f);
            k.Box((int)MS.Light, new Vector3(2.0f, 1.75f, -1.66f), new Vector3(0.34f, 0.3f, 0.02f), 0.005f);
            Ladder(k, new Vector3(-1.2f, 1f, 1.72f), 2.8f, new Vector3(0f, 180f, 0f));
            Part(model, "Body", k, table, art);

            // The ram: rod and platen. MachineVisuals stomps it down by pistonTravel each cycle.
            var ram = new MeshKit();
            ram.Cylinder((int)MS.Chrome, new Vector3(0f, 0.72f, 0f), 0.3f, 1.1f, 16, default, 0.02f);
            ram.Box((int)MS.Dark, new Vector3(0f, 0f, 0f), new Vector3(2.2f, 0.4f, 1.95f), 0.06f);
            ram.Box((int)MS.Hazard, new Vector3(0f, 0f, -0.985f), new Vector3(2.0f, 0.2f, 0.02f), 0.005f);
            var ramGo = Part(model, "Ram", ram, table, art, new Vector3(0f, 2.56f, 0f));

            var billet = new MeshKit();
            billet.Box(0, Vector3.zero, new Vector3(1.3f, 0.14f, 0.9f), 0.04f);
            var billetGo = Part(model, "Billet", billet, new[] { glowMat }, art, new Vector3(0f, 1.37f, 0f));

            var wheel = new MeshKit();
            wheel.Cylinder((int)MS.Dark, Vector3.zero, 0.7f, 0.16f, 20, new Vector3(90f, 0f, 0f), 0.03f);
            wheel.Torus((int)MS.Accent, Vector3.zero, 0.6f, 0.06f, 20, 6, new Vector3(90f, 0f, 0f));
            for (int i = 0; i < 4; i++) wheel.Box((int)MS.Accent, Vector3.zero, new Vector3(0.1f, 1.1f, 0.2f), 0.02f, new Vector3(0f, 0f, i * 45f));
            wheel.Cylinder((int)MS.Chrome, Vector3.zero, 0.14f, 0.24f, 12, new Vector3(90f, 0f, 0f), 0.02f);
            var wheelGo = Part(model, "Flywheel", wheel, table, art, new Vector3(2.32f, 2.05f, 0.25f), new Vector3(0f, 90f, 0f));
            var light = Beacon(model, table, art, new Vector3(-2.0f, 1.08f, -1.5f), 0.5f);

            var l3 = Child(body, "Lv3_Chimney");
            var l3k = new MeshKit();
            foreach (int sz in new[] { -1, 1 })
            {
                l3k.Cylinder((int)MS.Accent, new Vector3(2.5f, 1.75f, 0.9f + sz * 0.36f), 0.3f, 1.5f, 14, default, 0.05f);
                l3k.Cylinder((int)MS.Dark, new Vector3(2.5f, 2.56f, 0.9f + sz * 0.36f), 0.2f, 0.14f, 12, default, 0.03f);
            }

            Part(model, "Lv3", l3k, table, l3);
            var l5 = Child(body, "Lv5_Crown");
            var l5k = new MeshKit();
            l5k.Box((int)MS.Copper, new Vector3(0f, 3.4f, -1.6f), new Vector3(4.14f, 0.1f, 0.06f), 0.02f);
            l5k.Torus((int)MS.Copper, new Vector3(0f, 4.86f, 0f), 0.5f, 0.07f, 18, 6);
            var gold = (Material[])table.Clone();
            gold[(int)MS.Copper] = Mat("MachGold", new Color(1f, 0.8f, 0.25f), 0.75f, 0.45f, Detail.Metal, 1f);
            Part(model, "Lv5", l5k, gold, l5);
            Tiers(root, body, (3, l3.gameObject), (5, l5.gameObject));

            var steam = body.Find("VFX_FurnaceSmoke");
            if (steam != null) steam.localPosition = new Vector3(0f, 4.95f, 0f);
            var visuals = root.GetComponent<MachineVisuals>();
            ArtAssets.Set(visuals, ("body", body), ("spinAxis", Vector3.forward), ("spinSpeed", 240f), ("statusLight", light), ("glow", billetGo.GetComponent<Renderer>()),
                ("pistonTravel", 0.98f));
            ArtAssets.SetArray(visuals, "spinners", new Object[] { wheelGo.transform });
            ArtAssets.SetArray(visuals, "pistons", new Object[] { ramGo.transform });
            ArtAssets.Set(root.GetComponent<Machine>(), ("levelVisuals", root.GetComponent<LevelVisuals>()));
        });
    }

    // The furnace battery (Revamp 2): four furnaces, one per metal, standing side by side along the hall's north wall.
    // One builder, four silhouettes: iron = charcoal dome with orange bands, copper = red box with twin stacks and
    // copper pipes, aluminum = tall silver kiln with blue bands, steel = wide dark stepped blast furnace with three
    // stacks. Each keeps the metal's colour from the Splitter's chutes on its bands or trim. Front = -z: hopper shelf
    // left, glowing mouth, output chute right. R2_Build owns the data side (definitions, pad icon, ports).
    enum FurnaceStyle { Dome, Box, Tower, Blast }

    static readonly (string prefab, string key, FurnaceStyle style, float width, Color shell, Color shellDark, Color accent, Color glow, Color emission)[] FurnaceSpecs =
    {
        ("Furnace", "Iron", FurnaceStyle.Dome, 2.8f, new Color(0.22f, 0.22f, 0.25f), new Color(0.13f, 0.13f, 0.15f), new Color(1f, 0.46f, 0.1f), new Color(1f, 0.5f, 0.12f),
            new Color(2.4f, 0.9f, 0.15f)),
        ("Furnace_Copper", "Copper", FurnaceStyle.Box, 2.8f, new Color(0.78f, 0.29f, 0.16f), new Color(0.5f, 0.17f, 0.1f), new Color(0.95f, 0.52f, 0.22f), new Color(1f, 0.38f, 0.1f),
            new Color(2.6f, 0.6f, 0.1f)),
        ("Furnace_Aluminum", "Aluminum", FurnaceStyle.Tower, 2.8f, new Color(0.8f, 0.84f, 0.88f), new Color(0.52f, 0.6f, 0.7f), new Color(0.42f, 0.7f, 0.96f), new Color(1f, 0.86f, 0.56f),
            new Color(2.2f, 1.6f, 0.9f)),
        ("Furnace_Steel", "Steel", FurnaceStyle.Blast, 3.3f, new Color(0.19f, 0.24f, 0.38f), new Color(0.11f, 0.14f, 0.23f), new Color(0.86f, 0.18f, 0.13f), new Color(1f, 0.78f, 0.32f),
            new Color(3.2f, 1.8f, 0.5f)),
    };

    static void Furnace()
    {
        foreach (var spec in FurnaceSpecs)
        {
            string path = $"{StationDir}/{spec.prefab}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;   // created by R2_Build.Prefabs
            FurnaceModel(path, spec.key, spec.style, spec.width, spec.shell, spec.shellDark, spec.accent, spec.glow, spec.emission);
        }
    }

    static void FurnaceModel(string path, string key, FurnaceStyle style, float w, Color shell, Color shellDark, Color accent, Color glow, Color emission)
    {
        var table = Table(Mat("Furnace" + key, shell, 0.4f, 0.15f, Detail.Metal, 0.8f), Mat("Furnace" + key + "Deep", shellDark, 0.35f, 0.15f, Detail.Metal, 0.8f),
            Mat("Furnace" + key + "Trim", accent, 0.5f, 0.1f, Detail.Paint, 0.7f));
        string model = "Mach_Furnace" + key;
        var glowMat = Mat("FurnaceMouth" + key, glow, 0.6f, 0f, Detail.Plain, 1f, emission);
        float hw = w * 0.5f;
        EditPrefab(path, root =>
        {
            var body = root.transform.Find("Body");
            StripVisuals(root.transform, "Body", "OutputPoint", "StationLabel", "InputPad", "PadIcon", "VFX_IngotSparks");
            StripVisuals(body, "Intake", "HopperPile", "VFX_FurnaceSmoke", "VFX_OverdriveSparks");
            foreach (var n in new[] { "Lv3_Chimney", "Lv5_Crown" })
            {
                var t = body.Find(n);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }

            // Base slab and the output chute (front right).
            var baseK = new MeshKit();
            baseK.Box((int)MS.Concrete, new Vector3(0f, 0.1f, 0.05f), new Vector3(w + 0.4f, 0.2f, 3.5f), 0.05f);
            baseK.Box((int)MS.Hazard, new Vector3(0f, 0.205f, -1.62f), new Vector3(w + 0.3f, 0.02f, 0.12f), 0.005f);
            baseK.Box((int)MS.Steel, new Vector3(0.85f, 0.66f, -1.7f), new Vector3(0.7f, 0.07f, 0.7f), 0.02f, new Vector3(-22f, 0f, 0f));
            for (int side = -1; side <= 1; side += 2)
                baseK.Box((int)MS.Dark, new Vector3(0.85f + side * 0.38f, 0.76f, -1.7f), new Vector3(0.06f, 0.22f, 0.7f), 0.02f, new Vector3(-22f, 0f, 0f));
            Part(model, "Base", baseK, table, Child(root.transform, "ArtBase"));

            var art = Child(body, "Art");
            var k = new MeshKit();
            // Plinth, hopper shelf (front left), mouth frame.
            k.Box((int)MS.Dark, new Vector3(0f, 0.6f, 0.05f), new Vector3(w, 1.2f, 3.0f), 0.1f, default, 2);
            k.Box((int)MS.Hazard, new Vector3(0f, 1.23f, 0.05f), new Vector3(w + 0.05f, 0.1f, 3.05f), 0.03f);
            k.Box((int)MS.Steel, new Vector3(-hw + 0.62f, 1.32f, -1.4f), new Vector3(1.05f, 0.1f, 0.8f), 0.02f);
            k.Box((int)MS.Dark, new Vector3(-hw + 0.08f, 1.45f, -1.4f), new Vector3(0.07f, 0.26f, 0.8f), 0.02f);
            float mouthW = style == FurnaceStyle.Blast ? 1.3f : 0.95f, mouthX = hw - mouthW * 0.5f - 0.2f;
            k.Box((int)MS.Dark, new Vector3(mouthX, 1.92f, -1.2f), new Vector3(mouthW + 0.3f, 1.05f, 0.34f), 0.05f);
            k.Cylinder((int)MS.White, new Vector3(-hw + 0.3f, 0.85f, -1.47f), 0.15f, 0.05f, 14, new Vector3(90f, 0f, 0f), 0.01f);
            k.Box((int)MS.Dark, new Vector3(-hw + 0.3f, 0.88f, -1.5f), new Vector3(0.02f, 0.1f, 0.01f), 0.003f, new Vector3(0f, 0f, 35f));

            Vector3 smoke, extraStack;
            float crownY, crownR;
            switch (style)
            {
                case FurnaceStyle.Dome:
                    k.Lathe((int)MS.Main, new Vector3(0f, 1.28f, 0.2f), new[] { new Vector2(hw - 0.12f, 0f), new Vector2(hw - 0.08f, 0.5f), new Vector2(hw - 0.2f, 1.4f),
                        new Vector2(hw - 0.5f, 1.95f), new Vector2(0.6f, 2.25f), new Vector2(0.42f, 2.3f) }, 18, default, true, 30f);
                    foreach (float y in new[] { 0.4f, 1.25f }) k.Torus((int)MS.Accent, new Vector3(0f, 1.28f + y, 0.2f), hw - (y < 1f ? 0.07f : 0.15f), 0.07f, 20, 6);
                    k.Cylinder((int)MS.MainDark, new Vector3(0.55f, 4.55f, 0.65f), 0.34f, 2.7f, 14, default, 0.04f);
                    k.Cylinder((int)MS.Accent, new Vector3(0.55f, 5.95f, 0.65f), 0.42f, 0.2f, 14, default, 0.04f);
                    smoke = new Vector3(0.55f, 6.1f, 0.65f);
                    extraStack = new Vector3(-0.6f, 4.2f, 0.75f);
                    crownY = 3.55f;
                    crownR = 0.62f;
                    break;
                case FurnaceStyle.Box:
                    k.Box((int)MS.Main, new Vector3(0f, 2.15f, 0.25f), new Vector3(w - 0.25f, 1.75f, 2.3f), 0.14f, default, 2);
                    k.Box((int)MS.MainDark, new Vector3(0f, 3.08f, 0.25f), new Vector3(w - 0.1f, 0.16f, 2.45f), 0.04f);
                    Bolts(k, new Vector3(-hw + 0.3f, 2.85f, -0.92f), new Vector3(hw - 0.3f, 2.85f, -0.92f), 7);
                    foreach (int side in new[] { -1, 1 })
                    {
                        k.Cylinder((int)MS.MainDark, new Vector3(side * 0.62f, 3.95f, 0.85f), 0.3f, 1.7f, 12, default, 0.03f);
                        k.Cylinder((int)MS.Dark, new Vector3(side * 0.62f, 4.85f, 0.85f), 0.37f, 0.18f, 12, default, 0.03f);
                        k.Cylinder((int)MS.Copper, new Vector3(side * (hw - 0.02f), 2.1f, 0.3f), 0.09f, 1.7f, 10, default, 0.02f);
                        k.Torus((int)MS.Copper, new Vector3(side * (hw - 0.3f), 2.95f, 0.3f), 0.28f, 0.09f, 12, 6, new Vector3(90f, 0f, 0f), null, 90f, side > 0 ? 0f : 90f);
                    }

                    smoke = new Vector3(0.62f, 5f, 0.85f);
                    extraStack = new Vector3(0f, 3.8f, 0.2f);
                    crownY = 3.2f;
                    crownR = 0f;
                    break;
                case FurnaceStyle.Tower:
                    k.Cylinder((int)MS.Main, new Vector3(0f, 2.9f, 0.3f), 1.12f, 3.3f, 20, default, 0.08f);
                    foreach (float y in new[] { 1.7f, 2.9f, 4.1f }) k.Torus((int)MS.Accent, new Vector3(0f, y, 0.3f), 1.14f, 0.07f, 22, 6);
                    k.Cylinder((int)MS.MainDark, new Vector3(0f, 4.68f, 0.3f), 0.9f, 0.26f, 18, default, 0.06f);
                    k.Cylinder((int)MS.Steel, new Vector3(0f, 5.25f, 0.3f), 0.28f, 0.9f, 12, default, 0.03f);
                    k.Box((int)MS.MainDark, new Vector3(hw - 0.22f, 2.35f, 1.0f), new Vector3(0.46f, 2.3f, 0.5f), 0.08f);
                    k.Box((int)MS.Accent, new Vector3(hw - 0.22f, 3.55f, 1.0f), new Vector3(0.52f, 0.12f, 0.56f), 0.03f);
                    smoke = new Vector3(0f, 5.8f, 0.3f);
                    extraStack = new Vector3(-0.85f, 4.5f, 0.9f);
                    crownY = 4.55f;
                    crownR = 0.94f;
                    break;
                default:
                    k.Box((int)MS.Main, new Vector3(0f, 2.05f, 0.2f), new Vector3(w - 0.1f, 1.6f, 2.6f), 0.12f, default, 2);
                    k.Box((int)MS.MainDark, new Vector3(0f, 3.3f, 0.2f), new Vector3(w - 0.7f, 0.95f, 2.1f), 0.12f);
                    k.Box((int)MS.Main, new Vector3(0f, 4.05f, 0.2f), new Vector3(w - 1.3f, 0.6f, 1.6f), 0.1f);
                    k.Box((int)MS.Hazard, new Vector3(0f, 2.86f, 0.2f), new Vector3(w - 0.05f, 0.1f, 2.65f), 0.03f);
                    Bolts(k, new Vector3(-hw + 0.3f, 2.6f, -1.12f), new Vector3(hw - 0.3f, 2.6f, -1.12f), 9);
                    foreach (float x in new[] { -0.75f, 0f, 0.75f })
                    {
                        k.Cylinder((int)MS.MainDark, new Vector3(x, 5.3f, 0.6f), 0.28f, 2.1f, 12, default, 0.03f);
                        k.Cylinder((int)MS.Accent, new Vector3(x, 6.4f, 0.6f), 0.34f, 0.16f, 12, default, 0.03f);
                    }

                    foreach (int side in new[] { -1, 1 })
                        k.Cylinder((int)MS.Steel, new Vector3(side * (hw + 0.02f), 2.2f, 0.9f), 0.2f, 2.0f, 12, default, 0.04f);
                    smoke = new Vector3(0f, 6.6f, 0.6f);
                    extraStack = new Vector3(0f, 4.6f, -0.35f);
                    crownY = 4.38f;
                    crownR = 0f;
                    break;
            }

            Part(model, "Body", k, table, art);

            var mouth = new MeshKit();
            mouth.Box(0, Vector3.zero, new Vector3(mouthW, 0.74f, 0.06f), 0.04f);
            var mouthGo = Part(model, "MouthGlow", mouth, new[] { glowMat }, art, new Vector3(mouthX, 1.9f, -1.38f));

            var fan = new MeshKit();
            fan.Cylinder((int)MS.Dark, Vector3.zero, 0.42f, 0.1f, 16, new Vector3(90f, 0f, 0f), 0.02f);
            for (int i = 0; i < 6; i++) fan.Box((int)MS.Steel, Quaternion.Euler(0f, 0f, i * 60f) * new Vector3(0f, 0.2f, -0.06f), new Vector3(0.14f, 0.34f, 0.03f), 0.01f,
                new Vector3(0f, 25f, i * 60f));
            fan.Cylinder((int)MS.Accent, new Vector3(0f, 0f, -0.08f), 0.09f, 0.06f, 10, new Vector3(90f, 0f, 0f), 0.01f);
            var fanGo = Part(model, "Fan", fan, table, art, new Vector3(-hw - 0.06f, 0.75f, 0.6f), new Vector3(0f, -90f, 0f));
            var bellows = new MeshKit();
            bellows.Cylinder((int)MS.Chrome, Vector3.zero, 0.2f, 0.42f, 12, default, 0.04f);
            bellows.Torus((int)MS.Rubber, new Vector3(0f, 0.08f, 0f), 0.2f, 0.035f, 12, 5);
            var bellowsGo = Part(model, "Bellows", bellows, table, art, new Vector3(-hw + 0.3f, 1.55f, 0.9f));
            var light = Beacon(model, table, art, new Vector3(hw - 0.14f, 1.28f, 1.3f), 0.5f);

            var l3 = Child(body, "Lv3_Chimney");
            var l3k = new MeshKit();
            l3k.Cylinder((int)MS.MainDark, extraStack, 0.26f, 1.9f, 12, default, 0.03f);
            l3k.Cylinder((int)MS.Accent, extraStack + Vector3.up * 1.02f, 0.32f, 0.16f, 12, default, 0.03f);
            Part(model, "Lv3", l3k, table, l3);
            var l5 = Child(body, "Lv5_Crown");
            var l5k = new MeshKit();
            if (crownR > 0f) l5k.Torus((int)MS.Copper, new Vector3(0f, crownY, style == FurnaceStyle.Dome ? 0.2f : 0.3f), crownR, 0.08f, 20, 6);
            else l5k.Box((int)MS.Copper, new Vector3(0f, crownY, 0.2f), new Vector3(w - (style == FurnaceStyle.Blast ? 1.2f : 0.05f), 0.1f, style == FurnaceStyle.Blast ? 1.7f : 2.5f), 0.03f);
            l5k.Box((int)MS.Copper, new Vector3(mouthX, 2.5f, -1.4f), new Vector3(mouthW + 0.36f, 0.08f, 0.08f), 0.02f);
            var gold = (Material[])table.Clone();
            gold[(int)MS.Copper] = Mat("MachGold", new Color(1f, 0.8f, 0.25f), 0.75f, 0.45f, Detail.Metal, 1f);
            Part(model, "Lv5", l5k, gold, l5);
            Tiers(root, body, (3, l3.gameObject), (5, l5.gameObject));

            var smokeVfx = body.Find("VFX_FurnaceSmoke");
            if (smokeVfx != null) smokeVfx.localPosition = smoke;
            var visuals = root.GetComponent<MachineVisuals>();
            ArtAssets.Set(visuals, ("body", body), ("spinAxis", Vector3.forward), ("spinSpeed", 300f), ("statusLight", light), ("glow", mouthGo.GetComponent<Renderer>()),
                ("pistonTravel", 0.22f));
            ArtAssets.SetArray(visuals, "spinners", new Object[] { fanGo.transform });
            ArtAssets.SetArray(visuals, "pistons", new Object[] { bellowsGo.transform });
            ArtAssets.Set(root.GetComponent<Machine>(), ("levelVisuals", root.GetComponent<LevelVisuals>()));
        });
    }

    // =====================================================================================

    static Material BinMain => Mat("BinBlue", new Color(0.18f, 0.46f, 0.86f), 0.45f, 0.05f, Detail.Paint, 0.7f);
    static Material BinDark => Mat("BinBlueDeep", new Color(0.11f, 0.27f, 0.56f), 0.45f, 0.05f, Detail.Paint, 0.7f);

    static void Storage()
    {
        var table = Table(BinMain, BinDark);
        const string model = "Mach_Storage";
        EditPrefab($"{StationDir}/Storage.prefab", root =>
        {
            StripVisuals(root.transform, "Pile", "StationLabel", "WithdrawPad");
            var lvOld = root.transform.Find("LevelVisuals");
            if (lvOld != null) Object.DestroyImmediate(lvOld.gameObject);
            var art = Child(root.transform, "Art");
            var k = new MeshKit();
            BinShell(k, 1.0f, 0f);
            Part(model, "Bin", k, table, art);
            var lv = Child(root.transform, "LevelVisuals");
            var tall = new MeshKit();
            BinShell(tall, 0.5f, 1.0f, false);
            var lamps = new MeshKit();
            foreach (var x in new[] { -1.72f, 1.72f })
            foreach (var z in new[] { -1.42f, 1.42f })
            {
                lamps.Cylinder((int)MS.Dark, new Vector3(x, 1.6f, z), 0.05f, 1.4f, 8, default, 0.01f);
                lamps.Sphere((int)MS.Light, new Vector3(x, 2.35f, z), Vector3.one * 0.13f, 10, 6);
                lamps.Cylinder((int)MS.Dark, new Vector3(x, 2.46f, z), 0.16f, 0.06f, 10, default, 0.01f);
            }

            Tiers(root, lv, (2, Tier(lv, "Lv2_TallWalls", model, tall, table)), (3, Tier(lv, "Lv3_Lamps", model, lamps, table)));
            ArtAssets.Set(root.GetComponent<ScrapYardKing.Factory.Storage>(), ("levelVisuals", root.GetComponent<LevelVisuals>()));
        });
    }

    /// <summary>Ribbed bin walls (main colour) on a dark frame with steel corner posts and a hazard rim.</summary>
    static void BinShell(MeshKit k, float height, float baseY, bool floor = true)
    {
        const float hx = 1.72f, hz = 1.42f;
        if (floor) k.Box((int)MS.Dark, new Vector3(0f, 0.1f, 0f), new Vector3(hx * 2f + 0.2f, 0.2f, hz * 2f + 0.2f), 0.04f);
        float y = baseY + 0.2f + height * 0.5f;
        if (!floor) y = baseY + height * 0.5f;
        k.Box((int)MS.Main, new Vector3(0f, y, hz), new Vector3(hx * 2f, height, 0.14f), 0.04f);
        k.Box((int)MS.Main, new Vector3(0f, y, -hz), new Vector3(hx * 2f, height, 0.14f), 0.04f);
        k.Box((int)MS.Main, new Vector3(hx, y, 0f), new Vector3(0.14f, height, hz * 2f), 0.04f);
        k.Box((int)MS.Main, new Vector3(-hx, y, 0f), new Vector3(0.14f, height, hz * 2f), 0.04f);
        for (int i = -3; i <= 3; i++)
        {
            k.Box((int)MS.MainDark, new Vector3(i * 0.45f, y, hz + 0.08f), new Vector3(0.12f, height * 0.9f, 0.05f), 0.02f);
            k.Box((int)MS.MainDark, new Vector3(i * 0.45f, y, -hz - 0.08f), new Vector3(0.12f, height * 0.9f, 0.05f), 0.02f);
        }

        for (int i = -2; i <= 2; i++)
        {
            k.Box((int)MS.MainDark, new Vector3(hx + 0.08f, y, i * 0.5f), new Vector3(0.05f, height * 0.9f, 0.12f), 0.02f);
            k.Box((int)MS.MainDark, new Vector3(-hx - 0.08f, y, i * 0.5f), new Vector3(0.05f, height * 0.9f, 0.12f), 0.02f);
        }

        float top = y + height * 0.5f;
        k.Box((int)MS.Hazard, new Vector3(0f, top + 0.04f, hz), new Vector3(hx * 2f + 0.12f, 0.08f, 0.2f), 0.02f);
        k.Box((int)MS.Hazard, new Vector3(0f, top + 0.04f, -hz), new Vector3(hx * 2f + 0.12f, 0.08f, 0.2f), 0.02f);
        k.Box((int)MS.Hazard, new Vector3(hx, top + 0.04f, 0f), new Vector3(0.2f, 0.08f, hz * 2f), 0.02f);
        k.Box((int)MS.Hazard, new Vector3(-hx, top + 0.04f, 0f), new Vector3(0.2f, 0.08f, hz * 2f), 0.02f);
        float postBottom = floor ? 0f : baseY;
        foreach (var x in new[] { -hx, hx })
        foreach (var z in new[] { -hz, hz })
            k.Box((int)MS.Steel, new Vector3(x, (top + 0.12f + postBottom) * 0.5f, z), new Vector3(0.24f, top + 0.12f - postBottom, 0.24f), 0.05f);
    }

    static Material StallMain => Mat("StallRed", new Color(0.88f, 0.2f, 0.15f), 0.45f, 0.05f, Detail.Paint, 0.7f);

    static void SellDesk()
    {
        var table = Table(StallMain, Mat("StallRedDeep", new Color(0.62f, 0.13f, 0.1f), 0.45f, 0.05f, Detail.Paint, 0.7f));
        const string model = "Mach_SellDesk";
        EditPrefab($"{StationDir}/SellDesk.prefab", root =>
        {
            StripVisuals(root.transform, "Register", "CounterPile", "BuyerPoint", "CashPile", "CashPad", "StationLabel", "StockPad");
            var register = root.transform.Find("Register");
            if (register != null) StripVisuals(register);
            var lvOld = root.transform.Find("LevelVisuals");
            if (lvOld != null) Object.DestroyImmediate(lvOld.gameObject);

            var art = Child(root.transform, "Art");
            var k = new MeshKit();
            // Deck, counter (wood top, painted planked front), posts, striped awning sloping to the customers.
            k.Box((int)MS.Wood, new Vector3(0f, 0.06f, 0.2f), new Vector3(4.4f, 0.12f, 2.2f), 0.03f);
            for (int i = -4; i <= 4; i++) k.Box((int)MS.Dark, new Vector3(i * 0.48f, 0.125f, 0.2f), new Vector3(0.02f, 0.01f, 2.15f), 0.002f);
            k.Box((int)MS.Main, new Vector3(0f, 0.55f, -0.05f), new Vector3(4.0f, 0.95f, 1.0f), 0.06f);
            for (int i = -7; i <= 7; i++) k.Box((int)MS.MainDark, new Vector3(i * 0.26f, 0.55f, -0.565f), new Vector3(0.04f, 0.85f, 0.03f), 0.008f);
            k.Box((int)MS.Wood, new Vector3(0f, 1.06f, -0.05f), new Vector3(4.2f, 0.1f, 1.15f), 0.03f);
            k.Box((int)MS.White, new Vector3(0f, 0.72f, -0.58f), new Vector3(1.5f, 0.32f, 0.03f), 0.01f);
            k.Box((int)MS.Main, new Vector3(-0.2f, 0.74f, -0.6f), new Vector3(0.18f, 0.2f, 0.01f), 0.003f);
            k.Box((int)MS.Main, new Vector3(0.2f, 0.74f, -0.6f), new Vector3(0.18f, 0.2f, 0.01f), 0.003f);
            // No roof (owner, 2026-10-06): the canopy hid the counter's stack and the stock pad behind it from the game
            // camera. The stall is an open counter now: striped front skirt and two short corner posts with a pennant.
            for (int i = 0; i < 8; i++)
                k.Box(i % 2 == 0 ? (int)MS.Main : (int)MS.White, new Vector3(-1.885f + i * 0.539f, 1.0f, -0.66f), new Vector3(0.54f, 0.26f, 0.05f), 0.015f, new Vector3(-12f, 0f, 0f));
            foreach (var x in new[] { -2.02f, 2.02f })
            {
                k.Box((int)MS.White, new Vector3(x, 0.85f, -0.6f), new Vector3(0.16f, 1.7f, 0.16f), 0.04f);
                k.Sphere((int)MS.Accent, new Vector3(x, 1.76f, -0.6f), Vector3.one * 0.13f, 10, 6);
                k.Prism((int)MS.Main, new Vector3(x + Mathf.Sign(x) * 0.02f, 1.36f, -0.6f), new[] { new Vector2(0f, 0f), new Vector2(0.5f, 0.14f), new Vector2(0f, 0.28f) }, 0.03f, 0.005f,
                    new Vector3(0f, x < 0f ? 180f : 0f, 0f));
            }

            // Goods on display and crates beside the stall.
            k.Box((int)MS.Wood, new Vector3(-2.65f, 0.35f, -0.2f), new Vector3(0.7f, 0.6f, 0.7f), 0.04f, new Vector3(0f, 12f, 0f));
            k.Box((int)MS.Wood, new Vector3(-2.6f, 0.95f, -0.25f), new Vector3(0.55f, 0.5f, 0.55f), 0.04f, new Vector3(0f, -10f, 0f));
            k.Box((int)MS.Steel, new Vector3(-2.65f, 0.67f, -0.2f), new Vector3(0.72f, 0.05f, 0.72f), 0.01f, new Vector3(0f, 12f, 0f));
            Part(model, "Stall", k, table, art);

            var reg = new MeshKit();
            reg.Box((int)MS.Dark, new Vector3(0f, 0.15f, 0f), new Vector3(0.6f, 0.3f, 0.45f), 0.05f);
            reg.Box((int)MS.Accent, new Vector3(0f, 0.32f, 0.08f), new Vector3(0.5f, 0.06f, 0.25f), 0.02f);
            reg.Box((int)MS.Dark, new Vector3(0f, 0.48f, -0.1f), new Vector3(0.4f, 0.3f, 0.06f), 0.02f, new Vector3(-15f, 0f, 0f));
            reg.Box((int)MS.Light, new Vector3(0f, 0.49f, -0.135f), new Vector3(0.32f, 0.2f, 0.02f), 0.01f, new Vector3(-15f, 0f, 0f));
            if (register != null) Part(model, "Register", reg, table, register);

            var lv = Child(root.transform, "LevelVisuals");
            var flags = new MeshKit();
            for (int i = 0; i < 9; i++)
            {
                float x = -2f + i * 0.5f;
                var c = (i % 3) switch { 0 => MS.Accent, 1 => MS.Main, _ => MS.White };
                flags.Prism((int)c, new Vector3(x, 0.52f, -0.72f), new[] { new Vector2(0f, 0f), new Vector2(0.18f, 0.32f), new Vector2(-0.18f, 0.32f) }, 0.03f, 0.005f,
                    new Vector3(0f, 90f, 0f));
            }

            var sign = new MeshKit();
            // The sign stands beside the stall on two legs (it used to sit on the roof).
            sign.Push(new Vector3(-3.05f, 0f, 0.35f));
            sign.Box((int)MS.Accent, new Vector3(0f, 1.75f, 0f), new Vector3(1.5f, 0.8f, 0.15f), 0.06f);
            sign.Box((int)MS.Dark, new Vector3(0f, 1.75f, -0.09f), new Vector3(1.34f, 0.64f, 0.02f), 0.02f);
            sign.Box((int)MS.Light, new Vector3(0f, 1.75f, -0.11f), new Vector3(0.14f, 0.44f, 0.02f), 0.01f);
            sign.Torus((int)MS.Light, new Vector3(0f, 1.81f, -0.11f), 0.13f, 0.035f, 12, 5, new Vector3(90f, 0f, 0f), null, 200f, 90f);
            sign.Torus((int)MS.Light, new Vector3(0f, 1.67f, -0.11f), 0.13f, 0.035f, 12, 5, new Vector3(90f, 0f, 0f), null, 200f, 270f);
            for (int side = -1; side <= 1; side += 2) sign.Box((int)MS.Dark, new Vector3(side * 0.55f, 0.68f, 0f), new Vector3(0.09f, 1.36f, 0.09f), 0.02f);
            sign.Pop();
            var trim = new MeshKit();
            trim.Box((int)MS.Copper, new Vector3(0f, 0.86f, -0.69f), new Vector3(4.34f, 0.05f, 0.05f), 0.01f);
            trim.Box((int)MS.Copper, new Vector3(0f, 1.12f, -0.05f), new Vector3(4.24f, 0.03f, 1.18f), 0.01f);
            var gold = (Material[])table.Clone();
            gold[(int)MS.Copper] = Mat("MachGold", new Color(1f, 0.8f, 0.25f), 0.75f, 0.45f, Detail.Metal, 1f);
            Tiers(root, lv, (2, Tier(lv, "Lv2_Flags", model, flags, table)), (3, Tier(lv, "Lv3_Sign", model, sign, table)), (5, Tier(lv, "Lv5_GoldTrim", model, trim, gold)));
            ArtAssets.Set(root.GetComponent<ScrapYardKing.Factory.SellDesk>(), ("levelVisuals", root.GetComponent<LevelVisuals>()));
        });
    }

    // =====================================================================================
    // Scene: conveyors and per-instance colours
    // =====================================================================================

    /// <summary>Custom belt between a conveyor's first and last point: frame, legs, end rollers, scrolling cleated belt.</summary>
    static void BuildConveyorVisual(Conveyor conveyor)
    {
        var so = new SerializedObject(conveyor);
        var pts = so.FindProperty("points");
        if (pts.arraySize < 2) return;
        var a = (Transform)pts.GetArrayElementAtIndex(0).objectReferenceValue;
        var b = (Transform)pts.GetArrayElementAtIndex(pts.arraySize - 1).objectReferenceValue;
        var keep = new HashSet<Transform>();
        for (int i = 0; i < pts.arraySize; i++) keep.Add((Transform)pts.GetArrayElementAtIndex(i).objectReferenceValue);
        foreach (Transform c in conveyor.transform.Cast<Transform>().ToArray())
            if (!keep.Contains(c)) Object.DestroyImmediate(c.gameObject);

        Vector3 la = conveyor.transform.InverseTransformPoint(a.position), lb = conveyor.transform.InverseTransformPoint(b.position);
        la.y = lb.y = 0f;
        float len = Vector3.Distance(la, lb) + 0.6f;
        Vector3 mid = (la + lb) * 0.5f;
        float yaw = Mathf.Atan2(lb.x - la.x, lb.z - la.z) * Mathf.Rad2Deg;
        var table = Table(Mat("ConveyorFrame", new Color(0.2f, 0.21f, 0.24f), 0.4f, 0.2f, Detail.Metal), Mat("ConveyorFrameDark", new Color(0.13f, 0.13f, 0.15f), 0.4f, 0.2f,
            Detail.Metal));
        const float top = 0.4f, width = 1.0f;
        var k = new MeshKit();
        k.Push(mid, new Vector3(0f, yaw, 0f));
        for (int side = -1; side <= 1; side += 2)
        {
            k.Box((int)MS.Accent, new Vector3(side * (width * 0.5f + 0.06f), top + 0.04f, 0f), new Vector3(0.1f, 0.22f, len), 0.03f);
            k.Box((int)MS.Hazard, new Vector3(side * (width * 0.5f + 0.115f), top + 0.04f, 0f), new Vector3(0.012f, 0.1f, len - 0.2f), 0.004f);
        }

        k.Box((int)MS.Main, new Vector3(0f, top - 0.12f, 0f), new Vector3(width, 0.2f, len), 0.04f);
        for (float z = -len * 0.5f + 0.4f; z < len * 0.5f - 0.2f; z += 1.2f)
            for (int side = -1; side <= 1; side += 2)
                k.Box((int)MS.MainDark, new Vector3(side * width * 0.42f, (top - 0.2f) * 0.5f, z), new Vector3(0.1f, top - 0.2f, 0.1f), 0.02f);
        foreach (float z in new[] { -len * 0.5f + 0.1f, len * 0.5f - 0.1f })
            k.Cylinder((int)MS.Steel, new Vector3(0f, top - 0.06f, z), 0.12f, width + 0.1f, 12, new Vector3(0f, 0f, 90f), 0.02f);
        k.Pop();
        var frame = ArtAssets.Part("Conveyors", conveyor.name + "_Frame", k, table, conveyor.transform);
        frame.name = "Frame";
        var belt = new MeshKit { UvScale = 1f };
        belt.Push(mid + Vector3.up * (top + 0.002f), new Vector3(0f, yaw, 0f));
        belt.Plane(0, Vector3.zero, new Vector2(width - 0.04f, len - 0.2f), default, false, new Rect(0f, 0f, 1f, len - 0.2f));
        belt.Pop();
        var beltGo = ArtAssets.Part("Conveyors", conveyor.name + "_Belt", belt, new[] { BeltMat() }, conveyor.transform, default, default, false);
        beltGo.name = "Belt";
        beltGo.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        so.FindProperty("rideHeight").floatValue = top + 0.17f;
        var belts = so.FindProperty("belts");
        belts.arraySize = 1;
        belts.GetArrayElementAtIndex(0).objectReferenceValue = beltGo.GetComponent<Renderer>();
        so.FindProperty("beltScroll").vector2Value = new Vector2(0f, -1f);
        so.ApplyModifiedPropertiesWithoutUndo();
        var box = conveyor.GetComponent<BoxCollider>();
        if (box != null)
        {
            box.center = conveyor.transform.InverseTransformPoint(conveyor.transform.TransformPoint(mid)) + Vector3.up * 0.25f;
            box.size = Quaternion.Euler(0f, yaw, 0f) * new Vector3(width + 0.3f, 0.5f, len);
            box.size = new Vector3(Mathf.Abs(box.size.x), 0.5f, Mathf.Abs(box.size.z));
        }
    }

    /// <summary>Recolours a station instance: every renderer using <paramref name="from"/> materials gets the matching replacement.</summary>
    static void Recolor(GameObject instance, params (Material from, Material to)[] swaps)
    {
        foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
                foreach (var (from, to) in swaps)
                    if (mats[i] == from)
                    {
                        mats[i] = to;
                        changed = true;
                    }

            if (changed) r.sharedMaterials = mats;
        }
    }

    public static string Scene()
    {
        Log.Clear();
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var conveyorBaker = new PaletteBaker("ConveyorPalette", KeepTextured);
        foreach (var c in Object.FindObjectsByType<Conveyor>(FindObjectsInactive.Include))
        {
            BuildConveyorVisual(c);
            conveyorBaker.Convert(c.gameObject, "Conveyors");
            Log.AppendLine("conveyor " + c.name);
        }

        conveyorBaker.Save();

        foreach (var s in Object.FindObjectsByType<ScrapYardKing.Factory.Storage>(FindObjectsInactive.Include))
        {
            (Material main, Material dark) colors = s.StationId switch
            {
                "bin_iron" => (Mat("BinIron", new Color(0.5f, 0.55f, 0.62f), 0.5f, 0.3f, Detail.Metal), Mat("BinIronDeep", new Color(0.33f, 0.36f, 0.42f), 0.5f, 0.3f, Detail.Metal)),
                "bin_copper" => (Mat("BinCopper", new Color(0.95f, 0.5f, 0.2f), 0.5f, 0.15f, Detail.Paint), Mat("BinCopperDeep", new Color(0.7f, 0.33f, 0.12f), 0.5f, 0.15f, Detail.Paint)),
                "bin_aluminum" => (Mat("BinAluminum", new Color(0.72f, 0.84f, 0.95f), 0.55f, 0.25f, Detail.Metal), Mat("BinAluminumDeep", new Color(0.48f, 0.62f, 0.78f), 0.55f, 0.25f,
                    Detail.Metal)),
                "bin_steel" => (Mat("BinSteel", new Color(0.3f, 0.36f, 0.5f), 0.5f, 0.3f, Detail.Metal), Mat("BinSteelDeep", new Color(0.19f, 0.23f, 0.34f), 0.5f, 0.3f, Detail.Metal)),
                "bar_storage" => (Mat("BinBars", new Color(0.2f, 0.22f, 0.28f), 0.45f, 0.3f, Detail.Metal), Mat("BinBarsTrim", new Color(1f, 0.76f, 0.1f), 0.5f, 0.05f, Detail.Paint, 0.7f)),
                "ingot_rack" => (Mat("BinRack", new Color(0.3f, 0.32f, 0.38f), 0.45f, 0.3f, Detail.Metal), Mat("BinRackOrange", new Color(0.95f, 0.5f, 0.15f), 0.45f, 0.1f, Detail.Paint)),
                _ => (BinMain, BinDark),
            };
            Recolor(s.gameObject, (BinMain, colors.main), (BinDark, colors.dark));
        }

        foreach (var d in Object.FindObjectsByType<ScrapYardKing.Factory.SellDesk>(FindObjectsInactive.Include))
            if (d.StationId == "market")
                Recolor(d.gameObject, (StallMain, Mat("StallGreen", new Color(0.22f, 0.68f, 0.32f), 0.45f, 0.05f, Detail.Paint, 0.7f)),
                    (Mat("StallRedDeep", new Color(0.62f, 0.13f, 0.1f), 0.45f, 0.05f, Detail.Paint, 0.7f), Mat("StallTeal", new Color(0.12f, 0.5f, 0.55f), 0.45f, 0.05f,
                        Detail.Paint, 0.7f)));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return Log.ToString();
    }

    public static string Prefabs()
    {
        Log.Clear();
        baker = new PaletteBaker("MachinePalette", KeepTextured);
        Crusher();
        Sorter();
        Furnace();
        Press();
        Storage();
        SellDesk();
        baker.Save();
        baker = null;
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    public static string Icons()
    {
        static void Hide(GameObject go)
        {
            foreach (var n in new[] { "InputPad", "PadIcon", "WithdrawPad", "StockPad", "CashPad", "StationLabel", "CashPile" })
            {
                var t = ArtAssets.FindDeep(go.transform, n);
                if (t != null) t.gameObject.SetActive(false);
            }
        }

        void Icon<T>(string asset, string prefab, string icon, Vector3 view) where T : ScriptableObject
        {
            var def = AssetDatabase.LoadAssetAtPath<T>(asset);
            if (def == null) return;
            ArtAssets.Set(def, ("icon", ArtIcons.Render(icon, AssetDatabase.LoadAssetAtPath<GameObject>($"{StationDir}/{prefab}.prefab"), view, Hide, 0.72f)));
        }

        Icon<MachineDefinition>($"{DataDir}/Factory/Machine_Crusher.asset", "Crusher", "Icon_Crusher", new Vector3(26f, -35f, 0f));
        Icon<MachineDefinition>($"{DataDir}/Factory/Machine_Sorter.asset", "Sorter", "Icon_Sorter", new Vector3(28f, -35f, 0f));
        Icon<MachineDefinition>($"{DataDir}/Factory/Machine_Furnace.asset", "Furnace", "Icon_Furnace", new Vector3(24f, 205f, 0f));
        if (AssetDatabase.LoadAssetAtPath<GameObject>($"{StationDir}/Press.prefab") != null)
            Icon<MachineDefinition>($"{DataDir}/Factory/Machine_Press.asset", "Press", "Icon_Press", new Vector3(24f, 205f, 0f));
        foreach (string metal in new[] { "Copper", "Aluminum", "Steel" })
            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{StationDir}/Furnace_{metal}.prefab") != null)
                Icon<MachineDefinition>($"{DataDir}/Factory/Machine_Furnace{metal}.asset", "Furnace_" + metal, "Icon_Furnace" + metal, new Vector3(24f, 205f, 0f));
        Icon<StorageDefinition>($"{DataDir}/Factory/Storage_Yard.asset", "Storage", "Icon_Storage", new Vector3(40f, -35f, 0f));
        Icon<SellDeskDefinition>($"{DataDir}/Factory/SellDesk_Yard.asset", "SellDesk", "Icon_SellDesk", new Vector3(25f, 160f, 0f));
        // Recoloured copies for the plant's shared bins (copper look) and the Metal Market (green stall).
        Sprite Recoloured(string prefab, string icon, Vector3 view, params (Material from, Material to)[] swaps)
        {
            var copy = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{StationDir}/{prefab}.prefab"));
            PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Recolor(copy, swaps);
            var tmp = ArtAssets.SavePrefab(copy, $"{P}/Prefabs/_IconTmp.prefab");
            var sprite = ArtIcons.Render(icon, tmp, view, Hide, 0.72f);
            AssetDatabase.DeleteAsset($"{P}/Prefabs/_IconTmp.prefab");
            return sprite;
        }

        var bins = AssetDatabase.LoadAssetAtPath<StorageDefinition>($"{DataDir}/Factory/Storage_Bins.asset");
        if (bins != null)
            ArtAssets.Set(bins, ("icon", Recoloured("Storage", "Icon_Bins", new Vector3(40f, -35f, 0f),
                (BinMain, Mat("BinCopper", new Color(0.95f, 0.5f, 0.2f), 0.5f, 0.15f, Detail.Paint)),
                (BinDark, Mat("BinCopperDeep", new Color(0.7f, 0.33f, 0.12f), 0.5f, 0.15f, Detail.Paint)))));
        var market = AssetDatabase.LoadAssetAtPath<SellDeskDefinition>($"{DataDir}/Factory/SellDesk_Market.asset");
        if (market != null)
            ArtAssets.Set(market, ("icon", Recoloured("SellDesk", "Icon_Market", new Vector3(25f, 160f, 0f),
                (StallMain, Mat("StallGreen", new Color(0.22f, 0.68f, 0.32f), 0.45f, 0.05f, Detail.Paint, 0.7f)),
                (Mat("StallRedDeep", new Color(0.62f, 0.13f, 0.1f), 0.45f, 0.05f, Detail.Paint, 0.7f), Mat("StallTeal", new Color(0.12f, 0.5f, 0.55f), 0.45f, 0.05f,
                    Detail.Paint, 0.7f)))));
        var rack = AssetDatabase.LoadAssetAtPath<StorageDefinition>($"{DataDir}/Factory/Storage_IngotRack.asset");
        if (rack != null)
            ArtAssets.Set(rack, ("icon", Recoloured("Storage", "Icon_IngotRack", new Vector3(40f, -35f, 0f),
                (BinMain, Mat("BinRack", new Color(0.3f, 0.32f, 0.38f), 0.45f, 0.3f, Detail.Metal)),
                (BinDark, Mat("BinRackOrange", new Color(0.95f, 0.5f, 0.15f), 0.45f, 0.1f, Detail.Paint)))));

        var plant = AssetDatabase.LoadAssetAtPath<ExpansionDefinition>($"{DataDir}/World/Expansion_RecyclingPlant.asset");
        var sorter = AssetDatabase.LoadAssetAtPath<MachineDefinition>($"{DataDir}/Factory/Machine_Sorter.asset");
        if (plant != null && sorter != null) ArtAssets.Set(plant, ("icon", sorter.Icon));
        var hall = AssetDatabase.LoadAssetAtPath<ExpansionDefinition>($"{DataDir}/World/Expansion_FurnaceHall.asset");
        var furnace = AssetDatabase.LoadAssetAtPath<MachineDefinition>($"{DataDir}/Factory/Machine_Furnace.asset");
        if (hall != null && furnace != null) ArtAssets.Set(hall, ("icon", furnace.Icon));
        AssetDatabase.SaveAssets();
        return "icons";
    }

    public static string All()
    {
        var sb = new StringBuilder();
        sb.Append(Prefabs());
        sb.Append(Scene());
        sb.Append(Icons());
        return sb.ToString();
    }

    /// <summary>Renders the station prefabs side by side for review.</summary>
    public static string Preview(string outDir)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        string[] names = { "Crusher", "Sorter", "Furnace", "Storage", "SellDesk" };
        float x = 0f;
        foreach (var n in names)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{StationDir}/{n}.prefab"));
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = new Vector3(x, 0f, 0f);
            foreach (var c in go.GetComponentsInChildren<Canvas>(true)) c.gameObject.SetActive(false);
            x += 6.5f;
        }

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        SceneManager.MoveGameObjectToScene(ground, scene);
        ground.transform.position = new Vector3(13f, 0f, 0f);
        ground.transform.localScale = new Vector3(5f, 1f, 2f);
        ground.GetComponent<Renderer>().sharedMaterial = Mat("PreviewGround", new Color(0.8f, 0.74f, 0.62f), 0.1f, 0f, Detail.Concrete);
        Directory.CreateDirectory(outDir);
        Render(scene, Path.Combine(outDir, "mach_game.png"), new Vector3(50f, 0f, 0f), new Vector3(13f, 1f, 0f), 34f, 1800, 700);
        Render(scene, Path.Combine(outDir, "mach_close.png"), new Vector3(22f, 205f, 0f), new Vector3(3.5f, 2f, 0f), 14f, 1600, 900);
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScrapYardKing.Customers;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Harvest;
using ScrapYardKing.Items;
using ScrapYardKing.Tiles;
using ScrapYardKing.Workers;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using static ScrapYardKing.EditorTools.Art.ArtMaterials;

// Environment and layout pass (after Revamp 1). Everything it builds is ProBuilder geometry (editable in the scene with
// the ProBuilder tools) in the project's toy materials.
//  - Layout: hire tiles, consoles and posts moved off belts, props and each other; customers enter from just off screen
//    instead of 40 m away.
//  - Density: more scrap spawn points in the Old Yard field, the Back Lot and above all the Heavy Scrap Yard, placed on a
//    grid that keeps lanes, the giant's landing zone, gates and existing objects clear.
//  - Old Yard look: paved ground and gravel pit (generated textures), hazard kerb with bollards around the pit, painted
//    floor words, the billboard on the north wall. This is the reference look; other areas follow once it is approved.
//  - Dress: service lanes with painted lines, work pads under heavy scrap, and clutter along the walls (scrap bales, pipe
//    stacks, barriers, drums, crates, tanks, floodlights). Clusters are placed only where the footprint is free, carry a
//    collider and a DropBlocker, and stay low along south walls (the camera looks north).
// Entry points: All (Layout + Density + Dress, saves the scene), then R1_Build.Bake for the NavMesh. Check reports tiles
// and pads that overlap each other or anything standing on them. Deterministic (fixed seed) and idempotent: everything it
// creates is named EnvPB_* or SP_Env_* and rebuilt on each run. Run it after R1_Build and the Art builders.
public static class Env_Build
{
    const string P = "Assets/_Project";
    const string DataDir = P + "/Data";
    const string ScenePath = P + "/Scenes/Area1_OldScrapYard.unity";

    static readonly StringBuilder Log = new();
    static System.Random rng;
    static readonly List<(Rect rect, string name, Transform owner)> Obstacles = new();

    // ---------- keep-out areas (blueprint frame) ----------
    static readonly Rect GiantZone = Rect.MinMaxRect(8f, 60f, 27f, 69.5f);
    static readonly Rect Gate3Lane = Rect.MinMaxRect(8.5f, 36f, 15.5f, 41.4f);
    static readonly Rect Gate2Lane = Rect.MinMaxRect(37.5f, 22f, 43f, 27f);

    // Service lanes: (rect, runs north–south). Only on bare ground, never across a concrete floor.
    static readonly (Rect rect, bool alongZ, string area)[] Lanes =
    {
        (Rect.MinMaxRect(10.5f, 10.7f, 13.5f, 23.4f), true, "yard"),
        (Rect.MinMaxRect(13.5f, 12.7f, 25.8f, 15.5f), false, "yard"),
        (Rect.MinMaxRect(10.5f, 38.6f, 13.5f, 49f), true, "heavy"),
        (Rect.MinMaxRect(3f, 49f, 37f, 52f), false, "heavy"),
        (Rect.MinMaxRect(16f, 52f, 19f, 60f), true, "heavy"),
    };

    public static string All()
    {
        Log.Clear();
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        rng = new System.Random(20261006);
        var env = Root(scene, "_Environment");
        var gameplay = Root(scene, "_Gameplay");

        // Remove what an earlier run built, so obstacles are measured without it.
        foreach (var t in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Where(t => t != null &&
                     (t.name.StartsWith("EnvPB_") || t.name.StartsWith("SP_Env_"))).ToArray())
            if (t != null) Object.DestroyImmediate(t.gameObject);

        Layout(gameplay);
        CollectObstacles(scene, false);
        foreach (var (rect, _, _) in Lanes) Obstacles.Add((rect, "lane", null));
        Obstacles.Add((GiantZone, "giant zone", null));
        Obstacles.Add((Gate3Lane, "gate 3", null));
        Obstacles.Add((Gate2Lane, "gate 2", null));
        HeavyRows(gameplay);
        foreach (var sp in All<ScrapSpawnPoint>(scene)) Obstacles.Add((SpawnRect(sp), sp.name, sp.transform));
        Density(gameplay);
        Dress(env, gameplay);
        OldYard(env);
        WorldGround(env);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("saved. Next: R1_Build.Bake (NavMesh).");
        return Log.ToString();
    }

    // =====================================================================================
    // LAYOUT
    // =====================================================================================

    // Tiles that sat on a belt, under a prop or against another tile. Positions are world x, z.
    static readonly (string path, float x, float z)[] TileMoves =
    {
        ("Tiles/Tile_Helper", 28f, 18.6f),
        ("Tiles/Tile_Porter", 15f, 20.8f),                   // makes room for the Claw Crane tile beside it
        ("Tiles/Tile_SellerYard", 27.4f, 14.7f),             // was under the crates at the stall's corner
        ("Tiles/Tile_BackLot", 30.8f, 28.4f),                // was touching the crusher's boost pad
        ("RecyclingPlant/Content/Tile_BoostSorter", 55.2f, 29.4f),
        ("RecyclingPlant/Content/Tile_SellerMarket", 54.7f, 13.3f),
        ("Tiles/Tile_RecyclingPlant", 36.4f, 25f),           // was touching the barrels by the gate
        ("RecyclingPlant/Content/Tile_TruckDock", 66.6f, 14.1f),   // was under the "locked" sign
    };

    // Operator posts: (tile, console, post, tile position, direction the operator faces).
    static readonly (string tile, string console, string post, Vector3 pos, Vector3 facing)[] Posts =
    {
        ("Tiles/Tile_Operator_crusher", "Stations/Console_Crusher", "Post_Crusher", new Vector3(21.6f, 0f, 23f), Vector3.forward),   // was on the belt
        ("FurnaceHall/Content/Tile_Operator_furnace", "FurnaceHall/Content/Console_Furnace", "Post_Furnace", new Vector3(68.7f, 0f, 30.9f), Vector3.forward),   // in front of the battery (R2)
    };

    static void Layout(Transform gameplay)
    {
        foreach (var (path, x, z) in TileMoves)
        {
            var t = gameplay.Find(path);
            if (t == null) Log.AppendLine("!! no " + path);
            else t.position = new Vector3(x, t.position.y, z);
        }

        foreach (var (tile, console, post, pos, facing) in Posts)
        {
            var t = gameplay.Find(tile);
            var c = gameplay.Find(console);
            var p = gameplay.Find("Workers/Sites/" + post);
            if (t == null || c == null || p == null)
            {
                Log.AppendLine($"!! post parts missing: {tile} {t != null} / {console} {c != null} / {post} {p != null}");
                continue;
            }

            t.position = pos;
            c.SetPositionAndRotation(pos + facing * 0.75f, Quaternion.LookRotation(facing));
            p.SetPositionAndRotation(pos, Quaternion.LookRotation(facing));
        }

        // Customers walked 40 m (yard) and 28 m (market) from their spawn to the line: with a faster line the street was
        // empty for seconds. They now enter and leave 17 m from the stall, just outside the camera.
        Path(gameplay.Find("Customers"), 15f);
        Path(gameplay.Find("RecyclingPlant/Content/Customers_Market"), 77f);
        Log.AppendLine("layout: tiles, posts and customer paths moved");
    }

    static void Path(Transform queue, float x)
    {
        if (queue == null)
        {
            Log.AppendLine("!! customer queue not found");
            return;
        }

        foreach (string n in new[] { "Entry/Spawn", "Exit/Away" })
        {
            var t = queue.Find(n);
            if (t != null) t.position = new Vector3(x, t.position.y, t.position.z);
        }
    }

    // =====================================================================================
    // OBSTACLES
    // =====================================================================================

    static void CollectObstacles(Scene scene, bool includeSpawns = true)
    {
        Obstacles.Clear();
        int ground = LayerMask.NameToLayer("Ground");
        string[] skipRoots = { "Fences", "Backdrop", "ArtBackdrop", "DockyardBackdrop", "ArtDustMotes", "ArtAmbient", "WorldGround", "NavMesh", "_UI", "_Camera", "ItemPool" };
        foreach (var root in scene.GetRootGameObjects())
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.gameObject.layer == ground) continue;
                if (Under(mf.transform, skipRoots)) continue;
                var b = WorldBounds(mf.sharedMesh.bounds, mf.transform.localToWorldMatrix);
                if (b.max.y < 0.3f || b.size.y < 0.2f || b.min.y > 2f || b.size.x > 30f || b.size.z > 30f) continue;   // floors, decals, overhead jibs
                Obstacles.Add((Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z), mf.name, mf.transform));
            }

        foreach (var (rect, name, owner) in PadRects(scene)) Obstacles.Add((Grow(rect, 0.6f), name, owner));
        if (includeSpawns)
            foreach (var sp in All<ScrapSpawnPoint>(scene)) Obstacles.Add((SpawnRect(sp), sp.name, sp.transform));
        foreach (var q in All<CustomerQueue>(scene))
            foreach (var t in q.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Slot_")) Obstacles.Add((Around(t.position, 0.9f), "customer line", t));
        foreach (var w in All<WorkerSite>(scene)) Obstacles.Add((Around(w.IdlePosition, 1f), w.name, w.transform));
        Log.AppendLine($"obstacles: {Obstacles.Count}");
    }

    /// <summary>Half-size of the square a spawn point's scrap needs: heavy vehicles 3 m, cars and appliances 2.3 m, barrels 1.3 m.</summary>
    static float Footprint(ScrapSpawnPoint sp)
    {
        var c = new SerializedObject(sp).FindProperty("candidates");
        float r = 1.3f;
        for (int i = 0; i < c.arraySize; i++)
            if (c.GetArrayElementAtIndex(i).objectReferenceValue is ScrapDefinition d)
                r = Mathf.Max(r, d.Tier >= 3 ? 3f : d.Id == "car_wreck" ? 2f : (int)d.SizeClass >= 1 ? 1.4f : 1f);
        return r;
    }

    /// <summary>Ground a spawn point's scrap covers: a parked bay for fixed-yaw heavy vehicles, a square for everything that spawns at a random yaw.</summary>
    static Rect SpawnRect(ScrapSpawnPoint sp)
    {
        var p = sp.transform.position;
        float r = Footprint(sp);
        return r < 3f || new SerializedObject(sp).FindProperty("randomYaw").boolValue ? Around(p, r) : Rect.MinMaxRect(p.x - BayW * 0.5f, p.z - BayD * 0.5f, p.x + BayW * 0.5f, p.z + BayD * 0.5f);
    }

    static IEnumerable<(Rect rect, string name, Transform owner)> PadRects(Scene scene)
    {
        foreach (var c in All<Tile>(scene).Cast<Component>().Concat(All<TransferPad>(scene)))
        {
            var p = new SerializedObject(c).FindProperty("size");
            Vector2 size = p != null ? p.vector2Value : new Vector2(2f, 2f);
            if (Mathf.RoundToInt(c.transform.eulerAngles.y / 90f) % 2 != 0) size = new Vector2(size.y, size.x);
            var pos = c.transform.position;
            yield return (new Rect(pos.x - size.x / 2f, pos.z - size.y / 2f, size.x, size.y), c.transform.parent.name + "/" + c.name, c.transform);
        }
    }

    static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));

    static bool Under(Transform t, string[] names)
    {
        for (; t != null; t = t.parent)
            if (names.Contains(t.name) || t.name.StartsWith("EnvPB_")) return true;
        return false;
    }

    static Bounds WorldBounds(Bounds local, Matrix4x4 m)
    {
        var b = new Bounds(m.MultiplyPoint3x4(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            b.Encapsulate(m.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1))));
        return b;
    }

    /// <summary>Debug: what blocks a point. Args: "x z".</summary>
    public static string Why(string args)
    {
        var a = args.Split(' ').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        CollectObstacles(SceneManager.GetActiveScene());
        var sb = new StringBuilder();
        var probe = Around(new Vector3(a[0], 0f, a[1]), a.Length > 2 ? a[2] : 0.05f);
        foreach (var o in Obstacles.Where(o => o.rect.Overlaps(probe)))
            sb.AppendLine($"{(o.owner != null ? PathOf(o.owner) : o.name)} {o.rect.width:0.0}x{o.rect.height:0.0} @ {o.rect.center}");
        return sb.Length == 0 ? "free" : sb.ToString();
    }

    /// <summary>Debug: occupancy map of a region ("x0 z0 x1 z1"), north at the top, one character per 2 m. '#' = blocked.</summary>
    public static string Map(string args)
    {
        var a = args.Split(' ').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        CollectObstacles(SceneManager.GetActiveScene());
        foreach (var (rect, _, _) in Lanes) Obstacles.Add((rect, "lane", null));
        Obstacles.Add((GiantZone, "giant zone", null));
        var sb = new StringBuilder();
        for (float z = a[3]; z >= a[1]; z -= 2f)
        {
            sb.Append($"{z,3:0} ");
            for (float x = a[0]; x <= a[2]; x += 2f)
            {
                var hit = Obstacles.FirstOrDefault(o => o.rect.Overlaps(Around(new Vector3(x, 0f, z), 0.9f)));
                sb.Append(hit.name == null ? '.' : hit.name == "lane" ? '=' : hit.name == "giant zone" ? 'G' : hit.name.StartsWith("SP_") ? 'S' : hit.name.StartsWith("EnvPB") || hit.name == "Mesh" ? 'm' : '#');
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    static Rect Around(Vector3 p, float r) => new(p.x - r, p.z - r, r * 2f, r * 2f);
    static Rect Grow(Rect r, float m) => new(r.x - m, r.y - m, r.width + m * 2f, r.height + m * 2f);
    static bool Free(Rect r) => !Obstacles.Any(o => o.rect.Overlaps(r));

    /// <summary>Tiles and pads against each other and against anything tall standing on them. Empty report = clean.</summary>
    public static string Check()
    {
        Log.Clear();
        var scene = SceneManager.GetActiveScene();
        CollectObstacles(scene);
        var pads = PadRects(scene).ToList();
        for (int i = 0; i < pads.Count; i++)
        {
            for (int j = i + 1; j < pads.Count; j++)
                if (Grow(pads[i].rect, 0.15f).Overlaps(pads[j].rect)) Log.AppendLine($"PAD/PAD {pads[i].name} <> {pads[j].name}");
            foreach (var o in Obstacles)
            {
                if (o.owner == null || o.owner == pads[i].owner || o.owner.IsChildOf(pads[i].owner)) continue;
                if (o.owner.GetComponent<MeshFilter>() == null) continue;                       // other pads were handled above
                if (o.owner.parent != null && o.owner.parent.name.StartsWith("Console_")) continue;   // an operator's console stands on the tile's edge by design
                if (pads[i].owner.parent != null && pads[i].owner.GetComponent<TransferPad>() != null && o.owner.IsChildOf(pads[i].owner.parent)) continue;
                if (Grow(pads[i].rect, -0.1f).Overlaps(o.rect)) Log.AppendLine($"ON PAD  {pads[i].name} <> {PathOf(o.owner)}");
            }
        }

        Log.AppendLine("(bounds are axis-aligned: a diagonal belt reports more ground than it covers; locked-only signs and teasers are never shown with the content behind them)");
        return Log.ToString();
    }

    static string PathOf(Transform t) => t.parent != null && t.parent.parent != null ? t.parent.parent.name + "/" + t.parent.name + "/" + t.name : t.name;

    // =====================================================================================
    // DENSITY
    // =====================================================================================

    // Heavy Scrap Yard: wrecks parked in rows between the lanes, like a breaker's yard, instead of eight vehicles
    // scattered at random angles. A bay is one vehicle wide; vehicles are long along z.
    const float BayW = 3.4f, BayD = 7f;
    static readonly (float z, float[] xs)[] HeavyBays =
    {
        (45.3f, new[] { 4.7f, 8.3f, 20.1f, 23.7f, 27.3f }),   // the slot at x 16.5 is the Heavy Yard crane's corner (tile, belt)
        (56f, new[] { 4.7f, 8.3f, 11.9f, 22.1f, 25.7f, 29.3f, 32.9f }),
        (64.8f, new[] { 4.9f, 30.3f, 33.9f }),
    };

    static void HeavyRows(Transform gameplay)
    {
        var content = gameplay.Find("HeavyYard/Content");
        if (content == null) return;
        var existing = content.GetComponentsInChildren<ScrapSpawnPoint>(true).Where(s => !s.name.StartsWith("SP_Env_")).OrderBy(s => s.name).ToList();
        if (existing.Count == 0) return;
        string[] cycle = { "Excavator", "Truck", "GarbageTruck", "Tractor", "Excavator", "Truck", "Tractor", "GarbageTruck" };
        int used = 0, made = 0, bay = 0;
        foreach (var (z, xs) in HeavyBays)
            foreach (float x in xs)
            {
                var pos = new Vector3(x, 0f, z);
                var rect = Rect.MinMaxRect(x - BayW * 0.5f, z - BayD * 0.5f, x + BayW * 0.5f, z + BayD * 0.5f);
                if (!Free(Grow(rect, -0.15f)))
                {
                    Log.AppendLine($"  bay {x},{z} blocked by {Obstacles.First(o => o.rect.Overlaps(Grow(rect, -0.15f))).name}");
                    continue;
                }

                ScrapSpawnPoint sp;
                if (used < existing.Count) sp = existing[used++];
                else
                {
                    sp = Object.Instantiate(existing[0].gameObject, content).GetComponent<ScrapSpawnPoint>();
                    string kind = cycle[made % cycle.Length];
                    sp.name = $"SP_Env_Bay{kind}_{made++:00}";
                    sp.gameObject.SetActive(existing[0].gameObject.activeSelf);
                    var so = new SerializedObject(sp);
                    var c = so.FindProperty("candidates");
                    c.arraySize = 1;
                    c.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<ScrapDefinition>($"{DataDir}/Scrap/Scrap_{kind}.asset");
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                // Parked nose in or nose out, a few degrees off: a row, not a parade.
                sp.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, (bay++ % 2) * 180f + Jitter(7f), 0f));
                var yaw = new SerializedObject(sp);
                yaw.FindProperty("randomYaw").boolValue = false;
                yaw.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(sp);
            }

        Log.AppendLine($"heavy rows: {used} existing points parked, +{made} new bays");
    }

    static void Density(Transform gameplay)
    {
        var car = gameplay.GetComponentsInChildren<ScrapSpawnPoint>(true).FirstOrDefault(s => s.name == "SP_CarWreck_01");
        var fridge = gameplay.Find("BackLot/Content")?.GetComponentsInChildren<ScrapSpawnPoint>(true).FirstOrDefault(s => !s.name.StartsWith("SP_Env_"));
        var heavyContent = gameplay.Find("HeavyYard/Content");
        if (car == null || fridge == null || heavyContent == null)
        {
            Log.AppendLine("!! spawn point templates missing");
            return;
        }

        // Old Yard field: always something to cut within a few steps.
        int a = SpawnGrid(car, car.transform.parent, Rect.MinMaxRect(4.2f, 24.8f, 17.6f, 35.4f), 2.1f, 8,
            new[] { "CarWreck" }, new[] { "Barrel", "TireStack" }, new[] { "TireStack", "Barrel" });
        int b = SpawnGrid(fridge, fridge.transform.parent, Rect.MinMaxRect(20.6f, 31.8f, 38.6f, 35.4f), 1.8f, 6,
            new[] { "Fridge", "Stove" }, new[] { "Kart" }, new[] { "Washer", "Stove" });
        // Heavy Scrap Yard: quick targets in the gaps between the rows (random yaw, like the Old Yard).
        int c = SpawnGrid(car, heavyContent, Rect.MinMaxRect(3.6f, 42.4f, 36.6f, 71f), 2.2f, 14,
            new[] { "CarWreck" }, new[] { "Fridge", "Stove", "Washer" }, new[] { "Barrel", "TireStack" }, new[] { "CarWreck" }, new[] { "Kart" });
        foreach (var sp in heavyContent.GetComponentsInChildren<ScrapSpawnPoint>(true).Where(s => s.name.StartsWith("SP_Env_") && !s.name.Contains("Bay")))
            sp.gameObject.SetActive(heavyContent.Find("Decor").gameObject.activeSelf);
        Log.AppendLine($"density: +{a} yard, +{b} back lot, +{c} heavy yard quick targets");
    }

    static int SpawnGrid(ScrapSpawnPoint template, Transform parent, Rect area, float spacing, int max, params string[][] cycle)
    {
        int made = 0, spot = 0;
        string[] small = { "Barrel", "TireStack" };
        for (float z = area.yMin; z <= area.yMax && made < max; z += spacing)
            for (float x = area.xMin + ((int)((z - area.yMin) / spacing) % 2) * spacing * 0.5f; x <= area.xMax && made < max; x += spacing)
            {
                var pos = new Vector3(x + Jitter(0.5f), 0f, z + Jitter(0.5f));
                if (!area.Contains(new Vector2(pos.x, pos.z))) continue;
                // The spot's turn in the cycle decides what stands here; if that does not fit, something smaller does.
                foreach (var kinds in new[] { cycle[spot++ % cycle.Length], small })
                {
                    var defs = kinds.Select(k => AssetDatabase.LoadAssetAtPath<ScrapDefinition>($"{DataDir}/Scrap/Scrap_{k}.asset")).Where(d => d != null).ToArray();
                    if (defs.Length == 0) continue;
                    float r = defs.Any(d => d.Tier >= 3) ? 2.9f : defs.Any(d => (int)d.SizeClass >= 1) ? (kinds.Contains("CarWreck") ? 2.1f : 1.4f) : 1f;
                    if (!Free(Around(pos, r))) continue;
                    var go = Object.Instantiate(template.gameObject, parent);
                    go.name = $"SP_Env_{kinds[0]}_{made:00}";
                    go.transform.SetPositionAndRotation(pos, Quaternion.identity);
                    go.SetActive(template.gameObject.activeSelf);
                    var so = new SerializedObject(go.GetComponent<ScrapSpawnPoint>());
                    var p = so.FindProperty("candidates");
                    p.arraySize = defs.Length;
                    for (int i = 0; i < defs.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = defs[i];
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Obstacles.Add((Around(pos, r + 0.3f), go.name, go.transform));
                    made++;
                    break;
                }
            }

        return made;
    }

    static float Jitter(float amount) => ((float)rng.NextDouble() * 2f - 1f) * amount;
    static float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    // =====================================================================================
    // DRESS (ProBuilder)
    // =====================================================================================

    static Material mAsphalt, mLineYellow, mLineWhite, mOilPad, mConcrete, mWood, mSteel, mDark, mLamp, mRed, mWhite;
    static Material[] scrapPaints, drumPaints, pipePaints;

    static void Materials()
    {
        mAsphalt = Mat("EnvAsphalt", new Color(0.23f, 0.24f, 0.27f), 0.15f, 0f, Detail.Concrete, 0.35f);
        mLineYellow = Mat("EnvLineYellow", new Color(1f, 0.8f, 0.12f), 0.3f, 0f, Detail.Paint, 0.7f);
        mLineWhite = Mat("EnvLineWhite", new Color(0.93f, 0.93f, 0.9f), 0.3f, 0f, Detail.Paint, 0.7f);
        mOilPad = Mat("EnvOilPad", new Color(0.36f, 0.31f, 0.27f), 0.25f, 0f, Detail.Concrete, 0.3f);
        mConcrete = Mat("EnvConcrete", new Color(0.72f, 0.7f, 0.67f), 0.15f, 0f, Detail.Concrete, 0.35f);
        mWood = Mat("EnvWood", new Color(0.8f, 0.58f, 0.34f), 0.22f, 0f, Detail.Wood, 0.6f);
        mSteel = Mat("EnvSteel", new Color(0.55f, 0.58f, 0.63f), 0.5f, 0.3f, Detail.Metal, 0.8f);
        mDark = Mat("EnvDark", new Color(0.17f, 0.18f, 0.2f), 0.35f, 0.1f, Detail.Metal, 0.8f);
        mLamp = Mat("EnvLamp", new Color(1f, 0.93f, 0.7f), 0.9f, 0f, Detail.Plain, 1f, new Color(1.6f, 1.35f, 0.8f));
        mRed = Mat("EnvRed", Red, 0.45f, 0.05f, Detail.Paint, 0.7f);
        mWhite = Mat("EnvWhite", new Color(0.95f, 0.94f, 0.9f), 0.4f, 0f, Detail.Paint, 0.7f);
        scrapPaints = new[]
        {
            Mat("EnvBaleRust", Rust, 0.3f, 0.2f, Detail.Metal, 0.9f), Mat("EnvBaleBlue", new Color(0.24f, 0.44f, 0.72f), 0.4f, 0.15f, Detail.Metal, 0.9f),
            Mat("EnvBaleGreen", new Color(0.3f, 0.6f, 0.36f), 0.4f, 0.15f, Detail.Metal, 0.9f), Mat("EnvBaleCream", Cream, 0.4f, 0.1f, Detail.Metal, 0.9f),
            Mat("EnvBaleGrey", Gray, 0.45f, 0.25f, Detail.Metal, 0.9f), Mat("EnvBaleOrange", Orange, 0.4f, 0.1f, Detail.Metal, 0.9f),
        };
        drumPaints = new[] { mRed, Mat("EnvDrumBlue", Blue, 0.45f, 0.1f, Detail.Paint, 0.7f), Mat("EnvDrumYellow", Yellow, 0.45f, 0.1f, Detail.Paint, 0.7f), scrapPaints[2] };
        pipePaints = new[] { mSteel, scrapPaints[0], mDark };
    }

    static void Dress(Transform env, Transform gameplay)
    {
        Materials();
        var yard = NewRoot(env, "EnvPB_Yard");
        var heavyDecor = gameplay.Find("HeavyYard/Content/Decor");
        var plantContent = gameplay.Find("RecyclingPlant/Content");
        var heavy = heavyDecor != null ? NewRoot(heavyDecor, "EnvPB_Heavy") : null;
        var plant = plantContent != null ? NewRoot(plantContent, "EnvPB_Plant") : null;
        if (plant != null) plant.gameObject.SetActive(plantContent.Find("Sorter").gameObject.activeSelf);

        // Lanes and work pads (flat, no colliders).
        // Only the Heavy Yard's lanes are painted. In the Old Yard the same strips are just kept clear of clutter: a road
        // painted across the yard ran under the light pole and the loose debris and dead-ended at the fence (owner:
        // "why is there a road?").
        foreach (var (rect, alongZ, area) in Lanes)
            if (area != "yard") Lane(heavy, rect, alongZ);
        if (heavy != null)
            foreach (var sp in gameplay.Find("HeavyYard/Content").GetComponentsInChildren<ScrapSpawnPoint>(true))
            {
                var so = new SerializedObject(sp);
                var first = so.FindProperty("candidates").arraySize > 0 ? so.FindProperty("candidates").GetArrayElementAtIndex(0).objectReferenceValue as ScrapDefinition : null;
                if (first != null && first.Tier >= 3) WorkPad(heavy, sp.transform.position, BayW - 0.2f, BayD - 0.4f);
            }

        // Clutter along the walls. (from, to, inward, low only, tall allowed)
        int n = 0;
        n += WallRun(yard, new Vector3(0f, 0f, 12f), new Vector3(0f, 0f, 37f), Vector3.right, false, false);
        n += WallRun(yard, new Vector3(40f, 0f, 37f), new Vector3(40f, 0f, 11f), Vector3.left, false, false);
        n += WallRun(yard, new Vector3(16f, 0f, 38f), new Vector3(39f, 0f, 38f), Vector3.back, false, true);
        int yardCount = n;
        if (heavy != null)
        {
            n += WallRun(heavy, new Vector3(0f, 0f, 40f), new Vector3(0f, 0f, 73f), Vector3.right, false, true);
            n += WallRun(heavy, new Vector3(40f, 0f, 73f), new Vector3(40f, 0f, 40f), Vector3.left, false, true);
            n += WallRun(heavy, new Vector3(2f, 0f, 74f), new Vector3(38f, 0f, 74f), Vector3.back, false, true);
            n += WallRun(heavy, new Vector3(16f, 0f, 38f), new Vector3(39f, 0f, 38f), Vector3.forward, true, false);
            n += WallRun(heavy, new Vector3(1f, 0f, 38f), new Vector3(8f, 0f, 38f), Vector3.forward, true, false);
        }

        // Low clutter islands in the open ground: the middle of an area should not be bare sand.
        if (heavy != null) n += Islands(heavy, Rect.MinMaxRect(3.4f, 42.4f, 36.6f, 71f), 2.3f, 14);
        int heavyCount = n - yardCount;
        Obstacles.Add((Around(new Vector3(12f, 0f, 17.5f), 3.2f), "player start", null));
        Obstacles.Add((Around(new Vector3(15f, 0f, 12.5f), 2f), "worker spawn", null));
        int islands = Islands(yard, Rect.MinMaxRect(2f, 11.6f, 26f, 22.6f), 2.3f, 7);
        yardCount += islands;
        n += islands;
        if (plant != null)
        {
            n += WallRun(plant, new Vector3(41f, 0f, 42f), new Vector3(60f, 0f, 42f), Vector3.back, false, true);
            n += WallRun(plant, new Vector3(40f, 0f, 28f), new Vector3(40f, 0f, 41f), Vector3.right, false, false);
            n += WallRun(plant, new Vector3(40f, 0f, 11f), new Vector3(40f, 0f, 21f), Vector3.right, false, false);
        }

        Log.AppendLine($"dress: {Lanes.Length} lanes, clusters yard {yardCount}, heavy yard {heavyCount}, plant {n - yardCount - heavyCount}");
    }

    static Transform NewRoot(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.position = Vector3.zero;
        return t;
    }

    // =====================================================================================
    // OLD YARD LOOK (the reference area: once this look is approved it goes to every area)
    // =====================================================================================

    // The scrap pit: the oil-dark field where scrap spawns. Openings in its kerb: the lane from the road (south), the
    // way to the crusher's pad (east) and Gate 3 (north).
    static readonly Rect Pit = Rect.MinMaxRect(1.5f, 23.5f, 18.5f, 36.5f);

    static void OldYard(Transform env)
    {
        var root = NewRoot(env, "EnvPB_OldYard");

        // Ground: the yard was one flat sand colour. It is now warm concrete paving (4 m slabs, joints, stains) and the
        // scrap field is dark gravel, so the place reads as a paved yard with a pit, not a beach.
        var pavers = GroundMaterial("GroundPavers", PaverTexture(), 0.22f);
        var gravel = GroundMaterial("GroundGravel", GravelTexture(), 0.12f);
        Apply(env.Find("Area1_Ground"), pavers, 16f);
        Apply(env.Find("ScrapZone_Floor"), gravel, 4f);

        // Hazard kerb around the pit (yellow / dark blocks), with bollards at each opening.
        var kerb = NewRoot(root, "EnvPB_PitKerb");
        Kerb(kerb, new Vector3(Pit.xMin, 0f, Pit.yMin), new Vector3(Pit.xMax, 0f, Pit.yMin), (10f, 14f));
        Kerb(kerb, new Vector3(Pit.xMax, 0f, Pit.yMin), new Vector3(Pit.xMax, 0f, Pit.yMax), (25f, 29.6f));
        Kerb(kerb, new Vector3(Pit.xMin, 0f, Pit.yMax), new Vector3(Pit.xMax, 0f, Pit.yMax), (9f, 15f));
        Merge(kerb);

        // No words painted on the floor: they ran under props and off the edge of the view and read as overlapping text.
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(P + "/Art/Fonts/GROBOLD SDF.asset");
        var fontMat = AssetDatabase.LoadAssetAtPath<Material>(P + "/Art/Fonts/GROBOLD World.mat");

        // Landmark: the yard's billboard above the north wall (north of everything the player walks on, so it never
        // hides them), lit by three lamps.
        Billboard(root, new Vector3(27f, 0f, 38.9f), font, fontMat);
        Log.AppendLine("old yard: paved ground, gravel pit with hazard kerb, billboard");
    }

    /// <summary>
    /// The grass everything stands on. Its mesh had shrunk to a 1 m cube (already so in the backup taken before the
    /// revamp), so outside the walls there was nothing: the backdrop trees stood over the void. It is rebuilt at an
    /// explicit size: west and south far past the camera's view; east past where the port used to be (F1 removed it). It stays out of the NavMesh bake, which was made without it.
    /// </summary>
    static void WorldGround(Transform env)
    {
        var slab = env.Find("WorldGround");
        if (slab == null || !slab.TryGetComponent(out MeshFilter mf))
        {
            Log.AppendLine("!! no WorldGround");
            return;
        }

        const float west = -90f, east = 200f, south = -70f, north = 170f, top = -0.05f, thick = 0.2f;
        slab.localScale = Vector3.one;
        var k = new MeshKit { UvScale = 1f };
        k.Box(0, slab.InverseTransformPoint(new Vector3((west + east) * 0.5f, top - thick * 0.5f, (south + north) * 0.5f)), new Vector3(east - west, thick, north - south), 0f);
        mf.sharedMesh = ArtAssets.SaveMesh("Ground", "WorldGround", k.ToMesh("WorldGround"));
        if (!slab.TryGetComponent(out NavMeshModifier modifier)) modifier = slab.gameObject.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = true;
        var b = slab.GetComponent<Renderer>().bounds;
        Log.AppendLine($"world ground: x {b.min.x:0}..{b.max.x:0}, z {b.min.z:0}..{b.max.z:0}, top {b.max.y:0.00}");

        // The road and its kerb stopped at x -25, in the middle of the grass (traffic drove on over the lawn). They now
        // run west as far as the ground does.
        foreach (string n in new[] { "Road_South", "Sidewalk" })
        {
            var strip = env.Find(n);
            if (strip == null || !strip.TryGetComponent(out MeshFilter stripMesh)) continue;
            var sb = strip.GetComponent<Renderer>().bounds;
            if (sb.min.x <= west + 0.5f) continue;
            var sk = new MeshKit { UvScale = 1f };
            var c = new Vector3((west + sb.max.x) * 0.5f, sb.center.y, sb.center.z);
            sk.Box(0, strip.InverseTransformPoint(c), new Vector3(sb.max.x - west, sb.size.y, sb.size.z), 0f);
            stripMesh.sharedMesh = ArtAssets.SaveMesh("Ground", n, sk.ToMesh(n));
        }
    }

    static Material GroundMaterial(string name, Texture2D texture, float smoothness)
    {
        var m = Mat(name, Color.white, smoothness, 0f, Detail.Plain, 1f);
        m.SetTexture("_BaseMap", texture);
        m.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Puts a tiling ground material on a scaled slab: one texture repeat every <paramref name="metres"/>.</summary>
    static void Apply(Transform slab, Material source, float metres)
    {
        if (slab == null || !slab.TryGetComponent(out Renderer r)) return;
        // One material per slab, because the repeat count depends on the slab's size.
        var m = Mat(source.name.Substring(2) + "_" + slab.name, Color.white, source.GetFloat("_Smoothness"), 0f, Detail.Plain, 1f);
        m.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
        m.SetTextureScale("_BaseMap", new Vector2(r.bounds.size.x / metres, r.bounds.size.z / metres));
        EditorUtility.SetDirty(m);
        r.sharedMaterial = m;
    }

    /// <summary>16 m of paving: 4 x 4 slabs with dark joints, a tone per slab, mottling and a few oil stains. Tiles.</summary>
    static Texture2D PaverTexture()
    {
        const int n = 512, slab = 128;
        var px = new Color[n * n];
        var concrete = new Color(0.82f, 0.77f, 0.68f);
        var hash = new System.Random(7);
        var tones = Enumerable.Range(0, 16).Select(_ => 0.93f + (float)hash.NextDouble() * 0.11f).ToArray();
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                int lx = x % slab, ly = y % slab;
                int edge = Mathf.Min(Mathf.Min(lx, slab - 1 - lx), Mathf.Min(ly, slab - 1 - ly));
                float joint = edge < 2 ? 0.55f : edge < 4 ? 0.86f : 1f;
                float mottle = 0.9f + 0.16f * Fbm(u, v, 8, 4, 11);
                float grit = 0.97f + 0.06f * Noise(u, v, 128, 3);
                float stain = Mathf.SmoothStep(0.6f, 0.8f, Fbm(u, v, 4, 3, 23));
                var c = concrete * (tones[x / slab + y / slab * 4] * mottle * grit * joint);
                c = Color.Lerp(c, new Color(0.34f, 0.3f, 0.27f), stain * 0.45f);
                c.a = 1f;
                px[y * n + x] = c;
            }

        return SaveTexture(ArtAssets.TexDir + "/T_GroundPavers.png", n, px, true);
    }

    /// <summary>4 m of dark gravel: oily base with light and dark stones.</summary>
    static Texture2D GravelTexture()
    {
        const int n = 256;
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float stones = Noise(u, v, 96, 5), fine = Noise(u, v, 128, 9), patch = Fbm(u, v, 4, 3, 31);
                float shade = 0.3f + 0.1f * patch + (stones > 0.72f ? 0.2f : stones < 0.25f ? -0.07f : 0f) + (fine - 0.5f) * 0.08f;
                px[y * n + x] = new Color(shade * 1.04f, shade * 0.98f, shade * 0.93f, 1f);
            }

        return SaveTexture(ArtAssets.TexDir + "/T_GroundGravel.png", n, px, true);
    }

    static void Kerb(Transform parent, Vector3 from, Vector3 to, (float min, float max) gap)
    {
        Vector3 dir = (to - from).normalized;
        bool alongX = Mathf.Abs(dir.x) > 0.5f;
        float len = Vector3.Distance(from, to);
        const float block = 1f;
        int index = 0;
        for (float s = 0f; s + block <= len + 0.01f; s += block, index++)
        {
            Vector3 c = from + dir * (s + block * 0.5f);
            float along = alongX ? c.x : c.z;
            if (along > gap.min && along < gap.max) continue;
            Cube("Kerb", parent, new Vector3(c.x, 0.09f, c.z), alongX ? new Vector3(block - 0.04f, 0.18f, 0.34f) : new Vector3(0.34f, 0.18f, block - 0.04f), default,
                index % 2 == 0 ? mLineYellow : mDark, 0.04f);
        }

        // A bollard on each side of the opening.
        foreach (float g in new[] { gap.min, gap.max })
        {
            Vector3 p = alongX ? new Vector3(g, 0f, from.z) : new Vector3(from.x, 0f, g);
            Tube("Bollard", parent, p + Vector3.up * 0.42f, 0.17f, 0.84f, default, mLineYellow, 12);
            Tube("BollardBand", parent, p + Vector3.up * 0.6f, 0.18f, 0.14f, default, mDark, 12);
            Tube("BollardCap", parent, p + Vector3.up * 0.87f, 0.2f, 0.08f, default, mDark, 12);
        }
    }

    static void Label(GameObject go, string text, Vector2 size, Color color, TMP_FontAsset font, Material fontMaterial)
    {
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = font;
        if (fontMaterial != null) tmp.fontSharedMaterial = fontMaterial;
        tmp.text = text;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.rectTransform.sizeDelta = size;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 1f;
        tmp.fontSizeMax = 40f;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
    }

    static void Billboard(Transform parent, Vector3 pos, TMP_FontAsset font, Material fontMaterial)
    {
        var root = NewRoot(parent, "EnvPB_Billboard");
        root.position = pos;
        const float w = 13f, bottom = 3.6f, h = 3.4f;
        foreach (float x in new[] { -4.6f, 4.6f })
        {
            Cube("Leg", root, new Vector3(x, bottom * 0.5f, 0.2f), new Vector3(0.5f, bottom, 0.5f), default, mDark, 0.06f);
            Cube("Brace", root, new Vector3(x, bottom * 0.55f, 0.75f), new Vector3(0.22f, bottom * 1.1f, 0.22f), new Vector3(-18f, 0f, 0f), mDark, 0.03f);
        }

        Cube("Frame", root, new Vector3(0f, bottom + h * 0.5f, 0f), new Vector3(w + 0.5f, h + 0.5f, 0.3f), default, mDark, 0.08f);
        Cube("Board", root, new Vector3(0f, bottom + h * 0.5f, -0.12f), new Vector3(w, h, 0.2f), default, mRed, 0.05f);
        Cube("StripeTop", root, new Vector3(0f, bottom + h - 0.32f, -0.2f), new Vector3(w - 0.5f, 0.2f, 0.1f), default, mLineYellow, 0.02f);
        Cube("StripeBottom", root, new Vector3(0f, bottom + 0.32f, -0.2f), new Vector3(w - 0.5f, 0.2f, 0.1f), default, mLineYellow, 0.02f);
        foreach (float x in new[] { -4.2f, 0f, 4.2f })
        {
            Cube("LampArm", root, new Vector3(x, bottom + h + 0.45f, -0.5f), new Vector3(0.1f, 0.1f, 1.1f), default, mDark, 0.02f);
            Cube("Lamp", root, new Vector3(x, bottom + h + 0.4f, -1.05f), new Vector3(0.6f, 0.22f, 0.34f), new Vector3(-30f, 0f, 0f), mLamp, 0.04f);
        }

        Merge(root);
        var text = new GameObject("Text");
        text.transform.SetParent(root, false);
        text.transform.localPosition = new Vector3(0f, bottom + h * 0.5f, -0.24f);
        Label(text, "SCRAP YARD KING", new Vector2(w - 1.2f, h - 1.1f), new Color(1f, 0.95f, 0.8f), font, fontMaterial);
    }

    // ---------- flat ground pieces ----------

    static void Lane(Transform parent, Rect r, bool alongZ)
    {
        if (parent == null) return;
        var root = NewRoot(parent, "EnvPB_Lane");
        Flat("Asphalt", root, r, 0.03f, 0.016f, mAsphalt);
        const float edge = 0.14f;
        if (alongZ)
        {
            Flat("EdgeW", root, Rect.MinMaxRect(r.xMin + 0.12f, r.yMin, r.xMin + 0.12f + edge, r.yMax), 0.04f, 0.008f, mLineYellow);
            Flat("EdgeE", root, Rect.MinMaxRect(r.xMax - 0.12f - edge, r.yMin, r.xMax - 0.12f, r.yMax), 0.04f, 0.008f, mLineYellow);
            for (float z = r.yMin + 0.8f; z + 1.2f < r.yMax; z += 2.6f)
                Flat("Dash", root, Rect.MinMaxRect(r.center.x - 0.07f, z, r.center.x + 0.07f, z + 1.2f), 0.04f, 0.008f, mLineWhite);
        }
        else
        {
            Flat("EdgeS", root, Rect.MinMaxRect(r.xMin, r.yMin + 0.12f, r.xMax, r.yMin + 0.12f + edge), 0.04f, 0.008f, mLineYellow);
            Flat("EdgeN", root, Rect.MinMaxRect(r.xMin, r.yMax - 0.12f - edge, r.xMax, r.yMax - 0.12f), 0.04f, 0.008f, mLineYellow);
            for (float x = r.xMin + 0.8f; x + 1.2f < r.xMax; x += 2.6f)
                Flat("Dash", root, Rect.MinMaxRect(x, r.center.y - 0.07f, x + 1.2f, r.center.y + 0.07f), 0.04f, 0.008f, mLineWhite);
        }

        Merge(root);
    }

    /// <summary>An oil-dark slab with yellow corner marks under a heavy vehicle: reads as a dismantling bay.</summary>
    static void WorkPad(Transform parent, Vector3 pos, float w, float d)
    {
        var root = NewRoot(parent, "EnvPB_Pad");
        var r = new Rect(pos.x - w / 2f, pos.z - d / 2f, w, d);
        Flat("Pad", root, r, 0.012f, 0.012f, mOilPad);
        const float len = 1.1f, th = 0.14f;
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                float cx = sx < 0 ? r.xMin : r.xMax, cz = sz < 0 ? r.yMin : r.yMax;
                Flat("MarkX", root, Rect.MinMaxRect(Mathf.Min(cx, cx - sx * len), Mathf.Min(cz, cz - sz * th), Mathf.Max(cx, cx - sx * len), Mathf.Max(cz, cz - sz * th)), 0.022f, 0.008f, mLineYellow);
                Flat("MarkZ", root, Rect.MinMaxRect(Mathf.Min(cx, cx - sx * th), Mathf.Min(cz, cz - sz * len), Mathf.Max(cx, cx - sx * th), Mathf.Max(cz, cz - sz * len)), 0.022f, 0.008f, mLineYellow);
            }

        Merge(root);
    }

    static void Flat(string name, Transform parent, Rect r, float y, float height, Material mat)
    {
        var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, new Vector3(r.width, height, r.height));
        Finish(pb, name, parent, new Vector3(r.center.x, y + height * 0.5f, r.center.y), default, mat);
        pb.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    // ---------- wall clutter ----------

    enum Kind { Bales, Pipes, Barriers, Drums, Crates, Tank, Floodlight }

    static (float w, float d) Size(Kind kind) => kind switch
    {
        Kind.Bales => (3.7f, 1.35f), Kind.Pipes => (3.5f, 1.8f), Kind.Barriers => (4.5f, 0.7f), Kind.Drums => (2.3f, 1.5f), Kind.Crates => (2.6f, 1.4f),
        Kind.Tank => (3.3f, 3.3f), _ => (1.1f, 1.1f),
    };

    /// <summary>Builds a cluster at <paramref name="center"/> if its footprint is free. <paramref name="facing"/> is its local +z.</summary>
    static bool Place(Transform parent, Kind kind, Vector3 center, Vector3 facing, bool low)
    {
        var (w, d) = Size(kind);
        Vector3 along = Vector3.Cross(Vector3.up, facing);
        Vector3 half = along * (w * 0.5f) + facing * (d * 0.5f);
        var rect = Rect.MinMaxRect(center.x - Mathf.Abs(half.x), center.z - Mathf.Abs(half.z), center.x + Mathf.Abs(half.x), center.z + Mathf.Abs(half.z));
        if (!Free(Grow(rect, 0.3f)))
        {
            return false;
        }

        var root = NewRoot(parent, $"EnvPB_{kind}");
        root.SetPositionAndRotation(center, Quaternion.LookRotation(facing, Vector3.up));
        float height = Build(kind, root, low);
        Merge(root);
        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, height * 0.5f, 0f);
        box.size = new Vector3(w, height, d);
        if (kind != Kind.Floodlight) root.gameObject.AddComponent<DropBlocker>();
        Obstacles.Add((Grow(rect, 0.9f), root.name, root));   // room to walk around it
        return true;
    }

    static int WallRun(Transform parent, Vector3 from, Vector3 to, Vector3 inward, bool lowOnly, bool tallOk)
    {
        if (parent == null) return 0;
        Vector3 dir = (to - from).normalized;
        float len = Vector3.Distance(from, to), s = Range(0.4f, 1.6f);
        int made = 0, sinceLight = 2;
        while (s < len - 1f)
        {
            Kind kind;
            if (tallOk && sinceLight >= 3 && rng.NextDouble() < 0.5) kind = Kind.Floodlight;
            else if (tallOk && rng.NextDouble() < 0.14) kind = Kind.Tank;
            else kind = (Kind)rng.Next(0, 5);
            var (w, d) = Size(kind);
            Vector3 center = from + dir * (s + w * 0.5f) + inward * (0.45f + d * 0.5f);
            if (s + w > len || !Place(parent, kind, center, inward, lowOnly))
            {
                s += 1.1f;
                continue;
            }

            sinceLight = kind == Kind.Floodlight ? 0 : sinceLight + 1;
            made++;
            s += w + Range(0.5f, 2.4f);
        }

        return made;
    }

    /// <summary>Scatters up to <paramref name="max"/> low clusters over free ground. Lanes, bays, pads and tiles are obstacles, so walkways stay open.</summary>
    static int Islands(Transform parent, Rect area, float spacing, int max)
    {
        if (parent == null) return 0;
        // Visit the grid in a shuffled order so the islands spread over the area instead of filling its south edge.
        var spots = new List<Vector2>();
        for (float z = area.yMin; z <= area.yMax; z += spacing)
            for (float x = area.xMin; x <= area.xMax; x += spacing)
                spots.Add(new Vector2(x, z));
        int made = 0;
        Vector3[] facings = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
        foreach (var spot in spots.OrderBy(_ => rng.Next()).ToList())
        {
            if (made >= max) break;
            var kind = (Kind)rng.Next(0, 5);
            if (Place(parent, kind, new Vector3(spot.x + Jitter(0.5f), 0f, spot.y + Jitter(0.5f)), facings[rng.Next(4)], true)) made++;
        }

        return made;
    }

    /// <summary>Builds one cluster in local space (x along the wall, +z into the yard). Returns its height.</summary>
    static float Build(Kind kind, Transform root, bool low)
    {
        switch (kind)
        {
            case Kind.Bales:
            {
                // Crushed scrap bales: three on the ground, two on top, sometimes a third layer.
                const float s = 1.12f;
                int layers = low ? 1 : rng.Next(2, 4);
                for (int layer = 0; layer < layers; layer++)
                {
                    int count = 3 - layer;
                    for (int i = 0; i < count; i++)
                    {
                        var size = new Vector3(s + Jitter(0.06f), s * Range(0.82f, 1f), s + Jitter(0.06f));
                        var pos = new Vector3((i - (count - 1) * 0.5f) * (s + 0.06f) + Jitter(0.05f), layer * s * 0.93f + size.y * 0.5f, Jitter(0.06f));
                        Cube("Bale", root, pos, size, new Vector3(0f, Jitter(7f), 0f), scrapPaints[rng.Next(scrapPaints.Length)], 0.07f);
                    }
                }

                return layers * s;
            }
            case Kind.Pipes:
            {
                const float r = 0.27f, length = 3.2f;
                var paint = pipePaints[rng.Next(pipePaints.Length)];
                int rows = low ? 1 : 3;
                for (int row = 0; row < rows; row++)
                    for (int i = 0; i < 3 - row; i++)
                        Tube("Pipe", root, new Vector3(Jitter(0.12f), r + row * r * 1.75f, (i - (2 - row) * 0.5f) * r * 2.05f), r, length, new Vector3(0f, 0f, 90f), row == 0 ? paint : pipePaints[rng.Next(pipePaints.Length)]);
                foreach (float x in new[] { -1.1f, 1.1f })
                    foreach (float z in new[] { -0.72f, 0.72f })
                        Cube("Chock", root, new Vector3(x, 0.11f, z), new Vector3(0.24f, 0.22f, 0.2f), default, mWood, 0.03f);
                return r * 2f + (rows - 1) * r * 1.75f;
            }
            case Kind.Barriers:
            {
                for (int i = 0; i < 2; i++)
                {
                    float x = (i - 0.5f) * 2.2f;
                    bool painted = rng.Next(2) == 0;
                    Cube("BarrierBase", root, new Vector3(x, 0.17f, 0f), new Vector3(2.05f, 0.34f, 0.62f), default, mConcrete, 0.05f);
                    Cube("BarrierMid", root, new Vector3(x, 0.48f, 0f), new Vector3(2.05f, 0.3f, 0.4f), default, painted ? mRed : mConcrete, 0.04f);
                    Cube("BarrierTop", root, new Vector3(x, 0.76f, 0f), new Vector3(2.05f, 0.28f, 0.26f), default, painted ? mWhite : mConcrete, 0.04f);
                }

                return 0.9f;
            }
            case Kind.Drums:
            {
                const float r = 0.31f, h = 0.9f;
                var spots = new List<Vector3>();
                for (int ix = 0; ix < 3; ix++)
                    for (int iz = 0; iz < 2; iz++)
                        if (rng.NextDouble() < 0.82) spots.Add(new Vector3((ix - 1) * 0.68f + Jitter(0.04f), h * 0.5f, (iz - 0.5f) * 0.68f + Jitter(0.04f)));
                foreach (var p in spots) Drum(root, p, r, h, drumPaints[rng.Next(drumPaints.Length)]);
                if (!low && spots.Count >= 4) Drum(root, new Vector3(Jitter(0.2f), h * 1.5f + 0.02f, Jitter(0.1f)), r, h, drumPaints[rng.Next(drumPaints.Length)]);
                return low ? h : h * 2f;
            }
            case Kind.Crates:
            {
                Cube("Pallet", root, new Vector3(0f, 0.07f, 0f), new Vector3(2.5f, 0.14f, 1.3f), default, mWood, 0.02f);
                for (int i = 0; i < 2; i++)
                {
                    float x = (i - 0.5f) * 1.18f;
                    Crate(root, new Vector3(x, 0.14f + 0.5f, Jitter(0.05f)), 1f, Jitter(5f));
                }

                if (!low && rng.Next(3) > 0) Crate(root, new Vector3(Jitter(0.3f), 0.14f + 1.42f, 0f), 0.84f, Jitter(12f));
                return low ? 1.15f : 2f;
            }
            case Kind.Tank:
            {
                var paint = rng.Next(2) == 0 ? mWhite : scrapPaints[1];
                Cube("Base", root, new Vector3(0f, 0.15f, 0f), new Vector3(3.1f, 0.3f, 3.1f), default, mConcrete, 0.05f);
                Tube("Tank", root, new Vector3(0f, 0.3f + 1.7f, 0f), 1.38f, 3.4f, default, paint, 18);
                foreach (float y in new[] { 1.1f, 2.4f, 3.5f }) Tube("Band", root, new Vector3(0f, y, 0f), 1.42f, 0.12f, default, mDark, 18);
                Tube("Cap", root, new Vector3(0f, 3.78f, 0f), 1.1f, 0.18f, default, mDark, 18);
                Tube("Vent", root, new Vector3(0.4f, 4.1f, 0f), 0.22f, 0.5f, default, mSteel, 10);
                Cube("Ladder", root, new Vector3(0f, 2f, 1.42f), new Vector3(0.5f, 3.4f, 0.08f), default, mLineYellow, 0.02f);
                Cube("Valve", root, new Vector3(-0.9f, 0.7f, 1.3f), new Vector3(0.34f, 0.34f, 0.4f), default, mRed, 0.05f);
                return 4.3f;
            }
            default:
            {
                Cube("Foot", root, new Vector3(0f, 0.2f, 0f), new Vector3(0.9f, 0.4f, 0.9f), default, mConcrete, 0.06f);
                Cube("FootStripe", root, new Vector3(0f, 0.43f, 0f), new Vector3(0.7f, 0.08f, 0.7f), default, mLineYellow, 0.02f);
                Tube("Pole", root, new Vector3(0f, 3.2f, 0f), 0.1f, 5.6f, default, mDark, 10);
                Cube("Arm", root, new Vector3(0f, 5.9f, 0.25f), new Vector3(2.1f, 0.14f, 0.14f), default, mDark, 0.03f);
                foreach (float x in new[] { -0.8f, 0f, 0.8f })
                {
                    Cube("LampBox", root, new Vector3(x, 5.72f, 0.42f), new Vector3(0.56f, 0.34f, 0.3f), new Vector3(38f, 0f, 0f), mDark, 0.04f);
                    Cube("LampGlass", root, new Vector3(x, 5.63f, 0.53f), new Vector3(0.44f, 0.24f, 0.06f), new Vector3(38f, 0f, 0f), mLamp, 0.01f);
                }

                return 6f;
            }
        }
    }

    static void Drum(Transform root, Vector3 pos, float r, float h, Material paint)
    {
        Tube("Drum", root, pos, r, h, default, paint, 12);
        foreach (float dy in new[] { -0.28f, 0.28f }) Tube("DrumRing", root, pos + Vector3.up * dy, r + 0.02f, 0.05f, default, mDark, 12);
    }

    static void Crate(Transform root, Vector3 pos, float s, float yaw)
    {
        var e = new Vector3(0f, yaw, 0f);
        Cube("Crate", root, pos, Vector3.one * s, e, mWood, 0.03f);
        // Dark edge battens on the two visible faces.
        var q = Quaternion.Euler(e);
        foreach (float dx in new[] { -0.42f, 0.42f })
            Cube("Batten", root, pos + q * new Vector3(dx * s, 0f, 0.5f * s), new Vector3(0.1f * s, s * 1.02f, 0.05f), e, mDark, 0.01f);
    }

    // ---------- ProBuilder primitives ----------

    static void Cube(string name, Transform parent, Vector3 localPos, Vector3 size, Vector3 euler, Material mat, float bevel)
    {
        var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
        if (bevel > 0f)
        {
            try
            {
                Bevel.BevelEdges(pb, pb.faces.SelectMany(f => f.edges).ToList(), bevel);
            }
            catch (Exception)
            {
                // A failed bevel leaves a plain cube, which is fine.
            }
        }

        Finish(pb, name, parent, localPos, euler, mat);
    }

    static void Tube(string name, Transform parent, Vector3 localPos, float radius, float height, Vector3 euler, Material mat, int sides = 12)
    {
        var pb = ShapeGenerator.GenerateCylinder(PivotLocation.Center, sides, radius, height, 0, 1);
        Finish(pb, name, parent, localPos, euler, mat);
    }

    static void Finish(ProBuilderMesh pb, string name, Transform parent, Vector3 localPos, Vector3 euler, Material mat)
    {
        pb.gameObject.name = name;
        pb.transform.SetParent(parent, false);
        pb.transform.localPosition = localPos;
        pb.transform.localRotation = Quaternion.Euler(euler);
        pb.SetMaterial(pb.faces, mat);
        pb.ToMesh();
        pb.Refresh();
        if (pb.TryGetComponent(out Collider col)) Object.DestroyImmediate(col);
    }

    /// <summary>Merges a cluster's pieces into one ProBuilder mesh on the first child (one object to move or edit, fewer renderers).</summary>
    static void Merge(Transform root)
    {
        var meshes = root.GetComponentsInChildren<ProBuilderMesh>().ToList();
        if (meshes.Count < 2) return;
        try
        {
            bool noShadows = meshes[0].GetComponent<Renderer>().shadowCastingMode == ShadowCastingMode.Off;
            var result = CombineMeshes.Combine(meshes, meshes[0]);
            foreach (var m in meshes.Skip(1))
                if (m != null && !result.Contains(m)) Object.DestroyImmediate(m.gameObject);
            foreach (var m in result)
            {
                m.ToMesh();
                m.Refresh();
                m.gameObject.name = "Mesh";
                if (m.TryGetComponent(out Collider col)) Object.DestroyImmediate(col);
                if (noShadows) m.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                GameObjectUtility.SetStaticEditorFlags(m.gameObject, StaticEditorFlags.BatchingStatic);
            }
        }
        catch (Exception e)
        {
            Log.AppendLine($"merge failed on {root.name}: {e.Message}");
        }
    }

    static Transform Root(Scene scene, string name) => scene.GetRootGameObjects().First(g => g.name == name).transform;
}

using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScrapYardKing.Economy;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Factory;
using ScrapYardKing.Feedback;
using ScrapYardKing.Items;
using ScrapYardKing.Progression;
using ScrapYardKing.Tiles;
using ScrapYardKing.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Fun pass F5 (owner's brief, 2026-10-09): Heavy Yard automation.
///  - the Dump Yard, a new area east of the Heavy Yard (x 40–76, z 42–74, walls built now so it shows as a fenced lot),
///    opened through a gate in the Heavy Yard's east wall at the cross lane; bought on a tile beside the gate;
///  - an excavator (in the Heavy Yard's old garbage-truck bay) scoops loose scrap onto dump trucks; two dump trucks
///    loop through the gate, tip their load on the dump pile and come back (DumpRoute, DumpTruck, Excavator);
///  - a crane loads the Heavy Crusher from the pile; the Heavy Crusher (a big crusher, several pieces per cycle) puts
///    Raw Metal on a belt that runs through the plant's north wall straight into the Metal Splitter, so the four
///    furnaces get the volume their higher levels can process;
///  - leftovers of the port in the catalog (ship_dock) go.
/// Run after F4_Build; then F4_Build.Scene again (it dresses the new belt) and R1_Build.Bake.
/// Entry points: Assets, Scene, All. Idempotent.
/// </summary>
public static class F5_Build
{
    const string Root = "Assets/_Project";
    const string DataDir = Root + "/Data";
    const string ScenePath = Root + "/Scenes/Area1_OldScrapYard.unity";
    const float GateZ = 50.5f, GateHalf = 2.7f;
    static readonly Vector3 CrusherAt = new(50f, 0f, 47.4f), CraneAt = new(56.5f, 0f, 51.5f), ExcavatorAt = new(32.9f, 0f, 56.4f);
    static readonly StringBuilder Log = new();

    static int C(PC c) => (int)c;

    public static string All() => Assets() + Scene();

    // ================================================================== data

    public static string Assets()
    {
        Log.Clear();
        var crusherDef = CopyAsset<MachineDefinition>("Factory/Machine_Crusher.asset", "Factory/Machine_HeavyCrusher.asset");
        Set(crusherDef, so =>
        {
            so.FindProperty("id").stringValue = "heavy_crusher";
            so.FindProperty("displayName").stringValue = "Heavy Crusher";
            var levels = so.FindProperty("levels");
            var table = new (int cap, float cycle, int n, long cost)[] { (60, 0.5f, 3, 0), (80, 0.42f, 3, 12000), (100, 0.36f, 4, 24000), (130, 0.3f, 4, 48000) };
            levels.arraySize = table.Length;
            for (int i = 0; i < table.Length; i++)
            {
                var l = levels.GetArrayElementAtIndex(i);
                l.FindPropertyRelative("inputCapacity").intValue = table[i].cap;
                l.FindPropertyRelative("cycleTime").floatValue = table[i].cycle;
                l.FindPropertyRelative("inputsPerCycle").intValue = table[i].n;
                l.FindPropertyRelative("outputsPerCycle").intValue = table[i].n;
                l.FindPropertyRelative("upgradeCost").longValue = table[i].cost;
            }
        });

        var craneDef = CopyAsset<ClawCraneDefinition>("Factory/ClawCrane_Heavy.asset", "Factory/ClawCrane_Dump.asset");
        Set(craneDef, so =>
        {
            so.FindProperty("id").stringValue = "dump_crane";
            so.FindProperty("displayName").stringValue = "Dump Yard Crane";
            var levels = so.FindProperty("levels");
            var table = new (float reach, int grab, float speed, long cost)[] { (10f, 6, 1f, 0), (11f, 9, 1.25f, 12000), (12f, 12, 1.5f, 24000) };
            levels.arraySize = table.Length;
            for (int i = 0; i < table.Length; i++)
            {
                var l = levels.GetArrayElementAtIndex(i);
                l.FindPropertyRelative("reach").floatValue = table[i].reach;
                l.FindPropertyRelative("grabSize").intValue = table[i].grab;
                l.FindPropertyRelative("speed").floatValue = table[i].speed;
                l.FindPropertyRelative("cost").longValue = table[i].cost;
            }
        });

        var heavy = Load<ExpansionDefinition>("World/Expansion_HeavyYard.asset");
        var area = Asset<ExpansionDefinition>("World/Expansion_DumpYard.asset");
        Set(area, so =>
        {
            so.FindProperty("id").stringValue = "dump_yard";
            so.FindProperty("displayName").stringValue = "Dump Yard";
            so.FindProperty("icon").objectReferenceValue = heavy != null ? heavy.Icon : null;
            so.FindProperty("cost").longValue = 25000;
            so.FindProperty("teaser").stringValue = "HEAVY CRUSHER";   // a longer line ran past the tile
            so.FindProperty("openSfx").objectReferenceValue = heavy != null ? new SerializedObject(heavy).FindProperty("openSfx").objectReferenceValue : null;
        });

        // catalog: the area on its tile, the crusher and crane in the panel; the port's last entry goes
        Set(Load<UpgradeCatalog>("Progression/UpgradeCatalog.asset"), so =>
        {
            var e = so.FindProperty("entries");
            for (int i = e.arraySize - 1; i >= 0; i--)
            {
                string id = e.GetArrayElementAtIndex(i).FindPropertyRelative("upgradeId").stringValue;
                if (id is "ship_dock" or "dump_yard" or "heavy_crusher" or "dump_crane") e.DeleteArrayElementAtIndex(i);
            }

            foreach (var (id, level, panel) in new[] { ("dump_yard", 12, false), ("heavy_crusher", 12, true), ("dump_crane", 12, true) })
            {
                e.arraySize++;
                var x = e.GetArrayElementAtIndex(e.arraySize - 1);
                x.FindPropertyRelative("upgradeId").stringValue = id;
                x.FindPropertyRelative("unlockLevel").intValue = level;
                x.FindPropertyRelative("inPanel").boolValue = panel;
                x.FindPropertyRelative("group").stringValue = panel ? "DUMP YARD" : "";
            }
        });

        // a diamond gift for the new area
        Set(Load<PremiumEconomyConfig>("Economy/PremiumEconomy.asset"), so =>
        {
            var m = so.FindProperty("milestones");
            for (int i = m.arraySize - 1; i >= 0; i--)
                if (m.GetArrayElementAtIndex(i).FindPropertyRelative("expansionId").stringValue == "dump_yard") m.DeleteArrayElementAtIndex(i);
            m.arraySize++;
            var x = m.GetArrayElementAtIndex(m.arraySize - 1);
            x.FindPropertyRelative("expansionId").stringValue = "dump_yard";
            x.FindPropertyRelative("diamonds").intValue = 40;
            x.FindPropertyRelative("title").stringValue = "NEW AREA BONUS";
            x.FindPropertyRelative("line").stringValue = "THE DUMP YARD IS OPEN";
        });

        // the main chain ends with opening it
        var task = CopyAsset<TaskDefinition>("Progression/Tasks/Task_t50_dockyard.asset", "Progression/Tasks/Task_t51_dump_yard.asset");
        Set(task, so =>
        {
            so.FindProperty("id").stringValue = "t51_dump_yard";
            so.FindProperty("title").stringValue = "Open the Dump Yard";
            so.FindProperty("targetId").stringValue = "dump_yard";
            so.FindProperty("rewardPremium").intValue = 25;
        });
        Set(Load<TaskChain>("Progression/TaskChain_Area1.asset"), so =>
        {
            var t = so.FindProperty("mainTasks");
            for (int i = t.arraySize - 1; i >= 0; i--)
                if (t.GetArrayElementAtIndex(i).objectReferenceValue == task)
                {
                    t.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    t.DeleteArrayElementAtIndex(i);
                }

            t.arraySize++;
            t.GetArrayElementAtIndex(t.arraySize - 1).objectReferenceValue = task;
        });

        AssetDatabase.SaveAssets();
        Log.AppendLine("assets: Heavy Crusher, Dump Yard Crane, Dump Yard area ($25K, Lv 12, 40 diamonds), task t51; ship_dock out of the catalog");
        return Log.ToString();
    }

    // ================================================================== scene

    public static string Scene()
    {
        Log.Clear();
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject RootGo(string n) => scene.GetRootGameObjects().First(g => g.name == n);
        var environment = RootGo("_Environment").transform;
        var gameplay = RootGo("_Gameplay").transform;

        foreach (var n in new[] { "DumpYard", "DumpYardRoute" })
        {
            var old = gameplay.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        var oldGate = environment.Find("Gate_HY_DY");
        if (oldGate != null) Object.DestroyImmediate(oldGate.gameObject);
        var oldFloor = environment.Find("DumpYardFloor");
        if (oldFloor != null) Object.DestroyImmediate(oldFloor.gameObject);

        Walls(environment.Find("Fences"));
        Floor(environment);
        Tidy(scene);

        // ---------- the area ----------
        var areaGo = new GameObject("DumpYard");
        areaGo.transform.SetParent(gameplay, false);
        var content = new GameObject("Content").transform;
        content.SetParent(areaGo.transform, false);
        content.gameObject.AddComponent<NavMeshModifier>();
        var focus = new GameObject("Focus").transform;
        focus.SetParent(areaGo.transform, false);
        focus.position = new Vector3(52f, 0f, 54f);

        var barriers = Gate(environment);
        var route = Route(gameplay);
        var crusher = HeavyCrusher(content);
        var belt = Belt(content, crusher);
        Set(crusher, so => so.FindProperty("outputTarget").objectReferenceValue = belt);
        Crane(content, gameplay, crusher);
        Excavator(content, route);
        DumpTruck(content, route, "DumpTruck_A", 0, PC.Orange);
        DumpTruck(content, route, "DumpTruck_B", 6, PC.Yellow);
        Pile(content);

        var expansion = areaGo.AddComponent<Expansion>();
        Set(expansion, so =>
        {
            so.FindProperty("definition").objectReferenceValue = Load<ExpansionDefinition>("World/Expansion_DumpYard.asset");
            var b = so.FindProperty("barriers");
            b.arraySize = barriers.Count;
            for (int i = 0; i < barriers.Count; i++) b.GetArrayElementAtIndex(i).objectReferenceValue = barriers[i];
            so.FindProperty("contentRoot").objectReferenceValue = content;
            so.FindProperty("area").boundsValue = new Bounds(new Vector3(58f, 0f, 58f), new Vector3(36f, 4f, 32f));
            so.FindProperty("focusPoint").objectReferenceValue = focus;
        });
        foreach (Transform c in content) c.gameObject.SetActive(false);

        BuyTile(gameplay);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("scene saved; run F4_Build.Scene (new belt) and R1_Build.Bake");
        return Log.ToString();
    }

    // ---------- walls and floor ----------

    static void Walls(Transform fences)
    {
        foreach (var n in new[]
                 {
                     "Wall_40_74_40_42", "Wall_40_74_40_53", "Wall_40_47_40_42", "Wall_40_74_76_74", "Wall_76_74_76_42", "Wall_40_42_76_42",
                     "Wall_40_42_49_42", "Wall_51_42_76_42"
                 })
        {
            var old = fences.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        // Heavy Yard east wall: wall | gate | wall
        Wall(fences, "Wall_40_74_40_53", new Vector3(40f, 0f, 74f), new Vector3(40f, 0f, GateZ + GateHalf + 0.2f), 41);
        Wall(fences, "Wall_40_47_40_42", new Vector3(40f, 0f, GateZ - GateHalf - 0.2f), new Vector3(40f, 0f, 42f), 42);
        // the Dump Yard's own north and east walls
        Wall(fences, "Wall_40_74_76_74", new Vector3(40f, 0f, 74f), new Vector3(76f, 0f, 74f), 43);
        Wall(fences, "Wall_76_74_76_42", new Vector3(76f, 0f, 74f), new Vector3(76f, 0f, 42f), 44);
        // the plant's north wall with a gap for the Heavy Crusher's belt
        Wall(fences, "Wall_40_42_49_42", new Vector3(40f, 0f, 42f), new Vector3(49.15f, 0f, 42f), 45);
        Wall(fences, "Wall_51_42_76_42", new Vector3(50.85f, 0f, 42f), new Vector3(76f, 0f, 42f), 46);
        Log.AppendLine("walls: gate in the Heavy Yard's east wall, Dump Yard fenced, belt gap in the plant's north wall");
    }

    static void Floor(Transform environment)
    {
        var k = new MeshKit { UvScale = 1f };
        k.Box(0, new Vector3(58f, -0.04f, 58f), new Vector3(36f, 0.1f, 32f), 0f);
        var floor = ArtAssets.MeshObject("DumpYardFloor", environment, ArtAssets.SaveMesh("Ground", "DumpYardFloor", k.ToMesh("DumpYardFloor", 1)),
            new[] { AssetDatabase.LoadAssetAtPath<Material>(Root + "/Art/Materials/M_Ground_Concrete.mat") });
        floor.AddComponent<BoxCollider>();
        floor.layer = LayerMask.NameToLayer("Ground");
        if (!floor.TryGetComponent(out NavMeshModifier m)) m = floor.AddComponent<NavMeshModifier>();
        m.ignoreFromBuild = true;
        GameObjectUtility.SetStaticEditorFlags(floor, StaticEditorFlags.BatchingStatic);
        // painted haul lane and dump bay
        var paint = new MeshKit();
        foreach (var (a, b) in new[] { (new Vector2(41f, 50.5f), new Vector2(46f, 52f)), (new Vector2(46f, 52f), new Vector2(51f, 58.5f)), (new Vector2(51f, 58.5f), new Vector2(56f, 64f)),
                     (new Vector2(56f, 64f), new Vector2(62f, 60f)), (new Vector2(62f, 60f), new Vector2(60f, 54f)), (new Vector2(60f, 54f), new Vector2(46.5f, 54f)) })
        {
            var d = b - a;
            float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            foreach (int side in new[] { -1, 1 })
            {
                var off = new Vector2(d.y, -d.x).normalized * 1.6f * side;
                var mid = (a + b) * 0.5f + off;
                paint.Box(C(PC.Yellow), new Vector3(mid.x, 0.015f, mid.y), new Vector3(0.12f, 0.01f, d.magnitude), 0f, new Vector3(0f, yaw, 0f));
            }
        }

        for (int i = 0; i < 8; i++)
            paint.Box(C(i % 2 == 0 ? PC.Yellow : PC.Charcoal), new Vector3(45.5f + i * 0.9f, 0.016f, 59.5f), new Vector3(0.9f, 0.01f, 0.4f), 0f);
        ArtAssets.MeshObject("Paint", floor.transform, ArtAssets.SaveMesh("Ground", "DumpYardPaint", paint.ToPaletteMesh("DumpYardPaint")), new[] { ArtPalette.Material });
    }

    /// <summary>Removes what stands where the trucks, the excavator and the belt now go.</summary>
    static void Tidy(Scene scene)
    {
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        int removed = 0;
        foreach (var t in all)
        {
            if (t == null) continue;
            var p = t.position;
            bool prop = t.name.StartsWith("Prop_");
            bool inLaneEnd = p.x > 31.5f && p.x < 39.8f && p.z > 48.6f && p.z < 52.6f;          // the loading bay
            bool inBeltRun = p.x > 48.6f && p.x < 51.4f && p.z > 30.6f && p.z < 41.8f;          // the belt through the plant
            if (prop && (inLaneEnd || inBeltRun))
            {
                Object.DestroyImmediate(t.gameObject);
                removed++;
            }
        }

        // the excavator takes the garbage truck's parking bay (removed: the Heavy Yard's reveal would switch it back on)
        foreach (var sp in Object.FindObjectsByType<ScrapYardKing.Harvest.ScrapSpawnPoint>(FindObjectsInactive.Include))
            if (Vector3.Distance(Flat(sp.transform.position), Flat(new Vector3(32.9f, 0f, 56f))) < 1.5f)
            {
                Object.DestroyImmediate(sp.gameObject);
                Log.AppendLine("tidy: wreck bay at (32.9, 56) now the excavator's");
            }

        Log.AppendLine($"tidy: {removed} props cleared from the loading bay and the belt run");
    }

    // ---------- gate ----------

    static List<Transform> Gate(Transform environment)
    {
        var gate = new GameObject("Gate_HY_DY").transform;
        gate.SetParent(environment, false);
        gate.SetPositionAndRotation(new Vector3(40f, 0f, GateZ), Quaternion.Euler(0f, 90f, 0f));
        gate.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        var posts = new MeshKit();
        foreach (int side in new[] { -1, 1 })
        {
            posts.Box(C(PC.Yellow), new Vector3(side * GateHalf, 1.4f, 0f), new Vector3(0.5f, 2.8f, 0.5f), 0.06f);
            posts.Box(C(PC.Charcoal), new Vector3(side * GateHalf, 0.2f, 0f), new Vector3(0.62f, 0.4f, 0.62f), 0.05f);
            posts.Cylinder(C(PC.Orange), new Vector3(side * GateHalf, 2.92f, 0f), 0.13f, 0.22f, 8, default, 0.04f);
        }

        posts.Box(C(PC.Yellow), new Vector3(0f, 3.1f, 0f), new Vector3(GateHalf * 2f + 0.5f, 0.3f, 0.3f), 0.04f);
        for (int i = 0; i < 8; i++) posts.Box(C(PC.Charcoal), new Vector3(-GateHalf + 0.35f + i * 0.68f, 3.1f, 0.16f), new Vector3(0.3f, 0.26f, 0.02f), 0f, new Vector3(0f, 0f, 40f));
        ArtAssets.MeshObject("Posts", gate, ArtAssets.SaveMesh("F5", "GatePosts", posts.ToPaletteMesh("F5GatePosts")), new[] { ArtPalette.Material });
        var bars = new MeshKit();
        for (int i = 0; i < 5; i++) bars.Box(C(i % 2 == 0 ? PC.Red : PC.White), new Vector3(-2.2f + i * 1.1f, 1.05f, 0f), new Vector3(1.1f, 0.26f, 0.2f), 0.02f);
        for (int i = 0; i < 5; i++) bars.Box(C(i % 2 == 0 ? PC.White : PC.Red), new Vector3(-2.2f + i * 1.1f, 0.55f, 0f), new Vector3(1.1f, 0.2f, 0.16f), 0.02f);
        var barsGo = ArtAssets.MeshObject("Bars", gate, ArtAssets.SaveMesh("F5", "GateBars", bars.ToPaletteMesh("F5GateBars")), new[] { ArtPalette.Material });
        var box = barsGo.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 1f, 0f);
        box.size = new Vector3(GateHalf * 2f - 0.4f, 2f, 0.6f);
        var obstacle = barsGo.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = box.center;
        obstacle.size = box.size;
        obstacle.carving = true;
        return new List<Transform> { barsGo.transform };
    }

    static void BuyTile(Transform gameplay)
    {
        // a copy of the Heavy Yard's own area tile, inside the yard beside the new gate
        var source = Object.FindObjectsByType<PurchaseTile>(FindObjectsInactive.Include).FirstOrDefault(t => new SerializedObject(t).FindProperty("upgradeId").stringValue == "heavy_yard");
        var old = Object.FindObjectsByType<PurchaseTile>(FindObjectsInactive.Include).FirstOrDefault(t => new SerializedObject(t).FindProperty("upgradeId").stringValue == "dump_yard");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        if (source == null) { Log.AppendLine("!! no heavy_yard tile to copy"); return; }
        var heavyContent = gameplay.Find("HeavyYard/Content");
        var tile = Object.Instantiate(source.gameObject, heavyContent != null ? heavyContent : gameplay);
        tile.name = "Tile_DumpYard";
        tile.transform.position = new Vector3(35.4f, 0f, 60.2f);   // free strip north of the excavator (Env_Build.Map)
        Set(tile.GetComponent<PurchaseTile>(), so => so.FindProperty("upgradeId").stringValue = "dump_yard");
        foreach (var a in tile.GetComponentsInChildren<GuideAnchor>(true))
            Set(a, so => so.FindProperty("id").stringValue = "tile/dump_yard");
        Log.AppendLine("tile: Dump Yard at (35.4, 60.2)");
    }

    // ---------- the route ----------

    static DumpRoute Route(Transform gameplay)
    {
        var root = new GameObject("DumpYardRoute");
        root.transform.SetParent(gameplay, false);
        var pts = new (float x, float z, bool rev)[]
        {
            (34.8f, 50.5f, true),  // 0 load bay (backed in)
            (41.0f, 50.5f, false), // 1 through the gate
            (46.0f, 52.0f, false), // 2
            (51.0f, 58.5f, false), // 3 tip
            (56.0f, 64.0f, false), // 4
            (62.0f, 60.0f, false), // 5
            (60.0f, 54.0f, false), // 6 hold
            (52.0f, 54.2f, false), // 7
            (46.5f, 54.0f, false), // 8
            (41.0f, 50.5f, true),  // 9 backing into the gate
        };
        var list = new List<Transform>();
        for (int i = 0; i < pts.Length; i++)
        {
            var t = new GameObject("P" + i).transform;
            t.SetParent(root.transform, false);
            t.position = new Vector3(pts[i].x, 0f, pts[i].z);
            list.Add(t);
        }

        var route = root.AddComponent<DumpRoute>();
        Set(route, so =>
        {
            var p = so.FindProperty("points");
            p.arraySize = list.Count;
            for (int i = 0; i < list.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
            var r = so.FindProperty("reverseInto");
            r.arraySize = pts.Length;
            for (int i = 0; i < pts.Length; i++) r.GetArrayElementAtIndex(i).boolValue = pts[i].rev;
            so.FindProperty("loadIndex").intValue = 0;
            so.FindProperty("tipIndex").intValue = 3;
            so.FindProperty("holdIndex").intValue = 6;
        });
        return route;
    }

    // ---------- Heavy Crusher, belt, crane, pile ----------

    static Machine HeavyCrusher(Transform content)
    {
        var source = Object.FindObjectsByType<Machine>(FindObjectsInactive.Include).First(m => m.StationId == "crusher" && m.Definition != null && m.Definition.Id == "crusher");
        var go = Object.Instantiate(source.gameObject, content);
        go.name = "HeavyCrusher";
        go.transform.SetPositionAndRotation(CrusherAt, Quaternion.identity);
        go.transform.localScale = Vector3.one * 1.4f;
        var machine = go.GetComponent<Machine>();
        Set(machine, so =>
        {
            so.FindProperty("definition").objectReferenceValue = Load<MachineDefinition>("Factory/Machine_HeavyCrusher.asset");
            so.FindProperty("level").intValue = 1;
            so.FindProperty("labelTitle").stringValue = "HEAVY CRUSHER";
        });
        foreach (var a in go.GetComponentsInChildren<GuideAnchor>(true))
            Set(a, so =>
            {
                var id = so.FindProperty("id");
                id.stringValue = id.stringValue.Replace("crusher", "heavy_crusher");
                so.FindProperty("station").objectReferenceValue = machine;
            });

        // what makes it the heavy one: twin crushing drums in the throat, a caged walkway, warning beacons
        var k = new MeshKit();
        foreach (int side in new[] { -1, 1 })
        {
            k.Cylinder(C(PC.Gray), new Vector3(side * 0.45f, 2.55f, 0.1f), 0.38f, 1.9f, 14, new Vector3(0f, 0f, 90f), 0.03f);
            for (int i = 0; i < 6; i++)
                k.Box(C(PC.Charcoal), new Vector3(side * 0.45f, 2.55f, 0.1f) + Quaternion.Euler(i * 60f, 0f, 0f) * new Vector3(0f, 0.38f, 0f), new Vector3(1.86f, 0.1f, 0.1f), 0.01f,
                    new Vector3(i * 60f, 0f, 0f));
            k.Cylinder(C(PC.Orange), new Vector3(side * 1.55f, 3.55f, -1.1f), 0.12f, 0.2f, 8, default, 0.03f);
            k.Box(C(PC.Yellow), new Vector3(side * 1.45f, 3.0f, 0.1f), new Vector3(0.06f, 0.9f, 2.6f), 0.01f);
        }

        k.Box(C(PC.Yellow), new Vector3(0f, 3.45f, 1.35f), new Vector3(2.96f, 0.06f, 0.06f), 0.01f);
        k.Box(C(PC.Yellow), new Vector3(0f, 3.45f, -1.15f), new Vector3(2.96f, 0.06f, 0.06f), 0.01f);
        ArtAssets.MeshObject("HeavyParts", go.transform, ArtAssets.SaveMesh("F5", "HeavyCrusherParts", k.ToPaletteMesh("F5HeavyCrusherParts")), new[] { ArtPalette.Material });
        Log.AppendLine("Heavy Crusher at (50, 47.4)");
        return machine;
    }

    static Conveyor Belt(Transform content, Machine crusher)
    {
        var template = Object.FindObjectsByType<Conveyor>(FindObjectsInactive.Include).First(c => c.name == "Conveyor_CrusherToStorage");
        var go = new GameObject("Conveyor_HeavyToSplitter");
        go.transform.SetParent(content, false);
        go.transform.position = new Vector3(50f, 0f, 38f);
        var start = new GameObject("Start").transform;
        start.SetParent(go.transform, false);
        start.position = new Vector3(50f, 0.7f, CrusherAt.z - 2.9f);
        var end = new GameObject("End").transform;
        end.SetParent(go.transform, false);
        end.position = new Vector3(50f, 0.7f, 30.6f);
        go.AddComponent<BoxCollider>();
        var mod = go.AddComponent<NavMeshModifier>();
        mod.overrideArea = true;
        mod.area = NavMesh.GetAreaFromName("Not Walkable");
        var conveyor = go.AddComponent<Conveyor>();
        var sorter = Object.FindObjectsByType<Machine>(FindObjectsInactive.Include).First(m => m.StationId == "sorter");
        var t = new SerializedObject(template);
        Set(conveyor, so =>
        {
            var p = so.FindProperty("points");
            p.arraySize = 2;
            p.GetArrayElementAtIndex(0).objectReferenceValue = start;
            p.GetArrayElementAtIndex(1).objectReferenceValue = end;
            so.FindProperty("speed").floatValue = t.FindProperty("speed").floatValue * 1.3f;
            so.FindProperty("spacing").floatValue = t.FindProperty("spacing").floatValue;
            so.FindProperty("destination").objectReferenceValue = sorter;
        });
        return conveyor;
    }

    static void Crane(Transform content, Transform gameplay, Machine crusher)
    {
        var source = Object.FindObjectsByType<ClawCrane>(FindObjectsInactive.Include).First(c => c.UpgradeId == "heavy_crane");
        var go = Object.Instantiate(source.gameObject, content);
        go.name = "DumpCrane";
        go.transform.SetPositionAndRotation(CraneAt, source.transform.rotation);
        var crane = go.GetComponent<ClawCrane>();
        var drop = go.transform.Find("DropPoint");
        drop.position = CrusherAt + new Vector3(0f, 4.2f, 0.3f);
        Set(crane, so =>
        {
            so.FindProperty("definition").objectReferenceValue = Load<ClawCraneDefinition>("Factory/ClawCrane_Dump.asset");
            so.FindProperty("level").intValue = 1;
            so.FindProperty("target").objectReferenceValue = crusher;
            so.FindProperty("dropPoint").objectReferenceValue = drop;
            so.FindProperty("liftWholeScrap").boolValue = false;
            var takes = so.FindProperty("takes");
            takes.arraySize = 1;
            takes.GetArrayElementAtIndex(0).objectReferenceValue = Load<ItemDefinition>("Items/Item_Scrap.asset");
        });
        Log.AppendLine("Dump Yard Crane at (56.5, 51.5)");
    }

    static void Pile(Transform content)
    {
        // a mound that is always there: the trucks tip onto it
        var k = new MeshKit();
        var rng = new System.Random(7);
        PC[] junk = { PC.Rust, PC.RustDark, PC.Gray, PC.SteelBlue, PC.Orange, PC.Charcoal };
        k.Dome(C(PC.BrownDark), new Vector3(48.6f, 0f, 56.2f), new Vector3(2.4f, 0.7f, 2.0f), 14, 5);
        for (int i = 0; i < 22; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = (float)rng.NextDouble() * 1.9f;
            var at = new Vector3(48.6f + Mathf.Cos(a) * r, 0.25f + (1.9f - r) * 0.22f, 56.2f + Mathf.Sin(a) * r * 0.8f);
            k.Box(C(junk[rng.Next(junk.Length)]), at, new Vector3(0.3f + (float)rng.NextDouble() * 0.5f, 0.15f + (float)rng.NextDouble() * 0.25f, 0.3f + (float)rng.NextDouble() * 0.4f),
                0.03f, new Vector3((float)rng.NextDouble() * 40f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 40f));
        }

        ArtAssets.MeshObject("DumpPile", content, ArtAssets.SaveMesh("F5", "DumpPile", k.ToPaletteMesh("F5DumpPile")), new[] { ArtPalette.Material });
    }

    // ---------- the excavator ----------

    static void Excavator(Transform content, DumpRoute route)
    {
        var mats = new[] { ArtPalette.Material };
        var root = new GameObject("Excavator").transform;
        root.SetParent(content, false);
        root.SetPositionAndRotation(ExcavatorAt, Quaternion.Euler(0f, 180f, 0f));

        var tracks = new MeshKit();
        foreach (int side in new[] { -1, 1 })
        {
            tracks.Box(C(PC.Charcoal), new Vector3(side * 1.05f, 0.42f, 0f), new Vector3(0.62f, 0.84f, 3.6f), 0.28f);
            for (int i = 0; i < 5; i++) tracks.Cylinder(C(PC.Gray), new Vector3(side * 1.05f, 0.36f, -1.3f + i * 0.65f), 0.22f, 0.66f, 10, new Vector3(0f, 0f, 90f), 0.02f);
            for (int i = 0; i < 14; i++) tracks.Box(C(PC.Rubber), new Vector3(side * 1.05f, 0.86f, -1.7f + i * 0.26f), new Vector3(0.64f, 0.05f, 0.14f), 0.01f);
        }

        tracks.Box(C(PC.Gray), new Vector3(0f, 0.62f, 0f), new Vector3(1.6f, 0.4f, 2.2f), 0.05f);
        tracks.Cylinder(C(PC.Charcoal), new Vector3(0f, 0.92f, 0f), 0.9f, 0.2f, 18, default, 0.03f);
        ArtAssets.MeshObject("Tracks", root, ArtAssets.SaveMesh("F5", "ExcTracks", tracks.ToPaletteMesh("F5ExcTracks")), mats);

        var upper = new GameObject("Upper").transform;
        upper.SetParent(root, false);
        upper.localPosition = new Vector3(0f, 1.0f, 0f);
        var body = new MeshKit();
        body.Box(C(PC.Yellow), new Vector3(0f, 0.5f, -0.3f), new Vector3(2.3f, 0.9f, 2.8f), 0.14f);
        body.Box(C(PC.Charcoal), new Vector3(0f, 0.55f, -1.75f), new Vector3(2.3f, 0.95f, 0.5f), 0.18f);     // counterweight
        body.Box(C(PC.Yellow), new Vector3(-0.55f, 1.55f, 0.55f), new Vector3(1.0f, 1.25f, 1.2f), 0.1f);     // cab
        body.Box(C(PC.Glass), new Vector3(-0.55f, 1.65f, 1.16f), new Vector3(0.84f, 0.8f, 0.04f), 0.02f);
        body.Box(C(PC.Glass), new Vector3(-1.06f, 1.65f, 0.55f), new Vector3(0.04f, 0.8f, 0.9f), 0.02f);
        body.Box(C(PC.Charcoal), new Vector3(-0.55f, 2.2f, 0.55f), new Vector3(1.04f, 0.06f, 1.24f), 0.02f);
        body.Cylinder(C(PC.Orange), new Vector3(-0.55f, 2.32f, 0.55f), 0.1f, 0.16f, 8, default, 0.03f);
        body.Box(C(PC.Charcoal), new Vector3(0.6f, 1.05f, -0.6f), new Vector3(0.7f, 0.25f, 1.0f), 0.03f);     // engine hood
        body.Cylinder(C(PC.Silver), new Vector3(0.75f, 1.5f, -1.0f), 0.07f, 0.7f, 8, default, 0.02f);       // exhaust
        for (int i = 0; i < 6; i++) body.Box(C(i % 2 == 0 ? PC.Yellow : PC.Charcoal), new Vector3(-0.95f + i * 0.38f, 0.3f, -2.01f), new Vector3(0.38f, 0.2f, 0.02f), 0f);
        ArtAssets.MeshObject("Body", upper, ArtAssets.SaveMesh("F5", "ExcBody", body.ToPaletteMesh("F5ExcBody")), mats);

        const float boomLen = 3.6f, stickLen = 2.6f;
        var boom = new GameObject("Boom").transform;
        boom.SetParent(upper, false);
        boom.localPosition = new Vector3(0.45f, 0.9f, 0.9f);
        var b = new MeshKit();
        b.Box(C(PC.Yellow), new Vector3(0f, 0f, boomLen * 0.5f), new Vector3(0.42f, 0.5f, boomLen), 0.08f);
        b.Cylinder(C(PC.Silver), new Vector3(0f, -0.35f, boomLen * 0.45f), 0.08f, boomLen * 0.7f, 8, new Vector3(90f, 0f, 0f), 0.01f);   // hydraulic ram
        b.Cylinder(C(PC.Charcoal), Vector3.zero, 0.18f, 0.5f, 10, new Vector3(0f, 0f, 90f), 0.01f);
        ArtAssets.MeshObject("BoomMesh", boom, ArtAssets.SaveMesh("F5", "ExcBoom", b.ToPaletteMesh("F5ExcBoom")), mats);

        var stick = new GameObject("Stick").transform;
        stick.SetParent(boom, false);
        stick.localPosition = new Vector3(0f, 0f, boomLen);
        var s = new MeshKit();
        s.Box(C(PC.Yellow), new Vector3(0f, 0f, stickLen * 0.5f), new Vector3(0.32f, 0.38f, stickLen), 0.06f);
        s.Cylinder(C(PC.Silver), new Vector3(0f, 0.28f, stickLen * 0.4f), 0.06f, stickLen * 0.6f, 8, new Vector3(90f, 0f, 0f), 0.01f);
        s.Cylinder(C(PC.Charcoal), Vector3.zero, 0.15f, 0.42f, 10, new Vector3(0f, 0f, 90f), 0.01f);
        ArtAssets.MeshObject("StickMesh", stick, ArtAssets.SaveMesh("F5", "ExcStick", s.ToPaletteMesh("F5ExcStick")), mats);

        var bucket = new GameObject("Bucket").transform;
        bucket.SetParent(stick, false);
        bucket.localPosition = new Vector3(0f, 0f, stickLen);
        var k = new MeshKit();
        // a scoop opening along +Z, hanging below the pivot
        k.Box(C(PC.Charcoal), new Vector3(0f, -0.45f, 0.15f), new Vector3(0.9f, 0.08f, 0.8f), 0.02f);
        k.Box(C(PC.Charcoal), new Vector3(0f, -0.15f, -0.22f), new Vector3(0.9f, 0.62f, 0.08f), 0.02f);
        foreach (int side in new[] { -1, 1 }) k.Box(C(PC.Gray), new Vector3(side * 0.45f, -0.2f, 0.1f), new Vector3(0.06f, 0.6f, 0.75f), 0.02f);
        for (int i = 0; i < 5; i++) k.Box(C(PC.Silver), new Vector3(-0.36f + i * 0.18f, -0.47f, 0.6f), new Vector3(0.08f, 0.06f, 0.16f), 0.01f);
        ArtAssets.MeshObject("BucketMesh", bucket, ArtAssets.SaveMesh("F5", "ExcBucket", k.ToPaletteMesh("F5ExcBucket")), mats);
        var hold = new GameObject("Hold").transform;
        hold.SetParent(bucket, false);
        hold.localPosition = new Vector3(0f, -0.25f, 0.1f);

        var exc = root.gameObject.AddComponent<Excavator>();
        Set(exc, so =>
        {
            so.FindProperty("route").objectReferenceValue = route;
            so.FindProperty("upper").objectReferenceValue = upper;
            so.FindProperty("boom").objectReferenceValue = boom;
            so.FindProperty("stick").objectReferenceValue = stick;
            so.FindProperty("bucket").objectReferenceValue = bucket;
            so.FindProperty("hold").objectReferenceValue = hold;
            so.FindProperty("boomLength").floatValue = boomLen;
            so.FindProperty("stickLength").floatValue = stickLen;
            so.FindProperty("scoopSfx").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SfxDefinition>(DataDir + "/Audio/Sfx_Pickup.asset");
            so.FindProperty("dumpSfx").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SfxDefinition>(DataDir + "/Audio/Sfx_TruckGate.asset");
        });
        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 1.2f, 0f);
        box.size = new Vector3(2.8f, 2.4f, 3.6f);
        Log.AppendLine("excavator in the bay at (32.9, 56.4)");
    }

    // ---------- the dump trucks ----------

    static void DumpTruck(Transform content, DumpRoute route, string name, int startIndex, PC body)
    {
        var mats = new[] { ArtPalette.Material };
        var root = new GameObject(name).transform;
        root.SetParent(content, false);
        var chassis = new GameObject("Body").transform;
        chassis.SetParent(root, false);

        var h = new MeshKit();
        h.Box(C(PC.Charcoal), new Vector3(0f, 0.72f, 0f), new Vector3(1.3f, 0.26f, 5.6f), 0.04f);
        var shell = new[] { new Vector2(1.6f, 0.86f), new Vector2(3.0f, 0.86f), new Vector2(3.02f, 1.6f), new Vector2(2.82f, 2.4f), new Vector2(2.56f, 2.9f), new Vector2(1.6f, 2.9f) };
        h.Prism(C(body), Vector3.zero, shell, 2.44f, 0.14f);
        h.Box(C(PC.Glass), new Vector3(0f, 2.62f, 2.71f), new Vector3(2.0f, 0.55f, 0.05f), 0.03f, new Vector3(-26f, 0f, 0f));
        foreach (int side in new[] { -1, 1 })
        {
            h.Box(C(PC.Glass), new Vector3(side * 1.225f, 2.36f, 2.15f), new Vector3(0.04f, 0.6f, 0.86f), 0.02f);
            h.Box(C(PC.Cream), new Vector3(side * 0.85f, 1.14f, 3.03f), new Vector3(0.4f, 0.22f, 0.05f), 0.03f);
            h.Box(C(PC.Charcoal), new Vector3(side * 1.45f, 2.25f, 2.6f), new Vector3(0.1f, 0.42f, 0.2f), 0.03f);
            h.Box(C(body), new Vector3(side * 1.08f, 1.14f, 2.1f), new Vector3(0.48f, 0.1f, 1.3f), 0.05f);
            h.Box(C(PC.Charcoal), new Vector3(side * 1.08f, 1.14f, -1.4f), new Vector3(0.5f, 0.08f, 2.2f), 0.03f);
            h.Box(C(PC.Rubber), new Vector3(side * 1.08f, 0.56f, -2.65f), new Vector3(0.46f, 0.5f, 0.04f), 0.01f);
            h.Cylinder(C(PC.Silver), new Vector3(side * 1.05f, 2.2f, 1.45f), 0.08f, 2.0f, 8, default, 0.02f);
        }

        h.Box(C(PC.Charcoal), new Vector3(0f, 1.25f, 3.04f), new Vector3(1.28f, 0.5f, 0.05f), 0.03f);
        for (int i = 0; i < 4; i++) h.Box(C(PC.Silver), new Vector3(0f, 1.06f + i * 0.12f, 3.07f), new Vector3(1.16f, 0.035f, 0.02f), 0.005f);
        h.Box(C(PC.Silver), new Vector3(0f, 0.68f, 3.1f), new Vector3(2.5f, 0.3f, 0.24f), 0.08f);
        h.Box(C(PC.Charcoal), new Vector3(0f, 1.05f, -0.6f), new Vector3(2.0f, 0.18f, 3.8f), 0.03f);    // sub-frame under the body
        h.Cylinder(C(PC.Silver), new Vector3(0f, 1.3f, 0.95f), 0.1f, 0.6f, 8, default, 0.02f);           // tipping ram
        ArtAssets.MeshObject("Hull", chassis, ArtAssets.SaveMesh("F5", name + "Hull", h.ToPaletteMesh("F5" + name + "Hull")), mats);

        var beacon = new GameObject("Beacon").transform;
        beacon.SetParent(chassis, false);
        beacon.localPosition = new Vector3(0f, 2.95f, 2.0f);
        var bk = new MeshKit();
        bk.Cylinder(C(PC.Orange), new Vector3(0f, 0.12f, 0f), 0.12f, 0.22f, 10, default, 0.04f);
        bk.Box(C(PC.White), new Vector3(0.07f, 0.12f, 0f), new Vector3(0.05f, 0.14f, 0.1f), 0.01f);
        ArtAssets.MeshObject("Lamp", beacon, ArtAssets.SaveMesh("F5", name + "Beacon", bk.ToPaletteMesh("F5" + name + "Beacon")), mats);

        // the dump body hinges at its back bottom edge
        var tipper = new GameObject("Tipper").transform;
        tipper.SetParent(chassis, false);
        tipper.localPosition = new Vector3(0f, 1.15f, -2.55f);
        var d = new MeshKit();
        const float len = 3.9f;
        d.Box(C(body), new Vector3(0f, 0.05f, len * 0.5f), new Vector3(2.4f, 0.12f, len), 0.03f);
        foreach (int side in new[] { -1, 1 })
        {
            d.Box(C(body), new Vector3(side * 1.18f, 0.55f, len * 0.5f), new Vector3(0.08f, 1.0f, len), 0.03f);
            for (int i = 0; i < 4; i++) d.Box(C(PC.Charcoal), new Vector3(side * 1.23f, 0.55f, 0.4f + i * 1.0f), new Vector3(0.04f, 0.96f, 0.12f), 0.01f);
        }

        d.Box(C(body), new Vector3(0f, 0.7f, len - 0.04f), new Vector3(2.4f, 1.3f, 0.1f), 0.03f);          // front wall
        d.Box(C(body), new Vector3(0f, 1.38f, len + 0.3f), new Vector3(2.4f, 0.08f, 0.7f), 0.02f);          // cab guard
        d.Box(C(PC.Yellow), new Vector3(0f, 0.2f, -0.02f), new Vector3(2.4f, 0.16f, 0.06f), 0.01f);
        ArtAssets.MeshObject("DumpBody", tipper, ArtAssets.SaveMesh("F5", name + "Dump", d.ToPaletteMesh("F5" + name + "Dump")), mats);
        var bedGo = new GameObject("Bed").transform;
        bedGo.SetParent(tipper, false);
        bedGo.localPosition = new Vector3(0f, 0.15f, len * 0.5f);
        var bed = bedGo.gameObject.AddComponent<ItemPile>();
        Set(bed, so =>
        {
            so.FindProperty("capacity").intValue = 20;
            so.FindProperty("columns").intValue = 4;
            so.FindProperty("rows").intValue = 5;
            so.FindProperty("cellSize").vector3Value = new Vector3(0.52f, 0.26f, 0.68f);
            so.FindProperty("arriveDuration").floatValue = 0.35f;
            so.FindProperty("arriveArc").floatValue = 1.4f;
            so.FindProperty("yawJitter").floatValue = 25f;
        });

        var pour = new GameObject("PourPoint").transform;
        pour.SetParent(root, false);
        pour.localPosition = new Vector3(0f, 0.6f, -3.6f);

        var w = new MeshKit();
        w.Cylinder(C(PC.Rubber), Vector3.zero, 0.56f, 0.46f, 18, new Vector3(0f, 0f, 90f), 0.08f);
        for (int i = 0; i < 14; i++)
        {
            float a = i * 360f / 14f;
            w.Box(C(PC.Charcoal), Quaternion.Euler(a, 0f, 0f) * new Vector3(0f, 0.555f, 0f), new Vector3(0.44f, 0.05f, 0.1f), 0.01f, new Vector3(a, 0f, 0f));
        }

        w.Cylinder(C(PC.Silver), Vector3.zero, 0.3f, 0.48f, 12, new Vector3(0f, 0f, 90f), 0.02f);
        var wheelMesh = ArtAssets.SaveMesh("F5", "DumpWheel", w.ToPaletteMesh("F5DumpWheel"));
        var wheels = new List<Transform>();
        foreach (int side in new[] { -1, 1 })
            foreach (float z in new[] { 2.1f, -1.0f, -2.2f })
                wheels.Add(ArtAssets.MeshObject("Wheel", root, wheelMesh, mats, new Vector3(side * 1.08f, 0.56f, z)).transform);

        var spin = root.gameObject.AddComponent<VehicleWheels>();
        Set(spin, so =>
        {
            var p = so.FindProperty("wheels");
            p.arraySize = wheels.Count;
            for (int i = 0; i < wheels.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = wheels[i];
            so.FindProperty("radius").floatValue = 0.56f;
            so.FindProperty("body").objectReferenceValue = chassis;
        });

        var truck = root.gameObject.AddComponent<DumpTruck>();
        Set(truck, so =>
        {
            so.FindProperty("route").objectReferenceValue = route;
            so.FindProperty("startIndex").intValue = startIndex;
            so.FindProperty("bed").objectReferenceValue = bed;
            var takes = so.FindProperty("takes");
            takes.arraySize = 1;
            takes.GetArrayElementAtIndex(0).objectReferenceValue = Load<ItemDefinition>("Items/Item_Scrap.asset");
            so.FindProperty("tipper").objectReferenceValue = tipper;
            so.FindProperty("pourPoint").objectReferenceValue = pour;
            so.FindProperty("beacon").objectReferenceValue = beacon;
            so.FindProperty("hornSfx").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SfxDefinition>(DataDir + "/Audio/Sfx_TruckHorn.asset");
            so.FindProperty("tipSfx").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SfxDefinition>(DataDir + "/Audio/Sfx_TruckGate.asset");
        });
        root.position = route.Point(startIndex);
    }

    // ================================================================== helpers

    static void Wall(Transform parent, string name, Vector3 from, Vector3 to, int seed)
    {
        var segment = new GameObject(name).transform;
        segment.SetParent(parent, false);
        Vector3 delta = to - from;
        float length = delta.magnitude;
        segment.position = from;
        segment.rotation = Quaternion.LookRotation(delta.normalized) * Quaternion.Euler(0f, -90f, 0f);
        var box = segment.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(length * 0.5f, 1.25f, 0f);
        box.size = new Vector3(length, 2.5f, 0.4f);
        var go = ArtAssets.MeshObject("Sheets", segment, ArtAssets.SaveMesh("Walls", name, CorrugatedWall(length, seed).ToPaletteMesh(name)), new[] { ArtPalette.Material });
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    /// <summary>Same sheets as M8_Build / Art_World.CorrugatedWall.</summary>
    static MeshKit CorrugatedWall(float length, int seed)
    {
        var rng = new System.Random(seed);
        PC[] colors = { PC.SteelBlue, PC.Gray, PC.Teal, PC.GrayLight, PC.SteelBlue, PC.RustDark, PC.Blue, PC.Cream };
        var k = new MeshKit();
        int panels = Mathf.Max(1, Mathf.CeilToInt(length / 2f));
        float w = length / panels;
        for (int i = 0; i < panels; i++)
        {
            var c = colors[rng.Next(colors.Length)];
            float x = (i + 0.5f) * w;
            float tilt = (float)(rng.NextDouble() - 0.5) * 3f, lean = (float)(rng.NextDouble() - 0.5) * 2f;
            float h = 2.25f + (float)rng.NextDouble() * 0.15f;
            k.Push(new Vector3(x, 0f, 0f), new Vector3(lean, 0f, tilt));
            k.Box(C(c), new Vector3(0f, h * 0.5f, 0f), new Vector3(w - 0.04f, h, 0.05f), 0f);
            for (float rx = -w * 0.5f + 0.16f; rx < w * 0.5f - 0.05f; rx += 0.32f)
                for (int side = -1; side <= 1; side += 2)
                    k.Box(C(c), new Vector3(rx, h * 0.5f, side * 0.045f), new Vector3(0.11f, h - 0.04f, 0.05f), 0f);
            if (rng.NextDouble() < 0.35)
                k.Box(C(PC.Rust), new Vector3((float)(rng.NextDouble() - 0.5) * w * 0.6f, 0.4f + (float)rng.NextDouble() * 0.6f, -0.08f), new Vector3(0.5f, 0.4f, 0.03f), 0.01f);
            k.Pop();
            k.Box(C(PC.Charcoal), new Vector3(i * w, 1.25f, 0f), new Vector3(0.16f, 2.5f, 0.16f), 0.03f);
        }

        k.Box(C(PC.Charcoal), new Vector3(length, 1.25f, 0f), new Vector3(0.16f, 2.5f, 0.16f), 0.03f);
        k.Box(C(PC.Yellow), new Vector3(length * 0.5f, 2.45f, 0f), new Vector3(length, 0.08f, 0.14f), 0.02f);
        return k;
    }

    static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
    static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>($"{DataDir}/{path}");

    static T Asset<T>(string path) where T : ScriptableObject
    {
        var x = Load<T>(path);
        if (x != null) return x;
        x = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(x, $"{DataDir}/{path}");
        return x;
    }

    static T CopyAsset<T>(string from, string to) where T : Object
    {
        var x = Load<T>(to);
        if (x != null) return x;
        AssetDatabase.CopyAsset($"{DataDir}/{from}", $"{DataDir}/{to}");
        return Load<T>(to);
    }

    static void Set(Object target, System.Action<SerializedObject> edit)
    {
        if (target == null) { Log.AppendLine("!! missing target"); return; }
        var so = new SerializedObject(target);
        edit(so);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}

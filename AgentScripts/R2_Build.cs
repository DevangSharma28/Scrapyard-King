using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScrapYardKing.Customers;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Factory;
using ScrapYardKing.Feedback;
using ScrapYardKing.Items;
using ScrapYardKing.Progression;
using ScrapYardKing.Tiles;
using ScrapYardKing.UI;
using ScrapYardKing.Workers;
using ScrapYardKing.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using static ScrapYardKing.EditorTools.Art.ArtMaterials;

// Revamp 2 builder: the furnace battery. The one two-recipe Furnace is retired; four furnaces, one per metal, stand side
// by side along the Furnace hall's north wall and feed one collector belt to the ingot rack.
//  - Iron Furnace: comes with the hall (id `furnace`, expansion `furnace_hall`: ids outlive names).
//  - Copper, Aluminum, Steel: each its own purchase on a tile in its slot (an Expansion inside the hall: camera pan,
//    the furnace pops in), unlocked one yard level after the other.
//  - Refined metals: iron, aluminum, copper and steel ingots, in the metal colours used since the Splitter.
//  - One Smelter serves all four (PorterRoute.extraDropoffs), one boost pad and one operator run the whole battery.
// Entry points, in order: Assets, Prefabs, then Art_Machines.Prefabs (the four models), Icons, Scene, then
// Art_Machines.Scene, Crane_Build.Scene, Env_Build.All, R1_Build.Bake, UI_Build.Apply. Incremental and idempotent.
public static class R2_Build
{
    const string P = "Assets/_Project";
    const string DataDir = P + "/Data";
    const string PrefabDir = P + "/Prefabs";
    const string ScenePath = P + "/Scenes/Area1_OldScrapYard.unity";

    // West → east along the hall's north wall. The order is the unlock order; the steel furnace is the widest.
    // (key, input item, ingot item, machine asset, machine id, prefab, short label, slot x, body width)
    static readonly (string key, string input, string ingot, string asset, string id, string prefab, string label, float x, float width)[] Furnaces =
    {
        ("Iron", "Iron", "IronIngot", "Machine_Furnace", "furnace", "Furnace", "IRON", 63.9f, 2.8f),
        ("Copper", "Copper", "CopperIngot", "Machine_FurnaceCopper", "furnace_copper", "Furnace_Copper", "COPPER", 67.1f, 2.8f),
        ("Aluminum", "Aluminum", "AluminumIngot", "Machine_FurnaceAluminum", "furnace_aluminum", "Furnace_Aluminum", "ALUMINUM", 70.3f, 2.8f),
        ("Steel", "Steel", "SteelIngot", "Machine_FurnaceSteel", "furnace_steel", "Furnace_Steel", "STEEL", 73.75f, 3.3f),
    };

    // ---------- hall layout (blueprint frame) ----------
    const float FurnaceZ = 38.6f, PadZ = 33.6f, BeltZ = 36.05f, DownX = 62.2f;
    static readonly Vector3 RackPos = new(63.3f, 0f, 28.4f);
    static readonly Vector3 OperatorPos = new(68.7f, 0f, 30.9f);

    static readonly StringBuilder Log = new();

    static string BuildId(string key) => "build_furnace_" + key.ToLowerInvariant();

    // =====================================================================================
    // ASSETS
    // =====================================================================================

    public static string Assets()
    {
        Log.Clear();
        AssetDatabase.Refresh();

        // Refined metals. An ingot is worth about 2.5 times its raw metal.
        var ingotColors = new Dictionary<string, Color>
        {
            ["IronIngot"] = new(0.72f, 0.78f, 0.88f), ["AluminumIngot"] = new(0.78f, 0.9f, 1f), ["CopperIngot"] = new(1f, 0.56f, 0.22f), ["SteelIngot"] = new(0.36f, 0.44f, 0.64f),
        };
        (string name, string id, string display, int value)[] ingots =
        {
            ("IronIngot", "iron_ingot", "Iron Ingot", 26), ("AluminumIngot", "aluminum_ingot", "Aluminum Ingot", 40), ("CopperIngot", "copper_ingot", "Copper Ingot", 60),
            ("SteelIngot", "steel_ingot", "Steel Ingot", 90),
        };
        foreach (var (name, id, display, value) in ingots)
            ArtAssets.Set(ArtAssets.LoadOrCreate<ItemDefinition>($"{DataDir}/Items/Item_{name}.asset"), ("id", id), ("displayName", display), ("color", ingotColors[name]),
                ("stackHeight", 0.2f), ("baseValue", value));

        // Sounds: the same clips, pitched by character. Copper rings higher, aluminum is the lightest, steel is slow and low.
        var cyclePitch = new Dictionary<string, (Vector2 pitch, float volume)>
        {
            ["Copper"] = (new Vector2(0.86f, 0.98f), 0.5f), ["Aluminum"] = (new Vector2(1.04f, 1.18f), 0.42f), ["Steel"] = (new Vector2(0.5f, 0.58f), 0.62f),
        };
        foreach (var kv in cyclePitch)
        {
            string path = $"{DataDir}/Audio/Sfx_FurnaceCycle_{kv.Key}.asset";
            if (Load<SfxDefinition>(path, false) == null) AssetDatabase.CopyAsset(DataDir + "/Audio/Sfx_FurnaceCycle.asset", path);
            ArtAssets.Set(Load<SfxDefinition>(path), ("pitchRange", kv.Value.pitch), ("volume", kv.Value.volume));
        }

        // Machines: (key → hopper sizes, seconds per ingot, upgrade costs).
        var stats = new Dictionary<string, (int[] capacity, float[] cycle, long[] cost)>
        {
            ["Iron"] = (new[] { 16, 20, 26, 32, 40 }, new[] { 0.9f, 0.76f, 0.63f, 0.52f, 0.43f }, new long[] { 0, 1000, 2200, 4200, 7500 }),
            ["Copper"] = (new[] { 14, 18, 24, 30, 38 }, new[] { 1.1f, 0.93f, 0.78f, 0.65f, 0.54f }, new long[] { 0, 1600, 3400, 6400, 11000 }),
            ["Aluminum"] = (new[] { 14, 18, 24, 30, 38 }, new[] { 1f, 0.85f, 0.71f, 0.59f, 0.49f }, new long[] { 0, 2200, 4600, 8600, 15000 }),
            ["Steel"] = (new[] { 12, 16, 22, 28, 36 }, new[] { 1.5f, 1.27f, 1.06f, 0.88f, 0.73f }, new long[] { 0, 3200, 6800, 12500, 22000 }),
        };
        foreach (var f in Furnaces)
        {
            var def = ArtAssets.LoadOrCreate<MachineDefinition>($"{DataDir}/Factory/{f.asset}.asset");
            ArtAssets.Set(def, ("id", f.id), ("displayName", f.key + " Furnace"), ("input", Item(f.input)), ("output", Item(f.ingot)),
                ("cycleSfx", Load<SfxDefinition>($"{DataDir}/Audio/Sfx_FurnaceCycle{(f.key == "Iron" ? "" : "_" + f.key)}.asset")),
                ("outputSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_IngotOut.asset")));
            var so = new SerializedObject(def);
            so.FindProperty("recipes").arraySize = 0;       // one metal per furnace: the two-recipe Furnace is gone
            so.FindProperty("outputMix").arraySize = 0;
            var levels = so.FindProperty("levels");
            var s = stats[f.key];
            levels.arraySize = s.cycle.Length;
            for (int i = 0; i < s.cycle.Length; i++)
            {
                var e = levels.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("inputCapacity").intValue = s.capacity[i];
                e.FindPropertyRelative("cycleTime").floatValue = s.cycle[i];
                e.FindPropertyRelative("inputsPerCycle").intValue = 1;
                e.FindPropertyRelative("outputsPerCycle").intValue = 1;
                e.FindPropertyRelative("upgradeCost").longValue = s.cost[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
        }

        // Purchases: the hall (with the Iron Furnace) and one build per further furnace.
        var hall = Load<ExpansionDefinition>(DataDir + "/World/Expansion_FurnaceHall.asset");
        ArtAssets.Set(hall, ("displayName", "Iron Furnace"), ("teaser", "IRON INGOTS"));
        SetLong(hall, "cost", 2000);
        (string key, long cost, int level, string teaser)[] builds = { ("Copper", 3500, 7, "COPPER INGOTS"), ("Aluminum", 6000, 8, "ALUMINUM INGOTS"), ("Steel", 10000, 9, "STEEL INGOTS") };
        foreach (var (key, cost, _, teaser) in builds)
        {
            var e = ArtAssets.LoadOrCreate<ExpansionDefinition>($"{DataDir}/World/Expansion_Furnace{key}.asset");
            ArtAssets.Set(e, ("id", BuildId(key)), ("displayName", key + " Furnace"), ("teaser", teaser), ("openSfx", new SerializedObject(hall).FindProperty("openSfx").objectReferenceValue));
            SetLong(e, "cost", cost);
        }

        var catalog = new List<(string id, int level, bool inPanel, string group)>
        {
            ("furnace_hall", 6, false, ""), ("furnace", 6, true, "FURNACES"), ("ingot_rack", 6, true, "FURNACES"), ("smelter", 6, false, ""),
        };
        foreach (var (key, _, level, _) in builds)
        {
            catalog.Add((BuildId(key), level, false, ""));
            catalog.Add(("furnace_" + key.ToLowerInvariant(), level, true, "FURNACES"));
        }

        UpsertCatalog(catalog);

        // The rack holds all four ingots now; the market and the truck take them.
        // One rack for four streams: a full rack stops the whole battery (seen in the first test at 40), so it starts roomy.
        SetColumn(Load<StorageDefinition>(DataDir + "/Factory/Storage_IngotRack.asset"), "capacity", 60, 100, 150, 220);
        var sells = new[] { "Iron", "Aluminum", "Copper", "Steel", "IronIngot", "AluminumIngot", "CopperIngot", "SteelIngot" }.Select(n => (Object)Item(n)).ToArray();
        ArtAssets.SetArray(Load<SellDeskDefinition>(DataDir + "/Factory/SellDesk_Market.asset"), "sells", sells);
        // Ingots are what the market mostly asks for once they exist (customers only order what the yard can make).
        float[] weights = { 0.12f, 0.1f, 0.1f, 0.08f, 0.2f, 0.15f, 0.15f, 0.1f };
        var orders = new SerializedObject(Load<CustomerConfig>(DataDir + "/Customers/CustomerConfig_Market.asset"));
        var options = orders.FindProperty("orderOptions");
        options.arraySize = sells.Length;
        for (int i = 0; i < sells.Length; i++)
        {
            options.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue = sells[i];
            options.GetArrayElementAtIndex(i).FindPropertyRelative("weight").floatValue = weights[i];
        }

        orders.ApplyModifiedPropertiesWithoutUndo();
        ArtAssets.SetArray(Load<TruckBayDefinition>(DataDir + "/Factory/TruckBay_Plant.asset"), "accepts", sells.Skip(4).ToArray());

        Balance();
        Tasks();
        AssetDatabase.SaveAssets();
        Log.AppendLine("R2 data: 4 ingots, 4 furnace definitions, 3 builds, catalog, market, truck, tasks");
        return Log.ToString();
    }

    /// <summary>
    /// Numbers that move because of the battery. The hall used to come at yard Lv 8 for $5,000 (46 minutes in the old
    /// run); the target is the first furnace around minute 13–16, the second and third by 25, all four by 35.
    /// Not measured yet: R7 measures the whole chain in one clean run.
    /// </summary>
    static void Balance()
    {
        // One Smelter carries for every furnace, so it comes with the first one and costs less.
        var smelter = Load<WorkerDefinition>(DataDir + "/Workers/Worker_Smelter.asset");
        var so = new SerializedObject(smelter);
        var costs = so.FindProperty("hireCosts");
        costs.arraySize = 2;
        costs.GetArrayElementAtIndex(0).longValue = 1500;
        costs.GetArrayElementAtIndex(1).longValue = 4000;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(smelter);
        // Levels 7 to 9 gate the three later furnaces: a gentler curve so a level is a few minutes, not ten.
        // (The level curve is R7_Build.Balance's now.)
    }

    static void Tasks()
    {
        string dir = DataDir + "/Progression/Tasks";
        var chain = Load<TaskChain>(DataDir + "/Progression/TaskChain_Area1.asset");
        var so = new SerializedObject(chain);
        var main = so.FindProperty("mainTasks");
        var list = new List<Object>();
        for (int i = 0; i < main.arraySize; i++) list.Add(main.GetArrayElementAtIndex(i).objectReferenceValue);
        list.RemoveAll(t => t == null || (t is TaskDefinition d && d.Id.StartsWith("r2_")));

        TaskDefinition Task(string id, string title, TaskType type, string target, int amount, int cash, int xp)
        {
            var t = ArtAssets.LoadOrCreate<TaskDefinition>($"{dir}/Task_{id}.asset");
            ArtAssets.Set(t, ("id", id), ("title", title), ("category", (int)TaskCategory.Main), ("type", (int)type), ("targetId", target), ("amount", amount), ("rewardCash", cash),
                ("rewardXp", xp), ("rewardPremium", 0));
            return t;
        }

        void InsertAfter(string afterId, params TaskDefinition[] tasks)
        {
            int at = list.FindIndex(t => t is TaskDefinition d && d.Id == afterId);
            if (at < 0)
            {
                Log.AppendLine("!! chain has no " + afterId);
                return;
            }

            list.InsertRange(at + 1, tasks);
        }

        // The hall is a Lv 6 purchase now.
        ArtAssets.Set(Load<TaskDefinition>($"{dir}/Task_t26_level8.asset"), ("amount", 6));
        ArtAssets.Set(Load<TaskDefinition>($"{dir}/Task_t28_furnace.asset"), ("title", "Build the Iron Furnace"));
        ArtAssets.Set(Load<TaskDefinition>($"{dir}/Task_t29_smelt.asset"), ("title", "Smelt {0} iron ingots"));
        ArtAssets.Set(Load<TaskDefinition>($"{dir}/Task_t31_boost_furnace.asset"), ("title", "Boost the furnaces"));
        // Run r2_run1: "Smelt 12 iron ingots" took ten minutes. The player alone carried iron to the furnace, a piece or two
        // at a time, because the Market Runner kept emptying the same bin. The Smelter hire now comes straight after the
        // furnace, before the first smelting task.
        var smelterTask = list.FirstOrDefault(t => t is TaskDefinition d && d.Id == "t32_smelter");
        if (smelterTask != null)
        {
            list.Remove(smelterTask);
            list.Insert(list.FindIndex(t => t is TaskDefinition d && d.Id == "t28_furnace") + 1, smelterTask);
        }

        InsertAfter("t31_boost_furnace",
            Task("r2_level7", "Reach yard Lv.{0}", TaskType.ReachLevel, "", 7, 300, 0),
            Task("r2_copper_furnace", "Build the Copper Furnace", TaskType.ReachUpgradeLevel, BuildId("Copper"), 1, 0, 150),
            Task("r2_smelt_copper", "Smelt {0} copper ingots", TaskType.ProcessItems, "furnace_copper", 15, 500, 80),
            Task("r2_level8", "Reach yard Lv.{0}", TaskType.ReachLevel, "", 8, 400, 0),
            Task("r2_aluminum_furnace", "Build the Aluminum Furnace", TaskType.ReachUpgradeLevel, BuildId("Aluminum"), 1, 0, 180),
            Task("r2_smelt_aluminum", "Smelt {0} aluminum ingots", TaskType.ProcessItems, "furnace_aluminum", 15, 700, 100));
        InsertAfter("t36_copper_ingots",
            Task("r2_steel_furnace", "Build the Steel Furnace", TaskType.ReachUpgradeLevel, BuildId("Steel"), 1, 0, 220),
            Task("r2_smelt_steel", "Smelt {0} steel ingots", TaskType.ProcessItems, "furnace_steel", 12, 1200, 140));
        ArtAssets.SetArray(chain, "mainTasks", list.ToArray());
        Log.AppendLine($"tasks: chain has {list.Count}");
    }

    // =====================================================================================
    // PREFABS
    // =====================================================================================

    public static string Prefabs()
    {
        Log.Clear();
        // The two new ingots: the iron ingot's prefab and mesh in their own metal.
        Ingot("AluminumIngot", new Color(0.78f, 0.9f, 1f), new Color(0.9f, 0.96f, 1f));
        Ingot("SteelIngot", new Color(0.32f, 0.4f, 0.6f), new Color(0.46f, 0.55f, 0.76f));

        foreach (var f in Furnaces)
        {
            string path = $"{PrefabDir}/Stations/{f.prefab}.prefab";
            if (Load<GameObject>(path, false) == null) AssetDatabase.CopyAsset(PrefabDir + "/Stations/Furnace.prefab", path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.name = f.prefab;
                var machine = root.GetComponent<Machine>();
                float hw = f.width * 0.5f;
                var body = root.transform.Find("Body");
                var output = root.transform.Find("OutputPoint");
                output.localPosition = new Vector3(0.85f, 0.7f, -1.9f);
                var sparks = root.transform.Find("VFX_IngotSparks");
                if (sparks != null) sparks.localPosition = new Vector3(0.85f, 0.95f, -1.8f);
                body.Find("Intake").localPosition = new Vector3(-hw + 0.62f, 1.6f, -1.4f);
                var pile = body.Find("HopperPile");
                pile.localPosition = new Vector3(-hw + 0.62f, 1.15f, -1.4f);
                ArtAssets.Set(pile.GetComponent<ItemPile>(), ("columns", 2), ("rows", 2), ("cellSize", new Vector3(0.42f, 0.3f, 0.34f)));
                ArtAssets.Set(machine, ("definition", Load<MachineDefinition>($"{DataDir}/Factory/{f.asset}.asset")), ("labelTitle", f.label), ("outputPoint", output));

                // Input pad in front of the furnace, across the collector belt, with the metal's icon on it.
                var pad = root.transform.Find("InputPad");
                pad.localPosition = new Vector3(0f, 0f, PadZ - FurnaceZ);
                pad.localRotation = Quaternion.Euler(0f, -90f, 0f);
                var icon = root.transform.Find("PadIcon");
                if (icon == null)
                {
                    icon = new GameObject("PadIcon").transform;
                    icon.SetParent(root.transform, false);
                    icon.gameObject.AddComponent<SpriteRenderer>();
                }

                icon.localPosition = new Vector3(0f, 0.08f, PadZ - FurnaceZ - 1.55f);
                icon.localRotation = Quaternion.Euler(90f, 0f, 0f);
                icon.localScale = Vector3.one * 0.16f;
                icon.GetComponent<SpriteRenderer>().sprite = Item(f.input).Icon;

                if (root.TryGetComponent(out BoxCollider box))
                {
                    box.center = new Vector3(0f, 1.3f, 0.05f);
                    box.size = new Vector3(f.width + 0.2f, 2.6f, 3.1f);
                }

                var label = root.transform.Find("StationLabel");
                if (label != null)
                {
                    label.localPosition = new Vector3(0f, 4.6f, -1.2f);
                    label.localScale = Vector3.one * 0.74f;
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        Log.AppendLine("ingot prefabs and four furnace prefabs. Next: Art_Machines.Prefabs, R2_Build.Icons.");
        return Log.ToString();
    }

    static void Ingot(string name, Color color, Color bright)
    {
        string path = $"{PrefabDir}/Items/Item_{name}.prefab";
        if (Load<GameObject>(path, false) == null) AssetDatabase.CopyAsset(PrefabDir + "/Items/Item_IronIngot.prefab", path);
        var main = Mat("Item" + name, color, 0.6f, 0.3f, Detail.Metal);
        var hi = Mat("Item" + name + "Hi", bright, 0.65f, 0.3f, Detail.Metal);
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            root.name = "Item_" + name;
            var renderer = root.GetComponentInChildren<MeshRenderer>(true);
            var mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = i == 0 ? main : hi;
            renderer.sharedMaterials = mats;
            ArtAssets.SetArray(root.GetComponent<WorldItem>(), "materialVariants", new Object[] { main, hi });
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        var prefab = Load<GameObject>(path);
        ArtAssets.Set(Item(name), ("prefab", prefab.GetComponent<WorldItem>()), ("icon", ArtIcons.Render("Icon_" + name, prefab, new Vector3(45f, 30f, 0f))));
    }

    /// <summary>After Art_Machines.Icons: each build shows the furnace it builds.</summary>
    public static string Icons()
    {
        Log.Clear();
        foreach (var f in Furnaces)
        {
            var icon = Load<MachineDefinition>($"{DataDir}/Factory/{f.asset}.asset").Icon;
            string asset = f.key == "Iron" ? "Expansion_FurnaceHall" : "Expansion_Furnace" + f.key;
            ArtAssets.Set(Load<ExpansionDefinition>($"{DataDir}/World/{asset}.asset"), ("icon", icon));
        }

        AssetDatabase.SaveAssets();
        return "build icons set";
    }

    // =====================================================================================
    // SCENE
    // =====================================================================================

    public static string Scene()
    {
        Log.Clear();
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var gameplay = scene.GetRootGameObjects().First(g => g.name == "_Gameplay").transform;
        var hall = gameplay.Find("FurnaceHall");
        var content = hall.Find("Content");
        var hallExpansion = hall.GetComponent<Expansion>();
        bool contentActive = content.Find("Floor_Hall").gameObject.activeSelf;

        // Clear what the old single-furnace layout left in the battery's way.
        foreach (string n in new[] { "Conveyor_Ingots" }.Concat(content.Cast<Transform>().Where(t => t.name.StartsWith("Conveyor_Collect") || t.name.EndsWith("FurnacePlot")).Select(t => t.name)).ToArray())
        {
            var t = content.Find(n);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        // The junk heap in the hall's east end stood where the steel furnace and its pad go.
        var heap = content.Find("Heap_HallEast");
        if (heap != null) Object.DestroyImmediate(heap.gameObject);
        var decor = content.Find("ArtDecor");
        if (decor != null)
            foreach (Transform prop in decor)
                if (prop.position.z > 30.5f && prop.gameObject.activeSelf)
                {
                    prop.gameObject.SetActive(false);
                    Log.AppendLine($"  hid {prop.name} at {prop.position.x:0.0},{prop.position.z:0.0} (in the battery)");
                }

        // Ingot rack: west side, under the end of the collector belt; its pad faces east into the hall.
        var rack = content.Find("IngotRack");
        rack.SetPositionAndRotation(RackPos, Quaternion.Euler(0f, 180f, 0f));
        var rackStorage = rack.GetComponent<Storage>();
        ArtAssets.Set(rackStorage, ("labelTitle", "INGOTS"));
        ArtAssets.SetArray(rack.Find("Pile").GetComponent<ItemPile>(), "accepted", Furnaces.Select(f => (Object)Item(f.ingot)).ToArray());
        var rackLabel = rack.GetComponentInChildren<StationLabel>(true);
        if (rackLabel != null) rackLabel.transform.position = RackPos + new Vector3(0f, 3f, 1.2f);

        // Collector belt: one segment per furnace slot, east to west, then south into the rack. All of it comes with the
        // hall, so a furnace bought out of order still has somewhere to send its ingots.
        var down = BuildConveyor(content, "Conveyor_Collect_Down", new Vector3(DownX, 0f, BeltZ + 0.35f), new Vector3(DownX, 0f, RackPos.z + 1.7f), rackStorage, contentActive);
        MonoBehaviour next = down;
        var belts = new Dictionary<string, Conveyor>();
        for (int i = 0; i < Furnaces.Length; i++)
        {
            var f = Furnaces[i];
            float endX = i == 0 ? DownX + 0.55f : Furnaces[i - 1].x + 1.05f;
            var belt = BuildConveyor(content, "Conveyor_Collect_" + f.key, new Vector3(f.x + 1f, 0f, BeltZ), new Vector3(endX, 0f, BeltZ), next, contentActive);
            belts[f.key] = belt;
            next = belt;
        }

        // Iron Furnace: the existing instance, moved into the first slot.
        var machines = new Dictionary<string, Machine>();
        var pads = new Dictionary<string, TransferPad>();
        var iron = content.Find("Furnace");
        iron.SetPositionAndRotation(new Vector3(Furnaces[0].x, 0f, FurnaceZ), Quaternion.identity);
        machines["Iron"] = iron.GetComponent<Machine>();

        // The three later furnaces: an Expansion each, inside the hall.
        var tilePrefab = Load<GameObject>(PrefabDir + "/Tiles/Tile_Purchase.prefab");
        foreach (var f in Furnaces.Skip(1))
        {
            var plot = Empty(f.key + "FurnacePlot", content, Vector3.zero);
            plot.gameObject.SetActive(contentActive);
            var locked = Empty("LockedOnly", plot, Vector3.zero);
            PlotMarker(locked, f);
            var plotContent = Empty("Content", plot, Vector3.zero);
            var furnace = ((GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>($"{PrefabDir}/Stations/{f.prefab}.prefab"), plotContent)).transform;
            furnace.name = f.prefab;
            furnace.SetPositionAndRotation(new Vector3(f.x, 0f, FurnaceZ), Quaternion.identity);
            machines[f.key] = furnace.GetComponent<Machine>();
            var focus = Empty("Focus", plot, new Vector3(f.x, 0f, FurnaceZ - 3f));

            var tile = ((GameObject)PrefabUtility.InstantiatePrefab(tilePrefab, locked)).transform;
            tile.name = "Tile_Build" + f.key;
            tile.position = new Vector3(f.x, 0f, PadZ);
            ArtAssets.Set(tile.GetComponent<PurchaseTile>(), ("upgradeId", BuildId(f.key)));
            ArtAssets.Set(tile.gameObject.AddComponent<GuideAnchor>(), ("id", "tile/" + BuildId(f.key)));

            var expansion = plot.gameObject.AddComponent<Expansion>();
            EditorUtility.CopySerialized(hallExpansion, expansion);
            var so = new SerializedObject(expansion);
            so.FindProperty("definition").objectReferenceValue = Load<ExpansionDefinition>($"{DataDir}/World/Expansion_Furnace{f.key}.asset");
            so.FindProperty("barriers").arraySize = 0;
            so.FindProperty("contentRoot").objectReferenceValue = plotContent;
            so.FindProperty("lockedOnly").objectReferenceValue = locked.gameObject;
            so.FindProperty("area").boundsValue = new Bounds(new Vector3(f.x, 0f, FurnaceZ), new Vector3(f.width + 0.4f, 4f, 3.6f));
            so.FindProperty("focusPoint").objectReferenceValue = focus;
            so.FindProperty("materialSwaps").arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        foreach (var f in Furnaces)
        {
            var m = machines[f.key];
            ArtAssets.Set(m, ("outputTarget", belts[f.key]));
            pads[f.key] = m.transform.Find("InputPad").GetComponent<TransferPad>();
        }

        // One Smelter, one boost pad and one operator for the whole battery.
        var others = Furnaces.Skip(1).Select(f => (Object)machines[f.key]).ToArray();
        var smelter = gameplay.Find("Workers/Sites/SmelterRoute");
        smelter.position = new Vector3(65.5f, 0f, 31.3f);
        var route = smelter.GetComponent<PorterRoute>();
        var plantContent = gameplay.Find("RecyclingPlant/Content");
        ArtAssets.SetArray(route, "pickupPads", Furnaces.Select(f => (Object)plantContent.Find("Bin_" + f.input + "/WithdrawPad").GetComponent<TransferPad>()).ToArray());
        ArtAssets.Set(route, ("dropoff", pads["Iron"]));
        ArtAssets.SetArray(route, "extraDropoffs", Furnaces.Skip(1).Select(f => (Object)pads[f.key]).ToArray());

        Move(content, "Tile_Smelter", new Vector3(73.9f, 0f, 30.9f));   // north of the ground the Dockyard sign covers   // east side: the pad of the Press takes the west (R3)
        var boost = Move(content, "Tile_BoostFurnace", new Vector3(70.6f, 0f, 28.2f));
        if (boost != null) ArtAssets.SetArray(boost.GetComponent<OverdriveTile>(), "alsoBoosts", others);
        Move(content, "Tile_Operator_furnace", OperatorPos);
        var console = content.Find("Console_Furnace");
        if (console != null) console.SetPositionAndRotation(OperatorPos + Vector3.forward * 0.75f, Quaternion.LookRotation(Vector3.forward));
        var post = gameplay.Find("Workers/Sites/Post_Furnace");
        if (post != null)
        {
            post.SetPositionAndRotation(OperatorPos, Quaternion.LookRotation(Vector3.forward));
            ArtAssets.SetArray(post.GetComponent<WorkPost>(), "alsoRuns", others);
        }

        // The market counter takes everything the market sells.
        var counter = plantContent.Find("MetalMarket/CounterPile");
        if (counter != null)
            ArtAssets.SetArray(counter.GetComponent<ItemPile>(), "accepted", new[] { "Iron", "Aluminum", "Copper", "Steel", "IronIngot", "AluminumIngot", "CopperIngot", "SteelIngot" }
                .Select(n => (Object)Item(n)).ToArray());

        // The save restores stock by item id: the pool has to know the new ingots.
        var pool = scene.GetRootGameObjects().First(g => g.name == "_Systems").GetComponentInChildren<ItemPool>(true);
        var poolSo = new SerializedObject(pool);
        var catalog = poolSo.FindProperty("catalog");
        var items = new List<Object>();
        for (int i = 0; i < catalog.arraySize; i++) items.Add(catalog.GetArrayElementAtIndex(i).objectReferenceValue);
        foreach (var f in Furnaces)
            if (!items.Contains(Item(f.ingot))) items.Add(Item(f.ingot));
        ArtAssets.SetArray(pool, "catalog", items.ToArray());

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("scene: battery of 4 furnaces, collector belt, rack, 3 build plots. Next: Art_Machines.Scene, Crane_Build.Scene, Env_Build.All, R1_Build.Bake, UI_Build.Apply.");
        return Log.ToString();
    }

    /// <summary>What stands in a slot before its furnace is bought: a hazard-framed foundation and a sign in the metal's colour.</summary>
    static void PlotMarker(Transform parent, (string key, string input, string ingot, string asset, string id, string prefab, string label, float x, float width) f)
    {
        var concrete = Mat("MachConcrete", new Color(0.72f, 0.7f, 0.67f), 0.15f, 0f, Detail.Concrete, 0.35f);
        var dark = Mat("MachDark", new Color(0.17f, 0.18f, 0.2f), 0.35f, 0.1f, Detail.Metal, 0.8f);
        var yellow = Mat("MachYellow", new Color(1f, 0.76f, 0.1f), 0.5f, 0.05f, Detail.Paint, 0.7f);
        var metal = Mat("Plot" + f.key, Item(f.input).Color, 0.55f, 0.25f, Detail.Metal);
        var k = new MeshKit();
        float w = f.width + 0.4f, d = 3.5f, hw = w * 0.5f, hd = d * 0.5f;
        k.Box(0, new Vector3(0f, 0.1f, 0.05f), new Vector3(w, 0.2f, d), 0.05f);
        k.Box(1, new Vector3(0f, 0.215f, 0.05f), new Vector3(w - 0.5f, 0.03f, d - 0.5f), 0.01f);
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                k.Cylinder(2, new Vector3(sx * (hw - 0.2f), 0.6f, 0.05f + sz * (hd - 0.2f)), 0.09f, 0.8f, 10);
                k.Cylinder(1, new Vector3(sx * (hw - 0.2f), 1.02f, 0.05f + sz * (hd - 0.2f)), 0.12f, 0.06f, 10);
            }

        // Sign: two legs and a plate in the metal's colour.
        foreach (int sx in new[] { -1, 1 }) k.Box(1, new Vector3(sx * 0.55f, 0.9f, 0.05f), new Vector3(0.1f, 1.4f, 0.1f), 0.02f);
        k.Box(3, new Vector3(0f, 1.75f, 0.05f), new Vector3(1.6f, 0.8f, 0.14f), 0.05f);
        k.Box(1, new Vector3(0f, 1.75f, -0.03f), new Vector3(1.42f, 0.62f, 0.04f), 0.02f);
        var go = ArtAssets.Part("R2_Plots", "Plot" + f.key, k, new[] { concrete, dark, yellow, metal }, parent, new Vector3(f.x, 0f, FurnaceZ));
        go.name = "Foundation";
        var icon = new GameObject("Icon");
        icon.transform.SetParent(parent, false);
        icon.transform.position = new Vector3(f.x, 1.75f, FurnaceZ - 0.01f);
        icon.transform.localScale = Vector3.one * 0.2f;
        icon.AddComponent<SpriteRenderer>().sprite = Item(f.ingot).Icon;
    }

    /// <summary>A bare belt (points, mover, collider); Art_Machines.Scene builds its frame and belt surface.</summary>
    static Conveyor BuildConveyor(Transform parent, string name, Vector3 from, Vector3 to, MonoBehaviour destination, bool active)
    {
        var existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        var root = Empty(name, parent, Vector3.zero);
        root.position = from;
        Vector3 delta = to - from;
        float length = delta.magnitude;
        root.rotation = Quaternion.LookRotation(delta.normalized);
        var start = Empty("Start", root, new Vector3(0f, 0f, 0.15f));
        var end = Empty("End", root, new Vector3(0f, 0f, length - 0.15f));
        var conveyor = root.gameObject.AddComponent<Conveyor>();
        ArtAssets.Set(conveyor, ("speed", 2.6f), ("spacing", 0.5f), ("rideHeight", 0.57f), ("destination", destination));
        ArtAssets.SetArray(conveyor, "points", new Object[] { start, end });
        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.25f, length * 0.5f);
        box.size = new Vector3(1.3f, 0.5f, length);
        var modifier = root.gameObject.AddComponent<NavMeshModifier>();
        modifier.overrideArea = true;
        modifier.area = NavMesh.GetAreaFromName("Not Walkable");
        root.gameObject.SetActive(active);
        return conveyor;
    }

    static Transform Move(Transform parent, string name, Vector3 position)
    {
        var t = parent.Find(name);
        if (t == null) Log.AppendLine("!! no " + name);
        else t.position = position;
        return t;
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    static ItemDefinition Item(string name) => Load<ItemDefinition>($"{DataDir}/Items/Item_{name}.asset");

    static T Load<T>(string path, bool warn = true) where T : Object
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a == null && warn) Log.AppendLine("!! missing " + path);
        return a;
    }

    static Transform Empty(string name, Transform parent, Vector3 localPos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        return t;
    }

    static void SetLong(Object target, string field, long value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).longValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void SetColumn(Object target, string field, params int[] values)
    {
        var so = new SerializedObject(target);
        var levels = so.FindProperty("levels");
        for (int i = 0; i < Mathf.Min(values.Length, levels.arraySize); i++) levels.GetArrayElementAtIndex(i).FindPropertyRelative(field).intValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void UpsertCatalog(List<(string id, int level, bool inPanel, string group)> entries)
    {
        var catalog = Load<UpgradeCatalog>(DataDir + "/Progression/UpgradeCatalog.asset");
        var so = new SerializedObject(catalog);
        var p = so.FindProperty("entries");
        foreach (var (id, level, inPanel, group) in entries)
        {
            int index = -1;
            for (int i = 0; i < p.arraySize; i++)
                if (p.GetArrayElementAtIndex(i).FindPropertyRelative("upgradeId").stringValue == id) index = i;
            if (index < 0) index = p.arraySize++;
            var e = p.GetArrayElementAtIndex(index);
            e.FindPropertyRelative("upgradeId").stringValue = id;
            e.FindPropertyRelative("unlockLevel").intValue = level;
            e.FindPropertyRelative("inPanel").boolValue = inPanel;
            e.FindPropertyRelative("group").stringValue = group;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalog);
    }
}

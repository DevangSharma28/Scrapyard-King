using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ScrapYardKing.Customers;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Factory;
using ScrapYardKing.Harvest;
using ScrapYardKing.Items;
using ScrapYardKing.Player;
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
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using static ScrapYardKing.EditorTools.Art.ArtMaterials;

// Revamp 1 builder ("fast factory", step 1 of the production-chain redesign):
//  - Balance: the speed pass. The measured session had the first worker at 7:48 and the plant at 31:40; the target is a
//    first worker inside 5 minutes and the Splitter inside 10. Every number changed for that lives in Balance().
//  - Raw Metal: the Crusher's product is now called Raw Metal (same item asset and id, `mixed_metal`, so saves, tasks
//    and anchors keep working).
//  - Metal Splitter: the Sorter becomes a four-way splitter (iron, aluminum, copper, steel), one colour-coded belt and
//    bin per material. Its id stays `sorter`. The model is built by Art_Machines.Sorter (run Art_Machines.Prefabs
//    first); this builder owns the data, the two new items, the bins, belts, ports, routes and the plant layout.
// Entry points, in order: Assets, Prefabs, Scene (or All). Incremental and idempotent like M3–M8. Run it after M8_Build
// and the Art builders, then UI_Build.Apply (it does not touch UI sprites, but the M-builders before it do).
public static class R1_Build
{
    const string P = "Assets/_Project";
    const string DataDir = P + "/Data";
    const string PrefabDir = P + "/Prefabs";
    const string ScenePath = P + "/Scenes/Area1_OldScrapYard.unity";
    const string ItemModel = "R1_Items";

    static readonly Color AluminumTint = new(0.72f, 0.84f, 0.95f);
    static readonly Color SteelTint = new(0.3f, 0.36f, 0.5f);

    // ---------- plant layout (blueprint frame: origin south-west, +X east, +Z north) ----------
    // Four bins in a row south of the Splitter, in belt order west → east. The belts fan out from the chutes.
    const float BinZ = 19.6f, BeltEndZ = 21.15f;
    static readonly (string id, string item, string label, float x)[] Bins =
    {
        ("bin_iron", "Iron", "IRON", 44.9f),
        ("bin_aluminum", "Aluminum", "ALUMINUM", 48.3f),
        ("bin_copper", "Copper", "COPPER", 51.7f),
        ("bin_steel", "Steel", "STEEL", 55.1f),
    };

    static readonly StringBuilder Log = new();

    public static string All()
    {
        var sb = new StringBuilder();
        sb.Append(Assets());
        sb.Append(Prefabs());
        sb.Append(Scene());
        return sb.ToString();
    }

    // =====================================================================================
    // ASSETS
    // =====================================================================================

    public static string Assets()
    {
        Log.Clear();
        AssetDatabase.Refresh();
        Balance();
        Materials();
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    /// <summary>
    /// The speed pass. Before: a customer every 8 s capped the yard at about $135 a minute, so "Sell 40 metal" alone took
    /// three minutes and the first worker came at 7:48. Selling, crushing and cutting are now roughly 2.5x faster, early
    /// hires and gates cost less and unlock a level earlier. Late-game costs (Furnace onward) are untouched: they get
    /// their own pass when the four furnaces, the press and the contracts replace that part of the chain.
    /// </summary>
    static void Balance()
    {
        // The player: more cuts per second, a bigger first backpack, a faster walk. Upgrades stack on top as before.
        SetMany(Load<PlayerConfig>(DataDir + "/Config/PlayerConfig.asset"), ("moveSpeed", 6.8f), ("cutRate", 7f), ("carryCapacity", 14), ("pickupRadius", 3f));
        // Owner, 2026-10-06 ("still slow"): a bigger first backpack and +3 per level instead of +2, a Crusher that keeps up
        // with it, Raw Metal worth $8, cheaper gates.
        SetFloatArray(Load<PlayerStatUpgradeDefinition>(DataDir + "/Progression/Upgrade_Backpack.asset"), "totalModifierPerLevel", 0f, 3f, 6f, 9f, 12f, 15f, 18f, 21f, 25f);
        SetMany(Load<ItemDefinition>(DataDir + "/Items/Item_MixedMetal.asset"), ("baseValue", 8));
        SetColumn(Load<StorageDefinition>(DataDir + "/Factory/Storage_Yard.asset"), "levels", "capacity", 60, 90, 130);

        // Scrap: only the car was slow for a first object (3.4 s at the new cut rate). Everything comes back sooner, so
        // the field is never empty while a porter and the player both work it.
        Scrap("CarWreck", 180f, 7f);
        Scrap("Barrel", 45f, 4f);
        Scrap("TireStack", 36f, 4f);
        Scrap("Fridge", 300f, 8f);
        Scrap("Kart", 240f, 7f);
        Scrap("Stove", 200f, 7f);
        Scrap("Washer", 170f, 6f);
        Scrap("Tractor", 700f, 14f);
        Scrap("Truck", 900f, 15f);
        Scrap("GarbageTruck", 1300f, 18f);

        // Machines: cycle times cut by about a third, bigger hoppers so one backpack never overfills them.
        var crusher = Load<MachineDefinition>(DataDir + "/Factory/Machine_Crusher.asset");
        SetColumn(crusher, "levels", "cycleTime", 0.35f, 0.3f, 0.25f, 0.2f, 0.16f);
        SetColumn(crusher, "levels", "inputCapacity", 20, 26, 32, 40, 50);
        SetColumn(crusher, "levels", "upgradeCost", 0L, 150L, 300L, 900L, 1800L);
        var sorter = Load<MachineDefinition>(DataDir + "/Factory/Machine_Sorter.asset");
        SetColumn(sorter, "levels", "cycleTime", 0.6f, 0.5f, 0.42f, 0.35f, 0.29f);
        SetColumn(sorter, "levels", "inputCapacity", 16, 20, 26, 32, 40);
        SetColumn(sorter, "levels", "upgradeCost", 0L, 500L, 1200L, 2600L, 4800L);
        // Furnace numbers live in R2_Build (four furnaces, one per metal).

        // Counters: the customer line was the session's slowest part. A buyer every 3.2 s at Lv.1 instead of 8 s, bigger
        // orders, bigger counters; customers also arrive and walk faster so the line refills.
        var desk = Load<SellDeskDefinition>(DataDir + "/Factory/SellDesk_Yard.asset");
        // Owner, 2026-10-06: a customer takes the goods at once, no waiting bar. What is left is a beat short enough to
        // read as "instant" (the walk to the counter and the cheer are the rhythm now). Desk levels still grow the
        // order size, the price, the counter and the line.
        SetColumn(desk, "levels", "saleInterval", 0.3f, 0.27f, 0.24f, 0.21f, 0.18f);
        SetMany(desk, ("firstSaleDelay", 0.2f));
        SetColumn(desk, "levels", "unitsPerSale", new Vector2Int(3, 5), new Vector2Int(3, 6), new Vector2Int(4, 7), new Vector2Int(4, 8), new Vector2Int(5, 9));
        SetColumn(desk, "levels", "counterCapacity", 16, 20, 26, 32, 40);
        SetColumn(desk, "levels", "upgradeCost", 0L, 250L, 500L, 1500L, 3000L);
        var market = Load<SellDeskDefinition>(DataDir + "/Factory/SellDesk_Market.asset");
        SetColumn(market, "levels", "saleInterval", 0.3f, 0.26f, 0.22f, 0.18f);
        SetMany(market, ("firstSaleDelay", 0.2f));
        SetColumn(market, "levels", "unitsPerSale", new Vector2Int(3, 5), new Vector2Int(3, 6), new Vector2Int(4, 7), new Vector2Int(5, 8));
        SetColumn(market, "levels", "counterCapacity", 24, 30, 36, 44);
        foreach (string c in new[] { "CustomerConfig", "CustomerConfig_Market" })
            SetMany(Load<CustomerConfig>($"{DataDir}/Customers/{c}.asset"), ("walkSpeed", 5.2f), ("arrivalInterval", new Vector2(0.35f, 0.9f)), ("handOverInterval", 0.05f));

        // Carriers walk and carry more; the first four hires cost less.
        Worker("Porter", 5.2f, 8, 300, 1000);
        Worker("Helper", 5f, 10, 450, 1400);
        Worker("Runner", 5.2f, 10, 1100, 2600);
        Worker("Smelter", 5.2f, 10, 1500, 4000);   // one Smelter serves all four furnaces (R2)
        Worker("Loader", 5.2f, 12, 6000, 11000);
        // A hired worker appears at the job (with the hire burst) instead of walking in from the yard's south edge: the
        // plant's workers took 10 s and more to show up after the purchase.
        foreach (string guid in AssetDatabase.FindAssets("t:WorkerDefinition", new[] { DataDir + "/Workers" }))
            SetMany(AssetDatabase.LoadAssetAtPath<WorkerDefinition>(AssetDatabase.GUIDToAssetPath(guid)), ("spawnAtSite", true));

        // Gates and unlock levels: the Back Lot and the plant come a level earlier and cheaper.
        SetMany(Load<ExpansionDefinition>(DataDir + "/World/Expansion_BackLot.asset"), ("cost", 700L));
        SetMany(Load<ExpansionDefinition>(DataDir + "/World/Expansion_RecyclingPlant.asset"), ("cost", 1000L));
        UnlockLevels(("boots", 3), ("storage_yard", 2), ("porter", 2), ("helper", 3), ("back_lot", 4), ("recycling_plant", 5), ("sorter", 5), ("metal_bins", 5),
            ("market", 5), ("hauler", 5), ("runner", 6));

        // Tasks sized to the new rates (each should take well under two minutes).
        TaskAmount("t10_sell40", 30);
        TaskAmount("t15_serve20", 12);
        TaskAmount("t16_level5", 4);
        TaskAmount("t19_crush150", 60);
        TaskAmount("t27_sort100", 80);
        // Run 3: "Sell 6 copper" took 2:44 with flat cash (copper is one piece in five and the buyer has to ask for it).
        // The first plant task after splitting is now the player's own action: stock the market with anything.
        SetMany(Load<TaskDefinition>(DataDir + "/Progression/Tasks/Task_t22_copper.asset"), ("title", "Stock the Metal Market"), ("type", (int)TaskType.DeliverItems),
            ("targetId", "market"), ("amount", 12L), ("rewardCash", 200L));

        // Run 4: "Serve 8 market customers" took 2:30 with flat cash, because the player alone carried Raw Metal from the
        // yard to the Splitter and then material to the market. The Hauler hire now comes first (and costs $600), so the
        // player only works the bins and the market while learning the plant.
        Worker("Hauler", 5.2f, 10, 600, 2000);
        MoveTaskBefore("t24_hauler", "t23_market");
        Log.AppendLine("balance applied");
    }

    static void MoveTaskBefore(string id, string beforeId)
    {
        var chain = Load<TaskChain>(DataDir + "/Progression/TaskChain_Area1.asset");
        var so = new SerializedObject(chain);
        var p = so.FindProperty("mainTasks");
        var list = new List<Object>();
        for (int i = 0; i < p.arraySize; i++) list.Add(p.GetArrayElementAtIndex(i).objectReferenceValue);
        var task = list.FirstOrDefault(t => t is TaskDefinition d && d.Id == id);
        if (task == null || !list.Any(t => t is TaskDefinition d && d.Id == beforeId))
        {
            Log.AppendLine($"!! task chain has no {id} or {beforeId}");
            return;
        }

        list.Remove(task);
        list.Insert(list.FindIndex(t => t is TaskDefinition d && d.Id == beforeId), task);
        SetArray(chain, "mainTasks", list.ToArray());
    }

    static void Scrap(string name, float health, float respawn) =>
        SetMany(Load<ScrapDefinition>($"{DataDir}/Scrap/Scrap_{name}.asset"), ("maxHealth", health), ("respawnDelay", respawn));

    static void Worker(string name, float speed, int carry, params long[] costs)
    {
        var w = Load<WorkerDefinition>($"{DataDir}/Workers/Worker_{name}.asset");
        SetMany(w, ("moveSpeed", speed), ("carryCapacity", carry));
        SetLongArray(w, "hireCosts", costs);
    }

    static void TaskAmount(string id, long amount) => SetMany(Load<TaskDefinition>($"{DataDir}/Progression/Tasks/Task_{id}.asset"), ("amount", amount));

    static void UnlockLevels(params (string id, int level)[] entries)
    {
        var catalog = Load<UpgradeCatalog>(DataDir + "/Progression/UpgradeCatalog.asset");
        var so = new SerializedObject(catalog);
        var p = so.FindProperty("entries");
        foreach (var (id, level) in entries)
        {
            bool found = false;
            for (int i = 0; i < p.arraySize; i++)
            {
                var e = p.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("upgradeId").stringValue != id) continue;
                e.FindPropertyRelative("unlockLevel").intValue = level;
                found = true;
            }

            if (!found) Log.AppendLine("!! catalog has no entry " + id);
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalog);
    }

    /// <summary>The two new metals, the Raw Metal name, the four-way split, and what the market sells and customers ask for.</summary>
    static void Materials()
    {
        var raw = Load<ItemDefinition>(DataDir + "/Items/Item_MixedMetal.asset");
        SetMany(raw, ("displayName", "Raw Metal"));
        var iron = Load<ItemDefinition>(DataDir + "/Items/Item_Iron.asset");
        var copper = Load<ItemDefinition>(DataDir + "/Items/Item_Copper.asset");
        var aluminum = LoadOrCreate<ItemDefinition>(DataDir + "/Items/Item_Aluminum.asset");
        var steel = LoadOrCreate<ItemDefinition>(DataDir + "/Items/Item_Steel.asset");
        // One Raw Metal ($6) splits into $18 of material on average: iron is the common one, steel the prize.
        SetMany(iron, ("baseValue", 10));
        SetMany(aluminum, ("id", "aluminum"), ("displayName", "Aluminum"), ("color", AluminumTint), ("stackHeight", 0.24f), ("baseValue", 16));
        SetMany(copper, ("baseValue", 24));
        SetMany(steel, ("id", "steel"), ("displayName", "Steel"), ("color", SteelTint), ("stackHeight", 0.24f), ("baseValue", 36));

        var splitter = Load<MachineDefinition>(DataDir + "/Factory/Machine_Sorter.asset");
        SetMany(splitter, ("displayName", "Metal Splitter"));
        SetStructArray(splitter, "outputMix", new[]
        {
            Lv(("item", iron), ("weight", 0.4f)), Lv(("item", aluminum), ("weight", 0.25f)), Lv(("item", copper), ("weight", 0.2f)), Lv(("item", steel), ("weight", 0.15f)),
        });

        var ironIngot = Load<ItemDefinition>(DataDir + "/Items/Item_IronIngot.asset");
        var copperIngot = Load<ItemDefinition>(DataDir + "/Items/Item_CopperIngot.asset");
        // What the market sells and what customers ask for is set by R2_Build.Assets (four raw metals and four ingots).
        _ = ironIngot;
        _ = copperIngot;

        // Task wording follows the new names (ids and targets stay).
        SetMany(Load<TaskDefinition>(DataDir + "/Progression/Tasks/Task_t10_sell40.asset"), ("title", "Sell {0} raw metal"));
        SetMany(Load<TaskDefinition>(DataDir + "/Progression/Tasks/Task_t21_sort.asset"), ("title", "Split {0} raw metal"));
        SetMany(Load<TaskDefinition>(DataDir + "/Progression/Tasks/Task_t25_boost_sorter.asset"), ("title", "Boost the splitter"));
        SetMany(Load<TaskDefinition>(DataDir + "/Progression/Tasks/Task_t27_sort100.asset"), ("title", "Split {0} raw metal"));
        Log.AppendLine("materials: raw metal, aluminum, steel, four-way split");
    }

    // =====================================================================================
    // PREFABS
    // =====================================================================================

    public static string Prefabs()
    {
        Log.Clear();
        // Aluminum: a strapped bale of crushed cans, pale blue-silver. Steel: a short I-beam, dark blue.
        var can = new MeshKit();
        can.Box(0, Vector3.zero, new Vector3(0.44f, 0.22f, 0.32f), 0.05f);
        can.Box(0, new Vector3(0f, 0.02f, 0f), new Vector3(0.36f, 0.24f, 0.24f), 0.04f);
        foreach (float x in new[] { -0.11f, 0.11f }) can.Box(1, new Vector3(x, 0f, 0f), new Vector3(0.045f, 0.235f, 0.335f), 0.01f);
        Item("Aluminum", can, Mat("ItemAluminum", AluminumTint, 0.6f, 0.3f, Detail.Metal), Mat("ItemAluminumBright", new Color(0.82f, 0.91f, 0.98f), 0.6f, 0.3f, Detail.Metal),
            Mat("ItemAluminumStrap", new Color(0.36f, 0.5f, 0.68f), 0.45f, 0.2f, Detail.Metal), 0.12f);

        var beam = new MeshKit();
        foreach (float y in new[] { -0.095f, 0.095f }) beam.Box(0, new Vector3(0f, y, 0f), new Vector3(0.46f, 0.055f, 0.28f), 0.015f);
        beam.Box(1, Vector3.zero, new Vector3(0.46f, 0.16f, 0.08f), 0.01f);
        Item("Steel", beam, Mat("ItemSteel", SteelTint, 0.55f, 0.3f, Detail.Metal), Mat("ItemSteelBright", new Color(0.38f, 0.45f, 0.62f), 0.55f, 0.3f, Detail.Metal),
            Mat("ItemSteelWeb", new Color(0.55f, 0.62f, 0.76f), 0.6f, 0.3f, Detail.Metal), 0.125f);

        SplitterPrefab();
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    /// <summary>A pooled item prefab: a copy of the iron prefab (same components and settings) with its own mesh, materials and icon.</summary>
    static void Item(string name, MeshKit kit, Material main, Material bright, Material accent, float halfHeight)
    {
        string path = $"{PrefabDir}/Items/Item_{name}.prefab";
        if (Load<GameObject>(path, false) == null) AssetDatabase.CopyAsset($"{PrefabDir}/Items/Item_Iron.prefab", path);
        var mesh = ArtAssets.SaveMesh(ItemModel, name, kit, 2);
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            root.name = "Item_" + name;
            var item = root.GetComponent<WorldItem>();
            var visual = root.transform.Find("Visual");
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;
            visual.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = visual.GetComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { main, accent };
            SetMany(item, ("halfHeight", halfHeight), ("visual", mr), ("meshFilter", visual.GetComponent<MeshFilter>()));
            SetArray(item, "meshVariants", Array.Empty<Object>());
            SetArray(item, "materialVariants", new Object[] { main, bright });
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        var prefab = Load<GameObject>(path);
        var def = Load<ItemDefinition>($"{DataDir}/Items/Item_{name}.asset");
        SetMany(def, ("prefab", prefab.GetComponent<WorldItem>()), ("icon", ArtIcons.Render("Icon_" + name, prefab, new Vector3(45f, 30f, 0f))));
        Log.AppendLine("item " + name);
    }

    /// <summary>The Splitter prefab's function side: four ports (targets are set per scene instance) and an item icon on each chute plate.</summary>
    static void SplitterPrefab()
    {
        string path = PrefabDir + "/Stations/Sorter.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var machine = root.GetComponent<Machine>();
            var ports = new List<Dictionary<string, object>>();
            var markers = root.transform.Find("Markers");
            if (markers == null)
            {
                markers = new GameObject("Markers").transform;
                markers.SetParent(root.transform, false);
            }

            foreach (Transform m in markers.Cast<Transform>().ToArray()) Object.DestroyImmediate(m.gameObject);
            foreach (var (_, itemName, _, _) in Bins)
            {
                var item = Load<ItemDefinition>($"{DataDir}/Items/Item_{itemName}.asset");
                var point = root.transform.Find(itemName + "Out");
                if (point == null)
                {
                    Log.AppendLine($"!! Sorter.prefab has no {itemName}Out: run Art_Machines.Prefabs first");
                    continue;
                }

                ports.Add(Lv(("item", item), ("target", null), ("point", point)));
                var icon = new GameObject(itemName + "Icon");
                icon.transform.SetParent(markers, false);
                icon.transform.localPosition = new Vector3(point.localPosition.x, 1.0f, -1.31f);
                icon.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                icon.transform.localScale = Vector3.one * 0.18f;
                icon.AddComponent<SpriteRenderer>().sprite = item.Icon;
            }

            SetStructArray(machine, "outputPorts", ports.ToArray());
            SetMany(machine, ("outputPoint", root.transform.Find("IronOut")));
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Log.AppendLine($"splitter prefab: {ports.Count} ports");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // =====================================================================================
    // SCENE
    // =====================================================================================

    // Tiles and the operator post around the wider machine and the four bins.
    static readonly Vector3 OperatorTile = new(54.9f, 0f, 26.3f);
    static readonly (string name, Vector3 pos)[] Moves =
    {
        ("Tile_Hauler", new Vector3(43.2f, 0f, 23.1f)),          // clear of the tire pile by the gate and of the iron belt
        ("Tile_Runner", new Vector3(58.2f, 0f, 21.8f)),          // north of the ground the market label covers
        ("Tile_UpgradesPlant", new Vector3(58.2f, 0f, 25.2f)),
        ("Tile_BoostSorter", new Vector3(54.9f, 0f, 28.8f)),
        ("Tile_Operator_sorter", new Vector3(54.9f, 0f, 26.3f)),
    };

    public static string Scene()
    {
        Log.Clear();
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var gameplay = Root(scene, "_Gameplay");
        var content = gameplay.Find("RecyclingPlant/Content");
        var splitterGo = content.Find("Sorter");
        var splitter = splitterGo.GetComponent<Machine>();
        bool contentActive = splitterGo.gameObject.activeSelf;

        // Floor: wide enough for four bins.
        var floor = content.Find("Floor_Production");
        if (floor != null)
        {
            var bounds = ArtAssets.RendererBounds(floor.gameObject);
            float want = 17f;
            floor.localScale = new Vector3(floor.localScale.x * want / Mathf.Max(0.01f, bounds.size.x), floor.localScale.y, floor.localScale.z);
            floor.position = new Vector3(50f, floor.position.y, floor.position.z);
        }

        // Bins: the iron bin leads the shared level; the others follow it.
        var binDef = Load<StorageDefinition>(DataDir + "/Factory/Storage_Bins.asset");
        var storagePrefab = Load<GameObject>(PrefabDir + "/Stations/Storage.prefab");
        var storages = new Dictionary<string, Storage>();
        Storage leader = null;
        foreach (var (id, itemName, label, x) in Bins)
        {
            var t = content.Find("Bin_" + itemName);
            if (t == null)
            {
                t = ((GameObject)PrefabUtility.InstantiatePrefab(storagePrefab, content)).transform;
                t.name = "Bin_" + itemName;
                t.gameObject.SetActive(contentActive);
            }

            t.SetPositionAndRotation(new Vector3(x, 0f, BinZ), Quaternion.Euler(0f, -90f, 0f));
            var storage = t.GetComponent<Storage>();
            leader ??= storage;
            SetMany(storage, ("definition", binDef), ("stationId", id), ("labelTitle", label), ("levelSource", storage == leader ? null : leader));
            SetArray(t.Find("Pile").GetComponent<ItemPile>(), "accepted", new Object[] { Load<ItemDefinition>($"{DataDir}/Items/Item_{itemName}.asset") });
            var stationLabel = t.GetComponentInChildren<StationLabel>(true);
            if (stationLabel != null)
            {
                stationLabel.transform.position = t.position + new Vector3(0f, 3.0f, 1.2f);
                stationLabel.transform.localScale = Vector3.one * 0.8f;
            }

            storages[itemName] = storage;
        }

        // Belts fan out from the four chutes to the bins.
        var ports = new List<Dictionary<string, object>>();
        Conveyor first = null;
        foreach (var (_, itemName, _, x) in Bins)
        {
            var point = splitterGo.Find(itemName + "Out");
            if (point == null)
            {
                Log.AppendLine($"!! scene Splitter has no {itemName}Out: run Art_Machines.Prefabs first");
                continue;
            }

            Vector3 from = point.position;
            from.y = 0f;
            var belt = BuildConveyor(content, "Conveyor_" + itemName, from + Vector3.back * 0.1f, new Vector3(x, 0f, BeltEndZ), storages[itemName]);
            belt.gameObject.SetActive(contentActive);
            first ??= belt;
            ports.Add(Lv(("item", Load<ItemDefinition>($"{DataDir}/Items/Item_{itemName}.asset")), ("target", belt), ("point", point)));
        }

        SetStructArray(splitter, "outputPorts", ports.ToArray());
        SetMany(splitter, ("outputTarget", first));

        // Market counter takes everything the market sells.
        var market = content.Find("MetalMarket");
        var sells = new[] { "Iron", "Aluminum", "Copper", "Steel", "IronIngot", "AluminumIngot", "CopperIngot", "SteelIngot" }
            .Select(n => (Object)Load<ItemDefinition>($"{DataDir}/Items/Item_{n}.asset", false)).Where(o => o != null).ToArray();
        SetArray(market.Find("CounterPile").GetComponent<ItemPile>(), "accepted", sells);

        // Tiles, the operator's console and post.
        foreach (var (name, pos) in Moves)
        {
            var t = content.Find(name);
            if (t == null) Log.AppendLine("!! no " + name);
            else t.position = pos;
        }

        var console = content.Find("Console_Sorter");
        if (console != null) console.SetPositionAndRotation(OperatorTile + Vector3.left * 0.75f, Quaternion.LookRotation(Vector3.left));
        var post = gameplay.Find("Workers/Sites/Post_Sorter");
        if (post != null) post.SetPositionAndRotation(OperatorTile, Quaternion.LookRotation(Vector3.left));
        else Log.AppendLine("!! no Post_Sorter");

        // The Market Runner serves all four bins (plus whatever pads it already had, e.g. the Ingot Rack).
        var runner = gameplay.Find("Workers/Sites/RunnerRoute");
        if (runner != null)
        {
            var route = runner.GetComponent<PorterRoute>();
            var so = new SerializedObject(route);
            var pads = so.FindProperty("pickupPads");
            var list = new List<Object>();
            for (int i = 0; i < pads.arraySize; i++) list.Add(pads.GetArrayElementAtIndex(i).objectReferenceValue);
            foreach (var st in storages.Values)
            {
                var pad = st.transform.Find("WithdrawPad").GetComponent<TransferPad>();
                if (!list.Contains(pad)) list.Add(pad);
            }

            SetArray(route, "pickupPads", list.Where(o => o != null).ToArray());
            var center = so.FindProperty("pickupCenter").objectReferenceValue as Transform;
            if (center != null) center.position = new Vector3(52f, 0f, 17.5f);
        }

        // Saved stock is restored by item id: the pool must know the new items.
        var pool = Root(scene, "_Systems").GetComponentInChildren<ItemPool>(true);
        var poolSo = new SerializedObject(pool);
        var catalog = poolSo.FindProperty("catalog");
        var items = new List<Object>();
        for (int i = 0; i < catalog.arraySize; i++) items.Add(catalog.GetArrayElementAtIndex(i).objectReferenceValue);
        foreach (string n in new[] { "Aluminum", "Steel" })
        {
            var item = Load<ItemDefinition>($"{DataDir}/Items/Item_{n}.asset");
            if (!items.Contains(item)) items.Add(item);
        }

        SetArray(pool, "catalog", items.ToArray());

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine($"scene: {storages.Count} bins, {ports.Count} belts. Next: Art_Machines.Scene (belt and bin art), then R1_Build.Bake.");
        return Log.ToString();
    }

    /// <summary>A bare belt (points, mover, collider); Art_Machines.Scene builds its frame and belt surface.</summary>
    static Conveyor BuildConveyor(Transform parent, string name, Vector3 from, Vector3 to, MonoBehaviour destination)
    {
        foreach (string old in new[] { name })
        {
            var existing = parent.Find(old);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
        }

        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        root.position = from;
        Vector3 delta = to - from;
        float length = delta.magnitude;
        root.rotation = Quaternion.LookRotation(delta.normalized);
        var start = new GameObject("Start").transform;
        start.SetParent(root, false);
        start.localPosition = new Vector3(0f, 0f, 0.15f);
        var end = new GameObject("End").transform;
        end.SetParent(root, false);
        end.localPosition = new Vector3(0f, 0f, length - 0.15f);
        var conveyor = root.gameObject.AddComponent<Conveyor>();
        SetMany(conveyor, ("speed", 2.4f), ("spacing", 0.55f), ("rideHeight", 0.57f), ("destination", destination));
        SetArray(conveyor, "points", new Object[] { start, end });
        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.25f, length * 0.5f);
        box.size = new Vector3(1.3f, 0.5f, length);
        var modifier = root.gameObject.AddComponent<NavMeshModifier>();
        modifier.overrideArea = true;
        modifier.area = NavMesh.GetAreaFromName("Not Walkable");
        return conveyor;
    }

    /// <summary>NavMesh over every area, with expansion content switched on for the bake (same recipe as M8_Build.Rebake).</summary>
    public static string Bake()
    {
        Log.Clear();
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var environment = Root(scene, "_Environment");
        var gameplay = Root(scene, "_Gameplay");
        var surface = environment.Find("NavMesh").GetComponent<NavMeshSurface>();
        // Every expansion's content, nested ones included (a furnace plot inside the Furnace hall): switched on for the
        // bake so machines carve the mesh, and off again afterwards. A parent's content is listed before its children's.
        var contents = gameplay.GetComponentsInChildren<Expansion>(true)
            .Select(e => new SerializedObject(e).FindProperty("contentRoot").objectReferenceValue as Transform).Where(t => t != null).ToArray();
        var toggled = new List<GameObject>();
        foreach (var c in contents)
            foreach (Transform child in c)
                if (!child.gameObject.activeSelf)
                {
                    child.gameObject.SetActive(true);
                    toggled.Add(child.gameObject);
                }

        surface.buildHeightMesh = true;
        foreach (var belt in Object.FindObjectsByType<Conveyor>(FindObjectsInactive.Include))
        {
            if (!belt.TryGetComponent(out NavMeshModifier m)) m = belt.gameObject.AddComponent<NavMeshModifier>();
            m.overrideArea = true;
            m.area = NavMesh.GetAreaFromName("Not Walkable");
        }

        surface.BuildNavMesh();
        foreach (var go in toggled) go.SetActive(false);
        foreach (var c in contents)
            foreach (Transform child in c)
                child.gameObject.SetActive(false);
        string navPath = Path.Combine(Path.GetDirectoryName(ScenePath), Path.GetFileNameWithoutExtension(ScenePath)).Replace('\\', '/') + "/NavMesh-Area1.asset";
        if (File.Exists(navPath)) AssetDatabase.DeleteAsset(navPath);
        AssetDatabase.CreateAsset(surface.navMeshData, navPath);
        EditorUtility.SetDirty(surface);
        foreach (var (_, _, _, x) in Bins)
            Log.AppendLine($"pad x {x}: on navmesh {NavMesh.SamplePosition(new Vector3(x, 0f, BinZ - 3.1f), out _, 0.4f, NavMesh.AllAreas)} (must be True)");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("navmesh baked");
        return Log.ToString();
    }

    static Transform Root(Scene scene, string name) => scene.GetRootGameObjects().First(g => g.name == name).transform;

    // =====================================================================================
    // Helpers (builders compile one file at a time)
    // =====================================================================================

    static Dictionary<string, object> Lv(params (string key, object value)[] fields) => fields.ToDictionary(f => f.key, f => f.value);

    static T Load<T>(string path, bool warn = true) where T : Object
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a == null && warn) Log.AppendLine("!! missing " + path);
        return a;
    }

    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a != null) return a;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        a = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(a, path);
        return a;
    }

    static void SetMany(Object target, params (string field, object value)[] values)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        foreach (var (field, value) in values)
        {
            var p = so.FindProperty(field);
            if (p == null)
            {
                Log.AppendLine($"!! {target.GetType().Name}.{field} not found");
                continue;
            }

            Assign(p, value, field);
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void Assign(SerializedProperty p, object value, string field)
    {
        switch (value)
        {
            case Object o: p.objectReferenceValue = o; break;
            case float f: p.floatValue = f; break;
            case int i when p.propertyType == SerializedPropertyType.Enum: p.enumValueIndex = i; break;
            case int i when p.propertyType == SerializedPropertyType.Float: p.floatValue = i; break;
            case int i: p.intValue = i; break;
            case long l: p.longValue = l; break;
            case bool b: p.boolValue = b; break;
            case string s: p.stringValue = s; break;
            case Color c: p.colorValue = c; break;
            case Vector2 v2: p.vector2Value = v2; break;
            case Vector2Int v2i: p.vector2IntValue = v2i; break;
            case Vector3 v3: p.vector3Value = v3; break;
            case null: p.objectReferenceValue = null; break;
            default: Log.AppendLine($"!! unsupported value for {field}"); break;
        }
    }

    static void SetColumn(Object target, string array, string field, params object[] values)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        var p = so.FindProperty(array);
        if (p == null)
        {
            Log.AppendLine($"!! {target.name}.{array} not found");
            return;
        }

        for (int i = 0; i < Mathf.Min(values.Length, p.arraySize); i++)
        {
            var child = p.GetArrayElementAtIndex(i).FindPropertyRelative(field);
            if (child == null) Log.AppendLine($"!! {array}[{i}].{field} not found");
            else if (values[i] != null) Assign(child, values[i], field);
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void SetArray(Object target, string field, Object[] values)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null)
        {
            Log.AppendLine($"!! {target.GetType().Name}.{field} not found");
            return;
        }

        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void SetFloatArray(Object target, string field, params float[] values)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null)
        {
            Log.AppendLine($"!! {target.GetType().Name}.{field} not found");
            return;
        }

        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).floatValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void SetLongArray(Object target, string field, long[] values)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).longValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void SetStructArray(Object target, string field, Dictionary<string, object>[] elements)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        p.arraySize = elements.Length;
        for (int i = 0; i < elements.Length; i++)
        {
            var element = p.GetArrayElementAtIndex(i);
            foreach (var kv in elements[i])
            {
                var child = element.FindPropertyRelative(kv.Key);
                if (child == null) Log.AppendLine($"!! {field}[{i}].{kv.Key} not found");
                else Assign(child, kv.Value, kv.Key);
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}

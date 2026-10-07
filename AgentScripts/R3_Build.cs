using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Factory;
using ScrapYardKing.Feedback;
using ScrapYardKing.Items;
using ScrapYardKing.Progression;
using ScrapYardKing.Tiles;
using ScrapYardKing.UI;
using ScrapYardKing.Workers;
using ScrapYardKing.World;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static ScrapYardKing.EditorTools.Art.ArtMaterials;

// Revamp 3 builder: the Industrial Press, metal bars, the bar storage and truck contracts.
//  - Press: one machine with four recipes (each ingot → its bar), bought as a build in the Furnace hall's south end.
//  - Bars ride a short belt south to the Bar Storage beside the Truck Dock. The truck now buys bars, not ingots.
//  - Contracts: every truck arrives with an order (TruckContractDefinition). Bars the order asked for pay its bonus;
//    the truck still takes any bar, so nothing can clog. The HUD shows the order on a card (ContractWidget).
//  - The Loader comes with the Press and works both hops: ingot rack → Press, bar storage → truck.
// Entry points, in order: Assets, Prefabs, then Art_Machines.Prefabs (the Press model), Art_Machines.Icons, Icons, Scene,
// then Art_Machines.Scene, Env_Build.All, R1_Build.Bake, UI_Build.Apply. Run after R2_Build. Incremental and idempotent.
public static class R3_Build
{
    const string P = "Assets/_Project";
    const string DataDir = P + "/Data";
    const string PrefabDir = P + "/Prefabs";
    const string KitDir = P + "/Art/UI/Kit";
    const string ScenePath = P + "/Scenes/Area1_OldScrapYard.unity";

    static readonly string[] Metals = { "Iron", "Aluminum", "Copper", "Steel" };

    // ---------- layout (blueprint frame). North to south: ingot rack → Press → belt → Bar Storage → truck pad. ----------
    static readonly Vector3 PressPos = new(69f, 0f, 23.2f);
    static readonly Vector3 PressPad = new(64.6f, 0f, 23.2f);
    static readonly Vector3 BarStoragePos = new(69f, 0f, 17.5f);

    static readonly StringBuilder Log = new();

    // =====================================================================================
    // ASSETS
    // =====================================================================================

    public static string Assets()
    {
        Log.Clear();
        AssetDatabase.Refresh();

        // Bars: a pressed ingot is worth half again as much.
        var barColors = new Dictionary<string, Color>
        {
            ["Iron"] = new(0.78f, 0.82f, 0.9f), ["Aluminum"] = new(0.8f, 0.92f, 1f), ["Copper"] = new(1f, 0.58f, 0.24f), ["Steel"] = new(0.34f, 0.43f, 0.66f),
        };
        var barValues = new Dictionary<string, int> { ["Iron"] = 40, ["Aluminum"] = 60, ["Copper"] = 90, ["Steel"] = 135 };
        foreach (string m in Metals)
            ArtAssets.Set(ArtAssets.LoadOrCreate<ItemDefinition>($"{DataDir}/Items/Item_{m}Bar.asset"), ("id", m.ToLowerInvariant() + "_bar"), ("displayName", m + " Bar"),
                ("color", barColors[m]), ("stackHeight", 0.13f), ("baseValue", barValues[m]));

        // Sounds: the press slams (a heavy landing, pitched), the truck sounds its horn when an order is complete.
        CopySfx("Sfx_GiantLand", "Sfx_PressSlam", new Vector2(0.95f, 1.08f), 0.42f);
        CopySfx("Sfx_ShipHorn", "Sfx_TruckHorn", new Vector2(0.6f, 0.64f), 0.8f);

        // The Press: four recipes on one machine.
        var press = ArtAssets.LoadOrCreate<MachineDefinition>(DataDir + "/Factory/Machine_Press.asset");
        ArtAssets.Set(press, ("id", "press"), ("displayName", "Industrial Press"), ("input", Item("IronIngot")), ("output", Item("IronBar")),
            ("cycleSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_PressSlam.asset")), ("outputSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_IngotOut.asset")));
        var so = new SerializedObject(press);
        so.FindProperty("outputMix").arraySize = 0;
        var recipes = so.FindProperty("recipes");
        recipes.arraySize = Metals.Length;
        for (int i = 0; i < Metals.Length; i++)
        {
            recipes.GetArrayElementAtIndex(i).FindPropertyRelative("input").objectReferenceValue = Item(Metals[i] + "Ingot");
            recipes.GetArrayElementAtIndex(i).FindPropertyRelative("output").objectReferenceValue = Item(Metals[i] + "Bar");
        }

        int[] capacity = { 20, 26, 32, 40, 50 };
        float[] cycle = { 0.8f, 0.68f, 0.57f, 0.47f, 0.39f };
        long[] cost = { 0, 2500, 5000, 9000, 16000 };
        var levels = so.FindProperty("levels");
        levels.arraySize = cycle.Length;
        for (int i = 0; i < cycle.Length; i++)
        {
            var e = levels.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("inputCapacity").intValue = capacity[i];
            e.FindPropertyRelative("cycleTime").floatValue = cycle[i];
            e.FindPropertyRelative("inputsPerCycle").intValue = 1;
            e.FindPropertyRelative("outputsPerCycle").intValue = 1;
            e.FindPropertyRelative("upgradeCost").longValue = cost[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(press);

        // Bar Storage.
        var bars = ArtAssets.LoadOrCreate<StorageDefinition>(DataDir + "/Factory/Storage_Bars.asset");
        ArtAssets.Set(bars, ("id", "bar_storage"), ("displayName", "Bar Storage"),
            ("icon", new SerializedObject(Load<StorageDefinition>(DataDir + "/Factory/Storage_IngotRack.asset")).FindProperty("icon").objectReferenceValue));
        var bso = new SerializedObject(bars);
        var blevels = bso.FindProperty("levels");
        (int capacity, float interval, int cost)[] rows = { (48, 0.06f, 0), (72, 0.05f, 2000), (110, 0.05f, 4500), (160, 0.04f, 9000) };
        blevels.arraySize = rows.Length;
        for (int i = 0; i < rows.Length; i++)
        {
            blevels.GetArrayElementAtIndex(i).FindPropertyRelative("capacity").intValue = rows[i].capacity;
            blevels.GetArrayElementAtIndex(i).FindPropertyRelative("pickupInterval").floatValue = rows[i].interval;
            blevels.GetArrayElementAtIndex(i).FindPropertyRelative("upgradeCost").intValue = rows[i].cost;
        }

        bso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(bars);

        // The build.
        var hall = Load<ExpansionDefinition>(DataDir + "/World/Expansion_FurnaceHall.asset");
        var build = ArtAssets.LoadOrCreate<ExpansionDefinition>(DataDir + "/World/Expansion_Press.asset");
        ArtAssets.Set(build, ("id", "build_press"), ("displayName", "Industrial Press"), ("teaser", "METAL BARS"),
            ("openSfx", new SerializedObject(hall).FindProperty("openSfx").objectReferenceValue));
        SetLong(build, "cost", 9000);

        // Orders. Shares are relative; amounts come from the truck's bed (75% of it), so they grow with the dock.
        var contracts = new List<Object>
        {
            Contract("Iron", "IRON ORDER", 1f, 1, 1.4f, ("Iron", 1f)),
            Contract("Aluminum", "ALUMINUM ORDER", 1.05f, 1, 1.1f, ("Aluminum", 1f)),
            Contract("Copper", "COPPER ORDER", 1.1f, 1, 1f, ("Copper", 1f)),
            Contract("Steel", "STEEL ORDER", 1.15f, 1, 0.8f, ("Steel", 1f)),
            Contract("Builder", "BUILDER'S ORDER", 1.2f, 1, 0.8f, ("Iron", 3f), ("Steel", 2f)),
            Contract("Mixed", "MIXED ORDER", 1.3f, 2, 0.8f, ("Iron", 8f), ("Aluminum", 5f), ("Copper", 4f), ("Steel", 3f)),
        };
        var dock = Load<TruckBayDefinition>(DataDir + "/Factory/TruckBay_Plant.asset");
        ArtAssets.SetArray(dock, "accepts", Metals.Select(m => (Object)Item(m + "Bar")).ToArray());
        ArtAssets.SetArray(dock, "contracts", contracts.ToArray());
        ArtAssets.Set(dock, ("contractFill", 0.75f), ("contractSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_TruckHorn.asset")));

        UpsertCatalog(("build_press", 9, false, ""), ("press", 9, true, "PRESS"), ("bar_storage", 9, true, "PRESS"), ("loader", 9, false, ""));

        // The Loader arrives with the Press (it feeds it and loads the truck), so it costs less than the dock-era hire.
        var loader = Load<WorkerDefinition>(DataDir + "/Workers/Worker_Loader.asset");
        var lso = new SerializedObject(loader);
        var costs = lso.FindProperty("hireCosts");
        costs.arraySize = 2;
        costs.GetArrayElementAtIndex(0).longValue = 3000;
        costs.GetArrayElementAtIndex(1).longValue = 8000;
        lso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(loader);

        Tasks();
        AssetDatabase.SaveAssets();
        Log.AppendLine("R3 data: 4 bars, Press (4 recipes), Bar Storage, build, 6 contracts, dock, catalog, tasks");
        return Log.ToString();
    }

    static TruckContractDefinition Contract(string name, string title, float reward, int minLevel, float weight, params (string metal, float share)[] lines)
    {
        var c = ArtAssets.LoadOrCreate<TruckContractDefinition>($"{DataDir}/Factory/Contracts/Contract_{name}.asset");
        ArtAssets.Set(c, ("id", "contract_" + name.ToLowerInvariant()), ("displayName", title), ("rewardMultiplier", reward), ("minLevel", minLevel), ("weight", weight));
        var so = new SerializedObject(c);
        var p = so.FindProperty("lines");
        p.arraySize = lines.Length;
        for (int i = 0; i < lines.Length; i++)
        {
            p.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue = Item(lines[i].metal + "Bar");
            p.GetArrayElementAtIndex(i).FindPropertyRelative("share").floatValue = lines[i].share;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(c);
        return c;
    }

    static void CopySfx(string from, string to, Vector2 pitch, float volume)
    {
        string path = $"{DataDir}/Audio/{to}.asset";
        if (Load<SfxDefinition>(path, false) == null) AssetDatabase.CopyAsset($"{DataDir}/Audio/{from}.asset", path);
        ArtAssets.Set(Load<SfxDefinition>(path), ("pitchRange", pitch), ("volume", volume));
    }

    static void Tasks()
    {
        string dir = DataDir + "/Progression/Tasks";
        var chain = Load<TaskChain>(DataDir + "/Progression/TaskChain_Area1.asset");
        var so = new SerializedObject(chain);
        var main = so.FindProperty("mainTasks");
        var list = new List<Object>();
        for (int i = 0; i < main.arraySize; i++) list.Add(main.GetArrayElementAtIndex(i).objectReferenceValue);
        list.RemoveAll(t => t == null || (t is TaskDefinition d && d.Id.StartsWith("r3_")));

        TaskDefinition Task(string id, string title, TaskType type, string target, int amount, int cash, int xp)
        {
            var t = ArtAssets.LoadOrCreate<TaskDefinition>($"{dir}/Task_{id}.asset");
            ArtAssets.Set(t, ("id", id), ("title", title), ("category", (int)TaskCategory.Main), ("type", (int)type), ("targetId", target), ("amount", amount), ("rewardCash", cash),
                ("rewardXp", xp), ("rewardPremium", 0));
            return t;
        }

        Object Pull(string id)
        {
            var t = list.FirstOrDefault(x => x is TaskDefinition d && d.Id == id);
            if (t == null) Log.AppendLine("!! chain has no " + id);
            else list.Remove(t);
            return t;
        }

        // After the last furnace: the Press, its Loader (hired first: a lone player feeding a machine was the slow stretch
        // of the R2 run), the first bars, then the dock and the first loads. The old dock tasks move up to here.
        var loader = Pull("t44_loader");
        var level10 = Pull("t41_level10");
        var dock = Pull("t42_dock");
        var ship = Pull("t43_ship");
        ArtAssets.Set(Load<TaskDefinition>($"{dir}/Task_t43_ship.asset"), ("title", "Load {0} bars on the truck"), ("amount", 24));
        ArtAssets.Set(Load<TaskDefinition>($"{dir}/Task_t49_ship_ingots.asset"), ("title", "Load {0} bars on the truck"));
        int at = list.FindIndex(t => t is TaskDefinition d && d.Id == "r2_smelt_steel");
        if (at < 0)
        {
            Log.AppendLine("!! chain has no r2_smelt_steel: run R2_Build.Assets first");
            return;
        }

        var block = new Object[]
        {
            Task("r3_press", "Build the Industrial Press", TaskType.ReachUpgradeLevel, "build_press", 1, 0, 250), loader,
            Task("r3_bars", "Press {0} metal bars", TaskType.ProcessItems, "press", 20, 1000, 120), level10, dock, ship,
        }.Where(t => t != null).ToArray();
        list.InsertRange(at + 1, block);
        ArtAssets.SetArray(chain, "mainTasks", list.ToArray());
        Log.AppendLine($"tasks: chain has {list.Count}");
    }

    // =====================================================================================
    // PREFABS
    // =====================================================================================

    public static string Prefabs()
    {
        Log.Clear();
        // Bars: a trapezoid bar with a gold stamp plate, one mesh, four metals.
        var k = new MeshKit();
        k.Prism(0, Vector3.zero, new[] { new Vector2(-0.1f, -0.055f), new Vector2(0.1f, -0.055f), new Vector2(0.072f, 0.055f), new Vector2(-0.072f, 0.055f) }, 0.5f, 0.012f);
        k.Box(1, new Vector3(0f, 0.058f, 0f), new Vector3(0.09f, 0.012f, 0.2f), 0.003f);
        var mesh = ArtAssets.SaveMesh("R3_Items", "Bar", k, 2);
        var stamp = Mat("ItemBarStamp", new Color(1f, 0.8f, 0.25f), 0.7f, 0.35f, Detail.Metal);
        var colors = new Dictionary<string, (Color main, Color bright)>
        {
            ["Iron"] = (new Color(0.74f, 0.78f, 0.86f), new Color(0.84f, 0.88f, 0.95f)), ["Aluminum"] = (new Color(0.76f, 0.9f, 1f), new Color(0.88f, 0.96f, 1f)),
            ["Copper"] = (new Color(0.98f, 0.54f, 0.2f), new Color(1f, 0.66f, 0.32f)), ["Steel"] = (new Color(0.3f, 0.38f, 0.6f), new Color(0.42f, 0.52f, 0.76f)),
        };
        foreach (string m in Metals)
        {
            string path = $"{PrefabDir}/Items/Item_{m}Bar.prefab";
            if (Load<GameObject>(path, false) == null) AssetDatabase.CopyAsset(PrefabDir + "/Items/Item_IronIngot.prefab", path);
            var main = Mat($"ItemBar{m}", colors[m].main, 0.7f, 0.3f, Detail.Metal);
            var bright = Mat($"ItemBar{m}Hi", colors[m].bright, 0.72f, 0.3f, Detail.Metal);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.name = $"Item_{m}Bar";
                var renderer = root.GetComponentInChildren<MeshRenderer>(true);
                renderer.transform.localPosition = Vector3.zero;
                renderer.transform.localRotation = Quaternion.identity;
                renderer.transform.localScale = Vector3.one;
                renderer.GetComponent<MeshFilter>().sharedMesh = mesh;
                renderer.sharedMaterials = new[] { main, stamp };
                var item = root.GetComponent<WorldItem>();
                ArtAssets.Set(item, ("halfHeight", 0.065f));
                ArtAssets.SetArray(item, "meshVariants", new Object[0]);
                ArtAssets.SetArray(item, "materialVariants", new Object[] { main, bright });
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            var prefab = Load<GameObject>(path);
            ArtAssets.Set(Item(m + "Bar"), ("prefab", prefab.GetComponent<WorldItem>()), ("icon", ArtIcons.Render($"Icon_{m}Bar", prefab, new Vector3(40f, 35f, 0f))));
        }

        // Press prefab: the furnace prefab's components (machine, visuals, hopper, pad, label, VFX), rewired.
        string pressPath = PrefabDir + "/Stations/Press.prefab";
        if (Load<GameObject>(pressPath, false) == null) AssetDatabase.CopyAsset(PrefabDir + "/Stations/Furnace.prefab", pressPath);
        var press = PrefabUtility.LoadPrefabContents(pressPath);
        try
        {
            press.name = "Press";
            var body = press.transform.Find("Body");
            var output = press.transform.Find("OutputPoint");
            output.localPosition = new Vector3(0f, 0.74f, -2.2f);
            var sparks = press.transform.Find("VFX_IngotSparks");
            if (sparks != null) sparks.localPosition = new Vector3(0f, 1.7f, 0f);
            body.Find("Intake").localPosition = new Vector3(-2.62f, 1.5f, 0f);
            var pile = body.Find("HopperPile");
            pile.localPosition = new Vector3(-2.62f, 0.88f, 0f);
            ArtAssets.Set(pile.GetComponent<ItemPile>(), ("columns", 2), ("rows", 3), ("cellSize", new Vector3(0.4f, 0.22f, 0.4f)));
            ArtAssets.Set(press.GetComponent<Machine>(), ("definition", Load<MachineDefinition>(DataDir + "/Factory/Machine_Press.asset")), ("labelTitle", "PRESS"), ("outputPoint", output));
            var pad = press.transform.Find("InputPad");
            pad.localPosition = PressPad - PressPos;
            pad.localRotation = Quaternion.identity;
            var padIcon = press.transform.Find("PadIcon");
            if (padIcon != null) Object.DestroyImmediate(padIcon.gameObject);
            if (press.TryGetComponent(out BoxCollider box))
            {
                box.center = new Vector3(0f, 1.3f, 0f);
                box.size = new Vector3(4.6f, 2.6f, 3.5f);
            }

            var label = press.transform.Find("StationLabel");
            if (label != null)
            {
                label.localPosition = new Vector3(0f, 5.4f, -0.6f);
                label.localScale = Vector3.one;
            }

            PrefabUtility.SaveAsPrefabAsset(press, pressPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(press);
        }

        AssetDatabase.SaveAssets();
        Log.AppendLine("bar prefabs and Press prefab. Next: Art_Machines.Prefabs, Art_Machines.Icons, R3_Build.Icons.");
        return Log.ToString();
    }

    /// <summary>After Art_Machines.Icons: the build shows the Press.</summary>
    public static string Icons()
    {
        ArtAssets.Set(Load<ExpansionDefinition>(DataDir + "/World/Expansion_Press.asset"), ("icon", Load<MachineDefinition>(DataDir + "/Factory/Machine_Press.asset").Icon));
        AssetDatabase.SaveAssets();
        return "press build icon set";
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
        bool contentActive = content.Find("Floor_Hall").gameObject.activeSelf;

        // The orange container stood where the Bar Storage goes.
        foreach (var t in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Where(t => t.name == "Prop_Container_Orange").ToArray())
            if (t != null && t.position.x > 66f && t.position.z < 20f) Object.DestroyImmediate(t.gameObject);

        // The Loader's hire tile lives in the Press plot (it was the dock's): keep it across a rebuild, or make it anew.
        var old = content.Find("PressPlot");
        var loaderTile = gameplay.Find("TruckDock/Content/Tile_Loader");
        if (loaderTile == null && old != null) loaderTile = old.Find("Content/Tile_Loader");
        if (loaderTile == null)
        {
            loaderTile = ((GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PrefabDir + "/Tiles/Tile_Purchase.prefab"), content)).transform;
            loaderTile.name = "Tile_Loader";
            ArtAssets.Set(loaderTile.GetComponent<PurchaseTile>(), ("upgradeId", "loader"));
            ArtAssets.Set(loaderTile.gameObject.AddComponent<GuideAnchor>(), ("id", "tile/loader"));
        }

        loaderTile.SetParent(content, true);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var plot = Empty("PressPlot", content, Vector3.zero);
        plot.gameObject.SetActive(contentActive);
        var locked = Empty("LockedOnly", plot, Vector3.zero);
        Foundation(locked);
        var plotContent = Empty("Content", plot, Vector3.zero);
        var focus = Empty("Focus", plot, new Vector3(PressPos.x, 0f, PressPos.z - 3f));

        // Bar Storage first (the belt needs it), pad on its east side toward the truck.
        var storageGo = ((GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PrefabDir + "/Stations/Storage.prefab"), plotContent)).transform;
        storageGo.name = "BarStorage";
        storageGo.SetPositionAndRotation(BarStoragePos, Quaternion.Euler(0f, 180f, 0f));
        var storage = storageGo.GetComponent<Storage>();
        ArtAssets.Set(storage, ("definition", Load<StorageDefinition>(DataDir + "/Factory/Storage_Bars.asset")), ("stationId", "bar_storage"), ("labelTitle", "BARS"), ("levelSource", null));
        ArtAssets.SetArray(storageGo.Find("Pile").GetComponent<ItemPile>(), "accepted", Metals.Select(m => (Object)Item(m + "Bar")).ToArray());
        var storageLabel = storageGo.GetComponentInChildren<StationLabel>(true);
        if (storageLabel != null)
        {
            storageLabel.transform.position = BarStoragePos + new Vector3(0f, 3f, 1.2f);
            storageLabel.transform.localScale = Vector3.one * 0.9f;
        }

        var pressGo = ((GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PrefabDir + "/Stations/Press.prefab"), plotContent)).transform;
        pressGo.name = "Press";
        pressGo.SetPositionAndRotation(PressPos, Quaternion.identity);
        var press = pressGo.GetComponent<Machine>();
        var belt = BuildConveyor(plotContent, "Conveyor_Bars", new Vector3(PressPos.x, 0f, PressPos.z - 2.25f), new Vector3(PressPos.x, 0f, BarStoragePos.z + 1.7f), storage);
        ArtAssets.Set(press, ("outputTarget", belt));

        // Boost pad for the Press.
        var boost = ((GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PrefabDir + "/Tiles/Tile_Boost.prefab"), plotContent)).transform;
        boost.name = "Tile_BoostPress";
        boost.position = new Vector3(65.6f, 0f, 20f);
        ArtAssets.Set(boost.GetComponent<OverdriveTile>(), ("machine", press));
        ArtAssets.Set(boost.gameObject.AddComponent<GuideAnchor>(), ("station", press), ("role", "boost"));

        // Build tile where the Press's pad will be.
        var tile = ((GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PrefabDir + "/Tiles/Tile_Purchase.prefab"), locked)).transform;
        tile.name = "Tile_BuildPress";
        tile.position = PressPad;
        ArtAssets.Set(tile.GetComponent<PurchaseTile>(), ("upgradeId", "build_press"));
        ArtAssets.Set(tile.gameObject.AddComponent<GuideAnchor>(), ("id", "tile/build_press"));

        var expansion = plot.gameObject.AddComponent<Expansion>();
        EditorUtility.CopySerialized(hall.GetComponent<Expansion>(), expansion);
        var eso = new SerializedObject(expansion);
        eso.FindProperty("definition").objectReferenceValue = Load<ExpansionDefinition>(DataDir + "/World/Expansion_Press.asset");
        eso.FindProperty("barriers").arraySize = 0;
        eso.FindProperty("contentRoot").objectReferenceValue = plotContent;
        eso.FindProperty("lockedOnly").objectReferenceValue = locked.gameObject;
        eso.FindProperty("area").boundsValue = new Bounds(new Vector3(PressPos.x, 0f, 20.6f), new Vector3(5.2f, 4f, 9.4f));
        eso.FindProperty("focusPoint").objectReferenceValue = focus;
        eso.FindProperty("materialSwaps").arraySize = 0;
        eso.ApplyModifiedPropertiesWithoutUndo();

        // The Loader: hired on a tile that appears with the Press; it feeds the Press and loads the truck.
        loaderTile.SetParent(plotContent, true);
        loaderTile.position = new Vector3(63f, 0f, 18.6f);

        var route = gameplay.Find("Workers/Sites/LoaderRoute");
        var truckPad = gameplay.Find("TruckDock/Content/TruckBay/Pad").GetComponent<TransferPad>();
        if (route != null)
        {
            route.position = new Vector3(72.6f, 0f, 20.4f);
            var r = route.GetComponent<PorterRoute>();
            ArtAssets.SetArray(r, "pickupPads", new Object[] { content.Find("IngotRack/WithdrawPad").GetComponent<TransferPad>(), storageGo.Find("WithdrawPad").GetComponent<TransferPad>() });
            ArtAssets.Set(r, ("dropoff", pressGo.Find("InputPad").GetComponent<TransferPad>()));
            ArtAssets.SetArray(r, "extraDropoffs", new Object[] { truckPad });
        }
        else Log.AppendLine("!! no LoaderRoute");

        // Room for the Press: the Dockyard tile moves east, the Smelter tile north-east.
        Move(content, "Tile_Dockyard", new Vector3(73.7f, 0f, 24f));
        Move(content, "Tile_Smelter", new Vector3(73.9f, 0f, 30.9f));   // north of the ground the Dockyard sign covers

        // Saved stock is restored by item id.
        var pool = scene.GetRootGameObjects().First(g => g.name == "_Systems").GetComponentInChildren<ItemPool>(true);
        var poolSo = new SerializedObject(pool);
        var catalog = poolSo.FindProperty("catalog");
        var items = new List<Object>();
        for (int i = 0; i < catalog.arraySize; i++) items.Add(catalog.GetArrayElementAtIndex(i).objectReferenceValue);
        foreach (string m in Metals)
            if (!items.Contains(Item(m + "Bar"))) items.Add(Item(m + "Bar"));
        ArtAssets.SetArray(pool, "catalog", items.ToArray());

        ContractCard(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("scene: Press plot, bar belt, Bar Storage, Loader route, contract card. Next: Art_Machines.Scene, Env_Build.All, R1_Build.Bake, UI_Build.Apply.");
        return Log.ToString();
    }

    /// <summary>What stands in the Press's place before it is bought.</summary>
    static void Foundation(Transform parent)
    {
        var concrete = Mat("MachConcrete", new Color(0.72f, 0.7f, 0.67f), 0.15f, 0f, Detail.Concrete, 0.35f);
        var dark = Mat("MachDark", new Color(0.17f, 0.18f, 0.2f), 0.35f, 0.1f, Detail.Metal, 0.8f);
        var yellow = Mat("MachYellow", new Color(1f, 0.76f, 0.1f), 0.5f, 0.05f, Detail.Paint, 0.7f);
        var blue = Mat("PressBlue", new Color(0.13f, 0.4f, 0.64f), 0.45f, 0.15f, Detail.Paint, 0.7f);
        var k = new MeshKit();
        k.Box(0, new Vector3(0f, 0.12f, 0f), new Vector3(5f, 0.24f, 3.9f), 0.06f);
        k.Box(1, new Vector3(0f, 0.255f, 0f), new Vector3(4.4f, 0.03f, 3.3f), 0.01f);
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                k.Cylinder(2, new Vector3(sx * 2.25f, 0.65f, sz * 1.7f), 0.1f, 0.85f, 10);
                k.Cylinder(1, new Vector3(sx * 2.25f, 1.1f, sz * 1.7f), 0.13f, 0.06f, 10);
            }

        foreach (int sx in new[] { -1, 1 }) k.Box(1, new Vector3(sx * 0.7f, 1f, 0f), new Vector3(0.12f, 1.5f, 0.12f), 0.02f);
        k.Box(3, new Vector3(0f, 1.95f, 0f), new Vector3(2f, 0.9f, 0.16f), 0.05f);
        k.Box(1, new Vector3(0f, 1.95f, -0.09f), new Vector3(1.8f, 0.7f, 0.04f), 0.02f);
        var go = ArtAssets.Part("R3_Plots", "PressFoundation", k, new[] { concrete, dark, yellow, blue }, parent, PressPos);
        go.name = "Foundation";
        var icon = new GameObject("Icon");
        icon.transform.SetParent(parent, false);
        icon.transform.position = PressPos + new Vector3(0f, 1.95f, -0.12f);
        icon.transform.localScale = Vector3.one * 0.22f;
        icon.AddComponent<SpriteRenderer>().sprite = Item("IronBar").Icon;
    }

    /// <summary>HUD card for the truck's order, under the level badge. Kit sprites; ContractWidget fills it from events.</summary>
    static void ContractCard(Scene scene)
    {
        var hud = scene.GetRootGameObjects().First(g => g.name == "_UI").transform.Find("Canvas/HUD");
        var existing = hud.Find("ContractWidget");
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        var font = Load<TMP_FontAsset>(P + "/Art/Fonts/GROBOLD SDF.asset");
        var outline = Load<Material>(P + "/Art/Fonts/GROBOLD Outline.mat");
        Sprite Panel(string name) => AssetDatabase.LoadAllAssetsAtPath(KitDir + "/Kit_Panels.png").OfType<Sprite>().FirstOrDefault(s => s.name == name);
        Sprite IconA(string name) => AssetDatabase.LoadAllAssetsAtPath(KitDir + "/Kit_IconsA.png").OfType<Sprite>().FirstOrDefault(s => s.name == name);

        RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        TMP_Text Text(string name, Transform parent, Vector2 pos, Vector2 size, float fontSize, Color color, TextAlignmentOptions align, string sample)
        {
            var rt = Rect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            if (outline != null) t.fontSharedMaterial = outline;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = align;
            t.text = sample;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.enableAutoSizing = true;
            t.fontSizeMin = 12f;
            t.fontSizeMax = fontSize;
            return t;
        }

        var size = new Vector2(452f, 132f);
        var widget = Rect("ContractWidget", hud, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -290f), size);
        var card = Rect("Card", widget, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, size);
        var group = card.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        var punch = Rect("Punch", card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
        var back = Rect("Back", punch, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size).gameObject.AddComponent<Image>();
        back.sprite = Panel("Bar_Dark");
        back.type = Image.Type.Sliced;
        back.pixelsPerUnitMultiplier = 1.1f;
        back.raycastTarget = false;
        var content = Rect("Content", punch, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
        var icon = Rect("Icon", content, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(66f, -66f), new Vector2(96f, 96f)).gameObject.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        var title = Text("Title", content, new Vector2(124f, -14f), new Vector2(304f, 38f), 30f, Color.white, TextAlignmentOptions.Left, "STEEL ORDER");

        const float trackH = 38f;
        var track = Rect("Track", content, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(124f, -56f), new Vector2(190f, trackH)).gameObject.AddComponent<Image>();
        track.sprite = Load<Sprite>(KitDir + "/Kit_BarTrack.png");
        track.type = Image.Type.Sliced;
        track.pixelsPerUnitMultiplier = 47f / trackH;
        track.raycastTarget = false;
        var fillRt = Rect("Fill", track.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        float inset = trackH / 47f;
        fillRt.offsetMin = new Vector2(10f * inset, 8.5f * inset);
        fillRt.offsetMax = new Vector2(-10f * inset, -9.5f * inset);
        var fill = fillRt.gameObject.AddComponent<Image>();
        fill.sprite = Load<Sprite>(KitDir + "/Kit_BarFill.png");
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillAmount = 0.5f;
        fill.color = new Color(0.35f, 0.85f, 0.4f);
        fill.raycastTarget = false;
        var progress = Text("Progress", content, new Vector2(124f, -56f), new Vector2(190f, trackH), 24f, Color.white, TextAlignmentOptions.Center, "12 / 24");
        var coin = Rect("Coin", content, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(344f, -75f), new Vector2(40f, 40f)).gameObject.AddComponent<Image>();
        coin.sprite = IconA("Icon_Cash");
        coin.preserveAspect = true;
        coin.raycastTarget = false;
        var reward = Text("Reward", content, new Vector2(366f, -56f), new Vector2(80f, trackH), 28f, new Color(0.62f, 1f, 0.5f), TextAlignmentOptions.Left, "1.2K");
        var hint = Text("Hint", content, new Vector2(124f, -98f), new Vector2(304f, 26f), 18f, new Color(1f, 0.93f, 0.75f), TextAlignmentOptions.Left, "LOAD THE TRUCK");
        _ = hint;

        var component = widget.gameObject.AddComponent<ContractWidget>();
        ArtAssets.Set(component, ("root", card), ("group", group), ("icon", icon), ("title", title), ("progress", progress), ("fill", fill), ("reward", reward), ("punchRoot", punch));
        // Right after the level badge in the HUD order, so panels opened later draw over it.
        var badge = hud.Find("LevelBadge");
        if (badge != null) widget.SetSiblingIndex(badge.GetSiblingIndex() + 1);
    }

    static Conveyor BuildConveyor(Transform parent, string name, Vector3 from, Vector3 to, MonoBehaviour destination)
    {
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

    static void UpsertCatalog(params (string id, int level, bool inPanel, string group)[] entries)
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

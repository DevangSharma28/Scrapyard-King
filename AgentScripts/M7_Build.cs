using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ScrapYardKing.Economy;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Factory;
using ScrapYardKing.Feedback;
using ScrapYardKing.Harvest;
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
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static ScrapYardKing.EditorTools.Art.ArtMaterials;

// Milestone 7 builder: worker specialisation and the Giant Scrap event.
//  - Operators: a control console beside the Crusher, Sorter and Furnace; a hired operator stands there and the machine
//    runs faster (Machine.Speed). One hire tile per machine, so the player chooses which bottleneck to staff.
//  - Sellers: work the customer line at the Sell Desk and the Metal Market (SellDesk.ServiceSpeed).
//  - Truck Dock + Loader: a loading dock in the plant's south-east corner. A truck backs in, takes ingots through a
//    deposit pad, leaves when full and pays for the load at a premium. The Loader carries ingots from the rack.
//  - Giant Scrap event: breaking heavy scrap charges it; a giant mining truck drops under the tower crane in the Heavy
//    Scrap Yard, with a HUD boss bar, a fight camera, a final destruction sequence and a cash bonus.
// All art is built with ArtKit (no Kenney). Entry points, in order: Assets, Prefabs, then Art_Characters.Build (gives the
// three new workers their look and icons), then Scene. Incremental like M3–M6: edits the existing scene and prefabs.
public static class M7_Build
{
    const string P = "Assets/_Project";
    const string MatDir = P + "/Art/Materials";
    const string UiDir = P + "/Art/UI";
    const string FontDir = P + "/Art/Fonts";
    const string DataDir = P + "/Data";
    const string PrefabDir = P + "/Prefabs";
    const string AudioDir = "Assets/ThirdParty/Kenney/Audio";
    const string ScenePath = P + "/Scenes/Area1_OldScrapYard.unity";
    const string MeshModel = "M7";

    static readonly Color Navy = new(0.1f, 0.15f, 0.32f);

    // ---------- layout (blueprint frame: origin south-west, +X east, +Z north) ----------
    // Operator posts: where the operator stands; the console sits 0.75 m in front, toward the machine.
    static readonly Vector3 CrusherPost = new(22.3f, 0f, 23.7f), CrusherPos = new(24f, 0f, 27f);
    static readonly Vector3 SorterPost = new(53.7f, 0f, 24.5f), SorterPos = new(50f, 0f, 27.4f);
    static readonly Vector3 FurnacePost = new(69.6f, 0f, 26f), FurnacePos = new(67f, 0f, 31.4f);
    // Sellers work the head of the customer line, on the street side of the counter (west of the queue; customers leave east).
    static readonly Vector3 YardSellerPost = new(30.75f, 0f, 10.95f), YardQueueHead = new(32f, 0f, 10.9f);
    static readonly Vector3 MarketSellerPost = new(58.75f, 0f, 10.95f), MarketQueueHead = new(60f, 0f, 10.9f);
    static readonly Vector3 YardSellerTile = new(28.4f, 0f, 13.7f), MarketSellerTile = new(56.4f, 0f, 13.2f);
    // Truck Dock: the plant's south-east corner. The bay root sits on the kerb line; the truck parks on the road shoulder.
    static readonly Vector3 DockRoot = new(70.8f, 0f, 10.5f);
    const float DockWest = 66.3f, DockEast = 75.3f, DockNorth = 15f, FenceZ = 10.5f;
    static readonly Vector3 DockTile = new(67.7f, 0f, 13.1f), LoaderTile = new(67.3f, 0f, 16.5f);
    // Giant Scrap: under the tower crane at the north end of the Heavy Scrap Yard.
    static readonly Vector3 ArenaPos = new(17.5f, 0f, 64.5f);
    const float ArenaYaw = 90f;

    static readonly StringBuilder Log = new();

    static int C(PC c) => (int)c;

    public static string All()
    {
        var sb = new StringBuilder();
        sb.Append(Assets());
        sb.Append(Prefabs());
        sb.Append(Scene());
        sb.AppendLine("Run Art_Characters.Build to give the Operator, Seller and Loader their look and icons.");
        return sb.ToString();
    }

    // =====================================================================================
    // ASSETS
    // =====================================================================================

    public static string Assets()
    {
        Log.Clear();
        AssetDatabase.Refresh();

        // ---------- audio (Kenney CC0 clips already in the project) ----------
        Clips("TruckArrive", 0.55f, new Vector2(0.55f, 0.65f), "Impact/impactPlate_medium_000", "Impact/impactPlate_medium_001", "Impact/impactPlate_medium_002");
        Clips("TruckDepart", 0.5f, new Vector2(0.5f, 0.54f), "Interface/bong_001");
        Clips("TruckPayout", 0.75f, new Vector2(0.95f, 1.05f), "RPG/handleCoins", "RPG/handleCoins2");
        Clips("GiantArrive", 0.8f, new Vector2(0.8f, 0.8f), "Jingles/jingles_STEEL07");
        Clips("GiantLand", 1f, new Vector2(0.45f, 0.52f), "Impact/impactMetal_heavy_000", "Impact/impactMetal_heavy_002", "Impact/impactMetal_heavy_004");
        Clips("GiantHit", 0.5f, new Vector2(0.62f, 0.78f), "Impact/impactMining_000", "Impact/impactMining_001", "Impact/impactMining_002", "Impact/impactMining_003",
            "Impact/impactMining_004");
        Clips("GiantThroe", 0.85f, new Vector2(0.8f, 0.95f), "Impact/impactPunch_heavy_000", "Impact/impactPunch_heavy_001", "Impact/impactPunch_heavy_002");
        Clips("GiantBreak", 1f, new Vector2(0.5f, 0.58f), "Impact/impactMetal_heavy_001", "Impact/impactMetal_heavy_003");
        Clips("GiantDefeat", 0.8f, new Vector2(1.12f, 1.12f), "Jingles/jingles_STEEL02");

        var iron = Load<ItemDefinition>(DataDir + "/Items/Item_Iron.asset");
        var copper = Load<ItemDefinition>(DataDir + "/Items/Item_Copper.asset");
        var ironIngot = Load<ItemDefinition>(DataDir + "/Items/Item_IronIngot.asset");
        var copperIngot = Load<ItemDefinition>(DataDir + "/Items/Item_CopperIngot.asset");
        var scrapItem = Load<ItemDefinition>(DataDir + "/Items/Item_Scrap.asset");
        _ = iron;

        // ---------- specialists (blueprint: Operator runs one machine faster, Loader moves goods to the truck, Seller cuts queue time) ----------
        Specialist("OperatorCrusher", "operator_crusher", "Crusher Operator", WorkerRole.Operator, "CRUSHER RUNS x1.5", 1.5f, false, 3500);
        Specialist("OperatorSorter", "operator_sorter", "Sorter Operator", WorkerRole.Operator, "SORTER RUNS x1.5", 1.5f, false, 4500);
        Specialist("OperatorFurnace", "operator_furnace", "Furnace Operator", WorkerRole.Operator, "FURNACE RUNS x1.5", 1.5f, false, 6000);
        Specialist("SellerYard", "seller_yard", "Yard Seller", WorkerRole.Seller, "CUSTOMERS SERVED FASTER", 1.6f, true, 5000);
        Specialist("SellerMarket", "seller_market", "Market Seller", WorkerRole.Seller, "MARKET LINE MOVES FASTER", 1.6f, true, 7500);
        var loader = LoadOrCreate<WorkerDefinition>(DataDir + "/Workers/Worker_Loader.asset");
        SetMany(loader, ("id", "loader"), ("displayName", "Loader"), ("role", (int)WorkerRole.Loader), ("siteId", "loader"), ("tagline", "LOADS THE TRUCK"),
            ("moveSpeed", 3.8f), ("carryCapacity", 10), ("efficiency", 1f), ("pickupRadius", 1f), ("collectLooseItems", false), ("workBoost", 1f),
            ("spawnAtSite", false));
        SetLongArray(loader, "hireCosts", new long[] { 6000, 11000 });

        // ---------- Truck Dock: a bulk buyer for ingots. Premium price, but the cash only comes when the truck is full. ----------
        var bay = LoadOrCreate<TruckBayDefinition>(DataDir + "/Factory/TruckBay_Plant.asset");
        SetMany(bay, ("id", "truck_bay"), ("displayName", "Truck Dock"), ("truckSpeed", 10f), ("firstArrivalDelay", 1.5f),
            ("arriveSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_TruckArrive.asset")), ("departSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_TruckDepart.asset")),
            ("payoutSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_TruckPayout.asset")));
        SetArray(bay, "accepts", new Object[] { ironIngot, copperIngot });
        SetStructArray(bay, "levels", new[]
        {
            // One layer on the truck bed is 16 crates: every level adds half a layer or more, so a full truck looks full.
            Lv(("capacity", 16), ("payoutMultiplier", 1.4f), ("returnDelay", 8f), ("upgradeCost", 0)),
            Lv(("capacity", 24), ("payoutMultiplier", 1.55f), ("returnDelay", 7f), ("upgradeCost", 5000)),
            Lv(("capacity", 32), ("payoutMultiplier", 1.7f), ("returnDelay", 6f), ("upgradeCost", 10000)),
            Lv(("capacity", 48), ("payoutMultiplier", 1.85f), ("returnDelay", 5f), ("upgradeCost", 18000)),
        });
        var dock = LoadOrCreate<ExpansionDefinition>(DataDir + "/World/Expansion_TruckDock.asset");
        SetMany(dock, ("id", "truck_dock"), ("displayName", "Truck Dock"), ("cost", 8000L), ("teaser", "BULK INGOT BUYER"),
            ("openSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_AreaOpen.asset")));

        // ---------- Giant Scrap (blueprint boss tier: 120+ pieces plus rare material) ----------
        var giant = LoadOrCreate<ScrapDefinition>(DataDir + "/Scrap/Scrap_GiantTruck.asset");
        SetMany(giant, ("id", "giant_truck"), ("displayName", "Giant Truck"), ("tier", 5), ("sizeClass", (int)ScrapSizeClass.Giant), ("maxHealth", 6000f),
            ("minCutPower", 40f), ("dropItem", scrapItem), ("dropAmount", new Vector2Int(140, 160)), ("partDropShare", 0.45f), ("dropLaunchSpeed", 8f),
            ("dropSpread", 3.2f), ("dropBurstDuration", 1.6f), ("rareDropItem", copper), ("rareDropChance", 1f), ("rareDropAmount", new Vector2Int(12, 18)),
            ("xpReward", 300), ("respawnDelay", 0f), ("hitSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_GiantHit.asset")),
            ("breakSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_GiantBreak.asset")), ("breakVfx", Vfx("VFX_BreakBurstHeavy")), ("breakVfxScale", 4f),
            ("breakShake", 1f), ("hitVfxScale", 1.8f), ("breakDelay", 1.5f), ("breakDelayBursts", 6), ("breakDelayVfx", Vfx("VFX_BreakBurst")),
            ("breakDelayVfxScale", 2.2f), ("breakDelaySfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_GiantThroe.asset")), ("breakDelayShake", 0.28f));
        var giantEvent = LoadOrCreate<GiantEventConfig>(DataDir + "/World/GiantEvent_HeavyYard.asset");
        SetMany(giantEvent, ("giant", giant), ("feederMinTier", 3), ("firstCharge", 3), ("repeatCharge", 8), ("cashBonus", 5000L), ("dropHeight", 14f),
            ("dropDuration", 0.7f), ("clearRadius", 6.5f), ("focusHold", 3.2f), ("landVfx", Vfx("VFX_SpawnDust")), ("landVfxScale", 5f), ("landShake", 0.9f),
            ("arriveSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_GiantArrive.asset")), ("landSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_GiantLand.asset")),
            ("cameraPullBack", 7f), ("fightRadius", 9f), ("defeatSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_GiantDefeat.asset")), ("defeatPunch", 0.8f));

        // ---------- unlocks: every M7 purchase is its own tile; only the dock's level-ups live in the panel ----------
        UpsertCatalog(Load<UpgradeCatalog>(DataDir + "/Progression/UpgradeCatalog.asset"),
            ("operator_crusher", 9, false, ""), ("operator_sorter", 9, false, ""), ("operator_furnace", 10, false, ""), ("seller_yard", 9, false, ""),
            ("seller_market", 10, false, ""), ("truck_dock", 10, false, ""), ("truck_bay", 10, true, "TRUCK DOCK"), ("loader", 10, false, ""));

        BuildTasks();
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    static void Specialist(string asset, string id, string name, WorkerRole role, string tagline, float boost, bool spawnAtSite, long cost)
    {
        var d = LoadOrCreate<WorkerDefinition>($"{DataDir}/Workers/Worker_{asset}.asset");
        SetMany(d, ("id", id), ("displayName", name), ("role", (int)role), ("siteId", id), ("tagline", tagline), ("moveSpeed", 3.6f), ("carryCapacity", 0),
            ("efficiency", 1f), ("pickupRadius", 1f), ("collectLooseItems", false), ("workBoost", boost), ("spawnAtSite", spawnAtSite));
        SetLongArray(d, "hireCosts", new[] { cost });
    }

    static ParticleSystem Vfx(string name) => Load<GameObject>($"{PrefabDir}/VFX/{name}.prefab")?.GetComponent<ParticleSystem>();

    /// <summary>Adds or replaces catalog entries by id and keeps everything else (earlier builders own the rest).</summary>
    static void UpsertCatalog(UpgradeCatalog catalog, params (string id, int level, bool inPanel, string group)[] entries)
    {
        var so = new SerializedObject(catalog);
        var p = so.FindProperty("entries");
        foreach (var (id, level, inPanel, group) in entries)
        {
            int index = -1;
            for (int i = 0; i < p.arraySize; i++)
                if (p.GetArrayElementAtIndex(i).FindPropertyRelative("upgradeId").stringValue == id) index = i;
            if (index < 0)
            {
                index = p.arraySize;
                p.arraySize++;
            }

            var e = p.GetArrayElementAtIndex(index);
            e.FindPropertyRelative("upgradeId").stringValue = id;
            e.FindPropertyRelative("unlockLevel").intValue = level;
            e.FindPropertyRelative("inPanel").boolValue = inPanel;
            e.FindPropertyRelative("group").stringValue = group;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalog);
    }

    static void BuildTasks()
    {
        string dir = DataDir + "/Progression/Tasks";
        var chain = Load<TaskChain>(DataDir + "/Progression/TaskChain_Area1.asset");
        var so = new SerializedObject(chain);
        var mainProp = so.FindProperty("mainTasks");
        var main = new List<Object>();
        for (int i = 0; i < mainProp.arraySize; i++) main.Add(mainProp.GetArrayElementAtIndex(i).objectReferenceValue);
        main.RemoveAll(t => t == null || IsM7Task(((TaskDefinition)t).Id));

        // The chain ended at t37_crush500. M7: staff a machine, meet the giant, open the dock, automate it, then the second giant.
        main.AddRange(new Object[]
        {
            Task(dir, "t38_operator", "Hire a crusher operator", TaskType.HireWorker, "operator_crusher", 1, 0, 150),
            Task(dir, "t39_giant", "Dismantle the Giant Truck", TaskType.BreakScrap, "giant_truck", 1, 1000, 400),
            Task(dir, "t40_seller", "Hire a yard seller", TaskType.HireWorker, "seller_yard", 1, 0, 150),
            Task(dir, "t41_level10", "Reach yard Lv.{0}", TaskType.ReachLevel, "", 10, 800, 0),
            Task(dir, "t42_dock", "Open the Truck Dock", TaskType.ReachUpgradeLevel, "truck_dock", 1, 0, 200),
            Task(dir, "t43_ship", "Load {0} ingots on the truck", TaskType.DeliverItems, "truck_bay", 32, 1200, 250),
            Task(dir, "t44_loader", "Hire a loader", TaskType.HireWorker, "loader", 1, 0, 150),
            Task(dir, "t45_operator_furnace", "Hire a furnace operator", TaskType.HireWorker, "operator_furnace", 1, 0, 200),
            Task(dir, "t46_serve", "Serve {0} customers", TaskType.ServeCustomers, "", 40, 2000, 300),
            Task(dir, "t47_giant2", "Dismantle another Giant Truck", TaskType.BreakScrap, "giant_truck", 1, 2500, 500),
        });
        SetArray(chain, "mainTasks", main.ToArray());
    }

    static bool IsM7Task(string id) => id.Length > 3 && id[0] == 't' && int.TryParse(id.Substring(1, 2), out int n) && n >= 38;

    static TaskDefinition Task(string dir, string id, string title, TaskType type, string target, long amount, long cash, int xp)
    {
        var t = LoadOrCreate<TaskDefinition>($"{dir}/Task_{id}.asset");
        SetMany(t, ("id", id), ("title", title), ("category", (int)TaskCategory.Main), ("type", (int)type), ("targetId", target), ("amount", amount),
            ("rewardCash", cash), ("rewardXp", xp), ("rewardPremium", 0));
        return t;
    }

    // =====================================================================================
    // PREFABS
    // =====================================================================================

    public static string Prefabs()
    {
        Log.Clear();
        Directory.CreateDirectory(PrefabDir + "/Props");

        // ---------- workers: skeletons copied from the Market Runner (a pad carrier); Art_Characters.Build dresses them ----------
        WorkerSkeleton("Worker_Loader", false, "Worker_Loader");
        WorkerSkeleton("Worker_Operator", true, "Worker_OperatorCrusher", "Worker_OperatorSorter", "Worker_OperatorFurnace");
        WorkerSkeleton("Worker_Seller", true, "Worker_SellerYard", "Worker_SellerMarket");

        BuildConsole();
        var bayPrefab = BuildTruckBay();
        BuildGiantTruck();

        // Icons: the dock truck for the dock tile and upgrade card.
        var iconHolder = new GameObject("TruckIcon");
        var truck = (GameObject)PrefabUtility.InstantiatePrefab(bayPrefab);
        PrefabUtility.UnpackPrefabInstance(truck, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var truckOnly = truck.transform.Find("Truck");
        truckOnly.SetParent(iconHolder.transform, false);
        truckOnly.localPosition = Vector3.zero;
        truckOnly.localRotation = Quaternion.identity;
        Object.DestroyImmediate(truck);
        var tmp = ArtAssets.SavePrefab(iconHolder, PrefabDir + "/Props/_IconTruck.prefab");
        var icon = ArtIcons.Render("Icon_TruckDock", tmp, new Vector3(20f, 215f, 0f), null, 0.8f);
        AssetDatabase.DeleteAsset(PrefabDir + "/Props/_IconTruck.prefab");
        ArtAssets.Set(Load<TruckBayDefinition>(DataDir + "/Factory/TruckBay_Plant.asset"), ("icon", icon));
        ArtAssets.Set(Load<ExpansionDefinition>(DataDir + "/World/Expansion_TruckDock.asset"), ("icon", icon));

        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    /// <summary>
    /// A worker prefab made from the Market Runner's: same body, agent and Worker component. Post workers (operator,
    /// seller) lose the carry stack and carrier brain and get a <see cref="PostBrain"/>.
    /// </summary>
    static void WorkerSkeleton(string name, bool post, params string[] definitions)
    {
        string path = $"{PrefabDir}/Workers/{name}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            AssetDatabase.CopyAsset($"{PrefabDir}/Workers/Worker_Runner.prefab", path);
            AssetDatabase.ImportAsset(path);
            Log.AppendLine("created " + path);
        }

        EditPrefab(path, root =>
        {
            root.name = name;
            if (!post) return;
            if (root.TryGetComponent(out PorterBrain porter)) Object.DestroyImmediate(porter);
            if (root.TryGetComponent(out ItemCollector collector)) Object.DestroyImmediate(collector);
            if (root.TryGetComponent(out CarryStack stack)) Object.DestroyImmediate(stack);
            var anchor = root.transform.Find("StackAnchor");
            if (anchor != null) Object.DestroyImmediate(anchor.gameObject);
            if (!root.TryGetComponent(out PostBrain _)) root.AddComponent<PostBrain>();
            SetMany(root.GetComponent<Worker>(), ("stack", null), ("collector", null));
        });

        var prefab = Load<GameObject>(path).GetComponent<Worker>();
        foreach (var d in definitions) SetMany(Load<WorkerDefinition>($"{DataDir}/Workers/{d}.asset"), ("prefab", prefab));
    }

    /// <summary>
    /// Operator console: a slanted control desk with buttons, a screen, a lever and a beacon, plus a rubber mat where the
    /// operator stands. Local frame: the operator stands on the mat at (0, 0, -0.75) facing +Z; the machine is beyond.
    /// </summary>
    static void BuildConsole()
    {
        var k = new MeshKit();
        k.Box(C(PC.Charcoal), new Vector3(0f, 0.055f, 0.05f), new Vector3(1.25f, 0.11f, 0.7f), 0.02f);
        k.Box(C(PC.Gray), new Vector3(0f, 0.46f, 0.1f), new Vector3(0.55f, 0.8f, 0.34f), 0.05f);
        k.Box(C(PC.Yellow), new Vector3(0f, 0.95f, 0.02f), new Vector3(1.2f, 0.3f, 0.62f), 0.07f, new Vector3(-18f, 0f, 0f));
        k.Push(new Vector3(0f, 1.095f, -0.03f), new Vector3(-18f, 0f, 0f));
        k.Box(C(PC.Charcoal), Vector3.zero, new Vector3(1.04f, 0.03f, 0.46f), 0.01f);
        k.Cylinder(C(PC.Red), new Vector3(-0.36f, 0.035f, -0.1f), 0.075f, 0.06f, 10, default, 0.014f);
        k.Cylinder(C(PC.Green), new Vector3(-0.16f, 0.03f, -0.1f), 0.055f, 0.045f, 10, default, 0.01f);
        k.Cylinder(C(PC.Blue), new Vector3(0.01f, 0.03f, -0.1f), 0.055f, 0.045f, 10, default, 0.01f);
        k.Box(C(PC.Mint), new Vector3(-0.17f, 0.02f, 0.09f), new Vector3(0.56f, 0.02f, 0.2f), 0.008f);
        k.Box(C(PC.Green), new Vector3(-0.3f, 0.034f, 0.09f), new Vector3(0.2f, 0.012f, 0.03f), 0.004f);
        k.Box(C(PC.Green), new Vector3(-0.08f, 0.034f, 0.12f), new Vector3(0.16f, 0.012f, 0.03f), 0.004f);
        k.Box(C(PC.Silver), new Vector3(0.34f, 0.025f, 0.01f), new Vector3(0.18f, 0.03f, 0.32f), 0.01f);
        k.Cylinder(C(PC.Silver), new Vector3(0.34f, 0.14f, -0.02f), 0.02f, 0.26f, 6, new Vector3(-22f, 0f, 0f), 0.004f);
        k.Sphere(C(PC.Red), new Vector3(0.34f, 0.27f, -0.07f), Vector3.one * 0.055f, 8, 5);
        k.Pop();
        // Back board: gauge, hazard strip, beacon.
        k.Box(C(PC.Gray), new Vector3(0f, 1.28f, 0.33f), new Vector3(1.2f, 0.52f, 0.08f), 0.03f);
        k.Cylinder(C(PC.White), new Vector3(-0.3f, 1.3f, 0.285f), 0.15f, 0.03f, 14, new Vector3(90f, 0f, 0f), 0.006f);
        k.Box(C(PC.Red), new Vector3(-0.3f, 1.34f, 0.265f), new Vector3(0.02f, 0.12f, 0.012f), 0.003f, new Vector3(0f, 0f, 35f));
        for (int i = 0; i < 4; i++)
            k.Box(C(i % 2 == 0 ? PC.Yellow : PC.Charcoal), new Vector3(0.1f + i * 0.13f, 1.3f, 0.288f), new Vector3(0.13f, 0.2f, 0.012f), 0.002f);
        k.Cylinder(C(PC.Charcoal), new Vector3(0.45f, 1.57f, 0.33f), 0.07f, 0.06f, 8, default, 0.01f);
        k.Cylinder(C(PC.Orange), new Vector3(0.45f, 1.66f, 0.33f), 0.06f, 0.13f, 8, default, 0.025f);
        // Cable to the machine and the operator's mat.
        k.Box(C(PC.Rubber), new Vector3(0.2f, 0.03f, 0.75f), new Vector3(0.09f, 0.05f, 0.9f), 0.02f);
        k.Box(C(PC.Rubber), new Vector3(0f, 0.012f, -0.75f), new Vector3(1.05f, 0.024f, 0.85f), 0.01f);
        foreach (int side in new[] { -1, 1 })
            k.Box(C(PC.Yellow), new Vector3(side * 0.49f, 0.016f, -0.75f), new Vector3(0.07f, 0.026f, 0.85f), 0.006f);

        var root = new GameObject("Prop_Console");
        var mesh = ArtAssets.SaveMesh(MeshModel, "Console", k.ToPaletteMesh("Console"));
        var go = ArtAssets.MeshObject("Mesh", root.transform, mesh, new[] { ArtPalette.Material });
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.7f, 0.12f);
        box.size = new Vector3(1.25f, 1.4f, 0.66f);
        ArtAssets.SavePrefab(root, PrefabDir + "/Props/Prop_Console.prefab");
        Log.AppendLine("built Prop_Console");
    }

    /// <summary>
    /// Truck Dock station. Root on the kerb line (plant to +Z, road to -Z): kerb, bollards, a ramp plate, the deposit pad,
    /// the cash pallet and pad, the label, and the truck itself (hull, six spinning wheels, cargo grid on the bed).
    /// </summary>
    static GameObject BuildTruckBay()
    {
        var root = new GameObject("TruckBay");
        var bay = root.AddComponent<TruckBay>();
        var art = new GameObject("Art").transform;
        art.SetParent(root.transform, false);

        // Kerb with hazard blocks, rubber bumpers toward the road, bollards with beacons, a steel ramp plate at the pad.
        var k = new MeshKit();
        float half = 4.5f;
        for (int i = 0; i < 18; i++)
            k.Box(C(i % 2 == 0 ? PC.Yellow : PC.Charcoal), new Vector3(-half + 0.25f + i * 0.5f, 0.14f, 0f), new Vector3(0.5f, 0.28f, 0.34f), 0.03f);
        foreach (float x in new[] { -2.6f, -0.2f, 2.2f }) k.Box(C(PC.Rubber), new Vector3(x, 0.3f, -0.22f), new Vector3(0.7f, 0.5f, 0.14f), 0.04f);
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * (half + 0.22f);
            k.Cylinder(C(PC.Yellow), new Vector3(x, 0.55f, 0f), 0.16f, 1.1f, 10, default, 0.03f);
            k.Cylinder(C(PC.Charcoal), new Vector3(x, 0.4f, 0f), 0.165f, 0.16f, 10, default, 0.01f);
            k.Cylinder(C(PC.Charcoal), new Vector3(x, 0.8f, 0f), 0.165f, 0.16f, 10, default, 0.01f);
            k.Cylinder(C(PC.Orange), new Vector3(x, 1.18f, 0f), 0.09f, 0.14f, 8, default, 0.03f);
        }

        k.Box(C(PC.Silver), new Vector3(-0.8f, 0.03f, 0.7f), new Vector3(2.5f, 0.05f, 1.0f), 0.015f);
        for (int i = 0; i < 5; i++) k.Box(C(PC.Gray), new Vector3(-0.8f, 0.058f, 0.32f + i * 0.19f), new Vector3(2.3f, 0.012f, 0.05f), 0.004f);
        var dockMesh = ArtAssets.SaveMesh(MeshModel, "Dock", k.ToPaletteMesh("Dock"));
        GameObjectUtility.SetStaticEditorFlags(ArtAssets.MeshObject("Dock", art, dockMesh, new[] { ArtPalette.Material }), StaticEditorFlags.BatchingStatic);

        // Level-up dressing: a floodlight mast (Lv.2) and a stack of ingot pallets with a second mast (Lv.3).
        var lv2 = new MeshKit();
        Mast(lv2, new Vector3(-half - 0.25f, 0f, 1.2f), 1);
        var lv2Go = ArtAssets.MeshObject("Lv2_Floodlight", root.transform, ArtAssets.SaveMesh(MeshModel, "DockLv2", lv2.ToPaletteMesh("DockLv2")),
            new[] { ArtPalette.Material });
        var lv3 = new MeshKit();
        Mast(lv3, new Vector3(half + 0.25f, 0f, 4.2f), -1);
        for (int i = 0; i < 2; i++)
        {
            float px = -3.7f + i * 1.25f;
            lv3.Box(C(PC.Wood), new Vector3(px, 0.07f, 3.9f), new Vector3(1.1f, 0.12f, 1.0f), 0.02f);
            for (int n = 0; n < 6; n++)
                lv3.Box(C(i == 0 ? PC.SteelBlue : PC.Orange), new Vector3(px - 0.26f + (n % 2) * 0.52f, 0.22f + (n / 2) * 0.2f, 3.9f), new Vector3(0.46f, 0.18f, 0.8f), 0.04f);
        }

        var lv3Go = ArtAssets.MeshObject("Lv3_Stock", root.transform, ArtAssets.SaveMesh(MeshModel, "DockLv3", lv3.ToPaletteMesh("DockLv3")),
            new[] { ArtPalette.Material });
        var levelVisuals = SetupLevelVisuals(root, (2, lv2Go), (3, lv3Go));

        // Pad, cash pile, cash pad and label reuse the Sell Desk's (same look and behaviour everywhere).
        var desk = Load<GameObject>(PrefabDir + "/Stations/SellDesk.prefab").transform;
        var pad = CloneChild(desk, "StockPad", root.transform, "Pad", new Vector3(-0.8f, 0f, 2.0f));
        SetMany(pad.GetComponent<TransferPad>(), ("target", bay), ("spillAfter", 0f));
        StationAnchor(pad, bay, "in");
        var cashPile = CloneChild(desk, "CashPile", root.transform, "CashPile", new Vector3(3.1f, 0f, 1.5f));
        var cashPad = CloneChild(desk, "CashPad", root.transform, "CashPad", new Vector3(3.1f, 0f, 3.4f));
        SetMany(cashPile.GetComponent<CashPile>(), ("pad", cashPad), ("stackRoot", cashPile.Find("Stack")));
        StationAnchor(cashPad, bay, "cash");
        var label = CloneChild(desk, "StationLabel", root.transform, "StationLabel", new Vector3(-0.8f, 3.3f, 1.3f));
        label.localScale = Vector3.one * 1.2f;

        // ---------- the truck (forward = +Z; it faces the way it leaves): a compact two-axle flatbed ----------
        var truck = new GameObject("Truck").transform;
        truck.SetParent(root.transform, false);
        // Bed centre (truck z = -0.7) lines up with the pad; the truck's side just touches the rubber bumpers.
        truck.localPosition = new Vector3(-0.1f, 0f, -1.4f);
        truck.localRotation = Quaternion.Euler(0f, 90f, 0f);
        var body = new GameObject("Body").transform;
        body.SetParent(truck, false);
        var h = new MeshKit();
        // Chassis, fuel tank, mud flaps.
        h.Box(C(PC.Charcoal), new Vector3(0f, 0.72f, 0.2f), new Vector3(1.5f, 0.28f, 5.3f), 0.05f);
        h.Cylinder(C(PC.Silver), new Vector3(1.0f, 0.62f, 0.35f), 0.28f, 0.9f, 12, new Vector3(90f, 0f, 0f), 0.05f);
        foreach (int side in new[] { -1, 1 }) h.Box(C(PC.Rubber), new Vector3(side * 1.08f, 0.5f, -2.3f), new Vector3(0.46f, 0.55f, 0.04f), 0.01f);
        // Cab: teal with a white band, big windscreen, visor, grille, lights, mirrors, exhaust stack.
        const float cabZ = 1.95f, noseZ = 2.8f;
        h.Box(C(PC.Teal), new Vector3(0f, 1.78f, cabZ), new Vector3(2.46f, 2.0f, 1.7f), 0.2f);
        h.Box(C(PC.White), new Vector3(0f, 1.3f, cabZ), new Vector3(2.5f, 0.24f, 1.72f), 0.04f);
        h.Box(C(PC.Glass), new Vector3(0f, 2.25f, noseZ), new Vector3(2.0f, 0.78f, 0.06f), 0.04f);
        h.Box(C(PC.Teal), new Vector3(0f, 2.74f, noseZ + 0.1f), new Vector3(2.2f, 0.1f, 0.3f), 0.03f);
        foreach (int side in new[] { -1, 1 })
        {
            h.Box(C(PC.Glass), new Vector3(side * 1.23f, 2.25f, cabZ + 0.1f), new Vector3(0.05f, 0.62f, 0.9f), 0.03f);
            h.Box(C(PC.Cream), new Vector3(side * 0.86f, 1.0f, noseZ + 0.01f), new Vector3(0.34f, 0.2f, 0.06f), 0.03f);
            h.Box(C(PC.Charcoal), new Vector3(side * 1.36f, 2.3f, noseZ - 0.15f), new Vector3(0.08f, 0.4f, 0.2f), 0.03f);
            h.Box(C(PC.Orange), new Vector3(side * 0.9f, 2.82f, cabZ + 0.45f), new Vector3(0.16f, 0.08f, 0.12f), 0.02f);
        }

        h.Box(C(PC.Charcoal), new Vector3(0f, 1.02f, noseZ + 0.01f), new Vector3(1.2f, 0.42f, 0.06f), 0.03f);
        for (int i = 0; i < 4; i++) h.Box(C(PC.Silver), new Vector3(0f, 0.88f + i * 0.09f, noseZ + 0.05f), new Vector3(1.08f, 0.03f, 0.02f), 0.005f);
        h.Box(C(PC.Silver), new Vector3(0f, 0.62f, noseZ + 0.12f), new Vector3(2.5f, 0.3f, 0.24f), 0.07f);
        h.Cylinder(C(PC.Silver), new Vector3(-1.05f, 2.4f, 0.98f), 0.09f, 2.2f, 8, default, 0.02f);
        h.Cylinder(C(PC.Charcoal), new Vector3(-1.05f, 3.5f, 0.98f), 0.11f, 0.22f, 8, default, 0.02f);
        // Flatbed: wooden deck, steel edge, headboard, low stake sides so the cargo stays readable.
        const float bedZ = -0.7f, bedLen = 3.4f;
        h.Box(C(PC.Wood), new Vector3(0f, 1.0f, bedZ), new Vector3(2.5f, 0.14f, bedLen), 0.03f);
        h.Box(C(PC.Charcoal), new Vector3(0f, 0.9f, bedZ), new Vector3(2.56f, 0.1f, bedLen + 0.06f), 0.02f);
        h.Box(C(PC.Teal), new Vector3(0f, 1.62f, bedZ + bedLen * 0.5f + 0.02f), new Vector3(2.5f, 1.1f, 0.1f), 0.03f);
        for (int i = 0; i < 4; i++) h.Box(C(PC.White), new Vector3(-0.9f + i * 0.6f, 1.62f, bedZ + bedLen * 0.5f - 0.04f), new Vector3(0.1f, 0.9f, 0.02f), 0.005f);
        foreach (int side in new[] { -1, 1 })
        {
            for (int i = 0; i < 4; i++)
                h.Box(C(PC.Silver), new Vector3(side * 1.22f, 1.26f, bedZ - bedLen * 0.5f + 0.15f + i * (bedLen - 0.3f) / 3f), new Vector3(0.08f, 0.4f, 0.08f), 0.015f);
            h.Box(C(PC.Teal), new Vector3(side * 1.22f, 1.42f, bedZ), new Vector3(0.06f, 0.1f, bedLen - 0.1f), 0.015f);
            h.Box(C(PC.Red), new Vector3(side * 1.0f, 0.9f, bedZ - bedLen * 0.5f - 0.04f), new Vector3(0.26f, 0.14f, 0.06f), 0.03f);
        }

        h.Box(C(PC.Teal), new Vector3(0f, 1.3f, bedZ - bedLen * 0.5f + 0.02f), new Vector3(2.5f, 0.34f, 0.08f), 0.02f);
        for (int i = 0; i < 6; i++)
            h.Box(C(i % 2 == 0 ? PC.Yellow : PC.Charcoal), new Vector3(-1.0f + i * 0.4f, 0.74f, bedZ - bedLen * 0.5f - 0.02f), new Vector3(0.4f, 0.16f, 0.05f), 0.01f);
        var hullMesh = ArtAssets.SaveMesh(MeshModel, "TruckHull", h.ToPaletteMesh("TruckHull"));
        ArtAssets.MeshObject("Hull", body, hullMesh, new[] { ArtPalette.Material });

        var wheelKit = new MeshKit();
        wheelKit.Cylinder(C(PC.Rubber), Vector3.zero, 0.52f, 0.42f, 16, new Vector3(0f, 0f, 90f), 0.07f);
        wheelKit.Cylinder(C(PC.Silver), Vector3.zero, 0.28f, 0.44f, 10, new Vector3(0f, 0f, 90f), 0.02f);
        for (int i = 0; i < 4; i++)
            wheelKit.Box(C(PC.Charcoal), Quaternion.Euler(i * 90f + 45f, 0f, 0f) * Vector3.up * 0.16f, new Vector3(0.46f, 0.07f, 0.07f), 0.01f, new Vector3(i * 90f + 45f, 0f, 0f));
        var wheelMesh = ArtAssets.SaveMesh(MeshModel, "TruckWheel", wheelKit.ToPaletteMesh("TruckWheel"));
        var wheels = new List<Object>();
        foreach (int side in new[] { -1, 1 })
        foreach (float z in new[] { 1.95f, -1.5f })
            wheels.Add(ArtAssets.MeshObject("Wheel", truck, wheelMesh, new[] { ArtPalette.Material }, new Vector3(side * 1.06f, 0.52f, z)).transform);

        // Cargo: 4 x 4 crates per layer, layers stack up as the dock levels.
        var cargo = new GameObject("Cargo").transform;
        cargo.SetParent(truck, false);
        cargo.localPosition = new Vector3(0f, 1.09f, bedZ);
        var pile = cargo.gameObject.AddComponent<ItemPile>();
        SetMany(pile, ("capacity", 16), ("columns", 4), ("rows", 4), ("cellSize", new Vector3(0.54f, 0.22f, 0.62f)), ("arriveDuration", 0.42f), ("arriveArc", 1.9f),
            ("yawJitter", 4f));
        var payPoint = new GameObject("PayPoint").transform;
        payPoint.SetParent(truck, false);
        payPoint.localPosition = new Vector3(-1.3f, 2.3f, cabZ);
        var smoke = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PrefabDir + "/VFX/VFX_ChainsawSmoke.prefab"), truck);
        smoke.name = "Exhaust";
        smoke.transform.localPosition = new Vector3(-1.05f, 3.65f, 0.98f);
        smoke.transform.localScale = Vector3.one * 2.2f;

        var park = new GameObject("ParkPoint").transform;
        park.SetParent(root.transform, false);
        park.localPosition = truck.localPosition;
        park.localRotation = truck.localRotation;
        var away = new GameObject("AwayPoint").transform;
        away.SetParent(root.transform, false);
        away.localPosition = truck.localPosition + new Vector3(42f, 0f, 0f);

        SetMany(bay, ("definition", Load<TruckBayDefinition>(DataDir + "/Factory/TruckBay_Plant.asset")), ("level", 1), ("truck", truck), ("cargo", pile),
            ("parkPoint", park), ("awayPoint", away), ("wheelRadius", 0.52f), ("truckBody", body), ("payPoint", payPoint),
            ("exhaust", smoke.GetComponent<ParticleSystem>()), ("cashPile", cashPile.GetComponent<CashPile>()), ("label", label.GetComponent<StationLabel>()),
            ("levelVisuals", levelVisuals));
        SetArray(bay, "wheels", wheels.ToArray());
        var saved = ArtAssets.SavePrefab(root, PrefabDir + "/Stations/TruckBay.prefab");
        Log.AppendLine("built TruckBay");
        return saved;
    }

    static void Mast(MeshKit k, Vector3 pos, int facing)
    {
        k.Push(pos);
        k.Box(C(PC.Concrete), new Vector3(0f, 0.12f, 0f), new Vector3(0.5f, 0.24f, 0.5f), 0.04f);
        k.Cylinder(C(PC.Gray), new Vector3(0f, 2.1f, 0f), 0.07f, 3.8f, 8, default, 0.01f);
        k.Box(C(PC.Charcoal), new Vector3(facing * 0.25f, 4.0f, 0f), new Vector3(0.7f, 0.1f, 0.1f), 0.02f);
        k.Box(C(PC.Charcoal), new Vector3(facing * 0.55f, 3.9f, 0f), new Vector3(0.34f, 0.26f, 0.42f), 0.04f, new Vector3(0f, 0f, facing * 25f));
        k.Box(C(PC.Cream), new Vector3(facing * 0.6f, 3.8f, 0f), new Vector3(0.26f, 0.05f, 0.36f), 0.01f, new Vector3(0f, 0f, facing * 25f));
        k.Pop();
    }

    static Transform CloneChild(Transform sourcePrefab, string child, Transform parent, string name, Vector3 localPosition)
    {
        var source = sourcePrefab.Find(child);
        var go = Object.Instantiate(source.gameObject, parent);
        go.name = name;
        go.transform.localPosition = localPosition;
        go.transform.localRotation = source.localRotation;
        go.transform.localScale = source.localScale;
        return go.transform;
    }

    // ---------- giant mining truck ----------

    enum GS { Paint, Paint2, Dark, Chrome, Glass, Rubber, Rim, Light, Tail, Rust, Hazard, Steel, Rock, White, Count }

    sealed class Wreck
    {
        public GameObject Root;
        public Transform Visual, Body;
        public Material[] Table;
        public readonly List<GameObject> Smoke = new(), Cracks = new(), Heavy = new();
    }

    const string GiantModel = "GiantTruck";

    static GameObject WStatic(Wreck w, string part, MeshKit k, Vector3 pos = default, Vector3 euler = default) =>
        ArtAssets.Part(GiantModel, part, k, w.Table, w.Body, pos, euler);

    static GameObject WPart(Wreck w, string part, MeshKit k, Vector3 pos, int order, Vector3 euler = default)
    {
        var go = ArtAssets.Part(GiantModel, part, k, w.Table, w.Body, pos, euler);
        ArtAssets.Set(go.AddComponent<ScrapPart>(), ("detachOrder", order), ("lifetime", 1.05f), ("gravity", 20f));
        return go;
    }

    static GameObject WEffect(Wreck w, string prefab, Vector3 pos, float scale)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>($"{PrefabDir}/VFX/{prefab}.prefab"), w.Body);
        go.transform.localPosition = pos;
        go.transform.localScale = Vector3.one * scale;
        go.SetActive(false);
        return go;
    }

    /// <summary>Earth-mover tyre: deep lugs, yellow rim, hub with bolts. Axle along X.</summary>
    static MeshKit GiantWheel(float r, float w)
    {
        var k = new MeshKit();
        float ri = r * 0.56f;
        k.Push(Vector3.zero, new Vector3(0f, 0f, 90f));
        k.Lathe((int)GS.Rubber, Vector3.zero, new[]
        {
            new Vector2(ri, -w * 0.5f), new Vector2(r - 0.1f, -w * 0.5f), new Vector2(r, -w * 0.5f + 0.12f), new Vector2(r, w * 0.5f - 0.12f),
            new Vector2(r - 0.1f, w * 0.5f), new Vector2(ri, w * 0.5f),
        }, 24, default, false, 40f, true);
        int lugs = 16;
        for (int i = 0; i < lugs; i++)
        {
            float a = 360f * i / lugs;
            var d = Quaternion.Euler(0f, a, 0f) * Vector3.right;
            k.Box((int)GS.Rubber, d * (r + 0.02f), new Vector3(0.12f, w * 0.86f, r * 0.2f), 0.03f, new Vector3(0f, -a + 14f, 0f));
        }

        k.Cylinder((int)GS.Rim, Vector3.zero, ri * 1.03f, w * 0.84f, 18, default, 0.04f);
        foreach (int side in new[] { -1, 1 })
        {
            k.Cylinder((int)GS.Chrome, new Vector3(0f, side * w * 0.43f, 0f), ri * 0.4f, 0.08f, 12, default, 0.02f);
            for (int i = 0; i < 8; i++)
                k.Cylinder((int)GS.Dark, Quaternion.Euler(0f, 45f * i, 0f) * Vector3.right * ri * 0.7f + new Vector3(0f, side * w * 0.42f, 0f), 0.05f, 0.05f, 6, default, 0.01f);
        }

        k.Pop();
        return k;
    }

    /// <summary>
    /// The Giant Truck: a wrecked mining dump truck about 10 m long. Sixteen parts come off as it is cut (bumper, ladder,
    /// rails, exhausts, canopy, hood, doors, tank, tailgate, side plates, four man-high wheels), smoke and sparks build up
    /// in three stages and the hull sags. No floating bar: the HUD boss bar shows its health.
    /// </summary>
    static void BuildGiantTruck()
    {
        var def = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_GiantTruck.asset");
        var table = new Material[(int)GS.Count];
        table[(int)GS.Paint] = Mat("GiantYellow", new Color(1f, 0.72f, 0.08f), 0.55f, 0.08f, Detail.Paint, 0.4f);
        table[(int)GS.Paint2] = Mat("GiantOrange", new Color(0.96f, 0.45f, 0.08f), 0.5f, 0.08f, Detail.Paint, 0.4f);
        table[(int)GS.Dark] = Mat("GiantDark", new Color(0.16f, 0.17f, 0.2f), 0.3f, 0f, Detail.Rubber, 1.2f);
        table[(int)GS.Chrome] = Mat("GiantChrome", new Color(0.8f, 0.82f, 0.86f), 0.78f, 0.35f, Detail.Metal, 1.2f);
        table[(int)GS.Glass] = Mat("GiantGlass", new Color(0.4f, 0.6f, 0.74f), 0.92f, 0.1f, Detail.Plain);
        table[(int)GS.Rubber] = Mat("GiantRubber", new Color(0.12f, 0.12f, 0.13f), 0.2f, 0f, Detail.Rubber, 1.5f);
        table[(int)GS.Rim] = Mat("GiantRim", new Color(1f, 0.78f, 0.14f), 0.55f, 0.15f, Detail.Paint, 1f);
        table[(int)GS.Light] = Mat("GiantHeadlight", new Color(1f, 0.96f, 0.82f), 0.9f, 0f, Detail.Plain, 1f, new Color(0.6f, 0.52f, 0.3f));
        table[(int)GS.Tail] = Mat("GiantTaillight", new Color(0.95f, 0.15f, 0.12f), 0.85f, 0f, Detail.Plain, 1f, new Color(0.5f, 0.05f, 0.03f));
        table[(int)GS.Rust] = Mat("GiantRust", new Color(0.6f, 0.3f, 0.14f), 0.15f, 0f, Detail.Plain);
        table[(int)GS.Hazard] = Mat("GiantHazard", new Color(0.93f, 0.2f, 0.14f), 0.45f, 0f, Detail.Paint, 1f);
        table[(int)GS.Steel] = Mat("GiantSteel", new Color(0.5f, 0.53f, 0.58f), 0.5f, 0.3f, Detail.Metal, 1.2f);
        table[(int)GS.Rock] = Mat("GiantRubble", new Color(0.46f, 0.42f, 0.38f), 0.15f, 0f, Detail.Plain);
        table[(int)GS.White] = Mat("GiantWhite", new Color(0.96f, 0.96f, 0.94f), 0.5f, 0f, Detail.Paint, 1f);

        var w = new Wreck { Table = table, Root = new GameObject("Scrap_GiantTruck") };
        w.Root.layer = LayerMask.NameToLayer("Scrap");
        w.Visual = new GameObject("Visual").transform;
        w.Visual.SetParent(w.Root.transform, false);
        w.Body = new GameObject("Body").transform;
        w.Body.SetParent(w.Visual, false);
        var scrap = w.Root.AddComponent<ScrapObject>();
        var box = w.Root.AddComponent<BoxCollider>();
        ArtAssets.Set(scrap, ("definition", def), ("visualRoot", w.Visual), ("hitCollider", box), ("healthBar", null));

        const float wr = 1.3f, ww = 1.0f, axleF = 2.75f, axleR = -2.35f, deckY = 3.36f, tubBottom = 2.75f, tubTop = 5.65f;
        var k = new MeshKit();
        // Frame rails, sub-frame under the tub, axles.
        k.Box((int)GS.Dark, new Vector3(0f, 1.55f, 0f), new Vector3(1.6f, 0.6f, 8.6f), 0.1f);
        k.Box((int)GS.Dark, new Vector3(0f, 2.3f, -1.4f), new Vector3(1.6f, 0.9f, 6.0f), 0.08f);
        k.Cylinder((int)GS.Dark, new Vector3(0f, wr, axleF), 0.3f, 4.2f, 10, new Vector3(0f, 0f, 90f), 0.05f);
        k.Cylinder((int)GS.Dark, new Vector3(0f, wr, axleR), 0.36f, 4.6f, 10, new Vector3(0f, 0f, 90f), 0.05f);
        // Engine block and nose: big radiator, stacked headlights.
        k.Box((int)GS.Paint, new Vector3(0f, 2.55f, 3.55f), new Vector3(3.3f, 1.5f, 2.2f), 0.18f, default, 2);
        k.Box((int)GS.Dark, new Vector3(0f, 2.5f, 4.67f), new Vector3(1.9f, 1.2f, 0.1f), 0.04f);
        for (int i = 0; i < 6; i++) k.Box((int)GS.Chrome, new Vector3(0f, 2.02f + i * 0.2f, 4.73f), new Vector3(1.7f, 0.06f, 0.04f), 0.01f);
        foreach (int side in new[] { -1, 1 })
        {
            k.Box((int)GS.Dark, new Vector3(side * 1.3f, 2.5f, 4.67f), new Vector3(0.6f, 1.0f, 0.1f), 0.04f);
            k.Box((int)GS.Light, new Vector3(side * 1.3f, 2.75f, 4.73f), new Vector3(0.44f, 0.3f, 0.06f), 0.05f);
            k.Box((int)GS.Light, new Vector3(side * 1.3f, 2.3f, 4.73f), new Vector3(0.44f, 0.3f, 0.06f), 0.05f);
            // Fenders over the front wheels.
            k.Box((int)GS.Paint, new Vector3(side * 2.15f, 2.85f, axleF), new Vector3(1.1f, 0.22f, 3.0f), 0.08f);
            k.Box((int)GS.Dark, new Vector3(side * 2.15f, 2.72f, axleF), new Vector3(1.0f, 0.1f, 2.8f), 0.03f);
        }

        // Walkway deck with the cab on the left and a hazard-striped front edge.
        k.Box((int)GS.Steel, new Vector3(0f, deckY, 3.3f), new Vector3(4.6f, 0.14f, 2.7f), 0.04f);
        for (int i = 0; i < 9; i++)
            k.Box(i % 2 == 0 ? (int)GS.Paint : (int)GS.Dark, new Vector3(-2.0f + i * 0.5f, deckY, 4.66f), new Vector3(0.5f, 0.16f, 0.06f), 0.01f);
        k.Box((int)GS.Paint, new Vector3(-1.25f, 4.25f, 3.0f), new Vector3(1.9f, 1.7f, 1.9f), 0.2f, default, 2);
        k.Box((int)GS.Glass, new Vector3(-1.25f, 4.5f, 3.97f), new Vector3(1.5f, 0.8f, 0.06f), 0.05f);
        k.Box((int)GS.Glass, new Vector3(-2.21f, 4.5f, 3.0f), new Vector3(0.06f, 0.8f, 1.3f), 0.05f);
        k.Box((int)GS.Glass, new Vector3(-0.29f, 4.5f, 3.0f), new Vector3(0.06f, 0.8f, 1.3f), 0.05f);
        k.Box((int)GS.Dark, new Vector3(-1.25f, 5.14f, 3.0f), new Vector3(2.0f, 0.1f, 2.0f), 0.04f);
        // Air filters and a hydraulic tank on the right of the deck.
        foreach (float z in new[] { 2.75f, 3.6f })
            k.Cylinder((int)GS.Dark, new Vector3(1.05f, 3.9f, z), 0.32f, 0.95f, 12, default, 0.06f);
        k.Box((int)GS.Paint2, new Vector3(1.95f, 3.8f, 3.4f), new Vector3(0.6f, 0.75f, 1.3f), 0.1f);
        // Dump body: a deep tub with a sloped tail, thick ribs, a dark rim and rubble heaped inside.
        k.Prism((int)GS.Paint, Vector3.zero, new[]
        {
            new Vector2(-4.9f, tubBottom + 0.95f), new Vector2(-4.9f, tubTop), new Vector2(2.1f, tubTop), new Vector2(2.1f, tubBottom), new Vector2(-2.6f, tubBottom),
        }, 5.0f, 0.12f);
        foreach (float z in new[] { -4.2f, -2.9f, -1.6f, -0.3f, 1.0f })
            foreach (int side in new[] { -1, 1 })
                k.Box((int)GS.Paint2, new Vector3(side * 2.53f, 4.25f, z), new Vector3(0.14f, 2.6f, 0.3f), 0.04f);
        k.Box((int)GS.Paint2, new Vector3(0f, tubTop + 0.02f, -1.4f), new Vector3(5.2f, 0.16f, 7.1f), 0.05f);
        k.Box((int)GS.Dark, new Vector3(0f, tubTop + 0.04f, -1.4f), new Vector3(4.5f, 0.16f, 6.5f), 0.02f);
        var rng = new System.Random(77);
        for (int i = 0; i < 18; i++)
        {
            float rx = -1.8f + (float)rng.NextDouble() * 3.6f, rz = -4.2f + (float)rng.NextDouble() * 5.6f, s = 0.55f + (float)rng.NextDouble() * 0.65f;
            k.Box(i % 5 == 0 ? (int)GS.Rust : i % 3 == 0 ? (int)GS.Steel : (int)GS.Rock, new Vector3(rx, tubTop + 0.1f + s * 0.25f, rz), new Vector3(s, s * 0.7f, s * 0.9f), 0.1f,
                new Vector3(rng.Next(-20, 20), rng.Next(0, 90), rng.Next(-20, 20)));
        }

        // Rear: tail lights on the frame, mud flaps, rust.
        foreach (int side in new[] { -1, 1 })
        {
            k.Box((int)GS.Tail, new Vector3(side * 0.55f, 1.6f, -4.33f), new Vector3(0.4f, 0.24f, 0.08f), 0.04f);
            k.Box((int)GS.Rubber, new Vector3(side * 1.75f, 1.25f, -3.9f), new Vector3(1.3f, 1.4f, 0.06f), 0.02f);
        }

        k.Box((int)GS.Rust, new Vector3(2.52f, 5.0f, -2.2f), new Vector3(0.03f, 0.7f, 1.3f), 0.01f);
        k.Box((int)GS.Rust, new Vector3(-2.52f, 3.9f, 0.3f), new Vector3(0.03f, 0.6f, 0.9f), 0.01f);
        k.Box((int)GS.Rust, new Vector3(-0.2f, deckY + 0.08f, 4.3f), new Vector3(0.9f, 0.02f, 0.5f), 0.005f);
        // Inner rear wheels stay; the outer four come off.
        foreach (int side in new[] { -1, 1 }) k.Append(GiantWheel(wr, ww * 0.8f).Shifted(new Vector3(side * 1.25f, wr, axleR)));
        WStatic(w, "Hull", k);

        int order = 0;
        var bumper = new MeshKit();
        bumper.Box((int)GS.Dark, Vector3.zero, new Vector3(4.6f, 0.6f, 0.4f), 0.12f, default, 2);
        for (int i = 0; i < 8; i++) bumper.Box(i % 2 == 0 ? (int)GS.Paint : (int)GS.Dark, new Vector3(-1.75f + i * 0.5f, 0f, 0.21f), new Vector3(0.5f, 0.4f, 0.03f), 0.01f);
        WPart(w, "Bumper", bumper, new Vector3(0f, 1.55f, 4.85f), order++);
        var ladder = new MeshKit();
        foreach (int side in new[] { -1, 1 }) ladder.Box((int)GS.Chrome, new Vector3(side * 0.3f, 0f, 0f), new Vector3(0.07f, 2.2f, 0.07f), 0.02f);
        for (int i = 0; i < 6; i++) ladder.Box((int)GS.Chrome, new Vector3(0f, -0.9f + i * 0.36f, 0f), new Vector3(0.6f, 0.06f, 0.07f), 0.015f);
        WPart(w, "Ladder", ladder, new Vector3(-1.4f, 2.3f, 4.95f), order++, new Vector3(-12f, 0f, 0f));
        var rail = new MeshKit();
        rail.Box((int)GS.Chrome, new Vector3(0f, 0.45f, 0f), new Vector3(2.3f, 0.06f, 0.06f), 0.015f);
        for (int i = 0; i < 4; i++) rail.Box((int)GS.Chrome, new Vector3(-1.1f + i * 0.73f, 0.2f, 0f), new Vector3(0.06f, 0.5f, 0.06f), 0.015f);
        WPart(w, "RailFront", rail, new Vector3(1.0f, deckY + 0.09f, 4.55f), order++);
        var stack = new MeshKit();
        stack.Cylinder((int)GS.Chrome, Vector3.zero, 0.16f, 2.0f, 10, default, 0.03f);
        stack.Cylinder((int)GS.Dark, new Vector3(0f, 1.05f, 0f), 0.2f, 0.3f, 10, default, 0.03f);
        WPart(w, "ExhaustL", stack, new Vector3(0.25f, deckY + 1.07f, 2.32f), order++);
        WPart(w, "WheelFL", GiantWheel(wr, ww), new Vector3(-2.15f, wr, axleF), order++);
        var canopy = new MeshKit();
        canopy.Box((int)GS.Paint, Vector3.zero, new Vector3(5.2f, 0.22f, 2.9f), 0.08f);
        canopy.Box((int)GS.Paint2, new Vector3(0f, -0.2f, 1.38f), new Vector3(5.2f, 0.4f, 0.14f), 0.05f);
        for (int i = 0; i < 3; i++) canopy.Box((int)GS.Paint2, new Vector3(-1.7f + i * 1.7f, 0.13f, 0f), new Vector3(0.2f, 0.08f, 2.8f), 0.02f);
        WPart(w, "Canopy", canopy, new Vector3(0f, tubTop + 0.21f, 3.45f), order++);
        var plate = new MeshKit();
        plate.Box((int)GS.Paint, Vector3.zero, new Vector3(0.1f, 1.7f, 2.4f), 0.04f);
        plate.Box((int)GS.White, new Vector3(0.06f, 0.1f, 0f), new Vector3(0.02f, 0.7f, 1.5f), 0.01f);
        plate.Box((int)GS.Hazard, new Vector3(0.075f, 0.1f, 0f), new Vector3(0.02f, 0.3f, 1.2f), 0.01f);
        WPart(w, "PlateR", plate, new Vector3(2.62f, 4.35f, -2.2f), order++);
        var panel = new MeshKit();
        panel.Box((int)GS.Paint2, Vector3.zero, new Vector3(0.1f, 1.2f, 1.9f), 0.04f);
        for (int i = 0; i < 5; i++) panel.Box((int)GS.Dark, new Vector3(0.06f, -0.36f + i * 0.18f, 0f), new Vector3(0.02f, 0.08f, 1.5f), 0.01f);
        WPart(w, "EnginePanel", panel, new Vector3(1.7f, 2.55f, 3.55f), order++);
        WPart(w, "WheelFR", GiantWheel(wr, ww), new Vector3(2.15f, wr, axleF), order++);
        var door = new MeshKit();
        door.Box((int)GS.Paint, Vector3.zero, new Vector3(0.1f, 1.5f, 1.2f), 0.05f);
        door.Box((int)GS.Glass, new Vector3(-0.06f, 0.3f, 0f), new Vector3(0.02f, 0.6f, 0.9f), 0.02f);
        door.Box((int)GS.Chrome, new Vector3(-0.07f, -0.2f, 0.4f), new Vector3(0.04f, 0.06f, 0.2f), 0.01f);
        WPart(w, "CabDoor", door, new Vector3(-2.26f, 4.2f, 3.0f), order++);
        var tank = new MeshKit();
        tank.Cylinder((int)GS.Chrome, Vector3.zero, 0.5f, 1.9f, 14, new Vector3(90f, 0f, 0f), 0.08f);
        foreach (float z in new[] { -0.55f, 0.55f }) tank.Cylinder((int)GS.Dark, new Vector3(0f, 0f, z), 0.52f, 0.12f, 14, new Vector3(90f, 0f, 0f), 0.02f);
        WPart(w, "FuelTank", tank, new Vector3(-1.85f, 1.75f, 0.3f), order++);
        var gate = new MeshKit();
        gate.Box((int)GS.Paint2, Vector3.zero, new Vector3(5.0f, 2.0f, 0.2f), 0.08f);
        for (int i = 0; i < 4; i++) gate.Box((int)GS.Paint, new Vector3(-1.8f + i * 1.2f, 0f, -0.12f), new Vector3(0.24f, 1.8f, 0.1f), 0.03f);
        for (int i = 0; i < 8; i++) gate.Box(i % 2 == 0 ? (int)GS.Hazard : (int)GS.White, new Vector3(-2.1f + i * 0.6f, -0.85f, -0.13f), new Vector3(0.6f, 0.26f, 0.04f), 0.01f);
        WPart(w, "Tailgate", gate, new Vector3(0f, 4.7f, -4.98f), order++, new Vector3(-10f, 0f, 0f));
        var plateL = new MeshKit();
        plateL.Box((int)GS.Paint, Vector3.zero, new Vector3(0.1f, 1.7f, 2.4f), 0.04f);
        plateL.Box((int)GS.White, new Vector3(-0.06f, 0.1f, 0f), new Vector3(0.02f, 0.7f, 1.5f), 0.01f);
        plateL.Box((int)GS.Hazard, new Vector3(-0.075f, 0.1f, 0f), new Vector3(0.02f, 0.3f, 1.2f), 0.01f);
        WPart(w, "PlateL", plateL, new Vector3(-2.62f, 4.35f, -2.2f), order++);
        WPart(w, "ExhaustR", stack, new Vector3(2.0f, deckY + 1.07f, 2.32f), order++);
        WPart(w, "WheelRL", GiantWheel(wr, ww), new Vector3(-2.3f, wr, axleR), order++);
        WPart(w, "WheelRR", GiantWheel(wr, ww), new Vector3(2.3f, wr, axleR), order++);

        // Damage stages: smoke first, then cracked glass and sparks, then fire-bright sparks and more smoke.
        w.Smoke.Add(WEffect(w, "VFX_ScrapSmoke", new Vector3(0.6f, 4.3f, 3.6f), 2.2f));
        var cracks = new MeshKit();
        Crack(cracks, new Vector3(-1.25f, 4.5f, 4.01f), Vector3.zero, 0.7f);
        Crack(cracks, new Vector3(-2.25f, 4.5f, 3.0f), new Vector3(0f, 90f, 0f), 0.6f);
        w.Cracks.Add(WStatic(w, "Cracks", cracks));
        w.Cracks.Add(WEffect(w, "VFX_ScrapSparksLoop", new Vector3(2.3f, 2.4f, 1.2f), 1.8f));
        w.Cracks.Add(WEffect(w, "VFX_ScrapSmoke", new Vector3(-1.4f, 6.1f, -2.5f), 2.4f));
        w.Heavy.Add(WEffect(w, "VFX_ScrapSparksLoop", new Vector3(-2.3f, 2.6f, -1.5f), 2f));
        w.Heavy.Add(WEffect(w, "VFX_ScrapSparksLoop", new Vector3(0f, 3.4f, 4.6f), 1.8f));
        w.Heavy.Add(WEffect(w, "VFX_ScrapSmoke", new Vector3(1.6f, 6.2f, 0.5f), 2.8f));

        var b = ArtAssets.RendererBounds(w.Visual.gameObject);
        box.center = w.Root.transform.InverseTransformPoint(b.center);
        box.size = new Vector3(b.size.x * 0.92f, b.size.y, b.size.z * 0.95f);
        var obstacle = w.Root.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = box.center;
        obstacle.size = box.size;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;

        var stages = w.Root.AddComponent<ScrapDamageStages>();
        var so = new SerializedObject(stages);
        so.FindProperty("scrap").objectReferenceValue = scrap;
        so.FindProperty("sagPivot").objectReferenceValue = w.Body;
        so.FindProperty("sagAxis").vector3Value = Vector3.forward;
        var sp = so.FindProperty("stages");
        var defs = new[] { (0.8f, w.Smoke, 0f), (0.55f, w.Cracks, 1f), (0.28f, w.Heavy, 2.5f) };
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

        // One palette material for the whole machine (it is on screen with ~150 loose pieces).
        var baker = new PaletteBaker("GiantPalette", null);
        baker.Convert(w.Root, GiantModel);
        baker.Save();

        var prefab = ArtAssets.SavePrefab(w.Root, PrefabDir + "/Scrap/Scrap_GiantTruck.prefab");
        ArtAssets.Set(def, ("prefab", prefab.GetComponent<ScrapObject>()));
        ArtAssets.SetArray(def, "prefabVariants", Array.Empty<Object>());
        Log.AppendLine($"built Scrap_GiantTruck ({b.size.x:0.0} x {b.size.y:0.0} x {b.size.z:0.0} m, {order} parts)");
    }

    static void Crack(MeshKit k, Vector3 center, Vector3 euler, float size)
    {
        k.Push(center, euler);
        for (int i = 0; i < 5; i++)
        {
            float a = i * 72f + 15f;
            var d = Quaternion.Euler(0f, 0f, a) * Vector3.up * size * 0.32f;
            k.Box((int)GS.White, d, new Vector3(0.03f, size * 0.62f, 0.02f), 0.006f, new Vector3(0f, 0f, a));
        }

        k.Pop();
    }

    // =====================================================================================
    // SCENE
    // =====================================================================================

    public static string Scene()
    {
        Log.Clear();
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var systems = Root(scene, "_Systems");
        var gameplay = Root(scene, "_Gameplay");
        var environment = Root(scene, "_Environment");
        var ui = Root(scene, "_UI");
        var font = Load<TMP_FontAsset>(FontDir + "/GROBOLD SDF.asset");
        var worldText = Load<Material>(FontDir + "/GROBOLD World.mat");
        var outline = Load<Material>(FontDir + "/GROBOLD Outline.mat");
        var tilePrefab = Load<GameObject>(PrefabDir + "/Tiles/Tile_Purchase.prefab");
        var consolePrefab = Load<GameObject>(PrefabDir + "/Props/Prop_Console.prefab");

        var plantContent = gameplay.Find("RecyclingPlant/Content");
        var hallContent = gameplay.Find("FurnaceHall/Content");
        var heavyContent = gameplay.Find("HeavyYard/Content");
        var stations = gameplay.Find("Stations");
        var tiles = gameplay.Find("Tiles");
        var sites = gameplay.Find("Workers/Sites");

        // ---------- make room: decor that sat on the dock and on the giant's landing zone ----------
        RemoveNear(plantContent.Find("ArtDecor"), "Prop_Dumpster", new Vector3(73.8f, 0f, 13.2f), 1.5f);
        var heavyDecor = heavyContent.Find("Decor");
        RemoveNear(heavyDecor, "Heap_HeavyCenter", new Vector3(17f, 0f, 65f), 2f);
        RemoveNear(heavyDecor, "Prop_CrushedCars", new Vector3(20.5f, 0f, 61.2f), 1.5f);
        RemoveNear(heavyDecor, "Prop_Tires", new Vector3(22.2f, 0f, 66.8f), 1.5f);
        // The east-bound ambient lane ran where the dock truck parks: three lanes now (traffic 4.0 and 6.5, dock truck 9.1).
        var ambient = environment.Find("ArtAmbient");
        if (ambient != null)
        {
            MoveLane(ambient, "Prop_RoadTruck_Blue", 4.0f);
            MoveLane(ambient, "Prop_RoadTruck_Red", 6.5f);
        }

        // ---------- operators: console + post + hire tile at each machine ----------
        var crusher = stations.Find("Crusher").GetComponent<Machine>();
        var sorter = plantContent.Find("Sorter").GetComponent<Machine>();
        var furnace = hallContent.Find("Furnace").GetComponent<Machine>();
        var posts = new List<Object>
        {
            OperatorPost(sites, stations, tiles, "Crusher", "operator_crusher", CrusherPost, CrusherPos, crusher, consolePrefab, tilePrefab),
            OperatorPost(sites, plantContent, plantContent, "Sorter", "operator_sorter", SorterPost, SorterPos, sorter, consolePrefab, tilePrefab),
            OperatorPost(sites, hallContent, hallContent, "Furnace", "operator_furnace", FurnacePost, FurnacePos, furnace, consolePrefab, tilePrefab),
        };

        // ---------- sellers: a post at the head of each customer line, the hire tile inside the yard ----------
        var yardDesk = stations.Find("SellDesk").GetComponent<SellDesk>();
        var market = plantContent.Find("MetalMarket").GetComponent<SellDesk>();
        posts.Add(SellerPost(sites, "Post_SellerYard", "seller_yard", YardSellerPost, YardQueueHead, yardDesk));
        posts.Add(SellerPost(sites, "Post_SellerMarket", "seller_market", MarketSellerPost, MarketQueueHead, market));
        PlaceTile(tiles, "Tile_SellerYard", tilePrefab, YardSellerTile, "seller_yard", "tile/seller_yard");
        PlaceTile(plantContent, "Tile_SellerMarket", tilePrefab, MarketSellerTile, "seller_market", "tile/seller_market");

        // ---------- Truck Dock + Loader ----------
        var dock = BuildTruckDock(gameplay, environment, plantContent, font, worldText, tilePrefab);
        var rackPad = hallContent.Find("IngotRack/WithdrawPad").GetComponent<TransferPad>();
        var loaderRoute = FindOrCreate(sites, "LoaderRoute");
        loaderRoute.position = new Vector3(68.6f, 0f, 14.3f);
        if (!loaderRoute.TryGetComponent(out PorterRoute loader)) loader = loaderRoute.gameObject.AddComponent<PorterRoute>();
        var loaderIdle = FindOrCreate(loaderRoute, "Idle");
        loaderIdle.position = loaderRoute.position;
        SetMany(loader, ("siteId", "loader"), ("role", (int)WorkerRole.Loader), ("idlePoint", loaderIdle), ("pickupCenter", loaderRoute), ("pickupRadius", 1f),
            ("dropoff", dock.pad));
        SetArray(loader, "pickupPads", new Object[] { rackPad });

        // ---------- Giant Scrap event ----------
        BuildGiantEvent(heavyContent, font, worldText);
        BuildGiantBar(ui.Find("Canvas/HUD"), font, outline);

        // ---------- workers ----------
        var wm = systems.Find("WorkerManager").GetComponent<WorkerManager>();
        var hireable = Current(wm, "hireable").Where(o => o != null).ToList();
        foreach (var n in new[] { "OperatorCrusher", "OperatorSorter", "OperatorFurnace", "SellerYard", "SellerMarket", "Loader" })
        {
            var d = Load<WorkerDefinition>($"{DataDir}/Workers/Worker_{n}.asset");
            if (!hireable.Contains(d)) hireable.Add(d);
        }

        SetArray(wm, "hireable", hireable.ToArray());
        var siteList = Current(wm, "sites").Where(o => o != null && !(o is WorkPost) && o != loader).ToList();
        siteList.AddRange(posts);
        siteList.Add(loader);
        SetArray(wm, "sites", siteList.ToArray());

        // ---------- NavMesh over everything (locked content active while baking so stations and consoles cut their holes) ----------
        var surface = environment.Find("NavMesh").GetComponent<NavMeshSurface>();
        var contents = new[] { plantContent, hallContent, heavyContent, gameplay.Find("BackLot/Content"), dock.content };
        var toggled = new List<GameObject>();
        foreach (var content in contents)
            foreach (Transform c in content)
                if (!c.gameObject.activeSelf)
                {
                    c.gameObject.SetActive(true);
                    toggled.Add(c.gameObject);
                }

        // Locked-state fences and teasers are NavMeshObstacles at runtime, not holes in the bake.
        foreach (var n in new[] { "FurnacePlot", "TruckDockPlot" })
        {
            var plot = plantContent.Find(n);
            if (plot == null) continue;
            foreach (Transform c in plot)
            {
                if (!c.TryGetComponent(out NavMeshModifier mod)) mod = c.gameObject.AddComponent<NavMeshModifier>();
                mod.ignoreFromBuild = true;
            }
        }

        // Accurate heights: without the height mesh, agents beside a tall obstacle (operators at their consoles) stand
        // up to 30 cm above the floor.
        surface.buildHeightMesh = true;
        // Belts are 0.5 m high, under the agent step height (0.75), so the bake would lay walkable floor on top of them:
        // workers would cross belts and guide paths would lead the player into one.
        foreach (var belt in Object.FindObjectsByType<Conveyor>(FindObjectsInactive.Include))
        {
            if (!belt.TryGetComponent(out NavMeshModifier beltModifier)) beltModifier = belt.gameObject.AddComponent<NavMeshModifier>();
            beltModifier.overrideArea = true;
            beltModifier.area = NavMesh.GetAreaFromName("Not Walkable");
        }

        surface.BuildNavMesh();
        foreach (var go in toggled) go.SetActive(false);
        foreach (var content in contents)
            foreach (Transform c in content)
                c.gameObject.SetActive(false);
        string navPath = Path.Combine(Path.GetDirectoryName(ScenePath), Path.GetFileNameWithoutExtension(ScenePath)).Replace('\\', '/') + "/NavMesh-Area1.asset";
        if (File.Exists(navPath)) AssetDatabase.DeleteAsset(navPath);
        AssetDatabase.CreateAsset(surface.navMeshData, navPath);
        EditorUtility.SetDirty(surface);
        foreach (var p in new[] { YardSellerPost, MarketSellerPost, CrusherPost, SorterPost, FurnacePost, loaderRoute.position })
            Log.AppendLine($"navmesh at {p}: {(NavMesh.SamplePosition(p, out var hit, 0.6f, NavMesh.AllAreas) ? "ok " + hit.position : "MISSING")}");
        Log.AppendLine("belt top walkable: " + NavMesh.SamplePosition(new Vector3(24f, 0.5f, 23f), out _, 0.25f, NavMesh.AllAreas) + " (must be False)");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("Saved " + ScenePath);
        return Log.ToString();
    }

    /// <summary>Moves an ambient road truck and its route points to lane <paramref name="z"/>.</summary>
    static void MoveLane(Transform ambient, string truck, float z)
    {
        foreach (Transform t in ambient)
        {
            if (!t.name.StartsWith(truck)) continue;
            if (t.TryGetComponent(out AmbientPath _)) t.position = new Vector3(t.position.x, t.position.y, z);
            else
                foreach (Transform point in t)
                    point.position = new Vector3(point.position.x, point.position.y, z);
        }
    }

    static void RemoveNear(Transform parent, string namePrefix, Vector3 position, float radius)
    {
        if (parent == null) return;
        foreach (var t in parent.Cast<Transform>().ToArray())
        {
            if (!t.name.StartsWith(namePrefix)) continue;
            Vector3 d = t.position - position;
            d.y = 0f;
            if (d.magnitude > radius) continue;
            Log.AppendLine("removed " + t.name + " at " + t.position);
            Object.DestroyImmediate(t.gameObject);
        }
    }

    static Object[] Current(Object target, string field)
    {
        var p = new SerializedObject(target).FindProperty(field);
        var list = new Object[p.arraySize];
        for (int i = 0; i < list.Length; i++) list[i] = p.GetArrayElementAtIndex(i).objectReferenceValue;
        return list;
    }

    /// <summary>Console facing the machine, a WorkPost where the operator stands, and the hire tile on that same spot.</summary>
    static WorkPost OperatorPost(Transform sites, Transform consoleParent, Transform tileParent, string machineName, string id, Vector3 post, Vector3 machinePos,
        Machine machine, GameObject consolePrefab, GameObject tilePrefab)
    {
        Vector3 forward = machinePos - post;
        forward.y = 0f;
        var rotation = Quaternion.LookRotation(forward.normalized);

        string consoleName = "Console_" + machineName;
        var old = consoleParent.Find(consoleName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var console = (GameObject)PrefabUtility.InstantiatePrefab(consolePrefab, consoleParent);
        console.name = consoleName;
        console.transform.SetPositionAndRotation(post + rotation * new Vector3(0f, 0f, 0.75f), rotation);

        var t = FindOrCreate(sites, "Post_" + machineName);
        t.SetPositionAndRotation(post, rotation);
        if (!t.TryGetComponent(out WorkPost workPost)) workPost = t.gameObject.AddComponent<WorkPost>();
        var idle = FindOrCreate(t, "Idle");
        idle.position = post;
        var look = FindOrCreate(t, "LookAt");
        look.position = console.transform.position;
        SetMany(workPost, ("siteId", id), ("idlePoint", idle), ("role", (int)WorkerRole.Operator), ("machine", machine), ("desk", null), ("lookAt", look));

        PlaceTile(tileParent, "Tile_" + char.ToUpperInvariant(id[0]) + id.Substring(1), tilePrefab, post, id, "tile/" + id);
        return workPost;
    }

    static WorkPost SellerPost(Transform sites, string name, string id, Vector3 post, Vector3 queueHead, SellDesk desk)
    {
        var t = FindOrCreate(sites, name);
        t.SetPositionAndRotation(post, Quaternion.LookRotation((queueHead - post).normalized));
        if (!t.TryGetComponent(out WorkPost workPost)) workPost = t.gameObject.AddComponent<WorkPost>();
        var idle = FindOrCreate(t, "Idle");
        idle.position = post;
        var look = FindOrCreate(t, "LookAt");
        look.position = queueHead;
        SetMany(workPost, ("siteId", id), ("idlePoint", idle), ("role", (int)WorkerRole.Seller), ("machine", null), ("desk", desk), ("lookAt", look));
        return workPost;
    }

    struct DockRefs
    {
        public Transform content;
        public TransferPad pad;
    }

    /// <summary>
    /// The Truck Dock. Locked: a fenced, signed plot with a purchase tile (inside the plant content, so it shows with the
    /// plant). Bought: the fence sinks and the concrete apron, the dock with its truck and the Loader tile pop in.
    /// </summary>
    static DockRefs BuildTruckDock(Transform gameplay, Transform environment, Transform plantContent, TMP_FontAsset font, Material worldText, GameObject tilePrefab)
    {
        var refs = new DockRefs();
        var fences = environment.Find("Fences");

        // The plant's south rail fence becomes: fence | dock gap | fence, with an invisible edge so nobody walks onto the road.
        foreach (var n in new[] { "Fence_62_76", "Fence_62_66", "Fence_75_76", "DockEdge" })
        {
            var old = fences.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        RailFence(fences, "Fence_62_66", new Vector3(62f, 0f, FenceZ), new Vector3(DockWest, 0f, FenceZ), true);
        RailFence(fences, "Fence_75_76", new Vector3(DockEast, 0f, FenceZ), new Vector3(76f, 0f, FenceZ), true);
        var edge = new GameObject("DockEdge");
        edge.transform.SetParent(fences, false);
        edge.transform.position = new Vector3((DockWest + DockEast) * 0.5f, 1f, FenceZ);
        edge.AddComponent<BoxCollider>().size = new Vector3(DockEast - DockWest, 2f, 0.4f);

        var root = FindOrCreate(gameplay, "TruckDock");
        root.position = Vector3.zero;
        if (!root.TryGetComponent(out Expansion expansion)) expansion = root.gameObject.AddComponent<Expansion>();
        var content = ResetChild(root, "Content");
        refs.content = content;

        // Locked state.
        var plot = ResetChild(plantContent, "TruckDockPlot");
        var barriers = Empty("Barriers", plot, Vector3.zero);
        var gap = RailFence(barriers, "Fence_DockGap", new Vector3(DockWest, 0f, FenceZ), new Vector3(DockEast, 0f, FenceZ), false);
        var locked = Empty("LockedOnly", plot, Vector3.zero);
        var sign = new MeshKit();
        float sx = (DockWest + DockEast) * 0.5f + 1.2f;
        foreach (int side in new[] { -1, 1 }) sign.Box(C(PC.Charcoal), new Vector3(sx + side * 1.7f, 1.2f, 11.2f), new Vector3(0.16f, 2.4f, 0.16f), 0.03f);
        sign.Box(C(PC.Yellow), new Vector3(sx, 2.35f, 11.2f), new Vector3(3.8f, 1.1f, 0.12f), 0.04f, new Vector3(15f, 0f, 0f));
        // Teaser: cones along the kerb and two pallets of ingots waiting for a truck.
        for (int i = 0; i < 5; i++)
        {
            var cp = new Vector3(DockWest + 1.2f + i * 1.7f, 0f, 11.6f + (i % 2) * 0.3f);
            sign.Box(C(PC.Charcoal), cp + new Vector3(0f, 0.03f, 0f), new Vector3(0.42f, 0.06f, 0.42f), 0.01f);
            sign.Cylinder(C(PC.Orange), cp + new Vector3(0f, 0.36f, 0f), 0.16f, 0.6f, 10, default, 0.01f, 0.04f);
            sign.Cylinder(C(PC.White), cp + new Vector3(0f, 0.4f, 0f), 0.115f, 0.12f, 10, default, 0.005f, 0.09f);
        }

        ArtAssets.MeshObject("Sign", locked, ArtAssets.SaveMesh(MeshModel, "DockSign", sign.ToPaletteMesh("DockSign")), new[] { ArtPalette.Material });
        var text = Text3D("Text", locked, new Vector3(sx, 2.35f, 11.12f), font, worldText, 5f, Navy, "<size=75%>TRUCK DOCK</size>\nLOCKED");
        text.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
        text.rectTransform.sizeDelta = new Vector2(3.6f, 1f);
        PlaceTile(plantContent, "Tile_TruckDock", tilePrefab, DockTile, "truck_dock", "tile/truck_dock");

        // Content (reveal order: apron, dock with truck, Loader tile).
        var apron = new MeshKit();
        float w = DockEast - DockWest, d = DockNorth - (FenceZ + 0.3f);
        apron.Box(0, Vector3.zero, new Vector3(w, 0.04f, d), 0f);
        var apronGo = ArtAssets.MeshObject("Floor_Dock", content, ArtAssets.SaveMesh(MeshModel, "DockApron", apron.ToMesh("DockApron", 1)),
            new[] { Load<Material>(MatDir + "/M_Ground_Concrete.mat") }, new Vector3((DockWest + DockEast) * 0.5f, 0.012f, FenceZ + 0.3f + d * 0.5f), default, false);
        GameObjectUtility.SetStaticEditorFlags(apronGo, StaticEditorFlags.BatchingStatic);
        var lines = new MeshKit();
        lines.Box(C(PC.Yellow), new Vector3(0f, 0.036f, d * 0.5f - 0.1f), new Vector3(w - 0.3f, 0.01f, 0.12f), 0f);
        foreach (int side in new[] { -1, 1 }) lines.Box(C(PC.Yellow), new Vector3(side * (w * 0.5f - 0.15f), 0.036f, 0f), new Vector3(0.12f, 0.01f, d - 0.2f), 0f);
        ArtAssets.MeshObject("Lines", apronGo.transform, ArtAssets.SaveMesh(MeshModel, "DockLines", lines.ToPaletteMesh("DockLines")), new[] { ArtPalette.Material },
            default, default, false);

        var bayGo = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PrefabDir + "/Stations/TruckBay.prefab"), content);
        bayGo.transform.SetPositionAndRotation(DockRoot, Quaternion.identity);
        refs.pad = bayGo.transform.Find("Pad").GetComponent<TransferPad>();
        PlaceTile(content, "Tile_Loader", tilePrefab, LoaderTile, "loader", "tile/loader");

        var focus = FindOrCreate(root, "Focus");
        focus.position = new Vector3(DockRoot.x, 0f, 11.5f);
        var area = new Bounds(new Vector3((DockWest + DockEast) * 0.5f, 0f, (FenceZ + DockNorth) * 0.5f + 0.2f), new Vector3(w - 0.2f, 4f, DockNorth - FenceZ - 0.6f));
        SetMany(expansion, ("definition", Load<ExpansionDefinition>(DataDir + "/World/Expansion_TruckDock.asset")), ("contentRoot", content),
            ("lockedOnly", locked.gameObject), ("area", area), ("focusPoint", focus), ("revealVfx", Vfx("VFX_ConstructionBurst")), ("focusHold", 3.4f),
            ("contentStagger", 0.25f), ("barrierSinkDepth", 1.6f));
        SetArray(expansion, "barriers", new Object[] { gap });
        SetStructArray(expansion, "materialSwaps", Array.Empty<Dictionary<string, object>>());
        foreach (Transform c in content) c.gameObject.SetActive(false);
        plot.gameObject.SetActive(false);
        plantContent.Find("Tile_TruckDock").gameObject.SetActive(false);
        return refs;
    }

    /// <summary>Low yellow rail fence (the plant's street side). Returns the segment; origin at <paramref name="from"/>.</summary>
    static Transform RailFence(Transform parent, string name, Vector3 from, Vector3 to, bool collider)
    {
        var segment = new GameObject(name).transform;
        segment.SetParent(parent, false);
        Vector3 delta = to - from;
        float length = delta.magnitude;
        segment.position = from;
        segment.rotation = Quaternion.LookRotation(delta.normalized) * Quaternion.Euler(0f, -90f, 0f);
        var k = new MeshKit();
        int posts = Mathf.Max(1, Mathf.RoundToInt(length / 2f));
        for (int i = 0; i <= posts; i++) k.Box(C(PC.Charcoal), new Vector3(length * i / posts, 0.5f, 0f), new Vector3(0.16f, 1.0f, 0.16f), 0.02f);
        k.Box(C(PC.Yellow), new Vector3(length * 0.5f, 0.9f, 0f), new Vector3(length, 0.1f, 0.08f), 0.02f);
        k.Box(C(PC.Yellow), new Vector3(length * 0.5f, 0.5f, 0f), new Vector3(length, 0.08f, 0.06f), 0.02f);
        ArtAssets.MeshObject("Rail", segment, ArtAssets.SaveMesh(MeshModel, name, k.ToPaletteMesh(name)), new[] { ArtPalette.Material });
        if (!collider) return segment;
        var box = segment.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(length * 0.5f, 1f, 0f);
        box.size = new Vector3(length, 2f, 0.4f);
        return segment;
    }

    /// <summary>The event object in the Heavy Scrap Yard: landing zone markings under the crane, a counter sign, the arena point.</summary>
    static void BuildGiantEvent(Transform heavyContent, TMP_FontAsset font, Material worldText)
    {
        var root = ResetChild(heavyContent, "GiantEvent");
        root.position = ArenaPos;
        var arena = Empty("Arena", root, Vector3.zero);
        arena.localRotation = Quaternion.Euler(0f, ArenaYaw, 0f);

        // Landing zone: a hazard-striped frame the size of the giant's footprint (rotated with the arena) and corner chevrons.
        var zone = Empty("ZoneMarkings", root, Vector3.zero);
        zone.localRotation = arena.localRotation;
        var k = new MeshKit();
        const float hx = 3.1f, hz = 5.4f, y = 0.03f;
        void Stripes(Vector3 a, Vector3 b)
        {
            float len = (b - a).magnitude;
            int n = Mathf.RoundToInt(len / 0.7f);
            for (int i = 0; i < n; i++)
            {
                var p = Vector3.Lerp(a, b, (i + 0.5f) / n);
                bool alongX = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.z - a.z);
                k.Box(C(i % 2 == 0 ? PC.Yellow : PC.Charcoal), p, alongX ? new Vector3(len / n, 0.012f, 0.3f) : new Vector3(0.3f, 0.012f, len / n), 0f);
            }
        }

        Stripes(new Vector3(-hx, y, -hz), new Vector3(hx, y, -hz));
        Stripes(new Vector3(-hx, y, hz), new Vector3(hx, y, hz));
        Stripes(new Vector3(-hx, y, -hz), new Vector3(-hx, y, hz));
        Stripes(new Vector3(hx, y, -hz), new Vector3(hx, y, hz));
        for (int i = 0; i < 3; i++)
            foreach (int side in new[] { -1, 1 })
            {
                k.Box(C(PC.Yellow), new Vector3(side * 0.55f, y, -2.6f + i * 2.6f), new Vector3(1.3f, 0.012f, 0.22f), 0f, new Vector3(0f, side * 35f, 0f));
            }

        var zoneGo = ArtAssets.MeshObject("Frame", zone, ArtAssets.SaveMesh(MeshModel, "GiantZone", k.ToPaletteMesh("GiantZone")), new[] { ArtPalette.Material },
            default, default, false);
        zoneGo.GetComponent<Renderer>().receiveShadows = true;

        // Counter sign beside the crane, north-east of the zone (never hidden by the giant), tilted to the camera.
        var signRoot = Empty("Sign", root, new Vector3(7.3f, 0f, 3.9f));
        var post = new MeshKit();
        foreach (int side in new[] { -1, 1 }) post.Box(C(PC.Charcoal), new Vector3(side * 1.5f, 1.1f, 0f), new Vector3(0.18f, 2.2f, 0.18f), 0.03f);
        post.Box(C(PC.Concrete), new Vector3(0f, 0.1f, 0f), new Vector3(3.6f, 0.2f, 0.6f), 0.04f);
        ArtAssets.MeshObject("Posts", signRoot, ArtAssets.SaveMesh(MeshModel, "GiantSignPosts", post.ToPaletteMesh("GiantSignPosts")), new[] { ArtPalette.Material });
        var board = Empty("Board", signRoot, new Vector3(0f, 2.45f, 0f));
        board.localRotation = Quaternion.Euler(18f, 0f, 0f);
        var boardKit = new MeshKit();
        boardKit.Box(C(PC.Charcoal), Vector3.zero, new Vector3(4.0f, 1.5f, 0.14f), 0.05f);
        boardKit.Box(C(PC.Red), new Vector3(0f, 0f, -0.06f), new Vector3(3.8f, 1.3f, 0.06f), 0.03f);
        foreach (int side in new[] { -1, 1 })
        {
            boardKit.Cylinder(C(PC.Charcoal), new Vector3(side * 1.75f, 0.83f, 0f), 0.09f, 0.08f, 8, default, 0.01f);
            boardKit.Cylinder(C(PC.Orange), new Vector3(side * 1.75f, 0.94f, 0f), 0.08f, 0.16f, 8, default, 0.03f);
        }

        ArtAssets.MeshObject("Plate", board, ArtAssets.SaveMesh(MeshModel, "GiantSignBoard", boardKit.ToPaletteMesh("GiantSignBoard")), new[] { ArtPalette.Material });
        var text = Text3D("Text", board, new Vector3(0f, 0f, -0.1f), font, worldText, 5.2f, Color.white, "GIANT SCRAP\n<size=62%>CUT HEAVY SCRAP</size>  0/3");
        text.rectTransform.sizeDelta = new Vector2(3.7f, 1.25f);
        text.lineSpacing = -18f;

        var giantEvent = root.gameObject.AddComponent<GiantScrapEvent>();
        SetMany(giantEvent, ("config", Load<GiantEventConfig>(DataDir + "/World/GiantEvent_HeavyYard.asset")), ("arena", arena), ("footprintMargin", 0.4f),
            ("sign", text), ("signPunch", board), ("zoneMarkings", zone.gameObject));
        var so = new SerializedObject(giantEvent);
        so.FindProperty("blockingLayers").intValue = LayerMask.GetMask("Characters");
        so.ApplyModifiedPropertiesWithoutUndo();
        root.gameObject.SetActive(false);
    }

    /// <summary>HUD boss bar under the task banner: name, a red-to-green fill with a pale damage trail, a skull-free hazard frame.</summary>
    static void BuildGiantBar(Transform hud, TMP_FontAsset font, Material outline)
    {
        var old = hud.Find("GiantBar");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var holder = Rect("GiantBar", hud);
        holder.anchorMin = Vector2.zero;
        holder.anchorMax = Vector2.one;
        holder.offsetMin = holder.offsetMax = Vector2.zero;
        // Under the task banner, above the announcer and the fly layer.
        var banner = hud.Find("TaskBanner");
        holder.SetSiblingIndex(banner != null ? banner.GetSiblingIndex() + 1 : 0);

        var root = Rect("Root", holder);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = new Vector2(0f, -348f);
        root.sizeDelta = new Vector2(820f, 104f);
        var group = root.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        var punch = Rect("Punch", root);
        punch.anchorMin = Vector2.zero;
        punch.anchorMax = Vector2.one;
        punch.offsetMin = punch.offsetMax = Vector2.zero;

        var round = Load<Sprite>(UiDir + "/UI_Round.png");
        var small = Load<Sprite>(UiDir + "/UI_RoundSmall.png");
        var frame = Img("Frame", punch, round, new Color(0.08f, 0.09f, 0.14f, 0.94f), new Vector2(820f, 60f));
        frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0f);
        frame.pivot = new Vector2(0.5f, 0f);
        frame.anchoredPosition = Vector2.zero;
        frame.GetComponent<Image>().type = Image.Type.Sliced;
        frame.GetComponent<Image>().pixelsPerUnitMultiplier = 2.4f;
        var trail = Img("Trail", frame, small, new Color(1f, 0.95f, 0.85f), new Vector2(796f, 40f));
        var fill = Img("Fill", frame, small, new Color(0.95f, 0.25f, 0.2f), new Vector2(796f, 40f));
        foreach (var rt in new[] { trail, fill })
        {
            var img = rt.GetComponent<Image>();
            img.preserveAspect = false;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = 0;
            img.fillAmount = 1f;
        }

        var title = Txt("Title", punch, font, outline, 40f, new Color(1f, 0.85f, 0.25f), TextAlignmentOptions.Center, "GIANT TRUCK");
        title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        title.rectTransform.pivot = new Vector2(0.5f, 1f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 6f);
        title.rectTransform.sizeDelta = new Vector2(800f, 50f);

        var bar = holder.gameObject.AddComponent<GiantScrapBar>();
        SetMany(bar, ("root", root), ("group", group), ("title", title), ("fill", fill.GetComponent<Image>()), ("trail", trail.GetComponent<Image>()),
            ("punchRoot", punch), ("hiddenOffset", 240f));
        var so = new SerializedObject(bar);
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(0.95f, 0.22f, 0.18f), 0f), new GradientColorKey(new Color(1f, 0.72f, 0.15f), 0.5f), new GradientColorKey(new Color(0.4f, 0.9f, 0.3f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        so.FindProperty("colorByHealth").gradientValue = gradient;
        so.ApplyModifiedPropertiesWithoutUndo();
        group.alpha = 0f;
    }

    // =====================================================================================
    // Helpers (builders compile one file at a time; geometry comes from ArtKit)
    // =====================================================================================

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static RectTransform Img(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
    {
        var rt = Rect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    static TextMeshProUGUI Txt(string name, Transform parent, TMP_FontAsset font, Material mat, float size, Color color, TextAlignmentOptions align, string text)
    {
        var rt = Rect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(300f, size * 1.4f);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font;
        if (mat != null) t.fontSharedMaterial = mat;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.text = text;
        t.raycastTarget = false;
        return t;
    }

    static TMP_Text Text3D(string name, Transform parent, Vector3 localPos, TMP_FontAsset font, Material mat, float size, Color color, string text)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = font;
        if (mat != null) tmp.fontSharedMaterial = mat;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.text = text;
        tmp.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        return tmp;
    }

    static Transform Empty(string name, Transform parent, Vector3 localPos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        return t;
    }

    static void PlaceTile(Transform parent, string name, GameObject prefab, Vector3 position, string upgradeId, string anchorId)
    {
        var existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.SetPositionAndRotation(position, Quaternion.identity);
        SetMany(go.GetComponent<PurchaseTile>(), ("upgradeId", upgradeId));
        SetMany(go.AddComponent<GuideAnchor>(), ("id", anchorId));
    }

    static void StationAnchor(Transform t, MonoBehaviour station, string role)
    {
        if (!t.TryGetComponent(out GuideAnchor anchor)) anchor = t.gameObject.AddComponent<GuideAnchor>();
        SetMany(anchor, ("id", ""), ("station", station), ("role", role));
    }

    static LevelVisuals SetupLevelVisuals(GameObject root, params (int level, GameObject go)[] tiers)
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
        return lv;
    }

    static Transform Root(Scene scene, string name) => scene.GetRootGameObjects().First(g => g.name == name).transform;

    static Transform FindOrCreate(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t;
        t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static Transform ResetChild(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static void EditPrefab(string path, Action<GameObject> edit)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            edit(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Log.AppendLine("edited " + path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

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

    static Dictionary<string, object> Lv(params (string key, object value)[] values) => values.ToDictionary(v => v.key, v => v.value);

    static void SetMany(Object target, params (string field, object value)[] values)
    {
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
            case int i: p.intValue = i; break;
            case long l: p.longValue = l; break;
            case bool b: p.boolValue = b; break;
            case string s: p.stringValue = s; break;
            case Color c: p.colorValue = c; break;
            case Vector2 v2: p.vector2Value = v2; break;
            case Vector2Int v2i: p.vector2IntValue = v2i; break;
            case Vector3 v3: p.vector3Value = v3; break;
            case Bounds bounds: p.boundsValue = bounds; break;
            case null: p.objectReferenceValue = null; break;
            default: Log.AppendLine($"!! unsupported value for {field}"); break;
        }
    }

    static void SetArray(Object target, string field, Object[] values)
    {
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

    static void SetLongArray(Object target, string field, long[] values)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).longValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void SetStructArray(Object target, string field, Dictionary<string, object>[] elements)
    {
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

    static void Clips(string sfx, float volume, Vector2 pitch, params string[] files)
    {
        var def = LoadOrCreate<SfxDefinition>($"{DataDir}/Audio/Sfx_{sfx}.asset");
        var clips = files.Select(f => Load<AudioClip>($"{AudioDir}/{f}.ogg")).Where(c => c != null).Cast<Object>().ToArray();
        SetArray(def, "clips", clips);
        SetMany(def, ("volume", volume), ("pitchRange", pitch));
    }
}

static class M7MeshKitExtensions
{
    /// <summary>A copy of the kit moved by <paramref name="offset"/> (for appending a built part somewhere else).</summary>
    public static MeshKit Shifted(this MeshKit kit, Vector3 offset)
    {
        var moved = new MeshKit();
        moved.Push(offset);
        moved.Append(kit);
        moved.Pop();
        return moved;
    }
}

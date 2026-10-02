using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ScrapYardKing.Customers;
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
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Milestone 6 builder: the Furnace (iron / copper → ingots) in a new hall of the Recycling Plant with an Ingot Rack and a
// Smelter worker, then Gate 3 — the Heavy Scrap Yard (Area 3, north of the Old Yard) with trucks, tractors and garbage
// trucks that need a stronger chainsaw and burst into big loot fountains.
// Entry points (in order): Assets, Prefabs, Scene (or All). Incremental like M3–M5: edits the existing scene and prefabs.
public static class M6_Build
{
    const string P = "Assets/_Project";
    const string MatDir = P + "/Art/Materials";
    const string TexDir = P + "/Art/Textures";
    const string UiDir = P + "/Art/UI";
    const string IconDir = P + "/Art/Icons";
    const string AnimDir = P + "/Art/Animation";
    const string FontDir = P + "/Art/Fonts";
    const string MeshDir = P + "/Art/Meshes";
    const string DataDir = P + "/Data";
    const string PrefabDir = P + "/Prefabs";
    const string K = "Assets/ThirdParty/Kenney";
    const string AudioDir = K + "/Audio";
    const string ScenePath = P + "/Scenes/Area1_OldScrapYard.unity";

    static readonly Color Navy = new(0.1f, 0.15f, 0.32f);
    static readonly Color IronIngotTint = new(0.72f, 0.78f, 0.88f);
    static readonly Color CopperIngotTint = new(1f, 0.56f, 0.22f);
    static readonly Color HeatOrange = new(1f, 0.45f, 0.08f);

    // ---------- layout (blueprint frame: origin south-west, +X east, +Z north) ----------
    // Furnace hall: the free north-east corner of the Recycling Plant, between the metal bins (west) and the market (south).
    static readonly Vector3 FurnacePos = new(67f, 0f, 31.4f);
    static readonly Vector3 RackPos = new(67f, 0f, 22.6f);
    const float HallWest = 61.1f, HallSouth = 19.8f, HallEast = 75.7f, HallNorth = 41.8f;
    // Gate 3: an opening in the Old Yard's north wall, straight out of the scrap field.
    const float GateX = 12f, WallZ = 38f, GateHalf = 2.6f;

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

        // ---------- audio: furnace roar, bright ingot clink, heavy metal for the big vehicles ----------
        Clips("FurnaceCycle", 0.5f, new Vector2(0.68f, 0.8f), "Impact/impactPunch_heavy_000", "Impact/impactPunch_heavy_001", "Impact/impactPunch_heavy_002");
        Clips("IngotOut", 0.42f, new Vector2(1.25f, 1.42f), "Impact/impactMetal_light_000", "Impact/impactMetal_light_001", "Impact/impactMetal_light_002",
            "Impact/impactMetal_light_003", "Impact/impactMetal_light_004");
        Clips("HeavyHit", 0.48f, new Vector2(0.78f, 0.92f), "Impact/impactMetal_medium_000", "Impact/impactMetal_medium_001", "Impact/impactMetal_medium_002",
            "Impact/impactMetal_medium_003", "Impact/impactMetal_medium_004");
        Clips("HeavyBreak", 0.95f, new Vector2(0.7f, 0.82f), "Impact/impactMetal_heavy_000", "Impact/impactMetal_heavy_001", "Impact/impactMetal_heavy_002",
            "Impact/impactMetal_heavy_003", "Impact/impactMetal_heavy_004");

        // ---------- items: ingots are worth ~2.5x the raw metal, so the Furnace is the next big income step ----------
        var iron = Load<ItemDefinition>(DataDir + "/Items/Item_Iron.asset");
        var copper = Load<ItemDefinition>(DataDir + "/Items/Item_Copper.asset");
        var ironIngot = LoadOrCreate<ItemDefinition>(DataDir + "/Items/Item_IronIngot.asset");
        SetMany(ironIngot, ("id", "iron_ingot"), ("displayName", "Iron Ingot"), ("color", IronIngotTint), ("stackHeight", 0.2f), ("baseValue", 30));
        var copperIngot = LoadOrCreate<ItemDefinition>(DataDir + "/Items/Item_CopperIngot.asset");
        SetMany(copperIngot, ("id", "copper_ingot"), ("displayName", "Copper Ingot"), ("color", CopperIngotTint), ("stackHeight", 0.2f), ("baseValue", 64));

        // ---------- Furnace: one machine, two recipes (blueprint chain: Sorter → Furnace → ingots) ----------
        var furnace = LoadOrCreate<MachineDefinition>(DataDir + "/Factory/Machine_Furnace.asset");
        SetMany(furnace, ("id", "furnace"), ("displayName", "Furnace"), ("input", iron), ("output", ironIngot),
            ("cycleSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_FurnaceCycle.asset")), ("outputSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_IngotOut.asset")));
        SetStructArray(furnace, "outputMix", Array.Empty<Dictionary<string, object>>());
        SetStructArray(furnace, "recipes", new[] { Lv(("input", iron), ("output", ironIngot)), Lv(("input", copper), ("output", copperIngot)) });
        SetStructArray(furnace, "levels", new[]
        {
            Lv(("inputCapacity", 12), ("cycleTime", 1.8f), ("inputsPerCycle", 1), ("outputsPerCycle", 1), ("upgradeCost", 0)),
            Lv(("inputCapacity", 16), ("cycleTime", 1.45f), ("inputsPerCycle", 1), ("outputsPerCycle", 1), ("upgradeCost", 2200)),
            Lv(("inputCapacity", 22), ("cycleTime", 1.15f), ("inputsPerCycle", 1), ("outputsPerCycle", 1), ("upgradeCost", 4500)),
            Lv(("inputCapacity", 30), ("cycleTime", 0.9f), ("inputsPerCycle", 1), ("outputsPerCycle", 1), ("upgradeCost", 8000)),
            Lv(("inputCapacity", 40), ("cycleTime", 0.7f), ("inputsPerCycle", 1), ("outputsPerCycle", 1), ("upgradeCost", 14000)),
        });

        var rack = LoadOrCreate<StorageDefinition>(DataDir + "/Factory/Storage_IngotRack.asset");
        SetMany(rack, ("id", "ingot_rack"), ("displayName", "Ingot Rack"));
        SetStructArray(rack, "levels", new[]
        {
            Lv(("capacity", 24), ("pickupInterval", 0.07f), ("upgradeCost", 0)),
            Lv(("capacity", 40), ("pickupInterval", 0.06f), ("upgradeCost", 1200)),
            Lv(("capacity", 60), ("pickupInterval", 0.05f), ("upgradeCost", 2800)),
            Lv(("capacity", 90), ("pickupInterval", 0.045f), ("upgradeCost", 6000)),
        });

        // ---------- the Metal Market buys ingots; customers ask for them once the Furnace exists ----------
        var market = Load<SellDeskDefinition>(DataDir + "/Factory/SellDesk_Market.asset");
        SetArray(market, "sells", new Object[] { iron, copper, ironIngot, copperIngot });
        var marketCustomers = Load<CustomerConfig>(DataDir + "/Customers/CustomerConfig_Market.asset");
        SetStructArray(marketCustomers, "orderOptions", new[]
        {
            Lv(("item", iron), ("weight", 0.42f)), Lv(("item", copper), ("weight", 0.24f)), Lv(("item", ironIngot), ("weight", 0.22f)),
            Lv(("item", copperIngot), ("weight", 0.12f)),
        });

        // ---------- Smelter: carries metal from the bins into the Furnace ----------
        var smelter = LoadOrCreate<WorkerDefinition>(DataDir + "/Workers/Worker_Smelter.asset");
        SetMany(smelter, ("id", "smelter"), ("displayName", "Smelter"), ("role", (int)WorkerRole.Porter), ("siteId", "smelter"),
            ("tagline", "FEEDS THE FURNACE"), ("moveSpeed", 3.8f), ("carryCapacity", 8), ("efficiency", 1f), ("pickupRadius", 1f),
            ("collectLooseItems", false));
        SetLongArray(smelter, "hireCosts", new long[] { 3000, 6000 });

        // ---------- heavy scrap (blueprint tier 3, large class 25-60 pieces, rare material chance) ----------
        var scrapItem = Load<ItemDefinition>(DataDir + "/Items/Item_Scrap.asset");
        var heavyHit = Load<SfxDefinition>(DataDir + "/Audio/Sfx_HeavyHit.asset");
        var heavyBreak = Load<SfxDefinition>(DataDir + "/Audio/Sfx_HeavyBreak.asset");
        HeavyDef("Tractor", "tractor", "Tractor", 700f, 30f, new Vector2Int(25, 32), copper, 0.5f, new Vector2Int(2, 4), 25, 20f, 0.4f, scrapItem, heavyHit,
            heavyBreak);
        HeavyDef("Truck", "truck", "Truck", 900f, 35f, new Vector2Int(30, 40), iron, 0.6f, new Vector2Int(3, 6), 30, 22f, 0.45f, scrapItem, heavyHit, heavyBreak);
        HeavyDef("GarbageTruck", "garbage_truck", "Garbage Truck", 1300f, 45f, new Vector2Int(45, 60), iron, 1f, new Vector2Int(4, 8), 45, 30f, 0.6f, scrapItem,
            heavyHit, heavyBreak);

        // ---------- unlocks ----------
        var hall = LoadOrCreate<ExpansionDefinition>(DataDir + "/World/Expansion_FurnaceHall.asset");
        SetMany(hall, ("id", "furnace_hall"), ("displayName", "Furnace"), ("cost", 5000L), ("teaser", "IRON + COPPER INGOTS"),
            ("openSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_AreaOpen.asset")));
        var heavy = LoadOrCreate<ExpansionDefinition>(DataDir + "/World/Expansion_HeavyYard.asset");
        SetMany(heavy, ("id", "heavy_yard"), ("displayName", "Heavy Scrap Yard"), ("cost", 10000L), ("teaser", "TRUCKS & TRACTORS"),
            ("openSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_AreaOpen.asset")));
        // Gate 2 was a long save (17 min in the guide-bot run): cheaper, and the chain now asks for the desk upgrade first.
        SetMany(Load<ExpansionDefinition>(DataDir + "/World/Expansion_RecyclingPlant.asset"), ("cost", 3000L));

        var catalog = Load<UpgradeCatalog>(DataDir + "/Progression/UpgradeCatalog.asset");
        SetStructArray(catalog, "entries", new[]
        {
            Lv(("upgradeId", "chainsaw"), ("unlockLevel", 1), ("inPanel", true), ("group", "YOU")),
            Lv(("upgradeId", "backpack"), ("unlockLevel", 2), ("inPanel", true), ("group", "YOU")),
            Lv(("upgradeId", "boots"), ("unlockLevel", 4), ("inPanel", true), ("group", "YOU")),
            Lv(("upgradeId", "crusher"), ("unlockLevel", 1), ("inPanel", true), ("group", "OLD YARD")),
            Lv(("upgradeId", "sell_desk"), ("unlockLevel", 1), ("inPanel", true), ("group", "OLD YARD")),
            Lv(("upgradeId", "storage_yard"), ("unlockLevel", 3), ("inPanel", true), ("group", "OLD YARD")),
            Lv(("upgradeId", "sorter"), ("unlockLevel", 6), ("inPanel", true), ("group", "RECYCLING PLANT")),
            Lv(("upgradeId", "metal_bins"), ("unlockLevel", 6), ("inPanel", true), ("group", "RECYCLING PLANT")),
            Lv(("upgradeId", "market"), ("unlockLevel", 6), ("inPanel", true), ("group", "RECYCLING PLANT")),
            Lv(("upgradeId", "furnace"), ("unlockLevel", 8), ("inPanel", true), ("group", "FURNACE")),
            Lv(("upgradeId", "ingot_rack"), ("unlockLevel", 8), ("inPanel", true), ("group", "FURNACE")),
            Lv(("upgradeId", "porter"), ("unlockLevel", 3), ("inPanel", false), ("group", "")),
            Lv(("upgradeId", "helper"), ("unlockLevel", 4), ("inPanel", false), ("group", "")),
            Lv(("upgradeId", "back_lot"), ("unlockLevel", 5), ("inPanel", false), ("group", "")),
            Lv(("upgradeId", "recycling_plant"), ("unlockLevel", 6), ("inPanel", false), ("group", "")),
            Lv(("upgradeId", "hauler"), ("unlockLevel", 6), ("inPanel", false), ("group", "")),
            Lv(("upgradeId", "runner"), ("unlockLevel", 7), ("inPanel", false), ("group", "")),
            Lv(("upgradeId", "furnace_hall"), ("unlockLevel", 8), ("inPanel", false), ("group", "")),
            Lv(("upgradeId", "smelter"), ("unlockLevel", 8), ("inPanel", false), ("group", "")),
            Lv(("upgradeId", "heavy_yard"), ("unlockLevel", 9), ("inPanel", false), ("group", "")),
        });

        BuildTasks();
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    static void HeavyDef(string asset, string id, string name, float hp, float minPower, Vector2Int drops, ItemDefinition rare, float rareChance,
        Vector2Int rareAmount, int xp, float respawn, float burst, ItemDefinition item, SfxDefinition hit, SfxDefinition brk)
    {
        var d = LoadOrCreate<ScrapDefinition>($"{DataDir}/Scrap/Scrap_{asset}.asset");
        SetMany(d, ("id", id), ("displayName", name), ("tier", 3), ("sizeClass", (int)ScrapSizeClass.Large), ("maxHealth", hp), ("minCutPower", minPower),
            ("dropItem", item), ("dropAmount", drops), ("partDropShare", 0.3f), ("dropLaunchSpeed", 6.5f), ("dropSpread", 1.6f), ("dropBurstDuration", burst),
            ("rareDropItem", rare), ("rareDropChance", rareChance), ("rareDropAmount", rareAmount), ("xpReward", xp), ("respawnDelay", respawn),
            ("hitSfx", hit), ("breakSfx", brk), ("breakVfxScale", 2.1f), ("breakShake", 0.8f));
    }

    static void BuildTasks()
    {
        string dir = DataDir + "/Progression/Tasks";
        var chain = Load<TaskChain>(DataDir + "/Progression/TaskChain_Area1.asset");
        var so = new SerializedObject(chain);
        var mainProp = so.FindProperty("mainTasks");
        var main = new List<Object>();
        for (int i = 0; i < mainProp.arraySize; i++) main.Add(mainProp.GetArrayElementAtIndex(i).objectReferenceValue);
        string[] ours = { "t18b_desk3", "t19b_crusher3" };
        main.RemoveAll(t => t == null || ours.Contains(((TaskDefinition)t).Id) || IsM6Task(((TaskDefinition)t).Id));

        // Pacing fix from the guide-bot run: the Sell Desk at Lv.2 capped income at ~$210/min while the player saved 17 minutes
        // for Gate 2. Point at the bottleneck (blueprint: one clear bottleneck, upgrade it, the next one appears).
        int fridges = main.FindIndex(t => ((TaskDefinition)t).Id == "t18_fridges");
        main.Insert(fridges + 1, Task(dir, "t18b_desk3", "Sell desk to Lv.{0}", TaskCategory.Main, TaskType.ReachUpgradeLevel, "sell_desk", 3, 0, 40));
        int crush = main.FindIndex(t => ((TaskDefinition)t).Id == "t19_crush150");
        main.Insert(crush + 1, Task(dir, "t19b_crusher3", "Crusher to Lv.{0}", TaskCategory.Main, TaskType.ReachUpgradeLevel, "crusher", 3, 0, 40));

        main.AddRange(new Object[]
        {
            Task(dir, "t28_furnace", "Build the Furnace", TaskCategory.Main, TaskType.ReachUpgradeLevel, "furnace_hall", 1, 0, 120),
            Task(dir, "t29_smelt", "Smelt {0} ingots", TaskCategory.Main, TaskType.ProcessItems, "furnace", 12, 300, 60),
            Task(dir, "t30_iron_ingots", "Sell {0} iron ingots", TaskCategory.Main, TaskType.SellItems, "iron_ingot", 15, 400, 60),
            Task(dir, "t31_boost_furnace", "Boost the furnace", TaskCategory.Main, TaskType.Overdrive, "furnace", 1, 100, 40),
            Task(dir, "t32_smelter", "Hire a smelter", TaskCategory.Main, TaskType.HireWorker, "smelter", 1, 0, 60),
            Task(dir, "t33_level9", "Reach yard Lv.{0}", TaskCategory.Main, TaskType.ReachLevel, "", 9, 500, 0),
            Task(dir, "t34_heavy_yard", "Open the Heavy Scrap Yard", TaskCategory.Main, TaskType.ReachUpgradeLevel, "heavy_yard", 1, 0, 150),
            Task(dir, "t35_trucks", "Cut {0} trucks", TaskCategory.Main, TaskType.BreakScrap, "truck", 2, 600, 80),
            Task(dir, "t36_copper_ingots", "Sell {0} copper ingots", TaskCategory.Main, TaskType.SellItems, "copper_ingot", 12, 600, 80),
            Task(dir, "t37_crush500", "Crush {0} scrap", TaskCategory.Main, TaskType.ProcessItems, "crusher", 500, 1500, 200),
        });
        SetArray(chain, "mainTasks", main.ToArray());
    }

    static bool IsM6Task(string id) => id.Length > 3 && id[0] == 't' && int.TryParse(id.Substring(1, 2), out int n) && n >= 28;

    static TaskDefinition Task(string dir, string id, string title, TaskCategory category, TaskType type, string target, long amount, long cash, int xp)
    {
        var t = LoadOrCreate<TaskDefinition>($"{dir}/Task_{id}.asset");
        SetMany(t, ("id", id), ("title", title), ("category", (int)category), ("type", (int)type), ("targetId", target), ("amount", amount),
            ("rewardCash", cash), ("rewardXp", xp), ("rewardPremium", 0));
        return t;
    }

    // =====================================================================================
    // PREFABS
    // =====================================================================================

    public static string Prefabs()
    {
        Log.Clear();
        var mSteelDark = Load<Material>(MatDir + "/M_SteelDark.mat");
        var mSteel = Load<Material>(MatDir + "/M_SteelMid.mat");
        var mHazard = Load<Material>(MatDir + "/M_HazardYellow.mat");
        var mConcrete = Load<Material>(MatDir + "/M_ConcretePad.mat");
        var mLight = Load<Material>(MatDir + "/M_StatusLight.mat");
        var mBrick = Lit("FurnaceBrick", new Color(0.7f, 0.25f, 0.16f), 0.25f);
        var mBrickDark = Lit("FurnaceBrickDark", new Color(0.5f, 0.17f, 0.12f), 0.25f);
        var mCrown = Lit("FurnaceCrown", new Color(1f, 0.62f, 0.24f), 0.7f, 0.3f);
        var mGlow = Emissive("FurnaceGlow", HeatOrange, 2.5f);

        // ---------- ingots ----------
        var ironDef = Load<ItemDefinition>(DataDir + "/Items/Item_IronIngot.asset");
        var copperDef = Load<ItemDefinition>(DataDir + "/Items/Item_CopperIngot.asset");
        var mIronIngot = Lit("IronIngot", IronIngotTint, 0.75f, 0.3f);
        var mIronIngotHi = Lit("IronIngotHi", new Color(0.84f, 0.88f, 0.95f), 0.8f, 0.3f);
        var mCopperIngot = Lit("CopperIngot", CopperIngotTint, 0.72f, 0.3f);
        var mCopperIngotHi = Lit("CopperIngotHi", new Color(1f, 0.7f, 0.38f), 0.78f, 0.3f);
        var ingotMesh = BakeIngots();
        var ironPrefab = SavePrefab(BuildItem("Item_IronIngot", ingotMesh, new[] { mIronIngot, mIronIngotHi }, new[] { mIronIngotHi, mIronIngot }, 0.1f),
            PrefabDir + "/Items/Item_IronIngot.prefab");
        var copperPrefab = SavePrefab(BuildItem("Item_CopperIngot", ingotMesh, new[] { mCopperIngot, mCopperIngotHi }, new[] { mCopperIngotHi, mCopperIngot }, 0.1f),
            PrefabDir + "/Items/Item_CopperIngot.prefab");
        SetMany(ironDef, ("prefab", ironPrefab.GetComponent<WorldItem>()));
        SetMany(copperDef, ("prefab", copperPrefab.GetComponent<WorldItem>()));

        // ---------- VFX: chimney smoke with embers (plays while the furnace works) ----------
        var smoke = SavePrefab(BuildFurnaceSmoke(), PrefabDir + "/VFX/VFX_FurnaceSmoke.prefab");

        // ---------- Furnace ----------
        var labelPrefab = Load<GameObject>(PrefabDir + "/Stations/StationLabel.prefab");
        var padPrefab = Load<GameObject>(PrefabDir + "/Stations/PadVisual.prefab");
        var sparks = Load<GameObject>(PrefabDir + "/VFX/VFX_OverdriveSparks.prefab");
        var hitSparks = Load<GameObject>(PrefabDir + "/VFX/VFX_HitSparks.prefab");
        SavePrefab(BuildFurnace(Load<MachineDefinition>(DataDir + "/Factory/Machine_Furnace.asset"), labelPrefab, padPrefab, sparks, hitSparks, smoke, mBrick,
            mBrickDark, mCrown, mGlow, mSteelDark, mSteel, mHazard, mConcrete, mLight), PrefabDir + "/Stations/Furnace.prefab");

        // ---------- Smelter worker (heat-red hard hat) ----------
        var controller = Load<AnimatorController>(AnimDir + "/AC_Worker.controller");
        var smelter = SavePrefab(BuildPadWorker("Worker_Smelter", "character-male-e", controller, Lit("CapRed", new Color(0.86f, 0.24f, 0.14f), 0.5f), mSteelDark),
            PrefabDir + "/Workers/Worker_Smelter.prefab");
        SetMany(Load<WorkerDefinition>(DataDir + "/Workers/Worker_Smelter.asset"), ("prefab", smelter.GetComponent<Worker>()));

        // ---------- heavy scrap ----------
        var heavyWreck = HeavyWreckMaterial();
        var barBg = Load<Material>(MatDir + "/M_BarBg.mat");
        var barFill = Load<Material>(MatDir + "/M_BarFill.mat");
        HeavyPrefabs("Tractor", heavyWreck, barBg, barFill, ("tractor", 4.4f), ("tractor-shovel", 5.0f));
        HeavyPrefabs("Truck", heavyWreck, barBg, barFill, ("delivery", 5.6f), ("delivery-flat", 5.6f));
        HeavyPrefabs("GarbageTruck", heavyWreck, barBg, barFill, ("garbage-truck", 6.2f), ("firetruck", 6.2f));

        Icons();
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    /// <summary>Renders the M6 icons (ingots show in customer bubbles and the HUD, the rest on cards and tiles).</summary>
    public static string Icons()
    {
        SetMany(Load<ItemDefinition>(DataDir + "/Items/Item_IronIngot.asset"),
            ("icon", RenderIcon("Icon_IronIngot", Load<GameObject>(PrefabDir + "/Items/Item_IronIngot.prefab"), new Vector3(38f, 30f, 0f))));
        SetMany(Load<ItemDefinition>(DataDir + "/Items/Item_CopperIngot.asset"),
            ("icon", RenderIcon("Icon_CopperIngot", Load<GameObject>(PrefabDir + "/Items/Item_CopperIngot.prefab"), new Vector3(38f, 30f, 0f))));
        var furnace = RenderIcon("Icon_Furnace", PrefabRoot("Stations/Furnace", "InputPad", "StationLabel", "VFX_OverdriveSparks", "VFX_FurnaceSmoke", "Markers"),
            new Vector3(24f, -30f, 0f));
        SetMany(Load<MachineDefinition>(DataDir + "/Factory/Machine_Furnace.asset"), ("icon", furnace));
        SetMany(Load<ExpansionDefinition>(DataDir + "/World/Expansion_FurnaceHall.asset"), ("icon", furnace));
        SetMany(Load<WorkerDefinition>(DataDir + "/Workers/Worker_Smelter.asset"),
            ("icon", RenderIcon("Icon_Smelter", Load<GameObject>(PrefabDir + "/Workers/Worker_Smelter.prefab"), new Vector3(15f, 160f, 0f), "idle")));
        var truck = RenderIcon("Icon_HeavyYard", PrefabRoot("Scrap/Scrap_GarbageTruck_garbage-truck", "HealthBar"), new Vector3(24f, 150f, 0f));
        SetMany(Load<ExpansionDefinition>(DataDir + "/World/Expansion_HeavyYard.asset"), ("icon", truck));
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    /// <summary>Three stacked trapezoid bars: the classic ingot pile, readable at stack size from the game camera.</summary>
    static Mesh BakeIngots()
    {
        var holder = new GameObject("tmp");
        var tmp = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var bottom = new[]
        {
            Ingot(holder.transform, new Vector3(-0.115f, -0.05f, 0f), tmp), Ingot(holder.transform, new Vector3(0.115f, -0.05f, 0f), tmp),
        };
        var top = new[] { Ingot(holder.transform, new Vector3(0f, 0.05f, 0f), tmp) };
        var mesh = CombineSubmeshes("Ingots", bottom, top);
        Object.DestroyImmediate(holder);
        return mesh;
    }

    static ProBuilderMesh Ingot(Transform parent, Vector3 position, Material mat)
    {
        var pb = Box("Ingot", parent, position, new Vector3(0.21f, 0.1f, 0.42f), mat, 0.012f);
        // Taper the top so it reads as a cast bar, not a brick.
        var positions = pb.positions.ToArray();
        for (int i = 0; i < positions.Length; i++)
        {
            float t = Mathf.InverseLerp(-0.05f, 0.05f, positions[i].y);
            positions[i] = new Vector3(positions[i].x * Mathf.Lerp(1f, 0.74f, t), positions[i].y, positions[i].z * Mathf.Lerp(1f, 0.86f, t));
        }

        pb.positions = positions;
        pb.ToMesh();
        pb.Refresh();
        return pb;
    }

    static GameObject BuildFurnaceSmoke()
    {
        var go = new GameObject("VFX_FurnaceSmoke");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.42f, 0.4f, 0.4f, 0.55f), new Color(0.3f, 0.29f, 0.3f, 0.5f));
        main.gravityModifier = -0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 60;
        var emission = ps.emission;
        emission.rateOverTime = 7f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.2f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.8f));
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = Load<Material>(MatDir + "/M_FX_Smoke.mat");
        r.shadowCastingMode = ShadowCastingMode.Off;

        var embers = new GameObject("Embers");
        embers.transform.SetParent(go.transform, false);
        var eps = embers.AddComponent<ParticleSystem>();
        eps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var em = eps.main;
        em.loop = true;
        em.playOnAwake = false;
        em.duration = 1f;
        em.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        em.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        em.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
        em.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f), new Color(1f, 0.85f, 0.35f));
        em.gravityModifier = -0.15f;
        em.simulationSpace = ParticleSystemSimulationSpace.World;
        em.scalingMode = ParticleSystemScalingMode.Hierarchy;
        em.maxParticles = 40;
        var ee = eps.emission;
        ee.rateOverTime = 12f;
        var es = eps.shape;
        es.shapeType = ParticleSystemShapeType.Cone;
        es.angle = 20f;
        es.radius = 0.15f;
        es.rotation = new Vector3(-90f, 0f, 0f);
        var er = embers.GetComponent<ParticleSystemRenderer>();
        er.sharedMaterial = Load<Material>(MatDir + "/M_FX_Sparks.mat");
        er.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    /// <summary>
    /// Red-brick furnace on a steel base: inputs pile on the west shelf and fly into a glowing mouth on the south face
    /// (towards the camera); ingots drop down a chute below it. Chimney smoke and embers while working, a bellows piston
    /// per cycle, a side fan, and more chimney / a copper crown at higher levels.
    /// </summary>
    static GameObject BuildFurnace(MachineDefinition def, GameObject labelPrefab, GameObject padPrefab, GameObject sparksPrefab, GameObject hitSparksPrefab,
        GameObject smokePrefab, Material brick, Material brickDark, Material crown, Material glow, Material steelDark, Material steel, Material hazard,
        Material concrete, Material lightMat)
    {
        var root = new GameObject("Furnace");
        Box("Plinth", root.transform, new Vector3(0f, 0.12f, 0.1f), new Vector3(4.8f, 0.24f, 4.4f), concrete, 0.04f);

        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        body.localPosition = new Vector3(0f, 0.24f, 0f);
        Box("Base", body, new Vector3(0f, 0.6f, 0.15f), new Vector3(4.0f, 1.2f, 3.6f), steelDark, 0.08f);
        Box("Band", body, new Vector3(0f, 1.22f, 0.15f), new Vector3(4.06f, 0.14f, 3.66f), hazard, 0.02f);
        Cylinder("Shell", body, new Vector3(0f, 2.35f, 0.3f), 1.65f, 2.2f, 16, brick);
        Cylinder("RingLow", body, new Vector3(0f, 1.42f, 0.3f), 1.74f, 0.18f, 16, steelDark);
        Cylinder("RingHigh", body, new Vector3(0f, 3.4f, 0.3f), 1.74f, 0.18f, 16, steelDark);
        Cylinder("Cap", body, new Vector3(0f, 3.62f, 0.3f), 1.25f, 0.34f, 16, brickDark);
        Cylinder("Chimney", body, new Vector3(0.75f, 5.0f, 0.75f), 0.38f, 2.8f, 12, steelDark);
        Cylinder("ChimneyTop", body, new Vector3(0.75f, 6.45f, 0.75f), 0.47f, 0.22f, 12, brickDark);

        // Mouth: dark frame with a glowing core the visuals breathe and flare.
        Box("MouthFrame", body, new Vector3(0f, 1.95f, -1.32f), new Vector3(1.55f, 1.25f, 0.34f), steelDark, 0.05f);
        var mouth = Box("MouthGlow", body, new Vector3(0f, 1.92f, -1.5f), new Vector3(1.15f, 0.88f, 0.06f), glow, 0f);
        Box("MouthLip", body, new Vector3(0f, 1.28f, -1.58f), new Vector3(1.7f, 0.12f, 0.3f), hazard, 0.02f);

        // West shelf: the hopper the input pad fills (stock level readable from the camera).
        Box("Shelf", body, new Vector3(-1.75f, 1.32f, -0.2f), new Vector3(1.05f, 0.12f, 1.5f), steel, 0.02f);
        var hopperPile = Empty("HopperPile", body, new Vector3(-1.75f, 1.42f, -0.2f)).gameObject.AddComponent<ItemPile>();
        SetMany(hopperPile, ("capacity", 12), ("columns", 2), ("rows", 3), ("cellSize", new Vector3(0.44f, 0.22f, 0.44f)), ("arriveDuration", 0.32f),
            ("arriveArc", 1.6f));

        // Bellows piston on the east side, a fan behind it.
        Box("BellowsFrame", body, new Vector3(1.85f, 1.9f, -0.6f), new Vector3(0.7f, 0.12f, 0.7f), steelDark, 0.02f);
        var bellows = Cylinder("Bellows", body, new Vector3(1.85f, 1.62f, -0.6f), 0.26f, 0.5f, 12, steel);
        var fan = Kenney("FactoryKit", "cog-a", body, new Vector3(2.08f, 1.05f, 0.75f), 1f, new Vector3(0f, 0f, 90f), Load<Material>(MatDir + "/M_Kenney_FactoryKit.mat"));
        var fb = RendererBounds(fan).size;
        fan.transform.localScale = Vector3.one * (0.95f / Mathf.Max(fb.y, fb.z, 0.01f));

        var light = Cylinder("StatusLight", body, new Vector3(1.25f, 1.0f, -1.42f), 0.15f, 0.12f, 12, lightMat);
        light.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var intake = Empty("Intake", body, new Vector3(0f, 1.9f, -1.1f));
        var chute = Box("Chute", root.transform, new Vector3(0f, 0.72f, -2.0f), new Vector3(1.0f, 0.1f, 0.9f), steelDark, 0.02f);
        chute.transform.localRotation = Quaternion.Euler(-22f, 0f, 0f);
        var outputPoint = Empty("OutputPoint", root.transform, new Vector3(0f, 0.7f, -2.3f));

        var smoke = (GameObject)PrefabUtility.InstantiatePrefab(smokePrefab, body);
        smoke.transform.localPosition = new Vector3(0.75f, 6.6f, 0.75f);
        var sparks = (GameObject)PrefabUtility.InstantiatePrefab(sparksPrefab, body);
        sparks.transform.localPosition = new Vector3(0f, 3.9f, 0.3f);
        var burst = (GameObject)PrefabUtility.InstantiatePrefab(hitSparksPrefab, root.transform);
        burst.name = "VFX_IngotSparks";
        burst.transform.localPosition = new Vector3(0f, 1.9f, -1.7f);

        // Level visuals: a second chimney at Lv.3, a copper crown at Lv.5.
        var tier3 = new GameObject("Lv3_Chimney");
        tier3.transform.SetParent(body, false);
        Cylinder("Chimney2", tier3.transform, new Vector3(-0.8f, 4.7f, 0.85f), 0.3f, 2.2f, 12, steelDark);
        Cylinder("Chimney2Top", tier3.transform, new Vector3(-0.8f, 5.85f, 0.85f), 0.38f, 0.2f, 12, brickDark);
        var tier5 = new GameObject("Lv5_Crown");
        tier5.transform.SetParent(body, false);
        Cylinder("Crown", tier5.transform, new Vector3(0f, 3.86f, 0.3f), 1.36f, 0.16f, 16, crown);
        Cylinder("ChimneyBand", tier5.transform, new Vector3(0.75f, 5.6f, 0.75f), 0.42f, 0.16f, 12, crown);
        var levelVisuals = SetupLevelVisuals(root, (3, tier3), (5, tier5));

        var labelGo = (GameObject)PrefabUtility.InstantiatePrefab(labelPrefab, root.transform);
        // In front of the chimney (camera side) so the level chip is never hidden behind it.
        labelGo.transform.localPosition = new Vector3(-0.6f, 4.9f, -1.0f);
        labelGo.transform.localScale = Vector3.one * 1.4f;

        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 1.9f, 0.15f);
        box.size = new Vector3(4.3f, 3.8f, 3.9f);

        var visuals = root.AddComponent<MachineVisuals>();
        SetMany(visuals, ("body", body), ("spinAxis", Vector3.up), ("spinSpeed", 300f), ("workParticles", smoke.GetComponent<ParticleSystem>()),
            ("statusLight", light.GetComponent<Renderer>()), ("overdriveParticles", sparks.GetComponent<ParticleSystem>()), ("pistonTravel", 0.3f),
            ("glow", mouth.GetComponent<Renderer>()), ("glowColor", HeatOrange), ("outputBurst", burst.GetComponent<ParticleSystem>()));
        SetArray(visuals, "spinners", new Object[] { fan.transform });
        SetArray(visuals, "pistons", new Object[] { bellows.transform });

        var machine = root.AddComponent<Machine>();
        SetMany(machine, ("definition", def), ("hopper", hopperPile), ("intake", intake), ("outputPoint", outputPoint), ("visuals", visuals),
            ("label", labelGo.GetComponent<StationLabel>()), ("levelVisuals", levelVisuals));
        SetStructArray(machine, "outputPorts", Array.Empty<Dictionary<string, object>>());

        var pad = Pad(root.transform, "InputPad", new Vector3(-3.75f, 0f, -0.2f), new Vector2(2.2f, 2.2f), TransferMode.Deposit, machine, new Color(1f, 0.78f, 0.15f),
            padPrefab, 90f, "Sfx_PadTransfer");
        StationAnchor(pad.transform, machine, "in");
        return root;
    }

    static Material HeavyWreckMaterial()
    {
        var src = Load<Material>(MatDir + "/M_Kenney_CarWreck.mat");
        var m = LoadOrCreateMaterial("Kenney_HeavyWreck", "Universal Render Pipeline/Lit");
        if (src != null) m.CopyPropertiesFromMaterial(src);
        // Dusty machinery yellow under the rust, so heavy vehicles read apart from the car wrecks.
        m.SetColor("_BaseColor", new Color(0.8f, 0.62f, 0.46f));
        EditorUtility.SetDirty(m);
        return m;
    }

    static void HeavyPrefabs(string asset, Material wreck, Material barBg, Material barFill, params (string model, float length)[] models)
    {
        var def = Load<ScrapDefinition>($"{DataDir}/Scrap/Scrap_{asset}.asset");
        // Drop variants this builder made earlier but no longer uses (e.g. the pickup "truck" that read as a car).
        foreach (var guid in AssetDatabase.FindAssets($"Scrap_{asset}_ t:Prefab", new[] { PrefabDir + "/Scrap" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!models.Any(m => path.EndsWith($"Scrap_{asset}_{m.model}.prefab"))) AssetDatabase.DeleteAsset(path);
        }

        var variants = new List<Object>();
        foreach (var (model, length) in models)
            variants.Add(SavePrefab(BuildHeavy($"Scrap_{asset}_{model}", model, length, def, wreck, barBg, barFill), $"{PrefabDir}/Scrap/Scrap_{asset}_{model}.prefab")
                .GetComponent<ScrapObject>());
        SetMany(def, ("prefab", variants[0]));
        SetArray(def, "prefabVariants", variants.ToArray());
    }

    /// <summary>
    /// A wrecked heavy vehicle: Kenney model in rusty machinery yellow, sagging on a missing wheel; the other wheels, a door
    /// and a roof plate come off as the chainsaw works through it.
    /// </summary>
    static GameObject BuildHeavy(string name, string model, float length, ScrapDefinition def, Material wreck, Material barBg, Material barFill)
    {
        var root = ScrapRoot(name, def, out var visual);
        var vehicle = Kenney("CarKit", model, visual, Vector3.zero, 1f, new Vector3(0f, 0f, 2.5f), wreck);
        var b = RendererBounds(vehicle);
        vehicle.transform.localScale = Vector3.one * (length / Mathf.Max(b.size.x, b.size.z, 0.01f));
        var driver = FindDeep(vehicle.transform, "character");
        if (driver != null) driver.gameObject.SetActive(false);

        var wheels = vehicle.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("wheel")).ToList();
        if (wheels.Count > 0) wheels[0].gameObject.SetActive(false);
        int order = 0;
        for (int i = 1; i < wheels.Count; i++) Part(wheels[i].gameObject, order++ * 2);

        b = RendererBounds(vehicle);
        vehicle.transform.position += new Vector3(-b.center.x, -b.min.y, -b.center.z);
        b = RendererBounds(vehicle);
        var door = Kenney("CarKit", "debris-door", visual, Vector3.zero, length * 0.36f, new Vector3(0f, -90f, 0f), wreck);
        door.transform.position = new Vector3(b.min.x - 0.02f, b.size.y * 0.4f, b.center.z + 0.2f);
        Part(door, 1);
        var plate = Kenney("CarKit", "debris-plate-small-a", visual, Vector3.zero, length * 0.3f, new Vector3(0f, 25f, 8f), wreck);
        plate.transform.position = new Vector3(b.center.x + b.size.x * 0.2f, b.max.y - 0.04f, b.center.z + b.size.z * 0.2f);
        Part(plate, 3);

        FitCollider(root, vehicle, 0.95f);
        HealthBar(root.transform, b.max.y + 0.8f, 2f, barBg, barFill);
        Carve(root);
        return root;
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
        var font = Load<TMP_FontAsset>(FontDir + "/GROBOLD SDF.asset");
        var worldText = Load<Material>(FontDir + "/GROBOLD World.mat");

        var plantContent = gameplay.Find("RecyclingPlant/Content");
        var ironPad = plantContent.Find("Bin_Iron/WithdrawPad").GetComponent<TransferPad>();
        var copperPad = plantContent.Find("Bin_Copper/WithdrawPad").GetComponent<TransferPad>();
        var market = plantContent.Find("MetalMarket").GetComponent<SellDesk>();
        var ironIngot = Load<ItemDefinition>(DataDir + "/Items/Item_IronIngot.asset");
        var copperIngot = Load<ItemDefinition>(DataDir + "/Items/Item_CopperIngot.asset");
        SetArray(market.transform.Find("CounterPile").GetComponent<ItemPile>(), "accepted", new Object[]
        {
            Load<ItemDefinition>(DataDir + "/Items/Item_Iron.asset"), Load<ItemDefinition>(DataDir + "/Items/Item_Copper.asset"), ironIngot, copperIngot
        });

        var hall = BuildFurnaceHall(gameplay, plantContent, font, worldText, ironIngot, copperIngot);
        var heavy = BuildHeavyYard(gameplay, environment, font, worldText);

        // The guide marker hides only once the player stands on the pad (pads are 2.2 m: 1.1 m from centre to rim).
        SetMany(systems.Find("GuideDirector").GetComponent<GuideDirector>(), ("arriveRadius", 0.9f));

        // ---------- workers ----------
        var sites = gameplay.Find("Workers/Sites");
        var smelterRoute = Route(sites, "SmelterRoute", "smelter", new Vector3(62.4f, 0f, 35.4f), new Object[] { ironPad, copperPad }, hall.furnacePad);
        var runRoute = sites.Find("RunnerRoute").GetComponent<PorterRoute>();
        SetArray(runRoute, "pickupPads", new Object[] { ironPad, copperPad, hall.rackPad });
        var porterRoute = sites.Find("PorterRoute").GetComponent<PorterRoute>();
        var heavyZone = FindOrCreate(porterRoute.transform, "HeavyYardZone");
        heavyZone.position = new Vector3(18f, 0f, 55f);
        SetStructArray(porterRoute, "extraZones", new[]
        {
            Lv(("center", porterRoute.transform.Find("BackLotZone")), ("radius", 9f)), Lv(("center", heavyZone), ("radius", 16f)),
        });

        var wm = systems.Find("WorkerManager").GetComponent<WorkerManager>();
        SetArray(wm, "hireable", new Object[]
        {
            Load<WorkerDefinition>(DataDir + "/Workers/Worker_Porter.asset"), Load<WorkerDefinition>(DataDir + "/Workers/Worker_Helper.asset"),
            Load<WorkerDefinition>(DataDir + "/Workers/Worker_Hauler.asset"), Load<WorkerDefinition>(DataDir + "/Workers/Worker_Runner.asset"),
            Load<WorkerDefinition>(DataDir + "/Workers/Worker_Smelter.asset"),
        });
        SetArray(wm, "sites", new Object[]
        {
            porterRoute, sites.Find("DeliveryRoute").GetComponent<PorterRoute>(), sites.Find("HaulerRoute").GetComponent<PorterRoute>(), runRoute, smelterRoute
        });

        // ---------- NavMesh over Areas 1-3 (locked content active while baking so machines carve their holes) ----------
        var surface = environment.Find("NavMesh").GetComponent<NavMeshSurface>();
        surface.center = new Vector3(38f, 1f, 42f);
        surface.size = new Vector3(80f, 6f, 66f);
        var toggled = new List<GameObject>();
        foreach (var content in new[] { plantContent, hall.content, heavy.content })
            foreach (Transform c in content)
                if (!c.gameObject.activeSelf)
                {
                    c.gameObject.SetActive(true);
                    toggled.Add(c.gameObject);
                }

        surface.BuildNavMesh();
        foreach (var go in toggled) go.SetActive(false);
        // Like the rest of the plant content, the furnace plot and its tile stay hidden until Gate 2 opens.
        plantContent.Find("FurnacePlot").gameObject.SetActive(false);
        plantContent.Find("Tile_FurnaceHall").gameObject.SetActive(false);
        string navDir = Path.Combine(Path.GetDirectoryName(ScenePath), Path.GetFileNameWithoutExtension(ScenePath));
        string navPath = navDir.Replace('\\', '/') + "/NavMesh-Area1.asset";
        if (File.Exists(navPath)) AssetDatabase.DeleteAsset(navPath);
        AssetDatabase.CreateAsset(surface.navMeshData, navPath);
        EditorUtility.SetDirty(surface);
        Log.AppendLine("navmesh baked: " + navPath);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("Saved " + ScenePath);
        return Log.ToString();
    }

    struct HallRefs
    {
        public Transform content;
        public TransferPad furnacePad, rackPad;
    }

    /// <summary>
    /// The Furnace hall: a fenced plot in the plant's north-east corner (fence + sign appear with the plant), bought on a
    /// tile beside it. Opening sinks the fence and pops in the floor, furnace, belt, ingot rack, boost pad and Smelter tile.
    /// </summary>
    static HallRefs BuildFurnaceHall(Transform gameplay, Transform plantContent, TMP_FontAsset font, Material worldText, ItemDefinition ironIngot,
        ItemDefinition copperIngot)
    {
        var refs = new HallRefs();
        var root = FindOrCreate(gameplay, "FurnaceHall");
        root.position = Vector3.zero;
        if (!root.TryGetComponent(out Expansion expansion)) expansion = root.gameObject.AddComponent<Expansion>();
        var content = ResetChild(root, "Content");
        refs.content = content;

        // Locked state lives inside the plant content, so it only shows once the plant is open.
        var plot = ResetChild(plantContent, "FurnacePlot");
        var barriers = Empty("Barriers", plot, Vector3.zero);
        if (!barriers.TryGetComponent(out NavMeshModifier modifier)) modifier = barriers.gameObject.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = true;
        var west = LotFence(barriers, "Fence_West", new Vector3(HallWest, 0f, HallSouth), new Vector3(HallWest, 0f, HallNorth - 0.1f));
        var south = LotFence(barriers, "Fence_South", new Vector3(HallWest, 0f, HallSouth), new Vector3(HallEast - 0.1f, 0f, HallSouth));
        var locked = Empty("LockedOnly", plot, Vector3.zero);
        var hazard = Lit("HazardYellow", new Color(1f, 0.78f, 0.1f), 0.3f);
        var dark = Lit("SteelDark", new Color(0.2f, 0.21f, 0.24f), 0.45f, 0.7f);
        var sign = Empty("Sign", locked, new Vector3((HallWest + HallEast) * 0.5f, 0f, HallSouth + 0.25f));
        Box("PostW", sign, new Vector3(-1.7f, 1.3f, 0f), new Vector3(0.16f, 2.6f, 0.16f), dark, 0.02f);
        Box("PostE", sign, new Vector3(1.7f, 1.3f, 0f), new Vector3(0.16f, 2.6f, 0.16f), dark, 0.02f);
        var board = Box("Board", sign, new Vector3(0f, 2.55f, 0f), new Vector3(3.8f, 1.1f, 0.12f), hazard, 0.04f);
        board.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
        var text = Text3D("Text", sign, new Vector3(0f, 2.55f, -0.08f), font, worldText, 5f, Navy, "<size=75%>FURNACE</size>\nLOCKED");
        text.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
        text.rectTransform.sizeDelta = new Vector2(3.6f, 1f);
        // Building-site teaser inside the fence.
        if (!locked.TryGetComponent(out NavMeshModifier lockedModifier)) lockedModifier = locked.gameObject.AddComponent<NavMeshModifier>();
        lockedModifier.ignoreFromBuild = true;
        var carKit = Load<Material>(MatDir + "/M_Kenney_CarKit.mat");
        var rng = new System.Random(66);
        for (int i = 0; i < 6; i++)
            Kenney("CarKit", "cone", locked, new Vector3(FurnacePos.x - 2.5f + i, 0f, FurnacePos.z - 3.2f + (i % 2) * 0.4f), 1.6f,
                new Vector3(0f, (float)rng.NextDouble() * 90f, 0f), carKit);
        Kenney("SurvivalKit", "box-large", locked, FurnacePos + new Vector3(2.4f, 0f, 1.5f), 3f, new Vector3(0f, 20f, 0f),
            Load<Material>(MatDir + "/M_Kenney_SurvivalKit.mat"));
        Kenney("SurvivalKit", "box-large", locked, FurnacePos + new Vector3(3.3f, 0f, 0.4f), 2.6f, new Vector3(0f, -15f, 0f),
            Load<Material>(MatDir + "/M_Kenney_SurvivalKit.mat"));

        // Content (reveal order: floor, furnace, belt, rack, pads).
        Slab("Floor_Hall", content, new Vector3((HallWest + HallEast) * 0.5f, 0.016f, (HallSouth + HallNorth) * 0.5f),
            new Vector3(HallEast - HallWest - 0.4f, 0.03f, HallNorth - HallSouth - 0.4f), Load<Material>(MatDir + "/M_Ground_Concrete.mat"));
        var furnaceGo = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PrefabDir + "/Stations/Furnace.prefab"), content);
        furnaceGo.transform.SetPositionAndRotation(FurnacePos, Quaternion.identity);
        var furnace = furnaceGo.GetComponent<Machine>();
        refs.furnacePad = furnaceGo.transform.Find("InputPad").GetComponent<TransferPad>();

        var rackGo = Station("Storage", content, RackPos, 0f);
        rackGo.name = "IngotRack";
        var rack = rackGo.GetComponent<Storage>();
        SetMany(rack, ("definition", Load<StorageDefinition>(DataDir + "/Factory/Storage_IngotRack.asset")), ("stationId", ""), ("labelTitle", "INGOTS"),
            ("levelSource", null));
        SetArray(rackGo.transform.Find("Pile").GetComponent<ItemPile>(), "accepted", new Object[] { ironIngot, copperIngot });
        var rackWall = Lit("RackSteel", new Color(0.3f, 0.32f, 0.38f), 0.45f, 0.4f);
        var rackTrim = Lit("RackOrange", new Color(0.95f, 0.5f, 0.15f), 0.4f, 0.2f);
        foreach (var r in rackGo.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                if (mats[i].name == "M_BinBlue") mats[i] = rackWall;
                else if (mats[i].name == "M_BinBlueDark") mats[i] = rackTrim;
            }

            r.sharedMaterials = mats;
        }

        refs.rackPad = rackGo.transform.Find("WithdrawPad").GetComponent<TransferPad>();
        // Label beside the rack (east), so it never covers the incoming belt.
        var rackLabel = rackGo.GetComponentInChildren<StationLabel>(true);
        if (rackLabel != null)
        {
            rackLabel.transform.position = RackPos + new Vector3(2.9f, 2.4f, 0.6f);
            rackLabel.transform.localScale = Vector3.one * 0.9f;
        }
        var belt = BuildConveyor(content, "Conveyor_Ingots", furnaceGo.transform.Find("OutputPoint").position.WithY(0f) + Vector3.back * 0.1f,
            RackPos + new Vector3(0f, 0f, 1.55f), rack);
        SetMany(furnace, ("outputTarget", belt));

        PlaceBoost(content, "Tile_BoostFurnace", FurnacePos + new Vector3(4.4f, 0f, -2.8f), furnace);
        PlaceTile(content, "Tile_Smelter", Load<GameObject>(PrefabDir + "/Tiles/Tile_Purchase.prefab"), new Vector3(63.4f, 0f, 26.8f), "smelter", "tile/smelter");

        // The purchase tile sits outside the fence, beside the sorter.
        PlaceTile(plantContent, "Tile_FurnaceHall", Load<GameObject>(PrefabDir + "/Tiles/Tile_Purchase.prefab"), new Vector3(59f, 0f, 30.6f), "furnace_hall",
            "tile/furnace_hall");

        var focus = FindOrCreate(root, "Focus");
        focus.position = FurnacePos + new Vector3(0f, 0f, -4f);
        var area = new Bounds(new Vector3((HallWest + HallEast) * 0.5f, 0f, (HallSouth + HallNorth) * 0.5f),
            new Vector3(HallEast - HallWest - 0.2f, 4f, HallNorth - HallSouth - 0.2f));
        SetMany(expansion, ("definition", Load<ExpansionDefinition>(DataDir + "/World/Expansion_FurnaceHall.asset")), ("contentRoot", content),
            ("lockedOnly", locked.gameObject), ("area", area), ("focusPoint", focus),
            ("revealVfx", Load<GameObject>(PrefabDir + "/VFX/VFX_UpgradeBurst.prefab").GetComponent<ParticleSystem>()), ("focusHold", 3f),
            ("contentStagger", 0.12f), ("barrierSinkDepth", 2.2f));
        SetArray(expansion, "barriers", new Object[] { west, south });
        SetStructArray(expansion, "materialSwaps", Array.Empty<Dictionary<string, object>>());
        foreach (Transform c in content) c.gameObject.SetActive(false);
        return refs;
    }

    struct HeavyRefs
    {
        public Transform content;
    }

    /// <summary>
    /// Gate 3 in the Old Yard's north wall and the Heavy Scrap Yard behind it: walls, gate, heavy vehicles on spawn points,
    /// a magnet crane and container stacks for the industrial read. The teaser vehicles behind the wall become real scrap.
    /// </summary>
    static HeavyRefs BuildHeavyYard(Transform gameplay, Transform environment, TMP_FontAsset font, Material worldText)
    {
        var refs = new HeavyRefs();
        var fences = environment.Find("Fences");
        foreach (var n in new[] { "Wall_0_38_40_38", "Wall_0_38_10_38", "Wall_14_38_40_38", "Wall_0_38_0_74", "Wall_0_74_40_74", "Wall_40_74_40_42" })
        {
            var old = fences.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        CorrugatedWall(fences, "Wall_0_38_10_38", new Vector3(0f, 0f, WallZ), new Vector3(GateX - GateHalf - 0.2f, 0f, WallZ));
        CorrugatedWall(fences, "Wall_14_38_40_38", new Vector3(GateX + GateHalf + 0.2f, 0f, WallZ), new Vector3(40f, 0f, WallZ));
        CorrugatedWall(fences, "Wall_0_38_0_74", new Vector3(0f, 0f, WallZ), new Vector3(0f, 0f, 74f));
        CorrugatedWall(fences, "Wall_0_74_40_74", new Vector3(0f, 0f, 74f), new Vector3(40f, 0f, 74f));
        CorrugatedWall(fences, "Wall_40_74_40_42", new Vector3(40f, 0f, 74f), new Vector3(40f, 0f, 42f));

        // Gate 3 (same look as Gate 2, turned to face south).
        var oldGate = environment.Find("Gate_A1_A3");
        if (oldGate != null) Object.DestroyImmediate(oldGate.gameObject);
        var gate = Empty("Gate_A1_A3", environment, new Vector3(GateX, 0f, WallZ));
        var hazard = Lit("HazardYellow", new Color(1f, 0.78f, 0.1f), 0.3f);
        var red = Lit("HazardRed", new Color(0.88f, 0.2f, 0.15f), 0.3f);
        var dark = Lit("SteelDark", new Color(0.2f, 0.21f, 0.24f), 0.45f, 0.7f);
        Box("PostW", gate, new Vector3(-GateHalf, 1.2f, 0f), new Vector3(0.45f, 2.4f, 0.45f), hazard, 0.05f);
        Box("PostE", gate, new Vector3(GateHalf, 1.2f, 0f), new Vector3(0.45f, 2.4f, 0.45f), hazard, 0.05f);
        var bars = new List<Object>();
        for (int i = 0; i < 5; i++)
            bars.Add(Box("Bar_" + i, gate, new Vector3(-2f + i, 1.05f, 0f), new Vector3(1f, 0.26f, 0.2f), i % 2 == 0 ? red : hazard, 0.02f).transform);
        var collider = new GameObject("GateCollider");
        collider.transform.SetParent(gate, false);
        var gateBox = collider.AddComponent<BoxCollider>();
        gateBox.center = new Vector3(0f, 1f, 0f);
        gateBox.size = new Vector3(5f, 2f, 0.6f);
        var obstacle = collider.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = gateBox.center;
        obstacle.size = gateBox.size + new Vector3(0f, 0f, 0.1f);
        obstacle.carving = true;
        bars.Add(collider.transform);
        var gateModifier = gate.gameObject.AddComponent<NavMeshModifier>();
        gateModifier.ignoreFromBuild = true;

        var root = FindOrCreate(gameplay, "HeavyYard");
        root.position = Vector3.zero;
        if (!root.TryGetComponent(out Expansion expansion)) expansion = root.gameObject.AddComponent<Expansion>();
        var locked = ResetChild(root, "LockedOnly");
        locked.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        var board = Box("Sign", locked, new Vector3(GateX, 3.2f, WallZ - 0.4f), new Vector3(3.8f, 1.3f, 0.14f), dark, 0.05f);
        board.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
        var signText = Text3D("SignText", locked, new Vector3(GateX, 3.2f, WallZ - 0.52f), font, worldText, 5f, new Color(1f, 0.8f, 0.15f),
            "<size=70%>HEAVY SCRAP YARD</size>\nLOCKED");
        signText.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
        signText.rectTransform.sizeDelta = new Vector2(3.6f, 1.2f);

        // The backdrop teaser (vehicles, containers, tank, chimney, big sign) gives way to the real yard.
        var backdrop = environment.Find("Backdrop");
        foreach (Transform t in backdrop.Cast<Transform>().ToArray())
            if (t.position.x > -0.5f && t.position.x < 40.5f && t.position.z > 38.5f && t.position.z < 74.5f)
                Object.DestroyImmediate(t.gameObject);

        var content = ResetChild(root, "Content");
        refs.content = content;
        var city = Load<Material>(MatDir + "/M_Kenney_CityKitIndustrial.mat");
        var factory = Load<Material>(MatDir + "/M_Kenney_FactoryKit.mat");
        var decor = Empty("Decor", content, Vector3.zero);
        Kenney("CityKitIndustrial", "shipping-container-a", decor, new Vector3(3.6f, 0f, 70.5f), 3f, new Vector3(0f, 90f, 0f), city);
        Kenney("CityKitIndustrial", "shipping-container-b", decor, new Vector3(3.6f, 2.5f, 70.8f), 3f, new Vector3(0f, 92f, 0f), city);
        Kenney("CityKitIndustrial", "shipping-container-c", decor, new Vector3(9.5f, 0f, 71.6f), 3f, Vector3.zero, city);
        Kenney("CityKitIndustrial", "detail-tank-large", decor, new Vector3(36.5f, 0f, 70.5f), 3f, Vector3.zero, city);
        Kenney("CityKitIndustrial", "chimney-large", decor, new Vector3(30f, 0f, 72f), 3f, Vector3.zero, city);
        var crane = Kenney("FactoryKit", "crane-magnet", decor, new Vector3(20f, 0f, 70.8f), 1f, new Vector3(0f, 180f, 0f), factory);
        var cb = RendererBounds(crane).size;
        crane.transform.localScale = Vector3.one * (6.5f / Mathf.Max(cb.y, 0.01f));
        var wreck = Load<Material>(MatDir + "/M_Kenney_CarWreck.mat");
        var survival = Load<Material>(MatDir + "/M_Kenney_SurvivalKit.mat");
        var rng = new System.Random(63);
        Heap(decor, "Heap_M6_West", new Vector3(1.4f, 0f, 56f), new Vector2(0.9f, 8f), 12, rng, wreck, survival);
        Heap(decor, "Heap_M6_East", new Vector3(38.6f, 0f, 58f), new Vector2(0.9f, 9f), 12, rng, wreck, survival);
        foreach (var r in decor.GetComponentsInChildren<Renderer>()) GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
        var containerBlock = Empty("ContainerBlock", decor, new Vector3(6.5f, 0f, 70.8f));
        containerBlock.gameObject.AddComponent<BoxCollider>().size = new Vector3(9f, 4f, 3.2f);
        var tankBlock = Empty("TankBlock", decor, new Vector3(36.5f, 0f, 70.5f));
        tankBlock.gameObject.AddComponent<BoxCollider>().size = new Vector3(4f, 4f, 4f);

        int characters = LayerMask.GetMask("Characters");
        var tractor = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_Tractor.asset");
        var truck = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_Truck.asset");
        var garbage = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_GarbageTruck.asset");
        SpawnPoint("SP_Tractor_01", content, new Vector3(7f, 0f, 45.5f), 30f, false, characters, 3f, tractor);
        SpawnPoint("SP_Truck_01", content, new Vector3(17.5f, 0f, 46.5f), -20f, false, characters, 3.4f, truck);
        SpawnPoint("SP_Tractor_02", content, new Vector3(28f, 0f, 45f), 160f, false, characters, 3f, tractor);
        SpawnPoint("SP_Truck_02", content, new Vector3(34.5f, 0f, 53.5f), 95f, false, characters, 3.4f, truck);
        SpawnPoint("SP_Garbage_01", content, new Vector3(11f, 0f, 56.5f), 200f, false, characters, 4f, garbage);
        SpawnPoint("SP_Truck_03", content, new Vector3(23.5f, 0f, 58f), 10f, false, characters, 3.4f, truck);
        SpawnPoint("SP_Tractor_03", content, new Vector3(6.5f, 0f, 65.5f), -40f, false, characters, 3f, tractor);
        SpawnPoint("SP_Garbage_02", content, new Vector3(28f, 0f, 65f), 0f, false, characters, 4f, garbage);
        foreach (Transform c in content)
        {
            if (c.TryGetComponent(out ScrapSpawnPoint sp)) SetMany(sp, ("animateFirstSpawn", true));
            c.gameObject.SetActive(false);
        }

        PlaceTile(gameplay.Find("Tiles"), "Tile_HeavyYard", Load<GameObject>(PrefabDir + "/Tiles/Tile_Purchase.prefab"), new Vector3(GateX, 0f, 35.2f), "heavy_yard",
            "tile/heavy_yard");

        var focus = FindOrCreate(root, "Focus");
        focus.position = new Vector3(17f, 0f, 52f);
        var ground = environment.Find("Area3_Locked").GetComponent<Renderer>();
        SetMany(expansion, ("definition", Load<ExpansionDefinition>(DataDir + "/World/Expansion_HeavyYard.asset")), ("contentRoot", content),
            ("lockedOnly", locked.gameObject), ("area", new Bounds(new Vector3(20f, 0f, 56f), new Vector3(39.6f, 4f, 35.6f))), ("focusPoint", focus),
            ("revealVfx", Load<GameObject>(PrefabDir + "/VFX/VFX_UpgradeBurst.prefab").GetComponent<ParticleSystem>()), ("focusHold", 3.4f),
            ("contentStagger", 0.1f), ("barrierSinkDepth", 2.6f));
        SetArray(expansion, "barriers", bars.ToArray());
        SetStructArray(expansion, "materialSwaps", new[] { Lv(("renderer", ground), ("material", Load<Material>(MatDir + "/M_Ground_Dirt.mat"))) });
        return refs;
    }

    static PorterRoute Route(Transform sites, string name, string siteId, Vector3 idle, Object[] pads, TransferPad dropoff)
    {
        var t = FindOrCreate(sites, name);
        t.position = idle;
        if (!t.TryGetComponent(out PorterRoute route)) route = t.gameObject.AddComponent<PorterRoute>();
        var idlePoint = FindOrCreate(t, "Idle");
        idlePoint.position = idle;
        SetMany(route, ("siteId", siteId), ("idlePoint", idlePoint), ("pickupCenter", t), ("pickupRadius", 1f), ("dropoff", dropoff));
        SetArray(route, "pickupPads", pads);
        return route;
    }

    // =====================================================================================
    // Helpers (copied from M5_Build / M4_Build; builders compile one file at a time)
    // =====================================================================================

    static GameObject BuildPadWorker(string name, string model, AnimatorController controller, Material cap, Material steelDark)
    {
        var root = new GameObject(name);
        root.layer = LayerMask.NameToLayer("Characters");
        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.4f;
        agent.height = 1.7f;
        agent.speed = 3.6f;
        agent.acceleration = 20f;
        agent.angularSpeed = 720f;
        agent.stoppingDistance = 0.2f;
        agent.autoBraking = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        var m = Kenney("MiniCharacters", model, body, Vector3.zero, 2.4f, Vector3.zero, null);
        m.name = "Model";
        if (!m.TryGetComponent(out Animator animator)) animator = m.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        var clips = AssetDatabase.LoadAllAssetsAtPath($"{K}/MiniCharacters/{model}.fbx").OfType<AnimationClip>().ToArray();
        clips.First(c => c.name == "idle").SampleAnimation(m, 0f);
        PlaceHardHat(root.transform, m.transform, cap);
        RebuildHat(root.transform, cap, false);

        Box("CarryRack", body, new Vector3(0f, 0.95f, -0.3f), new Vector3(0.5f, 0.55f, 0.08f), steelDark, 0.02f);
        var stackAnchor = Empty("StackAnchor", root.transform, new Vector3(0f, 0.85f, -0.45f));
        var stack = root.AddComponent<CarryStack>();
        SetMany(stack, ("stackRoot", stackAnchor), ("capacity", 8));
        var worker = root.AddComponent<Worker>();
        SetMany(worker, ("stack", stack), ("collector", null), ("animator", animator));
        root.AddComponent<PorterBrain>();
        return root;
    }


    static TransferPad Pad(Transform parent, string name, Vector3 position, Vector2 size, TransferMode mode, MonoBehaviour target, Color color,
        GameObject visualPrefab, float arrowYaw, string sfxName)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab, go.transform);
        string key = ColorUtility.ToHtmlStringRGB(color);
        var frame = Load<Material>($"{MatDir}/M_PadFrame_{key}.mat", false) ?? Unlit("PadFrame_" + key, color);
        var fill = Load<Material>($"{MatDir}/M_PadFill_{key}.mat", false) ?? TransparentUnlit("PadFill_" + key, new Color(color.r, color.g, color.b, 0.28f));
        const float bar = 0.1f;
        foreach (var r in visual.GetComponentsInChildren<Renderer>())
        {
            var t = r.transform;
            switch (r.name)
            {
                case "Fill":
                    r.sharedMaterial = fill;
                    t.localScale = new Vector3(size.x, size.y, 1f);
                    break;
                case "FrameN":
                case "FrameS":
                    r.sharedMaterial = frame;
                    t.localScale = new Vector3(size.x, 0.04f, bar);
                    t.localPosition = new Vector3(0f, 0.04f, (r.name == "FrameN" ? 1f : -1f) * (size.y - bar) * 0.5f);
                    break;
                case "FrameE":
                case "FrameW":
                    r.sharedMaterial = frame;
                    t.localScale = new Vector3(bar, 0.04f, size.y);
                    t.localPosition = new Vector3((r.name == "FrameE" ? 1f : -1f) * (size.x - bar) * 0.5f, 0.04f, 0f);
                    break;
                default:
                    r.sharedMaterial = frame;
                    break;
            }
        }

        var arrow = visual.transform.Find("Arrow");
        if (arrow != null)
        {
            arrow.localRotation = Quaternion.Euler(0f, arrowYaw, 0f);
            arrow.localScale = Vector3.one * 1.6f;
        }

        var pad = go.AddComponent<TransferPad>();
        SetMany(pad, ("mode", (int)mode), ("target", target), ("size", size), ("visual", visual.transform),
            ("transferSfx", Load<SfxDefinition>($"{DataDir}/Audio/{sfxName}.asset")));
        return pad;
    }


    static Material Unlit(string name, Color color)
    {
        var m = LoadOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
        m.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(m);
        return m;
    }


    static RectTransform WorldCanvas(string name, Transform parent, Vector3 localPos, Vector2 size, bool flat)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.localPosition = localPos;
        rt.localRotation = flat ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one * 0.01f;
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = flat ? 0 : 10;
        return rt;
    }


    static void AutoSize(TMP_Text t, float min, float max)
    {
        t.enableAutoSizing = true;
        t.fontSizeMin = min;
        t.fontSizeMax = max;
    }


    static GameObject Station(string prefab, Transform parent, Vector3 position, float yaw)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>($"{PrefabDir}/Stations/{prefab}.prefab"), parent);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        return go;
    }


    static void PlaceTile(Transform parent, string name, GameObject prefab, Vector3 position, string upgradeId, string anchorId)
    {
        var existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.SetPositionAndRotation(position, Quaternion.identity);
        if (upgradeId != null) SetMany(go.GetComponent<PurchaseTile>(), ("upgradeId", upgradeId));
        var anchor = go.AddComponent<GuideAnchor>();
        SetMany(anchor, ("id", anchorId));
    }


    static void PlaceBoost(Transform parent, string name, Vector3 position, Machine machine)
    {
        var existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PrefabDir + "/Tiles/Tile_Boost.prefab"), parent);
        go.name = name;
        go.transform.SetPositionAndRotation(position, Quaternion.identity);
        SetMany(go.GetComponent<OverdriveTile>(), ("machine", machine));
        var anchor = go.AddComponent<GuideAnchor>();
        SetMany(anchor, ("station", machine), ("role", "boost"));
    }


    static Conveyor BuildConveyor(Transform parent, string name, Vector3 from, Vector3 to, MonoBehaviour destination)
    {
        var existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        root.position = from;
        Vector3 delta = to - from;
        float length = delta.magnitude;
        root.rotation = Quaternion.LookRotation(delta.normalized);

        var factory = Load<Material>(MatDir + "/M_Kenney_FactoryKit.mat");
        int tiles = Mathf.Max(1, Mathf.CeilToInt(length));
        float tileLength = length / tiles;
        for (int i = 0; i < tiles; i++)
        {
            var tile = Kenney("FactoryKit", "conveyor-stripe", root, new Vector3(0f, 0f, tileLength * (i + 0.5f)), 1f, Vector3.zero, factory);
            tile.transform.localScale = new Vector3(1.0f, 1f, tileLength);
        }

        float top = RendererBounds(root.gameObject).max.y;
        var start = Empty("Start", root, new Vector3(0f, 0f, 0.15f));
        var end = Empty("End", root, new Vector3(0f, 0f, length - 0.15f));
        var conveyor = root.gameObject.AddComponent<Conveyor>();
        SetMany(conveyor, ("speed", 1.8f), ("spacing", 0.55f), ("rideHeight", top + 0.17f), ("destination", destination));
        SetArray(conveyor, "points", new Object[] { start, end });
        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.5f, length * 0.5f);
        box.size = new Vector3(1.0f, 1f, length);
        return conveyor;
    }


    static void CorrugatedWall(Transform parent, string name, Vector3 from, Vector3 to)
    {
        var segment = new GameObject(name).transform;
        segment.SetParent(parent, false);
        Vector3 delta = to - from;
        float length = delta.magnitude;
        segment.position = from;
        segment.rotation = Quaternion.LookRotation(delta.normalized) * Quaternion.Euler(0f, -90f, 0f);
        var tints = new[]
        {
            Load<Material>(MatDir + "/M_Kenney_SurvivalKit.mat"), Load<Material>(MatDir + "/M_Kenney_SurvivalRust.mat"),
            Load<Material>(MatDir + "/M_Kenney_SurvivalGrey.mat"),
        };
        var probe = Kenney("SurvivalKit", "metal-panel-screws", segment, Vector3.zero, 1f, Vector3.zero, null);
        var pb = RendererBounds(probe);
        Object.DestroyImmediate(probe);
        float native = Mathf.Max(pb.size.x, pb.size.z);
        float scale = 2.1f / native;
        float heightScale = 2.3f / Mathf.Max(pb.size.y, 0.01f);
        int count = Mathf.CeilToInt(length / 2f);
        var rng = new System.Random((int)(from.x * 13 + from.z * 7));
        for (int i = 0; i < count; i++)
        {
            float along = (i + 0.5f) * length / count;
            var panel = Kenney("SurvivalKit", "metal-panel-screws", segment, Vector3.zero, 1f, Vector3.zero, tints[rng.Next(tints.Length)]);
            panel.transform.localScale = new Vector3(scale, heightScale * (0.92f + (float)rng.NextDouble() * 0.12f), scale);
            panel.transform.localRotation = Quaternion.Euler(0f, pb.size.x >= pb.size.z ? 0f : 90f, (float)(rng.NextDouble() - 0.5) * 3f);
            panel.transform.localPosition = new Vector3(along, 0f, 0f);
            var b = RendererBounds(panel);
            panel.transform.position += new Vector3(0f, -b.min.y, 0f) + (segment.position + segment.right * along - new Vector3(b.center.x, 0f, b.center.z));
        }

        var box = segment.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(length * 0.5f, 1.1f, 0f);
        box.size = new Vector3(length, 2.2f, 0.5f);
    }


    static GameObject Slab(string name, Transform parent, Vector3 center, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.transform.localScale = size;
        go.layer = LayerMask.NameToLayer("Ground");
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }


    static GameObject ScrapRoot(string name, ScrapDefinition def, out Transform visual)
    {
        var root = new GameObject(name);
        root.layer = LayerMask.NameToLayer("Scrap");
        visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        var scrap = root.AddComponent<ScrapObject>();
        var box = root.AddComponent<BoxCollider>();
        SetMany(scrap, ("definition", def), ("visualRoot", visual), ("hitCollider", box));
        return root;
    }


    static void FitCollider(GameObject root, GameObject content, float shrink)
    {
        var b = RendererBounds(content);
        var box = root.GetComponent<BoxCollider>();
        box.center = root.transform.InverseTransformPoint(b.center);
        box.size = new Vector3(b.size.x * shrink, b.size.y, b.size.z * shrink);
    }


    static void Part(GameObject go, int order)
    {
        if (!go.TryGetComponent(out ScrapPart part)) part = go.AddComponent<ScrapPart>();
        SetMany(part, ("detachOrder", order));
    }


    static void HealthBar(Transform parent, float height, float width, Material bg, Material fill)
    {
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
        SetMany(hb, ("fill", fillRoot), ("fillRenderer", fillGo.GetComponent<Renderer>()));
        bar.SetActive(false);
        var scrap = parent.GetComponent<ScrapObject>();
        if (scrap != null) SetMany(scrap, ("healthBar", hb));
    }


    static void SpawnPoint(string name, Transform parent, Vector3 position, float yaw, bool randomYaw, int blocking, float clearRadius,
        params ScrapDefinition[] definitions)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.position = position;
        t.rotation = Quaternion.Euler(0f, yaw, 0f);
        var sp = t.gameObject.AddComponent<ScrapSpawnPoint>();
        SetMany(sp, ("randomYaw", randomYaw), ("clearRadius", clearRadius));
        var so = new SerializedObject(sp);
        so.FindProperty("blockingLayers").intValue = blocking;
        var candidates = so.FindProperty("candidates");
        candidates.arraySize = definitions.Length;
        for (int i = 0; i < definitions.Length; i++) candidates.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }


    static void Heap(Transform parent, string name, Vector3 center, Vector2 extents, int count, System.Random rng, Material wreck, Material survival)
    {
        var heap = Empty(name, parent, center);
        string[] cars = { "sedan", "van", "suv", "truck", "hatchback-sports", "delivery" };
        string[] debris = { "wheel-default", "debris-plate-a", "debris-door", "debris-bumper", "debris-tire", "debris-plate-b", "debris-spoiler-b" };
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        int shells = Mathf.Max(1, count / 6);
        for (int i = 0; i < shells; i++)
        {
            var body = Kenney("CarKit", cars[rng.Next(cars.Length)], heap, new Vector3(R(-extents.x, extents.x) * 0.7f, 0f, R(-extents.y, extents.y) * 0.4f),
                R(1.4f, 1.7f), new Vector3(R(-6f, 6f), R(0f, 360f), R(-12f, 12f)), wreck);
            foreach (var w in body.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("wheel")).ToArray())
                if (rng.NextDouble() < 0.5) w.gameObject.SetActive(false);
        }

        for (int i = 0; i < count; i++)
            Kenney("CarKit", debris[rng.Next(debris.Length)], heap, new Vector3(R(-extents.x, extents.x), R(0f, 0.9f), R(-extents.y, extents.y)), R(1.6f, 2.6f),
                new Vector3(R(-40f, 40f), R(0f, 360f), R(-40f, 40f)), wreck);

        for (int i = 0; i < count / 4; i++)
            Kenney("SurvivalKit", rng.NextDouble() < 0.5 ? "barrel" : "box-large", heap,
                new Vector3(R(-extents.x, extents.x), 0f, R(-extents.y, extents.y)), R(2.6f, 3.4f), new Vector3(0f, R(0f, 360f), rng.NextDouble() < 0.3 ? 90f : 0f), survival);

        foreach (var r in heap.GetComponentsInChildren<Renderer>()) GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
        var box = heap.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 1f, 0f);
        box.size = new Vector3(extents.x * 2f, 2f, extents.y * 2f);
    }


    static void PlaceHardHat(Transform root, Transform model, Material hat)
    {
        var headBone = FindDeep(model, "head");
        var headMesh = FindDeep(model, "head-mesh").GetComponent<SkinnedMeshRenderer>();
        var baked = new Mesh();
        headMesh.BakeMesh(baked, true);
        var verts = baked.vertices.Select(v => headMesh.transform.TransformPoint(v)).ToArray();
        Object.DestroyImmediate(baked);
        var hb = new Bounds(verts[0], Vector3.zero);
        foreach (var v in verts) hb.Encapsulate(v);
        var size = hb.size;
        var hatRoot = new GameObject("HardHat").transform;
        hatRoot.SetParent(root, false);
        hatRoot.position = new Vector3(hb.center.x, hb.max.y - size.y * 0.12f, hb.center.z);
        Box("Dome", hatRoot, new Vector3(0f, 0.09f, 0f), new Vector3(size.x * 1.06f, 0.18f, size.z * 1.06f), hat, 0.05f, true);
        Box("Brim", hatRoot, new Vector3(0f, 0.01f, size.z * 0.12f), new Vector3(size.x * 1.16f, 0.04f, size.z * 1.28f), hat, 0.015f, true);
        hatRoot.SetParent(headBone, true);
    }


    /// <summary>
    /// Replaces the box-shaped hard hat with a rounded dome, brim and ridge sized to the existing hat footprint, so it
    /// reads as a helmet from the top-down camera instead of a cube.
    /// </summary>
    static void RebuildHat(Transform root, Material mat, bool ridge)
    {
        var hat = FindDeep(root, "HardHat");
        var headMesh = FindDeep(root, "head-mesh");
        if (hat == null || headMesh == null || !headMesh.TryGetComponent(out SkinnedMeshRenderer skin))
        {
            Log.AppendLine("!! no HardHat/head-mesh under " + root.name);
            return;
        }

        // Size from the head itself (not from the previous hat), so re-running always gives the same helmet.
        var baked = new Mesh();
        skin.BakeMesh(baked, true);
        var verts = baked.vertices;
        var hb = new Bounds(skin.transform.TransformPoint(verts[0]), Vector3.zero);
        foreach (var v in verts) hb.Encapsulate(skin.transform.TransformPoint(v));
        Object.DestroyImmediate(baked);
        float w = hb.size.x, d = hb.size.z;

        foreach (Transform c in hat.Cast<Transform>().ToArray()) Object.DestroyImmediate(c.gameObject);

        // Dome: a squashed sphere whose lower half sinks into the head; sized close to the head so the character
        // still reads from the top-down camera.
        var shell = ShapeGenerator.GenerateIcosahedron(PivotLocation.Center, 0.5f, 2, true, false);
        FinishPb(shell, "Shell", hat, Vector3.zero, mat);
        shell.transform.position = hat.position + hat.up * 0.04f;
        shell.transform.rotation = hat.rotation;
        SetWorldScale(shell.transform, new Vector3(w * 1.07f, 0.44f * w, d * 1.07f));
        var brim = ShapeGenerator.GenerateCylinder(PivotLocation.Center, 24, 0.5f, 0.035f, 0, 1);
        FinishPb(brim, "Brim", hat, Vector3.zero, mat);
        brim.transform.position = hat.position + hat.forward * (d * 0.07f);
        brim.transform.rotation = hat.rotation;
        SetWorldScale(brim.transform, new Vector3(w * 1.15f, 1f, d * 1.27f));
        if (ridge)
        {
            var r = Box("Ridge", hat, Vector3.zero, new Vector3(0.08f, 0.07f, d * 0.8f), mat, 0.02f, true);
            r.transform.position = hat.position + hat.up * (0.04f + 0.21f * w);
            r.transform.rotation = hat.rotation;
        }
    }


    static void SetWorldScale(Transform t, Vector3 world)
    {
        var parent = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(world.x / parent.x, world.y / parent.y, world.z / parent.z);
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


    static void StationAnchor(Transform t, MonoBehaviour station, string role)
    {
        if (t == null || station == null)
        {
            Log.AppendLine($"!! anchor target missing ({role})");
            return;
        }

        if (!t.TryGetComponent(out GuideAnchor anchor)) anchor = t.gameObject.AddComponent<GuideAnchor>();
        SetMany(anchor, ("station", station), ("role", role));
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


    static GameObject PrefabRoot(string prefab, params string[] hide)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>($"{PrefabDir}/{prefab}.prefab"));
        foreach (var h in hide)
        {
            var t = go.transform.Find(h);
            if (t != null) t.gameObject.SetActive(false);
            var deep = FindDeep(go.transform, h);
            if (deep != null) deep.gameObject.SetActive(false);
        }

        return go;
    }


    /// <summary>Renders <paramref name="source"/> into a transparent 256px sprite in an isolated preview scene.</summary>
    static Sprite RenderIcon(string name, GameObject source, Vector3 viewEuler, string poseClip = null)
    {
        const int size = 256;
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject instance;
        bool sourceIsSceneObject = source.scene.IsValid() && !EditorUtility.IsPersistent(source);
        instance = sourceIsSceneObject ? source : Object.Instantiate(source);
        if (!sourceIsSceneObject) instance.name = source.name;
        SceneManager.MoveGameObjectToScene(instance, preview);
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;
        instance.SetActive(true);
        foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
        if (poseClip != null)
        {
            var anim = instance.GetComponentInChildren<Animator>();
            var model = anim != null ? anim.gameObject : instance;
            string fbx = anim != null && anim.avatar != null ? AssetDatabase.GetAssetPath(anim.avatar) : null;
            var clip = fbx != null ? AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().FirstOrDefault(c => c.name == poseClip) : null;
            if (clip == null)
            {
                var anyFbx = AssetDatabase.GetAssetPath(source);
                clip = AssetDatabase.LoadAllAssetsAtPath(anyFbx).OfType<AnimationClip>().FirstOrDefault(c => c.name == poseClip);
            }

            if (clip != null) clip.SampleAnimation(model, clip.length * 0.3f);
        }

        var renderers = instance.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer)).ToArray();
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

        var camGo = new GameObject("IconCamera");
        SceneManager.MoveGameObjectToScene(camGo, preview);
        var cam = camGo.AddComponent<Camera>();
        cam.scene = preview;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.orthographic = true;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 100f;
        var rot = Quaternion.Euler(viewEuler);
        camGo.transform.rotation = rot;
        camGo.transform.position = bounds.center - rot * Vector3.forward * (bounds.extents.magnitude * 3f);
        float radius = bounds.extents.magnitude;
        cam.orthographicSize = radius * 0.92f;

        var lightGo = new GameObject("IconLight");
        SceneManager.MoveGameObjectToScene(lightGo, preview);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.3f;
        lightGo.transform.rotation = Quaternion.Euler(45f, viewEuler.y - 40f, 0f);
        var fillGo = new GameObject("IconFill");
        SceneManager.MoveGameObjectToScene(fillGo, preview);
        var fillLight = fillGo.AddComponent<Light>();
        fillLight.type = LightType.Directional;
        fillLight.intensity = 0.6f;
        fillGo.transform.rotation = Quaternion.Euler(20f, viewEuler.y + 140f, 0f);

        var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.ReadPixels(new UnityEngine.Rect(0, 0, size, size), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        EditorSceneManager.ClosePreviewScene(preview);

        string path = $"{IconDir}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        return Load<Sprite>(path);
    }


    static Mesh CombineSubmeshes(string name, ProBuilderMesh[] first, ProBuilderMesh[] second)
    {
        Mesh Merge(ProBuilderMesh[] parts)
        {
            var m = new Mesh();
            m.CombineMeshes(parts.Select(p => new CombineInstance { mesh = p.GetComponent<MeshFilter>().sharedMesh, transform = p.transform.localToWorldMatrix }).ToArray(),
                true, true);
            return m;
        }

        var a = Merge(first);
        var b = Merge(second);
        var mesh = new Mesh { name = name };
        mesh.CombineMeshes(new[] { new CombineInstance { mesh = a, transform = Matrix4x4.identity }, new CombineInstance { mesh = b, transform = Matrix4x4.identity } },
            false, true);
        mesh.RecalculateBounds();
        string path = $"{MeshDir}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // Copy buffers explicitly: CopySerialized leaves the old GPU data on screen until a reimport.
        existing.Clear();
        existing.indexFormat = mesh.indexFormat;
        existing.SetVertices(mesh.vertices);
        existing.SetNormals(mesh.normals);
        existing.SetUVs(0, mesh.uv);
        existing.subMeshCount = mesh.subMeshCount;
        for (int i = 0; i < mesh.subMeshCount; i++) existing.SetTriangles(mesh.GetTriangles(i), i);
        existing.RecalculateBounds();
        existing.RecalculateTangents();
        existing.name = name;
        EditorUtility.SetDirty(existing);
        AssetDatabase.SaveAssetIfDirty(existing);
        Object.DestroyImmediate(mesh);
        return existing;
    }


    static GameObject BuildItem(string name, Mesh mesh, Material[] materials, Material[] materialVariants, float halfHeight)
    {
        var root = new GameObject(name);
        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        var mf = visual.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = visual.AddComponent<MeshRenderer>();
        mr.sharedMaterials = materials;
        var item = root.AddComponent<WorldItem>();
        SetMany(item, ("halfHeight", halfHeight), ("visual", mr), ("meshFilter", mf));
        SetArray(item, "meshVariants", Array.Empty<Object>());
        SetArray(item, "materialVariants", materialVariants.Cast<Object>().ToArray());
        return root;
    }


    static GameObject Kenney(string pack, string model, Transform parent, Vector3 localPos, float scale, Vector3 euler, Material overrideMaterial)
    {
        var asset = Load<GameObject>($"{K}/{pack}/{model}.fbx");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = Vector3.one * scale;
        if (overrideMaterial != null)
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = Enumerable.Repeat(overrideMaterial, r.sharedMaterials.Length).ToArray();
        return go;
    }


    static ProBuilderMesh Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, float bevel, bool worldSpace = false)
    {
        var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
        if (bevel > 0f)
        {
            try
            {
                Bevel.BevelEdges(pb, pb.faces.SelectMany(f => f.edges).ToList(), bevel);
            }
            catch (Exception e)
            {
                Log.AppendLine($"bevel failed on {name}: {e.Message}");
            }
        }

        FinishPb(pb, name, parent, localPos, mat, worldSpace);
        return pb;
    }


    static ProBuilderMesh Cylinder(string name, Transform parent, Vector3 localPos, float radius, float height, int sides, Material mat)
    {
        var pb = ShapeGenerator.GenerateCylinder(PivotLocation.Center, sides, radius, height, 0, 1);
        FinishPb(pb, name, parent, localPos, mat);
        return pb;
    }


    static void FinishPb(ProBuilderMesh pb, string name, Transform parent, Vector3 localPos, Material mat, bool worldSpace = false)
    {
        pb.gameObject.name = name;
        pb.transform.SetParent(parent, worldSpace);
        if (worldSpace)
        {
            pb.transform.position = parent.position + parent.rotation * localPos;
            pb.transform.rotation = parent.rotation;
        }
        else pb.transform.localPosition = localPos;

        pb.SetMaterial(pb.faces, mat);
        pb.ToMesh();
        pb.Refresh();
        var col = pb.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
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


    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            var r = FindDeep(c, name);
            if (r != null) return r;
        }

        return null;
    }


    static Bounds RendererBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        var b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }


    static Material KenneyMaterial(string pack)
    {
        string path = $"{MatDir}/M_Kenney_{pack}.mat";
        return Load<Material>(path);
    }


    static Material Lit(string name, Color color, float smoothness, float metallic = 0f)
    {
        var m = LoadOrCreateMaterial(name, "Universal Render Pipeline/Lit");
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", metallic);
        EditorUtility.SetDirty(m);
        return m;
    }


    static Material Emissive(string name, Color color, float intensity)
    {
        var m = LoadOrCreateMaterial(name, "Universal Render Pipeline/Lit");
        m.SetColor("_BaseColor", color);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetColor("_EmissionColor", color * intensity);
        EditorUtility.SetDirty(m);
        return m;
    }


    static Material TransparentUnlit(string name, Color color)
    {
        var m = LoadOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        return m;
    }


    static Material ParticleMaterial(string name, Texture2D texture, Color color, bool additive)
    {
        var m = LoadOrCreateMaterial("FX_" + name, "Universal Render Pipeline/Particles/Unlit");
        m.SetTexture("_BaseMap", texture);
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


    static Material LoadOrCreateMaterial(string name, string shader)
    {
        string path = $"{MatDir}/M_{name}.mat";
        var m = Load<Material>(path, false);
        if (m != null) return m;
        m = new Material(Shader.Find(shader)) { name = "M_" + name };
        AssetDatabase.CreateAsset(m, path);
        return m;
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
        a = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(a, path);
        return a;
    }


    static GameObject SavePrefab(GameObject go, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
        Object.DestroyImmediate(go);
        if (!ok) Log.AppendLine("FAILED prefab " + path);
        return prefab;
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


    static void Carve(GameObject root)
    {
        var box = root.GetComponent<BoxCollider>();
        var obstacle = root.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = box.center;
        obstacle.size = box.size;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
    }


    static Transform LotFence(Transform parent, string name, Vector3 from, Vector3 to)
    {
        var segment = new GameObject(name).transform;
        segment.SetParent(parent, false);
        Vector3 delta = to - from;
        float length = delta.magnitude;
        segment.position = from;
        segment.rotation = Quaternion.LookRotation(delta.normalized) * Quaternion.Euler(0f, -90f, 0f);
        var post = Lit("FencePost", new Color(0.3f, 0.32f, 0.36f), 0.4f, 0.6f);
        var yellow = Lit("FenceRail", new Color(1f, 0.78f, 0.1f), 0.35f);
        var red = Lit("HazardRed", new Color(0.88f, 0.2f, 0.15f), 0.3f);
        var mesh = TransparentUnlit("ChainLink", new Color(0.75f, 0.8f, 0.85f, 0.35f));
        int posts = Mathf.Max(1, Mathf.RoundToInt(length / 2.2f));
        for (int i = 0; i <= posts; i++)
            Box("Post", segment, new Vector3(length * i / posts, 0.8f, 0f), new Vector3(0.18f, 1.6f, 0.18f), post, 0.02f);
        // Striped top rail, plain mid rail, see-through mesh so the scrap inside stays visible.
        int stripes = Mathf.Max(2, Mathf.RoundToInt(length / 1.1f));
        for (int i = 0; i < stripes; i++)
            Box("Stripe", segment, new Vector3(length * (i + 0.5f) / stripes, 1.5f, 0f), new Vector3(length / stripes, 0.16f, 0.1f), i % 2 == 0 ? red : yellow, 0.01f);
        Box("RailMid", segment, new Vector3(length * 0.5f, 0.75f, 0f), new Vector3(length, 0.08f, 0.06f), yellow, 0.02f);
        var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panel.name = "Mesh";
        Object.DestroyImmediate(panel.GetComponent<Collider>());
        panel.transform.SetParent(segment, false);
        panel.transform.localPosition = new Vector3(length * 0.5f, 0.75f, 0f);
        panel.transform.localScale = new Vector3(length, 1.4f, 1f);
        var r = panel.GetComponent<Renderer>();
        r.sharedMaterial = mesh;
        r.shadowCastingMode = ShadowCastingMode.Off;

        var box = segment.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(length * 0.5f, 1f, 0f);
        box.size = new Vector3(length, 2f, 0.4f);
        var obstacle = segment.gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = box.center;
        obstacle.size = new Vector3(length, 2f, 0.5f);
        obstacle.carving = true;
        return segment;
    }

}

static class M6VectorExtensions
{
    public static Vector3 WithY(this Vector3 v, float y) => new(v.x, y, v.z);
}

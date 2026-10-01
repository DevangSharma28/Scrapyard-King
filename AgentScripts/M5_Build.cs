using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ScrapYardKing.Customers;
using ScrapYardKing.Economy;
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
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Milestone 5 builder: Recycling Plant (Gate 2) with Sorter → Iron/Copper bins → Metal Market, Metal Hauler and
// Market Runner, Active Overdrive pads, and a quality pass (Kenney CC0 audio on every SFX, dome hard hats).
// Entry points (in order): Assets, Prefabs, Scene (or All). Incremental like M3/M4: edits the existing scene and prefabs.
public static class M5_Build
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
    const string UiSheet1 = "Assets/300Mind/2D Game UI Kit/Sprites/UI-pack_Sprite_1.png";
    const string UiSheet2 = "Assets/300Mind/2D Game UI Kit/Sprites/UI-pack_Sprite_2.png";
    const string ScenePath = P + "/Scenes/Area1_OldScrapYard.unity";

    static readonly Color Navy = new(0.1f, 0.15f, 0.32f);
    static readonly Color Gold = new(1f, 0.82f, 0.25f);
    static readonly Color IronTint = new(0.55f, 0.6f, 0.68f);
    static readonly Color CopperTint = new(0.95f, 0.52f, 0.22f);

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

        ShapeSprite("UI_Ring", 256, 256, (x, y) => Mathf.Clamp01(Circle(x, y, 256, 124f)) * (1f - Mathf.Clamp01(Circle(x, y, 256, 92f))), Vector4.zero);
        ShapeSprite("UI_Bolt", 128, 128, (x, y) => Bolt(x, y, 128), Vector4.zero);

        ImportAudio();
        Audio();

        // Items. Prices: mixed metal 6 → sorted iron 12 / copper 26, so sorting roughly doubles a bale's value.
        var iron = LoadOrCreate<ItemDefinition>(DataDir + "/Items/Item_Iron.asset");
        SetMany(iron, ("id", "iron"), ("displayName", "Iron"), ("color", IronTint), ("stackHeight", 0.28f), ("baseValue", 12));
        var copper = LoadOrCreate<ItemDefinition>(DataDir + "/Items/Item_Copper.asset");
        SetMany(copper, ("id", "copper"), ("displayName", "Copper"), ("color", CopperTint), ("stackHeight", 0.24f), ("baseValue", 26));
        var mixed = Load<ItemDefinition>(DataDir + "/Items/Item_MixedMetal.asset");

        // Sorter (blueprint chain: mixed metal → Sorter → iron / copper / ...).
        var sorter = LoadOrCreate<MachineDefinition>(DataDir + "/Factory/Machine_Sorter.asset");
        SetMany(sorter, ("id", "sorter"), ("displayName", "Sorter"), ("input", mixed), ("output", iron),
            ("cycleSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_SorterCycle.asset")), ("outputSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_MachineOutput.asset")));
        SetStructArray(sorter, "outputMix", new[] { Lv(("item", iron), ("weight", 0.7f)), Lv(("item", copper), ("weight", 0.3f)) });
        SetStructArray(sorter, "levels", new[]
        {
            Lv(("inputCapacity", 12), ("cycleTime", 1.0f), ("inputsPerCycle", 1), ("outputsPerCycle", 1), ("upgradeCost", 0)),
            Lv(("inputCapacity", 16), ("cycleTime", 0.85f), ("inputsPerCycle", 1), ("outputsPerCycle", 1), ("upgradeCost", 600)),
            Lv(("inputCapacity", 20), ("cycleTime", 0.7f), ("inputsPerCycle", 1), ("outputsPerCycle", 1), ("upgradeCost", 1300)),
            Lv(("inputCapacity", 26), ("cycleTime", 0.58f), ("inputsPerCycle", 1), ("outputsPerCycle", 1), ("upgradeCost", 2600)),
            Lv(("inputCapacity", 32), ("cycleTime", 0.48f), ("inputsPerCycle", 1), ("outputsPerCycle", 1), ("upgradeCost", 4800)),
        });

        // Material bins: two bins, one shared upgrade (blueprint: "bigger storage" in the Recycling Plant).
        var bins = LoadOrCreate<StorageDefinition>(DataDir + "/Factory/Storage_Bins.asset");
        SetMany(bins, ("id", "metal_bins"), ("displayName", "Metal Bins"));
        SetStructArray(bins, "levels", new[]
        {
            Lv(("capacity", 30), ("pickupInterval", 0.08f), ("upgradeCost", 0)),
            Lv(("capacity", 45), ("pickupInterval", 0.07f), ("upgradeCost", 500)),
            Lv(("capacity", 70), ("pickupInterval", 0.06f), ("upgradeCost", 1100)),
            Lv(("capacity", 100), ("pickupInterval", 0.05f), ("upgradeCost", 2400)),
        });

        // Metal Market (blueprint: queue caps at 5 in the Recycling Plant, then scales with the desk level).
        var market = LoadOrCreate<SellDeskDefinition>(DataDir + "/Factory/SellDesk_Market.asset");
        SetMany(market, ("id", "market"), ("displayName", "Metal Market"), ("firstSaleDelay", 1f),
            ("saleSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_Sale.asset")));
        SetArray(market, "sells", new Object[] { iron, copper });
        SetStructArray(market, "levels", new[]
        {
            Lv(("saleInterval", 7f), ("unitsPerSale", new Vector2Int(2, 4)), ("priceMultiplier", 1f), ("counterCapacity", 16), ("queueCapacity", 5), ("upgradeCost", 0)),
            Lv(("saleInterval", 5.6f), ("unitsPerSale", new Vector2Int(3, 5)), ("priceMultiplier", 1.1f), ("counterCapacity", 20), ("queueCapacity", 5), ("upgradeCost", 800)),
            Lv(("saleInterval", 4.5f), ("unitsPerSale", new Vector2Int(3, 6)), ("priceMultiplier", 1.2f), ("counterCapacity", 24), ("queueCapacity", 6), ("upgradeCost", 1700)),
            Lv(("saleInterval", 3.6f), ("unitsPerSale", new Vector2Int(4, 7)), ("priceMultiplier", 1.35f), ("counterCapacity", 28), ("queueCapacity", 7), ("upgradeCost", 3400)),
        });
        var yardDesk = Load<SellDeskDefinition>(DataDir + "/Factory/SellDesk_Yard.asset");
        SetArray(yardDesk, "sells", new Object[] { mixed });

        var marketCustomers = LoadOrCreate<CustomerConfig>(DataDir + "/Customers/CustomerConfig_Market.asset");
        var yardCustomers = Load<CustomerConfig>(DataDir + "/Customers/CustomerConfig.asset");
        EditorUtility.CopySerialized(yardCustomers, marketCustomers);
        marketCustomers.name = "CustomerConfig_Market";
        SetMany(marketCustomers, ("orderItem", iron), ("preferInStock", 0.8f));
        SetStructArray(marketCustomers, "orderOptions", new[] { Lv(("item", iron), ("weight", 0.62f)), Lv(("item", copper), ("weight", 0.38f)) });

        // Active Overdrive (blueprint p.5: 2x throughput for a while when the player helps at the machine).
        var overdrive = LoadOrCreate<OverdriveConfig>(DataDir + "/Factory/OverdriveConfig.asset");
        SetMany(overdrive, ("speedMultiplier", 2f), ("duration", 10f), ("chargeTime", 1.5f), ("cooldown", 6f),
            ("chargeSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_OverdriveCharge.asset")),
            ("startSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_OverdriveStart.asset")),
            ("endSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_OverdriveEnd.asset")));

        // Workers (blueprint: 2-3 workers by 30-45 min).
        var hauler = LoadOrCreate<WorkerDefinition>(DataDir + "/Workers/Worker_Hauler.asset");
        SetMany(hauler, ("id", "hauler"), ("displayName", "Metal Hauler"), ("role", (int)WorkerRole.Porter), ("siteId", "hauler"),
            ("tagline", "HAULS BALES TO THE SORTER"), ("moveSpeed", 3.8f), ("carryCapacity", 8), ("efficiency", 1f), ("pickupRadius", 1f),
            ("collectLooseItems", false));
        SetLongArray(hauler, "hireCosts", new long[] { 1200, 2600 });
        var runner = LoadOrCreate<WorkerDefinition>(DataDir + "/Workers/Worker_Runner.asset");
        SetMany(runner, ("id", "runner"), ("displayName", "Market Runner"), ("role", (int)WorkerRole.Porter), ("siteId", "market"),
            ("tagline", "STOCKS THE METAL MARKET"), ("moveSpeed", 3.6f), ("carryCapacity", 8), ("efficiency", 1f), ("pickupRadius", 1f),
            ("collectLooseItems", false));
        SetLongArray(runner, "hireCosts", new long[] { 1500, 3000 });

        // Gate 2.
        var plant = LoadOrCreate<ExpansionDefinition>(DataDir + "/World/Expansion_RecyclingPlant.asset");
        SetMany(plant, ("id", "recycling_plant"), ("displayName", "Recycling Plant"), ("cost", 3500L), ("teaser", "SORTER + METAL MARKET"),
            ("openSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_AreaOpen.asset")));

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
            Lv(("upgradeId", "porter"), ("unlockLevel", 3), ("inPanel", false), ("group", "")),
            Lv(("upgradeId", "helper"), ("unlockLevel", 4), ("inPanel", false), ("group", "")),
            Lv(("upgradeId", "back_lot"), ("unlockLevel", 5), ("inPanel", false), ("group", "")),
            Lv(("upgradeId", "recycling_plant"), ("unlockLevel", 6), ("inPanel", false), ("group", "")),
            Lv(("upgradeId", "hauler"), ("unlockLevel", 6), ("inPanel", false), ("group", "")),
            Lv(("upgradeId", "runner"), ("unlockLevel", 7), ("inPanel", false), ("group", "")),
        });

        BuildTasks();
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    static void BuildTasks()
    {
        string dir = DataDir + "/Progression/Tasks";
        var chain = Load<TaskChain>(DataDir + "/Progression/TaskChain_Area1.asset");
        var so = new SerializedObject(chain);
        var main = new List<Object>();
        var mainProp = so.FindProperty("mainTasks");
        for (int i = 0; i < mainProp.arraySize; i++) main.Add(mainProp.GetArrayElementAtIndex(i).objectReferenceValue);
        main.RemoveAll(t => t == null || ((TaskDefinition)t).Id.StartsWith("t2") || ((TaskDefinition)t).Id == "t08b_boost");

        // Teach Active Overdrive right after the first real production push.
        int afterCrush = main.FindIndex(t => ((TaskDefinition)t).Id == "t08_crush30");
        main.Insert(afterCrush + 1, Task(dir, "t08b_boost", "Boost the crusher", TaskCategory.Main, TaskType.Overdrive, "crusher", 1, 40, 30));

        main.AddRange(new Object[]
        {
            Task(dir, "t20_plant", "Open the Recycling Plant", TaskCategory.Main, TaskType.ReachUpgradeLevel, "recycling_plant", 1, 0, 100),
            Task(dir, "t21_sort", "Sort {0} metal bales", TaskCategory.Main, TaskType.ProcessItems, "sorter", 15, 120, 50),
            Task(dir, "t22_copper", "Sell {0} copper", TaskCategory.Main, TaskType.SellItems, "copper", 6, 200, 50),
            Task(dir, "t23_market", "Serve {0} market customers", TaskCategory.Main, TaskType.ServeCustomers, "market", 8, 150, 60),
            Task(dir, "t24_hauler", "Hire a metal hauler", TaskCategory.Main, TaskType.HireWorker, "hauler", 1, 0, 60),
            Task(dir, "t25_boost_sorter", "Boost the sorter", TaskCategory.Main, TaskType.Overdrive, "sorter", 1, 60, 40),
            Task(dir, "t26_level8", "Reach yard Lv.{0}", TaskCategory.Main, TaskType.ReachLevel, "", 8, 300, 0),
            Task(dir, "t27_sort100", "Sort {0} metal bales", TaskCategory.Main, TaskType.ProcessItems, "sorter", 100, 500, 120),
        });
        SetArray(chain, "mainTasks", main.ToArray());
    }

    static TaskDefinition Task(string dir, string id, string title, TaskCategory category, TaskType type, string target, long amount, long cash, int xp)
    {
        var t = LoadOrCreate<TaskDefinition>($"{dir}/Task_{id}.asset");
        SetMany(t, ("id", id), ("title", title), ("category", (int)category), ("type", (int)type), ("targetId", target), ("amount", amount),
            ("rewardCash", cash), ("rewardXp", xp), ("rewardPremium", 0));
        return t;
    }

    // ---------- audio (Kenney CC0: Impact Sounds, Interface Sounds, RPG Audio, Casino Audio, Music Jingles) ----------

    static void ImportAudio()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var settings = importer.defaultSampleSettings;
            bool music = path.Contains("/Jingles/");
            settings.loadType = music ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.6f;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = true;
            importer.SaveAndReimport();
        }
    }

    static void Audio()
    {
        Clips("MetalHit", 0.42f, new Vector2(0.95f, 1.12f), "Impact/impactMetal_light_000", "Impact/impactMetal_light_001", "Impact/impactMetal_light_002",
            "Impact/impactMetal_light_003", "Impact/impactMetal_light_004");
        Clips("MetalBreak", 0.75f, new Vector2(0.92f, 1.05f), "Impact/impactMetal_heavy_000", "Impact/impactMetal_heavy_001", "Impact/impactMetal_heavy_002",
            "Impact/impactMetal_heavy_003", "Impact/impactMetal_heavy_004");
        Clips("RubberHit", 0.5f, new Vector2(0.95f, 1.1f), "Impact/impactSoft_medium_000", "Impact/impactSoft_medium_001", "Impact/impactSoft_medium_002");
        Clips("RubberBreak", 0.65f, new Vector2(0.9f, 1.05f), "Impact/impactPunch_heavy_000", "Impact/impactPunch_heavy_001", "Impact/impactPunch_heavy_002");
        Clips("PartDetach", 0.55f, new Vector2(0.95f, 1.1f), "Impact/impactPlate_medium_000", "Impact/impactPlate_medium_001", "Impact/impactPlate_medium_002");
        Clips("Pickup", 0.32f, new Vector2(1f, 1f), "Interface/pluck_001", "Interface/pluck_002");
        Clips("StackFull", 0.45f, new Vector2(1f, 1f), "Interface/bong_001");
        Clips("PadTransfer", 0.3f, new Vector2(1f, 1f), "Impact/impactPlate_light_000", "Impact/impactPlate_light_001", "Impact/impactPlate_light_002",
            "Impact/impactPlate_light_003", "Impact/impactPlate_light_004");
        Clips("MachineCycle", 0.38f, new Vector2(0.95f, 1.05f), "Impact/impactMining_000", "Impact/impactMining_001", "Impact/impactMining_002",
            "Impact/impactMining_003", "Impact/impactMining_004");
        Clips("MachineOutput", 0.36f, new Vector2(0.95f, 1.08f), "Impact/impactMetal_medium_000", "Impact/impactMetal_medium_001", "Impact/impactMetal_medium_002",
            "Impact/impactMetal_medium_003", "Impact/impactMetal_medium_004");
        Clips("SorterCycle", 0.36f, new Vector2(0.95f, 1.08f), "RPG/metalLatch", "RPG/metalClick");
        Clips("Sale", 0.6f, new Vector2(0.97f, 1.05f), "RPG/handleCoins2");
        Clips("CashCollect", 0.4f, new Vector2(1f, 1f), "Casino/chip-lay-1", "Casino/chip-lay-2", "Casino/chip-lay-3");
        Clips("TilePay", 0.3f, new Vector2(1f, 1f), "Casino/chips-collide-1", "Casino/chips-collide-2", "Casino/chips-collide-3", "Casino/chips-collide-4");
        Clips("Spawn", 0.35f, new Vector2(0.95f, 1.1f), "Interface/drop_001", "Interface/drop_002", "Interface/drop_003", "Interface/drop_004");
        Clips("Upgrade", 0.6f, new Vector2(1f, 1f), "Jingles/jingles_PIZZI04");
        Clips("LevelUp", 0.7f, new Vector2(1f, 1f), "Jingles/jingles_STEEL02");
        Clips("Hire", 0.6f, new Vector2(1f, 1f), "Jingles/jingles_PIZZI02");
        Clips("AreaOpen", 0.75f, new Vector2(1f, 1f), "Jingles/jingles_STEEL07");
        Clips("Denied", 0.45f, new Vector2(1f, 1f), "Interface/error_006");
        Clips("TileEngage", 0.4f, new Vector2(1f, 1.08f), "Interface/select_002");
        Clips("CustomerArrive", 0.3f, new Vector2(1f, 1.15f), "Interface/question_001", "Interface/question_002");
        Clips("CustomerHappy", 0.5f, new Vector2(0.97f, 1.08f), "RPG/handleCoins");
        Clips("OverdriveCharge", 0.35f, new Vector2(1f, 1f), "Interface/tick_001");
        Clips("OverdriveStart", 0.65f, new Vector2(1f, 1f), "Interface/maximize_006");
        Clips("OverdriveEnd", 0.45f, new Vector2(1f, 1f), "Interface/minimize_006");
    }

    static void Clips(string sfx, float volume, Vector2 pitch, params string[] files)
    {
        var def = LoadOrCreate<SfxDefinition>($"{DataDir}/Audio/Sfx_{sfx}.asset");
        var clips = files.Select(f => Load<AudioClip>($"{AudioDir}/{f}.ogg")).Where(c => c != null).Cast<Object>().ToArray();
        SetArray(def, "clips", clips);
        SetMany(def, ("volume", volume), ("pitchRange", pitch));
    }

    // =====================================================================================
    // PREFABS
    // =====================================================================================

    public static string Prefabs()
    {
        Log.Clear();
        var font = Load<TMP_FontAsset>(FontDir + "/GROBOLD SDF.asset");
        var outline = Load<Material>(FontDir + "/GROBOLD Outline.mat");
        var worldText = Load<Material>(FontDir + "/GROBOLD World.mat");
        var mSteelDark = Load<Material>(MatDir + "/M_SteelDark.mat");
        var mSteel = Load<Material>(MatDir + "/M_SteelMid.mat");
        var mHazard = Load<Material>(MatDir + "/M_HazardYellow.mat");
        var mConcrete = Load<Material>(MatDir + "/M_ConcretePad.mat");
        var mLight = Load<Material>(MatDir + "/M_StatusLight.mat");
        var factory = Load<Material>(MatDir + "/M_Kenney_FactoryKit.mat");
        var mYellow = Lit("SorterYellow", new Color(1f, 0.76f, 0.1f), 0.4f, 0.1f);
        var mYellowDark = Lit("SorterYellowDark", new Color(0.86f, 0.56f, 0.06f), 0.35f, 0.1f);
        var mMagnet = Lit("MagnetRed", new Color(0.86f, 0.18f, 0.14f), 0.45f, 0.3f);
        var mIron = Lit("IronDark", new Color(0.42f, 0.45f, 0.52f), 0.55f, 0.35f);
        var mIronBlue = Lit("IronBlue", new Color(0.38f, 0.44f, 0.54f), 0.55f, 0.35f);
        var mRust = Lit("IronStrap", new Color(0.66f, 0.3f, 0.15f), 0.35f, 0.2f);
        var mCopper = Lit("Copper", new Color(0.98f, 0.52f, 0.2f), 0.6f, 0.2f);
        var mCopperBright = Lit("CopperBright", new Color(1f, 0.62f, 0.3f), 0.6f, 0.2f);
        var mSpool = Lit("SpoolWood", new Color(0.55f, 0.36f, 0.2f), 0.3f);
        var mTile = Load<Material>(MatDir + "/M_TileBase.mat");

        // ---------- Items ----------
        var ironDef = Load<ItemDefinition>(DataDir + "/Items/Item_Iron.asset");
        var copperDef = Load<ItemDefinition>(DataDir + "/Items/Item_Copper.asset");
        var ironPrefab = SavePrefab(BuildItem("Item_Iron", BakeIronBars(), new[] { mIron, mRust }, new[] { mIron, mIronBlue }, 0.14f),
            PrefabDir + "/Items/Item_Iron.prefab");
        var copperPrefab = SavePrefab(BuildItem("Item_Copper", BakeCopperCoil(), new[] { mCopper, mSpool }, new[] { mCopper, mCopperBright }, 0.12f),
            PrefabDir + "/Items/Item_Copper.prefab");
        SetMany(ironDef, ("prefab", ironPrefab.GetComponent<WorldItem>()));
        SetMany(copperDef, ("prefab", copperPrefab.GetComponent<WorldItem>()));
        SetMany(ironDef, ("icon", RenderIcon("Icon_Iron", ironPrefab, new Vector3(35f, 30f, 0f))));
        SetMany(copperDef, ("icon", RenderIcon("Icon_Copper", copperPrefab, new Vector3(45f, 30f, 0f))));

        // ---------- VFX: overdrive sparks (looping while boosted) ----------
        var sparksPrefab = SavePrefab(BuildOverdriveSparks(), PrefabDir + "/VFX/VFX_OverdriveSparks.prefab");

        // ---------- Stations: derived guide anchors + crusher overdrive look ----------
        EditPrefab(PrefabDir + "/Stations/Crusher.prefab", root =>
        {
            var machine = root.GetComponent<Machine>();
            StationAnchor(root.transform.Find("InputPad"), machine, "in");
            var body = root.transform.Find("Body");
            var old = body.Find("VFX_OverdriveSparks");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var sparks = (GameObject)PrefabUtility.InstantiatePrefab(sparksPrefab, body);
            sparks.transform.localPosition = new Vector3(0f, 2.6f, 0f);
            SetMany(root.GetComponent<MachineVisuals>(), ("overdriveParticles", sparks.GetComponent<ParticleSystem>()));
        });
        EditPrefab(PrefabDir + "/Stations/Storage.prefab", root => StationAnchor(root.transform.Find("WithdrawPad"), root.GetComponent<Storage>(), "out"));
        EditPrefab(PrefabDir + "/Stations/SellDesk.prefab", root =>
        {
            var desk = root.GetComponent<SellDesk>();
            StationAnchor(root.transform.Find("StockPad"), desk, "in");
            StationAnchor(root.transform.Find("CashPad"), desk, "cash");
        });

        var labelPrefab = Load<GameObject>(PrefabDir + "/Stations/StationLabel.prefab");
        var padPrefab = Load<GameObject>(PrefabDir + "/Stations/PadVisual.prefab");
        var sorterDef = Load<MachineDefinition>(DataDir + "/Factory/Machine_Sorter.asset");
        SavePrefab(BuildSorter(sorterDef, labelPrefab, padPrefab, sparksPrefab, mYellow, mYellowDark, mMagnet, mSteelDark, mSteel, mHazard, mConcrete, mLight,
            factory, ironDef, copperDef), PrefabDir + "/Stations/Sorter.prefab");

        // ---------- Boost tile ----------
        SavePrefab(BuildBoostTile(font, outline, mTile), PrefabDir + "/Tiles/Tile_Boost.prefab");

        // ---------- Workers ----------
        var workerController = Load<AnimatorController>(AnimDir + "/AC_Worker.controller");
        var hatGrey = Lit("CapGrey", new Color(0.52f, 0.56f, 0.62f), 0.5f);
        var hatGreen = Lit("CapGreen", new Color(0.25f, 0.78f, 0.35f), 0.5f);
        var hauler = SavePrefab(BuildPadWorker("Worker_Hauler", "character-male-d", workerController, hatGrey, mSteelDark), PrefabDir + "/Workers/Worker_Hauler.prefab");
        var runner = SavePrefab(BuildPadWorker("Worker_Runner", "character-female-b", workerController, hatGreen, mSteelDark), PrefabDir + "/Workers/Worker_Runner.prefab");
        SetMany(Load<WorkerDefinition>(DataDir + "/Workers/Worker_Hauler.asset"), ("prefab", hauler.GetComponent<Worker>()));
        SetMany(Load<WorkerDefinition>(DataDir + "/Workers/Worker_Runner.asset"), ("prefab", runner.GetComponent<Worker>()));

        // ---------- Quality: round hard hats on everyone ----------
        EditPrefab(PrefabDir + "/Player/Player.prefab", root => RebuildHat(root.transform, Load<Material>(MatDir + "/M_HardHat.mat"), true));
        EditPrefab(PrefabDir + "/Workers/Worker_Porter.prefab", root => RebuildHat(root.transform, Load<Material>(MatDir + "/M_HardHatOrange.mat"), false));
        EditPrefab(PrefabDir + "/Workers/Worker_Helper.prefab", root => RebuildHat(root.transform, Load<Material>(MatDir + "/M_CapBlue.mat"), false));

        Icons();
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    /// <summary>Renders the M5 icons and assigns them.</summary>
    public static string Icons()
    {
        var sorter = RenderIcon("Icon_Sorter", PrefabRoot("Stations/Sorter", "InputPad", "StationLabel", "VFX_OverdriveSparks", "VFX_CrusherDust", "Markers"),
            new Vector3(28f, -35f, 0f));
        SetMany(Load<MachineDefinition>(DataDir + "/Factory/Machine_Sorter.asset"), ("icon", sorter));
        SetMany(Load<ExpansionDefinition>(DataDir + "/World/Expansion_RecyclingPlant.asset"), ("icon", sorter));
        var bins = RenderIcon("Icon_Bins", PrefabRoot("Stations/Storage", "WithdrawPad", "StationLabel", "LevelVisuals"), new Vector3(40f, -35f, 0f));
        SetMany(Load<StorageDefinition>(DataDir + "/Factory/Storage_Bins.asset"), ("icon", bins));
        var market = RenderIcon("Icon_Market", PrefabRoot("Stations/SellDesk", "StockPad", "CashPad", "StationLabel", "LevelVisuals", "CashPile"),
            new Vector3(25f, 160f, 0f));
        SetMany(Load<SellDeskDefinition>(DataDir + "/Factory/SellDesk_Market.asset"), ("icon", market));
        var hauler = RenderIcon("Icon_Hauler", Load<GameObject>(PrefabDir + "/Workers/Worker_Hauler.prefab"), new Vector3(15f, 160f, 0f), "idle");
        SetMany(Load<WorkerDefinition>(DataDir + "/Workers/Worker_Hauler.asset"), ("icon", hauler));
        var runner = RenderIcon("Icon_Runner", Load<GameObject>(PrefabDir + "/Workers/Worker_Runner.prefab"), new Vector3(15f, 160f, 0f), "idle");
        SetMany(Load<WorkerDefinition>(DataDir + "/Workers/Worker_Runner.asset"), ("icon", runner));
        // Hats changed: re-render the worker icons that show them.
        SetMany(Load<WorkerDefinition>(DataDir + "/Workers/Worker_Porter.asset"),
            ("icon", RenderIcon("Icon_Porter", Load<GameObject>(PrefabDir + "/Workers/Worker_Porter.prefab"), new Vector3(15f, 160f, 0f), "idle")));
        SetMany(Load<WorkerDefinition>(DataDir + "/Workers/Worker_Helper.asset"),
            ("icon", RenderIcon("Icon_Helper", Load<GameObject>(PrefabDir + "/Workers/Worker_Helper.prefab"), new Vector3(15f, 160f, 0f), "idle")));
        AssetDatabase.SaveAssets();
        return Log.ToString();
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

    static GameObject BuildSorter(MachineDefinition def, GameObject labelPrefab, GameObject padPrefab, GameObject sparksPrefab, Material yellow,
        Material yellowDark, Material magnet, Material steelDark, Material steel, Material hazard, Material concrete, Material lightMat, Material factory,
        ItemDefinition iron, ItemDefinition copper)
    {
        var root = new GameObject("Sorter");
        Box("Plinth", root.transform, new Vector3(0f, 0.12f, 0f), new Vector3(4.3f, 0.24f, 3.6f), concrete, 0.04f);

        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        body.localPosition = new Vector3(0f, 0.24f, 0f);
        Box("Housing", body, new Vector3(0f, 0.68f, 0.1f), new Vector3(3.7f, 1.36f, 2.7f), yellow, 0.1f);
        Box("Band", body, new Vector3(0f, 0.22f, 0.1f), new Vector3(3.76f, 0.2f, 2.76f), steelDark, 0.03f);
        Box("TopRim", body, new Vector3(0f, 1.38f, 0.1f), new Vector3(3.8f, 0.1f, 2.8f), yellowDark, 0.03f);
        for (int i = -1; i <= 1; i += 2)
            Box("Stripe", body, new Vector3(i * 1.86f, 0.68f, 0.1f), new Vector3(0.05f, 0.42f, 2.0f), hazard, 0f);

        // Vibrating screen deck with spinning rollers, and a red magnet drum pulling the copper aside.
        var deck = Box("Deck", body, new Vector3(0f, 1.52f, 0.05f), new Vector3(3.3f, 0.1f, 2.3f), steel, 0.02f);
        deck.transform.localRotation = Quaternion.Euler(-5f, 0f, 0f);
        var spinners = new List<Transform>();
        for (int i = 0; i < 4; i++)
        {
            var roller = Cylinder("Roller", body, new Vector3(0f, 1.66f, -0.72f + i * 0.48f), 0.12f, 3.1f, 10, steelDark);
            roller.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            spinners.Add(roller.transform);
        }

        foreach (var x in new[] { -1.72f, 1.72f })
            Box("DrumPost", body, new Vector3(x, 1.95f, 0.75f), new Vector3(0.16f, 0.9f, 0.16f), steelDark, 0.02f);
        var drum = Cylinder("MagnetDrum", body, new Vector3(0f, 2.25f, 0.75f), 0.34f, 3.3f, 16, magnet);
        drum.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        spinners.Add(drum.transform);

        var hopper = Kenney("FactoryKit", "hopper-square", body, new Vector3(0f, 1.56f, -0.55f), 1f, Vector3.zero, factory);
        var hb = RendererBounds(hopper).size;
        hopper.transform.localScale = new Vector3(1.6f / hb.x, 1.0f / hb.y, 1.3f / hb.z);

        // Two colour-coded chutes on the south face: iron (steel grey) west, copper (orange) east.
        var markers = new GameObject("Markers").transform;
        markers.SetParent(root.transform, false);
        var ironChute = Box("IronChute", root.transform, new Vector3(-1.4f, 0.74f, -1.62f), new Vector3(0.95f, 0.1f, 0.9f), steelDark, 0.02f);
        ironChute.transform.localRotation = Quaternion.Euler(-22f, 0f, 0f);
        var copperChute = Box("CopperChute", root.transform, new Vector3(1.4f, 0.74f, -1.62f), new Vector3(0.95f, 0.1f, 0.9f), steelDark, 0.02f);
        copperChute.transform.localRotation = Quaternion.Euler(-22f, 0f, 0f);
        Box("IronPlate", markers, new Vector3(-1.4f, 1.0f, -1.27f), new Vector3(0.9f, 0.5f, 0.06f), Lit("IronMarker", IronTint, 0.4f, 0.3f), 0.02f);
        Box("CopperPlate", markers, new Vector3(1.4f, 1.0f, -1.27f), new Vector3(0.9f, 0.5f, 0.06f), Lit("CopperMarker", CopperTint, 0.5f, 0.3f), 0.02f);
        ChuteIcon(markers, "IronIcon", new Vector3(-1.4f, 1.0f, -1.31f), iron);
        ChuteIcon(markers, "CopperIcon", new Vector3(1.4f, 1.0f, -1.31f), copper);

        var light = Cylinder("StatusLight", body, new Vector3(0f, 1.1f, -1.27f), 0.15f, 0.12f, 12, lightMat);
        light.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var intake = Empty("Intake", body, new Vector3(0f, 1.4f, -0.55f));
        var ironOut = Empty("IronOut", root.transform, new Vector3(-1.4f, 0.72f, -1.95f));
        var copperOut = Empty("CopperOut", root.transform, new Vector3(1.4f, 0.72f, -1.95f));
        var hopperPile = Empty("HopperPile", body, new Vector3(0f, 2.05f, -0.55f)).gameObject.AddComponent<ItemPile>();
        SetMany(hopperPile, ("capacity", 12), ("columns", 3), ("rows", 2), ("cellSize", new Vector3(0.48f, 0.3f, 0.48f)), ("arriveDuration", 0.32f),
            ("arriveArc", 1.6f));

        var dust = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PrefabDir + "/VFX/VFX_CrusherDust.prefab"), body);
        dust.transform.localPosition = new Vector3(0f, 2.2f, 0f);
        dust.transform.localScale = Vector3.one * 0.7f;
        var sparks = (GameObject)PrefabUtility.InstantiatePrefab(sparksPrefab, body);
        sparks.transform.localPosition = new Vector3(0f, 2.5f, 0.4f);

        var labelGo = (GameObject)PrefabUtility.InstantiatePrefab(labelPrefab, root.transform);
        labelGo.transform.localPosition = new Vector3(0f, 4.8f, 1.6f);
        labelGo.transform.localScale = Vector3.one * 1.4f;

        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 1.3f, 0.1f);
        box.size = new Vector3(4.0f, 2.6f, 3.0f);

        var visuals = root.AddComponent<MachineVisuals>();
        SetMany(visuals, ("body", body), ("spinAxis", Vector3.up), ("spinSpeed", 360f), ("workParticles", dust.GetComponent<ParticleSystem>()),
            ("statusLight", light.GetComponent<Renderer>()), ("overdriveParticles", sparks.GetComponent<ParticleSystem>()));
        SetArray(visuals, "spinners", spinners.Cast<Object>().ToArray());
        SetArray(visuals, "pistons", Array.Empty<Object>());

        var machine = root.AddComponent<Machine>();
        SetMany(machine, ("definition", def), ("hopper", hopperPile), ("intake", intake), ("outputPoint", ironOut), ("visuals", visuals),
            ("label", labelGo.GetComponent<StationLabel>()));
        SetStructArray(machine, "outputPorts", new[]
        {
            Lv(("item", iron), ("target", null), ("point", ironOut)),
            Lv(("item", copper), ("target", null), ("point", copperOut)),
        });

        var pad = Pad(root.transform, "InputPad", new Vector3(-3.4f, 0f, 0.1f), new Vector2(2.2f, 2.2f), TransferMode.Deposit, machine, new Color(1f, 0.78f, 0.15f),
            padPrefab, 90f, "Sfx_PadTransfer");
        StationAnchor(pad.transform, machine, "in");
        return root;
    }

    static void ChuteIcon(Transform parent, string name, Vector3 localPos, ItemDefinition item)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Load<Sprite>($"{IconDir}/Icon_{item.DisplayName}.png", false);
        go.transform.localScale = Vector3.one * 0.18f;
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
    }

    static GameObject BuildOverdriveSparks()
    {
        var go = new GameObject("VFX_OverdriveSparks");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        var colors = new Gradient();
        colors.SetKeys(new[] { new GradientColorKey(new Color(0.55f, 0.95f, 1f), 0f), new GradientColorKey(new Color(1f, 0.85f, 0.35f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(colors) { mode = ParticleSystemGradientMode.RandomColor };
        main.gravityModifier = 1.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 200;
        var emission = ps.emission;
        emission.rateOverTime = 70f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.6f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.06f;
        r.lengthScale = 1.5f;
        r.sharedMaterial = Load<Material>(MatDir + "/M_FX_Sparks.mat");
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    static GameObject BuildBoostTile(TMP_FontAsset font, Material outline, Material baseMat)
    {
        var root = new GameObject("Tile_Boost");
        var visual = Empty("Visual", root.transform, Vector3.zero);
        var baseMesh = Cylinder("Base", visual, new Vector3(0f, 0.04f, 0f), 1.1f, 0.08f, 24, baseMat);
        _ = baseMesh;
        var canvas = WorldCanvas("Display", visual, new Vector3(0f, 0.086f, 0f), new Vector2(220f, 220f), true);
        var content = Rect("Content", canvas);
        Stretch(content);
        var glow = Img("Glow", content, UiShape("UI_Circle"), new Color(1f, 0.8f, 0.2f, 0.18f), new Vector2(210f, 210f));
        var track = Img("Track", content, UiShape("UI_Ring"), new Color(0f, 0f, 0f, 0.35f), new Vector2(206f, 206f));
        _ = track;
        var gauge = Img("Gauge", content, UiShape("UI_Ring"), new Color(1f, 0.8f, 0.2f), new Vector2(206f, 206f));
        var gaugeImg = gauge.GetComponent<Image>();
        gaugeImg.type = Image.Type.Filled;
        gaugeImg.fillMethod = Image.FillMethod.Radial360;
        gaugeImg.fillOrigin = (int)Image.Origin360.Top;
        gaugeImg.fillClockwise = true;
        gaugeImg.fillAmount = 0f;
        var bolt = Img("Bolt", content, UiShape("UI_Bolt"), Color.white, new Vector2(96f, 96f));
        bolt.anchoredPosition = new Vector2(0f, 18f);
        var label = Txt("Label", content, font, outline, 30f, Color.white, TextAlignmentOptions.Center, "BOOST");
        label.rectTransform.anchoredPosition = new Vector2(0f, -52f);
        label.rectTransform.sizeDelta = new Vector2(180f, 40f);
        AutoSize(label, 16f, 30f);

        var tile = root.AddComponent<OverdriveTile>();
        SetMany(tile, ("size", new Vector2(2.2f, 2.2f)), ("visual", visual), ("engageSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_TileEngage.asset")),
            ("config", Load<OverdriveConfig>(DataDir + "/Factory/OverdriveConfig.asset")), ("displayRoot", content), ("gauge", gaugeImg),
            ("glow", glow.GetComponent<Image>()), ("label", label));
        return root;
    }

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

    /// <summary>
    /// Replaces the box-shaped hard hat with a rounded dome, brim and ridge sized to the existing hat footprint, so it
    /// reads as a helmet from the top-down camera instead of a cube.
    /// </summary>
    static void RebuildHat(Transform root, Material mat, bool ridge)
    {
        var hat = FindDeep(root, "HardHat");
        if (hat == null)
        {
            Log.AppendLine("!! no HardHat under " + root.name);
            return;
        }

        var dome = hat.Find("Dome");
        Vector3 size;
        if (dome != null && dome.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
        {
            var b = mf.sharedMesh.bounds;
            size = Vector3.Scale(b.size, dome.lossyScale);
        }
        else if (hat.Find("Shell") != null) return; // already rebuilt
        else size = new Vector3(0.45f, 0.18f, 0.45f);

        foreach (Transform c in hat.Cast<Transform>().ToArray()) Object.DestroyImmediate(c.gameObject);
        float w = size.x, d = size.z;

        // Dome: a squashed sphere whose lower half sinks into the head.
        var shell = ShapeGenerator.GenerateIcosahedron(PivotLocation.Center, 0.5f, 2, true, false);
        FinishPb(shell, "Shell", hat, Vector3.zero, mat);
        shell.transform.position = hat.position + hat.up * 0.02f;
        shell.transform.rotation = hat.rotation;
        SetWorldScale(shell.transform, new Vector3(w * 1.08f, 0.42f * w, d * 1.08f));
        var brim = ShapeGenerator.GenerateCylinder(PivotLocation.Center, 24, 0.5f, 0.04f, 0, 1);
        FinishPb(brim, "Brim", hat, Vector3.zero, mat);
        brim.transform.position = hat.position - hat.up * 0.0f + hat.forward * (d * 0.08f);
        brim.transform.rotation = hat.rotation;
        SetWorldScale(brim.transform, new Vector3(w * 1.2f, 1f, d * 1.34f));
        if (ridge)
        {
            var r = Box("Ridge", hat, Vector3.zero, new Vector3(0.09f, 0.08f, d * 0.9f), mat, 0.02f, true);
            r.transform.position = hat.position + hat.up * (0.21f * w);
            r.transform.rotation = hat.rotation;
        }
    }

    static void SetWorldScale(Transform t, Vector3 world)
    {
        var parent = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(world.x / parent.x, world.y / parent.y, world.z / parent.z);
    }

    // ---------- item meshes ----------

    static Mesh BakeIronBars()
    {
        var holder = new GameObject("tmp");
        var tmp = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var bars = new List<ProBuilderMesh>();
        for (int i = 0; i < 3; i++) bars.Add(Box("Bar", holder.transform, new Vector3((i - 1) * 0.15f, -0.065f, 0f), new Vector3(0.14f, 0.13f, 0.46f), tmp, 0.025f));
        for (int i = 0; i < 2; i++) bars.Add(Box("Bar", holder.transform, new Vector3((i - 0.5f) * 0.15f, 0.065f, 0f), new Vector3(0.14f, 0.13f, 0.44f), tmp, 0.025f));
        var strapA = Box("StrapA", holder.transform, new Vector3(0f, -0.065f, 0.13f), new Vector3(0.47f, 0.135f, 0.05f), tmp, 0f);
        var strapB = Box("StrapB", holder.transform, new Vector3(0f, -0.065f, -0.13f), new Vector3(0.47f, 0.135f, 0.05f), tmp, 0f);
        var mesh = CombineSubmeshes("IronBars", bars.ToArray(), new[] { strapA, strapB });
        Object.DestroyImmediate(holder);
        return mesh;
    }

    static Mesh BakeCopperCoil()
    {
        var holder = new GameObject("tmp");
        var tmp = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var rings = new List<ProBuilderMesh>();
        for (int i = 0; i < 3; i++)
        {
            var torus = ShapeGenerator.GenerateTorus(PivotLocation.Center, 10, 20, 0.22f, 0.045f, true, 360f, 360f);
            FinishPb(torus, "Ring", holder.transform, Vector3.zero, tmp);
            var b = torus.GetComponent<MeshFilter>().sharedMesh.bounds;
            // Lay it flat whatever axis the generator used, then fit it: 0.44 across, 0.085 thick.
            bool upright = b.size.y > Mathf.Min(b.size.x, b.size.z) * 1.5f;
            Vector3 flat = upright ? new Vector3(b.size.x, b.size.z, b.size.y) : b.size;
            if (upright) torus.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var fit = new Vector3(0.44f / flat.x, 0.085f / flat.y, 0.44f / flat.z);
            torus.transform.localScale = upright ? new Vector3(fit.x, fit.z, fit.y) : fit;
            torus.transform.localPosition = new Vector3(0f, -0.08f + i * 0.08f, 0f);
            rings.Add(torus);
        }

        var core = ShapeGenerator.GenerateCylinder(PivotLocation.Center, 12, 0.07f, 0.27f, 0, 1);
        FinishPb(core, "Core", holder.transform, Vector3.zero, tmp);
        var mesh = CombineSubmeshes("CopperCoil", rings.ToArray(), new[] { core });
        Object.DestroyImmediate(holder);
        return mesh;
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

    // ---------- pads (same look as M2) ----------

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

    static float Bolt(int x, int y, int size)
    {
        float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
        Vector2[] poly =
        {
            new(0.62f, 0.96f), new(0.26f, 0.5f), new(0.47f, 0.5f), new(0.36f, 0.04f), new(0.76f, 0.56f), new(0.55f, 0.56f), new(0.72f, 0.96f)
        };
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > v) != (poly[j].y > v) && u < (poly[j].x - poly[i].x) * (v - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        return inside ? 1f : 0f;
    }

    // =====================================================================================
    // SCENE
    // =====================================================================================

    public static string Scene()
    {
        Log.Clear();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var systems = Root(scene, "_Systems");
        var gameplay = Root(scene, "_Gameplay");
        var environment = Root(scene, "_Environment");
        var font = Load<TMP_FontAsset>(FontDir + "/GROBOLD SDF.asset");
        var worldText = Load<Material>(FontDir + "/GROBOLD World.mat");

        var crusher = gameplay.GetComponentsInChildren<Machine>(true).First(m => m.Definition != null && m.Definition.Id == "crusher");
        var yardStorage = gameplay.GetComponentsInChildren<Storage>(true).First(s => s.Definition != null && s.Definition.Id == "storage_yard");
        var yardDesk = gameplay.GetComponentsInChildren<SellDesk>(true).First(d => d.Definition != null && d.Definition.Id == "sell_desk");
        var storagePad = yardStorage.transform.Find("WithdrawPad").GetComponent<TransferPad>();

        // ---------- Area 2 boundary (static), backdrop props cleared from the plot ----------
        BuildPlantBoundary(environment);
        var backdrop = environment.Find("Backdrop");
        foreach (Transform t in backdrop.Cast<Transform>().ToArray())
            if (t.position.x > 40.5f && t.position.x < 76f && t.position.z > 10f && t.position.z < 42f) Object.DestroyImmediate(t.gameObject);

        // ---------- Recycling Plant expansion ----------
        var plantRoot = FindOrCreate(gameplay, "RecyclingPlant");
        plantRoot.position = Vector3.zero;
        var content = ResetChild(plantRoot, "Content");
        var plant = BuildPlantContent(content, font, worldText);
        PlantIcons(content);

        if (!plantRoot.TryGetComponent(out Expansion expansion)) expansion = plantRoot.gameObject.AddComponent<Expansion>();
        var gate = environment.Find("Gate_A1_A2");
        var lockedSign = gate.Find("LockedSign");
        if (lockedSign == null)
        {
            lockedSign = new GameObject("LockedSign").transform;
            lockedSign.SetParent(gate, false);
            foreach (var n in new[] { "Sign", "SignText" })
            {
                var t = gate.Find(n);
                if (t != null) t.SetParent(lockedSign, true);
            }
        }

        if (!gate.TryGetComponent(out NavMeshModifier gateModifier)) gateModifier = gate.gameObject.AddComponent<NavMeshModifier>();
        gateModifier.ignoreFromBuild = true;
        var gateCollider = gate.Find("GateCollider");
        if (!gateCollider.TryGetComponent(out NavMeshObstacle gateObstacle)) gateObstacle = gateCollider.gameObject.AddComponent<NavMeshObstacle>();
        var gateBox = gateCollider.GetComponent<BoxCollider>();
        gateObstacle.shape = NavMeshObstacleShape.Box;
        gateObstacle.center = gateBox.center;
        gateObstacle.size = gateBox.size + new Vector3(0.1f, 0f, 0f);
        gateObstacle.carving = true;
        var barriers = new List<Object>();
        for (int i = 0; i < 5; i++) barriers.Add(gate.Find("Bar_" + i));
        barriers.Add(gateCollider);

        var focus = FindOrCreate(plantRoot, "Focus");
        focus.position = new Vector3(51f, 0f, 22f);
        var ground = environment.Find("Area2_Locked").GetComponent<Renderer>();
        SetMany(expansion, ("definition", Load<ExpansionDefinition>(DataDir + "/World/Expansion_RecyclingPlant.asset")), ("contentRoot", content),
            ("lockedOnly", lockedSign.gameObject), ("area", new Bounds(new Vector3(58f, 0f, 26f), new Vector3(35.6f, 4f, 31.6f))), ("focusPoint", focus),
            ("revealVfx", Load<GameObject>(PrefabDir + "/VFX/VFX_UpgradeBurst.prefab").GetComponent<ParticleSystem>()), ("focusHold", 3.2f),
            ("contentStagger", 0.1f), ("barrierSinkDepth", 2.6f));
        SetArray(expansion, "barriers", barriers.ToArray());
        SetStructArray(expansion, "materialSwaps", new[] { Lv(("renderer", ground), ("material", Load<Material>(MatDir + "/M_Ground_Dirt.mat"))) });

        // ---------- Tiles: gate purchase + crusher boost ----------
        var tiles = gameplay.Find("Tiles");
        PlaceTile(tiles, "Tile_RecyclingPlant", Load<GameObject>(PrefabDir + "/Tiles/Tile_Purchase.prefab"), new Vector3(37.2f, 0f, 24.5f), "recycling_plant",
            "tile/recycling_plant");
        PlaceBoost(tiles, "Tile_BoostCrusher", new Vector3(26.9f, 0f, 26.6f), crusher);

        // ---------- Workers ----------
        var sites = gameplay.Find("Workers/Sites");
        SetArray(sites.Find("DeliveryRoute").GetComponent<PorterRoute>(), "pickupPads", new Object[] { storagePad });
        var haulRoute = Route(sites, "HaulerRoute", "hauler", new Vector3(43.6f, 0f, 25.4f), new Object[] { storagePad },
            plant.sorter.transform.Find("InputPad").GetComponent<TransferPad>());
        var runRoute = Route(sites, "RunnerRoute", "market", new Vector3(56.5f, 0f, 19.6f), new Object[] { plant.ironPad, plant.copperPad },
            plant.market.transform.Find("StockPad").GetComponent<TransferPad>());
        var wm = systems.Find("WorkerManager").GetComponent<WorkerManager>();
        SetArray(wm, "hireable", new Object[]
        {
            Load<WorkerDefinition>(DataDir + "/Workers/Worker_Porter.asset"), Load<WorkerDefinition>(DataDir + "/Workers/Worker_Helper.asset"),
            Load<WorkerDefinition>(DataDir + "/Workers/Worker_Hauler.asset"), Load<WorkerDefinition>(DataDir + "/Workers/Worker_Runner.asset"),
        });
        var porterRoute = sites.Find("PorterRoute").GetComponent<PorterRoute>();
        SetArray(wm, "sites", new Object[] { porterRoute, sites.Find("DeliveryRoute").GetComponent<PorterRoute>(), haulRoute, runRoute });

        // ---------- Old Yard customers now walk in from the west (the east road leads to the market) ----------
        var yardQueue = gameplay.Find("Customers");
        yardQueue.Find("Entry/Spawn").position = new Vector3(-8f, 0f, 2.6f);
        _ = yardDesk;

        // ---------- NavMesh over Area 1 + Area 2 (plant content active while baking so machines carve holes) ----------
        var navT = environment.Find("NavMesh");
        var surface = navT.GetComponent<NavMeshSurface>();
        surface.center = new Vector3(38f, 1f, 26f);
        surface.size = new Vector3(80f, 6f, 34f);
        foreach (Transform c in content) c.gameObject.SetActive(true);
        surface.BuildNavMesh();
        foreach (Transform c in content) c.gameObject.SetActive(false);
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

    struct PlantRefs
    {
        public Machine sorter;
        public SellDesk market;
        public TransferPad ironPad, copperPad;
    }

    /// <summary>Everything that pops in when Gate 2 opens, in reveal order (floor first, then machines, then tiles).</summary>
    static PlantRefs BuildPlantContent(Transform content, TMP_FontAsset font, Material worldText)
    {
        var refs = new PlantRefs();
        Slab("Floor_Production", content, new Vector3(50.2f, 0.012f, 23.2f), new Vector3(14f, 0.024f, 16.6f), Load<Material>(MatDir + "/M_Ground_Concrete.mat"));
        Slab("Floor_Market", content, new Vector3(61f, 0.014f, 13.4f), new Vector3(12f, 0.028f, 6.2f), Load<Material>(MatDir + "/M_Ground_ConcreteSell.mat"));

        var iron = Load<ItemDefinition>(DataDir + "/Items/Item_Iron.asset");
        var copper = Load<ItemDefinition>(DataDir + "/Items/Item_Copper.asset");

        var sorterGo = Station("Sorter", content, new Vector3(50f, 0f, 27.4f), 0f);
        refs.sorter = sorterGo.GetComponent<Machine>();

        var binIron = Station("Storage", content, new Vector3(48.3f, 0f, 19.6f), -90f);
        binIron.name = "Bin_Iron";
        var binCopper = Station("Storage", content, new Vector3(51.7f, 0f, 19.6f), -90f);
        binCopper.name = "Bin_Copper";
        var bins = Load<StorageDefinition>(DataDir + "/Factory/Storage_Bins.asset");
        ConfigureBin(binIron, bins, "bin_iron", "IRON", iron, null, Lit("BinIron", new Color(0.46f, 0.5f, 0.58f), 0.4f, 0.3f),
            Lit("BinIronDark", new Color(0.32f, 0.35f, 0.42f), 0.4f, 0.3f));
        ConfigureBin(binCopper, bins, "bin_copper", "COPPER", copper, binIron.GetComponent<Storage>(), Lit("BinCopper", new Color(0.93f, 0.5f, 0.2f), 0.45f, 0.2f),
            Lit("BinCopperDark", new Color(0.7f, 0.34f, 0.12f), 0.45f, 0.2f));
        refs.ironPad = binIron.transform.Find("WithdrawPad").GetComponent<TransferPad>();
        refs.copperPad = binCopper.transform.Find("WithdrawPad").GetComponent<TransferPad>();

        var ironBelt = BuildConveyor(content, "Conveyor_Iron", sorterGo.transform.Find("IronOut").position.WithY(0f) + Vector3.back * 0.1f,
            new Vector3(48.3f, 0f, 21.15f), binIron.GetComponent<Storage>());
        var copperBelt = BuildConveyor(content, "Conveyor_Copper", sorterGo.transform.Find("CopperOut").position.WithY(0f) + Vector3.back * 0.1f,
            new Vector3(51.7f, 0f, 21.15f), binCopper.GetComponent<Storage>());
        SetStructArray(refs.sorter, "outputPorts", new[]
        {
            Lv(("item", iron), ("target", ironBelt), ("point", sorterGo.transform.Find("IronOut"))),
            Lv(("item", copper), ("target", copperBelt), ("point", sorterGo.transform.Find("CopperOut"))),
        });
        SetMany(refs.sorter, ("outputTarget", ironBelt));

        // Metal Market: the Sell Desk prefab in plant colours.
        var marketGo = Station("SellDesk", content, new Vector3(60f, 0f, 12.4f), 0f);
        marketGo.name = "MetalMarket";
        refs.market = marketGo.GetComponent<SellDesk>();
        SetMany(refs.market, ("definition", Load<SellDeskDefinition>(DataDir + "/Factory/SellDesk_Market.asset")), ("walkInDemand", false));
        SetArray(marketGo.transform.Find("CounterPile").GetComponent<ItemPile>(), "accepted", new Object[] { iron, copper });
        var green = Lit("AwningGreen", new Color(0.22f, 0.7f, 0.34f), 0.35f);
        var teal = Lit("MarketTeal", new Color(0.16f, 0.55f, 0.62f), 0.35f, 0.1f);
        foreach (var r in marketGo.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name.StartsWith("Awning_") && r.sharedMaterial != null && r.sharedMaterial.name.Contains("Red")) r.sharedMaterial = green;
            if (r.name is "Counter" or "Roof") r.sharedMaterial = teal;
        }

        var queue = BuildMarketQueue(content, refs.market);
        _ = queue;

        // Tiles inside the plant.
        PlaceTile(content, "Tile_Hauler", Load<GameObject>(PrefabDir + "/Tiles/Tile_Purchase.prefab"), new Vector3(44.4f, 0f, 21.6f), "hauler", "tile/hauler");
        PlaceTile(content, "Tile_Runner", Load<GameObject>(PrefabDir + "/Tiles/Tile_Purchase.prefab"), new Vector3(56.4f, 0f, 16.4f), "runner", "tile/runner");
        PlaceTile(content, "Tile_UpgradesPlant", Load<GameObject>(PrefabDir + "/Tiles/Tile_Upgrade.prefab"), new Vector3(56.4f, 0f, 22.6f), null, "tile/upgrades_plant");
        PlaceBoost(content, "Tile_BoostSorter", new Vector3(54.4f, 0f, 27.6f), refs.sorter);
        return refs;
    }

    /// <summary>Card icons from the recoloured plant instances, so they don't look like the Old Yard storage and desk.</summary>
    static void PlantIcons(Transform content)
    {
        Sprite Render(string name, string source, Vector3 euler, params string[] hide)
        {
            var copy = Object.Instantiate(content.Find(source).gameObject);
            copy.SetActive(true);
            foreach (var h in hide)
            {
                var t = FindDeep(copy.transform, h);
                if (t != null) t.gameObject.SetActive(false);
            }

            return RenderIcon(name, copy, euler);
        }

        SetMany(Load<StorageDefinition>(DataDir + "/Factory/Storage_Bins.asset"),
            ("icon", Render("Icon_Bins", "Bin_Copper", new Vector3(40f, 55f, 0f), "WithdrawPad", "StationLabel", "LevelVisuals")));
        SetMany(Load<SellDeskDefinition>(DataDir + "/Factory/SellDesk_Market.asset"),
            ("icon", Render("Icon_Market", "MetalMarket", new Vector3(25f, 160f, 0f), "StockPad", "CashPad", "StationLabel", "LevelVisuals", "CashPile")));
    }

    static void ConfigureBin(GameObject bin, StorageDefinition def, string stationId, string title, ItemDefinition item, Storage leader, Material wall, Material wallDark)
    {
        var storage = bin.GetComponent<Storage>();
        SetMany(storage, ("definition", def), ("stationId", stationId), ("labelTitle", title), ("levelSource", leader));
        SetArray(bin.transform.Find("Pile").GetComponent<ItemPile>(), "accepted", new Object[] { item });
        // Recolour every blue part, including the taller walls the level visuals add.
        foreach (var r in bin.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                if (mats[i].name == "M_BinBlue") mats[i] = wall;
                else if (mats[i].name == "M_BinBlueDark") mats[i] = wallDark;
            }

            r.sharedMaterials = mats;
        }

        // Neighbouring bins: a smaller label straight above each bin so the two never overlap.
        var label = bin.GetComponentInChildren<StationLabel>(true);
        if (label != null)
        {
            label.transform.position = bin.transform.position + new Vector3(0f, 3.0f, 1.2f);
            label.transform.localScale = Vector3.one * 0.95f;
        }
    }

    static CustomerQueue BuildMarketQueue(Transform content, SellDesk market)
    {
        var root = ResetChild(content, "Customers_Market");
        var queue = root.gameObject.AddComponent<CustomerQueue>();
        var slotRoot = Empty("Slots", root, Vector3.zero);
        var slots = new List<Object>();
        for (int i = 0; i < 7; i++) slots.Add(Empty($"Slot_{i}", slotRoot, new Vector3(60f, 0f, 10.9f - i * 1.2f)));
        var entryRoot = Empty("Entry", root, Vector3.zero);
        var entry = new Object[] { Empty("Spawn", entryRoot, new Vector3(88f, 0f, 2.6f)), Empty("LineBack", entryRoot, new Vector3(60f, 0f, 2.6f)) };
        var exitRoot = Empty("Exit", root, Vector3.zero);
        var exit = new Object[]
        {
            Empty("StepAside", exitRoot, new Vector3(61.7f, 0f, 10f)), Empty("Road", exitRoot, new Vector3(61.7f, 0f, 1.2f)),
            Empty("Away", exitRoot, new Vector3(88f, 0f, 1.2f))
        };
        var crowd = Empty("Crowd", root, Vector3.zero);
        SetMany(queue, ("desk", market), ("config", Load<CustomerConfig>(DataDir + "/Customers/CustomerConfig_Market.asset")), ("customersRoot", crowd),
            ("cheerDuration", 0.45f));
        SetArray(queue, "slots", slots.ToArray());
        SetArray(queue, "entryPath", entry);
        SetArray(queue, "exitPath", exit);
        return queue;
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

    static void BuildPlantBoundary(Transform environment)
    {
        var fences = environment.Find("Fences");
        foreach (var n in new[] { "Wall_40_42_76_42", "Wall_76_42_76_11", "Wall_40_38_40_42", "Fence_40_58", "Fence_62_76", "Market_SideW", "Market_SideE" })
        {
            var old = fences.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        CorrugatedWall(fences, "Wall_40_42_76_42", new Vector3(40f, 0f, 42f), new Vector3(76f, 0f, 42f));
        CorrugatedWall(fences, "Wall_76_42_76_11", new Vector3(76f, 0f, 42f), new Vector3(76f, 0f, 10.5f));
        CorrugatedWall(fences, "Wall_40_38_40_42", new Vector3(40f, 0f, 38f), new Vector3(40f, 0f, 42f));
        FrameFence(fences, "Fence_40_58", new Vector3(40f, 0f, 10.5f), new Vector3(58f, 0f, 10.5f));
        FrameFence(fences, "Fence_62_76", new Vector3(62f, 0f, 10.5f), new Vector3(76f, 0f, 10.5f));
        Blocker(fences, "Market_SideW", new Vector3(58f, 1f, 11.2f), new Vector3(0.4f, 2f, 1.4f));
        Blocker(fences, "Market_SideE", new Vector3(62f, 1f, 11.2f), new Vector3(0.4f, 2f, 1.4f));
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

    static void FrameFence(Transform parent, string name, Vector3 from, Vector3 to)
    {
        var segment = new GameObject(name).transform;
        segment.SetParent(parent, false);
        Vector3 delta = to - from;
        float length = delta.magnitude;
        segment.position = from;
        segment.rotation = Quaternion.LookRotation(delta.normalized) * Quaternion.Euler(0f, -90f, 0f);
        var post = Load<Material>(MatDir + "/M_FencePost.mat");
        var rail = Load<Material>(MatDir + "/M_FenceRail.mat");
        int posts = Mathf.Max(1, Mathf.RoundToInt(length / 2f));
        for (int i = 0; i <= posts; i++)
            Box("Post", segment, new Vector3(length * i / posts, 0.5f, 0f), new Vector3(0.16f, 1.0f, 0.16f), post, 0.02f);
        Box("RailTop", segment, new Vector3(length * 0.5f, 0.9f, 0f), new Vector3(length, 0.1f, 0.08f), rail, 0.02f);
        Box("RailMid", segment, new Vector3(length * 0.5f, 0.5f, 0f), new Vector3(length, 0.08f, 0.06f), rail, 0.02f);
        var box = segment.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(length * 0.5f, 1f, 0f);
        box.size = new Vector3(length, 2f, 0.4f);
    }

    static void Blocker(Transform parent, string name, Vector3 worldCenter, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = worldCenter;
        go.AddComponent<BoxCollider>().size = size;
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

    // =====================================================================================
    // Helpers: M4 shapes
    // =====================================================================================

    static float Frame(int x, int y, int size, float r, int thickness)
    {
        // Rounded-rect outline: the outer shape minus the same shape inset by the thickness.
        float outer = Mathf.Clamp01(RoundRect(x, y, size, size, r));
        int t = thickness;
        bool inInner = x >= t && y >= t && x < size - t && y < size - t;
        float inner = inInner ? Mathf.Clamp01(RoundRect(x - t, y - t, size - 2 * t, size - 2 * t, r - t)) : 0f;
        return outer * (1f - inner);
    }

    static float Tail(int x, int y, int size)
    {
        // Downward-pointing triangle (speech bubble tail).
        float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
        float half = v * 0.5f;
        return Mathf.Abs(u - 0.5f) < half ? 1f : 0f;
    }

    static float Check(int x, int y, int size)
    {
        float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
        float a = SegmentDistance(u, v, 0.2f, 0.52f, 0.42f, 0.3f);
        float b = SegmentDistance(u, v, 0.42f, 0.3f, 0.82f, 0.72f);
        return Mathf.Clamp01((0.085f - Mathf.Min(a, b)) * size);
    }

    static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
    {
        float vx = bx - ax, vy = by - ay;
        float t = Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy));
        float dx = px - (ax + vx * t), dy = py - (ay + vy * t);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    // =====================================================================================
    // Helpers: scrap (same conventions as M2_Build)
    // =====================================================================================

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

    // =====================================================================================
    // Helpers: scene / UI
    // =====================================================================================

    static Transform Root(Scene scene, string name) => scene.GetRootGameObjects().First(g => g.name == name).transform;

    static Transform FindOrCreate(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t;
        t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static T SystemObject<T>(Transform systems) where T : Component
    {
        var t = FindOrCreate(systems, typeof(T).Name);
        return t.TryGetComponent(out T c) ? c : t.gameObject.AddComponent<T>();
    }

    static RectTransform ResetUI(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        return Rect(name, parent);
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static RectTransform Img(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
    {
        var rt = Rect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.preserveAspect = sprite != null && sprite.border == Vector4.zero;
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

    static Sprite UiShape(string name) => Load<Sprite>($"{UiDir}/{name}.png");

    static Sprite UiSprite(string sheet, string spriteName) =>
        AssetDatabase.LoadAllAssetsAtPath(sheet).OfType<Sprite>().FirstOrDefault(s => s.name == spriteName);

    // =====================================================================================
    // Helpers: prefabs
    // =====================================================================================

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

    static void Anchor(Transform t, string id)
    {
        if (t == null)
        {
            Log.AppendLine("!! anchor target missing for " + id);
            return;
        }

        if (!t.TryGetComponent(out GuideAnchor anchor)) anchor = t.gameObject.AddComponent<GuideAnchor>();
        SetMany(anchor, ("id", id));
    }

    static Transform ResetChild(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
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

    // =====================================================================================
    // Helpers: procedural sprites
    // =====================================================================================

    static void ShapeSprite(string name, int w, int h, Func<int, int, float> alpha, Vector4 border, Color? tint = null)
    {
        string path = $"{UiDir}/{name}.png";
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var c = tint ?? Color.white;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            tex.SetPixel(x, y, new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha(x, y))));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.spriteBorder = border;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
    }

    static float RoundRect(int x, int y, int w, int h, float r)
    {
        float px = x + 0.5f, py = y + 0.5f;
        float dx = Mathf.Max(r - px, 0f, px - (w - r));
        float dy = Mathf.Max(r - py, 0f, py - (h - r));
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        return r - d + 0.5f;
    }

    static float Circle(int x, int y, int size, float r)
    {
        float c = size * 0.5f;
        float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
        return r - d + 0.5f;
    }

    static float Star(int x, int y, int size)
    {
        float c = size * 0.5f;
        float px = x + 0.5f - c, py = y + 0.5f - c;
        float angle = Mathf.Atan2(py, px) + Mathf.PI / 2f;
        float r = Mathf.Sqrt(px * px + py * py);
        float sector = Mathf.Repeat(angle, Mathf.PI * 2f / 5f) / (Mathf.PI * 2f / 5f);
        float outer = size * 0.47f, inner = size * 0.21f;
        float edge = Mathf.Lerp(inner, outer, 1f - Mathf.Abs(sector - 0.5f) * 2f);
        return edge - r + 0.5f;
    }

    static float Arrow(int x, int y, int size)
    {
        // Upward-pointing triangle with a short shaft.
        float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
        bool head = v > 0.42f && v < 0.95f && Mathf.Abs(u - 0.5f) < (0.95f - v) * 0.85f;
        bool shaft = v > 0.08f && v <= 0.45f && Mathf.Abs(u - 0.5f) < 0.14f;
        return head || shaft ? 1f : 0f;
    }

    static float Padlock(int x, int y, int size)
    {
        float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
        bool body = u > 0.2f && u < 0.8f && v > 0.08f && v < 0.55f;
        float dx = u - 0.5f, dy = v - 0.55f;
        float r = Mathf.Sqrt(dx * dx + dy * dy);
        bool shackle = dy > 0f && r > 0.16f && r < 0.25f;
        bool hole = Mathf.Abs(u - 0.5f) < 0.05f && v > 0.2f && v < 0.38f;
        return (body && !hole) || shackle ? 1f : 0f;
    }

    static Texture2D RingTexture()
    {
        string path = TexDir + "/T_Ring.png";
        if (!File.Exists(path))
        {
            const int s = 256;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - s / 2f) * (x + 0.5f - s / 2f) + (y + 0.5f - s / 2f) * (y + 0.5f - s / 2f)) / (s / 2f);
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.12f);
                a = Mathf.Max(a, d < 0.8f ? 0.18f * (d / 0.8f) : 0f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        return Load<Texture2D>(path);
    }

    // =====================================================================================
    // Helpers: geometry / assets (same conventions as M2_Build)
    // =====================================================================================

    static GameObject BuildBurst(string name, Material confetti, Material star, short count, float scale)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f * scale, 9f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var colors = new Gradient();
        colors.SetKeys(new[]
        {
            new GradientColorKey(new Color(1f, 0.85f, 0.2f), 0f), new GradientColorKey(new Color(0.3f, 0.85f, 1f), 0.33f),
            new GradientColorKey(new Color(1f, 0.35f, 0.4f), 0.66f), new GradientColorKey(new Color(0.5f, 1f, 0.4f), 1f)
        }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(colors) { mode = ParticleSystemGradientMode.RandomColor };
        main.gravityModifier = 1.4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 128;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 40f;
        shape.radius = 0.3f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = confetti;
        r.shadowCastingMode = ShadowCastingMode.Off;

        var stars = new GameObject("Stars");
        stars.transform.SetParent(go.transform, false);
        var sps = stars.AddComponent<ParticleSystem>();
        sps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var sm = sps.main;
        sm.duration = 0.5f;
        sm.loop = false;
        sm.playOnAwake = false;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        sm.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        sm.startColor = new Color(1f, 0.95f, 0.6f);
        sm.simulationSpace = ParticleSystemSimulationSpace.World;
        sm.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var se = sps.emission;
        se.rateOverTime = 0f;
        se.SetBursts(new[] { new ParticleSystem.Burst(0f, 10) });
        var ss = sps.shape;
        ss.shapeType = ParticleSystemShapeType.Sphere;
        ss.radius = 0.6f;
        var sol = sps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
        var sr = stars.GetComponent<ParticleSystemRenderer>();
        sr.sharedMaterial = star;
        sr.shadowCastingMode = ShadowCastingMode.Off;
        return go;
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

    static SfxDefinition Sfx(string name, ProceduralSfxPreset preset, float volume, Vector2 pitch, float interval)
    {
        var sfx = LoadOrCreate<SfxDefinition>($"{DataDir}/Audio/Sfx_{name}.asset");
        SetMany(sfx, ("fallback", (int)preset), ("volume", volume), ("pitchRange", pitch), ("minInterval", interval));
        return sfx;
    }

    static Texture2D LoadTexture(string path) => Load<Texture2D>(path);

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
}

static class M5VectorExtensions
{
    public static Vector3 WithY(this Vector3 v, float y) => new(v.x, y, v.z);
}

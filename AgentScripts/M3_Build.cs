using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Factory;
using ScrapYardKing.Feedback;
using ScrapYardKing.Harvest;
using ScrapYardKing.Items;
using ScrapYardKing.Player;
using ScrapYardKing.Progression;
using ScrapYardKing.UI;
using ScrapYardKing.Workers;
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

// Milestone 3 builder: upgrades, tasks, XP, guide, Porter. Entry points (in order): Assets, Prefabs, Scene.
// Incremental by design: it edits existing prefabs and the existing scene (find-or-create by name) instead of
// regenerating them, so hand edits outside the objects it owns survive.
public static class M3_Build
{
    const string P = "Assets/_Project";
    const string MatDir = P + "/Art/Materials";
    const string TexDir = P + "/Art/Textures";
    const string UiDir = P + "/Art/UI";
    const string IconDir = P + "/Art/Icons";
    const string AnimDir = P + "/Art/Animation";
    const string FontDir = P + "/Art/Fonts";
    const string DataDir = P + "/Data";
    const string PrefabDir = P + "/Prefabs";
    const string K = "Assets/ThirdParty/Kenney";
    const string UiSheet1 = "Assets/300Mind/2D Game UI Kit/Sprites/UI-pack_Sprite_1.png";
    const string UiSheet2 = "Assets/300Mind/2D Game UI Kit/Sprites/UI-pack_Sprite_2.png";
    const string ScenePath = P + "/Scenes/Area1_OldScrapYard.unity";

    static readonly StringBuilder Log = new();

    // =====================================================================================
    // ASSETS
    // =====================================================================================

    public static string Assets()
    {
        Log.Clear();
        foreach (var d in new[] { UiDir, IconDir, DataDir + "/Progression", DataDir + "/Progression/Tasks", DataDir + "/Workers", PrefabDir + "/Workers", PrefabDir + "/UI" })
            Directory.CreateDirectory(d);
        AssetDatabase.Refresh();

        // UI shapes (white, tinted per use).
        ShapeSprite("UI_Round", 128, 128, (x, y) => RoundRect(x, y, 128, 128, 44f), new Vector4(48, 48, 48, 48));
        ShapeSprite("UI_RoundSmall", 64, 64, (x, y) => RoundRect(x, y, 64, 64, 18f), new Vector4(20, 20, 20, 20));
        ShapeSprite("UI_Circle", 256, 256, (x, y) => Circle(x, y, 256, 124f), Vector4.zero);
        ShapeSprite("UI_Star", 256, 256, (x, y) => Star(x, y, 256), Vector4.zero, new Color(1f, 0.82f, 0.2f));
        ShapeSprite("UI_Arrow", 128, 128, (x, y) => Arrow(x, y, 128), Vector4.zero);
        ShapeSprite("UI_Lock", 128, 128, (x, y) => Padlock(x, y, 128), Vector4.zero);

        // SFX.
        Sfx("Upgrade", ProceduralSfxPreset.Upgrade, 0.55f, new Vector2(1f, 1f), 0.05f);
        Sfx("LevelUp", ProceduralSfxPreset.Fanfare, 0.6f, new Vector2(1f, 1f), 0.2f);
        Sfx("Denied", ProceduralSfxPreset.Denied, 0.4f, new Vector2(1f, 1.05f), 0.15f);
        Sfx("Hire", ProceduralSfxPreset.Fanfare, 0.5f, new Vector2(1.1f, 1.1f), 0.2f);

        // VFX.
        var star = LoadTexture($"{K}/ParticlePack/star_04.png");
        var mConfetti = ParticleMaterial("Confetti", Load<Texture2D>(TexDir + "/T_SoftCircle.png"), Color.white, false);
        var mStar = ParticleMaterial("Star", star, new Color(2f, 1.8f, 1f, 1f), true);
        SavePrefab(BuildBurst("VFX_UpgradeBurst", mConfetti, mStar, 40, 1.2f), PrefabDir + "/VFX/VFX_UpgradeBurst.prefab");
        SavePrefab(BuildBurst("VFX_HirePoof", mConfetti, mStar, 28, 1f), PrefabDir + "/VFX/VFX_HirePoof.prefab");

        // Data.
        var progression = LoadOrCreate<ProgressionConfig>(DataDir + "/Progression/ProgressionConfig.asset");
        // Paced for blueprint "Yard Lv.10 at ~90 min": XP from breaking scrap, sales, upgrades, hires and tasks.
        SetMany(progression, ("baseXp", 60), ("growth", 1.55f), ("maxLevel", 30), ("xpPerItemProcessed", 0), ("xpPerSale", 2),
            ("xpPerUpgrade", 8), ("xpPerWorkerHired", 20), ("cashPerLevel", 25), ("levelUpSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_LevelUp.asset")));

        StatUpgrade("chainsaw", "Chainsaw", PlayerStat.CutPower, StatModifierType.Flat, new[] { 0f, 5, 10, 15, 20, 25, 30, 35, 40 },
            new long[] { 60, 150, 300, 550, 900, 1400, 2200, 3500 }, "{0:0} → {1:0} power");
        StatUpgrade("backpack", "Backpack", PlayerStat.CarryCapacity, StatModifierType.Flat, new[] { 0f, 2, 4, 6, 8, 10, 12, 14, 17 },
            new long[] { 80, 180, 350, 600, 1000, 1600, 2500, 3800 }, "{0:0} → {1:0} carry");
        StatUpgrade("boots", "Boots", PlayerStat.MoveSpeed, StatModifierType.PercentAdd, new[] { 0f, 0.08f, 0.16f, 0.24f, 0.32f, 0.4f },
            new long[] { 120, 300, 650, 1200, 2200 }, "{0:0.0} → {1:0.0} speed");

        // Sell desk gets two more levels for the 90-minute arc.
        var desk = Load<SellDeskDefinition>(DataDir + "/Factory/SellDesk_Yard.asset");
        SetStructArray(desk, "levels", new[]
        {
            Lv(("saleInterval", 8f), ("unitsPerSale", new Vector2Int(2, 4)), ("priceMultiplier", 1f), ("counterCapacity", 12), ("queueCapacity", 3), ("upgradeCost", 0)),
            Lv(("saleInterval", 6.5f), ("unitsPerSale", new Vector2Int(2, 5)), ("priceMultiplier", 1.1f), ("counterCapacity", 16), ("queueCapacity", 4), ("upgradeCost", 250)),
            Lv(("saleInterval", 5f), ("unitsPerSale", new Vector2Int(3, 6)), ("priceMultiplier", 1.25f), ("counterCapacity", 20), ("queueCapacity", 5), ("upgradeCost", 700)),
            Lv(("saleInterval", 4f), ("unitsPerSale", new Vector2Int(3, 7)), ("priceMultiplier", 1.4f), ("counterCapacity", 24), ("queueCapacity", 6), ("upgradeCost", 1500)),
            Lv(("saleInterval", 3.2f), ("unitsPerSale", new Vector2Int(4, 8)), ("priceMultiplier", 1.6f), ("counterCapacity", 30), ("queueCapacity", 7), ("upgradeCost", 3000)),
        });

        var porter = LoadOrCreate<WorkerDefinition>(DataDir + "/Workers/Worker_Porter.asset");
        SetMany(porter, ("id", "porter"), ("displayName", "Porter"), ("role", (int)WorkerRole.Porter), ("moveSpeed", 3.6f), ("carryCapacity", 6),
            ("efficiency", 0.8f), ("pickupRadius", 1.6f));
        SetLongArray(porter, "hireCosts", new long[] { 500, 1500 });

        var catalog = LoadOrCreate<UpgradeCatalog>(DataDir + "/Progression/UpgradeCatalog.asset");
        SetStructArray(catalog, "entries", new[]
        {
            Lv(("upgradeId", "chainsaw"), ("unlockLevel", 1)),
            Lv(("upgradeId", "crusher"), ("unlockLevel", 1)),
            Lv(("upgradeId", "sell_desk"), ("unlockLevel", 1)),
            Lv(("upgradeId", "backpack"), ("unlockLevel", 2)),
            Lv(("upgradeId", "porter"), ("unlockLevel", 3)),
            Lv(("upgradeId", "storage_yard"), ("unlockLevel", 3)),
            Lv(("upgradeId", "boots"), ("unlockLevel", 4)),
        });

        SetMany(Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_CarWreck.asset"), ("xpReward", 6));
        SetMany(Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_Barrel.asset"), ("xpReward", 1));
        SetMany(Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_TireStack.asset"), ("xpReward", 1));

        BuildTasks();
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    static void BuildTasks()
    {
        string dir = DataDir + "/Progression/Tasks";
        var main = new List<Object>
        {
            Task(dir, "t01_cut_car", "Cut the car", TaskCategory.Main, TaskType.BreakScrap, "car_wreck", 1, 0, 20),
            Task(dir, "t02_collect", "Collect {0} scrap", TaskCategory.Main, TaskType.CollectItems, "scrap", 6, 0, 15),
            Task(dir, "t03_crush", "Crush {0} scrap", TaskCategory.Main, TaskType.ProcessItems, "crusher", 8, 0, 20),
            Task(dir, "t04_stock", "Stock the sell desk", TaskCategory.Main, TaskType.DeliverItems, "sell_desk", 6, 0, 20),
            Task(dir, "t05_first_sale", "Make your first sale", TaskCategory.Main, TaskType.SellItems, "", 1, 20, 20),
            Task(dir, "t06_cash", "Collect ${0}", TaskCategory.Main, TaskType.EarnCash, "", 40, 0, 20),
            Task(dir, "t07_chainsaw", "Upgrade the chainsaw", TaskCategory.Main, TaskType.PurchaseUpgrade, "chainsaw", 1, 0, 30),
            Task(dir, "t08_crush30", "Crush {0} scrap", TaskCategory.Main, TaskType.ProcessItems, "crusher", 30, 50, 40),
            Task(dir, "t09_crusher2", "Crusher to Lv.{0}", TaskCategory.Main, TaskType.ReachUpgradeLevel, "crusher", 2, 0, 40),
            Task(dir, "t10_sell40", "Sell {0} metal", TaskCategory.Main, TaskType.SellItems, "", 40, 80, 50),
            Task(dir, "t11_desk2", "Sell desk to Lv.{0}", TaskCategory.Main, TaskType.ReachUpgradeLevel, "sell_desk", 2, 0, 50),
            Task(dir, "t12_backpack", "Upgrade the backpack", TaskCategory.Main, TaskType.ReachUpgradeLevel, "backpack", 2, 0, 40),
            Task(dir, "t13_porter", "Hire a porter", TaskCategory.Main, TaskType.HireWorker, "porter", 1, 0, 60),
            Task(dir, "t14_level5", "Reach yard Lv.{0}", TaskCategory.Main, TaskType.ReachLevel, "", 5, 150, 0),
            Task(dir, "t15_crush150", "Crush {0} scrap", TaskCategory.Main, TaskType.ProcessItems, "crusher", 150, 200, 80),
        };
        var side = new List<Object>
        {
            Task(dir, "s01_barrels", "Cut {0} barrels", TaskCategory.Side, TaskType.BreakScrap, "barrel", 5, 40, 20),
            Task(dir, "s02_tires", "Cut {0} tire stacks", TaskCategory.Side, TaskType.BreakScrap, "tire_stack", 4, 40, 20),
            Task(dir, "s03_cars", "Cut {0} cars", TaskCategory.Side, TaskType.BreakScrap, "car_wreck", 3, 90, 40),
            Task(dir, "s04_earn", "Earn ${0}", TaskCategory.Side, TaskType.EarnCash, "", 300, 60, 30),
            Task(dir, "s05_collect", "Collect {0} scrap", TaskCategory.Side, TaskType.CollectItems, "scrap", 60, 50, 30),
        };

        var chain = LoadOrCreate<TaskChain>(DataDir + "/Progression/TaskChain_Area1.asset");
        SetArray(chain, "mainTasks", main.ToArray());
        SetArray(chain, "sideTasks", side.ToArray());
        SetMany(chain, ("sideTasksFromMainIndex", 7), ("maxActiveSideTasks", 1));
    }

    static TaskDefinition Task(string dir, string id, string title, TaskCategory category, TaskType type, string target, long amount, long cash, int xp)
    {
        var t = LoadOrCreate<TaskDefinition>($"{dir}/Task_{id}.asset");
        SetMany(t, ("id", id), ("title", title), ("category", (int)category), ("type", (int)type), ("targetId", target), ("amount", amount),
            ("rewardCash", cash), ("rewardXp", xp), ("rewardPremium", 0));
        return t;
    }

    static void StatUpgrade(string id, string name, PlayerStat stat, StatModifierType type, float[] totals, long[] costs, string format)
    {
        var u = LoadOrCreate<PlayerStatUpgradeDefinition>($"{DataDir}/Progression/Upgrade_{name}.asset");
        SetMany(u, ("id", id), ("displayName", name), ("stat", (int)stat), ("modifierType", (int)type), ("effectFormat", format));
        var so = new SerializedObject(u);
        var t = so.FindProperty("totalModifierPerLevel");
        t.arraySize = totals.Length;
        for (int i = 0; i < totals.Length; i++) t.GetArrayElementAtIndex(i).floatValue = totals[i];
        var c = so.FindProperty("costs");
        c.arraySize = costs.Length;
        for (int i = 0; i < costs.Length; i++) c.GetArrayElementAtIndex(i).longValue = costs[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // =====================================================================================
    // PREFABS
    // =====================================================================================

    public static string Prefabs()
    {
        Log.Clear();
        var font = Load<TMP_FontAsset>(FontDir + "/GROBOLD SDF.asset");
        var worldText = Load<Material>(FontDir + "/GROBOLD World.mat");
        var mSteelDark = Lit("SteelDark", new Color(0.2f, 0.21f, 0.24f), 0.45f, 0.7f);
        var mSteel = Lit("SteelMid", new Color(0.45f, 0.47f, 0.52f), 0.5f, 0.75f);
        var mGold = Lit("Gold", new Color(1f, 0.78f, 0.25f), 0.7f, 0.9f);
        var mHazard = Lit("HazardYellow", new Color(1f, 0.78f, 0.1f), 0.3f);
        var mBinBlue = Lit("BinBlue", new Color(0.18f, 0.45f, 0.8f), 0.35f, 0.3f);
        var mFlagRed = Lit("AwningRed", new Color(0.9f, 0.25f, 0.2f), 0.3f);
        var mWhite = Lit("OffWhite", new Color(0.95f, 0.93f, 0.88f), 0.3f);
        var mHatOrange = Lit("HardHatOrange", new Color(1f, 0.55f, 0.12f), 0.5f);
        var mGuide = Emissive("GuideYellow", new Color(1f, 0.85f, 0.2f), 1.4f);
        var mGuideRing = TransparentUnlit("GuideRing", new Color(1f, 0.85f, 0.2f, 0.55f));
        var factory = KenneyMaterial("FactoryKit");

        // ---------- Worker: Porter ----------
        var workerController = BuildWorkerController(K + "/MiniCharacters/character-male-e.fbx", AnimDir + "/AC_Worker.controller");
        var porterDef = Load<WorkerDefinition>(DataDir + "/Workers/Worker_Porter.asset");
        var scrapItem = Load<ItemDefinition>(DataDir + "/Items/Item_Scrap.asset");
        var porterPrefab = SavePrefab(BuildPorter(workerController, mHatOrange, mSteelDark, scrapItem), PrefabDir + "/Workers/Worker_Porter.prefab");
        SetMany(porterDef, ("prefab", porterPrefab.GetComponent<Worker>()));

        // ---------- Guide marker ----------
        SavePrefab(BuildGuideMarker(mGuide, mGuideRing), PrefabDir + "/UI/GuideMarker.prefab");

        // ---------- Player: upgrades + chainsaw tiers ----------
        EditPrefab(PrefabDir + "/Player/Player.prefab", root =>
        {
            if (!root.TryGetComponent(out PlayerUpgrades upgrades)) upgrades = root.AddComponent<PlayerUpgrades>();
            SetMany(upgrades, ("stats", root.GetComponent<PlayerStats>()));
            SetArray(upgrades, "upgrades", new Object[]
            {
                Load<PlayerStatUpgradeDefinition>(DataDir + "/Progression/Upgrade_Chainsaw.asset"),
                Load<PlayerStatUpgradeDefinition>(DataDir + "/Progression/Upgrade_Backpack.asset"),
                Load<PlayerStatUpgradeDefinition>(DataDir + "/Progression/Upgrade_Boots.asset"),
            });

            var saw = FindDeep(root.transform, "Chainsaw");
            var motor = saw != null ? saw.Find("Motor") : null;
            if (motor != null)
            {
                if (!saw.TryGetComponent(out UpgradeVisualTiers tiers)) tiers = saw.gameObject.AddComponent<UpgradeVisualTiers>();
                SetMany(tiers, ("upgradeId", "chainsaw"), ("punchTarget", saw));
                SetArray(tiers, "renderers", new Object[] { motor.GetComponent<Renderer>() });
                SetStructArray(tiers, "tiers", new[]
                {
                    Lv(("minLevel", 1), ("material", Load<Material>(MatDir + "/M_SawOrange.mat"))),
                    Lv(("minLevel", 4), ("material", Lit("SawRed", new Color(0.9f, 0.15f, 0.12f), 0.5f, 0.2f))),
                    Lv(("minLevel", 7), ("material", mGold)),
                });
            }
        });

        // ---------- Stations: guide anchors + level visuals ----------
        EditPrefab(PrefabDir + "/Stations/Crusher.prefab", root =>
        {
            Anchor(root.transform.Find("InputPad"), "crusher/in");
            var body = root.transform.Find("Body");
            var lv = ResetChild(body, "LevelVisuals");
            var stack = new GameObject("Lv2_Exhaust").transform;
            stack.SetParent(lv, false);
            Cylinder("Pipe", stack, new Vector3(-0.95f, 2.6f, 1.0f), 0.17f, 1.5f, 12, mSteelDark);
            Cylinder("Cap", stack, new Vector3(-0.95f, 3.38f, 1.0f), 0.24f, 0.12f, 12, mSteel);
            var cog = Kenney("FactoryKit", "cog-b", lv, new Vector3(-1.4f, 0.95f, -0.3f), 1f, new Vector3(0f, 0f, 90f), factory);
            cog.name = "Lv3_Cog";
            var cb = RendererBounds(cog).size;
            cog.transform.localScale = Vector3.one * (0.95f / Mathf.Max(cb.x, cb.y, cb.z));
            var beacons = new GameObject("Lv4_Beacons").transform;
            beacons.SetParent(lv, false);
            foreach (var x in new[] { -1.25f, 1.25f })
            {
                var b = Kenney("FactoryKit", "warning-orange", beacons, new Vector3(x, 1.95f, -1.2f), 1f, Vector3.zero, factory);
                var bb = RendererBounds(b).size;
                b.transform.localScale = Vector3.one * (0.9f / Mathf.Max(bb.y, 0.01f));
            }
            var trim = Box("Lv5_GoldTrim", lv, new Vector3(0f, 1.84f, 0f), new Vector3(2.86f, 0.14f, 2.76f), mGold, 0.03f);
            var levelVisuals = SetupLevelVisuals(root, (2, stack.gameObject), (3, cog), (4, beacons.gameObject), (5, trim.gameObject));
            SetMany(root.GetComponent<Machine>(), ("levelVisuals", levelVisuals));
            var visuals = root.GetComponent<MachineVisuals>();
            var so = new SerializedObject(visuals);
            var spinners = so.FindProperty("spinners");
            bool has = false;
            for (int i = 0; i < spinners.arraySize; i++) has |= spinners.GetArrayElementAtIndex(i).objectReferenceValue == cog.transform;
            if (!has)
            {
                spinners.arraySize++;
                spinners.GetArrayElementAtIndex(spinners.arraySize - 1).objectReferenceValue = cog.transform;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        });

        EditPrefab(PrefabDir + "/Stations/Storage.prefab", root =>
        {
            Anchor(root.transform.Find("WithdrawPad"), "storage_yard/out");
            var lv = ResetChild(root.transform, "LevelVisuals");
            var walls = new GameObject("Lv2_TallWalls").transform;
            walls.SetParent(lv, false);
            Box("N", walls, new Vector3(0f, 1.0f, 1.42f), new Vector3(3.6f, 0.4f, 0.16f), mBinBlue, 0.03f);
            Box("S", walls, new Vector3(0f, 1.0f, -1.42f), new Vector3(3.6f, 0.4f, 0.16f), mBinBlue, 0.03f);
            Box("E", walls, new Vector3(1.72f, 1.0f, 0f), new Vector3(0.16f, 0.4f, 3.0f), mBinBlue, 0.03f);
            Box("W", walls, new Vector3(-1.72f, 1.0f, 0f), new Vector3(0.16f, 0.4f, 3.0f), mBinBlue, 0.03f);
            var lamps = new GameObject("Lv3_Lamps").transform;
            lamps.SetParent(lv, false);
            foreach (var x in new[] { -1.72f, 1.72f })
            {
                var b = Kenney("FactoryKit", "warning-orange", lamps, new Vector3(x, 1.1f, 1.42f), 1f, Vector3.zero, factory);
                var bb = RendererBounds(b).size;
                b.transform.localScale = Vector3.one * (0.9f / Mathf.Max(bb.y, 0.01f));
            }
            var levelVisuals = SetupLevelVisuals(root, (2, walls.gameObject), (3, lamps.gameObject));
            SetMany(root.GetComponent<Storage>(), ("levelVisuals", levelVisuals));
        });

        EditPrefab(PrefabDir + "/Stations/SellDesk.prefab", root =>
        {
            Anchor(root.transform.Find("StockPad"), "sell_desk/in");
            Anchor(root.transform.Find("CashPad"), "sell_desk/cash");
            var lv = ResetChild(root.transform, "LevelVisuals");
            var flags = new GameObject("Lv2_Flags").transform;
            flags.SetParent(lv, false);
            foreach (var x in new[] { -2.0f, 2.0f })
            {
                Cylinder("Pole", flags, new Vector3(x, 4.35f, 1.1f), 0.04f, 1.2f, 8, mSteelDark);
                var flag = ShapeGenerator.GeneratePrism(PivotLocation.Center, new Vector3(0.7f, 0.45f, 0.04f));
                FinishPb(flag, "Flag", flags, new Vector3(x + 0.35f, 4.7f, 1.1f), mFlagRed);
                flag.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            }
            var sign = new GameObject("Lv3_Sign").transform;
            sign.SetParent(lv, false);
            Box("Board", sign, new Vector3(0f, 4.15f, -0.55f), new Vector3(2.4f, 0.6f, 0.1f), mWhite, 0.03f);
            var text = Text3D("Text", sign, new Vector3(0f, 4.15f, -0.62f), font, worldText, 5f, new Color(0.95f, 0.3f, 0.2f), "SCRAP $");
            text.rectTransform.sizeDelta = new Vector2(2.4f, 0.6f);
            var trim = Box("Lv5_GoldTrim", lv, new Vector3(0f, 3.85f, 0.35f), new Vector3(4.5f, 0.08f, 2.0f), mGold, 0.02f);
            var levelVisuals = SetupLevelVisuals(root, (2, flags.gameObject), (3, sign.gameObject), (5, trim.gameObject));
            SetMany(root.GetComponent<SellDesk>(), ("levelVisuals", levelVisuals));
        });

        // Scrap carves the NavMesh so porters path around it.
        foreach (var path in Directory.GetFiles(PrefabDir + "/Scrap", "*.prefab"))
            EditPrefab(path.Replace('\\', '/'), root =>
            {
                var box = root.GetComponent<BoxCollider>();
                if (!root.TryGetComponent(out NavMeshObstacle obstacle)) obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = box.center;
                obstacle.size = box.size;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
            });

        Icons();

        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    /// <summary>Renders upgrade card icons from the real models and assigns them to the definitions.</summary>
    public static string Icons()
    {
        var porterDef = Load<WorkerDefinition>(DataDir + "/Workers/Worker_Porter.asset");
        var icons = new Dictionary<string, Sprite>
        {
            ["chainsaw"] = RenderIcon("Icon_Chainsaw", FindDeep(Load<GameObject>(PrefabDir + "/Player/Player.prefab").transform, "Chainsaw").gameObject, new Vector3(25f, -130f, 0f)),
            ["crusher"] = RenderIcon("Icon_Crusher", PrefabRoot("Stations/Crusher", "InputPad", "StationLabel", "LevelVisuals"), new Vector3(28f, -35f, 0f)),
            ["storage"] = RenderIcon("Icon_Storage", PrefabRoot("Stations/Storage", "WithdrawPad", "StationLabel", "LevelVisuals"), new Vector3(40f, -35f, 0f)),
            ["sell_desk"] = RenderIcon("Icon_SellDesk", PrefabRoot("Stations/SellDesk", "StockPad", "CashPad", "StationLabel", "LevelVisuals", "CashPile"), new Vector3(25f, 160f, 0f)),
            ["porter"] = RenderIcon("Icon_Porter", Load<GameObject>(PrefabDir + "/Workers/Worker_Porter.prefab"), new Vector3(15f, 160f, 0f), "idle"),
            ["boots"] = RenderIcon("Icon_Boots", BuildBootsIconModel(), new Vector3(22f, -150f, 0f)),
            ["backpack"] = RenderIcon("Icon_Backpack", BuildBackpackIconModel(), new Vector3(25f, 155f, 0f)),
        };
        SetMany(Load<PlayerStatUpgradeDefinition>(DataDir + "/Progression/Upgrade_Chainsaw.asset"), ("icon", icons["chainsaw"]));
        SetMany(Load<PlayerStatUpgradeDefinition>(DataDir + "/Progression/Upgrade_Backpack.asset"), ("icon", icons["backpack"]));
        SetMany(Load<PlayerStatUpgradeDefinition>(DataDir + "/Progression/Upgrade_Boots.asset"), ("icon", icons["boots"]));
        SetMany(Load<MachineDefinition>(DataDir + "/Factory/Machine_Crusher.asset"), ("icon", icons["crusher"]));
        SetMany(Load<StorageDefinition>(DataDir + "/Factory/Storage_Yard.asset"), ("icon", icons["storage"]));
        SetMany(Load<SellDeskDefinition>(DataDir + "/Factory/SellDesk_Yard.asset"), ("icon", icons["sell_desk"]));
        SetMany(porterDef, ("icon", icons["porter"]));

        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    static GameObject BuildPorter(AnimatorController controller, Material hat, Material steelDark, ItemDefinition scrap)
    {
        var root = new GameObject("Worker_Porter");
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
        var model = Kenney("MiniCharacters", "character-male-e", body, Vector3.zero, 2.4f, Vector3.zero, null);
        model.name = "Model";
        if (!model.TryGetComponent(out Animator animator)) animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        var clips = AssetDatabase.LoadAllAssetsAtPath(K + "/MiniCharacters/character-male-e.fbx").OfType<AnimationClip>().ToArray();
        clips.First(c => c.name == "idle").SampleAnimation(model, 0f);
        PlaceHardHat(root.transform, model.transform, hat);

        Box("CarryRack", body, new Vector3(0f, 0.95f, -0.3f), new Vector3(0.5f, 0.55f, 0.08f), steelDark, 0.02f);
        var stackAnchor = Empty("StackAnchor", root.transform, new Vector3(0f, 0.85f, -0.45f));

        var stack = root.AddComponent<CarryStack>();
        SetMany(stack, ("stackRoot", stackAnchor), ("capacity", 6));
        var collector = root.AddComponent<ItemCollector>();
        SetMany(collector, ("stack", stack), ("radius", 1.6f), ("playFeedback", false), ("pickupInterval", 0.06f));
        SetArray(collector, "onlyItems", new Object[] { scrap });
        var worker = root.AddComponent<Worker>();
        SetMany(worker, ("stack", stack), ("collector", collector), ("animator", animator));
        root.AddComponent<PorterBrain>();
        return root;
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

    static AnimatorController BuildWorkerController(string fbx, string path)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToDictionary(c => c.name);
        if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
        var ac = AnimatorController.CreateAnimatorControllerAtPath(path);
        ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
        var state = ac.CreateBlendTreeInController("Locomotion", out var tree, 0);
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(clips["idle"], 0f);
        tree.AddChild(clips["walk"], 0.5f);
        tree.AddChild(clips["sprint"], 1f);
        ac.layers[0].stateMachine.defaultState = state;
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        return ac;
    }

    static GameObject BuildGuideMarker(Material arrowMat, Material ringMat)
    {
        var root = new GameObject("GuideMarker");
        var visual = Empty("Visual", root.transform, Vector3.zero);
        var pivot = Empty("ArrowPivot", visual, Vector3.zero);
        var arrow = Empty("Arrow", pivot, new Vector3(0f, 2.4f, 0f));
        // Downward arrow: prism head pointing -Y plus a shaft, chunky so it reads at phone scale.
        var head = ShapeGenerator.GeneratePrism(PivotLocation.Center, new Vector3(0.9f, 0.7f, 0.3f));
        FinishPb(head, "Head", arrow, new Vector3(0f, -0.35f, 0f), arrowMat);
        head.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
        Box("Shaft", arrow, new Vector3(0f, 0.25f, 0f), new Vector3(0.36f, 0.6f, 0.3f), arrowMat, 0.04f);
        foreach (var r in arrow.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;

        var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ring.name = "Ring";
        Object.DestroyImmediate(ring.GetComponent<Collider>());
        ring.transform.SetParent(visual, false);
        ring.transform.localPosition = new Vector3(0f, 0.04f, 0f);
        ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ring.transform.localScale = new Vector3(2.4f, 2.4f, 1f);
        var ringMatTex = new Material(ringMat) { name = "M_GuideRingTex" };
        string ringPath = MatDir + "/M_GuideRingTex.mat";
        var existing = Load<Material>(ringPath, false);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(ringMatTex, ringPath);
            existing = ringMatTex;
        }

        existing.SetTexture("_BaseMap", RingTexture());
        ring.GetComponent<Renderer>().sharedMaterial = existing;
        ring.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        var marker = root.AddComponent<GuideMarker>();
        SetMany(marker, ("visual", visual), ("arrowPivot", pivot), ("arrow", arrow), ("ring", ring.transform), ("arrowHeight", 2.4f));
        return root;
    }

    static GameObject BuildBootsIconModel()
    {
        var root = new GameObject("BootsIcon");
        var leather = Lit("BootLeather", new Color(0.62f, 0.38f, 0.2f), 0.3f);
        var sole = Lit("RubberBlack", new Color(0.09f, 0.09f, 0.1f), 0.25f);
        var cap = Lit("HazardYellow", new Color(1f, 0.78f, 0.1f), 0.3f);
        foreach (var x in new[] { -0.22f, 0.22f })
        {
            var boot = new GameObject("Boot").transform;
            boot.SetParent(root.transform, false);
            boot.localPosition = new Vector3(x, 0f, x * 0.4f);
            Box("Sole", boot, new Vector3(0f, 0.04f, 0.06f), new Vector3(0.3f, 0.08f, 0.56f), sole, 0.02f);
            Box("Foot", boot, new Vector3(0f, 0.15f, 0.08f), new Vector3(0.28f, 0.16f, 0.5f), leather, 0.04f);
            Box("Shaft", boot, new Vector3(0f, 0.38f, -0.1f), new Vector3(0.28f, 0.42f, 0.26f), leather, 0.04f);
            Box("ToeCap", boot, new Vector3(0f, 0.15f, 0.3f), new Vector3(0.29f, 0.15f, 0.1f), cap, 0.03f);
            Box("Cuff", boot, new Vector3(0f, 0.6f, -0.1f), new Vector3(0.31f, 0.06f, 0.29f), cap, 0.02f);
        }

        return root;
    }

    static GameObject BuildBackpackIconModel()
    {
        var root = new GameObject("BackpackIcon");
        var rack = Box("Rack", root.transform, new Vector3(0f, 0.55f, -0.08f), new Vector3(0.55f, 1.1f, 0.08f), Load<Material>(MatDir + "/M_SteelDark.mat"), 0.02f);
        _ = rack;
        var scrapPrefab = Load<GameObject>(PrefabDir + "/Items/Item_ScrapPiece.prefab");
        var meshes = new[] { "Scrap_debris-plate-small-a", "Scrap_debris-nut", "Scrap_debris-plate-a", "Scrap_debris-bolt" };
        for (int i = 0; i < 4; i++)
        {
            var piece = (GameObject)PrefabUtility.InstantiatePrefab(scrapPrefab, root.transform);
            piece.transform.localPosition = new Vector3(0f, 0.15f + i * 0.27f, 0.1f);
            piece.transform.localRotation = Quaternion.Euler(0f, i * 23f, 0f);
            var mesh = Load<Mesh>($"{P}/Art/Meshes/{meshes[i]}.asset");
            if (mesh != null) piece.GetComponentInChildren<MeshFilter>().sharedMesh = mesh;
        }

        return root;
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
        var ui = Root(scene, "_UI");
        var font = Load<TMP_FontAsset>(FontDir + "/GROBOLD SDF.asset");
        var outline = Load<Material>(FontDir + "/GROBOLD Outline.mat");

        var crusher = gameplay.GetComponentsInChildren<Machine>(true).First();
        var desk = gameplay.GetComponentsInChildren<SellDesk>(true).First();

        // ---------- Gameplay objects ----------
        var workers = FindOrCreate(gameplay, "Workers");
        var spawn = FindOrCreate(workers, "WorkerSpawn");
        spawn.position = new Vector3(15f, 0f, 12.5f);
        spawn.rotation = Quaternion.Euler(0f, 0f, 0f);
        var sites = FindOrCreate(workers, "Sites");
        var routeT = FindOrCreate(sites, "PorterRoute");
        routeT.position = new Vector3(10f, 0f, 30f);
        if (!routeT.TryGetComponent(out PorterRoute route)) route = routeT.gameObject.AddComponent<PorterRoute>();
        var idle = FindOrCreate(routeT, "Idle");
        idle.position = new Vector3(17.5f, 0f, 24f);
        SetMany(route, ("idlePoint", idle), ("pickupCenter", routeT), ("pickupRadius", 9.5f), ("dropoff", crusher.transform.Find("InputPad").GetComponent<TransferPad>()));

        var markerT = gameplay.Find("GuideMarker");
        if (markerT == null)
        {
            markerT = ((GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PrefabDir + "/UI/GuideMarker.prefab"), gameplay)).transform;
            markerT.name = "GuideMarker";
        }

        // ---------- Systems ----------
        SetMany(SystemObject<ProgressionManager>(systems), ("config", Load<ProgressionConfig>(DataDir + "/Progression/ProgressionConfig.asset")));
        SetMany(SystemObject<UpgradeManager>(systems), ("catalog", Load<UpgradeCatalog>(DataDir + "/Progression/UpgradeCatalog.asset")),
            ("purchaseSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_Upgrade.asset")), ("deniedSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_Denied.asset")),
            ("purchaseVfx", Load<GameObject>(PrefabDir + "/VFX/VFX_UpgradeBurst.prefab").GetComponent<ParticleSystem>()));
        SetMany(SystemObject<TaskManager>(systems), ("chain", Load<TaskChain>(DataDir + "/Progression/TaskChain_Area1.asset")));
        SetMany(SystemObject<GuideDirector>(systems), ("marker", markerT.GetComponent<GuideMarker>()));
        var wm = SystemObject<WorkerManager>(systems);
        SetMany(wm, ("spawnPoint", spawn), ("workersRoot", workers), ("hireVfx", Load<GameObject>(PrefabDir + "/VFX/VFX_HirePoof.prefab").GetComponent<ParticleSystem>()),
            ("hireSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_Hire.asset")));
        SetArray(wm, "hireable", new Object[] { Load<WorkerDefinition>(DataDir + "/Workers/Worker_Porter.asset") });
        SetArray(wm, "sites", new Object[] { route });

        // ---------- NavMesh (Area 1 volume) ----------
        var navT = FindOrCreate(environment, "NavMesh");
        if (!navT.TryGetComponent(out NavMeshSurface surface)) surface = navT.gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Volume;
        surface.center = new Vector3(20f, 1f, 24f);
        surface.size = new Vector3(42f, 6f, 30f);
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = ~LayerMask.GetMask("Characters", "Scrap", "UI");
        navT.position = Vector3.zero;
        surface.BuildNavMesh();
        string navDir = Path.Combine(Path.GetDirectoryName(ScenePath), Path.GetFileNameWithoutExtension(ScenePath));
        Directory.CreateDirectory(navDir);
        string navPath = navDir.Replace('\\', '/') + "/NavMesh-Area1.asset";
        if (File.Exists(navPath)) AssetDatabase.DeleteAsset(navPath);
        AssetDatabase.CreateAsset(surface.navMeshData, navPath);
        EditorUtility.SetDirty(surface);
        Log.AppendLine("navmesh baked: " + navPath);

        // ---------- UI ----------
        var canvas = ui.Find("Canvas");
        var hud = canvas.Find("HUD");
        var joystickBase = canvas.Find("JoystickArea/Base");
        if (joystickBase != null) ((RectTransform)joystickBase).anchoredPosition = new Vector2(0f, -430f);
        LayoutTopBar(hud);
        BuildLevelBadge(hud, font, outline);
        BuildTaskBanner(hud, font, outline);
        // The bottom upgrade rail was replaced by the upgrade tile + panel in M4 (see M4_Build.Scene).
        BuildAnnouncer(hud, font, outline);
        BuildGuidePointer(hud);
        // Fly layer stays the top-most HUD layer so coins/stars render above cards and banners.
        hud.Find("FlyLayer")?.SetAsLastSibling();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("Saved " + ScenePath);
        return Log.ToString();
    }

    static void LayoutTopBar(Transform hud)
    {
        var cash = (RectTransform)hud.Find("CashPill");
        var gem = (RectTransform)hud.Find("GemPill");
        if (cash != null)
        {
            cash.anchoredPosition = new Vector2(-30f, -46f);
            cash.sizeDelta = new Vector2(370f, 92f);
        }

        if (gem != null)
        {
            gem.anchoredPosition = new Vector2(-436f, -46f);
            gem.sizeDelta = new Vector2(236f, 92f);
        }
    }

    static void BuildLevelBadge(Transform hud, TMP_FontAsset font, Material outline)
    {
        var root = ResetUI(hud, "LevelBadge");
        root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = new Vector2(28f, -36f);
        root.sizeDelta = new Vector2(380f, 120f);

        var barBg = Img("XpBar", root, UiShape("UI_Round"), new Color(0.07f, 0.12f, 0.3f, 0.9f), new Vector2(255f, 46f));
        barBg.anchorMin = barBg.anchorMax = new Vector2(0f, 0.5f);
        barBg.pivot = new Vector2(0f, 0.5f);
        barBg.anchoredPosition = new Vector2(92f, -8f);
        barBg.GetComponent<Image>().type = Image.Type.Sliced;
        barBg.GetComponent<Image>().pixelsPerUnitMultiplier = 3f;
        var fill = Img("Fill", barBg, UiShape("UI_RoundSmall"), new Color(0.55f, 0.95f, 0.3f), new Vector2(239f, 32f));
        fill.anchoredPosition = new Vector2(6f, 0f);
        var fillImg = fill.GetComponent<Image>();
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = 0;
        fillImg.fillAmount = 0.2f;
        var xp = Txt("XpText", barBg, font, outline, 24f, Color.white, TextAlignmentOptions.Center, "0/50");
        Stretch(xp.rectTransform);
        var caption = Txt("Caption", root, font, outline, 24f, new Color(1f, 0.85f, 0.3f), TextAlignmentOptions.Left, "YARD LEVEL");
        caption.rectTransform.anchorMin = caption.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        caption.rectTransform.pivot = new Vector2(0f, 0.5f);
        caption.rectTransform.anchoredPosition = new Vector2(112f, 30f);
        caption.rectTransform.sizeDelta = new Vector2(240f, 34f);

        var badge = Rect("Badge", root);
        badge.anchorMin = badge.anchorMax = new Vector2(0f, 0.5f);
        badge.anchoredPosition = new Vector2(60f, 0f);
        badge.sizeDelta = new Vector2(118f, 118f);
        Img("Star", badge, UiShape("UI_Star"), Color.white, new Vector2(150f, 150f));
        Img("Circle", badge, UiSprite(UiSheet1, "UI-pack_Sprite_1_8"), Color.white, new Vector2(112f, 112f));
        var level = Txt("Level", badge, font, outline, 58f, Color.white, TextAlignmentOptions.Center, "1");
        Stretch(level.rectTransform);

        var lb = root.gameObject.AddComponent<LevelBadge>();
        SetMany(lb, ("badge", badge), ("levelText", level), ("fill", fillImg), ("xpText", xp), ("flyer", hud.Find("FlyLayer").GetComponent<UIFlyer>()),
            ("xpIcon", UiShape("UI_Star")));
    }

    static void BuildTaskBanner(Transform hud, TMP_FontAsset font, Material outline)
    {
        var root = ResetUI(hud, "TaskBanner");
        Stretch(root);
        var panel = Rect("Panel", root);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
        panel.pivot = new Vector2(0.5f, 1f);
        panel.anchoredPosition = new Vector2(0f, -160f);
        panel.sizeDelta = new Vector2(820f, 112f);
        var shadow = Img("Shadow", panel, UiShape("UI_Round"), new Color(0f, 0f, 0f, 0.25f), Vector2.zero);
        Stretch(shadow);
        shadow.anchoredPosition = new Vector2(0f, -8f);
        shadow.GetComponent<Image>().type = Image.Type.Sliced;
        var panelBg = Img("Background", panel, UiShape("UI_Round"), new Color(1f, 1f, 1f, 0.96f), Vector2.zero);
        Stretch(panelBg);
        panelBg.GetComponent<Image>().type = Image.Type.Sliced;

        var plain = font.material;
        var title = Txt("Title", panel, font, plain, 40f, new Color(0.1f, 0.15f, 0.32f), TextAlignmentOptions.Left, "CUT THE CAR");
        title.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        title.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        title.rectTransform.offsetMin = new Vector2(30f, 0f);
        title.rectTransform.offsetMax = new Vector2(-150f, 50f);
        var barBg = Img("Bar", panel, UiShape("UI_Round"), new Color(0.85f, 0.87f, 0.92f), new Vector2(560f, 30f));
        barBg.GetComponent<Image>().type = Image.Type.Sliced;
        barBg.GetComponent<Image>().pixelsPerUnitMultiplier = 3f;
        barBg.anchorMin = barBg.anchorMax = new Vector2(0f, 0.5f);
        barBg.pivot = new Vector2(0f, 0.5f);
        barBg.anchoredPosition = new Vector2(30f, -26f);
        var fill = Img("Fill", barBg, UiShape("UI_RoundSmall"), new Color(0.35f, 0.8f, 0.3f), new Vector2(552f, 22f));
        var fillImg = fill.GetComponent<Image>();
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        var progress = Txt("Progress", panel, font, plain, 38f, new Color(0.1f, 0.15f, 0.32f), TextAlignmentOptions.Right, "0/1");
        progress.rectTransform.anchorMin = progress.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        progress.rectTransform.pivot = new Vector2(1f, 0.5f);
        progress.rectTransform.anchoredPosition = new Vector2(-36f, -22f);
        progress.rectTransform.sizeDelta = new Vector2(220f, 50f);
        var check = Img("Check", panel, UiSprite(UiSheet1, "UI-pack_Sprite_1_5"), Color.white, new Vector2(96f, 96f));
        check.anchorMin = check.anchorMax = new Vector2(1f, 0.5f);
        check.anchoredPosition = new Vector2(-70f, 16f);
        var reward = Txt("Reward", panel, font, outline, 34f, new Color(1f, 0.85f, 0.25f), TextAlignmentOptions.Center, "+$20");
        reward.rectTransform.anchorMin = reward.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        reward.rectTransform.anchoredPosition = new Vector2(0f, -30f);
        reward.rectTransform.sizeDelta = new Vector2(700f, 50f);
        var side = Txt("SideTask", root, font, outline, 26f, Color.white, TextAlignmentOptions.Center, "");
        side.rectTransform.anchorMin = side.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        side.rectTransform.anchoredPosition = new Vector2(0f, -310f);
        side.rectTransform.sizeDelta = new Vector2(980f, 44f);

        var banner = root.gameObject.AddComponent<TaskBanner>();
        SetMany(banner, ("panel", panel), ("title", title), ("progressText", progress), ("fill", fillImg), ("check", check), ("reward", reward),
            ("sideLine", side));
    }

    static void BuildAnnouncer(Transform hud, TMP_FontAsset font, Material outline)
    {
        var root = ResetUI(hud, "Announcer");
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = new Vector2(0f, 240f);
        root.sizeDelta = new Vector2(1000f, 300f);
        var group = root.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        var headline = Txt("Headline", root, font, outline, 112f, new Color(1f, 0.85f, 0.2f), TextAlignmentOptions.Center, "LEVEL UP!");
        headline.rectTransform.anchoredPosition = new Vector2(0f, 40f);
        headline.rectTransform.sizeDelta = new Vector2(1000f, 140f);
        var subline = Txt("Subline", root, font, outline, 44f, Color.white, TextAlignmentOptions.Center, "YARD LEVEL 2");
        subline.rectTransform.anchoredPosition = new Vector2(0f, -60f);
        subline.rectTransform.sizeDelta = new Vector2(1000f, 60f);
        var announcer = root.gameObject.AddComponent<Announcer>();
        SetMany(announcer, ("root", root), ("group", group), ("headline", headline), ("subline", subline));
    }

    static void BuildGuidePointer(Transform hud)
    {
        var root = ResetUI(hud, "GuidePointer");
        Stretch(root);
        var arrow = Img("Arrow", root, UiShape("UI_Arrow"), new Color(1f, 0.85f, 0.2f), new Vector2(104f, 104f));
        var outlineShadow = arrow.gameObject.AddComponent<Shadow>();
        outlineShadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
        outlineShadow.effectDistance = new Vector2(3f, -5f);
        var pointer = root.gameObject.AddComponent<GuidePointer>();
        SetMany(pointer, ("arrow", arrow), ("area", root));
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

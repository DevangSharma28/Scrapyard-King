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

// Milestone 4 builder: upgrade tile + panel (replaces the bottom rail), hire tiles, Delivery Helper, customer queue,
// Back Lot expansion with tier-2 scrap. Entry points (in order): Assets, Prefabs, Scene (or All).
// Incremental like M3_Build: it edits the existing scene and prefabs (find-or-create by name); objects it owns are
// rebuilt in place, everything else is left alone.
public static class M4_Build
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

    static readonly Color Navy = new(0.1f, 0.15f, 0.32f);
    static readonly Color Gold = new(1f, 0.82f, 0.25f);

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
        foreach (var d in new[] { DataDir + "/Customers", DataDir + "/World", PrefabDir + "/Customers", PrefabDir + "/Tiles" })
            Directory.CreateDirectory(d);
        AssetDatabase.Refresh();

        // UI shapes (white, tinted per use).
        ShapeSprite("UI_Frame", 128, 128, (x, y) => Frame(x, y, 128, 40f, 10), new Vector4(48, 48, 48, 48));
        ShapeSprite("UI_Tail", 64, 64, (x, y) => Tail(x, y, 64), Vector4.zero);
        ShapeSprite("UI_Check", 128, 128, (x, y) => Check(x, y, 128), Vector4.zero);

        // SFX.
        Sfx("TilePay", ProceduralSfxPreset.Coin, 0.28f, new Vector2(1f, 1f), 0.035f);
        Sfx("TileEngage", ProceduralSfxPreset.Pop, 0.35f, new Vector2(1.1f, 1.2f), 0.1f);
        Sfx("CustomerArrive", ProceduralSfxPreset.Pop, 0.25f, new Vector2(1.25f, 1.45f), 0.2f);
        Sfx("CustomerHappy", ProceduralSfxPreset.Coin, 0.45f, new Vector2(1.25f, 1.35f), 0.1f);
        Sfx("AreaOpen", ProceduralSfxPreset.Fanfare, 0.65f, new Vector2(0.85f, 0.85f), 0.5f);

        // Workers.
        var porter = Load<WorkerDefinition>(DataDir + "/Workers/Worker_Porter.asset");
        SetMany(porter, ("displayName", "Scrap Porter"), ("siteId", "scrap"), ("tagline", "HAULS SCRAP TO THE CRUSHER"), ("collectLooseItems", true));
        var helper = LoadOrCreate<WorkerDefinition>(DataDir + "/Workers/Worker_Helper.asset");
        SetMany(helper, ("id", "helper"), ("displayName", "Delivery Helper"), ("role", (int)WorkerRole.Porter), ("siteId", "delivery"),
            ("tagline", "STOCKS THE SELL DESK FOR YOU"), ("moveSpeed", 3.4f), ("carryCapacity", 8), ("efficiency", 1f), ("pickupRadius", 1f),
            ("collectLooseItems", false));
        SetLongArray(helper, "hireCosts", new long[] { 800, 2000 });

        // First expansion (blueprint: 1,500 cash).
        var backLot = LoadOrCreate<ExpansionDefinition>(DataDir + "/World/Expansion_BackLot.asset");
        SetMany(backLot, ("id", "back_lot"), ("displayName", "Back Lot"), ("cost", 1500L), ("teaser", "FRIDGES & KARTS"),
            ("openSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_AreaOpen.asset")));

        // Customers.
        var customers = LoadOrCreate<CustomerConfig>(DataDir + "/Customers/CustomerConfig.asset");
        SetMany(customers, ("walkSpeed", 2.8f), ("arrivalInterval", new Vector2(1.2f, 3f)), ("handOverInterval", 0.16f),
            ("orderItem", Load<ItemDefinition>(DataDir + "/Items/Item_MixedMetal.asset")),
            ("arriveSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_CustomerArrive.asset")),
            ("happySfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_CustomerHappy.asset")));

        // Tier-2 scrap (blueprint medium class: 10-25 pieces). Fridges need chainsaw Lv.3, the rest Lv.2.
        var scrapItem = Load<ItemDefinition>(DataDir + "/Items/Item_Scrap.asset");
        ScrapDef("Fridge", "fridge", "Fridge", 320f, 20f, new Vector2Int(20, 25), 6, 12f, 0.5f, scrapItem);
        ScrapDef("Washer", "washer", "Washer", 180f, 15f, new Vector2Int(10, 14), 3, 9f, 0.3f, scrapItem);
        ScrapDef("Stove", "stove", "Stove", 220f, 15f, new Vector2Int(12, 16), 3, 10f, 0.35f, scrapItem);
        ScrapDef("Kart", "kart", "Go-Kart", 260f, 15f, new Vector2Int(15, 20), 4, 11f, 0.4f, scrapItem);

        SetMany(Load<SellDeskDefinition>(DataDir + "/Factory/SellDesk_Yard.asset"), ("displayName", "Sell Desk"));

        // Panel entries are grouped YOU / YARD; hires and the expansion are bought on their own tiles.
        var catalog = Load<UpgradeCatalog>(DataDir + "/Progression/UpgradeCatalog.asset");
        SetStructArray(catalog, "entries", new[]
        {
            Lv(("upgradeId", "chainsaw"), ("unlockLevel", 1), ("inPanel", true), ("group", "YOU")),
            Lv(("upgradeId", "backpack"), ("unlockLevel", 2), ("inPanel", true), ("group", "YOU")),
            Lv(("upgradeId", "boots"), ("unlockLevel", 4), ("inPanel", true), ("group", "YOU")),
            Lv(("upgradeId", "crusher"), ("unlockLevel", 1), ("inPanel", true), ("group", "YARD")),
            Lv(("upgradeId", "sell_desk"), ("unlockLevel", 1), ("inPanel", true), ("group", "YARD")),
            Lv(("upgradeId", "storage_yard"), ("unlockLevel", 3), ("inPanel", true), ("group", "YARD")),
            Lv(("upgradeId", "porter"), ("unlockLevel", 3), ("inPanel", false), ("group", "")),
            Lv(("upgradeId", "helper"), ("unlockLevel", 4), ("inPanel", false), ("group", "")),
            Lv(("upgradeId", "back_lot"), ("unlockLevel", 5), ("inPanel", false), ("group", "")),
        });

        BuildTasks();
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    static void ScrapDef(string asset, string id, string name, float hp, float minPower, Vector2Int drops, int xp, float respawn, float shake, ItemDefinition item)
    {
        var d = LoadOrCreate<ScrapDefinition>($"{DataDir}/Scrap/Scrap_{asset}.asset");
        SetMany(d, ("id", id), ("displayName", name), ("tier", 2), ("sizeClass", (int)ScrapSizeClass.Medium), ("maxHealth", hp), ("minCutPower", minPower),
            ("dropItem", item), ("dropAmount", drops), ("partDropShare", 0.35f), ("dropLaunchSpeed", 6.5f), ("dropSpread", 1.4f), ("xpReward", xp),
            ("respawnDelay", respawn), ("breakVfxScale", 1.3f), ("breakShake", shake));
    }

    static void BuildTasks()
    {
        string dir = DataDir + "/Progression/Tasks";
        // Keep GUIDs of the M3 tasks that move down the chain.
        Rename(dir, "t14_level5", "t16_level5");
        Rename(dir, "t15_crush150", "t19_crush150");

        var main = new List<Object>
        {
            Task(dir, "t01_cut_car", "Cut the car", TaskCategory.Main, TaskType.BreakScrap, "car_wreck", 1, 0, 20),
            Task(dir, "t02_collect", "Collect {0} scrap", TaskCategory.Main, TaskType.CollectItems, "scrap", 6, 0, 15),
            Task(dir, "t03_crush", "Crush {0} scrap", TaskCategory.Main, TaskType.ProcessItems, "crusher", 8, 0, 20),
            Task(dir, "t04_stock", "Stock the sell desk", TaskCategory.Main, TaskType.DeliverItems, "sell_desk", 6, 0, 20),
            Task(dir, "t05_first_sale", "Serve your first customer", TaskCategory.Main, TaskType.ServeCustomers, "sell_desk", 1, 20, 20),
            Task(dir, "t06_cash", "Collect ${0}", TaskCategory.Main, TaskType.EarnCash, "", 40, 0, 20),
            Task(dir, "t07_chainsaw", "Upgrade the chainsaw", TaskCategory.Main, TaskType.PurchaseUpgrade, "chainsaw", 1, 0, 30),
            Task(dir, "t08_crush30", "Crush {0} scrap", TaskCategory.Main, TaskType.ProcessItems, "crusher", 30, 50, 40),
            Task(dir, "t09_crusher2", "Crusher to Lv.{0}", TaskCategory.Main, TaskType.ReachUpgradeLevel, "crusher", 2, 0, 40),
            Task(dir, "t10_sell40", "Sell {0} metal", TaskCategory.Main, TaskType.SellItems, "", 40, 80, 50),
            Task(dir, "t11_desk2", "Sell desk to Lv.{0}", TaskCategory.Main, TaskType.ReachUpgradeLevel, "sell_desk", 2, 0, 50),
            Task(dir, "t12_backpack", "Upgrade the backpack", TaskCategory.Main, TaskType.ReachUpgradeLevel, "backpack", 2, 0, 40),
            Task(dir, "t13_porter", "Hire a scrap porter", TaskCategory.Main, TaskType.HireWorker, "porter", 1, 0, 60),
            Task(dir, "t14_helper", "Hire a delivery helper", TaskCategory.Main, TaskType.HireWorker, "helper", 1, 0, 60),
            Task(dir, "t15_serve20", "Serve {0} customers", TaskCategory.Main, TaskType.ServeCustomers, "", 20, 120, 50),
            Task(dir, "t16_level5", "Reach yard Lv.{0}", TaskCategory.Main, TaskType.ReachLevel, "", 5, 150, 0),
            Task(dir, "t17_back_lot", "Open the Back Lot", TaskCategory.Main, TaskType.ReachUpgradeLevel, "back_lot", 1, 0, 80),
            Task(dir, "t18_fridges", "Cut {0} fridges", TaskCategory.Main, TaskType.BreakScrap, "fridge", 2, 150, 40),
            Task(dir, "t19_crush150", "Crush {0} scrap", TaskCategory.Main, TaskType.ProcessItems, "crusher", 150, 200, 80),
        };
        var side = new List<Object>
        {
            Task(dir, "s01_barrels", "Cut {0} barrels", TaskCategory.Side, TaskType.BreakScrap, "barrel", 5, 40, 20),
            Task(dir, "s02_tires", "Cut {0} tire stacks", TaskCategory.Side, TaskType.BreakScrap, "tire_stack", 4, 40, 20),
            Task(dir, "s06_serve", "Serve {0} customers", TaskCategory.Side, TaskType.ServeCustomers, "", 10, 60, 30),
            Task(dir, "s03_cars", "Cut {0} cars", TaskCategory.Side, TaskType.BreakScrap, "car_wreck", 3, 90, 40),
            Task(dir, "s04_earn", "Earn ${0}", TaskCategory.Side, TaskType.EarnCash, "", 300, 60, 30),
            Task(dir, "s05_collect", "Collect {0} scrap", TaskCategory.Side, TaskType.CollectItems, "scrap", 60, 50, 30),
        };

        var chain = Load<TaskChain>(DataDir + "/Progression/TaskChain_Area1.asset");
        SetArray(chain, "mainTasks", main.ToArray());
        SetArray(chain, "sideTasks", side.ToArray());
    }

    static void Rename(string dir, string from, string to)
    {
        string a = $"{dir}/Task_{from}.asset", b = $"{dir}/Task_{to}.asset";
        if (File.Exists(a) && !File.Exists(b)) Log.AppendLine(AssetDatabase.MoveAsset(a, b) is var e && string.IsNullOrEmpty(e) ? $"moved {from} → {to}" : "!! " + e);
    }

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
        var font = Load<TMP_FontAsset>(FontDir + "/GROBOLD SDF.asset");
        var outline = Load<Material>(FontDir + "/GROBOLD Outline.mat");
        var mSteelDark = Lit("SteelDark", new Color(0.2f, 0.21f, 0.24f), 0.45f, 0.7f);
        var mCapBlue = Lit("CapBlue", new Color(0.2f, 0.5f, 0.95f), 0.5f);
        var mTile = Lit("TileBase", new Color(0.13f, 0.17f, 0.3f), 0.35f);
        var mTileGold = Lit("TileBaseGold", new Color(0.95f, 0.62f, 0.12f), 0.4f);
        var mBarBg = Load<Material>(MatDir + "/M_BarBg.mat");
        var mBarFill = Load<Material>(MatDir + "/M_BarFill.mat");
        var mWreck = Load<Material>(MatDir + "/M_Kenney_CarWreck.mat");

        // ---------- Delivery Helper ----------
        var workerController = Load<AnimatorController>(AnimDir + "/AC_Worker.controller");
        var helperDef = Load<WorkerDefinition>(DataDir + "/Workers/Worker_Helper.asset");
        var helperPrefab = SavePrefab(BuildHelper(workerController, mCapBlue, mSteelDark), PrefabDir + "/Workers/Worker_Helper.prefab");
        SetMany(helperDef, ("prefab", helperPrefab.GetComponent<Worker>()));

        // ---------- Customers ----------
        var customerController = BuildCustomerController(K + "/MiniCharacters/character-female-a.fbx", AnimDir + "/AC_Customer.controller");
        var customerPrefabs = new List<Object>();
        foreach (var model in new[] { "female-a", "female-e", "female-d", "male-f", "female-c" })
            customerPrefabs.Add(SavePrefab(BuildCustomer(model, customerController, font, outline), $"{PrefabDir}/Customers/Customer_{model}.prefab")
                .GetComponent<Customer>());
        SetArray(Load<CustomerConfig>(DataDir + "/Customers/CustomerConfig.asset"), "prefabs", customerPrefabs.ToArray());

        // ---------- Tiles ----------
        var bill = Load<GameObject>(PrefabDir + "/Economy/CashBundle.prefab").transform;
        SavePrefab(BuildPurchaseTile(font, outline, mTile, bill), PrefabDir + "/Tiles/Tile_Purchase.prefab");
        SavePrefab(BuildUpgradeTile(font, outline, mTileGold), PrefabDir + "/Tiles/Tile_Upgrade.prefab");

        // ---------- Station labels: rounded world-canvas pills (same look as tiles and bubbles) ----------
        EditPrefab(PrefabDir + "/Stations/StationLabel.prefab", root => BuildStationLabel(root, font, outline));

        // ---------- Tier-2 scrap ----------
        var fridge = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_Fridge.asset");
        var washer = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_Washer.asset");
        var stove = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_Stove.asset");
        var kart = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_Kart.asset");
        var fridgePrefab = SaveScrap(BuildAppliance("Scrap_Fridge", "kitchenFridgeLarge", 2.6f, fridge, new[] { "doorLeft", "doorRight" }, mWreck, mBarBg, mBarFill));
        var washerPrefab = SaveScrap(BuildAppliance("Scrap_Washer", "washer", 1.55f, washer, new[] { "washerDoor" }, mWreck, mBarBg, mBarFill));
        var stovePrefab = SaveScrap(BuildAppliance("Scrap_Stove", "kitchenStove", 1.6f, stove, new[] { "door" }, mWreck, mBarBg, mBarFill));
        SetMany(fridge, ("prefab", fridgePrefab));
        SetMany(washer, ("prefab", washerPrefab));
        SetMany(stove, ("prefab", stovePrefab));
        foreach (var d in new[] { fridge, washer, stove }) SetArray(d, "prefabVariants", Array.Empty<Object>());
        var karts = new List<Object>();
        foreach (var model in new[] { "kart-oobi", "kart-oodi", "kart-ooli", "kart-oopi" })
            karts.Add(SaveScrap(BuildKart(model, kart, mWreck, mBarBg, mBarFill)));
        SetMany(kart, ("prefab", karts[0]));
        SetArray(kart, "prefabVariants", karts.ToArray());

        Icons();
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    /// <summary>Renders the M4 icons (helper, back lot, mixed metal) and assigns them.</summary>
    public static string Icons()
    {
        var helper = RenderIcon("Icon_Helper", Load<GameObject>(PrefabDir + "/Workers/Worker_Helper.prefab"), new Vector3(15f, 160f, 0f), "idle");
        SetMany(Load<WorkerDefinition>(DataDir + "/Workers/Worker_Helper.asset"), ("icon", helper));
        var lot = RenderIcon("Icon_BackLot", PrefabRoot("Scrap/Scrap_Fridge", "HealthBar"), new Vector3(18f, -28f, 0f));
        SetMany(Load<ExpansionDefinition>(DataDir + "/World/Expansion_BackLot.asset"), ("icon", lot));
        var metal = RenderIcon("Icon_MixedMetal", Load<GameObject>(PrefabDir + "/Items/Item_MixedMetal.prefab"), new Vector3(35f, 30f, 0f));
        SetMany(Load<ItemDefinition>(DataDir + "/Items/Item_MixedMetal.asset"), ("icon", metal));
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    static ScrapObject SaveScrap(GameObject go) => SavePrefab(go, $"{PrefabDir}/Scrap/{go.name}.prefab").GetComponent<ScrapObject>();

    static GameObject BuildHelper(AnimatorController controller, Material cap, Material steelDark)
    {
        var root = new GameObject("Worker_Helper");
        root.layer = LayerMask.NameToLayer("Characters");
        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.4f;
        agent.height = 1.7f;
        agent.speed = 3.4f;
        agent.acceleration = 20f;
        agent.angularSpeed = 720f;
        agent.stoppingDistance = 0.2f;
        agent.autoBraking = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        var model = Kenney("MiniCharacters", "character-male-a", body, Vector3.zero, 2.4f, Vector3.zero, null);
        model.name = "Model";
        if (!model.TryGetComponent(out Animator animator)) animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        var clips = AssetDatabase.LoadAllAssetsAtPath(K + "/MiniCharacters/character-male-a.fbx").OfType<AnimationClip>().ToArray();
        clips.First(c => c.name == "idle").SampleAnimation(model, 0f);
        PlaceHardHat(root.transform, model.transform, cap);

        Box("CarryRack", body, new Vector3(0f, 0.95f, -0.3f), new Vector3(0.5f, 0.55f, 0.08f), steelDark, 0.02f);
        var stackAnchor = Empty("StackAnchor", root.transform, new Vector3(0f, 0.85f, -0.45f));
        var stack = root.AddComponent<CarryStack>();
        SetMany(stack, ("stackRoot", stackAnchor), ("capacity", 8));
        // Pad-only worker: no collector, so it never vacuums loose scrap.
        var worker = root.AddComponent<Worker>();
        SetMany(worker, ("stack", stack), ("collector", null), ("animator", animator));
        root.AddComponent<PorterBrain>();
        return root;
    }

    static AnimatorController BuildCustomerController(string fbx, string path)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToDictionary(c => c.name);
        if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
        var ac = AnimatorController.CreateAnimatorControllerAtPath(path);
        ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ac.AddParameter("Happy", AnimatorControllerParameterType.Trigger);
        var locomotion = ac.CreateBlendTreeInController("Locomotion", out var tree, 0);
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(clips["idle"], 0f);
        tree.AddChild(clips["walk"], 1f);
        var sm = ac.layers[0].stateMachine;
        var happy = sm.AddState("Happy");
        happy.motion = clips["emote-yes"];
        var toHappy = sm.AddAnyStateTransition(happy);
        toHappy.AddCondition(AnimatorConditionMode.If, 0f, "Happy");
        toHappy.duration = 0.08f;
        toHappy.canTransitionToSelf = false;
        var back = happy.AddTransition(locomotion);
        back.hasExitTime = true;
        back.exitTime = 0.9f;
        back.duration = 0.15f;
        sm.defaultState = locomotion;
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        return ac;
    }

    static GameObject BuildCustomer(string model, AnimatorController controller, TMP_FontAsset font, Material outline)
    {
        var root = new GameObject("Customer_" + model);
        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        var m = Kenney("MiniCharacters", "character-" + model, body, Vector3.zero, 2.4f, Vector3.zero, null);
        m.name = "Model";
        if (!m.TryGetComponent(out Animator animator)) animator = m.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        var hand = Empty("HandPoint", root.transform, new Vector3(0f, 1.0f, 0.35f));
        var bubble = BuildBubble(root.transform, font, outline);
        var customer = root.AddComponent<Customer>();
        SetMany(customer, ("animator", animator), ("bubble", bubble), ("handPoint", hand));
        return root;
    }

    static CustomerBubble BuildBubble(Transform parent, TMP_FontAsset font, Material outline)
    {
        var canvasRt = WorldCanvas("Bubble", parent, new Vector3(0f, 2.6f, 0f), new Vector2(200f, 150f), false);
        var root = Rect("Root", canvasRt);
        Stretch(root);
        var tail = Img("Tail", root, UiShape("UI_Tail"), Color.white, new Vector2(46f, 34f));
        tail.anchoredPosition = new Vector2(0f, -58f);
        var bg = Img("Bg", root, UiShape("UI_Round"), Color.white, new Vector2(176f, 104f));
        Sliced(bg, 1.6f);
        bg.anchoredPosition = new Vector2(0f, 6f);

        var order = Rect("Order", root);
        Stretch(order);
        var icon = Img("Icon", order, Load<Sprite>(IconDir + "/Icon_MixedMetal.png", false), Color.white, new Vector2(84f, 84f));
        icon.anchoredPosition = new Vector2(-38f, 10f);
        var count = Txt("Count", order, font, font.material, 64f, Navy, TextAlignmentOptions.Center, "3");
        count.rectTransform.anchoredPosition = new Vector2(40f, 8f);
        count.rectTransform.sizeDelta = new Vector2(84f, 80f);
        var waitBg = Img("WaitBg", order, UiShape("UI_RoundSmall"), new Color(0f, 0f, 0f, 0.15f), new Vector2(140f, 14f));
        Sliced(waitBg, 3f);
        waitBg.anchoredPosition = new Vector2(0f, -30f);
        var wait = Img("Wait", waitBg, UiShape("UI_RoundSmall"), new Color(0.35f, 0.8f, 0.3f), new Vector2(140f, 14f));
        var waitImg = wait.GetComponent<Image>();
        waitImg.type = Image.Type.Filled;
        waitImg.fillMethod = Image.FillMethod.Horizontal;
        waitImg.fillAmount = 0.5f;

        var happy = Rect("Happy", root);
        Stretch(happy);
        var circle = Img("Circle", happy, UiShape("UI_Circle"), new Color(0.3f, 0.82f, 0.32f), new Vector2(92f, 92f));
        circle.anchoredPosition = new Vector2(0f, 6f);
        Img("Check", circle, UiShape("UI_Check"), Color.white, new Vector2(64f, 64f));
        happy.gameObject.SetActive(false);

        var bubble = canvasRt.gameObject.AddComponent<CustomerBubble>();
        SetMany(bubble, ("root", root), ("icon", icon.GetComponent<Image>()), ("count", count), ("waitRing", waitImg), ("orderGroup", order.gameObject),
            ("happyGroup", happy.gameObject));
        return bubble;
    }

    static GameObject BuildPurchaseTile(TMP_FontAsset font, Material outline, Material baseMat, Transform bill)
    {
        var root = new GameObject("Tile_Purchase");
        var visual = Empty("Visual", root.transform, Vector3.zero);
        Box("Base", visual, new Vector3(0f, 0.04f, 0f), new Vector3(2.5f, 0.08f, 2.5f), baseMat, 0.03f);
        var canvas = WorldCanvas("Display", visual, new Vector3(0f, 0.086f, 0f), new Vector2(250f, 250f), true);
        var content = Rect("Content", canvas);
        Stretch(content);

        var fill = Img("Fill", content, UiShape("UI_Round"), new Color(0.35f, 0.95f, 0.4f, 0.6f), new Vector2(226f, 226f));
        var fillImg = fill.GetComponent<Image>();
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Radial360;
        fillImg.fillOrigin = (int)Image.Origin360.Top;
        fillImg.fillClockwise = true;
        fillImg.fillAmount = 0f;
        var frame = Img("Frame", content, UiShape("UI_Frame"), Color.white, new Vector2(244f, 244f));
        Sliced(frame, 1f);

        var icon = Img("Icon", content, null, Color.white, new Vector2(112f, 112f));
        icon.anchoredPosition = new Vector2(0f, 10f);
        var title = Txt("Title", content, font, outline, 28f, Color.white, TextAlignmentOptions.Center, "SCRAP PORTER");
        title.rectTransform.anchoredPosition = new Vector2(0f, 90f);
        title.rectTransform.sizeDelta = new Vector2(220f, 40f);
        AutoSize(title, 16f, 28f);
        var count = Txt("Count", content, font, outline, 22f, Gold, TextAlignmentOptions.Center, "0/2");
        count.rectTransform.anchoredPosition = new Vector2(0f, -52f);
        count.rectTransform.sizeDelta = new Vector2(220f, 30f);
        AutoSize(count, 14f, 22f);

        var priceRow = Rect("Price", content);
        priceRow.anchoredPosition = new Vector2(0f, -88f);
        priceRow.sizeDelta = new Vector2(220f, 50f);
        var coin = Img("Coin", priceRow, UiSprite(UiSheet1, "UI-pack_Sprite_1_16"), Color.white, new Vector2(40f, 40f));
        coin.anchoredPosition = new Vector2(-62f, 0f);
        var price = Txt("Amount", priceRow, font, outline, 40f, Color.white, TextAlignmentOptions.Center, "500");
        price.rectTransform.anchoredPosition = new Vector2(18f, 0f);
        price.rectTransform.sizeDelta = new Vector2(150f, 50f);

        var lockRoot = Rect("Lock", content);
        Stretch(lockRoot);
        var dim = Img("Dim", lockRoot, UiShape("UI_Round"), new Color(0.05f, 0.07f, 0.15f, 0.72f), new Vector2(226f, 226f));
        Sliced(dim, 1f);
        var lockIcon = Img("LockIcon", lockRoot, UiShape("UI_Lock"), Color.white, new Vector2(72f, 72f));
        lockIcon.anchoredPosition = new Vector2(0f, 2f);
        var lockText = Txt("LockText", lockRoot, font, outline, 34f, Color.white, TextAlignmentOptions.Center, "LV 3");
        lockText.rectTransform.anchoredPosition = new Vector2(0f, -78f);
        lockText.rectTransform.sizeDelta = new Vector2(200f, 44f);

        var tile = root.AddComponent<PurchaseTile>();
        SetMany(tile, ("size", new Vector2(2.5f, 2.5f)), ("visual", visual), ("engageSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_TileEngage.asset")),
            ("billPrefab", bill), ("paySfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_TilePay.asset")),
            ("completeSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_Upgrade.asset")),
            ("completeVfx", Load<GameObject>(PrefabDir + "/VFX/VFX_UpgradeBurst.prefab").GetComponent<ParticleSystem>()),
            ("displayRoot", content), ("icon", icon.GetComponent<Image>()), ("title", title), ("price", price), ("countText", count), ("coinIcon", coin.gameObject),
            ("fill", fillImg), ("frame", frame.GetComponent<Image>()), ("lockRoot", lockRoot.gameObject), ("lockText", lockText));
        return root;
    }

    static GameObject BuildUpgradeTile(TMP_FontAsset font, Material outline, Material baseMat)
    {
        var root = new GameObject("Tile_Upgrade");
        var visual = Empty("Visual", root.transform, Vector3.zero);
        Box("Base", visual, new Vector3(0f, 0.04f, 0f), new Vector3(2.8f, 0.08f, 2.8f), baseMat, 0.03f);
        var canvas = WorldCanvas("Display", visual, new Vector3(0f, 0.086f, 0f), new Vector2(280f, 280f), true);
        var content = Rect("Content", canvas);
        Stretch(content);
        var frame = Img("Frame", content, UiShape("UI_Frame"), Color.white, new Vector2(272f, 272f));
        Sliced(frame, 1f);
        var badge = Img("Badge", content, UiShape("UI_Circle"), new Color(0.3f, 0.82f, 0.32f), new Vector2(136f, 136f));
        badge.anchoredPosition = new Vector2(0f, 26f);
        var arrow = Img("Arrow", badge, UiShape("UI_Arrow"), Color.white, new Vector2(96f, 96f));
        arrow.anchoredPosition = new Vector2(0f, 4f);
        var title = Txt("Title", content, font, outline, 40f, Color.white, TextAlignmentOptions.Center, "UPGRADES");
        title.rectTransform.anchoredPosition = new Vector2(0f, -86f);
        title.rectTransform.sizeDelta = new Vector2(250f, 54f);

        // Floating "!" when something is affordable.
        var alert = WorldCanvas("Alert", root.transform, new Vector3(0f, 2.1f, 0f), new Vector2(120f, 120f), false);
        alert.gameObject.AddComponent<Billboard>();
        var dot = Img("Dot", alert, UiShape("UI_Circle"), new Color(0.95f, 0.25f, 0.2f), new Vector2(110f, 110f));
        var bang = Txt("Bang", dot, font, outline, 84f, Color.white, TextAlignmentOptions.Center, "!");
        Stretch(bang.rectTransform);

        var tile = root.AddComponent<UpgradeTile>();
        SetMany(tile, ("size", new Vector2(2.8f, 2.8f)), ("visual", visual), ("engageSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_TileEngage.asset")),
            ("alertBadge", alert));
        return root;
    }

    static void BuildStationLabel(GameObject root, TMP_FontAsset font, Material outline)
    {
        foreach (Transform c in root.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(c.gameObject);
        var canvas = WorldCanvas("Canvas", root.transform, Vector3.zero, new Vector2(320f, 160f), false);
        var header = Img("Header", canvas, UiShape("UI_Round"), new Color(0.1f, 0.15f, 0.32f, 0.94f), new Vector2(300f, 76f));
        Sliced(header, 1.6f);
        header.anchoredPosition = new Vector2(0f, 34f);
        var title = Txt("Title", header, font, outline, 40f, Color.white, TextAlignmentOptions.Center, "CRUSHER");
        title.rectTransform.anchoredPosition = new Vector2(-40f, 2f);
        title.rectTransform.sizeDelta = new Vector2(186f, 60f);
        AutoSize(title, 20f, 34f);
        var pill = Img("LevelPill", header, UiShape("UI_Round"), new Color(1f, 0.55f, 0.15f), new Vector2(84f, 50f));
        Sliced(pill, 3f);
        pill.anchoredPosition = new Vector2(98f, 0f);
        var level = Txt("Level", pill, font, outline, 28f, Color.white, TextAlignmentOptions.Center, "Lv.1");
        Stretch(level.rectTransform);
        var counterRoot = Rect("Counter", canvas);
        counterRoot.anchoredPosition = new Vector2(0f, -34f);
        counterRoot.sizeDelta = new Vector2(150f, 54f);
        var chip = Img("Chip", counterRoot, UiShape("UI_Round"), new Color(1f, 1f, 1f, 0.96f), new Vector2(150f, 54f));
        Sliced(chip, 2.4f);
        var value = Txt("Value", counterRoot, font, font.material, 36f, Navy, TextAlignmentOptions.Center, "0/10");
        Stretch(value.rectTransform);

        var label = root.GetComponent<StationLabel>();
        SetMany(label, ("title", title), ("level", level), ("counter", value), ("counterRoot", counterRoot), ("counterColor", Navy),
            ("fullColor", new Color(0.9f, 0.2f, 0.15f)));
    }

    static GameObject BuildAppliance(string name, string model, float height, ScrapDefinition def, string[] parts, Material wreck, Material barBg, Material barFill)
    {
        var root = ScrapRoot(name, def, out var visual);
        var body = Kenney("FurnitureKit", model, visual, Vector3.zero, 1f, Vector3.zero, null);
        var b = RendererBounds(body);
        body.transform.localScale = Vector3.one * (height / Mathf.Max(b.size.y, 0.01f));
        b = RendererBounds(body);
        body.transform.position += new Vector3(-b.center.x, -b.min.y, -b.center.z);
        // Junk, not showroom: a slight lean.
        body.transform.localRotation = Quaternion.Euler(0f, 0f, 2.5f);
        b = RendererBounds(body);

        int order = 0;
        foreach (var p in parts)
        {
            var t = FindDeep(body.transform, p);
            if (t != null) Part(t.gameObject, order++);
            else Log.AppendLine($"!! {name}: part {p} not found");
        }

        FitCollider(root, body, 0.95f);
        HealthBar(root.transform, b.max.y + 0.6f, 1.2f, barBg, barFill);
        Carve(root);
        return root;
    }

    static GameObject BuildKart(string model, ScrapDefinition def, Material wreck, Material barBg, Material barFill)
    {
        var root = ScrapRoot("Scrap_Kart_" + model.Replace("kart-", ""), def, out var visual);
        var kart = Kenney("CarKit", model, visual, Vector3.zero, 1f, new Vector3(0f, 0f, -3f), wreck);
        var b = RendererBounds(kart);
        kart.transform.localScale = Vector3.one * (2.7f / Mathf.Max(b.size.x, b.size.z, 0.01f));
        var driver = kart.transform.Find("character");
        if (driver != null) driver.gameObject.SetActive(false);
        var missing = kart.transform.Find("wheel-front-left");
        if (missing != null) missing.gameObject.SetActive(false);
        int order = 0;
        foreach (var wheel in new[] { "wheel-back-left", "wheel-front-right", "wheel-back-right" })
        {
            var w = kart.transform.Find(wheel);
            if (w != null) Part(w.gameObject, order++);
        }

        b = RendererBounds(kart);
        kart.transform.position += new Vector3(-b.center.x, -b.min.y, -b.center.z);
        b = RendererBounds(kart);
        FitCollider(root, kart, 0.95f);
        HealthBar(root.transform, b.max.y + 0.7f, 1.3f, barBg, barFill);
        Carve(root);
        return root;
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

    /// <summary>World-space canvas. Flat ones lie on the ground facing up (read from the south-facing camera).</summary>
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

    static void Sliced(RectTransform rt, float pixelsPerUnit)
    {
        var img = rt.GetComponent<Image>();
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = pixelsPerUnit;
    }

    /// <summary>Stretches to the parent with offsets (left, bottom, right, top; positive = inward).</summary>
    static void Inset(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    static void AutoSize(TMP_Text t, float min, float max)
    {
        t.enableAutoSizing = true;
        t.fontSizeMin = min;
        t.fontSizeMax = max;
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
        var worldText = Load<Material>(FontDir + "/GROBOLD World.mat");

        var crusher = gameplay.GetComponentsInChildren<Machine>(true).First();
        var storage = gameplay.GetComponentsInChildren<Storage>(true).First();
        var desk = gameplay.GetComponentsInChildren<SellDesk>(true).First();
        var storagePad = storage.transform.Find("WithdrawPad").GetComponent<TransferPad>();
        var stockPad = desk.transform.Find("StockPad").GetComponent<TransferPad>();
        _ = crusher;

        // ---------- Tiles ----------
        var tiles = FindOrCreate(gameplay, "Tiles");
        var purchasePrefab = Load<GameObject>(PrefabDir + "/Tiles/Tile_Purchase.prefab");
        PlaceTile(tiles, "Tile_Upgrades", Load<GameObject>(PrefabDir + "/Tiles/Tile_Upgrade.prefab"), new Vector3(27.6f, 0f, 23f), null, "tile/upgrades");
        PlaceTile(tiles, "Tile_Porter", purchasePrefab, new Vector3(16.4f, 0f, 20.6f), "porter", "tile/porter");
        PlaceTile(tiles, "Tile_Helper", purchasePrefab, new Vector3(27.6f, 0f, 17.6f), "helper", "tile/helper");
        PlaceTile(tiles, "Tile_BackLot", purchasePrefab, new Vector3(29.5f, 0f, 28.5f), "back_lot", "tile/back_lot");

        // ---------- Workers ----------
        var workers = gameplay.Find("Workers");
        var sites = workers.Find("Sites");
        var porterRoute = sites.Find("PorterRoute").GetComponent<PorterRoute>();
        var lotZone = FindOrCreate(porterRoute.transform, "BackLotZone");
        lotZone.position = new Vector3(29f, 0f, 34f);
        SetMany(porterRoute, ("siteId", "scrap"));
        SetStructArray(porterRoute, "extraZones", new[] { Lv(("center", lotZone), ("radius", 9f)) });

        var deliveryT = FindOrCreate(sites, "DeliveryRoute");
        deliveryT.position = new Vector3(26.2f, 0f, 15.6f);
        if (!deliveryT.TryGetComponent(out PorterRoute delivery)) delivery = deliveryT.gameObject.AddComponent<PorterRoute>();
        var deliveryIdle = FindOrCreate(deliveryT, "Idle");
        deliveryIdle.position = new Vector3(26.2f, 0f, 15.6f);
        SetMany(delivery, ("siteId", "delivery"), ("idlePoint", deliveryIdle), ("pickupCenter", deliveryT), ("pickupRadius", 1f), ("pickupPad", storagePad),
            ("dropoff", stockPad));

        var wm = systems.Find("WorkerManager").GetComponent<WorkerManager>();
        SetArray(wm, "hireable", new Object[]
        {
            Load<WorkerDefinition>(DataDir + "/Workers/Worker_Porter.asset"), Load<WorkerDefinition>(DataDir + "/Workers/Worker_Helper.asset")
        });
        SetArray(wm, "sites", new Object[] { porterRoute, delivery });

        // ---------- Customers ----------
        var customersT = FindOrCreate(gameplay, "Customers");
        if (!customersT.TryGetComponent(out CustomerQueue queue)) queue = customersT.gameObject.AddComponent<CustomerQueue>();
        var slotRoot = ResetChild(customersT, "Slots");
        var slots = new List<Object>();
        for (int i = 0; i < 7; i++) slots.Add(Empty($"Slot_{i}", slotRoot, new Vector3(32f, 0f, 10.9f - i * 1.2f)));
        var entryRoot = ResetChild(customersT, "Entry");
        var entry = new Object[] { Empty("Spawn", entryRoot, new Vector3(47f, 0f, 2.6f)), Empty("LineBack", entryRoot, new Vector3(32f, 0f, 2.6f)) };
        var exitRoot = ResetChild(customersT, "Exit");
        var exit = new Object[]
        {
            Empty("StepAside", exitRoot, new Vector3(33.7f, 0f, 10f)), Empty("Road", exitRoot, new Vector3(33.7f, 0f, 1.2f)),
            Empty("Away", exitRoot, new Vector3(-8f, 0f, 1.2f))
        };
        var crowd = FindOrCreate(customersT, "Crowd");
        SetMany(queue, ("desk", desk), ("config", Load<CustomerConfig>(DataDir + "/Customers/CustomerConfig.asset")), ("customersRoot", crowd),
            ("cheerDuration", 0.45f));
        SetArray(queue, "slots", slots.ToArray());
        SetArray(queue, "entryPath", entry);
        SetArray(queue, "exitPath", exit);
        SetMany(desk, ("walkInDemand", false));

        // ---------- Back Lot ----------
        BuildBackLot(gameplay, environment, font, worldText);

        // ---------- NavMesh (rebake: junk moved, barriers ignored so they leave no hole when they sink) ----------
        var navT = environment.Find("NavMesh");
        var surface = navT.GetComponent<NavMeshSurface>();
        surface.BuildNavMesh();
        string navDir = Path.Combine(Path.GetDirectoryName(ScenePath), Path.GetFileNameWithoutExtension(ScenePath));
        string navPath = navDir.Replace('\\', '/') + "/NavMesh-Area1.asset";
        if (File.Exists(navPath)) AssetDatabase.DeleteAsset(navPath);
        AssetDatabase.CreateAsset(surface.navMeshData, navPath);
        EditorUtility.SetDirty(surface);
        Log.AppendLine("navmesh baked: " + navPath);

        // ---------- UI: the upgrade panel replaces the bottom rail ----------
        var canvas = ui.Find("Canvas");
        var hud = canvas.Find("HUD");
        var rail = hud.Find("UpgradeRail");
        if (rail != null) Object.DestroyImmediate(rail.gameObject);
        BuildUpgradePanel(hud, font, outline);
        var joystick = canvas.GetComponentInChildren<VirtualJoystick>(true);
        if (joystick != null) SetMany(joystick, ("idleAlpha", 0.3f));
        hud.Find("Announcer")?.SetAsLastSibling();
        hud.Find("FlyLayer")?.SetAsLastSibling();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("Saved " + ScenePath);
        return Log.ToString();
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

    static void BuildBackLot(Transform gameplay, Transform environment, TMP_FontAsset font, Material worldText)
    {
        var lot = FindOrCreate(gameplay, "BackLot");
        lot.position = Vector3.zero;
        if (!lot.TryGetComponent(out Expansion expansion)) expansion = lot.gameObject.AddComponent<Expansion>();

        // Barriers: tall hazard fence along the south and west edges. Ignored by the bake, carved while standing.
        var barriers = ResetChild(lot, "Barriers");
        if (!barriers.TryGetComponent(out NavMeshModifier modifier)) modifier = barriers.gameObject.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = true;
        var south = LotFence(barriers, "Fence_South", new Vector3(19f, 0f, 30.3f), new Vector3(39.7f, 0f, 30.3f));
        var west = LotFence(barriers, "Fence_West", new Vector3(19f, 0f, 30.3f), new Vector3(19f, 0f, 37.9f));

        // Sign over the fence while locked.
        var locked = ResetChild(lot, "LockedOnly");
        var hazard = Lit("HazardYellow", new Color(1f, 0.78f, 0.1f), 0.3f);
        var dark = Lit("SteelDark", new Color(0.2f, 0.21f, 0.24f), 0.45f, 0.7f);
        var sign = Empty("Sign", locked, new Vector3(29.5f, 0f, 30.55f));
        Box("PostW", sign, new Vector3(-1.7f, 1.3f, 0f), new Vector3(0.16f, 2.6f, 0.16f), dark, 0.02f);
        Box("PostE", sign, new Vector3(1.7f, 1.3f, 0f), new Vector3(0.16f, 2.6f, 0.16f), dark, 0.02f);
        var board = Box("Board", sign, new Vector3(0f, 2.55f, 0f), new Vector3(3.8f, 1.1f, 0.12f), hazard, 0.04f);
        board.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
        var text = Text3D("Text", sign, new Vector3(0f, 2.55f, -0.08f), font, worldText, 5f, Navy, "<size=75%>BACK LOT</size>\nLOCKED");
        text.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
        text.rectTransform.sizeDelta = new Vector2(3.6f, 1f);

        // Content: tier-2 scrap that pops in when the lot opens.
        var content = ResetChild(lot, "Content");
        int characters = LayerMask.GetMask("Characters");
        var fridge = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_Fridge.asset");
        var washer = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_Washer.asset");
        var stove = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_Stove.asset");
        var kart = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_Kart.asset");
        SpawnPoint("SP_Fridge_01", content, new Vector3(22.6f, 0f, 33.2f), 10f, false, characters, 1.9f, fridge);
        SpawnPoint("SP_Stove_01", content, new Vector3(26.8f, 0f, 32f), 0f, true, characters, 1.6f, stove);
        SpawnPoint("SP_Washer_01", content, new Vector3(26.4f, 0f, 35.4f), 0f, true, characters, 1.6f, washer);
        SpawnPoint("SP_Kart_01", content, new Vector3(31.4f, 0f, 33.8f), 35f, false, characters, 2.1f, kart);
        SpawnPoint("SP_Fridge_02", content, new Vector3(35.8f, 0f, 35.3f), -12f, false, characters, 1.9f, fridge);
        SpawnPoint("SP_Washer_02", content, new Vector3(36.2f, 0f, 32.1f), 0f, true, characters, 1.6f, washer, stove);
        foreach (Transform c in content)
        {
            SetMany(c.GetComponent<ScrapSpawnPoint>(), ("animateFirstSpawn", true));
            c.gameObject.SetActive(false);
        }

        var focus = FindOrCreate(lot, "Focus");
        focus.position = new Vector3(29.5f, 0f, 33.2f);
        SetMany(expansion, ("definition", Load<ExpansionDefinition>(DataDir + "/World/Expansion_BackLot.asset")), ("contentRoot", content),
            ("lockedOnly", locked.gameObject), ("area", new Bounds(new Vector3(29.35f, 0f, 34.1f), new Vector3(20.7f, 4f, 7.6f))), ("focusPoint", focus),
            ("revealVfx", Load<GameObject>(PrefabDir + "/VFX/VFX_UpgradeBurst.prefab").GetComponent<ParticleSystem>()));
        SetArray(expansion, "barriers", new Object[] { south, west });

        // Junk: clear the lot interior, keep a low heap along the back wall as a teaser, move the east heap off the fence line.
        var junk = environment.Find("JunkPiles");
        foreach (Transform heap in junk.Cast<Transform>().ToArray())
        {
            var p = heap.position;
            bool inLot = p.x > 18.5f && p.z > 30f;
            if (inLot || heap.name.StartsWith("Heap_M4")) Object.DestroyImmediate(heap.gameObject);
        }

        var wreck = Load<Material>(MatDir + "/M_Kenney_CarWreck.mat");
        var survival = Load<Material>(MatDir + "/M_Kenney_SurvivalKit.mat");
        var rng = new System.Random(41);
        Heap(junk, "Heap_M4_Back", new Vector3(29.4f, 0f, 37.3f), new Vector2(10f, 0.55f), 18, rng, wreck, survival);
        Heap(junk, "Heap_M4_East", new Vector3(38.7f, 0f, 28.6f), new Vector2(0.9f, 1.3f), 6, rng, wreck, survival);
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

    static void BuildUpgradePanel(Transform hud, TMP_FontAsset font, Material outline)
    {
        var root = ResetUI(hud, "UpgradePanel");
        Stretch(root);

        var sheet = Rect("Sheet", root);
        sheet.anchorMin = new Vector2(0f, 0f);
        sheet.anchorMax = new Vector2(1f, 0f);
        sheet.pivot = new Vector2(0.5f, 0f);
        sheet.sizeDelta = new Vector2(0f, 940f);
        sheet.anchoredPosition = Vector2.zero;
        var group = sheet.gameObject.AddComponent<CanvasGroup>();

        var shadow = Img("Shadow", sheet, UiShape("UI_Round"), new Color(0f, 0f, 0f, 0.3f), Vector2.zero);
        Stretch(shadow);
        shadow.offsetMin = new Vector2(8f, -80f);
        shadow.offsetMax = new Vector2(-8f, 6f);
        Sliced(shadow, 1f);
        var back = Img("Back", sheet, UiShape("UI_Round"), new Color(0.11f, 0.16f, 0.34f, 0.98f), Vector2.zero);
        Stretch(back);
        back.offsetMin = new Vector2(14f, -80f);
        back.offsetMax = new Vector2(-14f, 0f);
        Sliced(back, 1f);
        back.GetComponent<Image>().raycastTarget = true;
        var inner = Img("Inner", sheet, UiShape("UI_Round"), new Color(0.07f, 0.1f, 0.24f, 1f), Vector2.zero);
        Stretch(inner);
        inner.offsetMin = new Vector2(30f, 20f);
        inner.offsetMax = new Vector2(-30f, -100f);
        Sliced(inner, 1.4f);

        var title = Txt("Title", sheet, font, outline, 60f, Color.white, TextAlignmentOptions.Left, "UPGRADES");
        title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0f, 1f);
        title.rectTransform.pivot = new Vector2(0f, 0.5f);
        title.rectTransform.anchoredPosition = new Vector2(48f, -52f);
        title.rectTransform.sizeDelta = new Vector2(600f, 80f);

        var close = Img("Close", sheet, UiShape("UI_Circle"), new Color(0.92f, 0.3f, 0.25f), new Vector2(92f, 92f));
        close.anchorMin = close.anchorMax = new Vector2(1f, 1f);
        close.anchoredPosition = new Vector2(-70f, -52f);
        close.GetComponent<Image>().raycastTarget = true;
        var closeButton = close.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = close.GetComponent<Image>();
        var x = Txt("X", close, font, outline, 52f, Color.white, TextAlignmentOptions.Center, "X");
        Stretch(x.rectTransform);

        // Scroll view.
        var scrollRt = Rect("Scroll", sheet);
        Stretch(scrollRt);
        scrollRt.offsetMin = new Vector2(30f, 20f);
        scrollRt.offsetMax = new Vector2(-30f, -100f);
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
        viewport.gameObject.layer = LayerMask.NameToLayer("UI");
        viewport.SetParent(scrollRt, false);
        Stretch(viewport);
        var content = Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 6, 20);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 30f;

        // Templates live outside the scroll content.
        var templates = Rect("Templates", sheet);
        var header = Txt("HeaderTemplate", templates, font, outline, 40f, Gold, TextAlignmentOptions.Left, "YOU");
        var headerLayout = header.gameObject.AddComponent<LayoutElement>();
        headerLayout.preferredHeight = 50f;
        var grid = Rect("GridTemplate", templates);
        var gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
        gridLayout.cellSize = new Vector2(310f, 330f);
        gridLayout.spacing = new Vector2(16f, 16f);
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = 3;
        gridLayout.childAlignment = TextAnchor.UpperCenter;
        grid.gameObject.AddComponent<GridColumnsFit>();
        var card = BuildCard(templates, font, outline);

        var panel = root.gameObject.AddComponent<UpgradePanel>();
        SetMany(panel, ("sheet", sheet), ("group", group), ("closeButton", closeButton), ("content", content), ("scroll", scroll), ("headerTemplate", header),
            ("gridTemplate", grid), ("cardTemplate", card), ("cameraShift", new Vector3(0f, 0f, -3.4f)));
    }

    static UpgradeCard BuildCard(Transform parent, TMP_FontAsset font, Material outline)
    {
        var plain = font.material;
        var card = Rect("CardTemplate", parent);
        card.sizeDelta = new Vector2(310f, 330f);

        // Backgrounds stretch with the card so it can shrink to fit narrow screens (GridColumnsFit).
        var highlight = Img("Highlight", card, UiShape("UI_Round"), new Color(1f, 0.85f, 0.15f), Vector2.zero);
        Inset(highlight, -10f, -10f, -10f, -10f);
        Sliced(highlight, 1f);
        var shadow = Img("Shadow", card, UiShape("UI_Round"), new Color(0f, 0f, 0f, 0.35f), Vector2.zero);
        Inset(shadow, 0f, -8f, 0f, 8f);
        Sliced(shadow, 1f);
        var bg = Img("Background", card, UiShape("UI_Round"), Color.white, Vector2.zero);
        Inset(bg, 0f, 0f, 0f, 0f);
        Sliced(bg, 1f);
        bg.GetComponent<Image>().raycastTarget = true;
        var top = Img("IconBack", card, UiShape("UI_Round"), new Color(0.87f, 0.92f, 1f), Vector2.zero);
        top.anchorMin = new Vector2(0f, 1f);
        top.anchorMax = new Vector2(1f, 1f);
        top.pivot = new Vector2(0.5f, 1f);
        top.offsetMin = new Vector2(14f, -146f);
        top.offsetMax = new Vector2(-14f, -12f);
        Sliced(top, 1.4f);

        var icon = Img("Icon", card, null, Color.white, new Vector2(124f, 124f));
        icon.anchoredPosition = new Vector2(0f, 86f);
        var levelPill = Img("LevelPill", card, UiShape("UI_Round"), new Color(1f, 0.55f, 0.15f), new Vector2(96f, 40f));
        Sliced(levelPill, 3f);
        levelPill.anchorMin = levelPill.anchorMax = new Vector2(0f, 1f);
        levelPill.anchoredPosition = new Vector2(62f, -30f);
        var level = Txt("Level", levelPill, font, outline, 24f, Color.white, TextAlignmentOptions.Center, "Lv.1");
        Stretch(level.rectTransform);

        var title = Txt("Title", card, font, plain, 32f, Navy, TextAlignmentOptions.Center, "Crusher");
        title.rectTransform.anchoredPosition = new Vector2(0f, -4f);
        title.rectTransform.sizeDelta = new Vector2(280f, 40f);
        AutoSize(title, 20f, 32f);

        var pipRoot = Rect("Pips", card);
        pipRoot.anchoredPosition = new Vector2(0f, -38f);
        pipRoot.sizeDelta = new Vector2(280f, 14f);
        var pipLayout = pipRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
        pipLayout.spacing = 5f;
        pipLayout.childAlignment = TextAnchor.MiddleCenter;
        pipLayout.childControlWidth = false;
        pipLayout.childControlHeight = false;
        pipLayout.childForceExpandWidth = false;
        pipLayout.childForceExpandHeight = false;
        var pip = Img("PipTemplate", pipRoot, UiShape("UI_RoundSmall"), Color.white, new Vector2(24f, 12f));
        Sliced(pip, 4f);

        var effect = Txt("Effect", card, font, plain, 22f, new Color(0.35f, 0.42f, 0.55f), TextAlignmentOptions.Center, "75 → 92/min");
        effect.rectTransform.anchoredPosition = new Vector2(0f, -64f);
        effect.rectTransform.sizeDelta = new Vector2(280f, 30f);
        AutoSize(effect, 14f, 22f);

        var buttonRect = Img("Buy", card, UiSprite(UiSheet2, "UI-pack_Sprite_2_16"), Color.white, new Vector2(270f, 72f));
        buttonRect.GetComponent<Image>().preserveAspect = false;
        buttonRect.GetComponent<Image>().raycastTarget = true;
        buttonRect.anchoredPosition = new Vector2(0f, -116f);
        var button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonRect.GetComponent<Image>();
        var colors = button.colors;
        colors.disabledColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        button.colors = colors;
        var coin = Img("Coin", buttonRect, UiSprite(UiSheet1, "UI-pack_Sprite_1_16"), Color.white, new Vector2(50f, 50f));
        coin.anchoredPosition = new Vector2(-80f, 3f);
        var cost = Txt("Cost", buttonRect, font, outline, 40f, Color.white, TextAlignmentOptions.Center, "150");
        cost.rectTransform.anchoredPosition = new Vector2(22f, 3f);
        cost.rectTransform.sizeDelta = new Vector2(170f, 60f);

        // Locked: a pale veil greys the card out, a dark pill replaces the buy button with the required level.
        var lockOverlay = Img("Lock", card, UiShape("UI_Round"), new Color(0.82f, 0.85f, 0.92f, 0.72f), Vector2.zero);
        Inset(lockOverlay, 0f, 0f, 0f, 0f);
        Sliced(lockOverlay, 1f);
        lockOverlay.GetComponent<Image>().raycastTarget = true;
        var lockPill = Img("Pill", lockOverlay, UiShape("UI_Round"), new Color(0.1f, 0.15f, 0.32f, 0.96f), new Vector2(270f, 72f));
        Sliced(lockPill, 2f);
        lockPill.anchoredPosition = new Vector2(0f, -116f);
        var lockIcon = Img("LockIcon", lockPill, UiShape("UI_Lock"), Color.white, new Vector2(46f, 46f));
        lockIcon.anchoredPosition = new Vector2(-78f, 2f);
        var lockText = Txt("LockText", lockPill, font, outline, 34f, Color.white, TextAlignmentOptions.Center, "LV 3");
        lockText.rectTransform.anchoredPosition = new Vector2(22f, 2f);
        lockText.rectTransform.sizeDelta = new Vector2(170f, 50f);

        var component = card.gameObject.AddComponent<UpgradeCard>();
        SetMany(component, ("icon", icon.GetComponent<Image>()), ("title", title), ("level", level), ("effect", effect), ("cost", cost), ("buyButton", button),
            ("buttonImage", buttonRect.GetComponent<Image>()), ("coinIcon", coin.gameObject), ("lockOverlay", lockOverlay.gameObject), ("lockText", lockText),
            ("highlight", highlight), ("pipRoot", pipRoot), ("pipTemplate", pip.GetComponent<Image>()));
        return component;
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

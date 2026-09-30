using System.IO;
using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using ScrapYardKing.Harvest;
using ScrapYardKing.Items;
using ScrapYardKing.Player;
using ScrapYardKing.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Builds (or rebuilds in place) every Milestone 1 asset: materials, VFX, SFX, data and prefabs.
// Idempotent: existing assets at the same path are updated, so GUID references survive re-runs.
public static class M1_Assets
{
    const string Root = "Assets/_Project";
    const string MatDir = Root + "/Art/Materials";
    const string TexDir = Root + "/Art/Textures";
    const string DataDir = Root + "/Data";
    const string PrefabDir = Root + "/Prefabs";

    static readonly System.Text.StringBuilder Log = new();

    public static string Run()
    {
        Log.Clear();
        foreach (var dir in new[] { MatDir, TexDir, DataDir + "/Config", DataDir + "/Items", DataDir + "/Scrap", DataDir + "/Audio",
                     PrefabDir + "/Items", PrefabDir + "/Scrap", PrefabDir + "/Player", PrefabDir + "/VFX", PrefabDir + "/UI" })
            Directory.CreateDirectory(dir);
        AssetDatabase.Refresh();

        // ---------- Materials ----------
        var mDirt = Lit("Dirt", new Color(0.56f, 0.46f, 0.34f), 0.1f);
        Lit("Grass", new Color(0.38f, 0.52f, 0.28f), 0.1f);
        Lit("Asphalt", new Color(0.23f, 0.24f, 0.26f), 0.25f);
        Lit("RoadLine", new Color(0.95f, 0.85f, 0.35f), 0.2f);
        Lit("OilDirt", new Color(0.43f, 0.37f, 0.29f), 0.2f);
        Lit("LockedGround", new Color(0.4f, 0.35f, 0.3f), 0.05f);
        Lit("FencePost", new Color(0.36f, 0.38f, 0.4f), 0.4f, 0.6f);
        Lit("FenceMesh", new Color(0.62f, 0.65f, 0.68f), 0.35f, 0.5f);
        Lit("HazardYellow", new Color(1f, 0.78f, 0.1f), 0.3f);
        Lit("HazardRed", new Color(0.85f, 0.18f, 0.14f), 0.3f);
        Lit("SignDark", new Color(0.12f, 0.13f, 0.15f), 0.3f);
        Lit("TreeTrunk", new Color(0.4f, 0.28f, 0.18f), 0.1f);
        Lit("TreeLeaves", new Color(0.3f, 0.55f, 0.25f), 0.1f);
        Lit("Crate", new Color(0.62f, 0.45f, 0.26f), 0.15f);
        var mCarPaint = Lit("CarPaint", new Color(0.32f, 0.58f, 0.62f), 0.35f, 0.2f);
        var mCarPaintDark = Lit("CarPaintDark", new Color(0.24f, 0.44f, 0.48f), 0.35f, 0.2f);
        var mGlass = Lit("Glass", new Color(0.18f, 0.24f, 0.3f), 0.8f);
        var mRust = Lit("Rust", new Color(0.58f, 0.3f, 0.14f), 0.1f, 0.2f);
        var mMetalDark = Lit("MetalDark", new Color(0.26f, 0.27f, 0.29f), 0.45f, 0.7f);
        var mRubber = Lit("Rubber", new Color(0.1f, 0.1f, 0.11f), 0.2f);
        var mBarrelRed = Lit("BarrelRed", new Color(0.78f, 0.22f, 0.16f), 0.35f, 0.3f);
        var mScrapRust = Lit("ScrapRust", new Color(0.62f, 0.38f, 0.22f), 0.25f, 0.4f);
        var mScrapSteel = Lit("ScrapSteel", new Color(0.6f, 0.62f, 0.64f), 0.5f, 0.8f);
        var mScrapPaint = Lit("ScrapPaint", new Color(0.36f, 0.6f, 0.64f), 0.35f, 0.3f);
        var mOveralls = Lit("Overalls", new Color(0.18f, 0.38f, 0.78f), 0.2f);
        var mOverallsDark = Lit("OverallsDark", new Color(0.13f, 0.27f, 0.55f), 0.2f);
        var mSkin = Lit("Skin", new Color(0.96f, 0.76f, 0.6f), 0.3f);
        var mHardHat = Lit("HardHat", new Color(1f, 0.8f, 0.12f), 0.5f);
        var mSawOrange = Lit("SawOrange", new Color(1f, 0.48f, 0.1f), 0.4f);
        var mBlade = Lit("Blade", new Color(0.8f, 0.82f, 0.85f), 0.8f, 0.9f);
        var mBarBg = Unlit("BarBg", new Color(0.08f, 0.08f, 0.1f));
        var mBarFill = Unlit("BarFill", new Color(0.35f, 0.9f, 0.35f));

        // ---------- VFX ----------
        var softCircle = SoftCircleTexture();
        var mSparks = ParticleMaterial("Sparks", softCircle, new Color(2.2f, 1.6f, 0.7f, 1f), additive: true);
        var mDust = ParticleMaterial("Dust", softCircle, new Color(1f, 1f, 1f, 1f), additive: false);
        var hitSparks = SavePrefab(BuildHitSparks(mSparks), PrefabDir + "/VFX/VFX_HitSparks.prefab").GetComponent<ParticleSystem>();
        var breakBurst = SavePrefab(BuildBreakBurst(mDust, mSparks), PrefabDir + "/VFX/VFX_BreakBurst.prefab").GetComponent<ParticleSystem>();
        var spawnDust = SavePrefab(BuildSpawnDust(mDust), PrefabDir + "/VFX/VFX_SpawnDust.prefab").GetComponent<ParticleSystem>();

        // ---------- SFX ----------
        var sfxMetalHit = Sfx("MetalHit", ProceduralSfxPreset.MetalHit, 0.45f, new Vector2(0.9f, 1.15f), 0.04f);
        var sfxMetalBreak = Sfx("MetalBreak", ProceduralSfxPreset.MetalBreak, 0.9f, new Vector2(0.92f, 1.05f), 0.05f);
        var sfxRubberHit = Sfx("RubberHit", ProceduralSfxPreset.Thud, 0.6f, new Vector2(0.9f, 1.1f), 0.04f);
        var sfxRubberBreak = Sfx("RubberBreak", ProceduralSfxPreset.Thud, 0.9f, new Vector2(0.7f, 0.8f), 0.05f);
        var sfxPartDetach = Sfx("PartDetach", ProceduralSfxPreset.Clunk, 0.55f, new Vector2(0.85f, 1.05f), 0.05f);
        var sfxPickup = Sfx("Pickup", ProceduralSfxPreset.Pop, 0.3f, new Vector2(1f, 1.03f), 0.02f);
        var sfxStackFull = Sfx("StackFull", ProceduralSfxPreset.Clunk, 0.7f, new Vector2(0.7f, 0.72f), 0.3f);
        var sfxSpawn = Sfx("Spawn", ProceduralSfxPreset.Whoosh, 0.35f, new Vector2(0.9f, 1.1f), 0.1f);

        // ---------- Config ----------
        var feedback = LoadOrCreate<FeedbackConfig>(DataDir + "/Config/FeedbackConfig.asset");
        Set(feedback, "defaultHitVfx", hitSparks);
        Set(feedback, "defaultBreakVfx", breakBurst);
        Set(feedback, "spawnVfx", spawnDust);
        Set(feedback, "defaultHitSfx", sfxMetalHit);
        Set(feedback, "defaultBreakSfx", sfxMetalBreak);
        Set(feedback, "partDetachSfx", sfxPartDetach);
        Set(feedback, "pickupSfx", sfxPickup);
        Set(feedback, "stackFullSfx", sfxStackFull);
        Set(feedback, "spawnSfx", sfxSpawn);

        var gameConfig = LoadOrCreate<GameConfig>(DataDir + "/Config/GameConfig.asset");
        Set(gameConfig, "feedback", feedback);
        var playerConfig = LoadOrCreate<PlayerConfig>(DataDir + "/Config/PlayerConfig.asset");

        // ---------- Items ----------
        var scrapItem = LoadOrCreate<ItemDefinition>(DataDir + "/Items/Item_Scrap.asset");
        var scrapPiecePrefab = SavePrefab(BuildScrapPiece(mScrapRust, new[] { mScrapRust, mScrapSteel, mScrapPaint, mScrapRust }),
            PrefabDir + "/Items/Item_ScrapPiece.prefab").GetComponent<WorldItem>();
        SetMany(scrapItem, ("id", "scrap"), ("displayName", "Scrap"), ("color", new Color(1f, 0.78f, 0.3f)),
            ("prefab", scrapPiecePrefab), ("stackHeight", 0.3f), ("baseValue", 2));

        // ---------- Scrap ----------
        var car = LoadOrCreate<ScrapDefinition>(DataDir + "/Scrap/Scrap_CarWreck.asset");
        var barrel = LoadOrCreate<ScrapDefinition>(DataDir + "/Scrap/Scrap_Barrel.asset");
        var tires = LoadOrCreate<ScrapDefinition>(DataDir + "/Scrap/Scrap_TireStack.asset");

        var carPrefab = SavePrefab(BuildCar(car, mCarPaint, mCarPaintDark, mGlass, mRust, mMetalDark, mRubber, mBarBg, mBarFill),
            PrefabDir + "/Scrap/Scrap_CarWreck.prefab").GetComponent<ScrapObject>();
        var barrelPrefab = SavePrefab(BuildBarrel(barrel, mBarrelRed, mMetalDark, mBarBg, mBarFill),
            PrefabDir + "/Scrap/Scrap_Barrel.prefab").GetComponent<ScrapObject>();
        var tirePrefab = SavePrefab(BuildTireStack(tires, mRubber, mMetalDark, mBarBg, mBarFill),
            PrefabDir + "/Scrap/Scrap_TireStack.prefab").GetComponent<ScrapObject>();

        // Tutorial wreck: blueprint "cut first car" at minute 0-5. Medium drop so the first cut is a real payoff.
        SetMany(car, ("id", "car_wreck"), ("displayName", "Car Wreck"), ("tier", 1), ("sizeClass", (int)ScrapSizeClass.Medium),
            ("prefab", carPrefab), ("maxHealth", 220f), ("minCutPower", 0f), ("dropItem", scrapItem),
            ("dropAmount", new Vector2Int(18, 22)), ("partDropShare", 0.35f), ("dropLaunchSpeed", 7f), ("dropSpread", 2f),
            ("xpReward", 10), ("respawnDelay", 12f), ("breakVfxScale", 1.5f), ("breakShake", 0.45f));
        SetMany(barrel, ("id", "barrel"), ("displayName", "Barrel"), ("tier", 1), ("sizeClass", (int)ScrapSizeClass.Small),
            ("prefab", barrelPrefab), ("maxHealth", 50f), ("minCutPower", 0f), ("dropItem", scrapItem),
            ("dropAmount", new Vector2Int(4, 6)), ("partDropShare", 0.3f), ("dropLaunchSpeed", 5.5f), ("dropSpread", 0.8f),
            ("xpReward", 2), ("respawnDelay", 6f), ("breakVfxScale", 0.8f), ("breakShake", 0.2f));
        SetMany(tires, ("id", "tire_stack"), ("displayName", "Tire Stack"), ("tier", 1), ("sizeClass", (int)ScrapSizeClass.Small),
            ("prefab", tirePrefab), ("maxHealth", 40f), ("minCutPower", 0f), ("dropItem", scrapItem),
            ("dropAmount", new Vector2Int(4, 5)), ("partDropShare", 0.4f), ("dropLaunchSpeed", 5f), ("dropSpread", 0.8f),
            ("xpReward", 2), ("respawnDelay", 6f), ("hitSfx", sfxRubberHit), ("breakSfx", sfxRubberBreak),
            ("breakVfxScale", 0.8f), ("breakShake", 0.2f));

        // ---------- UI / Player ----------
        var outline = AssetDatabase.LoadAssetAtPath<Material>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Outline.mat");
        SavePrefab(BuildFloatingText(outline), PrefabDir + "/UI/FloatingText.prefab");
        SavePrefab(BuildPlayer(playerConfig, outline, mOveralls, mOverallsDark, mSkin, mHardHat, mSawOrange, mBlade, mMetalDark),
            PrefabDir + "/Player/Player.prefab");

        AssetDatabase.SaveAssets();
        Log.AppendLine($"Car cut time @ base DPS {playerConfig.CutPower * playerConfig.CutRate}: {car.EstimateCutTime(playerConfig.CutPower * playerConfig.CutRate):0.0}s");
        return Log.ToString();
    }

    // =====================================================================================
    // Prefab builders
    // =====================================================================================

    static GameObject BuildScrapPiece(Material baseMat, Material[] variants)
    {
        var root = new GameObject("Item_ScrapPiece");
        var cube = Prim(PrimitiveType.Cube, "Visual", root.transform, Vector3.zero, new Vector3(0.36f, 0.26f, 0.36f), baseMat);
        var chip = Prim(PrimitiveType.Cube, "Chip", cube.transform, new Vector3(0.25f, 0.45f, 0.1f), new Vector3(0.5f, 0.6f, 0.6f), baseMat,
            new Vector3(20f, 35f, 10f));
        chip.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        var item = root.AddComponent<WorldItem>();
        SetMany(item, ("halfHeight", 0.13f), ("visual", cube.GetComponent<Renderer>()));
        SetArray(item, "materialVariants", variants);
        return root;
    }

    static GameObject BuildCar(ScrapDefinition def, Material paint, Material paintDark, Material glass, Material rust, Material metal,
        Material rubber, Material barBg, Material barFill)
    {
        var root = ScrapRoot("Scrap_CarWreck", def, new Vector3(0f, 0.75f, 0f), new Vector3(2.0f, 1.5f, 4.4f), out var visual);
        visual.localRotation = Quaternion.Euler(0f, 0f, 3f);

        Prim(PrimitiveType.Cube, "Body", visual, new Vector3(0f, 0.6f, 0f), new Vector3(1.8f, 0.6f, 4.2f), paint);
        Prim(PrimitiveType.Cube, "Cabin", visual, new Vector3(0f, 1.17f, -0.25f), new Vector3(1.6f, 0.55f, 2.0f), paintDark);
        Prim(PrimitiveType.Cube, "Windows", visual, new Vector3(0f, 1.2f, -0.25f), new Vector3(1.64f, 0.32f, 1.7f), glass);
        Prim(PrimitiveType.Cube, "BumperFront", visual, new Vector3(0f, 0.38f, 2.12f), new Vector3(1.85f, 0.2f, 0.2f), metal);
        Prim(PrimitiveType.Cube, "BumperRear", visual, new Vector3(0f, 0.38f, -2.12f), new Vector3(1.85f, 0.2f, 0.2f), metal);
        Prim(PrimitiveType.Cube, "RustRoof", visual, new Vector3(0.3f, 1.455f, -0.4f), new Vector3(0.7f, 0.02f, 0.9f), rust);
        Prim(PrimitiveType.Cube, "RustSide", visual, new Vector3(0.905f, 0.55f, -1.3f), new Vector3(0.02f, 0.35f, 0.8f), rust);

        Wheel(visual, "WheelFR", new Vector3(0.88f, 0.36f, 1.35f), rubber, metal);
        Wheel(visual, "WheelRL", new Vector3(-0.88f, 0.36f, -1.35f), rubber, metal);
        Wheel(visual, "WheelRR", new Vector3(0.88f, 0.36f, -1.35f), rubber, metal);

        Part(Prim(PrimitiveType.Cube, "Hood", visual, new Vector3(0f, 0.95f, 1.45f), new Vector3(1.7f, 0.1f, 1.2f), paint), 0);
        Part(Prim(PrimitiveType.Cube, "DoorL", visual, new Vector3(-0.93f, 0.62f, -0.1f), new Vector3(0.08f, 0.5f, 1.1f), paintDark), 1);
        Part(Prim(PrimitiveType.Cube, "Trunk", visual, new Vector3(0f, 0.95f, -1.7f), new Vector3(1.7f, 0.1f, 0.8f), paint), 2);
        Part(Prim(PrimitiveType.Cube, "DoorR", visual, new Vector3(0.93f, 0.62f, -0.1f), new Vector3(0.08f, 0.5f, 1.1f), paintDark), 3);
        Part(Wheel(visual, "WheelFL", new Vector3(-0.88f, 0.36f, 1.35f), rubber, metal), 4);

        HealthBar(root.transform, 2.3f, 1.6f, barBg, barFill);
        return root;
    }

    static GameObject BuildBarrel(ScrapDefinition def, Material paint, Material metal, Material barBg, Material barFill)
    {
        var root = ScrapRoot("Scrap_Barrel", def, new Vector3(0f, 0.575f, 0f), new Vector3(0.9f, 1.15f, 0.9f), out var visual);
        Prim(PrimitiveType.Cylinder, "Drum", visual, new Vector3(0f, 0.55f, 0f), new Vector3(0.85f, 0.55f, 0.85f), paint);
        Prim(PrimitiveType.Cylinder, "RingLow", visual, new Vector3(0f, 0.35f, 0f), new Vector3(0.88f, 0.03f, 0.88f), metal);
        Prim(PrimitiveType.Cylinder, "RingHigh", visual, new Vector3(0f, 0.8f, 0f), new Vector3(0.88f, 0.03f, 0.88f), metal);
        Part(Prim(PrimitiveType.Cylinder, "Lid", visual, new Vector3(0f, 1.12f, 0f), new Vector3(0.8f, 0.04f, 0.8f), metal), 0);
        HealthBar(root.transform, 1.7f, 1.0f, barBg, barFill);
        return root;
    }

    static GameObject BuildTireStack(ScrapDefinition def, Material rubber, Material metal, Material barBg, Material barFill)
    {
        var root = ScrapRoot("Scrap_TireStack", def, new Vector3(0f, 0.4f, 0f), new Vector3(1.1f, 0.8f, 1.1f), out var visual);
        Prim(PrimitiveType.Cylinder, "TireLow", visual, new Vector3(0f, 0.17f, 0f), new Vector3(1.05f, 0.17f, 1.05f), rubber);
        Prim(PrimitiveType.Cylinder, "HubLow", visual, new Vector3(0f, 0.175f, 0f), new Vector3(0.45f, 0.18f, 0.45f), metal);
        var top = Prim(PrimitiveType.Cylinder, "TireTop", visual, new Vector3(0.06f, 0.52f, -0.04f), new Vector3(1.0f, 0.17f, 1.0f), rubber,
            new Vector3(4f, 0f, 3f));
        Prim(PrimitiveType.Cylinder, "HubTop", top.transform, Vector3.zero, new Vector3(0.45f, 1.03f, 0.45f), metal);
        Part(top, 0);
        HealthBar(root.transform, 1.4f, 1.0f, barBg, barFill);
        return root;
    }

    static GameObject BuildFloatingText(Material outline)
    {
        var go = new GameObject("FloatingText");
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = "+10";
        tmp.fontSize = 7f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        if (outline != null) tmp.fontSharedMaterial = outline;
        tmp.rectTransform.sizeDelta = new Vector2(6f, 2f);
        tmp.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    static GameObject BuildPlayer(PlayerConfig config, Material outline, Material overalls, Material overallsDark, Material skin,
        Material hardHat, Material sawOrange, Material blade, Material metal)
    {
        var root = new GameObject("Player");
        root.layer = LayerMask.NameToLayer("Characters");

        var cc = root.AddComponent<CharacterController>();
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.height = 1.8f;
        cc.radius = 0.4f;
        cc.stepOffset = 0.3f;
        cc.skinWidth = 0.05f;

        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        Prim(PrimitiveType.Capsule, "Torso", body, new Vector3(0f, 0.98f, 0f), new Vector3(0.75f, 0.52f, 0.55f), overalls);
        Prim(PrimitiveType.Capsule, "LegL", body, new Vector3(-0.17f, 0.35f, 0f), new Vector3(0.26f, 0.36f, 0.26f), overallsDark);
        Prim(PrimitiveType.Capsule, "LegR", body, new Vector3(0.17f, 0.35f, 0f), new Vector3(0.26f, 0.36f, 0.26f), overallsDark);
        Prim(PrimitiveType.Sphere, "Head", body, new Vector3(0f, 1.62f, 0.02f), Vector3.one * 0.5f, skin);
        Prim(PrimitiveType.Sphere, "Helmet", body, new Vector3(0f, 1.76f, 0.02f), new Vector3(0.56f, 0.36f, 0.56f), hardHat);
        Prim(PrimitiveType.Cylinder, "Brim", body, new Vector3(0f, 1.7f, 0.08f), new Vector3(0.66f, 0.02f, 0.66f), hardHat);
        Prim(PrimitiveType.Cube, "CarryRack", body, new Vector3(0f, 1.0f, -0.34f), new Vector3(0.55f, 0.6f, 0.08f), metal);
        Prim(PrimitiveType.Capsule, "ArmL", body, new Vector3(-0.45f, 1.0f, 0.08f), new Vector3(0.18f, 0.3f, 0.18f), overalls,
            new Vector3(20f, 0f, 10f));

        var toolPivot = new GameObject("ToolPivot").transform;
        toolPivot.SetParent(body, false);
        toolPivot.localPosition = new Vector3(0.38f, 1.0f, 0.18f);
        Prim(PrimitiveType.Capsule, "ArmR", toolPivot, new Vector3(0.02f, -0.05f, 0.1f), new Vector3(0.18f, 0.3f, 0.18f), overalls,
            new Vector3(70f, 0f, 0f));
        Prim(PrimitiveType.Cube, "SawBody", toolPivot, new Vector3(0f, -0.05f, 0.4f), new Vector3(0.2f, 0.22f, 0.5f), sawOrange);
        var bladeGo = Prim(PrimitiveType.Cylinder, "Blade", toolPivot, new Vector3(0.13f, -0.05f, 0.62f), new Vector3(0.62f, 0.012f, 0.62f), blade,
            new Vector3(0f, 0f, 90f));
        var tip = new GameObject("ToolTip").transform;
        tip.SetParent(toolPivot, false);
        tip.localPosition = new Vector3(0.13f, -0.05f, 0.95f);

        var stackAnchor = new GameObject("StackAnchor").transform;
        stackAnchor.SetParent(root.transform, false);
        stackAnchor.localPosition = new Vector3(0f, 0.75f, -0.55f);

        var indicatorGo = new GameObject("StackIndicator");
        indicatorGo.transform.SetParent(root.transform, false);
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(indicatorGo.transform, false);
        var label = labelGo.AddComponent<TextMeshPro>();
        label.text = "MAX";
        label.fontSize = 6f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, 0.35f, 0.25f);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        if (outline != null) label.fontSharedMaterial = outline;
        label.rectTransform.sizeDelta = new Vector2(4f, 1.5f);
        labelGo.SetActive(false);

        var stats = root.AddComponent<PlayerStats>();
        var input = root.AddComponent<PlayerInputReader>();
        var harvest = root.AddComponent<HarvestTool>();
        var controller = root.AddComponent<PlayerController>();
        var stack = root.AddComponent<CarryStack>();
        var collector = root.AddComponent<ItemCollector>();
        var character = root.AddComponent<PlayerCharacter>();
        var visuals = root.AddComponent<PlayerVisuals>();
        var indicator = indicatorGo.AddComponent<CarryStackIndicator>();

        Set(stats, "config", config);
        Set(harvest, "toolTip", tip);
        SetMany(controller, ("input", input), ("stats", stats), ("harvestTool", harvest));
        SetMany(stack, ("stackRoot", stackAnchor), ("capacity", config.CarryCapacity));
        SetMany(collector, ("stack", stack), ("radius", config.PickupRadius));
        SetMany(character, ("stats", stats), ("controller", controller), ("harvestTool", harvest), ("carryStack", stack), ("itemCollector", collector));
        SetMany(visuals, ("controller", controller), ("harvestTool", harvest), ("body", body), ("toolPivot", toolPivot), ("blade", bladeGo.transform));
        SetMany(indicator, ("stack", stack), ("label", label));
        return root;
    }

    static GameObject ScrapRoot(string name, ScrapDefinition def, Vector3 colliderCenter, Vector3 colliderSize, out Transform visual)
    {
        var root = new GameObject(name);
        root.layer = LayerMask.NameToLayer("Scrap");
        var box = root.AddComponent<BoxCollider>();
        box.center = colliderCenter;
        box.size = colliderSize;
        visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        var scrap = root.AddComponent<ScrapObject>();
        SetMany(scrap, ("definition", def), ("visualRoot", visual), ("hitCollider", box));
        return root;
    }

    static GameObject Wheel(Transform parent, string name, Vector3 position, Material rubber, Material metal)
    {
        var wheel = Prim(PrimitiveType.Cylinder, name, parent, position, new Vector3(0.72f, 0.13f, 0.72f), rubber, new Vector3(0f, 0f, 90f));
        Prim(PrimitiveType.Cylinder, "Hub", wheel.transform, Vector3.zero, new Vector3(0.5f, 1.05f, 0.5f), metal);
        return wheel;
    }

    static void Part(GameObject go, int order) => Set(go.AddComponent<ScrapPart>(), "detachOrder", order);

    static void HealthBar(Transform parent, float height, float width, Material bg, Material fill)
    {
        var bar = new GameObject("HealthBar");
        bar.transform.SetParent(parent, false);
        bar.transform.localPosition = new Vector3(0f, height, 0f);
        bar.transform.localScale = new Vector3(width, 0.16f, 1f);
        var bgGo = Prim(PrimitiveType.Quad, "Bg", bar.transform, Vector3.zero, Vector3.one, bg);
        var fillGo = Prim(PrimitiveType.Quad, "Fill", bar.transform, new Vector3(0f, 0f, -0.01f), new Vector3(1f, 1f, 1f), fill);
        fillGo.transform.localScale = new Vector3(0.96f, 0.7f, 1f);
        foreach (var r in new[] { bgGo.GetComponent<Renderer>(), fillGo.GetComponent<Renderer>() })
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // Fill is wrapped so WorldHealthBar can scale X from 0..1 while the inner quad keeps its margin.
        var fillRoot = new GameObject("FillRoot").transform;
        fillRoot.SetParent(bar.transform, false);
        fillRoot.localPosition = new Vector3(0f, 0f, -0.01f);
        fillGo.transform.SetParent(fillRoot, false);
        fillGo.transform.localPosition = Vector3.zero;

        var hb = bar.AddComponent<WorldHealthBar>();
        SetMany(hb, ("fill", fillRoot), ("fillRenderer", fillGo.GetComponent<Renderer>()));
        bar.SetActive(false);

        var scrap = parent.GetComponent<ScrapObject>();
        if (scrap != null) Set(scrap, "healthBar", hb);
    }

    // ---------- VFX ----------

    static GameObject BuildHitSparks(Material mat)
    {
        var go = new GameObject("VFX_HitSparks");
        var ps = go.AddComponent<ParticleSystem>();
        ConfigureSparks(ps, mat, 16, 24, 6f, 13f, 40f, 0.18f, 0.4f);
        return go;
    }

    static GameObject BuildBreakBurst(Material dust, Material sparks)
    {
        var go = new GameObject("VFX_BreakBurst");
        var ps = go.AddComponent<ParticleSystem>();
        Stop(ps);
        var main = ps.main;
        main.duration = 0.6f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.62f, 0.54f, 0.44f, 0.75f), new Color(0.45f, 0.4f, 0.35f, 0.6f));
        main.gravityModifier = -0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 64;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 16, 22) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.7f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 1.5f;
        limit.dampen = 0.15f;
        FadeOut(ps);
        GrowOverLifetime(ps, 0.7f, 1.5f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = dust;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        var sparksGo = new GameObject("Sparks");
        sparksGo.transform.SetParent(go.transform, false);
        var sparksPs = sparksGo.AddComponent<ParticleSystem>();
        ConfigureSparks(sparksPs, sparks, 26, 36, 8f, 15f, 70f, 0.2f, 0.45f);
        sparksGo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        return go;
    }

    static GameObject BuildSpawnDust(Material dust)
    {
        var go = new GameObject("VFX_SpawnDust");
        var ps = go.AddComponent<ParticleSystem>();
        Stop(ps);
        var main = ps.main;
        main.duration = 0.4f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startColor = new Color(0.66f, 0.58f, 0.46f, 0.7f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 32;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.9f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        FadeOut(ps);
        GrowOverLifetime(ps, 0.8f, 1.4f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = dust;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    static void ConfigureSparks(ParticleSystem ps, Material mat, short min, short max, float speedMin, float speedMax, float angle,
        float lifeMin, float lifeMax)
    {
        Stop(ps);
        var main = ps.main;
        main.duration = 0.3f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.17f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.92f, 0.5f), new Color(1f, 0.55f, 0.12f));
        main.gravityModifier = 1.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 64;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, min, max) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = 0.05f;
        GrowOverLifetime(ps, 1f, 0f);
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.05f;
        r.lengthScale = 2f;
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    static void Stop(ParticleSystem ps) => ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

    static void FadeOut(ParticleSystem ps)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    static void GrowOverLifetime(ParticleSystem ps, float from, float to)
    {
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, from, 1f, to));
    }

    // =====================================================================================
    // Asset helpers
    // =====================================================================================

    static Material Lit(string name, Color color, float smoothness, float metallic = 0f)
    {
        var m = LoadOrCreateMaterial(name, "Universal Render Pipeline/Lit");
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", metallic);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Unlit(string name, Color color)
    {
        var m = LoadOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
        m.SetColor("_BaseColor", color);
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
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material LoadOrCreateMaterial(string name, string shader)
    {
        string path = $"{MatDir}/M_{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find(shader)) { name = "M_" + name };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Texture2D SoftCircleTexture()
    {
        string path = TexDir + "/T_SoftCircle.png";
        if (!File.Exists(path))
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3f - 2f * a);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static SfxDefinition Sfx(string name, ProceduralSfxPreset preset, float volume, Vector2 pitch, float interval)
    {
        var sfx = LoadOrCreate<SfxDefinition>($"{DataDir}/Audio/Sfx_{name}.asset");
        SetMany(sfx, ("fallback", (int)preset), ("volume", volume), ("pitchRange", pitch), ("minInterval", interval));
        return sfx;
    }

    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    static GameObject SavePrefab(GameObject go, string path)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
        Object.DestroyImmediate(go);
        Log.AppendLine((ok ? "Prefab " : "FAILED prefab ") + path);
        return prefab;
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material,
        Vector3? euler = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        if (euler.HasValue) go.transform.localRotation = Quaternion.Euler(euler.Value);
        go.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    // SerializedObject setters keep private [SerializeField] encapsulation intact.
    static void Set(Object target, string field, object value) => SetMany(target, (field, value));

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

            switch (value)
            {
                case Object o: p.objectReferenceValue = o; break;
                case float f: p.floatValue = f; break;
                case int i when p.propertyType == SerializedPropertyType.Enum: p.enumValueIndex = i; break;
                case int i: p.intValue = i; break;
                case bool b: p.boolValue = b; break;
                case string s: p.stringValue = s; break;
                case Color c: p.colorValue = c; break;
                case Vector2 v2: p.vector2Value = v2; break;
                case Vector2Int v2i: p.vector2IntValue = v2i; break;
                case Vector3 v3: p.vector3Value = v3; break;
                default: Log.AppendLine($"!! unsupported value for {field}"); break;
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void SetArray(Object target, string field, Object[] values)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}

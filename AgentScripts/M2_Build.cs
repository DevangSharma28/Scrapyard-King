using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ScrapYardKing.CameraSystem;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Factory;
using ScrapYardKing.Feedback;
using ScrapYardKing.Harvest;
using ScrapYardKing.Items;
using ScrapYardKing.Player;
using ScrapYardKing.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Milestone 2 content builder. Entry points (run in order): Assets, Prefabs, Scene.
// Idempotent: assets are updated in place so GUID references survive re-runs. Scene() rebuilds the scene from scratch.
public static class M2_Build
{
    const string P = "Assets/_Project";
    const string MatDir = P + "/Art/Materials";
    const string TexDir = P + "/Art/Textures";
    const string MeshDir = P + "/Art/Meshes";
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
        foreach (var d in new[] { MatDir, TexDir, MeshDir, AnimDir, FontDir, DataDir + "/Economy", DataDir + "/Factory", DataDir + "/Items",
                     DataDir + "/Audio", PrefabDir + "/Items", PrefabDir + "/Stations", PrefabDir + "/VFX", PrefabDir + "/Economy" })
            Directory.CreateDirectory(d);
        AssetDatabase.Refresh();

        ConfigureCharacterClips();

        // Ground textures (tileable, procedural).
        NoiseTexture("T_Dirt", new Color(0.63f, 0.5f, 0.35f), new Color(0.5f, 0.39f, 0.27f), 6, new Color(0.36f, 0.28f, 0.2f), 0.012f, 11);
        NoiseTexture("T_Grass", new Color(0.42f, 0.62f, 0.3f), new Color(0.33f, 0.52f, 0.24f), 5, new Color(0.5f, 0.7f, 0.35f), 0.01f, 23);
        NoiseTexture("T_Asphalt", new Color(0.27f, 0.28f, 0.3f), new Color(0.22f, 0.23f, 0.25f), 8, new Color(0.4f, 0.4f, 0.42f), 0.02f, 37);
        NoiseTexture("T_Concrete", new Color(0.66f, 0.66f, 0.64f), new Color(0.58f, 0.58f, 0.57f), 4, new Color(0.5f, 0.5f, 0.5f), 0.006f, 51);

        var font = GroboldFont();
        TextMaterial(font, "GROBOLD Outline", 0.28f, new Color(0.08f, 0.08f, 0.12f), true);
        TextMaterial(font, "GROBOLD World", 0.32f, new Color(0.08f, 0.08f, 0.12f), false);

        // Baked item meshes.
        var debris = new List<Mesh>();
        foreach (var n in new[] { "debris-plate-small-a", "debris-plate-small-b", "debris-plate-a", "debris-bolt", "debris-nut", "debris-spoiler-a" })
            debris.Add(BakeModel($"{K}/CarKit/{n}.fbx", "Scrap_" + n, new Vector3(0.46f, 0.2f, 0.46f)));
        var baleMesh = BakeBale();
        var cashMesh = BakeCash();

        var carColormap = KenneyMaterial("CarKit");
        var mSteel = Lit("BaleSteel", new Color(0.62f, 0.66f, 0.7f), 0.55f, 0.75f);
        var mSteelBlue = Lit("BaleSteelBlue", new Color(0.5f, 0.6f, 0.72f), 0.55f, 0.75f);
        var mSteelWarm = Lit("BaleSteelWarm", new Color(0.7f, 0.62f, 0.55f), 0.5f, 0.7f);
        var mStrap = Lit("BaleStrap", new Color(0.12f, 0.12f, 0.14f), 0.3f, 0.2f);
        var mCash = Lit("Cash", new Color(0.35f, 0.78f, 0.3f), 0.35f);
        var mCashBand = Lit("CashBand", new Color(0.98f, 0.93f, 0.75f), 0.3f);

        // ---------- Items ----------
        var scrap = Load<ItemDefinition>(DataDir + "/Items/Item_Scrap.asset");
        var scrapPrefab = SavePrefab(BuildItem("Item_ScrapPiece", debris[0], new[] { carColormap }, debris.ToArray(), null, 0.1f),
            PrefabDir + "/Items/Item_ScrapPiece.prefab").GetComponent<WorldItem>();
        SetMany(scrap, ("prefab", scrapPrefab), ("stackHeight", 0.26f), ("baseValue", 0));

        var metal = LoadOrCreate<ItemDefinition>(DataDir + "/Items/Item_MixedMetal.asset");
        var metalPrefab = SavePrefab(BuildItem("Item_MixedMetal", baleMesh, new[] { mSteel, mStrap }, null, new[] { mSteel, mSteelBlue, mSteelWarm }, 0.17f),
            PrefabDir + "/Items/Item_MixedMetal.prefab").GetComponent<WorldItem>();
        SetMany(metal, ("id", "mixed_metal"), ("displayName", "Mixed Metal"), ("color", new Color(0.6f, 0.85f, 1f)), ("prefab", metalPrefab),
            ("stackHeight", 0.36f), ("baseValue", 6));

        var cashBundle = new GameObject("CashBundle");
        var cashVisual = new GameObject("Visual");
        cashVisual.transform.SetParent(cashBundle.transform, false);
        cashVisual.AddComponent<MeshFilter>().sharedMesh = cashMesh;
        var cashRenderer = cashVisual.AddComponent<MeshRenderer>();
        cashRenderer.sharedMaterials = new[] { mCash, mCashBand };
        SavePrefab(cashBundle, PrefabDir + "/Economy/CashBundle.prefab");

        // ---------- Audio ----------
        Sfx("MachineCycle", ProceduralSfxPreset.Clunk, 0.35f, new Vector2(0.75f, 0.85f), 0.15f);
        Sfx("MachineOutput", ProceduralSfxPreset.Pop, 0.3f, new Vector2(0.6f, 0.7f), 0.1f);
        Sfx("Sale", ProceduralSfxPreset.Coin, 0.6f, new Vector2(0.95f, 1.05f), 0.1f);
        Sfx("CashCollect", ProceduralSfxPreset.Coin, 0.22f, new Vector2(1.1f, 1.15f), 0.02f);
        Sfx("PadTransfer", ProceduralSfxPreset.Pop, 0.28f, new Vector2(0.9f, 0.95f), 0.02f);

        // ---------- VFX ----------
        var smokeTex = LoadTexture($"{K}/ParticlePack/smoke_04.png");
        var mSmoke = ParticleMaterial("Smoke", smokeTex, new Color(1f, 1f, 1f, 1f), false);
        SavePrefab(BuildSmoke("VFX_CrusherDust", mSmoke, 9f, new Color(0.7f, 0.65f, 0.58f, 0.55f), 0.6f, 1.3f, 1.2f),
            PrefabDir + "/VFX/VFX_CrusherDust.prefab");
        SavePrefab(BuildSmoke("VFX_ChainsawSmoke", mSmoke, 14f, new Color(0.55f, 0.55f, 0.58f, 0.5f), 0.12f, 0.35f, 0.6f),
            PrefabDir + "/VFX/VFX_ChainsawSmoke.prefab");

        // ---------- Data ----------
        var economy = LoadOrCreate<EconomyConfig>(DataDir + "/Economy/EconomyConfig.asset");
        SetMany(economy, ("startingCash", 0L), ("startingPremium", 0L), ("cashBundleValue", 5));

        var crusher = LoadOrCreate<MachineDefinition>(DataDir + "/Factory/Machine_Crusher.asset");
        SetMany(crusher, ("id", "crusher"), ("displayName", "Crusher"), ("input", scrap), ("output", metal),
            ("cycleSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_MachineCycle.asset")),
            ("outputSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_MachineOutput.asset")));
        // Blueprint: Crusher Lv.1 = 10 scrap input / 8 sec.
        SetStructArray(crusher, "levels", new[]
        {
            new Dictionary<string, object> { ["inputCapacity"] = 10, ["cycleTime"] = 0.8f, ["inputsPerCycle"] = 1, ["outputsPerCycle"] = 1, ["upgradeCost"] = 0 },
            new Dictionary<string, object> { ["inputCapacity"] = 14, ["cycleTime"] = 0.65f, ["inputsPerCycle"] = 1, ["outputsPerCycle"] = 1, ["upgradeCost"] = 150 },
            new Dictionary<string, object> { ["inputCapacity"] = 20, ["cycleTime"] = 0.5f, ["inputsPerCycle"] = 1, ["outputsPerCycle"] = 1, ["upgradeCost"] = 400 },
            new Dictionary<string, object> { ["inputCapacity"] = 26, ["cycleTime"] = 0.4f, ["inputsPerCycle"] = 1, ["outputsPerCycle"] = 1, ["upgradeCost"] = 900 },
            new Dictionary<string, object> { ["inputCapacity"] = 32, ["cycleTime"] = 0.32f, ["inputsPerCycle"] = 1, ["outputsPerCycle"] = 1, ["upgradeCost"] = 1800 },
        });

        var storage = LoadOrCreate<StorageDefinition>(DataDir + "/Factory/Storage_Yard.asset");
        SetMany(storage, ("id", "storage_yard"), ("displayName", "Storage"));
        SetStructArray(storage, "levels", new[]
        {
            new Dictionary<string, object> { ["capacity"] = 40, ["pickupInterval"] = 0.07f, ["upgradeCost"] = 0 },
            new Dictionary<string, object> { ["capacity"] = 60, ["pickupInterval"] = 0.06f, ["upgradeCost"] = 300 },
            new Dictionary<string, object> { ["capacity"] = 90, ["pickupInterval"] = 0.05f, ["upgradeCost"] = 800 },
        });

        var desk = LoadOrCreate<SellDeskDefinition>(DataDir + "/Factory/SellDesk_Yard.asset");
        SetMany(desk, ("id", "sell_desk"), ("displayName", "Sell"), ("firstSaleDelay", 1.2f), ("saleSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_Sale.asset")));
        // Blueprint: Sell Desk Lv.1 = 1 customer / 8 sec.
        SetStructArray(desk, "levels", new[]
        {
            new Dictionary<string, object> { ["saleInterval"] = 8f, ["unitsPerSale"] = new Vector2Int(2, 4), ["priceMultiplier"] = 1f, ["counterCapacity"] = 12, ["queueCapacity"] = 3, ["upgradeCost"] = 0 },
            new Dictionary<string, object> { ["saleInterval"] = 6.5f, ["unitsPerSale"] = new Vector2Int(2, 5), ["priceMultiplier"] = 1.1f, ["counterCapacity"] = 16, ["queueCapacity"] = 4, ["upgradeCost"] = 250 },
            new Dictionary<string, object> { ["saleInterval"] = 5f, ["unitsPerSale"] = new Vector2Int(3, 6), ["priceMultiplier"] = 1.25f, ["counterCapacity"] = 20, ["queueCapacity"] = 5, ["upgradeCost"] = 700 },
        });

        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    static void ConfigureCharacterClips()
    {
        var loops = new HashSet<string> { "idle", "walk", "sprint", "holding-right", "holding-left", "holding-both", "attack-melee-right", "interact-right", "sit", "static" };
        foreach (var file in Directory.GetFiles(K + "/MiniCharacters", "character-*.fbx"))
        {
            string path = file.Replace('\\', '/');
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            bool dirty = importer.clipAnimations.Length == 0;
            foreach (var clip in clips)
            {
                bool loop = loops.Contains(clip.name);
                if (clip.loopTime == loop) continue;
                clip.loopTime = loop;
                dirty = true;
            }

            if (!dirty) continue;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            Log.AppendLine("clips configured: " + Path.GetFileName(path));
        }
    }

    // =====================================================================================
    // PREFABS
    // =====================================================================================

    public static string Prefabs()
    {
        Log.Clear();
        var font = Load<TMP_FontAsset>(FontDir + "/GROBOLD SDF.asset");
        var worldText = Load<Material>(FontDir + "/GROBOLD World.mat");

        // Palette.
        var mWreck = KenneyMaterial("CarKit", "M_Kenney_CarWreck", new Color(0.76f, 0.68f, 0.6f));
        var mSurvival = KenneyMaterial("SurvivalKit");
        var mFactory = KenneyMaterial("FactoryKit");
        var mRed = Lit("CrusherRed", new Color(0.86f, 0.22f, 0.16f), 0.35f, 0.2f);
        var mRedDark = Lit("CrusherRedDark", new Color(0.55f, 0.13f, 0.1f), 0.3f, 0.2f);
        var mSteelDark = Lit("SteelDark", new Color(0.2f, 0.21f, 0.24f), 0.45f, 0.7f);
        var mSteel = Lit("SteelMid", new Color(0.45f, 0.47f, 0.52f), 0.5f, 0.75f);
        var mHazard = Lit("HazardYellow", new Color(1f, 0.78f, 0.1f), 0.3f);
        var mConcrete = LitTex("ConcretePad", Load<Texture2D>(TexDir + "/T_Concrete.png"), new Vector2(2f, 2f), Color.white, 0.15f);
        var mBinBlue = Lit("BinBlue", new Color(0.18f, 0.45f, 0.8f), 0.35f, 0.3f);
        var mBinBlueDark = Lit("BinBlueDark", new Color(0.12f, 0.28f, 0.52f), 0.35f, 0.3f);
        var mCounter = Lit("CounterYellow", new Color(1f, 0.76f, 0.2f), 0.35f);
        var mWood = Lit("Wood", new Color(0.62f, 0.43f, 0.26f), 0.2f);
        var mAwning = Lit("AwningRed", new Color(0.9f, 0.25f, 0.2f), 0.3f);
        var mWhite = Lit("OffWhite", new Color(0.95f, 0.93f, 0.88f), 0.3f);
        var mScreen = Emissive("ScreenGreen", new Color(0.3f, 1f, 0.45f), 1.5f);
        var mLight = Emissive("StatusLight", new Color(1f, 0.8f, 0.2f), 2f);
        var mBarBg = Load<Material>(MatDir + "/M_BarBg.mat");
        var mBarFill = Load<Material>(MatDir + "/M_BarFill.mat");
        var mOrange = Lit("SawOrange", new Color(1f, 0.48f, 0.1f), 0.4f);
        var mBlade = Lit("Blade", new Color(0.8f, 0.82f, 0.85f), 0.8f, 0.9f);
        var mBlack = Lit("RubberBlack", new Color(0.09f, 0.09f, 0.1f), 0.25f);

        // ---------- Scrap ----------
        var carDef = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_CarWreck.asset");
        var barrelDef = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_Barrel.asset");
        var tireDef = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_TireStack.asset");

        var carVariants = new List<ScrapObject>();
        foreach (var model in new[] { "sedan", "suv", "hatchback-sports" })
            carVariants.Add(SavePrefab(BuildCarWreck(model, carDef, mWreck, mBarBg, mBarFill), $"{PrefabDir}/Scrap/Scrap_Car_{model}.prefab").GetComponent<ScrapObject>());
        var barrel = SavePrefab(BuildBarrel(barrelDef, mSurvival, mBarBg, mBarFill), PrefabDir + "/Scrap/Scrap_Barrel.prefab").GetComponent<ScrapObject>();
        var tires = SavePrefab(BuildTires(tireDef, mBarBg, mBarFill), PrefabDir + "/Scrap/Scrap_TireStack.prefab").GetComponent<ScrapObject>();

        SetMany(carDef, ("prefab", carVariants[0]), ("maxHealth", 220f), ("dropAmount", new Vector2Int(18, 22)));
        SetArray(carDef, "prefabVariants", carVariants.Cast<Object>().ToArray());
        SetMany(barrelDef, ("prefab", barrel));
        SetArray(barrelDef, "prefabVariants", Array.Empty<Object>());
        SetMany(tireDef, ("prefab", tires));
        SetArray(tireDef, "prefabVariants", Array.Empty<Object>());

        // ---------- Stations ----------
        var padPrefab = SavePrefab(BuildPadVisual(), PrefabDir + "/Stations/PadVisual.prefab");
        var label = SavePrefab(BuildStationLabel(font, worldText), PrefabDir + "/Stations/StationLabel.prefab");
        var crusherDef = Load<MachineDefinition>(DataDir + "/Factory/Machine_Crusher.asset");
        var storageDef = Load<StorageDefinition>(DataDir + "/Factory/Storage_Yard.asset");
        var deskDef = Load<SellDeskDefinition>(DataDir + "/Factory/SellDesk_Yard.asset");

        SavePrefab(BuildCrusher(crusherDef, label, padPrefab, mRed, mRedDark, mSteelDark, mSteel, mHazard, mConcrete, mLight, mFactory),
            PrefabDir + "/Stations/Crusher.prefab");
        SavePrefab(BuildStorage(storageDef, label, padPrefab, mBinBlue, mBinBlueDark, mHazard, mConcrete, mSteelDark),
            PrefabDir + "/Stations/Storage.prefab");
        SavePrefab(BuildSellDesk(deskDef, label, padPrefab, mCounter, mWood, mAwning, mWhite, mSteelDark, mScreen, mConcrete, font, worldText),
            PrefabDir + "/Stations/SellDesk.prefab");

        // ---------- Player ----------
        var controller = BuildCharacterController(K + "/MiniCharacters/character-male-b.fbx", AnimDir + "/AC_Player.controller", true);
        SavePrefab(BuildPlayer(controller, font, worldText, mHazard, mOrange, mBlack, mBlade, mSteelDark), PrefabDir + "/Player/Player.prefab");

        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    static GameObject BuildCarWreck(string model, ScrapDefinition def, Material wreck, Material barBg, Material barFill)
    {
        var root = ScrapRoot("Scrap_Car_" + model, def, out var visual);
        var car = Kenney("CarKit", model, visual, new Vector3(0f, 0f, 0f), 1.75f, new Vector3(0f, 0f, 3.5f), wreck);

        // One wheel missing and the body sagging on that corner reads as "wreck" at a glance.
        var missing = car.transform.Find("wheel-back-left");
        if (missing != null) missing.gameObject.SetActive(false);
        int order = 0;
        foreach (var wheel in new[] { "wheel-front-left", "wheel-back-right", "wheel-front-right" })
        {
            var w = car.transform.Find(wheel);
            if (w != null) Part(w.gameObject, order++ * 2);
        }

        var bounds = RendererBounds(car);
        var door = Kenney("CarKit", "debris-door", visual, Vector3.zero, 1.6f, new Vector3(0f, -90f, 0f), wreck);
        door.transform.position = new Vector3(bounds.min.x - 0.02f, 0.55f, bounds.center.z + 0.1f);
        Part(door, 1);
        var bumper = Kenney("CarKit", "debris-bumper", visual, Vector3.zero, 1.7f, Vector3.zero, wreck);
        bumper.transform.position = new Vector3(bounds.center.x, 0.45f, bounds.max.z - 0.05f);
        Part(bumper, 3);

        FitCollider(root, car, 0.95f);
        HealthBar(root.transform, bounds.max.y + 0.7f, 1.6f, barBg, barFill);
        return root;
    }

    static GameObject BuildBarrel(ScrapDefinition def, Material survival, Material barBg, Material barFill)
    {
        var root = ScrapRoot("Scrap_Barrel", def, out var visual);
        var drum = Kenney("SurvivalKit", "barrel", visual, Vector3.zero, 3.4f, Vector3.zero, survival);
        var lid = Kenney("CarKit", "debris-plate-small-a", visual, new Vector3(0.05f, RendererBounds(drum).max.y, 0f), 1.4f, new Vector3(0f, 25f, 8f),
            Load<Material>(MatDir + "/M_Kenney_CarWreck.mat"));
        Part(lid, 0);
        FitCollider(root, drum, 1f);
        HealthBar(root.transform, RendererBounds(drum).max.y + 0.6f, 1f, barBg, barFill);
        return root;
    }

    static GameObject BuildTires(ScrapDefinition def, Material barBg, Material barFill)
    {
        var root = ScrapRoot("Scrap_TireStack", def, out var visual);
        var carMat = KenneyMaterial("CarKit");
        var probe = Kenney("CarKit", "wheel-default", visual, Vector3.zero, 1f, new Vector3(0f, 0f, 90f), carMat);
        var size = RendererBounds(probe).size;
        Object.DestroyImmediate(probe);
        float scale = 1.05f / Mathf.Max(size.x, size.z);
        float thickness = size.y * scale;
        GameObject top = null;
        for (int i = 0; i < 3; i++)
        {
            var offset = new Vector3(i == 1 ? 0.06f : -0.03f * i, thickness * (i + 0.5f), i == 2 ? 0.05f : 0f);
            var tire = Kenney("CarKit", "wheel-default", visual, offset, scale, new Vector3(0f, 0f, 90f), carMat);
            tire.transform.localRotation = Quaternion.Euler(0f, i * 37f, 0f) * Quaternion.Euler(0f, 0f, 90f);
            tire.transform.localPosition = offset;
            if (i > 0) Part(tire, 2 - i);
            top = tire;
        }

        FitCollider(root, visual.gameObject, 1f);
        HealthBar(root.transform, RendererBounds(top).max.y + 0.6f, 1f, barBg, barFill);
        return root;
    }

    static GameObject BuildCrusher(MachineDefinition def, GameObject labelPrefab, GameObject padPrefab, Material red, Material redDark,
        Material steelDark, Material steel, Material hazard, Material concrete, Material lightMat, Material factory)
    {
        var root = new GameObject("Crusher");
        Box("Plinth", root.transform, new Vector3(0f, 0.12f, 0f), new Vector3(3.4f, 0.24f, 3.4f), concrete, 0.04f);

        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        body.localPosition = new Vector3(0f, 0.24f, 0f);
        Box("Housing", body, new Vector3(0f, 0.9f, 0f), new Vector3(2.7f, 1.8f, 2.6f), red, 0.1f);
        Box("Band", body, new Vector3(0f, 0.35f, 0f), new Vector3(2.76f, 0.22f, 2.66f), steelDark, 0.03f);
        Box("TopRim", body, new Vector3(0f, 1.84f, 0f), new Vector3(2.8f, 0.12f, 2.7f), redDark, 0.03f);
        for (int i = -1; i <= 1; i += 2)
        {
            Box("Stripe", body, new Vector3(i * 1.36f, 0.9f, 0f), new Vector3(0.05f, 0.5f, 2.0f), hazard, 0f);
            Box("Piston_" + (i < 0 ? "L" : "R"), body, new Vector3(i * 0.9f, 2.3f, -1.45f), new Vector3(0.34f, 0.7f, 0.34f), steel, 0.04f);
        }

        var hopper = Kenney("FactoryKit", "hopper-square", body, new Vector3(0f, 1.9f, 0f), 1f, Vector3.zero, factory);
        var hb = RendererBounds(hopper).size;
        hopper.transform.localScale = new Vector3(2.5f / hb.x, 1.3f / hb.y, 2.4f / hb.z);

        var rollers = new List<Transform>();
        for (int i = -1; i <= 1; i += 2)
        {
            var roller = Cylinder("Roller", body, new Vector3(0f, 2.05f, i * 0.28f), 0.26f, 1.9f, 10, steelDark);
            roller.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            rollers.Add(roller.transform);
        }

        var cog = Kenney("FactoryKit", "cog-a", body, new Vector3(1.4f, 0.95f, 0.3f), 1f, new Vector3(0f, 0f, 90f), factory);
        var cb = RendererBounds(cog).size;
        cog.transform.localScale = Vector3.one * (1.1f / Mathf.Max(cb.x, cb.y, cb.z));
        rollers.Add(cog.transform);

        var chute = Box("Chute", root.transform, new Vector3(0f, 0.72f, -1.62f), new Vector3(1.0f, 0.1f, 0.9f), steel, 0.02f);
        chute.transform.localRotation = Quaternion.Euler(-22f, 0f, 0f);
        var light = Cylinder("StatusLight", body, new Vector3(1.0f, 1.5f, -1.36f), 0.16f, 0.12f, 12, lightMat);
        light.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var intake = Empty("Intake", body, new Vector3(0f, 1.3f, 0f));
        var output = Empty("OutputPoint", root.transform, new Vector3(0f, 0.72f, -1.95f));
        var hopperPile = Empty("HopperPile", body, new Vector3(0f, 2.15f, 0f)).gameObject.AddComponent<ItemPile>();
        SetMany(hopperPile, ("capacity", 10), ("columns", 3), ("rows", 3), ("cellSize", new Vector3(0.5f, 0.3f, 0.5f)), ("arriveDuration", 0.32f),
            ("arriveArc", 1.6f));

        var dust = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PrefabDir + "/VFX/VFX_CrusherDust.prefab"), body);
        dust.transform.localPosition = new Vector3(0f, 2.9f, 0f);

        var labelGo = (GameObject)PrefabUtility.InstantiatePrefab(labelPrefab, root.transform);
        labelGo.transform.localPosition = new Vector3(0f, 5.4f, 1.5f);
        labelGo.transform.localScale = Vector3.one * 1.4f;

        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 1.6f, 0f);
        box.size = new Vector3(3.0f, 3.2f, 3.0f);

        var visuals = root.AddComponent<MachineVisuals>();
        SetMany(visuals, ("body", body), ("spinAxis", Vector3.up), ("spinSpeed", 420f), ("workParticles", dust.GetComponent<ParticleSystem>()),
            ("statusLight", light.GetComponent<Renderer>()), ("pistonTravel", 0.3f));
        SetArray(visuals, "spinners", rollers.Cast<Object>().ToArray());
        SetArray(visuals, "pistons", new Object[] { body.Find("Piston_L"), body.Find("Piston_R") });

        var machine = root.AddComponent<Machine>();
        SetMany(machine, ("definition", def), ("hopper", hopperPile), ("intake", intake), ("outputPoint", output), ("visuals", visuals),
            ("label", labelGo.GetComponent<StationLabel>()));

        Pad(root.transform, "InputPad", new Vector3(-3.1f, 0f, 0f), new Vector2(2.2f, 2.2f), TransferMode.Deposit, machine, new Color(1f, 0.78f, 0.15f),
            padPrefab, 90f, "Sfx_PadTransfer");
        return root;
    }

    static GameObject BuildStorage(StorageDefinition def, GameObject labelPrefab, GameObject padPrefab, Material blue, Material blueDark, Material hazard,
        Material concrete, Material steelDark)
    {
        var root = new GameObject("Storage");
        Box("Floor", root.transform, new Vector3(0f, 0.1f, 0f), new Vector3(3.6f, 0.2f, 3.0f), concrete, 0.04f);
        Box("WallN", root.transform, new Vector3(0f, 0.5f, 1.42f), new Vector3(3.6f, 0.6f, 0.16f), blue, 0.04f);
        Box("WallS", root.transform, new Vector3(0f, 0.5f, -1.42f), new Vector3(3.6f, 0.6f, 0.16f), blue, 0.04f);
        Box("WallE", root.transform, new Vector3(1.72f, 0.5f, 0f), new Vector3(0.16f, 0.6f, 3.0f), blueDark, 0.04f);
        Box("WallW", root.transform, new Vector3(-1.72f, 0.5f, 0f), new Vector3(0.16f, 0.6f, 3.0f), blueDark, 0.04f);
        Box("RimN", root.transform, new Vector3(0f, 0.82f, 1.42f), new Vector3(3.64f, 0.06f, 0.2f), hazard, 0f);
        Box("RimS", root.transform, new Vector3(0f, 0.82f, -1.42f), new Vector3(3.64f, 0.06f, 0.2f), hazard, 0f);
        foreach (var x in new[] { -1.72f, 1.72f })
        foreach (var z in new[] { -1.42f, 1.42f })
            Box("Post", root.transform, new Vector3(x, 0.55f, z), new Vector3(0.26f, 1.1f, 0.26f), steelDark, 0.03f);

        var pile = Empty("Pile", root.transform, new Vector3(0f, 0.22f, 0f)).gameObject.AddComponent<ItemPile>();
        SetMany(pile, ("capacity", 40), ("columns", 5), ("rows", 4), ("cellSize", new Vector3(0.6f, 0.36f, 0.58f)), ("arriveDuration", 0.3f), ("arriveArc", 0.9f));

        var labelGo = (GameObject)PrefabUtility.InstantiatePrefab(labelPrefab, root.transform);
        labelGo.transform.localPosition = new Vector3(0f, 3.6f, 1.8f);
        labelGo.transform.localScale = Vector3.one * 1.4f;

        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.6f, 0f);
        box.size = new Vector3(3.6f, 1.2f, 3.0f);

        var storage = root.AddComponent<Storage>();
        var pad = Pad(root.transform, "WithdrawPad", new Vector3(-3.1f, 0f, 0f), new Vector2(2.2f, 2.2f), TransferMode.Withdraw, storage,
            new Color(0.35f, 0.9f, 0.35f), padPrefab, 270f, "Sfx_PadTransfer");
        SetMany(storage, ("definition", def), ("pile", pile), ("label", labelGo.GetComponent<StationLabel>()), ("withdrawPad", pad));
        return root;
    }

    static GameObject BuildSellDesk(SellDeskDefinition def, GameObject labelPrefab, GameObject padPrefab, Material counterMat, Material wood,
        Material awning, Material white, Material steelDark, Material screen, Material concrete, TMP_FontAsset font, Material worldText)
    {
        var root = new GameObject("SellDesk");
        Box("Floor", root.transform, new Vector3(0f, 0.06f, 0f), new Vector3(4.6f, 0.12f, 2.4f), concrete, 0.03f);
        Box("Counter", root.transform, new Vector3(0f, 0.55f, 0f), new Vector3(3.8f, 0.95f, 1.1f), counterMat, 0.06f);
        Box("CounterFront", root.transform, new Vector3(0f, 0.55f, -0.56f), new Vector3(3.5f, 0.6f, 0.04f), white, 0f);
        Box("CounterTop", root.transform, new Vector3(0f, 1.07f, 0f), new Vector3(4.0f, 0.1f, 1.3f), wood, 0.03f);
        foreach (var x in new[] { -1.95f, 1.95f })
        foreach (var z in new[] { -0.6f, 0.9f })
            Box("Post", root.transform, new Vector3(x, 1.85f, z), new Vector3(0.14f, 3.7f, 0.14f), steelDark, 0.02f);
        // High canopy so the top-down camera can still see stock on the counter underneath.
        Box("Roof", root.transform, new Vector3(0f, 3.75f, 0.35f), new Vector3(4.4f, 0.16f, 1.9f), counterMat, 0.05f);
        for (int i = 0; i < 6; i++)
            Box("Awning_" + i, root.transform, new Vector3(-1.83f + i * 0.733f, 3.52f, -0.62f), new Vector3(0.733f, 0.34f, 0.08f), i % 2 == 0 ? awning : white, 0f);

        var register = new GameObject("Register").transform;
        register.SetParent(root.transform, false);
        register.localPosition = new Vector3(1.4f, 1.12f, 0.15f);
        Box("RegisterBody", register, new Vector3(0f, 0.18f, 0f), new Vector3(0.6f, 0.36f, 0.45f), steelDark, 0.04f);
        Box("RegisterScreen", register, new Vector3(0f, 0.45f, -0.05f), new Vector3(0.45f, 0.26f, 0.05f), screen, 0f).transform.localRotation =
            Quaternion.Euler(-15f, 0f, 0f);

        var counter = Empty("CounterPile", root.transform, new Vector3(-0.45f, 1.12f, 0.05f)).gameObject.AddComponent<ItemPile>();
        SetMany(counter, ("capacity", 12), ("columns", 4), ("rows", 2), ("cellSize", new Vector3(0.55f, 0.36f, 0.48f)), ("arriveDuration", 0.32f), ("arriveArc", 1.1f));
        var buyer = Empty("BuyerPoint", root.transform, new Vector3(0f, 0.9f, -2.4f));

        // Cash pile on a pallet to the east, collected from the player side.
        var cashRoot = new GameObject("CashPile").transform;
        cashRoot.SetParent(root.transform, false);
        cashRoot.localPosition = new Vector3(3.7f, 0f, 0.1f);
        Box("Pallet", cashRoot, new Vector3(0f, 0.08f, 0f), new Vector3(1.7f, 0.16f, 1.2f), wood, 0.02f);
        var stackRoot = Empty("Stack", cashRoot, new Vector3(0f, 0.22f, 0f));
        var cashPad = PadVisualOnly(root.transform, "CashPad", new Vector3(3.7f, 0f, 2.0f), new Vector2(2.0f, 1.8f), new Color(0.35f, 0.9f, 0.35f),
            padPrefab, font, "$");
        var cashPile = cashRoot.gameObject.AddComponent<CashPile>();
        SetMany(cashPile, ("bundlePrefab", Load<GameObject>(PrefabDir + "/Economy/CashBundle.prefab").transform), ("stackRoot", stackRoot),
            ("columns", 3), ("rows", 2), ("cellSize", new Vector3(0.5f, 0.12f, 0.32f)), ("pad", cashPad), ("padSize", new Vector2(2.0f, 1.8f)),
            ("collectSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_CashCollect.asset")));

        var labelGo = (GameObject)PrefabUtility.InstantiatePrefab(labelPrefab, root.transform);
        labelGo.transform.localPosition = new Vector3(0f, 5.2f, 1.2f);
        labelGo.transform.localScale = Vector3.one * 1.4f;

        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.6f, 0f);
        box.size = new Vector3(4.0f, 1.2f, 1.3f);
        var palletBox = cashRoot.gameObject.AddComponent<BoxCollider>();
        palletBox.center = new Vector3(0f, 0.4f, 0f);
        palletBox.size = new Vector3(1.7f, 0.8f, 1.2f);

        var desk = root.AddComponent<SellDesk>();
        SetMany(desk, ("definition", def), ("counter", counter), ("cashPile", cashPile), ("buyerPoint", buyer), ("register", register),
            ("label", labelGo.GetComponent<StationLabel>()));
        Pad(root.transform, "StockPad", new Vector3(0f, 0f, 2.0f), new Vector2(2.4f, 1.8f), TransferMode.Deposit, desk, new Color(0.3f, 0.65f, 1f),
            padPrefab, 180f, "Sfx_PadTransfer");
        return root;
    }

    static GameObject BuildPadVisual()
    {
        var root = new GameObject("PadVisual");
        var fillMat = TransparentUnlit("PadFill", new Color(1f, 1f, 1f, 0.22f));
        var frameMat = Unlit("PadFrame", Color.white);
        var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fill.name = "Fill";
        Object.DestroyImmediate(fill.GetComponent<Collider>());
        fill.transform.SetParent(root.transform, false);
        fill.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        fill.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        fill.GetComponent<Renderer>().sharedMaterial = fillMat;
        fill.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        foreach (var (n, pos, size) in new[]
                 {
                     ("FrameN", new Vector3(0f, 0.04f, 0.47f), new Vector3(1f, 0.04f, 0.06f)),
                     ("FrameS", new Vector3(0f, 0.04f, -0.47f), new Vector3(1f, 0.04f, 0.06f)),
                     ("FrameE", new Vector3(0.47f, 0.04f, 0f), new Vector3(0.06f, 0.04f, 1f)),
                     ("FrameW", new Vector3(-0.47f, 0.04f, 0f), new Vector3(0.06f, 0.04f, 1f)),
                 })
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = n;
            Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.transform.SetParent(root.transform, false);
            bar.transform.localPosition = pos;
            bar.transform.localScale = size;
            bar.GetComponent<Renderer>().sharedMaterial = frameMat;
            bar.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // Arrow: ProBuilder prism head + shaft, lying flat, pointing +Z (toward the target by default).
        var arrow = new GameObject("Arrow").transform;
        arrow.SetParent(root.transform, false);
        arrow.localPosition = new Vector3(0f, 0.045f, 0f);
        var head = ShapeGenerator.GeneratePrism(PivotLocation.Center, new Vector3(0.36f, 0.26f, 0.02f));
        FinishPb(head, "Head", arrow, new Vector3(0f, 0f, 0.13f), frameMat);
        head.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        FinishPb(ShapeGenerator.GenerateCube(PivotLocation.Center, new Vector3(0.14f, 0.02f, 0.26f)), "Shaft", arrow, new Vector3(0f, 0f, -0.1f), frameMat);
        return root;
    }

    static GameObject BuildStationLabel(TMP_FontAsset font, Material worldText)
    {
        var root = new GameObject("StationLabel");
        var bg = new GameObject("Background");
        bg.transform.SetParent(root.transform, false);
        var sr = bg.AddComponent<SpriteRenderer>();
        sr.sprite = UiSprite(UiSheet2, "UI-pack_Sprite_2_10");
        sr.color = new Color(1f, 1f, 1f, 0.92f);
        bg.transform.localScale = new Vector3(1.1f, 0.8f, 1f);
        bg.transform.localPosition = new Vector3(0f, 0f, 0.02f);

        var title = Text3D("Title", root.transform, new Vector3(-0.15f, 0.12f, 0f), font, worldText, 2.4f, Color.white, TextAlignmentOptions.Center, "CRUSHER");
        var level = Text3D("Level", root.transform, new Vector3(-0.15f, -0.3f, 0f), font, worldText, 2.1f, new Color(0.55f, 1f, 0.35f), TextAlignmentOptions.Center, "Lv.1");
        var counterRoot = new GameObject("Counter").transform;
        counterRoot.SetParent(root.transform, false);
        counterRoot.localPosition = new Vector3(0f, -0.95f, 0f);
        var counter = Text3D("Value", counterRoot, Vector3.zero, font, worldText, 2.3f, Color.white, TextAlignmentOptions.Center, "0/10");

        var label = root.AddComponent<StationLabel>();
        SetMany(label, ("title", title), ("level", level), ("counter", counter), ("counterRoot", counterRoot));
        return root;
    }

    static GameObject BuildPlayer(AnimatorController controller, TMP_FontAsset font, Material worldText, Material hardHat, Material orange,
        Material black, Material blade, Material steelDark)
    {
        var old = Load<GameObject>(PrefabDir + "/Player/Player.prefab");
        var config = old != null ? old.GetComponent<PlayerStats>() : null;
        var playerConfig = Load<PlayerConfig>(DataDir + "/Config/PlayerConfig.asset");

        var root = new GameObject("Player");
        root.layer = LayerMask.NameToLayer("Characters");
        var cc = root.AddComponent<CharacterController>();
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.height = 1.8f;
        cc.radius = 0.42f;
        cc.stepOffset = 0.3f;
        cc.skinWidth = 0.05f;

        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        var model = Kenney("MiniCharacters", "character-male-b", body, Vector3.zero, 2.6f, Vector3.zero, null);
        model.name = "Model";
        if (!model.TryGetComponent(out Animator animator)) animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var clips = AssetDatabase.LoadAllAssetsAtPath(K + "/MiniCharacters/character-male-b.fbx").OfType<AnimationClip>().ToArray();
        clips.First(c => c.name == "idle").SampleAnimation(model, 0f);

        // Hard hat on the head bone, sized from the head mesh's bind-pose bounds (skinned renderer bounds are conservative).
        var headBone = FindDeep(model.transform, "head");
        var headMesh = FindDeep(model.transform, "head-mesh").GetComponent<SkinnedMeshRenderer>();
        var baked = new Mesh();
        headMesh.BakeMesh(baked, true);
        var world = baked.vertices.Select(v => headMesh.transform.TransformPoint(v)).ToArray();
        Object.DestroyImmediate(baked);
        var hb = new Bounds(world[0], Vector3.zero);
        foreach (var v in world) hb.Encapsulate(v);
        Vector3 top = new Vector3(hb.center.x, hb.max.y, hb.center.z);
        Vector3 headSize = hb.size;
        var hat = new GameObject("HardHat").transform;
        hat.SetParent(root.transform, false);
        hat.position = top - Vector3.up * (headSize.y * 0.12f);
        Box("Dome", hat, new Vector3(0f, 0.1f, 0f), new Vector3(headSize.x * 1.06f, 0.2f, headSize.z * 1.06f), hardHat, 0.05f, worldSpace: true);
        Box("Brim", hat, new Vector3(0f, 0.01f, headSize.z * 0.12f), new Vector3(headSize.x * 1.18f, 0.04f, headSize.z * 1.3f), hardHat, 0.015f, worldSpace: true);
        Box("Ridge", hat, new Vector3(0f, 0.21f, 0f), new Vector3(0.1f, 0.05f, headSize.z * 0.95f), hardHat, 0.01f, worldSpace: true);
        hat.SetParent(headBone, true);
        Log.AppendLine($"head size {headSize:F2} top {top:F2}");

        // Pose the rig in the tool-holding pose so the chainsaw is placed where it will be seen in play.
        clips.First(c => c.name == "holding-right").SampleAnimation(model, 0f);

        // Chainsaw in the right hand. Built in world scale, then parented so the bar follows the arm animation.
        var arm = FindDeep(model.transform, "arm-right");
        var saw = new GameObject("Chainsaw").transform;
        saw.SetParent(root.transform, false);
        var armMesh = FindDeep(model.transform, "body-mesh").GetComponent<Renderer>();
        Vector3 shoulder = arm.position;
        Vector3 armDir = arm.TransformDirection(Vector3.right);
        if (Vector3.Dot(armDir, root.transform.right) < 0f && Vector3.Dot(armDir, root.transform.forward) < 0.3f) armDir = -armDir;
        Vector3 hand = shoulder + armDir.normalized * 0.34f;
        saw.position = new Vector3(hand.x, Mathf.Max(hand.y, 0.75f), hand.z);
        saw.rotation = root.transform.rotation;
        Log.AppendLine($"arm dir {armDir:F2} shoulder {shoulder:F2} hand {hand:F2} bodyBounds {armMesh.bounds.size:F2}");
        Box("Motor", saw, new Vector3(0f, 0f, 0f), new Vector3(0.26f, 0.3f, 0.42f), orange, 0.05f, worldSpace: true);
        Box("Handle", saw, new Vector3(0f, 0.2f, -0.05f), new Vector3(0.08f, 0.08f, 0.3f), black, 0.02f, worldSpace: true);
        Box("Guard", saw, new Vector3(0f, 0.12f, 0.2f), new Vector3(0.22f, 0.04f, 0.06f), black, 0.01f, worldSpace: true);
        Box("Bar", saw, new Vector3(0f, -0.03f, 0.62f), new Vector3(0.075f, 0.17f, 0.74f), blade, 0.02f, worldSpace: true);
        Box("Chain", saw, new Vector3(0f, -0.03f, 0.62f), new Vector3(0.05f, 0.21f, 0.7f), steelDark, 0.01f, worldSpace: true);
        var tipCap = Cylinder("Tip", saw, new Vector3(0f, -0.03f, 0.98f), 0.1f, 0.07f, 10, blade);
        tipCap.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        var toolTip = Empty("ToolTip", saw, new Vector3(0f, -0.03f, 1.0f));
        var smoke = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PrefabDir + "/VFX/VFX_ChainsawSmoke.prefab"), saw);
        smoke.transform.localPosition = new Vector3(-0.12f, 0.1f, -0.15f);
        saw.localScale = Vector3.one * 1.3f;
        saw.SetParent(arm, true);

        var stackAnchor = Empty("StackAnchor", root.transform, new Vector3(0f, 0.95f, -0.5f));
        var chest = Empty("Chest", root.transform, new Vector3(0f, 1.2f, 0f));

        var indicator = new GameObject("StackIndicator");
        indicator.transform.SetParent(root.transform, false);
        var maxLabel = Text3D("Label", indicator.transform, Vector3.zero, font, worldText, 5f, new Color(1f, 0.35f, 0.25f), TextAlignmentOptions.Center, "MAX");
        maxLabel.gameObject.SetActive(false);

        var stats = root.AddComponent<PlayerStats>();
        var input = root.AddComponent<PlayerInputReader>();
        var harvest = root.AddComponent<HarvestTool>();
        var controllerComp = root.AddComponent<PlayerController>();
        var stack = root.AddComponent<CarryStack>();
        var collector = root.AddComponent<ItemCollector>();
        var character = root.AddComponent<PlayerCharacter>();
        var visuals = root.AddComponent<PlayerVisuals>();
        var cash = root.AddComponent<CashCollector>();
        var ind = indicator.AddComponent<CarryStackIndicator>();

        SetMany(stats, ("config", playerConfig));
        SetMany(harvest, ("toolTip", toolTip));
        SetMany(controllerComp, ("input", input), ("stats", stats), ("harvestTool", harvest));
        SetMany(stack, ("stackRoot", stackAnchor), ("capacity", playerConfig.CarryCapacity));
        SetMany(collector, ("stack", stack), ("radius", playerConfig.PickupRadius));
        SetMany(character, ("stats", stats), ("controller", controllerComp), ("harvestTool", harvest), ("carryStack", stack), ("itemCollector", collector));
        SetMany(visuals, ("controller", controllerComp), ("harvestTool", harvest), ("animator", animator), ("body", body), ("tool", saw),
            ("toolSmoke", smoke.GetComponent<ParticleSystem>()));
        SetMany(cash, ("target", chest));
        SetMany(ind, ("stack", stack), ("label", maxLabel));
        _ = config;
        return root;
    }

    static AnimatorController BuildCharacterController(string fbxPath, string path, bool withToolLayer)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview"))
            .ToDictionary(c => c.name);
        if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
        var ac = AnimatorController.CreateAnimatorControllerAtPath(path);
        ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ac.AddParameter("Cutting", AnimatorControllerParameterType.Bool);

        var locomotion = ac.CreateBlendTreeInController("Locomotion", out var tree, 0);
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(clips["idle"], 0f);
        tree.AddChild(clips["walk"], 0.35f);
        tree.AddChild(clips["sprint"], 1f);
        ac.layers[0].stateMachine.defaultState = locomotion;

        if (withToolLayer)
        {
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(fbxPath));
            var mask = new AvatarMask { name = "Mask_RightArm" };
            mask.AddTransformPath(temp.transform, true);
            for (int i = 0; i < mask.transformCount; i++)
            {
                string p = mask.GetTransformPath(i);
                mask.SetTransformActive(i, p.EndsWith("arm-right"));
            }

            Object.DestroyImmediate(temp);
            AssetDatabase.AddObjectToAsset(mask, ac);

            ac.AddLayer("Tool");
            var layers = ac.layers;
            layers[1].defaultWeight = 1f;
            layers[1].avatarMask = mask;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            ac.layers = layers;

            var sm = ac.layers[1].stateMachine;
            var hold = sm.AddState("Hold");
            hold.motion = clips["holding-right"];
            var cut = sm.AddState("Cut");
            cut.motion = clips["attack-melee-right"];
            cut.speed = 1.5f;
            sm.defaultState = hold;

            var toCut = hold.AddTransition(cut);
            toCut.hasExitTime = false;
            toCut.duration = 0.06f;
            toCut.AddCondition(AnimatorConditionMode.If, 0f, "Cutting");
            var toHold = cut.AddTransition(hold);
            toHold.hasExitTime = false;
            toHold.duration = 0.12f;
            toHold.AddCondition(AnimatorConditionMode.IfNot, 0f, "Cutting");
        }

        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        return ac;
    }

    // =====================================================================================
    // Helpers: building blocks
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

    static TransferPad Pad(Transform parent, string name, Vector3 position, Vector2 size, TransferMode mode, MonoBehaviour target, Color color,
        GameObject visualPrefab, float arrowYaw, string sfxName)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        var visual = PadVisualInstance(go.transform, size, color, visualPrefab, arrowYaw);
        var pad = go.AddComponent<TransferPad>();
        SetMany(pad, ("mode", (int)mode), ("target", target), ("size", size), ("visual", visual),
            ("transferSfx", Load<SfxDefinition>($"{DataDir}/Audio/{sfxName}.asset")));
        return pad;
    }

    static Transform PadVisualOnly(Transform parent, string name, Vector3 position, Vector2 size, Color color, GameObject visualPrefab, TMP_FontAsset font,
        string glyph)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        var visual = PadVisualInstance(go.transform, size, color, visualPrefab, 0f);
        var arrow = visual.Find("Arrow");
        if (arrow != null) arrow.gameObject.SetActive(false);
        var text = Text3D("Glyph", visual, new Vector3(0f, 0.06f, 0f), font, Load<Material>(FontDir + "/GROBOLD World.mat"), 8f, Color.white,
            TextAlignmentOptions.Center, glyph);
        text.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        return go.transform;
    }

    static Transform PadVisualInstance(Transform parent, Vector2 size, Color color, GameObject visualPrefab, float arrowYaw)
    {
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab, parent);
        string key = ColorUtility.ToHtmlStringRGB(color);
        var frame = Unlit("PadFrame_" + key, color);
        var fill = TransparentUnlit("PadFill_" + key, new Color(color.r, color.g, color.b, 0.28f));
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

        return visual.transform;
    }

    static TMP_Text Text3D(string name, Transform parent, Vector3 localPos, TMP_FontAsset font, Material mat, float size, Color color,
        TextAlignmentOptions align, string text)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = font;
        if (mat != null) tmp.fontSharedMaterial = mat;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.text = text;
        tmp.rectTransform.sizeDelta = new Vector2(6f, 1.2f);
        tmp.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        return tmp;
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
        if (worldSpace) pb.transform.position = parent.position + parent.rotation * localPos;
        else pb.transform.localPosition = localPos;
        if (worldSpace) pb.transform.rotation = parent.rotation;
        pb.SetMaterial(pb.faces, mat);
        pb.ToMesh();
        pb.Refresh();
        var col = pb.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
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

    static Sprite UiSprite(string sheet, string spriteName) =>
        AssetDatabase.LoadAllAssetsAtPath(sheet).OfType<Sprite>().FirstOrDefault(s => s.name == spriteName);

    // =====================================================================================
    // Helpers: assets
    // =====================================================================================

    static GameObject BuildItem(string name, Mesh mesh, Material[] materials, Mesh[] meshVariants, Material[] materialVariants, float halfHeight)
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
        SetArray(item, "meshVariants", meshVariants != null ? meshVariants.Cast<Object>().ToArray() : Array.Empty<Object>());
        SetArray(item, "materialVariants", materialVariants != null ? materialVariants.Cast<Object>().ToArray() : Array.Empty<Object>());
        return root;
    }

    static Mesh BakeModel(string fbx, string outName, Vector3 fitBox)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(fbx));
        var filters = go.GetComponentsInChildren<MeshFilter>();
        var combine = filters.Select(f => new CombineInstance { mesh = f.sharedMesh, transform = go.transform.worldToLocalMatrix * f.transform.localToWorldMatrix }).ToArray();
        var mesh = new Mesh { name = outName };
        mesh.CombineMeshes(combine, true, true);
        Object.DestroyImmediate(go);

        var b = mesh.bounds;
        float s = Mathf.Min(fitBox.x / Mathf.Max(b.size.x, 0.001f), fitBox.z / Mathf.Max(b.size.z, 0.001f), 0.9f / Mathf.Max(b.size.y, 0.001f));
        float minHeight = fitBox.y * 0.6f;
        float sy = b.size.y * s < minHeight ? minHeight / Mathf.Max(b.size.y, 0.001f) : s;
        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            var v = verts[i] - b.center;
            verts[i] = new Vector3(v.x * s, v.y * sy, v.z * s);
        }

        mesh.vertices = verts;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return SaveMesh(mesh, outName);
    }

    static Mesh BakeBale()
    {
        var holder = new GameObject("tmp");
        var tmp = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var body = Box("Body", holder.transform, Vector3.zero, new Vector3(0.46f, 0.32f, 0.46f), tmp, 0.05f);
        var strapA = Box("StrapA", holder.transform, new Vector3(-0.12f, 0f, 0f), new Vector3(0.05f, 0.335f, 0.475f), tmp, 0f);
        var strapB = Box("StrapB", holder.transform, new Vector3(0.12f, 0f, 0f), new Vector3(0.05f, 0.335f, 0.475f), tmp, 0f);
        var mesh = CombineSubmeshes("Bale", new[] { body }, new[] { strapA, strapB });
        Object.DestroyImmediate(holder);
        return mesh;
    }

    static Mesh BakeCash()
    {
        var holder = new GameObject("tmp");
        var tmp = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var body = Box("Body", holder.transform, Vector3.zero, new Vector3(0.46f, 0.1f, 0.26f), tmp, 0.02f);
        var band = Box("Band", holder.transform, Vector3.zero, new Vector3(0.1f, 0.105f, 0.265f), tmp, 0f);
        var mesh = CombineSubmeshes("CashBundle", new[] { body }, new[] { band });
        Object.DestroyImmediate(holder);
        return mesh;
    }

    static Mesh CombineSubmeshes(string name, ProBuilderMesh[] first, ProBuilderMesh[] second)
    {
        Mesh Merge(ProBuilderMesh[] parts)
        {
            var m = new Mesh();
            m.CombineMeshes(parts.Select(p => new CombineInstance { mesh = p.GetComponent<MeshFilter>().sharedMesh, transform = p.transform.localToWorldMatrix }).ToArray(), true, true);
            return m;
        }

        var a = Merge(first);
        var b = Merge(second);
        var mesh = new Mesh { name = name };
        mesh.CombineMeshes(new[] { new CombineInstance { mesh = a, transform = Matrix4x4.identity }, new CombineInstance { mesh = b, transform = Matrix4x4.identity } },
            false, true);
        return SaveMesh(mesh, name);
    }

    static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = $"{MeshDir}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        EditorUtility.CopySerialized(mesh, existing);
        existing.name = name;
        EditorUtility.SetDirty(existing);
        return existing;
    }

    static void NoiseTexture(string name, Color a, Color b, int basePeriod, Color speckle, float speckleDensity, int seed)
    {
        string path = $"{TexDir}/{name}.png";
        if (File.Exists(path)) return;

        const int size = 512;
        var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
        var rng = new System.Random(seed);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = x / (float)size, v = y / (float)size;
            float n = 0f, amp = 0.55f, total = 0f;
            for (int o = 0; o < 4; o++)
            {
                n += amp * ValueNoise(u, v, basePeriod << o, seed + o * 31);
                total += amp;
                amp *= 0.5f;
            }

            n /= total;
            var c = Color.Lerp(b, a, Mathf.SmoothStep(0.2f, 0.8f, n));
            pixels[y * size + x] = c;
        }

        int speckles = (int)(size * size * speckleDensity);
        for (int i = 0; i < speckles; i++)
        {
            int sx = rng.Next(size), sy = rng.Next(size), r = rng.Next(1, 3);
            for (int dy = 0; dy < r; dy++)
            for (int dx = 0; dx < r; dx++)
                pixels[((sy + dy) % size) * size + (sx + dx) % size] = Color.Lerp(pixels[sy * size + sx], speckle, 0.7f);
        }

        tex.SetPixels(pixels);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.anisoLevel = 4;
        importer.SaveAndReimport();
    }

    static float ValueNoise(float u, float v, int period, int seed)
    {
        float x = u * period, y = v * period;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float Lattice(int ix, int iy)
        {
            ix = ((ix % period) + period) % period;
            iy = ((iy % period) + period) % period;
            unchecked
            {
                uint h = (uint)(ix * 374761393 + iy * 668265263 + seed * 982451653);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h & 0xFFFF) / 65535f;
            }
        }

        float a = Mathf.Lerp(Lattice(x0, y0), Lattice(x0 + 1, y0), fx);
        float b = Mathf.Lerp(Lattice(x0, y0 + 1), Lattice(x0 + 1, y0 + 1), fx);
        return Mathf.Lerp(a, b, fy);
    }

    static TMP_FontAsset GroboldFont()
    {
        string path = FontDir + "/GROBOLD SDF.asset";
        var existing = Load<TMP_FontAsset>(path, false);
        if (existing != null) return existing;

        var source = Load<Font>("Assets/300Mind/2D Game UI Kit/Fonts/GROBOLD.ttf");
        var fa = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        fa.name = "GROBOLD SDF";
        AssetDatabase.CreateAsset(fa, path);
        fa.atlasTextures[0].name = "GROBOLD SDF Atlas";
        AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
        fa.material.name = "GROBOLD SDF Material";
        AssetDatabase.AddObjectToAsset(fa.material, fa);
        fa.TryAddCharacters("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz+-$/.:!%,x ");
        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        Log.AppendLine("font created: " + path);
        return fa;
    }

    static void TextMaterial(TMP_FontAsset font, string name, float outline, Color outlineColor, bool underlay)
    {
        string path = $"{FontDir}/{name}.mat";
        var m = Load<Material>(path, false);
        if (m == null)
        {
            m = new Material(font.material) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }

        m.shader = font.material.shader;
        m.SetTexture(ShaderUtilities.ID_MainTex, font.atlasTexture);
        m.EnableKeyword("OUTLINE_ON");
        m.SetFloat(ShaderUtilities.ID_OutlineWidth, outline);
        m.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
        m.SetFloat(ShaderUtilities.ID_FaceDilate, 0.15f);
        if (underlay)
        {
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.55f));
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
            m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.25f);
        }

        EditorUtility.SetDirty(m);
    }

    static Material KenneyMaterial(string pack, string name = null, Color? tint = null)
    {
        name ??= "M_Kenney_" + pack;
        string path = $"{MatDir}/{name}.mat";
        var m = Load<Material>(path, false);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }

        m.SetTexture("_BaseMap", LoadTexture($"{K}/{pack}/Textures/colormap.png"));
        m.SetColor("_BaseColor", tint ?? Color.white);
        m.SetFloat("_Smoothness", 0.25f);
        m.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(m);
        return m;
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

    static Material LitTex(string name, Texture2D tex, Vector2 tiling, Color color, float smoothness)
    {
        var m = LoadOrCreateMaterial(name, "Universal Render Pipeline/Lit");
        m.SetTexture("_BaseMap", tex);
        m.SetTextureScale("_BaseMap", tiling);
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", 0f);
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

    static Material Unlit(string name, Color color)
    {
        var m = LoadOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
        m.SetColor("_BaseColor", color);
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
        if (m != null)
        {
            if (m.shader.name != shader) m.shader = Shader.Find(shader);
            return m;
        }

        m = new Material(Shader.Find(shader)) { name = "M_" + name };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static GameObject BuildSmoke(string name, Material mat, float rate, Color color, float sizeMin, float sizeMax, float lifetime)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = color;
        main.gravityModifier = -0.15f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 60;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 25f;
        shape.radius = 0.1f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
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

    // =====================================================================================
    // SCENE
    // =====================================================================================

    public static string Scene()
    {
        Log.Clear();
        var font = Load<TMP_FontAsset>(FontDir + "/GROBOLD SDF.asset");
        var outline = Load<Material>(FontDir + "/GROBOLD Outline.mat");
        var worldText = Load<Material>(FontDir + "/GROBOLD World.mat");
        UpdateFloatingTextPrefab(font, worldText);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var systems = new GameObject("_Systems").transform;
        var environment = new GameObject("_Environment").transform;
        var gameplay = new GameObject("_Gameplay").transform;
        var cameraRoot = new GameObject("_Camera").transform;
        var lighting = new GameObject("_Lighting").transform;
        var ui = new GameObject("_UI").transform;

        BuildLighting(lighting);
        BuildEnvironment(environment, font, worldText);

        // ---------- Gameplay ----------
        var playerStart = Empty("PlayerStart", gameplay, new Vector3(12f, 0f, 17.5f));
        var looseItems = Empty("LooseItems", gameplay, Vector3.zero);
        var itemPoolRoot = Empty("ItemPool", gameplay, new Vector3(0f, -20f, 0f));
        var spawnZone = Empty("ScrapSpawnZone", gameplay, new Vector3(10f, 0f, 30f));
        var spawned = new GameObject("Spawned").transform;
        spawned.SetParent(spawnZone, true);

        var car = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_CarWreck.asset");
        var barrel = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_Barrel.asset");
        var tires = Load<ScrapDefinition>(DataDir + "/Scrap/Scrap_TireStack.asset");
        int characters = LayerMask.GetMask("Characters");
        SpawnPoint("SP_CarWreck_01", spawnZone, new Vector3(9f, 0f, 31f), 20f, false, characters, 2.8f, car);
        SpawnPoint("SP_Barrel_01", spawnZone, new Vector3(3.5f, 0f, 26.5f), 0f, true, characters, 1.2f, barrel);
        SpawnPoint("SP_Barrel_02", spawnZone, new Vector3(16f, 0f, 34.5f), 0f, true, characters, 1.2f, barrel);
        SpawnPoint("SP_Barrel_03", spawnZone, new Vector3(15.5f, 0f, 25.5f), 0f, true, characters, 1.2f, barrel);
        SpawnPoint("SP_TireStack_01", spawnZone, new Vector3(4f, 0f, 35f), 0f, true, characters, 1.2f, tires);
        SpawnPoint("SP_TireStack_02", spawnZone, new Vector3(14f, 0f, 29.5f), 0f, true, characters, 1.2f, tires);

        // Production line (blueprint p.4): Crusher → conveyor → Storage → (carry) → Sell Desk.
        var stations = Empty("Stations", gameplay, Vector3.zero);
        var crusher = Station("Crusher", stations, new Vector3(24f, 0f, 27f));
        var storage = Station("Storage", stations, new Vector3(24f, 0f, 19.2f));
        var desk = Station("SellDesk", stations, new Vector3(32f, 0f, 12.4f));
        var conveyor = BuildConveyor(stations, new Vector3(24f, 0f, 24.95f), new Vector3(24f, 0f, 20.75f), storage.GetComponent<Storage>());
        SetMany(crusher.GetComponent<Machine>(), ("outputTarget", conveyor));

        var slots = Empty("Slots", gameplay, Vector3.zero);
        Empty("Slot_CustomerQueue", slots, new Vector3(32f, 0f, 8f));
        Empty("Slot_Gate_A1_A2", slots, new Vector3(40f, 0f, 24.5f));

        var player = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PrefabDir + "/Player/Player.prefab"), gameplay);
        player.transform.SetPositionAndRotation(playerStart.position, Quaternion.identity);

        // ---------- Systems ----------
        SetMany(Component<GameManager>("GameManager", systems), ("config", Load<GameConfig>(DataDir + "/Config/GameConfig.asset")));
        SetMany(Component<EconomyManager>("EconomyManager", systems), ("config", Load<EconomyConfig>(DataDir + "/Economy/EconomyConfig.asset")));
        SetMany(Component<ItemPool>("ItemPool", systems), ("root", itemPoolRoot));
        SetMany(Component<ScrapManager>("ScrapManager", systems), ("spawnedRoot", spawned));
        SetMany(Component<HarvestManager>("HarvestManager", systems), ("looseItemRoot", looseItems),
            ("dropBounds", new Bounds(new Vector3(20f, 0f, 24f), new Vector3(39f, 20f, 27f))));
        Component<AudioManager>("AudioManager", systems);
        Component<VFXManager>("VFXManager", systems);
        Component<HitStopController>("HitStopController", systems);
        SetMany(Component<FloatingTextManager>("FloatingTextManager", systems),
            ("prefab", Load<GameObject>(PrefabDir + "/UI/FloatingText.prefab").GetComponent<TextMeshPro>()));

        // ---------- Camera ----------
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        camGo.transform.SetParent(cameraRoot, false);
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.5f;
        cam.farClipPlane = 160f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.56f, 0.74f, 0.52f);
        if (!camGo.TryGetComponent<UniversalAdditionalCameraData>(out var camData)) camData = camGo.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = true;
        camGo.AddComponent<AudioListener>();
        var camController = camGo.AddComponent<CameraController>();
        SetMany(camController, ("target", player.transform), ("cam", cam), ("pitch", 50f), ("verticalFov", 38f), ("minVisibleWidth", 14f),
            ("baseDistance", 24f));
        camGo.transform.SetPositionAndRotation(player.transform.position + new Vector3(0f, 22f, -18f), Quaternion.Euler(50f, 0f, 0f));

        // ---------- UI ----------
        var joystick = BuildUI(ui, font, outline);
        SetMany(player.GetComponent<PlayerInputReader>(), ("joystick", joystick));

        EditorSceneManager.SaveScene(scene, ScenePath);
        var buildScenes = new List<EditorBuildSettingsScene> { new(ScenePath, true) };
        buildScenes.AddRange(EditorBuildSettings.scenes.Where(s => s.path != ScenePath).Select(s => new EditorBuildSettingsScene(s.path, false)));
        EditorBuildSettings.scenes = buildScenes.ToArray();
        Log.AppendLine("Saved " + ScenePath);
        return Log.ToString();
    }

    static GameObject Station(string prefab, Transform parent, Vector3 position)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>($"{PrefabDir}/Stations/{prefab}.prefab"), parent);
        go.transform.position = position;
        return go;
    }

    static Conveyor BuildConveyor(Transform parent, Vector3 from, Vector3 to, MonoBehaviour destination)
    {
        var root = new GameObject("Conveyor_CrusherToStorage").transform;
        root.SetParent(parent, false);
        root.position = from;
        Vector3 delta = to - from;
        float length = delta.magnitude;
        root.rotation = Quaternion.LookRotation(delta.normalized);

        var factory = KenneyMaterial("FactoryKit");
        int tiles = Mathf.CeilToInt(length);
        float tileLength = length / tiles;
        for (int i = 0; i < tiles; i++)
        {
            var tile = Kenney("FactoryKit", "conveyor-stripe", root, new Vector3(0f, 0f, tileLength * (i + 0.5f)), 1f, Vector3.zero, factory);
            tile.transform.localScale = new Vector3(1.1f, 1f, tileLength);
        }

        float top = RendererBounds(root.gameObject).max.y;
        var start = Empty("Start", root, new Vector3(0f, 0f, 0.15f));
        var end = Empty("End", root, new Vector3(0f, 0f, length - 0.15f));
        var conveyor = root.gameObject.AddComponent<Conveyor>();
        SetMany(conveyor, ("speed", 1.8f), ("spacing", 0.55f), ("rideHeight", top + 0.17f), ("destination", destination));
        SetArray(conveyor, "points", new Object[] { start, end });

        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.5f, length * 0.5f);
        box.size = new Vector3(1.1f, 1f, length);
        return conveyor;
    }

    static void BuildLighting(Transform root)
    {
        var lightGo = new GameObject("Directional Light");
        lightGo.transform.SetParent(root, false);
        lightGo.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.94f, 0.84f);
        light.intensity = 1.45f;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.6f;
        if (!lightGo.TryGetComponent<UniversalAdditionalLightData>(out _)) lightGo.AddComponent<UniversalAdditionalLightData>();

        string profilePath = P + "/Settings/VP_Yard.asset";
        Directory.CreateDirectory(P + "/Settings");
        var profile = Load<VolumeProfile>(profilePath, false);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
        }

        foreach (var c in profile.components.ToArray())
        {
            profile.Remove(c.GetType());
            Object.DestroyImmediate(c, true);
        }

        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(1f);
        bloom.intensity.Override(0.45f);
        bloom.scatter.Override(0.6f);
        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.Neutral);
        var color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.25f);
        color.contrast.Override(12f);
        color.saturation.Override(22f);
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.2f);
        vignette.smoothness.Override(0.45f);
        foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        var volumeGo = new GameObject("Global Volume");
        volumeGo.transform.SetParent(root, false);
        var volume = volumeGo.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.78f, 0.84f, 0.94f);
        RenderSettings.ambientEquatorColor = new Color(0.62f, 0.6f, 0.54f);
        RenderSettings.ambientGroundColor = new Color(0.38f, 0.33f, 0.27f);
        RenderSettings.skybox = null;
        RenderSettings.fog = false;
    }

    static void BuildEnvironment(Transform root, TMP_FontAsset font, Material worldText)
    {
        var dirt = Load<Texture2D>(TexDir + "/T_Dirt.png");
        var grass = Load<Texture2D>(TexDir + "/T_Grass.png");
        var asphalt = Load<Texture2D>(TexDir + "/T_Asphalt.png");
        var concrete = Load<Texture2D>(TexDir + "/T_Concrete.png");

        Slab("WorldGround", root, new Vector3(60f, -0.15f, 45f), new Vector3(170f, 0.2f, 140f), LitTex("Ground_Grass", grass, new Vector2(40f, 33f), Color.white, 0.05f));
        Slab("Road_South", root, new Vector3(60f, -0.05f, 5f), new Vector3(170f, 0.1f, 10f), LitTex("Ground_Asphalt", asphalt, new Vector2(40f, 2.4f), Color.white, 0.2f));
        var markings = Empty("Road_Markings", root, Vector3.zero);
        var lineMat = Lit("RoadLine", new Color(0.97f, 0.86f, 0.35f), 0.2f);
        for (float x = -20f; x <= 140f; x += 6f)
            Deco(PrimitiveType.Cube, "Dash", markings, new Vector3(x, 0.006f, 5f), new Vector3(3f, 0.012f, 0.25f), lineMat);
        Slab("Sidewalk", root, new Vector3(60f, -0.04f, 10.25f), new Vector3(170f, 0.1f, 0.5f), Lit("Curb", new Color(0.72f, 0.72f, 0.7f), 0.2f));

        Slab("Area1_Ground", root, new Vector3(20f, -0.045f, 24f), new Vector3(40f, 0.1f, 28f), LitTex("Ground_Dirt", dirt, new Vector2(9f, 6.3f), Color.white, 0.08f));
        Slab("ScrapZone_Floor", root, new Vector3(10f, 0.008f, 30f), new Vector3(17f, 0.016f, 13f),
            LitTex("Ground_OilDirt", dirt, new Vector2(4f, 3f), new Color(0.78f, 0.72f, 0.66f), 0.25f), false);
        Slab("WorkArea_Concrete", root, new Vector3(24.5f, 0.01f, 23.5f), new Vector3(11f, 0.02f, 14f),
            LitTex("Ground_Concrete", concrete, new Vector2(3f, 4f), Color.white, 0.12f), false);
        Slab("SellArea_Concrete", root, new Vector3(33.5f, 0.012f, 13.2f), new Vector3(11f, 0.024f, 6f),
            LitTex("Ground_ConcreteSell", concrete, new Vector2(3f, 1.7f), new Color(0.95f, 0.93f, 0.88f), 0.12f), false);
        var locked = LitTex("Ground_Locked", dirt, new Vector2(8f, 7f), new Color(0.62f, 0.6f, 0.58f), 0.05f);
        Slab("Area2_Locked", root, new Vector3(58f, -0.048f, 26f), new Vector3(36f, 0.1f, 32f), locked);
        Slab("Area3_Locked", root, new Vector3(20f, -0.048f, 56f), new Vector3(40f, 0.1f, 36f), locked);

        // Perimeter. Corrugated metal on the back walls, low frame fence facing the camera and the road.
        var fences = Empty("Fences", root, Vector3.zero);
        CorrugatedWall(fences, new Vector3(0f, 0f, 38f), new Vector3(40f, 0f, 38f));
        CorrugatedWall(fences, new Vector3(0f, 0f, 10.5f), new Vector3(0f, 0f, 38f));
        CorrugatedWall(fences, new Vector3(40f, 0f, 38f), new Vector3(40f, 0f, 27f));
        CorrugatedWall(fences, new Vector3(40f, 0f, 22f), new Vector3(40f, 0f, 10.5f));
        FrameFence(fences, new Vector3(0f, 0f, 10.5f), new Vector3(26f, 0f, 10.5f));
        FrameFence(fences, new Vector3(26f, 0f, 10.5f), new Vector3(30f, 0f, 10.5f));
        FrameFence(fences, new Vector3(34f, 0f, 10.5f), new Vector3(40f, 0f, 10.5f));
        Blocker(fences, "Desk_SideW", new Vector3(30f, 1f, 11.2f), new Vector3(0.4f, 2f, 1.4f));
        Blocker(fences, "Desk_SideE", new Vector3(34f, 1f, 11.2f), new Vector3(0.4f, 2f, 1.4f));

        BuildGate(root, font, worldText);
        BuildJunk(root);
        BuildBackdrop(root, font, worldText);
    }

    static void CorrugatedWall(Transform parent, Vector3 from, Vector3 to)
    {
        var segment = new GameObject($"Wall_{from.x:0}_{from.z:0}_{to.x:0}_{to.z:0}").transform;
        segment.SetParent(parent, false);
        Vector3 delta = to - from;
        float length = delta.magnitude;
        segment.position = from;
        segment.rotation = Quaternion.LookRotation(delta.normalized) * Quaternion.Euler(0f, -90f, 0f);

        var tints = new[]
        {
            KenneyMaterial("SurvivalKit"),
            KenneyMaterial("SurvivalKit", "M_Kenney_SurvivalRust", new Color(0.85f, 0.62f, 0.45f)),
            KenneyMaterial("SurvivalKit", "M_Kenney_SurvivalGrey", new Color(0.62f, 0.66f, 0.72f)),
        };

        var probe = Kenney("SurvivalKit", "metal-panel-screws", segment, Vector3.zero, 1f, Vector3.zero, null);
        var pb = RendererBounds(probe);
        Object.DestroyImmediate(probe);
        float native = Mathf.Max(pb.size.x, pb.size.z);
        const float panelWidth = 2.1f;
        float scale = panelWidth / native;
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

    static void FrameFence(Transform parent, Vector3 from, Vector3 to)
    {
        var segment = new GameObject($"Fence_{from.x:0}_{to.x:0}").transform;
        segment.SetParent(parent, false);
        Vector3 delta = to - from;
        float length = delta.magnitude;
        segment.position = from;
        segment.rotation = Quaternion.LookRotation(delta.normalized) * Quaternion.Euler(0f, -90f, 0f);
        var post = Lit("FencePost", new Color(0.3f, 0.32f, 0.36f), 0.4f, 0.6f);
        var rail = Lit("FenceRail", new Color(1f, 0.78f, 0.1f), 0.35f);
        int posts = Mathf.Max(1, Mathf.RoundToInt(length / 2f));
        for (int i = 0; i <= posts; i++)
            Box("Post", segment, new Vector3(length * i / posts, 0.5f, 0f), new Vector3(0.16f, 1.0f, 0.16f), post, 0.02f);
        Box("RailTop", segment, new Vector3(length * 0.5f, 0.9f, 0f), new Vector3(length, 0.1f, 0.08f), rail, 0.02f);
        Box("RailMid", segment, new Vector3(length * 0.5f, 0.5f, 0f), new Vector3(length, 0.08f, 0.06f), rail, 0.02f);
        var box = segment.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(length * 0.5f, 1f, 0f);
        box.size = new Vector3(length, 2f, 0.4f);
    }

    static void BuildGate(Transform root, TMP_FontAsset font, Material worldText)
    {
        var gate = Empty("Gate_A1_A2", root, new Vector3(40f, 0f, 24.5f));
        var hazard = Lit("HazardYellow", new Color(1f, 0.78f, 0.1f), 0.3f);
        var red = Lit("HazardRed", new Color(0.88f, 0.2f, 0.15f), 0.3f);
        var dark = Lit("SteelDark", new Color(0.2f, 0.21f, 0.24f), 0.45f, 0.7f);
        Box("PostS", gate, new Vector3(0f, 1.2f, -2.6f), new Vector3(0.45f, 2.4f, 0.45f), hazard, 0.05f);
        Box("PostN", gate, new Vector3(0f, 1.2f, 2.6f), new Vector3(0.45f, 2.4f, 0.45f), hazard, 0.05f);
        for (int i = 0; i < 5; i++)
            Box("Bar_" + i, gate, new Vector3(0f, 1.05f, -2f + i), new Vector3(0.2f, 0.26f, 1f), i % 2 == 0 ? red : hazard, 0.02f);
        var board = Box("Sign", gate, new Vector3(-0.4f, 3.2f, 0f), new Vector3(3.8f, 1.3f, 0.14f), dark, 0.05f);
        board.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
        var text = Text3D("SignText", gate, new Vector3(-0.4f, 3.2f, -0.12f), font, worldText, 5f, new Color(1f, 0.8f, 0.15f), TextAlignmentOptions.Center,
            "<size=70%>RECYCLING PLANT</size>\nLOCKED");
        text.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
        text.rectTransform.sizeDelta = new Vector2(3.4f, 1.2f);
        var b = new GameObject("GateCollider");
        b.transform.SetParent(gate, false);
        var box = b.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 1f, 0f);
        box.size = new Vector3(0.6f, 2f, 5f);
    }

    static void BuildJunk(Transform root)
    {
        // Static scrap heaps along the back walls: sells the "scrapyard" read without blocking routes.
        var junk = Empty("JunkPiles", root, Vector3.zero);
        var wreck = KenneyMaterial("CarKit", "M_Kenney_CarWreck", new Color(0.76f, 0.68f, 0.6f));
        var survival = KenneyMaterial("SurvivalKit");
        var rng = new System.Random(7);
        Heap(junk, new Vector3(24f, 0f, 36.4f), new Vector2(7f, 1.6f), 16, rng, wreck, survival);
        Heap(junk, new Vector3(34.5f, 0f, 36.4f), new Vector2(6f, 1.6f), 14, rng, wreck, survival);
        Heap(junk, new Vector3(1.4f, 0f, 16.5f), new Vector2(1.4f, 5f), 12, rng, wreck, survival);
        Heap(junk, new Vector3(38.4f, 0f, 32f), new Vector2(1.4f, 4f), 9, rng, wreck, survival);
        Heap(junk, new Vector3(2.5f, 0f, 12.2f), new Vector2(2.5f, 1.2f), 7, rng, wreck, survival);
    }

    static void Heap(Transform parent, Vector3 center, Vector2 extents, int count, System.Random rng, Material wreck, Material survival)
    {
        var heap = Empty("Heap", parent, center);
        string[] cars = { "sedan", "van", "suv", "truck", "hatchback-sports", "delivery" };
        string[] debris = { "wheel-default", "debris-plate-a", "debris-door", "debris-bumper", "debris-tire", "debris-plate-b", "debris-spoiler-b" };
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        int shells = Mathf.Max(1, count / 6);
        for (int i = 0; i < shells; i++)
        {
            var body = Kenney("CarKit", cars[rng.Next(cars.Length)], heap.transform, new Vector3(R(-extents.x, extents.x) * 0.7f, 0f, R(-extents.y, extents.y) * 0.4f),
                R(1.4f, 1.7f), new Vector3(R(-6f, 6f), R(0f, 360f), R(-12f, 12f)), wreck);
            foreach (var w in body.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("wheel")).ToArray())
                if (rng.NextDouble() < 0.5) w.gameObject.SetActive(false);
        }

        for (int i = 0; i < count; i++)
        {
            string model = debris[rng.Next(debris.Length)];
            var piece = Kenney("CarKit", model, heap.transform, new Vector3(R(-extents.x, extents.x), R(0f, 0.9f), R(-extents.y, extents.y)), R(1.6f, 2.6f),
                new Vector3(R(-40f, 40f), R(0f, 360f), R(-40f, 40f)), wreck);
            _ = piece;
        }

        for (int i = 0; i < count / 4; i++)
            Kenney("SurvivalKit", rng.NextDouble() < 0.5 ? "barrel" : "box-large", heap.transform,
                new Vector3(R(-extents.x, extents.x), 0f, R(-extents.y, extents.y)), R(2.6f, 3.4f), new Vector3(0f, R(0f, 360f), rng.NextDouble() < 0.3 ? 90f : 0f), survival);

        foreach (var r in heap.GetComponentsInChildren<Renderer>()) GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
        var box = heap.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 1f, 0f);
        box.size = new Vector3(extents.x * 2f, 2f, extents.y * 2f);
    }

    static void BuildBackdrop(Transform root, TMP_FontAsset font, Material worldText)
    {
        var backdrop = Empty("Backdrop", root, Vector3.zero);
        var city = KenneyMaterial("CityKitIndustrial");
        var survival = KenneyMaterial("SurvivalKit");
        var wreck = KenneyMaterial("CarKit", "M_Kenney_CarWreck", new Color(0.76f, 0.68f, 0.6f));

        // Heavy yard teaser behind the north wall (Area 3, locked).
        Kenney("CarKit", "tractor", backdrop, new Vector3(8f, 0f, 44f), 2.2f, new Vector3(0f, 30f, 8f), wreck);
        Kenney("CarKit", "truck", backdrop, new Vector3(18f, 0f, 45f), 2.1f, new Vector3(0f, -20f, -6f), wreck);
        Kenney("CarKit", "garbage-truck", backdrop, new Vector3(30f, 0f, 46f), 2f, new Vector3(0f, 200f, 0f), wreck);
        Kenney("CityKitIndustrial", "shipping-container-a", backdrop, new Vector3(4f, 0f, 50f), 3f, new Vector3(0f, 90f, 0f), city);
        Kenney("CityKitIndustrial", "shipping-container-b", backdrop, new Vector3(4f, 2.5f, 50.3f), 3f, new Vector3(0f, 92f, 0f), city);
        Kenney("CityKitIndustrial", "shipping-container-c", backdrop, new Vector3(36f, 0f, 49f), 3f, new Vector3(0f, 0f, 0f), city);
        Kenney("CityKitIndustrial", "detail-tank-large", backdrop, new Vector3(25f, 0f, 52f), 3f, Vector3.zero, city);
        Kenney("CityKitIndustrial", "chimney-large", backdrop, new Vector3(13f, 0f, 54f), 3f, Vector3.zero, city);
        var sign = Text3D("Area3Sign", backdrop, new Vector3(20f, 3.2f, 40.5f), font, worldText, 7f, new Color(1f, 0.55f, 0.2f), TextAlignmentOptions.Center,
            "<size=60%>HEAVY SCRAP YARD</size>\nLOCKED");
        sign.transform.rotation = Quaternion.Euler(35f, 0f, 0f);
        sign.rectTransform.sizeDelta = new Vector2(10f, 2f);

        // Recycling plant teaser east of the gate (Area 2, locked).
        Kenney("CityKitIndustrial", "building-d", backdrop, new Vector3(56f, 0f, 30f), 3.5f, new Vector3(0f, -90f, 0f), city);
        Kenney("CityKitIndustrial", "detail-tank", backdrop, new Vector3(48f, 0f, 18f), 3f, Vector3.zero, city);

        var rng = new System.Random(3);
        var trees = new[]
        {
            new Vector3(-3f, 0f, 14f), new Vector3(-3.5f, 0f, 22f), new Vector3(-2.8f, 0f, 30f), new Vector3(-3.2f, 0f, 37f), new Vector3(-6f, 0f, 18f),
            new Vector3(-6.5f, 0f, 33f), new Vector3(-5f, 0f, 26f), new Vector3(44f, 0f, 12.5f), new Vector3(42.5f, 0f, 36f), new Vector3(10f, 0f, -3f),
            new Vector3(22f, 0f, -3.5f), new Vector3(34f, 0f, -3f), new Vector3(-2f, 0f, 41f),
        };
        foreach (var t in trees)
            Kenney("SurvivalKit", rng.NextDouble() < 0.5 ? "tree" : "tree-tall", backdrop, t, (float)(3.2 + rng.NextDouble() * 1.4), new Vector3(0f, (float)rng.NextDouble() * 360f, 0f), survival);

        foreach (var r in backdrop.GetComponentsInChildren<Renderer>()) GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
    }

    static VirtualJoystick BuildUI(Transform root, TMP_FontAsset font, Material outline)
    {
        var canvasGo = new GameObject("Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(root, false);
        canvasGo.layer = LayerMask.NameToLayer("UI");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // Joystick area first so HUD buttons drawn later win raycasts.
        var area = Rect("JoystickArea", canvasGo.transform);
        Stretch(area);
        area.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        var baseRect = UIImage("Base", area, knob, new Color(1f, 1f, 1f, 0.3f), new Vector2(250f, 250f));
        baseRect.anchoredPosition = new Vector2(0f, -640f);
        UIImage("Ring", baseRect, knob, new Color(0f, 0f, 0f, 0.15f), new Vector2(170f, 170f));
        var handle = UIImage("Handle", baseRect, knob, new Color(1f, 1f, 1f, 0.9f), new Vector2(115f, 115f));
        var group = baseRect.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        var joystick = area.gameObject.AddComponent<VirtualJoystick>();
        SetMany(joystick, ("baseRect", baseRect), ("handle", handle), ("visuals", group), ("idleAlpha", 0.5f), ("activeAlpha", 1f));

        // HUD (blueprint: cash + premium top right).
        var hud = Rect("HUD", canvasGo.transform);
        Stretch(hud);
        var coin = UiSprite(UiSheet1, "UI-pack_Sprite_1_16");
        var gem = UiSprite(UiSheet1, "UI-pack_Sprite_1_22");
        var plus = UiSprite(UiSheet1, "UI-pack_Sprite_1_17");
        var pill = UiSprite(UiSheet2, "UI-pack_Sprite_2_5");
        var cash = CurrencyPill("CashPill", hud, new Vector2(-36f, -60f), new Vector2(400f, 96f), pill, coin, plus, font, outline);
        var premium = CurrencyPill("GemPill", hud, new Vector2(-470f, -60f), new Vector2(290f, 96f), pill, gem, plus, font, outline);

        var fly = Rect("FlyLayer", hud);
        Stretch(fly);
        var template = UIImage("CoinTemplate", fly, coin, Color.white, new Vector2(78f, 78f));
        template.gameObject.SetActive(false);
        var flyer = fly.gameObject.AddComponent<UIFlyer>();
        SetMany(flyer, ("layer", fly), ("iconPrefab", template.GetComponent<Image>()));

        var controller = hud.gameObject.AddComponent<HudController>();
        SetMany(controller, ("cash", cash), ("premium", premium), ("flyer", flyer), ("cashIcon", coin));

        var es = new GameObject("EventSystem");
        es.transform.SetParent(root, false);
        es.AddComponent<EventSystem>();
        es.AddComponent<InputSystemUIInputModule>();
        return joystick;
    }

    static CurrencyWidget CurrencyPill(string name, RectTransform parent, Vector2 position, Vector2 size, Sprite background, Sprite icon, Sprite plus,
        TMP_FontAsset font, Material outline)
    {
        var pill = Rect(name, parent);
        pill.anchorMin = pill.anchorMax = new Vector2(1f, 1f);
        pill.pivot = new Vector2(1f, 1f);
        pill.anchoredPosition = position;
        pill.sizeDelta = size;
        var bg = pill.gameObject.AddComponent<Image>();
        bg.sprite = background;
        bg.color = new Color(1f, 1f, 1f, 0.92f);
        bg.raycastTarget = false;

        var iconRect = UIImage("Icon", pill, icon, Color.white, new Vector2(size.y * 1.15f, size.y * 1.15f));
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(size.y * 0.25f, 0f);

        var plusRect = UIImage("Plus", pill, plus, Color.white, new Vector2(size.y * 0.78f, size.y * 0.78f));
        plusRect.anchorMin = plusRect.anchorMax = new Vector2(1f, 0.5f);
        plusRect.anchoredPosition = new Vector2(-size.y * 0.45f, 0f);

        var labelRect = Rect("Label", pill);
        Stretch(labelRect);
        labelRect.offsetMin = new Vector2(size.y * 0.9f, 4f);
        labelRect.offsetMax = new Vector2(-size.y * 0.9f, -4f);
        var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSharedMaterial = outline;
        label.fontSize = size.y * 0.58f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.text = "0";
        label.raycastTarget = false;

        var widget = pill.gameObject.AddComponent<CurrencyWidget>();
        SetMany(widget, ("label", label), ("icon", iconRect));
        return widget;
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

    static RectTransform UIImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
    {
        var rt = Rect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.preserveAspect = true;
        img.raycastTarget = false;
        return rt;
    }

    static void UpdateFloatingTextPrefab(TMP_FontAsset font, Material worldText)
    {
        string path = PrefabDir + "/UI/FloatingText.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        var tmp = root.GetComponent<TextMeshPro>();
        tmp.font = font;
        tmp.fontSharedMaterial = worldText;
        tmp.fontSize = 8f;
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
    }

    static void SpawnPoint(string name, Transform parent, Vector3 position, float yaw, bool randomYaw, int blocking, float clearRadius,
        ScrapDefinition definition)
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
        candidates.arraySize = 1;
        candidates.GetArrayElementAtIndex(0).objectReferenceValue = definition;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static GameObject Slab(string name, Transform parent, Vector3 center, Vector3 size, Material material, bool collider = true)
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
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    static void Deco(PrimitiveType type, string name, Transform parent, Vector3 worldPos, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = worldPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    static void Blocker(Transform parent, string name, Vector3 worldCenter, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = worldCenter;
        go.AddComponent<BoxCollider>().size = size;
    }

    static T Component<T>(string name, Transform parent) where T : Component
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<T>();
    }
}

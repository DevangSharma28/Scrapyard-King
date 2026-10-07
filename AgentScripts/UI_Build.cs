using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ScrapYardKing.Customers;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Tiles;
using ScrapYardKing.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// UI pass: turns the owner's painted atlases in <c>Assets/UI</c> into a sliced sprite kit and dresses the game's UI
/// with it.
///
/// Run (Editor open, not in Play mode), in this order or with <c>UI_Build.All</c>:
///   unity command run_script --file AgentScripts/UI_Build.cs --entry UI_Build.Kit --timeout 300
///   unity command run_script --file AgentScripts/UI_Build.cs --entry UI_Build.Apply --timeout 300
///
/// <b>Kit</b> leaves the source PNGs untouched and writes cleaned copies to <c>Art/UI/Kit</c>:
/// - Atlas1 → <c>Kit_IconsA</c> (items, vehicles, glyphs), Atlas2 → <c>Kit_Panels</c> (panels, buttons, bars),
///   Atlas3 → <c>Kit_IconsB</c> (workers, shop, system buttons).
/// - The paintings sit on a soft haze with colour fringes. A sprite is a connected island of pixels above an alpha
///   threshold; everything else is cleared and the island keeps a one pixel soft rim in its own colour.
/// - Sprites used by the game get a name and 9-slice borders from the tables below (found by a point inside the
///   sprite, so the tables survive a re-export of the atlas with small shifts). The rest are named by row and column.
/// - Three pieces are assembled from atlas parts: an empty bar track, a white bar fill and a white pill, the last two
///   so that code can tint them.
/// Idempotent: sprite names are stable, so references survive a re-run.
///
/// <b>Apply</b> edits the existing prefabs and scene in place (nothing is rebuilt, no script reference changes):
/// - HUD: currency pills, level badge and XP bar, task banner, boss bar, upgrade panel and its card template.
/// - World UI: purchase / upgrade / boost tiles, station labels, customer bubbles.
/// - Adds the loading screen (<c>_UI/LoadingCanvas</c>: background art, title logo, bar) and sets the app icon.
/// Icons of things that exist as models (machines, workers, materials) stay the rendered ones, so a card shows what
/// stands in the yard. Run it again after any M-builder <c>Prefabs</c> or <c>Scene</c> step: those put the old
/// sprites back.
/// </summary>
public static class UI_Build
{
    const string SrcDir = "Assets/UI";
    const string KitDir = "Assets/_Project/Art/UI/Kit";

    // ------------------------------------------------------------------ kit tables

    /// <summary>A named sprite: any point inside it (x from the left, y from the top of the atlas) and its border.</summary>
    readonly struct Pick
    {
        public readonly string name; public readonly int x, yTop; public readonly Vector4 border;
        /// <param name="l">Left border.</param><param name="b">Bottom border.</param><param name="r">Right border.</param><param name="t">Top border.</param>
        public Pick(string name, int x, int yTop, float l = 0, float b = 0, float r = 0, float t = 0)
        { this.name = name; this.x = x; this.yTop = yTop; border = new Vector4(l, b, r, t); }
    }

    static readonly Pick[] PanelPicks =
    {
        new("Panel_CreamHazard", 198, 220, 108, 58, 102, 78),
        new("Panel_BlueHeader", 516, 226, 34, 44, 34, 72),
        new("Panel_Dark", 792, 222, 62, 36, 62, 74),
        new("Panel_CreamTitle", 1121, 123, 68, 28, 68, 78),
        new("Panel_CreamTab", 1418, 123, 60, 50, 60, 60),
        new("Plate_DarkHazard", 1121, 321, 92, 42, 92, 38),
        new("Card_BlueCream", 1414, 318, 22, 40, 22, 52),
        new("Card_Yellow", 1160, 476, 30, 24, 30, 46),
        new("Bubble", 1411, 482),
        new("Bar_Dark", 241, 495, 32, 32, 32, 32),
        new("Bar_Cream", 749, 495, 32, 32, 32, 32),
        new("Btn_Yellow", 138, 600, 26, 28, 26, 24), new("Btn_Green", 387, 600, 26, 28, 26, 24),
        new("Btn_Blue", 635, 600, 26, 28, 26, 24), new("Btn_Red", 884, 600, 26, 28, 26, 24),
        new("Btn_Grey", 1134, 600, 26, 28, 26, 24), new("Btn_Dark", 1393, 603, 26, 26, 26, 24),
        new("BtnFrame_Yellow", 137, 698, 34, 28, 34, 28), new("BtnFrame_Green", 386, 698, 34, 28, 34, 28),
        new("BtnFrame_Blue", 634, 698, 34, 28, 34, 28), new("BtnFrame_Red", 885, 698, 34, 28, 34, 28),
        new("BtnFrame_Grey", 1136, 698, 34, 28, 34, 28),
        new("Sq_DarkCorners", 1339, 721, 34, 34, 34, 34), new("Sq_DarkMetal", 1465, 720, 30, 30, 30, 30),
        new("BtnSmall_Yellow", 105, 793, 24, 26, 24, 22), new("BtnSmall_Green", 284, 793, 24, 26, 24, 22),
        new("BtnSmall_Blue", 461, 793, 24, 26, 24, 22), new("BtnSmall_Red", 639, 793, 24, 26, 24, 22),
        new("BtnSmall_Grey", 817, 793, 24, 26, 24, 22),
        new("Sq_DarkYellow", 978, 795, 28, 30, 28, 28), new("Sq_Cream", 1091, 795, 28, 30, 28, 28),
        new("Sq_Navy", 1203, 796, 28, 30, 28, 28),
        new("Sq_Blue", 1339, 836, 30, 32, 30, 30), new("Sq_Grey", 1465, 836, 30, 32, 30, 30),
        new("Prog_Green", 161, 866), new("Prog_Yellow", 444, 866), new("Prog_Blue", 710, 866),
        new("Toggle_On", 897, 870), new("Toggle_Off", 992, 871),
        new("Dot_Green", 1073, 872), new("Dot_Red", 1120, 872), new("Dot_Blue", 1166, 872),
        new("Dot_Yellow", 1211, 872), new("Dot_Grey", 1253, 872),
        new("Title_Yellow", 171, 955, 104, 0, 104, 0), new("Title_Blue", 496, 952, 100, 0, 100, 0),
        new("Title_Red", 814, 954, 80, 0, 80, 0), new("Title_DarkHazard", 1183, 955, 78, 30, 78, 26),
        new("Tab_Yellow", 1433, 950), new("Tab_Grey", 1493, 950),
    };

    static readonly Pick[] IconAPicks =
    {
        new("Icon_Cash", 77, 74), new("Icon_Coins", 200, 70), new("Icon_Gem", 325, 73), new("Icon_Gear", 1072, 66),
        new("Icon_CashUp", 65, 808), new("Icon_Trophy", 176, 807), new("Icon_Crown", 293, 807), new("Icon_Star", 404, 807),
        new("Icon_Gift", 510, 809), new("Icon_Chest", 620, 809), new("Icon_Lock", 722, 804), new("Icon_ArrowUp", 819, 805),
        new("Icon_ArrowDown", 918, 806), new("Icon_Hammer", 1025, 807), new("Icon_Magnet", 1186, 810),
        new("Icon_Stopwatch", 1307, 805), new("Icon_Fire", 1427, 811), new("Icon_Bolt", 881, 931),
    };

    static readonly Pick[] IconBPicks =
    {
        new("Icon_Upgrade", 424, 72), new("Icon_Backpack", 60, 644), new("Icon_BackpackUp", 178, 646),
        new("Icon_Shoe", 397, 647), new("Icon_SawUp", 516, 646), new("Icon_Saw", 640, 650),
        new("Icon_Settings", 55, 958), new("Icon_Sound", 145, 956), new("Icon_Music", 223, 955),
        new("Btn_Play", 999, 958), new("Btn_Pause", 1084, 958), new("Btn_Stop", 1168, 958),
        new("Btn_Check", 1252, 958), new("Btn_Close", 1336, 958), new("Icon_Bell", 1421, 958),
    };

    // ------------------------------------------------------------------ entry

    public static string Kit()
    {
        Directory.CreateDirectory(KitDir);
        var log = new StringBuilder();

        Atlas("Atlas1.png", "Kit_IconsA.png", 235, "A", IconAPicks, false, log);
        var panels = Atlas("Atlas2.png", "Kit_Panels.png", 128, "P", PanelPicks, true, log);
        Atlas("Atlas3.png", "Kit_IconsB.png", 128, "B", IconBPicks, false, log);
        BarParts(panels, log);

        SingleSprite($"{SrcDir}/LoadingBG.png", false);
        SingleSprite($"{SrcDir}/MainTitleLogo.png", true);
        SingleSprite($"{SrcDir}/MainAppIcon.png", false);
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // ------------------------------------------------------------------ apply

    const string PrefabDir = "Assets/_Project/Prefabs";
    const string FontDir = "Assets/_Project/Art/Fonts";
    const float TrackHeight = 47f;   // Kit_BarTrack as painted; the fill insets below are measured at this height

    public static string All() => Kit() + Apply();

    public static string Apply()
    {
        if (EditorApplication.isPlaying) return "UI_Build.Apply: stop Play mode first";
        var scene = SceneManager.GetActiveScene();
        if (scene.isDirty) EditorSceneManager.SaveScene(scene);
        var log = new StringBuilder();

        // prefabs first: scene instances then already match and get no overrides
        EditPrefab($"{PrefabDir}/Stations/StationLabel.prefab", root => Labels(root));
        int stations = 0, customers = 0;
        foreach (string path in PrefabsWith<StationLabel>($"{PrefabDir}/Stations"))
            if (!path.EndsWith("/StationLabel.prefab")) { EditPrefab(path, root => Labels(root)); stations++; }
        foreach (string path in PrefabsWith<CustomerBubble>($"{PrefabDir}/Customers")) { EditPrefab(path, Bubbles); customers++; }
        EditPrefab($"{PrefabDir}/Tiles/Tile_Purchase.prefab", PurchaseTile);
        EditPrefab($"{PrefabDir}/Tiles/Tile_Upgrade.prefab", UpgradeTile);
        EditPrefab($"{PrefabDir}/Tiles/Tile_Boost.prefab", BoostTile);
        log.AppendLine($"prefabs: label + {stations} stations, {customers} customers, 3 tiles");

        int labels = 0;
        foreach (var label in Object.FindObjectsByType<StationLabel>(FindObjectsInactive.Include))
            labels += Labels(label.transform);
        var ui = GameObject.Find("_UI");
        if (ui == null) throw new Exception("UI_Build: no _UI root in the open scene");
        Hud(Child(ui.transform, "Canvas/HUD"));
        Loading(ui.transform);
        log.AppendLine($"scene: HUD, {labels} station labels, loading screen");

        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>($"{SrcDir}/MainAppIcon.png");
        if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // ------------------------------------------------------------------ HUD

    static void Hud(Transform hud)
    {
        var track = Single("Kit_BarTrack"); var fill = Single("Kit_BarFill");
        var outline = AssetDatabase.LoadAssetAtPath<Material>($"{FontDir}/GROBOLD Outline.mat");

        // currency
        Skin(hud, "CashPill", P("Bar_Dark"), Image.Type.Sliced, 1.2f, Color.white);
        Skin(hud, "CashPill/Icon", A("Icon_Cash"), preserve: true);
        Skin(hud, "GemPill", P("Bar_Dark"), Image.Type.Sliced, 1.2f, Color.white);
        Skin(hud, "GemPill/Icon", A("Icon_Gem"), preserve: true);
        Skin(hud, "FlyLayer/CoinTemplate", A("Icon_Cash"), preserve: true);
        SetRef(hud.GetComponent<HudController>(), "cashIcon", A("Icon_Cash"));

        // yard level
        Place(hud, "LevelBadge/XpBar", new Vector2(112f, -10f), new Vector2(200f, 46f));   // pivot (0, 0.5), set by U1_Build
        Skin(hud, "LevelBadge/XpBar", track, Image.Type.Sliced, TrackHeight / 48f, Color.white);
        BarFill(hud, "LevelBadge/XpBar/Fill", fill, 48f);
        Skin(hud, "LevelBadge/Badge/Star", A("Icon_Star"), preserve: true);
        Hide(hud, "LevelBadge/Badge/Circle");
        SetRef(Child(hud, "LevelBadge").GetComponent<LevelBadge>(), "xpIcon", A("Icon_Star"));

        // task
        Skin(hud, "TaskBanner/Panel/Background", P("Bar_Cream"), Image.Type.Sliced, 1f, Color.white);
        Place(hud, "TaskBanner/Panel/Bar", null, new Vector2(560f, 36f));
        Skin(hud, "TaskBanner/Panel/Bar", track, Image.Type.Sliced, TrackHeight / 36f, Color.white);
        BarFill(hud, "TaskBanner/Panel/Bar/Fill", fill, 36f);
        Skin(hud, "TaskBanner/Panel/Check", B("Btn_Check"), preserve: true);

        // boss bar: the hazard plate is the frame, the health runs in its slot
        Place(hud, "GiantBar/Root", null, new Vector2(820f, 146f));
        Place(hud, "GiantBar/Root/Punch/Frame", null, new Vector2(800f, 96f));
        Skin(hud, "GiantBar/Root/Punch/Frame", P("Title_DarkHazard"), Image.Type.Sliced, 1f, Color.white);
        foreach (string bar in new[] { "Trail", "Fill" })
        {
            var image = BarFill(hud, $"GiantBar/Root/Punch/Frame/{bar}", fill, TrackHeight);
            Stretch((RectTransform)image.transform, 38f, 28f, 43f, 24f);
        }

        UpgradePanel(Child(hud, "UpgradePanel/Sheet"), outline);
    }

    static void UpgradePanel(Transform sheet, Material outline)
    {
        Skin(sheet, "Back", P("Panel_Dark"), Image.Type.Sliced, 0.55f, Color.white);
        Hide(sheet, "Inner");

        // title on a plate that straddles the top edge
        var title = (RectTransform)Child(sheet, "Title");
        var plateRoot = sheet.Find("TitlePlate");
        if (plateRoot == null)
        {
            plateRoot = new GameObject("TitlePlate", typeof(RectTransform), typeof(Image)).transform;
            plateRoot.SetParent(sheet, false);
        }
        plateRoot.gameObject.layer = sheet.gameObject.layer;
        plateRoot.SetSiblingIndex(title.GetSiblingIndex());
        title.SetSiblingIndex(plateRoot.GetSiblingIndex() + 1);   // the title draws over its plate on every re-run
        var plate = (RectTransform)plateRoot;
        plate.anchorMin = plate.anchorMax = new Vector2(0.5f, 1f); plate.pivot = new Vector2(0.5f, 0.5f);
        plate.anchoredPosition = new Vector2(0f, -46f); plate.sizeDelta = new Vector2(560f, 112f);
        var plateImage = plate.GetComponent<Image>();
        plateImage.sprite = P("Title_Yellow"); plateImage.type = Image.Type.Sliced;
        plateImage.pixelsPerUnitMultiplier = 93f / 112f; plateImage.raycastTarget = false;

        title.anchorMin = title.anchorMax = new Vector2(0.5f, 1f); title.pivot = new Vector2(0.5f, 0.5f);
        title.anchoredPosition = new Vector2(0f, -50f); title.sizeDelta = new Vector2(320f, 70f);
        var titleText = title.GetComponent<TMP_Text>();
        titleText.alignment = TextAlignmentOptions.Center; titleText.fontSize = 46f;
        Touch(title); Touch(titleText);

        Place(sheet, "Close", new Vector2(-66f, -50f), new Vector2(96f, 96f));
        Skin(sheet, "Close", B("Btn_Close"), color: Color.white, preserve: true);
        Hide(sheet, "Close/X");

        Stretch((RectTransform)Child(sheet, "Scroll"), 50f, 20f, 50f, 140f);

        // card: blue header carries the name, the body the icon and numbers, the footer the button
        var card = Child(sheet, "Templates/CardTemplate");
        Skin(card, "Background", P("Panel_BlueHeader"), Image.Type.Sliced, 1.3f, Color.white);
        Hide(card, "IconBack");
        Place(card, "Icon", new Vector2(0f, 52f), new Vector2(118f, 118f));
        Place(card, "LevelPill", new Vector2(76f, -78f), new Vector2(124f, 40f));   // fits "LV 12 → 13" (U5)
        Skin(card, "LevelPill", P("BtnSmall_Yellow"), Image.Type.Sliced, 72f / 38f, Color.white);
        Child(card, "LevelPill/Level").GetComponent<TMP_Text>().fontSize = 22f;

        Place(card, "Title", new Vector2(0f, 139f), new Vector2(270f, 40f));
        var name = Child(card, "Title").GetComponent<TMP_Text>();
        name.color = Color.white; name.fontSizeMax = 28f; name.fontSize = 28f;
        if (outline != null) name.fontSharedMaterial = outline;
        Touch(name);

        Place(card, "Pips", new Vector2(0f, -24f), null);
        Place(card, "Effect", new Vector2(0f, -50f), null);
        Place(card, "Buy", new Vector2(0f, -112f), new Vector2(266f, 72f));
        Skin(card, "Buy", P("Btn_Green"));
        var savings = Child(card, "Buy/Savings").GetComponent<Image>();
        savings.sprite = P("Btn_Green"); Touch(savings);
        Skin(card, "Buy/Coin", A("Icon_Cash"), preserve: true);

        var lockOverlay = Child(card, "Lock").GetComponent<Image>();
        lockOverlay.pixelsPerUnitMultiplier = 2.6f; Touch(lockOverlay);
        Place(card, "Lock/Pill", new Vector2(0f, -112f), new Vector2(266f, 72f));
        Skin(card, "Lock/Pill", P("Btn_Dark"), color: Color.white);
        Skin(card, "Lock/Pill/LockIcon", A("Icon_Lock"), preserve: true);
    }

    // ------------------------------------------------------------------ world UI

    /// <summary>Restyles every station label under <paramref name="root"/>; returns how many it found.</summary>
    static int Labels(Transform root)
    {
        int count = 0;
        foreach (var label in root.GetComponentsInChildren<StationLabel>(true))
        {
            var canvas = label.transform.Find("Canvas");
            if (canvas == null) continue;
            Skin(canvas, "Header", P("Bar_Dark"), Image.Type.Sliced, 1.5f, Color.white);
            Skin(canvas, "Header/LevelPill", P("BtnSmall_Yellow"), Image.Type.Sliced, 1.45f, Color.white);
            Skin(canvas, "Counter/Chip", P("Sq_Cream"), Image.Type.Sliced, 1.9f, Color.white);
            Skin(canvas, "Status", Single("Kit_PillWhite"), Image.Type.Sliced, 1.45f);   // colour = status, set in code
            count++;
        }
        return count;
    }

    static void Bubbles(Transform root)
    {
        var cream = P("Sq_Cream");
        var lip = SamplePng(cream, 0.5f, 0.07f);
        foreach (var bubble in root.GetComponentsInChildren<CustomerBubble>(true))
        {
            var t = bubble.transform;
            Skin(t, "Root/Bg", cream, Image.Type.Sliced, 1.1f, Color.white);
            var tail = Child(t, "Root/Tail").GetComponent<Image>();
            tail.color = lip; Touch(tail);
            Place(t, "Root/Order/WaitBg", null, new Vector2(144f, 18f));
            Skin(t, "Root/Order/WaitBg", Single("Kit_BarTrack"), Image.Type.Sliced, TrackHeight / 18f, Color.white);
            BarFill(t, "Root/Order/WaitBg/Wait", Single("Kit_BarFill"), 18f);
            Skin(t, "Root/Happy/Circle", B("Btn_Check"), color: Color.white, preserve: true);
            Hide(t, "Root/Happy/Circle/Check");
        }
    }

    /// <summary>
    /// The buy tile. It used to be a UI card lying on the ground (painted frame with green corner blobs on a flat slab),
    /// which did not belong to the world around it (owner, 2026-10-06). It is now a piece of the yard: a steel floor
    /// plate with a hazard-striped rim and corner bolts, the same language as the pit kerb and the machines' bases, with
    /// a plain rounded panel on it. The panel's colour is the state (navy = save up, green = you can pay, grey =
    /// locked); the pay fill, icon, name and price sit on it.
    /// </summary>
    static void PurchaseTile(Transform root)
    {
        var content = Child(root, "Visual/Display/Content");
        var round = content.Find("Fill").GetComponent<Image>().sprite;

        // 3D base: replaces the flat slab.
        var visual = Child(root, "Visual");
        var slab = visual.Find("Base");
        if (slab != null && slab.TryGetComponent(out Renderer slabRenderer)) slabRenderer.enabled = false;
        var oldArt = visual.Find("ArtBase");
        if (oldArt != null) Object.DestroyImmediate(oldArt.gameObject);
        var steel = ArtMaterials.Mat("TilePlate", new Color(0.2f, 0.22f, 0.27f), 0.45f, 0.25f, ArtMaterials.Detail.Metal, 0.8f);
        var bolt = ArtMaterials.Mat("MachSteel", new Color(0.55f, 0.58f, 0.63f), 0.5f, 0.3f, ArtMaterials.Detail.Metal, 0.8f);
        var hazard = AssetDatabase.LoadAssetAtPath<Material>(ArtAssets.MatDir + "/M_MachHazard.mat");
        if (hazard == null) hazard = ArtMaterials.Mat("TileRim", new Color(1f, 0.78f, 0.12f), 0.4f, 0f, ArtMaterials.Detail.Paint, 0.7f);
        const float size = 2.5f, rim = 0.17f;
        var k = new MeshKit();
        k.Box(0, new Vector3(0f, 0.03f, 0f), new Vector3(size, 0.06f, size), 0.02f);
        float edge = size * 0.5f - rim * 0.5f;
        k.Box(1, new Vector3(0f, 0.065f, edge), new Vector3(size, 0.05f, rim), 0.015f);
        k.Box(1, new Vector3(0f, 0.065f, -edge), new Vector3(size, 0.05f, rim), 0.015f);
        k.Box(1, new Vector3(edge, 0.065f, 0f), new Vector3(rim, 0.05f, size - rim * 2f), 0.015f);
        k.Box(1, new Vector3(-edge, 0.065f, 0f), new Vector3(rim, 0.05f, size - rim * 2f), 0.015f);
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                k.Cylinder(2, new Vector3(sx * edge, 0.1f, sz * edge), 0.055f, 0.03f, 8);
        var art = ArtAssets.Part("Tiles", "PurchaseBase", k, new[] { steel, hazard, bolt }, visual);
        art.name = "ArtBase";
        art.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Panel inside the rim. PurchaseTile tints it by state, so it is a white sprite.
        var frame = Skin(content, "Frame", round, Image.Type.Sliced, 1f, Color.white);
        frame.fillCenter = true;
        frame.transform.SetAsFirstSibling();
        Place(content, "Frame", Vector2.zero, new Vector2(206f, 206f));
        Place(content, "Fill", Vector2.zero, new Vector2(206f, 206f));
        Place(content, "Lock/Dim", Vector2.zero, new Vector2(206f, 206f));
        var tile = root.GetComponent<PurchaseTile>();
        var so = new SerializedObject(tile);
        so.FindProperty("idleColor").colorValue = new Color(0.11f, 0.15f, 0.25f, 0.94f);
        so.FindProperty("readyColor").colorValue = new Color(0.16f, 0.5f, 0.24f, 0.96f);
        so.FindProperty("lockedColor").colorValue = new Color(0.2f, 0.21f, 0.25f, 0.94f);
        so.ApplyModifiedPropertiesWithoutUndo();
        var fill = content.Find("Fill").GetComponent<Image>();
        fill.color = new Color(0.4f, 0.95f, 0.45f, 0.5f);

        // Bigger icon in the middle, name above, price below; everything inside the panel.
        Place(content, "Icon", new Vector2(0f, 6f), new Vector2(118f, 118f));
        Place(content, "Title", new Vector2(0f, 80f), new Vector2(196f, 34f));
        Place(content, "Count", new Vector2(64f, 50f), new Vector2(70f, 26f));   // top right, clear of the lock text and the price
        Place(content, "Price", new Vector2(0f, -76f), new Vector2(196f, 44f));
        Place(content, "Lock/LockIcon", new Vector2(0f, 8f), new Vector2(76f, 76f));
        Place(content, "Lock/LockText", new Vector2(0f, -68f), new Vector2(190f, 44f));
        Skin(content, "Price/Coin", A("Icon_Cash"), preserve: true);
        Skin(content, "Lock/LockIcon", A("Icon_Lock"), preserve: true);
        Touch(tile);
    }

    static void UpgradeTile(Transform root)
    {
        var content = Child(root, "Visual/Display/Content");
        Place(content, "Badge", null, new Vector2(164f, 164f));
        Skin(content, "Badge", B("Icon_Upgrade"), color: Color.white, preserve: true);
        Hide(content, "Badge/Arrow");
    }

    static void BoostTile(Transform root) =>
        Skin(Child(root, "Visual/Display/Content"), "Bolt", A("Icon_Bolt"), color: Color.white, preserve: true);

    // ------------------------------------------------------------------ loading screen

    /// <summary>
    /// <c>_UI/LoadingCanvas</c>: its own overlay canvas above the HUD. The cover is rebuilt on every run and left
    /// inactive; <see cref="LoadingScreen"/> shows it when the game starts.
    /// </summary>
    static void Loading(Transform ui)
    {
        var mainScaler = Child(ui, "Canvas").GetComponent<CanvasScaler>();
        var root = ui.Find("LoadingCanvas");
        if (root == null)
        {
            root = new GameObject("LoadingCanvas", typeof(RectTransform)).transform;
            root.SetParent(ui, false);
        }
        root.gameObject.layer = mainScaler.gameObject.layer;
        var canvas = Ensure<Canvas>(root);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = Ensure<CanvasScaler>(root);
        scaler.uiScaleMode = mainScaler.uiScaleMode;
        scaler.referenceResolution = mainScaler.referenceResolution;
        scaler.screenMatchMode = mainScaler.screenMatchMode;
        scaler.matchWidthOrHeight = mainScaler.matchWidthOrHeight;
        Ensure<GraphicRaycaster>(root);

        var old = root.Find("Cover");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var cover = Node("Cover", root);
        Stretch(cover, 0f, 0f, 0f, 0f);
        var group = cover.gameObject.AddComponent<CanvasGroup>();

        var backdrop = Picture("Backdrop", cover, null, new Color(0.09f, 0.12f, 0.2f));
        Stretch(backdrop.rectTransform, 0f, 0f, 0f, 0f);

        // the painting covers the screen at any aspect; what does not fit is cropped evenly
        var art = Picture("Art", cover, LoadSprite($"{SrcDir}/LoadingBG.png"), Color.white);
        var fitter = art.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = art.sprite.rect.width / art.sprite.rect.height;

        var logo = Picture("Logo", cover, LoadSprite($"{SrcDir}/MainTitleLogo.png"), Color.white);
        logo.preserveAspect = true; logo.raycastTarget = false;
        Anchor(logo.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -340f), new Vector2(1000f, 500f));

        var plate = Picture("Plate", cover, P("Plate_DarkHazard"), Color.white);
        plate.type = Image.Type.Sliced; plate.pixelsPerUnitMultiplier = 0.85f; plate.raycastTarget = false;
        Anchor(plate.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 240f), new Vector2(900f, 230f));

        var label = Label("Label", plate.transform, "LOADING...", 40f, new Color(1f, 0.85f, 0.25f));
        Anchor(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 38f), new Vector2(700f, 54f));

        var track = Picture("Track", plate.transform, Single("Kit_BarTrack"), Color.white);
        track.type = Image.Type.Sliced; track.pixelsPerUnitMultiplier = TrackHeight / 52f; track.raycastTarget = false;
        Anchor(track.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -28f), new Vector2(720f, 52f));
        Picture("Fill", track.transform, null, new Color(0.5f, 0.92f, 0.28f)).raycastTarget = false;
        var fill = BarFill(track.transform, "Fill", Single("Kit_BarFill"), 52f);
        fill.fillAmount = 0.4f;
        var percent = Label("Percent", track.transform, "40%", 28f, Color.white);
        Stretch(percent.rectTransform, 0f, 0f, 0f, 0f);

        var screen = Ensure<LoadingScreen>(root);
        var so = new SerializedObject(screen);
        so.FindProperty("cover").objectReferenceValue = cover.gameObject;
        so.FindProperty("group").objectReferenceValue = group;
        so.FindProperty("fill").objectReferenceValue = fill;
        so.FindProperty("logo").objectReferenceValue = logo.rectTransform;
        so.FindProperty("percent").objectReferenceValue = percent;
        so.ApplyModifiedPropertiesWithoutUndo();

        cover.gameObject.SetActive(false);
    }

    static RectTransform Node(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static Image Picture(string name, Transform parent, Sprite sprite, Color color)
    {
        var image = Node(name, parent).gameObject.AddComponent<Image>();
        image.sprite = sprite; image.color = color;
        return image;
    }

    static TMP_Text Label(string name, Transform parent, string text, float size, Color color)
    {
        var label = Node(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{FontDir}/GROBOLD SDF.asset");
        var outline = AssetDatabase.LoadAssetAtPath<Material>($"{FontDir}/GROBOLD Outline.mat");
        if (outline != null) label.fontSharedMaterial = outline;
        label.text = text; label.fontSize = size; label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }

    static void Anchor(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position; rt.sizeDelta = size;
    }

    // ------------------------------------------------------------------ helpers

    static readonly Dictionary<string, Sprite[]> sheetCache = new();
    static readonly Dictionary<string, Texture2D> pngCache = new();

    static Sprite KitSprite(string sheet, string name)
    {
        if (!sheetCache.TryGetValue(sheet, out var all))
            sheetCache[sheet] = all = AssetDatabase.LoadAllAssetsAtPath($"{KitDir}/{sheet}.png").OfType<Sprite>().ToArray();
        var sprite = all.FirstOrDefault(s => s.name == name);
        if (sprite == null) throw new Exception($"UI_Build: no sprite '{name}' in {sheet} (run UI_Build.Kit first)");
        return sprite;
    }

    static Sprite P(string name) => KitSprite("Kit_Panels", name);
    static Sprite A(string name) => KitSprite("Kit_IconsA", name);
    static Sprite B(string name) => KitSprite("Kit_IconsB", name);
    static Sprite Single(string name) => LoadSprite($"{KitDir}/{name}.png");

    static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) throw new Exception($"UI_Build: no sprite at {path} (run UI_Build.Kit first)");
        return sprite;
    }

    /// <summary>The colour of a sprite at a point of its rect (0–1 from the bottom left), read from the PNG.</summary>
    static Color SamplePng(Sprite sprite, float u, float v)
    {
        string path = AssetDatabase.GetAssetPath(sprite);
        if (!pngCache.TryGetValue(path, out var tex) || tex == null)
        {
            tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            tex.LoadImage(File.ReadAllBytes(path));
            pngCache[path] = tex;
        }
        var r = sprite.rect;
        var c = tex.GetPixel(Mathf.RoundToInt(r.x + r.width * u), Mathf.RoundToInt(r.y + r.height * v));
        c.a = 1f;
        return c;
    }

    static Transform Child(Transform root, string path)
    {
        var t = root.Find(path);
        if (t == null) throw new Exception($"UI_Build: '{path}' not found under '{root.name}'");
        return t;
    }

    static T Ensure<T>(Transform t) where T : Component => t.TryGetComponent<T>(out var c) ? c : t.gameObject.AddComponent<T>();

    /// <summary>Marks an edited object for saving; on a prefab instance the change is recorded as an override.</summary>
    static void Touch(Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    static Image Skin(Transform root, string path, Sprite sprite, Image.Type type = Image.Type.Simple, float ppu = 1f,
        Color? color = null, bool preserve = false)
    {
        var image = Child(root, path).GetComponent<Image>();
        if (image == null) throw new Exception($"UI_Build: '{path}' under '{root.name}' has no Image");
        image.sprite = sprite;
        image.type = type;
        image.pixelsPerUnitMultiplier = ppu;
        image.preserveAspect = preserve;
        if (color.HasValue) image.color = color.Value;
        Touch(image);
        return image;
    }

    /// <summary>A horizontal fill sitting inside a <c>Kit_BarTrack</c> of the given height.</summary>
    static Image BarFill(Transform root, string path, Sprite sprite, float barHeight)
    {
        var image = Child(root, path).GetComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Horizontal;
        image.fillOrigin = (int)Image.OriginHorizontal.Left;
        image.preserveAspect = false;
        float k = barHeight / TrackHeight;
        Stretch((RectTransform)image.transform, 10f * k, 8.5f * k, 10f * k, 9.5f * k);
        Touch(image);
        return image;
    }

    static void Hide(Transform root, string path)
    {
        var graphic = Child(root, path).GetComponent<Graphic>();
        if (graphic == null) return;
        graphic.enabled = false;
        Touch(graphic);
    }

    static void Place(Transform root, string path, Vector2? position, Vector2? size)
    {
        var rt = (RectTransform)Child(root, path);
        if (position.HasValue) rt.anchoredPosition = position.Value;
        if (size.HasValue) rt.sizeDelta = size.Value;
        Touch(rt);
    }

    static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
        Touch(rt);
    }

    static void SetRef(Object target, string property, Object value)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(property);
        if (prop == null) throw new Exception($"UI_Build: {target.GetType().Name} has no field '{property}'");
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void EditPrefab(string path, Action<Transform> edit)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            edit(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static List<string> PrefabsWith<T>(string folder) where T : Component =>
        AssetDatabase.FindAssets("t:Prefab", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => AssetDatabase.LoadAssetAtPath<GameObject>(p).GetComponentInChildren<T>(true) != null).OrderBy(p => p).ToList();

    // ------------------------------------------------------------------ atlas

    sealed class Sheet
    {
        public Color32[] source, clean; public int w, h;
        public readonly Dictionary<string, RectInt> rects = new();
    }

    const int MinIsland = 30;   // smaller islands belong to the icon next to them (a "Z", a sparkle)
    const int IslandGap = 6;
    const int RowTolerance = 50;

    /// <param name="opaque">Panels and buttons: the painted bodies are slightly see-through; make everything but the
    /// edge fully opaque so the world does not show through a panel.</param>
    static Sheet Atlas(string file, string output, int threshold, string prefix, Pick[] picks, bool opaque, StringBuilder log)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes($"{SrcDir}/{file}"));
        var sheet = new Sheet { source = tex.GetPixels32(), w = tex.width, h = tex.height };
        Object.DestroyImmediate(tex);
        int w = sheet.w, h = sheet.h;
        var px = sheet.source;

        var rects = Islands(px, w, h, threshold);
        var keep = new bool[px.Length];
        foreach (var r in rects)
            for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                    if (px[y * w + x].a >= threshold) keep[y * w + x] = true;

        sheet.clean = Clean(px, keep, w, h, threshold);
        if (opaque) FillBodies(sheet.clean, keep, w, h);

        // rows from the top, columns from the left: the fallback name of a sprite
        var rows = new List<List<RectInt>>();
        foreach (var r in rects.OrderByDescending(r => r.center.y))
        {
            var row = rows.FirstOrDefault(rw => Mathf.Abs((float)rw.Average(q => q.center.y) - r.center.y) < RowTolerance);
            if (row == null) rows.Add(row = new List<RectInt>());
            row.Add(r);
        }

        var meta = new List<SpriteMetaData>();
        var used = new HashSet<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i].OrderBy(r => r.xMin).ToList();
            for (int c = 0; c < row.Count; c++)
            {
                var r = row[c];
                string name = $"{prefix}_r{i}_c{c}";
                var border = Vector4.zero;
                foreach (var p in picks)
                {
                    if (!r.Contains(new Vector2Int(p.x, h - 1 - p.yTop))) continue;
                    name = p.name; border = p.border; used.Add(p.name);
                    break;
                }
                var padded = new RectInt(Mathf.Max(0, r.xMin - 1), Mathf.Max(0, r.yMin - 1), 0, 0);
                padded.xMax = Mathf.Min(w, r.xMax + 1); padded.yMax = Mathf.Min(h, r.yMax + 1);
                sheet.rects[name] = padded;
                meta.Add(new SpriteMetaData
                {
                    name = name, rect = new Rect(padded.x, padded.y, padded.width, padded.height),
                    alignment = (int)SpriteAlignment.Center, pivot = new Vector2(0.5f, 0.5f),
                    border = border == Vector4.zero ? border : border + Vector4.one,   // the padding pixel
                });
            }
        }
        foreach (var p in picks) if (!used.Contains(p.name)) log.AppendLine($"  !! {file}: no sprite under {p.name} ({p.x},{p.yTop})");

        string path = $"{KitDir}/{output}";
        WritePng(path, sheet.clean, w, h);
        ImportSheet(path, meta.ToArray());
        log.AppendLine($"{output}: {meta.Count} sprites in {rows.Count} rows, {used.Count} named");
        return sheet;
    }

    /// <summary>Bounding boxes of the connected pixel islands at or above the alpha threshold; small ones join a neighbour.</summary>
    static List<RectInt> Islands(Color32[] px, int w, int h, int threshold)
    {
        var seen = new bool[px.Length];
        var rects = new List<RectInt>();
        var stack = new Stack<int>();
        for (int i = 0; i < px.Length; i++)
        {
            if (seen[i] || px[i].a < threshold) continue;
            int x0 = w, x1 = 0, y0 = h, y1 = 0;
            stack.Push(i); seen[i] = true;
            while (stack.Count > 0)
            {
                int c = stack.Pop(); int cx = c % w, cy = c / w;
                if (cx < x0) x0 = cx; if (cx > x1) x1 = cx; if (cy < y0) y0 = cy; if (cy > y1) y1 = cy;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int ni = ny * w + nx;
                        if (seen[ni] || px[ni].a < threshold) continue;
                        seen[ni] = true; stack.Push(ni);
                    }
            }
            rects.Add(new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
        }

        bool merged = true;
        while (merged)
        {
            merged = false;
            for (int i = 0; i < rects.Count && !merged; i++)
                for (int j = i + 1; j < rects.Count && !merged; j++)
                {
                    var a = rects[i]; var b = rects[j];
                    bool near = a.xMin - IslandGap < b.xMax && b.xMin - IslandGap < a.xMax &&
                                a.yMin - IslandGap < b.yMax && b.yMin - IslandGap < a.yMax;
                    bool smallA = a.width < MinIsland || a.height < MinIsland, smallB = b.width < MinIsland || b.height < MinIsland;
                    if (!near || (!smallA && !smallB)) continue;
                    int x0 = Mathf.Min(a.xMin, b.xMin), y0 = Mathf.Min(a.yMin, b.yMin);
                    rects[i] = new RectInt(x0, y0, Mathf.Max(a.xMax, b.xMax) - x0, Mathf.Max(a.yMax, b.yMax) - y0);
                    rects.RemoveAt(j); merged = true;
                }
        }
        return rects.Where(r => r.width >= MinIsland && r.height >= MinIsland).ToList();
    }

    /// <summary>Kept pixels as painted; a one pixel rim around them in their own colour at reduced alpha; nothing else.</summary>
    static Color32[] Clean(Color32[] px, bool[] keep, int w, int h, int threshold)
    {
        const int MinRimAlpha = 24;
        const float RimStrength = 0.5f;
        var result = new Color32[px.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (keep[i]) { result[i] = px[i]; continue; }
                if (px[i].a < MinRimAlpha) continue;
                int r = 0, g = 0, b = 0, n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int ni = ny * w + nx;
                        if (!keep[ni]) continue;
                        r += px[ni].r; g += px[ni].g; b += px[ni].b; n++;
                    }
                if (n == 0) continue;
                byte alpha = (byte)(Mathf.Min(px[i].a, threshold) * RimStrength * Mathf.Min(1f, n / 3f));
                result[i] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), alpha);
            }
        return result;
    }

    /// <summary>Full alpha for every kept pixel that is at least <c>Edge</c> pixels inside its island (the soft edge stays).</summary>
    static void FillBodies(Color32[] clean, bool[] keep, int w, int h)
    {
        const int Edge = 2;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!keep[i] || clean[i].a == 255) continue;
                bool inside = x >= Edge && y >= Edge && x < w - Edge && y < h - Edge;
                for (int dy = -Edge; inside && dy <= Edge; dy++)
                    for (int dx = -Edge; dx <= Edge; dx++)
                        if (!keep[(y + dy) * w + x + dx]) { inside = false; break; }
                if (inside) clean[i].a = 255;
            }
    }

    // ------------------------------------------------------------------ assembled pieces

    /// <summary>
    /// The atlas bars are painted with a fill in them. The track is the empty right end mirrored; the fill is the green
    /// one, stretched to a long strip and turned white so <c>Image.color</c> decides the colour.
    /// </summary>
    static void BarParts(Sheet panels, StringBuilder log)
    {
        var px = panels.clean; int w = panels.w;
        var bar = panels.rects["Prog_Green"];

        const int End = 72;
        int tw = End * 2, th = bar.height;
        var track = new Color32[tw * th];
        for (int y = 0; y < th; y++)
            for (int x = 0; x < End; x++)
            {
                var c = px[(bar.yMin + y) * w + bar.xMax - End + x];
                track[y * tw + End + x] = c;
                track[y * tw + End - 1 - x] = c;
            }
        WritePng($"{KitDir}/Kit_BarTrack.png", track, tw, th);
        ImportSingle($"{KitDir}/Kit_BarTrack.png", new Vector4(22, 20, 22, 20));

        // the fill: saturated green pixels inside the bar
        int x0 = int.MaxValue, x1 = 0, y0 = int.MaxValue, y1 = 0;
        for (int y = bar.yMin; y < bar.yMax; y++)
            for (int x = bar.xMin; x < bar.xMax; x++)
            {
                var c = px[y * w + x];
                if (c.a < 200 || c.g < 120 || c.g < c.r * 1.25f || c.g < c.b * 1.6f) continue;
                if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
            }
        // one more pixel all round: the dark outline of the fill
        x0--; y0--; x1++; y1++;
        int fh = y1 - y0 + 1, cap = 14, fw = fh * 12;
        int peak = 1;
        for (int y = y0; y <= y1; y++) { var c = px[y * w + (x0 + x1) / 2]; peak = Mathf.Max(peak, Mathf.Max(c.r, Mathf.Max(c.g, c.b))); }
        var fill = new Color32[fw * fh];
        for (int y = 0; y < fh; y++)
            for (int x = 0; x < fw; x++)
            {
                int sx = x < cap ? x0 + x : x >= fw - cap ? x1 - (fw - 1 - x) : (x0 + x1) / 2;
                var c = px[(y0 + y) * w + sx];
                bool green = c.g >= c.r && c.g >= c.b;
                // outside the rounded caps the track shows through: transparent
                byte a = green || (sx > x0 + 3 && sx < x1 - 3 && y > 0 && y < fh - 1) ? c.a : (byte)0;
                byte v = (byte)Mathf.Min(255, Mathf.Max(c.r, Mathf.Max(c.g, c.b)) * 250 / peak);
                fill[y * fw + x] = new Color32(v, v, v, a);
            }
        WritePng($"{KitDir}/Kit_BarFill.png", fill, fw, fh);
        ImportSingle($"{KitDir}/Kit_BarFill.png", Vector4.zero);

        // white pill: the small grey button with its value lifted to white
        var grey = panels.rects["BtnSmall_Grey"];
        var body = px[(grey.yMin + grey.height / 2) * w + grey.xMin + grey.width / 2];
        int bodyValue = Mathf.Max(1, Mathf.Max(body.r, Mathf.Max(body.g, body.b)));
        var pill = new Color32[grey.width * grey.height];
        for (int y = 0; y < grey.height; y++)
            for (int x = 0; x < grey.width; x++)
            {
                var c = px[(grey.yMin + y) * w + grey.xMin + x];
                byte v = (byte)Mathf.Min(255, Mathf.Max(c.r, Mathf.Max(c.g, c.b)) * 236 / bodyValue);
                pill[y * grey.width + x] = new Color32(v, v, v, c.a);
            }
        WritePng($"{KitDir}/Kit_PillWhite.png", pill, grey.width, grey.height);
        ImportSingle($"{KitDir}/Kit_PillWhite.png", new Vector4(25, 27, 25, 23));

        log.AppendLine($"bar: track {tw}x{th}, fill {fw}x{fh} from ({x0 - bar.xMin},{y0 - bar.yMin}) {x1 - x0 + 1}x{fh} in a {bar.width}x{bar.height} bar; pill {grey.width}x{grey.height}");
    }

    // ------------------------------------------------------------------ import

    static void WritePng(string path, Color32[] pixels, int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(pixels); tex.Apply();
        var bytes = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes)) return;
        File.WriteAllBytes(path, bytes);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    static TextureImporter UiImporter(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = 100;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);
        return importer;
    }

    static void ImportSheet(string path, SpriteMetaData[] sprites)
    {
        var importer = UiImporter(path);
        importer.spriteImportMode = SpriteImportMode.Multiple;
#pragma warning disable 618   // the 2D Sprite package (ISpriteEditorDataProvider) is not in this project
        importer.spritesheet = sprites;
#pragma warning restore 618
        importer.SaveAndReimport();
    }

    static void ImportSingle(string path, Vector4 border)
    {
        var importer = UiImporter(path);
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = border;
        importer.SaveAndReimport();
    }

    /// <summary>Source art used as it is (loading background, logo, app icon): one sprite per file.</summary>
    static void SingleSprite(string path, bool transparent)
    {
        var importer = UiImporter(path);
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = transparent;
        importer.SaveAndReimport();
    }
}

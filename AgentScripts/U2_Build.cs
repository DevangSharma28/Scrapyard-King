using System.IO;
using System.Linq;
using System.Text;
using ScrapYardKing.Boosts;
using ScrapYardKing.Economy;
using ScrapYardKing.Feedback;
using ScrapYardKing.Shop;
using ScrapYardKing.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// UI step U2: the shop and real-money purchases.
///  - Assets: <c>Data/Shop</c>: eight <c>IAPProductConfig</c> (starter pack, six diamond bundles, factory pack, a
///    disabled No Ads) and <c>ShopCatalog</c> (diamond boosts, cash crates, free diamonds for a video).
///  - Scene: <c>StoreService</c> in _Systems; "+" buttons on the cash and diamond capsules; the shop panel
///    (<c>_UI/Canvas/ShopPanel</c>) with four tabs and three card templates; the currency capsules, the coin layer and
///    the popup layer get their own sorting so the balance stays readable (and coins land visibly) over the shop.
/// Entry points: Assets, Scene (or All). Idempotent: the shop panel is rebuilt on each run. Run after U1_Build.Scene.
/// </summary>
public static class U2_Build
{
    const string Root = "Assets/_Project";
    const string KitDir = Root + "/Art/UI/Kit";
    const string ShopDir = Root + "/Data/Shop";
    const string BoostDir = Root + "/Data/Boosts";
    const string ScenePath = Root + "/Scenes/Area1_OldScrapYard.unity";

    static readonly StringBuilder Log = new();
    static TMP_FontAsset font;
    static Material outline;
    static readonly Color Navy = new(0.12f, 0.17f, 0.3f);

    public static string All() => Assets() + Scene();

    // ================================================================== assets

    public static string Assets()
    {
        Log.Clear();
        Directory.CreateDirectory(ShopDir);
        var production = AssetDatabase.LoadAssetAtPath<BoostDefinition>(BoostDir + "/Boost_Production.asset");
        var cashBoost = AssetDatabase.LoadAssetAtPath<BoostDefinition>(BoostDir + "/Boost_Cash.asset");

        // price ladder: diamonds per dollar rise with the pack (101 → 110 → 150 → 163 → 180 → 220), the starter beats all
        var starter = Product("IAP_Starter", "syk_starter_pack", StoreProductType.NonConsumable, ShopTier.Starter, "INDUSTRIAL STARTER PACK",
            B("B_r3_c1"), 500, 0, 150, production, 30f, null, 0f, false, "BEST VALUE", "$1.99", 1.99f);
        var bundles = new[]
        {
            Product("IAP_Gems100", "syk_gems_100", StoreProductType.Consumable, ShopTier.Starter, "HANDFUL OF DIAMONDS", A("Icon_Gem"), 100, 0, 0, null, 0, null, 0, false, "", "$0.99", 0.99f),
            Product("IAP_Gems500", "syk_gems_500", StoreProductType.Consumable, ShopTier.Value, "POUCH OF DIAMONDS", B("B_r4_c7"), 500, 50, 0, null, 0, null, 0, false, "", "$4.99", 4.99f),
            Product("IAP_Gems1200", "syk_gems_1200", StoreProductType.Consumable, ShopTier.Value, "BAG OF DIAMONDS", B("B_r4_c9"), 1200, 300, 0, null, 0, null, 0, false, "POPULAR", "$9.99", 9.99f),
            Product("IAP_Gems2500", "syk_gems_2500", StoreProductType.Consumable, ShopTier.Power, "SACK OF DIAMONDS", B("B_r3_c3"), 2500, 750, 0, null, 0, null, 0, false, "", "$19.99", 19.99f),
            Product("IAP_Gems6500", "syk_gems_6500", StoreProductType.Consumable, ShopTier.Power, "CHEST OF DIAMONDS", B("B_r4_c10"), 6500, 2500, 0, null, 0, null, 0, false, "", "$49.99", 49.99f),
            Product("IAP_Gems15000", "syk_gems_15000", StoreProductType.Consumable, ShopTier.Mega, "DIAMOND VAULT", B("B_r4_c12"), 15000, 7000, 0, null, 0, null, 0, false, "BEST VALUE", "$99.99", 99.99f),
        };
        var factory = Product("IAP_FactoryPack", "syk_factory_pack", StoreProductType.Consumable, ShopTier.Power, "FACTORY BOOST PACK",
            B("B_r3_c6"), 300, 0, 0, production, 30f, cashBoost, 30f, false, "POWER", "$4.99", 4.99f);
        // No Ads is defined but not sold: the game shows no forced ads, so there is nothing for it to remove yet.
        var noAds = Product("IAP_NoAds", "syk_no_ads", StoreProductType.NonConsumable, ShopTier.Value, "GO AD-FREE",
            B("B_r8_c10"), 0, 0, 0, null, 0, null, 0, true, "", "$3.99", 3.99f);
        SetBool(noAds, "enabled", false);

        var catalog = LoadOrCreate<ShopCatalog>(ShopDir + "/ShopCatalog.asset");
        var so = new SerializedObject(catalog);
        so.FindProperty("hero").objectReferenceValue = starter;
        Array(so.FindProperty("bundles"), bundles);
        Array(so.FindProperty("specials"), new Object[] { factory, noAds });
        var boostItems = new (string asset, float minutes)[] { ("Boost_Cash", 10f), ("Boost_Production", 10f), ("Boost_ScrapRush", 5f), ("Boost_Speed", 10f), ("Boost_TruckRush", 10f) };
        var boosts = so.FindProperty("boosts");
        boosts.arraySize = boostItems.Length;
        for (int i = 0; i < boostItems.Length; i++)
        {
            var e = boosts.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("boost").objectReferenceValue = AssetDatabase.LoadAssetAtPath<BoostDefinition>($"{BoostDir}/{boostItems[i].asset}.asset");
            e.FindPropertyRelative("minutes").floatValue = boostItems[i].minutes;
        }

        var crates = new (string title, int diamonds, float bonus, Sprite icon)[]
        {
            ("STACK OF CASH", 20, 1f, A("Icon_Cash")), ("CASH CRATE", 60, 1.1f, A("A_r6_c6")), ("CASH VAULT", 150, 1.25f, B("B_r7_c11")),
        };
        var crateList = so.FindProperty("crates");
        crateList.arraySize = crates.Length;
        for (int i = 0; i < crates.Length; i++)
        {
            var e = crateList.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("title").stringValue = crates[i].title;
            e.FindPropertyRelative("diamonds").intValue = crates[i].diamonds;
            e.FindPropertyRelative("bonus").floatValue = crates[i].bonus;
            e.FindPropertyRelative("icon").objectReferenceValue = crates[i].icon;
        }

        so.FindProperty("freeDiamonds").intValue = 5;
        so.FindProperty("freeCooldownHours").floatValue = 4f;
        so.FindProperty("freeDailyCap").intValue = 3;
        so.FindProperty("unlockWithFirstDiamond").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Log.AppendLine($"assets: {bundles.Length} bundles, starter, factory pack, no-ads (off), catalog with {boostItems.Length} boosts and {crates.Length} crates");
        return Log.ToString();
    }

    static IAPProductConfig Product(string asset, string id, StoreProductType type, ShopTier tier, string title, Sprite icon, int diamonds, int bonus,
        int cashAsDiamonds, BoostDefinition boost, float boostMinutes, BoostDefinition second, float secondMinutes, bool removesAds, string badge, string price, float usd)
    {
        var p = LoadOrCreate<IAPProductConfig>($"{ShopDir}/{asset}.asset");
        var so = new SerializedObject(p);
        so.FindProperty("productId").stringValue = id;
        so.FindProperty("type").enumValueIndex = (int)type;
        so.FindProperty("tier").enumValueIndex = (int)tier;
        so.FindProperty("title").stringValue = title;
        so.FindProperty("icon").objectReferenceValue = icon;
        so.FindProperty("enabled").boolValue = true;
        so.FindProperty("diamonds").intValue = diamonds;
        so.FindProperty("bonusDiamonds").intValue = bonus;
        so.FindProperty("cashAsDiamonds").intValue = cashAsDiamonds;
        so.FindProperty("boost").objectReferenceValue = boost;
        so.FindProperty("boostMinutes").floatValue = boostMinutes;
        so.FindProperty("secondBoost").objectReferenceValue = second;
        so.FindProperty("secondBoostMinutes").floatValue = secondMinutes;
        so.FindProperty("removesAds").boolValue = removesAds;
        so.FindProperty("badge").stringValue = badge;
        so.FindProperty("fallbackPrice").stringValue = price;
        so.FindProperty("referencePriceUsd").floatValue = usd;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(p);
        return p;
    }

    static void Array(SerializedProperty p, Object[] values)
    {
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    static void SetBool(Object target, string name, bool value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(name).boolValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a != null) return a;
        a = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(a, path);
        return a;
    }

    // ================================================================== scene

    public static string Scene()
    {
        Log.Clear();
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "/Art/Fonts/GROBOLD SDF.asset");
        outline = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Art/Fonts/GROBOLD Outline.mat");

        var systems = scene.GetRootGameObjects().First(g => g.name == "_Systems").transform;
        var storeT = systems.Find("StoreService");
        if (storeT == null)
        {
            storeT = new GameObject("StoreService").transform;
            storeT.SetParent(systems, false);
        }

        var store = storeT.TryGetComponent(out StoreService s) ? s : storeT.gameObject.AddComponent<StoreService>();
        SetRefs(store, ("catalog", AssetDatabase.LoadAssetAtPath<ShopCatalog>(ShopDir + "/ShopCatalog.asset")),
            ("premium", AssetDatabase.LoadAssetAtPath<PremiumEconomyConfig>(Root + "/Data/Economy/PremiumEconomy.asset")),
            ("diamondIcon", A("Icon_Gem")), ("cashIcon", A("Icon_Cash")));

        var canvas = scene.GetRootGameObjects().First(g => g.name == "_UI").transform.Find("Canvas");
        var hud = canvas.Find("HUD");
        // the diamond capsule needs room for "1,250" beside its + button
        var gemPill = (RectTransform)hud.Find("GemPill");
        gemPill.sizeDelta = new Vector2(262f, 92f);
        var cashPlus = Plus((RectTransform)hud.Find("CashPill"));
        var gemPlus = Plus((RectTransform)hud.Find("GemPill"));
        Layer(hud.Find("CashPill"), 10, true);
        Layer(hud.Find("GemPill"), 10, true);
        Layer(hud.Find("FlyLayer"), 11, false);
        Layer(canvas.Find("PopupLayer"), 20, true);

        var old = canvas.Find("ShopPanel");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var shop = ShopPanel(canvas, cashPlus, gemPlus);
        var settings = canvas.Find("SettingsPanel");
        shop.transform.SetSiblingIndex(settings != null ? settings.GetSiblingIndex() : canvas.childCount - 1);

        int feel = 0;
        var click = AssetDatabase.LoadAssetAtPath<SfxDefinition>(Root + "/Data/Audio/Sfx_UIClick.asset");
        var denied = AssetDatabase.LoadAssetAtPath<SfxDefinition>(Root + "/Data/Audio/Sfx_Denied.asset");
        foreach (var b in canvas.GetComponentsInChildren<Button>(true))
        {
            if (!b.TryGetComponent<UIButtonFeel>(out var f)) f = b.gameObject.AddComponent<UIButtonFeel>();
            SetRefs(f, ("clickSfx", click), ("deniedSfx", denied));
            feel++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine($"scene: StoreService, + buttons, sorting layers, shop panel, button feel on {feel} buttons");
        return Log.ToString();
    }

    /// <summary>A nested canvas that draws above the main canvas' panels (and takes taps if it has buttons).</summary>
    static void Layer(Transform t, int order, bool raycast)
    {
        if (t == null) return;
        var c = t.TryGetComponent(out Canvas existing) ? existing : t.gameObject.AddComponent<Canvas>();
        c.overrideSorting = true;
        c.sortingOrder = order;
        if (raycast && !t.TryGetComponent<GraphicRaycaster>(out _)) t.gameObject.AddComponent<GraphicRaycaster>();
    }

    static Button Plus(RectTransform pill)
    {
        var plus = (RectTransform)pill.Find("Plus");
        plus.anchorMin = plus.anchorMax = new Vector2(1f, 0.5f);
        plus.pivot = new Vector2(0.5f, 0.5f);
        plus.anchoredPosition = new Vector2(-40f, 0f);
        plus.sizeDelta = new Vector2(62f, 62f);
        var img = plus.GetComponent<Image>();
        img.sprite = P("BtnSmall_Green");
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1.2f;
        img.color = Color.white;
        img.raycastTarget = true;
        var glyph = plus.Find("Glyph");
        if (glyph == null)
        {
            var t = Text("Glyph", plus, new Vector2(0f, 3f), new Vector2(60f, 60f), 50f, Color.white, TextAlignmentOptions.Center, "+", true);
            glyph = t.transform;
        }

        var b = plus.TryGetComponent(out Button existing) ? existing : plus.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None;
        var label = (RectTransform)pill.Find("Label");
        label.offsetMin = new Vector2(96f, 4f);
        label.offsetMax = new Vector2(-72f, -4f);
        plus.gameObject.SetActive(false);   // ShopPanel shows them with the first diamond
        return b;
    }

    // ------------------------------------------------------------------ shop panel

    static ShopPanel ShopPanel(Transform canvas, Button cashEntry, Button gemEntry)
    {
        var holder = Stretch(Node("ShopPanel", canvas));
        var root = Stretch(Node("Root", holder));
        var dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0.03f, 0.05f, 0.1f, 0f);
        dim.raycastTarget = true;

        var card = Node("Card", root);
        card.anchoredPosition = new Vector2(0f, -110f);
        card.sizeDelta = new Vector2(990f, 1560f);
        var group = card.gameObject.AddComponent<CanvasGroup>();
        Stretch(Img("Back", card, Vector2.zero, card.sizeDelta, P("Panel_Dark"), Image.Type.Sliced, 0.55f).rectTransform);
        var plate = Img("TitlePlate", card, new Vector2(0f, 760f), new Vector2(560f, 124f), P("Title_Yellow"), Image.Type.Sliced, 93f / 124f);
        Text("Title", plate.transform, new Vector2(0f, 4f), new Vector2(420f, 76f), 56f, Color.white, TextAlignmentOptions.Center, "SHOP", true);
        var close = Node("Close", card);
        close.anchoredPosition = new Vector2(440f, 730f);
        close.sizeDelta = new Vector2(104f, 104f);
        var closeImg = close.gameObject.AddComponent<Image>();
        closeImg.sprite = B("Btn_Close");
        closeImg.preserveAspect = true;
        var closeButton = close.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeImg;
        closeButton.transition = Selectable.Transition.None;

        // tabs
        string[] names = { "DIAMONDS", "BOOSTS", "SPECIAL", "FREE" };
        var tabs = new Button[4];
        var backs = new Image[4];
        for (int i = 0; i < 4; i++)
        {
            var t = Node("Tab_" + names[i], card);
            t.anchoredPosition = new Vector2(-354f + i * 236f, 600f);
            t.sizeDelta = new Vector2(222f, 96f);
            backs[i] = t.gameObject.AddComponent<Image>();
            backs[i].sprite = P(i == 0 ? "BtnSmall_Yellow" : "BtnSmall_Grey");
            backs[i].type = Image.Type.Sliced;
            backs[i].pixelsPerUnitMultiplier = 1f;
            tabs[i] = t.gameObject.AddComponent<Button>();
            tabs[i].targetGraphic = backs[i];
            tabs[i].transition = Selectable.Transition.None;
            var label = Text("Label", t, Vector2.zero, Vector2.zero, 32f, Color.white, TextAlignmentOptions.Center, names[i], true);
            Stretch(label.rectTransform, 12f, 8f, 12f, 6f);
        }

        // scroll with one page per tab
        var scrollRt = Node("Scroll", card);
        Stretch(scrollRt, 36f, 40f, 36f, 250f);
        var scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.scrollSensitivity = 30f;
        var viewport = Stretch(Node("Viewport", scrollRt));
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        scroll.viewport = viewport;
        var pages = new RectTransform[4];
        for (int i = 0; i < 4; i++)
        {
            var page = Node("Page_" + names[i], viewport);
            page.anchorMin = new Vector2(0f, 1f);
            page.anchorMax = new Vector2(1f, 1f);
            page.pivot = new Vector2(0.5f, 1f);
            page.anchoredPosition = Vector2.zero;
            page.sizeDelta = Vector2.zero;
            var v = page.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(4, 4, 30, 40);
            v.spacing = 26f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlHeight = true;
            v.childControlWidth = false;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = false;
            page.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            pages[i] = page;
            page.gameObject.SetActive(i == 0);
        }

        scroll.content = pages[0];

        var templates = Node("Templates", root);
        var grid = Node("BundleGrid", templates);
        grid.sizeDelta = new Vector2(900f, 0f);
        var g = grid.gameObject.AddComponent<GridLayoutGroup>();
        g.cellSize = new Vector2(430f, 480f);
        g.spacing = new Vector2(30f, 30f);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = 2;
        g.childAlignment = TextAnchor.UpperCenter;
        var hero = Hero(templates);
        var bundle = Bundle(templates);
        var row = Row(templates);

        var panel = holder.gameObject.AddComponent<ShopPanel>();
        SetRefs(panel, ("root", root.gameObject), ("dim", dim), ("card", card), ("cardGroup", group), ("close", closeButton), ("scroll", scroll),
            ("tabOn", P("BtnSmall_Yellow")), ("tabOff", P("BtnSmall_Grey")), ("bundleGridTemplate", grid),
            ("heroTemplate", hero), ("bundleTemplate", bundle), ("rowTemplate", row), ("cashEntry", cashEntry), ("diamondEntry", gemEntry),
            ("cashIcon", A("Icon_Cash")), ("diamondIcon", A("Icon_Gem")),
            ("openSfx", Sfx("Sfx_UIOpen")), ("closeSfx", Sfx("Sfx_UIClose")), ("buySfx", Sfx("Sfx_Diamond")));
        var so = new SerializedObject(panel);
        Arr(so.FindProperty("tabs"), tabs);
        Arr(so.FindProperty("tabBacks"), backs);
        Arr(so.FindProperty("pages"), pages);
        so.ApplyModifiedPropertiesWithoutUndo();
        templates.gameObject.SetActive(true);
        root.gameObject.SetActive(false);
        return panel;
    }

    static void Arr(SerializedProperty p, Object[] values)
    {
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    /// <summary>Big offer: cream hazard plate, picture on the left, what is inside on the right, ribbon and price.</summary>
    static ShopCard Hero(Transform parent)
    {
        var c = Node("HeroCard", parent);
        c.sizeDelta = new Vector2(910f, 440f);
        c.gameObject.AddComponent<LayoutElement>().preferredHeight = 440f;
        Stretch(Img("Back", c, Vector2.zero, c.sizeDelta, P("Panel_CreamHazard"), Image.Type.Sliced, 0.75f).rectTransform);
        var rays = Img("Rays", c, new Vector2(-285f, -15f), new Vector2(380f, 380f), Single("Fx_Rays"), Image.Type.Simple, 1f);
        rays.color = new Color(1f, 0.75f, 0.2f, 0.4f);
        var icon = Img("Icon", c, new Vector2(-285f, -15f), new Vector2(230f, 230f), null, Image.Type.Simple, 1f);
        icon.preserveAspect = true;
        var title = Text("Title", c, new Vector2(150f, 100f), new Vector2(560f, 60f), 42f, Navy, TextAlignmentOptions.Left, "INDUSTRIAL STARTER PACK", false);
        var sub = Text("Subtitle", c, new Vector2(150f, -12f), new Vector2(560f, 124f), 32f, new Color(0.2f, 0.3f, 0.5f), TextAlignmentOptions.TopLeft, "500 DIAMONDS\n$9K CASH\n2X FACTORY · 30 MIN", false);
        sub.textWrappingMode = TextWrappingModes.Normal;
        var detail = Text("Detail", c, new Vector2(-285f, -158f), new Vector2(300f, 40f), 26f, new Color(0.75f, 0.2f, 0.15f), TextAlignmentOptions.Center, "ONE TIME OFFER", false);
        var (badge, badgeText) = Ribbon(c, new Vector2(300f, 210f), new Vector2(280f, 78f), P("Title_Red"));
        var (button, back, label, bIcon) = PriceButton(c, new Vector2(260f, -150f), new Vector2(300f, 110f));
        var card = c.gameObject.AddComponent<ShopCard>();
        Wire(card, icon, title, sub, detail, badge, badgeText, null, null, null, button, back, label, bIcon);
        c.gameObject.SetActive(false);
        return card;
    }

    /// <summary>Diamond bundle: blue header with the count, the pile, bonus pill and total, price button.</summary>
    static ShopCard Bundle(Transform parent)
    {
        var c = Node("BundleCard", parent);
        c.sizeDelta = new Vector2(430f, 480f);
        Stretch(Img("Back", c, Vector2.zero, c.sizeDelta, P("Card_BlueCream"), Image.Type.Sliced, 0.8f).rectTransform);
        var title = Text("Title", c, new Vector2(0f, 196f), new Vector2(380f, 64f), 50f, Color.white, TextAlignmentOptions.Center, "1,200", true);
        var rays = Img("Rays", c, new Vector2(0f, 60f), new Vector2(260f, 260f), Single("Fx_Rays"), Image.Type.Simple, 1f);
        rays.color = new Color(0.6f, 0.8f, 1f, 0.35f);
        var icon = Img("Icon", c, new Vector2(0f, 64f), new Vector2(170f, 170f), null, Image.Type.Simple, 1f);
        icon.preserveAspect = true;
        var sub = Text("Subtitle", c, new Vector2(0f, -40f), new Vector2(380f, 40f), 28f, Navy, TextAlignmentOptions.Center, "DIAMONDS", false);
        var bonus = Img("Bonus", c, new Vector2(0f, -84f), new Vector2(270f, 50f), P("BtnSmall_Green"), Image.Type.Sliced, 1.4f);
        var bonusText = Text("Text", bonus.transform, Vector2.zero, Vector2.zero, 28f, Color.white, TextAlignmentOptions.Center, "+300 BONUS", true);
        Stretch(bonusText.rectTransform, 10f, 4f, 10f, 4f);
        var total = Text("Total", c, new Vector2(0f, -126f), new Vector2(380f, 36f), 26f, Navy, TextAlignmentOptions.Center, "1,500 TOTAL", false);
        var (badge, badgeText) = Ribbon(c, new Vector2(120f, 236f), new Vector2(210f, 58f), P("BtnSmall_Red"));
        var (button, back, label, bIcon) = PriceButton(c, new Vector2(0f, -190f), new Vector2(300f, 92f));
        var card = c.gameObject.AddComponent<ShopCard>();
        Wire(card, icon, title, sub, null, badge, badgeText, bonus.gameObject, bonusText, total, button, back, label, bIcon);
        c.gameObject.SetActive(false);
        return card;
    }

    /// <summary>Row: picture, name and one line on cream, button on the right.</summary>
    static ShopCard Row(Transform parent)
    {
        var c = Node("RowCard", parent);
        c.sizeDelta = new Vector2(910f, 180f);
        c.gameObject.AddComponent<LayoutElement>().preferredHeight = 180f;
        Stretch(Img("Back", c, Vector2.zero, c.sizeDelta, P("Sq_Cream"), Image.Type.Sliced, 0.8f).rectTransform);
        var icon = Img("Icon", c, new Vector2(-370f, 0f), new Vector2(130f, 130f), null, Image.Type.Simple, 1f);
        icon.preserveAspect = true;
        var title = Text("Title", c, new Vector2(-70f, 28f), new Vector2(440f, 56f), 40f, Navy, TextAlignmentOptions.Left, "2X CASH", false);
        var sub = Text("Subtitle", c, new Vector2(-70f, -30f), new Vector2(440f, 70f), 28f, new Color(0.2f, 0.3f, 0.5f), TextAlignmentOptions.Left, "10 MINUTES", false);
        sub.textWrappingMode = TextWrappingModes.Normal;
        var (badge, badgeText) = Ribbon(c, new Vector2(-300f, 86f), new Vector2(180f, 50f), P("BtnSmall_Red"));
        var (button, back, label, bIcon) = PriceButton(c, new Vector2(300f, 0f), new Vector2(250f, 104f));
        var card = c.gameObject.AddComponent<ShopCard>();
        Wire(card, icon, title, sub, null, badge, badgeText, null, null, null, button, back, label, bIcon);
        c.gameObject.SetActive(false);
        return card;
    }

    static (GameObject, TMP_Text) Ribbon(Transform parent, Vector2 pos, Vector2 size, Sprite sprite)
    {
        var r = Img("Badge", parent, pos, size, sprite, Image.Type.Sliced, 1.2f);
        var t = Text("Text", r.transform, Vector2.zero, Vector2.zero, 30f, Color.white, TextAlignmentOptions.Center, "BEST VALUE", true);
        Stretch(t.rectTransform, 16f, 6f, 16f, 6f);
        r.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -4f);
        return (r.gameObject, t);
    }

    static (Button, Image, TMP_Text, Image) PriceButton(Transform parent, Vector2 pos, Vector2 size)
    {
        var rt = Node("Buy", parent);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var back = rt.gameObject.AddComponent<Image>();
        back.sprite = P("Btn_Green");
        back.type = Image.Type.Sliced;
        back.pixelsPerUnitMultiplier = 1f;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = back;
        b.transition = Selectable.Transition.None;
        var icon = Img("Icon", rt, new Vector2(46f, 2f), new Vector2(60f, 60f), A("Icon_Gem"), Image.Type.Simple, 1f);
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        icon.preserveAspect = true;
        var label = Text("Label", rt, Vector2.zero, Vector2.zero, 42f, Color.white, TextAlignmentOptions.Center, "$0.99", true);
        Stretch(label.rectTransform, 14f, 10f, 14f, 8f);
        return (b, back, label, icon);
    }

    static void Wire(ShopCard card, Image icon, TMP_Text title, TMP_Text sub, TMP_Text detail, GameObject badge, TMP_Text badgeText,
        GameObject bonus, TMP_Text bonusText, TMP_Text total, Button button, Image back, TMP_Text label, Image bIcon)
    {
        SetRefs(card, ("icon", icon), ("title", title), ("subtitle", sub), ("detail", detail), ("badge", badge), ("badgeText", badgeText),
            ("bonus", bonus), ("bonusText", bonusText), ("total", total), ("button", button), ("buttonBack", back), ("buttonLabel", label),
            ("buttonIcon", bIcon), ("green", P("Btn_Green")), ("blue", P("Btn_Blue")), ("grey", P("Btn_Grey")), ("yellow", P("Btn_Yellow")),
            ("gem", A("Icon_Gem")), ("video", B("Btn_Play")));
    }

    // ------------------------------------------------------------------ helpers

    static RectTransform Node(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        return rt;
    }

    static RectTransform Stretch(RectTransform rt, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
        return rt;
    }

    static Image Img(string name, Transform parent, Vector2 pos, Vector2 size, Sprite sprite, Image.Type type, float ppu)
    {
        var rt = Node(name, parent);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.type = type;
        img.pixelsPerUnitMultiplier = ppu;
        img.raycastTarget = false;
        return img;
    }

    static TMP_Text Text(string name, Transform parent, Vector2 pos, Vector2 size, float fontSize, Color color, TextAlignmentOptions align, string sample, bool outlined)
    {
        var rt = Node(name, parent);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font;
        t.fontSharedMaterial = outlined && outline != null ? outline : font.material;
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.enableAutoSizing = true;
        t.fontSizeMin = 14f;
        t.fontSizeMax = fontSize;
        t.text = sample;
        return t;
    }

    static void SetRefs(Object target, params (string name, Object value)[] refs)
    {
        var so = new SerializedObject(target);
        foreach (var (name, value) in refs)
        {
            var p = so.FindProperty(name);
            if (p == null) { Log.AppendLine($"!! {target.GetType().Name}.{name} not found"); continue; }
            p.objectReferenceValue = value;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static SfxDefinition Sfx(string name) => AssetDatabase.LoadAssetAtPath<SfxDefinition>($"{Root}/Data/Audio/{name}.asset");

    static Sprite KitSprite(string sheet, string name)
    {
        var s = AssetDatabase.LoadAllAssetsAtPath($"{KitDir}/{sheet}.png").OfType<Sprite>().FirstOrDefault(x => x.name == name);
        if (s == null) Log.AppendLine($"!! no kit sprite {sheet}/{name}");
        return s;
    }

    static Sprite P(string name) => KitSprite("Kit_Panels", name);
    static Sprite A(string name) => KitSprite("Kit_IconsA", name);
    static Sprite B(string name) => KitSprite("Kit_IconsB", name);
    static Sprite Single(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{KitDir}/{name}.png");
}

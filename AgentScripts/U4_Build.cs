using System.Linq;
using System.Text;
using ScrapYardKing.Boosts;
using ScrapYardKing.Feedback;
using ScrapYardKing.Progression;
using ScrapYardKing.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// UI step U4: missions, daily rewards, badges and the bottom bar.
///  - Assets: <c>Data/Progression/MissionConfig</c> (pool of eight daily missions, seven achievements with tiers) and
///    <c>DailyRewardConfig</c> (7-day calendar).
///  - Scene: <c>MissionManager</c>, <c>DailyRewardManager</c> in _Systems; the bottom bar (SHOP, MISSIONS, DAILY with
///    badges); the missions panel (MAIN, DAILY, ACHIEVEMENTS) and the daily rewards panel.
/// Entry points: Assets, Scene (or All). Idempotent: panels and the bar are rebuilt on each run. Run after U3_Build.
/// </summary>
public static class U4_Build
{
    const string Root = "Assets/_Project";
    const string KitDir = Root + "/Art/UI/Kit";
    const string DataDir = Root + "/Data/Progression";
    const string ScenePath = Root + "/Scenes/Area1_OldScrapYard.unity";

    static readonly StringBuilder Log = new();
    static TMP_FontAsset font;
    static Material outline;
    static readonly Color Navy = new(0.12f, 0.17f, 0.3f);
    static readonly Color Cream = new(1f, 0.93f, 0.76f);

    public static string All() => Assets() + Scene();

    // ================================================================== assets

    public static string Assets()
    {
        Log.Clear();
        var cashBoost = AssetDatabase.LoadAssetAtPath<BoostDefinition>(Root + "/Data/Boosts/Boost_Cash.asset");
        var factoryBoost = AssetDatabase.LoadAssetAtPath<BoostDefinition>(Root + "/Data/Boosts/Boost_Production.asset");

        var missions = LoadOrCreate<MissionConfig>(DataDir + "/MissionConfig.asset");
        var so = new SerializedObject(missions);
        so.FindProperty("dailyCount").intValue = 3;
        // (id, title, stat, base, per level, min level, icon, cash/level, diamonds). About 9 free diamonds a day from dailies.
        var pool = new (string id, string title, MissionStat stat, int b, int per, int min, Sprite icon, long cash, int gems)[]
        {
            ("d_break", "BREAK {0} SCRAP", MissionStat.ScrapBroken, 15, 3, 1, B("Icon_Saw"), 120, 0),
            ("d_collect", "PICK UP {0} PIECES", MissionStat.ItemsCollected, 120, 25, 1, B("Icon_Backpack"), 150, 0),
            ("d_process", "MACHINES PROCESS {0}", MissionStat.ItemsProcessed, 150, 40, 1, B("B_r0_c9"), 0, 3),
            ("d_sell", "SELL {0} GOODS", MissionStat.ItemsSold, 60, 20, 1, B("B_r4_c2"), 200, 0),
            ("d_cash", "EARN ${0}", MissionStat.CashEarned, 3000, 1500, 1, A("Icon_Cash"), 0, 3),
            ("d_serve", "SERVE {0} CUSTOMERS", MissionStat.CustomersServed, 20, 3, 1, B("B_r1_c11"), 150, 0),
            ("d_boost", "USE A BOOST PAD {0} TIME{s}", MissionStat.Overdrives, 3, 0, 3, A("Icon_Bolt"), 0, 3),
            ("d_orders", "COMPLETE {0} TRUCK ORDER{s}", MissionStat.OrdersCompleted, 3, 0, 10, A("A_r1_c6"), 0, 5),
        };
        var p = so.FindProperty("dailyPool");
        p.arraySize = pool.Length;
        for (int i = 0; i < pool.Length; i++)
        {
            var e = p.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("id").stringValue = pool[i].id;
            e.FindPropertyRelative("title").stringValue = pool[i].title;
            e.FindPropertyRelative("icon").objectReferenceValue = pool[i].icon;
            e.FindPropertyRelative("stat").enumValueIndex = (int)pool[i].stat;
            e.FindPropertyRelative("baseTarget").intValue = pool[i].b;
            e.FindPropertyRelative("targetPerLevel").intValue = pool[i].per;
            e.FindPropertyRelative("minLevel").intValue = pool[i].min;
            Reward(e.FindPropertyRelative("reward"), pool[i].cash, pool[i].gems, null, 0f);
        }

        var achievements = new (string id, string title, MissionStat stat, Sprite icon, long[] targets, int[] gems)[]
        {
            ("a_scrap", "BREAK {0} SCRAP", MissionStat.ScrapBroken, B("Icon_Saw"), new long[] { 100, 500, 2000, 8000, 25000 }, new[] { 5, 10, 15, 25, 40 }),
            ("a_cash", "EARN ${0} IN TOTAL", MissionStat.CashEarned, A("Icon_CashUp"), new long[] { 50000, 1000000, 10000000, 100000000 }, new[] { 5, 10, 20, 40 }),
            ("a_sold", "SELL {0} GOODS", MissionStat.ItemsSold, B("B_r4_c2"), new long[] { 500, 5000, 25000, 100000 }, new[] { 5, 10, 20, 30 }),
            ("a_workers", "HIRE {0} WORKER{s}", MissionStat.WorkersHired, B("B_r1_c11"), new long[] { 3, 8, 14, 20 }, new[] { 5, 10, 15, 25 }),
            ("a_areas", "OPEN {0} AREA{s}", MissionStat.AreasOpened, B("B_r1_c8"), new long[] { 3, 6, 10 }, new[] { 5, 15, 30 }),
            ("a_orders", "COMPLETE {0} TRUCK ORDER{s}", MissionStat.OrdersCompleted, A("A_r1_c6"), new long[] { 5, 25, 100, 500 }, new[] { 10, 15, 25, 40 }),
            ("a_upgrades", "BUY {0} UPGRADES", MissionStat.UpgradesBought, B("Icon_Upgrade"), new long[] { 25, 100, 300 }, new[] { 5, 15, 30 }),
        };
        var a = so.FindProperty("achievements");
        a.arraySize = achievements.Length;
        for (int i = 0; i < achievements.Length; i++)
        {
            var e = a.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("id").stringValue = achievements[i].id;
            e.FindPropertyRelative("title").stringValue = achievements[i].title;
            e.FindPropertyRelative("icon").objectReferenceValue = achievements[i].icon;
            e.FindPropertyRelative("stat").enumValueIndex = (int)achievements[i].stat;
            var t = e.FindPropertyRelative("targets");
            t.arraySize = achievements[i].targets.Length;
            for (int k = 0; k < t.arraySize; k++) t.GetArrayElementAtIndex(k).longValue = achievements[i].targets[k];
            var g = e.FindPropertyRelative("diamonds");
            g.arraySize = achievements[i].gems.Length;
            for (int k = 0; k < g.arraySize; k++) g.GetArrayElementAtIndex(k).intValue = achievements[i].gems[k];
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(missions);

        // 7-day calendar: cash, diamonds, a boost, more cash, diamonds, a longer boost, then the big one. 75 diamonds a week.
        var daily = LoadOrCreate<DailyRewardConfig>(DataDir + "/DailyRewardConfig.asset");
        var dso = new SerializedObject(daily);
        var days = new (string title, Sprite icon, long cash, int gems, BoostDefinition boost, float minutes)[]
        {
            ("DAY 1", A("Icon_Cash"), 200, 0, null, 0f), ("DAY 2", A("Icon_Gem"), 0, 10, null, 0f), ("DAY 3", A("Icon_CashUp"), 0, 0, cashBoost, 10f),
            ("DAY 4", A("A_r6_c6"), 400, 0, null, 0f), ("DAY 5", B("B_r4_c7"), 0, 15, null, 0f), ("DAY 6", A("Icon_Bolt"), 0, 0, factoryBoost, 15f),
            ("DAY 7", B("B_r3_c1"), 600, 50, null, 0f),
        };
        var dp = dso.FindProperty("days");
        dp.arraySize = days.Length;
        for (int i = 0; i < days.Length; i++)
        {
            var e = dp.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("title").stringValue = days[i].title;
            e.FindPropertyRelative("icon").objectReferenceValue = days[i].icon;
            Reward(e.FindPropertyRelative("reward"), days[i].cash, days[i].gems, days[i].boost, days[i].minutes);
        }

        dso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(daily);
        AssetDatabase.SaveAssets();
        Log.AppendLine($"assets: {pool.Length} daily missions, {achievements.Length} achievements, 7 daily rewards");
        return Log.ToString();
    }

    static void Reward(SerializedProperty r, long cashPerLevel, int diamonds, BoostDefinition boost, float minutes)
    {
        r.FindPropertyRelative("cashPerLevel").longValue = cashPerLevel;
        r.FindPropertyRelative("diamonds").intValue = diamonds;
        r.FindPropertyRelative("boost").objectReferenceValue = boost;
        r.FindPropertyRelative("boostMinutes").floatValue = minutes;
    }

    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var x = AssetDatabase.LoadAssetAtPath<T>(path);
        if (x != null) return x;
        x = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(x, path);
        return x;
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
        SetRefs(Service<MissionManager>(systems, "MissionManager"), ("config", AssetDatabase.LoadAssetAtPath<MissionConfig>(DataDir + "/MissionConfig.asset")));
        SetRefs(Service<DailyRewardManager>(systems, "DailyRewardManager"), ("config", AssetDatabase.LoadAssetAtPath<DailyRewardConfig>(DataDir + "/DailyRewardConfig.asset")));

        var canvas = scene.GetRootGameObjects().First(g => g.name == "_UI").transform.Find("Canvas");
        var hud = canvas.Find("HUD");
        foreach (string n in new[] { "MissionsPanel", "DailyPanel" }) Remove(canvas, n);
        Remove(hud, "NavBar");

        var missions = MissionsPanel(canvas);
        var daily = DailyPanel(canvas);
        var shop = canvas.Find("ShopPanel").GetComponent<ShopPanel>();
        NavBar(hud, shop, missions, daily);
        int at = shop.transform.GetSiblingIndex() + 1;
        missions.transform.SetSiblingIndex(at);
        daily.transform.SetSiblingIndex(at + 1);

        var click = AssetDatabase.LoadAssetAtPath<SfxDefinition>(Root + "/Data/Audio/Sfx_UIClick.asset");
        var denied = AssetDatabase.LoadAssetAtPath<SfxDefinition>(Root + "/Data/Audio/Sfx_Denied.asset");
        foreach (var b in canvas.GetComponentsInChildren<Button>(true))
        {
            if (b.TryGetComponent<UIButtonFeel>(out _)) continue;
            SetRefs(b.gameObject.AddComponent<UIButtonFeel>(), ("clickSfx", click), ("deniedSfx", denied));
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("scene: mission + daily services, bottom bar, missions panel, daily panel");
        return Log.ToString();
    }

    static T Service<T>(Transform systems, string name) where T : Component
    {
        var t = systems.Find(name);
        if (t == null)
        {
            t = new GameObject(name).transform;
            t.SetParent(systems, false);
        }

        return t.TryGetComponent(out T c) ? c : t.gameObject.AddComponent<T>();
    }

    static void Remove(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) Object.DestroyImmediate(t.gameObject);
    }

    // ------------------------------------------------------------------ bottom bar

    static void NavBar(Transform hud, ShopPanel shop, MissionsPanel missions, DailyPanel daily)
    {
        var bar = Node("NavBar", hud);
        bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, 110f);
        bar.sizeDelta = new Vector2(540f, 170f);
        var items = new (string label, Sprite icon, string badge)[] { ("SHOP", B("B_r4_c2"), "shop"), ("MISSIONS", B("B_r2_c4"), "missions"), ("DAILY", B("B_r4_c5"), "daily") };
        var roots = new RectTransform[3];
        var buttons = new Button[3];
        for (int i = 0; i < 3; i++)
        {
            var r = Node("Btn_" + items[i].label, bar);
            r.anchoredPosition = new Vector2(-176f + i * 176f, 0f);
            r.sizeDelta = new Vector2(150f, 150f);
            var back = r.gameObject.AddComponent<Image>();
            back.sprite = P("Sq_DarkYellow");
            back.type = Image.Type.Sliced;
            back.pixelsPerUnitMultiplier = 1.1f;
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = back;
            b.transition = Selectable.Transition.None;
            var icon = Img("Icon", r, new Vector2(0f, 14f), new Vector2(100f, 100f), items[i].icon, Image.Type.Simple, 1f);
            icon.preserveAspect = true;
            Text("Label", r, new Vector2(0f, -50f), new Vector2(150f, 40f), 28f, Color.white, TextAlignmentOptions.Center, items[i].label, true);
            var dot = Img("Badge", r, new Vector2(58f, 58f), new Vector2(50f, 56f), P("Dot_Red"), Image.Type.Simple, 1f);
            var count = Text("Count", dot.transform, new Vector2(0f, 2f), new Vector2(46f, 46f), 30f, Color.white, TextAlignmentOptions.Center, "1", true);
            dot.gameObject.SetActive(false);
            var badge = r.gameObject.AddComponent<BadgeDot>();
            var so = new SerializedObject(badge);
            so.FindProperty("key").stringValue = items[i].badge;
            so.FindProperty("dot").objectReferenceValue = dot.rectTransform;
            so.FindProperty("count").objectReferenceValue = count;
            so.ApplyModifiedPropertiesWithoutUndo();
            roots[i] = r;
            buttons[i] = b;
        }

        var nav = bar.gameObject.AddComponent<NavBar>();
        SetRefs(nav, ("shop", buttons[0]), ("missions", buttons[1]), ("daily", buttons[2]), ("shopPanel", shop), ("missionsPanel", missions), ("dailyPanel", daily));
        var nso = new SerializedObject(nav);
        var arr = nso.FindProperty("buttons");
        arr.arraySize = 3;
        for (int i = 0; i < 3; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = roots[i];
        nso.ApplyModifiedPropertiesWithoutUndo();
        foreach (var r in roots) r.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ panels

    static (RectTransform holder, GameObject root, Image dim, RectTransform card, CanvasGroup group, Button close) Frame(Transform canvas, string name, string title, Sprite plateSprite, Vector2 size, float y)
    {
        var holder = Stretch(Node(name, canvas));
        var root = Stretch(Node("Root", holder));
        var dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0.03f, 0.05f, 0.1f, 0f);
        var card = Node("Card", root);
        card.anchoredPosition = new Vector2(0f, y);
        card.sizeDelta = size;
        var group = card.gameObject.AddComponent<CanvasGroup>();
        Stretch(Img("Back", card, Vector2.zero, size, P("Panel_Dark"), Image.Type.Sliced, 0.55f).rectTransform);
        var plate = Img("TitlePlate", card, new Vector2(0f, size.y * 0.5f - 20f), new Vector2(620f, 124f), plateSprite, Image.Type.Sliced, 93f / 124f);
        Text("Title", plate.transform, new Vector2(0f, 4f), new Vector2(500f, 76f), 52f, Color.white, TextAlignmentOptions.Center, title, true);
        var c = Node("Close", card);
        c.anchoredPosition = new Vector2(size.x * 0.5f - 55f, size.y * 0.5f - 50f);
        c.sizeDelta = new Vector2(104f, 104f);
        var ci = c.gameObject.AddComponent<Image>();
        ci.sprite = B("Btn_Close");
        ci.preserveAspect = true;
        var close = c.gameObject.AddComponent<Button>();
        close.targetGraphic = ci;
        close.transition = Selectable.Transition.None;
        return (holder, root.gameObject, dim, card, group, close);
    }

    static MissionsPanel MissionsPanel(Transform canvas)
    {
        var f = Frame(canvas, "MissionsPanel", "MISSIONS", P("Title_Blue"), new Vector2(990f, 1560f), -110f);
        string[] names = { "MAIN", "DAILY", "ACHIEVEMENTS" };
        var tabs = new Button[3];
        var backs = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            var t = Node("Tab_" + names[i], f.card);
            t.anchoredPosition = new Vector2(-300f + i * 300f, 600f);
            t.sizeDelta = new Vector2(286f, 96f);
            backs[i] = t.gameObject.AddComponent<Image>();
            backs[i].sprite = P("BtnSmall_Grey");
            backs[i].type = Image.Type.Sliced;
            tabs[i] = t.gameObject.AddComponent<Button>();
            tabs[i].targetGraphic = backs[i];
            tabs[i].transition = Selectable.Transition.None;
            Stretch(Text("Label", t, Vector2.zero, Vector2.zero, 32f, Color.white, TextAlignmentOptions.Center, names[i], true).rectTransform, 12f, 8f, 12f, 6f);
        }

        var (scroll, pages) = Pages(f.card, names);

        var templates = Node("Templates", f.root.transform);
        var row = Node("MissionRow", templates);
        row.sizeDelta = new Vector2(910f, 190f);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 190f;
        Stretch(Img("Back", row, Vector2.zero, row.sizeDelta, P("Sq_Cream"), Image.Type.Sliced, 0.8f).rectTransform);
        var icon = Img("Icon", row, new Vector2(-375f, 0f), new Vector2(120f, 120f), null, Image.Type.Simple, 1f);
        icon.preserveAspect = true;
        var title = Text("Title", row, new Vector2(-40f, 50f), new Vector2(520f, 52f), 36f, Navy, TextAlignmentOptions.Left, "BREAK 30 SCRAP", false);
        var sub = Text("Sub", row, new Vector2(-40f, 12f), new Vector2(520f, 30f), 24f, new Color(0.35f, 0.42f, 0.6f), TextAlignmentOptions.Left, "DAILY", false);
        var track = Img("Track", row, new Vector2(-40f, -42f), new Vector2(520f, 46f), Single("Kit_BarTrack"), Image.Type.Sliced, 1f);
        var fill = Img("Fill", track.transform, Vector2.zero, Vector2.zero, Single("Kit_BarFill"), Image.Type.Filled, 1f);
        Stretch(fill.rectTransform, 10f, 9.5f, 10f, 8.5f);
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.color = new Color(0.45f, 0.86f, 0.3f);
        var progress = Text("Progress", track.transform, Vector2.zero, Vector2.zero, 28f, Color.white, TextAlignmentOptions.Center, "12 / 30", true);
        Stretch(progress.rectTransform, 10f, 4f, 10f, 4f);
        var btn = Node("Claim", row);
        btn.anchoredPosition = new Vector2(330f, -6f);
        btn.sizeDelta = new Vector2(210f, 110f);
        var bBack = btn.gameObject.AddComponent<Image>();
        bBack.sprite = P("Btn_Grey");
        bBack.type = Image.Type.Sliced;
        var button = btn.gameObject.AddComponent<Button>();
        button.targetGraphic = bBack;
        button.transition = Selectable.Transition.None;
        var rIcon = Img("RewardIcon", btn, new Vector2(-62f, 2f), new Vector2(58f, 58f), A("Icon_Gem"), Image.Type.Simple, 1f);
        rIcon.preserveAspect = true;
        var bLabel = Text("Label", btn, new Vector2(24f, 2f), new Vector2(130f, 60f), 34f, Color.white, TextAlignmentOptions.Center, "5", true);
        var mr = row.gameObject.AddComponent<MissionRow>();
        SetRefs(mr, ("icon", icon), ("title", title), ("sub", sub), ("fill", fill), ("progress", progress), ("button", button), ("buttonBack", bBack),
            ("buttonLabel", bLabel), ("rewardIcon", rIcon), ("claimSprite", P("Btn_Green")), ("waitSprite", P("Btn_Grey")), ("doneIcon", B("Btn_Check")));
        row.gameObject.SetActive(false);

        var note = Text("Note", templates, Vector2.zero, new Vector2(880f, 90f), 28f, Cream, TextAlignmentOptions.Center, "NEW MISSIONS IN 5h", true);
        note.textWrappingMode = TextWrappingModes.Normal;
        note.gameObject.AddComponent<LayoutElement>().preferredHeight = 90f;
        note.gameObject.SetActive(false);

        var panel = f.holder.gameObject.AddComponent<MissionsPanel>();
        SetRefs(panel, ("root", f.root), ("dim", f.dim), ("card", f.card), ("cardGroup", f.group), ("close", f.close), ("scroll", scroll),
            ("tabOn", P("BtnSmall_Yellow")), ("tabOff", P("BtnSmall_Grey")), ("rowTemplate", mr), ("noteTemplate", note),
            ("cashIcon", A("Icon_Cash")), ("diamondIcon", A("Icon_Gem")), ("xpIcon", A("Icon_Star")), ("mainIcon", B("B_r2_c5")), ("boostIcon", A("Icon_Bolt")),
            ("openSfx", Sfx("Sfx_UIOpen")), ("closeSfx", Sfx("Sfx_UIClose")), ("claimSfx", Sfx("Sfx_Reward")));
        var so = new SerializedObject(panel);
        Arr(so.FindProperty("tabs"), tabs);
        Arr(so.FindProperty("tabBacks"), backs);
        Arr(so.FindProperty("pages"), pages);
        so.ApplyModifiedPropertiesWithoutUndo();
        f.root.SetActive(false);
        return panel;
    }

    static (ScrollRect, RectTransform[]) Pages(RectTransform card, string[] names)
    {
        var scrollRt = Node("Scroll", card);
        Stretch(scrollRt, 36f, 40f, 36f, 250f);
        var scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.scrollSensitivity = 30f;
        var viewport = Stretch(Node("Viewport", scrollRt));
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        scroll.viewport = viewport;
        var pages = new RectTransform[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            var page = Node("Page_" + names[i], viewport);
            page.anchorMin = new Vector2(0f, 1f);
            page.anchorMax = new Vector2(1f, 1f);
            page.pivot = new Vector2(0.5f, 1f);
            page.anchoredPosition = Vector2.zero;
            page.sizeDelta = Vector2.zero;
            var v = page.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(4, 4, 30, 40);
            v.spacing = 22f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlHeight = true;
            v.childControlWidth = false;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = false;
            page.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            pages[i] = page;
        }

        scroll.content = pages[0];
        return (scroll, pages);
    }

    static DailyPanel DailyPanel(Transform canvas)
    {
        var f = Frame(canvas, "DailyPanel", "DAILY REWARDS", P("Title_Yellow"), new Vector2(960f, 1420f), -60f);
        Text("Line", f.card, new Vector2(0f, 560f), new Vector2(800f, 50f), 32f, Cream, TextAlignmentOptions.Center, "COME BACK EVERY DAY · DAY 7 IS THE BIG ONE", true);
        var tiles = new DailyPanel.Tile[7];
        for (int i = 0; i < 6; i++)
        {
            var pos = new Vector2(-290f + (i % 3) * 290f, 370f - (i / 3) * 300f);
            tiles[i] = Tile(f.card, i, pos, new Vector2(270f, 280f), false);
        }

        tiles[6] = Tile(f.card, 6, new Vector2(0f, -240f), new Vector2(850f, 270f), true);

        var claim = Node("Claim", f.card);
        claim.anchoredPosition = new Vector2(0f, -550f);
        claim.sizeDelta = new Vector2(560f, 150f);
        var cBack = claim.gameObject.AddComponent<Image>();
        cBack.sprite = P("Btn_Green");
        cBack.type = Image.Type.Sliced;
        cBack.pixelsPerUnitMultiplier = 0.9f;
        var cb = claim.gameObject.AddComponent<Button>();
        cb.targetGraphic = cBack;
        cb.transition = Selectable.Transition.None;
        var cLabel = Text("Label", claim, Vector2.zero, Vector2.zero, 46f, Color.white, TextAlignmentOptions.Center, "CLAIM", true);
        Stretch(cLabel.rectTransform, 30f, 14f, 30f, 10f);

        var panel = f.holder.gameObject.AddComponent<DailyPanel>();
        SetRefs(panel, ("root", f.root), ("dim", f.dim), ("card", f.card), ("cardGroup", f.group), ("close", f.close), ("claim", cb), ("claimBack", cBack),
            ("claimLabel", cLabel), ("claimOn", P("Btn_Green")), ("claimOff", P("Btn_Grey")), ("tileToday", P("Card_Yellow")), ("tileFuture", P("Sq_Cream")),
            ("tileTaken", P("Sq_Grey")), ("cashIcon", A("Icon_Cash")), ("diamondIcon", A("Icon_Gem")),
            ("openSfx", Sfx("Sfx_UIOpen")), ("closeSfx", Sfx("Sfx_UIClose")), ("claimSfx", Sfx("Sfx_Reward")));
        var so = new SerializedObject(panel);
        var arr = so.FindProperty("tiles");
        arr.arraySize = 7;
        for (int i = 0; i < 7; i++)
        {
            var e = arr.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("root").objectReferenceValue = tiles[i].root;
            e.FindPropertyRelative("back").objectReferenceValue = tiles[i].back;
            e.FindPropertyRelative("day").objectReferenceValue = tiles[i].day;
            e.FindPropertyRelative("icon").objectReferenceValue = tiles[i].icon;
            e.FindPropertyRelative("amount").objectReferenceValue = tiles[i].amount;
            e.FindPropertyRelative("check").objectReferenceValue = tiles[i].check;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        f.root.SetActive(false);
        return panel;
    }

    static DailyPanel.Tile Tile(RectTransform card, int i, Vector2 pos, Vector2 size, bool big)
    {
        var r = Node("Day" + (i + 1), card);
        r.anchoredPosition = pos;
        r.sizeDelta = size;
        var back = r.gameObject.AddComponent<Image>();
        back.sprite = P("Sq_Cream");
        back.type = Image.Type.Sliced;
        back.pixelsPerUnitMultiplier = 0.8f;
        var t = new DailyPanel.Tile { root = r, back = back };
        if (big)
        {
            var rays = Img("Rays", r, new Vector2(-250f, 0f), new Vector2(330f, 330f), Single("Fx_Rays"), Image.Type.Simple, 1f);
            rays.color = new Color(1f, 0.78f, 0.25f, 0.5f);
            t.icon = Img("Icon", r, new Vector2(-250f, 0f), new Vector2(200f, 200f), null, Image.Type.Simple, 1f);
            t.day = Text("Day", r, new Vector2(120f, 60f), new Vector2(520f, 54f), 40f, Navy, TextAlignmentOptions.Center, "DAY 7", false);
            t.amount = Text("Amount", r, new Vector2(120f, -30f), new Vector2(520f, 70f), 50f, Navy, TextAlignmentOptions.Center, "$50K + 50", false);
        }
        else
        {
            t.day = Text("Day", r, new Vector2(0f, 104f), new Vector2(240f, 44f), 32f, Navy, TextAlignmentOptions.Center, "DAY 1", false);
            t.icon = Img("Icon", r, new Vector2(0f, 6f), new Vector2(130f, 130f), null, Image.Type.Simple, 1f);
            t.amount = Text("Amount", r, new Vector2(0f, -98f), new Vector2(250f, 44f), 32f, Navy, TextAlignmentOptions.Center, "$2K", false);
        }

        t.icon.preserveAspect = true;
        var check = Img("Check", r, new Vector2(size.x * 0.5f - 34f, size.y * 0.5f - 34f), new Vector2(76f, 76f), B("Btn_Check"), Image.Type.Simple, 1f);
        check.preserveAspect = true;
        t.check = check.gameObject;
        check.gameObject.SetActive(false);
        return t;
    }

    // ------------------------------------------------------------------ helpers

    static void Arr(SerializedProperty p, Object[] values)
    {
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

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

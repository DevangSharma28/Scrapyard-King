using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScrapYardKing.Boosts;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Feedback;
using ScrapYardKing.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Revamp 5 builder: timed boosts, optional rewarded-video offers and offline income.
//  - Assets: five BoostDefinitions (cash, production, speed, scrap rush, truck rush) and five AdOfferDefinitions (the
//    first four boosts on the rotating HUD offer, free cash, and "double the order" right after a truck).
//  - Scene: the services in _Systems (BoostManager, AdService, OfferDirector, IdleIncomeManager) and their HUD: boost
//    chips under the cash pill, one offer button on the right edge, the "while you were away" panel.
// No ad SDK is integrated: AdService grants rewards through its built-in stand-in until a provider is plugged in.
// Entry points: Assets, Scene. Idempotent. Run after UI_Build.Kit (it uses kit sprites).
public static class R5_Build
{
    const string P = "Assets/_Project";
    const string DataDir = P + "/Data";
    const string KitDir = P + "/Art/UI/Kit";
    const string ScenePath = P + "/Scenes/Area1_OldScrapYard.unity";

    static readonly StringBuilder Log = new();
    static TMP_FontAsset font;
    static Material outline;

    // (asset, id, name, kind, multiplier, seconds, colour, kit icon, icon atlas)
    static readonly (string asset, string id, string name, BoostKind kind, float mult, float seconds, Color color, string icon, string atlas)[] Boosts =
    {
        ("Boost_Cash", "boost_cash", "2X CASH", BoostKind.Cash, 2f, 120f, new Color(0.42f, 0.86f, 0.3f), "Icon_CashUp", "Kit_IconsA"),
        ("Boost_Production", "boost_production", "2X FACTORY", BoostKind.Production, 2f, 90f, new Color(0.3f, 0.75f, 1f), "Icon_Bolt", "Kit_IconsA"),
        ("Boost_Speed", "boost_speed", "FAST BOOTS", BoostKind.MoveSpeed, 1.5f, 120f, new Color(1f, 0.6f, 0.2f), "Icon_Shoe", "Kit_IconsB"),
        ("Boost_ScrapRush", "boost_scrap", "SCRAP RUSH", BoostKind.ScrapSpawn, 2.5f, 90f, new Color(1f, 0.42f, 0.25f), "Icon_Fire", "Kit_IconsA"),
        ("Boost_TruckRush", "boost_truck", "TRUCK RUSH", BoostKind.TruckTempo, 3f, 120f, new Color(0.95f, 0.8f, 0.2f), "Icon_Stopwatch", "Kit_IconsA"),
    };

    public static string Assets()
    {
        Log.Clear();
        var start = AssetDatabase.LoadAssetAtPath<SfxDefinition>(DataDir + "/Audio/Sfx_OverdriveStart.asset");
        var end = AssetDatabase.LoadAssetAtPath<SfxDefinition>(DataDir + "/Audio/Sfx_OverdriveEnd.asset");
        foreach (var b in Boosts)
        {
            var def = ArtAssets.LoadOrCreate<BoostDefinition>($"{DataDir}/Boosts/{b.asset}.asset");
            ArtAssets.Set(def, ("id", b.id), ("displayName", b.name), ("icon", Kit(b.atlas, b.icon)), ("kind", (int)b.kind), ("multiplier", b.mult), ("duration", b.seconds),
                ("maxStack", 3f), ("color", b.color), ("startSfx", start), ("endSfx", end));
        }

        // Offers. The boosts rotate on the HUD button; free cash joins them; the truck double is offered by the director
        // right after a completed order. Cooldowns are per offer, on top of the quiet gap between any two offers.
        Offer("Offer_Cash", "offer_cash", "2X CASH", AdOfferKind.Boost, "Boost_Cash", null, 0, 25f, 240f, 2, 1.2f);
        Offer("Offer_Production", "offer_production", "2X FACTORY", AdOfferKind.Boost, "Boost_Production", null, 0, 25f, 240f, 3, 1f);
        Offer("Offer_Speed", "offer_speed", "FAST BOOTS", AdOfferKind.Boost, "Boost_Speed", null, 0, 20f, 300f, 2, 0.6f);
        Offer("Offer_ScrapRush", "offer_scrap", "SCRAP RUSH", AdOfferKind.Boost, "Boost_ScrapRush", null, 0, 25f, 240f, 2, 1f);
        Offer("Offer_TruckRush", "offer_truck_rush", "TRUCK RUSH", AdOfferKind.Boost, "Boost_TruckRush", null, 0, 25f, 300f, 10, 0.8f);
        Offer("Offer_FreeCash", "offer_free_cash", "FREE CASH", AdOfferKind.FreeCash, null, Kit("Kit_IconsA", "Icon_Gift"), 250, 20f, 300f, 2, 0.8f);
        Offer("Offer_TruckDouble", "offer_truck_double", "2X ORDER PAY", AdOfferKind.DoubleTruck, null, Kit("Kit_IconsA", "Icon_Cash"), 0, 12f, 45f, 1, 0f);
        AssetDatabase.SaveAssets();
        Log.AppendLine("boosts: 5, offers: 7");
        return Log.ToString();
    }

    static void Offer(string asset, string id, string title, AdOfferKind kind, string boost, Sprite icon, int cashPerLevel, float show, float cooldown, int minLevel, float weight)
    {
        var def = ArtAssets.LoadOrCreate<AdOfferDefinition>($"{DataDir}/Boosts/{asset}.asset");
        ArtAssets.Set(def, ("id", id), ("title", title), ("kind", (int)kind), ("boost", boost != null ? AssetDatabase.LoadAssetAtPath<BoostDefinition>($"{DataDir}/Boosts/{boost}.asset") : null),
            ("icon", icon), ("cashPerLevel", cashPerLevel), ("showSeconds", show), ("cooldown", cooldown), ("minLevel", minLevel), ("weight", weight));
    }

    public static string Scene()
    {
        Log.Clear();
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(P + "/Art/Fonts/GROBOLD SDF.asset");
        outline = AssetDatabase.LoadAssetAtPath<Material>(P + "/Art/Fonts/GROBOLD Outline.mat");

        // ---------- services ----------
        var systems = scene.GetRootGameObjects().First(g => g.name == "_Systems").transform;
        var boosts = Service<BoostManager>(systems, "BoostManager");
        ArtAssets.SetArray(boosts, "known", Boosts.Select(b => (Object)AssetDatabase.LoadAssetAtPath<BoostDefinition>($"{DataDir}/Boosts/{b.asset}.asset")).ToArray());
        Service<AdService>(systems, "AdService");
        var director = Service<OfferDirector>(systems, "OfferDirector");
        ArtAssets.SetArray(director, "offers", new[] { "Offer_Cash", "Offer_Production", "Offer_Speed", "Offer_ScrapRush", "Offer_TruckRush", "Offer_FreeCash" }
            .Select(n => (Object)AssetDatabase.LoadAssetAtPath<AdOfferDefinition>($"{DataDir}/Boosts/{n}.asset")).ToArray());
        ArtAssets.Set(director, ("doubleTruckOffer", AssetDatabase.LoadAssetAtPath<AdOfferDefinition>(DataDir + "/Boosts/Offer_TruckDouble.asset")));
        Service<IdleIncomeManager>(systems, "IdleIncomeManager");

        // ---------- HUD ----------
        var canvas = scene.GetRootGameObjects().First(g => g.name == "_UI").transform.Find("Canvas");
        var hud = canvas.Find("HUD");
        foreach (string n in new[] { "BoostBar", "OfferButton" })
        {
            var t = hud.Find(n);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        var oldPanel = canvas.Find("OfflinePanel");
        if (oldPanel != null) Object.DestroyImmediate(oldPanel.gameObject);
        BoostChips(hud);
        OfferButton(hud);
        OfflinePanel(canvas);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("scene: 4 services, boost chips, offer button, offline panel");
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

    // ---------- HUD pieces ----------

    /// <summary>Four chips under the currency pills, top right.</summary>
    static void BoostChips(Transform hud)
    {
        var bar = Rect("BoostBar", hud, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -156f), new Vector2(230f, 300f));
        var chips = new List<(GameObject root, Image icon, Image fill, TMP_Text time)>();
        for (int i = 0; i < 4; i++)
        {
            var chip = Rect("Chip" + i, bar, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -i * 74f), new Vector2(230f, 66f));
            Img("Back", chip, Vector2.zero, new Vector2(230f, 66f), Kit("Kit_Panels", "Bar_Dark"), Image.Type.Sliced, 1.5f, false, new Vector2(0.5f, 0.5f));
            var icon = Img("Icon", chip, new Vector2(-78f, 0f), new Vector2(54f, 54f), null, Image.Type.Simple, 1f, false, new Vector2(0.5f, 0.5f));
            icon.preserveAspect = true;
            var track = Img("Track", chip, new Vector2(30f, -14f), new Vector2(136f, 14f), Load<Sprite>(KitDir + "/Kit_PillWhite.png"), Image.Type.Sliced, 5f, false, new Vector2(0.5f, 0.5f));
            track.color = new Color(0f, 0f, 0f, 0.45f);
            var fill = Img("Fill", chip, new Vector2(30f, -14f), new Vector2(132f, 10f), Load<Sprite>(KitDir + "/Kit_BarFill.png"), Image.Type.Filled, 1f, false, new Vector2(0.5f, 0.5f));
            fill.fillMethod = Image.FillMethod.Horizontal;
            var time = Text("Time", chip, new Vector2(30f, 12f), new Vector2(136f, 30f), 26f, Color.white, TextAlignmentOptions.Center, "1:30", new Vector2(0.5f, 0.5f));
            chips.Add((chip.gameObject, icon, fill, time));
        }

        var component = bar.gameObject.AddComponent<BoostBar>();
        var so = new SerializedObject(component);
        var p = so.FindProperty("chips");
        p.arraySize = chips.Count;
        for (int i = 0; i < chips.Count; i++)
        {
            var e = p.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("root").objectReferenceValue = chips[i].root;
            e.FindPropertyRelative("icon").objectReferenceValue = chips[i].icon;
            e.FindPropertyRelative("fill").objectReferenceValue = chips[i].fill;
            e.FindPropertyRelative("time").objectReferenceValue = chips[i].time;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>One offer button on the right edge, above the thumb zone.</summary>
    static void OfferButton(Transform hud)
    {
        var holder = Rect("OfferButton", hud, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-22f, 150f), new Vector2(196f, 232f));
        var root = Rect("Root", holder, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(196f, 232f));
        var pulse = Rect("Pulse", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(196f, 232f));
        var back = Img("Back", pulse, Vector2.zero, new Vector2(196f, 232f), Kit("Kit_Panels", "Sq_Navy"), Image.Type.Sliced, 1.4f, true, new Vector2(0.5f, 0.5f));
        var icon = Img("Icon", pulse, new Vector2(0f, 34f), new Vector2(104f, 104f), null, Image.Type.Simple, 1f, false, new Vector2(0.5f, 0.5f));
        icon.preserveAspect = true;
        var title = Text("Title", pulse, new Vector2(0f, 96f), new Vector2(180f, 34f), 26f, Color.white, TextAlignmentOptions.Center, "2X CASH", new Vector2(0.5f, 0.5f));
        Img("Pill", pulse, new Vector2(0f, -58f), new Vector2(160f, 54f), Kit("Kit_Panels", "BtnSmall_Green"), Image.Type.Sliced, 1.2f, false, new Vector2(0.5f, 0.5f));
        var detail = Text("Detail", pulse, new Vector2(0f, -56f), new Vector2(150f, 40f), 26f, Color.white, TextAlignmentOptions.Center, "WATCH", new Vector2(0.5f, 0.5f));
        var track = Img("TimerTrack", pulse, new Vector2(0f, -100f), new Vector2(160f, 12f), Load<Sprite>(KitDir + "/Kit_PillWhite.png"), Image.Type.Sliced, 6f, false, new Vector2(0.5f, 0.5f));
        track.color = new Color(0f, 0f, 0f, 0.45f);
        var timer = Img("Timer", pulse, new Vector2(0f, -100f), new Vector2(156f, 8f), Load<Sprite>(KitDir + "/Kit_BarFill.png"), Image.Type.Filled, 1f, false, new Vector2(0.5f, 0.5f));
        timer.fillMethod = Image.FillMethod.Horizontal;
        timer.color = new Color(1f, 0.82f, 0.2f);
        var button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        button.transition = Selectable.Transition.None;
        var component = holder.gameObject.AddComponent<OfferButton>();
        ArtAssets.Set(component, ("root", root), ("button", button), ("icon", icon), ("title", title), ("detail", detail), ("timer", timer), ("pulseRoot", pulse));
    }

    /// <summary>"While you were away": dimmed screen, a card with the amount and two buttons.</summary>
    static void OfflinePanel(Transform canvas)
    {
        var holder = Rect("OfflinePanel", canvas, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        holder.anchorMin = Vector2.zero;
        holder.anchorMax = Vector2.one;
        holder.offsetMin = holder.offsetMax = Vector2.zero;
        var root = Rect("Root", holder, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        var dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0.03f, 0.05f, 0.1f, 0.72f);
        dim.raycastTarget = true;   // nothing behind it takes a touch while it is up

        var card = Rect("Card", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(820f, 700f));
        Img("Back", card, Vector2.zero, new Vector2(820f, 700f), Kit("Kit_Panels", "Panel_Dark"), Image.Type.Sliced, 0.6f, false, new Vector2(0.5f, 0.5f));
        Img("TitlePlate", card, new Vector2(0f, 310f), new Vector2(700f, 120f), Kit("Kit_Panels", "Title_Yellow"), Image.Type.Simple, 1f, false, new Vector2(0.5f, 0.5f));
        Text("Title", card, new Vector2(0f, 314f), new Vector2(600f, 70f), 44f, Color.white, TextAlignmentOptions.Center, "WHILE YOU WERE AWAY", new Vector2(0.5f, 0.5f));
        Text("Line", card, new Vector2(0f, 190f), new Vector2(700f, 44f), 32f, new Color(1f, 0.93f, 0.75f), TextAlignmentOptions.Center, "YOUR YARD KEPT WORKING FOR", new Vector2(0.5f, 0.5f));
        var away = Text("Away", card, new Vector2(0f, 136f), new Vector2(700f, 56f), 46f, Color.white, TextAlignmentOptions.Center, "2 H 14 MIN", new Vector2(0.5f, 0.5f));
        var coin = Img("Coin", card, new Vector2(-170f, 20f), new Vector2(120f, 120f), Kit("Kit_IconsA", "Icon_Cash"), Image.Type.Simple, 1f, false, new Vector2(0.5f, 0.5f));
        coin.preserveAspect = true;
        var amount = Text("Amount", card, new Vector2(70f, 20f), new Vector2(360f, 100f), 84f, new Color(0.62f, 1f, 0.5f), TextAlignmentOptions.Left, "+12.5K", new Vector2(0.5f, 0.5f));

        Button Btn(string name, Vector2 pos, string sprite, string label, out TMP_Text sub)
        {
            var rt = Rect(name, card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(340f, 150f));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Kit("Kit_Panels", sprite);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            Text("Label", rt, new Vector2(0f, 24f), new Vector2(300f, 54f), 40f, Color.white, TextAlignmentOptions.Center, label, new Vector2(0.5f, 0.5f));
            sub = Text("Sub", rt, new Vector2(0f, -30f), new Vector2(300f, 40f), 28f, new Color(1f, 0.96f, 0.8f), TextAlignmentOptions.Center, "", new Vector2(0.5f, 0.5f));
            return b;
        }

        var claim = Btn("Claim", new Vector2(-190f, -200f), "Btn_Yellow", "CLAIM", out var claimSub);
        claimSub.text = "";
        var claimDouble = Btn("ClaimDouble", new Vector2(190f, -200f), "Btn_Green", "2X  WATCH", out var doubleSub);
        var panel = holder.gameObject.AddComponent<OfflinePanel>();
        ArtAssets.Set(panel, ("root", root.gameObject), ("card", card), ("away", away), ("amount", amount), ("doubleAmount", doubleSub), ("claim", claim), ("claimDouble", claimDouble));
        root.gameObject.SetActive(false);
        holder.SetAsLastSibling();
    }

    // ---------- small UI helpers ----------

    static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    static Image Img(string name, Transform parent, Vector2 pos, Vector2 size, Sprite sprite, Image.Type type, float ppu, bool raycast, Vector2 anchor)
    {
        var img = Rect(name, parent, anchor, new Vector2(0.5f, 0.5f), pos, size).gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.type = type;
        img.pixelsPerUnitMultiplier = ppu;
        img.raycastTarget = raycast;
        return img;
    }

    static TMP_Text Text(string name, Transform parent, Vector2 pos, Vector2 size, float fontSize, Color color, TextAlignmentOptions align, string sample, Vector2 anchor)
    {
        var t = Rect(name, parent, anchor, new Vector2(0.5f, 0.5f), pos, size).gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font;
        if (outline != null) t.fontSharedMaterial = outline;
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = align;
        t.text = sample;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.enableAutoSizing = true;
        t.fontSizeMin = 12f;
        t.fontSizeMax = fontSize;
        return t;
    }

    static Sprite Kit(string atlas, string name)
    {
        var sprite = AssetDatabase.LoadAllAssetsAtPath($"{KitDir}/{atlas}.png").OfType<Sprite>().FirstOrDefault(s => s.name == name);
        if (sprite == null) Log.AppendLine($"!! no kit sprite {atlas}/{name}");
        return sprite;
    }

    static T Load<T>(string path) where T : Object
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a == null) Log.AppendLine("!! missing " + path);
        return a;
    }
}

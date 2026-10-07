using System;
using System.IO;
using System.Linq;
using System.Text;
using ScrapYardKing.Economy;
using ScrapYardKing.Feedback;
using ScrapYardKing.Settings;
using ScrapYardKing.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// UI step U1 (owner's UI/economy brief, 2026-10-06): the foundation every later UI step builds on.
///  - Assets: two FX sprites drawn here (sun rays, sparkle), UI sounds from the Kenney CC0 clips, and
///    <c>Data/Economy/PremiumEconomy.asset</c> (diamond prices for time and cash, confirm threshold, free sources).
///  - Scene: <c>SettingsApplier</c> and <c>DiamondRewards</c> in _Systems; the HUD inside the safe area with a tidier top
///    row (level · diamonds · cash · gear), gain tickers and a glow on the cash capsule; the shared popup card
///    (<c>_UI/Canvas/PopupLayer</c>); the settings / pause panel (<c>_UI/Canvas/SettingsPanel</c>); a tactile press
///    (<c>UIButtonFeel</c>) on every button.
/// Entry points: Assets, Scene (or All). Idempotent: the popup layer and settings panel are rebuilt on each run, the HUD
/// is edited in place. Run after UI_Build.Apply and R5_Build.Scene (both reset parts of the HUD this step moves).
/// </summary>
public static class U1_Build
{
    const string Root = "Assets/_Project";
    const string KitDir = Root + "/Art/UI/Kit";
    const string AudioDir = Root + "/Data/Audio";
    const string KenneyAudio = "Assets/ThirdParty/Kenney/Audio";
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
        Rays($"{KitDir}/Fx_Rays.png");
        Sparkle($"{KitDir}/Fx_Sparkle.png");

        Sfx("Sfx_UIClick", 0.45f, new Vector2(0.97f, 1.04f), "Interface/select_002.ogg");
        Sfx("Sfx_UIOpen", 0.5f, new Vector2(1f, 1f), "Interface/maximize_006.ogg");
        Sfx("Sfx_UIClose", 0.4f, new Vector2(1f, 1f), "Interface/minimize_006.ogg");
        Sfx("Sfx_Reward", 0.7f, new Vector2(1f, 1f), "Jingles/jingles_STEEL02.ogg");
        Sfx("Sfx_Diamond", 0.55f, new Vector2(1.05f, 1.2f), "Casino/chips-collide-1.ogg", "Casino/chips-collide-2.ogg", "Casino/chips-collide-3.ogg");

        var premium = LoadOrCreate<PremiumEconomyConfig>(Root + "/Data/Economy/PremiumEconomy.asset");
        var so = new SerializedObject(premium);
        so.FindProperty("minSkipCost").intValue = 2;
        so.FindProperty("diamondsPerMinute").floatValue = 3f;
        so.FindProperty("bulkExponent").floatValue = 0.85f;
        so.FindProperty("maxSkipCost").intValue = 400;
        so.FindProperty("cashPerDiamond").longValue = 60;
        so.FindProperty("cashPerDiamondGrowth").floatValue = 1.3f;
        so.FindProperty("confirmAbove").intValue = 20;
        so.FindProperty("levelUpDiamonds").intValue = 5;
        so.FindProperty("levelUpFrom").intValue = 6;
        so.FindProperty("levelUpEvery").intValue = 2;
        so.FindProperty("firstDiamondLesson").stringValue = "DIAMONDS SPEED THINGS UP";
        // One gift per new part of the yard. The first (the plant, ~minute 9) is the first diamond moment.
        var gifts = new (string id, int diamonds, string title, string line)[]
        {
            ("recycling_plant", 25, "NEW AREA BONUS", "RECYCLING PLANT OPEN"),
            ("furnace_hall", 15, "FURNACE BONUS", "FURNACE HALL OPEN"),
            ("build_press", 15, "PRESS BONUS", "INDUSTRIAL PRESS BUILT"),
            ("truck_dock", 20, "DOCK BONUS", "TRUCK ORDERS START"),
            ("heavy_yard", 25, "NEW AREA BONUS", "HEAVY SCRAP YARD OPEN"),
            ("dockyard", 50, "PORT BONUS", "THE DOCKYARD IS OPEN"),
        };
        var list = so.FindProperty("milestones");
        list.arraySize = gifts.Length;
        for (int i = 0; i < gifts.Length; i++)
        {
            var e = list.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("expansionId").stringValue = gifts[i].id;
            e.FindPropertyRelative("diamonds").intValue = gifts[i].diamonds;
            e.FindPropertyRelative("title").stringValue = gifts[i].title;
            e.FindPropertyRelative("line").stringValue = gifts[i].line;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(premium);
        AssetDatabase.SaveAssets();
        Log.AppendLine($"assets: fx sprites, 5 sfx, PremiumEconomy ({gifts.Length} milestones, {gifts.Sum(g => g.diamonds)} diamonds)");
        return Log.ToString();
    }

    /// <summary>Soft sun rays: 14 wedges fading out from the centre (white, tinted in the scene).</summary>
    static void Rays(string path)
    {
        const int n = 256;
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Atan2(dy, dx);
            float wedge = Edge(0.35f, 0.75f, Mathf.Abs(Mathf.Sin(a * 7f)));
            float fade = Mathf.Clamp01(1f - r) * Edge(0.05f, 0.25f, r);
            float glow = Mathf.Clamp01(1f - r * 1.6f) * 0.5f;
            byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(wedge * fade * 1.4f + glow) * 255f);
            px[y * n + x] = new Color32(255, 255, 255, alpha);
        }

        WriteSprite(path, px, n);
    }

    /// <summary>GLSL-style smoothstep: 0 below <paramref name="e0"/>, 1 above <paramref name="e1"/>, smooth between.
    /// (Mathf.SmoothStep is an interpolation between two values, not this.)</summary>
    static float Edge(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    /// <summary>Four-point sparkle with a soft halo.</summary>
    static void Sparkle(string path)
    {
        const int n = 128;
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = Mathf.Abs((x + 0.5f) / n * 2f - 1f), dy = Mathf.Abs((y + 0.5f) / n * 2f - 1f);
            float star = Mathf.Pow(dx, 0.5f) + Mathf.Pow(dy, 0.5f);
            float core = 1f - Edge(0.55f, 0.85f, star);
            float halo = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 1.8f) * 0.35f;
            px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(core + halo) * 255f));
        }

        WriteSprite(path, px, n);
    }

    static void WriteSprite(string path, Color32[] px, int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
    }

    static void Sfx(string asset, float volume, Vector2 pitch, params string[] clips)
    {
        var def = LoadOrCreate<SfxDefinition>($"{AudioDir}/{asset}.asset");
        var so = new SerializedObject(def);
        var c = so.FindProperty("clips");
        var found = clips.Select(p => AssetDatabase.LoadAssetAtPath<AudioClip>($"{KenneyAudio}/{p}")).Where(a => a != null).ToArray();
        if (found.Length < clips.Length) Log.AppendLine($"!! {asset}: {clips.Length - found.Length} clip(s) missing");
        c.arraySize = found.Length;
        for (int i = 0; i < found.Length; i++) c.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
        so.FindProperty("volume").floatValue = volume;
        so.FindProperty("pitchRange").vector2Value = pitch;
        so.FindProperty("minInterval").floatValue = 0.05f;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(def);
    }

    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a != null) return a;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
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
        Service<SettingsApplier>(systems, "SettingsApplier");
        var rewards = Service<DiamondRewards>(systems, "DiamondRewards");
        SetRefs(rewards, ("config", AssetDatabase.LoadAssetAtPath<PremiumEconomyConfig>(Root + "/Data/Economy/PremiumEconomy.asset")),
            ("diamondIcon", A("Icon_Gem")));

        var canvas = scene.GetRootGameObjects().First(g => g.name == "_UI").transform.Find("Canvas");
        var hud = canvas.Find("HUD");
        Hud(hud);

        Rebuild(canvas, "SettingsPanel");
        Rebuild(canvas, "PopupLayer");
        var settings = SettingsPanel(canvas, hud.Find("SettingsButton").GetComponent<Button>());
        var popups = PopupLayer(canvas);
        var offline = canvas.Find("OfflinePanel");
        if (offline != null) offline.SetSiblingIndex(hud.GetSiblingIndex() + 1);
        settings.transform.SetAsLastSibling();
        popups.transform.SetAsLastSibling();

        int feel = ButtonFeel(canvas);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine($"scene: services, HUD top row + safe area, popup layer, settings panel, button feel on {feel} buttons");
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

    static void Rebuild(Transform parent, string name)
    {
        var old = parent.Find(name);
        if (old != null) Object.DestroyImmediate(old.gameObject);
    }

    // ------------------------------------------------------------------ HUD

    static void Hud(Transform hud)
    {
        if (!hud.TryGetComponent<SafeArea>(out _)) hud.gameObject.AddComponent<SafeArea>();

        // level badge: the star no longer covers the caption, the bar ends before the diamond capsule
        var badge = hud.Find("LevelBadge");
        Size(badge.Find("Badge/Star"), new Vector2(132f, 132f));
        var bar = (RectTransform)badge.Find("XpBar");
        bar.pivot = new Vector2(0f, 0.5f);
        bar.anchoredPosition = new Vector2(112f, -10f);
        bar.sizeDelta = new Vector2(200f, 46f);
        var caption = (RectTransform)badge.Find("Caption");
        caption.pivot = new Vector2(0f, 0.5f);
        caption.anchoredPosition = new Vector2(132f, 30f);
        caption.sizeDelta = new Vector2(190f, 34f);
        var captionText = caption.GetComponent<TMP_Text>();
        captionText.alignment = TextAlignmentOptions.Left;
        captionText.fontSize = 24f;
        captionText.enableAutoSizing = false;
        EditorUtility.SetDirty(captionText);

        // right group: diamonds · cash · gear
        var gem = (RectTransform)hud.Find("GemPill");
        var cash = (RectTransform)hud.Find("CashPill");
        Pill(gem, new Vector2(-478f, -46f), new Vector2(220f, 92f));
        Pill(cash, new Vector2(-124f, -46f), new Vector2(340f, 92f));
        foreach (var pill in new[] { gem, cash })
        {
            var label = (RectTransform)pill.Find("Label");
            label.offsetMin = new Vector2(100f, 4f);
            label.offsetMax = new Vector2(-16f, -4f);
            var text = label.GetComponent<TMP_Text>();
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = true;
            text.fontSizeMax = text.fontSize;
            text.fontSizeMin = 24f;
            EditorUtility.SetDirty(text);
        }

        Ticker(gem, "Ticker");
        var cashTicker = Ticker(cash, "Ticker");
        var glow = cash.Find("Glow") as RectTransform;
        if (glow == null)
        {
            glow = Node("Glow", cash);
            glow.gameObject.AddComponent<Image>();
        }

        glow.SetSiblingIndex(0);
        glow.anchorMin = Vector2.zero;
        glow.anchorMax = Vector2.one;
        glow.offsetMin = new Vector2(-16f, -14f);
        glow.offsetMax = new Vector2(16f, 14f);
        var glowImage = glow.GetComponent<Image>();
        glowImage.sprite = Single("Kit_PillWhite");
        glowImage.type = Image.Type.Sliced;
        glowImage.pixelsPerUnitMultiplier = 0.7f;
        glowImage.color = new Color(1f, 0.9f, 0.4f, 0f);
        glowImage.raycastTarget = false;

        var gear = hud.Find("SettingsButton") as RectTransform;
        if (gear == null)
        {
            gear = Node("SettingsButton", hud);
            gear.gameObject.AddComponent<Image>();
            gear.gameObject.AddComponent<Button>();
            var icon = Node("Icon", gear);
            icon.gameObject.AddComponent<Image>();
        }

        gear.anchorMin = gear.anchorMax = new Vector2(1f, 1f);
        gear.pivot = new Vector2(1f, 0.5f);
        gear.anchoredPosition = new Vector2(-22f, -46f);
        gear.sizeDelta = new Vector2(92f, 92f);
        var gearBack = gear.GetComponent<Image>();
        gearBack.sprite = P("Sq_DarkYellow");
        gearBack.type = Image.Type.Sliced;
        gearBack.pixelsPerUnitMultiplier = 1.3f;
        var gearButton = gear.GetComponent<Button>();
        gearButton.targetGraphic = gearBack;
        gearButton.transition = Selectable.Transition.None;
        var gearIcon = (RectTransform)gear.Find("Icon");
        gearIcon.anchorMin = gearIcon.anchorMax = new Vector2(0.5f, 0.5f);
        gearIcon.anchoredPosition = Vector2.zero;
        gearIcon.sizeDelta = new Vector2(66f, 66f);
        var gearIconImage = gearIcon.GetComponent<Image>();
        gearIconImage.sprite = B("Icon_Settings");
        gearIconImage.preserveAspect = true;
        gearIconImage.raycastTarget = false;

        // boost chips under the task banner instead of beside it
        var boosts = hud.Find("BoostBar") as RectTransform;
        if (boosts != null) boosts.anchoredPosition = new Vector2(-30f, -236f);

        var controller = hud.GetComponent<HudController>();
        SetRefs(controller, ("premiumIcon", A("Icon_Gem")), ("cashTicker", cashTicker), ("premiumTicker", gem.Find("Ticker").GetComponent<TMP_Text>()),
            ("cashGlow", glowImage), ("bigGainSfx", Sfx("Sfx_CashCollect")), ("diamondSfx", Sfx("Sfx_Diamond")));
        EditorUtility.SetDirty(controller);
    }

    static void Pill(RectTransform pill, Vector2 position, Vector2 size)
    {
        pill.pivot = new Vector2(1f, 0.5f);
        pill.anchorMin = pill.anchorMax = new Vector2(1f, 1f);
        pill.anchoredPosition = position;
        pill.sizeDelta = size;
    }

    static TMP_Text Ticker(RectTransform pill, string name)
    {
        var t = pill.Find(name) as RectTransform;
        TMP_Text text;
        if (t == null)
        {
            t = Node(name, pill);
            text = t.gameObject.AddComponent<TextMeshProUGUI>();
        }
        else text = t.GetComponent<TMP_Text>();

        t.anchorMin = t.anchorMax = new Vector2(0.5f, 0f);
        t.pivot = new Vector2(0.5f, 1f);
        t.anchoredPosition = new Vector2(24f, -2f);
        t.sizeDelta = new Vector2(300f, 48f);
        Style(text, 36f, Color.white, TextAlignmentOptions.Center, true);
        text.text = "+0";
        text.alpha = 0f;
        return text;
    }

    static void Size(Transform t, Vector2 size)
    {
        if (t is RectTransform rt) rt.sizeDelta = size;
    }

    // ------------------------------------------------------------------ popup card

    static PopupManager PopupLayer(Transform canvas)
    {
        var layer = Stretch(Node("PopupLayer", canvas));
        var root = Stretch(Node("Root", layer));
        var dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0.03f, 0.05f, 0.1f, 0f);
        dim.raycastTarget = true;

        var card = Node("Card", root);
        card.anchoredPosition = new Vector2(0f, 30f);
        card.sizeDelta = new Vector2(860f, 1080f);
        var group = card.gameObject.AddComponent<CanvasGroup>();
        Stretch(Img("Back", card, Vector2.zero, card.sizeDelta, P("Panel_Dark"), Image.Type.Sliced, 0.55f).rectTransform);

        var rays = Img("Rays", card, new Vector2(0f, 230f), new Vector2(720f, 720f), Single("Fx_Rays"), Image.Type.Simple, 1f);
        rays.color = new Color(1f, 0.82f, 0.32f, 0.45f);
        var sparkles = Node("Sparkles", card);
        sparkles.anchoredPosition = new Vector2(0f, 230f);
        for (int i = 0; i < 12; i++)
        {
            var s = Img("Sparkle" + i, sparkles, Vector2.zero, new Vector2(72f, 72f), Single("Fx_Sparkle"), Image.Type.Simple, 1f);
            s.color = i % 3 == 0 ? Color.white : new Color(1f, 0.85f, 0.35f);
            s.rectTransform.localScale = Vector3.zero;
        }

        var icon = Img("Icon", card, new Vector2(0f, 230f), new Vector2(280f, 280f), A("Icon_Gem"), Image.Type.Simple, 1f);
        icon.preserveAspect = true;
        var amount = Text("Amount", card, new Vector2(0f, 40f), new Vector2(760f, 120f), 104f, Color.white, TextAlignmentOptions.Center, "+25", true);
        var subtitle = Text("Subtitle", card, new Vector2(0f, -52f), new Vector2(760f, 60f), 38f, Cream, TextAlignmentOptions.Center, "DIAMONDS SPEED THINGS UP", true);
        var body = Text("Body", card, new Vector2(0f, -190f), new Vector2(720f, 190f), 32f, Cream, TextAlignmentOptions.Center, "", true);
        // the card shrinks around missing parts (no icon, no amount, no body): the top block hangs from the top edge,
        // the body and buttons stand on the bottom edge
        foreach (var t in new[] { rays.rectTransform, sparkles, icon.rectTransform, amount.rectTransform, subtitle.rectTransform })
            Pin(t, 1f, 540f);
        Pin(body.rectTransform, 0f, -540f);
        body.textWrappingMode = TextWrappingModes.Normal;
        body.fontSizeMin = 22f;

        var plate = Img("TitlePlate", card, new Vector2(0f, 520f), new Vector2(660f, 128f), P("Title_Yellow"), Image.Type.Sliced, 93f / 128f);
        var title = Text("Title", plate.transform, new Vector2(0f, 4f), new Vector2(520f, 76f), 52f, Color.white, TextAlignmentOptions.Center, "BONUS!", true);

        var primary = BigButton("Primary", card, new Vector2(0f, -410f), new Vector2(520f, 150f), P("Btn_Green"), "CLAIM",
            out var primaryBack, out var primaryLabel, out var primarySub, out var primaryIcon);
        var secondary = BigButton("Secondary", card, new Vector2(195f, -410f), new Vector2(360f, 150f), P("Btn_Dark"), "WATCH",
            out var secondaryBack, out var secondaryLabel, out var secondarySub, out var secondaryIcon);

        // hold-to-confirm (danger): red plate, a fill that grows while held
        var holdRoot = Node("Hold", card);
        holdRoot.anchoredPosition = new Vector2(0f, -410f);
        holdRoot.sizeDelta = new Vector2(560f, 150f);
        var holdBack = holdRoot.gameObject.AddComponent<Image>();
        holdBack.sprite = P("Btn_Red");
        holdBack.type = Image.Type.Sliced;
        holdBack.pixelsPerUnitMultiplier = 0.9f;
        var holdFill = Img("Fill", holdRoot, Vector2.zero, new Vector2(520f, 110f), Single("Kit_BarFill"), Image.Type.Filled, 1f);
        holdFill.fillMethod = Image.FillMethod.Horizontal;
        holdFill.color = new Color(1f, 1f, 1f, 0.4f);
        holdFill.fillAmount = 0f;
        var holdLabel = Text("Label", holdRoot, new Vector2(0f, 2f), new Vector2(500f, 70f), 46f, Color.white, TextAlignmentOptions.Center, "HOLD TO RESET", true);
        var hold = holdRoot.gameObject.AddComponent<HoldButton>();
        SetRefs(hold, ("fill", holdFill));
        SetFloat(hold, "holdSeconds", 2f);

        var close = Node("Close", card);
        close.anchoredPosition = new Vector2(372f, 488f);
        close.sizeDelta = new Vector2(104f, 104f);
        var closeImage = close.gameObject.AddComponent<Image>();
        closeImage.sprite = B("Btn_Close");
        closeImage.preserveAspect = true;
        var closeButton = close.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeImage;
        closeButton.transition = Selectable.Transition.None;

        foreach (var t in new[] { (RectTransform)primary.transform, (RectTransform)secondary.transform, holdRoot })
            Pin(t, 0f, -540f);
        Pin(plate.rectTransform, 1f, 540f);
        Pin(close, 1f, 540f);

        var manager = layer.gameObject.AddComponent<PopupManager>();
        SetRefs(manager, ("root", root.gameObject), ("dim", dim), ("card", card), ("cardGroup", group), ("titlePlate", plate), ("title", title),
            ("subtitle", subtitle), ("body", body), ("rays", rays.rectTransform), ("icon", icon), ("amount", amount), ("sparkleRoot", sparkles),
            ("primary", primary), ("primaryBack", primaryBack), ("primaryLabel", primaryLabel), ("primarySub", primarySub), ("primaryIcon", primaryIcon),
            ("hold", hold), ("holdLabel", holdLabel),
            ("secondary", secondary), ("secondaryBack", secondaryBack), ("secondaryLabel", secondaryLabel), ("secondarySub", secondarySub), ("secondaryIcon", secondaryIcon),
            ("close", closeButton),
            ("plateYellow", P("Title_Yellow")), ("plateBlue", P("Title_Blue")), ("plateRed", P("Title_Red")),
            ("buttonGreen", P("Btn_Green")), ("buttonDark", P("Btn_Dark")), ("buttonYellow", P("Btn_Yellow")),
            ("openSfx", Sfx("Sfx_UIOpen")), ("rewardSfx", Sfx("Sfx_Reward")), ("closeSfx", Sfx("Sfx_UIClose")));
        root.gameObject.SetActive(false);
        return manager;
    }

    static Button BigButton(string name, Transform parent, Vector2 pos, Vector2 size, Sprite sprite, string label,
        out Image back, out TMP_Text text, out TMP_Text sub, out Image icon)
    {
        var rt = Node(name, parent);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        back = rt.gameObject.AddComponent<Image>();
        back.sprite = sprite;
        back.type = Image.Type.Sliced;
        back.pixelsPerUnitMultiplier = 0.9f;
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        button.transition = Selectable.Transition.None;
        icon = Img("Icon", rt, new Vector2(62f, 4f), new Vector2(76f, 76f), null, Image.Type.Simple, 1f);
        var ir = icon.rectTransform;
        ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f);
        icon.preserveAspect = true;
        text = Text("Label", rt, new Vector2(0f, 18f), new Vector2(size.x - 60f, 64f), 46f, Color.white, TextAlignmentOptions.Center, label, true);
        Stretch(text.rectTransform, 30f, 50f, 30f, 10f);
        sub = Text("Sub", rt, new Vector2(0f, -36f), new Vector2(size.x - 60f, 40f), 28f, Cream, TextAlignmentOptions.Center, "", true);
        var sr = sub.rectTransform;
        sr.anchorMin = new Vector2(0f, 0f);
        sr.anchorMax = new Vector2(1f, 0f);
        sr.pivot = new Vector2(0.5f, 0f);
        sr.offsetMin = new Vector2(30f, 18f);
        sr.offsetMax = new Vector2(-30f, 58f);
        return button;
    }

    // ------------------------------------------------------------------ settings / pause

    static SettingsPanel SettingsPanel(Transform canvas, Button open)
    {
        var holder = Stretch(Node("SettingsPanel", canvas));
        var root = Stretch(Node("Root", holder));
        var dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0.03f, 0.05f, 0.1f, 0f);
        dim.raycastTarget = true;

        var card = Node("Card", root);
        card.anchoredPosition = new Vector2(0f, -20f);
        card.sizeDelta = new Vector2(960f, 1640f);
        var group = card.gameObject.AddComponent<CanvasGroup>();
        Img("Back", card, Vector2.zero, card.sizeDelta, P("Panel_Dark"), Image.Type.Sliced, 0.55f);
        var plate = Img("TitlePlate", card, new Vector2(0f, 800f), new Vector2(600f, 124f), P("Title_Blue"), Image.Type.Sliced, 95f / 124f);
        Text("Title", plate.transform, new Vector2(0f, 6f), new Vector2(480f, 76f), 54f, Color.white, TextAlignmentOptions.Center, "SETTINGS", true);
        var close = Node("Close", card);
        close.anchoredPosition = new Vector2(430f, 770f);
        close.sizeDelta = new Vector2(104f, 104f);
        var closeImage = close.gameObject.AddComponent<Image>();
        closeImage.sprite = B("Btn_Close");
        closeImage.preserveAspect = true;
        var closeButton = close.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeImage;
        closeButton.transition = Selectable.Transition.None;

        // scroll area
        var scrollRt = Node("Scroll", card);
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(44f, 220f);
        scrollRt.offsetMax = new Vector2(-44f, -110f);
        var scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 30f;
        var viewport = Stretch(Node("Viewport", scrollRt));
        viewport.gameObject.AddComponent<RectMask2D>();
        var hit = viewport.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        var content = Node("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 24, 40);
        layout.spacing = 10f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport;
        scroll.content = content;

        var panel = holder.gameObject.AddComponent<SettingsPanel>();

        Section(content, "AUDIO", new Color(1f, 0.82f, 0.25f), out var audio);
        var sound = ToggleRow(audio, "SOUND", B("Icon_Music"));
        var master = SliderRow(audio, "VOLUME", B("Icon_Sound"));

        Section(content, "GAMEPLAY", new Color(1f, 0.82f, 0.25f), out var gameplay);
        var vibration = ToggleRow(gameplay, "VIBRATION", B("B_r8_c3"));
        var shake = ToggleRow(gameplay, "CAMERA SHAKE", B("B_r7_c7"));
        var numbers = ToggleRow(gameplay, "POP-UP NUMBERS", A("Icon_CashUp"));

        Section(content, "GRAPHICS", new Color(1f, 0.82f, 0.25f), out var graphics);
        var quality = Row(graphics, "QUALITY", B("B_r7_c8"));
        var low = Segment(quality, "LOW", new Vector2(-206f, 0f));
        var high = Segment(quality, "HIGH", new Vector2(-62f, 0f));
        var fps = ToggleRow(graphics, "60 FPS", A("Icon_Stopwatch"));
        var battery = ToggleRow(graphics, "BATTERY SAVER", B("B_r5_c8"));

        Section(content, "ACCOUNT", new Color(1f, 0.82f, 0.25f), out var account);
        var saveRow = Row(account, "PROGRESS", B("B_r8_c7"));
        var saveStatus = Text("Status", saveRow, new Vector2(-24f, 0f), new Vector2(330f, 50f), 30f, new Color(0.15f, 0.5f, 0.2f), TextAlignmentOptions.Right, "SAVED JUST NOW", false);
        Right(saveStatus.rectTransform);
        var restore = ActionRow(account, "RESTORE PURCHASES", B("B_r8_c6"), "RESTORE", P("BtnSmall_Blue"));

        Section(content, "SUPPORT", new Color(1f, 0.82f, 0.25f), out var support);
        var help = ActionRow(support, "HOW TO PLAY", A("A_r6_c4"), "OPEN", P("BtnSmall_Blue"));
        var contact = ActionRow(support, "CONTACT US", B("B_r8_c4"), "OPEN", P("BtnSmall_Blue"));
        var privacy = ActionRow(support, "PRIVACY POLICY", B("B_r5_c12"), "OPEN", P("BtnSmall_Blue"));
        var terms = ActionRow(support, "TERMS OF SERVICE", B("B_r2_c4"), "OPEN", P("BtnSmall_Blue"));

        Section(content, "ABOUT", new Color(1f, 0.82f, 0.25f), out var about);
        var credits = ActionRow(about, "CREDITS", B("B_r2_c1"), "OPEN", P("BtnSmall_Blue"));
        var versionRow = Row(about, "VERSION", A("Icon_Star"));
        var version = Text("Version", versionRow, new Vector2(-24f, 0f), new Vector2(330f, 50f), 30f, Navy, TextAlignmentOptions.Right, "VERSION 1.0", false);
        Right(version.rectTransform);

        Section(content, "DANGER ZONE", new Color(1f, 0.42f, 0.35f), out var danger);
        danger.GetComponent<Image>().color = new Color(1f, 0.86f, 0.82f);
        var dangerRow = Node("Reset", danger);
        dangerRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 170f;
        var reset = BigButton("ResetButton", dangerRow, new Vector2(0f, 20f), new Vector2(560f, 110f), P("Btn_Red"), "RESET PROGRESS", out _, out var resetLabel, out _, out var resetIcon);
        resetLabel.fontSize = resetLabel.fontSizeMax = 40f;
        Stretch(resetLabel.rectTransform, 30f, 10f, 30f, 10f);
        resetIcon.sprite = A("A_r6_c9");
        Text("Note", dangerRow, new Vector2(0f, -60f), new Vector2(760f, 40f), 26f, new Color(0.55f, 0.15f, 0.12f), TextAlignmentOptions.Center, "DELETES ALL PROGRESS. SETTINGS STAY.", false);

        var resume = BigButton("Resume", card, new Vector2(0f, -720f), new Vector2(560f, 150f), P("Btn_Green"), "RESUME", out _, out _, out _, out var resumeIcon);
        resumeIcon.sprite = B("Btn_Play");

        SetRefs(panel, ("openButton", open), ("root", root.gameObject), ("dim", dim), ("card", card), ("cardGroup", group), ("closeButton", closeButton),
            ("resumeButton", resume), ("scroll", scroll), ("sound", sound), ("master", master), ("vibration", vibration), ("cameraShake", shake),
            ("popupNumbers", numbers), ("qualityLow", low), ("qualityHigh", high), ("segmentOn", P("BtnSmall_Green")), ("segmentOff", P("BtnSmall_Grey")),
            ("highFrameRate", fps), ("batterySaver", battery), ("saveStatus", saveStatus), ("restoreRow", restore.gameObject), ("restoreButton", restore),
            ("helpButton", help), ("contactButton", contact), ("privacyButton", privacy), ("termsButton", terms), ("creditsButton", credits),
            ("version", version), ("helpIcon", A("A_r6_c4")), ("resetButton", reset), ("warningIcon", A("A_r6_c9")),
            ("openSfx", Sfx("Sfx_UIOpen")), ("closeSfx", Sfx("Sfx_UIClose")));
        var so = new SerializedObject(panel);
        so.FindProperty("helpText").stringValue =
            "Walk into scrap to cut it. Carry the pieces to a machine's yellow pad, pick up what it makes on the green pad, " +
            "stock the counter on the blue pad and collect the cash on the $ pad.\n" +
            "Stand on a dark tile with a price to hire a worker or open an area. The orange UPGRADES tile opens the upgrades.";
        so.FindProperty("creditsText").stringValue =
            "SCRAP YARD KING\nSounds: Kenney (CC0, kenney.nl)\nTweening: DOTween by Demigiant\nFont: GROBOLD";
        so.ApplyModifiedPropertiesWithoutUndo();
        root.gameObject.SetActive(false);
        return panel;
    }

    static void Section(Transform content, string heading, Color color, out RectTransform card)
    {
        var head = Node("Head_" + heading, content);
        head.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
        var t = Text("Label", head, new Vector2(12f, -6f), new Vector2(800f, 56f), 40f, color, TextAlignmentOptions.Left, heading, true);
        Stretch(t.rectTransform, 14f, 0f, 0f, 0f);

        card = Node("Card_" + heading, content);
        var image = card.gameObject.AddComponent<Image>();
        image.sprite = P("Sq_Cream");
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 0.8f;
        image.raycastTarget = false;
        var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(18, 18, 14, 14);
        v.spacing = 4f;
        v.childControlHeight = true;
        v.childControlWidth = true;
        v.childForceExpandHeight = false;
        v.childForceExpandWidth = true;
    }

    static RectTransform Row(Transform card, string label, Sprite icon)
    {
        var row = Node("Row_" + label, card);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 100f;
        var i = Img("Icon", row, new Vector2(48f, 0f), new Vector2(70f, 70f), icon, Image.Type.Simple, 1f);
        i.preserveAspect = true;
        var ir = i.rectTransform;
        ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f);
        var t = Text("Label", row, Vector2.zero, Vector2.zero, 36f, Navy, TextAlignmentOptions.Left, label, false);
        var tr = t.rectTransform;
        tr.anchorMin = new Vector2(0f, 0f);
        tr.anchorMax = new Vector2(1f, 1f);
        tr.offsetMin = new Vector2(104f, 0f);
        tr.offsetMax = new Vector2(-300f, 0f);
        return row;
    }

    static UIToggle ToggleRow(Transform card, string label, Sprite icon)
    {
        var row = Row(card, label, icon);
        var track = Img("Toggle", row, new Vector2(-24f, 0f), new Vector2(128f, 68f), P("Toggle_On"), Image.Type.Simple, 1f);
        track.preserveAspect = true;
        Right(track.rectTransform);
        var state = Text("State", row, new Vector2(-170f, 0f), new Vector2(100f, 50f), 30f, Navy, TextAlignmentOptions.Right, "ON", false);
        Right(state.rectTransform);
        var hit = row.gameObject.AddComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0f);
        var toggle = row.gameObject.AddComponent<UIToggle>();
        SetRefs(toggle, ("track", track), ("stateLabel", state), ("onSprite", P("Toggle_On")), ("offSprite", P("Toggle_Off")));
        return toggle;
    }

    static Slider SliderRow(Transform card, string label, Sprite icon)
    {
        var row = Row(card, label, icon);
        var rt = Node("Slider", row);
        Right(rt);
        rt.anchoredPosition = new Vector2(-30f, 0f);
        rt.sizeDelta = new Vector2(330f, 46f);
        var back = Img("Background", rt, Vector2.zero, rt.sizeDelta, Single("Kit_BarTrack"), Image.Type.Sliced, 47f / 46f);
        Stretch(back.rectTransform, 0f, 0f, 0f, 0f);
        var fillArea = Node("Fill Area", rt);
        Stretch(fillArea, 14f, 12f, 14f, 12f);
        var fill = Img("Fill", fillArea, Vector2.zero, Vector2.zero, Single("Kit_BarFill"), Image.Type.Sliced, 1f);
        fill.color = new Color(0.45f, 0.86f, 0.3f);
        Stretch(fill.rectTransform, 0f, 0f, 0f, 0f);
        var handleArea = Node("Handle Slide Area", rt);
        Stretch(handleArea, 18f, 0f, 18f, 0f);
        var handle = Img("Handle", handleArea, Vector2.zero, new Vector2(46f, 58f), P("Dot_Yellow"), Image.Type.Simple, 1f);
        handle.raycastTarget = true;
        var slider = rt.gameObject.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;
        slider.transition = Selectable.Transition.None;
        return slider;
    }

    static Button Segment(RectTransform row, string label, Vector2 pos)
    {
        var rt = Node("Seg_" + label, row);
        Right(rt);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(132f, 72f);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = P("BtnSmall_Grey");
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None;
        var t = Text("Label", rt, new Vector2(0f, 2f), new Vector2(116f, 50f), 32f, Color.white, TextAlignmentOptions.Center, label, true);
        Stretch(t.rectTransform, 8f, 6f, 8f, 4f);
        return b;
    }

    static Button ActionRow(Transform card, string label, Sprite icon, string action, Sprite pill)
    {
        var row = Row(card, label, icon);
        var hit = row.gameObject.AddComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0f);
        var b = row.gameObject.AddComponent<Button>();
        b.targetGraphic = hit;
        b.transition = Selectable.Transition.None;
        var p = Img("Pill", row, new Vector2(-24f, 0f), new Vector2(200f, 72f), pill, Image.Type.Sliced, 1f);
        Right(p.rectTransform);
        var t = Text("Action", p.transform, Vector2.zero, new Vector2(180f, 50f), 30f, Color.white, TextAlignmentOptions.Center, action, true);
        Stretch(t.rectTransform, 10f, 6f, 10f, 4f);
        return b;
    }

    /// <summary>Re-anchors a centre-anchored child to the card's top (y=1) or bottom (y=0) edge without moving it.</summary>
    static void Pin(RectTransform rt, float edge, float edgeOffset)
    {
        var p = rt.anchoredPosition;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, edge);
        rt.anchoredPosition = new Vector2(p.x, p.y - edgeOffset);
    }

    static void Right(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
    }

    // ------------------------------------------------------------------ button feel

    static int ButtonFeel(Transform canvas)
    {
        var click = Sfx("Sfx_UIClick");
        var denied = Sfx("Sfx_Denied");
        int n = 0;
        foreach (var button in canvas.GetComponentsInChildren<Button>(true))
        {
            if (!button.TryGetComponent<UIButtonFeel>(out var feel)) feel = button.gameObject.AddComponent<UIButtonFeel>();
            SetRefs(feel, ("clickSfx", click), ("deniedSfx", denied));
            n++;
        }

        return n;
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
        Style(t, fontSize, color, align, outlined);
        t.text = sample;
        return t;
    }

    static void Style(TMP_Text t, float fontSize, Color color, TextAlignmentOptions align, bool outlined)
    {
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
        EditorUtility.SetDirty(t);
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

    static void SetFloat(Object target, string name, float value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(name).floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static SfxDefinition Sfx(string name)
    {
        var s = AssetDatabase.LoadAssetAtPath<SfxDefinition>($"{AudioDir}/{name}.asset");
        if (s == null) Log.AppendLine("!! missing " + name);
        return s;
    }

    static Sprite KitSprite(string sheet, string name)
    {
        var s = AssetDatabase.LoadAllAssetsAtPath($"{KitDir}/{sheet}.png").OfType<Sprite>().FirstOrDefault(x => x.name == name);
        if (s == null) Log.AppendLine($"!! no kit sprite {sheet}/{name}");
        return s;
    }

    static Sprite P(string name) => KitSprite("Kit_Panels", name);
    static Sprite A(string name) => KitSprite("Kit_IconsA", name);
    static Sprite B(string name) => KitSprite("Kit_IconsB", name);

    static Sprite Single(string name)
    {
        var s = AssetDatabase.LoadAssetAtPath<Sprite>($"{KitDir}/{name}.png");
        if (s == null) Log.AppendLine("!! missing sprite " + name);
        return s;
    }
}

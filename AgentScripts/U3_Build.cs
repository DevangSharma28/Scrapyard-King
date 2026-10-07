using System.Linq;
using System.Text;
using ScrapYardKing.Boosts;
using ScrapYardKing.Feedback;
using ScrapYardKing.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// UI step U3: one rewarded-video system and the first diamond spends in normal play.
///  - Assets: cooldowns, daily caps and priorities on every <c>Offer_*</c> (free cash 10 min, scrap rush 15 min, ...),
///    and two new offers: <c>Offer_InstantTruck</c> (FINISH ORDER) and <c>Offer_OfflineDouble</c> (2X on WELCOME BACK).
///  - Scene: <c>OfferDirector</c> wired to them; the old offline panel replaced by <c>WelcomeBack</c> (a popup card);
///    <c>TruckSkipButton</c> under the order card (finish the order by video or diamonds, bring the next truck now).
/// Entry points: Assets, Scene (or All). Idempotent. Run after U2_Build.Scene.
/// </summary>
public static class U3_Build
{
    const string Root = "Assets/_Project";
    const string KitDir = Root + "/Art/UI/Kit";
    const string BoostDir = Root + "/Data/Boosts";
    const string ScenePath = Root + "/Scenes/Area1_OldScrapYard.unity";

    static readonly StringBuilder Log = new();
    static TMP_FontAsset font;
    static Material outline;

    public static string All() => Assets() + Scene();

    public static string Assets()
    {
        Log.Clear();
        // (asset, cooldown s, daily cap, priority). The brief's cadence: free cash every 10 min, scrap rush every 15.
        var tuning = new (string asset, float cooldown, int cap, int priority)[]
        {
            ("Offer_Cash", 600f, 6, 0), ("Offer_Production", 600f, 6, 0), ("Offer_Speed", 600f, 4, 0),
            ("Offer_ScrapRush", 900f, 6, 1), ("Offer_TruckRush", 900f, 4, 0), ("Offer_FreeCash", 600f, 8, 0),
            ("Offer_TruckDouble", 180f, 20, 0),
        };
        foreach (var t in tuning)
        {
            var o = AssetDatabase.LoadAssetAtPath<AdOfferDefinition>($"{BoostDir}/{t.asset}.asset");
            if (o == null) { Log.AppendLine("!! missing " + t.asset); continue; }
            Set(o, ("cooldown", t.cooldown), ("dailyCap", t.cap), ("priority", t.priority));
        }

        Offer("Offer_InstantTruck", "offer_instant_truck", "FINISH ORDER", AdOfferKind.InstantTruck, 600f, 5, A("A_r1_c6"));
        Offer("Offer_OfflineDouble", "offer_offline_double", "2X AWAY CASH", AdOfferKind.OfflineDouble, 0f, 6, A("Icon_Cash"));
        AssetDatabase.SaveAssets();
        Log.AppendLine($"assets: {tuning.Length} offers tuned, 2 new offers");
        return Log.ToString();
    }

    static void Offer(string asset, string id, string title, AdOfferKind kind, float cooldown, int cap, Sprite icon)
    {
        string path = $"{BoostDir}/{asset}.asset";
        var o = AssetDatabase.LoadAssetAtPath<AdOfferDefinition>(path);
        if (o == null)
        {
            o = ScriptableObject.CreateInstance<AdOfferDefinition>();
            AssetDatabase.CreateAsset(o, path);
        }

        var so = new SerializedObject(o);
        so.FindProperty("id").stringValue = id;
        so.FindProperty("title").stringValue = title;
        so.FindProperty("kind").enumValueIndex = (int)kind;
        so.FindProperty("icon").objectReferenceValue = icon;
        so.FindProperty("cooldown").floatValue = cooldown;
        so.FindProperty("dailyCap").intValue = cap;
        so.FindProperty("minLevel").intValue = 1;
        so.FindProperty("weight").floatValue = 0f;   // never in the HUD rotation
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(o);
    }

    static void Set(Object target, params (string name, object value)[] values)
    {
        var so = new SerializedObject(target);
        foreach (var (name, value) in values)
        {
            var p = so.FindProperty(name);
            if (p == null) { Log.AppendLine($"!! {target.name}.{name}"); continue; }
            switch (value)
            {
                case float f: p.floatValue = f; break;
                case int i: p.intValue = i; break;
                case Object obj: p.objectReferenceValue = obj; break;
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
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

        var director = Object.FindAnyObjectByType<OfferDirector>(FindObjectsInactive.Include);
        Set(director, ("instantTruckOffer", AssetDatabase.LoadAssetAtPath<AdOfferDefinition>(BoostDir + "/Offer_InstantTruck.asset")),
            ("offlineOffer", AssetDatabase.LoadAssetAtPath<AdOfferDefinition>(BoostDir + "/Offer_OfflineDouble.asset")),
            ("videoIcon", B("Btn_Play")), ("cashIcon", A("Icon_Cash")));
        var dso = new SerializedObject(director);
        dso.FindProperty("minDoubleReward").longValue = 400;
        dso.ApplyModifiedPropertiesWithoutUndo();

        var canvas = scene.GetRootGameObjects().First(g => g.name == "_UI").transform.Find("Canvas");
        var hud = canvas.Find("HUD");
        var oldOffline = canvas.Find("OfflinePanel");
        if (oldOffline != null) Object.DestroyImmediate(oldOffline.gameObject);

        var welcome = hud.Find("WelcomeBack");
        if (welcome == null)
        {
            welcome = new GameObject("WelcomeBack", typeof(RectTransform)).transform;
            welcome.SetParent(hud, false);
        }

        var wb = welcome.TryGetComponent(out WelcomeBack w) ? w : welcome.gameObject.AddComponent<WelcomeBack>();
        Set(wb, ("cashIcon", A("Icon_Cash")));

        var oldSkip = hud.Find("TruckSkip");
        if (oldSkip != null) Object.DestroyImmediate(oldSkip.gameObject);
        TruckSkip(hud);

        var click = AssetDatabase.LoadAssetAtPath<SfxDefinition>(Root + "/Data/Audio/Sfx_UIClick.asset");
        var denied = AssetDatabase.LoadAssetAtPath<SfxDefinition>(Root + "/Data/Audio/Sfx_Denied.asset");
        foreach (var b in canvas.GetComponentsInChildren<Button>(true))
        {
            if (b.TryGetComponent<UIButtonFeel>(out _)) continue;
            var f = b.gameObject.AddComponent<UIButtonFeel>();
            Set(f, ("clickSfx", click), ("deniedSfx", denied));
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("scene: OfferDirector wired, WelcomeBack (old offline panel removed), TruckSkip button");
        return Log.ToString();
    }

    static void TruckSkip(Transform hud)
    {
        var holder = Node("TruckSkip", hud);
        holder.anchorMin = holder.anchorMax = new Vector2(0f, 1f);
        holder.pivot = new Vector2(0f, 1f);
        holder.anchoredPosition = new Vector2(24f, -436f);
        holder.sizeDelta = new Vector2(372f, 100f);
        var root = Node("Root", holder);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        var back = root.gameObject.AddComponent<Image>();
        back.sprite = P("Btn_Blue");
        back.type = Image.Type.Sliced;
        back.pixelsPerUnitMultiplier = 1f;
        var button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        button.transition = Selectable.Transition.None;
        var icon = Node("Icon", root);
        icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
        icon.anchoredPosition = new Vector2(52f, 2f);
        icon.sizeDelta = new Vector2(70f, 70f);
        var iconImage = icon.gameObject.AddComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        var label = Text("Label", root, 30f, "FINISH ORDER");
        Stretch(label.rectTransform, 96f, 46f, 16f, 10f);
        var sub = Text("Sub", root, 30f, "WATCH");
        Stretch(sub.rectTransform, 96f, 10f, 16f, 50f);
        sub.color = new Color(1f, 0.95f, 0.75f);

        var c = holder.gameObject.AddComponent<TruckSkipButton>();
        var so = new SerializedObject(c);
        so.FindProperty("root").objectReferenceValue = root;
        so.FindProperty("button").objectReferenceValue = button;
        so.FindProperty("back").objectReferenceValue = back;
        so.FindProperty("icon").objectReferenceValue = iconImage;
        so.FindProperty("label").objectReferenceValue = label;
        so.FindProperty("sub").objectReferenceValue = sub;
        so.FindProperty("videoBack").objectReferenceValue = P("Btn_Yellow");
        so.FindProperty("diamondBack").objectReferenceValue = P("Btn_Blue");
        so.FindProperty("diamondIcon").objectReferenceValue = A("Icon_Gem");
        so.FindProperty("truckIcon").objectReferenceValue = A("A_r1_c6");
        so.ApplyModifiedPropertiesWithoutUndo();
        root.gameObject.SetActive(false);
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

    static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    static TMP_Text Text(string name, Transform parent, float size, string sample)
    {
        var t = Node(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font;
        t.fontSharedMaterial = outline != null ? outline : font.material;
        t.fontSize = size;
        t.fontSizeMax = size;
        t.fontSizeMin = 14f;
        t.enableAutoSizing = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.alignment = TextAlignmentOptions.Left;
        t.color = Color.white;
        t.raycastTarget = false;
        t.text = sample;
        return t;
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
}

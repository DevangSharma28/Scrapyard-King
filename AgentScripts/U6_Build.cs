using System.Linq;
using System.Text;
using ScrapYardKing.Boosts;
using ScrapYardKing.Economy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// UI step U6 (economy tools and the guardrail tuning they asked for):
///  - Scene: the <c>EconomyLedger</c> service in _Systems (live numbers for Window → Scrap Yard King → Economy Window);
///    the pause between HUD video offers 70 → 150 s and the offline cap 4 → 2 h. The simulated profiles showed an
///    offer about every two minutes of play and one return paying five 15-minute sessions of income
///    (Docs/ECONOMY.md, "Simulated players"). Top HUD rows re-laid so nothing overlaps (see <see cref="Hud"/>).
/// Data changes of the same step live where their data is built: achievement tiers in <c>U4_Build.Assets</c>, the late
/// cash-per-diamond slope as a field default on <c>PremiumEconomyConfig</c> (set here too, so the asset says it).
/// Entry points: Assets, Scene, All. Idempotent. Run after U5_Build.
/// </summary>
public static class U6_Build
{
    const string Root = "Assets/_Project";
    const string ScenePath = Root + "/Scenes/Area1_OldScrapYard.unity";
    static readonly StringBuilder Log = new();

    public const float OfferQuietSeconds = 150f;
    public const float OfflineMaxHours = 2f;

    public static string All() => Assets() + Scene();

    public static string Assets()
    {
        Log.Clear();
        var premium = AssetDatabase.LoadAssetAtPath<PremiumEconomyConfig>(Root + "/Data/Economy/PremiumEconomy.asset");
        var so = new SerializedObject(premium);
        so.FindProperty("cashGrowthUntilLevel").intValue = 12;
        so.FindProperty("lateCashGrowth").floatValue = 1.08f;
        so.FindProperty("finishMinLoaded").floatValue = 0.25f;   // FINISH ORDER never fills an empty truck
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(premium);
        AssetDatabase.SaveAssets();
        Log.AppendLine("assets: cash per diamond x1.3 a level to Lv 12, x1.08 after; FINISH ORDER from 25% loaded");
        return Log.ToString();
    }

    public static string Scene()
    {
        Log.Clear();
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var systems = scene.GetRootGameObjects().First(g => g.name == "_Systems").transform;

        Service<EconomyLedger>(systems, "EconomyLedger");
        Hud(scene.GetRootGameObjects().First(g => g.name == "_UI").transform.Find("Canvas/HUD"));

        var director = Object.FindAnyObjectByType<OfferDirector>(FindObjectsInactive.Include);
        Set(director, "quietSeconds", OfferQuietSeconds);
        var idle = Object.FindAnyObjectByType<IdleIncomeManager>(FindObjectsInactive.Include);
        Set(idle, "maxHours", OfflineMaxHours);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine($"scene: EconomyLedger, offer pause {OfferQuietSeconds:0} s, offline cap {OfflineMaxHours:0} h");
        return Log.ToString();
    }

    /// <summary>
    /// Top HUD rows that collided (owner's screenshots, 2026-10-07): the level star ran into the task banner's corner,
    /// the task reward hung under the banner on the same row as the side task (both texts drawn over each other), the
    /// side task crossed the order card, and the boost chips started 10 px inside the banner. From the top now:
    /// banner (reward inside, on the bar row) → side task row → order card / boost chips → FINISH ORDER.
    /// </summary>
    static void Hud(Transform hud)
    {
        var banner = hud.Find("TaskBanner");
        var panel = (RectTransform)banner.Find("Panel");
        panel.anchoredPosition = new Vector2(20f, -160f);   // clear of the level star (its right edge is at x 154)
        panel.sizeDelta = new Vector2(780f, 112f);

        var bar = (RectTransform)panel.Find("Bar");
        bar.sizeDelta = new Vector2(380f, bar.sizeDelta.y);

        var reward = panel.Find("Reward").GetComponent<TMPro.TMP_Text>();
        var r = reward.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0f, 0.5f);
        r.anchoredPosition = new Vector2(424f, -24f);
        r.sizeDelta = new Vector2(166f, 44f);
        Fit(reward, TMPro.TextAlignmentOptions.Left, 18f, 30f);
        // on the cream plate the pale gold with an outline washed out: plain face, deep orange
        reward.fontSharedMaterial = panel.Find("Title").GetComponent<TMPro.TMP_Text>().fontSharedMaterial;
        reward.color = new Color(0.9f, 0.45f, 0.02f);

        var progress = panel.Find("Progress").GetComponent<TMPro.TMP_Text>();
        progress.rectTransform.sizeDelta = new Vector2(150f, 50f);
        Fit(progress, TMPro.TextAlignmentOptions.Right, 24f, 38f);

        var side = banner.Find("SideTask").GetComponent<TMPro.TMP_Text>();
        side.rectTransform.anchoredPosition = new Vector2(0f, -300f);
        side.rectTransform.sizeDelta = new Vector2(560f, 40f);   // between the order card's column and the boost chips
        Fit(side, TMPro.TextAlignmentOptions.Center, 16f, 26f);

        ((RectTransform)hud.Find("ContractWidget")).anchoredPosition = new Vector2(24f, -332f);
        ((RectTransform)hud.Find("TruckSkip")).anchoredPosition = new Vector2(24f, -478f);
        ((RectTransform)hud.Find("BoostBar")).anchoredPosition = new Vector2(-30f, -290f);
        Log.AppendLine("hud: banner clear of the star, reward inside the banner, side task row, order card / chips below it");
    }

    static void Fit(TMPro.TMP_Text t, TMPro.TextAlignmentOptions align, float min, float max)
    {
        t.alignment = align;
        t.enableAutoSizing = true;
        t.fontSizeMin = min;
        t.fontSizeMax = max;
        t.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        t.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        EditorUtility.SetDirty(t);
    }

    static void Set(Object target, string field, float value)
    {
        if (target == null) { Log.AppendLine($"!! no target for {field}"); return; }
        var so = new SerializedObject(target);
        so.FindProperty(field).floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
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
}
